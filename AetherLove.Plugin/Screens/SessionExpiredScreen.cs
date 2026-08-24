using System.Numerics;
using AetherLove.Navigation;
using AetherLove.Services.Auth;
using AetherLove.Services.Crypto;
using AetherLove.Services.Localization;
using AetherLove.Services.Signal;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherLove.Screens;

/// <summary>Shown when the refresh token is dead: the phone is not offline, the session simply ended, and
/// nothing it holds can reach the server again. Signing in is the only way out, so the button wipes the
/// dead session and sends the phone back through the splash, which boots it into the sign-in flow.</summary>
public sealed class SessionExpiredScreen(
    TokenService tokens,
    KeyStorageService keys,
    SessionBootstrapper bootstrap,
    AetherSignalService signal,
    ScreenRouter router)
{
    public void OnShow()
    {
    }

    public void Draw()
    {
        var winW = ImGui.GetWindowSize().X;
        var scrollH = ImGui.GetContentRegionAvail().Y;
        var padX = Px(16f);

        PushScrollbarStyle();
        using (var scroll = ImRaii.Child("##sessionExpiredScroll", new Vector2(0f, scrollH), false))
        {
            PopScrollbarStyle();
            if (!scroll.Success)
            {
                return;
            }

            ImGui.Spacing();
            ImGui.Spacing();

            var iconPx = Px(40f);
            var iconSz = IconDraw.Measure(FontAwesomeIcon.UserLock, iconPx);
            var iconOrigin = ImGui.GetCursorScreenPos();
            IconDraw.Add(ImGui.GetWindowDrawList(), FontAwesomeIcon.UserLock, iconPx,
                new Vector2(iconOrigin.X + ((winW - iconSz.X) * 0.5f), iconOrigin.Y),
                ImGui.GetColorU32(UiColors.Amber));
            ImGui.Dummy(new Vector2(winW, iconSz.Y));
            ImGui.Spacing();

            using (UiFonts.H2?.Push())
            {
                var title = Loc.T("common.session_expired_title");
                var titleSz = ImGui.CalcTextSize(title);
                ImGui.SetCursorPosX((winW - titleSz.X) * 0.5f);
                ImGui.TextColored(UiColors.Amber, title);
            }
            ImGui.Spacing();

            ImGui.SetCursorPosX(padX);
            ImGui.PushTextWrapPos(winW - padX);
            ImGui.TextColored(new Vector4(0.92f, 0.92f, 0.92f, 1f), Loc.T("common.session_expired_body"));
            ImGui.PopTextWrapPos();
            ImGui.Spacing();
            ImGui.Spacing();

            ImGui.SetCursorPosX(padX);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
            if (ImGui.Button(Loc.T("common.session_expired_button"), new Vector2(winW - (padX * 2f), Px(34f))))
            {
                Restart();
            }
            ImGui.PopStyleVar();
            ImGui.Spacing();

            ImGui.SetCursorPosX(padX);
            ImGui.PushTextWrapPos(winW - padX);
            ImGui.TextColored(new Vector4(0.60f, 0.60f, 0.60f, 1f), Loc.T("common.session_expired_hint"));
            ImGui.PopTextWrapPos();
        }
    }

    /// <summary>Drops the dead session and reboots the phone. The keys go with the tokens because they are
    /// unlocked per account session; the splash then finds no refresh token and routes to sign-in.</summary>
    private void Restart()
    {
        // The hub is still reconnecting in the background with the dead token; let it go before the splash
        // starts a fresh session.
        _ = signal.DisconnectAsync();
        tokens.Clear();
        keys.Clear();
        bootstrap.Reset();
        router.Navigate(Screen.Splash);
    }
}
