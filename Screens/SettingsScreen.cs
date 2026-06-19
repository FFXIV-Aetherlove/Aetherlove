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
    private readonly NewsScreen _newsScreen;

    private enum View { Normal, ConfirmDelete, Deleting, Deleted, WarningsList, Feedback, Tos, Contributors }

    // volatile: written from the deletion task, read on the UI thread.
    private volatile int _viewRaw = (int)View.Normal;
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

    // Gradient-button colours (top → bottom) for the "Other" group and the danger-zone delete button.
    private static readonly Vector4 DiscordTop = new(0.43f, 0.48f, 1.00f, 1f);
    // Distinct moderation orange (the "in review" accent), not a flat yellow.
    private static readonly Vector4 WarnTop = new(0.97f, 0.55f, 0.20f, 1f);
    private static readonly Vector4 WarnBottom = new(0.66f, 0.30f, 0.07f, 1f);
    private static readonly Vector4 DangerTop = new(0.66f, 0.16f, 0.16f, 1f);
    private static readonly Vector4 DangerBottom = new(0.34f, 0.05f, 0.05f, 1f);
    private static readonly Vector4 DangerLabelColor = new(0.93f, 0.36f, 0.36f, 1f);

    public SettingsScreen(ScreenRouter router,
                          AetherLoveHubClient hubClient,
                          AetherSignalService signal,
                          TokenService tokens,
                          SessionBootstrapper bootstrap,
                          ChangelogWindow changelogWindow,
                          NewsScreen newsScreen)
    {
        _router = router;
        _hubClient = hubClient;
        _signal = signal;
        _tokens = tokens;
        _bootstrap = bootstrap;
        _changelogWindow = changelogWindow;
        _newsScreen = newsScreen;
    }

    public void OnShow()
    {
        _view = View.Normal;
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
            case View.Normal:
                DrawNormal(winW);
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
            case View.WarningsList:
                DrawWarningsList(winW);
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


    private void DrawNormal(float winW)
    {
        var t = ThemeService.Current;

        var scrollH = ImGui.GetContentRegionAvail().Y;
        PushScrollbarStyle();

        using (var scroll = Dalamud.Interface.Utility.Raii.ImRaii.Child("##settNorm", new Vector2(0f, scrollH), false))
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
            ImGui.TextColored(new Vector4(1f, 1f, 1f, 1f), Loc.T("settings.title"));
            ImGui.Spacing();
            Divider(dl, winW);

            SectionLabel(Loc.T("settings.section_appearance"), t);
            ImGui.Spacing();
            Widgets.AppearancePicker.DrawThemeCards(winW, PadX);
            ImGui.Spacing();
            Divider(dl, winW);

            SectionLabel(Loc.T("settings.section_phone_size"), t);
            ImGui.Spacing();
            Widgets.AppearancePicker.DrawPhoneSizeButtons(winW, PadX, t);
            ImGui.Spacing();
            Divider(dl, winW);

            SectionLabel(Loc.T("settings.section_plugin_language"), t);
            ImGui.Spacing();
            DrawLanguagePills(winW);
            ImGui.Spacing();
            Divider(dl, winW);

            // General now also hosts the privacy toggles and the pulse opt-out.
            SectionLabel(Loc.T("settings.section_general"), t);
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
            Divider(dl, winW);

            SectionLabel(Loc.T("settings.section_notifications"), t);
            ImGui.Spacing();
            DrawNotificationSettings(winW);
            ImGui.Spacing();
            Divider(dl, winW);

            SectionLabel(Loc.T("settings.section_other"), t);
            ImGui.Spacing();
            DrawOtherButtons(winW, t);
            ImGui.Spacing();
            Divider(dl, winW);

            SectionLabel(Loc.T("settings.section_danger_zone"), DangerLabelColor);
            ImGui.Spacing();
            DrawWarningsButton(winW);
            if (GradientMenuButton("##settDelete", winW, Loc.T("settings.delete_account"),
                    FontAwesomeIcon.TrashAlt, DangerTop, DangerBottom))
            {
                _view = View.ConfirmDelete;
            }

            ImGui.Spacing();
            ImGui.Spacing();
        }
    }

    private void DrawConfirmDelete(float winW)
    {
        var scrollH = ImGui.GetContentRegionAvail().Y;
        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, new Vector4(0.08f, 0.08f, 0.08f, 0.6f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, new Vector4(0.65f, 0.15f, 0.15f, 0.85f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, new Vector4(0.80f, 0.22f, 0.22f, 1.0f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, new Vector4(0.45f, 0.08f, 0.08f, 1.0f));
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarSize, Px(6f));

        using (var scroll = Dalamud.Interface.Utility.Raii.ImRaii.Child("##settConfirm", new Vector2(0f, scrollH), false))
        {
            ImGui.PopStyleVar();
            ImGui.PopStyleColor(4);

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
                _view = View.Normal;
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
        var totalW = entries.Length * pillW + (entries.Length - 1) * PillGap;
        var startX = Px(PadX) + MathF.Max(0f, (usableW - totalW) * 0.5f);
        var startY = ImGui.GetCursorPosY();

        var currentLang = LanguageProvider.Current.LanguageName;

        for (int i = 0; i < entries.Length; i++)
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

    private void DrawOtherButtons(float winW, ThemeDefinition t)
    {
        var rowH = Px(44f);
        const int rowCount = 6;
        var origin = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        var cardMin = new Vector2(origin.X + Px(PadX), origin.Y);
        var cardMax = new Vector2(origin.X + winW - Px(PadX), origin.Y + rowH * rowCount);
        dl.AddRectFilled(cardMin, cardMax, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.045f)), Px(10f));

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0f, 0f));

        if (DrawMenuRow(winW, rowH, "##settTos", FontAwesomeIcon.FileContract, t.Accent, Loc.T("settings.terms_of_service"), false, false, 0))
        {
            _view = View.Tos;
        }
        if (DrawMenuRow(winW, rowH, "##settChangelog", FontAwesomeIcon.History, t.Accent, Loc.T("settings.view_changelog"), false, false, 0))
        {
            _changelogWindow.IsOpen = true;
        }
        var newsBadge = _bootstrap.HasUnseenNews ? _bootstrap.LastConnection!.UnseenNews.Length : 0;
        if (DrawMenuRow(winW, rowH, "##settNews", FontAwesomeIcon.Newspaper, t.Accent, Loc.T("news.settings_button"), false, false, newsBadge))
        {
            _newsScreen.RequestListView();
            _router.Navigate(Screen.News);
        }
        if (DrawMenuRow(winW, rowH, "##settFeedback", FontAwesomeIcon.CommentDots, t.Accent, Loc.T("settings.send_feedback"), false, false, 0))
        {
            ResetFeedback();
            _view = View.Feedback;
        }
        if (DrawMenuRow(winW, rowH, "##settDiscord", FontAwesomeIcon.Comments, DiscordTop, "Discord", false, true, 0))
        {
            OpenDiscord();
        }
        if (DrawMenuRow(winW, rowH, "##settContributors", FontAwesomeIcon.Heart, t.Accent, Loc.T("settings.contributors"), true, false, 0))
        {
            _thanksConfetti.Reset();
            _view = View.Contributors;
        }

        ImGui.PopStyleVar();

        dl.AddRect(cardMin, cardMax, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.07f)), Px(10f), ImDrawFlags.None, Px(1f));
    }

    /// <summary>One row of the "Other" menu card: a full-width hit target with a leading icon, a label, an
    /// optional unseen-count badge, and a trailing chevron (or external-link glyph for browser links). Rows
    /// share one grouped card so the section reads as a menu rather than a stack of buttons.</summary>
    private bool DrawMenuRow(float winW, float rowH, string id, FontAwesomeIcon icon, Vector4 iconColor, string label, bool isLast, bool external, int badge)
    {
        ImGui.SetCursorPosX(Px(PadX));
        ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(1f, 1f, 1f, 0.05f));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(1f, 1f, 1f, 0.08f));
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(1f, 1f, 1f, 0.12f));
        var clicked = ImGui.Selectable(id, false, ImGuiSelectableFlags.None, new Vector2(winW - Px(PadX) * 2f, rowH));
        ImGui.PopStyleColor(3);

        var rmin = ImGui.GetItemRectMin();
        var rmax = ImGui.GetItemRectMax();
        var dl = ImGui.GetWindowDrawList();
        var midY = (rmin.Y + rmax.Y) * 0.5f;
        var iconFontPtr = Plugin.PluginInterface.UiBuilder.FontIcon;

        var iconPx = Px(18f);
        ImGui.PushFont(iconFontPtr);
        var iconFont = ImGui.GetFont();
        var iconGlyph = icon.ToIconString();
        var iconSz = ImGui.CalcTextSize(iconGlyph) * (iconPx / ImGui.GetFontSize());
        ImGui.PopFont();
        var iconX = rmin.X + Px(14f);
        dl.AddText(iconFont, iconPx, new Vector2(iconX, midY - iconSz.Y * 0.5f), ImGui.GetColorU32(iconColor), iconGlyph);

        var labelSz = ImGui.CalcTextSize(label);
        dl.AddText(new Vector2(iconX + iconSz.X + Px(14f), midY - labelSz.Y * 0.5f),
            ImGui.GetColorU32(new Vector4(0.93f, 0.93f, 0.96f, 1f)), label);

        var rightX = rmax.X - Px(14f);
        var chevGlyph = (external ? FontAwesomeIcon.ExternalLinkAlt : FontAwesomeIcon.ChevronRight).ToIconString();
        var chevPx = external ? Px(12f) : Px(13f);
        ImGui.PushFont(iconFontPtr);
        var chevFont = ImGui.GetFont();
        var chevSz = ImGui.CalcTextSize(chevGlyph) * (chevPx / ImGui.GetFontSize());
        ImGui.PopFont();
        dl.AddText(chevFont, chevPx, new Vector2(rightX - chevSz.X, midY - chevSz.Y * 0.5f),
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.30f)), chevGlyph);

        if (badge > 0)
        {
            var badgeText = badge.ToString();
            var badgeSz = ImGui.CalcTextSize(badgeText);
            var pad = Px(7f);
            var pillH = Px(18f);
            var pillRight = rightX - chevSz.X - Px(10f);
            var pillMin = new Vector2(pillRight - badgeSz.X - pad * 2f, midY - pillH * 0.5f);
            var pillMax = new Vector2(pillRight, midY + pillH * 0.5f);
            dl.AddRectFilled(pillMin, pillMax, ImGui.GetColorU32(ThemeService.Current.Accent), pillH * 0.5f);
            dl.AddText(new Vector2(pillMin.X + pad, midY - badgeSz.Y * 0.5f), 0xFFFFFFFFu, badgeText);
        }

        if (!isLast)
        {
            dl.AddLine(new Vector2(rmin.X + Px(14f), rmax.Y), new Vector2(rmax.X - Px(14f), rmax.Y),
                ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.06f)), 1f);
        }

        return clicked;
    }

    /// <summary>The "view warnings" button — shown in the danger zone only when warnings exist.</summary>
    private void DrawWarningsButton(float winW)
    {
        var conn = _bootstrap.LastConnection;
        var total = conn?.Warnings.Length ?? 0;
        if (total == 0)
        {
            return;
        }

        var unseen = 0;
        for (int i = 0; i < conn!.Warnings.Length; i++)
        {
            if (!conn.Warnings[i].Seen)
            {
                unseen++;
            }
        }
        var label = unseen > 0
            ? Loc.T("settings.warnings_button_unseen", unseen, total)
            : Loc.T("settings.warnings_button", total);

        if (GradientMenuButton("##settWarnings", winW, label,
                FontAwesomeIcon.ExclamationTriangle, WarnTop, WarnBottom))
        {
            _view = View.WarningsList;
        }
        ImGui.Spacing();
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

    /// <summary>Full-width rounded button with a vertical gradient, a leading icon and a left-aligned label.
    /// Drawn by hand because the gradient and icon are beyond a plain <c>ImGui.Button</c>.</summary>
    private static bool GradientMenuButton(string id, float winW, string label, FontAwesomeIcon icon,
                                           Vector4 top, Vector4 bottom)
    {
        var w = winW - Px(PadX) * 2f;
        var h = Px(38f);
        ImGui.SetCursorPosX(Px(PadX));
        ImGui.InvisibleButton(id, new Vector2(w, h));
        var clicked = ImGui.IsItemClicked();
        var hovered = ImGui.IsItemHovered();
        var active = ImGui.IsItemActive();

        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var dl = ImGui.GetWindowDrawList();
        var r = Px(9f);

        var lift = active ? -0.05f : (hovered ? 0.08f : 0f);
        var topU = ImGui.ColorConvertFloat4ToU32(Shade(top, lift));
        var botU = ImGui.ColorConvertFloat4ToU32(Shade(bottom, lift));

        // Rounded top half + rounded bottom half, blended in the middle so all four corners stay rounded.
        dl.AddRectFilled(min, max, topU, r, ImDrawFlags.RoundCornersTop);
        dl.AddRectFilled(new Vector2(min.X, min.Y + h * 0.5f), max, botU, r, ImDrawFlags.RoundCornersBottom);
        dl.AddRectFilledMultiColor(
            new Vector2(min.X, min.Y + h * 0.28f), new Vector2(max.X, min.Y + h * 0.72f),
            topU, topU, botU, botU);
        // Glossy sheen, inset so it never reaches the rounded corners.
        dl.AddRectFilledMultiColor(
            new Vector2(min.X + r, min.Y + Px(1f)), new Vector2(max.X - r, min.Y + h * 0.5f),
            0x24FFFFFFu, 0x24FFFFFFu, 0x00FFFFFFu, 0x00FFFFFFu);
        dl.AddRect(min, max, ImGui.ColorConvertFloat4ToU32(Shade(top, hovered ? 0.22f : 0.10f)),
            r, ImDrawFlags.None, Px(1f));

        ImGui.PushFont(Plugin.PluginInterface.UiBuilder.FontIcon);
        var iconStr = icon.ToIconString();
        var iconSz = ImGui.CalcTextSize(iconStr);
        dl.AddText(new Vector2(min.X + Px(16f), min.Y + (h - iconSz.Y) * 0.5f), 0xFFFFFFFFu, iconStr);
        ImGui.PopFont();

        var labelSz = ImGui.CalcTextSize(label);
        dl.AddText(new Vector2(min.X + Px(44f), min.Y + (h - labelSz.Y) * 0.5f), 0xFFFFFFFFu, label);

        return clicked;
    }

    private static Vector4 Shade(Vector4 c, float d) =>
        new(Math.Clamp(c.X + d, 0f, 1f), Math.Clamp(c.Y + d, 0f, 1f), Math.Clamp(c.Z + d, 0f, 1f), c.W);

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
                _view = View.Normal;
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
                _view = View.Normal;
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

    private void DrawWarningsList(float winW)
    {
        var t = ThemeService.Current;
        var scrollH = ImGui.GetContentRegionAvail().Y;

        PushScrollbarStyle();

        using (var scroll = Dalamud.Interface.Utility.Raii.ImRaii.Child("##settWarnList", new Vector2(0f, scrollH), false))
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
            ImGui.TextColored(new Vector4(1f, 1f, 1f, 1f), Loc.T("settings.warnings_title"));
            ImGui.Spacing();
            Divider(dl, winW);

            var warnings = _bootstrap.LastConnection?.Warnings ?? [];
            if (warnings.Length == 0)
            {
                ImGui.SetCursorPosX(Px(PadX));
                ImGui.TextColored(new Vector4(0.60f, 0.60f, 0.60f, 1f),
                    Loc.T("settings.no_warnings"));
            }
            else
            {
                foreach (var w in warnings)
                {
                    ImGui.SetCursorPosX(Px(PadX));
                    ImGui.PushTextWrapPos(winW - Px(PadX));

                    var dateColor = w.Seen
                        ? new Vector4(0.45f, 0.45f, 0.45f, 1f)
                        : new Vector4(0.65f, 0.55f, 0.30f, 1f);
                    var reasonColor = w.Seen
                        ? new Vector4(0.70f, 0.70f, 0.70f, 1f)
                        : new Vector4(0.95f, 0.95f, 0.95f, 1f);

                    ImGui.TextColored(dateColor,
                        w.CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
                    ImGui.SetCursorPosX(Px(PadX));
                    ImGui.TextColored(reasonColor, w.Reason);
                    ImGui.PopTextWrapPos();

                    ImGui.Spacing();
                    ImGui.Spacing();
                }
            }

            ImGui.Spacing();
            ImGui.SetCursorPosX(Px(PadX));
            ImGui.PushStyleColor(ImGuiCol.Button, t.ButtonNormal);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, t.ButtonHovered);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, t.ButtonActive);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
            if (ImGui.Button(Loc.T("settings.back_to_settings_arrow"), new Vector2(winW - Px(PadX) * 2f, Px(32f))))
            {
                _view = View.Normal;
            }
            ImGui.PopStyleVar();
            ImGui.PopStyleColor(3);

            ImGui.Spacing();
        }
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
                    _view = View.Normal;
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
                _view = View.Normal;
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
