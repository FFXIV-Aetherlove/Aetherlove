using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove;
using AetherLove.Config;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Shared;
using AetherLove.Shared.Patreon;
using AetherLove.Shared.Profile;
using AetherLove.Shared.Sparks;
using AetherLove.UI;
using AetherLove.Widgets;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Settings;

/// <summary>The AetherOS settings app body: a categorized hub (General / Appearance / Other) with back-pill
/// sub-pages, plus the app list that hosts other apps' settings.</summary>
public sealed class SettingsScreen
{
    private readonly ISettingsHost _host;
    private readonly IAppCapabilities _caps;

    private const float PadX = 20f;

    public SettingsScreen(ISettingsHost host, IAppCapabilities caps)
    {
        _host = host;
        _caps = caps;
    }

    private enum View { Hub, General, Language, Notifications, Audio, Appearance, Wallpaper, Profile, AvatarRing, Supporter, StaffNotices, Tos, Contributors, Developer, Party }

    private View _view = View.Hub;

    /// <summary>The app whose settings page is currently hosted inside this Settings app; null shows the root.</summary>
    private IAetherApp? _openApp;
    private IOsShell? _shell;

    private bool _resetHomeOpen;
    private float _resetHomePanelH;

    private bool _supporterConfirmUnlink;
    private bool _pendingSupporterView;
    private string? _supporterReturnApp;
    private bool _linkNoMemberOpen;
    private float _linkNoMemberPanelH;
    private SupporterFlowState _lastPatreonFlowState;

    public void OnShow()
    {
        _view = View.Hub;
        _openApp = null;
        _resetHomeOpen = false;
        _devToast = null;
        _devClicks = 0;
        if (_pendingSupporterView)
        {
            _pendingSupporterView = false;
            _supporterConfirmUnlink = false;
            _host.PatreonReset();
            _view = View.Supporter;
        }
        else
        {
            _supporterReturnApp = null;
        }
        if (_pendingWallpaperView)
        {
            _pendingWallpaperView = false;
            _view = View.Wallpaper;
        }
        if (_pendingLanguageView)
        {
            _pendingLanguageView = false;
            _view = View.Language;
        }
        else
        {
            _languageReturnApp = null;
        }
    }

    private bool _pendingWallpaperView;
    private bool _pendingLanguageView;
    private string? _languageReturnApp;

    /// <summary>Deep-links the next open to the wallpaper page (a shared photo just became the wallpaper).</summary>
    public void RequestWallpaperView() => _pendingWallpaperView = true;

    /// <summary>Deep-links the next open to the languages page (a right-clicked "translation settings").
    /// <paramref name="returnApp"/> makes the page's back pill return to the text being translated.</summary>
    public void RequestLanguageView(string? returnApp = null)
    {
        _pendingLanguageView = true;
        _languageReturnApp = returnApp;
    }

    /// <summary>Deep-links the next open straight to the supporter page (from a "become a supporter" pitch).
    /// <paramref name="returnApp"/> is the sending app's id; the supporter page's back pill then returns there
    /// instead of the settings hub.</summary>
    public void RequestSupporterView(string? returnApp = null)
    {
        _pendingSupporterView = true;
        _supporterReturnApp = returnApp;
    }

    public void Draw(OsAppContext ctx)
    {
        _shell = ctx.Shell;
        // An app switched off server-side mid-session loses its row, so drop the page it may have left open.
        if (_openApp is { } hosted && !HasHostedSettings(hosted))
        {
            _openApp = null;
        }
        if (_openApp is { } open && open is IAppSettings settings)
        {
            try
            {
                settings.DrawSettings(ctx, () => _openApp = null);
            }
            catch (Exception ex)
            {
                UiHost.Log.Error(ex, $"[AetherOS] Hosted settings for '{open.Id}' failed.");
                _openApp = null;
            }
            return;
        }

        switch (_view)
        {
            case View.Hub:
                DrawHub(ctx);
                break;
            case View.General:
                DrawGeneralPage();
                break;
            case View.Language:
                DrawLanguagePage();
                break;
            case View.Audio:
                DrawAudioPage();
                break;
            case View.Notifications:
                DrawNotificationsPage();
                break;
            case View.Appearance:
                DrawAppearancePage();
                break;
            case View.Wallpaper:
                DrawWallpaperPage();
                break;
            case View.Profile:
                DrawProfilePage(ctx);
                break;
            case View.AvatarRing:
                DrawAvatarRingPage(ctx);
                break;
            case View.Supporter:
                DrawSupporterPage();
                break;
            case View.StaffNotices:
                DrawStaffNoticesPage();
                break;
            case View.Tos:
                DrawTosPage();
                break;
            case View.Contributors:
                DrawContributorsPage();
                break;
            case View.Developer:
                DrawDeveloperPage();
                break;
            case View.Party:
                DrawPartyPage();
                break;
        }
    }

    /// <summary>The Settings app's own <see cref="IAppSettings"/> body. It is only ever shown at the OS root, so
    /// <paramref name="onBack"/> is always null; it renders the same categorized hub.</summary>
    public void DrawSettingsSurface(OsAppContext ctx, Action? onBack) => Draw(ctx);

    private void DrawHub(OsAppContext ctx)
    {
        var winW = ImGui.GetWindowSize().X;
        var t = ThemeService.Current;

        var scrollH = ImGui.GetContentRegionAvail().Y;
        PushScrollbarStyle();
        using (var scroll = ImRaii.Child("##osSettings", new Vector2(0f, scrollH), false))
        {
            PopScrollbarStyle();
            if (!scroll)
            {
                return;
            }

            ImGui.Dummy(Px(0f, 12f));
            ImGui.SetCursorPosX(Px(PadX));
            using (UiFonts.H1?.Push())
            {
                ImGui.TextUnformatted(Loc.T("common.nav_settings"));
            }
            ImGui.Dummy(Px(0f, 2f));

            DrawUserCard(winW);

            GroupLabel(Loc.T("os.cat_general"));
            DrawMenuCard("osGeneral", winW, PadX, new List<MenuRow>
            {
                new(FontAwesomeIcon.Cog, t.Accent, Loc.T("os.menu_general"), 0, false, () => _view = View.General),
                new(FontAwesomeIcon.Language, t.Accent, Loc.T("settings.menu_language"), 0, false, () => _view = View.Language),
                new(FontAwesomeIcon.Bell, t.Accent, Loc.T("settings.section_notifications"), 0, false, () => _view = View.Notifications),
                new(FontAwesomeIcon.VolumeUp, t.Accent, Loc.T("os.menu_audio"), 0, false, () => _view = View.Audio),
            });

            GroupLabel(Loc.T("os.cat_appearance"));
            DrawMenuCard("osAppearance", winW, PadX, new List<MenuRow>
            {
                new(FontAwesomeIcon.Palette, t.Accent, Loc.T("os.menu_appearance"), 0, false, () => _view = View.Appearance),
                new(FontAwesomeIcon.Image, t.Accent, Loc.T("os.group_wallpaper"), 0, false, () => _view = View.Wallpaper),
                new(FontAwesomeIcon.Undo, t.Accent, Loc.T("os.menu_reset_home"), 0, false, () =>
                {
                    _resetHomeOpen = true;
                    _resetHomePanelH = 0f;
                }),
            });

            GroupLabel(Loc.T("os.cat_other"));
            DrawMenuCard("osOther", winW, PadX, new List<MenuRow>
            {
                new(FontAwesomeIcon.Envelope, t.Accent, Loc.T("settings.staff_notices_title"), _host.UnseenStaffNoticeCount, false, () =>
                {
                    _host.DismissStaffNoticeNotification();
                    _view = View.StaffNotices;
                }),
                new(FontAwesomeIcon.HandHoldingHeart, UiColors.Patreon, Loc.T("settings.menu_supporter"), 0, false, () =>
                {
                    _supporterConfirmUnlink = false;
                    _supporterReturnApp = null;
                    _host.PatreonReset();
                    _view = View.Supporter;
                }),
                new(FontAwesomeIcon.Comments, UiColors.Discord, "Discord", 0, true, () => OpenDiscord()),
                new(FontAwesomeIcon.Route, t.Accent, Loc.T("os.settings_tour"), 0, false, ctx.Shell.StartTour),
                new(FontAwesomeIcon.FileContract, t.Accent, Loc.T("settings.terms_of_service"), 0, false, () => _view = View.Tos),
                new(FontAwesomeIcon.History, t.Accent, Loc.T("settings.view_changelog"), 0, false, _host.OpenChangelog),
                new(FontAwesomeIcon.Heart, t.Accent, Loc.T("settings.contributors"), 0, false, () => _view = View.Contributors),
            });

            DrawAppsGroup(ctx, winW);

            GroupLabel(Loc.T("os.group_about"));
            DrawAboutGroup(winW);
            if (UiHost.Configuration.OsSettings.DeveloperMode)
            {
                DrawMenuCard("osDeveloper", winW, PadX, new List<MenuRow>
                {
                    new(FontAwesomeIcon.Code, t.Accent, "Developer settings", 0, false, () =>
                    {
                        _devSparks = null;
                        _devSparksError = null;
                        _view = View.Developer;
                    }),
                });
            }
            ImGui.Dummy(Px(0f, 18f));
        }

        if (_resetHomeOpen)
        {
            DrawResetHomePopup();
        }
    }

    private string _profName = "";
    private PhotoUploadDto? _profUpload;
    private string? _profPreviewPath;
    private bool _profBusy;
    private string? _profError;
    private bool _profDonePending;

    private void OpenProfileEditor()
    {
        _profName = _host.Account?.OsDisplayName ?? "";
        _profUpload = null;
        _profPreviewPath = null;
        _profBusy = false;
        _profError = null;
        _profDonePending = false;
        _view = View.Profile;
    }

