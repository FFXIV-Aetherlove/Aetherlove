using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AetherLove.Services;
using AetherLove.Services.Auth;
using AetherLove.Services.Hub;
using AetherLove.Services.Patreon;
using AetherLove.Windows;
using AetherOS.Apps.Settings;
using AetherLove.Shared.Patreon;
using AetherLove.Shared.Profile;
using Dalamud.Interface.Textures;

namespace AetherLove.Os;

/// <summary>The Settings app's bridge: wallpaper operations, the OS avatar, the account-card snapshot, the
/// supporter/Patreon flow, the account-level staff notices, and the plugin-only navigations it needs. Wallpaper
/// file-picking comes from the shared app capabilities.</summary>
public sealed class SettingsHostService : ISettingsHost
{
    private readonly WallpaperService _wallpapers;
    private readonly OsAvatarCache _osAvatar;
    private readonly SessionBootstrapper _bootstrap;
    private readonly PatreonLinkFlow _patreon;
    private readonly ChangelogWindow _changelogWindow;
    private readonly AetherHubContext _hub;
    private readonly AetherOS.Sdk.IOsShell _shell;
    private readonly Services.Store.PremiumThemeService _premiumThemes;
    private readonly IOsTogether _together;
    private readonly ServerBarService _serverBar;

    public SettingsHostService(WallpaperService wallpapers, OsAvatarCache osAvatar, SessionBootstrapper bootstrap,
        PatreonLinkFlow patreon, ChangelogWindow changelogWindow, AetherHubContext hub, AetherOS.Sdk.IOsShell shell,
        Services.Store.PremiumThemeService premiumThemes, IOsTogether together, ServerBarService serverBar)
    {
        _premiumThemes = premiumThemes;
        _together = together;
        _serverBar = serverBar;
        _wallpapers = wallpapers;
        _osAvatar = osAvatar;
        _bootstrap = bootstrap;
        _patreon = patreon;
        _changelogWindow = changelogWindow;
        _hub = hub;
        _shell = shell;
    }

    public IReadOnlyList<string> BuiltIns => _wallpapers.BuiltIns;

    public ISharedImmediateTexture? GetBuiltInTexture(string fileName) => _wallpapers.GetBuiltInTexture(fileName);

    public ISharedImmediateTexture? GetTexture(string absPath) => _wallpapers.GetTexture(absPath);

    public void SelectGradient() => _wallpapers.SelectGradient();

    public void SelectBuiltIn(string fileName) => _wallpapers.SelectBuiltIn(fileName);

    public bool ApplyCustomFromFile(string sourcePath) => _wallpapers.ApplyCustomFromFile(sourcePath);

    public void RemoveCustom() => _wallpapers.RemoveCustom();

    public ISharedImmediateTexture? OsAvatar => _osAvatar.Texture;

    public string? EquippedFrameRef => _bootstrap.LastAccount?.EquippedFrameRef;

    public Task<AetherLove.Shared.Store.AvatarRingDto[]> GetOwnedRingsAsync() => _hub.GetMyAvatarRingsAsync();

    public async Task SetAvatarRingAsync(string? frameRef)
    {
        await _hub.SetAvatarRingAsync(AetherLove.Shared.Store.AvatarRingSurface.Os, frameRef).ConfigureAwait(false);
        await _bootstrap.RefreshAccountInfoAsync().ConfigureAwait(false);
    }

    public async Task<AetherLove.Shared.Store.OwnedThemeDto[]?> GetOwnedThemesAsync()
    {
        try
        {
            return await _hub.GetMyStoreThemesAsync().ConfigureAwait(false);
        }
        catch (System.Exception ex)
        {
            Plugin.Log.Debug(ex, "[Settings] Owned themes fetch failed.");
            return null;
        }
    }

    public System.Guid? ActivePremiumThemeId => ThemeService.PremiumThemeId;

    public Task<bool> EnablePremiumThemeAsync(System.Guid productId) => _premiumThemes.EnableAsync(productId);

    public Task<bool> RefreshPremiumThemeAsync(System.Guid productId) => _premiumThemes.RefreshAsync(productId);

    public async Task<bool> SelectPremiumWallpaperAsync(System.Guid productId)
    {
        if (_premiumThemes.BackgroundWrap(productId) is null
            && !await _premiumThemes.DownloadAndSealAsync(productId).ConfigureAwait(false))
        {
            return false;
        }
        _wallpapers.SelectPremium(productId);
        return true;
    }

