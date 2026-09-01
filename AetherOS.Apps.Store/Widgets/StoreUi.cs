using System;
using System.Numerics;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Store;

/// <summary>The Store's own versions of the shared widgets that would otherwise paint themselves in the
/// user's phone theme. AppKit's <c>ModalUi.Button</c> and <c>DrawFloatingBackPill</c> read
/// <c>ThemeService.Current</c> inside themselves, so no amount of changing call sites decouples them; the
/// Store is the one app with a fixed identity, so it carries these rather than adding a colour parameter
/// every other app would have to pass.</summary>
internal static class StoreUi
{
    /// <summary>A full-width action button in the Store's blue.</summary>
    public static bool Button(string label, float width, float height = 34f)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, StorePalette.ButtonNormal);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, StorePalette.ButtonHovered);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, StorePalette.ButtonActive);
        ImGui.PushStyleColor(ImGuiCol.Text, StorePalette.Body);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(9f));
        var pressed = SharedUiHelpers.Button(label, new Vector2(width, Px(height)));
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(4);
        return pressed;
    }

    public const float BackPillHeight = 30f;

    /// <summary>The header's back control: one capsule carrying a chevron and the store's own glyph, so the way out
    /// is a single obvious target at the top of the screen rather than a lone glyph floating over the art.
    /// Returns its width so the caller can set the title beside it.</summary>
    public static bool BackPill(Vector2 pos, string tooltip, FontAwesomeIcon icon, out float width)
    {
        var dl = ImGui.GetWindowDrawList();
        var height = Px(BackPillHeight);
        width = Px(54f);
        ImGui.SetCursorScreenPos(pos);
        var pressed = ImGui.InvisibleButton("##storeBack", new Vector2(width, height));
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            SharedUiHelpers.HandOnHover();
            ImGui.SetTooltip(tooltip);
        }

        var br = pos + new Vector2(width, height);
        dl.AddRectFilled(pos, br, StorePalette.SurfaceWithAlpha(hovered ? 0.98f : 0.86f), height * 0.5f);
        dl.AddRect(pos, br, StorePalette.BlueWithAlpha(hovered ? 0.9f : 0.4f), height * 0.5f,
            ImDrawFlags.RoundCornersAll, Px(1.3f));

        var tint = hovered ? StorePalette.BlueLightU32 : StorePalette.BodyU32;
        IconDraw.AddCentered(dl, FontAwesomeIcon.ChevronLeft, Px(11f),
            pos + new Vector2(Px(16f), height * 0.5f), tint);
        IconDraw.AddCentered(dl, icon, Px(13f), pos + new Vector2(Px(37f), height * 0.5f), tint);
        return pressed;
    }

    /// <summary>A small pill with a label, the Store's one interactive chip shape. Filled blue when it is
    /// carrying a live choice, hollow when it is only an affordance.</summary>
    public static bool Pill(
        string id, Vector2 tl, string label, bool active, float scale = 1f, FontAwesomeIcon? icon = null)
    {
        var dl = ImGui.GetWindowDrawList();
        var fontSize = ImGui.GetFontSize() * 0.86f * scale;
        var textSz = StoreChips.MeasureAt(label, fontSize);
        var iconW = icon is null ? 0f : Px(11f) + Px(5f);
        var padX = Px(10f);
        var size = new Vector2(textSz.X + iconW + padX * 2f, Px(26f) * scale);

        ImGui.SetCursorScreenPos(tl);
        var pressed = ImGui.InvisibleButton(id, size);
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            SharedUiHelpers.HandOnHover();
        }

        var radius = size.Y * 0.5f;
        if (active)
        {
            dl.AddRectFilled(tl, tl + size, StorePalette.BlueWithAlpha(hovered ? 1f : 0.88f), radius);
        }
        else
        {
            dl.AddRectFilled(tl, tl + size, StorePalette.SurfaceWithAlpha(hovered ? 1f : 0.8f), radius);
            dl.AddRect(tl, tl + size, StorePalette.BlueWithAlpha(hovered ? 0.7f : 0.28f), radius, 0, Px(1.2f));
        }

        var textColor = active ? 0xFFFFFFFFu : StorePalette.BodyU32;
        var x = tl.X + padX;
        if (icon is { } glyph)
        {
            IconDraw.AddCentered(dl, glyph, Px(11f), new Vector2(x + Px(5.5f), tl.Y + size.Y * 0.5f), textColor);
            x += iconW;
        }
        dl.AddText(ImGui.GetFont(), fontSize, new Vector2(x, tl.Y + (size.Y - textSz.Y) * 0.5f), textColor, label);
        LastPillWidth = size.X;
        return pressed;
    }

    /// <summary>The width the last <see cref="Pill"/> occupied, so a caller can flow the next one.</summary>
    public static float LastPillWidth { get; private set; }

    /// <summary>Measures a pill without drawing it, for right-aligned layout.</summary>
    public static float MeasurePill(string label, float scale = 1f, bool hasIcon = false)
    {
        var fontSize = ImGui.GetFontSize() * 0.86f * scale;
        return StoreChips.MeasureAt(label, fontSize).X
            + (hasIcon ? Px(11f) + Px(5f) : 0f) + Px(10f) * 2f;
    }

    /// <summary>A removable filter pill: the label plus an x, the whole thing one hit target.</summary>
    public static bool RemovablePill(string id, Vector2 tl, string label)
    {
        var dl = ImGui.GetWindowDrawList();
        var fontSize = ImGui.GetFontSize() * 0.82f;
        var textSz = StoreChips.MeasureAt(label, fontSize);
        var padX = Px(9f);
        var size = new Vector2(textSz.X + padX * 2f + Px(14f), Px(24f));

        ImGui.SetCursorScreenPos(tl);
        var pressed = ImGui.InvisibleButton(id, size);
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            SharedUiHelpers.HandOnHover();
        }

        var radius = size.Y * 0.5f;
        dl.AddRectFilled(tl, tl + size, StorePalette.BlueWithAlpha(hovered ? 0.55f : 0.34f), radius);
        dl.AddText(ImGui.GetFont(), fontSize,
            new Vector2(tl.X + padX, tl.Y + (size.Y - textSz.Y) * 0.5f), StorePalette.BodyU32, label);
        IconDraw.AddCentered(dl, FontAwesomeIcon.Times, Px(9f),
            new Vector2(tl.X + size.X - padX * 0.8f, tl.Y + size.Y * 0.5f),
            hovered ? 0xFFFFFFFFu : StorePalette.HintU32);
        LastPillWidth = size.X;
        return pressed;
    }
}
