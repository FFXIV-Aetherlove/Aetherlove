using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using AetherLove.Emoji;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Shared;
using AetherLove.Shared.Profile.Enums;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;

namespace AetherLove.Screens;

public partial class OnboardingScreen
{
    private string _displayName = "";
    private string _bio = "";

    private readonly ISharedImmediateTexture?[] _langFlags =
        new ISharedImmediateTexture?[LanguageEntries.Length];
    private bool _langFlagsLoaded;

    private void EnsureLangFlags()
    {
        if (_langFlagsLoaded) return;
        _langFlagsLoaded = true;
        var dir = Path.GetDirectoryName(Plugin.PluginInterface.AssemblyLocation.FullName) ?? "";
        for (int i = 0; i < LanguageEntries.Length; i++)
        {
            var path = Path.Combine(dir, "Media", LanguageEntries[i].FlagFile);
            if (File.Exists(path))
                _langFlags[i] = Plugin.TextureProvider.GetFromFile(path);
        }
    }


    private int _regionIdx;
    private int _raceIdx;
    private int _genderIdx;
    private readonly bool[] _langSelected = new bool[LanguageEntries.Length];
    private readonly bool[] _contentInterests = new bool[ContentLabels.Length];
    private readonly bool[] _lookingFor = new bool[LookingForLabels.Length];
    private bool _nsfwOptIn;
    private int _timezoneIdx;
    private OtterGui.Widgets.ClippedSelectableCombo<string>? _timezoneCombo;

    private OtterGui.Widgets.ClippedSelectableCombo<string> EnsureTimezoneCombo() =>
        _timezoneCombo ??= new OtterGui.Widgets.ClippedSelectableCombo<string>(
            "tzcombo", "##tzcombo", Px(340f), TimezoneNames.ToList(), s => s);

    private readonly EmojiPickerPopup _bioEmojiPicker = new();

