using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace AetherOS.Apps.Market;

/// <summary>Shared eye-candy for Market surfaces.</summary>
internal static class MarketFx
{
    /// <summary>A glossy diagonal band that sweeps across the rect every so often; the seed staggers the
    /// timing so a group of surfaces never flashes in unison.</summary>
    public static void DrawShine(ImDrawListPtr dl, Vector2 tl, Vector2 br, int seed, bool reduceMotion)
    {
        if (reduceMotion)
        {
            return;
        }
        var period = 9.0 + seed * 2.7 % 7.0;
        var phase = (ImGui.GetTime() + seed * 3.31) % period;
        const double Duration = 1.15;
        if (phase > Duration)
        {
            return;
        }
        var p = (float)(phase / Duration);
        p = 1f - (1f - p) * (1f - p);

        var w = br.X - tl.X;
        var h = br.Y - tl.Y;
        var slant = h * 0.55f;
        var bandW = w * 0.30f;
        var travel = w + slant + bandW * 2f;
        var x = tl.X - bandW - slant + travel * p;

        dl.PushClipRect(tl + new Vector2(1f, 1f), br - new Vector2(1f, 1f), true);
        Span<float> alphas = [0.05f, 0.12f, 0.05f];
        Span<float> offsets = [0f, bandW * 0.33f, bandW * 0.66f];
        for (var i = 0; i < 3; i++)
        {
            var x0 = x + offsets[i];
            var color = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, alphas[i]));
            dl.AddQuadFilled(
                new Vector2(x0 + slant, tl.Y),
                new Vector2(x0 + slant + bandW * 0.34f, tl.Y),
                new Vector2(x0 + bandW * 0.34f, br.Y),
                new Vector2(x0, br.Y),
                color);
        }
        dl.PopClipRect();
    }
}
