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

    /// <summary>The AetherLove profile this install is acting as. Sent with every token refresh so the
    /// selection survives token rotation; also the key under which the flat per-profile config state below is
    /// stashed on a profile switch. Null on a pre-multi-profile config until the first bootstrap stamps it.</summary>
    public Guid? ActiveProfileId { get; set; }
}

/// <summary>The user's X25519 identity keypair in unwrapped form.</summary>
[Serializable]
public class CryptoKeys
{
    public byte[] PublicKey { get; set; } = [];
    public byte[] PrivateKey { get; set; } = [];
}

/// <summary>Echo's local state: where its browser runtime lives and how it is played back.</summary>
[Serializable]
public class EchoClientState
{
    /// <summary>Development escape hatch pointing straight at a local WatchHost build output, skipping the
    /// runtime download entirely. Empty in every shipped install.</summary>
    public string HostPathOverride { get; set; } = "";

    /// <summary>The installed runtime version, so a manifest bump can be detected without touching disk.</summary>
    public string InstalledHostVersion { get; set; } = "";

    /// <summary>Playback volume, 0 to 1, shared by solo and room watching.</summary>
    public float Volume { get; set; } = 0.7f;

    /// <summary>Set once the onboarding has been completed; the app opens straight into its home after.</summary>
    public bool SetupDone { get; set; }

    /// <summary>What the browser renders at, which is what YouTube picks its stream quality from. Matching
    /// the stage was the old behaviour and is why a small window got a low-resolution stream.</summary>
    public EchoRenderQuality Quality { get; set; } = EchoRenderQuality.Hd1080;

    /// <summary>Subtitles and closed captions, off by default. Toggling it re-loads the current video.</summary>
    public bool Captions { get; set; }

    /// <summary>The room owner's queue auto-advance. Only the owner's client acts on it.</summary>
    public bool AutoPlayNext { get; set; } = true;

    /// <summary>Runs the browser's compositing on the CPU. Off by default because the GPU path is what makes
    /// 1080p watchable, but some drivers handle Chromium's GPU process badly enough that the video stutters
    /// (the same machines usually see it in Discord and in Chromium browsers too), and software rendering is
    /// the way out. Read when the playback host is launched, so it takes a host restart to apply.</summary>
    public bool DisableHardwareAcceleration { get; set; }
}

/// <summary>The browser's render size. <see cref="MatchStage"/> follows the stage, which is cheapest and
/// worst-looking; the fixed sizes cost the same copy no matter how small the window is.</summary>
public enum EchoRenderQuality
{
    MatchStage = 0,
    Hd720 = 1,
    Hd1080 = 2,
}

/// <summary>Background presence cadence.</summary>
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

/// <summary>Client-side tracking for spark activity milestones. Only an optimization: the server dedups
/// every report, so lost or stale state costs at most one wasted call.</summary>
[Serializable]
public class SparkClientState
{
    /// <summary>The UTC day (yyyy-MM-dd) the fields below belong to; a new day resets them.</summary>
    public string ActivityDayUtc { get; set; } = string.Empty;

    /// <summary>Distinct app ids foregrounded today, kept until the three-apps milestone is reported.</summary>
    public List<string> AppsOpenedToday { get; set; } = [];

    public bool AppsMilestoneReported { get; set; }

    public bool MarketActivityReported { get; set; }

    public bool YapperCheckReported { get; set; }

    public bool GrooveReported { get; set; }

    public bool StoreVisitReported { get; set; }

    public bool WalletVisitReported { get; set; }

    public bool AetherlingGameReported { get; set; }

    /// <summary>Arcade rounds already reported today. Unlike the milestones this one counts rather than
    /// flags, because the action is worth several grants a day.</summary>
    public int ArcadeGamesReported { get; set; }
}

/// <summary>Places browse preferences.</summary>
[Serializable]
public class PlacesState
{
    /// <summary>Set once the filters and the 18+ toggle have been seeded to their defaults.</summary>
    public bool FilterDefaultsSeeded { get; set; }

