using System;
using System.Collections.Generic;
using System.Numerics;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Navigation;
using AetherLove.Services;
using AetherLove.Services.Auth;
using AetherLove.Services.Hub;
using AetherLove.Services.Localization;
using AetherLove.Shared.Messaging;
using AetherLove.Shared.Profile;
using AetherLove.UI;
using AetherLove.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using static AetherLove.UI.OnboardingUi;

namespace AetherLove.Screens;

/// <summary>The AetherOS shell onboarding, shown once per human: design, ToS, XIVAuth sign-in, passphrase, and an
/// OS name. Gated on <c>AetherAccountInfoDto.OsOnboarded == false</c>, so migrated accounts never see it.
/// XIVAuth + passphrase reuse the same AuthService / CryptoService the AetherLove wizard uses. Completing it
/// stamps the account and lands on the shell Home; the AetherLove dating profile is a separate onboarding, set up
/// only when the user opens the AetherLove app.</summary>
public sealed partial class OsOnboardingScreen
{
    private enum Step
    {
        Welcome = 0,
        Design = 1,
        Terms = 2,
        SignIn = 3,
        PassphraseIntro = 4,
        Passphrase = 5,
        PassphraseConfirm = 6,
        Profile = 7,
        Translations = 8,
        Done = 9,
    }

    private const int TotalSteps = 10;
    private const int PassphraseMinLength = 8;

    private readonly ScreenRouter _router;
    private readonly AetherHubContext _hub;
    private readonly SessionBootstrapper _bootstrap;
    private readonly AuthService _authService;
    private readonly Services.Crypto.CryptoService _crypto;
    private readonly Services.Crypto.KeyStorageService _keyStorage;
    private readonly OsAvatarCache _osAvatar;
    private readonly SelfieCaptureOverlay _selfieOverlay;
    private readonly AetherOS.Apps.Camera.ICameraLibrary _cameraRoll;
    private readonly PendingImagePick _pendingPick;

    private Step _step = Step.Welcome;
    private string _osName = string.Empty;
    private bool _tosAccepted;

    private string _passphrase = string.Empty;
    private string _passphraseConfirm = string.Empty;
    private bool _passphraseAcknowledged;
    private bool _passphraseCopied;
    private bool _showPassphrase;
    private volatile bool _passphraseUploaded;
    private volatile bool _passphraseProcessing;
    private volatile string? _passphraseError;

    private volatile bool _saving;
    private volatile bool _completePending;
    private volatile bool _advanceFromPassphrasePending;
    private volatile string? _saveError;

    private DateTime _authCompletedAt = DateTime.MinValue;
    private bool _postAuthBootstrapStarted;

    public OsOnboardingScreen(
        ScreenRouter router,
        AetherHubContext hub,
        SessionBootstrapper bootstrap,
        AuthService authService,
        Services.Crypto.CryptoService crypto,
        Services.Crypto.KeyStorageService keyStorage,
        OsAvatarCache osAvatar,
        ImageRequirementsModal imageReqModal,
        SelfieCaptureOverlay selfieOverlay,
        AetherOS.Apps.Camera.ICameraLibrary cameraRoll)
    {
        _router = router;
        _hub = hub;
        _bootstrap = bootstrap;
        _authService = authService;
        _crypto = crypto;
        _keyStorage = keyStorage;
        _osAvatar = osAvatar;
        _selfieOverlay = selfieOverlay;
        _cameraRoll = cameraRoll;
        _pendingPick = new PendingImagePick(imageReqModal);
    }

