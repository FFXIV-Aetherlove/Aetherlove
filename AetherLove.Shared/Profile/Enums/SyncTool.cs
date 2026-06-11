using System;

namespace AetherLove.Shared.Profile.Enums;

[Flags]
public enum SyncTool : short
{
    None = 0,
    Lightless = 1,
    PlayerSync = 2,
    HonseFarm = 4,
    Snowcloak = 8,
}
