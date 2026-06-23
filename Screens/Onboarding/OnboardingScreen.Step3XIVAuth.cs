using System;
using System.IO;
using System.Numerics;
using AetherLove.Navigation;
using AetherLove.Services;
using AetherLove.Services.Auth;
using AetherLove.Services.Localization;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;

namespace AetherLove.Screens;

public partial class OnboardingScreen
{
    private DateTime _authCompletedShownAt = DateTime.MinValue;
    private const double AuthCompletedHoldSeconds = 0.6;

    private bool _postAuthBootstrapStarted;

    private ISharedImmediateTexture? _xivAuthLogo;
    private bool _xivAuthLogoLoaded;

    private DateTime _welcomeBackShownAt = DateTime.MinValue;
    private const double WelcomeBackHoldSeconds = 1.8;

    private const string XivAuthRegisterUrl = "https://xivauth.net/auth/register";

    private void DrawStepAuth()
    {
        var t = ThemeService.Current;
        var centerX = ImGui.GetContentRegionAvail().X * 0.5f;

        ImGui.Spacing();
        ImGui.Spacing();
        var Head = Loc.T("onboarding.auth_signin_with_xivauth");
        ImGui.SetCursorPosX(centerX - ImGui.CalcTextSize(Head).X * 0.5f);
        ImGui.TextColored(t.AccentLight, Head);
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.TextWrapped(Loc.T("onboarding.auth_intro"));
        ImGui.Spacing();
        ImGui.Spacing();

        switch (_authService.State)
        {
            case AuthFlowState.Idle:
                DrawAuthIdle(t, centerX);
                break;
            case AuthFlowState.Starting:
                DrawAuthSpinner(centerX, Loc.T("onboarding.auth_contacting"));
                break;
            case AuthFlowState.AwaitingBrowser:
                DrawAuthAwaitingBrowser(t, centerX);
                break;
            case AuthFlowState.Completed:
                DrawAuthCompleted(centerX);
                break;
            case AuthFlowState.Failed:
                DrawAuthFailed(t, centerX);
                break;
        }
    }

    private void DrawAuthIdle(ThemeDefinition t, float centerX)
    {
        _authCompletedShownAt = DateTime.MinValue;
        _postAuthBootstrapStarted = false;
        _welcomeBackShownAt = DateTime.MinValue;

        var label = Loc.T("onboarding.auth_signin_with_xivauth");
        const float BtnW = 220f;
        ImGui.SetCursorPosX(centerX - Px(BtnW) * 0.5f);
        PushThemeButton(t);
        var clicked = ImGui.Button(label, Px(BtnW, 36f));
        PopThemeButton();
        DrawXivAuthButtonIcon(label);
        if (clicked)
        {
            _authService.StartSignIn();
        }

        DrawNoXivAuthSection(t, centerX);
    }

    /// <summary>Overlays the XIVAuth logo just left of the (centered) button label, scaled to the button height
    /// while preserving the source 25×30 aspect ratio.</summary>
    private void DrawXivAuthButtonIcon(string label)
    {
        EnsureXivAuthLogo();
        var wrap = _xivAuthLogo?.GetWrapOrDefault();
        if (wrap is null)
        {
            return;
        }

        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var iconH = Px(20f);
        var iconW = iconH * (25f / 30f);
        var gap = Px(8f);
        var iconX = MathF.Max(min.X + Px(10f), (min.X + max.X) * 0.5f - ImGui.CalcTextSize(label).X * 0.5f - gap - iconW);
        var iconY = (min.Y + max.Y) * 0.5f - iconH * 0.5f;
        ImGui.GetWindowDrawList().AddImage(wrap.Handle, new Vector2(iconX, iconY), new Vector2(iconX + iconW, iconY + iconH));
    }

    private void EnsureXivAuthLogo()
    {
        if (_xivAuthLogoLoaded)
        {
            return;
        }
        _xivAuthLogoLoaded = true;
        var dir = Path.GetDirectoryName(Plugin.PluginInterface.AssemblyLocation.FullName) ?? "";
        var path = Path.Combine(dir, "Media", "xivauth-logo.png");
        if (File.Exists(path))
        {
            _xivAuthLogo = Plugin.TextureProvider.GetFromFile(path);
        }
    }

