using System;
using System.Numerics;
using AetherLove.Services;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;

namespace AetherOS.Apps.Wallet;

/// <summary>The weekly-cap ring: the routine arc in the theme accent and the exempt arc in gold, stacked
/// on a dim track scaled to the total weekly ceiling, with a tick marking the routine ceiling (cap plus
/// banked carry). The center shows this week's earned total over the ceiling.</summary>
internal static class CapRing
{
    public static void Draw(Vector2 center, float radius, float thickness,
        int routineEarned, int exemptEarned, int routineCeiling, int totalCap, float reveal)
    {
        var dl = ImGui.GetWindowDrawList();
        var t = ThemeService.Current;
        var total = Math.Max(1, totalCap);
        var start = -MathF.PI / 2f;

        dl.AddCircle(center, radius, OsDrawShared.White(0.08f), 96, thickness);

        var routineFrac = Math.Clamp(routineEarned / (float)total, 0f, 1f) * reveal;
        var exemptFrac = Math.Clamp(exemptEarned / (float)total, 0f, 1f - routineFrac) * reveal;
        StrokeArc(dl, center, radius, thickness, start, start + MathF.Tau * routineFrac,
            ImGui.GetColorU32(t.Accent));
        StrokeArc(dl, center, radius, thickness, start + MathF.Tau * routineFrac,
            start + MathF.Tau * (routineFrac + exemptFrac), UiColors.FavoriteStar);

        var ceilingFrac = Math.Clamp(routineCeiling / (float)total, 0f, 1f);
        if (ceilingFrac is > 0f and < 1f)
        {
            var a = start + MathF.Tau * ceilingFrac;
            var dir = new Vector2(MathF.Cos(a), MathF.Sin(a));
            dl.AddLine(center + dir * (radius - thickness * 0.9f), center + dir * (radius + thickness * 0.9f),
                OsDrawShared.White(0.75f), Px(2f));
        }

        var earned = (long)((routineEarned + exemptEarned) * reveal);
        string big;
        Vector2 bigSz;
        using (UiFonts.H1?.Push())
        {
            big = earned.ToString("N0");
            bigSz = ImGui.CalcTextSize(big);
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
                center - new Vector2(bigSz.X * 0.5f, bigSz.Y * 0.72f), ImGui.GetColorU32(UiColors.Body), big);
        }
        var sub = $"/ {totalCap:N0}";
        var subSz = ImGui.CalcTextSize(sub);
        dl.AddText(new Vector2(center.X - subSz.X * 0.5f, center.Y + bigSz.Y * 0.34f),
            ImGui.GetColorU32(UiColors.Hint), sub);
    }

    private static void StrokeArc(ImDrawListPtr dl, Vector2 center, float radius, float thickness,
        float a0, float a1, uint color)
    {
        if (a1 - a0 < 0.001f)
        {
            return;
        }
        var segments = Math.Max(3, (int)(96 * (a1 - a0) / MathF.Tau));
        dl.PathClear();
        for (var i = 0; i <= segments; i++)
        {
            var a = a0 + (a1 - a0) * (i / (float)segments);
            dl.PathLineTo(new Vector2(center.X + MathF.Cos(a) * radius, center.Y + MathF.Sin(a) * radius));
        }
        dl.PathStroke(color, ImDrawFlags.None, thickness);
    }
}
