using System;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Config;
using AetherLove.Navigation;
using AetherLove.Services;
using AetherLove.Services.Auth;
using AetherLove.Services.Hub;
using AetherLove.Services.Localization;
using AetherLove.Services.Signal;
using AetherLove.Shared.Feedback;
using AetherLove.Shared.Profile.Enums;
using AetherLove.UI;
using AetherLove.Windows;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherLove.Screens;

/// <summary>Settings screen with theme selection, about info, and account deletion.</summary>
public class SettingsScreen
{
    private readonly ScreenRouter _router;
    private readonly AetherLoveHubClient _hubClient;
    private readonly AetherSignalService _signal;
    private readonly TokenService _tokens;
    private readonly SessionBootstrapper _bootstrap;
    private readonly ChangelogWindow _changelogWindow;

    private enum View { Hub, LanguageTheme, General, Notifications, Appearance, ChatColors, ConfirmDelete, Deleting, Deleted, Feedback, Tos, Contributors }

    // volatile: written from the deletion task, read on the UI thread.
    private volatile int _viewRaw = (int)View.Hub;
    private View _view
    {
        get => (View)_viewRaw;
        set => _viewRaw = (int)value;
    }

    private volatile string? _deleteError;

    private FeedbackKind _feedbackKind = FeedbackKind.Bug;
    private string _feedbackText = string.Empty;
    private volatile bool _feedbackSubmitting;
    private volatile string? _feedbackError;
    private volatile bool _feedbackDone;

    private volatile bool _nsfwLoaded;
    private volatile bool _nsfwOn;
    private volatile bool _nsfwLocked;
    private volatile bool _nsfwBusy;
    private volatile string? _nsfwError;
    private bool _nsfwShowLockedHint;

    private readonly Widgets.ConfettiBurst _thanksConfetti = new();

    private const float PadX = 16f;

    // Icon tint for the "Other" Discord row, and the danger-zone label / delete-button outline colour.
    private static readonly Vector4 DiscordTop = new(0.43f, 0.48f, 1.00f, 1f);
    private static readonly Vector4 DangerLabelColor = new(0.93f, 0.36f, 0.36f, 1f);

    public SettingsScreen(ScreenRouter router,
                          AetherLoveHubClient hubClient,
                          AetherSignalService signal,
                          TokenService tokens,
                          SessionBootstrapper bootstrap,
                          ChangelogWindow changelogWindow)
    {
        _router = router;
        _hubClient = hubClient;
        _signal = signal;
        _tokens = tokens;
        _bootstrap = bootstrap;
        _changelogWindow = changelogWindow;
    }

    public void OnShow()
    {
        _view = View.Hub;
        _deleteError = null;
        ResetFeedback();
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

    private void ResetFeedback()
    {
        _feedbackKind = FeedbackKind.Bug;
        _feedbackText = string.Empty;
        _feedbackSubmitting = false;
        _feedbackError = null;
        _feedbackDone = false;
    }

    public void Draw()
    {
        var winW = ImGui.GetWindowSize().X;

        switch (_view)
        {
            case View.Hub:
                DrawHub(winW);
                break;
            case View.LanguageTheme:
                DrawLanguageThemePage(winW);
                break;
            case View.General:
                DrawGeneralPage(winW);
                break;
            case View.Notifications:
                DrawNotificationsPage(winW);
                break;
            case View.Appearance:
                DrawAppearancePage(winW);
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
            case View.Deleted:
                DrawDeleted(winW);
                break;
            case View.Feedback:
                DrawFeedback(winW);
                break;
            case View.Tos:
                DrawTos(winW);
                break;
            case View.Contributors:
                DrawContributors(winW);
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

            ImGui.Spacing();
            ImGui.Spacing();

            DrawSectionHeader(Loc.T("settings.section_plugin_settings"), PadX);
            DrawMenuCard("settMain", winW, PadX, new System.Collections.Generic.List<MenuRow>
            {
                new(FontAwesomeIcon.Cog, t.Accent, Loc.T("settings.section_general"), 0, false, () => _view = View.General),
                new(FontAwesomeIcon.Palette, t.Accent, Loc.T("settings.menu_language_theme"), 0, false, () => _view = View.LanguageTheme),
                new(FontAwesomeIcon.MobileAlt, t.Accent, Loc.T("settings.menu_appearance"), 0, false, () => _view = View.Appearance),
                new(FontAwesomeIcon.Bell, t.Accent, Loc.T("settings.section_notifications"), 0, false, () => _view = View.Notifications),
                new(FontAwesomeIcon.Comments, t.Accent, Loc.T("settings.menu_chat_colors"), 0, false, () => _view = View.ChatColors),
            });

            ImGui.Spacing();
            ImGui.Spacing();

            DrawSectionHeader(Loc.T("settings.section_other"), PadX);
            DrawOtherCard(winW, t);

            ImGui.Spacing();
            ImGui.Spacing();

            SectionLabel(Loc.T("settings.section_danger_zone"), DangerLabelColor);
            ImGui.Spacing();
            DrawDeleteButton(winW);

            ImGui.Spacing();
            ImGui.Spacing();
        }
    }

    private void DrawGeneralPage(float winW)
    {
        DrawSubpageBack();
        DrawSubpageHeading(Loc.T("settings.section_general"), PadX);

        var scrollH = ImGui.GetContentRegionAvail().Y;
        PushScrollbarStyle();
        using (var scroll = Dalamud.Interface.Utility.Raii.ImRaii.Child("##settGeneral", new Vector2(0f, scrollH), false))
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
            SettingCheckbox(Loc.T("settings.disable_startup_heartbeat"),
                () => Plugin.Configuration.DisableStartupHeartbeatSound,
                v => Plugin.Configuration.DisableStartupHeartbeatSound = v);
            SettingCheckbox(Loc.T("settings.confirm_before_close"),
                () => !Plugin.Configuration.SkipCloseConfirmation,
                v => Plugin.Configuration.SkipCloseConfirmation = !v);
            DrawPulseOptOut(winW);
            ImGui.Spacing();
        }
    }

