using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Config;
using AetherLove.Services;
using AetherLove.Services.Auth;
using AetherLove.Services.Hub;
using AetherLove.Services.Localization;
using AetherLove.Services.Signal;
using AetherLove.Shared.Profile.Enums;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherLove.Screens;

/// <summary>AetherLove's own (app-specific) settings: its notifications, content preferences, chat colours,
/// and the account delete flow. Phone-wide settings live in the AetherOS Settings app.</summary>
public class SettingsScreen
{
    private readonly LoveRouter _router;
    private readonly AetherHubContext _hubClient;
    private readonly AetherSignalService _signal;
    private readonly TokenService _tokens;
    private readonly Services.Chat.ChatCacheStore _chatCache;
    private readonly LoveShell _shell;
    private readonly SessionBootstrapper _bootstrap;
    private readonly IServerBar _serverBar;

    private enum View { Hub, Notifications, Nsfw, ChatColors, ConfirmDelete, Deleting }

    /// <summary>Wired by the app: navigates to the profile picker as an in-app switch (with a back pill).</summary>
    public Action? OpenProfilePicker { get; set; }

    // volatile: written from the deletion task, read on the UI thread.
    private volatile int _viewRaw = (int)View.Hub;
    private View _view
    {
        get => (View)_viewRaw;
        set => _viewRaw = (int)value;
    }

    private volatile string? _deleteError;

    private volatile bool _nsfwLoaded;
    private volatile bool _nsfwOn;
    private volatile bool _nsfwLocked;
    private volatile bool _nsfwBusy;
    private volatile string? _nsfwError;
    private bool _nsfwShowLockedHint;

    private readonly EntranceAnimation _entrance = new();
    private View _lastDrawnView = View.Hub;

    /// <summary>Non-null while hosted as a page inside the Settings app; the hub then shows a back pill wired to it.
    /// Null when the AetherLove app hosts the surface itself (its bottom nav is the way out), so no hub back pill.</summary>
    private Action? _hostBack;
    /// <summary>Last ImGui frame this surface drew on; a gap means it was just (re)entered, so reset to the hub.</summary>
    private int _lastDrawFrame = -8;

    private const float PadX = 16f;

    // Danger-zone label / delete-button outline colour.
    private static readonly Vector4 DangerLabelColor = new(0.93f, 0.36f, 0.36f, 1f);

    public SettingsScreen(LoveRouter router,
                          AetherHubContext hubClient,
                          AetherSignalService signal,
                          TokenService tokens,
                          Services.Chat.ChatCacheStore chatCache,
                          LoveShell shell,
                          SessionBootstrapper bootstrap,
        IAppCapabilities caps)
    {
        _router = router;
        _hubClient = hubClient;
        _signal = signal;
        _tokens = tokens;
        _chatCache = chatCache;
        _shell = shell;
        _bootstrap = bootstrap;
        _serverBar = caps.ServerBar("aetherlove");
    }

    /// <summary>Deep-links a "become a supporter" pitch to the supporter page in the AetherOS Settings app.
    /// The intent carries this app as the return app, so the supporter page's back pill comes straight back to
    /// AetherLove (whose warm resume restores the exact view the pitch was opened from).</summary>
    public void RequestSupporterView()
    {
        _shell.Shell?.SendIntent("settings",
            AetherOS.Sdk.OsIntents.CreateReturn(AetherOS.Sdk.OsIntents.OpenSupporter, "aetherlove"));
    }

    public void OnShow()
    {
        EnterFresh();
        _lastDrawFrame = ImGui.GetFrameCount();
    }

    /// <summary>Reset to the hub and reload live state. Runs on router entry (<see cref="OnShow"/>) and on re-entry
    /// detected via a frame gap in <see cref="Draw"/>, so the surface always opens at the hub in either host.</summary>
    private void EnterFresh()
    {
        _view = View.Hub;
        _entrance.Arm();
        _deleteError = null;
        LoadNsfwState();
    }

