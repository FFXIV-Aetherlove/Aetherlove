using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AetherLove.Shared.Racing.Cards;

public enum RaceCardBand
{
    Gold,
    Silver,
}

/// <summary>How the generated catalogue JSON is read: camelCase, case-insensitive, bands by their lower-case names.</summary>
public static class RaceCardJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}

/// <summary>The closed set of trigger kinds the resolver understands. A card naming any other kind fails
/// <see cref="RaceCard.Validate"/>; a kind added here without a resolver branch would be a silent no-op.</summary>
public static class RaceCardTriggerKinds
{
    public const string Always = "always";
    public const string Early = "early";
    public const string Late = "late";
    public const string Uphill = "uphill";
    public const string Downhill = "downhill";
    public const string Bend = "bend";
    public const string Straight = "straight";
    public const string Drafting = "drafting";
    public const string Blocked = "blocked";
    public const string BehindLeader = "behind_leader";
    public const string LowStamina = "low_stamina";
    public const string Stumbled = "stumbled";
    public const string Recovering = "recovering";
    public const string CornerExit = "corner_exit";
    public const string ClimbExit = "climb_exit";
    public const string Pack = "pack";
    public const string Opening = "opening";

    public static readonly string[] All =
    [
        Always, Early, Late, Uphill, Downhill, Bend, Straight, Drafting, Blocked, BehindLeader, LowStamina, Stumbled,
        Recovering, CornerExit, ClimbExit, Pack, Opening,
    ];

    public static bool IsKnown(string kind) => Array.IndexOf(All, kind) >= 0;
}

/// <summary>The closed set of operation kinds and the caps on the Silver channels. The five silver_* kinds feed
/// the engine's <c>SilverBuffs</c>; the rest are Gold operations applied once per race.</summary>
public static class RaceCardOperationKinds
{
    public const string SilverTank = "silver_tank";
    public const string SilverCraft = "silver_craft";
    public const string SilverSpark = "silver_spark";
    public const string SilverStrideStart = "silver_stride_start";
    public const string SilverGateStart = "silver_gate_start";
    public const string Rush = "rush";
    public const string RecoverSpeed = "recover_speed";
    public const string PaceSurge = "pace_surge";
    public const string Ghost = "ghost";
    public const string NativeBurst = "native_burst";
    public const string RestoreStamina = "restore_stamina";
    public const string SoftenStumble = "soften_stumble";

    public static readonly string[] All =
    [
        SilverTank, SilverCraft, SilverSpark, SilverStrideStart, SilverGateStart, Rush, RecoverSpeed, PaceSurge, Ghost,
        NativeBurst, RestoreStamina, SoftenStumble,
    ];

    public const string SilverPrefix = "silver_";

    public const float TankCap = 0.12f;
    public const float CraftCap = 0.06f;
    public const float SparkCap = 0.30f;
    public const float StrideCap = 0.002f;
    public const float GateCap = 0.12f;

    public static bool IsKnown(string kind) => Array.IndexOf(All, kind) >= 0;

    public static bool IsSilver(string kind) => kind.StartsWith(SilverPrefix, StringComparison.Ordinal);

    /// <summary>The cap an authored amount may not exceed on a Silver channel; infinity for a Gold operation.</summary>
    public static float ChannelCap(string kind) => kind switch
    {
        SilverTank => TankCap,
        SilverCraft => CraftCap,
        SilverSpark => SparkCap,
        SilverStrideStart => StrideCap,
        SilverGateStart => GateCap,
        _ => float.PositiveInfinity,
    };
}

/// <summary>The course tags a card's eligibility may name, one per <see cref="AetherRaceLive.RaceCategory"/>.</summary>
public static class RaceCardCourseTags
{
    public const string Sprint = "sprint";
    public const string Route = "route";
    public const string Journey = "journey";

    public static readonly string[] All = [Sprint, Route, Journey];

