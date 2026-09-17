namespace AetherLove.Shared.Racing.Cards;

/// <summary>What one line of a Gold card's race story says: the window opened, the card fired, or it was
/// refused for good.</summary>
public enum RaceSupportOutcome
{
    Window,
    Fired,
    Denied,
}

/// <summary>One line of a Gold card's race story, logged by the runner's controller. <see cref="Slot"/> is the
/// runner's index, <see cref="Tick"/> and <see cref="Time"/> the engine clock when it was logged,
/// <see cref="Section"/> and <see cref="Progress"/> where the runner was, and <see cref="Utility"/> against
/// <see cref="Threshold"/> the decision that fired it. A Gold logs at most one Window and exactly one terminal
/// Fired or Denied line.</summary>
public sealed record RaceSupportEvent(
    int Slot,
    string CardId,
    int Tick,
    float Time,
    RaceSupportOutcome Outcome,
    string Reason,
    string Section,
    float Progress,
    float Utility,
    float Threshold);

/// <summary>The reason strings the resolver writes into <see cref="RaceSupportEvent.Reason"/>. They are stored
/// with the race and shown by the result screen, so they are identifiers, never copy.</summary>
public static class RaceSupportReasons
{
    public const string FirstEligibleWindow = "first_eligible_window";
    public const string Applied = "applied";
    public const string StaticIneligible = "static_ineligible";
    public const string UnknownCard = "unknown_card";
    public const string WrongBand = "wrong_band";
    public const string TriggerNeverMet = "trigger_never_met";
    public const string TriggerNotMet = "trigger_not_met";
    public const string TimingMissedPrefix = "timing_missed:";
    public const string HardPredicateClosed = "hard_predicate_closed";
    public const string UtilityBelowThreshold = "utility_below_threshold";
    public const string NotRunning = "not_running";
    public const string NoRefillCapacity = "no_refill_capacity";
    public const string NoCurrentStumble = "no_current_stumble";
    public const string NoAccelerationHeadroom = "no_acceleration_headroom";
    public const string MomentumChannelBusy = "momentum_channel_busy";
    public const string InsufficientFuel = "insufficient_fuel";
    public const string NoMomentumHeadroom = "no_momentum_headroom";
    public const string MomentumPathNotStraight = "momentum_path_not_straight";
    public const string SurgeChannelBusy = "surge_channel_busy";
    public const string SurgeSpeedTooLow = "surge_speed_too_low";
    public const string NaturalCloserKickDue = "natural_closer_kick_due";
    public const string NoTrafficPressure = "no_traffic_pressure";
    public const string BurstOrSetbackBusy = "burst_or_setback_busy";
    public const string BurstPathNotStraight = "burst_path_not_straight";
    public const string NoRivalAhead = "no_rival_ahead";
    public const string UnsupportedOperation = "unsupported_operation";

    /// <summary>The terminal reason of a Gold whose window opened but whose last decision beat said no.</summary>
    public static string TimingMissed(string lastDenial) => TimingMissedPrefix + lastDenial;
}
