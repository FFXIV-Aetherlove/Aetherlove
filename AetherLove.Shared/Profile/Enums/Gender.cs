using System;

namespace AetherLove.Shared.Profile.Enums;

/// <summary><see cref="None"/> (unset) is reserved for moderator-authored fake (NPC) profiles whose
/// gender doesn't apply; real users always pick one. <see cref="Other"/> is a real, selectable gender for
/// anyone who doesn't identify as Male/Female. Clients omit the gender icon when it's None or Other.</summary>
[Flags]
public enum Gender : short
{
    None = 0,
    Male = 1,
    Female = 2,
    Other = 4,
}
