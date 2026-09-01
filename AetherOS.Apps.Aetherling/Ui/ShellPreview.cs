using System.Numerics;
using AetherOS.PetKit.Rendering.LineArt;
using Dalamud.Bindings.ImGui;

namespace AetherOS.Apps.Aetherling.Ui;

/// <summary>A form as a little line drawing: the shell's own geometry at its rest pose, inked and
/// unfilled on a pale card, for the wardrobe rows to show what a form actually looks like.
///
/// <para>The drawing is the SHELL's, not a picture of it. Every drawn body already knows how to
/// render itself from a pose, so a preview is that same call with the fills turned transparent and
/// the ink left black: no second asset, no thumbnail to re-render when a shell is retuned, and a
/// form that changes shape changes here on the same commit.</para></summary>
internal static class ShellPreview
{
    /// <summary>The card the outline sits on. Ink on the wardrobe's own dark row would be invisible,
    /// so the preview brings its own paper.</summary>
    private static readonly Vector4 Card = new(0.93f, 0.95f, 0.97f, 1f);

    private static readonly Vector4 Ink = new(0f, 0f, 0f, 1f);

    /// <summary>The skin key a wardrobe row stands for: a shell's own asset folder, or the trueform's
    /// sheet identity for the row that wears no shell at all.</summary>
    public static string SkinFor(string itemRef) =>
        itemRef.Length == 0 ? "wispv2" : ShellCatalog.Find(itemRef)?.Folder ?? string.Empty;

    /// <summary>The canvas the one-colour outlines share. A socket icon is drawn on the draw thread like
    /// everything else here, and a canvas carries no per-shell state between calls.</summary>
    private static readonly LineCanvas IconCanvas = new();

    /// <summary>The form as a bare outline in one colour, centred on <paramref name="centre"/>, for a
    /// socket or a tab with room for a glyph rather than a card. Returns false for a form this build
    /// cannot draw, which leaves the caller's own icon showing.</summary>
    public static bool PaintOutline(ImDrawListPtr dl, string itemRef, Vector2 centre, float side, uint colour)
    {
        var shell = LineArtDispatch.ShellFor(SkinFor(itemRef));
        if (shell == 0 || side < 6f)
        {
            return false;
        }

        var rest = LineArtDispatch.PoseAt(shell, 0, 0, 0, 0, 0f);
        var restFace = LineArtDispatch.FaceAt(shell, 0, 0, 0f);
        var clear = new Vector4(0f, 0f, 0f, 0f);
        var ink = ImGui.ColorConvertU32ToFloat4(colour);
        var feet = centre + new Vector2(0f, side * 0.5f);
        LineArtDispatch.Draw(
            shell, IconCanvas, dl, feet, side, rest, rest, restFace.Eye, restFace.Blush,
            clear, clear, clear, ink, Vector2.One, flip: false);
        return true;
    }

    /// <summary>Draws the form for <paramref name="itemRef"/> inside the square at
    /// <paramref name="tl"/>. Nothing is drawn for a form this build cannot render, which leaves the
    /// caller's own placeholder showing.</summary>
    public static bool Draw(ImDrawListPtr dl, LineCanvas canvas, string itemRef, Vector2 tl, float side)
    {
        var shell = LineArtDispatch.ShellFor(SkinFor(itemRef));
        if (shell == 0 || side < 8f)
        {
            return false;
        }

        dl.AddRectFilled(tl, tl + new Vector2(side, side), ImGui.ColorConvertFloat4ToU32(Card), side * 0.22f);

        // Rest pose exactly: one cell either side of itself at phase zero, so the spline returns the
        // authored frame and no mood, beat or spring reaches a picture that is meant to hold still.
        var rest = LineArtDispatch.PoseAt(shell, 0, 0, 0, 0, 0f);
        var restFace = LineArtDispatch.FaceAt(shell, 0, 0, 0f);

        // Inset, because a shell draws to the edges of its own cell and a form pressed against the
        // card's corners reads as clipped rather than as drawn.
        const float Inset = 0.86f;
        var feet = tl + new Vector2(side * 0.5f, side - ((side * (1f - Inset)) * 0.5f));

        // Every fill takes the CARD's colour rather than going transparent, and that is what makes
        // one call serve both ways a shell inks itself. Most stroke an outline around a filled
        // shape, so a transparent fill would do; the Serpent lays its ink down as oversized stamps
        // and covers them with the fill, so a transparent one leaves a solid black tube where the
        // others left a line drawing. Painted in paper the two come out the same: outline only.
        LineArtDispatch.Draw(
            shell, canvas, dl, feet, side * Inset, rest, rest, restFace.Eye, restFace.Blush,
            Card, Card, Card, Ink, Vector2.One, flip: false);
        return true;
    }
}
