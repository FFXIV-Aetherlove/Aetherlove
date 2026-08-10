using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace AetherLove.UI;

/// <summary>Pure-ImGui draw primitives for the OS layer, shared between the plugin shell and the surface apps.</summary>
internal static class OsDrawShared
{
    /// <summary>Vertical gradient inside a rounded rect: a white rounded fill whose vertices are recolored
    /// by Y, preserving each vertex's anti-aliasing alpha.</summary>
    public static void RoundedGradient(ImDrawListPtr dl, Vector2 tl, Vector2 br, float rounding,
        Vector4 top, Vector4 bottom, float alpha = 1f)
    {
        var vtxStart = dl.VtxBuffer.Size;
        dl.AddRectFilled(tl, br, 0xFFFFFFFFu, rounding, ImDrawFlags.RoundCornersAll);
        var h = MathF.Max(1f, br.Y - tl.Y);
        for (int v = vtxStart; v < dl.VtxBuffer.Size; v++)
        {
            var vert = dl.VtxBuffer[v];
            var t = Math.Clamp((vert.Pos.Y - tl.Y) / h, 0f, 1f);
            var col = Vector4.Lerp(top, bottom, t);
            col.W *= alpha * ((vert.Col >> 24) & 0xFF) / 255f;
            vert.Col = ImGui.ColorConvertFloat4ToU32(col);
            dl.VtxBuffer[v] = vert;
        }
    }

    /// <summary>Draws centred text, clamped to <paramref name="maxWidth"/> when given. Returns true when the
    /// text had to be ellipsized, so the caller can offer the full string in a tooltip.</summary>
    public static bool CenteredText(ImDrawListPtr dl, string text, float centerX, float y, uint color,
        float sizeMul = 1f, float maxWidth = 0f)
    {
        var fsz = ImGui.GetFontSize() * sizeMul;
        var drawn = maxWidth > 0f ? Ellipsize(text, sizeMul, maxWidth) : text;
        var sz = ImGui.CalcTextSize(drawn) * sizeMul;
        dl.AddText(ImGui.GetFont(), fsz, new Vector2(centerX - sz.X * 0.5f, y), color, drawn);
        return drawn.Length != text.Length;
    }

    /// <summary>Trims text to fit <paramref name="maxWidth"/> at the given scale, appending an ellipsis;
    /// never splits a surrogate pair.</summary>
    public static string Ellipsize(string text, float sizeMul, float maxWidth)
    {
        if (ImGui.CalcTextSize(text).X * sizeMul <= maxWidth)
        {
            return text;
        }
        const string Ellipsis = "…";
        var budget = maxWidth - ImGui.CalcTextSize(Ellipsis).X * sizeMul;
        if (budget <= 0f)
        {
            return Ellipsis;
        }
        var len = text.Length;
        while (len > 0)
        {
            if (char.IsHighSurrogate(text[len - 1]))
            {
                len--;
                continue;
            }
            if (ImGui.CalcTextSize(text[..len]).X * sizeMul <= budget)
            {
                break;
            }
            len--;
        }
        return text[..len].TrimEnd() + Ellipsis;
    }

    public static uint White(float alpha) => ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, alpha));
    public static uint Black(float alpha) => ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, alpha));

    /// <summary>Cover-fit UVs cropping a texture of (texW, texH) onto a destination of aspect destW/destH.</summary>
    public static (Vector2 uv0, Vector2 uv1) CoverUv(float texW, float texH, float destW, float destH)
    {
        var texAspect = texW / texH;
        var destAspect = destW / destH;
        if (texAspect > destAspect)
        {
            var crop = destAspect / texAspect;
            var x0 = (1f - crop) * 0.5f;
            return (new Vector2(x0, 0f), new Vector2(x0 + crop, 1f));
        }
        var cropY = texAspect / destAspect;
        var y0 = (1f - cropY) * 0.5f;
        return (new Vector2(0f, y0), new Vector2(1f, y0 + cropY));
    }
}
