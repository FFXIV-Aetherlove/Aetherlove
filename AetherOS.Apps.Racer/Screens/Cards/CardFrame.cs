using System;
using System.Numerics;
using AetherLove.Shared.Racing.Cards;
using AetherOS.Apps.Racer.Rendering;
using Dalamud.Bindings.ImGui;

namespace AetherOS.Apps.Racer.Screens.Cards;

/// <summary>The surface the vector frame draws on, in reference units (a 150 by 219 face), so one authored
/// frame serves every size.</summary>
internal interface ICardFrameCanvas
{
    void Fill(ReadOnlySpan<Vector2> points, Vector4 colour);

    void Stroke(ReadOnlySpan<Vector2> points, Vector4 colour, float width, bool closed);

    void Circle(Vector2 center, float radius, Vector4 colour, bool filled, float width = 1f);
}

/// <summary>An ImGui draw list as a frame canvas: <paramref name="origin"/> is the face's top-left in screen
/// pixels and <paramref name="unit"/> the device pixels per reference unit.</summary>
internal readonly struct DrawListCanvas(ImDrawListPtr drawList, Vector2 origin, float unit) : ICardFrameCanvas
{
    private const float MinStroke = 0.6f;
    private const int FineSegments = 48;
    private const int CoarseSegments = 16;
    private const float FineRadius = 12f;

    public void Fill(ReadOnlySpan<Vector2> points, Vector4 colour)
    {
        foreach (var point in points)
        {
            drawList.PathLineTo(origin + (point * unit));
        }

        drawList.PathFillConvex(ElementFx.U32(colour));
    }

    public void Stroke(ReadOnlySpan<Vector2> points, Vector4 colour, float width, bool closed)
    {
        foreach (var point in points)
        {
            drawList.PathLineTo(origin + (point * unit));
        }

        drawList.PathStroke(ElementFx.U32(colour), closed ? ImDrawFlags.Closed : ImDrawFlags.None, MathF.Max(MinStroke, width * unit));
    }

    public void Circle(Vector2 center, float radius, Vector4 colour, bool filled, float width = 1f)
    {
        var segments = radius > FineRadius ? FineSegments : CoarseSegments;
        if (filled)
        {
            drawList.AddCircleFilled(origin + (center * unit), radius * unit, ElementFx.U32(colour), segments);
        }
        else
        {
            drawList.AddCircle(origin + (center * unit), radius * unit, ElementFx.U32(colour), segments, MathF.Max(MinStroke, width * unit));
        }
    }
}

/// <summary>The five tones of one metal: the deep edge, the mid face, the bright highlight, the name plaque
/// and the lettering on it.</summary>
internal readonly record struct CardFramePalette(Vector4 Deep, Vector4 Mid, Vector4 Bright, Vector4 Plaque, Vector4 Lettering)
{
    public static CardFramePalette Silver => new(
        new Vector4(0.16f, 0.20f, 0.27f, 1f),
        new Vector4(0.52f, 0.62f, 0.72f, 1f),
        new Vector4(0.87f, 0.94f, 1f, 1f),
        new Vector4(0.77f, 0.82f, 0.87f, 1f),
        new Vector4(0.07f, 0.10f, 0.14f, 1f));

    public static CardFramePalette Gold => new(
        new Vector4(0.29f, 0.18f, 0.07f, 1f),
        new Vector4(0.74f, 0.54f, 0.24f, 1f),
        new Vector4(0.98f, 0.89f, 0.65f, 1f),
        new Vector4(0.87f, 0.76f, 0.52f, 1f),
        new Vector4(0.12f, 0.085f, 0.035f, 1f));

    public static CardFramePalette For(RaceCardBand band) => band == RaceCardBand.Gold ? Gold : Silver;
}

/// <summary>What each element looks like on a card: the accent the enamel and the crystal take, and the
/// crystal shape cut into the top-left socket.</summary>
internal static class CardElementStyle
{
    public const string Ember = "ember";
    public const string Droplet = "droplet";
    public const string Feather = "feather";
    public const string Facet = "facet";
    public const string Star = "star";
    public const string Prism = "prism";

