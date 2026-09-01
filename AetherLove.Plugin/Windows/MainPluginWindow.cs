using System;
using System.Numerics;
using AetherLove.Config;
using AetherLove.Emoji;
using AetherLove.Navigation;
using AetherLove.Screens;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using AetherLove.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;

namespace AetherLove.Windows;

/// <summary>Main plugin window. Renders the active screen with a cross-fade.</summary>
public class MainPluginWindow : Window, IDisposable
{
    private readonly ScreenRouter _router;
    private readonly HomeScreen _homeScreen;
    private readonly SplashScreen _splashScreen;
    private readonly OsOnboardingScreen _osOnboardingScreen;
    private readonly BannedScreen _bannedScreen;
    private readonly WarningAcknowledgeScreen _warningsAckScreen;
    private readonly ModeratorMessageScreen _moderatorMessageScreen;
    private readonly StaffNoticeScreen _staffNoticeScreen;
    private readonly PassphraseUnlockScreen _passphraseUnlockScreen;
    private readonly EncryptionRecoveryScreen _encryptionRecoveryScreen;
    private readonly OfflineScreen _offlineScreen;
    private readonly SessionExpiredScreen _sessionExpiredScreen;
    private readonly OutdatedScreen _outdatedScreen;
    private readonly NotificationCenter _notifications;
    private readonly OwnAvatarCache _ownAvatar;
    private readonly OsAvatarCache _osAvatar;
    private readonly Services.Auth.SessionBootstrapper _bootstrap;
    private readonly Services.Signal.AetherSignalService _signal;
    private readonly Services.Auth.TokenService _tokens;

    private MiniWindow? _miniWindow;

    private readonly PhoneShellWidget _phoneShell = new();

    private Screen? _lastScreen;
    private AetherOS.Sdk.IAetherApp? _foregroundApp;
    private float _transitionAlpha = 1f;
    private const float TransitionSpeed = 12f;

    private bool _startupFadeActive;
    private float _startupFadeT;
    private const float StartupFadeDur = 0.45f;
    private static readonly Vector4 StartupFadeColor = new(1f / 255f, 10f / 255f, 28f / 255f, 1f);

    public MainPluginWindow(
        ScreenRouter router,
        HomeScreen homeScreen,
        SplashScreen splashScreen,
        OsOnboardingScreen osOnboardingScreen,
        BannedScreen bannedScreen,
        WarningAcknowledgeScreen warningsAckScreen,
        ModeratorMessageScreen moderatorMessageScreen,
        StaffNoticeScreen staffNoticeScreen,
        PassphraseUnlockScreen passphraseUnlockScreen,
        EncryptionRecoveryScreen encryptionRecoveryScreen,
        OfflineScreen offlineScreen,
        SessionExpiredScreen sessionExpiredScreen,
        Services.Auth.TokenService tokens,
        OutdatedScreen outdatedScreen,
        NotificationCenter notifications,
        OwnAvatarCache ownAvatar,
        OsAvatarCache osAvatar,
        Services.Auth.SessionBootstrapper bootstrap,
        Services.Signal.AetherSignalService signal,
        Os.OsShell osShell,
        Os.NotificationShade osShade,
        Os.StatusBar osStatusBar,
        Os.AppCapabilities capabilities,
        Os.ShareSheet osShareSheet,
        Os.OsTour osTour,
        Os.NewAppOffer newAppOffer,
        Os.TranslationOffer translationOffer,
        Os.TogetherOnboarding partyIntro,
        Services.Sparks.SparkActivityReporter sparkActivity,
        SkinPreviewWindow skinPreview,
        PartyDockWindow partyDock
    ) : base("AetherLove##MainWindow",
             ImGuiWindowFlags.NoResize
           | ImGuiWindowFlags.NoScrollbar
           | ImGuiWindowFlags.NoScrollWithMouse
           | ImGuiWindowFlags.NoTitleBar
           | ImGuiWindowFlags.NoDocking
           | ImGuiWindowFlags.NoBackground)
    {
        Size = UiScale.Design;
        SizeCondition = ImGuiCond.Always;

        _router = router;
        _homeScreen = homeScreen;
        _splashScreen = splashScreen;
        _osOnboardingScreen = osOnboardingScreen;
        _bannedScreen = bannedScreen;
        _warningsAckScreen = warningsAckScreen;
        _moderatorMessageScreen = moderatorMessageScreen;
        _staffNoticeScreen = staffNoticeScreen;
        _passphraseUnlockScreen = passphraseUnlockScreen;
        _encryptionRecoveryScreen = encryptionRecoveryScreen;
        _offlineScreen = offlineScreen;
        _sessionExpiredScreen = sessionExpiredScreen;
        _tokens = tokens;
        _outdatedScreen = outdatedScreen;
        _notifications = notifications;
        _ownAvatar = ownAvatar;
        _osAvatar = osAvatar;
        _bootstrap = bootstrap;
        _signal = signal;
        _osShell = osShell;
        _osShade = osShade;
        _osStatusBar = osStatusBar;
        _capabilities = capabilities;
        _osShareSheet = osShareSheet;
        _osTour = osTour;
        _newAppOffer = newAppOffer;
        _translationOffer = translationOffer;
        _partyIntro = partyIntro;
        _sparkActivity = sparkActivity;
        _skinPreview = skinPreview;
        _partyDock = partyDock;
    }
    private readonly Services.Sparks.SparkActivityReporter _sparkActivity;
    private readonly SkinPreviewWindow _skinPreview;
    private readonly PartyDockWindow _partyDock;
    private readonly Os.OsShell _osShell;
    private readonly Os.NotificationShade _osShade;
    private readonly Os.StatusBar _osStatusBar;
    private readonly Os.AppCapabilities _capabilities;
    private readonly Os.ShareSheet _osShareSheet;
    private readonly Os.OsTour _osTour;
    private readonly Os.NewAppOffer _newAppOffer;
    private readonly Os.TranslationOffer _translationOffer;
    private readonly Os.TogetherOnboarding _partyIntro;

