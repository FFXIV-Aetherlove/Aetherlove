using System;
using System.Numerics;
using AetherLove.UI;
using AetherOS.Apps.Aetherling.Engine;
using AetherOS.Apps.Aetherling.Ui;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Aetherling.Screens.Games;

/// <summary>The pieces all three games share: resolving pose cells by animation name, the pastel skies,
/// the little crystal and cloud shapes, and the mapping between screen points and a ParticleFx pool's
/// 256 design space.</summary>
internal static class GameScene
{
    /// <summary>A cell index picked from a named clip by fraction. Frame LISTS are per-form data (the
    /// baby's blink borrows cells the adult's does not), so games speak in names and fractions and never
    /// in literal indices.</summary>
    public static int Cell(AtlasManifest manifest, string animation, float fraction)
    {
        if (!manifest.Animations.TryGetValue(animation, out var def) || def.Frames.Length == 0)
        {
            return 0;
        }
        var index = (int)(Math.Clamp(fraction, 0f, 0.999f) * def.Frames.Length);
        return def.Frames[index];
    }

    /// <summary>A sky falling from one colour into another with a soft bloom, the games' stage floor.
    /// Both ends are given fully opaque so the phone behind can never bleed through.</summary>
    public static void Sky(ImDrawListPtr dl, Vector2 origin, Vector2 size, Vector4 top, Vector4 bottom, Vector4 bloom)
    {
        var t = Look.U32(top with { W = 1f });
        var b = Look.U32(bottom with { W = 1f });
        dl.AddRectFilledMultiColor(origin, origin + size, t, t, b, b);
        Look.Halo(dl, new Vector2(origin.X + (size.X * 0.5f), origin.Y + (size.Y * 0.2f)),
            size.X * 0.8f, bloom, 0.06f, 6);
    }

    /// <summary>A puffy cloud: a flat pill of a base with three lobes on top, each lobe carrying its own
    /// brighter cap so the shape reads lit from above instead of a smudge of circles. An art override wins
    /// outright when the player has shipped one.</summary>
    public static void Cloud(ImDrawListPtr dl, Vector2 centre, float halfWidth, Vector4 colour, float alpha,
        ImTextureID? art = null)
    {
        if (art is { } texture)
        {
            var half = new Vector2(halfWidth * 1.1f, halfWidth * 0.62f);
            dl.AddImage(texture, centre - half, centre + half, Vector2.Zero, Vector2.One,
                Look.U32(new Vector4(1f, 1f, 1f, Math.Clamp(alpha * 1.6f, 0f, 1f))));
            return;
        }

        var r = halfWidth * 0.9f;
        var body = colour with { W = alpha };
        var cap = Vector4.Lerp(colour, new Vector4(1f, 1f, 1f, 1f), 0.5f) with { W = alpha };
        Look.Halo(dl, centre + new Vector2(0f, r * 0.2f), halfWidth * 1.5f, colour, alpha * 0.3f, 3);

        // The flat base, a pill so the underside reads as something to stand on.
        var baseTL = centre + new Vector2(-halfWidth, -r * 0.16f);
        var baseBR = centre + new Vector2(halfWidth, r * 0.4f);
        dl.AddRectFilled(baseTL, baseBR, Look.U32(body), (baseBR.Y - baseTL.Y) * 0.5f);

        Span<(Vector2 At, float R)> lobes =
        [
            (centre + new Vector2(-halfWidth * 0.5f, -r * 0.3f), r * 0.42f),
            (centre + new Vector2(halfWidth * 0.02f, -r * 0.46f), r * 0.5f),
            (centre + new Vector2(halfWidth * 0.52f, -r * 0.26f), r * 0.38f),
        ];
        foreach (var (at, radius) in lobes)
        {
            dl.AddCircleFilled(at, radius, Look.U32(body), 26);
        }
        foreach (var (at, radius) in lobes)
        {
            dl.AddCircleFilled(at + new Vector2(-radius * 0.18f, -radius * 0.22f), radius * 0.62f,
                Look.U32(cap, alpha * 0.85f), 22);
        }
    }

    /// <summary>A four-point crystal with a rim and a glint; the games' collectible.</summary>
    public static void Crystal(ImDrawListPtr dl, Vector2 centre, float size, Vector4 accent, float alpha = 1f)
    {
        Look.Halo(dl, centre, size * 2.6f, accent, 0.18f * alpha, 3);
        dl.PathLineTo(centre + new Vector2(0f, -size));
        dl.PathLineTo(centre + new Vector2(size * 0.68f, 0f));
        dl.PathLineTo(centre + new Vector2(0f, size));
        dl.PathLineTo(centre + new Vector2(-size * 0.68f, 0f));
        dl.PathFillConvex(Look.U32(accent, 0.92f * alpha));
        dl.PathLineTo(centre + new Vector2(0f, -size));
        dl.PathLineTo(centre + new Vector2(size * 0.68f, 0f));
        dl.PathLineTo(centre + new Vector2(0f, size));
        dl.PathLineTo(centre + new Vector2(-size * 0.68f, 0f));
        dl.PathStroke(Look.U32(Look.CrystalPale, 0.8f * alpha), ImDrawFlags.Closed, MathF.Max(1f, size * 0.1f));
        dl.AddCircleFilled(centre + new Vector2(-size * 0.18f, -size * 0.3f), size * 0.16f,
            Look.U32(Look.CrystalPale, 0.9f * alpha), 10);
    }