    private static readonly Vector4 Fire = Rgb(0xF06A4F);
    private static readonly Vector4 Lightning = Rgb(0xF1D36D);
    private static readonly Vector4 Wind = Rgb(0x89C76F);
    private static readonly Vector4 Ice = Rgb(0x8ECDE8);
    private static readonly Vector4 Water = Rgb(0x46A8C8);
    private static readonly Vector4 Earth = Rgb(0xC29B62);
    private static readonly Vector4 Neutral = new(0.66f, 0.70f, 0.85f, 1f);

    public static Vector4 Accent(string element) => element switch
    {
        "fire" => Fire,
        "lightning" => Lightning,
        "wind" => Wind,
        "ice" => Ice,
        "water" => Water,
        "earth" => Earth,
        _ => Neutral,
    };

    public static string CrystalKind(string element) => element switch
    {
        "fire" => Ember,
        "lightning" => Star,
        "wind" => Feather,
        "ice" => Prism,
        "water" => Droplet,
        "earth" => Facet,
        _ => Prism,
    };

    private static Vector4 Rgb(uint rgb) => new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
}

/// <summary>The vector frame every racing card wears, authored in reference units. <see cref="Base"/> goes
/// under the art, <see cref="Trim"/> over it; <see cref="Back"/> is the one common back that discloses
/// nothing. The level lights the resonance sockets one by one, adds the side rails from level 2 and the
/// chased shoulders at the top level; the foil and halo live on the face renderer because they read the
/// pointer.</summary>
internal static class CardFrameDesign
{
    public static readonly Vector4 Paper = new(0.075f, 0.083f, 0.105f, 1f);
    public static readonly Vector4 BackAccent = new(0.66f, 0.70f, 0.85f, 1f);
    public static readonly Vector2 CrystalCenter = new(21f, 20f);
    public static readonly Vector2 RankCenter = new(83f, CardFaceLayout.FooterY);
    public const float CrystalRadius = 7f;
    public const float RankFontSize = 9.5f;
    public const int SocketCount = 3;
    private const float SocketX = 111f;
    private const float SocketPitch = 11f;
    private const float EnamelBlend = 0.18f;
    private const int RailLevel = 2;
    private const int ShoulderLevel = RaceCardLevels.MaxLevel;

    private static readonly Vector2[][] Crystals =
    [
        [new(0f, -1f), new(.62f, -.05f), new(.4f, .8f), new(0f, 1f), new(-.4f, .8f), new(-.62f, -.05f)],
        [new(0f, -1f), new(.46f, -.15f), new(.62f, .35f), new(.32f, .88f), new(0f, 1f), new(-.32f, .88f), new(-.62f, .35f), new(-.46f, -.15f)],
        [new(0f, -1f), new(.45f, -.1f), new(.2f, .9f), new(0f, 1f), new(-.2f, .9f), new(-.45f, -.1f)],
        [new(0f, -1f), new(.68f, -.48f), new(.68f, .48f), new(0f, 1f), new(-.68f, .48f), new(-.68f, -.48f)],
        [new(0f, -1f), new(.58f, -.1f), new(.3f, .85f), new(0f, 1f), new(-.3f, .85f), new(-.58f, -.1f)],
    ];

    /// <summary>Whether a level carries the foil sheen over the art.</summary>
    public static bool HasFoil(int level) => level >= RailLevel;

    /// <summary>Whether a level carries the outer halo while the card is active.</summary>
    public static bool HasHalo(int level) => level >= RaceCardLevels.MaxLevel;

