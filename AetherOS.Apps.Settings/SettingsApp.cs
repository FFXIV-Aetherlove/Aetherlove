using System;
using System.Collections.Generic;
using System.Numerics;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Settings;

/// <summary>The phone settings app: a fully self-contained surface app whose UI lives in
/// <see cref="SettingsScreen"/>. It still exposes its own settings body through <see cref="IAppSettings"/>.</summary>
public sealed class SettingsApp : IAetherApp, IAppSettings
{
    private readonly Func<string> _name;
    private readonly ISettingsHost _host;
    private readonly SettingsScreen _screen;

    public SettingsApp(Func<string> name, ISettingsHost host, IAppCapabilities caps)
    {
        _name = name;
        _host = host;
        _screen = new SettingsScreen(host, caps);
    }

    public string Id => "settings";
    public string Name => _name();
    public FontAwesomeIcon Icon => FontAwesomeIcon.Cog;
    public Vector4 TileTop => new(0.56f, 0.60f, 0.68f, 1f);
    public Vector4 TileBottom => new(0.27f, 0.29f, 0.35f, 1f);
    public int Badge => 0;
    public bool HasSurface => true;

    /// <summary>A shared photo becomes the phone wallpaper.</summary>
    public IReadOnlyList<string> AcceptedShareTypes { get; } = [ShareTypes.Photo];

    public string ShareTargetLabel(string shareType) =>
        shareType == ShareTypes.Photo ? AetherLove.Services.Localization.Loc.T("os.share_wallpaper") : Name;

    public void Open()
    {
    }

    public void OnForeground() => _screen.OnShow();

    public void OnBackground() => AetherLove.Services.NotificationSoundPlayer.Stop();

    public void Draw(OsAppContext ctx) => _screen.Draw(ctx);

    public void DrawSettings(OsAppContext ctx, Action? onBack) => _screen.DrawSettingsSurface(ctx, onBack);

    public void OnIntent(OsIntent intent)
    {
        if (intent.Type == OsIntents.OpenSupporter)
        {
            _screen.RequestSupporterView(OsIntents.TryGetReturnApp(intent, out var returnApp) ? returnApp : null);
        }
        if (intent.Type == OsIntents.OpenWallpaper)
        {
            _screen.RequestWallpaperView();
        }
        if (intent.Type == ShareIntent.Type && ShareIntent.TryUnwrap(intent, out var shared)
            && shared.Type == ShareTypes.Photo && shared.LocalPath is { Length: > 0 } path
            && _host.ApplyCustomFromFile(path))
        {
            _screen.RequestWallpaperView();
        }
    }
}
