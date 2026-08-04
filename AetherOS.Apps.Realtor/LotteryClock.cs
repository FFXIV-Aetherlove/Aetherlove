using System;
using AetherLove.Services.Realtor;
using AetherOS.Sdk;

namespace AetherOS.Apps.Realtor;

/// <summary>The housing lottery runs on ONE clock for the whole game: every world flips phase at the same
/// instant. PaissaDB can only ever report it per plot, as whoever last walked past saw it, so a busy world
/// serves today's phase and a quiet one serves a phase that expired a fortnight ago.
///
/// So rather than believe the freshest sighting, this treats any sighting as an ANCHOR and rolls it forward
/// through the fixed cycle until it lands on the present. One sighting, from any world, at any time in the
/// past, is therefore enough to know the phase on every world, including one nobody has scouted in months.
/// The anchor is persisted, so after the first successful load the app never needs a fresh sighting again.</summary>
public sealed class LotteryClock
{
    /// <summary>The cycle: five days taking entries, four days publishing results, repeating forever.</summary>
    private static readonly TimeSpan AcceptingLength = TimeSpan.FromDays(5);
    private static readonly TimeSpan ResultsLength = TimeSpan.FromDays(4);

    private const string StorageKey = "lotteryAnchor";
    private const int MaxRollForward = 1000;

    private readonly IAppStorage _storage;
    private Anchor? _anchor;
    private bool _loaded;

    public LotteryClock(IAppStorage storage) => _storage = storage;

    /// <summary>A single observed phase deadline. Stored rather than the phase alone, because the deadline is
    /// what makes the cycle re-derivable at any later date.</summary>
    public sealed class Anchor
    {
        public int Phase { get; set; }
        public long UntilUnix { get; set; }
        public long ObservedUnix { get; set; }
    }

    /// <summary>A lottery plot whose own last sighting disagrees with the global phase has not been walked
    /// past since the cycle turned, so its price, entry count and even its availability are all suspect.
    /// Takes the phase as an argument rather than reading <see cref="Current"/>, so a caller classifying a
    /// few hundred plots in one frame resolves the clock once.</summary>
    public static bool IsStale(PaissaPlot plot, int? globalPhase) =>
        globalPhase is { } phase && plot.IsLottery && plot.LottoPhase != phase;

    /// <summary>Splits a world's open plots into the ones the current cycle confirms and the ones whose data
    /// predates it.</summary>
    public static (int Verified, int Stale) CountPlots(PaissaWorldDetail detail, int? globalPhase)
    {
        var verified = 0;
        var stale = 0;
        foreach (var district in detail.Districts)
        {
            if (district.OpenPlots is not { } plots)
            {
                continue;
            }
            foreach (var plot in plots)
            {
                if (IsStale(plot, globalPhase))
                {
                    stale++;
                }
                else
                {
                    verified++;
                }
            }
        }
        return (verified, stale);
    }

    /// <summary>Takes the newest sighting in a world's plots, if it beats the anchor we already hold.</summary>
    public void Observe(PaissaWorldDetail detail)
    {
        Load();
        var changed = false;
        foreach (var district in detail.Districts)
        {
            if (district.OpenPlots is not { } plots)
            {
                continue;
            }
            foreach (var plot in plots)
            {
                if (plot.LottoPhase is not { } phase || plot.PhaseUntil is not { } until
                    || phase is not (PaissaLottoPhase.Accepting or PaissaLottoPhase.Results))
                {
                    continue;
                }
                var observed = plot.LastUpdatedAt.ToUnixTimeSeconds();
                if (_anchor is { } held && observed <= held.ObservedUnix)
                {
                    continue;
                }
                _anchor = new Anchor
                {
                    Phase = phase,
                    UntilUnix = until.ToUnixTimeSeconds(),
                    ObservedUnix = observed,
                };
                changed = true;
            }
        }
        if (changed)
        {
            _storage.Set(StorageKey, _anchor);
        }
    }

    /// <summary>The phase right now and when it ends, wherever the player is. Null only when no sighting has
    /// ever been recorded on this install.</summary>
    public (int Phase, DateTimeOffset Until)? Current
    {
        get
        {
            Load();
            if (_anchor is not { } anchor)
            {
                return null;
            }
            var phase = anchor.Phase;
            var until = DateTimeOffset.FromUnixTimeSeconds(anchor.UntilUnix);
            var now = DateTimeOffset.UtcNow;
            for (var i = 0; until <= now && i < MaxRollForward; i++)
            {
                phase = phase == PaissaLottoPhase.Accepting ? PaissaLottoPhase.Results : PaissaLottoPhase.Accepting;
                until += phase == PaissaLottoPhase.Accepting ? AcceptingLength : ResultsLength;
            }
            return until > now ? (phase, until) : null;
        }
    }

    private void Load()
    {
        if (_loaded)
        {
            return;
        }
        _loaded = true;
        _anchor = _storage.Get<Anchor>(StorageKey);
    }
}