    /// <summary>Selected <c>VenueTag</c> bits; 0 = every tag.</summary>
    public int TagMask { get; set; }

    /// <summary>Selected <c>Region</c> bits; 0 = every region.</summary>
    public short RegionMask { get; set; }

    /// <summary>The region mask seeded as the default (the player's home region); the "filters active" hint
    /// compares against this so the default region selection never counts as an applied filter.</summary>
    public short RegionMaskDefault { get; set; }

    /// <summary>Include 18+ venues in the browse feed; seeded from the profile's NSFW consent.</summary>
    public bool IncludeNsfw { get; set; }

    /// <summary>Hide venues tagged 24/7 from the browse feed; off by default so nothing disappears.</summary>
    public bool HideAlwaysOpen { get; set; }

    /// <summary>Venues hidden from the browse feed, id → name; the stored name renders the unhide list.</summary>
    public Dictionary<Guid, string> HiddenVenues { get; set; } = new();

    /// <summary>Set after the one-time hide explainer; the first hide attempt only shows the popup.</summary>
    public bool SeenHideVenueIntro { get; set; }

    /// <summary>Set once the Places tour has been opened, on the first visit; it stays reachable from the menu.</summary>
    public bool SeenTour { get; set; }
}

/// <summary>Levemetes browse filters. Account-level like the ads themselves, so it stays out of the
/// per-profile state swap. 18+ listings are opt-in every install; nothing seeds this from the profile.</summary>
[Serializable]
public class LevemetesState
{
    /// <summary>Selected categories, bit index = wire category value; 0 = every category.</summary>
    public int CategoryMask { get; set; }

    /// <summary>Selected <c>Region</c> bits; 0 = every region.</summary>
    public short RegionMask { get; set; }

    /// <summary>Looking-for/offering restriction; 0 = both.</summary>
    public short Kind { get; set; }

    /// <summary>Include 18+ listings in the browse feed; always off until toggled by hand.</summary>
    public bool IncludeNsfw { get; set; }

    /// <summary>Set once the Levemetes tour has been opened, on the first visit; it stays reachable from the menu.</summary>
    public bool SeenTour { get; set; }
}

/// <summary>Client-side hangout preferences, first-run flags, and dismissals.</summary>
[Serializable]
public class HangoutClientState
{
    /// <summary>Game-chat line when someone clicks "on my way" on the user's hangout.</summary>
    public bool NotifyRsvp { get; set; } = true;

    /// <summary>Game-chat line when a hangout the user signed up for ends early or is cancelled.</summary>
    public bool NotifyEnded { get; set; } = true;

    /// <summary>Game-chat line when one of the user's messenger friends starts a hangout.</summary>
    public bool NotifyMatchStarted { get; set; } = true;

    /// <summary>Retired: hangouts no longer mark the match list. Kept so old configs deserialize.</summary>
    public bool ShowInMatchList { get; set; } = true;

    /// <summary>Retired: the two intro popups were replaced by the three-step tour. Kept so old configs deserialize.</summary>
    public bool SeenDirectoryIntro { get; set; }

    /// <summary>Retired: the two intro popups were replaced by the three-step tour. Kept so old configs deserialize.</summary>
    public bool SeenCreateIntro { get; set; }

    /// <summary>Set once the three-step Hangouts tour has been completed, on first visit.</summary>
    public bool SeenTour { get; set; }
}

/// <summary>Client-side messenger preferences and first-run flags.</summary>
[Serializable]
public class MessengerClientState
{
    /// <summary>Game-chat line when a messenger message arrives while the chat isn't open.</summary>
    public bool NotifyMessage { get; set; } = true;

    /// <summary>Game-chat line when a contact request arrives.</summary>
    public bool NotifyRequest { get; set; } = true;

    /// <summary>Separate server-info-bar entry with the messenger unread count.</summary>
    public bool EnableDtrEntry { get; set; } = true;

