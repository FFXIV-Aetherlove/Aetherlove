using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Config;
using AetherLove.Emoji;
using AetherLove.Navigation;
using AetherLove.Screens;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Services.Hub;
using AetherLove.UI;
using AetherLove.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;

namespace AetherLove.Windows;

/// <summary>Main plugin window. Renders the active screen with a cross-fade.</summary>
public class MainPluginWindow : Window, IDisposable
{
    private readonly ScreenRouter _router;
    private readonly SplashScreen _splashScreen;
    private readonly OnboardingScreen _onboardingScreen;
    private readonly DeckScreen _deckScreen;
    private readonly MatchScreen _matchScreen;
    private readonly ChatListScreen _chatListScreen;
    private readonly ChatCategoryScreen _chatCategoryScreen;
    private readonly ChatScreen _chatScreen;
    private readonly ProfileScreen _profileScreen;
    private readonly SettingsScreen _settingsScreen;
    private readonly MyProfileScreen _myProfileScreen;
    private readonly BannedScreen _bannedScreen;
    private readonly WarningAcknowledgeScreen _warningsAckScreen;
    private readonly ModeratorMessageScreen _moderatorMessageScreen;
    private readonly PassphraseUnlockScreen _passphraseUnlockScreen;
    private readonly EncryptionRecoveryScreen _encryptionRecoveryScreen;
    private readonly EncryptionVerificationScreen _encryptionVerificationScreen;
    private readonly NewsScreen _newsScreen;
    private readonly OfflineScreen _offlineScreen;
    private readonly OutdatedScreen _outdatedScreen;
    private readonly PlacesScreen _placesScreen;
    private readonly MyVenuesScreen _myVenuesScreen;
    private readonly NotificationCenter _notifications;
    private readonly AetherLoveHubClient _hubClient;
    private readonly OwnAvatarCache _ownAvatar;
    private readonly Services.Auth.SessionBootstrapper _bootstrap;

    private MiniWindow? _miniWindow;

    private readonly PhoneShellWidget _phoneShell = new();

    private Screen? _lastScreen;
    private float _transitionAlpha = 1f;
    private const float TransitionSpeed = 12f;

    public MainPluginWindow(
        ScreenRouter router,
        SplashScreen splashScreen,
        OnboardingScreen onboardingScreen,
        DeckScreen deckScreen,
        MatchScreen matchScreen,
        ChatListScreen chatListScreen,
        ChatCategoryScreen chatCategoryScreen,
        ChatScreen chatScreen,
        ProfileScreen profileScreen,
        SettingsScreen settingsScreen,
        MyProfileScreen myProfileScreen,
        BannedScreen bannedScreen,
        WarningAcknowledgeScreen warningsAckScreen,
        ModeratorMessageScreen moderatorMessageScreen,
        PassphraseUnlockScreen passphraseUnlockScreen,
        EncryptionRecoveryScreen encryptionRecoveryScreen,
        EncryptionVerificationScreen encryptionVerificationScreen,
        NewsScreen newsScreen,
        OfflineScreen offlineScreen,
        OutdatedScreen outdatedScreen,
        PlacesScreen placesScreen,
        MyVenuesScreen myVenuesScreen,
        HangoutsScreen hangoutsScreen,
        BlockedScreen blockedScreen,
        Widgets.SupporterThanksScene supporterThanks,
        NotificationCenter notifications,
        AetherLoveHubClient hubClient,
        OwnAvatarCache ownAvatar,
        Services.Auth.SessionBootstrapper bootstrap
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
        _splashScreen = splashScreen;
        _onboardingScreen = onboardingScreen;
        _deckScreen = deckScreen;
        _matchScreen = matchScreen;
        _chatListScreen = chatListScreen;
        _chatCategoryScreen = chatCategoryScreen;
        _chatScreen = chatScreen;
        _profileScreen = profileScreen;
        _settingsScreen = settingsScreen;
        _myProfileScreen = myProfileScreen;
        _bannedScreen = bannedScreen;
        _warningsAckScreen = warningsAckScreen;
        _moderatorMessageScreen = moderatorMessageScreen;
        _passphraseUnlockScreen = passphraseUnlockScreen;
        _encryptionRecoveryScreen = encryptionRecoveryScreen;
        _encryptionVerificationScreen = encryptionVerificationScreen;
        _newsScreen = newsScreen;
        _offlineScreen = offlineScreen;
        _outdatedScreen = outdatedScreen;
        _placesScreen = placesScreen;
        _myVenuesScreen = myVenuesScreen;
        _hangoutsScreen = hangoutsScreen;
        _blockedScreen = blockedScreen;
        _supporterThanks = supporterThanks;
        _notifications = notifications;
        _hubClient = hubClient;
        _ownAvatar = ownAvatar;
        _bootstrap = bootstrap;
    }
    private readonly HangoutsScreen _hangoutsScreen;
    private readonly BlockedScreen _blockedScreen;
    private readonly Widgets.SupporterThanksScene _supporterThanks;

