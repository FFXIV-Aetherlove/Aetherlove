using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using AetherLove.Shared.Aetherling;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;

namespace AetherOS.Apps.Racer.Screens;

/// <summary>The statistics page's artwork. Each piece is cropped to its painted bounds and drawn at that crop's own
/// aspect ratio, so a banner is never stretched to a box it was not painted for. A piece that carries text also names
/// its blank plate, the part of the painting the text is printed on. Every rectangle is (left, top, right, bottom) in
/// source pixels, measured off the delivered PNGs under <c>racer/stats/</c>; a re-delivered picture needs them
/// re-measured.</summary>
internal static class StatsArtwork
{
    public const string Title = "stats-title";
    public const string Scoreboard = "stats-scoreboard";
    public const string Section = "stats-section";
    public const string Party = "stats-party";
    public const string Practice = "stats-practice";
    public const string Cards = "stats-cards";
    public const string Star = "stats-star";
    public const string MedalGold = "stats-medal-gold";
    public const string MedalSilver = "stats-medal-silver";
    public const string MedalBronze = "stats-medal-bronze";

    private readonly record struct Piece(Vector2 Source, Vector4 Bounds, Vector4 Plate);

    private static readonly Vector2 BannerSource = new(1024, 341);
    private static readonly Vector2 BadgeSource = new(320, 320);

    // The three medals share one crop and one face, and the two mode banners share one crop, so a row of them
    // draws every piece at the same scale.
    private static readonly Vector4 MedalBounds = new(11, 34, 309, 272);
    private static readonly Vector4 MedalFace = new(102, 80, 218, 186);
    private static readonly Vector4 ModeBounds = new(7, 83, 1017, 253);

    private static readonly Dictionary<string, Piece> Pieces = new()
    {
        [Title] = new(BannerSource, new(13, 52, 1011, 296), new(215, 128, 810, 236)),
        [Scoreboard] = new(BannerSource, new(16, 53, 1009, 287), new(318, 98, 706, 260)),
        [Section] = new(BannerSource, new(28, 95, 996, 221), new(240, 124, 790, 200)),
        [Party] = new(BannerSource, ModeBounds, new(245, 108, 778, 234)),
        [Practice] = new(BannerSource, ModeBounds, new(240, 106, 782, 214)),
        [MedalGold] = new(BadgeSource, MedalBounds, MedalFace),
        [MedalSilver] = new(BadgeSource, MedalBounds, MedalFace),
        [MedalBronze] = new(BadgeSource, MedalBounds, MedalFace),
        [Cards] = new(BadgeSource, new(29, 44, 287, 279), default),
        [Star] = new(BadgeSource, new(17, 23, 303, 284), default),
        ["stats-element-neutral"] = new(BadgeSource, new(31, 30, 287, 285), default),
        ["stats-element-fire"] = new(BadgeSource, new(18, 15, 301, 297), default),
        ["stats-element-lightning"] = new(BadgeSource, new(14, 12, 306, 298), default),
        ["stats-element-wind"] = new(BadgeSource, new(14, 13, 305, 299), default),
        ["stats-element-ice"] = new(BadgeSource, new(20, 17, 298, 291), default),
        ["stats-element-water"] = new(BadgeSource, new(31, 30, 288, 282), default),
        ["stats-element-earth"] = new(BadgeSource, new(25, 24, 294, 289), default),
    };

    /// <summary>Width over height of the painted part of <paramref name="name"/>.</summary>
    public static float Aspect(string name)
    {
        var bounds = Pieces[name].Bounds;
        return (bounds.Z - bounds.X) / (bounds.W - bounds.Y);
    }

    /// <summary>The height <paramref name="name"/> draws at when it is <paramref name="width"/> wide.</summary>
    public static float HeightFor(string name, float width) => width / Aspect(name);

    /// <summary>Draws <paramref name="name"/> <paramref name="width"/> wide at its own aspect ratio. False when the
    /// picture has not downloaded yet, so the caller can draw a stand-in of the same size.</summary>
    public static bool Draw(OsAppContext ctx, IRacerHost host, string name, Vector2 at, float width)
    {
        var texture = ctx.Capabilities.Textures.Get(Path.Combine(host.PetAssetRoot, "racer", "stats", name + ".png"));
        if (texture is null)
        {
            return false;
        }

        var piece = Pieces[name];
        var size = new Vector2(width, HeightFor(name, width));
        ImGui.GetWindowDrawList().AddImage(texture.Value, at, at + size,
            new Vector2(piece.Bounds.X, piece.Bounds.Y) / piece.Source,
            new Vector2(piece.Bounds.Z, piece.Bounds.W) / piece.Source, 0xFFFFFFFF);
        return true;
    }

    /// <summary>Draws <paramref name="name"/> as large as fits inside <paramref name="box"/>, centred, and returns
    /// the rectangle it took.</summary>
    public static (Vector2 At, Vector2 Size) DrawInside(OsAppContext ctx, IRacerHost host, string name, Vector2 at, Vector2 box)
    {
        var width = MathF.Min(box.X, box.Y * Aspect(name));
        var size = new Vector2(width, HeightFor(name, width));
        var placed = at + ((box - size) / 2f);
        Draw(ctx, host, name, placed, width);
        return (placed, size);
    }

    /// <summary>The blank plate of <paramref name="name"/> when the piece is drawn at <paramref name="at"/>,
    /// <paramref name="width"/> wide: where its text goes.</summary>
    public static (Vector2 At, Vector2 Size) Plate(string name, Vector2 at, float width)
    {
        var piece = Pieces[name];
        var scale = width / (piece.Bounds.Z - piece.Bounds.X);
        var plateAt = at + (new Vector2(piece.Plate.X - piece.Bounds.X, piece.Plate.Y - piece.Bounds.Y) * scale);
        var plateSize = new Vector2(piece.Plate.Z - piece.Plate.X, piece.Plate.W - piece.Plate.Y) * scale;
        return (plateAt, plateSize);
    }

    public static string Element(AetherlingElement element) => element switch
    {
        AetherlingElement.Fire => "stats-element-fire",
        AetherlingElement.Lightning => "stats-element-lightning",
        AetherlingElement.Wind => "stats-element-wind",
        AetherlingElement.Ice => "stats-element-ice",
        AetherlingElement.Water => "stats-element-water",
        AetherlingElement.Earth => "stats-element-earth",
        _ => "stats-element-neutral",
    };
}