    private void LoadNsfwState()
    {
        _nsfwLoaded = false;
        _nsfwError = null;
        _nsfwShowLockedHint = false;
        _ = Task.Run(async () =>
        {
            try
            {
                var dto = await _hubClient.GetMyProfileDetailAsync().ConfigureAwait(false);
                _nsfwOn = dto.IsNsfw;
                _nsfwLocked = dto.LookingForMask.HasFlag(LookingFor.Erp)
                    || dto.Photos.Any(p => p.IsNsfw);
                _nsfwLoaded = true;
            }
            catch (Exception ex)
            {
                _nsfwError = HubErrorText.Localize(ex);
            }
        });
    }

    private void SetNsfw(bool enabled)
    {
        _nsfwBusy = true;
        _nsfwError = null;
        var prev = _nsfwOn;
        _nsfwOn = enabled;
        _ = Task.Run(async () =>
        {
            try
            {
                await _hubClient.SetProfileNsfwAsync(enabled).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _nsfwOn = prev;
                _nsfwError = HubErrorText.Localize(ex);
            }
            finally
            {
                _nsfwBusy = false;
            }
        });
    }

    /// <summary>Draws the settings surface. <paramref name="onBack"/> is non-null only when hosted as a page inside
    /// the OS Settings app (the hub then shows a back pill wired to it); null when the AetherLove app hosts it.</summary>
    public void Draw(Action? onBack = null)
    {
        _hostBack = onBack;
        var frame = ImGui.GetFrameCount();
        if (frame - _lastDrawFrame > 1)
        {
            EnterFresh();
        }
        _lastDrawFrame = frame;

        var winW = ImGui.GetWindowSize().X;

        if (_view == View.Hub && _lastDrawnView != View.Hub)
        {
            _entrance.Arm();
        }
        _lastDrawnView = _view;

        switch (_view)
        {
            case View.Hub:
                DrawHub(winW);
                break;
            case View.Notifications:
                DrawNotificationsPage(winW);
                break;
            case View.Nsfw:
                DrawNsfwPage(winW);
                break;
            case View.ChatColors:
                DrawChatColorsPage(winW);
                break;
            case View.ConfirmDelete:
                DrawConfirmDelete(winW);
                break;
            case View.Deleting:
                DrawDeleting(winW);
                break;
        }
    }

    private void DrawHub(float winW)
    {
        var t = ThemeService.Current;
        var scrollH = ImGui.GetContentRegionAvail().Y;
        PushScrollbarStyle();

        using (var scroll = Dalamud.Interface.Utility.Raii.ImRaii.Child("##settHub", new Vector2(0f, scrollH), false))
        {
            PopScrollbarStyle();

            if (!scroll.Success)
            {
                return;
            }

            _entrance.BeginFrame();

            // Hosted inside the Settings app: offer a pill back to its app list. In-app (bottom nav) it is null.
            if (_hostBack is { } hostBack)
            {
                ImGui.Spacing();
                ImGui.Spacing();
                ImGui.SetCursorPosX(Px(PadX));
                if (DrawFloatingBackPill(ImGui.GetCursorScreenPos(), Loc.T("settings.back_arrow"), FontAwesomeIcon.Cog))
                {
                    hostBack();
                }
            }

            ImGui.Spacing();
            ImGui.Spacing();

            DrawMenuCard("settMain", winW, PadX, new List<MenuRow>
            {
                new(FontAwesomeIcon.Bell, t.Accent, Loc.T("settings.section_notifications"), 0, false, () => _view = View.Notifications),
                new(FontAwesomeIcon.EyeSlash, t.Accent, Loc.T("settings.menu_nsfw"), 0, false, () => _view = View.Nsfw),
                new(FontAwesomeIcon.Comments, t.Accent, Loc.T("settings.menu_chat_colors"), 0, false, () => _view = View.ChatColors),
                new(FontAwesomeIcon.UserFriends, t.Accent, Loc.T("settings.menu_switch_profile"), 0, false, () => OpenProfilePicker?.Invoke()),
            });

            ImGui.Spacing();
            ImGui.Spacing();

            SectionLabel(Loc.T("settings.section_danger_zone"), DangerLabelColor);
            ImGui.Spacing();
            DrawDeleteButton(winW);

            ImGui.Spacing();
            ImGui.Spacing();
            _entrance.EndFrame();
        }
    }

