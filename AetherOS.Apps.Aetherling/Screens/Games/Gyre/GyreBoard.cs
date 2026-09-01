using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AetherLove.Shared.Aetherling;

namespace AetherOS.Apps.Aetherling.Screens.Games.Gyre;

internal enum GyrePowerup
{
    Aetherlight = 0,
    Driftmoss = 1,
    Recoil = 2,
    Shatterstone = 3,
    Threadneedle = 4,
    Sparkfall = 5,
}

internal enum GyreEventKind
{
    Pop,
    Slam,
    Swallow,
    PowerTaken,
    LifeLost,
    StageCleared,
    ExtraLife,
    DudCrumble,
    PowerFired,
}

internal readonly record struct GyreEvent(
    GyreEventKind Kind,
    Vector2 At,
    int Colour,
    int Count,
    int Cascade,
    int Points,
    GyrePowerup Powerup,
    AetherlingElement Element);

internal sealed class GyreMarble
{
    public int Id;
    public int Kind;
    public bool Dud;
    public float D;

    /// <summary>A powerup riding IN the chain. It keeps its colour and its place in the line, so taking it
    /// is a matter of matching it out rather than of chasing a token around the board.</summary>
    public GyrePowerup? Power;
}

internal sealed class GyreChain
{
    public required GyrePath Path;

    /// <summary>Ascending by distance: index 0 is the rearmost marble at the mouth, the last is the
    /// front marble nearest the fissure.</summary>
    public readonly List<GyreMarble> Marbles = [];

    public float Recoil;

    public float FrontFrac => Marbles.Count == 0 ? 0f : Path.Frac(Marbles[^1].D);
}

/// <summary>The Gyre simulation: chains crawling their paths, insertion, matching, gap-back slams,
/// powerups, duds, the element powers and the endless ramp. No opinion about pictures or sound; every
/// consequence comes back as an event for the feel layer to spend.</summary>
internal sealed class GyreBoard
{
    private const float Spacing = GyreStages.MarbleSpacing;
    private const float MagnetExtraSpeed = 600f;
    private const float SlamPushback = 90f;
    private const float RecoilDecay = 3.2f;

    /// <summary>How long after a pop another one still counts as the same cascade. A cascade is a chain of
    /// consequences, so it is measured on the clock and not on which code path did the popping.</summary>
    private const float CascadeWindowSeconds = 1.35f;

    /// <summary>The fissure's shortest bite-to-bite beat. Marbles arrive a spacing apart so the chain's
    /// own pace usually spaces them wider; this only stops a shove from feeding several in one frame.</summary>
    private const float SwallowInterval = 0.24f;

    /// <summary>The stage that teaches. On it the powerups are DEALT rather than rolled: one of each, in a
    /// shuffled order, spaced evenly down the pool, so nobody finishes their first stage having met two of
    /// the six. Every stage after it goes back to the roll.</summary>
    private const int TeachingStage = 1;

    /// <summary>What share of a stage's per-pop powerup chance is rolled per MARBLE instead, now that a
    /// powerup rides in the chain rather than falling out of a pop.</summary>
    private const float PowerupPerMarble = 0.30f;

    /// <summary>Applied to every speed the stage file names. The authored numbers were tuned before the
    /// chain played the way it does now: a fifth more pace read right on paper and a tenth too fast in the
    /// hand, so 2026-08-28 took ten percent back off it.</summary>
    private const float SpeedScale = 1.08f;

    /// <summary>The end-of-stage surge, once the mouth has nothing left to feed. It exists so a stage cannot
    /// stall forever, not to take the finish away: the stage files all author it at about 1.8x, which snapped
    /// in on one frame and read as the last marbles doubling their speed for no reason a player could see.
    /// It is capped to <see cref="MaxSurgeFactor"/> of the stage's own pace and eased in over
    /// <see cref="SurgeRampSeconds"/>.</summary>
    private const float MaxSurgeFactor = 1.15f;
    private const float SurgeRampSeconds = 2.5f;

    /// <summary>How much of its own place a wedged marble takes on the frame it lands; the rest is opened
    /// by <see cref="Settle"/>, and how fast that happens.</summary>
    private const float WedgeSeat = 0.35f;
    private const float SettleSpeed = 700f;

    /// <summary>The most of a wedge's room the section in FRONT may give up. Without it a shot into the
    /// head of a chain already at the fissure would push it in.</summary>
    private const float MaxForwardGive = 0.75f;

