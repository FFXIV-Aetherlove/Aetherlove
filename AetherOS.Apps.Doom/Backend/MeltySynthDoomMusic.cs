//
// Copyright (C) 1993-1996 Id Software, Inc.
// Copyright (C) 2019-2020 Nobuaki Tanaka
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// Derived from managed-doom's src/Silk/SilkMusic.cs. The MUS and MIDI decoders are upstream's and are
// platform-agnostic; only the audio sink changed, from an OpenAL stream to the phone's NAudio mixer.
//

using System;
using System.IO;
using ManagedDoom;
using ManagedDoom.Audio;
using MeltySynth;

namespace AetherOS.Apps.Doom.Backend;

internal sealed class MeltySynthDoomMusic : IMusic, IDoomMusicSource, IDisposable
{
    private readonly Config config;
    private readonly Wad wad;
    private readonly Synthesizer synthesizer;

    private readonly object gate = new();
    private float[] left = new float[0];
    private float[] right = new float[0];

    private IDecoder? current;
    private IDecoder? reserved;
    private Bgm playing = Bgm.NONE;

    public MeltySynthDoomMusic(Config config, GameContent content, string soundFontPath)
    {
        this.config = config;
        this.wad = content.Wad;

        config.audio_musicvolume = Math.Clamp(config.audio_musicvolume, 0, MaxVolume);

        var settings = new SynthesizerSettings(DoomAudioOutput.SampleRate)
        {
            BlockSize = DoomAudioOutput.SampleRate / 140,
            EnableReverbAndChorus = config.audio_musiceffect,
        };
        this.synthesizer = new Synthesizer(soundFontPath, settings);
    }

    public void StartMusic(Bgm bgm, bool loop)
    {
        if (bgm == this.playing)
        {
            return;
        }

        var lump = "D_" + DoomInfo.BgmNames[(int)bgm].ToString().ToUpper();
        if (this.wad.GetLumpNumber(lump) == -1)
        {
            return;
        }

        var decoder = ReadData(this.wad.ReadLump(lump), loop);
        lock (this.gate)
        {
            this.reserved = decoder;
        }
        this.playing = bgm;
    }

    private static IDecoder ReadData(byte[] data, bool loop)
    {
        if (Matches(data, MusDecoder.MusHeader))
        {
            return new MusDecoder(data, loop);
        }
        if (Matches(data, MidiDecoder.MidiHeader))
        {
            return new MidiDecoder(data, loop);
        }
        throw new Exception("Unknown format!");
    }

