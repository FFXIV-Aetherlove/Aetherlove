using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace AetherLove.Widgets;

/// <summary>Time-driven rotating arc spinner, drawn into the current window's draw list.</summary>
public static class LoadingSpinner
{
    public static void Draw(Vector2 center, float radius, float thickness, uint color)
    {
        var dl = ImGui.GetWindowDrawList();
        var time = (float)ImGui.GetTime();
        const int Segments = 24;
        var start = time * 6f;
        var arc = MathF.PI * 1.5f;

        dl.PathClear();
        for (int i = 0; i <= Segments; i++)
        {
            var a = start + (i / (float)Segments) * arc;
            dl.PathLineTo(new Vector2(center.X + MathF.Cos(a) * radius, center.Y + MathF.Sin(a) * radius));
        }
        dl.PathStroke(color, ImDrawFlags.None, thickness);
    }
}
