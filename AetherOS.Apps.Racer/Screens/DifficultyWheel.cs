using System;
using System.Numerics;
using AetherLove.Shared.Aetherling;
using AetherLove.Shared.Racing;
using AetherLove.UI;
using AetherOS.Apps.Racer.Rendering;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Racer.Screens;

/// <summary>Which surface the wheel is drawn on. Paper takes every colour DOWN to ink weight and prints
/// Easy in the page's own ink, because a white flag cannot show on white paper; Night lifts them instead
/// and lets Easy keep its flag.</summary>
internal enum WheelSurface
{
    Paper,
    Night,
}

/// <summary>The element wheel, drawn rather than dealt as art: six wedges turned so the racer's own
/// element sits at the top, the three grades arced outside the rim. Two pages show it (the explainer and
/// the onboarding), and a wheel that disagreed with itself between them would be worse than no wheel, so
/// there is one drawer and it takes the surface as an argument.</summary>
internal static class DifficultyWheel
{
    private const float IconReach = 0.62f;
    private const float IconSize = 0.34f;
    private const float ArcGap = 9f;
    private const float ArcRoom = 14f;

    /// <summary>How far the arcs and their labels reach past the rim, so a caller can centre the wheel
    /// without measuring it.</summary>
    public static float Overhang(float line) => Px(ArcGap + ArcRoom) + line + Px(6);

    /// <summary>The wheel at <paramref name="centre"/>. <paramref name="own"/> null draws it unturned and
    /// with no wedge filled, which is what an onboarding shows before there is a racer to speak of.</summary>
    public static void Draw(
        OsAppContext ctx, ImDrawListPtr dl, Vector2 centre, float radius, AetherlingElement? own,
        WheelSurface surface, Vector4 ink, bool labels = true)
    {
        var line = ImGui.GetTextLineHeight();
        var inkU32 = ImGui.ColorConvertFloat4ToU32(ink);
        var top = own is { } o ? Array.IndexOf(RacingElements.WheelOrder, RacingElements.NameOf(o)) : 0;

        for (var slot = 0; slot < 6; slot++)
        {
            var element = RacingElements.WheelOrder[(top + slot + 6) % 6];
            var mid = (-MathF.PI / 2f) + (slot * MathF.PI / 3f);
            var a0 = mid - (MathF.PI / 6f);
            var a1 = mid + (MathF.PI / 6f);
            var tint = Tone(ElementFx.For(element).Tint, surface);
            var mine = own is not null && slot == 0;

            if (mine)
            {
                dl.PathLineTo(centre);
                dl.PathArcTo(centre, radius, a0, a1, 24);
                dl.PathFillConvex(ImGui.ColorConvertFloat4ToU32(tint with { W = 0.88f }));
            }

            dl.AddLine(centre, centre + (new Vector2(MathF.Cos(a0), MathF.Sin(a0)) * radius), inkU32, Px(1.4f));

            var at = centre + (new Vector2(MathF.Cos(mid), MathF.Sin(mid)) * (radius * IconReach));
            IconDraw.AddCentered(dl, Glyph(element), radius * IconSize, at,
                mine ? 0xFFFFFFFFu : ImGui.ColorConvertFloat4ToU32(tint));
        }

        dl.AddCircle(centre, radius, inkU32, 72, Px(2f));

        Arc(ctx, dl, centre, radius, 0, 1, (short)LumiRaceDifficulty.Easy, Px(4.5f), false, line, surface, ink, labels);
        Arc(ctx, dl, centre, radius, 1, 1, (short)LumiRaceDifficulty.Normal, Px(3f), false, line, surface, ink, labels);
        Arc(ctx, dl, centre, radius, 5, 1, (short)LumiRaceDifficulty.Normal, Px(3f), false, line, surface, ink, false);
        Arc(ctx, dl, centre, radius, 2, 3, (short)LumiRaceDifficulty.Hard, Px(2f), true, line, surface, ink, labels);
    }

    /// <summary>A grade's colour on this surface. Easy's flag is white, which paper cannot show; it
    /// prints in racing green rather than the page's own blue, which sat a shade from Normal's and made
    /// the two grades read as one.</summary>
    public static Vector4 GradeInk(short grade, WheelSurface surface, Vector4 ink) =>
        surface == WheelSurface.Paper && grade == (short)LumiRaceDifficulty.Easy
            ? RacerChrome.GradeGreen
            : Tone(RacerChrome.GradeFlag(grade), surface);

    /// <summary>One grade's arc outside the rim, spanning whole wedge slots, its label at the arc's
    /// middle. Slot 0 is the top wedge; slots count clockwise.</summary>
    private static void Arc(
        OsAppContext ctx, ImDrawListPtr dl, Vector2 centre, float radius, int slotFrom, int slots,
        short grade, float stroke, bool dashed, float line, WheelSurface surface, Vector4 ink, bool label)
    {
        const float trim = 0.05f;
        var r = radius + Px(ArcGap);
        var a0 = (-MathF.PI / 2f) + (slotFrom * MathF.PI / 3f) - (MathF.PI / 6f) + trim;
        var a1 = a0 + (slots * MathF.PI / 3f) - (trim * 2f);
        var colour = ImGui.ColorConvertFloat4ToU32(GradeInk(grade, surface, ink));

        if (dashed)
        {
            const float dash = 0.10f;
            const float gap = 0.07f;
            for (var a = a0; a < a1; a += dash + gap)
            {
                dl.PathArcTo(centre, r, a, MathF.Min(a + dash, a1), 8);
                dl.PathStroke(colour, ImDrawFlags.None, stroke);
            }
        }
        else
        {
            dl.PathArcTo(centre, r, a0, a1, 24);
            dl.PathStroke(colour, ImDrawFlags.None, stroke);
        }

        if (!label)
        {
            return;
        }

        var mid = (a0 + a1) * 0.5f;
        var text = RacerChrome.DifficultyLabel(ctx, grade);
        var size = ImGui.CalcTextSize(text);
        var at = centre + (new Vector2(MathF.Cos(mid), MathF.Sin(mid)) * (r + Px(ArcRoom)));
        at -= new Vector2(size.X * 0.5f, line * 0.5f);
        at += new Vector2(MathF.Cos(mid) * size.X * 0.5f, MathF.Sin(mid) * line * 0.5f);
        dl.AddText(at, colour, text);
    }

    /// <summary>A colour brought to the weight its surface can hold: down towards ink on paper, up towards
    /// light on the night picture.</summary>
    public static Vector4 Tone(Vector4 colour, WheelSurface surface)
    {
        const float paperLuminance = 0.42f;
        const float nightLuminance = 0.62f;
        if (surface == WheelSurface.Paper)
        {
            return ElementFx.Luminance(colour) <= paperLuminance
                ? colour
                : ElementFx.AtLuminance(colour, paperLuminance);
        }
        return ElementFx.Luminance(colour) >= nightLuminance
            ? colour
            : ElementFx.AtLuminance(colour, nightLuminance);
    }

    /// <summary>The six elements' marks, in glyphs every client already ships.</summary>
    private static FontAwesomeIcon Glyph(string element) => element switch
    {
        "fire" => FontAwesomeIcon.Fire,
        "lightning" => FontAwesomeIcon.Bolt,
        "wind" => FontAwesomeIcon.Wind,
        "ice" => FontAwesomeIcon.Snowflake,
        "water" => FontAwesomeIcon.Tint,
        _ => FontAwesomeIcon.Mountain,
    };
}