    public static string Of(AetherRaceLive.RaceCategory category) => category switch
    {
        AetherRaceLive.RaceCategory.Sprint => Sprint,
        AetherRaceLive.RaceCategory.Route => Route,
        AetherRaceLive.RaceCategory.Journey => Journey,
        _ => string.Empty,
    };
}

/// <summary>Where a card may be played: course categories by tag and skies by weather key. An empty list means
/// any.</summary>
public sealed record RaceCardEligibility
{
    public string[] CourseTags { get; init; } = [];

    public string[] WeatherKeys { get; init; } = [];

    public bool Fits(AetherRaceLive.CourseDef course, string weatherKey)
    {
        var courseFits = CourseTags.Length == 0 || Array.IndexOf(CourseTags, RaceCardCourseTags.Of(course.Category)) >= 0;
        var skyFits = WeatherKeys.Length == 0 || Array.IndexOf(WeatherKeys, weatherKey) >= 0;
        return courseFits && skyFits;
    }
}

/// <summary>When a card may act. <see cref="Min"/> and <see cref="Max"/> are the progress window as fractions of
/// the course; the authored progressMin/progressMax win, else late starts at 0.75 and early ends at 0.25.</summary>
public sealed record RaceCardTrigger
{
    private const float LateDefaultMin = 0.75f;
    private const float EarlyDefaultMax = 0.25f;

    public string Kind { get; init; } = RaceCardTriggerKinds.Always;

    public float? WindowSeconds { get; init; }

    public float? ProgressMin { get; init; }

    public float? ProgressMax { get; init; }

    public float? Threshold { get; init; }

    [JsonIgnore]
    public float Min => ProgressMin ?? (Kind == RaceCardTriggerKinds.Late ? LateDefaultMin : 0f);

    [JsonIgnore]
    public float Max => ProgressMax ?? (Kind == RaceCardTriggerKinds.Early ? EarlyDefaultMax : 1f);
}

/// <summary>What a card does. <see cref="Amount"/> is the authored level-1 quantity; the resolver scales it by
/// <see cref="RaceCardLevels.Scale"/>. Duration and cost never scale.</summary>
public sealed record RaceCardOperation
{
    public const float DefaultCost = 0.01f;

    public string Kind { get; init; } = string.Empty;

    public float Amount { get; init; }

    public float DurationSeconds { get; init; }

    public float Cost { get; init; } = DefaultCost;
}

/// <summary>One card's mechanics as the catalogue carries them. Display copy lives in the Racer app's strings.</summary>
public sealed record RaceCard
{
    private const int MinRank = 1;
    private const int MaxRank = 9;
    private const float MaxWindowSeconds = 300f;
    private const float MaxDurationSeconds = 10f;
    private const float MaxCost = 1f;
    private const float MaxRestoreAmount = 1f;
    private const float MaxRecoverSpeedAmount = 2f;
    private const float MinFuelCost = 0.01f;
    private const float MaxSurgeAmount = 0.10f;
    private const float MaxSurgeSeconds = 2.5f;

    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public int EffectVersion { get; init; } = 1;

    public string Element { get; init; } = string.Empty;

    public int Rank { get; init; }

    public RaceCardBand Band { get; init; }

    public RaceCardEligibility Eligibility { get; init; } = new();

    public RaceCardEligibility Home { get; init; } = new();

    public RaceCardTrigger Trigger { get; init; } = new();

    public RaceCardOperation Operation { get; init; } = new();

    public string StackGroup { get; init; } = string.Empty;

    [JsonIgnore]
    public bool IsGold => Band == RaceCardBand.Gold;

