using System;
using System.Collections.Generic;
using AetherLove;
using AetherLove.Services;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AetherOS.Apps.Doom.Backend;

/// <summary>The one output device the Doom cabinet plays through: a fixed set of voices mixed into a single
/// stereo stream. Sound effects and music share it deliberately, so both honour the output device the user
/// chose for the phone rather than music escaping to a different one.
///
/// The voice set is allocated once and never resized. Doom asks for at most eight positional channels plus
/// a UI channel, and adding or removing mixer inputs while the playback thread is reading them is exactly
/// the sort of race that produces intermittent silence, so idle voices simply render zeroes.</summary>
internal sealed class DoomAudioOutput : IDisposable
{
    public const int SampleRate = 44100;

    private readonly IWavePlayer output;
    private readonly Mixer mixer;

    public DoomAudioOutput(int voiceCount)
    {
        var voices = new List<DoomVoice>(voiceCount);
        for (var i = 0; i < voiceCount; i++)
        {
            voices.Add(new DoomVoice());
        }
        this.Voices = voices;

        this.mixer = new Mixer(this);
        this.output = CreatePlayer(this.mixer);
        // Silent on a normal stop; a playback thread dying mid-run is the one event worth a line.
        this.output.PlaybackStopped += (_, e) =>
        {
            if (e.Exception is not null)
            {
                UiHost.Log.Error(e.Exception, "[DoomAudio] playback thread died.");
            }
        };
        this.output.Play();
    }

    /// <summary>WASAPI first, waveOut only as the fallback. Instrumentation showed the mixer handing the old
    /// waveOut layer correctly-timed audio while it stuttered and wedged anyway, so the output moves to the
    /// modern path the game itself uses. The mix stays at Doom's own rate and is resampled in managed code to
    /// whatever the device runs at, deliberately avoiding the DMO resampler WasapiOut reaches for on a format
    /// mismatch, which cannot be counted on under Wine.</summary>
    private static IWavePlayer CreatePlayer(Mixer mixer)
    {
        try
        {
            var device = ResolveWasapiDevice();
            var deviceRate = device.AudioClient.MixFormat.SampleRate;
            ISampleProvider source = mixer;
            if (deviceRate != SampleRate)
            {
                source = new WdlResamplingSampleProvider(mixer, deviceRate);
            }
            var wasapi = new WasapiOut(device, AudioClientShareMode.Shared, true, 100);
            wasapi.Init(source);
            UiHost.Log.Information("[DoomAudio] wasapi output on '{Device}' at {Rate} Hz.",
                device.FriendlyName, deviceRate);
            return wasapi;
        }
        catch (Exception ex)
        {
            UiHost.Log.Warning(ex, "[DoomAudio] wasapi unavailable; falling back to waveOut.");
        }
        var waveOut = new WaveOutEvent
        {
            DeviceNumber = NotificationSoundPlayer.ResolveDeviceIndex(),
            DesiredLatency = 120,
            NumberOfBuffers = 3,
        };
        waveOut.Init(mixer);
        return waveOut;
    }

    /// <summary>The device the phone is configured to play through, matched by name prefix because waveOut
    /// truncates its names to 31 characters; no configured device means the default endpoint.</summary>
    private static MMDevice ResolveWasapiDevice()
    {
        using var enumerator = new MMDeviceEnumerator();
        var configured = UiHost.Configuration.OsSettings.AudioOutputDevice;
        if (configured.Length > 0)
        {
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                if (device.FriendlyName.StartsWith(configured, StringComparison.Ordinal))
                {
                    return device;
                }
            }
        }
        return enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
    }

    public IReadOnlyList<DoomVoice> Voices { get; }

    /// <summary>Rendered into the mix ahead of the voices. Null until music starts.</summary>
    public IDoomMusicSource? Music { get; set; }

    /// <summary>Silences the cabinet outright. Checked at the top of the mix, so muting also stops all the
    /// per-voice work rather than rendering a mix nobody hears.</summary>
    public volatile bool Muted;

    /// <summary>The player's own level, 0 to 1, applied with the master gain at the end of the mix.
    /// Set from the UI thread and read on the playback thread, which is why it is a plain float
    /// written whole: a torn read is impossible on a float and the worst a race can do is spend one
    /// buffer at the previous level.</summary>
    public volatile float Level = 1f;

    public void Dispose()
    {
        try
        {
            this.output.Stop();
            this.output.Dispose();
        }
        catch (Exception ex)
        {
            UiHost.Log.Debug(ex, "[Doom] Audio shutdown failed.");
        }
    }

    /// <summary>Sums every voice plus the music source. Runs on NAudio's playback thread, so everything it
    /// touches is either immutable or guarded by the voice's own lock.</summary>
    private sealed class Mixer : ISampleProvider
    {
        /// <summary>Headroom. Doom can have nine voices going at once and each arrives near full scale, so a
        /// plain sum spends most of a firefight pinned against the clamp, which is heard as distortion rather
        /// than as loudness.</summary>
        private const float MasterGain = 0.42f;

        private readonly DoomAudioOutput owner;
        private float[] scratch = new float[0];

        public Mixer(DoomAudioOutput owner) => this.owner = owner;

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 2);

        public int Read(float[] buffer, int offset, int count)
        {
            Array.Clear(buffer, offset, count);
            if (this.owner.Muted)
            {
                return count;
            }
            if (this.scratch.Length < count)
            {
                this.scratch = new float[count];
            }

            var music = this.owner.Music;
            if (music != null)
            {
                music.Render(this.scratch, count);
                for (var i = 0; i < count; i++)
                {
                    buffer[offset + i] += this.scratch[i];
                }
            }

            foreach (var voice in this.owner.Voices)
            {
                if (!voice.Render(this.scratch, count))
                {
                    continue;
                }
                for (var i = 0; i < count; i++)
                {
                    buffer[offset + i] += this.scratch[i];
                }
            }

            // Read once for the whole buffer: sampled per sample it would step mid-buffer while a
            // bar is being dragged, which is a click rather than a slide.
            var level = Math.Clamp(this.owner.Level, 0f, 1f);
            for (var i = 0; i < count; i++)
            {
                buffer[offset + i] = Math.Clamp(MasterGain * level * buffer[offset + i], -1f, 1f);
            }
            return count;
        }
    }
}

/// <summary>Anything that can fill an interleaved stereo block, which is how the music backend joins the
/// mix without the output needing to know about MIDI.</summary>
internal interface IDoomMusicSource
{
    void Render(float[] buffer, int count);
}