    public void SetMiniWindow(MiniWindow mini) => _miniWindow = mini;

    private bool _recenterRequested;
    private bool _offlineGateWasActive;
    private bool _phoneFocused;
    private bool _poweredOff;

    /// <summary>Whether the phone has been open at all this session, which is what makes restoring from the
    /// bubble a return rather than a first launch.</summary>
    private bool _hasContext;

    /// <summary>Whether the phone window (or one of its child regions) currently holds ImGui focus. False when
    /// the user clicks out to the game world or another window; used to pause the tomestone emote.</summary>
    public bool IsPhoneFocused => _phoneFocused;

    /// <summary>Queues a one-shot recenter on the next frame (ImGui isn't valid from the command thread).</summary>
    public void RequestRecenter() => _recenterRequested = true;

    public override void OnOpen()
    {
        _hasContext = true;
        _ownAvatar.Refresh(onlyIfCold: true);
        _osAvatar.Refresh(onlyIfCold: true);

        // Coming back from a power-off has to bring the session up again, because powering off dropped the
        // connection. Restoring the bubble is not a power-on and must not re-run the ladder.
        if (_poweredOff)
        {
            _poweredOff = false;
            PhonePower.Set(true);
            _ = _bootstrap.RunAsync();
        }

        // Runs before this frame's navigation is processed, so it wins over the open path's target.
        if (_notifications.HasPendingWarning)
        {
            _notifications.ClearPendingWarning();
            _warningsAckScreen.RequestLiveAcknowledge();
            _router.Navigate(Screen.WarningsAcknowledge);
        }
        else if (_notifications.HasPendingModeratorMessage)
        {
            _notifications.ClearPendingModeratorMessage();
            _moderatorMessageScreen.RequestLiveAcknowledge();
            _router.Navigate(Screen.ModeratorMessages);
        }
        else if (_notifications.HasPendingStaffNotice)
        {
            _notifications.ClearPendingStaffNotice();
            PostStaffNoticeNotification();
            _staffNoticeScreen.RequestLiveAcknowledge();
            _router.Navigate(Screen.StaffNotice);
        }
    }

    /// <summary>Posts the shade entry that survives the staff-notice gate: once acknowledged, it is the pointer
    /// back to the Settings history, and the Settings staff page clears it by tag. Tagged, so a second batch
    /// replaces it rather than stacking.</summary>
    private void PostStaffNoticeNotification()
    {
        if (_router.Current == Screen.StaffNotice)
        {
            return;
        }
        _osShell.PostNotification(
            appId: "settings",
            title: Loc.T("notif.staff_notice_title"),
            body: Loc.T("notif.staff_notice_body"),
            onTap: () => _osShell.OpenApp("settings"),
            tag: StaffNoticeScreen.NotificationTag);
    }

    /// <summary>Fires on every close path (minimize, close button, ESC), so the foreground app always gets its
    /// OnBackground and the next open fires a fresh OnForeground.</summary>
    public override void OnClose()
    {
        _foregroundApp?.OnBackground();
        _foregroundApp = null;
        _phoneFocused = false;
    }

    /// <summary>Whether either surface of the phone is up. Everything AetherOS puts outside its own windows
    /// (the DTR entries, the floating Aetherling) hangs off this, so a powered-off phone leaves nothing of
    /// itself on screen.</summary>
    public bool IsPoweredOn => IsOpen || (_miniWindow?.IsOpen ?? false);

    public void OpenToChat()
    {
        if (_miniWindow != null)
        {
            _miniWindow.IsOpen = false;
        }
        IsOpen = true;
        _osShell.SendIntent("aetherlove", AetherOS.Sdk.OsIntents.Create(AetherOS.Sdk.OsIntents.OpenMessages));
    }

    /// <summary>Restores the full window to the OS home screen, ignoring whatever was last open. The mini
    /// bubble tap uses this so it always lands on home rather than resuming the previous app.</summary>
    public void OpenToHome()
    {
        if (_miniWindow != null)
        {
            _miniWindow.IsOpen = false;
        }
        IsOpen = true;
        _hasContext = true;
        _osShell.GoHome();
    }

    /// <summary>Comes back from the bubble on whatever was open before. Minimising is not leaving, so the app
    /// and screen are still where they were; only a phone that has not been opened this session has nothing to
    /// return to, and that one lands on home.</summary>
    public void Restore()
    {
        if (!_hasContext)
        {
            OpenToHome();
            return;
        }
        if (_miniWindow != null)
        {
            _miniWindow.IsOpen = false;
        }
        IsOpen = true;
    }

    public void OpenToDeck()
    {
        if (_miniWindow != null)
        {
            _miniWindow.IsOpen = false;
        }
        IsOpen = true;
        _osShell.SendIntent("aetherlove", AetherOS.Sdk.OsIntents.Create(AetherOS.Sdk.OsIntents.OpenDeck));
    }

    public void OpenToSettings()
    {
        if (_miniWindow != null)
        {
            _miniWindow.IsOpen = false;
        }
        IsOpen = true;
        _osShell.SendIntent("aetherlove", AetherOS.Sdk.OsIntents.Create(AetherOS.Sdk.OsIntents.OpenSettings));
    }

    public void OpenToNews()
    {
        if (_miniWindow != null)
        {
            _miniWindow.IsOpen = false;
        }
        IsOpen = true;
        _osShell.OpenApp("news");
    }

