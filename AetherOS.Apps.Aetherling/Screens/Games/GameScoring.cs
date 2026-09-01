namespace AetherOS.Apps.Aetherling.Screens.Games;

/// <summary>Every number the three minigames score with, in one place. Must track
/// AetherLove.Server/Arcade/ArcadeScoreChecker.cs (the CloudHop, CrystalCatch and HillRoll consts): the
/// server bounds submitted scores with these same values, so if the two files disagree honest runs get
/// flagged for moderation. Tune them together or not at all.</summary>
internal static class GameScoring
{
    // Cloud Hop. The normal arc gains exactly one row: apex = BounceVy^2 / (2 * Gravity) = 100 units
    // against a 90 unit row spacing. A super cloud launches at SuperBounceFactor times that, sailing
    // several rows, and its bonus pays PER ROW GAINED rather than flat. Row points count the HIGHEST row
    // only, but the perfect bonus pays on every landing, so a long run scores far past ten a row and the
    // server must bound the two separately. MinSecondsPerRow is the super chain's own rate, the fastest
    // climb that can exist: ~0.278s per row on paper, but the Euler integrator flies a lower arc than
    // the continuous maths and lands the 5-row catch early, so a real chain averages ~0.26s per row.
    // MinSecondsPerLanding is BounceVy / Gravity, the soonest a bounce can come back down (landing at
    // the apex itself).
    public const float CloudHopGravity = 800f;
    public const float CloudHopBounceVy = 400f;
    public const float CloudHopSuperBounceFactor = 2.2f;
    public const float CloudHopRowSpacing = 90f;
    public const int CloudHopRowPoints = 10;
    public const int CloudHopPerfectBonus = 5;
    public const int CloudHopSuperBonusPerRow = 5;
    public const double CloudHopMinSecondsPerRow = 0.26;
    public const double CloudHopMinSecondsPerLanding = 0.5;

    // Crystal Catch. Catches cannot outpace the spawn interval, which floors at SpawnFloorSeconds;
    // twin drops consume two intervals so the total never exceeds elapsed / floor. A bonus crystal is
    // worth triple, so the richest possible catch is BonusPoints + ComboCap, which is the per-catch
    // ceiling the server bounds with.
    public const int CrystalCatchPoints = 10;
    public const int CrystalCatchBonusPoints = 30;
    public const int CrystalCatchComboCap = 10;
    public const float CrystalCatchSpawnFloorSeconds = 0.45f;

    // Hill Roll. The speed clamp is applied AFTER the downhill acceleration, so MaxSpeed is a hard
    // ceiling on normal rolling; a turbo locks the cart at TurboSpeed for its burst, which is why the
    // SERVER'S distance bound uses TurboSpeed, not MaxSpeed. Crystals are pre-rolled at least
    // MinCrystalSpacing apart.
    public const double HillRollPointsPerMetre = 0.5;
    public const int HillRollCrystalPoints = 25;
    public const float HillRollMaxSpeed = 26f;
    public const float HillRollTurboSpeed = 32f;

    /// <summary>An air boost throws the cart forward far harder than a ground turbo, and it is therefore
    /// the highest speed the game can reach, which is the one the server's distance bound must honour.</summary>
    public const float HillRollAirBoostSpeed = 40f;
    public const float HillRollMinCrystalSpacing = 25f;

    // Lumi-Link. Scoring is per resolve step: a group pays by size, every point in a step is multiplied
    // by the cascade depth (capped), and specials pay per cell they clear. The clock is the only budget
    // (StartSeconds, refilled on every level-up), so the server bounds a score by the
    // run's ELAPSED TIME: the richest legal second is a Prism-and-Prism wipe, and no honest run chains
    // those back to back, so MaxPointsPerSecond sits well above any real run and well below a forged
    // one. Metric1 is the level reached, Metric2 the deepest cascade, bounded by MaxCascade.
    public const int LumiLinkMatch3 = 50;
    public const int LumiLinkMatch4 = 100;
    public const int LumiLinkMatch5 = 200;
    public const int LumiLinkPerExtraCell = 25;
    public const int LumiLinkSpecialPerCell = 15;
    public const int LumiLinkPrismPerCell = 20;
    public const int LumiLinkPrismPrism = 2_000;
    public const int LumiLinkPowerPerCell = 10;
    public const int LumiLinkCascadeCap = 8;
    public const int LumiLinkLevelUpBonus = 250;
    public const int LumiLinkLevel1Target = 525;
    public const float LumiLinkLevelGrowth = 1.15f;
    public const float LumiLinkStartSeconds = 60f;
    public const float LumiLinkIceFreezeSeconds = 10f;
    public const int LumiLinkPowerMeterPoints = 2_500;
    public const int LumiLinkMaxPointsPerSecond = 20_000;
    public const int LumiLinkMaxCascade = 40;

    // Gyre. A pop pays by group size, multiplied by the cascade depth (capped); a gap-back slam pays its
    // own bonus because it is the skill move. Points come only from chain pops and catches, so the server
    // bounds a score by ELAPSED TIME: the richest legal second is an Earth quake resolving into a full
    // cascade, and no honest run chains those. Stage clears cannot outpace the surge draining the
    // shortest chain (MinSecondsPerStage), and the ladder ends at stage 20, the endless Core. Metric1 is
    // the highest stage reached, Metric2 the deepest cascade, bounded by MaxCascade.
    public const int GyreMatch3 = 50;
    public const int GyrePerExtraMarble = 25;
    public const int GyreCascadeCap = 8;
    public const int GyreGapSlamBonus = 100;
    public const int GyrePowerupCatch = 50;
    public const int GyreStageClearBonus = 500;
    public const int GyreTimeBonusPerSecond = 15;
    public const int GyreHpRegainEvery = 50_000;
    public const int GyreHpRegain = 3;
    public const int GyreMaxHp = 10;
    public const float GyreShotSpeed = 1400f;
    public const float GyreAimSeconds = 10f;
    public const float GyreSlowSeconds = 8f;
    public const float GyreRecoilUnits = 400f;
    public const int GyreShatterShots = 3;
    public const int GyreNeedleShots = 3;
    public const float GyreDoubleSeconds = 8f;
    public const int GyreFireTorchCount = 12;
    public const float GyreIceFreezeSeconds = 10f;
    public const float GyreWindShoveUnits = 700f;
    public const int GyrePowerMeterPoints = 2_500;
    public const int GyreMaxPointsPerSecond = 15_000;
    public const int GyreMaxCascade = 12;
    public const double GyreMinSecondsPerStage = 12.0;
    public const int GyreMaxStage = 20;
}
