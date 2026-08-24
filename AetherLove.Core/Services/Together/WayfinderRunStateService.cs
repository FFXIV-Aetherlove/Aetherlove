using System;
using AetherLove.Shared.Wayfinder;

namespace AetherLove.Services.Together;

/// <summary>The client's view of the party hunt, full-replace pushes from the hub. Lock-guarded because
/// pushes land on the signal thread while the Wayfinder app reads <see cref="Run"/> on the draw thread.
/// A terminal run (Completed/Expired/Cancelled) stays readable so the app can show results until the user
/// dismisses them; the party ending clears everything.</summary>
public sealed class WayfinderRunStateService
{
    private readonly object _lock = new();
    private WayfinderPartyRunDto? _run;

    public WayfinderPartyRunDto? Run
    {
        get
        {
            lock (_lock)
            {
                return _run;
            }
        }
    }

    /// <summary>Roster and verdict pushes never carry the challenge image, so a same-run update without
    /// bytes keeps the ones already held.</summary>
    public void ApplyRun(WayfinderPartyRunDto run)
    {
        lock (_lock)
        {
            if (run.ImageBytes is null && _run is { } previous && previous.RunId == run.RunId)
            {
                run = run with { ImageBytes = previous.ImageBytes };
            }
            _run = run;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _run = null;
        }
    }

    /// <summary>Drops the run only when it is over, for the app's "done with the results" dismissal; a live
    /// run is never discarded client-side.</summary>
    public void DismissIfResolved()
    {
        lock (_lock)
        {
            if (_run is { } run && (WayfinderRunStatus)run.Status
                is not (WayfinderRunStatus.Gathering or WayfinderRunStatus.Active))
            {
                _run = null;
            }
        }
    }
}
