using System.Numerics;

namespace AetherOS.Apps.Notes;

/// <summary>The per-note accent palette. Index 0 is the neutral "no colour" card; the rest are the swatches
/// offered in the editor's colour picker.</summary>
internal static class NoteColors
{
    private static readonly Vector4[] Accents =
    [
        new(0.95f, 0.71f, 0.24f, 1f),
        new(0.96f, 0.51f, 0.34f, 1f),
        new(0.93f, 0.42f, 0.55f, 1f),
        new(0.68f, 0.52f, 0.94f, 1f),
        new(0.40f, 0.68f, 0.95f, 1f),
        new(0.36f, 0.82f, 0.66f, 1f),
        new(0.62f, 0.80f, 0.36f, 1f),
        new(0.72f, 0.68f, 0.62f, 1f),
    ];

    internal static int Count => Accents.Length;

    internal static Vector4 Accent(int index)
    {
        if (index < 0 || index >= Accents.Length)
        {
            return Accents[0];
        }
        return Accents[index];
    }

    /// <summary>The card body tint: the accent pulled far down so text keeps its contrast.</summary>
    internal static Vector4 Surface(int index, float alpha)
    {
        var a = Accent(index);
        return new Vector4(a.X * 0.20f + 0.07f, a.Y * 0.20f + 0.07f, a.Z * 0.20f + 0.08f, alpha);
    }
}