    private bool IsLookingForErp()
    {
        var count = Math.Min(_lookingFor.Length, LookingForValues.Length);
        for (var i = 0; i < count; i++)
        {
            if (_lookingFor[i] && LookingForValues[i] == LookingFor.Erp)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>True when the selected race is Lalafell, which gates adult features off.</summary>
    private bool IsLalafellSelected() =>
        _raceIdx >= 0 && _raceIdx < RaceValues.Length && RaceValues[_raceIdx] == Race.Lalafell;

    private void ClearAdultFlagsForLalafell()
    {
        for (var i = 0; i < LookingForValues.Length && i < _lookingFor.Length; i++)
        {
            if (LookingForValues[i] == LookingFor.Erp)
            {
                _lookingFor[i] = false;
            }
        }
        _nsfwOptIn = false;
    }


    private void AutoDetectDefaults()
    {
        _timezoneIdx = Math.Max(0,
            Array.FindIndex(AllTimezones, tz => tz.Id == TimeZoneInfo.Local.Id));

        // LanguageEntries: 0=English, 1=Spanish, 2=French, 3=Russian, 4=German
        try
        {
            var langIdx = Plugin.ClientState.ClientLanguage switch
            {
                Dalamud.Game.ClientLanguage.English  => 0,
                Dalamud.Game.ClientLanguage.French   => 2,
                Dalamud.Game.ClientLanguage.German   => 4,
                Dalamud.Game.ClientLanguage.Japanese => 0,
                _                                    => 0,
            };
            _pluginLangIdx = langIdx;
            _langSelected[langIdx] = true;
            if (langIdx < _filterLanguages.Length)
            {
                _filterLanguages[langIdx] = true;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "[OnboardingScreen] Locale auto-detect failed.");
        }

        try
        {
            var player = Plugin.ObjectTable.LocalPlayer;
            if (player != null)
            {
                byte raceId = player.Customize[0]; // 1–8
                if (raceId >= 1 && raceId <= Races.Length)
                {
                    _raceIdx = raceId - 1;
                }

                byte genderByte = player.Customize[1]; // 0=male 1=female
                if (genderByte <= 1)
                {
                    _genderIdx = genderByte;
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "[OnboardingScreen] Race/gender auto-detect failed.");
        }

        // Lumina WorldDCGroupType.Region: 1=JP, 2=NA, 3=EU, 4=OCE.
        try
        {
            var worldId = Plugin.ObjectTable.LocalPlayer?.HomeWorld.RowId ?? 0u;
            if (worldId > 0)
            {
                var worldSheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.World>();
                var dcId = worldSheet.GetRow(worldId).DataCenter.RowId;
                var dcSheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.WorldDCGroupType>();
                var regionId = dcSheet.GetRow(dcId).Region.RowId;

                _regionIdx = regionId switch
                {
                    2 => 0, // North America
                    3 => 1, // Europe
                    4 => 2, // Oceania
                    1 => 3, // Japan
                    _ => 0, // default to North America when the region can't be detected
                };
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "[OnboardingScreen] Region auto-detect failed.");
        }
    }

    private void DrawLanguagePills()
    {
        EnsureLangFlags();
        DrawLanguagePillsCore(
            _langFlags,
            flagW: Px(36f),
            flagH: Px(27f),
            useCode: true,
            idPrefix: "lang",
            isSelected: i => _langSelected[i],
            onToggle: ToggleSpokenLanguage);
    }

    private void ToggleSpokenLanguage(int i)
    {
        _langSelected[i] = !_langSelected[i];
        if (_langSelected[i] && i < _filterLanguages.Length)
        {
            _filterLanguages[i] = true;
        }
    }


    private void DrawStepProfile()
    {
        var availH = ImGui.GetContentRegionAvail().Y;
        using var scroll = ImRaii.Child("##profileScroll", new Vector2(0f, availH), false);
        if (!scroll.Success) return;

        var t = ThemeService.Current;

        ImGui.Spacing();
        ImGui.TextWrapped(Loc.T("onboarding.profile_intro"));
        ImGui.Spacing();

        DrawSectionHeading(Loc.T("onboarding.profile_identity"), t);

        DrawFieldLabel(Loc.T("onboarding.profile_display_name"), t);
        ImGui.SameLine(); HelpTooltip(Loc.T("onboarding.profile_display_name_tip"));
        ImGui.SetNextItemWidth(Px(260f));
        ImGui.InputText("##dname", ref _displayName, 32);
        if (_displayName.Contains(' ')) _displayName = _displayName.Replace(" ", "");
        ImGui.TextColored(UiColors.Hint, Loc.T("onboarding.profile_display_name_hint"));
        ImGui.Spacing();

        DrawFieldLabel(Loc.T("onboarding.profile_about_me"), t);
        ImGui.SameLine(); HelpTooltip(Loc.T("onboarding.profile_about_me_tip"));
        ImGui.SameLine();
        {
            var iconH   = ImGui.GetTextLineHeight();
            var grinTex = Plugin.EmojiService.GetEmoji("grinning")?.GetWrapOrDefault();
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Px(2f, 2f));
            var clicked = grinTex != null
                ? ImGui.ImageButton(grinTex.Handle, new Vector2(iconH - Px(4f)))
                : ImGui.SmallButton(":)##bioEmoji");
            ImGui.PopStyleVar();
            _bioEmojiPicker.Draw();
            if (clicked) _bioEmojiPicker.Open(name =>
            {
                var add = $":{name}: ";
                if (EmojiText.EffectiveLength(_bio + add) <= EmojiText.MaxBioLength)
                    _bio += add;
            });
        }
        ImGui.SetNextItemWidth(Px(340f));
        var bioBefore = _bio;
        InputTextMultilineWithPaste("##bio", ref _bio, EmojiText.MaxBioRawLength, Px(340f, 68f));
        if (EmojiText.EffectiveLength(_bio) > EmojiText.MaxBioLength)
        {
            _bio = bioBefore;
        }

        var parsedBio    = ParsedMessage.Parse(_bio);
        var effectiveLen = EmojiText.EffectiveLength(_bio);
        ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X);
        ImGui.TextColored(
            effectiveLen > EmojiText.MaxBioLength
                ? UiColors.BioOverLimit
                : UiColors.Hint,
            Loc.T("onboarding.profile_char_count", effectiveLen));
        ImGui.PopTextWrapPos();

        ImGui.Spacing();
        ImGui.TextColored(UiColors.Hint, Loc.T("onboarding.profile_preview"));
        var previewW = Px(340f);
        if (_bio.Length > 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.BioText);
            parsedBio.DrawWrapped("##bioPreview", previewW);
            ImGui.PopStyleColor();
        }
        else
        {
            ImGui.TextColored(UiColors.BioPlaceholder, Loc.T("onboarding.profile_bio_placeholder"));
        }

        DrawSectionHeading(Loc.T("onboarding.profile_location"), t);

        DrawFieldLabel(Loc.T("onboarding.profile_server_region"), t);
        ImGui.SameLine(); HelpTooltip(Loc.T("onboarding.profile_server_region_tip"));
        ImGui.SetNextItemWidth(Px(220f));
        ImGui.Combo("##region", ref _regionIdx, Regions, Regions.Length);

        DrawSectionHeading(Loc.T("onboarding.profile_character"), t);

        var halfW = (ImGui.GetContentRegionAvail().X - Px(8f)) * 0.5f;
        DrawFieldLabel(Loc.T("onboarding.profile_race"), t);
        ImGui.SameLine(halfW + Px(8f));
        DrawFieldLabel(Loc.T("onboarding.profile_gender"), t);
        ImGui.SetNextItemWidth(halfW);
        var prevRaceIdx = _raceIdx;
        ImGui.Combo("##race", ref _raceIdx, Races, Races.Length);
        if (_raceIdx != prevRaceIdx && IsLalafellSelected())
        {
            ClearAdultFlagsForLalafell();
        }
        ImGui.SameLine(halfW + Px(8f));
        ImGui.SetNextItemWidth(halfW);
        ImGui.Combo("##gender", ref _genderIdx, Genders, Genders.Length);

        ImGui.Spacing();
        DrawWarningCard(Loc.T("onboarding.race_gender_warning"), ImGui.GetContentRegionAvail().X);

        ImGui.Spacing();
        ImGui.Spacing();

        DrawSectionHeading(Loc.T("onboarding.profile_languages_speak"), t);

        ImGui.TextColored(UiColors.Muted with { W = 0.80f },
            Loc.T("onboarding.profile_languages_hint"));
        ImGui.Spacing();
        DrawLanguagePills();

        ImGui.Spacing();
        ImGui.Spacing();

        DrawSectionHeading(Loc.T("onboarding.profile_content_heading"), t);

        ImGui.PushTextWrapPos(0f);
        ImGui.TextColored(UiColors.Muted with { W = 0.80f },
            Loc.T("onboarding.profile_content_hint"));
        ImGui.PopTextWrapPos();
        ImGui.Spacing();
        var ContentCol = Px(185f);
        for (int i = 0; i < ContentLabels.Length; i++)
        {
            if (i % 2 == 1) ImGui.SameLine(ContentCol);
            ImGui.Checkbox($"{ContentLabels[i]}##ci{i}", ref _contentInterests[i]);
        }
        ImGui.Spacing();
        ImGui.Spacing();

        DrawSectionHeading(Loc.T("onboarding.profile_looking_for_heading"), t);

        ImGui.PushTextWrapPos(0f);
        ImGui.TextColored(UiColors.Muted with { W = 0.80f },
            Loc.T("onboarding.profile_looking_for_hint"));
        ImGui.PopTextWrapPos();
        ImGui.Spacing();
        var lalafell = IsLalafellSelected();
        for (int i = 0; i < LookingForLabels.Length; i++)
        {
            if (lalafell && LookingForValues[i] == LookingFor.Erp)
            {
                continue;
            }

            var wasChecked = _lookingFor[i];
            ImGui.Checkbox($"{LookingForLabels[i]}##lf{i}", ref _lookingFor[i]);

            if (LookingForValues[i] == LookingFor.Erp)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.65f, 0.65f, 0.65f, 1f));
                ImGui.TextWrapped(Loc.T("onboarding.profile_erp_enables_nsfw"));
                ImGui.PopStyleColor();

