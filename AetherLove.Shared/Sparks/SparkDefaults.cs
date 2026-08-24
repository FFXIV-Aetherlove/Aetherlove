namespace AetherLove.Shared.Sparks;

/// <summary>The weekly caps as shipped. The server owns the live values (`SparkOptions`, overridable from
/// appsettings) and sends them to the client on every wallet snapshot; these are what both sides say before
/// one arrives, and they live here so a rebalance cannot leave the two disagreeing.</summary>
public static class SparkDefaults
{
    public const int RoutineWeeklyCap = 300;

    public const int TotalWeeklyCap = 550;

    public const int BonusWeeklyCap = 150;
}