    /// <summary>Throws <see cref="InvalidDataException"/> when the card breaks the authoring contract: unknown
    /// kinds, out-of-range quantities, a Silver channel over its cap, or a constructor-only channel without opening
    /// eligibility. The bounds are the prototype's, ported exactly.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || Array.IndexOf(RacingElements.WheelOrder, Element) < 0
            || Rank is < MinRank or > MaxRank || EffectVersion < 1 || !Enum.IsDefined(Band) || string.IsNullOrWhiteSpace(StackGroup))
        {
            throw Invalid("invalid identity, element, rank, effect version, band or stack group");
        }

        if (!RaceCardTriggerKinds.IsKnown(Trigger.Kind))
        {
            throw Invalid($"unknown trigger {Trigger.Kind}");
        }

        if (!RaceCardOperationKinds.IsKnown(Operation.Kind))
        {
            throw Invalid($"unsupported operation {Operation.Kind}; it needs a real engine hook");
        }

        if (!EligibilityIsKnown(Eligibility) || !EligibilityIsKnown(Home))
        {
            throw Invalid("unknown course or weather eligibility");
        }

        var trigger = Trigger;
        if (!float.IsFinite(trigger.Min) || !float.IsFinite(trigger.Max) || trigger.Min < 0f || trigger.Max > 1f || trigger.Min > trigger.Max
            || (trigger.Threshold is { } threshold && !float.IsFinite(threshold))
            || (trigger.WindowSeconds is { } window && (!float.IsFinite(window) || window <= 0f || window > MaxWindowSeconds)))
        {
            throw Invalid("invalid trigger bounds");
        }

        var op = Operation;
        if (!float.IsFinite(op.Amount) || op.Amount < 0f || !float.IsFinite(op.DurationSeconds) || op.DurationSeconds < 0f
            || op.DurationSeconds > MaxDurationSeconds || !float.IsFinite(op.Cost) || op.Cost < 0f || op.Cost > MaxCost)
        {
            throw Invalid("invalid operation quantities");
        }

        if ((Band == RaceCardBand.Silver) != RaceCardOperationKinds.IsSilver(op.Kind))
        {
            throw Invalid("band and operation do not match");
        }

        if (op.Kind is RaceCardOperationKinds.Rush or RaceCardOperationKinds.Ghost or RaceCardOperationKinds.NativeBurst && op.DurationSeconds <= 0f)
        {
            throw Invalid("a duration is required");
        }

        var cap = RaceCardOperationKinds.ChannelCap(op.Kind);
        if (op.Amount > cap)
        {
            throw Invalid($"authored amount exceeds the {op.Kind} cap {cap}");
        }

        if ((op.Kind == RaceCardOperationKinds.RestoreStamina && op.Amount > MaxRestoreAmount)
            || (op.Kind == RaceCardOperationKinds.SoftenStumble && op.Amount > AetherRaceLive.Dials.StumbleSecs))
        {
            throw Invalid("excess resource or duration amount");
        }

        if (op.Kind == RaceCardOperationKinds.RecoverSpeed && (op.Amount <= 0f || op.Amount > MaxRecoverSpeedAmount || op.Cost < MinFuelCost))
        {
            throw Invalid("momentum needs 0 < amount <= 2 bounds/s and at least 1% fuel cost");
        }

        if (op.Kind == RaceCardOperationKinds.PaceSurge
            && (op.Amount <= 0f || op.Amount > MaxSurgeAmount || op.DurationSeconds <= 0f || op.DurationSeconds > MaxSurgeSeconds || op.Cost < MinFuelCost))
        {
            throw Invalid("surge exceeds the bounded magnitude, duration or fuel contract");
        }

        if (op.Kind is RaceCardOperationKinds.SilverStrideStart or RaceCardOperationKinds.SilverGateStart
            && (trigger.Kind is not (RaceCardTriggerKinds.Always or RaceCardTriggerKinds.Early) || trigger.Min > 0f))
        {
            throw Invalid("a constructor-only channel needs opening eligibility");
        }
    }

    private static bool EligibilityIsKnown(RaceCardEligibility eligibility)
    {
        return eligibility.CourseTags.All(t => Array.IndexOf(RaceCardCourseTags.All, t) >= 0)
            && eligibility.WeatherKeys.All(AetherRaceLive.Weathers.ContainsKey);
    }

    private InvalidDataException Invalid(string reason) => new($"{Id}: {reason}.");
}
