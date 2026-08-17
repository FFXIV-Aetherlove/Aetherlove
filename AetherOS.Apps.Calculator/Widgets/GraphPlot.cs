using System;
using System.Numerics;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;

namespace AetherOS.Apps.Calculator;

/// <summary>Renders the plot: axes with nice ticks, one polyline per enabled slot sampled per horizontal
/// pixel, and the trace cursor. Everything is clipped to the plot rect.</summary>
internal static class GraphPlot
{
    /// <summary>A jump larger than this many window heights between neighbouring samples is a pole, not a
    /// steep slope, so the polyline breaks instead of drawing the asymptote as a line.</summary>
    private const double JumpFactor = 6d;

    private const int MaxGridLines = 64;

    public static Vector2 ToScreen(GraphWindow w, Vector2 tl, Vector2 size, double x, double y)
    {
        var sx = tl.X + (float)((x - w.XMin) / w.Width) * size.X;
        var sy = tl.Y + (float)((w.YMax - y) / w.Height) * size.Y;
        return new Vector2(sx, sy);
    }

    public static double ScreenToX(GraphWindow w, Vector2 tl, Vector2 size, float sx) =>
        w.XMin + ((sx - tl.X) / size.X) * w.Width;

    public static double ScreenToY(GraphWindow w, Vector2 tl, Vector2 size, float sy) =>
        w.YMax - ((sy - tl.Y) / size.Y) * w.Height;

    /// <summary>The panel, its grid, both axes, their ticks and the tick labels.</summary>
    public static void DrawFrame(OsAppContext ctx, ImDrawListPtr dl, Vector2 tl, Vector2 size, GraphWindow w)
    {
        DeviceUi.Lcd(ctx, dl, tl, size);
        dl.PushClipRect(tl, tl + size, true);

        var stepX = NiceStep(w.Width);
        var stepY = NiceStep(w.Height);
        var grid = DeviceUi.Ink(0.14f);
        var axis = DeviceUi.Ink(0.85f);
        var tickInk = DeviceUi.Ink(0.7f);

        var runX = GridRun(w.XMin, w.XMax, stepX);
        var runY = GridRun(w.YMin, w.YMax, stepY);
        for (var k = 0; k <= runX.Count; k++)
        {
            var p = ToScreen(w, tl, size, (runX.First + k) * stepX, w.YMax);
            dl.AddLine(new Vector2(p.X, tl.Y), new Vector2(p.X, tl.Y + size.Y), grid, ctx.Px(1f));
        }
        for (var k = 0; k <= runY.Count; k++)
        {
            var p = ToScreen(w, tl, size, w.XMin, (runY.First + k) * stepY);
            dl.AddLine(new Vector2(tl.X, p.Y), new Vector2(tl.X + size.X, p.Y), grid, ctx.Px(1f));
        }

        var originY = Math.Clamp(ToScreen(w, tl, size, 0d, 0d).Y, tl.Y, tl.Y + size.Y);
        var originX = Math.Clamp(ToScreen(w, tl, size, 0d, 0d).X, tl.X, tl.X + size.X);
        dl.AddLine(new Vector2(tl.X, originY), new Vector2(tl.X + size.X, originY), axis, ctx.Px(1.4f));
        dl.AddLine(new Vector2(originX, tl.Y), new Vector2(originX, tl.Y + size.Y), axis, ctx.Px(1.4f));

        var tick = ctx.Px(3f);
        var labelScale = 0.72f;
        var labelEvery = w.Width / stepX > 9d ? 2 : 1;
        for (var index = 0; index <= runX.Count; index++)
        {
            var gx = (runX.First + index) * stepX;
            var p = ToScreen(w, tl, size, gx, 0d);
            dl.AddLine(new Vector2(p.X, originY - tick), new Vector2(p.X, originY + tick), tickInk, ctx.Px(1f));
            if (Math.Abs(gx) < stepX * 0.5d || index % labelEvery != 0)
            {
                continue;
            }
            var label = CalcFormat.Axis(gx);
            var sz = ImGui.CalcTextSize(label) * labelScale;
            var ly = MathF.Min(originY + tick + ctx.Px(1f), tl.Y + size.Y - sz.Y);
            DeviceUi.SmallText(dl, label, new Vector2(p.X - sz.X * 0.5f, ly), labelScale, tickInk);
        }

        var labelEveryY = w.Height / stepY > 9d ? 2 : 1;
        for (var index = 0; index <= runY.Count; index++)
        {
            var gy = (runY.First + index) * stepY;
            var p = ToScreen(w, tl, size, 0d, gy);
            dl.AddLine(new Vector2(originX - tick, p.Y), new Vector2(originX + tick, p.Y), tickInk, ctx.Px(1f));
            if (Math.Abs(gy) < stepY * 0.5d || index % labelEveryY != 0)
            {
                continue;
            }
            var label = CalcFormat.Axis(gy);
            var sz = ImGui.CalcTextSize(label) * labelScale;
            var lx = MathF.Max(tl.X + ctx.Px(1f), originX - tick - ctx.Px(2f) - sz.X);
            DeviceUi.SmallText(dl, label, new Vector2(lx, p.Y - sz.Y * 0.5f), labelScale, tickInk);
        }

        dl.PopClipRect();
    }

