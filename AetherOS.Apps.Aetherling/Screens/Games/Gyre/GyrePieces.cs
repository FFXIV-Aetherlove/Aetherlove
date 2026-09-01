using System;
using System.IO;
using System.Numerics;
using AetherLove.UI;
using AetherOS.Apps.Aetherling.Screens.Games.LumiLink;
using AetherOS.Apps.Aetherling.Ui;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Aetherling.Screens.Games.Gyre;

/// <summary>How a marble and a powerup token look, shared by the board and the guide. Kind order and
/// colours are LumiLink's six candy primaries, so an element reads the same in both games. Baked art
/// under `games/gyre/` wins outright when it exists; until then everything draws procedurally, which is
/// the shipped look while the art set is still owed.</summary>
internal static class GyrePieces
{
    public static readonly Vector4[] KindColours = LumiLinkPieces.KindColours;

    public static readonly string[] Elements = LumiLinkPieces.Elements;

    private static readonly FontAwesomeIcon[] TokenIcons =
    [
        FontAwesomeIcon.Crosshairs,
        FontAwesomeIcon.Feather,
        FontAwesomeIcon.UndoAlt,
        FontAwesomeIcon.Bomb,
        FontAwesomeIcon.LocationArrow,
        FontAwesomeIcon.Star,
    ];

    public static void Ellipse(ImDrawListPtr dl, Vector2 centre, Vector2 radii, uint colour, float thickness)
    {
        for (var i = 0; i < 28; i++)
        {
            var a = MathF.Tau * i / 28f;
            dl.PathLineTo(centre + new Vector2(MathF.Cos(a) * radii.X, MathF.Sin(a) * radii.Y));
        }
        dl.PathStroke(colour, ImDrawFlags.Closed, thickness);
    }

    public static void EllipseFilled(ImDrawListPtr dl, Vector2 centre, Vector2 radii, uint colour)
    {
        for (var i = 0; i < 28; i++)
        {
            var a = MathF.Tau * i / 28f;
            dl.PathLineTo(centre + new Vector2(MathF.Cos(a) * radii.X, MathF.Sin(a) * radii.Y));
        }
        dl.PathFillConvex(colour);
    }

    /// <summary>A marble, rolling. <paramref name="spin"/> is the distance it has travelled: turned into an
    /// angle by the ball's own radius, which is rolling without slipping, so a marble shoved backwards rolls
    /// backwards too.
    ///
    /// <para>What sells it is WHICH parts turn. The swirl is carried on the sphere's surface, so each mark
    /// is placed by longitude and latitude, hidden when it goes round the back, and squashed toward the rim
    /// by the cosine of its own longitude. The specular highlight does NOT turn: a real ball's catchlight
    /// belongs to the lamp, not to the ball, and pinning it is what makes the rest read as rotation rather
    /// than as a spinning picture.</para></summary>
    public static void Marble(OsAppContext ctx, ImDrawListPtr dl, string assetRoot, Vector2 centre,
        float size, int kind, bool dud, float alpha = 1f, float spin = 0f, GyrePowerup? power = null)
    {
        var file = dud ? "marble_dud.png" : $"marble_{kind}.png";
        var path = Path.Combine(assetRoot, "games", "gyre", file);
        if (ctx.Capabilities.Textures.Get(path) is { } handle)
        {
            // The sprite is a lit ball with a swirl on it, drawn to be turned: so turn it. This branch used
            // to return before any of the rolling below, which is why marbles with art shipped stationary
            // while the procedural fallback rolled.
            var half = size * 0.5f;
            var turn = spin / MathF.Max(1f, half * 0.92f);
            var cos = MathF.Cos(turn);
            var sin = MathF.Sin(turn);
            var ax = new Vector2(cos, sin) * half;
            var ay = new Vector2(-sin, cos) * half;
            var tint = Look.U32(new Vector4(1f, 1f, 1f, alpha));
            dl.AddImageQuad(handle,
                centre - ax - ay, centre + ax - ay, centre + ax + ay, centre - ax + ay,
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f), tint);
            DrawPowerRing(dl, centre, size, spin, alpha, power);
            return;
        }

        var r = size * 0.46f;
        if (dud)
        {
            var grey = new Vector4(0.42f, 0.42f, 0.47f, 1f);
            dl.AddCircleFilled(centre, r, Look.U32(grey, alpha), 28);
            dl.AddCircle(centre, r, Look.U32(new Vector4(0.2f, 0.2f, 0.24f, 0.8f), alpha), 28, 1.4f);
            dl.AddLine(centre + new Vector2(-r * 0.4f, -r * 0.2f), centre + new Vector2(r * 0.25f, r * 0.35f),
                Look.U32(new Vector4(0.25f, 0.25f, 0.3f, 0.7f), alpha), 1.6f);
            return;
        }

