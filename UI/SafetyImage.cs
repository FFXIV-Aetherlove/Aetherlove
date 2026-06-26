using System;
using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;

namespace AetherLove.UI;

/// <summary>Renders a conversation's verification fingerprint as a deterministic Truchet weave plus
/// human-comparable text. The image is derived purely from the fingerprint bytes, so both peers draw the
/// exact same weave and safety code. The palette is intentionally fixed (not the user's selected theme):
/// two peers may run different themes, and the image must still match on both screens.</summary>
public static class SafetyImage
{
    // Fixed pool spanning all three app themes (see the class summary), not the user's selected theme.
    private static readonly uint[] Palette =
    {
        0xFFC96BBA, // #BA6BC9
        0xFFEB669E, // #9E66EB
        0xFFC773FA, // #FA73C7
        0xFFE68FD9, // #D98FE6
        0xFF4DB8FF, // #FFB84D
        0xFF80D9FF, // #FFD980
        0xFFADE666, // #66E6AD
        0xFF6647F2, // #F24766
        0xFF947AFF, // #FF7A94
        0xFFF58F5C, // #5C8FF5
    };

    private const uint Background = 0xFF1C1315; // #15131C

    /// <summary>Draws the weave filling a square of <paramref name="size"/> at <paramref name="topLeft"/>.</summary>
    public static void DrawTruchet(ImDrawListPtr dl, Vector2 topLeft, float size, byte[] fp)
    {
        const int N = 6;
        var cell = size / N;
        var rounding = size * 0.06f;
        dl.AddRectFilled(topLeft, topLeft + new Vector2(size, size), Background, rounding);

        var r = cell * 0.5f;
        var thickness = cell * 0.30f;
        const int segs = 10;

        for (var gy = 0; gy < N; gy++)
        {
            for (var gx = 0; gx < N; gx++)
            {
                var i = gy * N + gx;
                var ox = topLeft.X + gx * cell;
                var oy = topLeft.Y + gy * cell;
                var bit = (fp[(i >> 3) % fp.Length] >> (i & 7)) & 1;
                var col = Palette[fp[(i * 7 + 5) % fp.Length] % Palette.Length];

                if (bit == 0)
                {
                    DrawArc(dl, new Vector2(ox, oy), r, 0f, MathF.PI * 0.5f, col, thickness, segs);
                    DrawArc(dl, new Vector2(ox + cell, oy + cell), r, MathF.PI, MathF.PI * 1.5f, col, thickness, segs);
                }
                else
                {
                    DrawArc(dl, new Vector2(ox + cell, oy), r, MathF.PI * 0.5f, MathF.PI, col, thickness, segs);
                    DrawArc(dl, new Vector2(ox, oy + cell), r, MathF.PI * 1.5f, MathF.PI * 2f, col, thickness, segs);
                }
            }
        }
    }

    private static void DrawArc(ImDrawListPtr dl, Vector2 center, float radius, float a0, float a1,
        uint col, float thickness, int segs)
    {
        for (var s = 0; s <= segs; s++)
        {
            var a = a0 + (a1 - a0) * (s / (float)segs);
            dl.PathLineTo(new Vector2(center.X + MathF.Cos(a) * radius, center.Y + MathF.Sin(a) * radius));
        }
        dl.PathStroke(col, ImDrawFlags.None, thickness);
    }

    /// <summary>The fingerprint as a short safety code: 8 groups of 4 hex chars (first 16 bytes).</summary>
    public static string SafetyCode(byte[] fp)
    {
        var sb = new StringBuilder();
        for (var g = 0; g < 8; g++)
        {
            if (g > 0)
            {
                sb.Append(g == 4 ? '\n' : ' ');
            }
            sb.Append(fp[g * 2 % fp.Length].ToString("X2"));
            sb.Append(fp[(g * 2 + 1) % fp.Length].ToString("X2"));
        }
        return sb.ToString();
    }

    /// <summary>A short hex excerpt of a public key: first and last 6 bytes with an ellipsis between.</summary>
    public static string KeyExcerpt(byte[] key)
    {
        if (key.Length <= 12)
        {
            return Hex(key, 0, key.Length);
        }
        return Hex(key, 0, 6) + "  ...  " + Hex(key, key.Length - 6, 6);
    }

    private static string Hex(byte[] key, int start, int count)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < count; i++)
        {
            if (i > 0)
            {
                sb.Append(' ');
            }
            sb.Append(key[start + i].ToString("X2"));
        }
        return sb.ToString();
    }
}
