using System;

namespace AetherLove.Shared.Profile.Enums;

/// <summary>What the user wants to find on the app.</summary>
[Flags]
public enum LookingFor : short
{
    Chatting = 1,
    InGameRomance = 2,
    LongTermRelationship = 4,
    RoleplayPartners = 8,
    CasualPlayBuddies = 16,
    GposeAndGlamour = 32,

    /// <summary>Adult roleplay. Picking this forces <c>NsfwEnabled</c> on.</summary>
    Erp = 64,
}
