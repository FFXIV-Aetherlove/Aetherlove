using System;
using System.Numerics;
using AetherLove.Shared.Store;

namespace AetherOS.Apps.Store;

/// <summary>Which part of a product's picture a shelf card shows. A centred cover crop is right for a
/// picture composed as a picture, and wrong for the two kinds whose art is a shape: a ring shrunk to a
/// thumbnail is a thin hoop of nothing around a hole, and a whole phone squeezed into a landscape card is a
/// sliver. Both are far more legible cropped to the part that carries the detail.</summary>
internal static class StoreArtCrop
{
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