    public Dalamud.Interface.Textures.TextureWraps.IDalamudTextureWrap? PremiumWallpaper(System.Guid productId) =>
        _premiumThemes.BackgroundWrap(productId);

    public async Task SaveOsProfileAsync(string name, PhotoUploadDto? avatar)
    {
        if (avatar != null)
        {
            var stored = await _hub.SetOsAvatarAsync(avatar).ConfigureAwait(false);
            _osAvatar.SetFromBytes(stored);
        }
        var trimmed = name.Trim();
        if (trimmed.Length > 0 && trimmed != _bootstrap.LastAccount?.OsDisplayName)
        {
            await _hub.CompleteOsOnboardingAsync(new OsOnboardingCompleteDto(trimmed)).ConfigureAwait(false);
        }
        await _bootstrap.RefreshAccountInfoAsync().ConfigureAwait(false);
    }

    public SettingsAccount? Account => _bootstrap.LastAccount is { } account
        ? new SettingsAccount(account.OsDisplayName, account.CharacterName, account.HomeWorld)
        : null;

    public bool IsSupporter => _bootstrap.LastConnection is { IsSupporter: true };

    public string PluginVersion => typeof(Plugin).Assembly.GetName().Version?.ToString(3) ?? "?";

    public SupporterFlowState PatreonState => (SupporterFlowState)(int)_patreon.State;

    public string? PatreonError => _patreon.ErrorMessage;

    public PatreonStatusDto? PatreonStatus => _patreon.Status;

    public void PatreonReset() => _patreon.Reset();

    public void PatreonStartLink() => _patreon.StartLink();

    public void PatreonReopenBrowser() => _patreon.ReopenBrowser();

    public void PatreonCancel() => _patreon.Cancel();

    public void PatreonUnlink() => _ = _patreon.UnlinkAsync();

    public void OpenChangelog() => _changelogWindow.IsOpen = true;

    public IReadOnlyList<WarningDto> StaffWarnings => _bootstrap.LastAccount?.StaffWarnings ?? [];

    public IReadOnlyList<ModeratorMessageDto> StaffMessages => _bootstrap.LastAccount?.StaffMessages ?? [];

    public int UnseenStaffNoticeCount
    {
        get
        {
            var account = _bootstrap.LastAccount;
            var count = 0;
            foreach (var warning in account?.StaffWarnings ?? [])
            {
                if (!warning.Seen)
                {
                    count++;
                }
            }
            foreach (var message in account?.StaffMessages ?? [])
            {
                if (!message.Seen)
                {
                    count++;
                }
            }
            return count;
        }
    }

    public void DismissStaffNoticeNotification() =>
        _shell.DismissByTag(Screens.StaffNoticeScreen.NotificationTag);

    public Task<AetherLove.Shared.Sparks.SparkStatusDto> GetSparkStatusAsync() => _hub.GetSparkStatusAsync();

    // Not gated on the hub: the receiving switch and the size are local preferences, and a settings row
    // that vanishes while offline reads as a missing feature.
    public bool PartyAvailable => true;

    public bool ShowPartyPets
    {
        get => _together.ShowPartyPets;
        set => _together.ShowPartyPets = value;
    }

    public bool ShareMyPet
    {
        get => _together.ShareMyPet;
        set => _together.ShareMyPet = value;
    }

    public bool HasPet => _together.HasPet;

    public int PartyPetSize
    {
        get => _together.PartyPetSize;
        set => _together.PartyPetSize = value;
    }

    public int PartyPetSizeCount => _together.PartyPetSizeCount;
    public IReadOnlyList<SettingsServerBarEntry> ServerBarEntries =>
        [.. _serverBar.Entries.Select(e => new SettingsServerBarEntry(e.AppId, e.EntryId, e.LabelKey))];

    public bool GetServerBarEnabled(string appId, string? entryId) => entryId is null
        ? _serverBar.IsAppEnabled(appId)
        : _serverBar.IsEntryEnabled(appId, entryId);

    public void SetServerBarEnabled(string appId, string? entryId, bool on)
    {
        if (entryId is null)
        {
            _serverBar.SetAppEnabled(appId, on);
        }
        else
        {
            _serverBar.SetEntryEnabled(appId, entryId, on);
        }
    }
}
