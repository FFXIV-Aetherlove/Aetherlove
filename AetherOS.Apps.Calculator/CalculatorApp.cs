using System;
using System.Collections.Generic;
using System.Numerics;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Calculator;

/// <summary>A graphing calculator in the shape of the handheld it is imitating: an LCD tape with a full
/// keypad, a Y= editor with four plotted slots, a real graph with trace and zoom, and a value table.</summary>
public sealed class CalculatorApp : IAetherApp
{
    private const float PadX = 14f;

    private enum View
    {
        Home,
        Graph,
        Table,
        Tour,
    }

    private readonly Func<string> _name;
    private readonly IAppStorage _storage;
    private readonly CalcSettings _settings;
    private readonly CalcSession _session = new();
    private readonly HomeScreen _home;
    private readonly SimpleScreen _simple;
    private readonly GraphScreen _graph;
    private readonly TableScreen _table;
    private readonly TourScreen _tour;
    private View _view = View.Home;
    private bool _tourSeen;
    private bool _tourSeenLoaded;
    private bool _graphingSeen;
    private bool _graphingSeenLoaded;

    public CalculatorApp(Func<string> name, IAppCapabilities caps)
    {
        _name = name;
        _storage = caps.Storage("calculator");
        _settings = new CalcSettings(_storage);
        _home = new HomeScreen(_session, Navigate);
        _simple = new SimpleScreen(_session);
        _graph = new GraphScreen(_session, Navigate);
        _table = new TableScreen(_session, Navigate);
        _tour = new TourScreen(FinishTour, () => _settings.Mode);
    }

    private bool Graphing => _settings.Mode == CalcMode.Graphing;

    public string Id => "calculator";

    public string Name => _name();

    public FontAwesomeIcon Icon => FontAwesomeIcon.Calculator;

    public Vector4 TileTop => new(0.259f, 0.400f, 0.427f, 1f);

    public Vector4 TileBottom => new(0.086f, 0.145f, 0.180f, 1f);

    public int Badge => 0;

    public bool HasSurface => true;

    /// <summary>Everything is computed on the device, so the calculator works signed out and offline.</summary>
    public bool RequiresConnection => false;

    public bool UsesAccount => false;

    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Strings =>
        Localization.AppStrings.Packs;

    public void Open()
    {
    }

    public void OnForeground()
    {
        switch (_view)
        {
            case View.Home:
                ShowKeypad();
                break;
            case View.Graph:
                _graph.OnShow();
                break;
            case View.Table:
                _table.OnShow();
                break;
        }
    }

    public void OnIntent(OsIntent intent)
    {
    }