    private void DrawNoXivAuthSection(ThemeDefinition t, float centerX)
    {
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.Spacing();

        var Head = Loc.T("onboarding.auth_dont_have_xivauth");
        ImGui.SetCursorPosX(centerX - ImGui.CalcTextSize(Head).X * 0.5f);
        ImGui.TextColored(t.AccentLight, Head);
        ImGui.Spacing();

        ImGui.PushTextWrapPos(0f);
        ImGui.TextWrapped(Loc.T("onboarding.auth_xivauth_explainer"));
        ImGui.PopTextWrapPos();
        ImGui.Spacing();
        ImGui.Spacing();

        const float BtnW = 240f;
        var createLabel = Loc.T("onboarding.auth_create_account");
        ImGui.SetCursorPosX(centerX - Px(BtnW) * 0.5f);
        var createClicked = ImGui.Button(createLabel, Px(BtnW, 32f));
        DrawXivAuthButtonIcon(createLabel);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(XivAuthRegisterUrl);
        }
        if (createClicked)
        {
            OpenXivAuthRegistration();
        }
    }

    private static void OpenXivAuthRegistration()
    {
        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(XivAuthRegisterUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[Onboarding] Failed to open XIVAuth registration URL.");
        }
    }

    private void DrawAuthAwaitingBrowser(ThemeDefinition t, float centerX)
    {
        DrawAuthSpinner(centerX, Loc.T("onboarding.auth_complete_in_browser"));
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.TextWrapped(Loc.T("onboarding.auth_browser_opened"));
        ImGui.Spacing();
        ImGui.Spacing();

        const float BtnW = 160f;
        var totalW = Px(BtnW * 2f + 8f);

        ImGui.SetCursorPosX(centerX - totalW * 0.5f);
        PushThemeButton(t);
        if (ImGui.Button(Loc.T("onboarding.auth_open_browser_again"), Px(BtnW, 28f)))
        {
            _authService.ReopenBrowser();
        }
        PopThemeButton();

        ImGui.SameLine(0f, Px(8f));
        if (ImGui.Button(Loc.T("onboarding.auth_cancel"), Px(BtnW, 28f)))
        {
            _authService.Cancel();
        }
    }

    private void DrawAuthCompleted(float centerX)
    {
        if (!_postAuthBootstrapStarted)
        {
            _postAuthBootstrapStarted = true;
            _bootstrap.Reset();
            _ = _bootstrap.RunAsync();
        }

        if (_bootstrap.LastResult == SessionBootstrapResult.Pending)
        {
            DrawAuthSpinner(centerX, Loc.T("onboarding.auth_loading_profile"));
            return;
        }

        // New device / existing account: server has a key bundle but this machine has no private key.
        // Route to the passphrase screen to rebuild it, else messaging has no chat key. Mirrors SplashScreen.
        if (_bootstrap.NeedsPassphraseUnlock)
        {
            _router.Navigate(Screen.PassphraseUnlock);
            return;
        }

        // Active account with no server key bundle (e.g. re-registered after deletion): set up encryption
        // once before reaching the deck, otherwise messaging stays broken with no in-app fix.
        if (_bootstrap.NeedsEncryptionRecovery)
        {
            _router.Navigate(Screen.EncryptionRecovery);
            return;
        }

        if (_bootstrap.LastResult == SessionBootstrapResult.SignedInActive)
        {
            DrawWelcomeBack(centerX);
            return;
        }

        var Ok = Loc.T("onboarding.auth_signed_in");
        ImGui.SetCursorPosX(centerX - ImGui.CalcTextSize(Ok).X * 0.5f);
        ImGui.TextColored(UiColors.Success, Ok);

        if (_authCompletedShownAt == DateTime.MinValue)
        {
            _authCompletedShownAt = DateTime.Now;
        }

        if ((DateTime.Now - _authCompletedShownAt).TotalSeconds >= AuthCompletedHoldSeconds)
        {
            _authCompletedShownAt = DateTime.MinValue;
            GoNext();
        }
    }

    private void DrawWelcomeBack(float centerX)
    {
        if (_welcomeBackShownAt == DateTime.MinValue)
        {
            _welcomeBackShownAt = DateTime.Now;
        }

        var name = _bootstrap.LastDisplayName;
        var heading = string.IsNullOrEmpty(name) ? Loc.T("onboarding.auth_welcome_back") : Loc.T("onboarding.auth_welcome_back_named", name);

        ImGui.Spacing();
        ImGui.SetCursorPosX(centerX - ImGui.CalcTextSize(heading).X * 0.5f);
        ImGui.TextColored(UiColors.Success, heading);
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.PushTextWrapPos(0f);
        ImGui.TextWrapped(Loc.T("onboarding.auth_details_loaded"));
        ImGui.PopTextWrapPos();

        if ((DateTime.Now - _welcomeBackShownAt).TotalSeconds >= WelcomeBackHoldSeconds)
        {
            _welcomeBackShownAt = DateTime.MinValue;
            _router.Navigate(Screen.Deck);
        }
    }

    private void DrawAuthFailed(ThemeDefinition t, float centerX)
    {
        var msg = _authService.LastFailureWasExpiry
            ? Loc.T("onboarding.auth_timeout")
            : (_authService.ErrorMessage ?? Loc.T("onboarding.auth_failed"));

        ImGui.PushTextWrapPos(0f);
        ImGui.TextColored(UiColors.Danger, msg);
        ImGui.PopTextWrapPos();
        ImGui.Spacing();
        ImGui.Spacing();

        const float BtnW = 200f;
        ImGui.SetCursorPosX(centerX - Px(BtnW) * 0.5f);
        PushThemeButton(t);
        if (ImGui.Button(Loc.T("onboarding.auth_try_again"), Px(BtnW, 36f)))
        {
            _authService.StartSignIn();
        }
        PopThemeButton();
    }

    private static void DrawAuthSpinner(float centerX, string label)
    {
        var dotCount = (int)(DateTime.Now.TimeOfDay.TotalSeconds * 3) % 4;
        var line = label + new string('.', dotCount);
        ImGui.SetCursorPosX(centerX - ImGui.CalcTextSize(line).X * 0.5f);
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), line);
    }
}