    public void OpenToHangouts()
    {
        if (_miniWindow != null)
        {
            _miniWindow.IsOpen = false;
        }
        IsOpen = true;
        _osShell.OpenApp("hangouts");
    }

    /// <summary>Brings the phone up full size on the Aetherling's status page. The floating creature's own
    /// menu is the only caller: it sits outside the phone, including while the phone is a bubble.</summary>
    public void OpenToAetherlingStatus()
    {
        if (_miniWindow != null)
        {
            _miniWindow.IsOpen = false;
        }
        IsOpen = true;
        _osShell.SendIntent("aetherling", AetherOS.Sdk.OsIntents.Create(AetherOS.Sdk.OsIntents.AetherlingStatus));
    }

    public void OpenToMessenger()
    {
        if (_miniWindow != null)
        {
            _miniWindow.IsOpen = false;
        }
        IsOpen = true;
        _osShell.OpenApp("messenger");
    }

    public void OpenToGroove()
    {
        if (_miniWindow != null)
        {
            _miniWindow.IsOpen = false;
        }
        IsOpen = true;
        _osShell.OpenApp("groove");
    }

    public void OpenToMarketItem(uint itemId)
    {
        if (_miniWindow != null)
        {
            _miniWindow.IsOpen = false;
        }
        IsOpen = true;
        _osShell.SendIntent("market", AetherOS.Sdk.OsIntents.CreateMarketItem(itemId));
    }

    public void OpenToPhotoSettings()
    {
        if (_miniWindow != null)
        {
            _miniWindow.IsOpen = false;
        }
        IsOpen = true;
        _osShell.SendIntent("photos", AetherOS.Sdk.OsIntents.Create(AetherOS.Sdk.OsIntents.PhotosOpenSettings));
    }

    public void OpenToMyHangout()
    {
        if (_miniWindow != null)
        {
            _miniWindow.IsOpen = false;
        }
        IsOpen = true;
        _osShell.SendIntent("hangouts", AetherOS.Sdk.OsIntents.Create(AetherOS.Sdk.OsIntents.OpenManage));
    }

    public void OpenToClock()
    {
        if (_miniWindow != null)
        {
            _miniWindow.IsOpen = false;
        }
        IsOpen = true;
        _osShell.SendIntent("clock", AetherOS.Sdk.OsIntents.Create(AetherOS.Sdk.OsIntents.OpenClockTimers));
    }

    public void OpenToTimers()
    {
        if (_miniWindow != null)
        {
            _miniWindow.IsOpen = false;
        }
        IsOpen = true;
        _osShell.OpenApp("timers");
    }

    public void OpenToCalendar()
    {
        if (_miniWindow != null)
        {
            _miniWindow.IsOpen = false;
        }
        IsOpen = true;
        _osShell.OpenApp("calendar");
    }

    public void Dispose()
    {
        _splashScreen.Dispose();
        _phoneShell.Dispose();
    }

    /// <summary>Visibility rule: always hidden while logged out. In combat, Hide = auto-hide via
    /// DrawConditions; Minimize / LeaveOpen = always visible (bootstrap handles the explicit swap).</summary>
    public override bool DrawConditions()
        => Plugin.ClientState.IsLoggedIn
           && !Widgets.SelfieCaptureOverlay.Active
           && (Plugin.Configuration.CombatBehavior != CombatBehavior.Hide
               || !Plugin.Condition[ConditionFlag.InCombat]);

    private float _savedFontGlobalScale = 1f;

    public override void PreDraw()
    {
        Size = Px(ThemeService.Current.WindowWidth, UiScale.Design.Y);

        var appHoldsDrags = _router.Current == Screen.App
            && _osShell.ActiveSurfaceApp is { LocksWindowDrag: true };
        if (Plugin.Configuration.LockPhonePosition || appHoldsDrags
            || AetherLove.Widgets.VolumeBar.HoldsWindowDrag)
        {
            Flags |= ImGuiWindowFlags.NoMove;
        }
        else
        {
            Flags &= ~ImGuiWindowFlags.NoMove;
        }

        // A window opened without any navigation (post-reload initial screen) still owes that screen its
        // OnShow; without this the splash draws un-armed (no logo, no bootstrap).
        if (_lastScreen is null && !_router.NavigationOccurred)
        {
            _lastScreen = _router.Current;
            OnScreenChanged(_lastScreen.Value);
        }

        if (_router.NavigationOccurred)
        {
            _router.NavigationOccurred = false;

            var newScreen = _router.Current;
            if (_lastScreen != newScreen)
            {
                var previousScreen = _lastScreen;
                _lastScreen = newScreen;
                OnScreenChanged(newScreen);
                // Leaving the startup splash cross-fades into the first screen (the splash has already faded
                // its own content out to the same navy, so the cover picks up seamlessly).
                if (previousScreen == Screen.Splash && !AccessibilityService.ReduceMotion)
                {
                    _startupFadeActive = true;
                    _startupFadeT = 0f;
                }
            }

            // Foreground/background lifecycle, driven by what is actually shown: entering an app (from any
            // screen, an in-place swap between two surface apps, or the window reopening onto one) fires
            // OnForeground before that frame's Draw; leaving it fires OnBackground. Render-only pauses
            // (combat auto-hide, the selfie overlay) never fire either.
            var surfaceApp = newScreen == Screen.App ? _osShell.ActiveSurfaceApp : null;
            if (!ReferenceEquals(surfaceApp, _foregroundApp))
            {
                _foregroundApp?.OnBackground();
                _foregroundApp = surfaceApp;
                surfaceApp?.OnForeground();
                if (surfaceApp is not null)
                {
                    _sparkActivity.NoteAppForeground(surfaceApp.Id);
                }
            }

            _transitionAlpha = 0.88f;
        }

        var dt = (float)ImGui.GetIO().DeltaTime;
        _transitionAlpha = Math.Clamp(_transitionAlpha + dt * TransitionSpeed, 0f, 1f);

        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, _transitionAlpha);

