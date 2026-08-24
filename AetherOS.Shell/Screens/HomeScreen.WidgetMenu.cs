using System;
using System.Collections.Generic;
using System.Numerics;
using AetherLove.Os;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherLove.Screens;

/// <summary>The widget page's own context menu, drawn by hand rather than through an ImGui popup: the stock
/// popup is a grey box with a grey chevron on a phone that is neither. It fades and lifts into place after a
/// beat, its rows arrive one after another, and the flyout slides out of the parent's edge.</summary>
public sealed partial class HomeScreen
{
    /// <summary>How long the press is held before anything appears. A menu that snaps up the instant the
    /// button goes down reads as a mis-click; a beat of nothing reads as deliberate.</summary>
    private const float MenuDelaySeconds = 0.10f;

    /// <summary>Hover time before the flyout opens, and before it closes again on the way out. The close
    /// side is longer on purpose: the cursor has to cross the parent row's edge to reach the flyout.</summary>
    private const float SubOpenSeconds = 0.16f;
    private const float SubCloseSeconds = 0.35f;

    private const float MenuRowHeight = 38f;
    private const float MenuIconColumn = 30f;
    private const float MenuCorner = 14f;

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

    /// <summary>The card the rows sit on: a soft drop shadow, the phone's own dark glass, and a hairline in
    /// the theme accent.</summary>
    private static void DrawMenuPanel(ImDrawListPtr dl, Vector2 tl, Vector2 size, float ease)
    {
        var br = tl + size;
        var radius = Px(MenuCorner);
        for (var i = 4; i >= 1; i--)
        {
            var spread = Px(2f) * i;
            dl.AddRectFilled(tl - new Vector2(spread, spread - Px(1f)), br + new Vector2(spread, spread),
                OsDraw.Black(0.10f * ease * (1f - ((i - 1) / 4f))), radius + spread);
        }
        dl.AddRectFilled(tl, br, ImGui.ColorConvertFloat4ToU32(new Vector4(0.09f, 0.08f, 0.12f, 0.98f * ease)), radius);
        dl.AddRectFilled(tl, new Vector2(br.X, tl.Y + (size.Y * 0.5f)),
            OsDraw.White(0.035f * ease), radius, ImDrawFlags.RoundCornersTop);
        dl.AddRect(tl, br, ThemeService.Current.AccentWithAlpha(0.32f * ease),
            radius, ImDrawFlags.RoundCornersAll, Px(1f));
    }

    /// <summary>One row, hover highlight included. Returns whether the cursor is on it.</summary>
    private static bool DrawMenuRow(ImDrawListPtr dl, MenuRow row, Vector2 tl, float width, float ease, string id)
    {
        var height = Px(MenuRowHeight);
        ImGui.SetCursorScreenPos(tl);
        ImGui.InvisibleButton(id, new Vector2(width, height));
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            SharedUiHelpers.HandOnHover();
        }

        var inset = Px(4f);
        if (hovered && ease > 0.5f)
        {
            dl.AddRectFilled(tl + new Vector2(inset, Px(1f)), tl + new Vector2(width - inset, height - Px(1f)),
                ThemeService.Current.AccentWithAlpha(0.22f * ease), Px(9f));
        }

        // Text and icon slide in from the left a couple of pixels behind the panel, which is what gives the
        // rows their cascade.
        var slide = (1f - ease) * Px(8f);
        var iconC = new Vector2(tl.X + Px(MenuIconColumn * 0.5f) + slide, tl.Y + (height * 0.5f));
        IconDraw.AddCentered(dl, row.Icon, Px(14f), iconC, OsDraw.White((hovered ? 0.98f : 0.72f) * ease));
        var labelSize = ImGui.GetFontSize() * 1.08f;
        dl.AddText(ImGui.GetFont(), labelSize,
            new Vector2(tl.X + Px(MenuIconColumn) + slide, tl.Y + ((height - labelSize) * 0.5f)),
            OsDraw.White((hovered ? 0.98f : 0.86f) * ease), row.Label);

        if (row.HasFlyout)
        {
            IconDraw.AddCentered(dl, FontAwesomeIcon.ChevronRight, Px(9f),
                new Vector2(tl.X + width - Px(14f), tl.Y + (height * 0.5f)), OsDraw.White(0.55f * ease));
        }
        return hovered;
    }

    private static float MenuWidth(List<MenuRow> rows)
    {
        var text = 0f;
        foreach (var row in rows)
        {
            text = MathF.Max(text, (ImGui.CalcTextSize(row.Label).X * 1.08f) + (row.HasFlyout ? Px(20f) : 0f));
        }
        return MathF.Max(Px(150f), text + Px(MenuIconColumn) + Px(24f));
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
