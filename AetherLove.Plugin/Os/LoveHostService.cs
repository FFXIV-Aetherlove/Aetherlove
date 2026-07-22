using AetherLove.Services;
using AetherOS.Apps.Love;

namespace AetherLove.Os;

/// <summary>The AetherLove app's bridge: the Pulse activity ping, which cannot live in a surface app (it
/// writes the native FFXIV chat log). Selfie capture now comes from the shared app capabilities.</summary>
public sealed class LoveHostService : ILoveHost
{
    private readonly PulseService _pulse;

    public LoveHostService(PulseService pulse)
    {
        _pulse = pulse;
    }

    public void MarkActivity() => _pulse.MarkActivity();
}
