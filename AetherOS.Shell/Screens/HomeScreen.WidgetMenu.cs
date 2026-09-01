using System;
using System.Collections.Generic;
using System.Numerics;
using AetherLove.Os;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using AetherLove.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherLove.Screens;

/// <summary>The widget page's own context menu. The card and the rows are the shared <see cref="OsMenu"/>
/// (this page is where that look was drawn first); the placement is this page's own, because the menu has to
/// be submitted before the page-wide swipe button to keep its rows clickable, which a popup cannot do. It
/// fades and lifts into place after a beat, its rows arrive one after another, and the flyout slides out of
/// the parent's edge.</summary>
public sealed partial class HomeScreen
{
    /// <summary>How long the press is held before anything appears. A menu that snaps up the instant the
    /// button goes down reads as a mis-click; a beat of nothing reads as deliberate.</summary>
    private const float MenuDelaySeconds = 0.10f;

    /// <summary>Hover time before the flyout opens, and before it closes again on the way out. The close
    /// side is longer on purpose: the cursor has to cross the parent row's edge to reach the flyout.</summary>
    private const float SubOpenSeconds = 0.16f;
    private const float SubCloseSeconds = 0.35f;

    private static float MenuRowHeight => OsMenu.RowHeight;

    private readonly List<MenuRow> _menuRows = [];
    private readonly List<MenuRow> _menuSubRows = [];

    private string? _menuWidget;
    private bool _menuOpen;
    private Vector2 _menuAt;
    private float _menuDelay;
    private float _menuT;
    private float _menuSubT;
    private float _menuSubHover;
    private bool _menuSubOpen;

    private readonly record struct MenuRow(FontAwesomeIcon Icon, string Label, Action? Invoke, bool HasFlyout);

    /// <summary>Opens the menu for a widget, or for the page itself when <paramref name="widgetId"/> is null.
    /// Nothing is drawn until the delay has run, so the press has a moment to be a press.</summary>
    private void OpenWidgetMenu(string? widgetId, Vector2 at)
    {
        _menuWidget = widgetId;
        _menuOpen = true;
        _menuAt = at;
        _menuDelay = AccessibilityService.ReduceMotion ? 0f : MenuDelaySeconds;
        _menuT = AccessibilityService.ReduceMotion ? 1f : 0f;
        _menuSubT = 0f;
        _menuSubHover = 0f;
        _menuSubOpen = false;
    }

    private void CloseWidgetMenu()
    {
        _menuOpen = false;
        _menuWidget = null;
        _menuT = 0f;
        _menuSubT = 0f;
        _menuSubOpen = false;
    }

    /// <summary>Draws the menu over everything else on the page. Called at the end of the widget page and
    /// therefore submitted before the page-wide swipe button, which is what keeps its rows clickable.</summary>
    private void DrawWidgetMenu(Vector2 origin, Vector2 avail)
    {
        if (!_menuOpen)
        {
            return;
        }

        var dt = ImGui.GetIO().DeltaTime;
        if (_menuDelay > 0f)
        {
            _menuDelay -= dt;
            return;
        }

        BuildMenuRows();
        if (_menuRows.Count == 0)
        {
            CloseWidgetMenu();
            return;
        }

        _menuT = AccessibilityService.ReduceMotion ? 1f : MathF.Min(1f, _menuT + (dt * 7.5f));
        var ease = 1f - MathF.Pow(1f - _menuT, 3f);

        var dl = ImGui.GetWindowDrawList();
        var width = MenuWidth(_menuRows);
        var height = (_menuRows.Count * Px(MenuRowHeight)) + Px(10f);
        var panel = ClampToPage(_menuAt, new Vector2(width, height), origin, avail);
        // Lifts the last few pixels into place rather than appearing at rest: the movement is what makes it
        // read as opening from the press instead of being pasted over the page.
        panel.Y += (1f - ease) * Px(10f);

        DrawMenuPanel(dl, panel, new Vector2(width, height), ease);

        var rowY = panel.Y + Px(5f);
        var flyoutAnchor = Vector2.Zero;
        var overFlyoutRow = false;
        for (var i = 0; i < _menuRows.Count; i++)
        {
            var row = _menuRows[i];
            // Each row arrives a beat after the one above it. Cheap, and it is the whole difference between
            // a panel appearing and a menu opening.
            var rowEase = Math.Clamp((ease * 1.35f) - (i * 0.10f), 0f, 1f);
            var hovered = DrawMenuRow(dl, row, new Vector2(panel.X, rowY), width, rowEase, $"##wmenu{i}");
            if (row.HasFlyout)
            {
                flyoutAnchor = new Vector2(panel.X + width, rowY);
                overFlyoutRow = hovered;
            }
            else if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                row.Invoke?.Invoke();
                CloseWidgetMenu();
                return;
            }
            rowY += Px(MenuRowHeight);
        }

        // Drawn unconditionally while open, hover or not: gating the draw on the cursor having LEFT the
        // parent row is how the flyout only ever appeared after the mouse moved away.
        var overFlyout = _menuSubOpen && DrawFlyout(dl, flyoutAnchor, origin, avail, ease);
        if (!_menuSubOpen)
        {
            _menuSubHover = overFlyoutRow ? _menuSubHover + dt : 0f;
            if (_menuSubHover >= SubOpenSeconds)
            {
                _menuSubOpen = true;
                _menuSubT = 0f;
            }
        }
        else if (overFlyoutRow || overFlyout)
        {
            _menuSubHover = SubOpenSeconds;
        }
        else
        {
            _menuSubHover -= dt * (SubOpenSeconds / SubCloseSeconds);
            if (_menuSubHover <= 0f)
            {
                _menuSubOpen = false;
                _menuSubHover = 0f;
            }
        }

