using System;
using System.IO;
using System.Media;
using System.Threading.Tasks;

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

/// <summary>Plays a bundled notification .wav off-thread.</summary>
public static class NotificationSoundPlayer
{
    public static void Play(NotificationSound sound)
    {
        // SoundPlayer bypasses the game mixer, so honour the game's mute switches manually.
        if (!AccessibilityService.SoundEffectsEnabled)
        {
            return;
        }

        var file = sound.FileName();
        _ = Task.Run(() =>
        {
            try
            {
                var dir = Path.GetDirectoryName(Plugin.PluginInterface.AssemblyLocation.FullName) ?? "";
                var path = Path.Combine(dir, "Media", "notifications", file);
                if (!File.Exists(path))
                {
                    Plugin.Log.Debug($"[NotificationSound] {path} not found; skipping playback.");
                    return;
                }

                using var player = new SoundPlayer(path);
                player.PlaySync();
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "[NotificationSound] Playback failed.");
            }
        });
    }
}
