using System;
using System.Collections.Generic;

namespace AetherOS.Apps.Realtor;

/// <summary>The houses the app shows and counts: every tracked house, less the Free Company ones while that
/// setting is off. The watcher records both kinds regardless, so switching the setting on later already has
/// its history. Memoized because the tile badge reads the count every frame.</summary>
internal sealed class EstateView
{
    private static readonly TimeSpan CountRecompute = TimeSpan.FromSeconds(5);

    private readonly IEstateWatch _watch;
    private readonly RealtorSettings _settings;
    private IReadOnlyList<EstateRecord> _estates = [];
    private int _forVersion = -1;
    private bool _forTrackFc;
    private int _atRiskCount;
    private DateTime _countComputedUtc = DateTime.MinValue;

    public EstateView(IEstateWatch watch, RealtorSettings settings)
    {
        _watch = watch;
        _settings = settings;
    }

    public IReadOnlyList<EstateRecord> Estates
    {
        get
        {
            Refresh();
            return _estates;
        }
    }

    /// <summary>A count of whole days, so between two changes of the list it is recomputed on a slow timer
    /// rather than per frame.</summary>
    public int AtRiskCount
    {
        get
        {
            Refresh();
            var now = DateTime.UtcNow;
            if (now - _countComputedUtc >= CountRecompute)
            {
                _countComputedUtc = now;
                _atRiskCount = EstateRisk.AtRiskCount(_estates, now);
            }
            return _atRiskCount;
        }
    }

    public void Remove(EstateRecord estate) => _watch.Remove(estate.ContentId, estate.Kind);

    private void Refresh()
    {
        var version = _watch.Version;
        var trackFc = _settings.TrackFcEstate;
        if (version == _forVersion && trackFc == _forTrackFc)
        {
            return;
        }
        _forVersion = version;
        _forTrackFc = trackFc;
        _countComputedUtc = DateTime.MinValue;

        var all = _watch.Estates;
        if (trackFc)
        {
            _estates = all;
            return;
        }
        var personal = new List<EstateRecord>(all.Count);
        foreach (var estate in all)
        {
            if (estate.Kind == EstateKind.Personal)
            {
                personal.Add(estate);
            }
        }
        _estates = personal;
    }
}