    /// <summary>Applied to every stage's marble count. A stage's LENGTH is its pool: the mouth can only
    /// feed as fast as the chain clears its own width, so the pool sets the floor on how long a stage runs.
    /// The paths themselves are not a lever, because each board is painted with its groove baked in and
    /// shortening a spline would leave the chain running outside its own picture.</summary>
    private const float PoolScale = 0.70f;

    public readonly List<GyreChain> Chains = [];
    public readonly List<GyreEvent> Events = [];

    private IReadOnlyList<GyreStageDto> _stages = [];
    private GyreStageDto? _stage;
    private Random _rng = new();
    private int _nextId;
    private int _pool;
    private float _stageTime;
    private int _cascade;
    private float _cascadeUntil;
    private float _swallowNext;
    private float _surgeAt = -1f;
    private int _fed;
    private int _poolTotal;
    private readonly List<(int At, GyrePowerup Kind)> _taught = [];
    private int _lifeThresholds;

    public int Score { get; private set; }

    public int Stage { get; private set; } = 1;

    /// <summary>The run's health. A marble through the fissure drains one point, zero ends the run, and
    /// NOTHING resets: no lives, no stage replay. Points buy it back, capped at full.</summary>
    public int Hp { get; private set; }

    public int DeepestCascade { get; private set; }

    public bool Over => Hp <= 0;

    public bool Endless => Stage >= GyreStages.EndlessStage;

    public bool Surging => !Endless && _pool <= 0;

    public float FrozenLeft { get; private set; }

    public float SlowLeft { get; private set; }

    public float DoubleLeft { get; private set; }

    public float AimLeft { get; private set; }

    public int ShatterShots { get; private set; }

    public int NeedleShots { get; private set; }

    public GyreStageDto? StageData => _stage;

    /// <summary>How many colours are in play. The first two stages hold at three so the shot is learned
    /// before the palette grows, then one more every three stages up to the full six. A stage file may ask
    /// for fewer than the cap; it may never ask for more.</summary>
    /// <summary>How many times The Core has stepped up. Zero everywhere else.</summary>
    public int EndlessSteps => Endless ? _fed / GyreStages.EndlessStepMarbles : 0;

    public int Colours => Endless
        ? Math.Clamp(GyreStages.EndlessStartColours + EndlessSteps, 2, ColourCap(Stage))
        : Math.Clamp(_stage?.Colours ?? 3, 2, ColourCap(Stage));

    /// <summary>One colour per five stages, so a band is a difficulty step the player can feel. A stage
    /// asking for more than its band allows is clamped down to it, which is what keeps the ladder the
    /// authority over stages.json.</summary>
    public static int ColourCap(int stage) => stage switch
    {
        <= 5 => 3,
        <= 10 => 4,
        <= 15 => 5,
        _ => 6,
    };

    public void Reset(Random rng, IReadOnlyList<GyreStageDto> stages)
    {
        _rng = rng;
        _stages = stages;
        Score = 0;
        Stage = 1;
        Hp = GameScoring.GyreMaxHp;
        DeepestCascade = 0;
        _lifeThresholds = 0;
        Events.Clear();
        LoadStage(1);
    }

    private void LoadStage(int stage)
    {
        Stage = stage;
        _stage = _stages.FirstOrDefault(s => s.Id == stage);
        _fullPalette = null;
        Chains.Clear();
        _stageTime = 0f;
        _cascade = 0;
        _cascadeUntil = 0f;
        _swallowNext = 0f;
        _surgeAt = -1f;
        FrozenLeft = 0f;
        SlowLeft = 0f;
        DoubleLeft = 0f;
        AimLeft = 0f;
        ShatterShots = 0;
        NeedleShots = 0;
        if (_stage is null)
        {
            Hp = 0;
            return;
        }
        _pool = StagePool();
        _poolTotal = _pool;
        _fed = 0;
        PlanTaughtPowerups();
        foreach (var p in _stage.Paths)
        {
            Chains.Add(new GyreChain { Path = new GyrePath(p) });
        }
    }

