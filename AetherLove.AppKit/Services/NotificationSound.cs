using System;
using System.IO;
using NAudio.Wave;
// The reader is fed from memory on purpose: streaming from the bundled file kept it locked inside the
// game process, which broke plugin rebuilds and updates.
using NAudio.Wave.SampleProviders;

namespace AetherLove.Services;

/// <summary>Bundled notification sounds in Media/notifications.</summary>
public enum NotificationSound
{
    Aol = 0,
    Facebook = 1,
    FacebookOldskool = 2,
    Icq = 3,
    Iphone = 4,
    Msn = 5,
    Samsung = 6,
    SamsungWhistle = 7,
    Xiaomi = 8,
}

public static class NotificationSoundExtensions
{
    public static string DisplayName(this NotificationSound sound) => sound switch
    {
        NotificationSound.Aol => "AOL",
        NotificationSound.Facebook => "Facebook",
        NotificationSound.FacebookOldskool => "Facebook (Old Skool)",
        NotificationSound.Icq => "ICQ",
        NotificationSound.Iphone => "iPhone",
        NotificationSound.Msn => "MSN",
        NotificationSound.Samsung => "Samsung",
        NotificationSound.SamsungWhistle => "Samsung Whistle",
        NotificationSound.Xiaomi => "Xiaomi",
        _ => sound.ToString(),
    };

    public static string FileName(this NotificationSound sound) => sound switch
    {
        NotificationSound.Aol => "aol.wav",
        NotificationSound.Facebook => "facebook.wav",
        NotificationSound.FacebookOldskool => "facebook_oldskool.wav",
        NotificationSound.Icq => "icq.wav",
        NotificationSound.Iphone => "iphone.wav",
        NotificationSound.Msn => "msn.wav",
        NotificationSound.Samsung => "samsung.wav",
        NotificationSound.SamsungWhistle => "samsung_whistle.wav",
        NotificationSound.Xiaomi => "xiaomi.wav",
        _ => "msn.wav",
    };
}

/// <summary>Plays a bundled notification .wav through NAudio's waveOut backend, honouring the app's own
/// volume and output-device settings; the game's audio device and mute switches are deliberately not
/// involved. One shared output: a new sound interrupts the previous one instead of stacking, so spamming the
/// preview collapses to a single chime, and <see cref="Stop"/> silences it when a settings screen is left.
/// The output device is stored by NAME so a shifting waveOut index (devices plugged/unplugged) can never
/// silently reroute; a missing name falls back to the system default.</summary>
public static class NotificationSoundPlayer
{
    private static readonly object Gate = new();
    private static WaveOutEvent? _output;
    private static WaveFileReader? _reader;

    /// <summary>The bundled-sound folder (Media/notifications, next to the plugin assembly).</summary>
    public static string SoundDirectory =>
        Path.Combine(Path.GetDirectoryName(UiHost.PluginInterface.AssemblyLocation.FullName) ?? "", "Media", "notifications");

    /// <summary>Full path to a sound's .wav on disk.</summary>
    public static string ResolvePath(NotificationSound sound) => Path.Combine(SoundDirectory, sound.FileName());

    /// <summary>The waveOut device names available right now, in device order.</summary>
    public static string[] ListDevices()
    {
        try
        {
            var names = new string[WaveOut.DeviceCount];
            for (var i = 0; i < names.Length; i++)
            {
                names[i] = WaveOut.GetCapabilities(i).ProductName;
            }
            return names;
        }
        catch (Exception ex)
        {
            UiHost.Log.Warning(ex, "[NotificationSound] Device enumeration failed.");
            return [];
        }
    }

    /// <summary>Human-readable description of where sound goes right now, for settings and diagnostics.</summary>
    public static string DescribeOutput()
    {
        var configured = UiHost.Configuration.OsSettings.AudioOutputDevice;
        if (configured.Length == 0)
        {
            return "System default";
        }
        return ResolveDeviceIndex() >= 0
            ? configured
            : $"{configured} (not found, using the system default)";
    }

    private static int ResolveDeviceIndex()
    {
        var configured = UiHost.Configuration.OsSettings.AudioOutputDevice;
        if (configured.Length == 0)
        {
            return -1;
        }
        try
        {
            for (var i = 0; i < WaveOut.DeviceCount; i++)
            {
                if (WaveOut.GetCapabilities(i).ProductName == configured)
                {
                    return i;
                }
            }
        }
        catch (Exception ex)
        {
            UiHost.Log.Warning(ex, "[NotificationSound] Device lookup failed; using the system default.");
        }
        return -1;
    }

    public static void Play(NotificationSound sound)
    {
        if (TryPlay(sound) is { } error)
        {
            UiHost.Log.Warning("[NotificationSound] Playback failed: {Error}", error);
        }
    }

    /// <summary>Plays and reports the outcome instead of just logging it: null when playback started, else a
    /// human-readable error for the settings test button and the diagnostics tool.</summary>
    public static string? TryPlay(NotificationSound sound)
    {
        var path = ResolvePath(sound);
        if (!File.Exists(path))
        {
            return $"File not found: {path}";
        }

        lock (Gate)
        {
            try
            {
                StopLocked();
                _reader = new WaveFileReader(new MemoryStream(File.ReadAllBytes(path)));
                var volume = new VolumeSampleProvider(_reader.ToSampleProvider())
                {
                    Volume = Math.Clamp(UiHost.Configuration.OsSettings.NotificationVolume, 0f, 1f),
                };
                _output = new WaveOutEvent { DeviceNumber = ResolveDeviceIndex() };
                _output.Init(volume);
                _output.Play();
                return null;
            }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, "[NotificationSound] Playback failed.");
                return $"{ex.GetType().Name}: {ex.Message}";
            }
        }
    }

    /// <summary>Silences any in-progress playback (e.g. leaving the notification-sound settings).</summary>
    public static void Stop()
    {
        lock (Gate)
        {
            try
            {
                StopLocked();
            }
            catch
            {
                // Best effort; nothing to recover if the handle is already gone.
            }
        }
    }

    private static void StopLocked()
    {
        _output?.Stop();
        _output?.Dispose();
        _reader?.Dispose();
        _output = null;
        _reader = null;
    }
}
