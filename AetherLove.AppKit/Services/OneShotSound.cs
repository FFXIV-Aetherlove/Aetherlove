using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NAudio.Vorbis;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AetherLove.Services;

/// <summary>Plays a single ogg through once. Decoded from memory like <see cref="BgmPlayer"/> so no file
/// handle is held inside the game process, and on the shared output device the notification sounds use.
///
/// Voices are kept until they finish rather than disposed at the call, because disposing an output that is
/// still draining cuts the sound off mid-word.</summary>
public sealed class OneShotSound : IDisposable
{
    private readonly object gate = new();
    private readonly List<Voice> voices = [];
    private bool disposed;

    /// <summary>Fires and forgets. A missing or undecodable file is silence, never an error: a sound effect
    /// is never worth taking a screen down for.</summary>
    public void Play(string path, float volume = 1f)
    {
        byte[] bytes;
        try
        {
            if (!File.Exists(path))
            {
                return;
            }
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex)
        {
            UiHost.Log.Debug($"[OneShotSound] Could not read {path}: {ex.Message}");
            return;
        }

        lock (this.gate)
        {
            if (this.disposed)
            {
                return;
            }
            Sweep();
            try
            {
                this.voices.Add(new Voice(bytes, Math.Clamp(volume, 0f, 1f)));
            }
            catch (Exception ex)
            {
                UiHost.Log.Debug($"[OneShotSound] Playback failed: {ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        lock (this.gate)
        {
            this.disposed = true;
            foreach (var voice in this.voices)
            {
                voice.Dispose();
            }
            this.voices.Clear();
        }
    }

    private void Sweep()
    {
        for (var i = this.voices.Count - 1; i >= 0; i--)
        {
            if (this.voices[i].Finished)
            {
                this.voices[i].Dispose();
                this.voices.RemoveAt(i);
            }
        }
    }

    private sealed class Voice : IDisposable
    {
        private readonly VorbisWaveReader reader;
        private readonly WaveOutEvent output;

        public Voice(byte[] bytes, float volume)
        {
            this.reader = new VorbisWaveReader(new MemoryStream(bytes, writable: false));
            var sample = this.reader.ToSampleProvider();
            this.output = new WaveOutEvent { DeviceNumber = NotificationSoundPlayer.ResolveDeviceIndex() };
            this.output.Init(new VolumeSampleProvider(sample) { Volume = volume });
            this.output.PlaybackStopped += (_, _) => this.Finished = true;
            this.output.Play();
        }

        public bool Finished { get; private set; }

        public void Dispose()
        {
            try
            {
                this.output.Dispose();
            }
            catch (Exception ex)
            {
                UiHost.Log.Debug($"[OneShotSound] Disposing a voice threw: {ex.Message}");
            }
            try
            {
                this.reader.Dispose();
            }
            catch (Exception ex)
            {
                UiHost.Log.Debug($"[OneShotSound] Disposing a reader threw: {ex.Message}");
            }
        }
    }
}