    /// <summary>Lays out one of every powerup across the teaching stage's pool: shuffled so the order is
    /// not the enum's, and spaced on the half-step so the first is met early and the last is not the very
    /// last marble fed.</summary>
    private void PlanTaughtPowerups()
    {
        _taught.Clear();
        if (Stage != TeachingStage || _stage is null)
        {
            return;
        }

        var kinds = new List<GyrePowerup>();
        for (var i = 0; i < 6; i++)
        {
            kinds.Add((GyrePowerup)i);
        }
        for (var i = kinds.Count - 1; i > 0; i--)
        {
            var j = _rng.Next(i + 1);
            (kinds[i], kinds[j]) = (kinds[j], kinds[i]);
        }

        var pool = StagePool();
        for (var i = 0; i < kinds.Count; i++)
        {
            _taught.Add(((int)((i + 0.5f) * pool / kinds.Count), kinds[i]));
        }
    }

    /// <summary>How many marbles this stage deals. The Core never runs out.</summary>
    private int StagePool() => Endless || _stage is null
        ? int.MaxValue
        : Math.Max(3, (int)MathF.Round(_stage.Marbles * PoolScale));

    public float CurrentSpeed
    {
        get
        {
            if (_stage is null)
            {
                return 0f;
            }
            var speed = _stage.Speed * SpeedScale;
            if (Endless)
            {
                var steps = _fed / GyreStages.EndlessStepMarbles;
                speed = MathF.Min(GyreStages.EndlessSpeedCap, speed + (steps * GyreStages.EndlessSpeedStep));
            }
            else if (Surging)
            {
                var target = MathF.Min(_stage.SurgeSpeed, _stage.Speed * MaxSurgeFactor) * SpeedScale;
                var into = _surgeAt < 0f
                    ? 0f
                    : Math.Clamp((_stageTime - _surgeAt) / SurgeRampSeconds, 0f, 1f);
                speed += (target - speed) * (into * into * (3f - (2f * into)));
            }
            if (SlowLeft > 0f)
            {
                speed *= 0.5f;
            }
            return speed;
        }
    }

    public void Update(float dt)
    {
        if (_stage is null || Over)
        {
            return;
        }
        _stageTime += dt;
        _swallowNext = MathF.Max(0f, _swallowNext - dt);
        FrozenLeft = MathF.Max(0f, FrozenLeft - dt);
        SlowLeft = MathF.Max(0f, SlowLeft - dt);
        DoubleLeft = MathF.Max(0f, DoubleLeft - dt);
        AimLeft = MathF.Max(0f, AimLeft - dt);

        if (Surging && _surgeAt < 0f)
        {
            _surgeAt = _stageTime;
        }

        var speed = CurrentSpeed;
        var frozen = FrozenLeft > 0f;
        foreach (var chain in Chains)
        {
            StepChain(chain, frozen ? 0f : speed, dt);
        }

        if (!frozen)
        {
            FeedMouths();
        }

        // The fissure is a leak, not a cliff. A marble that reaches the end goes in and costs one HP; the
        // chain plays on behind it and the stage never restarts. At zero the run is over, that is all.
        if (_swallowNext <= 0f)
        {
            foreach (var chain in Chains)
            {
                if (chain.Marbles.Count == 0 || chain.Marbles[^1].D < chain.Path.Length)
                {
                    continue;
                }
                var last = chain.Marbles.Count - 1;
                Events.Add(new GyreEvent(GyreEventKind.Swallow,
                    chain.Path.PosAt(chain.Path.Length), chain.Marbles[last].Dud ? -1 : chain.Marbles[last].Kind,
                    0, 0, 0, default, default));
                chain.Marbles.RemoveAt(last);
                Hp--;
                _swallowNext = SwallowInterval;
                if (Over)
                {
                    Events.Add(new GyreEvent(GyreEventKind.LifeLost,
                        chain.Path.PosAt(chain.Path.Length), 0, 0, 0, 0, default, default));
                    return;
                }
                break;
            }
        }

        if (!Endless && _pool <= 0 && Chains.All(c => c.Marbles.Count == 0))
        {
            ClearStage();
        }
    }

