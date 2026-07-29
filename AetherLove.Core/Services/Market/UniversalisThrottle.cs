using System;
using System.Threading;
using System.Threading.Tasks;

namespace AetherLove.Services.Market;

/// <summary>Politeness gate for all Universalis traffic: at most 2 concurrent requests with 150 ms
/// between request starts (Universalis allows 25 req/s and 8 connections; we stay far under both), plus a
/// circuit breaker that pauses every request for a while after repeated failures or a 429, so an outage or
/// rate-limit strike is never answered with a retry storm.</summary>
internal sealed class UniversalisThrottle
{
    private static readonly TimeSpan Spacing = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan FailureBreak = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan RateLimitBreak = TimeSpan.FromSeconds(30);
    private const int FailuresToTrip = 3;

    private readonly SemaphoreSlim _slots = new(2, 2);
    private readonly object _gate = new();
    private long _nextStartAt;
    private long _pausedUntil;
    private int _consecutiveFailures;

    public bool IsPaused => Volatile.Read(ref _pausedUntil) > Environment.TickCount64;

    /// <summary>Waits for a slot and the pacing gap. False when the breaker is open and the caller
    /// should skip the request entirely.</summary>
    public async Task<bool> EnterAsync(CancellationToken ct)
    {
        if (IsPaused)
        {
            return false;
        }
        await _slots.WaitAsync(ct).ConfigureAwait(false);
        if (IsPaused)
        {
            _slots.Release();
            return false;
        }

        TimeSpan delay;
        lock (_gate)
        {
            var now = Environment.TickCount64;
            var start = Math.Max(now, _nextStartAt);
            _nextStartAt = start + (long)Spacing.TotalMilliseconds;
            delay = TimeSpan.FromMilliseconds(start - now);
        }
        if (delay > TimeSpan.Zero)
        {
            try
            {
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _slots.Release();
                throw;
            }
        }
        return true;
    }

    public void Exit() => _slots.Release();

    public void ReportSuccess() => Interlocked.Exchange(ref _consecutiveFailures, 0);

    public void ReportFailure()
    {
        if (Interlocked.Increment(ref _consecutiveFailures) >= FailuresToTrip)
        {
            Pause(FailureBreak, "repeated failures");
        }
    }

    public void ReportRateLimited() => Pause(RateLimitBreak, "HTTP 429");

    private void Pause(TimeSpan duration, string reason)
    {
        var until = Environment.TickCount64 + (long)duration.TotalMilliseconds;
        if (Volatile.Read(ref _pausedUntil) < until)
        {
            Volatile.Write(ref _pausedUntil, until);
            Interlocked.Exchange(ref _consecutiveFailures, 0);
            UiHost.Log.Information($"[Universalis] Pausing requests for {duration.TotalSeconds:0}s ({reason}).");
        }
    }
}
