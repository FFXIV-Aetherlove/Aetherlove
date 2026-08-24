using System.Collections.Generic;
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

    /// <summary>App sound volume, 0..1, applied per playback.</summary>
    public float NotificationVolume { get; set; } = 0.8f;

    /// <summary>waveOut device product name; empty = the system default. Stored by name so a shifting device
    /// index can never silently reroute.</summary>
    public string AudioOutputDevice { get; set; } = string.Empty;

    /// <summary>Suppress every notification (chat, popup, sound) while the player is in combat.</summary>
    public bool HideNotificationsDuringCombat { get; set; } = true;

    /// <summary>Open the minimised bubble automatically on character login.</summary>
    public bool AutoOpenMinimizedOnLogin { get; set; } = true;

    /// <summary>What to do with the plugin windows when the player enters combat.</summary>
    public CombatBehavior CombatBehavior { get; set; } = CombatBehavior.Hide;

    /// <summary>Keep AetherLove drawing during group pose, overriding Dalamud's hide-plugins-in-gpose.</summary>
    public bool ShowDuringGpose { get; set; } = true;

    /// <summary>Whether the together-mode explainer has been shown once. Set when its last page is
    /// dismissed, so the first create or join teaches the feature and no later one interrupts it.</summary>
    public bool TogetherOnboardingSeen { get; set; }

    /// <summary>Receiving half of party pets: whether the Aetherlings of the party's members gather around
    /// your own out on the game screen. The sending half is the account's own switch, server-side, so a pet
    /// only ever appears when BOTH are on.</summary>
    public bool PartyPetsShown { get; set; } = true;

    /// <summary>How big the party's pets stand, as an index into the floating pet's own size ladder.
    /// Deliberately separate from your own size: a screen full of party pets wants smaller ones.</summary>
    public int PartyPetSize { get; set; } = 1;

    /// <summary>The message-translation opt-in (ADR 9). False until the user accepts the consent
    /// explainer: translating sends the text to Google, a third party, so it is never on by default.</summary>
    public bool TranslationsEnabled { get; set; }

    /// <summary>Target language code for translations ("en", "de", "ja").</summary>
    public string TranslationLanguage { get; set; } = "en";

    /// <summary>Whether the translation opt-in has been put in front of the user once: the OS onboarding
    /// step for fresh phones, the one-time update offer for phones that predate the feature. Never blocks
    /// the settings toggle; it only stops the offer from nagging twice.</summary>
    public bool TranslationOfferSeen { get; set; }

    /// <summary>Hide AetherLove during cutscenes (Dalamud's default). Unset to keep it drawing through them.</summary>
    public bool HideDuringCutscenes { get; set; } = true;

    /// <summary>Play the "Read Tomestone" (/tomescroll) looping emote while the phone is open and focused, so the
    /// character appears to be reading their device. Only starts in a safe area and never interrupts another
    /// emote; it isn't force-cancelled, so it ends on its own the next time the player moves.</summary>
    public bool ShowTomestoneEmote { get; set; } = false;

    /// <summary>Widgets the player took off the widget page, by id: the built-in ones are named
    /// ("clock", "status", "notifications", "party") and an app's widget is its app id. A list of what is
    /// HIDDEN rather than what is shown, so a new widget (or a newly installed app) appears on its own
    /// rather than waiting to be added. The connection card starts off it: it says nothing a phone that is
    /// working does not already show, and it is one right-click away.</summary>
    public List<string> HiddenWidgets { get; set; } = ["status"];

    /// <summary>The player's own top-to-bottom order for the widget page, by the same ids. Empty until a card
    /// is dragged, and then it holds every widget the page knew at that moment, hidden ones included, so
    /// putting one back keeps the place it had. An id the list has never seen (a newly installed app) is drawn
    /// after the ones it has.</summary>
    public List<string> WidgetOrder { get; set; } = [];

    /// <summary>Unlocked the hidden developer settings by tapping the About version enough times.</summary>
    public bool DeveloperMode { get; set; } = false;
}
