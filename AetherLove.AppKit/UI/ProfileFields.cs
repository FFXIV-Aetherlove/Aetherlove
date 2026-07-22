using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AetherLove.Services.Localization;
using AetherLove.Shared.Profile.Enums;

namespace AetherLove.UI;

/// <summary>Canonical profile-field option arrays and their localized labels.</summary>
internal static class ProfileFields
{
    internal static readonly Race[] RaceValues =
    [
        Race.Hyur,
        Race.Elezen,
        Race.Lalafell,
        Race.Miqote,
        Race.Roegadyn,
        Race.AuRa,
        Race.Hrothgar,
        Race.Viera,
    ];

    internal static readonly Gender[] GenderValues =
    [
        Gender.Male,
        Gender.Female,
        Gender.Other,
    ];

    internal static readonly Region[] RegionValues =
    [
        Region.NorthAmerica,
        Region.Europe,
        Region.Oceania,
        Region.Japan,
    ];

    internal static readonly Language[] LanguageValues =
    [
        Language.English,
        Language.Spanish,
        Language.French,
        Language.Russian,
        Language.German,
        Language.Portuguese,
        Language.Japanese,
    ];

    internal static readonly ContentInterest[] ContentInterestValues =
    [
        ContentInterest.Housing,
        ContentInterest.MusicAndBard,
        ContentInterest.RoleplayingVenues,
        ContentInterest.ClubVenues,
        ContentInterest.Gpose,
        ContentInterest.StoryAndLore,
        ContentInterest.Roulettes,
        ContentInterest.RegularContent,
        ContentInterest.ExtremeTrials,
        ContentInterest.SavageRaiding,
        ContentInterest.UltimateRaiding,
        ContentInterest.DeepDungeons,
        ContentInterest.VariantCriterionDungeons,
        ContentInterest.FieldOperations,
        ContentInterest.Pvp,
        ContentInterest.BlueMage,
        ContentInterest.TripleTriad,
        ContentInterest.Mahjong,
        ContentInterest.TreasureHunts,
        ContentInterest.CraftingAndGathering,
        ContentInterest.Fishing,
        ContentInterest.AchievementHunting,
    ];

    internal static readonly LookingFor[] LookingForValues =
    [
        LookingFor.Chatting,
        LookingFor.CasualPlayBuddies,
        LookingFor.GposeAndGlamour,
        LookingFor.InGameRomance,
        LookingFor.LongTermRelationship,
        LookingFor.RoleplayPartners,
        LookingFor.Erp,
    ];

    internal static readonly Expansion[] ExpansionValues =
    [
        Expansion.None,
        Expansion.ARealmReborn,
        Expansion.Heavensward,
        Expansion.Stormblood,
        Expansion.Shadowbringers,
        Expansion.Endwalker,
        Expansion.Dawntrail,
    ];

    internal static readonly SyncTool[] SyncToolValues =
    [
        SyncTool.Lightless,
        SyncTool.PlayerSync,
        SyncTool.HonseFarm,
        SyncTool.Snowcloak,
    ];

    /// <summary>Sync-tool brand names, parallel to <see cref="SyncToolValues"/>. Not localized.</summary>
    internal static readonly string[] SyncToolLabels =
    [
        "Lightless",
        "PlayerSync",
        "HonseFarm",
        "Snowcloak",
    ];

    internal static readonly Job[] JobValues =
    [
        Job.None,
        Job.Paladin, Job.Warrior, Job.DarkKnight, Job.Gunbreaker,
        Job.WhiteMage, Job.Scholar, Job.Astrologian, Job.Sage,
        Job.Monk, Job.Dragoon, Job.Ninja, Job.Samurai, Job.Reaper, Job.Viper,
        Job.Bard, Job.Machinist, Job.Dancer,
        Job.BlackMage, Job.Summoner, Job.RedMage, Job.Pictomancer,
        Job.BlueMage,
        Job.Carpenter, Job.Blacksmith, Job.Armorer, Job.Goldsmith,
        Job.Leatherworker, Job.Weaver, Job.Alchemist, Job.Culinarian,
        Job.Miner, Job.Botanist, Job.Fisher,
    ];

    // UI-translatable languages (those with an ILanguageService) MUST stay first: the plugin-language pickers
    // render only the leading LanguageProvider.UiLanguageCount; spoken-only ones (Japanese) are appended after.
    internal static readonly (string Name, string Code, string FlagFile)[] LanguageEntries =
    [
        ("English",    "EN", "flag_en.png"),
        ("Spanish",    "ES", "flag_es.png"),
        ("French",     "FR", "flag_fr.png"),
        ("Russian",    "RU", "flag_ru.png"),
        ("German",     "DE", "flag_de.png"),
        ("Portuguese", "PT", "flag_pt.png"),
        ("Japanese",   "JA", "flag_jp.png"),
    ];

    internal static readonly string[] Languages =
        LanguageEntries.Select(e => e.Name).ToArray();

    internal static readonly TimeZoneInfo[] AllTimezones =
        TimeZoneInfo.GetSystemTimeZones().ToArray();

    internal static readonly string[] TimezoneNames =
        AllTimezones.Select(tz => tz.DisplayName).ToArray();

