using AetherLove.Services;
using AetherOS.Apps.Love;

namespace AetherLove.Os;

/// <summary>The AetherLove app's bridge: the Pulse activity ping, which cannot live in a surface app (it
/// writes the native FFXIV chat log). Selfie capture now comes from the shared app capabilities.</summary>
public sealed class LoveHostService : ILoveHost
{
    private readonly PulseService _pulse;
    private readonly AetherLove.Navigation.ScreenRouter _router;

    public LoveHostService(PulseService pulse, AetherLove.Navigation.ScreenRouter router)
    {
        _pulse = pulse;
        _router = router;
    }

    public void MarkActivity() => _pulse.MarkActivity();

    public void OpenEncryptionRecovery() => _router.Navigate(AetherLove.Navigation.Screen.EncryptionRecovery);
}
