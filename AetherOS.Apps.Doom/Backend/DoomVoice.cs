using System;

namespace AetherOS.Apps.Doom.Backend;

/// <summary>One playing sound effect. Doom's lumps are 8-bit unsigned mono at their own sample rate
/// (usually 11025 Hz), so each voice resamples on the fly into the mixer's 44.1 kHz stereo.
///
/// The upstream desktop frontend handed a 3D position to OpenAL and let it do the spatialisation. There is
/// no such thing here, so the caller collapses the source direction into a stereo pan and a gain, which is
/// what OpenAL was ultimately doing for a listener with two ears.
///
/// The game thread writes the fields while NAudio's playback thread reads them, hence the lock: it is taken
/// once per buffer (about every 20 ms), not per sample.</summary>
internal sealed class DoomVoice
{
    private readonly object gate = new();

    private byte[]? samples;
    private double step;
    private double position;
    private float gainLeft;
    private float gainRight;
    private bool playing;
    private bool paused;

    public bool Playing
    {
        get
        {
            lock (this.gate)
            {
                return this.playing;
            }
        }
    }

    public void Play(byte[] pcm, int sampleRate, float volume, float pan, float pitch)
    {
        lock (this.gate)
        {
            this.samples = pcm;
            this.step = sampleRate * (double)pitch / DoomAudioOutput.SampleRate;
            this.position = 0.0;
            this.playing = pcm.Length > 0;
            this.paused = false;
            ApplyGain(volume, pan);
        }
    }

    public void SetLevel(float volume, float pan)
    {
        lock (this.gate)
        {
            ApplyGain(volume, pan);
        }
    }

    public void Stop()
    {
        lock (this.gate)
        {
            this.playing = false;
            this.samples = null;
        }
    }

    public void Pause()
    {
        lock (this.gate)
        {
            this.paused = true;
        }
    }

    public void Resume()
    {
        lock (this.gate)
        {
            this.paused = false;
        }
    }

    /// <summary>Fills <paramref name="buffer"/> with interleaved stereo. False means the voice contributed
    /// nothing and the caller can skip the accumulate.</summary>
    public bool Render(float[] buffer, int count)
    {
        lock (this.gate)
        {
            if (!this.playing || this.paused || this.samples is not { } pcm)
            {
                return false;
            }

            for (var i = 0; i < count; i += 2)
            {
                var index = (int)this.position;
                if (index >= pcm.Length)
                {
                    this.playing = false;
                    this.samples = null;
                    Array.Clear(buffer, i, count - i);
                    return true;
                }

                // 8-bit unsigned, so 128 is silence.
                var sample = (pcm[index] - 128) / 128f;
                buffer[i] = sample * this.gainLeft;
                buffer[i + 1] = sample * this.gainRight;
                this.position += this.step;
            }
            return true;
        }
    }

    /// <summary>Equal-power pan, so a sound sweeping past the player keeps a steady loudness instead of
    /// dipping as it crosses the centre.</summary>
    private void ApplyGain(float volume, float pan)
    {
        var angle = (Math.Clamp(pan, -1f, 1f) + 1f) * 0.25f * MathF.PI;
        this.gainLeft = volume * MathF.Cos(angle);
        this.gainRight = volume * MathF.Sin(angle);
    }
}
