using System;
using System.Globalization;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Shared.Profile;
using AetherLove.Shared.Profile.Enums;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherLove.Screens;

public partial class MyProfileScreen
{
    private NameStyle _supStyle;
    private bool _supShowBadge = true;
    private volatile bool _supSaving;
    private float _supSavedTimer;
    private string? _supSaveError;
    private volatile SupporterStatsDto? _supStats;

    private void OpenSupporterVanity()
    {
        var conn = _bootstrap.LastConnection;
        _supStyle = conn?.NameStyle ?? NameStyle.None;
        _supShowBadge = conn?.ShowSupporterBadge ?? true;
        _supSavedTimer = 0f;
        _supSaveError = null;
        _entrance.Arm();
        _section = Section.SupporterVanity;
    }

    private void OpenSupporterStats()
    {
        _entrance.Arm();
        _section = Section.SupporterStats;
        StartSupporterStatsFetch();
    }

    private void StartSupporterStatsFetch()
    {
        var ct = _cts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                _supStats = await _hubClient.GetSupporterStatsAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                {
                    Plugin.Log.Warning(ex, "[MyProfileScreen] GetSupporterStatsAsync failed.");
                }
            }
        }, ct);
    }

    private static string StyleLabel(NameStyle s) => Loc.T(s switch
    {
        NameStyle.Crimson => "profile.style_crimson",
        NameStyle.Gold => "profile.style_gold",
        NameStyle.Emerald => "profile.style_emerald",
        NameStyle.Sapphire => "profile.style_sapphire",
        NameStyle.Violet => "profile.style_violet",
        NameStyle.Rose => "profile.style_rose",
        NameStyle.RainbowCycle => "profile.style_rainbow",
        NameStyle.Shimmer => "profile.style_shimmer",
        NameStyle.Pulse => "profile.style_pulse",
        _ => "profile.style_none",
    });

    private void DrawSupporterVanityView()
    {
        DrawHubBackButton();
        DrawSubpageHeading(Loc.T("profile.menu_sup_vanity"), HubPadX);

        if (_supSavedTimer > 0f)
        {
            _supSavedTimer -= ImGui.GetIO().DeltaTime;
        }

        using var scroll = ImRaii.Child("##supVanity", ImGui.GetContentRegionAvail(), false);
        if (!scroll.Success)
        {
            return;
        }
        _entrance.BeginFrame();
        var availW = ImGui.GetContentRegionAvail().X;
        var padX = Px(HubPadX);

        ImGui.SetCursorPosX(padX);
        ImGui.PushTextWrapPos(availW - padX);
        ImGui.TextColored(UiColors.Muted, Loc.T("profile.sup_intro"));
        ImGui.PopTextWrapPos();
        ImGui.Spacing();

        // Live preview: the caller's own name in the selected style, star included.
        var name = _bootstrap.LastConnection?.DisplayName ?? "?";
        var previewCol = ImGui.ColorConvertU32ToFloat4(SupporterStyle.NameColor(_supStyle, 0xFFFFFFFF));
        ImGui.SetCursorPosX(padX);
        using (UiFonts.H2?.Push())
        {
            ImGui.TextColored(previewCol, name);
        }
        ImGui.SameLine(0f, Px(6f));
        ImGui.PushFont(Plugin.PluginInterface.UiBuilder.FontIcon);
        ImGui.TextColored(ImGui.ColorConvertU32ToFloat4(UiColors.FavoriteStar), FontAwesomeIcon.Star.ToIconString());
        ImGui.PopFont();

        ImGui.Spacing();
        ImGui.Spacing();
        DrawSectionHeading(Loc.T("profile.sup_name_style"), ThemeService.Current);

        foreach (NameStyle s in Enum.GetValues<NameStyle>())
        {
            var rowCol = ImGui.ColorConvertU32ToFloat4(SupporterStyle.NameColor(s, 0xFFFFFFFF));
            ImGui.SetCursorPosX(padX);
            if (ImGui.RadioButton($"##sty{(int)s}", _supStyle == s))
            {
                _supStyle = s;
            }
            ImGui.SameLine(0f, Px(8f));
            ImGui.TextColored(rowCol, StyleLabel(s));
        }

        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.SetCursorPosX(padX);
        ImGui.Checkbox(Loc.T("profile.sup_show_badge"), ref _supShowBadge);
        ImGui.SetCursorPosX(padX);
        ImGui.PushTextWrapPos(availW - padX);
        ImGui.TextColored(UiColors.Hint, Loc.T("profile.sup_badge_hint"));
        ImGui.PopTextWrapPos();

        ImGui.Spacing();
        if (_supSaveError is not null)
        {
            ImGui.SetCursorPosX(padX);
            ImGui.PushTextWrapPos(availW - padX);
            ImGui.TextColored(UiColors.Danger, _supSaveError);
            ImGui.PopTextWrapPos();
            ImGui.Spacing();
        }

        var label = _supSaving ? Loc.T("profile.saving")
                  : _supSavedTimer > 0f ? Loc.T("profile.saved")
                  : Loc.T("profile.save_changes");
        ImGui.SetCursorPosX(padX);
        PushThemeButton(ThemeService.Current);
        if (ImGui.Button($"{label}##supSave", new Vector2(availW - padX * 2f, Px(30f))) && !_supSaving)
        {
            SaveSupporterOptions();
        }
        PopThemeButton();
        ImGui.Spacing();
        _entrance.EndFrame();
    }

    private void DrawSupporterStatsView()
    {
        DrawHubBackButton();
        DrawSubpageHeading(Loc.T("profile.menu_sup_stats"), HubPadX);

        using var scroll = ImRaii.Child("##supStats", ImGui.GetContentRegionAvail(), false);
        if (!scroll.Success)
        {
            return;
        }
        if (_supStats is not { } stats)
        {
            Widgets.LoadingIndicator.Draw();
            return;
        }

        _entrance.BeginFrame();
        var t = ThemeService.Current;
        var availW = ImGui.GetContentRegionAvail().X;
        var padX = Px(HubPadX);

        ImGui.SetCursorPosX(padX);
        ImGui.PushTextWrapPos(availW - padX);
        ImGui.TextColored(UiColors.Muted, Loc.T("profile.sup_stats_intro"));
        ImGui.PopTextWrapPos();
        ImGui.Spacing();
        ImGui.Spacing();

        var dl = ImGui.GetWindowDrawList();
        var gap = Px(8f);
        var blockH = Px(92f);
        var halfW = (availW - padX * 2f - gap) / 2f;
        var thirdW = (availW - padX * 2f - gap * 2f) / 3f;

        string Pct(double v) => v.ToString("0.#", CultureInfo.InvariantCulture) + "%";

        void PairRow(FontAwesomeIcon iconA, Vector4 accentA, string valueA, string labelA, bool beatA,
                     FontAwesomeIcon iconB, Vector4 accentB, string valueB, string labelB)
        {
            var origin = ImGui.GetCursorScreenPos() + new Vector2(padX, 0f);
            DrawStatBlock(dl, origin, halfW, blockH, iconA, accentA, valueA, labelA, beatA);
            DrawStatBlock(dl, origin + new Vector2(halfW + gap, 0f), halfW, blockH, iconB, accentB, valueB, labelB, beat: false);
            ImGui.Dummy(new Vector2(availW, blockH + gap));
        }

        PairRow(
            FontAwesomeIcon.Heart, t.SecondaryEnd, stats.LikesReceived.ToString("N0"), Loc.T("profile.sup_stat_likes_received"), beatA: true,
            FontAwesomeIcon.Star, UiColors.Amber, stats.SuperlikesReceived.ToString("N0"), Loc.T("profile.sup_stat_superlikes"));
        PairRow(
            FontAwesomeIcon.Eye, t.Accent, stats.ProfileViews.ToString("N0"), Loc.T("profile.sup_stat_views"), beatA: false,
            FontAwesomeIcon.LayerGroup, t.AccentLight, stats.Impressions.ToString("N0"), Loc.T("profile.sup_stat_impressions"));
        PairRow(
            FontAwesomeIcon.ThumbsUp, t.SecondaryStart, stats.LikesGiven.ToString("N0"), Loc.T("profile.sup_stat_likes_given"), beatA: false,
            FontAwesomeIcon.Times, UiColors.Patreon, stats.PassesGiven.ToString("N0"), Loc.T("profile.sup_stat_passes_given"));

        var last = ImGui.GetCursorScreenPos() + new Vector2(padX, 0f);
        DrawStatBlock(dl, last, thirdW, blockH, FontAwesomeIcon.Fire, t.Accent, stats.Matches.ToString("N0"), Loc.T("profile.sup_stat_matches"), beat: false);
        DrawStatBlock(dl, last + new Vector2(thirdW + gap, 0f), thirdW, blockH, FontAwesomeIcon.Percent, t.AccentLight, Pct(stats.LikeRateGivenPct), Loc.T("profile.sup_stat_like_rate"), beat: false);
        DrawStatBlock(dl, last + new Vector2((thirdW + gap) * 2f, 0f), thirdW, blockH, FontAwesomeIcon.ChartLine, t.SecondaryEnd, Pct(stats.MatchRatePct), Loc.T("profile.sup_stat_match_rate"), beat: false);
        ImGui.Dummy(new Vector2(availW, blockH + gap));

        ImGui.Spacing();
        _entrance.EndFrame();
    }

    private void SaveSupporterOptions()
    {
        _supSaving = true;
        _supSaveError = null;
        var ct = _cts.Token;
        var style = _supStyle;
        var showBadge = _supShowBadge;
        _ = Task.Run(async () =>
        {
            try
            {
                await _hubClient.SetSupporterOptionsAsync(style, showBadge, ct).ConfigureAwait(false);
                // Reopening the page seeds from the connection snapshot, so a stale one re-selects Default.
                if (_bootstrap.LastConnection is { } conn)
                {
                    _bootstrap.ReplaceConnectionSnapshot(conn with { NameStyle = style, ShowSupporterBadge = showBadge });
                }
                _supSavedTimer = 2.5f;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                {
                    _supSaveError = HubErrorText.Localize(ex);
                    Plugin.Log.Warning(ex, "[MyProfileScreen] SetSupporterOptionsAsync failed.");
                }
            }
            finally
            {
                _supSaving = false;
            }
        }, ct);
    }
}
