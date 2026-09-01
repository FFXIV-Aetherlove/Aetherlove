using System;
using System.Collections.Generic;
using System.Numerics;
using AetherLove.Services;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherLove.Widgets;

/// <summary>The phone's own context menu: dark glass, a hairline in the theme accent, an icon column, and
/// rows that arrive one after another after a beat of nothing. The look is the home screen's widget menu,
/// which is where it was drawn first; this is that same card and those same rows, in one place, for every
/// other surface that answers a right-click.
///
/// <para>The home screen draws its own placement (it has to be submitted before the page's swipe button, so
/// it cannot live in a popup) and calls <see cref="Panel"/> and <see cref="Row"/> directly. Everywhere else
/// the menu wants to leave the window it was opened from, so <see cref="Draw"/> hosts it in an ImGui popup
/// with the stock chrome turned off: ImGui keeps the placement, the focus, click-outside and Escape, and
/// nothing of its grey box survives.</para></summary>
public static class OsMenu
{
    /// <summary>One row. <paramref name="KeepsOpen"/> is for a row that leads somewhere inside the menu
    /// (a submenu, a way back), so picking it swaps what the caller passes rather than closing.</summary>
    public readonly record struct MenuRow(
        FontAwesomeIcon Icon, string Label, bool HasFlyout = false, bool KeepsOpen = false);

    /// <summary>How long the press is held before anything appears. A menu that snaps up the instant the
    /// button goes down reads as a mis-click; a beat of nothing reads as deliberate.</summary>
    private const float DelaySeconds = 0.10f;

    public const float RowHeight = 38f;

    private const float IconColumn = 30f;

    private const float Corner = 14f;

    /// <summary>Room around the card for its own drop shadow, which a popup window would otherwise clip.</summary>
    private const float ShadowPad = 10f;

    private static readonly Dictionary<string, double> OpenedAt = [];

    private static readonly Dictionary<string, Vector2> OpenedFrom = [];

    private static readonly Dictionary<string, (Vector2 Tl, Vector2 Br)> Bounds = [];

    /// <summary>Opens the popup <paramref name="id"/> and restarts its animation.</summary>
    /// <param name="insideWindow">Keep the card inside the window it is opened from, rather than letting
    /// ImGui hang it off the side into whatever is behind. True for a menu opened on a phone page: a menu
    /// half outside the bezel reads as belonging to the game, not the phone.</param>
    public static void Open(string id, bool insideWindow = false)
    {
        OpenedAt[id] = ImGui.GetTime();
        OpenedFrom[id] = ImGui.GetMousePos();
        if (insideWindow)
        {
            var tl = ImGui.GetWindowPos();
            Bounds[id] = (tl, tl + ImGui.GetWindowSize());
        }
        else
        {
            Bounds.Remove(id);
        }
        ImGui.OpenPopup(id);
    }

    /// <summary>Replays the opening for a menu that stayed open on a different set of rows, so a submenu
    /// arrives the way the menu did rather than swapping under the cursor.</summary>
    public static void Restart(string id) => OpenedAt[id] = ImGui.GetTime();

    /// <summary>Draws the menu for <paramref name="id"/> if it is open, and returns the index of the row
    /// that was clicked this frame, or -1. The popup closes itself on a pick, on a click outside and on
    /// Escape; a picked row's own work belongs to the caller, after the call.</summary>
    public static int Draw(string id, IReadOnlyList<MenuRow> rows)
    {
        var width = MeasureWidth(rows);
        var height = (rows.Count * Px(RowHeight)) + Px(10f);
        var pad = Px(ShadowPad);
        var full = new Vector2(width + (pad * 2f), height + (pad * 2f));
        if (Bounds.TryGetValue(id, out var bounds) && OpenedFrom.TryGetValue(id, out var from))
        {
            var margin = Px(6f);
            ImGui.SetNextWindowPos(new Vector2(
                Math.Clamp(from.X, bounds.Tl.X + margin,
                    MathF.Max(bounds.Tl.X + margin, bounds.Br.X - full.X - margin)),
                Math.Clamp(from.Y, bounds.Tl.Y + margin,
                    MathF.Max(bounds.Tl.Y + margin, bounds.Br.Y - full.Y - margin))));
        }

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(0f, 0f, 0f, 0f));
        var open = ImGui.BeginPopup(id, ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings);
        if (!open)
        {
            ImGui.PopStyleColor();
            ImGui.PopStyleVar(2);
            return -1;
        }

