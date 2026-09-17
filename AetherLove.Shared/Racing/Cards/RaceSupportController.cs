using System;
using System.Collections.Generic;

namespace AetherLove.Shared.Racing.Cards;

/// <summary>One runner's card work for one race, ported operation for operation from the prototype's support
/// controller: the constructor-only Silver channels (stride, gate), the per-tick Silver upkeep into the engine's
/// <see cref="AetherRaceLive.SilverBuffs"/>, and the Gold's window, decision beats and single activation.
/// <see cref="RaceSupportSession"/> drives it: <see cref="BeforeStep"/> on every runner, then
/// <see cref="ApplyPending"/> on every runner, then the engine's step, so every runner decides on the same
/// pre-application state. It writes public <see cref="AetherRaceLive.Runner"/> fields only and never touches the
/// engine's RNG. Every amount is the authored one scaled by <see cref="RaceCardLevels"/>; durations, windows,
/// thresholds and costs never scale.</summary>
public sealed class RaceSupportController
{
    private const int NeverTick = -10000;
    private const int NotOpened = -1;

    // The Gold decision: the threshold in Focus, the decision cadence in ticks, and how the threshold relaxes
    // late in the window.
    private const float ThresholdBase = 0.40f;
    private const float ThresholdFocusShare = 0.25f;
    private const float ThresholdSpread = 0.20f;
    private const float CadenceBase = 24f;
    private const float CadenceFocusShare = 12f;
    private const float RelaxFrom = 0.85f;
    private const float RelaxSpan = 0.15f;
    private const float RelaxFloor = 0.25f;
    private const float MinWindowSpan = 0.0001f;

    // Predicate defaults and reach; an authored threshold wins where a card carries one.
    private const float DefaultGradeThreshold = 0.01f;
    private const float DefaultBendKappa = 0.025f;
    private const float DefaultStraightKappa = 0.01f;
    private const float DefaultDraftingShare = 0.5f;
    private const float DefaultBlockedSeconds = 0.25f;
    private const float DefaultLeaderGap = 1f;
    private const float DefaultLowStamina = 0.30f;
    private const float DefaultStumbleSeconds = 0f;
    private const float DefaultPackCount = 1f;
    private const float DefaultOpeningGap = 0f;
    private const int RecoveringTicks = 60;
    private const int ExitTicks = 30;
    private const float ExitSpeedShare = 0.9f;
    private const float PackReach = 6f;
    private const float NeighbourReach = 1.8f;
    private const float NeighbourLateral = 1.2f;

    // Operation guards and utilities.
    private const float FuelFloor = 0.15f;
    private const float SpeedHeadroomShare = 0.9f;
    private const float MomentumMinHeadroom = 0.25f;
    private const float MomentumLookahead = 6f;
    private const float SurgeMinSpeedShare = 0.75f;
    private const float CloserKickWeight = 0.36f;
    private const float CloserKickStamina = 0.2f;
    private const float StraightPathKappa = 0.01f;
    private const float PathSampleStep = 2f;
    private const float BurstLookMin = 4f;
    private const float BurstLookMax = 24f;
    private const float RivalGapSpan = 6f;
    private const float GhostBlockedSpan = 2f;
    private const float GhostNeighbourWeight = 0.1f;
    private const float AccelBaseShare = 0.85f;
    private const float AccelPowerShare = 0.30f;
    private const float MinSurgeDistance = 1f;
    private const float SurgeCooldownTicks = 2f;
    private const float MinGateDelay = 0.04f;

    private readonly AetherRaceLive.Race race;
    private readonly AetherRaceLive.Runner runner;
    private readonly string goldId;
    private readonly RaceCard? gold;
    private readonly float goldAmount;
    private readonly bool goldEligible;
    private readonly SilverEntry[] silvers;
    private readonly float focus;
    private readonly float threshold;
    private readonly int cadence;
    private readonly int phase;
    private readonly List<RaceSupportEvent> events = [];
    private readonly Action<RaceSupportEvent>? observer;
    private float previousStumble;
    private float previousSpeed;
    private float observedAcceleration;
    private int lastStumbleEnd = NeverTick;
    private int lastBlocked = NeverTick;
    private int lastBend = NeverTick;
    private int lastClimb = NeverTick;
    private int opened = NotOpened;
    private int closeTick = int.MaxValue;
    private bool terminal;
    private bool ownBurst;
    private float previousBurstCooldown;
    private bool previousLastLight;
    private bool ownPace;
    private int paceEnd;
    private float previousPaceTop;
    private float previousPaceCooldown;
    private bool pending;
    private float pendingUtility;
    private float pendingThreshold;
    private string lastDenial = RaceSupportReasons.TriggerNotMet;

