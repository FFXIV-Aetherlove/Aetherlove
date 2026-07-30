using System;
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
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using static AetherLove.UI.OnboardingUi;

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
        var dir = Path.GetDirectoryName(UiHost.PluginInterface.AssemblyLocation.FullName) ?? "";
        for (int i = 0; i < LanguageEntries.Length; i++)
        {
            var path = Path.Combine(dir, "Media", LanguageEntries[i].FlagFile);
            if (File.Exists(path))
                _langFlags[i] = UiHost.TextureProvider.GetFromFile(path);
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
            var langIdx = UiHost.ClientState.ClientLanguage switch
            {
                Dalamud.Game.ClientLanguage.English  => 0,
                Dalamud.Game.ClientLanguage.French   => 2,
                Dalamud.Game.ClientLanguage.German   => 4,
                Dalamud.Game.ClientLanguage.Japanese => 0,
                _                                    => 0,
            };
            _langSelected[langIdx] = true;
            if (langIdx < _filterLanguages.Length)
            {
                _filterLanguages[langIdx] = true;
            }
        }
        catch (Exception ex)
        {
            UiHost.Log.Debug(ex, "[OnboardingScreen] Locale auto-detect failed.");
        }

        try
        {
            var player = UiHost.ObjectTable.LocalPlayer;
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
            UiHost.Log.Debug(ex, "[OnboardingScreen] Race/gender auto-detect failed.");
        }

        // Lumina WorldDCGroupType.Region: 1=JP, 2=NA, 3=EU, 4=OCE.
        try
        {
            var worldId = UiHost.ObjectTable.LocalPlayer?.HomeWorld.RowId ?? 0u;
            if (worldId > 0)
            {
                var worldSheet = UiHost.DataManager.GetExcelSheet<Lumina.Excel.Sheets.World>();
                var dcId = worldSheet.GetRow(worldId).DataCenter.RowId;
                var dcSheet = UiHost.DataManager.GetExcelSheet<Lumina.Excel.Sheets.WorldDCGroupType>();
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
            UiHost.Log.Debug(ex, "[OnboardingScreen] Region auto-detect failed.");
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

    private void DrawStepName()
    {
        DrawHero("love_name", FontAwesomeIcon.Signature, Loc.T("onboarding.hero_name_title"),
            Loc.T("onboarding.hero_name_sub"), 34f);

        var winW = ImGui.GetWindowSize().X;
        ImGui.SetCursorPosX(Px(20f));
        ImGui.SetNextItemWidth(winW - Px(40f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(10f));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(Px(11f), Px(10f)));
        ImGui.InputTextWithHint("##dname", Loc.T("onboarding.profile_display_name"), ref _displayName, 32);
        ImGui.PopStyleVar(2);
        // The dating display name disallows spaces; it seeds from the OS name's first word and stays spaceless.
        if (_displayName.Contains(' '))
        {
            _displayName = _displayName.Replace(" ", string.Empty);
        }

        ImGui.Dummy(new Vector2(0f, Px(6f)));
        ImGui.SetCursorPosX(Px(20f));
        ImGui.PushTextWrapPos(winW - Px(20f));
        ImGui.TextColored(UiColors.Hint, Loc.T("onboarding.profile_display_name_hint"));
        ImGui.PopTextWrapPos();
    }

    private void DrawStepBio()
    {
        DrawHero("love_bio", FontAwesomeIcon.PenNib, Loc.T("onboarding.hero_bio_title"),
            Loc.T("onboarding.hero_bio_sub"), 34f);

        var t = ThemeService.Current;
        var winW = ImGui.GetWindowSize().X;
        var fieldW = winW - Px(40f);

        ImGui.SetCursorPosX(Px(20f));
        ImGui.TextColored(t.AccentLight, Loc.T("onboarding.profile_about_me"));
        ImGui.SameLine();
        {
            var iconH = ImGui.GetTextLineHeight();
            var grinTex = UiHost.EmojiService.GetEmoji("grinning")?.GetWrapOrDefault();
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

        ImGui.SetCursorPosX(Px(20f));
        var bioBefore = _bio;
        InputTextMultilineWithPaste("##bio", ref _bio, EmojiText.MaxBioRawLength, new Vector2(fieldW, Px(96f)));
        if (EmojiText.EffectiveLength(_bio) > EmojiText.MaxBioLength)
        {
            _bio = bioBefore;
        }

        var parsedBio = ParsedMessage.Parse(_bio);
        var effectiveLen = EmojiText.EffectiveLength(_bio);
        ImGui.SetCursorPosX(Px(20f));
        ImGui.TextColored(
            effectiveLen > EmojiText.MaxBioLength ? UiColors.BioOverLimit : UiColors.Hint,
            Loc.T("onboarding.profile_char_count", effectiveLen));

        ImGui.Dummy(new Vector2(0f, Px(8f)));
        ImGui.SetCursorPosX(Px(20f));
        ImGui.TextColored(UiColors.Hint, Loc.T("onboarding.profile_preview"));
        ImGui.SetCursorPosX(Px(20f));
        if (_bio.Length > 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.BioText);
            parsedBio.DrawWrapped("##bioPreview", fieldW);
            ImGui.PopStyleColor();
        }
        else
        {
            ImGui.TextColored(UiColors.BioPlaceholder, Loc.T("onboarding.profile_bio_placeholder"));
        }
    }

    private void DrawStepCharacter()
    {
        DrawHero("love_character", FontAwesomeIcon.User, Loc.T("onboarding.hero_character_title"),
            Loc.T("onboarding.hero_character_sub"), 30f);

        var t = ThemeService.Current;
        var winW = ImGui.GetWindowSize().X;
        var fullW = winW - Px(40f);
        var halfW = (fullW - Px(8f)) * 0.5f;
        var rightX = Px(20f) + halfW + Px(8f);

        ImGui.SetCursorPosX(Px(20f));
        DrawFieldLabel(Loc.T("onboarding.profile_race"), t);
        ImGui.SameLine(rightX);
        DrawFieldLabel(Loc.T("onboarding.profile_gender"), t);

        ImGui.SetCursorPosX(Px(20f));
        ImGui.SetNextItemWidth(halfW);
        var prevRaceIdx = _raceIdx;
        ImGui.Combo("##race", ref _raceIdx, Races, Races.Length);
        if (_raceIdx != prevRaceIdx && IsLalafellSelected())
        {
            ClearAdultFlagsForLalafell();
        }
        ImGui.SameLine(rightX);
        ImGui.SetNextItemWidth(halfW);
        ImGui.Combo("##gender", ref _genderIdx, Genders, Genders.Length);

        ImGui.Dummy(new Vector2(0f, Px(12f)));
        ImGui.SetCursorPosX(Px(20f));
        DrawFieldLabel(Loc.T("onboarding.profile_server_region"), t);
        ImGui.SameLine();
        HelpTooltip(Loc.T("onboarding.profile_server_region_tip"));
        ImGui.SetCursorPosX(Px(20f));
        ImGui.SetNextItemWidth(fullW);
        ImGui.Combo("##region", ref _regionIdx, Regions, Regions.Length);

        ImGui.Dummy(new Vector2(0f, Px(14f)));
        DrawInfoCallout(Loc.T("onboarding.race_gender_warning"), UiColors.WarningAccent,
            FontAwesomeIcon.ExclamationTriangle);
    }

    private void DrawStepLanguages()
    {
        DrawHero("love_languages", FontAwesomeIcon.Language, Loc.T("onboarding.hero_languages_title"),
            Loc.T("onboarding.hero_languages_sub"), 30f);

        ImGui.SetCursorPosX(Px(20f));
        ImGui.PushTextWrapPos(ImGui.GetWindowSize().X - Px(20f));
        ImGui.TextColored(UiColors.Muted with { W = 0.80f }, Loc.T("onboarding.profile_languages_hint"));
        ImGui.PopTextWrapPos();

        ImGui.Dummy(new Vector2(0f, Px(10f)));
        ImGui.SetCursorPosX(Px(20f));
        DrawLanguagePills();
    }

    private void DrawStepInterests()
    {
        DrawHero("love_interests", FontAwesomeIcon.Gamepad, Loc.T("onboarding.hero_interests_title"),
            Loc.T("onboarding.hero_interests_sub"), 30f);

        ImGui.SetCursorPosX(Px(20f));
        ImGui.PushTextWrapPos(ImGui.GetWindowSize().X - Px(20f));
        ImGui.TextColored(UiColors.Muted with { W = 0.80f }, Loc.T("onboarding.profile_content_hint"));
        ImGui.PopTextWrapPos();

        ImGui.Dummy(new Vector2(0f, Px(10f)));
        var col2 = Px(20f) + Px(190f);
        var colW = MathF.Min(Px(190f), ImGui.GetWindowSize().X - col2 - Px(20f)) - Px(6f);
        for (int i = 0; i < ContentLabels.Length; i++)
        {
            if (i % 2 == 0)
            {
                ImGui.SetCursorPosX(Px(20f));
            }
            else
            {
                ImGui.SameLine(col2);
            }
            CheckboxTruncated($"ci{i}", ContentLabels[i], ref _contentInterests[i], colW);
        }
    }

    private void DrawStepLookingFor()
    {
        DrawHero("love_lookingfor", FontAwesomeIcon.HandHoldingHeart, Loc.T("onboarding.hero_lookingfor_title"),
            Loc.T("onboarding.hero_lookingfor_sub"), 30f);

        var t = ThemeService.Current;
        var winW = ImGui.GetWindowSize().X;

        ImGui.SetCursorPosX(Px(20f));
        ImGui.PushTextWrapPos(winW - Px(20f));
        ImGui.TextColored(UiColors.Muted with { W = 0.80f }, Loc.T("onboarding.profile_looking_for_hint"));
        ImGui.PopTextWrapPos();

        ImGui.Dummy(new Vector2(0f, Px(10f)));
        var lalafell = IsLalafellSelected();
        for (int i = 0; i < LookingForLabels.Length; i++)
        {
            if (lalafell && LookingForValues[i] == LookingFor.Erp)
            {
                continue;
            }

            var wasChecked = _lookingFor[i];
            ImGui.SetCursorPosX(Px(20f));
            ImGui.Checkbox($"{LookingForLabels[i]}##lf{i}", ref _lookingFor[i]);

            // Picking ERP turns NSFW on; the NSFW section below explains and shows the locked state.
            if (LookingForValues[i] == LookingFor.Erp && !wasChecked && _lookingFor[i])
            {
                _nsfwOptIn = true;
            }
        }

        ImGui.Dummy(new Vector2(0f, Px(14f)));
        DrawNsfwOptIn(t, lalafell, winW);
    }

    /// <summary>The NSFW opt-in block: hidden/forced off for Lalafell, force-enabled and locked while "ERP" is a
    /// selected intent, otherwise a plain opt-in checkbox.</summary>
    private void DrawNsfwOptIn(ThemeDefinition t, bool lalafell, float winW)
    {
        ImGui.SetCursorPosX(Px(20f));
        DrawFieldLabel(Loc.T("onboarding.profile_nsfw_heading"), t);

        if (lalafell)
        {
            ImGui.SetCursorPosX(Px(20f));
            ImGui.PushTextWrapPos(winW - Px(20f));
            ImGui.TextColored(new Vector4(0.65f, 0.65f, 0.65f, 1f), Loc.T("onboarding.profile_nsfw_lalafell"));
            ImGui.PopTextWrapPos();
            return;
        }

        var erpSelected = IsLookingForErp();
        if (erpSelected)
        {
            _nsfwOptIn = true;
        }

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
        ImGui.SetCursorPosX(Px(20f));
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
            ImGui.SetCursorPosX(Px(20f));
            ImGui.PushTextWrapPos(winW - Px(20f));
            ImGui.TextColored(new Vector4(0.65f, 0.65f, 0.65f, 1f), Loc.T("onboarding.profile_nsfw_locked"));
            ImGui.PopTextWrapPos();
        }

        // The full explanation is tucked into an accordion so the step stays light.
        ImGui.Dummy(new Vector2(0f, Px(2f)));
        ImGui.SetCursorPosX(Px(20f));
        ImGui.PushStyleColor(ImGuiCol.Text, t.AccentLight);
        var open = ImGui.TreeNodeEx(Loc.T("onboarding.profile_nsfw_learn_more"), ImGuiTreeNodeFlags.SpanAvailWidth);
        ImGui.PopStyleColor();
        if (open)
        {
            ImGui.PushTextWrapPos(winW - Px(20f));
            ImGui.TextColored(UiColors.Muted with { W = 0.85f }, Loc.T("onboarding.profile_nsfw_explainer"));
            ImGui.PopTextWrapPos();
            ImGui.TreePop();
        }
    }
}
