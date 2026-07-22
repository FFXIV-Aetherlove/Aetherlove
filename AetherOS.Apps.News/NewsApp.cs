using System;
using System.Numerics;
using System.Text.Json;
using AetherLove;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.News;

/// <summary>Daily Eorzean: the gazette. The sole home for AetherLove news, a self-contained surface app.</summary>
public sealed class NewsApp : IAetherApp, IAppSettings
{
    private readonly Func<string> _name;
    private readonly INewsHost _host;
    private readonly NewsScreen _screen;

    public NewsApp(Func<string> name, INewsHost host)
    {
        _name = name;
        _host = host;
        _screen = new NewsScreen(host);
    }

    public string Id => "news";
    public string Name => _name();
    public FontAwesomeIcon Icon => FontAwesomeIcon.Newspaper;
    public Vector4 TileTop => new(0.90f, 0.83f, 0.66f, 1f);
    public Vector4 TileBottom => new(0.55f, 0.40f, 0.24f, 1f);
    public int Badge => _host.UnreadCount;
    public bool HasSurface => true;

    public bool RequiresConnection => true;

    public void Open()
    {
    }

    public void OnForeground() => _screen.OnForeground();

    public void Draw(OsAppContext ctx) => _screen.Draw(ctx);

    /// <summary>The app's single setting: a gate for its notifications. The tile unread badge is unaffected.</summary>
    public void DrawSettings(OsAppContext ctx, Action? onBack)
    {
        const float padX = 16f;

        if (onBack != null)
        {
            ImGui.Spacing();
            ImGui.Spacing();
            ImGui.SetCursorPosX(Px(padX));
            if (DrawFloatingBackPill(ImGui.GetCursorScreenPos(), ctx.Localize("settings.back_arrow"), FontAwesomeIcon.Cog))
            {
                onBack();
            }
            ImGui.Spacing();
        }

        DrawSubpageHeading(ctx.Localize("os.app_news"), padX);

        var scrollH = ImGui.GetContentRegionAvail().Y;
        using var scroll = ImRaii.Child("##newsSettings", new Vector2(0f, scrollH), false);
        if (!scroll.Success)
        {
            return;
        }

        ImGui.Spacing();
        SettingCheckbox(padX, ctx.Localize("os.news_notifications"),
            () => UiHost.Configuration.NewsNotificationsEnabled,
            v => UiHost.Configuration.NewsNotificationsEnabled = v);
        ImGui.Spacing();
    }

    public void OnIntent(OsIntent intent)
    {
        var preview = intent.Type == OsIntents.OpenPreview;
        if (intent.Type != OsIntents.OpenEntry && !preview)
        {
            return;
        }
        if (OsIntents.TryGetId(intent, out var id))
        {
            _screen.RequestOpenEntry(id, preview,
                OsIntents.TryGetReturnApp(intent, out var returnApp) ? returnApp : null);
        }
    }
}
