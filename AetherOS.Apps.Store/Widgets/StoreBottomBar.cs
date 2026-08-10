using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AetherLove.Services.Localization;
using AetherLove.Shared.Store;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Store;

/// <summary>The Store's docked bar: Home plus the root categories. It is the app's whole navigation, which
/// is why the category chips are gone from the browse header and the category menu is gone from the home
/// page, and it is also the only way out of Browse. Height is reserved by shrinking the content child, never
/// by overlaying it, so nothing can hide underneath.</summary>
internal static class StoreBottomBar
{
    public const float Height = 58f;

    /// <summary>The most root categories that fit beside Home before the labels stop being readable.</summary>
    private const int MaxCategories = 5;

    /// <summary>Roots the bar never offers, matched the way the deep link matches them: on the English name,
    /// which is the only category handle the catalog DTO carries. Their products stay reachable by search and
    /// by a deep link, so this hides the door rather than the room.</summary>
    private static readonly string[] HiddenRoots = ["Aetherling", "Boosts"];

    /// <summary>What the user picked: null id means Home, otherwise a root category.</summary>
    internal readonly record struct Pick(bool Home, Guid CategoryId);

    /// <summary>Draws the bar over the app window. <paramref name="activeCategory"/> is the root the browse
    /// screen is showing, or null when the user is on Home.</summary>
    public static Pick? Draw(
        OsAppContext ctx, StoreFrontDto? front, bool onHome, Guid? activeCategory)
    {
        var dl = ImGui.GetWindowDrawList();
        var winPos = ImGui.GetWindowPos();
        var winSize = ImGui.GetWindowSize();
        var barH = Px(Height);
        var barTl = winPos + new Vector2(0f, winSize.Y - barH);

        dl.AddRectFilled(barTl, winPos + winSize, ImGui.ColorConvertFloat4ToU32(StorePalette.Surface));
        dl.AddLine(barTl, barTl + new Vector2(winSize.X, 0f), StorePalette.BlueWithAlpha(0.22f), Px(1f));

        var roots = front is null
            ? []
            : front.Categories
                .Where(c => c.ParentId is null && !IsHidden(c))
                .OrderBy(c => c.SortOrder)
                .Take(MaxCategories)
                .ToList();
        var slots = roots.Count + 1;
        var slotW = winSize.X / slots;

        Pick? picked = null;
        if (DrawSlot(ctx, dl, barTl, slotW, 0, barH, FontAwesomeIcon.Store, Loc.T("os.store_nav_home"), onHome))
        {
            picked = new Pick(true, Guid.Empty);
        }
        for (var i = 0; i < roots.Count; i++)
        {
            var category = roots[i];
            var active = !onHome && activeCategory == category.Id;
            if (DrawSlot(ctx, dl, barTl, slotW, i + 1, barH,
                Glyph(category.Icon, i), StoreLoc.Name(category), active, i))
            {
                picked = new Pick(false, category.Id);
            }
        }
        return picked;
    }

    private static bool IsHidden(StoreCategoryDto category) =>
        HiddenRoots.Contains(category.NameEnglish, StringComparer.OrdinalIgnoreCase);

    private static bool DrawSlot(
        OsAppContext ctx, ImDrawListPtr dl, Vector2 barTl, float slotW, int index, float barH,
        FontAwesomeIcon icon, string label, bool active, int swatchIndex = -1)
    {
        var tl = barTl + new Vector2(slotW * index, 0f);
        var size = new Vector2(slotW, barH);
        ImGui.SetCursorScreenPos(tl);
        var pressed = ImGui.InvisibleButton($"##storeNav{index}", size);
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            SharedUiHelpers.HandOnHover();
        }

        var center = tl + new Vector2(slotW * 0.5f, barH * 0.5f);
        var accent = swatchIndex < 0 ? StorePalette.BlueLight : StorePalette.SwatchAccent(swatchIndex);
        if (active)
        {
            // A soft plate behind the live slot, plus the rule that ties it to the page above.
            var plateHalf = new Vector2(slotW * 0.5f - Px(4f), barH * 0.5f - Px(5f));
            dl.AddRectFilled(center - plateHalf, center + plateHalf,
                ImGui.ColorConvertFloat4ToU32(accent with { W = 0.16f }), Px(10f));
            dl.AddRectFilled(new Vector2(tl.X + Px(10f), tl.Y),
                new Vector2(tl.X + slotW - Px(10f), tl.Y + Px(2f)),
                ImGui.ColorConvertFloat4ToU32(accent), Px(1f));
        }

        var glyphColor = active
            ? ImGui.ColorConvertFloat4ToU32(accent)
            : StorePalette.SurfaceWithAlpha(0f) | (hovered ? 0xCCFFFFFFu : 0x8FFFFFFFu);
        IconDraw.AddCentered(dl, icon, Px(17f), center - new Vector2(0f, Px(8f)), glyphColor);

        // A full bar squeezes six slots into the phone's width, so the label shrinks before it ellipsizes.
        var fontSize = ImGui.GetFontSize() * (slotW < Px(66f) ? 0.6f : 0.68f);
        var textSz = StoreChips.MeasureAt(label, fontSize);
        var maxW = slotW - Px(6f);
        var shown = label;
        while (textSz.X > maxW && shown.Length > 2)
        {
            shown = shown[..^2] + "…";
            textSz = StoreChips.MeasureAt(shown, fontSize);
        }
        dl.AddText(ImGui.GetFont(), fontSize,
            new Vector2(center.X - textSz.X * 0.5f, center.Y + Px(4f)),
            active ? ImGui.ColorConvertFloat4ToU32(accent) : StorePalette.HintU32, shown);
        _ = ctx;
        return pressed;
    }

    /// <summary>The category's chosen glyph, falling back to one picked by slot position for a category
    /// authored before icons existed or carrying a key this build does not know.</summary>
    internal static FontAwesomeIcon Glyph(string? icon, int index) => icon switch
    {
        "phone" => FontAwesomeIcon.MobileAlt,
        "ring" => FontAwesomeIcon.CircleNotch,
        "gift" => FontAwesomeIcon.Gifts,
        "palette" => FontAwesomeIcon.Palette,
        "wallpaper" => FontAwesomeIcon.Image,
        "sparkle" => FontAwesomeIcon.Magic,
        "star" => FontAwesomeIcon.Star,
        "crown" => FontAwesomeIcon.Crown,
        "shirt" => FontAwesomeIcon.Tshirt,
        "mask" => FontAwesomeIcon.Mask,
        "wand" => FontAwesomeIcon.Magic,
        "gem" => FontAwesomeIcon.Gem,
        "music" => FontAwesomeIcon.Music,
        "gamepad" => FontAwesomeIcon.Gamepad,
        "tag" => FontAwesomeIcon.Tag,
        "box" => FontAwesomeIcon.BoxOpen,
        "heart" => FontAwesomeIcon.Heart,
        "bolt" => FontAwesomeIcon.Bolt,
        "rocket" => FontAwesomeIcon.Rocket,
        "bug" => FontAwesomeIcon.Bug,
        _ => index switch
        {
            0 => FontAwesomeIcon.MobileAlt,
            1 => FontAwesomeIcon.CircleNotch,
            2 => FontAwesomeIcon.Gifts,
            3 => FontAwesomeIcon.Bolt,
            _ => FontAwesomeIcon.ThLarge,
        },
    };
}
