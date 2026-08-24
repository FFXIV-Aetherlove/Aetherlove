using System;
using System.IO;
using System.Numerics;
using AetherOS.Apps.Aetherling.Ui;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;

namespace AetherOS.Apps.Aetherling.Screens.Games.LumiLink;

/// <summary>How a tile looks in each theme, shared by the board and the explainer so the tutorial's
/// pieces are the game's pieces. Three themes, all baked to PNG under `games/lumilink/` in the six
/// candy primaries: the element crystals pulled to their kind's hue; Lumis from the adult sheet; the
/// summer wardrobe recoloured per kind (`summer_<kind>.png`, baked from the worn art and pulled to
/// the kind's hue; the worn items themselves are untouched). The crystals carry a faint square rim in their own colour, the summer pieces
/// the same rim over a light wash; the Lumis stand bare. Art is always drawn at
/// its own aspect inside the square, never stretched to it.</summary>
internal static class LumiLinkPieces
{
    public const int Themes = 3;

    public static readonly string[] Elements = ["fire", "water", "ice", "wind", "lightning", "earth"];

    /// <summary>The six kind colours, the candy primaries: red, blue, purple, green, yellow, orange.
    /// Kind order is the element order (fire, water, ice, wind, lightning, earth) and every theme's
    /// baked art is pulled to the same hue, so kind 2 is purple whether it is a crystal, a Lumi or a
    /// cooler.</summary>
    public static readonly Vector4[] KindColours =
    [
        new(1f, 0.28f, 0.24f, 1f),
        new(0.26f, 0.5f, 1f, 1f),
        new(0.72f, 0.36f, 1f, 1f),
        new(0.3f, 0.82f, 0.32f, 1f),
        new(1f, 0.85f, 0.25f, 1f),
        new(1f, 0.58f, 0.18f, 1f),
    ];

    public static void Draw(OsAppContext ctx, ImDrawListPtr dl, string assetRoot, int theme,
        Vector2 centre, float size, int kind, Special special, float alpha, float tilt, Vector2 squash,
        double now, bool reduceMotion)
    {
        var half = new Vector2(size * 0.5f * squash.X, size * 0.5f * squash.Y);
        var tint = Look.U32(new Vector4(1f, 1f, 1f, alpha));
        var colour = KindColours[kind];

        if (theme != 1)
        {
            // A whisper of the kind's colour around the square: a rim for crystals, a rim with a faint
            // wash for the summer finds, so a wedge or a pail reads against any neighbour.
            var plateHalf = new Vector2(size * 0.54f);
            if (theme == 2)
            {
                dl.AddRectFilled(centre - plateHalf, centre + plateHalf, Look.U32(colour with { W = 0.14f * alpha }), size * 0.22f);
            }
            dl.AddRect(centre - plateHalf, centre + plateHalf, Look.U32(colour with { W = 0.28f * alpha }), size * 0.22f,
                ImDrawFlags.RoundCornersAll, 1.2f);
        }

        var path = theme switch
        {
            0 => Path.Combine(assetRoot, "games", "lumilink", $"crystal_{kind}.png"),
            1 => Path.Combine(assetRoot, "games", "lumilink", $"lumi_{kind}.png"),
            _ => Path.Combine(assetRoot, "games", "lumilink", $"summer_{kind}.png"),
        };
        var tex = ctx.Capabilities.Textures.Get(path);
        if (tex is { } handle)
        {
            var texSize = ctx.Capabilities.Textures.GetSize(path);
            var fit = texSize is { } ts && ts.X > 0f && ts.Y > 0f
                ? half * (ts.X >= ts.Y ? new Vector2(1f, ts.Y / ts.X) : new Vector2(ts.X / ts.Y, 1f))
                : half;
            if (theme != 0)
            {
                fit *= 0.88f;
                AddImageRotated(dl, handle, centre + new Vector2(1.5f, 2.5f), fit, tilt,
                    Look.U32(new Vector4(0f, 0f, 0f, 0.45f * alpha)));
            }
            AddImageRotated(dl, handle, centre, fit, tilt, tint);
        }

        if (special == Special.None)
        {
            return;
        }
        DrawSpecialOverlay(dl, centre, size, kind, special, alpha, now, reduceMotion);
    }

    public static void DrawSpecialOverlay(ImDrawListPtr dl, Vector2 centre, float size, int kind, Special special,
        float alpha, double now, bool reduceMotion)
    {
        var glow = 0.6f + (reduceMotion ? 0f : 0.4f * MathF.Sin((float)(now * 7.0)));
        var colour = KindColours[kind] with { W = alpha };
        var r = size * 0.5f;
        switch (special)
        {
            case Special.BoltRow:
                dl.AddRectFilled(centre - new Vector2(r, r * 0.16f), centre + new Vector2(r, r * 0.16f),
                    Look.U32(new Vector4(1f, 1f, 1f, 0.8f * glow * alpha)), r * 0.16f);
                break;
            case Special.BoltColumn:
                dl.AddRectFilled(centre - new Vector2(r * 0.16f, r), centre + new Vector2(r * 0.16f, r),
                    Look.U32(new Vector4(1f, 1f, 1f, 0.8f * glow * alpha)), r * 0.16f);
                break;
            case Special.Burst:
                dl.AddCircle(centre, r * 0.92f, Look.U32(new Vector4(1f, 1f, 1f, 0.85f * glow * alpha)), 32, 2.2f);
                break;
            case Special.TBurst:
                dl.AddCircle(centre, r * 0.92f, Look.U32(new Vector4(1f, 1f, 1f, 0.85f * glow * alpha)), 32, 2.2f);
                dl.AddLine(centre - new Vector2(r, 0f), centre + new Vector2(r, 0f), Look.U32(colour with { W = 0.9f * alpha }), 2f);
                dl.AddLine(centre - new Vector2(0f, r), centre + new Vector2(0f, r), Look.U32(colour with { W = 0.9f * alpha }), 2f);
                break;
            case Special.Prism:
                for (var i = 0; i < 6; i++)
                {
                    var a0 = (float)(now * 2.0) + (i * MathF.Tau / 6f);
                    var a1 = a0 + (MathF.Tau / 6f);
                    dl.PathArcTo(centre, r * 1.02f, a0, a1, 8);
                    dl.PathStroke(Look.U32(KindColours[i] with { W = 0.95f * alpha }), ImDrawFlags.None, 3f);
                }
                Look.Halo(dl, centre, r * 1.5f, Look.CrystalPale, 0.2f * glow);
                break;
        }
    }

    public static void AddImageRotated(ImDrawListPtr dl, ImTextureID handle, Vector2 centre, Vector2 half, float angle, uint tint)
    {
        if (MathF.Abs(angle) < 0.001f)
        {
            dl.AddImage(handle, centre - half, centre + half, Vector2.Zero, Vector2.One, tint);
            return;
        }
        var cos = MathF.Cos(angle);
        var sin = MathF.Sin(angle);
        Vector2 Rot(Vector2 p) => centre + new Vector2((p.X * cos) - (p.Y * sin), (p.X * sin) + (p.Y * cos));
        dl.AddImageQuad(handle,
            Rot(new Vector2(-half.X, -half.Y)), Rot(new Vector2(half.X, -half.Y)),
            Rot(new Vector2(half.X, half.Y)), Rot(new Vector2(-half.X, half.Y)),
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f), tint);
    }
}
