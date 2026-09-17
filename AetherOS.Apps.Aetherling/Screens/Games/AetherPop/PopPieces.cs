using System;
using System.IO;
using System.Numerics;
using System.Text.Json;
using AetherLove.UI;
using AetherOS.PetKit.Engine;
using AetherOS.Apps.Aetherling.Screens.Games.Gyre;
using AetherOS.Apps.Aetherling.Ui;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Aetherling.Screens.Games.AetherPop;

/// <summary>How a bubble looks: Gyre's rolling marble for the six kinds, and the three specials and the
/// frost drawn over or instead of it. Shared by the board, the shooter, the drops and the guide, so a
/// star in the explainer is the star on the board.</summary>
internal static class PopPieces
{
    public static readonly Vector4[] KindColours = GyrePieces.KindColours;

    public static readonly string[] Elements = GyrePieces.Elements;

    private static readonly Vector4 Frost = new(0.72f, 0.9f, 1f, 1f);

    public static void Bubble(OsAppContext ctx, ImDrawListPtr dl, string assetRoot, Vector2 centre, float size,
        int kind, PopSpecial special, int letter, bool frozen, double now, float alpha = 1f, float spin = 0f)
    {
        switch (special)
        {
            case PopSpecial.Star:
                Star(dl, centre, size, now, alpha);
                break;
            case PopSpecial.Rainbow:
                Rainbow(dl, centre, size, now, alpha);
                break;
            case PopSpecial.Lumi:
                Baby(ctx, dl, assetRoot, centre, size, Math.Max(0, kind), now, alpha);
                break;
            default:
                GyrePieces.Marble(ctx, dl, assetRoot, centre, size, Math.Max(0, kind), false, alpha, spin);
                if (special == PopSpecial.Letter && letter >= 0 && letter < PopBoard.Letters.Length)
                {
                    LetterFace(dl, centre, size, letter, alpha);
                }
                break;
        }
        if (frozen)
        {
            Ice(dl, centre, size, now, alpha);
        }
    }

    /// <summary>A pale, lit ball with a turning five-point star: nothing else on the board is white.</summary>
    private static void Star(ImDrawListPtr dl, Vector2 centre, float size, double now, float alpha)
    {
        var r = size * 0.46f;
        var body = new Vector4(0.97f, 0.96f, 0.9f, 1f);
        var dark = new Vector4(0.7f, 0.66f, 0.5f, 1f);
        Look.Halo(dl, centre, r * 1.5f, Look.Spark, 0.22f * alpha, 3);
        dl.AddCircleFilled(centre, r, Look.U32(dark, alpha), 30);
        dl.AddCircleFilled(centre + new Vector2(-r * 0.05f, -r * 0.07f), r * 0.93f, Look.U32(body, alpha), 30);
        var turn = (float)(now * 0.9);
        for (var i = 0; i < 5; i++)
        {
            var a0 = turn + (MathF.Tau * i / 5f) - (MathF.PI * 0.5f);
            var a1 = a0 + (MathF.Tau / 10f);
            var a2 = a0 + (MathF.Tau / 5f);
            dl.PathLineTo(centre + new Vector2(MathF.Cos(a0), MathF.Sin(a0)) * (r * 0.62f));
            dl.PathLineTo(centre + new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * (r * 0.26f));
            dl.PathLineTo(centre + new Vector2(MathF.Cos(a2), MathF.Sin(a2)) * (r * 0.62f));
            dl.PathLineTo(centre);
            dl.PathFillConvex(Look.U32(Look.Spark, alpha));
        }
        dl.AddCircleFilled(centre + new Vector2(-r * 0.36f, -r * 0.40f), r * 0.18f,
            Look.U32(new Vector4(1f, 1f, 1f, 0.9f), alpha), 14);
        dl.AddCircle(centre, r, Look.U32(dark with { W = 0.85f }, alpha), 30, MathF.Max(1f, r * 0.08f));
    }

