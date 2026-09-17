using System;
using System.Numerics;

namespace AetherOS.Apps.Racer.Screens.Cards;

/// <summary>A small, bounded faux-perspective tilt applied to every vertex of a hovered card. The pointer is
/// the card-local position of the mouse in -1..1; the same pointer always gives the same tilt.</summary>
internal static class CardTilt
{
    private const float Turn = 0.025f;
    private const float MinDepth = 0.95f;
    private const float MaxDepth = 1.05f;

    public static Vector2 Project(Vector2 point, Vector2 centre, Vector2 size, Vector2 pointer)
    {
        pointer = Vector2.Clamp(pointer, -Vector2.One, Vector2.One);
        var offset = point - centre;
        var angle = pointer.X * Turn;
        var cos = MathF.Cos(angle);
        var sin = MathF.Sin(angle);
        var depth = 1f + (Turn * ((pointer.X * offset.X / MathF.Max(1f, size.X)) + (pointer.Y * offset.Y / MathF.Max(1f, size.Y))));
        depth = Math.Clamp(depth, MinDepth, MaxDepth);
        var p = new Vector2(offset.X * (1f - (Turn * MathF.Abs(pointer.X))), offset.Y * (1f - (Turn * MathF.Abs(pointer.Y)))) / depth;
        return centre + new Vector2((p.X * cos) - (p.Y * sin), (p.X * sin) + (p.Y * cos));
    }
}
