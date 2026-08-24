using System;

namespace AetherLove.Shared.Profile.Enums;

[Flags]
public enum Region : short
{
    NorthAmerica = 1,
    Europe = 2,
    Oceania = 4,
    Japan = 8,
    PreferNotToSay = 16,
}

/// <summary>Mask constants for <see cref="Region"/>. A profile's own region is a nonzero mask of the
/// selectable bits; PreferNotToSay is retired and rejected on write, kept declared only for legacy rows.</summary>
public static class RegionBits
{
    public const short Selectable = (short)(Region.NorthAmerica | Region.Europe | Region.Oceania | Region.Japan);
}