    public void OnShow()
    {
        _step = Step.Welcome;
        _tosAccepted = false;
        _passphrase = string.Empty;
        _passphraseConfirm = string.Empty;
        _passphraseAcknowledged = false;
        _passphraseCopied = false;
        _passphraseUploaded = false;
        _passphraseProcessing = false;
        _passphraseError = null;
        _saving = false;
        _completePending = false;
        _advanceFromPassphrasePending = false;
        _saveError = null;
        _authCompletedAt = DateTime.MinValue;
        _postAuthBootstrapStarted = false;
        _authService.Cancel();

        // The OS name is a free display name (spaces allowed); default to the full character name.
        var seeded = _bootstrap.LastAccount?.OsDisplayName;
        if (string.IsNullOrWhiteSpace(seeded))
        {
            seeded = Plugin.ObjectTable.LocalPlayer?.Name.TextValue ?? string.Empty;
        }
        _osName = seeded ?? string.Empty;
        _langIdx = Math.Max(0, Array.FindIndex(LanguageEntries, e => e.Name == Plugin.Configuration.PluginLanguage));
        ResetAvatarState();

        // Resume a partially-finished setup at the step the user left off. A signed-in session that reaches this
        // screen still owes OS setup (the router only sends it here when NeedsOsSetup): a set passphrase means jump
        // straight to the profile step; otherwise the passphrase step (sign-in is already done). A fresh, session-
        // less start stays on Welcome and goes through sign-in normally.
        if (_bootstrap.LastResult is SessionBootstrapResult.SignedInOnboarding
                                   or SessionBootstrapResult.SignedInActive)
        {
            _step = _bootstrap.LastConnection?.HasKeyBundle == true ? Step.Profile : Step.PassphraseIntro;
        }
    }

    public void Draw()
    {
        _fileDialog.Draw();
        _pendingPick.Poll();
        _cropPopup.Draw(ImGui.GetWindowPos(), ImGui.GetWindowSize());

        if (_completePending)
        {
            _completePending = false;
            _bootstrap.MarkOsOnboardedInSnapshot();
            SeedTranslationStep();
            _step = Step.Translations;
        }
        if (_advanceFromPassphrasePending)
        {
            _advanceFromPassphrasePending = false;
            _step = Step.Profile;
        }

        DrawProgress();

        const float topH = 34f;
        var navH = _step is Step.SignIn or Step.Done ? 8f : 62f;
        var contentH = ImGui.GetWindowSize().Y - Px(topH) - Px(navH);

        ImGui.SetCursorPos(new Vector2(0f, Px(topH)));
        PushScrollbarStyle();
        using (var content = ImRaii.Child("##osOnbContent", new Vector2(0f, contentH), false))
        {
            if (content.Success)
            {
                switch (_step)
                {
                    case Step.Welcome:
                        DrawWelcome();
                        break;
                    case Step.Design:
                        DrawDesign();
                        break;
                    case Step.Terms:
                        DrawTerms();
                        break;
                    case Step.SignIn:
                        DrawSignIn();
                        break;
                    case Step.PassphraseIntro:
                        DrawPassphraseIntro();
                        break;
                    case Step.Passphrase:
                        DrawPassphrase();
                        break;
                    case Step.PassphraseConfirm:
                        DrawPassphraseConfirm();
                        break;
                    case Step.Profile:
                        DrawProfile();
                        break;
                    case Step.Translations:
                        DrawTranslations();
                        break;
                    case Step.Done:
                        DrawDone();
                        break;
                }
            }
        }
        PopScrollbarStyle();

        DrawBottomNav();
    }

    /// <summary>A slim segmented progress bar plus a back chevron on the pre-sign-in steps.</summary>
    private void DrawProgress()
    {
        // Back on the pre-sign-in steps, and within the three passphrase pages (never back past sign-in).
        var canGoBack = ((_step > Step.Welcome && _step <= Step.Terms)
                         || _step == Step.Passphrase || _step == Step.PassphraseConfirm)
                        && !_saving && !_passphraseProcessing;
        if (OnboardingUi.DrawProgress((int)_step, TotalSteps, canGoBack))
        {
            _step = (Step)((int)_step - 1);
            _saveError = null;
        }
    }

