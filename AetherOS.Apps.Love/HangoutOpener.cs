using AetherLove.Services;
using AetherLove.Shared.Hangouts;

namespace AetherLove.Screens;

/// <summary>Opens a hangout in the AetherOS Hangouts app: stages the tapped hangout and switches to the app,
/// which polls the deep link and shows the detail overlay. Used by the chat card, match list, and profile banner.</summary>
public sealed class HangoutOpener
{
    private readonly LoveShell _shell;
    private readonly HangoutShareContext _shareCtx;

    public HangoutOpener(LoveShell shell, HangoutShareContext shareCtx)
    {
        _shell = shell;
        _shareCtx = shareCtx;
    }

    /// <summary><paramref name="fromChat"/> makes the detail's back return to the originating chat.</summary>
    public void Open(HangoutSummaryDto hangout, bool fromChat = false)
    {
        _shareCtx.PendingOpenHangout = hangout;
        _shareCtx.PendingOpenFromChat = fromChat;
        _shell.Shell?.OpenApp("hangouts");
    }
}
