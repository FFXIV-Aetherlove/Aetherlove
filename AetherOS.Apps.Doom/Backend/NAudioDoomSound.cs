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
// Derived from managed-doom's src/Silk/SilkSound.cs: the lump decoding, channel allocation and priority
// rules are upstream's. The audio device underneath them is NAudio rather than OpenAL.
//

using System;
using ManagedDoom;
using ManagedDoom.Audio;

namespace AetherOS.Apps.Doom.Backend;

internal sealed class NAudioDoomSound : ISound, IDisposable
{
    private const int ChannelCount = 8;
    private const int UiChannel = ChannelCount;

    private static readonly float FastDecay = (float)Math.Pow(0.5, 1.0 / (35 / 5));
    private static readonly float SlowDecay = (float)Math.Pow(0.5, 1.0 / 35);

    private const float ClipDist = 1200f;
    private const float CloseDist = 160f;
    private const float Attenuator = ClipDist - CloseDist;

    private readonly Config config;
    private readonly DoomAudioOutput output;

    private readonly byte[]?[] buffers;
    private readonly int[] sampleRates;
    private readonly float[] amplitudes;

    private readonly DoomRandom? random;

    private readonly ChannelInfo[] infos;
    private Sfx uiReserved;

    private Mobj? listener;
    private float masterVolumeDecay;
    private DateTime lastUpdate;

    public NAudioDoomSound(Config config, GameContent content, DoomAudioOutput output)
    {
        this.config = config;
        this.output = output;

        config.audio_soundvolume = Math.Clamp(config.audio_soundvolume, 0, MaxVolume);

        this.buffers = new byte[DoomInfo.SfxNames.Length][];
        this.sampleRates = new int[DoomInfo.SfxNames.Length];
        this.amplitudes = new float[DoomInfo.SfxNames.Length];

        if (config.audio_randompitch)
        {
            this.random = new DoomRandom();
        }

        for (var i = 0; i < DoomInfo.SfxNames.Length; i++)
        {
            var name = "DS" + DoomInfo.SfxNames[i].ToString().ToUpper();
            if (content.Wad.GetLumpNumber(name) == -1)
            {
                continue;
            }

            var samples = GetSamples(content.Wad, name, out var sampleRate, out var sampleCount);
            if (samples != null && samples.Length > 0)
            {
                this.buffers[i] = samples;
                this.sampleRates[i] = sampleRate;
                this.amplitudes[i] = GetAmplitude(samples, sampleRate, sampleCount);
            }
        }

        this.infos = new ChannelInfo[ChannelCount];
        for (var i = 0; i < this.infos.Length; i++)
        {
            this.infos[i] = new ChannelInfo();
        }

        this.uiReserved = Sfx.NONE;
        this.masterVolumeDecay = (float)config.audio_soundvolume / MaxVolume;
        this.lastUpdate = DateTime.MinValue;
    }

    private static byte[]? GetSamples(Wad wad, string name, out int sampleRate, out int sampleCount)
    {
        var data = wad.ReadLump(name);
        if (data.Length < 8)
        {
            sampleRate = -1;
            sampleCount = -1;
            return null;
        }

        sampleRate = BitConverter.ToUInt16(data, 2);
        sampleCount = BitConverter.ToInt32(data, 4);

        var offset = 8;
        if (ContainsDmxPadding(data))
        {
            offset += 16;
            sampleCount -= 32;
        }

        if (sampleCount <= 0 || offset + sampleCount > data.Length)
        {
            return null;
        }

        var result = new byte[sampleCount];
        Array.Copy(data, offset, result, 0, sampleCount);
        return result;
    }

