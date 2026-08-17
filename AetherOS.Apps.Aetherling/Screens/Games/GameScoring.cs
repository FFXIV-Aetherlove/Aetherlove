namespace AetherOS.Apps.Aetherling.Screens.Games;

/// <summary>Every number the three minigames score with, in one place. Must track
/// AetherLove.Server/Arcade/ArcadeScoreChecker.cs (the CloudHop, CrystalCatch and HillRoll consts): the
/// server bounds submitted scores with these same values, so if the two files disagree honest runs get
/// flagged for moderation. Tune them together or not at all.</summary>
internal static class GameScoring
{
    // Cloud Hop. The normal arc gains exactly one row: apex = BounceVy^2 / (2 * Gravity) = 100 units
    // against a 90 unit row spacing. A super cloud launches at SuperBounceFactor times that, sailing
    // several rows, and its bonus pays PER ROW GAINED rather than flat, so the worst honest landing is
    // worth RowPoints + PerfectBonus + SuperBonusPerRow per row whatever the launch was caught on.
    // MinSecondsPerRow is the super chain's own rate, which is the fastest climb that can exist.
    public const float CloudHopGravity = 800f;
    public const float CloudHopBounceVy = 400f;
    public const float CloudHopSuperBounceFactor = 2.2f;
    public const float CloudHopRowSpacing = 90f;
    public const int CloudHopRowPoints = 10;
    public const int CloudHopPerfectBonus = 5;
    public const int CloudHopSuperBonusPerRow = 5;
    public const int CloudHopMaxPointsPerRow = 18;
    public const double CloudHopMinSecondsPerRow = 0.35;

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
}
