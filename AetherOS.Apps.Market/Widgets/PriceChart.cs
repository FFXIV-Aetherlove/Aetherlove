using System;
using System.Collections.Generic;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Market;
using Dalamud.Bindings.ImGui;

namespace AetherOS.Apps.Market;

/// <summary>Animated area chart for price history: accent line over a translucent fill, three gridlines
/// with compact gil labels, a left-to-right reveal on new data, and a hover crosshair with a tooltip.</summary>
internal sealed class PriceChart
{
    public readonly record struct PricePoint(DateTimeOffset Time, float Price, int Quantity);

    private const float LabelGutter = 38f;

    private IReadOnlyList<PricePoint> _points = [];
    private double _revealStart = double.MinValue;

    public void SetData(IReadOnlyList<PricePoint> points)
    {
        _points = points;
        _revealStart = ImGui.GetTime();
    }

    public bool HasData => _points.Count >= 2;

    public void Draw(Vector2 size, bool reduceMotion)
    {
        var t = ThemeService.Current;
        ImGui.InvisibleButton("##marketPriceChart", size);
        var tl = ImGui.GetItemRectMin();
        var br = ImGui.GetItemRectMax();
        var hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        var points = _points;
        if (points.Count < 2)
        {
            return;
        }

        var minPrice = float.MaxValue;
        var maxPrice = float.MinValue;
        foreach (var point in points)
        {
            minPrice = MathF.Min(minPrice, point.Price);
            maxPrice = MathF.Max(maxPrice, point.Price);
        }
        var pad = MathF.Max((maxPrice - minPrice) * 0.10f, maxPrice * 0.02f + 1f);
        var lo = MathF.Max(0f, minPrice - pad);
        var hi = maxPrice + pad;

        var plotTl = new Vector2(tl.X + Px(LabelGutter), tl.Y + Px(6f));
        var plotBr = new Vector2(br.X - Px(4f), br.Y - Px(6f));
        var plotW = plotBr.X - plotTl.X;
        var plotH = plotBr.Y - plotTl.Y;

        for (var g = 0; g < 3; g++)
        {
            var frac = g / 2f;
            var y = plotBr.Y - plotH * frac;
            dl.AddLine(new Vector2(plotTl.X, y), new Vector2(plotBr.X, y),
                ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.08f)), Px(1f));
            var label = MarketFormat.Gil((long)(lo + (hi - lo) * frac));
            var labelSz = ImGui.CalcTextSize(label);
            dl.AddText(new Vector2(plotTl.X - Px(6f) - labelSz.X, y - labelSz.Y * 0.5f),
                ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.40f)), label);
        }

        var xs = new float[points.Count];
        var ys = new float[points.Count];
        for (var i = 0; i < points.Count; i++)
        {
            xs[i] = plotTl.X + plotW * i / (points.Count - 1);
            ys[i] = plotBr.Y - plotH * ((points[i].Price - lo) / (hi - lo));
        }

        var reveal = 1f;
        if (!reduceMotion)
        {
            var p = Math.Clamp((ImGui.GetTime() - _revealStart) / 0.45, 0.0, 1.0);
            var inv = 1f - (float)p;
            reveal = 1f - inv * inv * inv;
        }
        dl.PushClipRect(plotTl with { Y = tl.Y }, new Vector2(plotTl.X + plotW * reveal + Px(1f), br.Y), true);

        var fill = ImGui.GetColorU32(t.Accent with { W = 0.16f });
        for (var i = 1; i < points.Count; i++)
        {
            dl.AddQuadFilled(
                new Vector2(xs[i - 1], ys[i - 1]),
                new Vector2(xs[i], ys[i]),
                new Vector2(xs[i], plotBr.Y),
                new Vector2(xs[i - 1], plotBr.Y),
                fill);
        }
        for (var i = 0; i < points.Count; i++)
        {
            dl.PathLineTo(new Vector2(xs[i], ys[i]));
        }
        dl.PathStroke(ImGui.GetColorU32(t.AccentLight), ImDrawFlags.None, Px(2f));
        dl.PopClipRect();

        if (hovered && reveal >= 1f)
        {
            DrawHover(dl, points, xs, ys, plotTl, plotBr, t.AccentLight);
        }
    }

    private static void DrawHover(ImDrawListPtr dl, IReadOnlyList<PricePoint> points, float[] xs, float[] ys,
        Vector2 plotTl, Vector2 plotBr, Vector4 accent)
    {
        var mouseX = ImGui.GetMousePos().X;
        var nearest = 0;
        var bestDist = float.MaxValue;
        for (var i = 0; i < xs.Length; i++)
        {
            var d = MathF.Abs(xs[i] - mouseX);
            if (d < bestDist)
            {
                bestDist = d;
                nearest = i;
            }
        }

        dl.AddLine(new Vector2(xs[nearest], plotTl.Y), new Vector2(xs[nearest], plotBr.Y),
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.22f)), Px(1f));
        dl.AddCircleFilled(new Vector2(xs[nearest], ys[nearest]), Px(3.5f), ImGui.GetColorU32(accent));

        var point = points[nearest];
        var line1 = point.Time.ToLocalTime().ToString("d MMM");
        var line2 = $"{MarketFormat.GilFull((long)point.Price)}g · x{point.Quantity}";
        var sz1 = ImGui.CalcTextSize(line1);
        var sz2 = ImGui.CalcTextSize(line2);
        var boxW = MathF.Max(sz1.X, sz2.X) + Px(16f);
        var boxH = sz1.Y + sz2.Y + Px(12f);
        var boxX = Math.Clamp(xs[nearest] + Px(10f), plotTl.X, plotBr.X - boxW);
        var boxY = Math.Clamp(ys[nearest] - boxH - Px(8f), plotTl.Y, plotBr.Y - boxH);
        var boxTl = new Vector2(boxX, boxY);
        dl.AddRectFilled(boxTl, boxTl + new Vector2(boxW, boxH), ImGui.GetColorU32(new Vector4(0.10f, 0.09f, 0.13f, 0.96f)), Px(6f));
        dl.AddRect(boxTl, boxTl + new Vector2(boxW, boxH), ImGui.GetColorU32(accent with { W = 0.45f }), Px(6f));
        dl.AddText(boxTl + new Vector2(Px(8f), Px(4f)), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.65f)), line1);
        dl.AddText(boxTl + new Vector2(Px(8f), Px(6f) + sz1.Y), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.95f)), line2);
    }
}