    public void SetMiniWindow(MiniWindow mini) => _miniWindow = mini;

    private bool _recenterRequested;

    /// <summary>Queues a one-shot recenter on the next frame (ImGui isn't valid from the command thread).</summary>
    public void RequestRecenter() => _recenterRequested = true;

    public override void OnOpen()
    {
        _ownAvatar.Refresh(onlyIfCold: true);

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
        else if (_notifications.HasPendingNews)
        {
            _notifications.ClearPendingNews();
            _newsScreen.RequestLiveUnseenFlow();
            _router.Navigate(Screen.News);
        }
    }

    public void OpenToChat()
    {
        if (_miniWindow != null)
        {
            _miniWindow.IsOpen = false;
        }
        IsOpen = true;
        _router.Navigate(Screen.ChatList);
    }

    public void OpenToDeck()
    {
        if (_miniWindow != null)
        {
            _miniWindow.IsOpen = false;
        }
        IsOpen = true;
        _router.Navigate(Screen.Deck);
    }

    public void OpenToNews()
    {
        if (_miniWindow != null)
        {
            _miniWindow.IsOpen = false;
        }
        IsOpen = true;
        _newsScreen.RequestListView();
        _router.Navigate(Screen.News);
    }

    public void OpenToHangouts()
    {
        if (_miniWindow != null)
        {
            _miniWindow.IsOpen = false;
        }
        IsOpen = true;
        _router.Navigate(Screen.Hangouts);
    }

