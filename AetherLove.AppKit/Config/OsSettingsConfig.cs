using AetherLove.Services;

namespace AetherLove.Config;

/// <summary>Phone-wide (AetherOS) settings surfaced by the Settings app: general behaviour, language, and the
/// notification infrastructure. Split out of the root <see cref="Configuration"/> so these OS-level settings live
/// in one place. The old root-level properties remain as forwarders (see <see cref="Configuration"/>) so existing
/// config files migrate on load and every existing reader keeps working unchanged.</summary>
public sealed class OsSettingsConfig
{
    /// <summary>UI language for the plugin (e.g. "English", "French").</summary>
    public string PluginLanguage { get; set; } = "English";

    /// <summary>Show wall clocks (home screen + status bar) in 24-hour form (22:23) rather than 12-hour (10:23).
    /// No AM/PM either way: the digits-only Clock font can't render letters.</summary>
    public bool Use24HourClock { get; set; } = true;

    /// <summary>Skip the "close AetherLove?" confirmation modal and close the windows immediately.</summary>
    public bool SkipCloseConfirmation { get; set; } = false;

    /// <summary>Master switch over every notification; when off the options below are ignored.</summary>
    public bool EnableNotifications { get; set; } = true;

    /// <summary>Play a sound when a notification is shown.</summary>
    public bool EnableNotificationSounds { get; set; } = false;

    /// <summary>Which bundled sound to play for notifications.</summary>
    public NotificationSound NotificationSoundChoice { get; set; } = NotificationSound.Msn;

    /// <summary>Suppress every notification (chat, popup, sound) while the player is in combat.</summary>
    public bool HideNotificationsDuringCombat { get; set; } = true;

    /// <summary>Open the minimised bubble automatically on character login.</summary>
    public bool AutoOpenMinimizedOnLogin { get; set; } = true;

    /// <summary>What to do with the plugin windows when the player enters combat.</summary>
    public CombatBehavior CombatBehavior { get; set; } = CombatBehavior.Hide;

    /// <summary>Keep AetherLove drawing during group pose, overriding Dalamud's hide-plugins-in-gpose.</summary>
    public bool ShowDuringGpose { get; set; } = true;

    /// <summary>Hide AetherLove during cutscenes (Dalamud's default). Unset to keep it drawing through them.</summary>
    public bool HideDuringCutscenes { get; set; } = true;

    /// <summary>Play the "Read Tomestone" (/tomescroll) looping emote while the phone is open and focused, so the
    /// character appears to be reading their device. Only starts in a safe area and never interrupts another
    /// emote; it isn't force-cancelled, so it ends on its own the next time the player moves.</summary>
    public bool ShowTomestoneEmote { get; set; } = false;

    /// <summary>Unlocked the hidden developer settings by tapping the About version enough times.</summary>
    public bool DeveloperMode { get; set; } = false;
}
