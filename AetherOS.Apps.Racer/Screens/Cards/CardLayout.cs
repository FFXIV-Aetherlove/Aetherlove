using System;
using System.Numerics;

namespace AetherOS.Apps.Racer.Screens.Cards;

/// <summary>A card face in reference units: 150 wide by 219 tall, the 1:1.46 every face keeps at every size.
/// The art window ends at <see cref="ArtBottom"/>; the name plaque sits between <see cref="NameTop"/> and
/// <see cref="NameBottom"/>; the effect lines run from there to <see cref="BodyBottom"/>.</summary>
internal readonly record struct CardFaceLayout(float ArtBottom, float NameTop, float NameBottom)
{
    public const float Width = 150f;
    public const float Height = 219f;
    public const float Ratio = Height / Width;
    public const float BodyBottom = 197f;
    public const float FooterY = 206f;
    private const float WindowInset = 6f;
    private const float GalleryArtBottom = 127f;
    private const float GalleryNameTop = 130f;
    private const float GalleryNameBottom = 157f;
    private const float DetailArtShare = 0.61f;
    private const float NamePlaqueGap = 3f;
    private const float NamePlaqueHeight = 21f;
    private const float BodyGap = 5f;
    private const float LineSpacing = 1.2f;
    private const int MaxBodyLines = 4;

    public Vector2 ArtMin => new(WindowInset, WindowInset);

    public Vector2 ArtMax => new(Width - WindowInset, ArtBottom);

    /// <summary>The name plaque's top left: as wide as the art window, from <see cref="NameTop"/>.</summary>
    public Vector2 NameMin => new(WindowInset, NameTop);

    /// <summary>The name plaque's bottom right.</summary>
    public Vector2 NameMax => new(Width - WindowInset, NameBottom);

    public float NameCenter => (NameTop + NameBottom) * 0.5f;

    public float BodyTop => NameBottom + BodyGap;

    /// <summary>The gallery face: the art window over the name plaque, with the effect lines under it. The plaque
    /// is tall enough for a name at the card text floor on the narrowest grid tile the Album lays out. How many
    /// effect lines fit depends on the drawn width (<see cref="BodyLines"/>); at the Album's default tile width
    /// that is one line at the card text floor.</summary>
    public static CardFaceLayout Gallery => new(GalleryArtBottom, GalleryNameTop, GalleryNameBottom);

    /// <summary>The inspect face trades body room for a taller art window at its larger text size.</summary>
    public static CardFaceLayout Detail
    {
        get
        {
            var art = Height * DetailArtShare;
            return new CardFaceLayout(art, art + NamePlaqueGap, art + NamePlaqueGap + NamePlaqueHeight);
        }
    }

    public static CardFaceLayout For(bool detail) => detail ? Detail : Gallery;

    /// <summary>How many effect lines fit under the plaque at <paramref name="fontPx"/> for a face drawn at
    /// <paramref name="unit"/> device pixels per reference unit.</summary>
    public int BodyLines(float unit, float fontPx)
    {
        if (unit <= 0f || fontPx <= 0f)
        {
            return 0;
        }

        return Math.Clamp((int)MathF.Floor((BodyBottom - BodyTop) * unit / (fontPx * LineSpacing)), 0, MaxBodyLines);
    }
}

/// <summary>Pure layout maths the Album shares with its gallery and inspect faces.</summary>
internal static class CardLayout
{
    private const float PointerSlackShare = 0.015f;

    /// <summary>Inclusive first row and exclusive end row for the rows a scrolled grid can show; nothing is
    /// submitted for the rest.</summary>
    public static (int First, int End) VisibleRows(int count, int columns, float rowPitch, float scrollY, float viewportHeight)
    {
        if (count <= 0 || columns <= 0 || rowPitch <= 0f || viewportHeight <= 0f)
        {
            return (0, 0);
        }

        var rows = (count + columns - 1) / columns;
        var first = Math.Clamp((int)MathF.Floor(scrollY / rowPitch), 0, rows);
        var end = Math.Clamp((int)MathF.Ceiling((scrollY + viewportHeight) / rowPitch), first, rows);
        return (first, end);
    }

    /// <summary>Cover-fits a source rectangle of <paramref name="imageSize"/> into <paramref name="targetSize"/>.
    /// <paramref name="crop"/> is the normalized source rectangle (left, top, width, height), inset by half a
    /// texel so the linear sampler never borrows from a neighbouring atlas cell; <paramref name="pointer"/>
    /// slides the window a little inside its slack.</summary>
    public static (Vector2 Min, Vector2 Max) CoverUv(Vector2 imageSize, Vector2 targetSize, Vector4 crop, Vector2 focus, Vector2 pointer)
    {
        var origin = new Vector2(crop.X, crop.Y);
        var extent = new Vector2(crop.Z, crop.W);
        if (imageSize.X <= 0f || imageSize.Y <= 0f || targetSize.X <= 0f || targetSize.Y <= 0f)
        {
            return (origin, origin + extent);
        }

        var inset = Vector2.Min(new Vector2(0.5f) / imageSize, extent * 0.25f);
        origin += inset;
        extent -= inset * 2f;
        var source = imageSize * extent;
        var ratio = targetSize.X / targetSize.Y;
        var visible = extent;
        if (source.X / source.Y > ratio)
        {
            visible.X *= ratio / (source.X / source.Y);
        }
        else
        {
            visible.Y *= (source.X / source.Y) / ratio;
        }

        var slack = extent - visible;
        var nudge = Vector2.Clamp(pointer, -Vector2.One, Vector2.One) * Vector2.Min(slack * 0.5f, extent * PointerSlackShare);
        var offset = Vector2.Clamp((focus * extent) - (visible * 0.5f) + nudge, Vector2.Zero, slack);
        var min = origin + offset;
        return (min, min + visible);
    }
}