    /// <summary>AetherLove's Notifications sub-page: the master notification gate, its per-event chat/popup toggles,
    /// and the pulse opt-out.</summary>
    private void DrawNotificationsPage(float winW)
    {
        DrawSubpageBack();
        DrawSubpageHeading(Loc.T("settings.section_notifications"), PadX);

        var scrollH = ImGui.GetContentRegionAvail().Y;
        PushScrollbarStyle();
        using (var scroll = Dalamud.Interface.Utility.Raii.ImRaii.Child("##settNotifications", new Vector2(0f, scrollH), false))
        {
            PopScrollbarStyle();
            if (!scroll.Success)
            {
                return;
            }

            ImGui.Spacing();
            DrawAetherLoveNotifications(winW);
            ImGui.Spacing();
        }
    }

    /// <summary>AetherLove's NSFW sub-page: the always-blur-NSFW preference and the "my profile is NSFW" toggle.</summary>
    private void DrawNsfwPage(float winW)
    {
        DrawSubpageBack();
        DrawSubpageHeading(Loc.T("settings.menu_nsfw"), PadX);

        var scrollH = ImGui.GetContentRegionAvail().Y;
        PushScrollbarStyle();
        using (var scroll = Dalamud.Interface.Utility.Raii.ImRaii.Child("##settNsfw", new Vector2(0f, scrollH), false))
        {
            PopScrollbarStyle();
            if (!scroll.Success)
            {
                return;
            }

            ImGui.Spacing();
            DrawNsfwBlurToggle(winW);
            ImGui.Spacing();
            DrawNsfwProfileToggle(winW);
            ImGui.Spacing();
        }
    }

    private void DrawAetherLoveNotifications(float winW)
    {
        var master = UiHost.Configuration.AetherLoveNotificationsEnabled;
        ToggleRow(winW, "##loveNotifMaster", Loc.T("settings.enable_aetherlove_notifications"),
            Loc.T("settings.enable_aetherlove_notifications_hint"), master,
            v => UiHost.Configuration.AetherLoveNotificationsEnabled = v);

        ImGui.BeginDisabled(!master);
        ToggleRow(winW, "##loveNotifChatMsg", Loc.T("settings.announce_messages_chat"),
            Loc.T("settings.announce_messages_chat_hint"), UiHost.Configuration.NotifyChatOnMessage,
            v => UiHost.Configuration.NotifyChatOnMessage = v);
        ToggleRow(winW, "##loveNotifChatMatch", Loc.T("settings.announce_matches_chat"),
            Loc.T("settings.announce_matches_chat_hint"), UiHost.Configuration.NotifyChatOnMatch,
            v => UiHost.Configuration.NotifyChatOnMatch = v);
        ToggleRow(winW, "##loveNotifPopupMsg", Loc.T("settings.popup_messages"),
            Loc.T("settings.popup_messages_hint"), UiHost.Configuration.NotifyPopupOnMessage,
            v => UiHost.Configuration.NotifyPopupOnMessage = v);
        ToggleRow(winW, "##loveNotifPopupMatch", Loc.T("settings.popup_matches"),
            Loc.T("settings.popup_matches_hint"), UiHost.Configuration.NotifyPopupOnMatch,
            v => UiHost.Configuration.NotifyPopupOnMatch = v);
        ImGui.EndDisabled();

        DrawPulseOptOut(winW);

        ImGui.Dummy(new Vector2(winW, Px(10f)));
        SectionLabel(Loc.T("settings.section_serverbar"), ThemeService.Current.Accent);
        ToggleRow(winW, "##loveDtrApp", Loc.T("settings.serverbar_love_app"),
            Loc.T("settings.serverbar_love_app_hint"), _serverBar.AppEnabled,
            v => _serverBar.AppEnabled = v);
        ImGui.BeginDisabled(!_serverBar.AppEnabled);
        ToggleRow(winW, "##loveDtrChats", Loc.T("settings.serverbar_chats"),
            Loc.T("settings.serverbar_chats_hint"), ChatsEntry.Enabled, v => ChatsEntry.Enabled = v);
        ToggleRow(winW, "##loveDtrMatches", Loc.T("settings.serverbar_matches"),
            Loc.T("settings.serverbar_matches_hint"), MatchesEntry.Enabled, v => MatchesEntry.Enabled = v);
        ImGui.EndDisabled();
    }