    /// <summary>A grey dud puff: three fuzzy discs and two sleepy dots for eyes, so even the thing to
    /// avoid stays friendly.</summary>
    public static void Puff(ImDrawListPtr dl, Vector2 centre, float size, float alpha = 1f)
    {
        var grey = new Vector4(0.52f, 0.53f, 0.60f, 1f);
        dl.AddCircleFilled(centre + new Vector2(-size * 0.4f, size * 0.1f), size * 0.62f, Look.U32(grey, 0.5f * alpha), 18);
        dl.AddCircleFilled(centre + new Vector2(size * 0.4f, size * 0.12f), size * 0.6f, Look.U32(grey, 0.5f * alpha), 18);
        dl.AddCircleFilled(centre, size * 0.78f, Look.U32(grey, 0.78f * alpha), 22);
        var ink = Look.U32(new Vector4(0.18f, 0.18f, 0.24f, 1f), alpha);
        dl.AddCircleFilled(centre + new Vector2(-size * 0.24f, -size * 0.08f), size * 0.09f, ink, 8);
        dl.AddCircleFilled(centre + new Vector2(size * 0.24f, -size * 0.08f), size * 0.09f, ink, 8);
    }

    /// <summary>Maps a screen point into a ParticleFx pool's 256 design space, for pools whose Draw is
    /// fed <paramref name="fxBottomCentre"/> and <paramref name="fxSize"/> every frame.</summary>
    public static Vector2 FxPoint(Vector2 screen, Vector2 fxBottomCentre, float fxSize)
    {
        var ds = fxSize / 256f;
        return ((screen - fxBottomCentre) / ds) + new Vector2(128f, 256f);
    }

    /// <summary>The score readout floating over a run: a soft pill top centre, and a smaller aside
    /// under it when there is a second number worth showing.</summary>
    public static void Hud(ImDrawListPtr dl, GameStage stage, string main, string? aside, Vector4 accent)
    {
        var centreX = stage.Origin.X + (stage.Size.X * 0.5f);
        var h = Look.Pill(dl, main, centreX, stage.Origin.Y + Px(10f), accent, 0.95f, 1.05f);
        if (aside is { Length: > 0 })
        {
            Look.Centred(dl, aside, centreX, stage.Origin.Y + Px(12f) + h, Look.U32(Look.Whisper), 0.85f);
        }
    }

    /// <summary>The steering reminder at the start of a run: translucent A and D key caps with their
    /// arrows, flanking the play area, holding for the first seconds and then slowly fading away.</summary>
    public static void KeyGuide(ImDrawListPtr dl, GameStage stage, float elapsed)
    {
        const float HoldSeconds = 3f;
        const float FadeSeconds = 2.5f;
        var alpha = elapsed < HoldSeconds ? 1f : 1f - ((elapsed - HoldSeconds) / FadeSeconds);
        if (alpha <= 0f)
        {
            return;
        }
        alpha *= 0.7f;

        // At the foot of the stage, where hands and eyes already are; high enough to clear nothing.
        var y = stage.Origin.Y + stage.Size.Y - Px(46f);
        KeyCap(dl, new Vector2(stage.Origin.X + (stage.Size.X * 0.2f), y), "A",
            FontAwesomeIcon.ArrowLeft, leftArrow: true, alpha);
        KeyCap(dl, new Vector2(stage.Origin.X + (stage.Size.X * 0.8f), y), "D",
            FontAwesomeIcon.ArrowRight, leftArrow: false, alpha);
    }

    private static void KeyCap(ImDrawListPtr dl, Vector2 centre, string letter, FontAwesomeIcon arrow,
        bool leftArrow, float alpha)
    {
        var side = Px(34f);
        var half = side * 0.5f;
        var tl = centre - new Vector2(half, half);
        var br = centre + new Vector2(half, half);
        dl.AddRectFilled(tl, br, Look.U32(new Vector4(1f, 1f, 1f, 0.1f * alpha)), Px(8f));
        dl.AddRect(tl, br, Look.U32(Look.CrystalPale, 0.6f * alpha), Px(8f), ImDrawFlags.None, Px(1.4f));
        Look.Centred(dl, letter, centre.X, centre.Y - (ImGui.GetTextLineHeight() * 0.5f),
            Look.U32(Look.CrystalPale, alpha));
        var arrowX = leftArrow ? tl.X - Px(16f) : br.X + Px(16f);
        IconDraw.AddCentered(dl, arrow, Px(13f), new Vector2(arrowX, centre.Y),
            Look.U32(Look.CrystalPale, 0.85f * alpha));
    }

    /// <summary>Hearts for the catch game, drawn right-aligned; a lost heart keeps its slot as a faint
    /// outline so the player sees what is gone, not just what is left.</summary>
    public static void Hearts(ImDrawListPtr dl, GameStage stage, int alive, int total)
    {
        var size = Px(14f);
        var gap = Px(6f);
        var y = stage.Origin.Y + Px(14f);
        // Clear of the mute chip, which sits in this same corner for the whole run.
        var right = stage.Origin.X + stage.Size.X - GamesScreen.CornerReserve;
        for (var i = 0; i < total; i++)
        {
            var centre = new Vector2(right - (size * 0.5f) - ((total - 1 - i) * (size + gap)), y + (size * 0.5f));
            var full = i < alive;
            var colour = new Vector4(1f, 0.55f, 0.65f, full ? 1f : 0.22f);
            IconDraw.AddCentered(dl, FontAwesomeIcon.Heart, size, centre, Look.U32(colour));
        }
    }
}