    private void StepChain(GyreChain chain, float speed, float dt)
    {
        var marbles = chain.Marbles;
        if (marbles.Count == 0)
        {
            chain.Recoil = 0f;
            return;
        }

        var shift = (speed * dt) - (chain.Recoil * dt);
        chain.Recoil = MathF.Max(0f, chain.Recoil - (chain.Recoil * RecoilDecay * dt));
        if (shift < 0f && marbles[0].D + shift < 0f)
        {
            shift = -marbles[0].D;
        }

        var groups = GroupRanges(marbles);
        foreach (var (start, end) in groups)
        {
            for (var i = start; i <= end; i++)
            {
                marbles[i].D += shift;
            }
        }

        // EVERY gap closes, not only a matching one. The front section always rolls back onto the rear;
        // whether the meeting pops is a separate question, answered by the match scan below. Closing only
        // matching gaps left a permanent hole in the chain after any pop whose two ends differed.
        if (speed > 0f)
        {
            for (var g = 0; g < groups.Count - 1; g++)
            {
                var rear = marbles[groups[g].End];
                var front = marbles[groups[g + 1].Start];
                var gap = front.D - rear.D - Spacing;
                if (gap <= 0f)
                {
                    continue;
                }

                var pull = MathF.Min(MagnetExtraSpeed * dt, gap);
                for (var i = groups[g + 1].Start; i <= groups[g + 1].End; i++)
                {
                    marbles[i].D -= pull;
                }

                if (gap - pull > 0.5f)
                {
                    continue;
                }

                // The seam just closed: that is the snap, and it pays when the two ends match.
                var matched = !rear.Dud && !front.Dud && rear.Kind == front.Kind;
                var points = matched ? Apply(GameScoring.GyreGapSlamBonus) : 0;
                if (matched)
                {
                    AddPoints(points);
                    chain.Recoil += SlamPushback;
                }
                Events.Add(new GyreEvent(GyreEventKind.Slam, chain.Path.PosAt(front.D),
                    matched ? front.Kind : -1, 0, _cascade, points, default, default));
            }
        }

        Settle(chain, dt);
        ResolveMatches(chain);
        CrumbleLoneDuds(chain);
        StopAtMouth(chain);
    }

    /// <summary>Pushes overlapping marbles apart at a finite rate, so a wedged shot ripples the queue open
    /// instead of displacing it in one frame.
    ///
    /// <para>The LIGHTER side gives. A queue cannot compress, so something has to move, and which side it
    /// is follows from how many marbles are on each: wedge near the head and the two or three ahead of it
    /// slide forward, wedge near the mouth and the short tail backs up. Shoving the whole chain backwards
    /// whatever you hit was arithmetically tidy and made no sense to look at.</para></summary>
    private static void Settle(GyreChain chain, float dt)
    {
        var marbles = chain.Marbles;
        var room = SettleSpeed * dt;
        for (var i = marbles.Count - 1; i > 0; i--)
        {
            var overlap = marbles[i - 1].D + Spacing - marbles[i].D;
            if (overlap <= 0f)
            {
                continue;
            }

            var move = MathF.Min(overlap, room);

            // Each side gives in proportion to how heavy the OTHER one is, and the two shares sum to one
            // because a queue cannot compress. The forward share is capped so a wedge at the head cannot
            // tip a chain that is already at the fissure over the edge.
            var frontGive = MathF.Min(MaxForwardGive, i / (float)marbles.Count);
            var rearGive = 1f - frontGive;
            for (var j = 0; j < i; j++)
            {
                marbles[j].D -= move * rearGive;
            }
            for (var j = i; j < marbles.Count; j++)
            {
                marbles[j].D += move * frontGive;
            }
        }
    }

    /// <summary>The mouth is a wall, not an opening the chain can reverse into. A wedge, a recoil or a wind
    /// shove all push backwards, and without a stop the rear marbles slide back inside the hole they came
    /// out of, which reads as the chain hiding rather than as it being pushed. The far end gives instead,
    /// which is what a stop actually means.</summary>
    private static void StopAtMouth(GyreChain chain)
    {
        if (chain.Marbles.Count == 0 || chain.Marbles[0].D >= 0f)
        {
            return;
        }
        var push = -chain.Marbles[0].D;
        foreach (var m in chain.Marbles)
        {
            m.D += push;
        }
    }

    /// <summary>Pops the first run of three or more touching marbles of one colour, wherever it is in the
    /// chain. One per frame per chain, which is what gives a cascade its rhythm: pop, the gap closes, the
    /// next run meets, pop again.
    ///
    /// <para>This scan is the ONLY thing that pops a run. The old code popped only at the shot's insertion
    /// point and at a slam whose test could never be true, so a run formed by two groups meeting sat in the
    /// chain unresolved until some later shot happened to touch it.</para></summary>
    private bool ResolveMatches(GyreChain chain)
    {
        var marbles = chain.Marbles;
        var start = 0;
        for (var i = 1; i <= marbles.Count; i++)
        {
            var runs = i < marbles.Count
                && !marbles[i].Dud
                && !marbles[start].Dud
                && marbles[i].Kind == marbles[start].Kind
                && marbles[i].D - marbles[i - 1].D <= Spacing + 0.5f;
            if (runs)
            {
                continue;
            }

            if (i - start >= 3 && !marbles[start].Dud)
            {
                Pop(chain, start, i - start);
                return true;
            }
            start = i;
        }
        return false;
    }