    private void DrawBottomNav()
    {
        // The sign-in step advances itself once auth completes; the Done step has its own in-content button.
        if (_step is Step.SignIn or Step.Done)
        {
            return;
        }

        var winH = ImGui.GetWindowSize().Y;
        ImGui.SetCursorPos(new Vector2(0f, winH - Px(54f)));

        var canProceed = _step switch
        {
            Step.Terms => _tosAccepted,
            Step.PassphraseIntro => _passphraseAcknowledged,
            Step.Passphrase => _passphrase.Length >= PassphraseMinLength && _passphrase == _passphraseConfirm,
            Step.PassphraseConfirm => _passphraseUploaded || (_passphraseCopied && !_passphraseProcessing),
            // Name and avatar are both mandatory on the combined profile step.
            Step.Profile => _osName.Trim().Length >= AetherLove.Shared.ProfileLimits.DisplayNameMinLength
                            && _avatarConfirmed,
            _ => true,
        };

        var label = _saving || _passphraseProcessing
            ? Loc.T("onboarding.saving")
            : _step == Step.Profile
                ? (_saveError is not null ? Loc.T("onboarding.retry") : Loc.T("os_onboarding.finish"))
                : Loc.T("onboarding.next");

        if (DrawPrimaryButton(label, canProceed && !_saving && !_passphraseProcessing))
        {
            GoNext();
        }
    }

    private void GoNext()
    {
        if (_saving || _passphraseProcessing)
        {
            return;
        }

        switch (_step)
        {
            case Step.PassphraseConfirm:
                if (_passphraseUploaded)
                {
                    _step = Step.Profile;
                    return;
                }
                if (!_passphraseCopied || !CanAdvancePassphrase())
                {
                    return;
                }
                StartPassphraseUpload();
                return;
            case Step.Profile:
                StartComplete();
                return;
            case Step.Translations:
                CommitTranslationStep();
                _step = Step.Done;
                return;
            default:
                _step = (Step)((int)_step + 1);
                _saveError = null;
                return;
        }
    }

    private void DrawWelcome()
    {
        ImGui.Dummy(new Vector2(0f, Px(20f)));
        DrawHero("onb_welcome", FontAwesomeIcon.MobileAlt, Loc.T("os_onboarding.welcome_title"),
            Loc.T("os_onboarding.welcome_body"), 40f);
    }

    private void DrawDesign()
    {
        var t = ThemeService.Current;
        DrawHero("onb_design", FontAwesomeIcon.Palette, Loc.T("os_onboarding.header_design"), null, 26f);

        DrawDesignSectionLabel(Loc.T("settings.section_theme"), t);
        AppearancePicker.DrawThemeCards(ImGui.GetWindowSize().X, 16f);
        ImGui.Dummy(new Vector2(0f, Px(14f)));

        DrawDesignSectionLabel(Loc.T("settings.section_phone_size"), t);
        AppearancePicker.DrawPhoneSizeButtons(ImGui.GetWindowSize().X, 16f, t);
        ImGui.Dummy(new Vector2(0f, Px(14f)));

        DrawDesignLanguage();
        ImGui.Dummy(new Vector2(0f, Px(10f)));
    }

    /// <summary>A section heading for the "Make it yours" step, sized above the body so theme, phone size and
    /// language read as three clear, equal sections.</summary>
    private static void DrawDesignSectionLabel(string text, ThemeDefinition t)
    {
        ImGui.SetCursorPosX(Px(16f));
        using (UiFonts.H3?.Push())
        {
            ImGui.TextColored(t.AccentLight, text);
        }
        ImGui.Dummy(new Vector2(0f, Px(4f)));
    }

    private void DrawTerms()
    {
        DrawHero("onb_terms", FontAwesomeIcon.FileContract, Loc.T("os_onboarding.terms_title"), null, 26f);

        var availW = ImGui.GetWindowSize().X;
        var boxH = ImGui.GetContentRegionAvail().Y - Px(40f);
        ImGui.SetCursorPosX(Px(16f));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(1f, 1f, 1f, 0.045f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, Px(12f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(Px(14f), Px(12f)));
        using (var box = ImRaii.Child("##osTos", new Vector2(availW - Px(32f), boxH), false))
        {
            if (box.Success)
            {
                using (UiFonts.H3?.Push())
                {
                    ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X);
                    string[] paras =
                    {
                        Loc.T("os_onboarding.tos_1"),
                        Loc.T("os_onboarding.tos_2"),
                        Loc.T("os_onboarding.tos_3"),
                        Loc.T("os_onboarding.tos_4"),
                    };
                    foreach (var para in paras)
                    {
                        ImGui.TextColored(new Vector4(0.78f, 0.78f, 0.80f, 1f), para);
                        ImGui.Dummy(new Vector2(0f, Px(6f)));
                    }
                    ImGui.PopTextWrapPos();
                }
            }
        }
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor();

        ImGui.Dummy(new Vector2(0f, Px(8f)));
        ImGui.SetCursorPosX(Px(16f));
        ImGui.Checkbox(Loc.T("os_onboarding.terms_agree"), ref _tosAccepted);
    }