    /// <summary>The app's two bar entries, re-asked by id only: the empty title and label leave what
    /// the publishing service registered untouched, and this page just reaches the toggles.</summary>
    private IServerBarEntry ChatsEntry => _serverBar.Entry("chats", "", "");

    private IServerBarEntry MatchesEntry => _serverBar.Entry("matches", "", "");

    /// <summary>A switch row over its explanation, the Groove settings shape.</summary>
    private static void ToggleRow(float winW, string id, string label, string hint, bool value,
        Action<bool> apply)
    {
        ImGui.SetCursorPosX(Px(PadX));
        if (DrawToggleSwitch(id, label, value))
        {
            apply(!value);
            UiHost.Configuration.Save();
        }
        ImGui.SetCursorPosX(Px(PadX));
        ImGui.PushTextWrapPos(winW - Px(PadX));
        ImGui.TextColored(UiColors.Hint, hint);
        ImGui.PopTextWrapPos();
        ImGui.Dummy(new Vector2(0f, Px(8f)));
    }

    private void DrawChatColorsPage(float winW)
    {
        DrawSubpageBack();
        DrawSubpageHeading(Loc.T("settings.menu_chat_colors"), PadX);

        var scrollH = ImGui.GetContentRegionAvail().Y;
        PushScrollbarStyle();
        using (var scroll = Dalamud.Interface.Utility.Raii.ImRaii.Child("##settChatColors", new Vector2(0f, scrollH), false))
        {
            PopScrollbarStyle();
            if (!scroll.Success)
            {
                return;
            }

            var dl = ImGui.GetWindowDrawList();

            ImGui.Spacing();
            DrawChatPreview();
            ImGui.Spacing();
            Divider(dl, winW);

            DrawColorRow(Loc.T("settings.chat_own_bg"), "ownbg",
                () => UiHost.Configuration.OwnChatBg, v => UiHost.Configuration.OwnChatBg = v, ChatColors.OwnBgDefault);
            DrawColorRow(Loc.T("settings.chat_own_fg"), "ownfg",
                () => UiHost.Configuration.OwnChatFg, v => UiHost.Configuration.OwnChatFg = v, ChatColors.OwnFgDefault);
            DrawColorRow(Loc.T("settings.chat_peer_bg"), "peerbg",
                () => UiHost.Configuration.PeerChatBg, v => UiHost.Configuration.PeerChatBg = v, ChatColors.PeerBgDefault);
            DrawColorRow(Loc.T("settings.chat_peer_fg"), "peerfg",
                () => UiHost.Configuration.PeerChatFg, v => UiHost.Configuration.PeerChatFg = v, ChatColors.PeerFgDefault);

            ImGui.Spacing();
        }
    }

    private static void DrawColorRow(string label, string id, Func<Vector4?> get, Action<Vector4?> set, Vector4 themeDefault)
    {
        ImGui.SetCursorPosX(Px(PadX));
        ImGui.TextColored(UiColors.Body, label);

        ImGui.SetCursorPosX(Px(PadX));
        var current = get() ?? themeDefault;
        // Live-update the in-memory value each frame so the preview follows the picker; persist only on release.
        if (ImGui.ColorEdit4($"##{id}", ref current, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaPreviewHalf))
        {
            set(current);
        }
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            UiHost.Configuration.Save();
        }