    private void DrawNotificationsPage(float winW)
    {
        DrawSubpageBack();
        DrawSubpageHeading(Loc.T("settings.section_notifications"), PadX);

        var scrollH = ImGui.GetContentRegionAvail().Y;
        PushScrollbarStyle();
        using (var scroll = Dalamud.Interface.Utility.Raii.ImRaii.Child("##settNotifs", new Vector2(0f, scrollH), false))
        {
            PopScrollbarStyle();
            if (!scroll.Success)
            {
                return;
            }

            ImGui.Spacing();
            DrawNotificationSettings(winW);
            ImGui.Spacing();
        }
    }

    private void DrawAppearancePage(float winW)
    {
        var t = ThemeService.Current;
        DrawSubpageBack();
        DrawSubpageHeading(Loc.T("settings.menu_appearance"), PadX);

        var scrollH = ImGui.GetContentRegionAvail().Y;
        PushScrollbarStyle();
        using (var scroll = Dalamud.Interface.Utility.Raii.ImRaii.Child("##settAppearance", new Vector2(0f, scrollH), false))
        {
            PopScrollbarStyle();
            if (!scroll.Success)
            {
                return;
            }

            var dl = ImGui.GetWindowDrawList();

            ImGui.Spacing();
            SectionLabel(Loc.T("settings.section_phone_size"), t);
            ImGui.Spacing();
            Widgets.AppearancePicker.DrawPhoneSizeButtons(winW, PadX, t);
            ImGui.Spacing();
            Divider(dl, winW);

            SectionLabel(Loc.T("settings.section_mini_phone_size"), t);
            ImGui.Spacing();
            Widgets.AppearancePicker.DrawMiniSizeButtons(winW, PadX, t);
            ImGui.Spacing();
            Widgets.AppearancePicker.DrawMiniSizePreview(winW, PadX);
            ImGui.Spacing();
        }
    }

    private void DrawLanguageThemePage(float winW)
    {
        var t = ThemeService.Current;
        DrawSubpageBack();
        DrawSubpageHeading(Loc.T("settings.menu_language_theme"), PadX);

        var scrollH = ImGui.GetContentRegionAvail().Y;
        PushScrollbarStyle();
        using (var scroll = Dalamud.Interface.Utility.Raii.ImRaii.Child("##settLangTheme", new Vector2(0f, scrollH), false))
        {
            PopScrollbarStyle();
            if (!scroll.Success)
            {
                return;
            }

            var dl = ImGui.GetWindowDrawList();

            ImGui.Spacing();
            SectionLabel(Loc.T("settings.section_plugin_language"), t);
            ImGui.Spacing();
            DrawLanguagePills(winW);
            ImGui.Spacing();
            Divider(dl, winW);

            SectionLabel(Loc.T("settings.section_theme"), t);
            ImGui.Spacing();
            Widgets.AppearancePicker.DrawThemeCards(winW, PadX);
            ImGui.Spacing();
        }
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
                () => Plugin.Configuration.OwnChatBg, v => Plugin.Configuration.OwnChatBg = v, ChatColors.OwnBgDefault);
            DrawColorRow(Loc.T("settings.chat_own_fg"), "ownfg",
                () => Plugin.Configuration.OwnChatFg, v => Plugin.Configuration.OwnChatFg = v, ChatColors.OwnFgDefault);
            DrawColorRow(Loc.T("settings.chat_peer_bg"), "peerbg",
                () => Plugin.Configuration.PeerChatBg, v => Plugin.Configuration.PeerChatBg = v, ChatColors.PeerBgDefault);
            DrawColorRow(Loc.T("settings.chat_peer_fg"), "peerfg",
                () => Plugin.Configuration.PeerChatFg, v => Plugin.Configuration.PeerChatFg = v, ChatColors.PeerFgDefault);

