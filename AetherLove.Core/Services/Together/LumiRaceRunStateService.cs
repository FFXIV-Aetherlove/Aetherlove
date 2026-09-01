using AetherLove.Shared.Racing;

namespace AetherLove.Services.Together;

/// <summary>The client's view of the party race, full-replace pushes from the hub. Lock-guarded because
/// pushes land on the signal thread while the racer app reads <see cref="Run"/> on the draw thread. The
/// party ending clears everything.</summary>
public sealed class LumiRaceRunStateService
{
    private readonly object _lock = new();
    private LumiRacePartyRunDto? _run;

    public LumiRacePartyRunDto? Run
    {
        get
        {
            lock (_lock)
            {
                return _run;
            }
        }
    }

    public void ApplyRun(LumiRacePartyRunDto run)
    {
        lock (_lock)
        {
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
}