    // Check if the data contains pad bytes. If the first and last 16 samples are the same, the data should
    // contain pad bytes. https://doomwiki.org/wiki/Sound
    private static bool ContainsDmxPadding(byte[] data)
    {
        var sampleCount = BitConverter.ToInt32(data, 4);
        if (sampleCount < 32 || 8 + sampleCount > data.Length)
        {
            return false;
        }

        var first = data[8];
        for (var i = 1; i < 16; i++)
        {
            if (data[8 + i] != first)
            {
                return false;
            }
        }

        var last = data[8 + sampleCount - 1];
        for (var i = 1; i < 16; i++)
        {
            if (data[8 + sampleCount - i - 1] != last)
            {
                return false;
            }
        }
        return true;
    }

    private static float GetAmplitude(byte[] samples, int sampleRate, int sampleCount)
    {
        var max = 0;
        if (sampleCount > 0)
        {
            var count = Math.Min(sampleRate / 5, sampleCount);
            for (var t = 0; t < count; t++)
            {
                var a = Math.Abs(samples[t] - 128);
                if (a > max)
                {
                    max = a;
                }
            }
        }
        return (float)max / 128;
    }

    public void SetListener(Mobj listener) => this.listener = listener;

    public void Update()
    {
        var now = DateTime.Now;
        if ((now - this.lastUpdate).TotalSeconds < 0.01)
        {
            return;
        }

        for (var i = 0; i < this.infos.Length; i++)
        {
            var info = this.infos[i];
            var voice = this.output.Voices[i];

            if (info.Playing != Sfx.NONE)
            {
                if (voice.Playing)
                {
                    info.Priority *= info.Type == SfxType.Diffuse ? SlowDecay : FastDecay;
                    SetParam(voice, info);
                }
                else
                {
                    info.Playing = Sfx.NONE;
                    if (info.Reserved == Sfx.NONE)
                    {
                        info.Source = null;
                    }
                }
            }

            if (info.Reserved != Sfx.NONE)
            {
                if (info.Playing != Sfx.NONE)
                {
                    voice.Stop();
                }

                var index = (int)info.Reserved;
                var level = GetLevel(info);
                voice.Play(this.buffers[index]!, this.sampleRates[index], level.Volume, level.Pan,
                    GetPitch(info.Type, info.Reserved));
                info.Playing = info.Reserved;
                info.Reserved = Sfx.NONE;
            }
        }

        if (this.uiReserved != Sfx.NONE)
        {
            var index = (int)this.uiReserved;
            this.output.Voices[UiChannel].Play(
                this.buffers[index]!, this.sampleRates[index], this.masterVolumeDecay, 0f, 1f);
            this.uiReserved = Sfx.NONE;
        }

        this.lastUpdate = now;
    }

    public void StartSound(Sfx sfx)
    {
        if (this.buffers[(int)sfx] == null)
        {
            return;
        }
        this.uiReserved = sfx;
    }

    public void StartSound(Mobj mobj, Sfx sfx, SfxType type) => StartSound(mobj, sfx, type, 100);

    public void StartSound(Mobj mobj, Sfx sfx, SfxType type, int volume)
    {
        if (this.buffers[(int)sfx] == null || this.listener == null)
        {
            return;
        }

        var x = (mobj.X - this.listener.X).ToFloat();
        var y = (mobj.Y - this.listener.Y).ToFloat();
        var dist = MathF.Sqrt((x * x) + (y * y));

        var priority = type == SfxType.Diffuse
            ? volume
            : this.amplitudes[(int)sfx] * GetDistanceDecay(dist) * volume;

        foreach (var info in this.infos)
        {
            if (info.Source == mobj && info.Type == type)
            {
                info.Reserved = sfx;
                info.Priority = priority;
                info.Volume = volume;
                return;
            }
        }

        foreach (var info in this.infos)
        {
            if (info.Reserved == Sfx.NONE && info.Playing == Sfx.NONE)
            {
                info.Reserved = sfx;
                info.Priority = priority;
                info.Source = mobj;
                info.Type = type;
                info.Volume = volume;
                return;
            }
        }

        var minPriority = float.MaxValue;
        var minChannel = -1;
        for (var i = 0; i < this.infos.Length; i++)
        {
            if (this.infos[i].Priority < minPriority)
            {
                minPriority = this.infos[i].Priority;
                minChannel = i;
            }
        }
        if (priority >= minPriority && minChannel >= 0)
        {
            var info = this.infos[minChannel];
            info.Reserved = sfx;
            info.Priority = priority;
            info.Source = mobj;
            info.Type = type;
            info.Volume = volume;
        }
    }