            ImGui.Spacing();
        }
    }

    /// <summary>A label + colour swatch (opens a picker) + a reset-to-theme button for one chat colour.</summary>
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
            Plugin.Configuration.Save();
        }

        ImGui.SameLine(0f, Px(8f));
        PushThemeButton(ThemeService.Current);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(6f));
        if (ImGui.Button($"{Loc.T("settings.chat_reset")}##reset{id}", new Vector2(0f, Px(24f))))
        {
            set(null);
            Plugin.Configuration.Save();
        }
        ImGui.PopStyleVar();
        PopThemeButton();

        ImGui.Spacing();
        ImGui.Spacing();
    }

    /// <summary>A live three-bubble chat sample (own, peer, own) using the current chat colours.</summary>
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

    /// <summary>The themed "← Back" pill at the top of a settings sub-page; returns to the settings hub.</summary>
    private void DrawSubpageBack()
    {
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.SetCursorPosX(Px(PadX));
        if (DrawBackButton(Loc.T("settings.back_arrow")))
        {
            _view = View.Hub;
        }
        ImGui.Spacing();
    }

    private void DrawOtherCard(float winW, ThemeDefinition t)
    {
        DrawMenuCard("settOther", winW, PadX, new System.Collections.Generic.List<MenuRow>
        {
            new(FontAwesomeIcon.FileContract, t.Accent, Loc.T("settings.terms_of_service"), 0, false, () => _view = View.Tos),
            new(FontAwesomeIcon.History, t.Accent, Loc.T("settings.view_changelog"), 0, false, () => _changelogWindow.IsOpen = true),
            new(FontAwesomeIcon.CommentDots, t.Accent, Loc.T("settings.send_feedback"), 0, false, () =>
            {
                ResetFeedback();
                _view = View.Feedback;
            }),
            new(FontAwesomeIcon.Comments, DiscordTop, "Discord", 0, true, OpenDiscord),
            new(FontAwesomeIcon.Heart, t.Accent, Loc.T("settings.contributors"), 0, false, () =>
            {
                _thanksConfetti.Reset();
                _view = View.Contributors;
            }),
        });
    }

    /// <summary>A red-outlined (not filled) "Delete Account" button that opens the confirmation flow.</summary>
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
        if (ImGui.Button(Loc.T("settings.delete_account"), new Vector2(winW - Px(PadX) * 2f, Px(38f))))
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
            ImGui.TextColored(new Vector4(0.92f, 0.28f, 0.28f, 1f), Loc.T("settings.delete_account"));
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
                Loc.T("settings.delete_warning_intro"));
            ImGui.PopTextWrapPos();
            ImGui.Spacing();
            ImGui.Spacing();

            ImpactBullet(winW, Loc.T("settings.delete_bullet_account"));
            ImpactBullet(winW, Loc.T("settings.delete_bullet_matches"));
            ImpactBullet(winW, Loc.T("settings.delete_bullet_preferences"));
            ImpactBullet(winW, Loc.T("settings.delete_bullet_pictures"));
            ImGui.Spacing();

            ImGui.SetCursorPosX(Px(PadX));
            ImGui.PushTextWrapPos(winW - Px(PadX));
            ImGui.TextColored(new Vector4(0.65f, 0.65f, 0.65f, 1f),
                Loc.T("settings.delete_reregister"));
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
            if (ImGui.Button($"{Loc.T("settings.cancel")}##delCancel", new Vector2(cancelW, Px(36f))))
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
            if (ImGui.Button($"{Loc.T("settings.delete_account")}##delConfirm", new Vector2(deleteW, Px(36f))))
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
        var t = ThemeService.Current;
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


    private void DrawDeleted(float winW)
    {
        var t = ThemeService.Current;

        var scrollH = ImGui.GetContentRegionAvail().Y;
        using (var scroll = Dalamud.Interface.Utility.Raii.ImRaii.Child("##settDeleted", new Vector2(0f, scrollH), false))
        {
            if (!scroll.Success)
            {
                return;
            }

            var dl = ImGui.GetWindowDrawList();

            ImGui.Spacing();
            ImGui.Spacing();

            ImGui.SetCursorPosX(Px(PadX));
            ImGui.TextColored(new Vector4(0.88f, 0.88f, 0.88f, 1f), Loc.T("settings.deleted_title"));
            ImGui.Spacing();
            Divider(dl, winW);

            ImGui.Spacing();
            ImGui.SetCursorPosX(Px(PadX));
            ImGui.PushTextWrapPos(winW - Px(PadX));
            ImGui.TextColored(UiColors.Body,
                Loc.T("settings.deleted_body"));
            ImGui.PopTextWrapPos();
            ImGui.Spacing();
            ImGui.Spacing();

            ImGui.SetCursorPosX(Px(PadX));
            ImGui.PushStyleColor(ImGuiCol.Button, t.ButtonNormal);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, t.ButtonHovered);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, t.ButtonActive);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
            if (ImGui.Button(Loc.T("settings.create_new_profile"), new Vector2(winW - Px(PadX) * 2f, Px(36f))))
            {
                StartFreshOnboarding();
            }
            ImGui.PopStyleVar();
            ImGui.PopStyleColor(3);

            ImGui.Spacing();
            ImGui.Spacing();
        }
    }


    private void StartDeletion()
    {
        _deleteError = null;
        _ = Task.Run(async () =>
        {
            try
            {
                await _hubClient.DeleteAccountAsync(CancellationToken.None).ConfigureAwait(false);
                await _signal.DisconnectAsync().ConfigureAwait(false);
                _tokens.Clear();
                ClearLocalProfileConfig();
                _view = View.Deleted;
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "[Settings] DeleteAccountAsync failed.");
                _deleteError = HubErrorText.Localize(ex);
                _view = View.ConfirmDelete;
            }
        });
    }

    private void StartFreshOnboarding()
    {
        ClearLocalProfileConfig();
        _router.Navigate(Screen.Onboarding);
    }

    private static void ClearLocalProfileConfig()
    {
        Plugin.Configuration.Crypto = new CryptoKeys();
        Plugin.Configuration.Save();
    }


    private static void DrawLanguagePills(float winW)
    {
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var entries = LanguageEntries;
        var count = LanguageProvider.UiLanguageCount;

        var FlagW = Px(36f);
        var FlagH = Px(27f);
        var PillPadX = Px(6f);
        var PillPadY = Px(5f);
        var LabelGap = Px(3f);
        var PillGap = Px(6f);
        var labelH = ImGui.GetTextLineHeight();
        var pillW = FlagW + PillPadX * 2f;
        var pillH = PillPadY + FlagH + LabelGap + labelH + PillPadY;

        var usableW = winW - Px(PadX) * 2f;
        var totalW = count * pillW + (count - 1) * PillGap;
        var startX = Px(PadX) + MathF.Max(0f, (usableW - totalW) * 0.5f);
        var startY = ImGui.GetCursorPosY();

        var currentLang = LanguageProvider.Current.LanguageName;

        for (int i = 0; i < count; i++)
        {
            var entry = entries[i];
            var selected = string.Equals(entry.Name, currentLang, StringComparison.OrdinalIgnoreCase);
            var pillX = startX + i * (pillW + PillGap);

            ImGui.SetCursorPos(new Vector2(pillX, startY));
            var sp = ImGui.GetCursorScreenPos();

            ImGui.InvisibleButton($"##settLang{i}", new Vector2(pillW, pillH));
            if (ImGui.IsItemClicked())
            {
                LanguageProvider.SetLanguage(entry.Name);
            }
            var hovered = ImGui.IsItemHovered();

            var bgCol = selected
                ? t.AccentWithAlpha(0.28f)
                : hovered ? 0x22FFFFFFu : 0x0DFFFFFFu;
            dl.AddRectFilled(sp, sp + new Vector2(pillW, pillH), bgCol, Px(7f));

            var borderCol = selected ? t.AccentU32 : (hovered ? 0x55FFFFFFu : 0x33FFFFFFu);
            var borderThick = selected ? 2f : 1f;
            dl.AddRect(sp, sp + new Vector2(pillW, pillH), borderCol, Px(7f), ImDrawFlags.None, borderThick);

            var flagTL = sp + new Vector2(PillPadX, PillPadY);
            var flagBR = flagTL + new Vector2(FlagW, FlagH);
            var flagTex = LanguageFlagService.GetFlag(entry.Name)?.GetWrapOrDefault();
            if (flagTex != null)
            {
                dl.AddImageRounded(flagTex.Handle, flagTL, flagBR,
                    Vector2.Zero, Vector2.One, 0xFFFFFFFF, Px(3f));
            }
            else
            {
                dl.AddRectFilled(flagTL, flagBR, t.AccentDarkWithAlpha(0.6f), Px(3f));
                var codeSz = ImGui.CalcTextSize(entry.Code);
                dl.AddText(flagTL + (new Vector2(FlagW, FlagH) - codeSz) * 0.5f,
                    0xFFFFFFFF, entry.Code);
            }

            var labelSz = ImGui.CalcTextSize(entry.Code);
            var labelX = sp.X + (pillW - labelSz.X) * 0.5f;
            var labelY = sp.Y + PillPadY + FlagH + LabelGap;
            dl.AddText(new Vector2(labelX, labelY),
                selected ? 0xFFFFFFFF : 0xAAFFFFFF, entry.Code);
        }

        ImGui.SetCursorPosY(startY + pillH + Px(4f));
    }

    private static void DrawNsfwBlurToggle(float winW)
    {
        ImGui.SetCursorPosX(Px(PadX));
        var blur = Plugin.Configuration.AlwaysBlurNsfw;
        if (ImGui.Checkbox(Loc.T("settings.always_blur_nsfw"), ref blur))
        {
            Plugin.Configuration.AlwaysBlurNsfw = blur;
            Plugin.Configuration.Save();
        }
        ImGui.SameLine();

        ImGui.PushFont(Plugin.PluginInterface.UiBuilder.FontIcon);
        var iconStr = FontAwesomeIcon.QuestionCircle.ToIconString();
        ImGui.TextColored(UiColors.Muted, iconStr);
        ImGui.PopFont();
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(Px(360f));
            ImGui.TextUnformatted(Loc.T("settings.always_blur_nsfw_tooltip"));
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
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

    private static void HelpMarker(string text)
    {
        ImGui.PushFont(Plugin.PluginInterface.UiBuilder.FontIcon);
        ImGui.TextColored(UiColors.Muted, FontAwesomeIcon.QuestionCircle.ToIconString());
        ImGui.PopFont();
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(Px(360f));
            ImGui.TextUnformatted(text);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
    }

    private static void SettingCheckbox(string label, Func<bool> get, Action<bool> set)
    {
        ImGui.SetCursorPosX(Px(PadX));
        var value = get();
        if (ImGui.Checkbox(label, ref value))
        {
            set(value);
            Plugin.Configuration.Save();
        }
    }

    private static void DrawNotificationSettings(float winW)
    {
        // Master switch — when off, every notification option below is greyed out and ignored.
        SettingCheckbox(Loc.T("settings.enable_notifications"),
            () => Plugin.Configuration.EnableNotifications,
            v => Plugin.Configuration.EnableNotifications = v);
        ImGui.SameLine();
        HelpMarker(Loc.T("settings.enable_notifications_tooltip"));
        ImGui.Spacing();

        ImGui.BeginDisabled(!Plugin.Configuration.EnableNotifications);

        SettingCheckbox(Loc.T("settings.enable_notification_sounds"),
            () => Plugin.Configuration.EnableNotificationSounds,
            v => Plugin.Configuration.EnableNotificationSounds = v);
        ImGui.SameLine();
        HelpMarker(Loc.T("settings.enable_notification_sounds_tooltip"));
        ImGui.Spacing();

        DrawNotificationSoundCombo(winW);
        ImGui.Spacing();

        SettingCheckbox(Loc.T("settings.announce_messages_chat"),
            () => Plugin.Configuration.NotifyChatOnMessage,
            v => Plugin.Configuration.NotifyChatOnMessage = v);
        SettingCheckbox(Loc.T("settings.announce_matches_chat"),
            () => Plugin.Configuration.NotifyChatOnMatch,
            v => Plugin.Configuration.NotifyChatOnMatch = v);
        SettingCheckbox(Loc.T("settings.popup_messages"),
            () => Plugin.Configuration.NotifyPopupOnMessage,
            v => Plugin.Configuration.NotifyPopupOnMessage = v);
        SettingCheckbox(Loc.T("settings.popup_matches"),
            () => Plugin.Configuration.NotifyPopupOnMatch,
            v => Plugin.Configuration.NotifyPopupOnMatch = v);

        ImGui.EndDisabled();

        // Opening the minimized bubble on login isn't a notification, so it stays available.
        SettingCheckbox(Loc.T("settings.auto_open_minimized"),
            () => Plugin.Configuration.AutoOpenMinimizedOnLogin,
            v => Plugin.Configuration.AutoOpenMinimizedOnLogin = v);

        SettingCheckbox(Loc.T("settings.hide_notifications_in_combat"),
            () => Plugin.Configuration.HideNotificationsDuringCombat,
            v => Plugin.Configuration.HideNotificationsDuringCombat = v);
        ImGui.SameLine();
        HelpMarker(Loc.T("settings.hide_notifications_in_combat_tooltip"));

        ImGui.Spacing();
        ImGui.SetCursorPosX(Px(PadX));
        ImGui.TextColored(new Vector4(0.70f, 0.70f, 0.70f, 1f), Loc.T("settings.combat_behavior"));
        ImGui.Spacing();

        var options = new[]
        {
            (CombatBehavior.Hide, Loc.T("settings.combat_behavior_hide")),
            (CombatBehavior.Minimize, Loc.T("settings.combat_behavior_minimize")),
            (CombatBehavior.LeaveOpen, Loc.T("settings.combat_behavior_leave_open")),
        };
        foreach (var (value, label) in options)
        {
            ImGui.SetCursorPosX(Px(PadX));
            var selected = Plugin.Configuration.CombatBehavior == value;
            if (ImGui.RadioButton(label, selected))
            {
                Plugin.Configuration.CombatBehavior = value;
                Plugin.Configuration.Save();
            }
        }
    }

    private static void DrawNotificationSoundCombo(float winW)
    {
        var soundsOn = Plugin.Configuration.EnableNotificationSounds;

        ImGui.SetCursorPosX(Px(PadX));
        ImGui.TextColored(new Vector4(0.70f, 0.70f, 0.70f, 1f), Loc.T("settings.notification_sound"));

        ImGui.BeginDisabled(!soundsOn);

        var PlayBtnW = Px(34f);
        var Gap = Px(6f);

        ImGui.SetCursorPosX(Px(PadX));
        ImGui.SetNextItemWidth(winW - Px(PadX) * 2f - PlayBtnW - Gap);
        var current = Plugin.Configuration.NotificationSoundChoice;
        if (ImGui.BeginCombo("##notifSound", current.DisplayName()))
        {
            foreach (var sound in Enum.GetValues<NotificationSound>())
            {
                var selected = sound == current;
                if (ImGui.Selectable(sound.DisplayName(), selected))
                {
                    Plugin.Configuration.NotificationSoundChoice = sound;
                    Plugin.Configuration.Save();
                }
                if (selected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }
            ImGui.EndCombo();
        }

        ImGui.SameLine(0f, Gap);
        ImGui.PushFont(Plugin.PluginInterface.UiBuilder.FontIcon);
        var play = ImGui.Button($"{FontAwesomeIcon.Play.ToIconString()}##playNotifSound", new Vector2(PlayBtnW, 0f));
        ImGui.PopFont();
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("settings.play"));
        }
        if (play)
        {
            NotificationSoundPlayer.Play(Plugin.Configuration.NotificationSoundChoice);
        }

        ImGui.EndDisabled();
    }

    private static void DrawPulseOptOut(float winW)
    {
        // Surfaces only after the player has actually seen a pulse line — keeps it a surprise until then.
        if (!Plugin.Configuration.Pulse.SeenPulse)
        {
            return;
        }
        ImGui.Spacing();
        ImGui.SetCursorPosX(Px(PadX));
        var on = !Plugin.Configuration.Pulse.MutePulse;
        if (ImGui.Checkbox("##pulseOptout", ref on))
        {
            Plugin.Configuration.Pulse.MutePulse = !on;
            Plugin.Configuration.Save();
        }
        ImGui.SameLine(0f, Px(6f));
        HelpMarker(Loc.T("settings.pulse_optout_tooltip"));
        // The label is long and playful, so it wraps instead of overflowing the checkbox row.
        ImGui.SameLine(0f, Px(6f));
        ImGui.PushTextWrapPos(winW - Px(PadX));
        ImGui.TextWrapped(Loc.T("settings.pulse_optout"));
        ImGui.PopTextWrapPos();
    }

    private void DrawTos(float winW)
    {
        var t = ThemeService.Current;
        var scrollH = ImGui.GetContentRegionAvail().Y;

        PushScrollbarStyle();
        using (var scroll = Dalamud.Interface.Utility.Raii.ImRaii.Child("##settTos", new Vector2(0f, scrollH), false))
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
            ImGui.TextColored(new Vector4(1f, 1f, 1f, 1f), Loc.T("settings.terms_of_service"));
            ImGui.Spacing();
            Divider(dl, winW);

            foreach (var para in TermsOfServiceParagraphs())
            {
                ImGui.SetCursorPosX(Px(PadX));
                ImGui.PushTextWrapPos(winW - Px(PadX));
                ImGui.TextColored(UiColors.Body, para);
                ImGui.PopTextWrapPos();
                ImGui.Spacing();
                ImGui.Spacing();
            }

            ImGui.Spacing();
            ImGui.SetCursorPosX(Px(PadX));
            ImGui.PushStyleColor(ImGuiCol.Button, t.ButtonNormal);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, t.ButtonHovered);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, t.ButtonActive);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
            if (ImGui.Button(Loc.T("settings.back_to_settings_arrow"), new Vector2(winW - Px(PadX) * 2f, Px(32f))))
            {
                _view = View.Hub;
            }
            ImGui.PopStyleVar();
            ImGui.PopStyleColor(3);

            ImGui.Spacing();
        }
    }

    private void DrawContributors(float winW)
    {
        var t = ThemeService.Current;
        var scrollH = ImGui.GetContentRegionAvail().Y;

        PushScrollbarStyle();
        using (var scroll = Dalamud.Interface.Utility.Raii.ImRaii.Child("##settContributors", new Vector2(0f, scrollH), false))
        {
            PopScrollbarStyle();
            if (!scroll.Success)
            {
                return;
            }

            if (!AccessibilityService.ReduceMotion)
            {
                var winPos = ImGui.GetWindowPos();
                _thanksConfetti.Draw(winPos, winPos + ImGui.GetWindowSize());
            }

            ImGui.Dummy(new Vector2(0f, Px(20f)));
            CenteredIcon(FontAwesomeIcon.Heart, t.Accent, winW, Px(48f));
            ImGui.Dummy(new Vector2(0f, Px(12f)));

            using (UiFonts.H1?.Push())
            {
                CenteredHeading(Loc.T("settings.contributors_thanks_title"), new Vector4(1f, 1f, 1f, 1f), winW);
            }
            ImGui.Dummy(new Vector2(0f, Px(4f)));
            var introCol = new Vector4(t.AccentLight.X, t.AccentLight.Y, t.AccentLight.Z, 1f);
            using (UiFonts.H3?.Push())
            {
                CenteredHeading(Loc.T("settings.contributors_intro"), introCol, winW);
            }

            ImGui.Dummy(new Vector2(0f, Px(18f)));

            string[] credits =
            [
                Loc.T("settings.contributors_leads"),
                Loc.T("settings.contributors_council"),
                Loc.T("settings.contributors_moderation"),
                Loc.T("settings.contributors_translators"),
                Loc.T("settings.contributors_xivauth"),
                Loc.T("settings.contributors_punish"),
                Loc.T("settings.contributors_dalamud"),
                Loc.T("settings.contributors_testers"),
            ];

            var heartGlyph = FontAwesomeIcon.Heart.ToIconString();
            var rowCol = new Vector4(0.94f, 0.93f, 0.97f, 1f);
            using (UiFonts.H3?.Push())
            {
                foreach (var line in credits)
                {
                    ImGui.SetCursorPosX(Px(PadX) + Px(10f));
                    ImGui.PushFont(Plugin.PluginInterface.UiBuilder.FontIcon);
                    ImGui.TextColored(t.Accent, heartGlyph);
                    ImGui.PopFont();
                    ImGui.SameLine(0f, Px(12f));
                    ImGui.PushTextWrapPos(winW - Px(PadX));
                    ImGui.TextColored(rowCol, line);
                    ImGui.PopTextWrapPos();
                    ImGui.Dummy(new Vector2(0f, Px(9f)));
                }
            }

            ImGui.Dummy(new Vector2(0f, Px(12f)));
            ImGui.SetCursorPosX(Px(PadX));
            ImGui.PushStyleColor(ImGuiCol.Button, t.ButtonNormal);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, t.ButtonHovered);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, t.ButtonActive);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
            if (ImGui.Button(Loc.T("settings.back_to_settings_arrow"), new Vector2(winW - Px(PadX) * 2f, Px(32f))))
            {
                _view = View.Hub;
            }
            ImGui.PopStyleVar();
            ImGui.PopStyleColor(3);

            ImGui.Spacing();
        }
    }

    /// <summary>Centers a single line horizontally in the content area; falls back to a left-padded, wrapped
    /// paragraph when the text is too wide to fit on one line.</summary>
    private static void CenteredHeading(string text, Vector4 color, float winW)
    {
        var avail = winW - Px(PadX) * 2f;
        var w = ImGui.CalcTextSize(text).X;
        if (w <= avail)
        {
            ImGui.SetCursorPosX(MathF.Max(Px(PadX), (winW - w) * 0.5f));
            ImGui.TextColored(color, text);
        }
        else
        {
            ImGui.SetCursorPosX(Px(PadX));
            ImGui.PushTextWrapPos(winW - Px(PadX));
            ImGui.TextColored(color, text);
            ImGui.PopTextWrapPos();
        }
    }

    /// <summary>Draws a FontAwesome glyph centered in the content area, scaled to <paramref name="sizePx"/>.</summary>
    private static void CenteredIcon(FontAwesomeIcon icon, Vector4 color, float winW, float sizePx)
    {
        ImGui.PushFont(Plugin.PluginInterface.UiBuilder.FontIcon);
        var iconFont = ImGui.GetFont();
        var glyph = icon.ToIconString();
        var sz = ImGui.CalcTextSize(glyph) * (sizePx / ImGui.GetFontSize());
        ImGui.PopFont();

        var origin = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        dl.AddText(iconFont, sizePx, new Vector2(origin.X + (winW - sz.X) * 0.5f, origin.Y), ImGui.GetColorU32(color), glyph);
        ImGui.Dummy(new Vector2(winW, sz.Y));
    }

    private void DrawFeedback(float winW)
    {
        var t = ThemeService.Current;
        var scrollH = ImGui.GetContentRegionAvail().Y;

        PushScrollbarStyle();

        using (var scroll = Dalamud.Interface.Utility.Raii.ImRaii.Child("##settFeedback", new Vector2(0f, scrollH), false))
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
            ImGui.TextColored(new Vector4(1f, 1f, 1f, 1f), Loc.T("settings.send_feedback"));
            ImGui.Spacing();
            Divider(dl, winW);

            if (_feedbackDone)
            {
                ImGui.Spacing();
                ImGui.SetCursorPosX(Px(PadX));
                ImGui.PushTextWrapPos(winW - Px(PadX));
                ImGui.TextColored(UiColors.Success,
                    Loc.T("settings.feedback_thanks"));
                ImGui.PopTextWrapPos();
                ImGui.Spacing();
                ImGui.Spacing();
                ImGui.SetCursorPosX(Px(PadX));
                ImGui.PushStyleColor(ImGuiCol.Button, t.ButtonNormal);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, t.ButtonHovered);
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, t.ButtonActive);
                ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
                if (ImGui.Button(Loc.T("settings.back_to_settings"), new Vector2(winW - Px(PadX) * 2f, Px(34f))))
                {
                    _view = View.Hub;
                }
                ImGui.PopStyleVar();
                ImGui.PopStyleColor(3);
                return;
            }

            ImGui.SetCursorPosX(Px(PadX));
            ImGui.PushTextWrapPos(winW - Px(PadX));
            ImGui.TextColored(new Vector4(0.82f, 0.82f, 0.82f, 1f),
                Loc.T("settings.feedback_intro"));
            ImGui.PopTextWrapPos();
            ImGui.Spacing();

            ImGui.SetCursorPosX(Px(PadX));
            ImGui.PushTextWrapPos(winW - Px(PadX));
            ImGui.TextColored(new Vector4(0.80f, 0.66f, 0.40f, 1f),
                Loc.T("settings.feedback_note"));
            ImGui.PopTextWrapPos();
            ImGui.Spacing();
            ImGui.Spacing();

            ImGui.SetCursorPosX(Px(PadX));
            ImGui.TextColored(new Vector4(0.70f, 0.70f, 0.70f, 1f), Loc.T("settings.feedback_type"));
            ImGui.Spacing();
            ImGui.SetCursorPosX(Px(PadX));
            DrawKindButton(winW, Loc.T("settings.feedback_kind_bug"), FeedbackKind.Bug, t);
            ImGui.SameLine(0f, Px(6f));
            DrawKindButton(winW, Loc.T("settings.feedback_kind_improvement"), FeedbackKind.Improvement, t);
            ImGui.SameLine(0f, Px(6f));
            DrawKindButton(winW, Loc.T("settings.feedback_kind_other"), FeedbackKind.Other, t);
            ImGui.Spacing();
            ImGui.Spacing();

            ImGui.SetCursorPosX(Px(PadX));
            ImGui.TextColored(new Vector4(0.70f, 0.70f, 0.70f, 1f), Loc.T("settings.feedback_your_message"));
            ImGui.Spacing();
            ImGui.SetCursorPosX(Px(PadX));
            ImGui.InputTextMultiline("##feedbackText", ref _feedbackText, 4000, new Vector2(winW - Px(PadX) * 2f, Px(140f)));
            ImGui.Spacing();

            if (_feedbackError is not null)
            {
                ImGui.SetCursorPosX(Px(PadX));
                ImGui.PushTextWrapPos(winW - Px(PadX));
                ImGui.TextColored(UiColors.Danger, _feedbackError);
                ImGui.PopTextWrapPos();
                ImGui.Spacing();
            }

            var canSubmit = !_feedbackSubmitting && !string.IsNullOrWhiteSpace(_feedbackText);
            var availBtnW = winW - Px(PadX) * 2f;
            var backW = MathF.Floor(availBtnW * 0.40f);
            var submitW = availBtnW - backW - Px(8f);

            ImGui.SetCursorPosX(Px(PadX));
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.20f, 0.20f, 0.20f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.34f, 0.34f, 0.34f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.14f, 0.14f, 0.14f, 1f));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
            if (ImGui.Button(Loc.T("settings.back"), new Vector2(backW, Px(36f))))
            {
                _view = View.Hub;
            }
            ImGui.PopStyleVar();
            ImGui.PopStyleColor(3);

            ImGui.SameLine(0f, Px(8f));
            ImGui.BeginDisabled(!canSubmit);
            ImGui.PushStyleColor(ImGuiCol.Button, t.ButtonNormal);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, t.ButtonHovered);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, t.ButtonActive);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
            if (ImGui.Button(_feedbackSubmitting ? Loc.T("settings.sending") : Loc.T("settings.submit"), new Vector2(submitW, Px(36f))))
            {
                StartSubmitFeedback();
            }
            ImGui.PopStyleVar();
            ImGui.PopStyleColor(3);
            ImGui.EndDisabled();

            ImGui.Spacing();
        }
    }

    private void DrawKindButton(float winW, string label, FeedbackKind kind, ThemeDefinition t)
    {
        var selected = _feedbackKind == kind;
        var w = (winW - Px(PadX) * 2f - Px(12f)) / 3f;

        if (selected)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, t.ButtonNormal);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, t.ButtonHovered);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, t.ButtonActive);
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.18f, 0.18f, 0.18f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.26f, 0.26f, 0.26f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.14f, 0.14f, 0.14f, 1f));
        }
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
        if (ImGui.Button($"{label}##fbkind{kind}", new Vector2(w, Px(30f))))
        {
            _feedbackKind = kind;
        }
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);
    }

    private void StartSubmitFeedback()
    {
        if (_feedbackSubmitting || string.IsNullOrWhiteSpace(_feedbackText))
        {
            return;
        }
        _feedbackSubmitting = true;
        _feedbackError = null;
        var kind = _feedbackKind;
        var text = _feedbackText;

        _ = Task.Run(async () =>
        {
            try
            {
                await _hubClient.SubmitFeedbackAsync(new SubmitFeedbackRequest(kind, text), CancellationToken.None)
                    .ConfigureAwait(false);
                _feedbackDone = true;
            }
            catch (RateLimitException rl)
            {
                _feedbackError = Loc.T("settings.feedback_rate_limited", rl.Limit);
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "[Settings] SubmitFeedbackAsync failed.");
                _feedbackError = Loc.T("settings.feedback_send_failed");
            }
            finally
            {
                _feedbackSubmitting = false;
            }
        });
    }

    private static void SectionLabel(string title, ThemeDefinition t) => SectionLabel(title, t.Accent);

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