    private readonly record struct SilverEntry(RaceCard Card, float Amount, bool Eligible);

    /// <summary>Builds the controller over the catalogue. See the main constructor.</summary>
    public RaceSupportController(AetherRaceLive.Race race, int slot, RaceCardHand hand, string weatherKey, int seed, string supportVersion)
        : this(race, slot, hand, weatherKey, seed, supportVersion, RaceCardCatalogue.Find, null)
    {
    }

    /// <summary>Builds the controller for <paramref name="slot"/> of <paramref name="race"/> and applies the
    /// constructor-only Silver channels at once, so it must run before the first engine step. <paramref name="resolve"/>
    /// maps a card id to its mechanics (the catalogue in production, authored cards in tests); an unknown or
    /// wrong-band Gold logs one Denied line and is otherwise empty, an unknown or wrong-band Silver is an empty
    /// slot. <paramref name="observer"/> also receives every logged event, in order.</summary>
    public RaceSupportController(
        AetherRaceLive.Race race,
        int slot,
        RaceCardHand hand,
        string weatherKey,
        int seed,
        string supportVersion,
        Func<string, RaceCard?> resolve,
        Action<RaceSupportEvent>? observer)
    {
        ArgumentNullException.ThrowIfNull(race);
        ArgumentNullException.ThrowIfNull(hand);
        ArgumentNullException.ThrowIfNull(resolve);
        if (slot < 0 || slot >= race.Runners.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(slot));
        }

        this.race = race;
        this.Slot = slot;
        this.runner = race.Runners[slot];
        this.observer = observer;
        this.focus = AetherRaceLive.Cfn(this.runner.Stats.Focus);
        this.silvers = ResolveSilvers(hand, resolve, race.Course, weatherKey);
        this.goldId = hand.Gold.CardId;

        if (!hand.Gold.IsEmpty)
        {
            var card = resolve(hand.Gold.CardId);
            if (card is null || !card.IsGold)
            {
                this.terminal = true;
                this.Log(RaceSupportOutcome.Denied, card is null ? RaceSupportReasons.UnknownCard : RaceSupportReasons.WrongBand, 0f, 0f);
            }
            else
            {
                this.gold = card;
                this.goldAmount = RaceCardLevels.Effective(card.Operation.Amount, hand.Gold.Level);
                var domain = RaceCardSeedDomains.GoldDomain(supportVersion, seed, slot, card.Id, card.EffectVersion);
                var u = RaceCardSeedDomains.Unit(RaceCardSeedDomains.Hash(domain + RaceCardSeedDomains.ThresholdSuffix));
                this.threshold = ThresholdBase + (ThresholdFocusShare * this.focus) + (ThresholdSpread * (1f - this.focus) * ((2f * u) - 1f));
                this.cadence = Math.Max(1, (int)MathF.Round(CadenceBase - (CadenceFocusShare * this.focus)));
                this.phase = (int)(RaceCardSeedDomains.Hash(domain + RaceCardSeedDomains.CadenceSuffix) % (uint)this.cadence);
                this.goldEligible = card.Eligibility.Fits(race.Course, weatherKey);
                if (!this.goldEligible)
                {
                    this.terminal = true;
                    this.Log(RaceSupportOutcome.Denied, RaceSupportReasons.StaticIneligible, 0f, this.threshold);
                }
            }
        }

        // The engine reads Silver.Stride and Silver.Gate only inside Create, so these two channels are written
        // straight onto the fields it derived from them.
        var stride = 0f;
        var gate = 0f;
        foreach (var silver in this.silvers)
        {
            if (!silver.Eligible)
            {
                continue;
            }

            switch (silver.Card.Operation.Kind)
            {
                case RaceCardOperationKinds.SilverStrideStart:
                    stride += silver.Amount;
                    break;
                case RaceCardOperationKinds.SilverGateStart:
                    gate += silver.Amount;
                    break;
            }
        }

