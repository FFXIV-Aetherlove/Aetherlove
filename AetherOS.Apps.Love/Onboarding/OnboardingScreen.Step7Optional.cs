using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Shared.Profile.Enums;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using OtterGui.Widgets;
using static AetherLove.UI.OnboardingUi;

namespace AetherLove.Screens;

public partial class OnboardingScreen
{
    private ClippedSelectableCombo<string>? _jobCombo;

    private ClippedSelectableCombo<string> EnsureJobCombo() =>
        _jobCombo ??= new ClippedSelectableCombo<string>(
            "jobcombo", "##jobcombo", 260f, GetJobLabels().ToList(), s => s);

    private int _jobComboIdx = 0;

    // Index 0 = "(None)", 1+ = _locationNames[idx-1]
    private ClippedSelectableCombo<string>? _locationCombo;
    private int                             _locationComboIdx = 0;

    private int  _expansionIdx  = 0;
    private readonly bool[] _syncToolsSelected = new bool[SyncToolValues.Length];

    private readonly bool[] _weekdayHours = new bool[24];
    private readonly bool[] _weekendHours = new bool[24];

    private bool                     _locationsLoaded;
    private (uint Id, string Name)[] _locations     = [];
    private string[]                 _locationNames = [];

    private MusicLinkField[]? _musicFields;

    /// <summary>Spotify, SoundCloud, Apple Music, YouTube Music - in that order.</summary>
    private MusicLinkField[] MusicFields => _musicFields ??=
    [
        new MusicLinkField(MusicProvider.Spotify, _hubClient.ResolveMusicLinkAsync),
        new MusicLinkField(MusicProvider.SoundCloud, _hubClient.ResolveMusicLinkAsync),
        new MusicLinkField(MusicProvider.AppleMusic, _hubClient.ResolveMusicLinkAsync),
        new MusicLinkField(MusicProvider.YouTubeMusic, _hubClient.ResolveMusicLinkAsync),
    ];

    private string _favoriteMovie       = "";
    private string _favoriteAnime       = "";
    private string _favoriteFFCharacter = "";


    /// <summary>Renders optional profile fields: FFXIV favorites, music links, and availability settings.</summary>
    private void DrawStepExtras()
    {
        DrawHero("love_extras", FontAwesomeIcon.Star, Loc.T("onboarding.hero_extras_title"),
            Loc.T("onboarding.hero_extras_sub"), 30f);

        var t = ThemeService.Current;
        var winW = ImGui.GetWindowSize().X;
        var w = winW - Px(40f);
        var inputW = Math.Min(w, Px(280f));

        DrawExtrasHeading(Loc.T("onboarding.opt_section_favourites"), t);

        ImGui.SetCursorPosX(Px(20f));
        ImGui.Text(Loc.T("onboarding.opt_fav_job"));
        ImGui.SetCursorPosX(Px(20f));
        if (EnsureJobCombo().Draw(_jobComboIdx, out var newJobIdx))
        {
            _jobComboIdx = newJobIdx;
        }
        ImGui.Spacing();

        EnsureLocationsLoaded();
        ImGui.SetCursorPosX(Px(20f));
        ImGui.Text(Loc.T("onboarding.opt_fav_location"));
        ImGui.SetCursorPosX(Px(20f));
        if (_locationCombo != null)
        {
            if (_locationCombo.Draw(_locationComboIdx, out var newLocIdx))
            {
                _locationComboIdx = newLocIdx;
            }
        }
        else
        {
            ImGui.TextColored(UiColors.Hint, Loc.T("onboarding.opt_location_unavailable"));
        }
        ImGui.Spacing();

        ImGui.SetCursorPosX(Px(20f));
        ImGui.Text(Loc.T("onboarding.opt_fav_expansion"));
        ImGui.SetCursorPosX(Px(20f));
        ImGui.SetNextItemWidth(inputW);
        ImGui.Combo("##exp", ref _expansionIdx, Expansions, Expansions.Length);
        ImGui.Spacing();

        DrawExtrasTextField("##favMovie", Loc.T("onboarding.opt_fav_movie"), ref _favoriteMovie, inputW);
        DrawExtrasTextField("##favAnime", Loc.T("onboarding.opt_fav_anime"), ref _favoriteAnime, inputW);
        DrawExtrasTextField("##favFFChar", Loc.T("onboarding.opt_fav_ff_character"), ref _favoriteFFCharacter, inputW);

        DrawExtrasHeading(Loc.T("onboarding.opt_music_heading"), t, Loc.T("onboarding.opt_music_tip"));
        var musicW = Math.Min(w, Px(280f));
        ImGui.SetCursorPosX(Px(20f));
        DrawMusicLinkField(MusicFields[0], Loc.T("onboarding.opt_fav_spotify"), musicW);
        ImGui.SetCursorPosX(Px(20f));
        DrawMusicLinkField(MusicFields[1], Loc.T("onboarding.opt_fav_soundcloud"), musicW);
        ImGui.SetCursorPosX(Px(20f));
        DrawMusicLinkField(MusicFields[2], Loc.T("onboarding.opt_fav_apple"), musicW);
        ImGui.SetCursorPosX(Px(20f));
        DrawMusicLinkField(MusicFields[3], Loc.T("onboarding.opt_fav_youtube"), musicW);

        DrawExtrasHeading(Loc.T("onboarding.opt_section_availability"), t);

        ImGui.SetCursorPosX(Px(20f));
        ImGui.Text(Loc.T("onboarding.profile_timezone"));
        ImGui.SetCursorPosX(Px(20f));
        if (EnsureTimezoneCombo().Draw(_timezoneIdx, out var newTzIdx))
        {
            _timezoneIdx = newTzIdx;
        }
        ImGui.Spacing();

        ImGui.SetCursorPosX(Px(20f));
        ImGui.Text(Loc.T("onboarding.opt_weekday_playtimes"));
        ImGui.SetCursorPosX(Px(20f));
        DrawOnlineHoursEditor(w, _weekdayHours, "wd");
        ImGui.Spacing();

        ImGui.SetCursorPosX(Px(20f));
        ImGui.Text(Loc.T("onboarding.opt_weekend_playtimes"));
        ImGui.SetCursorPosX(Px(20f));
        DrawOnlineHoursEditor(w, _weekendHours, "we");
        ImGui.Spacing();

        ImGui.SetCursorPosX(Px(20f));
        ImGui.Text(Loc.T("onboarding.opt_sync_tool"));
        var col2 = Px(20f) + Px(190f);
        for (int i = 0; i < SyncToolLabels.Length; i++)
        {
            if (i % 2 == 0)
            {
                ImGui.SetCursorPosX(Px(20f));
            }
            else
            {
                ImGui.SameLine(col2);
            }
            ImGui.Checkbox($"{SyncToolLabels[i]}##sync{i}", ref _syncToolsSelected[i]);
        }
        ImGui.Spacing();
    }

