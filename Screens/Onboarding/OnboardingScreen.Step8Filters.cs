using System;
using System.Linq;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;

namespace AetherLove.Screens;

public partial class OnboardingScreen
{
    private readonly bool[] _filterRaces = new bool[Races.Length];
    private readonly bool[] _filterGenders = new bool[Genders.Length];
    private readonly bool[] _filterRegions = new bool[Regions.Length - 1]; // excludes "Prefer not to say"
    private readonly bool[] _filterLanguages = new bool[LanguageEntries.Length];

    private void ResetFilters()
    {
        for (int i = 0; i < _filterRaces.Length; i++)
        {
            _filterRaces[i] = true;
        }
        for (int i = 0; i < _filterGenders.Length; i++)
        {
            _filterGenders[i] = true;
        }
        for (int i = 0; i < _filterRegions.Length; i++)
        {
            _filterRegions[i] = false;
        }
        var ownRegion = _regionIdx < _filterRegions.Length ? _regionIdx : 0;
        _filterRegions[ownRegion] = true;
        for (int i = 0; i < _filterLanguages.Length; i++)
        {
            _filterLanguages[i] = i < _langSelected.Length && _langSelected[i];
        }
    }

    private void DrawStepFilters()
    {
        var t = ThemeService.Current;
        var muted = UiColors.Muted with { W = 0.75f };

        ImGui.Spacing();
        ImGui.TextWrapped(Loc.T("onboarding.filters_intro"));
        ImGui.Spacing();

        DrawSectionHeading(Loc.T("onboarding.filters_heading"), t);

        DrawFieldLabel(Loc.T("onboarding.filters_race"), t);
        ImGui.SameLine(0, Px(10f));
        if (ImGui.SmallButton($"{Loc.T("onboarding.filters_all")}##allRace"))
        {
            for (int i = 0; i < _filterRaces.Length; i++)
            {
                _filterRaces[i] = true;
            }
        }
        ImGui.SameLine(0, Px(4f));
        if (ImGui.SmallButton($"{Loc.T("onboarding.filters_none")}##noneRace"))
        {
            for (int i = 0; i < _filterRaces.Length; i++)
            {
                _filterRaces[i] = false;
            }
        }

        var raceCol = Px(155f);
        for (int i = 0; i < Races.Length; i++)
        {
            if (i % 2 == 1)
            {
                ImGui.SameLine(raceCol);
            }
            ImGui.Checkbox($"{Races[i]}##fr{i}", ref _filterRaces[i]);
        }
        if (!_filterRaces.Any(x => x))
        {
            ImGui.TextColored(muted, Loc.T("onboarding.filters_any_race"));
        }
        ImGui.Spacing();
        ImGui.Spacing();

        DrawFieldLabel(Loc.T("onboarding.filters_gender"), t);
        ImGui.SameLine(0, Px(10f));
        if (ImGui.SmallButton($"{Loc.T("onboarding.filters_all")}##allGender"))
        {
            for (int i = 0; i < _filterGenders.Length; i++)
            {
                _filterGenders[i] = true;
            }
        }
        ImGui.SameLine(0, Px(4f));
        if (ImGui.SmallButton($"{Loc.T("onboarding.filters_none")}##noneGender"))
        {
            for (int i = 0; i < _filterGenders.Length; i++)
            {
                _filterGenders[i] = false;
            }
        }

        var genderCol = Px(155f);
        for (int i = 0; i < Genders.Length; i++)
        {
            if (i % 2 == 1)
            {
                ImGui.SameLine(genderCol);
            }
            ImGui.Checkbox($"{Genders[i]}##fg{i}", ref _filterGenders[i]);
        }
        if (!_filterGenders.Any(x => x))
        {
            ImGui.TextColored(muted, Loc.T("onboarding.filters_any_gender"));
        }
        ImGui.Spacing();
        ImGui.Spacing();

        DrawFieldLabel(Loc.T("onboarding.filters_region"), t);
        ImGui.SameLine(0, Px(10f));
        if (ImGui.SmallButton($"{Loc.T("onboarding.filters_all")}##allRegion"))
        {
            for (int i = 0; i < _filterRegions.Length; i++)
            {
                _filterRegions[i] = true;
            }
        }
        ImGui.SameLine(0, Px(4f));
        if (ImGui.SmallButton($"{Loc.T("onboarding.filters_none")}##noneRegion"))
        {
            for (int i = 0; i < _filterRegions.Length; i++)
            {
                _filterRegions[i] = false;
            }
        }

        var regionCol = Px(155f);
        for (int i = 0; i < _filterRegions.Length; i++)
        {
            if (i % 2 == 1)
            {
                ImGui.SameLine(regionCol);
            }
            ImGui.Checkbox($"{Regions[i]}##fR{i}", ref _filterRegions[i]);
        }
        if (!_filterRegions.Any(x => x))
        {
            ImGui.TextColored(muted, Loc.T("onboarding.filters_any_region"));
        }
        ImGui.Spacing();
        ImGui.Spacing();

        DrawFieldLabel(Loc.T("onboarding.filters_spoken_language"), t);
        ImGui.SameLine(); HelpTooltip(Loc.T("onboarding.filters_language_tip"));
        ImGui.SameLine(0, Px(10f));
        if (ImGui.SmallButton($"{Loc.T("onboarding.filters_clear")}##clrLang"))
        {
            for (int i = 0; i < _filterLanguages.Length; i++)
            {
                _filterLanguages[i] = false;
            }
        }

        EnsureLangFlags();
        ImGui.Spacing();
        DrawFilterLanguagePills();

        if (!_filterLanguages.Any(x => x))
        {
            ImGui.TextColored(muted, Loc.T("onboarding.filters_any_language"));
        }
        ImGui.Spacing();
    }

    private void DrawFilterLanguagePills() =>
        DrawLanguagePillsCore(
            _langFlags,
            flagW: Px(36f),
            flagH: Px(27f),
            useCode: true,
            idPrefix: "filterLang",
            isSelected: i => _filterLanguages[i],
            onToggle: i => _filterLanguages[i] = !_filterLanguages[i]);
}
