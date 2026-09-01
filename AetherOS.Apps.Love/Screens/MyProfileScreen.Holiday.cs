using System;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherLove.Screens;

public partial class MyProfileScreen
{
    private bool _holidayOn;
    private string _holidayMessage = string.Empty;
    private volatile bool _holidaySaving;
    private float _holidaySavedTimer;
    private volatile string? _holidaySaveError;

    private void OpenHoliday()
    {
        var conn = _bootstrap.LastConnection;
        _holidayOn = conn?.HolidayMode ?? false;
        _holidayMessage = conn?.HolidayMessage ?? string.Empty;
        _holidaySavedTimer = 0f;
        _holidaySaveError = null;
        _entrance.Arm();
        _section = Section.Holiday;
    }

    /// <summary>Purple banner pinned to the top of the hub while holiday mode is on; clicking opens the config.</summary>
    private void DrawHolidayActiveBanner(float winW)
    {
        var dl = ImGui.GetWindowDrawList();
        var pad = Px(HubPadX);
        var h = Px(34f);
        var origin = ImGui.GetCursorScreenPos();
        var tl = new Vector2(origin.X + pad, origin.Y);
        var br = tl + new Vector2(winW - pad * 2f, h);

        ImGui.SetCursorScreenPos(tl);
        var clicked = ImGui.InvisibleButton("##holidayHubBanner", new Vector2(winW - pad * 2f, h));
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            SharedUiHelpers.HandOnHover();
        }

        dl.AddRectFilled(tl, br, ImGui.GetColorU32(UiColors.HolidayPurple with { W = hovered ? 0.55f : 0.40f }), Px(10f));
        dl.AddRect(tl, br, ImGui.GetColorU32(UiColors.HolidayPurple with { W = 0.9f }), Px(10f), ImDrawFlags.None, Px(1.2f));

        var label = Loc.T("profile.holiday_active_banner");
        var labelSz = ImGui.CalcTextSize(label);
        var iconPx = Px(14f);
        var iconSz = IconDraw.Measure(FontAwesomeIcon.UmbrellaBeach, iconPx);
        var totalW = iconSz.X + Px(8f) + labelSz.X;
        var startX = tl.X + ((br.X - tl.X) - totalW) * 0.5f;
        IconDraw.Add(dl, FontAwesomeIcon.UmbrellaBeach, iconPx,
            new Vector2(startX, tl.Y + (h - iconSz.Y) * 0.5f), 0xFFFFFFFFu);
        dl.AddText(new Vector2(startX + iconSz.X + Px(8f), tl.Y + (h - labelSz.Y) * 0.5f), 0xFFFFFFFFu, label);

        if (clicked)
        {
            OpenHoliday();
        }
        ImGui.SetCursorScreenPos(new Vector2(origin.X, br.Y + Px(8f)));
        ImGui.Spacing();
    }

    private void DrawHolidayView()
    {
        DrawHubBackButton();
        DrawSubpageHeading(Loc.T("profile.menu_holiday"), HubPadX);

        if (_holidaySavedTimer > 0f)
        {
            _holidaySavedTimer -= ImGui.GetIO().DeltaTime;
        }

        using var scroll = ImRaii.Child("##holidayView", ImGui.GetContentRegionAvail(), false);
        if (!scroll.Success)
        {
            return;
        }
        _entrance.BeginFrame();
        var t = ThemeService.Current;
        var availW = ImGui.GetContentRegionAvail().X;
        var padX = Px(HubPadX);
        var w = availW - padX * 2f;

        ImGui.SetCursorPosX(padX);
        ImGui.PushTextWrapPos(availW - padX);
        ImGui.TextColored(UiColors.Muted, Loc.T("profile.holiday_msg_hint"));
        ImGui.PopTextWrapPos();
        ImGui.Spacing();
        ImGui.Spacing();

        ImGui.SetCursorPosX(padX);
        if (DrawToggleSwitch("##holidayOn", Loc.T("profile.holiday_toggle"), _holidayOn))
        {
            _holidayOn = !_holidayOn;
        }
        ImGui.Spacing();

        ImGui.SetCursorPosX(padX);
        DrawFieldLabel(Loc.T("profile.holiday_msg_label"), t);
        ImGui.SetCursorPosX(padX);
        InputTextMultilineWithPaste("##holidayMsg", ref _holidayMessage,
            AetherLove.Shared.EmojiText.MaxHolidayMessageLength, new Vector2(w, Px(64f)));

        if (_holidaySaveError is not null)
        {
            ImGui.SetCursorPosX(padX);
            ImGui.PushTextWrapPos(availW - padX);
            ImGui.TextColored(UiColors.Danger, _holidaySaveError);
            ImGui.PopTextWrapPos();
        }
        if (_holidaySavedTimer > 0f)
        {
            ImGui.SetCursorPosX(padX);
            ImGui.TextColored(UiColors.Success, Loc.T("profile.holiday_saved"));
        }

        ImGui.Spacing();
        ImGui.SetCursorPosX(padX);
        PushThemeButton(t);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(10f));
        var holidaySaving = _holidaySaving;
        if (holidaySaving)
        {
            ImGui.BeginDisabled();
        }
        if (SharedUiHelpers.Button($"{Loc.T("profile.holiday_save")}##holidaySave", new Vector2(w, Px(34f))))
        {
            SaveHoliday();
        }
        if (holidaySaving)
        {
            ImGui.EndDisabled();
        }
        ImGui.PopStyleVar();
        PopThemeButton();
        _entrance.EndFrame();
    }

    private void SaveHoliday()
    {
        _holidaySaving = true;
        _holidaySaveError = null;
        var on = _holidayOn;
        var msg = _holidayMessage.Trim();
        var ct = _cts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await _hubClient.SetHolidayModeAsync(on, msg, ct).ConfigureAwait(false);
                if (_bootstrap.LastConnection is { } conn)
                {
                    _bootstrap.ReplaceConnectionSnapshot(conn with { HolidayMode = on, HolidayMessage = msg });
                }
                _holidaySavedTimer = 2.5f;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _holidaySaveError = HubErrorText.Localize(ex);
            }
            finally
            {
                _holidaySaving = false;
            }
        }, ct);
    }
}