    private static bool Matches(byte[] data, byte[] header)
    {
        if (data.Length < header.Length)
        {
            return false;
        }
        for (var i = 0; i < header.Length; i++)
        {
            if (data[i] != header[i])
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>Renders the next interleaved stereo block. Runs on NAudio's playback thread.</summary>
    public void Render(float[] buffer, int count)
    {
        var frames = count / 2;
        lock (this.gate)
        {
            if (this.reserved != this.current)
            {
                this.synthesizer.Reset();
                this.current = this.reserved;
            }

            if (this.current is not { } decoder)
            {
                Array.Clear(buffer, 0, count);
                return;
            }

            if (this.left.Length < frames)
            {
                this.left = new float[frames];
                this.right = new float[frames];
            }

            decoder.RenderWaveform(this.synthesizer, this.left.AsSpan(0, frames), this.right.AsSpan(0, frames));

            var gain = 2f * this.config.audio_musicvolume / MaxVolume;
            for (var i = 0; i < frames; i++)
            {
                buffer[2 * i] = Math.Clamp(gain * this.left[i], -1f, 1f);
                buffer[(2 * i) + 1] = Math.Clamp(gain * this.right[i], -1f, 1f);
            }
        }
    }

    public void Dispose()
    {
        lock (this.gate)
        {
            this.current = null;
            this.reserved = null;
        }
    }

    public int MaxVolume => 15;

    public int Volume
    {
        get => this.config.audio_musicvolume;
        set => this.config.audio_musicvolume = value;
    }

    private interface IDecoder
    {
        void RenderWaveform(Synthesizer synthesizer, Span<float> left, Span<float> right);
    }

    private sealed class MusDecoder : IDecoder
    {
        public static readonly byte[] MusHeader = [(byte)'M', (byte)'U', (byte)'S', 0x1A];

        private readonly byte[] data;
        private readonly bool loop;

        private readonly int scoreStart;
        private readonly MusEvent[] events;
        private readonly int[] lastVolume;

        private int eventCount;
        private int p;
        private int delay;
        private int blockWrote;

        public MusDecoder(byte[] data, bool loop)
        {
            this.data = data;
            this.loop = loop;

            this.scoreStart = BitConverter.ToUInt16(data, 6);

            this.events = new MusEvent[128];
            for (var i = 0; i < this.events.Length; i++)
            {
                this.events[i] = new MusEvent();
            }
            this.eventCount = 0;
            this.lastVolume = new int[16];

            Reset();
            this.blockWrote = int.MaxValue;
        }

        public void RenderWaveform(Synthesizer synthesizer, Span<float> left, Span<float> right)
        {
            if (this.blockWrote == int.MaxValue)
            {
                this.blockWrote = synthesizer.BlockSize;
            }

            var wrote = 0;
            while (wrote < left.Length)
            {
                if (this.blockWrote == synthesizer.BlockSize)
                {
                    ProcessMidiEvents(synthesizer);
                    this.blockWrote = 0;
                }

                var rem = Math.Min(synthesizer.BlockSize - this.blockWrote, left.Length - wrote);
                synthesizer.Render(left.Slice(wrote, rem), right.Slice(wrote, rem));

                this.blockWrote += rem;
                wrote += rem;
            }
        }

        private void ProcessMidiEvents(Synthesizer synthesizer)
        {
            if (this.delay > 0)
            {
                this.delay--;
            }

            if (this.delay == 0)
            {
                this.delay = ReadSingleEventGroup();
                SendEvents(synthesizer);

                if (this.delay == -1)
                {
                    synthesizer.NoteOffAll(false);
                    if (this.loop)
                    {
                        Reset();
                    }
                }
            }
        }

        private void Reset()
        {
            for (var i = 0; i < this.lastVolume.Length; i++)
            {
                this.lastVolume[i] = 0;
            }
            this.p = this.scoreStart;
            this.delay = 0;
        }

        private int ReadSingleEventGroup()
        {
            this.eventCount = 0;
            while (true)
            {
                var result = ReadSingleEvent();
                if (result == ReadResult.EndOfGroup)
                {
                    break;
                }
                if (result == ReadResult.EndOfFile)
                {
                    return -1;
                }
            }

            var time = 0;
            while (true)
            {
                var value = this.data[this.p++];
                time = (time * 128) + (value & 127);
                if ((value & 128) == 0)
                {
                    break;
                }
            }
            return time;
        }

        private ReadResult ReadSingleEvent()
        {
            var channelNumber = this.data[this.p] & 0xF;
            if (channelNumber == 15)
            {
                channelNumber = 9;
            }
            else if (channelNumber >= 9)
            {
                channelNumber++;
            }

            var eventType = (this.data[this.p] & 0x70) >> 4;
            var last = (this.data[this.p] >> 7) != 0;
            this.p++;

            var me = this.events[this.eventCount];
            this.eventCount++;

            switch (eventType)
            {
                case 0: // RELEASE NOTE
                    me.Type = 0;
                    me.Channel = channelNumber;
                    me.Data1 = this.data[this.p++];
                    me.Data2 = 0;
                    break;

                case 1: // PLAY NOTE
                    me.Type = 1;
                    me.Channel = channelNumber;
                    var playNote = this.data[this.p++];
                    var noteVolume = (playNote & 128) != 0 ? this.data[this.p++] : -1;
                    me.Data1 = playNote & 127;
                    if (noteVolume == -1)
                    {
                        me.Data2 = this.lastVolume[channelNumber];
                    }
                    else
                    {
                        me.Data2 = noteVolume;
                        this.lastVolume[channelNumber] = noteVolume;
                    }
                    break;

                case 2: // PITCH WHEEL
                    me.Type = 2;
                    me.Channel = channelNumber;
                    var pw2 = (this.data[this.p++] << 7) / 2;
                    me.Data1 = pw2 & 127;
                    me.Data2 = pw2 >> 7;
                    break;

                case 3: // SYSTEM EVENT
                    me.Type = 3;
                    me.Channel = channelNumber;
                    me.Data1 = this.data[this.p++];
                    me.Data2 = 0;
                    break;

                case 4: // CONTROL CHANGE
                    me.Type = 4;
                    me.Channel = channelNumber;
                    me.Data1 = this.data[this.p++];
                    me.Data2 = this.data[this.p++];
                    break;

                case 6: // END OF FILE
                    return ReadResult.EndOfFile;

                default:
                    throw new Exception("Unknown event type!");
            }

            return last ? ReadResult.EndOfGroup : ReadResult.Ongoing;
        }

        private void SendEvents(Synthesizer synthesizer)
        {
            for (var i = 0; i < this.eventCount; i++)
            {
                var me = this.events[i];
                switch (me.Type)
                {
                    case 0:
                        synthesizer.NoteOff(me.Channel, me.Data1);
                        break;

                    case 1:
                        synthesizer.NoteOn(me.Channel, me.Data1, me.Data2);
                        break;

                    case 2:
                        synthesizer.ProcessMidiMessage(me.Channel, 0xE0, me.Data1, me.Data2);
                        break;

                    case 3:
                        switch (me.Data1)
                        {
                            case 11:
                                synthesizer.NoteOffAll(me.Channel, false);
                                break;
                            case 14:
                                synthesizer.ResetAllControllers(me.Channel);
                                break;
                        }
                        break;

                    case 4:
                        SendControlChange(synthesizer, me);
                        break;
                }
            }
        }

        private static void SendControlChange(Synthesizer synthesizer, MusEvent me)
        {
            switch (me.Data1)
            {
                case 0: // PROGRAM CHANGE
                    synthesizer.ProcessMidiMessage(me.Channel, 0xC0, me.Data2, 0);
                    break;
                case 1: // BANK SELECTION
                    synthesizer.ProcessMidiMessage(me.Channel, 0xB0, 0x00, me.Data2);
                    break;
                case 2: // MODULATION
                    synthesizer.ProcessMidiMessage(me.Channel, 0xB0, 0x01, me.Data2);
                    break;
                case 3: // VOLUME
                    synthesizer.ProcessMidiMessage(me.Channel, 0xB0, 0x07, me.Data2);
                    break;
                case 4: // PAN
                    synthesizer.ProcessMidiMessage(me.Channel, 0xB0, 0x0A, me.Data2);
                    break;
                case 5: // EXPRESSION
                    synthesizer.ProcessMidiMessage(me.Channel, 0xB0, 0x0B, me.Data2);
                    break;
                case 6: // REVERB
                    synthesizer.ProcessMidiMessage(me.Channel, 0xB0, 0x5B, me.Data2);
                    break;
                case 7: // CHORUS
                    synthesizer.ProcessMidiMessage(me.Channel, 0xB0, 0x5D, me.Data2);
                    break;
                case 8: // PEDAL
                    synthesizer.ProcessMidiMessage(me.Channel, 0xB0, 0x40, me.Data2);
                    break;
            }
        }

        private sealed class MusEvent
        {
            public int Type;
            public int Channel;
            public int Data1;
            public int Data2;
        }

        private enum ReadResult
        {
            Ongoing,
            EndOfGroup,
            EndOfFile,
        }
    }

    private sealed class MidiDecoder : IDecoder
    {
        public static readonly byte[] MidiHeader = [(byte)'M', (byte)'T', (byte)'h', (byte)'d'];

        private readonly MidiFile midi;
        private readonly bool loop;
        private MidiFileSequencer? sequencer;

        public MidiDecoder(byte[] data, bool loop)
        {
            this.midi = new MidiFile(new MemoryStream(data));
            this.loop = loop;
        }

        public void RenderWaveform(Synthesizer synthesizer, Span<float> left, Span<float> right)
        {
            if (this.sequencer == null)
            {
                this.sequencer = new MidiFileSequencer(synthesizer);
                this.sequencer.Play(this.midi, this.loop);
            }
            this.sequencer.Render(left, right);
        }
    }
}
