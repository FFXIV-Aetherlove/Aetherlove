using System.Collections.Generic;
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
/// supporter/Patreon flow, and the plugin-only navigations it needs. Wallpaper file-picking comes from the
/// shared app capabilities.</summary>
public sealed class SettingsHostService : ISettingsHost
{
    private readonly WallpaperService _wallpapers;
    private readonly OsAvatarCache _osAvatar;
    private readonly SessionBootstrapper _bootstrap;
    private readonly PatreonLinkFlow _patreon;
    private readonly ChangelogWindow _changelogWindow;
    private readonly AetherHubContext _hub;

    public SettingsHostService(WallpaperService wallpapers, OsAvatarCache osAvatar, SessionBootstrapper bootstrap,
        PatreonLinkFlow patreon, ChangelogWindow changelogWindow, AetherHubContext hub)
    {
        _wallpapers = wallpapers;
        _osAvatar = osAvatar;
        _bootstrap = bootstrap;
        _patreon = patreon;
        _changelogWindow = changelogWindow;
        _hub = hub;
    }

    public IReadOnlyList<string> BuiltIns => _wallpapers.BuiltIns;

    public ISharedImmediateTexture? GetBuiltInTexture(string fileName) => _wallpapers.GetBuiltInTexture(fileName);

    public ISharedImmediateTexture? GetTexture(string absPath) => _wallpapers.GetTexture(absPath);

    public void SelectGradient() => _wallpapers.SelectGradient();

    public void SelectBuiltIn(string fileName) => _wallpapers.SelectBuiltIn(fileName);

    public bool ApplyCustomFromFile(string sourcePath) => _wallpapers.ApplyCustomFromFile(sourcePath);

    public void RemoveCustom() => _wallpapers.RemoveCustom();

    public ISharedImmediateTexture? OsAvatar => _osAvatar.Texture;

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
}
