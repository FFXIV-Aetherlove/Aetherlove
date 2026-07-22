using AetherLove.Navigation;
using AetherLove.Os;

namespace AetherLove.Screens;

/// <summary>Where a live moderation gate (warning / moderator message) returns after acknowledgement: the
/// screen or surface app the user was on when the push interrupted them. Shared by both gate screens so a
/// chained second gate keeps the original location instead of capturing the first gate as its target.</summary>
internal static class LiveGateReturn
{
    private static Screen? _screen;
    private static string? _appId;

    public static void Capture(ScreenRouter router, OsShell shell)
    {
        var current = router.Current;
        if (current is Screen.WarningsAcknowledge or Screen.ModeratorMessages)
        {
            return;
        }
        _screen = current;
        _appId = current == Screen.App ? shell.ActiveSurfaceApp?.Id : null;
    }

    public static void Return(ScreenRouter router, OsShell shell)
    {
        var screen = _screen;
        var appId = _appId;
        _screen = null;
        _appId = null;

        if (screen == Screen.App)
        {
            if (appId is not null)
            {
                shell.OpenApp(appId);
                return;
            }
        }
        else if (screen is { } s)
        {
            router.Navigate(s);
            return;
        }
        shell.SendIntent("aetherlove", AetherOS.Sdk.OsIntents.Create(AetherOS.Sdk.OsIntents.OpenDeck));
    }
}