    public static void Base<T>(ref T c, CardFaceLayout layout, CardFramePalette metal, Vector4 accent, int level)
        where T : ICardFrameCanvas
    {
        Plate(ref c, new(0f, 2f), new(150f, 221f), 7f, new(0f, 0f, 0f, .28f));
        Plate(ref c, Vector2.Zero, new(150f, 219f), 7f, metal.Deep);
        Plate(ref c, new(1.5f), new(148.5f, 217.5f), 5.5f, metal.Mid);
        Plate(ref c, new(3.8f), new(146.2f, 215.2f), 3.5f, Paper);
        Line(ref c, new(7f, 1.1f), new(143f, 1.1f), metal.Bright with { W = .8f }, .8f);
        Line(ref c, new(1.1f, 7f), new(1.1f, 212f), metal.Bright with { W = .48f }, .8f);
        Line(ref c, new(7f, 217.6f), new(143f, 217.6f), metal.Deep, 1.2f);
        Line(ref c, new(148.5f, 7f), new(148.5f, 212f), metal.Deep, 1.2f);
        var enamel = Vector4.Lerp(Paper, accent, EnamelBlend);
        Plate(ref c, new(4.3f, 4.3f), new(145.7f, 214.7f), 3f, enamel);
        Plate(ref c, new(5.5f, layout.ArtBottom + 1f), new(144.5f, 214f), 2f, Paper);
        if (level >= RailLevel)
        {
            Line(ref c, new(3f, 15f), new(3f, 199f), metal.Bright with { W = .60f }, .65f);
            Line(ref c, new(147f, 15f), new(147f, 199f), metal.Bright with { W = .35f }, .65f);
        }
    }

    public static void Trim<T>(ref T c, CardFaceLayout layout, CardFramePalette metal, Vector4 accent, int level, bool detail)
        where T : ICardFrameCanvas
    {
        Outline(ref c, layout.ArtMin - new Vector2(.6f), layout.ArtMax + new Vector2(.6f), 0f, metal.Deep, 1.2f);
        Line(ref c, layout.ArtMin, new(144f, 6f), new(0f, 0f, 0f, .48f), .8f);
        Line(ref c, new(144f, 6f), layout.ArtMax, metal.Bright with { W = .28f }, .7f);
        Line(ref c, new(6f, layout.ArtBottom), layout.ArtMax, metal.Bright with { W = .55f }, .8f);

        var nameMin = layout.NameMin;
        var nameMax = layout.NameMax;
        Plate(ref c, nameMin + new Vector2(0f, 1.4f), nameMax + new Vector2(0f, 1.4f), 4f, new(0f, 0f, 0f, .48f));
        Plate(ref c, nameMin, nameMax, 4f, metal.Deep);
        Plate(ref c, nameMin + new Vector2(1.1f), nameMax - new Vector2(1.1f), 3f, metal.Plaque);
        Line(ref c, nameMin + new Vector2(5f, 1.5f), new(139f, layout.NameTop + 1.5f), metal.Bright, .8f);
        Line(ref c, new(11f, layout.NameBottom - 1.4f), nameMax - new Vector2(5f, 1.4f), metal.Mid, .9f);

        Plate(ref c, new(9f, 8f), new(33f, 33f), 5f, new(0f, 0f, 0f, .50f));
        Plate(ref c, new(9f, 7f), new(33f, 32f), 5f, metal.Mid);
        Plate(ref c, new(10.3f, 8.3f), new(31.7f, 30.7f), 4f, Paper);
        Line(ref c, new(15f, 8f), new(27f, 8f), metal.Bright with { W = .70f }, .7f);

        Plate(ref c, new(8f, 200f), new(142f, 212f), 3f, new(.045f, .051f, .067f, 1f));
        Line(ref c, new(12f, 200f), new(138f, 200f), metal.Mid with { W = .40f }, .6f);
        for (var i = 0; i < SocketCount; i++)
        {
            var at = new Vector2(SocketX + (i * SocketPitch), CardFaceLayout.FooterY);
            Diamond(ref c, at, 3.7f, metal.Deep);
            Diamond(ref c, at, 2.6f, new(.035f, .043f, .062f, 1f));
            if (level > i)
            {
                Diamond(ref c, at, 1.85f, i == SocketCount - 1 ? metal.Bright : accent);
                Line(ref c, at + new Vector2(-1.4f, 0f), at + new Vector2(0f, -1.4f), metal.Bright with { W = .8f }, .6f);
            }
        }

        if (detail || level >= ShoulderLevel)
        {
            for (var side = -1; side <= 1; side += 2)
            {
                var x = side < 0 ? 8f : 142f;
                var direction = -side;
                Line(ref c, new(x, 36f), new(x, 49f), metal.Bright with { W = .48f }, .65f);
                Line(ref c, new(x, 49f), new(x + (direction * 3f), 53f), metal.Mid, .65f);
                Line(ref c, new(x, layout.ArtBottom - 18f), new(x, layout.ArtBottom - 9f), metal.Mid, .65f);
                Line(ref c, new(x, layout.ArtBottom - 9f), new(x + (direction * 4f), layout.ArtBottom - 5f), metal.Bright with { W = .42f }, .65f);
            }
        }
    }