    /// <summary>The combined profile step: an OS display name (free text, spaces allowed) plus a mandatory photo.
    /// Both are required to finish; the profile-step gate blocks Finish until the name is long enough and a photo
    /// is confirmed.</summary>
    private void DrawProfile()
    {
        DrawHero("onb_profile", FontAwesomeIcon.UserCircle, Loc.T("os_onboarding.header_profile"),
            Loc.T("os_onboarding.name_body"), 26f);

        DrawAvatarSection();

        ImGui.Dummy(new Vector2(0f, Px(16f)));
        ImGui.SetCursorPosX(Px(20f));
        ImGui.TextColored(new Vector4(0.72f, 0.72f, 0.75f, 1f), Loc.T("os_onboarding.profile_name_label"));
        ImGui.Dummy(new Vector2(0f, Px(4f)));
        ImGui.SetCursorPosX(Px(20f));
        ImGui.SetNextItemWidth(ImGui.GetWindowSize().X - Px(40f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(10f));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(Px(11f), Px(9f)));
        ImGui.InputTextWithHint("##osName", Loc.T("os_onboarding.name_hint"), ref _osName, 50);
        ImGui.PopStyleVar(2);

        if (_saveError is not null)
        {
            ImGui.Dummy(new Vector2(0f, Px(10f)));
            ImGui.SetCursorPosX(Px(20f));
            ImGui.PushTextWrapPos(ImGui.GetWindowSize().X - Px(20f));
            ImGui.TextColored(UiColors.Danger, _saveError);
            ImGui.PopTextWrapPos();
        }
    }

    /// <summary>The final "you're all set" page. Setup already completed server-side when the profile step finished;
    /// this is just the celebration + the button that closes onboarding and drops the user on the home screen.</summary>
    private void DrawDone()
    {
        var t = ThemeService.Current;
        var winW = ImGui.GetWindowSize().X;
        var dl = ImGui.GetWindowDrawList();

        ImGui.Dummy(new Vector2(0f, ImGui.GetContentRegionAvail().Y * 0.16f));

        var cx = ImGui.GetWindowPos().X + winW * 0.5f;
        var cy = ImGui.GetCursorScreenPos().Y + Px(38f);
        var center = new Vector2(cx, cy);
        var pulse = AccessibilityService.ReduceMotion ? 0.5f : 0.5f + 0.5f * MathF.Sin((float)ImGui.GetTime() * 2.2f);
        dl.AddCircleFilled(center, Px(46f + 8f * pulse),
            ImGui.ColorConvertFloat4ToU32(t.Accent with { W = 0.10f + 0.06f * pulse }), 48);
        var doneIcon = CustomIcons.Get("onb_done")?.GetWrapOrDefault();
        if (doneIcon != null)
        {
            var half = Px(38f * 0.72f);
            dl.AddImage(doneIcon.Handle, center - new Vector2(half, half), center + new Vector2(half, half));
        }
        else
        {
            dl.AddCircleFilled(center, Px(38f), t.AccentU32, 48);
            dl.AddCircleFilled(new Vector2(cx, cy - Px(13f)), Px(22f),
                ImGui.ColorConvertFloat4ToU32(t.AccentLight with { W = 0.22f }), 32);
            IconDraw.AddCentered(dl, FontAwesomeIcon.Check, Px(36f), center, 0xFFFFFFFFu);
        }
        ImGui.Dummy(new Vector2(0f, Px(94f)));

        using (UiFonts.H1?.Push())
        {
            var title = Loc.T("os_onboarding.done_title");
            ImGui.SetCursorPosX((winW - ImGui.CalcTextSize(title).X) * 0.5f);
            ImGui.TextUnformatted(title);
        }
        ImGui.Dummy(new Vector2(0f, Px(8f)));
        DrawCenteredParagraph(Loc.T("os_onboarding.done_body"), winW - Px(48f), new Vector4(0.72f, 0.72f, 0.75f, 1f));

        ImGui.Dummy(new Vector2(0f, Px(26f)));
        if (DrawPrimaryButton(Loc.T("os_onboarding.done_start"), true))
        {
            Os.OsBootIntro.Play();
            _router.Navigate(_bootstrap.ResolveNextStartupScreen());
        }
    }