    /// <summary>Takes a run out: the score, the cascade, the powerups riding in it, and the event the feel
    /// layer spends. Cascade depth is measured on the clock, so a pop that follows another one within the
    /// window counts as the same run of consequences whatever caused it.</summary>
    private void Pop(GyreChain chain, int lo, int count)
    {
        var marbles = chain.Marbles;
        _cascade = _stageTime < _cascadeUntil
            ? Math.Min(_cascade + 1, GameScoring.GyreCascadeCap)
            : 1;
        _cascadeUntil = _stageTime + CascadeWindowSeconds;
        DeepestCascade = Math.Max(DeepestCascade, _cascade);

        var centre = chain.Path.PosAt(marbles[lo + (count / 2)].D);
        var kind = marbles[lo].Kind;
        var points = Apply((GameScoring.GyreMatch3
            + (GameScoring.GyrePerExtraMarble * (count - 3))) * _cascade);
        AddPoints(points);
        Events.Add(new GyreEvent(GyreEventKind.Pop, centre, kind, count, _cascade, points, default, default));

        for (var i = lo; i < lo + count; i++)
        {
            if (marbles[i].Power is { } power)
            {
                GrantPower(power, chain.Path.PosAt(marbles[i].D));
            }
        }
        marbles.RemoveRange(lo, count);
    }

    private static List<(int Start, int End)> GroupRanges(List<GyreMarble> marbles)
    {
        var groups = new List<(int, int)>();
        var start = 0;
        for (var i = 1; i < marbles.Count; i++)
        {
            if (marbles[i].D - marbles[i - 1].D > Spacing + 0.5f)
            {
                groups.Add((start, i - 1));
                start = i;
            }
        }
        groups.Add((start, marbles.Count - 1));
        return groups;
    }

    private void FeedMouths()
    {
        if (_stage is null || _pool <= 0)
        {
            return;
        }
        foreach (var (chain, dto) in Chains.Zip(_stage.Paths))
        {
            if (_stageTime < dto.SpawnDelay || _pool <= 0)
            {
                continue;
            }
            if (dto.SpawnAfter > 0f && !Endless && _fed < _poolTotal * dto.SpawnAfter)
            {
                continue;
            }
            if (dto.SpawnUntil > 0f && _stageTime >= dto.SpawnUntil)
            {
                continue;
            }
            if (chain.Marbles.Count > 0 && chain.Marbles[0].D < Spacing)
            {
                continue;
            }

            // Touching the chain it joins, never a marble's width behind it. Feeding at the mouth itself
            // opened a gap the moment the chain had crept forward at all, and every fed marble then
            // announced itself as a seam closing.
            var at = chain.Marbles.Count > 0 ? chain.Marbles[0].D - Spacing : 0f;
            chain.Marbles.Insert(0, NewMarble(at, ThirdOfAKindAt(chain), LanePalette(dto)));
            _fed++;
            if (!Endless)
            {
                _pool--;
            }
        }
    }

    /// <summary>The colour the mouth must NOT deal, because two of it are already sitting there and a
    /// third would pop on arrival. A match is something the player makes; the mouth never hands one out.</summary>
    private static int? ThirdOfAKindAt(GyreChain chain)
    {
        var marbles = chain.Marbles;
        if (marbles.Count < 2)
        {
            return null;
        }
        var a = marbles[0];
        var b = marbles[1];
        if (a.Dud || b.Dud || a.Kind != b.Kind || b.D - a.D > Spacing + 0.5f)
        {
            return null;
        }
        return a.Kind;
    }

    /// <summary>The colours a lane may deal: its own slice of the palette, or the whole palette when it
    /// declares none. A lane's slice is clipped to the stage's colour count, so the band cap still wins.</summary>
    private int[] LanePalette(GyrePathDto dto)
    {
        if (dto.Colours.Length == 0)
        {
            return FullPalette();
        }
        return dto.Colours.Where(c => c >= 0 && c < Colours).DefaultIfEmpty(0).ToArray();
    }

