using System;
using AetherLove.Services.Localization;
using AetherLove.Shared.Hangouts;
using Dalamud.Interface;

namespace AetherLove.UI;

internal static class HangoutFields
{
    internal static readonly HangoutCategory[] CategoryValues =
    [
        HangoutCategory.Pve,
        HangoutCategory.Pvp,
        HangoutCategory.Rp,
        HangoutCategory.Social,
        HangoutCategory.Fishing,
        HangoutCategory.Gposing,
        HangoutCategory.GoldSaucer,
        HangoutCategory.DeepDungeon,
        HangoutCategory.Mahjong,
        HangoutCategory.TripleTriad,
        HangoutCategory.TreasureHunting,
        HangoutCategory.Fates,
    ];

    internal static string[] CategoryLabels() =>
    [
        Loc.T("hangout.cat_pve"),
        Loc.T("hangout.cat_pvp"),
        Loc.T("hangout.cat_rp"),
        Loc.T("hangout.cat_social"),
        Loc.T("hangout.cat_fishing"),
        Loc.T("hangout.cat_gposing"),
        Loc.T("hangout.cat_goldsaucer"),
        Loc.T("hangout.cat_deepdungeon"),
        Loc.T("hangout.cat_mahjong"),
        Loc.T("hangout.cat_tripletriad"),
        Loc.T("hangout.cat_treasure"),
        Loc.T("hangout.cat_fates"),
    ];

    internal static string CategoryLabel(HangoutCategory category)
    {
        var idx = Array.IndexOf(CategoryValues, category);
        return idx >= 0 ? CategoryLabels()[idx] : category.ToString();
    }

    internal static FontAwesomeIcon CategoryIcon(HangoutCategory category) => category switch
    {
        HangoutCategory.Pve => FontAwesomeIcon.FistRaised,
        HangoutCategory.Pvp => FontAwesomeIcon.Crosshairs,
        HangoutCategory.Rp => FontAwesomeIcon.TheaterMasks,
        HangoutCategory.Social => FontAwesomeIcon.Users,
        HangoutCategory.Fishing => FontAwesomeIcon.Fish,
        HangoutCategory.Gposing => FontAwesomeIcon.Camera,
        HangoutCategory.GoldSaucer => FontAwesomeIcon.Dice,
        HangoutCategory.DeepDungeon => FontAwesomeIcon.Dungeon,
        HangoutCategory.Mahjong => FontAwesomeIcon.Th,
        HangoutCategory.TripleTriad => FontAwesomeIcon.Clone,
        HangoutCategory.TreasureHunting => FontAwesomeIcon.Gem,
        HangoutCategory.Fates => FontAwesomeIcon.Meteor,
        _ => FontAwesomeIcon.MapMarkerAlt,
    };

    internal static string FormatAddress(HangoutSummaryDto h)
    {
        var region = Services.GameWorldData.RegionOfDataCenter(h.DataCenter) switch
        {
            Shared.Profile.Enums.Region.NorthAmerica => "NA",
            Shared.Profile.Enums.Region.Europe => "EU",
            Shared.Profile.Enums.Region.Japan => "JPN",
            Shared.Profile.Enums.Region.Oceania => "OCE",
            _ => null,
        };
        var body = $"{h.DataCenter}, {h.World}, {h.Location}";
        return region is null ? body : $"{region}, {body}";
    }

    internal static bool IsLiveNow(HangoutSummaryDto h) => h.StartUtc <= DateTimeOffset.UtcNow;

    internal static bool IsAtCapacity(HangoutSummaryDto h) =>
        h.MaxAttendees is { } cap && h.RsvpCount >= cap;

    /// <summary>"3", "3/5", or "7/5" (the overshoot renders together with an "at capacity" marker).</summary>
    internal static string CountLabel(HangoutSummaryDto h) =>
        h.MaxAttendees is { } cap ? $"{h.RsvpCount}/{cap}" : h.RsvpCount.ToString();

    /// <summary>"1h 20m" / "35m" until the start, for the directory countdown.</summary>
    internal static string StartsInLabel(HangoutSummaryDto h)
    {
        var left = h.StartUtc - DateTimeOffset.UtcNow;
        if (left <= TimeSpan.Zero)
        {
            return "0m";
        }
        return left.TotalHours >= 1 ? $"{(int)left.TotalHours}h {left.Minutes:00}m" : $"{left.Minutes}m";
    }

    /// <summary>"Starts at 20:30" for upcoming, or "ends in 1h 12m" while live, in the viewer's local time.</summary>
    internal static string TimeLabel(HangoutSummaryDto h)
    {
        var now = DateTimeOffset.UtcNow;
        if (h.StartUtc > now)
        {
            return Loc.T("hangout.starts_at", h.StartUtc.ToLocalTime().ToString("HH:mm"));
        }
        var left = h.EndUtc - now;
        if (left <= TimeSpan.Zero)
        {
            return Loc.T("hangout.ended");
        }
        return left.TotalHours >= 1
            ? Loc.T("hangout.ends_in", $"{(int)left.TotalHours}h {left.Minutes:00}m")
            : Loc.T("hangout.ends_in", $"{left.Minutes}m");
    }
}
