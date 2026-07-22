using System;

namespace AetherLove.Shared.Profile.Enums;

/// <summary>FFXIV playable races. <see cref="None"/> (unset) is reserved for moderator-authored fake
/// (NPC) profiles (e.g. mobs, primals, Garleans) whose race players can't select; real users always
/// pick a single playable race. Clients omit the race entirely when it's None.</summary>
[Flags]
public enum Race : short
{
    None = 0,
    Hyur = 1,
    Elezen = 2,
    Lalafell = 4,
    Miqote = 8,
    Roegadyn = 16,
    AuRa = 32,
    Hrothgar = 64,
    Viera = 128,
}