    private void DrawSignIn()
    {
        if (_authService.State == AuthFlowState.Idle
            && _bootstrap.LastResult is SessionBootstrapResult.SignedInOnboarding
                                     or SessionBootstrapResult.SignedInActive)
        {
            _step = _bootstrap.LastConnection?.HasKeyBundle == true ? Step.Profile : Step.PassphraseIntro;
            return;
        }

        var t = ThemeService.Current;
        DrawHero("onb_signin", FontAwesomeIcon.SignInAlt, Loc.T("os_onboarding.header_signin"),
            Loc.T("onboarding.auth_intro"), 30f);
        var centerX = ImGui.GetContentRegionAvail().X * 0.5f;

        switch (_authService.State)
        {
            case AuthFlowState.Idle:
                _authCompletedAt = DateTime.MinValue;
                _postAuthBootstrapStarted = false;
                if (DrawPrimaryButton(Loc.T("onboarding.auth_signin_with_xivauth"), true))
                {
                    _authService.StartSignIn();
                }
                break;
            case AuthFlowState.Starting:
                DrawAuthSpinner(centerX, Loc.T("onboarding.auth_contacting"));
                break;
            case AuthFlowState.AwaitingBrowser:
                DrawAuthSpinner(centerX, Loc.T("onboarding.auth_complete_in_browser"));
                ImGui.Dummy(new Vector2(0f, Px(6f)));
                DrawCenteredParagraph(Loc.T("onboarding.auth_browser_opened"), ImGui.GetWindowSize().X - Px(48f),
                    new Vector4(0.72f, 0.72f, 0.75f, 1f));
                ImGui.Dummy(new Vector2(0f, Px(10f)));
                var totalW = Px(160f * 2f + 8f);
                ImGui.SetCursorPosX(centerX - totalW * 0.5f);
                PushThemeButton(t);
                if (ImGui.Button(Loc.T("onboarding.auth_open_browser_again"), Px(160f, 30f)))
                {
                    _authService.ReopenBrowser();
                }
                PopThemeButton();
                ImGui.SameLine(0f, Px(8f));
                if (ImGui.Button(Loc.T("onboarding.auth_cancel"), Px(160f, 30f)))
                {
                    _authService.Cancel();
                }
                break;
            case AuthFlowState.Completed:
                DrawAuthCompleted(centerX);
                break;
            case AuthFlowState.Failed:
                var msg = _authService.LastFailureWasExpiry
                    ? Loc.T("onboarding.auth_timeout")
                    : (_authService.ErrorMessage ?? Loc.T("onboarding.auth_failed"));
                ImGui.Dummy(new Vector2(0f, Px(4f)));
                DrawCenteredParagraph(msg, ImGui.GetWindowSize().X - Px(48f), UiColors.Danger);
                ImGui.Dummy(new Vector2(0f, Px(10f)));
                if (DrawPrimaryButton(Loc.T("onboarding.auth_try_again"), true))
                {
                    _authService.StartSignIn();
                }
                break;
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
        if (_bootstrap.LastResult == SessionBootstrapResult.ServerUnreachable)
        {
            _router.Navigate(Screen.Offline);
            return;
        }
        // New device on an existing account: unlock via passphrase, not fresh OS onboarding.
        if (_bootstrap.NeedsPassphraseUnlock)
        {
            _router.Navigate(Screen.PassphraseUnlock);
            return;
        }
        if (_bootstrap.NeedsEncryptionRecovery)
        {
            _router.Navigate(Screen.EncryptionRecovery);
            return;
        }
        // Skip the rest of OS onboarding ONLY for an account that is already fully set up (onboarded AND a key
        // bundle exists). An account that is marked onboarded but whose profile has no key bundle still needs the
        // passphrase step, so it must NOT skip here.
        if (!_bootstrap.NeedsOsSetup)
        {
            _router.Navigate(_bootstrap.ResolveNextStartupScreen());
            return;
        }

        var ok = Loc.T("onboarding.auth_signed_in");
        ImGui.SetCursorPosX(centerX - ImGui.CalcTextSize(ok).X * 0.5f);
        ImGui.TextColored(UiColors.Success, ok);
        if (_authCompletedAt == DateTime.MinValue)
        {
            _authCompletedAt = DateTime.Now;
        }
        if ((DateTime.Now - _authCompletedAt).TotalSeconds >= 0.6)
        {
            _authCompletedAt = DateTime.MinValue;
            _authService.Cancel();
            // A returning account that already has a key bundle (e.g. re-onboarding after a profile reset) skips
            // straight to the profile step; a genuinely new account sets its passphrase first.
            _step = _bootstrap.LastConnection?.HasKeyBundle == true ? Step.Profile : Step.PassphraseIntro;
        }
    }

    private static void DrawAuthSpinner(float centerX, string label)
    {
        var dots = (int)(DateTime.Now.TimeOfDay.TotalSeconds * 3) % 4;
        var line = label + new string('.', dots);
        ImGui.SetCursorPosX(centerX - ImGui.CalcTextSize(line).X * 0.5f);
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), line);
    }

    /// <summary>Page 1 of 3: what a passphrase is and the "no recovery" warning, gated behind an "I understand"
    /// acknowledgement.</summary>
    private void DrawPassphraseIntro()
    {
        var winW = ImGui.GetWindowSize().X;

        DrawHero("onb_passphrase", FontAwesomeIcon.Lock, Loc.T("onboarding.pass_heading"),
            Loc.T("onboarding.pass_intro"), 30f);

        DrawInfoCallout(Loc.T("onboarding.pass_warning"), new Vector4(1f, 0.66f, 0.22f, 1f),
            FontAwesomeIcon.ExclamationTriangle);
        ImGui.Dummy(new Vector2(0f, Px(14f)));

        ImGui.SetCursorPosX(Px(16f));
        ImGui.Checkbox("##osPassAck", ref _passphraseAcknowledged);
        ImGui.SameLine();
        ImGui.PushTextWrapPos(winW - Px(16f));
        ImGui.TextUnformatted(Loc.T("onboarding.pass_ack"));
        ImGui.PopTextWrapPos();
    }

    /// <summary>Page 2 of 3: choose and confirm the passphrase. Nothing is uploaded here; that happens on the
    /// confirm page once the user has saved it.</summary>
    private void DrawPassphrase()
    {
        var t = ThemeService.Current;
        var winW = ImGui.GetWindowSize().X;

        // Editing the passphrase invalidates a prior "I copied it" from the confirm page, so re-copying is forced.
        _passphraseCopied = false;

        DrawHero("onb_passphrase", FontAwesomeIcon.Lock, Loc.T("onboarding.pass_set_title"), null, 28f);

        var fieldW = winW - Px(32f);
        ImGui.SetCursorPosX(Px(16f));
        ImGui.TextColored(t.AccentLight, Loc.T("onboarding.pass_label"));
        ImGui.SetCursorPosX(Px(16f));
        DrawPassphraseField("##osPass", ref _passphrase, fieldW);
        ImGui.Dummy(new Vector2(0f, Px(6f)));
        ImGui.SetCursorPosX(Px(16f));
        ImGui.TextColored(t.AccentLight, Loc.T("onboarding.pass_confirm_label"));
        ImGui.SetCursorPosX(Px(16f));
        DrawPassphraseField("##osPassConfirm", ref _passphraseConfirm, fieldW);
        ImGui.Dummy(new Vector2(0f, Px(6f)));

        var lengthOk = _passphrase.Length >= PassphraseMinLength;
        var matches = _passphrase.Length > 0 && _passphrase == _passphraseConfirm;
        if (!lengthOk && _passphrase.Length > 0)
        {
            ImGui.SetCursorPosX(Px(16f));
            ImGui.TextColored(UiColors.Danger, Loc.T("onboarding.pass_err_too_short", PassphraseMinLength));
        }
        if (!matches && _passphraseConfirm.Length > 0)
        {
            ImGui.SetCursorPosX(Px(16f));
            ImGui.TextColored(UiColors.Danger, Loc.T("onboarding.pass_err_mismatch"));
        }
    }

    /// <summary>Page 3 of 3: shows the chosen passphrase in a large box and forces "I have copied this somewhere
    /// safe" before the passphrase is wrapped and uploaded.</summary>
    private void DrawPassphraseConfirm()
    {
        var winW = ImGui.GetWindowSize().X;

        DrawHero("onb_passphrase", FontAwesomeIcon.Key, Loc.T("onboarding.pass_show_title"),
            Loc.T("onboarding.pass_show_sub"), 28f);

        if (DrawSecretBox("##osPassShow", _passphrase, Loc.T("onboarding.pass_copy")))
        {
            ImGui.SetClipboardText(_passphrase);
        }

        ImGui.Dummy(new Vector2(0f, Px(12f)));
        ImGui.SetCursorPosX(Px(16f));
        ImGui.Checkbox("##osPassCopied", ref _passphraseCopied);
        ImGui.SameLine();
        ImGui.PushTextWrapPos(winW - Px(16f));
        ImGui.TextUnformatted(Loc.T("onboarding.pass_copied_ack"));
        ImGui.PopTextWrapPos();

        if (_passphraseError is not null)
        {
            ImGui.Dummy(new Vector2(0f, Px(6f)));
            ImGui.SetCursorPosX(Px(16f));
            ImGui.PushTextWrapPos(winW - Px(16f));
            ImGui.TextColored(UiColors.Danger, _passphraseError);
            ImGui.PopTextWrapPos();
        }
        if (_passphraseProcessing)
        {
            ImGui.Dummy(new Vector2(0f, Px(6f)));
            ImGui.SetCursorPosX(Px(16f));
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), Loc.T("onboarding.pass_securing"));
        }
    }

    private void DrawPassphraseField(string id, ref string buf, float width)
    {
        var eyeW = Px(30f);
        ImGui.SetNextItemWidth(width - eyeW - Px(4f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(9f));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(Px(10f), Px(8f)));
        var flags = _showPassphrase ? ImGuiInputTextFlags.None : ImGuiInputTextFlags.Password;
        ImGui.InputText(id, ref buf, 256, flags);
        ImGui.SameLine(0, Px(4f));
        ImGui.PushFont(Plugin.PluginInterface.UiBuilder.FontIcon);
        var icon = _showPassphrase ? FontAwesomeIcon.EyeSlash.ToIconString() : FontAwesomeIcon.Eye.ToIconString();
        if (ImGui.Button(icon + id + "Eye", new Vector2(eyeW, 0)))
        {
            _showPassphrase = !_showPassphrase;
        }
        ImGui.PopFont();
        ImGui.PopStyleVar(2);
    }

    private bool CanAdvancePassphrase() =>
        _passphrase.Length >= PassphraseMinLength
        && _passphrase == _passphraseConfirm
        && _passphraseAcknowledged
        && !_passphraseProcessing;

    /// <summary>Wraps a freshly generated keypair with a passphrase-derived KEK and uploads the bundle for the
    /// signed-in profile; the passphrase never leaves the device. Advancing to the Name step is deferred to the
    /// UI thread via a flag (never mutate step from the worker).</summary>
    private void StartPassphraseUpload()
    {
        if (_passphraseProcessing || _passphraseUploaded)
        {
            return;
        }
        _passphraseProcessing = true;
        _passphraseError = null;
        var passphrase = _passphrase;

        _ = Task.Run(async () =>
        {
            try
            {
                var (pubKey, privKey) = _crypto.GenerateIdentityKeyPair();
                var salt = new byte[Services.Crypto.CryptoService.KdfSaltLength];
                RandomNumberGenerator.Fill(salt);
                const int MemoryKb = 64 * 1024;
                const int Iterations = 3;
                const int Parallelism = 1;
                var kek = _crypto.DeriveKEK(passphrase, salt, MemoryKb, Iterations, Parallelism);
                var (wrapped, wrapNonce) = _crypto.WrapPrivateKey(privKey, kek);
                var bundle = new KeyBundleDto(
                    PublicKey: pubKey,
                    EncryptedPrivateKey: wrapped,
                    KdfSalt: salt,
                    KdfMemoryKb: MemoryKb,
                    KdfIterations: Iterations,
                    KdfParallelism: Parallelism,
                    WrapNonce: wrapNonce);
                await _hub.UploadKeyBundleAsync(bundle, CancellationToken.None).ConfigureAwait(false);
                // The same passphrase/KEK covers the whole account: publish its parameters + verifier so a
                // second profile's bundle can be wrapped under it and other devices can validate the
                // passphrase, and keep the KEK locally so nothing ever re-prompts on this install.
                try
                {
                    var (verifier, verifierNonce) = _crypto.CreatePassphraseVerifier(kek);
                    await _hub.SetAccountPassphraseAsync(
                        new AccountPassphraseDto(salt, MemoryKb, Iterations, Parallelism, verifier, verifierNonce),
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Plugin.Log.Warning(ex, "[OsOnboarding] SetAccountPassphrase failed; sibling provisioning will fall back.");
                }
                _keyStorage.Store(pubKey, privKey);
                _keyStorage.StoreKek(kek, salt, MemoryKb, Iterations, Parallelism);
                _passphraseUploaded = true;
                _advanceFromPassphrasePending = true;
            }
            catch (Exception ex)
            {
                _passphraseError = HubErrorText.Localize(ex);
                Plugin.Log.Warning(ex, "[OsOnboarding] Passphrase upload failed.");
            }
            finally
            {
                _passphraseProcessing = false;
            }
        });
    }

    private void StartComplete()
    {
        _saving = true;
        _saveError = null;
        var name = _osName.Trim();
        var avatarPath = _avatarConfirmed ? _avatarPath : null;
        var avatarCrop = _avatarCropRect;
        _ = Task.Run(async () =>
        {
            try
            {
                var upload = BuildAvatarUpload(avatarPath, avatarCrop);
                if (upload is not null)
                {
                    var stored = await _hub.SetOsAvatarAsync(upload, CancellationToken.None).ConfigureAwait(false);
                    _osAvatar.SetFromBytes(stored);
                }
                await _hub.CompleteOsOnboardingAsync(new OsOnboardingCompleteDto(name), CancellationToken.None)
                    .ConfigureAwait(false);
                // Refresh the connection snapshot so HasKeyBundle reflects the passphrase just set here;
                // otherwise the AetherLove first-run's OnShow would re-show the passphrase step.
                await _bootstrap.RefreshConnectionInfoAsync(CancellationToken.None).ConfigureAwait(false);
                // Refresh the account snapshot too so LastAccount.OsAvatarWebp carries the avatar just uploaded;
                // otherwise the AetherLove avatar step's "use my AetherOS photo" option stays hidden this session.
                await _bootstrap.RefreshAccountInfoAsync(CancellationToken.None).ConfigureAwait(false);
                _completePending = true;
            }
            catch (Exception ex)
            {
                _saveError = HubErrorText.Localize(ex);
                Plugin.Log.Warning(ex, "[OsOnboarding] CompleteOsOnboarding failed.");
            }
            finally
            {
                _saving = false;
            }
        });
    }
}