    /// <summary>Set once the messenger intro popup has been shown, on first open.</summary>
    public bool SeenIntro { get; set; }
}

[Serializable]
public class Configuration : IPluginConfiguration
{
    /// <summary>Dalamud config-schema version; bump when the stored shape changes.</summary>
    public int Version { get; set; } = 0;

    /// <summary>The active colour theme.</summary>
    public AppTheme SelectedTheme { get; set; } = AppTheme.CrystalVoid;

    /// <summary>The purchased theme in use, null when a built-in is selected. <see cref="SelectedTheme"/>
    /// stays the fallback the phone draws until the seal is decrypted (or if it never can be).</summary>
    public Guid? SelectedPremiumThemeId { get; set; }

    /// <summary>AetherOS appearance and home screen layout.</summary>
    public AetherOS.Sdk.OsConfig Os { get; set; } = new();

    /// <summary>Phone-wide settings shown in the Settings app (general behaviour, language, notification
    /// infrastructure). The old flat properties below forward here, so existing config files migrate on load and
    /// every existing reader keeps working unchanged.</summary>
    public OsSettingsConfig OsSettings { get; set; } = new();

    /// <summary>The file picker's own memory: the one folder every picker on the phone shares, the
    /// starred folders, and the window and view preferences. See AetherFileDialog.</summary>
    public FilePickerConfig FilePicker { get; set; } = new();

    /// <summary>Phone size preset; scales the whole UI uniformly.</summary>
    public PhoneScalePreset PhoneSize { get; set; } = PhoneScalePreset.Small;

    /// <summary>Minimised-bubble size preset; Medium is the bubble's authored (current) size.</summary>
    public PhoneScalePreset MiniPhoneSize { get; set; } = PhoneScalePreset.Medium;

    /// <summary>When set, the user can't drag the phone; a programmatic recenter still works.</summary>
    public bool LockPhonePosition { get; set; } = false;

    /// <summary>The bubble's own lock, and null until somebody sets it: one setting used to pin both
    /// windows, so an install that had locked the phone keeps a locked bubble until it says otherwise.
    /// Read <see cref="LockMiniPosition"/> rather than this.</summary>
    public bool? LockMiniPositionSet { get; set; }

    /// <summary>When set, the user can't drag the minimised bubble. It keeps its own position, so it
    /// keeps its own lock.</summary>
    [Newtonsoft.Json.JsonIgnore]
    public bool LockMiniPosition
    {
        get => LockMiniPositionSet ?? LockPhonePosition;
        set => LockMiniPositionSet = value;
    }

    /// <summary>Chat-bubble colour overrides; null means "use the theme default".</summary>
    public Vector4? OwnChatBg { get; set; }
    public Vector4? OwnChatFg { get; set; }
    public Vector4? PeerChatBg { get; set; }
    public Vector4? PeerChatFg { get; set; }

    /// <summary>Server access + refresh tokens for this install.</summary>
    public AuthState Auth { get; set; } = new();

    /// <summary>The active profile's identity keypair, unwrapped. Swapped on profile switch (see
    /// <see cref="SwitchProfileLocalState"/>).</summary>
    public CryptoKeys Crypto { get; set; } = new();

    /// <summary>The account passphrase KEK, account-level (never swapped). Wraps every profile's private key;
    /// lets a new profile's key bundle be created, and a sibling's bundle be unwrapped, without re-entering the
    /// passphrase. Stored with the same posture as the unwrapped private key above. Empty = not captured yet
    /// (the next passphrase entry stores it).</summary>
    public byte[] AccountKek { get; set; } = [];

    /// <summary>The Argon2id inputs the stored <see cref="AccountKek"/> was derived from, so provisioning can
    /// stamp new bundles with parameters that actually reproduce it. Empty salt = unknown (KEK stored before
    /// this was recorded).</summary>
    public byte[] AccountKekSalt { get; set; } = [];
    public int AccountKekMemoryKb { get; set; }
    public int AccountKekIterations { get; set; }
    public int AccountKekParallelism { get; set; }