        if (stride > 0f)
        {
            this.runner.VTop *= 1f + MathF.Min(RaceCardOperationKinds.StrideCap, stride);
        }

        if (gate > 0f)
        {
            this.runner.GateDelay = MathF.Max(MinGateDelay, this.runner.GateDelay * (1f - MathF.Min(RaceCardOperationKinds.GateCap, gate)));
        }
    }

    public int Slot { get; }

    /// <summary>The Gold this controller carries, or null when the slot is empty, unknown or the wrong band.</summary>
    public RaceCard? Gold => this.gold;

    public bool Fired { get; private set; }

    public bool Opened => this.opened >= 0;

    /// <summary>Whether the Gold has reached its one terminal line: fired, or refused for good.</summary>
    public bool Terminal => this.terminal;

    public float Focus => this.focus;

    /// <summary>The utility a decision beat must reach before the window's late relaxation.</summary>
    public float Threshold => this.threshold;

    /// <summary>Decision beats come every this many ticks, on the tick congruent to <see cref="Phase"/>.</summary>
    public int Cadence => this.cadence;

    public int Phase => this.phase;

    /// <summary>This runner's own lines, in the order they were logged.</summary>
    public IReadOnlyList<RaceSupportEvent> Events => this.events;

    /// <summary>The observation half of a tick: retire an expired surge or burst, track the transient predicates,
    /// rebuild the Silver channels from scratch, and decide whether the Gold fires this tick. Nothing here changes
    /// what another runner's predicates read; the decision waits for <see cref="ApplyPending"/>.</summary>
    public void BeforeStep()
    {
        var r = this.runner;
        if (this.ownPace && (this.race.Tick >= this.paceEnd || r.Finished))
        {
            this.EndPace();
        }
        else if (this.ownPace)
        {
            r.BurstCd = MathF.Max(r.BurstCd, SurgeCooldownTicks * AetherRaceLive.Dials.Dt);
        }

        this.observedAcceleration = (r.V - this.previousSpeed) / AetherRaceLive.Dials.Dt;
        this.previousSpeed = r.V;
        if (this.ownBurst && r.BurstT <= 0f)
        {
            r.LastLightBurst = this.previousLastLight;
            r.BurstCd = this.previousBurstCooldown;
            this.ownBurst = false;
        }
        else if (this.ownBurst)
        {
            r.BurstCd = r.BurstT + AetherRaceLive.Dials.Dt;
        }

        var here = this.race.Track.At(r.S);
        var bendThreshold = DefaultBendKappa;
        var climbThreshold = DefaultGradeThreshold;
        if (this.gold is { Trigger.Kind: RaceCardTriggerKinds.CornerExit } cornerGold)
        {
            bendThreshold = cornerGold.Trigger.Threshold ?? DefaultBendKappa;
        }

        if (this.gold is { Trigger.Kind: RaceCardTriggerKinds.ClimbExit } climbGold)
        {
            climbThreshold = climbGold.Trigger.Threshold ?? DefaultGradeThreshold;
        }

        if (this.previousStumble > 0f && r.StumbleT <= 0f)
        {
            this.lastStumbleEnd = this.race.Tick;
        }

        this.previousStumble = r.StumbleT;
        if (r.BlockedBy >= 0)
        {
            this.lastBlocked = this.race.Tick;
        }

        if (MathF.Abs(here.Kappa) >= bendThreshold)
        {
            this.lastBend = this.race.Tick;
        }

        if (here.Grade >= climbThreshold)
        {
            this.lastClimb = this.race.Tick;
        }

        r.Silver.Tank = 0f;
        r.Silver.Craft = 0f;
        r.Silver.Spark = 0f;
        foreach (var silver in this.silvers)
        {
            if (!silver.Eligible || !this.Match(silver.Card.Trigger, here))
            {
                continue;
            }

            switch (silver.Card.Operation.Kind)
            {
                case RaceCardOperationKinds.SilverTank:
                    r.Silver.Tank = MathF.Min(RaceCardOperationKinds.TankCap, r.Silver.Tank + silver.Amount);
                    break;
                case RaceCardOperationKinds.SilverCraft:
                    r.Silver.Craft = MathF.Min(RaceCardOperationKinds.CraftCap, r.Silver.Craft + silver.Amount);
                    break;
                case RaceCardOperationKinds.SilverSpark:
                    r.Silver.Spark = MathF.Min(RaceCardOperationKinds.SparkCap, r.Silver.Spark + silver.Amount);
                    break;
            }
        }

        if (this.gold is null || this.terminal || !this.goldEligible)
        {
            return;
        }

        var trigger = this.gold.Trigger;
        var progress = r.S / this.race.Track.Length;
        if (r.Finished || progress > trigger.Max || this.race.Tick > this.closeTick)
        {
            this.terminal = true;
            this.Log(RaceSupportOutcome.Denied, this.TerminalDenial(), 0f, this.threshold);
            return;
        }

        var matches = this.Match(trigger, here);
        if (matches && this.opened < 0)
        {
            this.opened = this.race.Tick;
            if (trigger.WindowSeconds is { } seconds)
            {
                this.closeTick = this.opened + (int)MathF.Round(seconds / AetherRaceLive.Dials.Dt);
            }

            this.Log(RaceSupportOutcome.Window, RaceSupportReasons.FirstEligibleWindow, 0f, this.threshold);
        }

        if (this.opened < 0 || this.race.Tick % this.cadence != this.phase)
        {
            return;
        }

        if (!matches)
        {
            this.lastDenial = RaceSupportReasons.HardPredicateClosed;
            return;
        }

        var (allowed, reason, utility) = this.OperationUtility();
        if (!allowed)
        {
            this.lastDenial = reason;
            return;
        }

        var elapsed = this.closeTick != int.MaxValue
            ? (float)(this.race.Tick - this.opened) / Math.Max(1, this.closeTick - this.opened)
            : (progress - trigger.Min) / MathF.Max(MinWindowSpan, trigger.Max - trigger.Min);
        var relax = Math.Clamp((elapsed - RelaxFrom) / RelaxSpan, 0f, 1f);
        var needed = this.threshold + ((MathF.Min(this.threshold, RelaxFloor) - this.threshold) * relax);
        if (utility < needed)
        {
            this.lastDenial = RaceSupportReasons.UtilityBelowThreshold;
            return;
        }

        this.pending = true;
        this.pendingUtility = utility;
        this.pendingThreshold = needed;
    }

    /// <summary>The application half of a tick, run only after every runner has observed the same state.</summary>
    public void ApplyPending()
    {
        if (!this.pending)
        {
            return;
        }

        this.pending = false;
        this.Apply();
        var r = this.runner;
        this.Fired = true;
        this.terminal = true;
        r.Gold = this.gold!.Id;
        r.GoldFired = true;
        this.Log(RaceSupportOutcome.Fired, RaceSupportReasons.Applied, this.pendingUtility, this.pendingThreshold);
    }

    /// <summary>Ends the race for this controller: restores anything a surge or burst still owns and writes the
    /// terminal line of a Gold that never fired. Idempotent.</summary>
    public void Complete()
    {
        if (this.ownPace)
        {
            this.EndPace();
        }

        if (this.ownBurst)
        {
            this.runner.LastLightBurst = this.previousLastLight;
            this.runner.BurstCd = this.previousBurstCooldown;
            this.runner.BurstT = 0f;
            this.ownBurst = false;
        }

        if (this.gold is null || this.terminal)
        {
            return;
        }

        this.terminal = true;
        this.Log(RaceSupportOutcome.Denied, this.TerminalDenial(), 0f, this.threshold);
    }

    private static SilverEntry[] ResolveSilvers(RaceCardHand hand, Func<string, RaceCard?> resolve, AetherRaceLive.CourseDef course, string weatherKey)
    {
        var entries = new List<SilverEntry>(RaceCardHandRules.SilverSlotCount);
        foreach (var slot in hand.Silvers)
        {
            if (slot.IsEmpty)
            {
                continue;
            }

            var card = resolve(slot.CardId);
            if (card is null || card.IsGold)
            {
                continue;
            }

            entries.Add(new SilverEntry(card, RaceCardLevels.Effective(card.Operation.Amount, slot.Level), card.Eligibility.Fits(course, weatherKey)));
        }

        return entries.ToArray();
    }

    private string TerminalDenial()
    {
        return this.opened >= 0 ? RaceSupportReasons.TimingMissed(this.lastDenial) : RaceSupportReasons.TriggerNeverMet;
    }

    private bool Match(RaceCardTrigger trigger, AetherRaceLive.TrackSample here)
    {
        var r = this.runner;
        var p = r.S / this.race.Track.Length;
        if (r.Finished || p < trigger.Min || p > trigger.Max)
        {
            return false;
        }

        var threshold = trigger.Threshold;
        return trigger.Kind switch
        {
            RaceCardTriggerKinds.Always or RaceCardTriggerKinds.Early or RaceCardTriggerKinds.Late => true,
            RaceCardTriggerKinds.Uphill => here.Grade >= (threshold ?? DefaultGradeThreshold),
            RaceCardTriggerKinds.Downhill => here.Grade <= -(threshold ?? DefaultGradeThreshold),
            RaceCardTriggerKinds.Bend => MathF.Abs(here.Kappa) >= (threshold ?? DefaultBendKappa),
            RaceCardTriggerKinds.Straight => MathF.Abs(here.Kappa) < (threshold ?? DefaultStraightKappa),
            RaceCardTriggerKinds.Drafting => r.Drafting >= (threshold ?? DefaultDraftingShare),
            RaceCardTriggerKinds.Blocked => r.BlockedBy >= 0 && r.BlockedT >= (threshold ?? DefaultBlockedSeconds),
            RaceCardTriggerKinds.BehindLeader => this.LeaderGap() > 0f && this.LeaderGap() >= MathF.Max(0f, threshold ?? DefaultLeaderGap),
            RaceCardTriggerKinds.LowStamina => r.Stamina <= (threshold ?? DefaultLowStamina),
            RaceCardTriggerKinds.Stumbled => r.StumbleT > (threshold ?? DefaultStumbleSeconds),
            RaceCardTriggerKinds.Recovering => r.StumbleT <= 0f && this.race.Tick - this.lastStumbleEnd <= RecoveringTicks,
            RaceCardTriggerKinds.CornerExit => this.race.Tick - this.lastBend <= ExitTicks && MathF.Abs(here.Kappa) < (threshold ?? DefaultBendKappa) && this.ExitWantsSpeed(),
            RaceCardTriggerKinds.ClimbExit => this.race.Tick - this.lastClimb <= ExitTicks && here.Grade < (threshold ?? DefaultGradeThreshold) && this.ExitWantsSpeed(),
            RaceCardTriggerKinds.Pack => this.PackCount() >= (threshold ?? DefaultPackCount),
            RaceCardTriggerKinds.Opening => this.race.Tick - this.lastBlocked <= ExitTicks && r.BlockedBy < 0 && this.LeaderGap() > 0f && this.LeaderGap() >= MathF.Max(0f, threshold ?? DefaultOpeningGap),
            _ => false,
        };
    }

    private bool ExitWantsSpeed()
    {
        return this.gold?.Operation.Kind == RaceCardOperationKinds.PaceSurge || this.runner.V < ExitSpeedShare * this.runner.VTop;
    }

    private float AccelerationUtility()
    {
        var r = this.runner;
        return Math.Clamp(this.observedAcceleration / (AetherRaceLive.Dials.Accel * (AccelBaseShare + (AccelPowerShare * r.CPower))), 0f, 1f);
    }

    private float RemainingUtility(float durationSeconds)
    {
        var r = this.runner;
        return Math.Clamp((this.race.Track.Length - r.S) / MathF.Max(MinSurgeDistance, r.VTop * durationSeconds), 0f, 1f);
    }

    private bool CloserKickDue()
    {
        var r = this.runner;
        return AetherRaceLive.PhaseAt(this.race.Course.Category, r.S / this.race.Track.Length) == AetherRaceLive.RacePhase.Kick
            && r.WCloser > CloserKickWeight && r.Stamina > CloserKickStamina;
    }

    private bool PathBends(float lookahead)
    {
        for (var d = 0f; d <= lookahead; d += PathSampleStep)
        {
            if (MathF.Abs(this.race.Track.At(this.runner.S + d).Kappa) >= StraightPathKappa)
            {
                return true;
            }
        }

        return false;
    }

    private (bool Allowed, string Reason, float Utility) OperationUtility()
    {
        var r = this.runner;
        var op = this.gold!.Operation;
        var amount = this.goldAmount;
        if (r.Finished || this.race.Time < r.GateDelay)
        {
            return (false, RaceSupportReasons.NotRunning, 0f);
        }

        switch (op.Kind)
        {
            case RaceCardOperationKinds.RestoreStamina:
                return r.Stamina + amount <= 1f && amount > 0f
                    ? (true, string.Empty, Math.Clamp((1f - r.Stamina) / (2f * amount), 0f, 1f))
                    : (false, RaceSupportReasons.NoRefillCapacity, 0f);
            case RaceCardOperationKinds.SoftenStumble:
                return r.StumbleT > 0f && amount > 0f
                    ? (true, string.Empty, 1f)
                    : (false, RaceSupportReasons.NoCurrentStumble, 0f);
            case RaceCardOperationKinds.Rush:
                return r.RushT <= 0f && r.StumbleT <= 0f && r.BlockedBy < 0 && r.V < SpeedHeadroomShare * r.VTop
                    ? (true, string.Empty, this.AccelerationUtility())
                    : (false, RaceSupportReasons.NoAccelerationHeadroom, 0f);
            case RaceCardOperationKinds.RecoverSpeed:
                if (r.RushT > 0f || r.BurstT > 0f || r.StumbleT > 0f || r.BlockedBy >= 0 || r.Fade)
                {
                    return (false, RaceSupportReasons.MomentumChannelBusy, 0f);
                }

                if (r.Stamina <= FuelFloor + op.Cost)
                {
                    return (false, RaceSupportReasons.InsufficientFuel, 0f);
                }

                if ((SpeedHeadroomShare * r.VTop) - r.V < MomentumMinHeadroom)
                {
                    return (false, RaceSupportReasons.NoMomentumHeadroom, 0f);
                }

                if (this.PathBends(MomentumLookahead))
                {
                    return (false, RaceSupportReasons.MomentumPathNotStraight, 0f);
                }

                return (true, string.Empty, this.AccelerationUtility());
            case RaceCardOperationKinds.PaceSurge:
                if (r.RushT > 0f || r.BurstT > 0f || r.BurstCd > 0f || r.StumbleT > 0f || r.BlockedBy >= 0 || r.Fade)
                {
                    return (false, RaceSupportReasons.SurgeChannelBusy, 0f);
                }

                if (r.Stamina <= FuelFloor + op.Cost)
                {
                    return (false, RaceSupportReasons.InsufficientFuel, 0f);
                }

                if (r.V < SurgeMinSpeedShare * r.VTop)
                {
                    return (false, RaceSupportReasons.SurgeSpeedTooLow, 0f);
                }

                if (this.CloserKickDue())
                {
                    return (false, RaceSupportReasons.NaturalCloserKickDue, 0f);
                }

                return (true, string.Empty, this.RemainingUtility(op.DurationSeconds));
            case RaceCardOperationKinds.Ghost:
                return r.GhostT <= 0f && r.StumbleT <= 0f && (r.BlockedBy >= 0 || this.Neighbours() >= 2)
                    ? (true, string.Empty, Math.Clamp((r.BlockedT / GhostBlockedSpan) + (this.Neighbours() * GhostNeighbourWeight), 0f, 1f))
                    : (false, RaceSupportReasons.NoTrafficPressure, 0f);
            case RaceCardOperationKinds.NativeBurst:
                if (r.BurstT > 0f || r.BurstCd > 0f || r.RushT > 0f || r.StumbleT > 0f || r.BlockedBy >= 0)
                {
                    return (false, RaceSupportReasons.BurstOrSetbackBusy, 0f);
                }

                if (r.Stamina <= FuelFloor + op.Cost || r.Fade)
                {
                    return (false, RaceSupportReasons.InsufficientFuel, 0f);
                }

                if (this.CloserKickDue())
                {
                    return (false, RaceSupportReasons.NaturalCloserKickDue, 0f);
                }

                var look = MathF.Min(BurstLookMax, MathF.Max(BurstLookMin, r.V * op.DurationSeconds));
                if (this.PathBends(look))
                {
                    return (false, RaceSupportReasons.BurstPathNotStraight, 0f);
                }

                if (this.gold.Trigger.Kind is RaceCardTriggerKinds.BehindLeader or RaceCardTriggerKinds.Opening)
                {
                    var gap = this.NearestAhead();
                    return float.IsFinite(gap)
                        ? (true, string.Empty, Math.Clamp(1f - (gap / RivalGapSpan), 0f, 1f))
                        : (false, RaceSupportReasons.NoRivalAhead, 0f);
                }

                return (true, string.Empty, this.RemainingUtility(op.DurationSeconds));
            default:
                return (false, RaceSupportReasons.UnsupportedOperation, 0f);
        }
    }

    private void Apply()
    {
        var r = this.runner;
        var op = this.gold!.Operation;
        var amount = this.goldAmount;
        switch (op.Kind)
        {
            case RaceCardOperationKinds.Rush:
                r.RushT = op.DurationSeconds;
                break;
            case RaceCardOperationKinds.RecoverSpeed:
                r.V = MathF.Min(SpeedHeadroomShare * r.VTop, r.V + amount);
                r.Stamina -= op.Cost;
                break;
            case RaceCardOperationKinds.PaceSurge:
                this.previousPaceTop = r.VTop;
                this.previousPaceCooldown = r.BurstCd;
                this.paceEnd = this.race.Tick + Math.Max(1, (int)MathF.Round(op.DurationSeconds / AetherRaceLive.Dials.Dt));
                this.ownPace = true;
                r.VTop *= 1f + amount;
                r.BurstCd = SurgeCooldownTicks * AetherRaceLive.Dials.Dt;
                r.Stamina -= op.Cost;
                break;
            case RaceCardOperationKinds.Ghost:
                r.GhostT = op.DurationSeconds;
                break;
            case RaceCardOperationKinds.NativeBurst:
                this.previousBurstCooldown = r.BurstCd;
                this.previousLastLight = r.LastLightBurst;
                r.BurstT = op.DurationSeconds;
                r.BurstCd = op.DurationSeconds + AetherRaceLive.Dials.Dt;
                this.ownBurst = true;
                // The half fade lift, never the ordinary burst's full exemption.
                r.LastLightBurst = true;
                r.Stamina -= op.Cost;
                break;
            case RaceCardOperationKinds.RestoreStamina:
                r.Stamina = MathF.Min(1f, r.Stamina + amount);
                r.Fade = false;
                break;
            case RaceCardOperationKinds.SoftenStumble:
                r.StumbleT = MathF.Max(0f, r.StumbleT - amount);
                break;
        }
    }

    private float LeaderGap()
    {
        var leader = float.NegativeInfinity;
        foreach (var other in this.race.Runners)
        {
            if (other.Idx != this.runner.Idx)
            {
                leader = MathF.Max(leader, other.S);
            }
        }

        return leader - this.runner.S;
    }

    private int PackCount()
    {
        var count = 0;
        foreach (var other in this.race.Runners)
        {
            if (!other.Finished && other.Idx != this.runner.Idx && MathF.Abs(other.S - this.runner.S) <= PackReach)
            {
                count++;
            }
        }

        return count;
    }

    private void EndPace()
    {
        this.runner.VTop = this.previousPaceTop;
        this.runner.BurstCd = this.previousPaceCooldown;
        this.ownPace = false;
    }

    private float NearestAhead()
    {
        var gap = float.PositiveInfinity;
        foreach (var other in this.race.Runners)
        {
            if (!other.Finished && other.Idx != this.runner.Idx && other.S > this.runner.S)
            {
                gap = MathF.Min(gap, other.S - this.runner.S);
            }
        }

        return gap;
    }

    private int Neighbours()
    {
        var count = 0;
        foreach (var other in this.race.Runners)
        {
            if (!other.Finished && other.Idx != this.runner.Idx
                && MathF.Abs(other.S - this.runner.S) < NeighbourReach && MathF.Abs(other.Lat - this.runner.Lat) < NeighbourLateral)
            {
                count++;
            }
        }

        return count;
    }

    private void Log(RaceSupportOutcome outcome, string reason, float utility, float needed)
    {
        var line = new RaceSupportEvent(
            this.Slot,
            this.goldId,
            this.race.Tick,
            this.race.Time,
            outcome,
            reason,
            this.race.Track.At(this.runner.S).Section,
            this.runner.S / this.race.Track.Length,
            utility,
            needed);
        this.events.Add(line);
        this.observer?.Invoke(line);
    }
}