    internal static string[] Regions =>
    [
        Loc.T("onboarding.region_na"),
        Loc.T("onboarding.region_eu"),
        Loc.T("onboarding.region_oceania"),
        Loc.T("onboarding.region_japan"),
    ];

    internal static string[] Races =>
    [
        Loc.T("onboarding.race_hyur"),
        Loc.T("onboarding.race_elezen"),
        Loc.T("onboarding.race_lalafell"),
        Loc.T("onboarding.race_miqote"),
        Loc.T("onboarding.race_roegadyn"),
        Loc.T("onboarding.race_au_ra"),
        Loc.T("onboarding.race_hrothgar"),
        Loc.T("onboarding.race_viera"),
    ];

    internal static string[] Genders =>
    [
        Loc.T("onboarding.gender_male"),
        Loc.T("onboarding.gender_female"),
        Loc.T("onboarding.gender_other"),
    ];

    internal static string[] Expansions =>
    [
        Loc.T("onboarding.expansion_none"),
        "A Realm Reborn", "Heavensward", "Stormblood",
        "Shadowbringers", "Endwalker", "Dawntrail",
    ];

    internal static string[] ContentLabels =>
    [
        Loc.T("onboarding.content_housing"),
        Loc.T("onboarding.content_music_bard"),
        Loc.T("onboarding.content_rp_venues"),
        Loc.T("onboarding.content_club_venues"),
        Loc.T("onboarding.content_gpose"),
        Loc.T("onboarding.content_story_lore"),
        Loc.T("onboarding.content_roulettes"),
        Loc.T("onboarding.content_regular"),
        Loc.T("onboarding.content_extreme_trials"),
        Loc.T("onboarding.content_savage"),
        Loc.T("onboarding.content_ultimate_raiding"),
        Loc.T("onboarding.content_deep_dungeons"),
        Loc.T("onboarding.content_variant_criterion"),
        Loc.T("onboarding.content_field_operations"),
        Loc.T("onboarding.content_pvp"),
        Loc.T("onboarding.content_blue_mage"),
        Loc.T("onboarding.content_triple_triad"),
        Loc.T("onboarding.content_mahjong"),
        Loc.T("onboarding.content_treasure_hunts"),
        Loc.T("onboarding.content_crafting_gathering"),
        Loc.T("onboarding.content_fishing"),
        Loc.T("onboarding.content_achievement_hunting"),
    ];

    internal static string[] LookingForLabels =>
    [
        Loc.T("onboarding.lf_chatting"),
        Loc.T("onboarding.lf_casual_buddies"),
        Loc.T("onboarding.lf_gpose_glamour"),
        Loc.T("onboarding.lf_romance"),
        Loc.T("onboarding.lf_long_term"),
        Loc.T("onboarding.lf_rp_partners"),
        Loc.T("onboarding.lf_erp"),
    ];

    private static string LabelFor<T>(T[] values, string[] labels, T value, string fallback)
        where T : struct, Enum
    {
        var idx = Array.IndexOf(values, value);
        return idx >= 0 && idx < labels.Length ? labels[idx] : fallback;
    }

    internal static string RaceLabel(Race r) => LabelFor(RaceValues, Races, r, string.Empty);
    internal static string GenderLabel(Gender g) => LabelFor(GenderValues, Genders, g, string.Empty);
    internal static string RegionLabel(Region r) => LabelFor(RegionValues, Regions, r, string.Empty);
    internal static string ExpansionLabel(Expansion e) => LabelFor(ExpansionValues, Expansions, e, string.Empty);

    private static List<string>? _jobLabelsCache;
    private static Dalamud.Game.ClientLanguage _jobLabelsLanguage;

    /// <summary>Job display labels parallel to <see cref="JobValues"/>, localized to the FFXIV client language
    /// and cached until that language changes.</summary>
    internal static IReadOnlyList<string> GetJobLabels()
    {
        var lang = PluginLanguageToClientLanguage();
        if (_jobLabelsCache is not null && _jobLabelsLanguage == lang)
        {
            return _jobLabelsCache;
        }

        var ti = CultureInfo.InvariantCulture.TextInfo;
        var sheet = UiHost.DataManager.GetExcelSheet<Lumina.Excel.Sheets.ClassJob>(lang);

        _jobLabelsCache = JobValues.Select(j =>
        {
            if (j == Job.None)
            {
                return "(None)";
            }
            try
            {
                var row = sheet.GetRow((uint)j);
                var name = row.Name.ToString();
                return string.IsNullOrEmpty(name) ? j.ToString() : ti.ToTitleCase(name);
            }
            catch
            {
                return j.ToString();
            }
        }).ToList();
        _jobLabelsLanguage = lang;

        return _jobLabelsCache;
    }

    // FFXIV game data ships only in Japanese/English/German/French; other plugin languages fall back to
    // English job names.
    private static Dalamud.Game.ClientLanguage PluginLanguageToClientLanguage() =>
        UiHost.Configuration.PluginLanguage switch
        {
            "German" => Dalamud.Game.ClientLanguage.German,
            "French" => Dalamud.Game.ClientLanguage.French,
            _ => Dalamud.Game.ClientLanguage.English,
        };
}