    /// <summary>Processes a picked or selfie image into the avatar upload off the UI thread and writes a baked
    /// preview copy for the circle (the upload PNG already has the crop applied, so the preview needs no UVs).</summary>
    private void StageAvatar(string path, Vector4 crop)
    {
        _profBusy = true;
        _profError = null;
        _ = Task.Run(() =>
        {
            try
            {
                var upload = ReadPhotoUpload(path, crop, isNsfw: false, PhotoKind.Avatar);
                var preview = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"aetheros_avatar_{Guid.NewGuid():N}.png");
                System.IO.File.WriteAllBytes(preview, Convert.FromBase64String(upload.Base64));
                _profUpload = upload;
                _profPreviewPath = preview;
            }
            catch (Exception ex)
            {
                _profError = HubErrorText.Localize(ex);
                UiHost.Log.Warning(ex, "[Settings] Avatar staging failed.");
            }
            finally
            {
                _profBusy = false;
            }
        });
    }

    /// <summary>The in-app OS profile editor: avatar (choose photo / selfie) + display name, mirrored from the
    /// OS onboarding's profile step.</summary>
    private void DrawProfilePage(OsAppContext ctx)
    {
        if (_profDonePending)
        {
            _profDonePending = false;
            _view = View.Hub;
            return;
        }
        DrawSubpageBack();
        DrawSubpageHeading(Loc.T("os.profile_edit_title"), PadX);

        var scrollH = ImGui.GetContentRegionAvail().Y;
        PushScrollbarStyle();
        using var scroll = ImRaii.Child("##osSettProfile", new Vector2(0f, scrollH), false);
        PopScrollbarStyle();
        if (!scroll)
        {
            return;
        }

        var t = ThemeService.Current;
        var winW = ImGui.GetWindowSize().X;
        var dl = ImGui.GetWindowDrawList();

        ImGui.Dummy(Px(0f, 12f));
        var r = Px(46f);
        var center = new Vector2(ImGui.GetWindowPos().X + winW * 0.5f, ImGui.GetCursorScreenPos().Y + r);
        var previewTex = _profPreviewPath is { } previewPath ? _caps.Textures.Get(previewPath) : null;
        if (previewTex is { } staged)
        {
            dl.AddImageRounded(staged, center - new Vector2(r, r), center + new Vector2(r, r),
                Vector2.Zero, Vector2.One, 0xFFFFFFFFu, r, ImDrawFlags.RoundCornersAll);
            dl.AddCircle(center, r, t.AccentU32, 64, Px(2.2f));
        }
        else if (_host.OsAvatar?.GetWrapOrDefault() is { } wrap)
        {
            dl.AddImageRounded(wrap.Handle, center - new Vector2(r, r), center + new Vector2(r, r),
                Vector2.Zero, Vector2.One, 0xFFFFFFFFu, r, ImDrawFlags.RoundCornersAll);
            dl.AddCircle(center, r, OsDrawShared.White(0.20f), 64, Px(1.5f));
            AvatarRings.Draw(dl, center, r, _host.EquippedFrameRef);
        }
        else
        {
            dl.AddCircleFilled(center, r, OsDrawShared.White(0.06f), 64);
            dl.AddCircle(center, r, ImGui.ColorConvertFloat4ToU32(t.Accent with { W = 0.55f }), 64, Px(2f));
            IconDraw.AddCentered(dl, FontAwesomeIcon.Camera, Px(24f), center,
                ImGui.ColorConvertFloat4ToU32(t.AccentLight));
        }
        ImGui.SetCursorScreenPos(new Vector2(ImGui.GetWindowPos().X, center.Y + r + Px(12f)));

        var chooseLabel = Loc.T("os_onboarding.avatar_choose");
        var selfieLabel = Loc.T("common.selfie");
        var chooseW = ImGui.CalcTextSize(chooseLabel).X + Px(26f);
        var selfieW = ImGui.CalcTextSize(selfieLabel).X + Px(26f);
        var gap = Px(8f);
        ImGui.SetCursorPosX((winW - chooseW - selfieW - gap) * 0.5f);
        PushThemeButton(t);
        ImGui.BeginDisabled(_profBusy);
        if (SharedUiHelpers.Button($"{chooseLabel}##osProfPick", new Vector2(chooseW, Px(32f))))
        {
            _caps.Images.PickAndCrop(new ImageCropRequest(
                    Loc.T("onboarding.select_photo"),
                    $"{Loc.T("onboarding.image_files")}{{.png,.jpg,.jpeg,.bmp,.webp}}",
                    Loc.T("onboarding.crop_avatar"),
                    1.0f, PhotoSpec.AvatarSize, PhotoSpec.AvatarSize),
                picked => StageAvatar(picked.Path, picked.Crop));
        }
        ImGui.SameLine(0f, gap);
        if (SharedUiHelpers.Button($"{selfieLabel}##osProfSelfie", new Vector2(selfieW, Px(32f))))
        {
            _caps.Camera.Capture(new CameraRequest(1f, PhotoSpec.AvatarSize),
                shot => StageAvatar(shot.Path, shot.Crop));
        }
        ImGui.EndDisabled();
        PopThemeButton();

        ImGui.Dummy(Px(0f, 16f));
        ImGui.SetCursorPosX(Px(PadX));
        ImGui.TextColored(UiColors.Subtle, Loc.T("os_onboarding.name_hint"));
        ImGui.SetCursorPosX(Px(PadX));
        ImGui.SetNextItemWidth(winW - Px(PadX) * 2f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(9f));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(Px(10f), Px(8f)));
        ImGui.InputTextWithHint("##osProfName", Loc.T("os_onboarding.name_hint"), ref _profName, 32);
        ImGui.PopStyleVar(2);

        ImGui.Dummy(Px(0f, 10f));
        ImGui.SetCursorPosX(Px(PadX));
        if (SharedUiHelpers.Button($"{Loc.T("rings.section_title")}##osProfRing", new Vector2(winW - Px(PadX) * 2f, Px(32f))))
        {
            OpenRingPicker();
        }

        if (_profError is { } err)
        {
            ImGui.Dummy(Px(0f, 6f));
            ImGui.SetCursorPosX(Px(PadX));
            ImGui.PushTextWrapPos(winW - Px(PadX));
            ImGui.TextColored(UiColors.Danger, err);
            ImGui.PopTextWrapPos();
        }

        ImGui.Dummy(Px(0f, 14f));
        ImGui.SetCursorPosX(Px(PadX));
        PushThemeButton(t);
        ImGui.BeginDisabled(_profBusy || _profName.Trim().Length == 0);
        if (SharedUiHelpers.Button($"{Loc.T("os.profile_save")}##osProfSave", new Vector2(winW - Px(PadX) * 2f, Px(34f))))
        {
            _profBusy = true;
            _profError = null;
            var name = _profName;
            var upload = _profUpload;
            _ = Task.Run(async () =>
            {
                try
                {
                    await _host.SaveOsProfileAsync(name, upload).ConfigureAwait(false);
                    _profDonePending = true;
                }
                catch (Exception ex)
                {
                    _profError = HubErrorText.Localize(ex);
                    UiHost.Log.Warning(ex, "[Settings] OS profile save failed.");
                }
                finally
                {
                    _profBusy = false;
                }
            });
        }
        ImGui.EndDisabled();
        PopThemeButton();
        ImGui.Dummy(Px(0f, 14f));
    }

    private readonly AetherLove.Widgets.RingPickerUi _ringPicker = new();

    private void OpenRingPicker()
    {
        _ringPicker.Open(_host.EquippedFrameRef);
        _view = View.AvatarRing;
        _ = Task.Run(async () =>
        {
            try
            {
                _ringPicker.SetOwned(await _host.GetOwnedRingsAsync().ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                _ringPicker.FailLoad(HubErrorText.Localize(ex));
                UiHost.Log.Warning(ex, "[Settings] GetOwnedRingsAsync failed.");
            }
        });
    }

    private void DrawAvatarRingPage(OsAppContext ctx)
    {
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.SetCursorPosX(Px(PadX));
        if (DrawFloatingBackPill(ImGui.GetCursorScreenPos(), Loc.T("settings.back_arrow"), FontAwesomeIcon.User))
        {
            _view = View.Profile;
        }
        ImGui.Spacing();
        DrawSubpageHeading(Loc.T("rings.picker_title"), PadX);

        PushScrollbarStyle();
        using var scroll = ImRaii.Child("##osSettRing", ImGui.GetContentRegionAvail(), false);
        PopScrollbarStyle();
        if (!scroll)
        {
            return;
        }
        var winW = ImGui.GetWindowSize().X;
        _ringPicker.Draw(_host.OsAvatar, winW, Px(PadX),
            selected =>
            {
                _ringPicker.BeginSave();
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _host.SetAvatarRingAsync(selected).ConfigureAwait(false);
                        _ringPicker.NotifySaved();
                    }
                    catch (Exception ex)
                    {
                        _ringPicker.NotifyError(HubErrorText.Localize(ex));
                        UiHost.Log.Warning(ex, "[Settings] SetAvatarRingAsync failed.");
                    }
                });
            },
            () => ctx.Shell.SendIntent("store",
                AetherOS.Sdk.OsIntents.CreatePath(AetherOS.Sdk.OsIntents.StoreOpen, "avatar-packs")));
        ImGui.Dummy(Px(0f, 14f));
    }

    /// <summary>Account card: the OS avatar (with a supporter star badge) + display name, plus an Edit button that
    /// opens the in-app profile editor.</summary>
    private void DrawUserCard(float winW)
    {
        if (_host.Account is not { } account)
        {
            return;
        }
        var name = string.IsNullOrWhiteSpace(account.OsDisplayName) ? "AetherOS" : account.OsDisplayName;

        var startY = ImGui.GetCursorPosY();
        var dl = ImGui.GetWindowDrawList();
        var pad = Px(PadX);
        var cardTL = ImGui.GetCursorScreenPos() + new Vector2(pad, Px(4f));
        var cardW = winW - pad * 2f;
        var cardH = Px(72f);
        var cardBR = cardTL + new Vector2(cardW, cardH);
        dl.AddRectFilled(cardTL, cardBR, OsDrawShared.White(0.06f), Px(14f));

        var avR = Px(26f);
        var avC = new Vector2(cardTL.X + Px(16f) + avR, (cardTL.Y + cardBR.Y) * 0.5f);
        var tex = _host.OsAvatar?.GetWrapOrDefault();
        if (tex != null)
        {
            dl.AddImageRounded(tex.Handle, avC - new Vector2(avR, avR), avC + new Vector2(avR, avR),
                Vector2.Zero, Vector2.One, 0xFFFFFFFF, avR);
        }
        else
        {
            dl.AddCircleFilled(avC, avR, ThemeService.Current.AccentU32, 32);
            var initial = char.ToUpperInvariant(name[0]).ToString();
            dl.AddText(avC - ImGui.CalcTextSize(initial) * 0.5f, 0xFFFFFFFF, initial);
        }
        dl.AddCircle(avC, avR, OsDrawShared.White(0.18f), 32, 1.5f);
        AvatarRings.Draw(dl, avC, avR, _host.EquippedFrameRef);

        if (_host.IsSupporter)
        {
            var badgeCenter = avC + new Vector2(avR * 0.72f, -avR * 0.72f);
            var badgeR = Px(8f);
            dl.AddCircleFilled(badgeCenter, badgeR, 0xFF1E1E24u, 24);
            dl.AddCircle(badgeCenter, badgeR, UiColors.FavoriteStar, 24, Px(1.2f));
            IconDraw.AddCentered(dl, FontAwesomeIcon.Star, badgeR * 1.2f, badgeCenter + new Vector2(0f, Px(0.5f)),
                UiColors.FavoriteStar);
        }

        var editLabel = Loc.T("os.edit_profile");
        var editW = ImGui.CalcTextSize(editLabel).X + Px(20f);
        var editH = Px(26f);
        var editTL = new Vector2(cardBR.X - Px(12f) - editW, (cardTL.Y + cardBR.Y) * 0.5f - editH * 0.5f);

        var textX = avC.X + avR + Px(14f);
        using (UiFonts.H3?.Push())
        {
            var nameH = ImGui.GetTextLineHeight();
            dl.PushClipRect(new Vector2(textX, cardTL.Y), new Vector2(editTL.X - Px(8f), cardBR.Y), true);
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
                new Vector2(textX, (cardTL.Y + cardBR.Y) * 0.5f - nameH * 0.5f), OsDrawShared.White(0.95f), name);
            dl.PopClipRect();
        }

        ImGui.SetCursorScreenPos(editTL);
        PushThemeButton(ThemeService.Current);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
        if (SharedUiHelpers.Button($"{editLabel}##editOsProfile", new Vector2(editW, editH)))
        {
            OpenProfileEditor();
        }
        ImGui.PopStyleVar();
        PopThemeButton();

        ImGui.SetCursorPosY(startY + cardH + Px(10f));
    }

    private void DrawGeneralPage()
    {
        DrawSubpageBack();
        DrawSubpageHeading(Loc.T("os.menu_general"), PadX);

        var scrollH = ImGui.GetContentRegionAvail().Y;
        PushScrollbarStyle();
        using var scroll = ImRaii.Child("##osSettGeneral", new Vector2(0f, scrollH), false);
        PopScrollbarStyle();
        if (!scroll)
        {
            return;
        }

        var os = UiHost.Configuration.OsSettings;
        ImGui.Spacing();
        SettingCheckbox(PadX, Loc.T("settings.confirm_before_close"),
            () => !os.SkipCloseConfirmation, v => os.SkipCloseConfirmation = !v);
        SettingCheckbox(PadX, Loc.T("settings.auto_open_minimized"),
            () => os.AutoOpenMinimizedOnLogin, v => os.AutoOpenMinimizedOnLogin = v);
        // Only offered once the user has actually run the battery flat; there is no point advertising the way out
        // of a joke nobody has seen.
        if (UiHost.Configuration.Os.BatteryEmptySeen)
        {
            SettingCheckbox(PadX, Loc.T("os.settings_hide_battery_grass"),
                () => UiHost.Configuration.Os.HideBatteryGrassPrompt,
                v => UiHost.Configuration.Os.HideBatteryGrassPrompt = v);
        }

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
            if (ImGui.RadioButton(label, os.CombatBehavior == value))
            {
                os.CombatBehavior = value;
                UiHost.Configuration.Save();
            }
        }

        ImGui.Spacing();
        SettingCheckbox(PadX, Loc.T("settings.show_during_gpose"),
            () => os.ShowDuringGpose,
            v =>
            {
                os.ShowDuringGpose = v;
                UiHost.PluginInterface.UiBuilder.DisableGposeUiHide = v;
            });
        ImGui.SameLine();
        HelpMarker(Loc.T("settings.show_during_gpose_tooltip"));

        SettingCheckbox(PadX, Loc.T("settings.hide_during_cutscene"),
            () => !os.HideDuringCutscenes,
            v =>
            {
                os.HideDuringCutscenes = !v;
                UiHost.PluginInterface.UiBuilder.DisableCutsceneUiHide = v;
            });
        ImGui.SameLine();
        HelpMarker(Loc.T("settings.hide_during_cutscene_tooltip"));

        SettingCheckbox(PadX, Loc.T("settings.tomestone_emote"),
            () => os.ShowTomestoneEmote, v => os.ShowTomestoneEmote = v);
        ImGui.SameLine();
        HelpMarker(Loc.T("settings.tomestone_emote_tooltip"));
        ImGui.Spacing();
    }

    private void DrawLanguagePage()
    {
        DrawLanguageBack();
        DrawSubpageHeading(Loc.T("settings.menu_language"), PadX);

        var scrollH = ImGui.GetContentRegionAvail().Y;
        PushScrollbarStyle();
        using var scroll = ImRaii.Child("##osSettLang", new Vector2(0f, scrollH), false);
        PopScrollbarStyle();
        if (!scroll)
        {
            return;
        }

        var winW = ImGui.GetWindowSize().X;
        ImGui.Spacing();
        DrawSectionHeader(Loc.T("settings.section_plugin_language"), PadX);
        ImGui.Spacing();
        DrawLanguagePills(winW);

        ImGui.Spacing();
        ImGui.Spacing();
        DrawSectionHeader(Loc.T("settings.section_translation"), PadX);
        ImGui.Spacing();
        DrawTranslationSection(winW);

        ImGui.Spacing();
        ImGui.Spacing();
        DrawSectionHeader(Loc.T("settings.section_time_format"), PadX);
        ImGui.Spacing();
        DrawTimeFormatRow(true, Loc.T("settings.time_24h"));
        DrawTimeFormatRow(false, Loc.T("settings.time_12h"));
        ImGui.Spacing();
    }

    /// <summary>The languages page's back pill: a "translation settings" deep link that carried a return
    /// app goes back to that app (warm resume restores the translatable, back in context); a hub-opened
    /// page goes to the hub.</summary>
    private void DrawLanguageBack()
    {
        if (_languageReturnApp is not { } returnApp || _shell is null)
        {
            DrawSubpageBack();
            return;
        }
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.SetCursorPosX(Px(PadX));
        var destIcon = FontAwesomeIcon.Home;
        foreach (var app in _shell.Apps)
        {
            if (app.Id == returnApp)
            {
                destIcon = app.Icon;
                break;
            }
        }
        if (DrawFloatingBackPill(ImGui.GetCursorScreenPos(), Loc.T("settings.back_arrow"), destIcon))
        {
            _languageReturnApp = null;
            _view = View.Hub;
            _shell.OpenApp(returnApp);
        }
        ImGui.Spacing();
    }

    private string _translationLangFilter = string.Empty;

    /// <summary>The translation opt-in and the target language, one searchable dropdown over every
    /// language the backend takes. The explainer under the toggle is the same consent text the inline
    /// popup shows, so the setting and the popup describe one identical bargain.</summary>
    private void DrawTranslationSection(float winW)
    {
        var os = UiHost.Configuration.OsSettings;

        ImGui.SetCursorPosX(Px(PadX));
        var enabled = os.TranslationsEnabled;
        if (ImGui.Checkbox(Loc.T("settings.translation_enable"), ref enabled))
        {
            os.TranslationsEnabled = enabled;
            UiHost.Configuration.Save();
        }
        SharedUiHelpers.HandOnHover();
        ImGui.SetCursorPosX(Px(PadX));
        ImGui.PushTextWrapPos(winW - Px(PadX));
        ImGui.TextColored(new Vector4(0.60f, 0.60f, 0.60f, 1f), Loc.T("os.translate_consent_body"));
        ImGui.PopTextWrapPos();
        ImGui.Spacing();

        ImGui.SetCursorPosX(Px(PadX));
        ImGui.TextColored(new Vector4(0.85f, 0.85f, 0.85f, 1f), Loc.T("settings.translation_language"));
        ImGui.SetCursorPosX(Px(PadX));
        var current = AetherLove.Services.Translation.TranslationLanguages.DisplayName(os.TranslationLanguage);
        ImGui.SetNextItemWidth(winW - Px(PadX) * 2f);
        if (!enabled)
        {
            ImGui.BeginDisabled();
        }
        if (ImGui.BeginCombo("##trLangPick", current, ImGuiComboFlags.HeightLarge))
        {
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            if (ImGui.IsWindowAppearing())
            {
                _translationLangFilter = string.Empty;
                ImGui.SetKeyboardFocusHere();
            }
            ImGui.InputTextWithHint("##trLangFilter", Loc.T("settings.translation_search"),
                ref _translationLangFilter, 40);
            ImGui.Separator();
            var filter = _translationLangFilter.Trim();
            using (var list = ImRaii.Child("##trLangList", new Vector2(0f, Px(220f)), false))
            {
                if (list)
                {
                    foreach (var language in AetherLove.Services.Translation.TranslationLanguages.Renderable)
                    {
                        if (filter.Length > 0 && !language.Matches(filter))
                        {
                            continue;
                        }
                        var selected = language.Code.Equals(os.TranslationLanguage, StringComparison.OrdinalIgnoreCase);
                        if (ImGui.Selectable($"{language.NativeName}##trLang{language.Code}", selected))
                        {
                            os.TranslationLanguage = language.Code;
                            UiHost.Configuration.Save();
                            ImGui.CloseCurrentPopup();
                        }
                        SharedUiHelpers.HandOnHover();
                        if (!string.Equals(language.NativeName, language.EnglishName, StringComparison.Ordinal))
                        {
                            ImGui.SameLine();
                            ImGui.TextColored(new Vector4(0.55f, 0.55f, 0.55f, 1f), language.EnglishName);
                        }
                    }
                }
            }
            ImGui.EndCombo();
        }
        SharedUiHelpers.HandOnHover();
        if (!enabled)
        {
            ImGui.EndDisabled();
        }
    }

    private static void DrawTimeFormatRow(bool is24h, string label)
    {
        var os = UiHost.Configuration.OsSettings;
        ImGui.SetCursorPosX(Px(PadX));
        if (ImGui.RadioButton($"{label}##timefmt{(is24h ? 24 : 12)}", os.Use24HourClock == is24h))
        {
            os.Use24HourClock = is24h;
            UiHost.Configuration.Save();
        }
        SharedUiHelpers.HandOnHover();
        ImGui.SameLine();
        ImGui.TextColored(new Vector4(0.60f, 0.60f, 0.60f, 1f), OsClock.Format(DateTime.Now, is24h));
    }

    private void DrawNotificationsPage()
    {
        DrawSubpageBack();
        DrawSubpageHeading(Loc.T("settings.section_notifications"), PadX);

        var scrollH = ImGui.GetContentRegionAvail().Y;
        PushScrollbarStyle();
        using var scroll = ImRaii.Child("##osSettNotifs", new Vector2(0f, scrollH), false);
        PopScrollbarStyle();
        if (!scroll)
        {
            return;
        }

        var winW = ImGui.GetWindowSize().X;
        var os = UiHost.Configuration.OsSettings;
        ImGui.Spacing();
        SettingCheckbox(PadX, Loc.T("settings.enable_notifications"),
            () => os.EnableNotifications, v => os.EnableNotifications = v);
        ImGui.SameLine();
        HelpMarker(Loc.T("settings.enable_notifications_tooltip"));
        ImGui.Spacing();

        ImGui.BeginDisabled(!os.EnableNotifications);
        SettingCheckbox(PadX, Loc.T("settings.enable_notification_sounds"),
            () => os.EnableNotificationSounds, v => os.EnableNotificationSounds = v);
        ImGui.SameLine();
        HelpMarker(Loc.T("settings.enable_notification_sounds_tooltip"));
        ImGui.Spacing();

        SettingCheckbox(PadX, Loc.T("settings.hide_notifications_in_combat"),
            () => os.HideNotificationsDuringCombat, v => os.HideNotificationsDuringCombat = v);
        ImGui.SameLine();
        HelpMarker(Loc.T("settings.hide_notifications_in_combat_tooltip"));
        ImGui.EndDisabled();
        ImGui.Spacing();

        SettingCheckbox(PadX, Loc.T("settings.show_dtr_count"),
            () => UiHost.Configuration.EnableDtrEntry, v => UiHost.Configuration.EnableDtrEntry = v);
        ImGui.SameLine();
        HelpMarker(Loc.T("settings.show_dtr_count_tooltip"));
        ImGui.Spacing();
    }

    /// <summary>AetherParty's own settings. The party lives in the shell rather than in an app, so this
    /// reaches it through the host bridge; the same switches are on the widget's cog.</summary>
    private void DrawPartyPage()
    {
        DrawSubpageBack();
        DrawSubpageHeading(Loc.T("os.party_settings"), PadX);

        var scrollH = ImGui.GetContentRegionAvail().Y;
        PushScrollbarStyle();
        using var scroll = ImRaii.Child("##osSettParty", new Vector2(0f, scrollH), false);
        PopScrollbarStyle();
        if (!scroll)
        {
            return;
        }

        var winW = ImGui.GetWindowSize().X;
        ImGui.Spacing();
        SettingCheckbox(PadX, Loc.T("os.party_intro_pets_show"),
            () => _host.ShowPartyPets, v => _host.ShowPartyPets = v);
        ImGui.SameLine();
        HelpMarker(Loc.T("os.party_intro_pets_show_hint"));
        ImGui.Spacing();

        if (_host.HasPet)
        {
            SettingCheckbox(PadX, Loc.T("os.party_intro_pets_share"),
                () => _host.ShareMyPet, v => _host.ShareMyPet = v);
            ImGui.SameLine();
            HelpMarker(Loc.T("os.party_intro_pets_share_hint"));
            ImGui.Spacing();
        }

        if (!_host.ShowPartyPets || _host.PartyPetSizeCount <= 0)
        {
            return;
        }
        ImGui.Spacing();
        ImGui.SetCursorPosX(Px(PadX));
        ImGui.TextColored(new Vector4(0.70f, 0.70f, 0.70f, 1f), Loc.T("os.party_intro_pets_size"));
        ImGui.SetCursorPosX(Px(PadX));
        ImGui.SetNextItemWidth(winW - Px(PadX) * 2f);
        var size = _host.PartyPetSize;
        if (ImGui.SliderInt("##partyPetSize", ref size, 0, _host.PartyPetSizeCount - 1,
                Loc.T($"os.party_pet_size_{Math.Clamp(size, 0, _host.PartyPetSizeCount - 1)}")))
        {
            _host.PartyPetSize = size;
        }
        ImGui.Spacing();
    }

    private string? _audioTestResult;

    private void DrawAudioPage()
    {
        DrawSubpageBack();
        DrawSubpageHeading(Loc.T("os.menu_audio"), PadX);

        var scrollH = ImGui.GetContentRegionAvail().Y;
        PushScrollbarStyle();
        using var scroll = ImRaii.Child("##osSettAudio", new Vector2(0f, scrollH), false);
        PopScrollbarStyle();
        if (!scroll)
        {
            return;
        }

        var winW = ImGui.GetWindowSize().X;
        var os = UiHost.Configuration.OsSettings;
        ImGui.Spacing();

        ImGui.SetCursorPosX(Px(PadX));
        ImGui.TextColored(new Vector4(0.70f, 0.70f, 0.70f, 1f), Loc.T("settings.audio_volume"));
        ImGui.SetCursorPosX(Px(PadX));
        ImGui.SetNextItemWidth(winW - Px(PadX) * 2f);
        var volumePct = (int)MathF.Round(os.NotificationVolume * 100f);
        if (ImGui.SliderInt("##audioVolume", ref volumePct, 0, 100, "%d%%"))
        {
            os.NotificationVolume = volumePct / 100f;
        }
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            UiHost.Configuration.Save();
            NotificationSoundPlayer.Play(os.NotificationSoundChoice);
        }
        ImGui.Spacing();

        ImGui.SetCursorPosX(Px(PadX));
        ImGui.TextColored(new Vector4(0.70f, 0.70f, 0.70f, 1f), Loc.T("settings.audio_device"));
        ImGui.SetCursorPosX(Px(PadX));
        ImGui.SetNextItemWidth(winW - Px(PadX) * 2f);
        var configured = os.AudioOutputDevice;
        var deviceLabel = configured.Length == 0 ? Loc.T("settings.audio_device_default") : configured;
        if (ImGui.BeginCombo("##audioDevice", deviceLabel))
        {
            if (ImGui.Selectable(Loc.T("settings.audio_device_default"), configured.Length == 0))
            {
                os.AudioOutputDevice = string.Empty;
                UiHost.Configuration.Save();
            }
            SharedUiHelpers.HandOnHover();
            foreach (var name in NotificationSoundPlayer.ListDevices())
            {
                if (ImGui.Selectable(name, name == configured))
                {
                    os.AudioOutputDevice = name;
                    UiHost.Configuration.Save();
                }
                SharedUiHelpers.HandOnHover();
            }
            ImGui.EndCombo();
        }
        ImGui.Spacing();

        if (!os.EnableNotifications || !os.EnableNotificationSounds)
        {
            ImGui.SetCursorPosX(Px(PadX));
            ImGui.PushTextWrapPos(winW - Px(PadX));
            ImGui.TextColored(UiColors.ReviewOrange, Loc.T("settings.audio_sounds_off"));
            ImGui.PopTextWrapPos();
            ImGui.SetCursorPosX(Px(PadX));
            PushThemeButton(ThemeService.Current);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
            if (SharedUiHelpers.Button($"{Loc.T("settings.audio_sounds_enable")}##audioEnable", new Vector2(winW - Px(PadX) * 2f, Px(28f))))
            {
                os.EnableNotifications = true;
                os.EnableNotificationSounds = true;
                UiHost.Configuration.Save();
            }
            ImGui.PopStyleVar();
            PopThemeButton();
            ImGui.Spacing();
        }

        DrawNotificationSoundCombo(winW);
        ImGui.Spacing();

        ImGui.SetCursorPosX(Px(PadX));
        PushThemeButton(ThemeService.Current);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
        if (SharedUiHelpers.Button($"{Loc.T("settings.audio_test")}##audioTest", new Vector2(winW - Px(PadX) * 2f, Px(32f))))
        {
            _audioTestResult = NotificationSoundPlayer.TryPlay(os.NotificationSoundChoice)
                ?? Loc.T("settings.audio_test_ok");
        }
        ImGui.PopStyleVar();
        PopThemeButton();

        if (_audioTestResult is { } result)
        {
            ImGui.Spacing();
            ImGui.SetCursorPosX(Px(PadX));
            ImGui.PushTextWrapPos(winW - Px(PadX));
            ImGui.TextColored(UiColors.Body, result);
            ImGui.PopTextWrapPos();
        }
        ImGui.Spacing();
    }

    private static void DrawNotificationSoundCombo(float winW)
    {
        var os = UiHost.Configuration.OsSettings;

        ImGui.SetCursorPosX(Px(PadX));
        ImGui.TextColored(new Vector4(0.70f, 0.70f, 0.70f, 1f), Loc.T("settings.notification_sound"));

        var playBtnW = Px(34f);
        var gap = Px(6f);

        ImGui.SetCursorPosX(Px(PadX));
        ImGui.SetNextItemWidth(winW - Px(PadX) * 2f - playBtnW - gap);
        var current = os.NotificationSoundChoice;
        if (ImGui.BeginCombo("##notifSound", current.DisplayName()))
        {
            foreach (var sound in Enum.GetValues<NotificationSound>())
            {
                var selected = sound == current;
                if (ImGui.Selectable(sound.DisplayName(), selected))
                {
                    os.NotificationSoundChoice = sound;
                    UiHost.Configuration.Save();
                }
                SharedUiHelpers.HandOnHover();
                if (selected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }
            ImGui.EndCombo();
        }

        ImGui.SameLine(0f, gap);
        ImGui.PushFont(UiHost.PluginInterface.UiBuilder.FontIcon);
        var play = SharedUiHelpers.Button($"{FontAwesomeIcon.Play.ToIconString()}##playNotifSound", new Vector2(playBtnW, 0f));
        ImGui.PopFont();
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("settings.play"));
        }
        if (play)
        {
            NotificationSoundPlayer.Play(os.NotificationSoundChoice);
        }
    }

    private void DrawAppearancePage()
    {
        DrawSubpageBack();
        DrawSubpageHeading(Loc.T("os.menu_appearance"), PadX);

        var scrollH = ImGui.GetContentRegionAvail().Y;
        PushScrollbarStyle();
        using var scroll = ImRaii.Child("##osSettAppearance", new Vector2(0f, scrollH), false);
        PopScrollbarStyle();
        if (!scroll)
        {
            return;
        }

        var winW = ImGui.GetWindowSize().X;
        var t = ThemeService.Current;
        ImGui.Spacing();
        AppearancePicker.DrawThemeCards(winW, PadX);
        DrawPremiumThemes(winW);
        ImGui.Spacing();
        ImGui.Spacing();
        DrawSubpageHeading(Loc.T("settings.font_header"), PadX);
        AppearancePicker.DrawFontCards(winW, PadX, t);
        ImGui.Spacing();
        ImGui.Spacing();
        DrawSubpageHeading(Loc.T("settings.phone_size_header"), PadX);
        AppearancePicker.DrawPhoneSizeButtons(winW, PadX, t);
        ImGui.Spacing();
        ImGui.Spacing();
        DrawSubpageHeading(Loc.T("settings.mini_phone_size_header"), PadX);
        AppearancePicker.DrawMiniSizeButtons(winW, PadX, t);
        ImGui.Spacing();
        AppearancePicker.DrawMiniSizePreview(winW, PadX);
        ImGui.Spacing();
        DrawLockRow();
        ImGui.Spacing();
    }

    private enum PremiumThemeState { Busy, Failed }

    private AetherLove.Shared.Store.OwnedThemeDto[] _ownedThemes = [];
    private bool _ownedThemesRequested;

    // Written from the enable task, read by the draw thread.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, PremiumThemeState> _themeStates = new();

    private void EnsureOwnedThemes()
    {
        if (_ownedThemesRequested)
        {
            return;
        }
        _ownedThemesRequested = true;
        _ = Task.Run(async () =>
        {
            if (await _host.GetOwnedThemesAsync().ConfigureAwait(false) is { } themes)
            {
                _ownedThemes = themes;
            }
        });
    }

    /// <summary>The themes the account bought, in their own shelf under the built-ins, each badged with the
    /// spark coin. Hidden entirely for an account that owns none.</summary>
    private void DrawPremiumThemes(float winW)
    {
        EnsureOwnedThemes();
        var owned = _ownedThemes;
        if (owned.Length == 0)
        {
            return;
        }
        ImGui.Spacing();
        DrawSubpageHeading(Loc.T("settings.premium_themes"), PadX);

        var dl = ImGui.GetWindowDrawList();
        var cardH = Px(66f);
        var swatchH = Px(28f);
        var gap = Px(8f);
        var rounding = Px(8f);
        const int cols = 2;
        var cardW = (winW - Px(PadX) * 2f - gap * (cols - 1)) / cols;

        ImGui.SetCursorPosX(Px(PadX));
        var originLocal = ImGui.GetCursorPos();
        var originScreen = ImGui.GetCursorScreenPos();

        for (var i = 0; i < owned.Length; i++)
        {
            var theme = owned[i];
            var selected = _host.ActivePremiumThemeId == theme.ProductId;
            var state = _themeStates.TryGetValue(theme.ProductId, out var s) ? s : (PremiumThemeState?)null;
            var busy = state == PremiumThemeState.Busy;
            var offset = new Vector2((i % cols) * (cardW + gap), (i / cols) * (cardH + gap));
            var tl = originScreen + offset;
            var br = tl + new Vector2(cardW, cardH);

            ImGui.SetCursorPos(originLocal + offset);
            ImGui.InvisibleButton($"##premiumTheme{i}", new Vector2(cardW, cardH));
            if (ImGui.IsItemClicked() && !busy)
            {
                EnablePremiumTheme(theme.ProductId);
            }
            var hovered = ImGui.IsItemHovered();
            if (hovered && !busy)
            {
                SharedUiHelpers.HandOnHover();
            }
            if (ImGui.BeginPopupContextItem($"##premiumThemeCtx{i}", ImGuiPopupFlags.MouseButtonRight))
            {
                ImGui.TextDisabled(ThemeName(theme));
                ImGui.Separator();
                if (SharedUiHelpers.DrawIconMenuItem(FontAwesomeIcon.SyncAlt, Loc.T("settings.premium_refresh"))
                    && !busy)
                {
                    ImGui.CloseCurrentPopup();
                    RefreshPremiumTheme(theme.ProductId);
                }
                ImGui.EndPopup();
            }

            var accent = ImGui.ColorConvertU32ToFloat4(Rgba(theme.Colors.Accent));
            dl.AddRectFilled(tl, br,
                ImGui.ColorConvertFloat4ToU32(accent with { W = selected ? 0.28f : (hovered ? 0.14f : 0.07f) }),
                rounding);
            var swatchBR = new Vector2(br.X, tl.Y + swatchH);
            dl.AddRectFilledMultiColor(tl, swatchBR, Rgba(theme.Colors.Accent), Rgba(theme.Colors.SecondaryEnd),
                Rgba(theme.Colors.SecondaryEnd), Rgba(theme.Colors.Accent));
            dl.AddRect(tl, br,
                ImGui.ColorConvertFloat4ToU32(accent with { W = selected ? 1f : (hovered ? 0.55f : 0.28f) }),
                rounding, ImDrawFlags.None, selected ? 2f : 1f);
            SparkIcon.Draw(dl, tl + new Vector2(cardW - Px(13f), Px(13f)), Px(14f));

            if (busy)
            {
                LoadingSpinner.Draw(new Vector2((tl.X + br.X) * 0.5f, tl.Y + swatchH + (cardH - swatchH) * 0.5f),
                    Px(8f), Px(2f), ImGui.ColorConvertFloat4ToU32(accent));
            }
            else
            {
                var name = state == PremiumThemeState.Failed
                    ? Loc.T("settings.premium_enable_failed")
                    : ThemeName(theme);
                var nameSz = ImGui.CalcTextSize(name);
                var nameY = tl.Y + swatchH + (cardH - swatchH - nameSz.Y) * 0.5f;
                dl.AddText(new Vector2(tl.X + (cardW - nameSz.X) * 0.5f, nameY),
                    OsDrawShared.White(selected ? 1f : (hovered ? 0.85f : 0.62f)), name);
            }
        }

        var rows = (owned.Length + cols - 1) / cols;
        ImGui.SetCursorPos(new Vector2(originLocal.X, originLocal.Y + rows * cardH + (rows - 1) * gap + Px(6f)));
    }

    /// <summary>Re-pulls a theme's assets. The owned list is re-fetched too, so a renamed theme shows its
    /// new name in the same beat.</summary>
    private void RefreshPremiumTheme(Guid productId)
    {
        _themeStates[productId] = PremiumThemeState.Busy;
        _ = Task.Run(async () =>
        {
            var ok = await _host.RefreshPremiumThemeAsync(productId).ConfigureAwait(false);
            if (ok)
            {
                _themeStates.TryRemove(productId, out _);
                _ownedThemesRequested = false;
            }
            else
            {
                _themeStates[productId] = PremiumThemeState.Failed;
            }
        });
    }

    private void EnablePremiumTheme(Guid productId)
    {
        _themeStates[productId] = PremiumThemeState.Busy;
        _ = Task.Run(async () =>
        {
            var ok = await _host.EnablePremiumThemeAsync(productId).ConfigureAwait(false);
            if (ok)
            {
                _themeStates.TryRemove(productId, out _);
            }
            else
            {
                _themeStates[productId] = PremiumThemeState.Failed;
            }
        });
    }

    private static string ThemeName(AetherLove.Shared.Store.OwnedThemeDto theme)
    {
        var pick = UiHost.Configuration.PluginLanguage switch
        {
            "Spanish" => theme.NameSpanish,
            "French" => theme.NameFrench,
            "Russian" => theme.NameRussian,
            "German" => theme.NameGerman,
            "Portuguese" => theme.NamePortuguese,
            _ => null,
        };
        return string.IsNullOrWhiteSpace(pick) ? theme.NameEnglish : pick;
    }

    /// <summary>Theme colours ride the wire as 0xAARRGGBB; ImGui wants 0xAABBGGRR.</summary>
    private static uint Rgba(uint argb) =>
        (argb & 0xFF00FF00u) | ((argb & 0x00FF0000u) >> 16) | ((argb & 0x000000FFu) << 16);

    // Labels are the column-by-typical-row counts, matching how Android launchers present grid presets.
    private static readonly (HomeGridPreset Preset, string Label)[] HomeGridChoices =
    [
        (HomeGridPreset.Comfortable, "3×4"),
        (HomeGridPreset.Standard, "4×4"),
        (HomeGridPreset.Compact, "4×5"),
        (HomeGridPreset.Dense, "5×5"),
    ];

    private static void DrawHomeGridRow(float winW, ThemeDefinition t)
    {
        var gap = Px(6f);
        var w = (winW - Px(PadX) * 2f - gap * (HomeGridChoices.Length - 1)) / HomeGridChoices.Length;
        ImGui.SetCursorPosX(Px(PadX));
        for (var i = 0; i < HomeGridChoices.Length; i++)
        {
            if (i > 0)
            {
                ImGui.SameLine(0f, gap);
            }
            var (preset, label) = HomeGridChoices[i];
            var selected = UiHost.Configuration.Os.HomeGrid == preset;
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
            if (SharedUiHelpers.Button($"{label}##homeGrid{preset}", new Vector2(w, Px(30f))) && !selected)
            {
                UiHost.Configuration.Os.HomeGrid = preset;
                UiHost.Configuration.Save();
            }
            ImGui.PopStyleVar();
            ImGui.PopStyleColor(3);
        }

        ImGui.Spacing();
        ImGui.SetCursorPosX(Px(PadX));
        ImGui.PushTextWrapPos(winW - Px(PadX));
        ImGui.TextColored(new Vector4(0.60f, 0.60f, 0.60f, 1f), Loc.T("os.settings_home_grid_caption"));
        ImGui.PopTextWrapPos();
    }

    private void DrawWallpaperPage()
    {
        DrawSubpageBack();
        DrawSubpageHeading(Loc.T("os.group_wallpaper"), PadX);

        var scrollH = ImGui.GetContentRegionAvail().Y;
        PushScrollbarStyle();
        using var scroll = ImRaii.Child("##osSettWallpaper", new Vector2(0f, scrollH), false);
        PopScrollbarStyle();
        if (!scroll)
        {
            return;
        }

        var winW = ImGui.GetWindowSize().X;
        ImGui.Spacing();
        DrawWallpaperPicker(winW);
        ImGui.Spacing();
        ImGui.Spacing();
        DrawSubpageHeading(Loc.T("os.settings_home_screen"), PadX);
        DrawHomeGridRow(winW, ThemeService.Current);
        ImGui.Spacing();
    }

    /// <summary>The history of account-level staff notices. Read-only: the fullscreen staff-notice gate owns
    /// acknowledgement, so opening this page never marks anything seen.</summary>
    private void DrawStaffNoticesPage()
    {
        DrawSubpageBack();
        DrawSubpageHeading(Loc.T("settings.staff_notices_title"), PadX);

        var warnings = _host.StaffWarnings;
        var messages = _host.StaffMessages;
        if (warnings.Count == 0 && messages.Count == 0)
        {
            ImGui.Spacing();
            ImGui.Spacing();
            CenteredHeading(Loc.T("settings.staff_notices_empty"), UiColors.Muted, ImGui.GetWindowSize().X);
            return;
        }

        var scrollH = ImGui.GetContentRegionAvail().Y;
        PushScrollbarStyle();
        using var scroll = ImRaii.Child("##osSettStaffNotices", new Vector2(0f, scrollH), false);
        PopScrollbarStyle();
        if (!scroll)
        {
            return;
        }

        var listW = ImGui.GetContentRegionAvail().X;
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0f, Px(10f)));
        if (warnings.Count > 0)
        {
            GroupLabel(Loc.T("settings.staff_warnings_section"));
            foreach (var warning in warnings)
            {
                DrawNoticeCard(listW, FontAwesomeIcon.ExclamationTriangle, UiColors.WarningAccent,
                    warning.CreatedAtUtc, warning.Reason, warning.Seen, PadX);
            }
        }
        if (messages.Count > 0)
        {
            GroupLabel(Loc.T("settings.staff_messages_section"));
            foreach (var message in messages)
            {
                DrawNoticeCard(listW, FontAwesomeIcon.Envelope, UiColors.MessageAccent,
                    message.CreatedAtUtc, message.Body, message.Seen, PadX);
            }
        }
        ImGui.PopStyleVar();
        ImGui.Dummy(Px(0f, 18f));
    }

    private void DrawTosPage()
    {
        DrawSubpageBack();
        DrawSubpageHeading(Loc.T("settings.terms_of_service"), PadX);

        var scrollH = ImGui.GetContentRegionAvail().Y;
        PushScrollbarStyle();
        using var scroll = ImRaii.Child("##osSettTos", new Vector2(0f, scrollH), false);
        PopScrollbarStyle();
        if (!scroll)
        {
            return;
        }

        var winW = ImGui.GetWindowSize().X;
        ImGui.Spacing();
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
    }

    private void DrawContributorsPage()
    {
        DrawSubpageBack();

        var scrollH = ImGui.GetContentRegionAvail().Y;
        PushScrollbarStyle();
        using var scroll = ImRaii.Child("##osSettContributors", new Vector2(0f, scrollH), false);
        PopScrollbarStyle();
        if (!scroll)
        {
            return;
        }

        var winW = ImGui.GetWindowSize().X;
        var t = ThemeService.Current;

        ImGui.Dummy(new Vector2(0f, Px(10f)));
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
            Loc.T("settings.contributors_vavenn"),
            Loc.T("settings.contributors_testers"),
        ];

        var heartGlyph = FontAwesomeIcon.Heart.ToIconString();
        var rowCol = new Vector4(0.94f, 0.93f, 0.97f, 1f);
        using (UiFonts.H3?.Push())
        {
            foreach (var line in credits)
            {
                ImGui.SetCursorPosX(Px(PadX) + Px(10f));
                ImGui.PushFont(UiHost.PluginInterface.UiBuilder.FontIcon);
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
    }

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

    private static void CenteredIcon(FontAwesomeIcon icon, Vector4 color, float winW, float sizePx)
    {
        var sz = IconDraw.Measure(icon, sizePx);
        var origin = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        IconDraw.Add(dl, icon, sizePx, new Vector2(origin.X + (winW - sz.X) * 0.5f, origin.Y), ImGui.GetColorU32(color));
        ImGui.Dummy(new Vector2(winW, sz.Y));
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

    /// <summary>The supporter page's back pill: a deep link that carried a return app goes back to that app
    /// (its warm resume restores the exact view the pitch came from); a hub-opened page goes to the hub.</summary>
    private void DrawSupporterBack()
    {
        if (_supporterReturnApp is not { } returnApp || _shell is null)
        {
            DrawSubpageBack();
            return;
        }
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.SetCursorPosX(Px(PadX));
        // The pill draws its own back arrow, so pass the return app's icon (not another arrow) as the destination.
        var destIcon = FontAwesomeIcon.Home;
        foreach (var app in _shell.Apps)
        {
            if (app.Id == returnApp)
            {
                destIcon = app.Icon;
                break;
            }
        }
        if (DrawFloatingBackPill(ImGui.GetCursorScreenPos(), Loc.T("settings.back_arrow"), destIcon))
        {
            _supporterReturnApp = null;
            _view = View.Hub;
            _shell.OpenApp(returnApp);
        }
        ImGui.Spacing();
    }

    private void DrawLanguagePills(float winW)
    {
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var entries = ProfileFields.LanguageEntries;
        var count = LanguageProvider.UiLanguageCount;

        var flagW = Px(36f);
        var flagH = Px(27f);
        var pillPadX = Px(6f);
        var pillPadY = Px(5f);
        var labelGap = Px(3f);
        var pillGap = Px(6f);
        var labelH = ImGui.GetTextLineHeight();
        var pillW = flagW + pillPadX * 2f;
        var pillH = pillPadY + flagH + labelGap + labelH + pillPadY;

        var usableW = winW - Px(PadX) * 2f;
        var totalW = count * pillW + (count - 1) * pillGap;
        var startX = Px(PadX) + MathF.Max(0f, (usableW - totalW) * 0.5f);
        var startY = ImGui.GetCursorPosY();

        var currentLang = LanguageProvider.Current.LanguageName;

        for (var i = 0; i < count; i++)
        {
            var entry = entries[i];
            var selected = string.Equals(entry.Name, currentLang, StringComparison.OrdinalIgnoreCase);
            var pillX = startX + i * (pillW + pillGap);

            ImGui.SetCursorPos(new Vector2(pillX, startY));
            var sp = ImGui.GetCursorScreenPos();

            ImGui.InvisibleButton($"##settLang{i}", new Vector2(pillW, pillH));
            SharedUiHelpers.HandOnHover();
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

            var flagTL = sp + new Vector2(pillPadX, pillPadY);
            var flagBR = flagTL + new Vector2(flagW, flagH);
            var flagTex = LanguageFlagService.GetFlag(entry.Name)?.GetWrapOrDefault();
            if (flagTex != null)
            {
                dl.AddImageRounded(flagTex.Handle, flagTL, flagBR, Vector2.Zero, Vector2.One, 0xFFFFFFFF, Px(3f));
            }
            else
            {
                dl.AddRectFilled(flagTL, flagBR, t.AccentDarkWithAlpha(0.6f), Px(3f));
                var codeSz = ImGui.CalcTextSize(entry.Code);
                dl.AddText(flagTL + (new Vector2(flagW, flagH) - codeSz) * 0.5f, 0xFFFFFFFF, entry.Code);
            }

            var labelSz = ImGui.CalcTextSize(entry.Code);
            var labelX = sp.X + (pillW - labelSz.X) * 0.5f;
            var labelY = sp.Y + pillPadY + flagH + labelGap;
            dl.AddText(new Vector2(labelX, labelY), selected ? 0xFFFFFFFF : 0xAAFFFFFF, entry.Code);
        }

        ImGui.SetCursorPosY(startY + pillH + Px(4f));
    }

    private void DrawSupporterPage()
    {
        var winW = ImGui.GetWindowSize().X;

        var flowState = _host.PatreonState;
        if (flowState != _lastPatreonFlowState)
        {
            if (flowState == SupporterFlowState.Completed
                && _host.PatreonStatus is { IsEntitled: false, IsSupporter: false })
            {
                _linkNoMemberOpen = true;
                _linkNoMemberPanelH = 0f;
            }
            _lastPatreonFlowState = flowState;
        }

        DrawSupporterBack();

        var scrollH = ImGui.GetContentRegionAvail().Y;
        PushScrollbarStyle();
        using (var scroll = ImRaii.Child("##osSettSupporter", new Vector2(0f, scrollH), false))
        {
            PopScrollbarStyle();
            if (!scroll)
            {
                return;
            }

            var t = ThemeService.Current;

            switch (_host.PatreonState)
            {
                case SupporterFlowState.Starting:
                    ImGui.Spacing();
                    LoadingIndicator.Draw(Loc.T("settings.supporter_contacting"));
                    return;
                case SupporterFlowState.AwaitingBrowser:
                    DrawSupporterAwaiting(winW, t);
                    return;
                case SupporterFlowState.Failed:
                    DrawSupporterFailed(winW, t);
                    return;
            }

            var status = _host.PatreonStatus;
            if (status is null)
            {
                ImGui.Spacing();
                LoadingIndicator.Draw();
                return;
            }
            if (!status.Enabled)
            {
                ImGui.Spacing();
                ImGui.Spacing();
                ImGui.SetCursorPosX(Px(PadX));
                ImGui.PushTextWrapPos(winW - Px(PadX));
                ImGui.TextColored(UiColors.Subtle, Loc.T("settings.supporter_unavailable"));
                ImGui.PopTextWrapPos();
                return;
            }

            if (status.Linked)
            {
                DrawSupporterLinked(winW, t, status);
            }
            else
            {
                DrawSupporterUnlinked(winW, t, status);
            }
        }

        if (_linkNoMemberOpen)
        {
            DrawLinkNoMembershipOverlay();
        }
    }

    private void DrawLinkNoMembershipOverlay()
    {
        var dismissed = DrawPageOverlayPanel("supNoMember", ImGui.GetWindowPos(), ImGui.GetWindowSize(),
            ref _linkNoMemberPanelH, Px(300f), w =>
        {
            ModalUi.Header(w, FontAwesomeIcon.ExclamationTriangle,
                Loc.T("settings.supporter_nomember_title"), UiColors.WarningAccent);
            ImGui.PushTextWrapPos(w);
            ImGui.TextColored(UiColors.Body, Loc.T("settings.supporter_nomember_body"));
            ImGui.PopTextWrapPos();
            ImGui.Spacing();
            var gap = Px(8f);
            var half = (w - gap) * 0.5f;
            if (ModalUi.Button($"{Loc.T("settings.supporter_unlink_button")}##noMemberUnlink", half))
            {
                _linkNoMemberOpen = false;
                _host.PatreonUnlink();
            }
            ImGui.SameLine(0f, gap);
            if (ModalUi.Button($"{Loc.T("common.got_it")}##noMemberOk", half))
            {
                _linkNoMemberOpen = false;
            }
        });
        if (dismissed)
        {
            _linkNoMemberOpen = false;
        }
    }

    private void DrawSupporterTitle(string text)
    {
        ImGui.SetCursorPosX(Px(PadX));
        using (UiFonts.H1?.Push())
        {
            ImGui.TextColored(ThemeService.Current.AccentLight, text);
        }
        ImGui.Spacing();
    }

    private void DrawSupporterUnlinked(float winW, ThemeDefinition t, PatreonStatusDto status)
    {
        var btnW = winW - Px(PadX) * 2f;

        ImGui.Spacing();
        DrawSupporterTitle(Loc.T("settings.supporter_title"));
        ImGui.SetCursorPosX(Px(PadX));
        ImGui.PushTextWrapPos(winW - Px(PadX));
        ImGui.TextColored(UiColors.Body, Loc.T("settings.supporter_intro"));
        ImGui.PopTextWrapPos();
        ImGui.Spacing();
        ImGui.Spacing();

        DrawPerkSections(winW);
        ImGui.Spacing();

        DrawSectionHeader(Loc.T("settings.supporter_how_heading"), PadX);
        ImGui.SetCursorPosX(Px(PadX));
        ImGui.PushTextWrapPos(winW - Px(PadX));
        ImGui.TextColored(UiColors.Subtle, Loc.T("settings.supporter_how_intro"));
        ImGui.PopTextWrapPos();
        ImGui.Spacing();

        DrawSupporterStep(winW, t, Loc.T("settings.supporter_step1_title"), Loc.T("settings.supporter_step1_body"));

        ImGui.SetCursorPosX(Px(PadX));
        ImGui.PushStyleColor(ImGuiCol.Button, UiColors.Patreon with { W = 0.92f });
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UiColors.Patreon);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, UiColors.Patreon with { W = 0.82f });
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 1f, 1f, 1f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(10f));
        if (SharedUiHelpers.Button(Loc.T("settings.supporter_become"), new Vector2(btnW, Px(40f))))
        {
            _caps.System.OpenUrl(AetherLove.Shared.AetherConstants.PatreonCampaignUrl);
        }
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(4);
        ImGui.Spacing();
        ImGui.Spacing();

        DrawSupporterStep(winW, t, Loc.T("settings.supporter_step2_title"), Loc.T("settings.supporter_step2_body"));

        ImGui.SetCursorPosX(Px(PadX));
        PushThemeButton(t);
        var link = SharedUiHelpers.Button(Loc.T("settings.supporter_link_button"), new Vector2(btnW, Px(36f)));
        PopThemeButton();
        if (link)
        {
            _host.PatreonStartLink();
        }
        ImGui.Spacing();

        ImGui.SetCursorPosX(Px(PadX));
        ImGui.PushTextWrapPos(winW - Px(PadX));
        ImGui.TextColored(UiColors.Hint, Loc.T("settings.supporter_data_note"));
        ImGui.PopTextWrapPos();
        ImGui.Spacing();
        ImGui.Spacing();
    }

    private void DrawSupporterAwaiting(float winW, ThemeDefinition t)
    {
        var btnW = winW - Px(PadX) * 2f;

        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.SetCursorPosX(Px(PadX));
        ImGui.PushTextWrapPos(winW - Px(PadX));
        ImGui.TextColored(UiColors.Body, Loc.T("settings.supporter_awaiting_browser"));
        ImGui.PopTextWrapPos();
        ImGui.Spacing();
        ImGui.Spacing();

        ImGui.SetCursorPosX(Px(PadX));
        PushThemeButton(t);
        if (SharedUiHelpers.Button(Loc.T("settings.supporter_open_again"), new Vector2(btnW, Px(32f))))
        {
            _host.PatreonReopenBrowser();
        }
        PopThemeButton();
        ImGui.Spacing();
        ImGui.SetCursorPosX(Px(PadX));
        if (SharedUiHelpers.Button(Loc.T("settings.supporter_cancel"), new Vector2(btnW, Px(30f))))
        {
            _host.PatreonCancel();
        }
    }

    private void DrawSupporterFailed(float winW, ThemeDefinition t)
    {
        var btnW = winW - Px(PadX) * 2f;

        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.SetCursorPosX(Px(PadX));
        ImGui.PushTextWrapPos(winW - Px(PadX));
        ImGui.TextColored(UiColors.Danger, _host.PatreonError ?? Loc.T("settings.supporter_failed"));
        ImGui.PopTextWrapPos();
        ImGui.Spacing();
        ImGui.Spacing();

        ImGui.SetCursorPosX(Px(PadX));
        PushThemeButton(t);
        if (SharedUiHelpers.Button(Loc.T("settings.supporter_retry"), new Vector2(btnW, Px(34f))))
        {
            _host.PatreonStartLink();
        }
        PopThemeButton();
    }

    private void DrawSupporterLinked(float winW, ThemeDefinition t, PatreonStatusDto status)
    {
        var btnW = winW - Px(PadX) * 2f;

        ImGui.Spacing();
        DrawSupporterTitle(Loc.T("settings.menu_supporter"));
        if (status.IsSupporter || status.IsEntitled)
        {
            ImGui.Spacing();
            DrawPerkCard(winW, PadX, FontAwesomeIcon.Star, new Vector4(0.45f, 0.85f, 0.48f, 1f),
                Loc.T("settings.supporter_you_are_title"), Loc.T("settings.supporter_you_are_body"));
        }
        else
        {
            ImGui.SetCursorPosX(Px(PadX));
            ImGui.TextColored(UiColors.Success, Loc.T("settings.supporter_linked"));
            ImGui.Spacing();
            ImGui.SetCursorPosX(Px(PadX));
            ImGui.PushTextWrapPos(winW - Px(PadX));
            ImGui.TextColored(UiColors.Subtle, Loc.T("settings.supporter_not_entitled"));
            ImGui.PopTextWrapPos();
            ImGui.Spacing();
            ImGui.Spacing();
        }

        DrawPerkSections(winW);
        ImGui.Spacing();

        if (!_supporterConfirmUnlink)
        {
            ImGui.SetCursorPosX(Px(PadX));
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.Danger);
            if (SharedUiHelpers.Button(Loc.T("settings.supporter_unlink_button"), new Vector2(btnW, Px(30f))))
            {
                _supporterConfirmUnlink = true;
            }
            ImGui.PopStyleColor();
            return;
        }

        ImGui.SetCursorPosX(Px(PadX));
        ImGui.PushTextWrapPos(winW - Px(PadX));
        ImGui.TextColored(UiColors.Danger, Loc.T("settings.supporter_unlink_confirm"));
        ImGui.PopTextWrapPos();
        ImGui.Spacing();

        var halfW = (btnW - Px(8f)) * 0.5f;
        ImGui.SetCursorPosX(Px(PadX));
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.55f, 0.14f, 0.14f, 0.85f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.68f, 0.18f, 0.18f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.45f, 0.10f, 0.10f, 1f));
        if (SharedUiHelpers.Button(Loc.T("settings.supporter_unlink_button") + "##confirm", new Vector2(halfW, Px(30f))))
        {
            _supporterConfirmUnlink = false;
            _host.PatreonUnlink();
        }
        ImGui.PopStyleColor(3);
        ImGui.SameLine(0f, Px(8f));
        if (SharedUiHelpers.Button(Loc.T("settings.supporter_cancel") + "##cancelunlink", new Vector2(halfW, Px(30f))))
        {
            _supporterConfirmUnlink = false;
        }
    }

    private void DrawSupporterStep(float winW, ThemeDefinition t, string title, string body)
    {
        var dl = ImGui.GetWindowDrawList();
        var pad = Px(PadX);
        var w = winW - pad * 2f;
        var innerPad = Px(12f);
        var textW = w - innerPad * 2f;

        var titleH = ImGui.GetTextLineHeight();
        var bodyH = ImGui.CalcTextSize(body, false, textW).Y;
        var cardH = innerPad * 2f + titleH + Px(4f) + bodyH;

        var cursorBefore = ImGui.GetCursorPos();
        var tl = ImGui.GetCursorScreenPos() + new Vector2(pad, 0f);
        var br = tl + new Vector2(w, cardH);

        dl.AddRectFilled(tl, br, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.04f)), Px(10f));
        dl.AddRect(tl, br, ImGui.GetColorU32(t.Accent with { W = 0.30f }), Px(10f), ImDrawFlags.None, Px(1f));

        ImGui.SetCursorScreenPos(new Vector2(tl.X + innerPad, tl.Y + innerPad));
        ImGui.TextColored(t.AccentLight, title);
        ImGui.SetCursorScreenPos(new Vector2(tl.X + innerPad, tl.Y + innerPad + titleH + Px(4f)));
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + textW);
        ImGui.TextColored(UiColors.Body, body);
        ImGui.PopTextWrapPos();

        ImGui.SetCursorPos(new Vector2(cursorBefore.X, cursorBefore.Y + cardH + Px(8f)));
    }

    private void DrawPerkSections(float winW)
    {
        DrawSectionHeader(Loc.T("settings.supporter_store_perks_header"), PadX);
        DrawStorePerks(winW);
        ImGui.Spacing();
        DrawSectionHeader(Loc.T("settings.supporter_msgr_perks_header"), PadX);
        DrawMessengerPerks(winW);
        ImGui.Spacing();
        DrawSectionHeader(Loc.T("settings.supporter_perks_header"), PadX);
        DrawSupporterPerks(winW);
    }

    private void DrawStorePerks(float winW)
    {
        var gold = new Vector4(0.95f, 0.78f, 0.34f, 1f);

        DrawPerkCard(winW, PadX, FontAwesomeIcon.ShoppingBag, gold,
            Loc.T("settings.supporter_perk_store_title"), Loc.T("settings.supporter_perk_store_body"));
    }

    private void DrawMessengerPerks(float winW)
    {
        var cyan = new Vector4(0.30f, 0.80f, 0.82f, 1f);
        var indigo = new Vector4(0.58f, 0.56f, 0.98f, 1f);

        DrawPerkCard(winW, PadX, FontAwesomeIcon.Users, cyan,
            Loc.T("settings.supporter_perk_msgr_groups_title"), Loc.T("settings.supporter_perk_msgr_groups_body"));
        DrawPerkCard(winW, PadX, FontAwesomeIcon.Hdd, indigo,
            Loc.T("settings.supporter_perk_msgr_storage_title"), Loc.T("settings.supporter_perk_msgr_storage_body"));
    }

    private void DrawSupporterPerks(float winW)
    {
        var rose = new Vector4(0.96f, 0.44f, 0.70f, 1f);
        var red = new Vector4(0.95f, 0.38f, 0.38f, 1f);
        var orange = new Vector4(0.97f, 0.62f, 0.26f, 1f);
        var yellow = new Vector4(0.96f, 0.83f, 0.32f, 1f);
        var green = new Vector4(0.45f, 0.85f, 0.48f, 1f);
        var blue = new Vector4(0.40f, 0.64f, 0.96f, 1f);
        var violet = new Vector4(0.72f, 0.52f, 0.96f, 1f);

        DrawPerkCard(winW, PadX, FontAwesomeIcon.UserFriends, rose,
            Loc.T("settings.supporter_perk_profiles_title"), Loc.T("settings.supporter_perk_profiles_body"));
        DrawPerkCard(winW, PadX, FontAwesomeIcon.Images, red,
            Loc.T("settings.supporter_perk_photos_title"), Loc.T("settings.supporter_perk_photos_body"));
        DrawPerkCard(winW, PadX, FontAwesomeIcon.Bolt, orange,
            Loc.T("settings.supporter_perk_superlike_title"), Loc.T("settings.supporter_perk_superlike_body"));
        DrawPerkCard(winW, PadX, FontAwesomeIcon.Undo, yellow,
            Loc.T("settings.supporter_perk_rewinds_title"), Loc.T("settings.supporter_perk_rewinds_body"));
        DrawPerkCard(winW, PadX, FontAwesomeIcon.ChartLine, green,
            Loc.T("settings.supporter_perk_analytics_title"), Loc.T("settings.supporter_perk_analytics_body"));
        DrawPerkCard(winW, PadX, FontAwesomeIcon.Palette, blue,
            Loc.T("settings.supporter_perk_colors_title"), Loc.T("settings.supporter_perk_colors_body"));
        DrawPerkCard(winW, PadX, FontAwesomeIcon.Star, violet,
            Loc.T("settings.supporter_perk_badge_title"), Loc.T("settings.supporter_perk_badge_body"));
    }

    private void DrawResetHomePopup()
    {
        var dismissed = DrawPageOverlayPanel("osResetHome", ImGui.GetWindowPos(), ImGui.GetWindowSize(),
            ref _resetHomePanelH, Px(220f), w =>
        {
            ModalUi.Header(w, FontAwesomeIcon.Undo, Loc.T("os.reset_home_confirm_title"), ThemeService.Current.Accent);
            ImGui.PushTextWrapPos(w);
            ImGui.TextColored(UiColors.Body, Loc.T("os.reset_home_confirm_body"));
            ImGui.PopTextWrapPos();
            ImGui.Spacing();
            var gap = Px(8f);
            var half = (w - gap) * 0.5f;
            if (ModalUi.Button($"{Loc.T("common.cancel")}##resetHomeCancel", half))
            {
                _resetHomeOpen = false;
            }
            ImGui.SameLine(0f, gap);
            if (ModalUi.Button($"{Loc.T("os.home_reset")}##resetHomeConfirm", half))
            {
                var os = UiHost.Configuration.Os;
                os.Pages.Clear();
                os.IconOrder.Clear();
                os.DockIds.Clear();
                os.LayoutColumns = 0;
                os.LayoutRows = 0;
                UiHost.Configuration.Save();
                _resetHomeOpen = false;
            }
        });
        if (dismissed)
        {
            _resetHomeOpen = false;
        }
    }

    private static void GroupLabel(string text)
    {
        ImGui.Dummy(Px(0f, 12f));
        ImGui.SetCursorPosX(Px(PadX));
        ImGui.TextColored(new Vector4(0.55f, 0.55f, 0.60f, 1f), text.ToUpperInvariant());
        ImGui.Spacing();
    }

    private static void DrawLockRow()
    {
        var cfg = UiHost.Configuration;
        ImGui.SetCursorPosX(Px(PadX));
        var locked = cfg.LockPhonePosition;
        if (ImGui.Checkbox(Loc.T("settings.lock_position"), ref locked))
        {
            cfg.LockPhonePosition = locked;
            cfg.Save();
        }
    }

    private void DrawWallpaperPicker(float winW)
    {
        var os = UiHost.Configuration.Os;
        var dl = ImGui.GetWindowDrawList();
        var padX = Px(PadX);
        var innerW = winW - padX * 2f;

        var thumbW = (innerW - Px(12f) * 2f) / 3f;
        var thumbH = thumbW * 1.72f;

        ImGui.SetCursorPosX(padX);
        var gridStart = ImGui.GetCursorScreenPos();

        var idx = 0;
        DrawGradientThumb(dl, ThumbRect(gridStart, thumbW, thumbH, idx++),
            os.WallpaperMode == WallpaperMode.ThemeGradient);
        foreach (var file in _host.BuiltIns)
        {
            DrawImageThumb(dl, ThumbRect(gridStart, thumbW, thumbH, idx++), file,
                os.WallpaperMode == WallpaperMode.BuiltIn && os.BuiltInWallpaper == file);
        }
        if (os.CustomWallpaperPath.Length > 0)
        {
            DrawCustomThumb(dl, ThumbRect(gridStart, thumbW, thumbH, idx++),
                os.WallpaperMode == WallpaperMode.Custom);
        }

        var rows = (idx + 2) / 3;
        ImGui.SetCursorScreenPos(gridStart + new Vector2(0f, rows * (thumbH + Px(12f))));

        DrawPremiumWallpapers(thumbW, thumbH);

        ImGui.SetCursorPosX(padX);
        var t = ThemeService.Current;
        ImGui.PushStyleColor(ImGuiCol.Button, t.ButtonNormal);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, t.ButtonHovered);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, t.ButtonActive);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(9f));
        if (SharedUiHelpers.Button(Loc.T("os.wallpaper_upload"), new Vector2(innerW, Px(34f))))
        {
            var request = new ImagePickRequest(Loc.T("os.wallpaper_upload"), Loc.T("profile.image_files_filter") + "{.png,.jpg,.jpeg}");
            _caps.Images.PickFile(request, path => _host.ApplyCustomFromFile(path));
        }
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);

        ImGui.Spacing();
        ImGui.SetCursorPosX(padX);
        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), Loc.T("os.wallpaper_dim"));
        ImGui.SetCursorPosX(padX);
        ImGui.SetNextItemWidth(innerW);
        var dim = os.WallpaperDim;
        if (ImGui.SliderFloat("##wallDim", ref dim, 0f, 0.98f, ""))
        {
            os.WallpaperDim = dim;
        }
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            UiHost.Configuration.Save();
        }
    }

    /// <summary>The wallpapers that came with purchased themes, in their own shelf. Picking one changes only
    /// the background; the palette stays whatever the user has chosen.</summary>
    private void DrawPremiumWallpapers(float thumbW, float thumbH)
    {
        EnsureOwnedThemes();
        var owned = Array.FindAll(_ownedThemes, t => t.HasBackground);
        if (owned.Length == 0)
        {
            return;
        }
        var os = UiHost.Configuration.Os;
        var dl = ImGui.GetWindowDrawList();

        ImGui.Spacing();
        DrawSubpageHeading(Loc.T("settings.premium_backgrounds"), PadX);
        ImGui.SetCursorPosX(Px(PadX));
        var gridStart = ImGui.GetCursorScreenPos();

        for (var i = 0; i < owned.Length; i++)
        {
            var theme = owned[i];
            var rect = ThumbRect(gridStart, thumbW, thumbH, i);
            ImGui.SetCursorScreenPos(rect.TL);
            if (ImGui.InvisibleButton($"##wallPremium{i}", rect.BR - rect.TL))
            {
                _ = Task.Run(() => _host.SelectPremiumWallpaperAsync(theme.ProductId));
            }
            SharedUiHelpers.HandOnHover();
            if (_host.PremiumWallpaper(theme.ProductId) is { } wrap)
            {
                var (uv0, uv1) = OsDrawShared.CoverUv(wrap.Width, wrap.Height,
                    rect.BR.X - rect.TL.X, rect.BR.Y - rect.TL.Y);
                dl.AddImageRounded(wrap.Handle, rect.TL, rect.BR, uv0, uv1, 0xFFFFFFFFu, Px(12f));
                DrawDimPreview(dl, rect);
            }
            else
            {
                dl.AddRectFilled(rect.TL, rect.BR, OsDrawShared.White(0.06f), Px(12f));
                LoadingSpinner.Draw((rect.TL + rect.BR) * 0.5f, Px(9f), Px(2f), ThemeService.Current.AccentU32);
            }
            var selected = os.WallpaperMode == WallpaperMode.Premium
                && os.PremiumWallpaperProductId == theme.ProductId;
            FinishThumb(dl, rect, selected, null);
            SparkIcon.Draw(dl, rect.TL + new Vector2(Px(14f), Px(14f)), Px(15f));
        }

        var rows = (owned.Length + 2) / 3;
        ImGui.SetCursorScreenPos(gridStart + new Vector2(0f, rows * (thumbH + Px(12f))));
    }

    private static (Vector2 TL, Vector2 BR) ThumbRect(Vector2 gridStart, float w, float h, int index)
    {
        var col = index % 3;
        var row = index / 3;
        var tl = gridStart + new Vector2(col * (w + Px(12f)), row * (h + Px(12f)));
        return (tl, tl + new Vector2(w, h));
    }

    private void DrawGradientThumb(ImDrawListPtr dl, (Vector2 TL, Vector2 BR) rect, bool selected)
    {
        ImGui.SetCursorScreenPos(rect.TL);
        if (ImGui.InvisibleButton("##wallGradient", rect.BR - rect.TL))
        {
            _host.SelectGradient();
        }
        SharedUiHelpers.HandOnHover();
        var t = ThemeService.Current;
        OsDrawShared.RoundedGradient(dl, rect.TL, rect.BR, Px(12f),
            new Vector4(t.ChipFill.X * 1.35f, t.ChipFill.Y * 1.35f, t.ChipFill.Z * 1.35f, 1f),
            new Vector4(t.ChipFill.X * 0.45f, t.ChipFill.Y * 0.45f, t.ChipFill.Z * 0.45f, 1f));
        IconDraw.AddCentered(dl, FontAwesomeIcon.Magic, Px(18f), (rect.TL + rect.BR) * 0.5f, OsDrawShared.White(0.8f));
        FinishThumb(dl, rect, selected, Loc.T("os.wallpaper_gradient"));
    }

    private void DrawImageThumb(ImDrawListPtr dl, (Vector2 TL, Vector2 BR) rect, string file, bool selected)
    {
        ImGui.SetCursorScreenPos(rect.TL);
        if (ImGui.InvisibleButton($"##wall_{file}", rect.BR - rect.TL))
        {
            _host.SelectBuiltIn(file);
        }
        SharedUiHelpers.HandOnHover();
        var wrap = _host.GetBuiltInTexture(file)?.GetWrapOrDefault();
        if (wrap != null)
        {
            var (uv0, uv1) = OsDrawShared.CoverUv(wrap.Width, wrap.Height,
                rect.BR.X - rect.TL.X, rect.BR.Y - rect.TL.Y);
            dl.AddImageRounded(wrap.Handle, rect.TL, rect.BR, uv0, uv1, 0xFFFFFFFFu, Px(12f));
            DrawDimPreview(dl, rect);
        }
        FinishThumb(dl, rect, selected, null);
    }

    /// <summary>The darken slider's live preview: image thumbs carry the same overlay the home screen applies.</summary>
    private static void DrawDimPreview(ImDrawListPtr dl, (Vector2 TL, Vector2 BR) rect)
    {
        var dim = UiHost.Configuration.Os.WallpaperDim;
        if (dim > 0.01f)
        {
            dl.AddRectFilled(rect.TL, rect.BR, OsDrawShared.Black(dim), Px(12f));
        }
    }

    private void DrawCustomThumb(ImDrawListPtr dl, (Vector2 TL, Vector2 BR) rect, bool selected)
    {
        var os = UiHost.Configuration.Os;
        var hovered = ImGui.IsMouseHoveringRect(rect.TL, rect.BR);

        // Submit the remove X BEFORE the full-tile select button so it wins clicks in its corner
        // (first-submitted overlapping item wins in ImGui); otherwise the tile swallows every click.
        var xC = new Vector2(rect.BR.X - Px(12f), rect.TL.Y + Px(12f));
        var removeClicked = false;
        if (hovered)
        {
            ImGui.SetCursorScreenPos(xC - Px(9f, 9f));
            removeClicked = ImGui.InvisibleButton("##wallCustomRemove", Px(18f, 18f));
            SharedUiHelpers.HandOnHover();
        }

        ImGui.SetCursorScreenPos(rect.TL);
        if (ImGui.InvisibleButton("##wallCustom", rect.BR - rect.TL))
        {
            UiHost.Configuration.Os.WallpaperMode = WallpaperMode.Custom;
            UiHost.Configuration.Save();
        }
        SharedUiHelpers.HandOnHover();
        var wrap = _host.GetTexture(os.CustomWallpaperPath)?.GetWrapOrDefault();
        if (wrap != null)
        {
            var (uv0, uv1) = OsDrawShared.CoverUv(wrap.Width, wrap.Height,
                rect.BR.X - rect.TL.X, rect.BR.Y - rect.TL.Y);
            dl.AddImageRounded(wrap.Handle, rect.TL, rect.BR, uv0, uv1, 0xFFFFFFFFu, Px(12f));
            DrawDimPreview(dl, rect);
        }
        FinishThumb(dl, rect, selected, Loc.T("os.wallpaper_custom"));

        if (hovered)
        {
            dl.AddCircleFilled(xC, Px(9f), OsDrawShared.Black(0.6f), 20);
            IconDraw.AddCentered(dl, FontAwesomeIcon.Times, Px(9f), xC, OsDrawShared.White(0.95f));
        }
        if (removeClicked)
        {
            _host.RemoveCustom();
        }
    }

    private static void FinishThumb(ImDrawListPtr dl, (Vector2 TL, Vector2 BR) rect, bool selected, string? caption)
    {
        var hovered = ImGui.IsMouseHoveringRect(rect.TL, rect.BR);
        if (selected)
        {
            dl.AddRect(rect.TL - new Vector2(2f, 2f), rect.BR + new Vector2(2f, 2f),
                ThemeService.Current.AccentU32, Px(13f), ImDrawFlags.RoundCornersAll, Px(2.4f));
            var checkC = new Vector2(rect.BR.X - Px(13f), rect.BR.Y - Px(13f));
            dl.AddCircleFilled(checkC, Px(9f), ThemeService.Current.AccentU32, 20);
            IconDraw.AddCentered(dl, FontAwesomeIcon.Check, Px(9f), checkC, OsDrawShared.White(1f));
        }
        else if (hovered)
        {
            dl.AddRect(rect.TL, rect.BR, OsDrawShared.White(0.5f), Px(12f), ImDrawFlags.RoundCornersAll, Px(1.4f));
        }
        else
        {
            dl.AddRect(rect.TL, rect.BR, OsDrawShared.White(0.14f), Px(12f), ImDrawFlags.RoundCornersAll, Px(1f));
        }
        if (caption != null)
        {
            var centerX = (rect.TL.X + rect.BR.X) * 0.5f;
            OsDrawShared.CenteredText(dl, caption, centerX, rect.BR.Y - Px(20f), OsDrawShared.White(0.9f), 0.78f);
        }
    }

    /// <summary>An app owns a row here when it exposes a settings surface and is actually on the phone: a
    /// server-disabled or user-removed app hides its tile, so it must not keep a settings entry either.</summary>
    private bool HasHostedSettings(IAetherApp app) =>
        app.Id != "settings" && app is IAppSettings && app.Available && _shell?.IsAppRemoved(app.Id) != true;

    /// <summary>Rows for apps that expose their own settings surface (<see cref="IAppSettings"/>). The Settings
    /// app's own settings are the hub above, so it is excluded; the group hides entirely when nothing qualifies.</summary>
    private void DrawAppsGroup(OsAppContext ctx, float winW)
    {
        // AetherParty belongs in this group even though it is not an app: it is shell-owned, so it has no
        // IAetherApp to be picked up by the loop below, and a settings-only app registration would put a
        // tile on somebody's home screen for a thing that has no screen.
        var hasAny = _host.PartyAvailable;
        foreach (var app in ctx.Shell.Apps)
        {
            if (HasHostedSettings(app))
            {
                hasAny = true;
                break;
            }
        }
        if (!hasAny)
        {
            return;
        }

        GroupLabel(Loc.T("os.group_apps"));

        var dl = ImGui.GetWindowDrawList();
        var padX = Px(PadX);
        var rowH = Px(52f);
        var innerW = winW - padX * 2f;

        if (_host.PartyAvailable)
        {
            ImGui.SetCursorPosX(padX);
            var partyTL = ImGui.GetCursorScreenPos();
            var partyBR = partyTL + new Vector2(innerW, rowH - Px(6f));
            if (ImGui.InvisibleButton("##osApp_aetherparty", new Vector2(innerW, rowH - Px(6f))))
            {
                _view = View.Party;
            }
            var partyHovered = ImGui.IsItemHovered();
            if (partyHovered)
            {
                SharedUiHelpers.HandOnHover();
            }
            dl.AddRectFilled(partyTL, partyBR, OsDrawShared.White(partyHovered ? 0.10f : 0.06f), Px(12f));
            var partyTileSz = Px(32f);
            var partyTileTL = partyTL + new Vector2(Px(10f), (rowH - Px(6f) - partyTileSz) * 0.5f);
            var partyTileBR = partyTileTL + new Vector2(partyTileSz, partyTileSz);
            OsDrawShared.RoundedGradient(dl, partyTileTL, partyTileBR, partyTileSz * 0.28f,
                new Vector4(0.30f, 0.72f, 0.42f, 1f), new Vector4(0.16f, 0.44f, 0.28f, 1f));
            IconDraw.AddCentered(dl, FontAwesomeIcon.UserFriends, partyTileSz * 0.46f,
                (partyTileTL + partyTileBR) * 0.5f, OsDrawShared.White(1f));
            dl.AddText(new Vector2(partyTileBR.X + Px(12f), partyTL.Y + (rowH - Px(6f) - ImGui.GetFontSize()) * 0.5f),
                OsDrawShared.White(0.94f), Loc.T("os.party_title"));
            IconDraw.AddCentered(dl, FontAwesomeIcon.ChevronRight, Px(11f),
                new Vector2(partyBR.X - Px(16f), (partyTL.Y + partyBR.Y) * 0.5f), OsDrawShared.White(0.4f));
            ImGui.SetCursorScreenPos(partyTL + new Vector2(-padX + Px(PadX), rowH));
        }

        foreach (var app in ctx.Shell.Apps)
        {
            if (!HasHostedSettings(app))
            {
                continue;
            }

            ImGui.SetCursorPosX(padX);
            var rowTL = ImGui.GetCursorScreenPos();
            var rowBR = rowTL + new Vector2(innerW, rowH - Px(6f));

            if (ImGui.InvisibleButton($"##osApp_{app.Id}", new Vector2(innerW, rowH - Px(6f))))
            {
                _openApp = app;
            }
            var hovered = ImGui.IsItemHovered();
            if (hovered)
            {
                SharedUiHelpers.HandOnHover();
            }

            dl.AddRectFilled(rowTL, rowBR, OsDrawShared.White(hovered ? 0.10f : 0.06f), Px(12f));
            var tileSz = Px(32f);
            var tileTL = rowTL + new Vector2(Px(10f), (rowH - Px(6f) - tileSz) * 0.5f);
            DrawAppTile(dl, app, tileTL, tileTL + new Vector2(tileSz, tileSz));
            dl.AddText(new Vector2(tileTL.X + tileSz + Px(12f), rowTL.Y + (rowH - Px(6f) - ImGui.GetFontSize()) * 0.5f),
                OsDrawShared.White(0.94f), app.Name);
            IconDraw.AddCentered(dl, FontAwesomeIcon.ChevronRight, Px(11f),
                new Vector2(rowBR.X - Px(16f), (rowTL.Y + rowBR.Y) * 0.5f), OsDrawShared.White(0.4f));

            ImGui.SetCursorScreenPos(rowTL + new Vector2(-padX + Px(PadX), rowH));
        }
    }

    /// <summary>A gradient app tile from the SDK-exposed icon/colors; apps in this list are never external.</summary>
    private static void DrawAppTile(ImDrawListPtr dl, IAetherApp app, Vector2 tl, Vector2 br)
    {
        var size = br.X - tl.X;
        var rounding = size * 0.28f;
        var center = (tl + br) * 0.5f;
        if ((app.TileImage ?? AppIcons.Tile(app.Id)) is { } iconImage)
        {
            dl.AddImageRounded(iconImage, tl, br, Vector2.Zero, Vector2.One, OsDrawShared.White(1f), rounding,
                ImDrawFlags.RoundCornersAll);
            dl.AddRect(tl + new Vector2(1f, 1f), br - new Vector2(1f, 1f),
                OsDrawShared.White(0.20f), rounding, ImDrawFlags.RoundCornersAll, 1f);
            return;
        }
        OsDrawShared.RoundedGradient(dl, tl, br, rounding, app.TileTop, app.TileBottom);
        dl.AddRect(tl + new Vector2(1f, 1f), br - new Vector2(1f, 1f),
            OsDrawShared.White(0.20f), rounding, ImDrawFlags.RoundCornersAll, 1f);
        IconDraw.AddCentered(dl, app.Icon, size * 0.46f, center + new Vector2(0f, 1f), OsDrawShared.Black(0.30f));
        IconDraw.AddCentered(dl, app.Icon, size * 0.46f, center, OsDrawShared.White(1f));
    }

    private const int DevUnlockTaps = 10;
    private int _devClicks;
    private string? _devToast;

    private void DrawAboutGroup(float winW)
    {
        var dl = ImGui.GetWindowDrawList();
        var padX = Px(PadX);
        var rowW = winW - padX * 2f;
        var rowH = Px(44f);
        var rowTL = ImGui.GetCursorScreenPos() + new Vector2(padX, 0f);
        var rowBR = rowTL + new Vector2(rowW, rowH);
        dl.AddRectFilled(rowTL, rowBR, OsDrawShared.White(0.06f), Px(12f));

        var version = _host.PluginVersion;
        dl.AddText(rowTL + Px(14f, 13f), OsDrawShared.White(0.94f), $"AetherOS {version}");

        // Android-style: tapping the version enough times unlocks the hidden developer page.
        ImGui.SetCursorScreenPos(rowTL);
        ImGui.InvisibleButton("##osAboutVersion", new Vector2(rowW, rowH));
        SharedUiHelpers.HandOnHover();
        if (ImGui.IsItemClicked())
        {
            HandleVersionTap();
        }

        if (_devToast is { } toast)
        {
            ImGui.Dummy(new Vector2(1f, Px(6f)));
            ImGui.SetCursorPosX(padX);
            ImGui.PushTextWrapPos(winW - padX);
            ImGui.TextColored(UiColors.Subtle, toast);
            ImGui.PopTextWrapPos();
        }
        ImGui.Dummy(new Vector2(1f, Px(4f)));
    }

    /// <summary>The developer-unlock tap counter behind the About version row (an easter egg): silent for the
    /// first few taps, then a descending countdown, then it flips <see cref="OsSettingsConfig.DeveloperMode"/>.</summary>
    private void HandleVersionTap()
    {
        var os = UiHost.Configuration.OsSettings;
        if (os.DeveloperMode)
        {
            _devToast = "You are already a developer.";
            return;
        }
        _devClicks++;
        var remaining = DevUnlockTaps - _devClicks;
        if (remaining <= 0)
        {
            os.DeveloperMode = true;
            UiHost.Configuration.Save();
            _devToast = "You are now a developer!";
        }
        else if (remaining <= DevUnlockTaps / 2)
        {
            _devToast = remaining == 1
                ? "Click 1 more time to become a developer."
                : $"Click {remaining} more times to become a developer.";
        }
        else
        {
            _devToast = null;
        }
    }

    private SparkStatusDto? _devSparks;
    private string? _devSparksError;
    private bool _devSparksLoading;

    private void LoadDevSparks()
    {
        _devSparksLoading = true;
        _devSparksError = null;
        _ = Task.Run(async () =>
        {
            try
            {
                _devSparks = await _host.GetSparkStatusAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _devSparksError = ex.Message;
            }
            finally
            {
                _devSparksLoading = false;
            }
        });
    }

    /// <summary>The hidden developer page, reached from the unlocked "Developer settings" row. English-only, by
    /// design: an easter egg for the curious, currently showing the sparks wallet nothing else surfaces yet.</summary>
    private void DrawDeveloperPage()
    {
        DrawSubpageBack();
        DrawSubpageHeading("Developer settings", PadX);

        if (_devSparks is null && _devSparksError is null && !_devSparksLoading)
        {
            LoadDevSparks();
        }

        var scrollH = ImGui.GetContentRegionAvail().Y;
        PushScrollbarStyle();
        using var scroll = ImRaii.Child("##osSettDeveloper", new Vector2(0f, scrollH), false);
        PopScrollbarStyle();
        if (!scroll)
        {
            return;
        }

        var winW = ImGui.GetWindowSize().X;
        var t = ThemeService.Current;

        ImGui.Dummy(new Vector2(0f, Px(18f)));
        CenteredIcon(FontAwesomeIcon.Bolt, t.Accent, winW, Px(40f));
        ImGui.Dummy(new Vector2(0f, Px(12f)));

        using (UiFonts.H1?.Push())
        {
            CenteredHeading("Sparks", new Vector4(1f, 1f, 1f, 1f), winW);
        }
        ImGui.Dummy(new Vector2(0f, Px(4f)));
        ImGui.SetCursorPosX(Px(PadX));
        ImGui.PushTextWrapPos(winW - Px(PadX));
        ImGui.TextColored(UiColors.Body, "A peek behind the curtain. Sparks are earned by using the phone; what they buy is still on the way.");
        ImGui.PopTextWrapPos();
        ImGui.Dummy(new Vector2(0f, Px(14f)));

        if (_devSparksLoading && _devSparks is null)
        {
            ImGui.SetCursorPosX(Px(PadX));
            ImGui.TextDisabled("Loading...");
            return;
        }
        if (_devSparksError is not null && _devSparks is null)
        {
            ImGui.SetCursorPosX(Px(PadX));
            ImGui.PushTextWrapPos(winW - Px(PadX));
            ImGui.TextColored(UiColors.WarningAccent, $"Unavailable: {_devSparksError}");
            ImGui.PopTextWrapPos();
            return;
        }
        var status = _devSparks;
        if (status is null)
        {
            return;
        }

        DevRow(winW, "Balance", status.Balance.ToString("N0"));
        DevRow(winW, "Lifetime earned", status.LifetimeEarned.ToString("N0"));
        var reset = status.WeekResetsAtUtc.ToLocalTime();
        DevRow(winW, "Week resets", $"{reset.ToString("d MMM HH:mm", LanguageProvider.CurrentCulture)} ({DevUntil(status.WeekResetsAtUtc)})");
        ImGui.Dummy(new Vector2(0f, Px(14f)));

        ImGui.SetCursorPosX(Px(PadX));
        using (UiFonts.H3?.Push())
        {
            ImGui.TextColored(t.AccentLight, "Last earnings");
        }
        ImGui.Dummy(new Vector2(0f, Px(6f)));

        if (status.Lines.Length == 0)
        {
            ImGui.SetCursorPosX(Px(PadX));
            ImGui.TextDisabled("Nothing earned yet.");
        }

        var dl = ImGui.GetWindowDrawList();
        var rowH = ImGui.GetTextLineHeight() + Px(10f);
        var right = winW - Px(PadX);
        foreach (var line in status.Lines)
        {
            var pos = ImGui.GetCursorScreenPos();
            dl.AddRectFilled(pos with { X = pos.X + Px(PadX) - Px(8f) }, pos + new Vector2(right - Px(PadX) + Px(8f), rowH),
                ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.04f)), Px(6f));
            var textY = pos.Y + Px(5f);
            dl.AddText(new Vector2(pos.X + Px(PadX), textY), ImGui.GetColorU32(UiColors.Body),
                line.AtUtc.ToLocalTime().ToString("d MMM HH:mm", LanguageProvider.CurrentCulture));
            dl.AddText(new Vector2(pos.X + Px(PadX) + Px(96f), textY), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.92f)),
                DevActionLabel(line));
            var amount = line.Amount.ToString("+#,0;-#,0;0");
            var amountW = ImGui.CalcTextSize(amount).X;
            dl.AddText(new Vector2(pos.X + right - amountW, textY),
                ImGui.GetColorU32(line.Amount >= 0 ? t.AccentLight : UiColors.WarningAccent), amount);
            ImGui.Dummy(new Vector2(winW, rowH + Px(3f)));
        }
        ImGui.Dummy(new Vector2(0f, Px(16f)));
    }

    private void DevRow(float winW, string label, string value)
    {
        ImGui.SetCursorPosX(Px(PadX));
        ImGui.TextColored(UiColors.Body, label);
        var valueW = ImGui.CalcTextSize(value).X;
        ImGui.SameLine(winW - Px(PadX) - valueW);
        ImGui.TextUnformatted(value);
    }

    private static string DevActionLabel(SparkLedgerLineDto line) => line.Action switch
    {
        SparkAction.AdminAdjust => $"Staff ({line.Kind})",
        _ => line.Action.ToString(),
    };

    private static string DevUntil(DateTimeOffset at)
    {
        var span = at - DateTimeOffset.UtcNow;
        if (span <= TimeSpan.Zero)
        {
            return "now";
        }
        return span.TotalDays >= 1 ? $"in {(int)span.TotalDays}d {span.Hours}h" : $"in {span.Hours}h {span.Minutes}m";
    }
}
