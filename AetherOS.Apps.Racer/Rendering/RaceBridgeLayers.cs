using System;
using AetherLove.Shared.Racing;

namespace AetherOS.Apps.Racer.Rendering;

/// <summary>Viewer-only bridge coverage, built once per race. The engine's <see cref="AetherRaceLive.Track.Under"/>
/// is a soft mask that also reaches along a ramp to its own higher rows, so a runner on an approach
/// reads as "under" a deck that is its own road. This keeps only coverage from a distinct overhead
/// branch, measured along the road, and never touches the track.</summary>
internal sealed class RaceBridgeLayers
{
    /// <summary>A row must sit this much higher, in bounds, to count as overhead.</summary>
    private const float OverheadRise = AetherRaceLive.DeckDrawn;

    /// <summary>The road neighbourhood around a row, as a multiple of the overlap span, that can never
    /// cover it: it is the same road ramping up.</summary>
    private const float OwnRoadReach = 2f;

    private float[] _coverage = [];
    private float _step;
    private bool _loop;

    public bool HasUnderpasses { get; private set; }

    public void Begin(AetherRaceLive.Track track)
    {
        HasUnderpasses = false;
        _step = track.Step;
        _loop = track.LapRows > 0;
        if (track.Under is not { } under)
        {
            _coverage = [];
            return;
        }

        var rows = _loop ? track.LapRows : track.Count;
        _coverage = new float[rows];
        for (var i = 0; i < rows; i++)
        {
            if (i >= under.Length || under[i] <= 0f)
            {
                continue;
            }

            var best = 0f;
            for (var j = 0; j < rows; j++)
            {
                if (track.Decks[j] - track.Decks[i] < OverheadRise)
                {
                    continue;
                }

                var span = ((track.Ws[i] + track.Ws[j]) * 0.5f) + 0.5f;
                var apart = Math.Abs(j - i);
                if (_loop)
                {
                    apart = Math.Min(apart, rows - apart);
                }

                if (apart * track.Step <= span * OwnRoadReach)
                {
                    continue;
                }

                var dx = track.Xs[j] - track.Xs[i];
                var dy = track.Ys[j] - track.Ys[i];
                var distance = MathF.Sqrt((dx * dx) + (dy * dy));
                if (distance >= span)
                {
                    continue;
                }

                var t = 1f - (distance / span);
                best = MathF.Max(best, t * t * (3f - (2f * t)));
            }

            // The engine's mask stays the upper bound: this only removes false self-coverage.
            _coverage[i] = MathF.Min(under[i], best);
            HasUnderpasses |= _coverage[i] > 0f;
        }
    }

    /// <summary>Whether a runner at this distance is under a distinct overhead branch and so draws
    /// before the deck overlay. Every other runner, ramp and deck included, draws after it.</summary>
    public bool DrawBeforeDeck(float distance) => UnderpassAt(distance) > 0f;

    /// <summary>Smooth coverage at the same interpolated distance used to position a runner.</summary>
    public float UnderpassAt(float distance)
    {
        if (!HasUnderpasses || !float.IsFinite(distance))
        {
            return 0f;
        }

        var row = distance / _step;
        if (!_loop)
        {
            row = Math.Clamp(row, 0f, _coverage.Length - 1);
        }

        var floor = MathF.Floor(row);
        var i = (int)floor;
        int next;
        if (_loop)
        {
            i = ((i % _coverage.Length) + _coverage.Length) % _coverage.Length;
            next = (i + 1) % _coverage.Length;
        }
        else
        {
            next = Math.Min(i + 1, _coverage.Length - 1);
        }

        return _coverage[i] + ((_coverage[next] - _coverage[i]) * (row - floor));
    }
}