        // Anywhere else, or Escape: the scrim is the rest of the page, hit-tested by hand so the page keeps
        // its own drag and scroll while the menu is up.
        var mouse = ImGui.GetMousePos();
        var insidePanel = mouse.X >= panel.X && mouse.X <= panel.X + width
            && mouse.Y >= panel.Y && mouse.Y <= panel.Y + height;
        if ((!insidePanel && !overFlyout && (ImGui.IsMouseClicked(ImGuiMouseButton.Left)
                || ImGui.IsMouseClicked(ImGuiMouseButton.Right)))
            || ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            CloseWidgetMenu();
        }
    }

    /// <summary>The flyout of widgets that can be put back. Slides out of the parent row's right edge, or
    /// its left when the phone has no room on that side. Returns whether the cursor is inside it.</summary>
    private bool DrawFlyout(ImDrawListPtr dl, Vector2 anchor, Vector2 origin, Vector2 avail, float parentEase)
    {
        if (_menuSubRows.Count == 0)
        {
            return false;
        }

        _menuSubT = AccessibilityService.ReduceMotion ? 1f : MathF.Min(1f, _menuSubT + (ImGui.GetIO().DeltaTime * 9f));
        var ease = 1f - MathF.Pow(1f - _menuSubT, 3f);

        var width = MenuWidth(_menuSubRows);
        var height = (_menuSubRows.Count * Px(MenuRowHeight)) + Px(10f);
        var flipped = anchor.X + width > origin.X + avail.X - Px(6f);
        var restX = flipped ? anchor.X - MenuWidth(_menuRows) - width + Px(6f) : anchor.X - Px(6f);
        var panel = ClampToPage(new Vector2(restX, anchor.Y - Px(5f)), new Vector2(width, height), origin, avail);
        panel.X += (1f - ease) * Px(flipped ? 10f : -10f);

        DrawMenuPanel(dl, panel, new Vector2(width, height), ease * parentEase);

        var rowY = panel.Y + Px(5f);
        var inside = false;
        for (var i = 0; i < _menuSubRows.Count; i++)
        {
            var row = _menuSubRows[i];
            var rowEase = Math.Clamp((ease * 1.35f) - (i * 0.08f), 0f, 1f);
            var hovered = DrawMenuRow(dl, row, new Vector2(panel.X, rowY), width, rowEase, $"##wsub{i}");
            inside |= hovered;
            if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                row.Invoke?.Invoke();
                CloseWidgetMenu();
                return false;
            }
            rowY += Px(MenuRowHeight);
        }

        var mouse = ImGui.GetMousePos();
        return inside || (mouse.X >= panel.X && mouse.X <= panel.X + width
            && mouse.Y >= panel.Y && mouse.Y <= panel.Y + height);
    }

    private static void DrawMenuPanel(ImDrawListPtr dl, Vector2 tl, Vector2 size, float ease) =>
        OsMenu.Panel(dl, tl, size, ease);

    private static bool DrawMenuRow(ImDrawListPtr dl, MenuRow row, Vector2 tl, float width, float ease, string id) =>
        OsMenu.Row(dl, new OsMenu.MenuRow(row.Icon, row.Label, row.HasFlyout), tl, width, ease, id);

    private static float MenuWidth(List<MenuRow> rows)
    {
        var shared = new OsMenu.MenuRow[rows.Count];
        for (var i = 0; i < rows.Count; i++)
        {
            shared[i] = new OsMenu.MenuRow(rows[i].Icon, rows[i].Label, rows[i].HasFlyout);
        }
        return OsMenu.MeasureWidth(shared);
    }

    /// <summary>Keeps the panel inside the phone: a menu opened near an edge grows the other way rather than
    /// hanging off the bezel.</summary>
    private static Vector2 ClampToPage(Vector2 wanted, Vector2 size, Vector2 origin, Vector2 avail)
    {
        var margin = Px(6f);
        return new Vector2(
            Math.Clamp(wanted.X, origin.X + margin, MathF.Max(origin.X + margin, origin.X + avail.X - size.X - margin)),
            Math.Clamp(wanted.Y, origin.Y + margin, MathF.Max(origin.Y + margin, origin.Y + avail.Y - size.Y - margin)));
    }

    private void BuildMenuRows()
    {
        _menuRows.Clear();
        _menuSubRows.Clear();

        foreach (var (id, name, icon) in HiddenWidgetChoices())
        {
            var widgetId = id;
            _menuSubRows.Add(new MenuRow(icon, name, () => ShowWidget(widgetId), false));
        }
        if (_menuSubRows.Count > 0)
        {
            _menuRows.Add(new MenuRow(FontAwesomeIcon.Plus, Loc.T("os.widget_add_new"), null, true));
        }

        if (_menuWidget is not { } widget)
        {
            return;
        }
        _menuRows.Add(new MenuRow(FontAwesomeIcon.TrashAlt, Loc.T("os.widget_remove"), () => HideWidget(widget), false));
        if (WidgetSettings(widget) is { } settings)
        {
            _menuRows.Add(new MenuRow(FontAwesomeIcon.Cog, Loc.T("os.widget_settings"), settings, false));
        }
    }
}