    /// <summary>Rebuilt whenever the count moves, because The Core's palette grows as it runs.</summary>
    private int[] FullPalette()
    {
        if (_fullPalette is null || _fullPalette.Length != Colours)
        {
            _fullPalette = Enumerable.Range(0, Colours).ToArray();
        }
        return _fullPalette;
    }

    private int[]? _fullPalette;

    private GyreMarble NewMarble(float d, int? forbid = null, int[]? palette = null)
    {
        var dud = _stage is not null && _rng.NextDouble() < _stage.DudChance;

        // A powerup is a marble like any other, colour and all: it queues in the line and is taken by
        // matching it out. The stage's chance is per POP in the file, so it is thinned for a per-marble
        // roll, and a dud never carries one.
        GyrePowerup? power = null;
        if (!dud && _taught.Count > 0)
        {
            // The teaching stage deals its own; the roll does not get a say on it either way.
            if (_fed >= _taught[0].At)
            {
                power = _taught[0].Kind;
                _taught.RemoveAt(0);
            }
        }
        else if (!dud && _stage is not null && Stage != TeachingStage
            && _rng.NextDouble() < _stage.PowerupChance * PowerupPerMarble)
        {
            power = (GyrePowerup)_rng.Next(6);
        }

        var lane = palette ?? FullPalette();
        var kind = lane[_rng.Next(lane.Length)];
        if (!dud && forbid is { } banned && lane.Length > 1 && lane.Contains(banned))
        {
            // Rolled off the remaining colours rather than re-rolled, so the ban cannot loop and the odds
            // stay even across everything it may still deal.
            var without = lane.Where(c => c != banned).ToArray();
            kind = without[_rng.Next(without.Length)];
        }

        return new GyreMarble
        {
            Id = _nextId++,
            Kind = kind,
            Dud = dud,
            D = d,
            Power = power,
        };
    }

    public int RollShotKind()
    {
        var present = new HashSet<int>();
        foreach (var chain in Chains)
        {
            foreach (var m in chain.Marbles)
            {
                if (!m.Dud)
                {
                    present.Add(m.Kind);
                }
            }
        }
        if (present.Count == 0)
        {
            return _rng.Next(Colours);
        }
        return present.ElementAt(_rng.Next(present.Count));
    }

    /// <summary>The first marble the shot's swept circle crosses, overpass layer first. Marbles hidden
    /// in a tunnel cannot be hit; empty track never blocks a shot.</summary>
    public (GyreChain Chain, int Index)? CollideShot(Vector2 pos)
    {
        (GyreChain, int)? best = null;
        var bestOver = false;
        var bestDist = GyreStages.MarbleDiameter * 0.92f;
        foreach (var chain in Chains)
        {
            for (var i = 0; i < chain.Marbles.Count; i++)
            {
                var m = chain.Marbles[i];
                if (chain.Path.InTunnel(m.D))
                {
                    continue;
                }
                var dist = Vector2.Distance(pos, chain.Path.PosAt(m.D));
                if (dist > GyreStages.MarbleDiameter * 0.92f)
                {
                    continue;
                }
                var over = chain.Path.InOverpass(m.D);
                if (best is null || (over && !bestOver) || (over == bestOver && dist < bestDist))
                {
                    best = (chain, i);
                    bestOver = over;
                    bestDist = dist;
                }
            }
        }
        return best;
    }

    public bool ConsumeNeedle()
    {
        if (NeedleShots <= 0)
        {
            return false;
        }
        NeedleShots--;
        return true;
    }

    /// <summary>Inserts the shot into the chain beside the hit marble and resolves the pop. A live
    /// Shatterstone charge explodes a radius instead, colour-blind.</summary>
    public void InsertShot(GyreChain chain, int index, Vector2 shotPos, int kind)
    {
        // A new shot starts its own run of consequences rather than inheriting the last one's depth.
        _cascadeUntil = 0f;
        var hit = chain.Marbles[index];
        if (ShatterShots > 0)
        {
            ShatterShots--;
            Explode(chain.Path.PosAt(hit.D));
            return;
        }

        var tangent = chain.Path.TangentAt(hit.D);
        var ahead = Vector2.Dot(shotPos - chain.Path.PosAt(hit.D), tangent) > 0f;
        var insertAt = ahead ? index + 1 : index;

        // The wedge goes in HALF seated and the chain shoves itself apart over the next few frames.
        // Displacing the queue by a whole marble on the frame of impact is a teleport, however correct the
        // arithmetic; the settle pass below is what makes it read as a ball forcing its way in.
        var d = ahead ? hit.D + (Spacing * WedgeSeat) : hit.D - (Spacing * WedgeSeat);
        chain.Marbles.Insert(insertAt, new GyreMarble { Id = _nextId++, Kind = kind, D = d });
        ResolveMatches(chain);
    }