        // Dalamud's global font scale would multiply every glyph on top of the phone's own scaling and
        // overflow the fixed window; pin it to 1 for our draw and restore in PostDraw. Pinned last so no
        // fallible PreDraw code runs between pin and restore.
        FontDiagnostics.Sample("MainWindow.PreDraw/before-pin");
        _savedFontGlobalScale = FontScalePin.Pin();
        FontDiagnostics.Sample("MainWindow.PreDraw/after-pin");
    }

    private const string BezelMenuId = "##phoneBezelMenu";

    private static float BezelLeft => ThemeService.Current.BezelLeft;
    private static float BezelRight => ThemeService.Current.BezelRight;
    private static float BezelTop => ThemeService.Current.BezelTop;
    private static float BezelBottom => ThemeService.Current.BezelBottom;

    public override void Draw()
    {
        if (_recenterRequested)
        {
            _recenterRequested = false;
            var vp = ImGui.GetMainViewport();
            ImGui.SetWindowPos(vp.Pos + (vp.Size - ImGui.GetWindowSize()) * 0.5f);
        }

        // While a size-preset change rebuilds the fonts, drawing would fall back to the default font at the
        // wrong size.
        if (!UiFonts.Ready)
        {
            DrawFontRebuildLoader();
            return;
        }

        using var bodyFont = UiFonts.Body?.Push();

        _phoneFocused = ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);

        ModalHost.Instance?.SetAnchor(ImGui.GetWindowPos(), ImGui.GetWindowSize());
        _skinPreview.SetAnchor(ImGui.GetWindowPos(), ImGui.GetWindowSize());
        _partyDock.SetAnchor(ImGui.GetWindowPos(), ImGui.GetWindowSize());

        _phoneShell.DrawBackground(ImGui.GetWindowPos(), ImGui.GetWindowSize());

        DrawBezelButtons();

        var winSize = ImGui.GetWindowSize();
        var contentW = winSize.X - Px(BezelLeft) - Px(BezelRight);
        var contentH = winSize.Y - Px(BezelTop) - Px(BezelBottom);

        _osShell.Connected = _signal.IsConnected;
        if (_signal.IsConnected)
        {
            _offlineGateWasActive = false;
        }

        ImGui.SetCursorPos(Px(BezelLeft, BezelTop));
        ImGui.BeginChild("##bezel", new Vector2(contentW, contentH), false,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        ImGui.PushTextWrapPos(contentW);

        // A dead refresh token is not a connection problem, and the reconnect loop will never fix it: every
        // retry re-refreshes, is rejected again, and the phone sits on "offline" for a session that ended.
        // Route out of whatever is on screen, except the flows that are already about signing in.
        if (_tokens.SessionExpired
            && _router.Current is not (Screen.SessionExpired or Screen.Splash or Screen.Outdated))
        {
            _router.Navigate(Screen.SessionExpired);
        }

        switch (_router.Current)
        {
            case Screen.Splash:
                _splashScreen.Draw();
                break;
            case Screen.Home:
                _homeScreen.Draw();
                break;
            case Screen.App:
                DrawSurfaceApp();
                break;
            case Screen.OsOnboarding:
                _osOnboardingScreen.Draw();
                break;
            case Screen.Banned:
                _bannedScreen.Draw();
                break;
            case Screen.WarningsAcknowledge:
                _warningsAckScreen.Draw();
                break;
            case Screen.ModeratorMessages:
                _moderatorMessageScreen.Draw();
                break;
            case Screen.StaffNotice:
                _staffNoticeScreen.Draw();
                break;
            case Screen.PassphraseUnlock:
                _passphraseUnlockScreen.Draw();
                break;
            case Screen.EncryptionRecovery:
                _encryptionRecoveryScreen.Draw();
                break;
            case Screen.Offline:
                _offlineScreen.Draw();
                break;
            case Screen.SessionExpired:
                _sessionExpiredScreen.Draw();
                break;
            case Screen.Outdated:
                _outdatedScreen.Draw();
                break;
        }

        ImGui.PopTextWrapPos();
        ImGui.EndChild();

        DrawStartupFadeIn(ImGui.GetWindowPos(), ImGui.GetWindowSize());

        // A flat battery takes the whole screen over whatever the user was doing, so nothing else is drawn: no
        // status bar, no home pill, no tour. Recharging drops them straight back where they were.
        if (Os.FlatBatteryOverlay.Active)
        {
            DrawFlatBatteryOverlay();
            HandleBezelInput();
            return;
        }

        if (_router.Current is Screen.Home || ShowsHomeIndicator(_router.Current))
        {
            _osStatusBar.Draw(ImGui.GetWindowPos(), ImGui.GetWindowSize(), _signal.IsConnected);
        }

        // The home button outlives every app-side lock (see IAetherApp.LocksShell): an onboarding that
        // hid it left anyone who opened the app by accident with no way out but making an account.
        if (_router.Current is Screen.Home or Screen.App || ShowsHomeIndicator(_router.Current))
        {
            DrawHomeIndicator();
        }

        if (_router.Current is Screen.Home && !Plugin.Configuration.Os.TourSeen
            && !Os.OsBootIntro.Active && !Os.OsTransitions.Active)
        {
            _osTour.Start();
        }

        DrawOsOverlays();

        HandleBezelInput();
    }

    /// <summary>Fades the incoming screen in from the splash's navy after the startup splash advances. The
    /// splash faded its own content out first, so the cover starts opaque and matches with no seam.</summary>
    private void DrawStartupFadeIn(Vector2 winPos, Vector2 winSize)
    {
        if (!_startupFadeActive)
        {
            return;
        }
        _startupFadeT += ImGui.GetIO().DeltaTime / StartupFadeDur;
        if (_startupFadeT >= 1f)
        {
            _startupFadeActive = false;
            return;
        }
        var tl = winPos + Px(BezelLeft, BezelTop);
        var br = new Vector2(winPos.X + winSize.X - Px(BezelRight), winPos.Y + winSize.Y - Px(BezelBottom));
        var col = ImGui.ColorConvertFloat4ToU32(StartupFadeColor with { W = 1f - Math.Clamp(_startupFadeT, 0f, 1f) });
        ImGui.GetWindowDrawList().AddRectFilled(tl, br, col);
    }

    public override void PostDraw()
    {
        FontDiagnostics.Sample("MainWindow.PostDraw/before-restore");
        FontScalePin.Restore(_savedFontGlobalScale);
        FontDiagnostics.Sample("MainWindow.PostDraw/after-restore");
        ImGui.PopStyleVar();
        EmojiFavoriteFx.Draw();
    }

    private void DrawFontRebuildLoader()
    {
        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        _phoneShell.DrawBackground(pos, size);
        LoadingSpinner.Draw(pos + size * 0.5f, Px(16f), Px(3.5f), ThemeService.Current.AccentU32);
    }

    private void OnScreenChanged(Screen newScreen)
    {
        switch (newScreen)
        {
            case Screen.Splash:
                _splashScreen.OnShow();
                break;
            case Screen.OsOnboarding:
                _osOnboardingScreen.OnShow();
                break;
            case Screen.Banned:
                _bannedScreen.OnShow();
                break;
            case Screen.WarningsAcknowledge:
                _warningsAckScreen.OnShow();
                break;
            case Screen.ModeratorMessages:
                _moderatorMessageScreen.OnShow();
                break;
            case Screen.StaffNotice:
                _staffNoticeScreen.OnShow();
                break;
            case Screen.PassphraseUnlock:
                _passphraseUnlockScreen.OnShow();
                break;
            case Screen.EncryptionRecovery:
                _encryptionRecoveryScreen.OnShow();
                break;
            case Screen.Offline:
                _offlineScreen.OnShow();
                break;
            case Screen.SessionExpired:
                _sessionExpiredScreen.OnShow();
                break;
            case Screen.Outdated:
                _outdatedScreen.OnShow();
                break;
        }
    }

    /// <summary>The OS home indicator + status bar show over the surface app, unless it declares a locked flow
    /// (its own first-run onboarding / key verification).</summary>
    private bool ShowsHomeIndicator(Screen s)
    {
        if (s is Screen.Banned)
        {
            return true;
        }
        if (s is not Screen.App || _osShell.ActiveSurfaceApp is not { } app)
        {
            return false;
        }
        // The account-ban gate replaces the app's surface, so the home indicator must stay even if the app's own
        // flow would normally lock the shell (e.g. AetherLove sitting on a locked onboarding/verify view).
        if (app.UsesAccount && _bootstrap.LastAccount is { AccountDisabled: true })
        {
            return true;
        }
        return !app.LocksShell;
    }

    /// <summary>Whether the current mouse press began on the home pill with no popup open, which is what
    /// entitles the geometric fallback below to treat the matching release as a click.</summary>
    private bool _homePillPressArmed;

    private void DrawHomeIndicator()
    {
        var winPos = ImGui.GetWindowPos();
        var winSize = ImGui.GetWindowSize();
        var theme = ThemeService.Current;
        var button = theme.HomeButton;

        // Anchor to the frame's horizontal centre (not the content centre), so an asymmetric bezel doesn't drag
        // the button off the frame's bottom cradle.
        var centerX = winPos.X + winSize.X * 0.5f + Px(button.CenterXOffset);
        var centerY = winPos.Y + winSize.Y - Px(BezelBottom) * 0.5f + Px(button.CenterYOffset);
        var center = new Vector2(centerX, centerY);

        // Double the clickable width (X only) for an easier target; the drawn pill (button.Draw below) is
        // unchanged, its size lives entirely in the renderer.
        var hitSize = Px(button.HitSize.X * 2f, button.HitSize.Y);
        ImGui.SetCursorScreenPos(center - hitSize * 0.5f);
        var released = ImGui.InvisibleButton("##osHome", hitSize);
        // A theme may place the button INSIDE the content rect, and every in-phone overlay is a child
        // window covering that rect, which is a window above this one and takes the click whatever order
        // things were submitted in. So the press is also read geometrically: the home button is the one
        // control that must work while something is open over the screen, since leaving is its whole job.
        //
        // Geometric means blind, so the fallback is anchored to its own PRESS, and only a press made with
        // no popup open arms it. A popup handles its clicks itself, closes on the release, and by the time
        // this runs the active id is already cleared, so a release-only test cannot tell "clicked the pill"
        // from "clicked a context-menu row that happened to sit over the pill": that was a right-click menu
        // low on the market list closing the whole app on "Copy item name".
        var hitTL = center - (hitSize * 0.5f);
        var hitBR = center + (hitSize * 0.5f);
        var overButton = ImGui.IsMouseHoveringRect(hitTL, hitBR, false);
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            _homePillPressArmed = overButton
                && !ImGui.IsPopupOpen(string.Empty, ImGuiPopupFlags.AnyPopupId | ImGuiPopupFlags.AnyPopupLevel);
        }
        if (!released && _homePillPressArmed && overButton
            && ImGui.IsMouseReleased(ImGuiMouseButton.Left) && !ImGui.IsAnyItemActive())
        {
            released = true;
        }
        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            _homePillPressArmed = false;
        }
        // Under exclusive key capture (Doom mid-run) the hidden field reclaims the active id on the frame
        // after any press, so the release this button normally fires on can never land; fire on the press
        // instead for exactly those frames, leaving the capture itself untouched.
        if (released || (Os.AppCapabilities.ExclusiveInputActive && ImGui.IsItemActivated()))
        {
            GoHomeAnimated();
        }

        var state = new HomeButtonState(ImGui.IsItemHovered() || overButton, ImGui.IsItemActive());
        if (state.Hovered)
        {
            SharedUiHelpers.HandOnHover();
            if (button.TooltipKey != null)
            {
                ImGui.SetTooltip(Loc.T(button.TooltipKey));
            }
        }
        button.Draw(ImGui.GetWindowDrawList(), center, state, theme, (float)ImGui.GetTime());
    }

    private void GoHomeAnimated()
    {
        if (_osShade.Visible)
        {
            _osShade.Close();
            return;
        }
        if (_router.Current == Screen.Home)
        {
            _homeScreen.HandleHomePress();
            return;
        }
        var appId = _osShell.AppIdForScreen(_router.Current);
        var app = appId != null ? _osShell.Find(appId) : null;
        if (app != null && _homeScreen.TryGetTileRect(app.Id, out var tl, out var br))
        {
            Os.OsTransitions.PlayClose(app, tl, br, () => _router.Navigate(Screen.Home));
        }
        else
        {
            _router.Navigate(Screen.Home);
        }
    }

    private void DrawSurfaceApp()
    {
        var app = _osShell.ActiveSurfaceApp;
        if (app == null)
        {
            _router.Navigate(Screen.Home);
            return;
        }

        // An account-wide ban blocks every server-backed app in place; the home grid and local apps stay usable.
        // The reason comes from the exempt account-info fetch, so it renders even while the account is banned.
        if (app.UsesAccount && _bootstrap.LastAccount is { AccountDisabled: true } bannedAccount)
        {
            DrawAccountBannedCard(bannedAccount.AccountDisabledReason);
            return;
        }

        // Connection-needing apps show the offline panel in place of their surface; the OS itself (home,
        // offline-capable apps) never blocks on connectivity. Debounced so a reconnect blip doesn't flash it;
        // a session that never connected gates immediately.
        var offlineGate = app.RequiresConnection && !_signal.IsConnected
            && (_signal.DebouncedOffline || _bootstrap.LastConnection is null);
        if (offlineGate)
        {
            if (!_offlineGateWasActive)
            {
                _offlineGateWasActive = true;
                _offlineScreen.OnShow();
            }
            _offlineScreen.Draw();
            return;
        }

        var t = ThemeService.Current;
        var ctx = new AetherOS.Sdk.OsAppContext
        {
            Scale = UiScale.S,
            ContentSize = ImGui.GetContentRegionAvail(),
            Theme = new AetherOS.Sdk.OsTheme(t.Accent, t.AccentLight, t.AccentDark, t.ChipFill,
                t.SecondaryStart, t.SecondaryEnd, t.ButtonNormal, t.ButtonHovered, t.ButtonActive),
            Localize = Loc.T,
            Culture = LanguageProvider.CurrentCulture,
            Shell = _osShell,
            ReduceMotion = AccessibilityService.ReduceMotion,
            TitleFont = UiFonts.H1,
            HeadingFont = UiFonts.H3,
            Capabilities = _capabilities,
        };

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Px(12f, 10f));
        ImGui.BeginChild("##appSurface", ImGui.GetContentRegionAvail(), false,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        ImGui.PopStyleVar();
        try
        {
            app.Draw(ctx);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, $"[AetherOS] App '{app.Id}' draw failed.");
        }
        ImGui.EndChild();

        _capabilities.DrawFrame();
    }

    /// <summary>In-app gate shown in place of a server-backed app's surface while the whole account is banned.
    /// The reason is read from the account snapshot (populated by the exempt account-info fetch and the
    /// AccountDisabled push), so it renders even while banned.</summary>
    private void DrawAccountBannedCard(string? reason)
    {
        // Same banned illustration + layout as the per-profile ban screen, with account-level copy.
        _bannedScreen.DrawGate(Loc.T("common.account_disabled_title"), Loc.T("common.account_disabled_body"), reason);
    }

    private void DrawOsOverlays()
    {
        var offering = _newAppOffer.Active && _router.Current is Screen.Home && !_osTour.Active
            && !Os.OsBootIntro.Active && !Os.OsTransitions.Active;
        // The translation offer waits for everything, the new-app offer included: two full-screen asks
        // stacked on one boot would be noise, and the app offer is the older, rarer one.
        var translationOffering = !offering && _translationOffer.Active && _router.Current is Screen.Home
            && !_osTour.Active && !Os.OsBootIntro.Active && !Os.OsTransitions.Active;
        if (!Os.OsTransitions.Active && !_osShade.Visible && !Os.OsBootIntro.Active && !_osShareSheet.Visible
            && !_osTour.Active && !offering && !translationOffering && !_partyIntro.Active)
        {
            return;
        }

        var winPos = ImGui.GetWindowPos();
        var winSize = ImGui.GetWindowSize();
        var contentTL = winPos + Px(BezelLeft, BezelTop);
        var contentBR = winPos + new Vector2(winSize.X - Px(BezelRight), winSize.Y - Px(BezelBottom));

        ImGui.SetCursorScreenPos(winPos);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        var open = ImGui.BeginChild("##osOverlay", winSize, false,
            ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoScrollbar
            | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoNav);
        ImGui.PopStyleVar();
        if (open)
        {
            var dl = ImGui.GetWindowDrawList();
            dl.PushClipRect(contentTL, contentBR, true);
            Os.OsBootIntro.Draw(dl, contentTL, contentBR);
            Os.OsTransitions.Draw(dl, contentTL, contentBR);
            // The tour opens the shade for its demo; look-only mode skips its widgets entirely so they can
            // neither react nor steal hover from the tour's overlapping buttons.
            _osShade.InputLocked = _osTour.Active;
            _osShade.Draw(contentTL, contentBR);
            _osShareSheet.Draw(contentTL, contentBR);
            dl.PopClipRect();
            // Unclipped on purpose: the tour highlights bezel elements (home button, status strip).
            _osTour.Draw(winPos, winSize);
            if (offering)
            {
                _newAppOffer.Draw(contentTL, contentBR);
            }
            else if (translationOffering)
            {
                _translationOffer.Draw(contentTL, contentBR);
            }
            else if (_partyIntro.Active)
            {
                _partyIntro.Draw(contentTL, contentBR);
            }
            else
            {
                _homeScreen.DrawPartyOverlays(contentTL, contentBR);
            }
        }
        ImGui.EndChild();
    }

    /// <summary>The dead-phone takeover, in its own child submitted after the screen content. A child renders
    /// above its parent's draw list and owns the hover, which the parent list cannot: drawn on the parent it
    /// would sit UNDER the app and the grass could never be clicked.</summary>
    private void DrawFlatBatteryOverlay()
    {
        var winPos = ImGui.GetWindowPos();
        var winSize = ImGui.GetWindowSize();
        var contentTL = winPos + Px(BezelLeft, BezelTop);
        var contentBR = winPos + new Vector2(winSize.X - Px(BezelRight), winSize.Y - Px(BezelBottom));

        ImGui.SetCursorScreenPos(winPos);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        var open = ImGui.BeginChild("##osFlatBattery", winSize, false,
            ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoScrollbar
            | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoNav);
        ImGui.PopStyleVar();
        if (open)
        {
            Os.FlatBatteryOverlay.Draw(contentTL, contentBR);
        }
        ImGui.EndChild();
    }

    /// <summary>The window minimize + close buttons, at each theme's per-theme rects. A theme with
    /// <see cref="ThemeDefinition.DrawWindowControls"/> has us draw a rounded accent key + glyph (like the home
    /// button); otherwise the rects are invisible hit areas over buttons the frame art already carries.</summary>
    private void DrawBezelButtons()
    {
        var winPos = ImGui.GetWindowPos();
        var theme = ThemeService.Current;

        DrawWindowControl(winPos, theme, theme.MinimizeButtonTL, theme.MinimizeButtonSize,
            "##bezelMinimize", FontAwesomeIcon.Minus, Loc.T("common.minimize_tooltip"), Minimize);
        DrawWindowControl(winPos, theme, theme.CloseButtonTL, theme.CloseButtonSize,
            "##bezelClose", FontAwesomeIcon.Times, Loc.T("common.close_plugin_tooltip"), RequestClose);
    }

    private void DrawWindowControl(Vector2 winPos, ThemeDefinition theme, Vector2 tlDesign, Vector2 sizeDesign,
        string id, FontAwesomeIcon icon, string tooltip, Action onClick)
    {
        var tl = winPos + Px(tlDesign.X, tlDesign.Y);
        var size = Px(sizeDesign.X, sizeDesign.Y);
        ImGui.SetCursorScreenPos(tl);
        ImGui.InvisibleButton(id, size);
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            SharedUiHelpers.HandOnHover();
            ImGui.SetTooltip(tooltip);
        }
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            onClick();
        }

        if (!theme.DrawWindowControls)
        {
            return;
        }

        var dl = ImGui.GetWindowDrawList();
        // The per-theme control colour, else the frame's neon (the home button's glow), and its breathing pulse.
        var neon = theme.WindowControlColor
            ?? (theme.HomeButton is NeonSquareHomeButton nsb ? nsb.GlowColor : theme.AccentLight);
        var center = tl + size * 0.5f;
        // Visual key is 15% smaller than the hit rect, centred, so the click target stays generous.
        var visSize = size * 0.85f;
        var vtl = center - visSize * 0.5f;
        var vbr = center + visSize * 0.5f;
        var round = Px(2f);
        const ImDrawFlags flags = ImDrawFlags.RoundCornersAll;

        var pulse = AccessibilityService.ReduceMotion
            ? 0.5f
            : 0.5f + 0.5f * MathF.Sin((float)ImGui.GetTime() * (MathF.PI * 2f / 2.6f));
        var h = hovered ? 1f : 0f;

        // Opaque dark key so the frame never shows through and the glyph reads, then the neon tint + glow on top.
        dl.AddRectFilled(vtl, vbr, ImGui.GetColorU32(new Vector4(0.02f, 0.05f, 0.10f, 1f)), round, flags);
        dl.AddRectFilled(vtl, vbr, ImGui.GetColorU32(neon with { W = 0.05f + 0.05f * pulse + 0.10f * h }), round, flags);

        var glowA = 0.10f + 0.06f * pulse + 0.18f * h;
        const int passes = 4;
        for (var i = 1; i <= passes; i++)
        {
            var exp = Px(1.7f * i);
            var a = glowA * (1f - (i - 1) / (float)passes);
            dl.AddRect(vtl - new Vector2(exp, exp), vbr + new Vector2(exp, exp),
                ImGui.GetColorU32(neon with { W = a }), round + exp, flags, Px(2.2f));
        }

        dl.AddRect(vtl, vbr, ImGui.GetColorU32(neon with { W = 0.85f + 0.15f * pulse }), round, flags, Px(2f));

        var glyph = hovered ? new Vector4(1f, 1f, 1f, 1f) : neon with { W = 1f };
        IconDraw.AddCentered(dl, icon, visSize.Y * 0.5f, center, ImGui.GetColorU32(glyph));
    }

    /// <summary>Closes AetherLove, first asking for confirmation unless the user has opted out.</summary>
    public void RequestClose()
    {
        if (Plugin.Configuration.SkipCloseConfirmation)
        {
            PerformClose();
            return;
        }
        ModalHost.Instance?.Open(380f, DrawCloseConfirmBody);
    }

    private void DrawCloseConfirmBody(float availW)
    {
        var t = ThemeService.Current;

        using (UiFonts.H3?.Push())
        {
            var title = Loc.T("common.close_plugin_title");
            var titleSz = ImGui.CalcTextSize(title);
            ImGui.SetCursorPosX((availW - titleSz.X) * 0.5f);
            ImGui.TextColored(t.AccentLight, title);
        }

        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.Separator,
            new Vector4(t.AccentLight.X, t.AccentLight.Y, t.AccentLight.Z, 0.35f));
        ImGui.Separator();
        ImGui.PopStyleColor();
        ImGui.Spacing();

        ImGui.TextColored(new Vector4(0.88f, 0.88f, 0.88f, 1f),
            Loc.T("common.close_plugin_body", AetherLoveBootstrap.CommandName));
        ImGui.Spacing();
        ImGui.TextColored(new Vector4(0.65f, 0.65f, 0.65f, 1f),
            Loc.T("common.close_plugin_tip"));
        ImGui.Spacing();
        ImGui.Spacing();

        var dontAsk = Plugin.Configuration.SkipCloseConfirmation;
        if (ImGui.Checkbox(Loc.T("common.close_plugin_dont_ask"), ref dontAsk))
        {
            Plugin.Configuration.SkipCloseConfirmation = dontAsk;
            Plugin.Configuration.Save();
        }
        ImGui.Spacing();
        ImGui.Spacing();

        var btnH = Px(32f);
        var btnW = (availW - Px(8f)) * 0.5f;

        ImGui.PushStyleColor(ImGuiCol.Button, t.ButtonNormal);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, t.ButtonHovered);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, t.ButtonActive);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
        if (ImGui.Button(Loc.T("common.cancel"), new Vector2(btnW, btnH)))
        {
            ModalHost.Instance?.Close();
        }
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);

        ImGui.SameLine(0f, Px(8f));

        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.55f, 0.18f, 0.20f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.72f, 0.24f, 0.26f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.45f, 0.14f, 0.16f, 1f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
        if (ImGui.Button(Loc.T("common.close"), new Vector2(btnW, btnH)))
        {
            ModalHost.Instance?.Close();
            PerformClose();
        }
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);
    }

    public void Minimize()
    {
        IsOpen = false;
        if (_miniWindow != null)
        {
            _miniWindow.IsOpen = true;
        }
    }

    /// <summary>The shell's own input: double-click to minimise, right-click for the shell menu. Both
    /// ignore the content area and any bezel widget so neither steals input meant for the screen.</summary>
    private void HandleBezelInput()
    {
        if (OnBezel())
        {
            if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                Minimize();
                return;
            }
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            {
                OsMenu.Open(BezelMenuId);
            }
        }
        DrawBezelMenu();
    }

    private bool OnBezel()
    {
        if (!ImGui.IsWindowHovered() || ImGui.IsAnyItemHovered())
        {
            return false;
        }

        var winPos = ImGui.GetWindowPos();
        var winSize = ImGui.GetWindowSize();
        var mouse = ImGui.GetMousePos();

        var contentTL = winPos + Px(BezelLeft, BezelTop);
        var contentBR = winPos + new Vector2(winSize.X - Px(BezelRight), winSize.Y - Px(BezelBottom));
        var inContent = mouse.X >= contentTL.X && mouse.X <= contentBR.X
                     && mouse.Y >= contentTL.Y && mouse.Y <= contentBR.Y;
        return !inContent;
    }

    /// <summary>What the shell can do without going through the screen: the same three answers the bezel
    /// already holds (the close cross, the double-click, the setting), where the hand already is.</summary>
    private void DrawBezelMenu()
    {
        var locked = Plugin.Configuration.LockPhonePosition;
        var rows = new OsMenu.MenuRow[]
        {
            new(FontAwesomeIcon.PowerOff, Loc.T("os.phone_menu_exit")),
            new(FontAwesomeIcon.WindowMinimize, Loc.T("os.phone_menu_minimize")),
            new(locked ? FontAwesomeIcon.LockOpen : FontAwesomeIcon.Lock,
                Loc.T(locked ? "os.phone_menu_unlock" : "os.phone_menu_lock")),
        };

        // The first two close the window the popup belongs to, so they land after the menu is done drawing.
        switch (OsMenu.Draw(BezelMenuId, rows))
        {
            case 0:
                RequestClose();
                break;
            case 1:
                Minimize();
                break;
            case 2:
                Plugin.Configuration.LockPhonePosition = !locked;
                Plugin.Configuration.Save();
                break;
        }
    }

    /// <summary>Powering the phone off, which means off: the hub connection goes with it, so no pushes, no
    /// notifications, no native chat lines and no DTR entries until it is switched on again. Minimising is
    /// the way to keep all of that while getting the window out of the way.</summary>
    private void PerformClose()
    {
        if (_miniWindow is not null)
        {
            _miniWindow.IsOpen = false;
        }
        IsOpen = false;
        _poweredOff = true;
        _hasContext = false;
        // Off means off: the services that tick on their own timers read this rather than the windows, so
        // nothing keeps printing ads or polling behind a phone the player has switched off.
        PhonePower.Set(false);
        _ = _signal.DisconnectAsync();
    }
}