    public void OpenToMyHangout()
    {
        if (_miniWindow != null)
        {
            _miniWindow.IsOpen = false;
        }
        IsOpen = true;
        _myProfileScreen.RequestHangoutView();
        _router.Navigate(Screen.MyProfile);
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

        if (_router.NavigationOccurred)
        {
            _router.NavigationOccurred = false;

            var newScreen = _router.Current;
            if (_lastScreen != newScreen)
            {
                if (_lastScreen.HasValue)
                {
                    OnScreenHidden(_lastScreen.Value);
                    // Leaving the deck's browse context drops the pinned card so a later return shows fresh profiles.
                    if (IsDeckEngaged(_lastScreen.Value) && !IsDeckEngaged(newScreen))
                    {
                        _deckScreen.MarkDeckLeft();
                    }
                }
                _lastScreen = newScreen;
                OnScreenChanged(newScreen);
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

    private const float NavBarHeight = 70f;

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

        ModalHost.Instance?.SetAnchor(ImGui.GetWindowPos(), ImGui.GetWindowSize());

        _phoneShell.DrawBackground(ImGui.GetWindowPos(), ImGui.GetWindowSize());

        DrawBezelButtons();

        if (_router.Current != Screen.Deck)
        {
            _deckScreen.MaybeBackgroundRefresh();
        }

        var winSize = ImGui.GetWindowSize();
        var contentW = winSize.X - Px(BezelLeft) - Px(BezelRight);
        var contentH = winSize.Y - Px(BezelTop) - Px(BezelBottom);
        var isMainScreen = _router.Current is Screen.Deck or Screen.ChatList or Screen.ChatCategory or Screen.Chat
                                                           or Screen.Settings or Screen.MyProfile
                                                           or Screen.Places or Screen.MyVenues or Screen.Hangouts
                                                           or Screen.Blocked;
        var isSplash = _router.Current is Screen.Splash;

        ImGui.SetCursorPos(Px(BezelLeft, BezelTop));
        var bezelH = isMainScreen ? contentH - Px(NavBarHeight) : contentH;
        ImGui.BeginChild("##bezel", new Vector2(contentW, bezelH), false,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        ImGui.PushTextWrapPos(contentW);

        if (isMainScreen)
        {
            ImGui.BeginChild("##content", new Vector2(0, contentH - Px(NavBarHeight)), false,
                ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        }

        switch (_router.Current)
        {
            case Screen.Splash:
                _splashScreen.Draw();
                break;
            case Screen.Onboarding:
                _onboardingScreen.Draw();
                break;
            case Screen.Deck:
                _deckScreen.Draw();
                break;
            case Screen.Match:
                _matchScreen.Draw();
                break;
            case Screen.ChatList:
                _chatListScreen.Draw();
                break;
            case Screen.ChatCategory:
                _chatCategoryScreen.Draw();
                break;
            case Screen.Chat:
                _chatScreen.Draw();
                break;
            case Screen.Profile:
                _profileScreen.Draw();
                break;
            case Screen.Settings:
                _settingsScreen.Draw();
                break;
            case Screen.MyProfile:
                _myProfileScreen.Draw();
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
            case Screen.EncryptionVerification:
                _encryptionVerificationScreen.Draw();
                break;
            case Screen.News:
                _newsScreen.Draw();
                break;
            case Screen.Offline:
                _offlineScreen.Draw();
                break;
            case Screen.Outdated:
                _outdatedScreen.Draw();
                break;
            case Screen.Places:
                _placesScreen.Draw();
                break;
            case Screen.MyVenues:
                _myVenuesScreen.Draw();
                break;
            case Screen.Hangouts:
                _hangoutsScreen.Draw();
                break;
            case Screen.Blocked:
                _blockedScreen.Draw();
                break;
        }

        if (isMainScreen)
        {
            ImGui.EndChild();
        }

        ImGui.PopTextWrapPos();
        ImGui.EndChild();

        if (isMainScreen)
        {
            DrawBottomNav();
        }

        _supporterThanks.Draw(ImGui.GetWindowPos(), ImGui.GetWindowSize());

        HandleBezelDoubleClickMinimize();
    }

    private static bool IsNavActive(Screen navTarget, Screen current) => navTarget switch
    {
        Screen.ChatList => current is Screen.ChatList or Screen.ChatCategory or Screen.Chat or Screen.Blocked,
        _ => current == navTarget,
    };

    private void DrawBottomNav()
    {
        var winPos = ImGui.GetWindowPos();
        var winSize = ImGui.GetWindowSize();
        var barTop = winPos.Y + winSize.Y - Px(BezelBottom) - Px(NavBarHeight) + Px(16f);
        var barLeft = winPos.X + Px(BezelLeft);

        var drawList = ImGui.GetWindowDrawList();

        // Profile draws the avatar, so its label is unused.
        var placesEnabled = _bootstrap.LastConnection?.PlacesEnabled != false;
        var hangoutsEnabled = _bootstrap.LastConnection?.HangoutsEnabled != false;
        var itemList = new System.Collections.Generic.List<(FontAwesomeIcon icon, string label, Screen target)>(6)
        {
            (FontAwesomeIcon.User, string.Empty, Screen.MyProfile),
            (FontAwesomeIcon.LayerGroup, Loc.T("common.nav_swipe"), Screen.Deck),
            (FontAwesomeIcon.Comment, Loc.T("common.nav_matches"), Screen.ChatList),
        };
        if (placesEnabled)
        {
            itemList.Add((FontAwesomeIcon.MapMarkedAlt, Loc.T("common.nav_places"), Screen.Places));
        }
        if (hangoutsEnabled)
        {
            itemList.Add((FontAwesomeIcon.Bullhorn, Loc.T("common.nav_hangouts"), Screen.Hangouts));
        }
        itemList.Add((FontAwesomeIcon.Cog, Loc.T("common.nav_settings"), Screen.Settings));
        var items = itemList.ToArray();

        var slotWidth = (winSize.X - Px(BezelLeft) - Px(BezelRight)) / items.Length;
        var iconFont = Plugin.PluginInterface.UiBuilder.FontIcon;
        var fontSize = ImGui.GetFontSize();
        var accentCol = ThemeService.Current.AccentU32;
        var gradA = ThemeService.Current.SecondaryStart;
        var gradB = ThemeService.Current.SecondaryEnd;
        var gradPhase = (float)ImGui.GetTime() * 1.6f;

        for (int i = 0; i < items.Length; i++)
        {
            var (icon, label, target) = items[i];
            var isActive = IsNavActive(target, _router.Current);
            var color = isActive ? accentCol : UiColors.TextMuted;

            var slotCenterX = barLeft + slotWidth * i + slotWidth * 0.5f;
            var iconY = barTop + Px(10f);
            var labelY = iconY + fontSize + Px(4f);

            if (target == Screen.MyProfile)
            {
                DrawProfileNavButton(drawList, slotCenterX, iconY, isActive, icon, color);
            }
            else
            {
                ImGui.PushFont(iconFont);
                var iconStr = icon.ToIconString();
                var iconSize = ImGui.CalcTextSize(iconStr);
                var iconVtx = drawList.VtxBuffer.Size;
                drawList.AddText(ImGui.GetFont(), fontSize,
                    new Vector2(slotCenterX - iconSize.X * 0.5f, iconY),
                    color, iconStr);
                if (isActive && !AccessibilityService.ReduceMotion)
                {
                    GradientSweepVertices(drawList, iconVtx, gradA, gradB, gradPhase);
                }
                ImGui.PopFont();

                if (target == Screen.ChatList)
                {
                    var total = _notifications.TotalBadge;
                    if (total > 0)
                    {
                        var badgeR = Px(7f);
                        var badgeCenter = new Vector2(slotCenterX + iconSize.X * 0.5f + Px(1f), iconY - Px(1f));
                        drawList.AddCircleFilled(badgeCenter, badgeR, UiColors.UnreadBadge);
                        var badgeLabel = total > 9 ? "9+" : total.ToString();
                        var badgeFsz = fontSize * 0.68f;
                        var badgeTsz = ImGui.CalcTextSize(badgeLabel) * (badgeFsz / fontSize);
                        drawList.AddText(ImGui.GetFont(), badgeFsz,
                            badgeCenter - badgeTsz * 0.5f, 0xFFFFFFFF, badgeLabel);
                    }
                }

                var labelSize = ImGui.CalcTextSize(label);
                var labelVtx = drawList.VtxBuffer.Size;
                drawList.AddText(new Vector2(slotCenterX - labelSize.X * 0.5f, labelY), color, label);

                if (isActive)
                {
                    if (!AccessibilityService.ReduceMotion)
                    {
                        GradientSweepVertices(drawList, labelVtx, gradA, gradB, gradPhase);
                    }
                    var dotY = labelY + fontSize + Px(3f);
                    drawList.AddCircleFilled(new Vector2(slotCenterX, dotY), Px(2.5f), accentCol);
                }
            }

            ImGui.SetCursorScreenPos(new Vector2(barLeft + slotWidth * i, barTop));
            if (ImGui.InvisibleButton($"##nav_{i}", new Vector2(slotWidth, Px(NavBarHeight))))
            {
                if (target != _router.Current)
                {
                    _router.Navigate(target);
                }
            }
        }
    }

    private void DrawProfileNavButton(ImDrawListPtr drawList, float centerX, float iconY,
                                      bool isActive, FontAwesomeIcon fallbackIcon, uint iconColor)
    {
        var fontSize = ImGui.GetFontSize();
        var baseRadius = fontSize + Px(2f);
        // Grows up from a fixed bottom edge.
        const float avatarScale = 1.44f;
        var radius = baseRadius * avatarScale;
        var center = new Vector2(centerX, iconY + baseRadius * 2f - radius);

        var wrap = _ownAvatar.Texture?.GetWrapOrDefault();
        if (wrap != null)
        {
            var tl = center - new Vector2(radius, radius);
            var br = center + new Vector2(radius, radius);
            drawList.AddImageRounded(wrap.Handle, tl, br,
                Vector2.Zero, Vector2.One, 0xFFFFFFFF, radius, ImDrawFlags.RoundCornersAll);
        }
        else
        {
            ImGui.PushFont(Plugin.PluginInterface.UiBuilder.FontIcon);
            var iconStr = fallbackIcon.ToIconString();
            var iconSize = ImGui.CalcTextSize(iconStr);
            drawList.AddText(ImGui.GetFont(), fontSize, center - iconSize * 0.5f, iconColor, iconStr);
            ImGui.PopFont();
        }

        drawList.AddCircle(center, radius, UiColors.AvatarRing, 64, Px(1f));

        if (isActive)
        {
            var th = ThemeService.Current;
            if (AccessibilityService.ReduceMotion)
            {
                drawList.AddCircle(center, radius, th.AccentU32, 64, Px(2f));
            }
            else
            {
                DrawGradientRing(drawList, center, radius, Px(2.5f), th.SecondaryStart, th.SecondaryEnd);
            }
        }

        if (_bootstrap.LastConnection is { IsSupporter: true })
        {
            var badgeCenter = center + new Vector2(radius * 0.74f, -radius * 0.74f);
            var badgeR = Px(9f);
            drawList.AddCircleFilled(badgeCenter, badgeR, 0xFF1E1E24u, 24);
            drawList.AddCircle(badgeCenter, badgeR, UiColors.FavoriteStar, 24, Px(1.2f));
            // The star glyph's empty descent makes box-centring sit visually high; nudge down to compensate.
            IconDraw.AddCentered(drawList, FontAwesomeIcon.Star, badgeR * 1.2f,
                badgeCenter + new Vector2(0f, Px(0.5f)), UiColors.FavoriteStar);
        }
    }

    private static void DrawGradientRing(ImDrawListPtr dl, Vector2 center, float radius, float thickness,
        Vector4 colorA, Vector4 colorB)
    {
        const int segments = 96;
        var phase = (float)ImGui.GetTime() * 1.6f;
        var prev = center + new Vector2(radius, 0f);
        for (int i = 1; i <= segments; i++)
        {
            var ang = i / (float)segments * MathF.Tau;
            var pt = center + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * radius;
            var blend = 0.5f + 0.5f * MathF.Sin((i - 0.5f) / segments * MathF.Tau - phase);
            var col = ImGui.ColorConvertFloat4ToU32(Vector4.Lerp(colorA, colorB, blend));
            dl.AddLine(prev, pt, col, thickness);
            prev = pt;
        }
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
            case Screen.Onboarding:
                _onboardingScreen.OnShow();
                break;
            case Screen.Deck:
                _deckScreen.OnShow();
                break;
            case Screen.Match:
                _matchScreen.OnShow();
                break;
            case Screen.ChatList:
                _chatListScreen.OnShow();
                MarkChatListSeen();
                break;
            case Screen.ChatCategory:
                _chatCategoryScreen.OnShow();
                break;
            case Screen.Chat:
                _chatScreen.OnShow();
                break;
            case Screen.Profile:
                _profileScreen.OnShow();
                break;
            case Screen.Settings:
                _settingsScreen.OnShow();
                break;
            case Screen.MyProfile:
                _myProfileScreen.OnShow();
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
            case Screen.EncryptionVerification:
                _encryptionVerificationScreen.OnShow();
                break;
            case Screen.News:
                _newsScreen.OnShow();
                break;
            case Screen.Offline:
                _offlineScreen.OnShow();
                break;
            case Screen.Outdated:
                _outdatedScreen.OnShow();
                break;
            case Screen.Places:
                _placesScreen.OnShow();
                break;
            case Screen.MyVenues:
                _myVenuesScreen.OnShow();
                break;
            case Screen.Hangouts:
                _hangoutsScreen.OnShow();
                break;
            case Screen.Blocked:
                _blockedScreen.OnShow();
                break;
        }
    }

    private void OnScreenHidden(Screen oldScreen)
    {
        switch (oldScreen)
        {
            case Screen.ChatList:
                _chatListScreen.OnHide();
                break;
            case Screen.ChatCategory:
                _chatCategoryScreen.OnHide();
                break;
            case Screen.Chat:
                _chatScreen.OnHide();
                break;
            case Screen.MyProfile:
                _myProfileScreen.OnHide();
                break;
        }
    }

    /// <summary>Invisible hit areas over the bezel art's minimize and exit buttons (per-theme rects).</summary>
    private void DrawBezelButtons()
    {
        var winPos = ImGui.GetWindowPos();
        var theme = ThemeService.Current;

        var minTL = winPos + Px(theme.MinimizeButtonTL.X, theme.MinimizeButtonTL.Y);
        var minSize = Px(theme.MinimizeButtonSize.X, theme.MinimizeButtonSize.Y);
        ImGui.SetCursorScreenPos(minTL);
        ImGui.InvisibleButton("##bezelMinimize", minSize);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("common.minimize_tooltip"));
        }
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            Minimize();
        }

        var closeTL = winPos + Px(theme.CloseButtonTL.X, theme.CloseButtonTL.Y);
        var closeSize = Px(theme.CloseButtonSize.X, theme.CloseButtonSize.Y);
        ImGui.SetCursorScreenPos(closeTL);
        ImGui.InvisibleButton("##bezelClose", closeSize);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("common.close_plugin_tooltip"));
        }
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            RequestClose();
        }
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
        if (IsDeckEngaged(_router.Current))
        {
            _deckScreen.MarkDeckLeft();
        }
        IsOpen = false;
        if (_miniWindow != null)
        {
            _miniWindow.IsOpen = true;
        }
    }

    /// <summary>True while the screen is part of the deck's browse flow: the deck itself, or a profile view
    /// opened from it.</summary>
    private bool IsDeckEngaged(Screen screen) =>
        screen == Screen.Deck || (screen == Screen.Profile && _profileScreen.Source == ProfileSource.Deck);

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
        if (IsDeckEngaged(_router.Current))
        {
            _deckScreen.MarkDeckLeft();
        }
        if (_miniWindow is not null)
        {
            _miniWindow.IsOpen = false;
        }
        IsOpen = false;
    }

    private void MarkChatListSeen()
    {
        _notifications.NewMatches = 0;
        _ = Task.Run(async () =>
        {
            try
            {
                await _hubClient.MarkMatchListSeenAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "[MainPluginWindow] MarkMatchListSeenAsync failed.");
            }
        });
    }
}
