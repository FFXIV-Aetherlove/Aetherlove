using System;
using System.Collections.Generic;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Groove;

/// <summary>The Groove app: a remote for whatever the PC is playing, read through the platform's media
/// session system. Purely local; the phone never plays audio itself.</summary>
public sealed partial class GrooveApp : IAetherApp, IAppSettings
{
    internal static readonly Vector4 TileTopColor = new(0.56f, 0.33f, 0.88f, 1f);
    internal static readonly Vector4 TileBottomColor = new(0.24f, 0.12f, 0.46f, 1f);

    private const float PadX = 16f;

    private enum View { Player, Settings }

    private readonly Func<string> _name;
    private readonly IGrooveHost _host;
    private readonly GrooveSettings _settings;
    private readonly EntranceAnimation _entrance = new();
    private View _view = View.Player;
    private bool _sourcesOpen;

    public GrooveApp(Func<string> name, IGrooveHost host, GrooveSettings settings)
    {
        _name = name;
        _host = host;
        _settings = settings;
    }

    public string Id => "groove";
    public string Name => _name();
    public FontAwesomeIcon Icon => FontAwesomeIcon.Music;
    public Vector4 TileTop => TileTopColor;
    public Vector4 TileBottom => TileBottomColor;
    public int Badge => 0;
    public bool HasSurface => true;
    public bool RequiresConnection => false;

    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Strings => Localization.AppStrings.Packs;

    public IReadOnlyList<OsWidgetItem> WidgetItems
    {
        get
        {
            if (!_settings.ShowWidget || _host.Current is not { } session || session.Title.Length == 0)
            {
                return Array.Empty<OsWidgetItem>();
            }
            return [new OsWidgetItem(session.Title, session.Artist)];
        }
    }

    public IReadOnlyList<OsWidgetAction> WidgetActions
    {
        get
        {
            if (!_settings.ShowWidget || _host.Current is not { CanControl: true } session)
            {
                return Array.Empty<OsWidgetAction>();
            }
            return
            [
                new OsWidgetAction(FontAwesomeIcon.StepBackward, Loc.T("os.groove_tt_prev"),
                    () => _host.Previous(session.SessionId)),
                new OsWidgetAction(session.IsPlaying ? FontAwesomeIcon.Pause : FontAwesomeIcon.Play,
                    Loc.T(session.IsPlaying ? "os.groove_tt_pause" : "os.groove_tt_play"),
                    () => _host.TogglePlayPause(session.SessionId), Primary: true),
                new OsWidgetAction(FontAwesomeIcon.StepForward, Loc.T("os.groove_tt_next"),
                    () => _host.Next(session.SessionId)),
            ];
        }
    }

    public void Open()
    {
    }

    public void OnForeground()
    {
        _entrance.Arm();
    }

    public void Draw(OsAppContext ctx)
    {
        _entrance.BeginFrame();
        // Settings owns its whole surface: the back pill is its only chrome, so the app header steps aside.
        if (_view != View.Settings)
        {
            DrawHeader(ctx);
        }

        PushScrollbarStyle();
        using (var body = ImRaii.Child("##grooveBody", new Vector2(0f, 0f), false))
        {
            if (body)
            {
                if (_view == View.Settings)
                {
                    DrawSettings(ctx, null);
                }
                else
                {
                    DrawPlayer(ctx);
                }
            }
        }
        PopScrollbarStyle();

        if (_sourcesOpen)
        {
            DrawSourcesOverlay(ctx);
        }
        _entrance.EndFrame();
    }

    public void OnIntent(OsIntent intent)
    {
    }

    private void DrawHeader(OsAppContext ctx)
    {
        var t = ThemeService.Current;
        ImGui.Dummy(new Vector2(0f, Px(10f)));
        ImGui.SetCursorPosX(Px(PadX));
        var titleTop = ImGui.GetCursorScreenPos().Y;
        float titleH;
        using (ctx.TitleFont?.Push())
        {
            var title = Loc.T("os.app_groove");
            titleH = ImGui.CalcTextSize(title).Y;
            ImGui.TextColored(t.AccentLight, title);
        }
        DrawMenu(titleTop + (titleH * 0.5f));
        ImGui.Dummy(new Vector2(0f, Px(6f)));
    }

    private void DrawMenu(float centerY)
    {
        const string popupId = "##grooveMenu";
        var menuTL = AppHeader.DrawMenuButton(ImGui.GetWindowSize().X, PadX, popupId, centerY: centerY);
        var open = AppHeader.BeginMenuPopup(menuTL, popupId);
        if (open)
        {
            var sources = Loc.T("os.groove_menu_sources");
            var settings = Loc.T("os.groove_menu_settings");
            var w = AppHeader.MenuWidth(sources, settings);
            var rowH = AppHeader.MenuRowHeight();

            if (!_host.ReducedTier && AppHeader.MenuRow(FontAwesomeIcon.BroadcastTower, sources, w, rowH))
            {
                _sourcesOpen = true;
                ImGui.CloseCurrentPopup();
            }
            if (AppHeader.MenuRow(FontAwesomeIcon.Cog, settings, w, rowH))
            {
                _view = View.Settings;
                ImGui.CloseCurrentPopup();
            }
        }
        AppHeader.EndMenuPopup(open);
    }
}
