using System.Numerics;


namespace AetherOS.PetKit.Rendering;

/// <summary>The one colourway a newborn wears. Palettes are data in the design that is coming; until there
/// is anything to choose between, a chooser would be a menu with one item in it.</summary>
internal static class PetTints
{
    private static readonly Vector4 Body = new(0.624f, 0.878f, 0.933f, 1f);
    private static readonly Vector4 Accent = new(1.000f, 0.875f, 0.620f, 1f);
    private static readonly Vector4 Eye = new(0.184f, 0.420f, 0.478f, 1f);

    public static CoreTints Dawn { get; } = new(Body, Accent, Eye);

    /// <summary>The same colours faded together, for anything that draws the newborn part-way in.</summary>
    public static CoreTints DawnAt(float alpha) => new(
        Body with { W = alpha },
        Accent with { W = alpha },
        Eye with { W = alpha });
}
