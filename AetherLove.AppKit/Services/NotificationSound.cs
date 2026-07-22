using System;
using System.IO;
using System.Media;

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

/// <summary>Plays a bundled notification .wav. One shared player: a new sound interrupts the previous one
/// instead of stacking, so spamming the preview collapses to a single chime rather than queuing a backlog,
/// and <see cref="Stop"/> silences it when the sound-settings screen is left.</summary>
public static class NotificationSoundPlayer
{
    private static readonly object Gate = new();
    private static SoundPlayer? _player;

    /// <summary>The bundled-sound folder (Media/notifications, next to the plugin assembly).</summary>
    public static string SoundDirectory =>
        Path.Combine(Path.GetDirectoryName(UiHost.PluginInterface.AssemblyLocation.FullName) ?? "", "Media", "notifications");

    /// <summary>Full path to a sound's .wav on disk.</summary>
    public static string ResolvePath(NotificationSound sound) => Path.Combine(SoundDirectory, sound.FileName());

    public static void Play(NotificationSound sound)
    {
        // SoundPlayer bypasses the game mixer, so honour the game's mute switches manually.
        if (!AccessibilityService.SoundEffectsEnabled)
        {
            return;
        }

        var path = ResolvePath(sound);
        if (!File.Exists(path))
        {
            UiHost.Log.Debug($"[NotificationSound] {path} not found; skipping playback.");
            return;
        }

        lock (Gate)
        {
            try
            {
                _player?.Stop();
                _player?.Dispose();
                // Async play (not PlaySync): the OS keeps a single active sound, so a rapid burst never
                // queues, and Stop() ends it immediately.
                _player = new SoundPlayer(path);
                _player.Play();
            }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, "[NotificationSound] Playback failed.");
            }
        }
    }

    /// <summary>Plays a sound for the diagnostics tool, returning a human-readable outcome rather than swallowing
    /// failures. When <paramref name="bypassMuteGate"/> is set the in-game SE mute/volume check is skipped, so the
    /// caller can tell a muted-game problem apart from broken playback.</summary>
    public static string TestPlay(NotificationSound sound, bool bypassMuteGate)
    {
        if (!bypassMuteGate && !AccessibilityService.SoundEffectsEnabled)
        {
            return "Blocked by the in-game Sound Effects mute/volume (see the values above). Use \"ignore game mute\" to test anyway.";
        }

        var path = ResolvePath(sound);
        if (!File.Exists(path))
        {
            return $"File not found: {path}";
        }

        lock (Gate)
        {
            try
            {
                _player?.Stop();
                _player?.Dispose();
                _player = new SoundPlayer(path);
                _player.Play();
                return "Playing. If you hear nothing, check Windows' Volume Mixer for the game (ffxiv_dx11.exe) and your output device.";
            }
            catch (Exception ex)
            {
                return $"Playback threw {ex.GetType().Name}: {ex.Message}";
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
                _player?.Stop();
            }
            catch
            {
                // Best effort; nothing to recover if the handle is already gone.
            }
        }
    }
}