    private static void DrawExtrasTextField(string id, string label, ref string value, float width)
    {
        ImGui.SetCursorPosX(Px(20f));
        ImGui.Text(label);
        ImGui.SetCursorPosX(Px(20f));
        ImGui.SetNextItemWidth(width);
        ImGui.InputText(id, ref value, 128);
        ImGui.Spacing();
    }

    /// <summary>Renders a section heading indented to align with fields below it.</summary>
    private static void DrawExtrasHeading(string title, ThemeDefinition t, string? tip = null)
    {
        ImGui.Dummy(new Vector2(0f, Px(6f)));
        ImGui.SetCursorPosX(Px(20f));
        using (UiFonts.H3?.Push())
        {
            ImGui.TextColored(t.AccentLight, title);
        }
        if (tip is not null)
        {
            ImGui.SameLine();
            HelpTooltip(tip);
        }
        ImGui.Dummy(new Vector2(0f, Px(4f)));
    }


    private void EnsureLocationsLoaded()
    {
        if (_locationsLoaded) return;
        _locationsLoaded = true;
        try
        {
            var list  = new List<(uint Id, string Name)>();
            var sheet = UiHost.DataManager.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>();
            if (sheet != null)
            {
                foreach (var row in sheet)
                {
                    try
                    {
                        var name = row.PlaceName.Value.Name.ExtractText();
                        if (!string.IsNullOrWhiteSpace(name))
                            list.Add((row.RowId, name));
                    }
                    catch { /* skip malformed rows */ }
                }
            }
            _locations     = list.OrderBy(x => x.Name).DistinctBy(x => x.Name).ToArray();
            _locationNames = _locations.Select(x => x.Name).ToArray();

            // Index 0 = "(None)", 1+ = _locationNames[idx-1]
            var locationItems = new[] { Loc.T("onboarding.opt_location_none") }.Concat(_locationNames).ToList();
            _locationCombo = new ClippedSelectableCombo<string>(
                "loccombo", "##loccombo", 300f, locationItems, s => s);
        }
        catch (Exception ex)
        {
            UiHost.Log.Warning(ex, "[Onboarding] Could not load territory locations.");
        }
    }
}
