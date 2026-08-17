using System;
using System.Numerics;
using AetherLove.Shared.Store;
using AetherLove.UI;

namespace AetherOS.Apps.Store;

/// <summary>Which part of a product's picture a shelf card shows. A centred cover crop is right for a
/// picture composed as a picture, and wrong for the two kinds whose art is a shape: a ring shrunk to a
/// thumbnail is a thin hoop of nothing around a hole, and a whole phone squeezed into a landscape card is a
/// sliver. Both are far more legible cropped to the part that carries the detail.</summary>
internal static class StoreArtCrop
{
    /// <summary>How much of a worn-pet render a shelf thumbnail keeps, as a fraction of the source side.
    /// Those pictures are composed for a product card, where the creature sits small in a lot of
    /// transparent room; at thumbnail size that room is most of the tile and the creature is a speck. This
    /// window is measured off the renders themselves: it holds every item's reach, banners out to one side
    /// and a wizard's beard hanging below, with a little margin.</summary>
    private const float PetWindow = 0.53f;

    /// <summary>The same window for a product card, which is several times the size of a tile and so needs
    /// far less of it: at tile zoom a card crops the tall pieces (a halo, a crown, a parasol) off their own
    /// picture, and a piece of furniture becomes a face with a sliver of cushion under it. Wide enough to
    /// hold the reach, tight enough that the creature is not a speck.</summary>
    private const float PetCardWindow = 0.72f;

    /// <summary>The renders are pinned so the creature's own silhouette always lands on the same rect, which
    /// sits a touch left of the canvas centre.</summary>
    private static readonly Vector2 PetWindowCentre = new(0.484f, 0.5f);

    /// <summary>A card's window sits a little higher than a tile's, because what a card crops first is
    /// whatever is worn on the head.</summary>
    private static readonly Vector2 PetCardWindowCentre = new(0.484f, 0.46f);

    /// <summary>The per-kind crop with the creature zoomed in on top of it, for anywhere a product is drawn
    /// small: the category and subcategory tiles, the home rails, the grid, a collection's rows. Anything
    /// that is not a worn-pet render takes <see cref="Uv"/> unchanged, so a ring keeps its corner and a
    /// phone skin its top edge; they have no creature to zoom onto and their own crops are the whole point.</summary>
    public static (Vector2 Uv0, Vector2 Uv1) PetThumbnailUv(
        StoreItemKind kind, float texWidth, float texHeight, float boxWidth, float boxHeight) =>
        PetUv(kind, texWidth, texHeight, boxWidth, boxHeight, PetWindow, PetWindowCentre);

    /// <summary>The same, at a product card's gentler zoom. Everything a card draws goes through here so a
    /// hat and a nook are cropped by one rule; the tile version stays tighter because a tile is a fraction
    /// of the size.</summary>
    public static (Vector2 Uv0, Vector2 Uv1) PetCardUv(
        StoreItemKind kind, float texWidth, float texHeight, float boxWidth, float boxHeight) =>
        PetUv(kind, texWidth, texHeight, boxWidth, boxHeight, PetCardWindow, PetCardWindowCentre);

    private static (Vector2 Uv0, Vector2 Uv1) PetUv(
        StoreItemKind kind, float texWidth, float texHeight, float boxWidth, float boxHeight,
        float window, Vector2 centre)
    {
        var (uv0, uv1) = Uv(kind, texWidth, texHeight, boxWidth, boxHeight);
        if (!IsWornPetRender(kind) || texWidth <= 0f || texHeight <= 0f)
        {
            return (uv0, uv1);
        }

        var size = (uv1 - uv0) * window;
        var half = size * 0.5f;
        // Clamped so the window never leaves the source, which would sample the wrap's edge and smear it.
        var cx = Math.Clamp(centre.X, half.X, 1f - half.X);
        var cy = Math.Clamp(centre.Y, half.Y, 1f - half.Y);
        return (new Vector2(cx - half.X, cy - half.Y), new Vector2(cx + half.X, cy + half.Y));
    }

    /// <summary>The kinds whose picture is the creature wearing the thing, rather than the thing itself.</summary>
    private static bool IsWornPetRender(StoreItemKind kind) =>
        kind is StoreItemKind.AetherlingAccessory
            or StoreItemKind.AetherlingArms
            or StoreItemKind.AetherlingPalette
            or StoreItemKind.AetherlingConsumable;

    /// <summary>The UV rect to sample, sized so the source is never distorted and anchored per kind.</summary>
    public static (Vector2 Uv0, Vector2 Uv1) Uv(
        StoreItemKind kind, float texWidth, float texHeight, float boxWidth, float boxHeight)
    {
        if (texWidth <= 0f || texHeight <= 0f || boxWidth <= 0f || boxHeight <= 0f)
        {
            return (Vector2.Zero, Vector2.One);
        }

        // The fraction of the source the card may see, where that window starts, and how the leftover is
        // spread once the aspect fit trims it further (0 keeps the window pinned to its anchor edge).
        var (focusW, focusH, anchorX, anchorY, alignX, alignY) = kind switch
        {
            // The ring's top-left corner: a quarter turn of the band at close to full detail, with the
            // transparent padding the art carries trimmed off the edges.
            StoreItemKind.AvatarFrame => (0.5f, 0.5f, 0.04f, 0.04f, 0f, 0f),
            // The phone's top edge, kept to the left corner when the box has to trim.
            StoreItemKind.ThemePack => (1f, 0.34f, 0f, 0f, 0f, 0f),
            _ => (1f, 1f, 0f, 0f, 0.5f, 0.5f),
        };

        // Cover-fit the destination inside the focus region, so whichever axis is proportionally longer
        // gets trimmed rather than squashed.
        var focusPxW = texWidth * focusW;
        var focusPxH = texHeight * focusH;
        var scale = MathF.Max(boxWidth / focusPxW, boxHeight / focusPxH);
        var sampleW = MathF.Min(focusW, boxWidth / (scale * texWidth));
        var sampleH = MathF.Min(focusH, boxHeight / (scale * texHeight));

        // Place what is left inside the focus region, then clamp so it never runs off the source.
        var u0 = Math.Clamp(anchorX + (focusW - sampleW) * alignX, 0f, 1f - sampleW);
        var v0 = Math.Clamp(anchorY + (focusH - sampleH) * alignY, 0f, 1f - sampleH);
        return (new Vector2(u0, v0), new Vector2(u0 + sampleW, v0 + sampleH));
    }
}