    /// <summary>The account-level X25519 messenger keypair, unwrapped. Account-scoped like the KEK (never
    /// swapped on profile switch); its wrapped form lives server-side as the account key bundle.</summary>
    public CryptoKeys AccountCrypto { get; set; } = new();

    /// <summary>Messenger notification toggles and first-run flags.</summary>
    public MessengerClientState Messenger { get; set; } = new();

    /// <summary>Stable per-install device id. Format: <c>AetherLove-Plugin-XXXXXX</c>.</summary>
    public string DeviceId { get; set; } = "";

    /// <summary>UI language for the plugin (e.g. "English", "French"). Forwards to <see cref="OsSettings"/>.</summary>
    public string PluginLanguage { get => OsSettings.PluginLanguage; set => OsSettings.PluginLanguage = value; }

    /// <summary>Blur NSFW photos until the viewer reveals each one.</summary>
    public bool AlwaysBlurNsfw { get; set; } = true;

    /// <summary>Per-machine WebP-decode capability; null until first probed. Null/false makes the server
    /// send JPEG.</summary>
    public bool? WebpSupported { get; set; } = null;

    /// <summary>Debug-screen override: force the server to send JPEG photos regardless of the WebP probe.</summary>
    public bool ForceJpegImages { get; set; } = false;

    /// <summary>The Market app's "view on the market board" entry in the game's item context menus;
    /// toggled from the Market menu.</summary>
    public bool MarketContextMenuEnabled { get; set; } = true;

    /// <summary>Skip the "close AetherLove?" confirmation modal. Forwards to <see cref="OsSettings"/>.</summary>
    public bool SkipCloseConfirmation { get => OsSettings.SkipCloseConfirmation; set => OsSettings.SkipCloseConfirmation = value; }

    /// <summary>Master switch over every notification; when off the options below are ignored. Forwards to <see cref="OsSettings"/>.</summary>
    public bool EnableNotifications { get => OsSettings.EnableNotifications; set => OsSettings.EnableNotifications = value; }

    /// <summary>Play a sound when a notification is shown. Forwards to <see cref="OsSettings"/>.</summary>
    public bool EnableNotificationSounds { get => OsSettings.EnableNotificationSounds; set => OsSettings.EnableNotificationSounds = value; }

    /// <summary>Which bundled sound to play for notifications. Forwards to <see cref="OsSettings"/>.</summary>
    public NotificationSound NotificationSoundChoice { get => OsSettings.NotificationSoundChoice; set => OsSettings.NotificationSoundChoice = value; }

    /// <summary>App-level gate for AetherLove's own notifications (message/match), independent of the phone-wide
    /// master switch, so AetherLove can be silenced without touching other apps.</summary>
    public bool AetherLoveNotificationsEnabled { get; set; } = true;

    /// <summary>Gate for the Daily Eorzean news app's notifications (the new-post OS notification + native chat
    /// line). The app tile's unread badge is independent of this and always reflects unread count.</summary>
    public bool NewsNotificationsEnabled { get; set; } = true;

    /// <summary>Add a game chat-log line when a message arrives.</summary>
    public bool NotifyChatOnMessage { get; set; } = true;
    /// <summary>Add a game chat-log line when a new match is made.</summary>
    public bool NotifyChatOnMatch { get; set; } = true;
    /// <summary>Show a Dalamud toast when a message arrives.</summary>
    public bool NotifyPopupOnMessage { get; set; } = true;
    /// <summary>Show a Dalamud toast when a new match is made.</summary>
    public bool NotifyPopupOnMatch { get; set; } = true;

    /// <summary>Show the AetherLove unread count in the game's server info bar (the DTR entry).</summary>
    public bool EnableDtrEntry { get; set; } = true;

