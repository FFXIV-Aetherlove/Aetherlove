namespace AetherLove.Shared.Profile.Enums;

/// <summary>FFXIV jobs. Values are Lumina ClassJob row IDs; <see cref="None"/> is the unset sentinel.</summary>
public enum Job : short
{
    None = 0,

    Paladin = 19,
    Warrior = 21,
    DarkKnight = 32,
    Gunbreaker = 37,

    WhiteMage = 24,
    Scholar = 28,
    Astrologian = 33,
    Sage = 40,

    Monk = 20,
    Dragoon = 22,
    Ninja = 30,
    Samurai = 34,
    Reaper = 39,
    Viper = 41,

    Bard = 23,
    Machinist = 31,
    Dancer = 38,

    BlackMage = 25,
    Summoner = 27,
    RedMage = 35,
    Pictomancer = 42,

    BlueMage = 36,

    Carpenter = 8,
    Blacksmith = 9,
    Armorer = 10,
    Goldsmith = 11,
    Leatherworker = 12,
    Weaver = 13,
    Alchemist = 14,
    Culinarian = 15,

    Miner = 16,
    Botanist = 17,
    Fisher = 18,
}
