using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove.Services;
using AetherLove.Services.Localization;
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

    private string _spotifyInput     = "";
    private string _spotifyTrackId   = "";
    private string _spotifyTrackName = "";
    private bool   _spotifyFetching;

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
            ImGui.TextColored(new Vector4(0.52f, 0.52f, 0.52f, 0.85f), Loc.T("onboarding.opt_location_unavailable"));
        ImGui.Spacing();

        ImGui.Text(Loc.T("onboarding.opt_fav_expansion"));
        ImGui.SameLine(); HelpTooltip(Loc.T("onboarding.opt_fav_expansion_tip"));
        ImGui.SetNextItemWidth(Px(240f));
        ImGui.Combo("##exp", ref _expansionIdx, Expansions, Expansions.Length);
        ImGui.Spacing();

        ImGui.Text(Loc.T("onboarding.opt_fav_spotify"));
        ImGui.SameLine(); HelpTooltip(Loc.T("onboarding.opt_fav_spotify_tip"));
        ImGui.Spacing();
        ImGui.TextColored(new Vector4(0.52f, 0.52f, 0.52f, 0.85f), SpotifyTrack.DisplayPrefix);
        ImGui.SameLine(0f, 0f);
        ImGui.SetNextItemWidth(Px(160f));
        if (ImGui.InputText("##spotify", ref _spotifyInput, 256))
            ProcessSpotifyInput();

        if (_spotifyFetching)
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), Loc.T("onboarding.opt_spotify_fetching"));
        else if (_spotifyTrackName.Length > 0)
        {
            ImGui.PushTextWrapPos(0f);
            ImGui.TextColored(new Vector4(0.35f, 0.85f, 0.45f, 1f), $"  {_spotifyTrackName}");
            ImGui.PopTextWrapPos();
        }
        else if (_spotifyTrackId.Length > 0)
            ImGui.TextColored(new Vector4(0.52f, 0.52f, 0.52f, 0.85f), Loc.T("onboarding.opt_spotify_track_id", _spotifyTrackId));
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


    private void ProcessSpotifyInput()
    {
        if (!SpotifyTrack.TryParseId(_spotifyInput, out var trackId))
        {
            _spotifyTrackId = ""; _spotifyTrackName = "";
            return;
        }
        _spotifyInput = trackId; // collapse a pasted URL down to the bare id in the box

        if (trackId == _spotifyTrackId) return;
        _spotifyTrackId   = trackId;
        _spotifyTrackName = "";
        _spotifyFetching  = true;
        _ = FetchSpotifyTitleAsync(trackId);
    }

    private async Task FetchSpotifyTitleAsync(string trackId)
    {
        try
        {
            _spotifyTrackName = await SpotifyTrack.FetchTrackLabelAsync(trackId).ConfigureAwait(false) ?? string.Empty;
        }
        catch
        {
            _spotifyTrackName = Loc.T("onboarding.opt_spotify_fetch_failed");
        }
        finally
        {
            _spotifyFetching = false;
        }
    }
}