    public static void Crystal<T>(ref T c, Vector2 at, float radius, string kind, Vector4 accent)
        where T : ICardFrameCanvas
    {
        if (kind == CardElementStyle.Star)
        {
            Diamond(ref c, at, radius, accent);
            c.Fill([at + new Vector2(-radius, 0f), at + new Vector2(0f, -radius * .28f), at + new Vector2(radius, 0f), at + new Vector2(0f, radius * .28f)], Vector4.Lerp(accent, Vector4.One, .27f));
            return;
        }

        var shape = Crystals[kind switch
        {
            CardElementStyle.Ember => 0,
            CardElementStyle.Droplet => 1,
            CardElementStyle.Feather => 2,
            CardElementStyle.Facet => 3,
            _ => 4,
        }];
        Span<Vector2> points = stackalloc Vector2[8];
        for (var i = 0; i < shape.Length; i++)
        {
            points[i] = at + (shape[i] * radius);
        }

        c.Fill(points[..shape.Length], Vector4.Lerp(accent, Paper, .28f) with { W = accent.W });
        var hub = at + new Vector2(-radius * .14f, radius * .12f);
        c.Fill([points[0], points[1], hub], Vector4.Lerp(accent, Vector4.One, .32f) with { W = accent.W });
        c.Fill([points[0], hub, points[shape.Length - 1]], accent);
        c.Stroke(points[..shape.Length], accent, .7f, true);
        Line(ref c, points[0], hub, Vector4.One with { W = accent.W * .40f }, .6f);
    }

    /// <summary>The common back: no identity, band or element goes in, and it stays symmetric under a half
    /// turn.</summary>
    public static void Back<T>(ref T c)
        where T : ICardFrameCanvas
    {
        var metal = CardFramePalette.Silver;
        var midnight = new Vector4(.045f, .075f, .14f, 1f);
        Plate(ref c, Vector2.Zero, new(150f, 219f), 7f, metal.Deep);
        Outline(ref c, new(1.5f), new(148.5f, 217.5f), 5.5f, metal.Mid, 1.2f);
        Plate(ref c, new(4f), new(146f, 215f), 4f, new(.11f, .17f, .25f, 1f));
        Plate(ref c, new(7f), new(143f, 212f), 3f, midnight);
        Outline(ref c, new(8f), new(142f, 211f), 3f, new(0f, 0f, 0f, .45f), 2f);
        Outline(ref c, new(5f), new(145f, 214f), 3f, BackAccent with { W = .34f }, .7f);
        var center = new Vector2(75f, 109.5f);
        for (var i = 0; i < 4; i++)
        {
            c.Circle(center, 61f - (i * 7f), new(.27f, .40f, .60f, .035f), true);
        }

        for (var sign = -1; sign <= 1; sign += 2)
        {
            for (var row = 0; row < 3; row++)
            {
                var y = 59f + (row * 12f);
                c.Stroke([center + (new Vector2(-52f, y - 8f) * sign), center + (new Vector2(-27f, y) * sign),
                    center + (new Vector2(0f, y - 5f) * sign), center + (new Vector2(27f, y) * sign),
                    center + (new Vector2(52f, y - 8f) * sign)], BackAccent with { W = .10f }, .7f, false);
            }

            Diamond(ref c, center + new Vector2(0f, 88f * sign), 4f, BackAccent with { W = .32f });
        }

        for (var i = 0; i < 6; i++)
        {
            var angle = i * MathF.PI / 3f;
            var f = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            var s = new Vector2(-f.Y, f.X);
            c.Fill([center + (f * 28f), center + (f * 41f) - (s * 8f), center + (f * 54f), center + (f * 41f) + (s * 8f)], BackAccent with { W = .065f });
            c.Stroke([center + (f * 31f), center + (f * 41f) + (s * 6f), center + (f * 51f), center + (f * 41f) - (s * 6f)], BackAccent with { W = .30f }, .65f, true);
        }

        c.Circle(center, 33f, new(0f, 0f, 0f, .35f), true);
        c.Circle(center, 30f, metal.Mid, true);
        c.Circle(center, 28.8f, midnight, true);
        c.Circle(center, 26.5f, BackAccent with { W = .30f }, false, .6f);
        for (var sign = -1; sign <= 1; sign += 2)
        {
            var p = center + (new Vector2(0f, -12f) * sign);
            c.Circle(p - (new Vector2(0f, 1f) * sign), 10f, BackAccent with { W = .83f }, true);
            c.Fill([p + (new Vector2(-2f, -9f) * sign), p + (new Vector2(2f, -15f) * sign), p + (new Vector2(4f, -9f) * sign)], BackAccent with { W = .83f });
            for (var side = -1; side <= 1; side += 2)
            {
                c.Stroke([p + (new Vector2((side * 4f) - 1.5f, 1f) * sign), p + (new Vector2(side * 4f, 2f) * sign), p + (new Vector2((side * 4f) + 1.5f, 1f) * sign)], midnight, 1f, false);
            }
        }

        Diamond(ref c, center, 2f, metal.Bright);
        for (var side = -1; side <= 1; side += 2)
        {
            for (var end = -1; end <= 1; end += 2)
            {
                var p = center + new Vector2(side * 59f, end * 92.5f);
                c.Stroke([p + new Vector2(0f, -end * 18f), p, p + new Vector2(-side * 18f, 0f)], BackAccent with { W = .48f }, .9f, false);
                Diamond(ref c, p - new Vector2(side * 5f, end * 5f), 2.2f, BackAccent with { W = .55f });
            }
        }
    }

