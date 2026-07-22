using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace AetherLove.UI;

/// <summary>Small ImGui colour/text draw helpers shared by the match-reveal effects and the offline screen.</summary>
public static class DrawFx
{
    public static uint U32(Vector4 c) => ImGui.ColorConvertFloat4ToU32(c);

    public static Vector4 Rgba(Vector4 c, float a) => new(c.X, c.Y, c.Z, a);

    /// <summary>Draws <paramref name="text"/> horizontally centred on <paramref name="cx"/> in the current font.</summary>
    public static void CenterText(ImDrawListPtr dl, float cx, float y, string text, uint col)
    {
        var w = ImGui.CalcTextSize(text).X;
        dl.AddText(new Vector2(cx - w * 0.5f, y), col, text);
    }
}