                if (!wasChecked && _lookingFor[i])
                {
                    _nsfwOptIn = true;
                }
            }
        }
        ImGui.Spacing();
        ImGui.Spacing();

        if (lalafell)
        {
            DrawSectionHeading(Loc.T("onboarding.profile_nsfw_heading"), t);
            ImGui.PushTextWrapPos(0f);
            ImGui.TextColored(new Vector4(0.65f, 0.65f, 0.65f, 1f),
                Loc.T("onboarding.profile_nsfw_lalafell"));
            ImGui.PopTextWrapPos();
        }
        else
        {
            DrawSectionHeading(Loc.T("onboarding.profile_nsfw_heading"), t);

            ImGui.TextWrapped(Loc.T("onboarding.profile_nsfw_explainer"));
            ImGui.Spacing();

            var erpSelected = IsLookingForErp();
            if (erpSelected)
            {
                _nsfwOptIn = true;
            }

            // Snapshot so the style Push and Pop below use the same value (the checkbox can toggle it).
            var nsfwStyled = _nsfwOptIn;
            if (nsfwStyled)
            {
                ImGui.PushStyleColor(ImGuiCol.FrameBg, UiColors.NsfwFrameBg);
                ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, UiColors.NsfwFrameBgHovered);
                ImGui.PushStyleColor(ImGuiCol.FrameBgActive, UiColors.NsfwFrameBgActive);
            }
            if (erpSelected)
            {
                ImGui.BeginDisabled();
            }
            ImGui.Checkbox(Loc.T("onboarding.profile_nsfw_checkbox"), ref _nsfwOptIn);
            if (erpSelected)
            {
                ImGui.EndDisabled();
            }
            if (nsfwStyled)
            {
                ImGui.PopStyleColor(3);
            }

            if (erpSelected)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.65f, 0.65f, 0.65f, 1f));
                ImGui.TextWrapped(Loc.T("onboarding.profile_nsfw_locked"));
                ImGui.PopStyleColor();
            }
        }

        ImGui.Spacing();
        ImGui.Spacing();

        DrawSectionHeading(Loc.T("onboarding.profile_timezone"), t);

        ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X);
        ImGui.TextColored(UiColors.Muted with { W = 0.80f },
            Loc.T("onboarding.profile_timezone_hint"));
        ImGui.PopTextWrapPos();
        ImGui.Spacing();
        if (EnsureTimezoneCombo().Draw(_timezoneIdx, out var newTzIdx))
        {
            _timezoneIdx = newTzIdx;
        }
        ImGui.Spacing();
    }
}
