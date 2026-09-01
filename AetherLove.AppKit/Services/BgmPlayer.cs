using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Vorbis;
using NAudio.Wave;

namespace AetherLove.Services;

/// <summary>One looping background track, decoded from memory rather than from the file on disk, so no file
/// handle stays held inside the game process.
/// <para>
/// Speed is a playback rate, not a time stretch: the track is handed to the output claiming a sample rate
/// scaled by the speed, so it runs faster and rises in pitch together. That rate is baked into the output at
/// initialisation, so a speed change means a second voice starting where the first left off while the two
/// cross-fade.
/// </para></summary>
public sealed class BgmPlayer : IDisposable
{
    private const float MinSpeed = 0.25f;
    private const float MaxSpeed = 4f;
    private const float SpeedEpsilon = 0.001f;
    private const int MinSampleRate = 8000;
    private const double FadeSeconds = 1.5;

    /// <summary>Playing volume. Background music sits under the game, not over it.</summary>
    private const float Level = 0.30f;

    private const double CrossfadeSeconds = 0.35;
    private const double MuteFadeSeconds = 0.2;

    /// <summary>Padding on a retiring voice's lifetime so the buffers already handed to the device drain
    /// after the ramp has reached silence.</summary>
    private const double DisposeGraceSeconds = 0.3;

    private readonly object gate = new();
    private readonly List<Voice> retiring = [];
    private byte[]? track;
    private Voice? current;
    private float speed = 1f;
    private float levelScale = 1f;

    /// <summary>The owner's own level, kept apart from the track's so a bar dragged during a
    /// minigame cannot wipe the softer level that minigame asked to play at.</summary>
    private float volume = 1f;
    private bool muted;
    private bool disposed;

    public bool IsPlaying
    {
        get
        {
            lock (this.gate)
            {
                return this.current != null;
            }
        }
    }