    /// <summary>Suppress every notification while in combat. Forwards to <see cref="OsSettings"/>.</summary>
    public bool HideNotificationsDuringCombat { get => OsSettings.HideNotificationsDuringCombat; set => OsSettings.HideNotificationsDuringCombat = value; }

    /// <summary>Open the minimised bubble automatically on character login. Forwards to <see cref="OsSettings"/>.</summary>
    public bool AutoOpenMinimizedOnLogin { get => OsSettings.AutoOpenMinimizedOnLogin; set => OsSettings.AutoOpenMinimizedOnLogin = value; }

    /// <summary>What to do with the plugin windows on combat. Forwards to <see cref="OsSettings"/>.</summary>
    public CombatBehavior CombatBehavior { get => OsSettings.CombatBehavior; set => OsSettings.CombatBehavior = value; }

    /// <summary>Keep AetherLove drawing during group pose. Forwards to <see cref="OsSettings"/>.</summary>
    public bool ShowDuringGpose { get => OsSettings.ShowDuringGpose; set => OsSettings.ShowDuringGpose = value; }

    /// <summary>Hide AetherLove during cutscenes. Forwards to <see cref="OsSettings"/>.</summary>
    public bool HideDuringCutscenes { get => OsSettings.HideDuringCutscenes; set => OsSettings.HideDuringCutscenes = value; }

    /// <summary>Window state captured at the last unload so a mid-session reload reopens as the user left
    /// it; unused on a normal game boot.</summary>
    public WindowOpenState LastWindowState { get; set; } = WindowOpenState.Minimized;

    /// <summary>Set after the first launch so onboarding is only force-opened on a fresh install.</summary>
    public bool HasCompletedFirstLaunch { get; set; } = false;

    /// <summary>Set once the link-safety copy warning has been acknowledged.</summary>
    public bool AcknowledgedProfileCopyTextWarning { get; set; } = false;

    /// <summary>Set once the reswipe intro has been shown; showing it does not consume the daily allowance.</summary>
    public bool SeenReswipeIntro { get; set; } = false;

    /// <summary>Set once the superlike intro has been shown; showing it does not consume a superlike.</summary>
    public bool SeenSuperlikeIntro { get; set; } = false;

    /// <summary>Set once the "My RP Profiles" intro popup has been shown, on first open of that menu.</summary>
    public bool SeenRpProfilesIntro { get; set; } = false;

    /// <summary>Changelog versions ("Major.Minor.Build") whose "What's New" window has already been shown.</summary>
    public HashSet<string> ShownChangelogVersions { get; set; } = [];

    /// <summary>Peer profile ids of matches whose chat has been opened; clears the "needs a first hello"
    /// highlight. Client-side only.</summary>
    public HashSet<Guid> OpenedChats { get; set; } = [];

    /// <summary>Private per-match notes (peer profile id to text). Local-only, never sent anywhere.</summary>
    public Dictionary<Guid, string> MatchNotes { get; set; } = [];

    /// <summary>Legacy archived-matches set, kept only for migration into the "Archive" chat category;
    /// emptied by that migration.</summary>
    public List<Guid> ArchivedMatches { get; set; } = [];

    /// <summary>User-created chat categories in display order. Client-side only.</summary>
    public List<ChatCategoryConfig> ChatCategories { get; set; } = [];

    /// <summary>Which category each chat lives in (peer profile id → category id); chats absent from the
    /// map are top-level. Client-side only.</summary>
    public Dictionary<Guid, Guid> ChatCategoryMembers { get; set; } = [];

    /// <summary>Show the search row on the matches screen (toggled from its overflow menu).</summary>
    public bool ShowChatSearch { get; set; } = false;

    /// <summary>Background presence cadence state.</summary>
    public PulseState Pulse { get; set; } = new();

    /// <summary>Client-side spark milestone tracking.</summary>
    public SparkClientState Sparks { get; set; } = new();