    /// <summary>A ball whose colour never settles: it walks the six kinds in a loop, which is the whole
    /// promise of the thing.</summary>
    private static void Rainbow(ImDrawListPtr dl, Vector2 centre, float size, double now, float alpha)
    {
        var r = size * 0.46f;
        var phase = (float)((now * 0.6) % 1.0) * 6f;
        var i = (int)phase;
        var f = phase - i;
        var colour = Vector4.Lerp(KindColours[i % 6], KindColours[(i + 1) % 6], f);
        var dark = Vector4.Lerp(colour, new Vector4(0f, 0f, 0f, 1f), 0.55f);
        dl.AddCircleFilled(centre, r, Look.U32(dark, alpha), 30);
        dl.AddCircleFilled(centre + new Vector2(-r * 0.05f, -r * 0.07f), r * 0.93f, Look.U32(colour, alpha), 30);
        // Six wedges of the six kinds turning inside, so it reads as every colour at once and not as a
        // seventh colour of its own.
        var turn = (float)(now * 1.4);
        for (var k = 0; k < 6; k++)
        {
            var a0 = turn + (MathF.Tau * k / 6f);
            var a1 = a0 + (MathF.Tau / 6f);
            dl.PathLineTo(centre);
            dl.PathArcTo(centre, r * 0.55f, a0, a1, 6);
            dl.PathFillConvex(Look.U32(KindColours[k] with { W = 0.75f }, alpha));
        }
        dl.AddCircleFilled(centre, r * 0.2f, Look.U32(new Vector4(1f, 1f, 1f, 0.85f), alpha), 14);
        dl.AddCircleFilled(centre + new Vector2(-r * 0.36f, -r * 0.40f), r * 0.18f,
            Look.U32(new Vector4(1f, 1f, 1f, 0.9f), alpha), 14);
        dl.AddCircle(centre, r, Look.U32(dark with { W = 0.85f }, alpha), 30, MathF.Max(1f, r * 0.08f));
    }

    private static AtlasManifest? _babyManifest;
    private static string? _babyRoot;

    /// <summary>The hatchling, asleep in a bubble of its colour: the form 1 sheets' rest cell, body and
    /// accent pulled to the kind's hue, drawn a little large so the blob fills the ball. Nothing else
    /// on the board has a face, which is what makes it read as a creature rather than a mark.</summary>
    public static void Baby(OsAppContext ctx, ImDrawListPtr dl, string assetRoot, Vector2 centre, float size,
        int kind, double now, float alpha)
    {
        var colour = KindColours[kind];
        var r = size * 0.46f;
        var dark = Vector4.Lerp(colour, new Vector4(0f, 0f, 0f, 1f), 0.55f);
        Look.Halo(dl, centre, r * 1.4f, colour, 0.22f * alpha, 3);
        dl.AddCircleFilled(centre, r, Look.U32(dark with { W = 0.55f }, alpha), 30);
        dl.AddCircle(centre, r, Look.U32(colour with { W = 0.85f }, alpha), 30, MathF.Max(1.2f, r * 0.09f));

        var manifest = BabyManifest(assetRoot);
        if (manifest is null)
        {
            dl.AddCircleFilled(centre, r * 0.6f, Look.U32(colour, alpha), 24);
            return;
        }
        var bob = MathF.Sin((float)now * 2.2f + centre.X * 0.01f) * r * 0.05f;
        var side = size * 1.7f;
        var min = centre + new Vector2(-side * 0.5f, -side * (190f / 256f) + bob);
        var max = min + new Vector2(side, side);
        var (u0, v0, u1, v1) = manifest.UvForCell(0);
        var uv0 = new Vector2(u0, v0);
        var uv1 = new Vector2(u1, v1);
        var pale = Vector4.Lerp(colour, Vector4.One, 0.45f);
        var eye = Vector4.Lerp(colour, new Vector4(0f, 0f, 0f, 1f), 0.7f);
        for (var i = 0; i < manifest.Layers.Count; i++)
        {
            var layer = manifest.Layers[i];
            var handle = ctx.Capabilities.Textures.Get(Path.Combine(assetRoot, "1", layer.File));
            if (handle is not { } tex)
            {
                continue;
            }
            var tint = layer.Role switch
            {
                TintRole.Body => colour,
                TintRole.Accent => pale,
                TintRole.Eye => eye,
                _ => Vector4.One,
            };
            tint.W = alpha * layer.Alpha;
            dl.AddImage(tex, min, max, uv0, uv1, Look.U32(tint));
        }
    }

    private static AtlasManifest? BabyManifest(string assetRoot)
    {
        if (_babyManifest is not null && _babyRoot == assetRoot)
        {
            return _babyManifest;
        }
        try
        {
            _babyManifest = JsonSerializer.Deserialize<AtlasManifest>(File.ReadAllText(Path.Combine(assetRoot, "1", "manifest.json")));
        }
        catch (Exception)
        {
            _babyManifest = null;
        }
        _babyRoot = assetRoot;
        return _babyManifest;
    }