    private void Explode(Vector2 at)
    {
        var removed = new List<(GyreChain Chain, GyreMarble Marble)>();
        foreach (var chain in Chains)
        {
            foreach (var m in chain.Marbles)
            {
                if (Vector2.Distance(at, chain.Path.PosAt(m.D)) < GyreStages.MarbleDiameter * 1.6f)
                {
                    removed.Add((chain, m));
                }
            }
        }
        if (removed.Count == 0)
        {
            return;
        }
        var points = Apply(GameScoring.GyreMatch3
            + (GameScoring.GyrePerExtraMarble * Math.Max(0, removed.Count - 3)));
        AddPoints(points);
        Events.Add(new GyreEvent(GyreEventKind.Pop, at, removed[0].Marble.Dud ? -1 : removed[0].Marble.Kind,
            removed.Count, 1, points, GyrePowerup.Shatterstone, default));
        foreach (var (chain, m) in removed)
        {
            if (m.Power is { } power)
            {
                GrantPower(power, chain.Path.PosAt(m.D));
            }
            chain.Marbles.Remove(m);
        }
    }

    private void CrumbleLoneDuds(GyreChain chain)
    {
        if (chain.Marbles.Count == 0)
        {
            return;
        }
        foreach (var (start, end) in GroupRanges(chain.Marbles))
        {
            // The rearmost group may still be growing at the mouth, so its duds get to wait for company.
            if (start == 0 && _pool > 0)
            {
                continue;
            }
            var allDud = true;
            for (var i = start; i <= end; i++)
            {
                if (!chain.Marbles[i].Dud)
                {
                    allDud = false;
                    break;
                }
            }
            if (allDud)
            {
                Events.Add(new GyreEvent(GyreEventKind.DudCrumble,
                    chain.Path.PosAt(chain.Marbles[start].D), -1, end - start + 1, 0, 0, default, default));
                chain.Marbles.RemoveRange(start, end - start + 1);
                return;
            }
        }
    }

    /// <summary>Spends a powerup that was matched out of the chain.</summary>
    private void GrantPower(GyrePowerup kind, Vector2 at)
    {
        AddPoints(GameScoring.GyrePowerupCatch);
        Events.Add(new GyreEvent(GyreEventKind.PowerTaken, at, 0, 0, 0,
            GameScoring.GyrePowerupCatch, kind, default));
        switch (kind)
        {
            case GyrePowerup.Aetherlight:
                AimLeft = GameScoring.GyreAimSeconds;
                break;
            case GyrePowerup.Driftmoss:
                SlowLeft = GameScoring.GyreSlowSeconds;
                break;
            case GyrePowerup.Recoil:
                foreach (var chain in Chains)
                {
                    chain.Recoil += GameScoring.GyreRecoilUnits;
                }
                break;
            case GyrePowerup.Shatterstone:
                ShatterShots = GameScoring.GyreShatterShots;
                break;
            case GyrePowerup.Threadneedle:
                NeedleShots = GameScoring.GyreNeedleShots;
                break;
            case GyrePowerup.Sparkfall:
                DoubleLeft = GameScoring.GyreDoubleSeconds;
                break;
        }
    }

    public void FireElement(AetherlingElement element, int heldKind)
    {
        if (_stage is null)
        {
            return;
        }
        Events.Add(new GyreEvent(GyreEventKind.PowerFired, default, heldKind, 0, 0, 0, default, element));
        switch (element)
        {
            case AetherlingElement.Fire:
                TorchNearFissure(GameScoring.GyreFireTorchCount);
                break;
            case AetherlingElement.Ice:
                FrozenLeft = GameScoring.GyreIceFreezeSeconds;
                break;
            case AetherlingElement.Wind:
                foreach (var chain in Chains)
                {
                    chain.Recoil += GameScoring.GyreWindShoveUnits;
                }
                break;
            case AetherlingElement.Earth:
                Earthquake();
                break;
            case AetherlingElement.Lightning:
                StrikeKind(heldKind);
                break;
            case AetherlingElement.Water:
                WashRuns();
                break;
        }
    }

