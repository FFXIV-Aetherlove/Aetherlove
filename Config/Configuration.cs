using AetherLove.Services;
using AetherLove.UI;
using Dalamud.Configuration;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace AetherLove.Config;

/// <summary>Persisted access and refresh tokens for the AetherLove server.</summary>
[Serializable]
public class AuthState
{
    public string AccessToken { get; set; } = "";
    public DateTimeOffset AccessTokenExpiresAtUtc { get; set; }
    public string RefreshToken { get; set; } = "";
    public DateTimeOffset RefreshTokenExpiresAtUtc { get; set; }
}

/// <summary>The user's X25519 identity keypair in unwrapped form.</summary>
[Serializable]
public class CryptoKeys
{
    public byte[] PublicKey { get; set; } = [];
    public byte[] PrivateKey { get; set; } = [];
}

/// <summary>Background presence cadence. Persisted so the schedule survives restarts.</summary>
[Serializable]
public class PulseState
{
    /// <summary>When the user last interacted with the plugin.</summary>
    public DateTimeOffset? LastActivityUtc { get; set; }

    /// <summary>Earliest time the next pulse may surface (last activity + a random window).</summary>
    public DateTimeOffset? NextEligibleUtc { get; set; }

    /// <summary>Set once a pulse has ever surfaced; gates the visibility of its opt-out.</summary>
    public bool SeenPulse { get; set; }

    /// <summary>User opted out.</summary>
    public bool MutePulse { get; set; }
}

[Serializable]
public class Configuration : IPluginConfiguration
{
    /// <summary>Dalamud config-schema version; bump when the stored shape changes.</summary>
    public int Version { get; set; } = 0;

    /// <summary>The active colour theme.</summary>
    public AppTheme SelectedTheme { get; set; } = AppTheme.CrystalVoid;

    /// <summary>Phone size preset; scales the whole UI uniformly.</summary>
    public PhoneScalePreset PhoneSize { get; set; } = PhoneScalePreset.Small;

    /// <summary>Minimised-bubble size preset; Medium is the bubble's authored (current) size.</summary>
    public PhoneScalePreset MiniPhoneSize { get; set; } = PhoneScalePreset.Medium;

    /// <summary>Chat-bubble colour overrides; null means "use the theme default" (see <see cref="ChatColors"/>).</summary>
    public Vector4? OwnChatBg { get; set; }
    public Vector4? OwnChatFg { get; set; }
    public Vector4? PeerChatBg { get; set; }
    public Vector4? PeerChatFg { get; set; }

    /// <summary>Server access + refresh tokens for this install.</summary>
    public AuthState Auth { get; set; } = new();

    /// <summary>This install's identity keypair, unwrapped.</summary>
    public CryptoKeys Crypto { get; set; } = new();

    /// <summary>Stable per-install device id. Format: <c>AetherLove-Plugin-XXXXXX</c>.</summary>
    public string DeviceId { get; set; } = "";

    /// <summary>UI language for the plugin (e.g. "English", "French").</summary>
    public string PluginLanguage { get; set; } = "English";

    /// <summary>Blur NSFW photos until the viewer reveals each one.</summary>
    public bool AlwaysBlurNsfw { get; set; } = true;

    /// <summary>Mutes the lub-dub heartbeat that plays on the startup splash.</summary>
    public bool DisableStartupHeartbeatSound { get; set; } = false;

    /// <summary>Per-machine WebP-decode capability, probed at startup. Null until first probed; null/false makes
    /// the server transcode photos to JPEG (safe default — never gray blocks).</summary>
    public bool? WebpSupported { get; set; } = null;

    /// <summary>Debug-screen override: force the server to send JPEG photos regardless of the WebP probe.</summary>
    public bool ForceJpegImages { get; set; } = false;

    /// <summary>Skip the "close AetherLove?" confirmation modal and close the windows immediately.</summary>
    public bool SkipCloseConfirmation { get; set; } = false;

    /// <summary>Master switch over every notification; when off the options below are ignored.</summary>
    public bool EnableNotifications { get; set; } = true;

    /// <summary>Play a sound when a notification is shown.</summary>
    public bool EnableNotificationSounds { get; set; } = false;

    /// <summary>Which bundled sound to play for notifications.</summary>
    public NotificationSound NotificationSoundChoice { get; set; } = NotificationSound.Msn;

    /// <summary>Add a game chat-log line when a message arrives.</summary>
    public bool NotifyChatOnMessage { get; set; } = true;
    /// <summary>Add a game chat-log line when a new match is made.</summary>
    public bool NotifyChatOnMatch { get; set; } = true;
    /// <summary>Show a Dalamud toast when a message arrives.</summary>
    public bool NotifyPopupOnMessage { get; set; } = true;
    /// <summary>Show a Dalamud toast when a new match is made.</summary>
    public bool NotifyPopupOnMatch { get; set; } = true;

    /// <summary>Suppress every notification (chat, popup, sound) while the player is in combat.</summary>
    public bool HideNotificationsDuringCombat { get; set; } = true;

    /// <summary>Open the minimised bubble automatically on character login.</summary>
    public bool AutoOpenMinimizedOnLogin { get; set; } = true;

    /// <summary>What to do with the plugin windows when the player enters combat.</summary>
    public CombatBehavior CombatBehavior { get; set; } = CombatBehavior.Hide;

    /// <summary>Set after the first launch so onboarding is only force-opened on a fresh install.</summary>
    public bool HasCompletedFirstLaunch { get; set; } = false;

    /// <summary>Set once the user has acknowledged the link-safety warning shown the first time they copy
    /// another player's profile text, so it appears only once.</summary>
    public bool AcknowledgedProfileCopyTextWarning { get; set; } = false;

    /// <summary>Changelog versions ("Major.Minor.Build") whose "What's New" window has already been shown.</summary>
    public HashSet<string> ShownChangelogVersions { get; set; } = [];

    /// <summary>Peer profile ids of matches the user has archived. Client-side only (per install). Replaced
    /// wholesale on change so it serializes as a consistent snapshot; <see cref="ChatArchiveStore"/>
    /// owns the live access.</summary>
    public List<Guid> ArchivedMatches { get; set; } = [];

    /// <summary>Show the search row on the matches screen (toggled from its overflow menu).</summary>
    public bool ShowChatSearch { get; set; } = false;

    /// <summary>Background presence cadence state.</summary>
    public PulseState Pulse { get; set; } = new();

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
