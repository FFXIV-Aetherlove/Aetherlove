using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Config;
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
    private readonly ChatArchiveScreen _chatArchiveScreen;
    private readonly ChatScreen _chatScreen;
    private readonly ProfileScreen _profileScreen;
    private readonly SettingsScreen _settingsScreen;
    private readonly MyProfileScreen _myProfileScreen;
    private readonly BannedScreen _bannedScreen;
    private readonly WarningAcknowledgeScreen _warningsAckScreen;
    private readonly PassphraseUnlockScreen _passphraseUnlockScreen;
    private readonly NewsScreen _newsScreen;
    private readonly OfflineScreen _offlineScreen;
    private readonly OutdatedScreen _outdatedScreen;
    private readonly NotificationCenter _notifications;
    private readonly AetherLoveHubClient _hubClient;
    private readonly OwnAvatarCache _ownAvatar;

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
        ChatArchiveScreen chatArchiveScreen,
        ChatScreen chatScreen,
        ProfileScreen profileScreen,
        SettingsScreen settingsScreen,
        MyProfileScreen myProfileScreen,
        BannedScreen bannedScreen,
        WarningAcknowledgeScreen warningsAckScreen,
        PassphraseUnlockScreen passphraseUnlockScreen,
        NewsScreen newsScreen,
        OfflineScreen offlineScreen,
        OutdatedScreen outdatedScreen,
        NotificationCenter notifications,
        AetherLoveHubClient hubClient,
        OwnAvatarCache ownAvatar
    ) : base("AetherLove##MainWindow",
             ImGuiWindowFlags.NoResize
           | ImGuiWindowFlags.NoScrollbar
           | ImGuiWindowFlags.NoScrollWithMouse
           | ImGuiWindowFlags.NoTitleBar
           | ImGuiWindowFlags.NoDocking)
    {
        Size = UiScale.Design;
        SizeCondition = ImGuiCond.Always;

        _router = router;
        _splashScreen = splashScreen;
        _onboardingScreen = onboardingScreen;
        _deckScreen = deckScreen;
        _matchScreen = matchScreen;
        _chatListScreen = chatListScreen;
        _chatArchiveScreen = chatArchiveScreen;
        _chatScreen = chatScreen;
        _profileScreen = profileScreen;
        _settingsScreen = settingsScreen;
        _myProfileScreen = myProfileScreen;
        _bannedScreen = bannedScreen;
        _warningsAckScreen = warningsAckScreen;
        _passphraseUnlockScreen = passphraseUnlockScreen;
        _newsScreen = newsScreen;
        _offlineScreen = offlineScreen;
        _outdatedScreen = outdatedScreen;
        _notifications = notifications;
        _hubClient = hubClient;
        _ownAvatar = ownAvatar;
    }

    public void SetMiniWindow(MiniWindow mini) => _miniWindow = mini;

    private bool _recenterRequested;

    /// <summary>Queues a one-shot recenter on the next frame (ImGui isn't valid from the command thread).</summary>
    public void RequestRecenter() => _recenterRequested = true;

    public override void OnOpen()
    {
        _ownAvatar.Refresh(onlyIfCold: true);

        // A warning that arrived while minimised was deferred; surface it now that the phone is open. Runs
        // before this frame's navigation is processed, so it wins over the open path's target (e.g. chat).
        if (_notifications.HasPendingWarning)
        {
            _notifications.ClearPendingWarning();
            _warningsAckScreen.RequestLiveAcknowledge();
            _router.Navigate(Screen.WarningsAcknowledge);
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
        // The phone and the bubble are mutually exclusive; opening one closes the other.
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

    public void Dispose()
    {
        _splashScreen.Dispose();
        _phoneShell.Dispose();
    }

    /// <summary>Visibility rule: always hidden while logged out. In combat, Hide = auto-hide via
    /// DrawConditions; Minimize / LeaveOpen = always visible (bootstrap handles the explicit swap).</summary>
    public override bool DrawConditions()
        => Plugin.ClientState.IsLoggedIn
           && (Plugin.Configuration.CombatBehavior != CombatBehavior.Hide
               || !Plugin.Condition[ConditionFlag.InCombat]);

    private float _savedFontGlobalScale;

    public override void PreDraw()
    {
        // The phone is authored at a fixed canvas × our own size preset. Dalamud's global font scale would
        // multiply every glyph (and font-derived widget heights) on top of that and overflow the fixed
        // window — so pin it to 1 for our draw and restore it in PostDraw. Dalamud's scale slider then has
        // zero effect inside the phone.
        Size = Px(UiScale.Design);

        var io = ImGui.GetIO();
        _savedFontGlobalScale = io.FontGlobalScale;
        io.FontGlobalScale = 1f;

        if (_router.NavigationOccurred)
        {
            _router.NavigationOccurred = false;

            var newScreen = _router.Current;
            if (_lastScreen != newScreen)
            {
                if (_lastScreen.HasValue)
                {
                    OnScreenHidden(_lastScreen.Value);
                }
                _lastScreen = newScreen;
                OnScreenChanged(newScreen);
            }

            _transitionAlpha = 0.88f;
        }

        var dt = (float)ImGui.GetIO().DeltaTime;
        _transitionAlpha = Math.Clamp(_transitionAlpha + dt * TransitionSpeed, 0f, 1f);

        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, _transitionAlpha);
    }

    private const float NavBarHeight = 70f;

    private const float BezelLeft = 44f;
    private const float BezelRight = 44f;
    private const float BezelTop = 50f;
    private const float BezelBottom = 60f;

    public override void Draw()
    {
        using var bodyFont = UiFonts.Body?.Push();

        if (_recenterRequested)
        {
            _recenterRequested = false;
            var vp = ImGui.GetMainViewport();
            ImGui.SetWindowPos(vp.Pos + (vp.Size - ImGui.GetWindowSize()) * 0.5f);
        }

        // Pin the modal host over the phone so its dim backdrop follows us onto whatever viewport/monitor
        // the window is on (Dalamud's "Enable multiple screens").
        ModalHost.Instance?.SetAnchor(ImGui.GetWindowPos(), ImGui.GetWindowSize());

        _phoneShell.DrawBackground(ImGui.GetWindowPos(), ImGui.GetWindowSize());

        DrawHiddenCloseAffordance();

        var winSize = ImGui.GetWindowSize();
        var contentW = winSize.X - Px(BezelLeft) - Px(BezelRight);
        var contentH = winSize.Y - Px(BezelTop) - Px(BezelBottom);
        var isMainScreen = _router.Current is Screen.Deck or Screen.ChatList or Screen.ChatArchive or Screen.Chat
                                                           or Screen.Settings or Screen.MyProfile;
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
            case Screen.ChatArchive:
                _chatArchiveScreen.Draw();
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
            case Screen.PassphraseUnlock:
                _passphraseUnlockScreen.Draw();
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
    }

    private static bool IsNavActive(Screen navTarget, Screen current) => navTarget switch
    {
        Screen.ChatList => current is Screen.ChatList or Screen.ChatArchive or Screen.Chat,
        _ => current == navTarget,
    };

    private void DrawBottomNav()
    {
        var winPos = ImGui.GetWindowPos();
        var winSize = ImGui.GetWindowSize();
        var barTop = winPos.Y + winSize.Y - Px(BezelBottom) - Px(NavBarHeight) + Px(16f);
        var barLeft = winPos.X + Px(BezelLeft);

        var drawList = ImGui.GetWindowDrawList();

        // Null target = Minimize. Profile draws the avatar (DrawProfileNavButton), so its label is unused.
        var items = new (FontAwesomeIcon icon, string label, Screen? target)[]
        {
            (FontAwesomeIcon.LayerGroup, Loc.T("common.nav_swipe"), Screen.Deck),
            (FontAwesomeIcon.Comment, Loc.T("common.nav_matches"), Screen.ChatList),
            (FontAwesomeIcon.User, string.Empty, Screen.MyProfile),
            (FontAwesomeIcon.Cog, Loc.T("common.nav_settings"), Screen.Settings),
            (FontAwesomeIcon.MobileAlt, Loc.T("common.nav_minimize"), null),
        };

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
            var isActive = target.HasValue && IsNavActive(target.Value, _router.Current);
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
                    GradientText(drawList, iconVtx, gradA, gradB, gradPhase);
                }
                ImGui.PopFont();

                if (target.HasValue && target.Value == Screen.ChatList)
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
                        GradientText(drawList, labelVtx, gradA, gradB, gradPhase);
                    }
                    var dotY = labelY + fontSize + Px(3f);
                    drawList.AddCircleFilled(new Vector2(slotCenterX, dotY), Px(2.5f), accentCol);
                }
            }

            ImGui.SetCursorScreenPos(new Vector2(barLeft + slotWidth * i, barTop));
            if (ImGui.InvisibleButton($"##nav_{i}", new Vector2(slotWidth, Px(NavBarHeight))))
            {
                if (target.HasValue)
                {
                    if (target.Value != _router.Current)
                    {
                        _router.Navigate(target.Value);
                    }
                }
                else
                {
                    IsOpen = false;
                    if (_miniWindow != null)
                    {
                        _miniWindow.IsOpen = true;
                    }
                }
            }
        }
    }

    /// <summary>Draws the Profile nav slot as the user's circular avatar, falling back to the user icon while it loads.</summary>
    private void DrawProfileNavButton(ImDrawListPtr drawList, float centerX, float iconY,
                                      bool isActive, FontAwesomeIcon fallbackIcon, uint iconColor)
    {
        var fontSize = ImGui.GetFontSize();
        var baseRadius = fontSize + Px(2f);
        // Enlarged centre avatar that grows up and out from a fixed bottom edge (a FAB-style button).
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

        // Permanent soft-grey ring so the avatar reads against the dark background.
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
    }

    /// <summary>Strokes a circular ring whose colour sweeps between the two colours around the
    /// circumference and slowly rotates over time — the selected bottom-nav avatar's accent border.</summary>
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

    /// <summary>Recolours the text vertices added since <paramref name="vtxStart"/> with a horizontal
    /// accent gradient anchored to screen-x and scrolled by <paramref name="phase"/> — the animated
    /// sheen on the selected nav button's icon and label.</summary>
    private static void GradientText(ImDrawListPtr dl, int vtxStart, Vector4 a, Vector4 b, float phase)
    {
        var k = MathF.Tau / Px(70f);
        for (int v = vtxStart; v < dl.VtxBuffer.Size; v++)
        {
            var vert = dl.VtxBuffer[v];
            var blend = 0.5f + 0.5f * MathF.Sin(vert.Pos.X * k - phase);
            vert.Col = ImGui.ColorConvertFloat4ToU32(Vector4.Lerp(a, b, blend));
            dl.VtxBuffer[v] = vert;
        }
    }

    public override void PostDraw()
    {
        ImGui.GetIO().FontGlobalScale = _savedFontGlobalScale;
        ImGui.PopStyleVar();
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
            case Screen.ChatArchive:
                _chatArchiveScreen.OnShow();
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
            case Screen.PassphraseUnlock:
                _passphraseUnlockScreen.OnShow();
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
        }
    }

    private void OnScreenHidden(Screen oldScreen)
    {
        switch (oldScreen)
        {
            case Screen.ChatList:
                _chatListScreen.OnHide();
                break;
            case Screen.ChatArchive:
                _chatArchiveScreen.OnHide();
                break;
            case Screen.Chat:
                _chatScreen.OnHide();
                break;
            case Screen.MyProfile:
                _myProfileScreen.OnHide();
                break;
        }
    }

    private void DrawHiddenCloseAffordance()
    {
        var winPos = ImGui.GetWindowPos();
        var winSize = ImGui.GetWindowSize();
        var hitH = Px(54f);
        var hitW = winSize.X / 6f * 0.8f;
        var hitTL = winPos + new Vector2((winSize.X - hitW) * 0.5f, winSize.Y - hitH);

        ImGui.SetCursorScreenPos(hitTL);
        ImGui.InvisibleButton("##fullClose", new Vector2(hitW, hitH));
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("common.close_plugin_tooltip"));
        }
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            RequestClose();
        }
    }

    /// <summary>Closes AetherLove, first asking for confirmation unless the user has opted out. Shared by
    /// the full window's close affordance and the minimised bubble's power button.</summary>
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

    private void PerformClose()
    {
        // Closing only hides the UI; the hub connection lives for the plugin's lifetime, so
        // notifications keep arriving while it's enabled.
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