    /// <summary>Starts the ogg at <paramref name="path"/> on loop, fading in. Always restarts from the top,
    /// so a caller that wants to keep the current playhead should call <see cref="SetSpeed"/> instead.
    /// <paramref name="levelScale"/> scales the fixed under-the-game level, for a theme meant softer still.</summary>
    public void Play(string path, float speed, float levelScale = 1f)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex)
        {
            UiHost.Log.Debug(ex, "[Bgm] Could not read {Path}.", path);
            return;
        }

        lock (this.gate)
        {
            if (this.disposed)
            {
                return;
            }
            this.speed = Math.Clamp(speed, MinSpeed, MaxSpeed);
            this.levelScale = Math.Clamp(levelScale, 0f, 1f);
            var voice = TryCreateVoice(bytes, this.speed, 0L);
            if (voice == null)
            {
                return;
            }
            RetireLocked(this.current, FadeSeconds);
            this.track = bytes;
            this.current = voice;
            voice.Ramp.FadeTo(this.muted ? 0f : Level * this.levelScale * this.volume, FadeSeconds);
        }
    }

    /// <summary>Re-rates playback, cross-fading a fresh voice in from the current playhead.</summary>
    public void SetSpeed(float speed)
    {
        var next = Math.Clamp(speed, MinSpeed, MaxSpeed);
        lock (this.gate)
        {
            if (this.disposed || Math.Abs(next - this.speed) < SpeedEpsilon)
            {
                return;
            }
            this.speed = next;
            if (this.current == null || this.track == null)
            {
                return;
            }
            var voice = TryCreateVoice(this.track, next, this.current.Position);
            if (voice == null)
            {
                return;
            }
            RetireLocked(this.current, CrossfadeSeconds);
            this.current = voice;
            voice.Ramp.FadeTo(this.muted ? 0f : Level * this.levelScale * this.volume, CrossfadeSeconds);
        }
    }

    /// <summary>The owner's own level, 0 to 1, ramped rather than stepped so a dragged bar slides
    /// instead of clicking. It multiplies <see cref="Level"/> the same way the one handed to
    /// <see cref="Play"/> does, so a caller may set it before a track or during one; the two are the
    /// same number and the last one to arrive wins.</summary>
    public void SetLevelScale(float scale)
    {
        lock (this.gate)
        {
            var clamped = Math.Clamp(scale, 0f, 1f);
            if (this.disposed || Math.Abs(this.volume - clamped) < 0.001f)
            {
                return;
            }
            this.volume = clamped;
            if (!this.muted)
            {
                this.current?.Ramp.FadeTo(Level * this.levelScale * this.volume, MuteFadeSeconds);
            }
        }
    }

    /// <summary>Ramps to silence without stopping, so unmuting picks the track up where it kept running.</summary>
    public void SetMuted(bool muted)
    {
        lock (this.gate)
        {
            if (this.disposed || this.muted == muted)
            {
                return;
            }
            this.muted = muted;
            this.current?.Ramp.FadeTo(muted ? 0f : Level * this.levelScale * this.volume, MuteFadeSeconds);
        }
    }

    /// <summary>Stops the loop, fading over <paramref name="seconds"/>. A scene that ends on its own
    /// beat wants its own fade, and the default is the one every other caller already got.</summary>
    public void Stop(double seconds = FadeSeconds)
    {
        lock (this.gate)
        {
            RetireLocked(this.current, seconds);
            this.current = null;
        }
    }

    public void Dispose()
    {
        Voice[] voices;
        lock (this.gate)
        {
            if (this.disposed)
            {
                return;
            }
            this.disposed = true;
            if (this.current != null)
            {
                this.retiring.Add(this.current);
                this.current = null;
            }
            voices = [.. this.retiring];
            this.retiring.Clear();
            this.track = null;
        }
        foreach (var voice in voices)
        {
            voice.Dispose();
        }
    }

    private static Voice? TryCreateVoice(byte[] bytes, float speed, long position)
    {
        VorbisWaveReader? reader = null;
        WaveOutEvent? output = null;
        try
        {
            reader = new VorbisWaveReader(new MemoryStream(bytes));
            var loop = new LoopingSampleProvider(reader, position);
            var ramp = new RampSampleProvider(new SpeedShiftSampleProvider(loop, speed));
            output = new WaveOutEvent { DeviceNumber = NotificationSoundPlayer.ResolveDeviceIndex() };
            output.Init(ramp);
            output.Play();
            return new Voice(reader, loop, ramp, output);
        }
        catch (Exception ex)
        {
            UiHost.Log.Debug(ex, "[Bgm] Playback failed.");
            output?.Dispose();
            reader?.Dispose();
            return null;
        }
    }

    private void RetireLocked(Voice? voice, double seconds)
    {
        if (voice == null)
        {
            return;
        }
        voice.Ramp.FadeTo(0f, seconds);
        this.retiring.Add(voice);
        _ = DisposeAfterAsync(voice, seconds + DisposeGraceSeconds);
    }

    private async Task DisposeAfterAsync(Voice voice, double seconds)
    {
        await Task.Delay(TimeSpan.FromSeconds(seconds)).ConfigureAwait(false);
        lock (this.gate)
        {
            if (!this.retiring.Remove(voice))
            {
                return;
            }
        }
        voice.Dispose();
    }

    private sealed class Voice(VorbisWaveReader reader, LoopingSampleProvider loop, RampSampleProvider ramp, WaveOutEvent output)
        : IDisposable
    {
        public RampSampleProvider Ramp { get; } = ramp;

        public long Position => loop.Position;

        public void Dispose()
        {
            // WaveOutEvent.Stop throws when the device has gone away, and a shared try would then skip both
            // disposes and leak the waveOut handle with its callback thread.
            try
            {
                output.Stop();
            }
            catch (Exception ex)
            {
                UiHost.Log.Debug(ex, "[Bgm] Voice stop failed.");
            }
            try
            {
                output.Dispose();
            }
            catch (Exception ex)
            {
                UiHost.Log.Debug(ex, "[Bgm] Voice output disposal failed.");
            }
            finally
            {
                reader.Dispose();
            }
        }
    }

    /// <summary>Seeks back to the start on end of stream, so the track never runs out. It also publishes the
    /// playhead the audio thread last read, which is what a speed change resumes from: reading the decoder's
    /// own position from another thread would race the decode.</summary>
    private sealed class LoopingSampleProvider : ISampleProvider
    {
        private readonly VorbisWaveReader reader;
        private long position;

        public LoopingSampleProvider(VorbisWaveReader reader, long startPosition)
        {
            this.reader = reader;
            if (startPosition > 0 && startPosition < reader.Length)
            {
                try
                {
                    reader.Position = startPosition;
                    this.position = startPosition;
                }
                catch (Exception ex)
                {
                    UiHost.Log.Debug(ex, "[Bgm] Seek failed; starting from the top.");
                }
            }
        }

        public WaveFormat WaveFormat => this.reader.WaveFormat;

        public long Position => Volatile.Read(ref this.position);

        public int Read(float[] buffer, int offset, int count)
        {
            try
            {
                var filled = 0;
                var rewound = false;
                while (filled < count)
                {
                    var read = this.reader.Read(buffer, offset + filled, count - filled);
                    if (read > 0)
                    {
                        filled += read;
                        rewound = false;
                        continue;
                    }
                    if (rewound)
                    {
                        break;
                    }
                    this.reader.Position = 0;
                    rewound = true;
                }
                Volatile.Write(ref this.position, this.reader.Position);
                return filled;
            }
            catch (Exception ex)
            {
                UiHost.Log.Debug(ex, "[Bgm] Decode failed.");
                return 0;
            }
        }
    }

    /// <summary>Pass-through that lies about its sample rate, which is what makes the track play faster and
    /// higher as the rate climbs.</summary>
    private sealed class SpeedShiftSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider source;

        public SpeedShiftSampleProvider(ISampleProvider source, float speed)
        {
            this.source = source;
            var rate = Math.Max(MinSampleRate, (int)(source.WaveFormat.SampleRate * speed));
            this.WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(rate, source.WaveFormat.Channels);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count) => this.source.Read(buffer, offset, count);
    }

    /// <summary>Applies a gain that walks towards its target one frame at a time, so every fade is smooth and
    /// costs no timer: the walk happens on the audio thread as the samples go past.</summary>
    private sealed class RampSampleProvider(ISampleProvider source) : ISampleProvider
    {
        private readonly object gate = new();
        private float gain;
        private float target;
        private float step;

        public WaveFormat WaveFormat => source.WaveFormat;

        /// <summary>Ramps to <paramref name="target"/> over <paramref name="seconds"/>. The step covers the
        /// distance actually being travelled: a step of one-per-fade crosses FULL SCALE in that time, so a
        /// track playing at 0.3 reached silence in a third of the seconds it was given, and a five second
        /// fade-out was heard as one.</summary>
        public void FadeTo(float target, double seconds)
        {
            var frames = Math.Max(1.0, seconds * this.WaveFormat.SampleRate);
            lock (this.gate)
            {
                this.target = Math.Clamp(target, 0f, 1f);
                var distance = MathF.Max(MathF.Abs(this.target - this.gain), 0.0001f);
                this.step = (float)(distance / frames);
            }
        }

        public int Read(float[] buffer, int offset, int count)
        {
            var read = source.Read(buffer, offset, count);
            if (read <= 0)
            {
                return read;
            }

            float level;
            float goal;
            float delta;
            lock (this.gate)
            {
                level = this.gain;
                goal = this.target;
                delta = this.step;
            }

            var channels = this.WaveFormat.Channels;
            var end = offset + read;
            var i = offset;
            while (i < end)
            {
                if (level < goal)
                {
                    level = Math.Min(goal, level + delta);
                }
                else if (level > goal)
                {
                    level = Math.Max(goal, level - delta);
                }
                for (var c = 0; c < channels && i < end; c++)
                {
                    buffer[i] *= level;
                    i++;
                }
            }

            lock (this.gate)
            {
                this.gain = level;
            }
            return read;
        }
    }
}
