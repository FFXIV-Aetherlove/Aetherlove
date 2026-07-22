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
    private readonly PassphraseUnlockScreen _passphraseUnlockScreen;
    private readonly EncryptionRecoveryScreen _encryptionRecoveryScreen;
    private readonly OfflineScreen _offlineScreen;
    private readonly OutdatedScreen _outdatedScreen;
    private readonly NotificationCenter _notifications;
    private readonly OwnAvatarCache _ownAvatar;
    private readonly OsAvatarCache _osAvatar;
    private readonly Services.Auth.SessionBootstrapper _bootstrap;
    private readonly Services.Signal.AetherSignalService _signal;

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
        PassphraseUnlockScreen passphraseUnlockScreen,
        EncryptionRecoveryScreen encryptionRecoveryScreen,
        OfflineScreen offlineScreen,
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
        Os.OsTour osTour
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
        _passphraseUnlockScreen = passphraseUnlockScreen;
        _encryptionRecoveryScreen = encryptionRecoveryScreen;
        _offlineScreen = offlineScreen;
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
    }
    private readonly Os.OsShell _osShell;
    private readonly Os.NotificationShade _osShade;
    private readonly Os.StatusBar _osStatusBar;
    private readonly Os.AppCapabilities _capabilities;
    private readonly Os.ShareSheet _osShareSheet;
    private readonly Os.OsTour _osTour;

    public void SetMiniWindow(MiniWindow mini) => _miniWindow = mini;

    private bool _recenterRequested;
    private bool _offlineGateWasActive;
    private bool _phoneFocused;

    /// <summary>Whether the phone window (or one of its child regions) currently holds ImGui focus. False when
    /// the user clicks out to the game world or another window; used to pause the tomestone emote.</summary>
    public bool IsPhoneFocused => _phoneFocused;

    /// <summary>Queues a one-shot recenter on the next frame (ImGui isn't valid from the command thread).</summary>
    public void RequestRecenter() => _recenterRequested = true;

    public override void OnOpen()
    {
        _ownAvatar.Refresh(onlyIfCold: true);
        _osAvatar.Refresh(onlyIfCold: true);

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
    }

    /// <summary>Fires on every close path (minimize, close button, ESC), so the foreground app always gets its
    /// OnBackground and the next open fires a fresh OnForeground.</summary>
    public override void OnClose()
    {
        _foregroundApp?.OnBackground();
        _foregroundApp = null;
        _phoneFocused = false;
    }

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
        _osShell.GoHome();
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

    public void OpenToMessenger()
    {
        if (_miniWindow != null)
        {
            _miniWindow.IsOpen = false;
        }
        IsOpen = true;
        _osShell.OpenApp("messenger");
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

        if (Plugin.Configuration.LockPhonePosition)
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
            }

            _transitionAlpha = 0.88f;
        }

        var dt = (float)ImGui.GetIO().DeltaTime;
        _transitionAlpha = Math.Clamp(_transitionAlpha + dt * TransitionSpeed, 0f, 1f);

        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, _transitionAlpha);

        // Dalamud's global font scale would multiply every glyph on top of the phone's own scaling and
        // overflow the fixed window; pin it to 1 for our draw and restore in PostDraw. Pinned last so no
        // fallible PreDraw code runs between pin and restore.
        var io = ImGui.GetIO();
        _savedFontGlobalScale = io.FontGlobalScale;
        io.FontGlobalScale = 1f;
    }

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
            case Screen.PassphraseUnlock:
                _passphraseUnlockScreen.Draw();
                break;
            case Screen.EncryptionRecovery:
                _encryptionRecoveryScreen.Draw();
                break;
            case Screen.Offline:
                _offlineScreen.Draw();
                break;
            case Screen.Outdated:
                _outdatedScreen.Draw();
                break;
        }

        ImGui.PopTextWrapPos();
        ImGui.EndChild();

        DrawStartupFadeIn(ImGui.GetWindowPos(), ImGui.GetWindowSize());

        if (_router.Current is Screen.Home || ShowsHomeIndicator(_router.Current))
        {
            _osStatusBar.Draw(ImGui.GetWindowPos(), ImGui.GetWindowSize(), _signal.IsConnected);
        }

        if (_router.Current is Screen.Home || ShowsHomeIndicator(_router.Current))
        {
            DrawHomeIndicator();
        }

        if (_router.Current is Screen.Home && !Plugin.Configuration.Os.TourSeen
            && !Os.OsBootIntro.Active && !Os.OsTransitions.Active)
        {
            _osTour.Start();
        }

        DrawOsOverlays();

        HandleBezelDoubleClickMinimize();
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
        ImGui.GetIO().FontGlobalScale = _savedFontGlobalScale;
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
            case Screen.PassphraseUnlock:
                _passphraseUnlockScreen.OnShow();
                break;
            case Screen.EncryptionRecovery:
                _encryptionRecoveryScreen.OnShow();
                break;
            case Screen.Offline:
                _offlineScreen.OnShow();
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
        if (app.RequiresConnection && _bootstrap.LastAccount is { AccountDisabled: true })
        {
            return true;
        }
        return !app.LocksShell;
    }

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
        if (ImGui.InvisibleButton("##osHome", hitSize))
        {
            GoHomeAnimated();
        }

        var state = new HomeButtonState(ImGui.IsItemHovered(), ImGui.IsItemActive());
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
        if (app.RequiresConnection && _bootstrap.LastAccount is { AccountDisabled: true } bannedAccount)
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
        if (!Os.OsTransitions.Active && !_osShade.Visible && !Os.OsBootIntro.Active && !_osShareSheet.Visible
            && !_osTour.Active)
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

    /// <summary>Ignores double-clicks over the content area or any bezel widget so it never steals input.</summary>
    private void HandleBezelDoubleClickMinimize()
    {
        if (!ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            return;
        }
        if (!ImGui.IsWindowHovered() || ImGui.IsAnyItemHovered())
        {
            return;
        }

        var winPos = ImGui.GetWindowPos();
        var winSize = ImGui.GetWindowSize();
        var mouse = ImGui.GetMousePos();

        var contentTL = winPos + Px(BezelLeft, BezelTop);
        var contentBR = winPos + new Vector2(winSize.X - Px(BezelRight), winSize.Y - Px(BezelBottom));
        var inContent = mouse.X >= contentTL.X && mouse.X <= contentBR.X
                     && mouse.Y >= contentTL.Y && mouse.Y <= contentBR.Y;
        if (inContent)
        {
            return;
        }

        Minimize();
    }

    private void PerformClose()
    {
        // Closing keeps the hub connected (it lives for the plugin's lifetime), so notifications keep arriving.
        if (_miniWindow is not null)
        {
            _miniWindow.IsOpen = false;
        }
        IsOpen = false;
    }
}