    public void StopSound(Mobj mobj)
    {
        foreach (var info in this.infos)
        {
            if (info.Source == mobj)
            {
                info.LastX = info.Source.X;
                info.LastY = info.Source.Y;
                info.Source = null;
                info.Volume /= 5;
            }
        }
    }

    public void Reset()
    {
        this.random?.Clear();
        for (var i = 0; i < this.infos.Length; i++)
        {
            this.output.Voices[i].Stop();
            this.infos[i].Clear();
        }
        this.listener = null;
    }

    public void Pause()
    {
        foreach (var voice in this.output.Voices)
        {
            voice.Pause();
        }
    }

    public void Resume()
    {
        foreach (var voice in this.output.Voices)
        {
            voice.Resume();
        }
    }

    private void SetParam(DoomVoice voice, ChannelInfo info)
    {
        var level = GetLevel(info);
        voice.SetLevel(level.Volume, level.Pan);
    }

    /// <summary>Collapses the source's direction relative to the listener into a gain and a stereo pan.
    /// Upstream handed the equivalent vector to OpenAL; the maths is the same, minus the ears.</summary>
    private (float Volume, float Pan) GetLevel(ChannelInfo info)
    {
        if (info.Type == SfxType.Diffuse || this.listener == null)
        {
            return (0.01f * this.masterVolumeDecay * info.Volume, 0f);
        }

        var sourceX = info.Source == null ? info.LastX : info.Source.X;
        var sourceY = info.Source == null ? info.LastY : info.Source.Y;

        var x = (sourceX - this.listener.X).ToFloat();
        var y = (sourceY - this.listener.Y).ToFloat();

        if (Math.Abs(x) < 16 && Math.Abs(y) < 16)
        {
            return (0.01f * this.masterVolumeDecay * info.Volume, 0f);
        }

        var dist = MathF.Sqrt((x * x) + (y * y));
        var angle = MathF.Atan2(y, x) - (float)this.listener.Angle.ToRadian();
        return (0.01f * this.masterVolumeDecay * GetDistanceDecay(dist) * info.Volume, -MathF.Sin(angle));
    }

    private static float GetDistanceDecay(float dist) =>
        dist < CloseDist ? 1f : Math.Max((ClipDist - dist) / Attenuator, 0f);

    private float GetPitch(SfxType type, Sfx sfx)
    {
        if (this.random == null)
        {
            return 1f;
        }
        if (sfx == Sfx.ITEMUP || sfx == Sfx.TINK || sfx == Sfx.RADIO)
        {
            return 1f;
        }
        var spread = type == SfxType.Voice ? 0.075f : 0.025f;
        return 1f + (spread * (this.random.Next() - 128) / 128);
    }

    public void Dispose() => Reset();

    public int MaxVolume => 15;

    public int Volume
    {
        get => this.config.audio_soundvolume;
        set
        {
            this.config.audio_soundvolume = value;
            this.masterVolumeDecay = (float)value / MaxVolume;
        }
    }

    private sealed class ChannelInfo
    {
        public Sfx Reserved;
        public Sfx Playing;
        public float Priority;

        public Mobj? Source;
        public SfxType Type;
        public int Volume;
        public Fixed LastX;
        public Fixed LastY;

        public void Clear()
        {
            this.Reserved = Sfx.NONE;
            this.Playing = Sfx.NONE;
            this.Priority = 0;
            this.Source = null;
            this.Type = 0;
            this.Volume = 0;
            this.LastX = Fixed.Zero;
            this.LastY = Fixed.Zero;
        }
    }
}