    private void TorchNearFissure(int count)
    {
        var all = new List<(GyreChain Chain, GyreMarble Marble, float Left)>();
        foreach (var chain in Chains)
        {
            foreach (var m in chain.Marbles)
            {
                all.Add((chain, m, chain.Path.Length - m.D));
            }
        }
        foreach (var (chain, m, _) in all.OrderBy(x => x.Left).Take(count))
        {
            var points = Apply(GameScoring.GyrePerExtraMarble);
            AddPoints(points);
            Events.Add(new GyreEvent(GyreEventKind.Pop, chain.Path.PosAt(m.D), m.Dud ? -1 : m.Kind,
                1, 1, points, default, AetherlingElement.Fire));
            chain.Marbles.Remove(m);
        }
    }

    private void StrikeKind(int kind)
    {
        foreach (var chain in Chains)
        {
            for (var i = chain.Marbles.Count - 1; i >= 0; i--)
            {
                var m = chain.Marbles[i];
                if (m.Dud || m.Kind != kind)
                {
                    continue;
                }
                var points = Apply(GameScoring.GyrePerExtraMarble);
                AddPoints(points);
                Events.Add(new GyreEvent(GyreEventKind.Pop, chain.Path.PosAt(m.D), m.Kind, 1, 1, points,
                    default, AetherlingElement.Lightning));
                chain.Marbles.RemoveAt(i);
            }
        }
    }

    private void Earthquake()
    {
        if (_stage is null)
        {
            return;
        }
        foreach (var (chain, dto) in Chains.Zip(_stage.Paths))
        {
            var lane = LanePalette(dto);
            foreach (var m in chain.Marbles)
            {
                if (!m.Dud)
                {
                    m.Kind = lane[_rng.Next(lane.Length)];
                }
            }
        }
        PopAllRuns(minRun: 3, element: AetherlingElement.Earth);
    }

    private void WashRuns()
    {
        PopAllRuns(minRun: 2, element: AetherlingElement.Water);
    }

    private void PopAllRuns(int minRun, AetherlingElement element)
    {
        foreach (var chain in Chains)
        {
            var marbles = chain.Marbles;
            var i = marbles.Count - 1;
            while (i >= 0)
            {
                var m = marbles[i];
                if (m.Dud)
                {
                    i--;
                    continue;
                }
                var lo = i;
                while (lo > 0 && !marbles[lo - 1].Dud && marbles[lo - 1].Kind == m.Kind
                    && marbles[lo].D - marbles[lo - 1].D <= Spacing + 0.5f)
                {
                    lo--;
                }
                var n = i - lo + 1;
                if (n >= minRun)
                {
                    var points = Apply(GameScoring.GyreMatch3
                        + (GameScoring.GyrePerExtraMarble * Math.Max(0, n - 3)));
                    AddPoints(points);
                    Events.Add(new GyreEvent(GyreEventKind.Pop, chain.Path.PosAt(marbles[(lo + i) / 2].D),
                        m.Kind, n, 1, points, default, element));
                    marbles.RemoveRange(lo, n);
                }
                i = lo - 1;
            }
        }
    }

    private int Apply(int points) => DoubleLeft > 0f ? points * 2 : points;

    private void AddPoints(int points)
    {
        Score += points;
        var threshold = (Score / GameScoring.GyreHpRegainEvery);
        if (threshold > _lifeThresholds && Hp < GameScoring.GyreMaxHp)
        {
            _lifeThresholds = threshold;
            Hp = Math.Min(GameScoring.GyreMaxHp, Hp + GameScoring.GyreHpRegain);
            Events.Add(new GyreEvent(GyreEventKind.ExtraLife, default, 0, 0, 0, 0, default, default));
        }
    }

    private void ClearStage()
    {
        if (_stage is null)
        {
            return;
        }
        var maxLen = Chains.Count == 0 ? 0f : Chains.Max(c => c.Path.Length);
        var par = _stage.Speed > 0f ? 2f * maxLen / (_stage.Speed * SpeedScale) : 0f;
        var bonus = GameScoring.GyreStageClearBonus
            + (int)(GameScoring.GyreTimeBonusPerSecond * MathF.Max(0f, par - _stageTime));
        AddPoints(bonus);
        Events.Add(new GyreEvent(GyreEventKind.StageCleared, default, 0, Stage, 0, bonus, default, default));
        LoadStage(Stage + 1);
    }
}
