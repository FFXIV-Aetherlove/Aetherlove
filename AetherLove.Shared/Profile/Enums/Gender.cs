using System;

namespace AetherLove.Shared.Profile.Enums;

/// <summary><see cref="None"/> (unset) is reserved for moderator-authored fake (NPC) profiles whose
/// gender doesn't apply; real users always pick one. Clients omit the gender icon when it's None.</summary>
[Flags]
public enum Gender : short
{
    None = 0,
    Male = 1,
    Female = 2,
}