        ImGui.SameLine(0f, Px(8f));
        PushThemeButton(ThemeService.Current);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(6f));
        if (SharedUiHelpers.Button($"{Loc.T("settings.chat_reset")}##reset{id}", new Vector2(0f, Px(24f))))
        {
            set(null);
            UiHost.Configuration.Save();
        }
        ImGui.PopStyleVar();
        PopThemeButton();

        ImGui.Spacing();
        ImGui.Spacing();
    }

    private static void DrawChatPreview()
    {
        var areaW = ImGui.GetContentRegionAvail().X;
        DrawPreviewBubble("Hello", true, areaW);
        DrawPreviewBubble("Hello to you", false, areaW);
        DrawPreviewBubble("How are you?", true, areaW);
    }

    private static void DrawPreviewBubble(string text, bool isOwn, float areaW)
    {
        var dl = ImGui.GetWindowDrawList();
        var bg = isOwn ? ChatColors.OwnBg : ChatColors.PeerBg;
        var fg = isOwn ? ChatColors.OwnFg : ChatColors.PeerFg;
        var padIn = Px(11f, 7f);
        var inset = Px(10f);

        var maxBubW = areaW * 0.72f;
        var textSz = ImGui.CalcTextSize(text);
        var bubbleW = MathF.Min(maxBubW, textSz.X + padIn.X * 2f);
        var bubbleH = textSz.Y + padIn.Y * 2f;

        var origin = ImGui.GetCursorScreenPos();
        var leftX = isOwn ? origin.X + areaW - bubbleW - inset : origin.X + inset;
        var tl = new Vector2(leftX, origin.Y);

        dl.AddRectFilled(tl, tl + new Vector2(bubbleW, bubbleH), ImGui.ColorConvertFloat4ToU32(bg), Px(10f));
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), tl + padIn, ImGui.ColorConvertFloat4ToU32(fg), text);

        ImGui.Dummy(new Vector2(areaW, bubbleH + Px(5f)));
    }

    private void DrawSubpageBack()
    {
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.SetCursorPosX(Px(PadX));
        if (DrawFloatingBackPill(ImGui.GetCursorScreenPos(), Loc.T("settings.back_arrow"), FontAwesomeIcon.Cog))
        {
            _view = View.Hub;
        }
        ImGui.Spacing();
    }

    private void DrawDeleteButton(float winW)
    {
        ImGui.SetCursorPosX(Px(PadX));
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.55f, 0.12f, 0.12f, 0.10f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.62f, 0.14f, 0.14f, 0.22f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.62f, 0.14f, 0.14f, 0.32f));
        ImGui.PushStyleColor(ImGuiCol.Border, DangerLabelColor);
        ImGui.PushStyleColor(ImGuiCol.Text, DangerLabelColor);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, Px(1.5f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
        if (SharedUiHelpers.Button(Loc.T("settings.delete_profile"), new Vector2(winW - Px(PadX) * 2f, Px(38f))))
        {
            _view = View.ConfirmDelete;
        }
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(5);
    }

    private void DrawConfirmDelete(float winW)
    {
        var scrollH = ImGui.GetContentRegionAvail().Y;
        PushScrollbarStyle(
            grab: new Vector4(0.65f, 0.15f, 0.15f, 0.85f),
            grabHovered: new Vector4(0.80f, 0.22f, 0.22f, 1.0f),
            grabActive: new Vector4(0.45f, 0.08f, 0.08f, 1.0f));

        using (var scroll = Dalamud.Interface.Utility.Raii.ImRaii.Child("##settConfirm", new Vector2(0f, scrollH), false))
        {
            PopScrollbarStyle();

            if (!scroll.Success)
            {
                return;
            }

            var dl = ImGui.GetWindowDrawList();

            ImGui.Spacing();
            ImGui.Spacing();

            ImGui.SetCursorPosX(Px(PadX));
            ImGui.TextColored(new Vector4(0.92f, 0.28f, 0.28f, 1f), Loc.T("settings.delete_profile"));
            ImGui.Spacing();

            {
                ImGui.SetCursorPosX(Px(PadX));
                var p = ImGui.GetCursorScreenPos();
                var endX = p.X + winW - Px(PadX) * 2f;
                dl.AddLine(p, new Vector2(endX, p.Y), UiColors.DangerDivider, 1f);
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() + Px(6f));
                ImGui.Spacing();
            }

            ImGui.SetCursorPosX(Px(PadX));
            ImGui.PushTextWrapPos(winW - Px(PadX));
            ImGui.TextColored(new Vector4(0.90f, 0.90f, 0.90f, 1f),
                Loc.T("settings.delete_profile_warning_intro"));
            ImGui.PopTextWrapPos();
            ImGui.Spacing();
            ImGui.Spacing();

            ImpactBullet(winW, Loc.T("settings.delete_bullet_profile"));
            ImpactBullet(winW, Loc.T("settings.delete_bullet_matches"));
            ImpactBullet(winW, Loc.T("settings.delete_bullet_preferences"));
            ImpactBullet(winW, Loc.T("settings.delete_bullet_pictures"));
            ImGui.Spacing();

            ImGui.SetCursorPosX(Px(PadX));
            ImGui.PushTextWrapPos(winW - Px(PadX));
            ImGui.TextColored(new Vector4(0.65f, 0.65f, 0.65f, 1f),
                Loc.T("settings.delete_profile_account_stays"));
            ImGui.PopTextWrapPos();
            ImGui.Spacing();
            ImGui.Spacing();

            var availBtnW = winW - Px(PadX) * 2f;
            var cancelW = MathF.Floor(availBtnW * 0.42f);
            var deleteW = availBtnW - cancelW - Px(8f);

            ImGui.SetCursorPosX(Px(PadX));
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.20f, 0.20f, 0.20f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.36f, 0.36f, 0.36f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.12f, 0.12f, 0.12f, 1f));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
            if (SharedUiHelpers.Button($"{Loc.T("settings.cancel")}##delCancel", new Vector2(cancelW, Px(36f))))
            {
                _view = View.Hub;
            }
            ImGui.PopStyleVar();
            ImGui.PopStyleColor(3);

            ImGui.SameLine(0f, Px(8f));
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.60f, 0.10f, 0.10f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.82f, 0.18f, 0.18f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.42f, 0.05f, 0.05f, 1f));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
            if (SharedUiHelpers.Button($"{Loc.T("settings.delete_profile")}##delConfirm", new Vector2(deleteW, Px(36f))))
            {
                _view = View.Deleting;
                StartDeletion();
            }
            ImGui.PopStyleVar();
            ImGui.PopStyleColor(3);

            if (_deleteError is not null)
            {
                ImGui.Spacing();
                ImGui.SetCursorPosX(Px(PadX));
                ImGui.PushTextWrapPos(winW - Px(PadX));
                ImGui.TextColored(UiColors.Danger,
                    Loc.T("settings.delete_previous_failed", _deleteError));
                ImGui.PopTextWrapPos();
            }

            ImGui.Spacing();
        }
    }

    private void DrawDeleting(float winW)
    {
        var scrollH = ImGui.GetContentRegionAvail().Y;
        using (var scroll = Dalamud.Interface.Utility.Raii.ImRaii.Child("##settDeleting", new Vector2(0f, scrollH), false))
        {
            if (!scroll.Success)
            {
                return;
            }

            ImGui.Spacing();
            ImGui.Spacing();

            ImGui.SetCursorPosX(Px(PadX));
            ImGui.TextColored(new Vector4(0.92f, 0.92f, 0.92f, 1f), Loc.T("settings.deleting_title"));
            ImGui.Spacing();

            ImGui.SetCursorPosX(Px(PadX));
            var dots = (int)(DateTime.Now.TimeOfDay.TotalSeconds * 3) % 4;
            ImGui.TextColored(new Vector4(0.65f, 0.65f, 0.65f, 1f),
                Loc.T("settings.deleting_body") + new string('.', dots));
        }
    }

    /// <summary>Deletes the AetherLove profile only: the AetherOS account session, tokens and the account KEK
    /// all survive. Local traces of the deleted profile (its chat cache folder, its keypair, its stashed
    /// config state) are dropped, the session re-bootstraps (the server now acts as the free sibling, or the
    /// tombstone when none is left) and the user lands on the profile picker to switch or create.</summary>
    private void StartDeletion()
    {
        _deleteError = null;
        var deletedId = UiHost.Configuration.Auth.ActiveProfileId;
        _ = Task.Run(async () =>
        {
            try
            {
                await _hubClient.DeleteProfileAsync(CancellationToken.None).ConfigureAwait(false);
                await _signal.DisconnectAsync().ConfigureAwait(false);
                _chatCache.Clear();
                UiHost.Configuration.Crypto = new CryptoKeys();
                if (deletedId is { } id)
                {
                    UiHost.Configuration.RemoveProfileLocalState(id);
                }
                UiHost.Configuration.Save();
                _bootstrap.Reset();
                await _bootstrap.RunAsync().ConfigureAwait(false);
                _view = View.Hub;
                _router.Navigate(LoveView.ProfilePicker);
            }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, "[Settings] DeleteProfileAsync failed.");
                _deleteError = HubErrorText.Localize(ex);
                _view = View.ConfirmDelete;
            }
        });
    }

    private static void DrawNsfwBlurToggle(float winW)
    {
        SettingCheckbox(PadX, Loc.T("settings.always_blur_nsfw"),
            () => UiHost.Configuration.AlwaysBlurNsfw,
            v => UiHost.Configuration.AlwaysBlurNsfw = v);
        ImGui.SameLine();
        HelpMarker(Loc.T("settings.always_blur_nsfw_tooltip"));
    }

    private void DrawNsfwProfileToggle(float winW)
    {
        ImGui.SetCursorPosX(Px(PadX));

        var blocked = !_nsfwLoaded || _nsfwBusy;
        if (blocked)
        {
            ImGui.BeginDisabled();
        }

        var on = _nsfwOn;
        if (ImGui.Checkbox(Loc.T("settings.nsfw_profile"), ref on))
        {
            if (!on && _nsfwLocked)
            {
                _nsfwShowLockedHint = true;
            }
            else
            {
                _nsfwShowLockedHint = false;
                SetNsfw(on);
            }
        }
        SharedUiHelpers.HandOnHover();

        if (blocked)
        {
            ImGui.EndDisabled();
        }

        ImGui.SameLine();
        HelpMarker(Loc.T("settings.nsfw_profile_tooltip"));

        if (_nsfwShowLockedHint || (_nsfwOn && _nsfwLocked))
        {
            ImGui.SetCursorPosX(Px(PadX));
            ImGui.PushTextWrapPos(winW - Px(PadX));
            ImGui.TextColored(new Vector4(0.95f, 0.65f, 0.25f, 1f), Loc.T("settings.nsfw_profile_locked"));
            ImGui.PopTextWrapPos();
        }

        if (_nsfwError is not null)
        {
            ImGui.SetCursorPosX(Px(PadX));
            ImGui.PushTextWrapPos(winW - Px(PadX));
            ImGui.TextColored(UiColors.Danger, _nsfwError);
            ImGui.PopTextWrapPos();
        }
    }

    private static void DrawPulseOptOut(float winW)
    {
        // Hidden until a pulse line has been seen, to keep it a surprise.
        if (!UiHost.Configuration.Pulse.SeenPulse)
        {
            return;
        }
        ImGui.Spacing();
        ImGui.SetCursorPosX(Px(PadX));
        var on = !UiHost.Configuration.Pulse.MutePulse;
        if (ImGui.Checkbox("##pulseOptout", ref on))
        {
            UiHost.Configuration.Pulse.MutePulse = !on;
            UiHost.Configuration.Save();
        }
        SharedUiHelpers.HandOnHover();
        ImGui.SameLine(0f, Px(6f));
        HelpMarker(Loc.T("settings.pulse_optout_tooltip"));
        ImGui.SameLine(0f, Px(6f));
        ImGui.PushTextWrapPos(winW - Px(PadX));
        ImGui.TextWrapped(Loc.T("settings.pulse_optout"));
        ImGui.PopTextWrapPos();
    }

    private static void SectionLabel(string title, Vector4 color)
    {
        ImGui.SetCursorPosX(Px(PadX));
        ImGui.TextColored(color, title);
        ImGui.Spacing();
    }

    private static void ImpactBullet(float winW, string text)
    {
        ImGui.SetCursorPosX(Px(PadX));
        ImGui.TextColored(new Vector4(0.88f, 0.28f, 0.28f, 1f), "•");
        ImGui.SameLine(0f, Px(6f));
        ImGui.PushTextWrapPos(winW - Px(PadX));
        ImGui.TextColored(new Vector4(0.88f, 0.88f, 0.88f, 1f), text);
        ImGui.PopTextWrapPos();
        ImGui.Spacing();
    }

    private static void Divider(ImDrawListPtr dl, float winW)
    {
        ImGui.SetCursorPosX(Px(PadX));
        var p = ImGui.GetCursorScreenPos();
        var endX = p.X + winW - Px(PadX) * 2f;
        dl.AddLine(p, new Vector2(endX, p.Y), UiColors.Divider, 1f);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + Px(6f));
        ImGui.Spacing();
    }
}