    /// <summary>Echo runtime location and playback preferences.</summary>
    public EchoClientState Echo { get; set; } = new();

    /// <summary>Places browse preferences.</summary>
    public PlacesState Places { get; set; } = new();

    /// <summary>Hangout preferences, first-run flags, and dismissals.</summary>
    public HangoutClientState Hangouts { get; set; } = new();

    /// <summary>Levemetes browse preferences (account-level, not swapped per profile).</summary>
    public LevemetesState Levemetes { get; set; } = new();

    /// <summary>Per-install tally of emoji reaction usage; powers the "most-used" quick-react bar.</summary>
    public Dictionary<string, int> ReactionUsage { get; set; } = new();

    /// <summary>Favorited emoji shortcodes in add order; the picker shows them first. Client-side only.</summary>
    public List<string> FavoriteEmojis { get; set; } = new();

    /// <summary>Stashed per-profile state for the account's INACTIVE profiles, keyed by profile id. The flat
    /// fields above always hold the ACTIVE profile's state (which is also how a pre-multi-profile config reads
    /// correctly without migration); <see cref="SwitchProfileLocalState"/> swaps them on a profile switch.</summary>
    public Dictionary<Guid, ProfileLocalState> ProfileLocal { get; set; } = new();

    /// <summary>Swaps the flat per-profile fields from <paramref name="oldId"/> to <paramref name="newId"/>:
    /// stashes the current values under the old profile and loads (and removes) the new profile's stash, or
    /// resets to fresh state when it has none. No-op when the ids match. A null/empty <paramref name="oldId"/>
    /// is the pre-multi-profile migration case: the flat state (chats prefs, categories, the E2E keypair)
    /// already belongs to the single legacy profile, so it is adopted as-is, never replaced. Callers save
    /// afterwards.</summary>
    public void SwitchProfileLocalState(Guid? oldId, Guid newId)
    {
        if (oldId == newId || newId == Guid.Empty)
        {
            return;
        }
        if (oldId is not { } old || old == Guid.Empty)
        {
            return;
        }
        ProfileLocal[old] = new ProfileLocalState
        {
            Crypto = Crypto,
            OpenedChats = OpenedChats,
            MatchNotes = MatchNotes,
            ReactionUsage = ReactionUsage,
            FavoriteEmojis = FavoriteEmojis,
            ChatCategories = ChatCategories,
            ChatCategoryMembers = ChatCategoryMembers,
        };
        var next = ProfileLocal.TryGetValue(newId, out var stash) ? stash : new ProfileLocalState();
        ProfileLocal.Remove(newId);
        Crypto = next.Crypto;
        OpenedChats = next.OpenedChats;
        MatchNotes = next.MatchNotes;
        ReactionUsage = next.ReactionUsage;
        FavoriteEmojis = next.FavoriteEmojis;
        ChatCategories = next.ChatCategories;
        ChatCategoryMembers = next.ChatCategoryMembers;
    }

    /// <summary>Drops a deleted profile's stashed state (the active profile's flat state is the caller's to
    /// reset).</summary>
    public void RemoveProfileLocalState(Guid profileId) => ProfileLocal.Remove(profileId);

    public void Save() => UiHost.PluginInterface.SavePluginConfig(this);
}

/// <summary>One inactive profile's stashed client-side state; the same fields live flat on
/// <see cref="Configuration"/> for the active profile.</summary>
[Serializable]
public class ProfileLocalState
{
    public CryptoKeys Crypto { get; set; } = new();
    public HashSet<Guid> OpenedChats { get; set; } = [];
    public Dictionary<Guid, string> MatchNotes { get; set; } = [];
    public Dictionary<string, int> ReactionUsage { get; set; } = new();
    public List<string> FavoriteEmojis { get; set; } = new();
    public List<ChatCategoryConfig> ChatCategories { get; set; } = [];
    public Dictionary<Guid, Guid> ChatCategoryMembers { get; set; } = [];
}