        ImGui.Dummy(full);

        var picked = -1;
        var elapsed = ImGui.GetTime() - (OpenedAt.TryGetValue(id, out var at) ? at : 0d);
        var reduce = AccessibilityService.ReduceMotion;
        if (reduce || elapsed >= DelaySeconds)
        {
            var t = reduce ? 1f : Math.Clamp((float)(elapsed - DelaySeconds) * 7.5f, 0f, 1f);
            var ease = 1f - MathF.Pow(1f - t, 3f);

            var dl = ImGui.GetWindowDrawList();
            var panel = ImGui.GetWindowPos() + new Vector2(pad, pad);
            // Lifts the last few pixels into place rather than appearing at rest: the movement is what
            // makes it read as opening from the press instead of being pasted over the screen.
            panel.Y += (1f - ease) * Px(10f);
            Panel(dl, panel, new Vector2(width, height), ease);

            var rowY = panel.Y + Px(5f);
            for (var i = 0; i < rows.Count; i++)
            {
                var rowEase = Math.Clamp((ease * 1.35f) - (i * 0.10f), 0f, 1f);
                if (Row(dl, rows[i], new Vector2(panel.X, rowY), width, rowEase, $"##osmenu{id}{i}")
                    && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    picked = i;
                }
                rowY += Px(RowHeight);
            }
        }

        if (picked >= 0 && !rows[picked].KeepsOpen)
        {
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndPopup();
        ImGui.PopStyleColor();
        ImGui.PopStyleVar(2);
        return picked;
    }

    /// <summary>The card the rows sit on: a soft drop shadow, the phone's own dark glass, and a hairline in
    /// the theme accent.</summary>
    public static void Panel(ImDrawListPtr dl, Vector2 tl, Vector2 size, float ease)
    {
        var br = tl + size;
        var radius = Px(Corner);
        for (var i = 4; i >= 1; i--)
        {
            var spread = Px(2f) * i;
            dl.AddRectFilled(tl - new Vector2(spread, spread - Px(1f)), br + new Vector2(spread, spread),
                OsDrawShared.Black(0.10f * ease * (1f - ((i - 1) / 4f))), radius + spread);
        }
        dl.AddRectFilled(tl, br, ImGui.ColorConvertFloat4ToU32(new Vector4(0.09f, 0.08f, 0.12f, 0.98f * ease)), radius);
        dl.AddRectFilled(tl, new Vector2(br.X, tl.Y + (size.Y * 0.5f)),
            OsDrawShared.White(0.035f * ease), radius, ImDrawFlags.RoundCornersTop);
        dl.AddRect(tl, br, ThemeService.Current.AccentWithAlpha(0.32f * ease),
            radius, ImDrawFlags.RoundCornersAll, Px(1f));
    }

    /// <summary>One row, hover highlight included. Returns whether the cursor is on it.</summary>
    public static bool Row(ImDrawListPtr dl, MenuRow row, Vector2 tl, float width, float ease, string id)
    {
        var height = Px(RowHeight);
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
        var iconC = new Vector2(tl.X + Px(IconColumn * 0.5f) + slide, tl.Y + (height * 0.5f));
        IconDraw.AddCentered(dl, row.Icon, Px(14f), iconC, OsDrawShared.White((hovered ? 0.98f : 0.72f) * ease));
        var labelSize = ImGui.GetFontSize() * 1.08f;
        dl.AddText(ImGui.GetFont(), labelSize,
            new Vector2(tl.X + Px(IconColumn) + slide, tl.Y + ((height - labelSize) * 0.5f)),
            OsDrawShared.White((hovered ? 0.98f : 0.86f) * ease), row.Label);

        if (row.HasFlyout)
        {
            IconDraw.AddCentered(dl, FontAwesomeIcon.ChevronRight, Px(9f),
                new Vector2(tl.X + width - Px(14f), tl.Y + (height * 0.5f)), OsDrawShared.White(0.55f * ease));
        }
        return hovered;
    }

    public static float MeasureWidth(IReadOnlyList<MenuRow> rows)
    {
        var text = 0f;
        foreach (var row in rows)
        {
            text = MathF.Max(text, (ImGui.CalcTextSize(row.Label).X * 1.08f) + (row.HasFlyout ? Px(20f) : 0f));
        }
        return MathF.Max(Px(150f), text + Px(IconColumn) + Px(24f));
    }
}
