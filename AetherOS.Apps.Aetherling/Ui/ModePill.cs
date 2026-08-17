using System;
using System.Numerics;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Aetherling.Ui;

internal enum PetMode
{
    Petting,
    Feeding,
}

/// <summary>What a press on the pill asked for that is not a mode.</summary>
internal enum PetPillAction
{
    None,
    Games,
    Wardrobe,
    Stats,
}

/// <summary>The frosted pill at the foot of the pet page. The left half picks what a touch on the
/// stage means; the right half is the doors (playtime, the wardrobe and the statistics page),
/// separated by a hairline because they behave differently: a mode stays lit, a door does not.
/// Game is a door rather than a mode because it navigates away, so it must never stay lit.</summary>
internal static class ModePill
{
    public static float Height => Px(44f);

    /// <summary>Draws the pill centred at the given top edge and returns the (possibly changed) mode.
    /// <paramref name="adult"/> false leaves out the two doors a growing pet has no use for: it cannot
    /// wear anything yet, and playtime is an adult's. They are omitted rather than dimmed, because a
    /// disabled door on a page with no explanation only asks a question it does not answer.</summary>
    public static PetMode Draw(
        OsAppContext ctx,
        ImDrawListPtr dl,
        Vector2 centreTop,
        PetMode mode,
        bool adult,
        out PetPillAction action)
    {
        action = PetPillAction.None;

        var segments = new (PetMode Mode, PetPillAction Action, FontAwesomeIcon Icon, string Key)[5];
        var count = 0;
        segments[count++] = (PetMode.Feeding, PetPillAction.None, FontAwesomeIcon.Cookie, "os.aetherling_mode_feed");
        segments[count++] = (PetMode.Petting, PetPillAction.None, FontAwesomeIcon.HandHoldingHeart, "os.aetherling_mode_pet");
        if (adult)
        {
            segments[count++] = (mode, PetPillAction.Games, FontAwesomeIcon.Gamepad, "os.aetherling_mode_game");
            segments[count++] = (mode, PetPillAction.Wardrobe, FontAwesomeIcon.HatWizard, "os.aetherling_menu_wardrobe");
        }
        segments[count++] = (mode, PetPillAction.Stats, FontAwesomeIcon.ChartArea, "os.aetherling_mode_stats");

        var height = Height;
        var segW = Px(54f);
        var width = segW * count;
        var tl = new Vector2(centreTop.X - (width * 0.5f), centreTop.Y);
        var br = tl + new Vector2(width, height);
        var radius = height * 0.5f;

        dl.AddRectFilled(tl, br, Look.U32(new Vector4(1f, 1f, 1f, 0.055f)), radius);
        dl.AddRectFilled(tl, tl + new Vector2(width, height * 0.5f),
            Look.U32(new Vector4(1f, 1f, 1f, 0.045f)), radius, ImDrawFlags.RoundCornersTop);
        dl.AddRect(tl, br, Look.U32(new Vector4(1f, 1f, 1f, 0.14f)), radius, ImDrawFlags.RoundCornersAll, Px(1f));

        // The hairline between what a touch means and where a touch goes.
        var divider = tl.X + (segW * 2f);
        dl.AddLine(new Vector2(divider, tl.Y + Px(10f)), new Vector2(divider, br.Y - Px(10f)),
            Look.U32(new Vector4(1f, 1f, 1f, 0.12f)), Px(1f));

        for (var i = 0; i < count; i++)
        {
            var (segMode, segAction, icon, key) = segments[i];
            var segTl = new Vector2(tl.X + (segW * i), tl.Y);
            ImGui.SetCursorScreenPos(segTl);
            var pressed = ImGui.InvisibleButton($"##aetherlingMode{i}", new Vector2(segW, height));
            var hovered = ImGui.IsItemHovered();
            if (hovered)
            {
                HandOnHover();
                ImGui.SetTooltip(ctx.Localize(key));
            }
            if (pressed)
            {
                if (segAction == PetPillAction.None)
                {
                    mode = segMode;
                }
                else
                {
                    action = segAction;
                }
            }

            var selected = segAction == PetPillAction.None && mode == segMode;
            if (selected)
            {
                var pad = Px(4f);
                dl.AddRectFilled(segTl + new Vector2(pad, pad),
                    segTl + new Vector2(segW - pad, height - pad),
                    Look.U32(Look.Crystal with { W = 0.28f }), (height - (pad * 2f)) * 0.5f);
            }

            var alpha = selected ? 1f : hovered ? 0.85f : 0.55f;
            IconDraw.AddCentered(dl, icon, Px(16f),
                segTl + new Vector2(segW * 0.5f, height * 0.5f), Look.U32(Look.CrystalPale, alpha));
        }

        return mode;
    }
}
