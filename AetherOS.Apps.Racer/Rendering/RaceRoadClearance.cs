namespace AetherOS.Apps.Racer.Rendering;

using System;
using System.Numerics;

using AetherLove.Shared.Racing;

/// <summary>World-space road clearance for the viewer's generation passes, built once per race.
/// Sixteen road segments share one padded bounding box, so a query rejects most of the lap on a
/// box test and runs the precise segment distance only nearby. Every branch of the lap is in it,
/// each segment is measured at its wider endpoint, and a decked segment carries the bridge's
/// own allowance. Never touches a track array.
///
/// <para><see cref="AetherRaceLive.Track.RoadClaims"/> stays the gate the initial dressing scatter and
/// the bridge piers are placed through, so their retained sets do not move; this cache serves the
/// relief builder and the final scenery clearance, which ask thousands of times per race.</para></summary>
internal sealed class RaceRoadClearance
{
    private const int SegmentsPerChunk = 16;
    private const float Margin = 0.02f;
    private const float BridgeAllowance = 0.5f;

    private readonly record struct Chunk(Vector2 Min, Vector2 Max, int Start, int End);

    private Chunk[] _chunks = [];
    private AetherRaceLive.Track? _track;
    private int _rows;

    public void Build(AetherRaceLive.Track track)
    {
        _track = track;
        _rows = track.LapRows > 0 ? track.LapRows : track.Count;
        var segments = _rows - (track.LapRows > 0 ? 0 : 1);
        _chunks = new Chunk[(segments + SegmentsPerChunk - 1) / SegmentsPerChunk];
        for (var chunk = 0; chunk < _chunks.Length; chunk++)
        {
            var first = chunk * SegmentsPerChunk;
            var end = Math.Min(segments, first + SegmentsPerChunk);
            var min = new Vector2(float.PositiveInfinity);
            var max = new Vector2(float.NegativeInfinity);
            var padding = 0f;
            for (var row = first; row < end; row++)
            {
                var next = (row + 1) % _rows;
                var a = new Vector2(track.Xs[row], track.Ys[row]);
                var b = new Vector2(track.Xs[next], track.Ys[next]);
                min = Vector2.Min(min, Vector2.Min(a, b));
                max = Vector2.Max(max, Vector2.Max(a, b));
                padding = MathF.Max(padding,
                    (MathF.Max(track.Ws[row], track.Ws[next]) * 0.5f) + (Bridged(track, row, next) ? BridgeAllowance : 0f) + Margin);
            }

            _chunks[chunk] = new Chunk(min - new Vector2(padding), max + new Vector2(padding), first, end);
        }
    }

    /// <summary>Is a world disc clear of every road branch?</summary>
    public bool Clear(Vector2 world, float radius)
    {
        if (_track is not { } track)
        {
            return false;
        }

        foreach (var chunk in _chunks)
        {
            if (world.X < chunk.Min.X - radius || world.X > chunk.Max.X + radius
                || world.Y < chunk.Min.Y - radius || world.Y > chunk.Max.Y + radius)
            {
                continue;
            }

            for (var i = chunk.Start; i < chunk.End; i++)
            {
                var next = (i + 1) % _rows;
                var a = new Vector2(track.Xs[i], track.Ys[i]);
                var b = new Vector2(track.Xs[next], track.Ys[next]);
                var edge = b - a;
                var length2 = edge.LengthSquared();
                var at = a + (edge * (length2 > 0f ? Math.Clamp(Vector2.Dot(world - a, edge) / length2, 0f, 1f) : 0f));
                var need = (MathF.Max(track.Ws[i], track.Ws[next]) * 0.5f) + radius + Margin;
                if (Bridged(track, i, next))
                {
                    need += BridgeAllowance;
                }

                if (Vector2.DistanceSquared(world, at) < need * need)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool Bridged(AetherRaceLive.Track track, int row, int next) =>
        (row < track.Decks.Length && track.Decks[row] > AetherRaceLive.DeckDrawn)
        || (next < track.Decks.Length && track.Decks[next] > AetherRaceLive.DeckDrawn);
}
