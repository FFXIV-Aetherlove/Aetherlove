using System.Collections.Generic;
using System.Numerics;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Aetherling.Ui;

/// <summary>Where a press on the nav bar goes.</summary>
internal enum PetNavAction
{
    None,
    Home,
    Games,
    Wardrobe,
    Emotes,
    Stats,
    Settings,
}

/// <summary>The frosted bar at the foot of every page the creature owns, and the app's only navigation:
/// there is no menu and no way back but this. The page you are on stays lit, so the bar answers where
/// you are as well as where you can go. The tour is not an entry: it replays from the settings page.</summary>
internal static class PetNavBar
{
    public static float Height => Px(44f);

    /// <summary>Segment widths in design px, by how many entries the bar carries. The UI suite's nav
    /// coordinates are derived from these, so a change here moves its taps too.</summary>
    public const float SegmentWidthSix = 54f;
    public const float SegmentWidthFive = 60f;

    /// <summary>Bar plus the margin under it, which is what a page must keep clear at its foot.</summary>
    public static float Reserved => Height + Px(20f);

    private static readonly Dictionary<PetNavAction, (Vector2 TL, Vector2 BR)> Segments = [];

    /// <summary>Where an entry was last drawn, for the tour's ring. Null for an entry the bar did not
    /// draw, or before the bar has drawn at all.</summary>
    public static (Vector2 TL, Vector2 BR)? SegmentRect(PetNavAction action) =>
        Segments.TryGetValue(action, out var rect) ? rect : null;

    /// <summary>Draws the bar centred at the given top edge. <paramref name="emotes"/> is the same gate the
    /// performance page uses, so the entry appears the day the creature can learn.</summary>
    public static PetNavAction Draw(
        OsAppContext ctx, ImDrawListPtr dl, Vector2 centreTop, PetNavAction current, bool emotes)
    {
        var picked = PetNavAction.None;

        var entries = new (PetNavAction Action, FontAwesomeIcon Icon, string Key)[6];
        var count = 0;
        entries[count++] = (PetNavAction.Home, FontAwesomeIcon.Heart, "os.aetherling_nav_home");
        entries[count++] = (PetNavAction.Games, FontAwesomeIcon.Gamepad, "os.aetherling_mode_game");
        entries[count++] = (PetNavAction.Wardrobe, FontAwesomeIcon.HatWizard, "os.aetherling_menu_wardrobe");
        if (emotes)
        {
            entries[count++] = (PetNavAction.Emotes, FontAwesomeIcon.TheaterMasks, "os.aetherling_menu_emotes");
        }
        entries[count++] = (PetNavAction.Stats, FontAwesomeIcon.ChartArea, "os.aetherling_mode_stats");
        entries[count++] = (PetNavAction.Settings, FontAwesomeIcon.Cog, "os.aetherling_nav_settings");

        var height = Height;
        var segW = count >= 6 ? Px(SegmentWidthSix) : Px(SegmentWidthFive);
        var width = segW * count;
        var tl = new Vector2(centreTop.X - (width * 0.5f), centreTop.Y);
        var br = tl + new Vector2(width, height);
        var radius = height * 0.5f;

        dl.AddRectFilled(tl, br, Look.U32(new Vector4(1f, 1f, 1f, 0.055f)), radius);
        dl.AddRectFilled(tl, tl + new Vector2(width, height * 0.5f),
            Look.U32(new Vector4(1f, 1f, 1f, 0.045f)), radius, ImDrawFlags.RoundCornersTop);
        dl.AddRect(tl, br, Look.U32(new Vector4(1f, 1f, 1f, 0.14f)), radius, ImDrawFlags.RoundCornersAll, Px(1f));

        Segments.Clear();
        for (var i = 0; i < count; i++)
        {
            var (action, icon, key) = entries[i];
            var segTl = new Vector2(tl.X + (segW * i), tl.Y);
            Segments[action] = (segTl, segTl + new Vector2(segW, height));
            ImGui.SetCursorScreenPos(segTl);
            var pressed = ImGui.InvisibleButton($"##aetherlingNav{i}", new Vector2(segW, height));
            var hovered = ImGui.IsItemHovered();
            if (hovered)
            {
                HandOnHover();
                ImGui.SetTooltip(ctx.Localize(key));
            }
            if (pressed)
            {
                picked = action;
            }

            var here = action == current;
            if (here)
            {
                var pad = Px(4f);
                dl.AddRectFilled(segTl + new Vector2(pad, pad),
                    segTl + new Vector2(segW - pad, height - pad),
                    Look.U32(Look.Crystal with { W = 0.28f }), (height - (pad * 2f)) * 0.5f);
            }

            var alpha = here ? 1f : hovered ? 0.95f : 0.6f;
            IconDraw.AddCentered(dl, icon, Px(18f),
                segTl + new Vector2(segW * 0.5f, height * 0.5f), Look.U32(Look.CrystalPale, alpha));
        }

        return picked;
    }
}
