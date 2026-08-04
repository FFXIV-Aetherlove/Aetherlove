using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AetherLove.Shared.Patreon;
using AetherLove.Shared.Profile;
using AetherLove.Shared.Sparks;
using Dalamud.Interface.Textures;

namespace AetherOS.Apps.Settings;

/// <summary>The account snapshot the Settings user card renders; null when no account is signed in.</summary>
public sealed record SettingsAccount(string OsDisplayName, string? CharacterName, string? HomeWorld);

/// <summary>App-side mirror of the plugin's Patreon link-flow state, so the supporter page can drive it without a
/// plugin reference.</summary>
public enum SupporterFlowState
{
    Idle = 0,
    Starting = 1,
    AwaitingBrowser = 2,
    Completed = 3,
    Failed = 4,
}

/// <summary>Host bridge into the plugin: wallpaper operations, the OS avatar texture, the account card
/// snapshot, the file-pick machinery, the supporter/Patreon flow, the account-level staff notices, and a few
/// plugin-only navigations.</summary>
public interface ISettingsHost
{
    /// <summary>The built-in wallpaper file names bundled with the plugin.</summary>
    IReadOnlyList<string> BuiltIns { get; }

    ISharedImmediateTexture? GetBuiltInTexture(string fileName);
    ISharedImmediateTexture? GetTexture(string absPath);
    void SelectGradient();
    void SelectBuiltIn(string fileName);
    bool ApplyCustomFromFile(string sourcePath);
    void RemoveCustom();

    /// <summary>The account's OS avatar, or null before the first fetch / for an account with none set.</summary>
    ISharedImmediateTexture? OsAvatar { get; }

    /// <summary>Saves the OS profile: the display name (when changed) and optionally a new avatar, then
    /// refreshes the cached avatar and account snapshot. Throws a hub error on failure.</summary>
    Task SaveOsProfileAsync(string name, PhotoUploadDto? avatar);

    /// <summary>The signed-in account for the settings user card, or null when there is none.</summary>
    SettingsAccount? Account { get; }

    /// <summary>Whether the signed-in account has an active supporter status (drives the avatar star badge).</summary>
    bool IsSupporter { get; }

    /// <summary>The plugin's three-part version string for the About row.</summary>
    string PluginVersion { get; }

    SupporterFlowState PatreonState { get; }
    string? PatreonError { get; }
    PatreonStatusDto? PatreonStatus { get; }
    void PatreonReset();
    void PatreonStartLink();
    void PatreonReopenBrowser();
    void PatreonCancel();
    void PatreonUnlink();

    /// <summary>Opens the plugin's changelog window.</summary>
    void OpenChangelog();

    /// <summary>The account-level staff warnings (the OS moderation track), newest first; empty when there are
    /// none. Disjoint from the profile-sourced AetherLove warnings, which stay in the AetherLove app.</summary>
    IReadOnlyList<WarningDto> StaffWarnings { get; }

    /// <summary>The account-level staff messages, newest first; empty when there are none.</summary>
    IReadOnlyList<ModeratorMessageDto> StaffMessages { get; }

    /// <summary>How many staff notices are still unacknowledged, across both lists; drives the menu row badge.</summary>
    int UnseenStaffNoticeCount { get; }

    /// <summary>Clears the staff-notice OS notification, so the shade entry, the home widget and the bell count
    /// all drop together when the history page is opened.</summary>
    void DismissStaffNoticeNotification();

    /// <summary>The caller's spark wallet snapshot, for the hidden developer page.</summary>
    Task<SparkStatusDto> GetSparkStatusAsync();
}