    /// <summary>The letter on a letter bubble: a dark plate so it reads on every kind, and the glyph in
    /// the gold the collected row wears.</summary>
    private static void LetterFace(ImDrawListPtr dl, Vector2 centre, float size, int letter, float alpha)
    {
        var r = size * 0.46f;
        dl.AddCircleFilled(centre, r * 0.56f, Look.U32(new Vector4(0.05f, 0.04f, 0.08f, 0.8f), alpha), 20);
        dl.AddCircle(centre, r * 0.56f, Look.U32(Look.Spark, 0.7f * alpha), 20, MathF.Max(1f, r * 0.035f));
        var text = PopBoard.Letters[letter].ToString();
        // Sized by the ball, never by the UI font: a capital's ink is about 0.7 of the font size, and
        // it should fill a little over half the plate.
        var fontSize = MathF.Max(9f, r * 0.85f);
        var scale = fontSize / ImGui.GetFontSize();
        var width = ImGui.CalcTextSize(text).X * scale;
        var lineH = ImGui.GetTextLineHeight() * scale;
        // A capital's ink sits in the upper part of the line box (the descender space is below it), so
        // the box is placed a little high for the glyph to land on the centre.
        var at = new Vector2(centre.X - (width * 0.5f), centre.Y - (lineH * 0.5f) - (lineH * 0.06f));
        dl.AddText(ImGui.GetFont(), fontSize, at, Look.U32(Look.Spark, alpha), text);
    }

    /// <summary>Frost grown over a bubble: a pale rind, six crystal spokes and a glint that slides.</summary>
    public static void Ice(ImDrawListPtr dl, Vector2 centre, float size, double now, float alpha)
    {
        var r = size * 0.48f;
        dl.AddCircleFilled(centre, r, Look.U32(Frost with { W = 0.42f }, alpha), 30);
        dl.AddCircle(centre, r, Look.U32(new Vector4(1f, 1f, 1f, 0.9f), alpha), 30, MathF.Max(1.2f, r * 0.09f));
        for (var i = 0; i < 6; i++)
        {
            var a = MathF.Tau * i / 6f;
            var dir = new Vector2(MathF.Cos(a), MathF.Sin(a));
            dl.AddLine(centre + (dir * r * 0.15f), centre + (dir * r * 0.82f),
                Look.U32(new Vector4(1f, 1f, 1f, 0.75f), alpha), MathF.Max(1f, r * 0.06f));
            var tip = centre + (dir * r * 0.55f);
            var side = new Vector2(-dir.Y, dir.X);
            dl.AddLine(tip + (side * r * 0.16f) - (dir * r * 0.1f), tip, Look.U32(new Vector4(1f, 1f, 1f, 0.6f), alpha), 1f);
            dl.AddLine(tip - (side * r * 0.16f) - (dir * r * 0.1f), tip, Look.U32(new Vector4(1f, 1f, 1f, 0.6f), alpha), 1f);
        }
        var glint = (float)((now * 0.8) % 1.0);
        var gx = centre.X - r + (glint * r * 2f);
        dl.AddLine(new Vector2(gx, centre.Y - (r * 0.7f)), new Vector2(gx + (r * 0.3f), centre.Y + (r * 0.7f)),
            Look.U32(new Vector4(1f, 1f, 1f, 0.35f * (1f - MathF.Abs((glint * 2f) - 1f))), alpha), r * 0.12f);
    }

    /// <summary>The metal bubble the Earth power fires: steel, heavy, no colour to match.</summary>
    public static void Metal(ImDrawListPtr dl, Vector2 centre, float size, float spin, float alpha = 1f)
    {
        var r = size * 0.5f;
        var steel = new Vector4(0.62f, 0.66f, 0.72f, 1f);
        var dark = new Vector4(0.22f, 0.24f, 0.3f, 1f);
        dl.AddCircleFilled(centre, r, Look.U32(dark, alpha), 32);
        dl.AddCircleFilled(centre + new Vector2(-r * 0.06f, -r * 0.08f), r * 0.9f, Look.U32(steel, alpha), 32);
        for (var i = 0; i < 3; i++)
        {
            var a = spin * 0.02f + (MathF.Tau * i / 3f);
            var p = centre + new Vector2(MathF.Cos(a), MathF.Sin(a)) * (r * 0.5f);
            dl.AddCircleFilled(p, r * 0.12f, Look.U32(dark with { W = 0.5f }, alpha), 10);
        }
        dl.AddCircleFilled(centre + new Vector2(-r * 0.34f, -r * 0.38f), r * 0.22f,
            Look.U32(new Vector4(1f, 1f, 1f, 0.85f), alpha), 14);
        dl.AddCircle(centre, r, Look.U32(dark, alpha), 32, MathF.Max(1.2f, r * 0.1f));
    }

    public static void ElementIcon(OsAppContext ctx, ImDrawListPtr dl, string assetRoot, Vector2 centre, float side,
        int index, float alpha)
    {
        var icon = ctx.Capabilities.Textures.Get(System.IO.Path.Combine(assetRoot, "crystals", Elements[index] + ".png"));
        if (icon is { } handle)
        {
            var half = side * 0.5f;
            dl.AddImage(handle, centre - new Vector2(half), centre + new Vector2(half), Vector2.Zero, Vector2.One,
                Look.U32(new Vector4(1f, 1f, 1f, alpha)));
            return;
        }
        IconDraw.AddCentered(dl, FontAwesomeIcon.Gem, side * 0.6f, centre, Look.U32(KindColours[index], alpha));
    }
}