    /// <summary>A pointer-stationary light on the hovered back.</summary>
    public static void BackLight<T>(ref T c, Vector2 pointer)
        where T : ICardFrameCanvas
    {
        var offset = Vector2.Clamp(pointer, -Vector2.One, Vector2.One) * 5f;
        var center = new Vector2(75f, 109.5f) + offset;
        c.Circle(center, 23f, new(.74f, .86f, 1f, .055f), true);
        c.Circle(center, 17f, new(.85f, .92f, 1f, .035f), true);
    }

    public static void Diamond<T>(ref T c, Vector2 at, float r, Vector4 colour)
        where T : ICardFrameCanvas
    {
        c.Fill([at - new Vector2(0f, r), at + new Vector2(r, 0f), at + new Vector2(0f, r), at - new Vector2(r, 0f)], colour);
    }

    private static void Line<T>(ref T c, Vector2 a, Vector2 b, Vector4 colour, float width)
        where T : ICardFrameCanvas
    {
        c.Stroke([a, b], colour, width, false);
    }

    private static void Plate<T>(ref T c, Vector2 a, Vector2 b, float cut, Vector4 colour)
        where T : ICardFrameCanvas
    {
        if (cut <= 0f)
        {
            c.Fill([a, new(b.X, a.Y), b, new(a.X, b.Y)], colour);
            return;
        }

        Span<Vector2> points = stackalloc Vector2[8];
        Corners(a, b, cut, points);
        c.Fill(points, colour);
    }

    private static void Outline<T>(ref T c, Vector2 a, Vector2 b, float cut, Vector4 colour, float width)
        where T : ICardFrameCanvas
    {
        if (cut <= 0f)
        {
            c.Stroke([a, new(b.X, a.Y), b, new(a.X, b.Y)], colour, width, true);
            return;
        }

        Span<Vector2> points = stackalloc Vector2[8];
        Corners(a, b, cut, points);
        c.Stroke(points, colour, width, true);
    }

    private static void Corners(Vector2 a, Vector2 b, float cut, Span<Vector2> points)
    {
        points[0] = new(a.X + cut, a.Y);
        points[1] = new(b.X - cut, a.Y);
        points[2] = new(b.X, a.Y + cut);
        points[3] = new(b.X, b.Y - cut);
        points[4] = new(b.X - cut, b.Y);
        points[5] = new(a.X + cut, b.Y);
        points[6] = new(a.X, b.Y - cut);
        points[7] = new(a.X, a.Y + cut);
    }
}