    public void Draw(OsAppContext ctx)
    {
        if (_view != View.Tour && ShouldAutoRunTour())
        {
            _view = View.Tour;
            _tour.OnShow();
        }
        else if (_view != View.Tour && Graphing && ShouldExplainGraphing())
        {
            _view = View.Tour;
            _tour.OnShowGraphing();
        }

        if (_view == View.Tour)
        {
            _tour.Draw(ctx);
            return;
        }

        DrawHeader(ctx);
        using var body = ImRaii.Child("##calcBody", new Vector2(0f, 0f), false,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        if (!body)
        {
            return;
        }
        switch (_view)
        {
            case View.Graph:
                _graph.Draw(ctx);
                break;
            case View.Table:
                _table.Draw(ctx);
                break;
            default:
                if (Graphing)
                {
                    _home.Draw(ctx);
                }
                else
                {
                    _simple.Draw(ctx);
                }
                break;
        }
    }

    /// <summary>The graph and table views belong to the graphing keypad, so dropping to simple takes any of
    /// them back to the keypad rather than leaving a view its keys can no longer reach.</summary>
    private void SetMode(CalcMode mode)
    {
        _settings.Mode = mode;
        if (mode == CalcMode.Simple)
        {
            _view = View.Home;
        }
        ShowKeypad();
    }

    private void ShowKeypad()
    {
        if (Graphing)
        {
            _home.OnShow();
        }
        else
        {
            _simple.OnShow();
        }
    }

    private void Navigate(CalcNav nav)
    {
        switch (nav)
        {
            case CalcNav.Home:
                _view = View.Home;
                _home.OnShow();
                break;
            case CalcNav.Graph:
                ShowGraph();
                break;
            case CalcNav.Table:
                ShowTable();
                break;
            case CalcNav.YEditor:
                ShowGraph();
                _graph.OpenPanel(GraphPanel.YEditor);
                break;
            case CalcNav.Window:
                ShowGraph();
                _graph.OpenPanel(GraphPanel.Window);
                break;
            case CalcNav.Zoom:
                ShowGraph();
                _graph.OpenPanel(GraphPanel.Zoom);
                break;
            case CalcNav.CalcMenu:
                ShowGraph();
                _graph.OpenPanel(GraphPanel.Calc);
                break;
            case CalcNav.Trace:
                ShowGraph();
                _graph.ToggleTrace();
                break;
            case CalcNav.TblSet:
                ShowTable();
                _table.OpenSetup();
                break;
        }
    }

    private void ShowGraph()
    {
        _view = View.Graph;
        _graph.OnShow();
    }

    private void ShowTable()
    {
        _view = View.Table;
        _table.OnShow();
    }

    private void DrawHeader(OsAppContext ctx)
    {
        ImGui.Dummy(new Vector2(0f, ctx.Px(8f)));
        var originX = ImGui.GetWindowPos().X;
        var rowTop = ImGui.GetCursorScreenPos().Y;
        var title = ctx.Localize(_view switch
        {
            View.Graph => "os.calc_view_graph",
            View.Table => "os.calc_view_table",
            _ => "os.app_calculator",
        });

        float titleH;
        using (ctx.TitleFont?.Push())
        {
            titleH = ImGui.CalcTextSize(title).Y;
        }
        var rowH = MathF.Max(titleH, ctx.Px(28f));
        var centerY = rowTop + rowH * 0.5f;

        ImGui.SetCursorScreenPos(new Vector2(originX + ctx.Px(PadX), centerY - titleH * 0.5f));
        using (ctx.TitleFont?.Push())
        {
            ImGui.TextColored(DeviceUi.Teal, title);
        }

        DrawMenu(ctx, centerY);
        ImGui.SetCursorScreenPos(new Vector2(originX, rowTop + rowH));
        ImGui.Dummy(new Vector2(0f, ctx.Px(4f)));
    }

    private void DrawMenu(OsAppContext ctx, float centerY)
    {
        const string popupId = "##calcMenu";
        var menuTL = AppHeader.DrawMenuButton(ImGui.GetWindowSize().X, PadX, popupId, centerY: centerY);
        var open = AppHeader.BeginMenuPopup(menuTL, popupId);
        if (open)
        {
            // Both modes are listed rather than one "switch" row, so the menu says which one you are on.
            var simple = ctx.Localize("os.calc_mode_simple");
            var graphing = ctx.Localize("os.calc_mode_graphing");
            var tour = ctx.Localize("os.calc_menu_tour");
            var clear = ctx.Localize("os.calc_menu_clear_history");
            var reset = ctx.Localize("os.calc_menu_reset_window");
            var w = AppHeader.MenuWidth(simple, graphing, tour, clear, reset);
            var rowH = AppHeader.MenuRowHeight();

            if (AppHeader.MenuRow(Graphing ? FontAwesomeIcon.None : FontAwesomeIcon.Check, simple, w, rowH))
            {
                SetMode(CalcMode.Simple);
                ImGui.CloseCurrentPopup();
            }
            if (AppHeader.MenuRow(Graphing ? FontAwesomeIcon.Check : FontAwesomeIcon.None, graphing, w, rowH))
            {
                SetMode(CalcMode.Graphing);
                ImGui.CloseCurrentPopup();
            }
            if (AppHeader.MenuRow(FontAwesomeIcon.Calculator, tour, w, rowH))
            {
                _view = View.Tour;
                _tour.OnShow();
                ImGui.CloseCurrentPopup();
            }
            if (AppHeader.MenuRow(FontAwesomeIcon.Eraser, clear, w, rowH))
            {
                _session.ClearHistory();
                ImGui.CloseCurrentPopup();
            }
            if (Graphing && AppHeader.MenuRow(FontAwesomeIcon.Expand, reset, w, rowH))
            {
                _session.Window = GraphWindow.Standard;
                ImGui.CloseCurrentPopup();
            }
        }
        AppHeader.EndMenuPopup(open);
    }

    private bool ShouldAutoRunTour()
    {
        if (!_tourSeenLoaded)
        {
            _tourSeen = _storage.Get<bool?>("tourSeen") ?? false;
            _tourSeenLoaded = true;
        }
        return !_tourSeen;
    }

    /// <summary>Tracked apart from the introduction, because somebody who chose simple at the start has never
    /// been shown any of this and would otherwise meet the graphing keypad cold on the day they switch.</summary>
    private bool ShouldExplainGraphing()
    {
        if (!_graphingSeenLoaded)
        {
            _graphingSeen = _storage.Get<bool?>("graphingSeen") ?? false;
            _graphingSeenLoaded = true;
        }
        return !_graphingSeen;
    }

    private void FinishTour(CalcMode mode)
    {
        _tourSeen = true;
        _storage.Set("tourSeen", (bool?)true);
        if (mode == CalcMode.Graphing)
        {
            // Finishing in graphing means the graphing steps have just been read, whichever route got here.
            _graphingSeen = true;
            _graphingSeenLoaded = true;
            _storage.Set("graphingSeen", (bool?)true);
        }
        _view = View.Home;
        SetMode(mode);
    }
}
