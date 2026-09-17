using AetherLove.Services.Assets;
using AetherLove.Shared.Assets;
using AetherLove.Windows;

namespace AetherLove.Os;

/// <summary>What the phone does when a pack lands: reload the emoji table, rescan wallpapers, move a
/// running Echo player onto its new build. Asset syncs never post a notification.</summary>
public sealed class AssetUpdateCoordinator
{
    private readonly AssetSyncService _assets;
    private readonly WallpaperService _wallpapers;
    private readonly EchoWindow _echo;

    public AssetUpdateCoordinator(AssetSyncService assets, WallpaperService wallpapers, EchoWindow echo)
    {
        _assets = assets;
        _wallpapers = wallpapers;
        _echo = echo;
    }

    public void Start()
    {
        _assets.PackReady += OnPackReady;
    }

    private void OnPackReady(string pack)
    {
        switch (pack)
        {
            case AssetPacks.Emoji:
                Plugin.EmojiService.Reload();
                break;
            case AssetPacks.Wallpapers:
                _wallpapers.InvalidateBuiltIns();
                break;
            case AssetPacks.EchoHost:
                _echo.RestartHostAfterUpdate();
                break;
        }
    }
}
