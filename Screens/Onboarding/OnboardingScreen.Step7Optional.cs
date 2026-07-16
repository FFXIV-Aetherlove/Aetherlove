using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Shared.Profile.Enums;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using OtterGui.Widgets;

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


    private void DrawStepOptional()
    {
        var t      = ThemeService.Current;
        var availH = ImGui.GetContentRegionAvail().Y;
        using var scroll = ImRaii.Child("##optScroll", new Vector2(0f, availH), false);
        if (!scroll.Success) return;

        var w = ImGui.GetContentRegionAvail().X - Px(8f);

        ImGui.Spacing();
        ImGui.TextColored(t.AccentLight, Loc.T("onboarding.opt_heading"));
        ImGui.Spacing();
        ImGui.TextWrapped(Loc.T("onboarding.opt_intro"));
        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();

        ImGui.Text(Loc.T("onboarding.opt_fav_job"));
        ImGui.SameLine(); HelpTooltip(Loc.T("onboarding.opt_fav_job_tip"));
        if (EnsureJobCombo().Draw(_jobComboIdx, out var newJobIdx))
        {
            _jobComboIdx = newJobIdx;
        }
        ImGui.Spacing();

        EnsureLocationsLoaded();
        ImGui.Text(Loc.T("onboarding.opt_fav_location"));
        ImGui.SameLine(); HelpTooltip(Loc.T("onboarding.opt_fav_location_tip"));
        if (_locationCombo != null)
        {
            if (_locationCombo.Draw(_locationComboIdx, out var newLocIdx))
                _locationComboIdx = newLocIdx;
        }
        else
            ImGui.TextColored(UiColors.Hint, Loc.T("onboarding.opt_location_unavailable"));
        ImGui.Spacing();

        ImGui.Text(Loc.T("onboarding.opt_fav_expansion"));
        ImGui.SameLine(); HelpTooltip(Loc.T("onboarding.opt_fav_expansion_tip"));
        ImGui.SetNextItemWidth(Px(240f));
        ImGui.Combo("##exp", ref _expansionIdx, Expansions, Expansions.Length);
        ImGui.Spacing();

        ImGui.TextColored(t.AccentLight, Loc.T("onboarding.opt_music_heading"));
        ImGui.SameLine(); HelpTooltip(Loc.T("onboarding.opt_music_tip"));
        ImGui.Spacing();
        var musicW = Math.Min(w, Px(280f));
        DrawMusicLinkField(MusicFields[0], Loc.T("onboarding.opt_fav_spotify"), Loc.T("onboarding.opt_fav_spotify_tip"), musicW);
        ImGui.Spacing();
        DrawMusicLinkField(MusicFields[1], Loc.T("onboarding.opt_fav_soundcloud"), Loc.T("onboarding.opt_music_tip"), musicW);
        ImGui.Spacing();
        DrawMusicLinkField(MusicFields[2], Loc.T("onboarding.opt_fav_apple"), Loc.T("onboarding.opt_music_tip"), musicW);
        ImGui.Spacing();
        DrawMusicLinkField(MusicFields[3], Loc.T("onboarding.opt_fav_youtube"), Loc.T("onboarding.opt_music_tip"), musicW);
        ImGui.Spacing();

        ImGui.Text(Loc.T("onboarding.opt_fav_movie"));
        ImGui.SameLine(); HelpTooltip(Loc.T("onboarding.opt_fav_movie_tip"));
        ImGui.SetNextItemWidth(Math.Min(w, Px(280f)));
        ImGui.InputText("##favMovie", ref _favoriteMovie, 128);
        ImGui.Spacing();

        ImGui.Text(Loc.T("onboarding.opt_fav_anime"));
        ImGui.SameLine(); HelpTooltip(Loc.T("onboarding.opt_fav_anime_tip"));
        ImGui.SetNextItemWidth(Math.Min(w, Px(280f)));
        ImGui.InputText("##favAnime", ref _favoriteAnime, 128);
        ImGui.Spacing();

        ImGui.Text(Loc.T("onboarding.opt_fav_ff_character"));
        ImGui.SameLine(); HelpTooltip(Loc.T("onboarding.opt_fav_ff_character_tip"));
        ImGui.SetNextItemWidth(Math.Min(w, Px(280f)));
        ImGui.InputText("##favFFChar", ref _favoriteFFCharacter, 128);
        ImGui.Spacing();

        ImGui.Text(Loc.T("onboarding.opt_weekday_playtimes"));
        ImGui.SameLine(); HelpTooltip(Loc.T("onboarding.opt_weekday_playtimes_tip"));
        ImGui.Spacing();
        DrawOnlineHoursEditor(w, _weekdayHours, "wd");
        ImGui.Spacing();

        ImGui.Text(Loc.T("onboarding.opt_weekend_playtimes"));
        ImGui.SameLine(); HelpTooltip(Loc.T("onboarding.opt_weekend_playtimes_tip"));
        ImGui.Spacing();
        DrawOnlineHoursEditor(w, _weekendHours, "we");
        ImGui.Spacing();

        ImGui.Text(Loc.T("onboarding.opt_sync_tool"));
        ImGui.SameLine(); HelpTooltip(Loc.T("onboarding.opt_sync_tool_tip"));
        ImGui.Spacing();
        var SyncCol = Px(185f);
        for (int i = 0; i < SyncToolLabels.Length; i++)
        {
            if (i % 2 == 1) ImGui.SameLine(SyncCol);
            ImGui.Checkbox($"{SyncToolLabels[i]}##sync{i}", ref _syncToolsSelected[i]);
        }
        ImGui.Spacing();
    }


    private void EnsureLocationsLoaded()
    {
        if (_locationsLoaded) return;
        _locationsLoaded = true;
        try
        {
            var list  = new List<(uint Id, string Name)>();
            var sheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>();
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
            Plugin.Log.Warning(ex, "[Onboarding] Could not load territory locations.");
        }
    }


}