        var colour = KindColours[kind];
        var dark = Vector4.Lerp(colour, new Vector4(0f, 0f, 0f, 1f), 0.55f);
        var pale = Vector4.Lerp(colour, Vector4.One, 0.55f);

        dl.AddCircleFilled(centre, r, Look.U32(dark, alpha), 30);
        dl.AddCircleFilled(centre + new Vector2(-r * 0.05f, -r * 0.07f), r * 0.93f, Look.U32(colour, alpha), 30);

        // The marks are carried ON the sphere: each is placed by longitude and latitude, squashed along the
        // turning axis by the cosine of its own longitude, and dropped once it goes round the back. Their
        // orbit radius is 0.80 so a mark at the rim cannot spill past the ball's own edge.
        var theta = spin / MathF.Max(1f, r);
        foreach (var (lon, lat, scale, isPale) in Marks)
        {
            var phi = lon + theta;
            var facing = MathF.Cos(phi);
            if (facing <= 0.04f)
            {
                continue;
            }
            var at = centre + new Vector2(
                r * MarkOrbit * MathF.Sin(phi) * MathF.Cos(lat),
                r * MarkOrbit * MathF.Sin(lat));
            EllipseFilled(dl, at, new Vector2(MathF.Max(0.5f, r * scale * facing), r * scale),
                Look.U32(isPale ? pale : dark, alpha * (0.30f + (0.55f * facing))));
        }

        // Shade and catchlight belong to the LAMP, not to the ball, so neither turns: pinning them is what
        // makes the marks read as a sphere rotating rather than as a picture spinning. The shade is struck
        // as arcs inside the rim, which needs no mask and cannot spill.
        for (var i = 0; i < 3; i++)
        {
            var rad = r * (0.95f - (i * 0.10f));
            dl.PathArcTo(centre, rad, -0.11f * MathF.PI, 0.61f * MathF.PI, 20);
            dl.PathStroke(Look.U32(dark with { W = 0.14f - (i * 0.04f) }, alpha), ImDrawFlags.None,
                MathF.Max(1f, r * 0.12f));
        }

        dl.AddCircleFilled(centre + new Vector2(-r * 0.36f, -r * 0.40f), r * 0.20f,
            Look.U32(new Vector4(1f, 1f, 1f, 0.9f), alpha), 14);
        dl.AddCircleFilled(centre + new Vector2(-r * 0.46f, -r * 0.20f), r * 0.07f,
            Look.U32(new Vector4(1f, 1f, 1f, 0.5f), alpha), 10);
        dl.AddCircle(centre, r, Look.U32(dark with { W = 0.85f }, alpha), 30, MathF.Max(1f, r * 0.08f));

        DrawPowerRing(dl, centre, size, spin, alpha, power);
    }

    /// <summary>Where each mark sits on the ball before it turns: longitude, latitude, size as a share of
    /// the radius, and whether it is the light mark or the dark one. Four, spread a quarter turn apart, so
    /// one is always crossing the face and the ball never looks momentarily still.</summary>
    private static readonly (float Lon, float Lat, float Scale, bool Pale)[] Marks =
    [
        (0f, -0.34f, 0.30f, true),
        (1.57f, 0.30f, 0.24f, false),
        (3.14f, 0.44f, 0.22f, true),
        (4.71f, -0.16f, 0.26f, false),
    ];

    private const float MarkOrbit = 0.80f;

    /// <summary>A powerup riding in the chain: a turning gold collar and its glyph, over the marble's own
    /// colour, so it is unmistakably one of the line rather than a token loose on the board.</summary>
    private static void DrawPowerRing(ImDrawListPtr dl, Vector2 centre, float size, float spin, float alpha,
        GyrePowerup? power)
    {
        if (power is not { } kind)
        {
            return;
        }

        var r = size * 0.5f;
        Look.Halo(dl, centre, r * 1.5f, Look.Spark, 0.28f * alpha, 3);
        var turn = spin * 0.02f;
        for (var i = 0; i < 8; i++)
        {
            var a = (MathF.Tau * i / 8f) + turn;
            var b = a + (MathF.Tau / 22f);
            dl.PathArcTo(centre, r * 0.94f, a, b, 4);
            dl.PathStroke(Look.U32(new Vector4(0.98f, 0.84f, 0.38f, 0.95f), alpha), ImDrawFlags.None,
                MathF.Max(1.4f, r * 0.14f));
        }
        IconDraw.AddCentered(dl, TokenIcons[(int)kind], r * 0.78f, centre,
            Look.U32(new Vector4(1f, 0.97f, 0.86f, 0.98f), alpha));
    }
}
