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