    /// <summary>One curve, sampled per horizontal pixel. A sample that will not evaluate is a hole, and a
    /// jump across a pole breaks the polyline rather than drawing a false vertical.</summary>
    public static void DrawCurve(OsAppContext ctx, ImDrawListPtr dl, Vector2 tl, Vector2 size,
        CalcSession session, GraphFunction fn, GraphWindow w)
    {
        if (fn.Compiled is null || !w.Valid)
        {
            return;
        }
        dl.PushClipRect(tl, tl + size, true);
        var color = ImGui.ColorConvertFloat4ToU32(fn.Color);
        var thickness = ctx.Px(1.8f);
        var columns = Math.Max(2, (int)size.X);
        var limitLow = tl.Y - size.Y * 4f;
        var limitHigh = tl.Y + size.Y * 5f;

        var havePrev = false;
        var prevY = 0d;
        var prevPoint = Vector2.Zero;
        for (var i = 0; i <= columns; i++)
        {
            var x = w.XMin + (w.Width * i / columns);
            if (!session.TrySample(fn, x, out var y))
            {
                havePrev = false;
                continue;
            }
            var point = ToScreen(w, tl, size, x, y);
            point.Y = Math.Clamp(point.Y, limitLow, limitHigh);
            if (havePrev && !Discontinuous(w, prevY, y))
            {
                dl.AddLine(prevPoint, point, color, thickness);
            }
            prevPoint = point;
            prevY = y;
            havePrev = true;
        }
        dl.PopClipRect();
    }

    /// <summary>The two signatures of a pole between neighbouring samples: the pair straddles the whole
    /// window from opposite sides, or the step is far larger than the window is tall.</summary>
    private static bool Discontinuous(GraphWindow w, double previous, double current)
    {
        var straddles = (previous > w.YMax && current < w.YMin) || (previous < w.YMin && current > w.YMax);
        if (straddles)
        {
            return true;
        }
        return Math.Abs(current - previous) > w.Height * JumpFactor;
    }

    /// <summary>The trace cursor: a ring on the curve plus its dropped guide lines.</summary>
    public static void DrawTraceCursor(OsAppContext ctx, ImDrawListPtr dl, Vector2 tl, Vector2 size,
        GraphWindow w, Vector2 point, Vector4 color)
    {
        dl.PushClipRect(tl, tl + size, true);
        var guide = ImGui.ColorConvertFloat4ToU32(color with { W = 0.45f });
        dl.AddLine(new Vector2(point.X, tl.Y), new Vector2(point.X, tl.Y + size.Y), guide, ctx.Px(1f));
        dl.AddCircleFilled(point, ctx.Px(4.5f), ImGui.ColorConvertFloat4ToU32(RetroLcd.Panel), 16);
        dl.AddCircle(point, ctx.Px(4.5f), ImGui.ColorConvertFloat4ToU32(color), 16, ctx.Px(1.8f));
        dl.PopClipRect();
    }

    /// <summary>Which grid lines a span carries, counted rather than accumulated: a window far from the origin
    /// can have a step smaller than one ULP of its own bounds, where "value += step" never advances and the
    /// loop hangs the draw thread.</summary>
    private static (double First, int Count) GridRun(double min, double max, double step)
    {
        var first = Math.Ceiling(min / step);
        var span = Math.Floor(max / step) - first;
        var count = double.IsFinite(span) ? (int)Math.Clamp(span, 0d, MaxGridLines) : 0;
        return (first, count);
    }

    /// <summary>A 1, 2 or 5 times a power of ten, so ticks land on numbers a human would have chosen.</summary>
    public static double NiceStep(double span)
    {
        if (!double.IsFinite(span) || span <= 0d)
        {
            return 1d;
        }
        var raw = span / 8d;
        var magnitude = Math.Pow(10d, Math.Floor(Math.Log10(raw)));
        var normalized = raw / magnitude;
        if (normalized <= 1d)
        {
            return magnitude;
        }
        if (normalized <= 2d)
        {
            return 2d * magnitude;
        }
        if (normalized <= 5d)
        {
            return 5d * magnitude;
        }
        return 10d * magnitude;
    }

    /// <summary>ZOOM FIT: the y range the enabled slots actually occupy over the current x range.</summary>
    public static bool TryFitY(CalcSession session, GraphWindow w, out double min, out double max)
    {
        min = double.MaxValue;
        max = double.MinValue;
        const int samples = 240;
        foreach (var fn in session.Functions)
        {
            if (!fn.Plotted)
            {
                continue;
            }
            for (var i = 0; i <= samples; i++)
            {
                var x = w.XMin + (w.Width * i / samples);
                if (!session.TrySample(fn, x, out var y))
                {
                    continue;
                }
                if (Math.Abs(y) > 1e9d)
                {
                    continue;
                }
                min = Math.Min(min, y);
                max = Math.Max(max, y);
            }
        }
        if (min > max)
        {
            min = 0d;
            max = 0d;
            return false;
        }
        if (max - min < 1e-6d)
        {
            min -= 1d;
            max += 1d;
        }
        var pad = (max - min) * 0.1d;
        min -= pad;
        max += pad;
        return true;
    }
}
