using System;

namespace AetherLove.Shared.Profile.Enums;

/// <summary>Activities / content the player enjoys.</summary>
[Flags]
public enum ContentInterest : int
{
    Roulettes = 1,
    Pvp = 2,
    RegularContent = 4,
    BlueMage = 8,
    ExtremeTrials = 16,
    TreasureHunts = 32,
    SavageRaiding = 64,
    CraftingAndGathering = 128,
    Fishing = 256,
    ClubVenues = 512,
    Gpose = 1024,
    AchievementHunting = 2048,
    Mahjong = 4096,
    Housing = 8192,
    MusicAndBard = 16384,
    RoleplayingVenues = 32768,
    TripleTriad = 65536,
    StoryAndLore = 131072,
    UltimateRaiding = 262144,
    FieldOperations = 524288,
    DeepDungeons = 1048576,
    VariantCriterionDungeons = 2097152,
}
