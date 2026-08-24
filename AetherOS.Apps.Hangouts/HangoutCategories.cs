using System.Linq;
using AetherLove.Services.Localization;
using AetherLove.Shared.Hangouts;
using AetherLove.UI;
using Dalamud.Interface;

namespace AetherOS.Apps.Hangouts;

/// <summary>The app's view of the activity list. <see cref="HangoutCategory.WatchParty"/> is filtered out of
/// the creatable set on purpose: a watch party only ever comes from "Publish as hangout" inside Echo, and the
/// server refuses the category without a room to back it.</summary>
internal static class HangoutCategories
{
    internal static readonly HangoutCategory[] CreatableValues =
        HangoutFields.CategoryValues
            .Where(c => c is not (HangoutCategory.WatchParty or HangoutCategory.AetherParty))
            .ToArray();

    /// <summary>What the directory can be filtered by. Watch parties belong here even though nobody can
    /// create one from the form, or a filtered directory would silently hide every one of them.</summary>
    internal static readonly HangoutCategory[] FilterValues =
        [.. CreatableValues, HangoutCategory.WatchParty, HangoutCategory.AetherParty];

    internal static string[] CreatableLabels() => CreatableValues.Select(Label).ToArray();

    internal static string[] FilterLabels() => FilterValues.Select(Label).ToArray();

    internal static string Label(HangoutCategory category) => category switch
    {
        HangoutCategory.WatchParty => Loc.T("hangout.cat_watchparty"),
        HangoutCategory.AetherParty => Loc.T("hangout.cat_aetherparty"),
        _ => HangoutFields.CategoryLabel(category),
    };

    internal static FontAwesomeIcon Icon(HangoutCategory category) => category switch
    {
        HangoutCategory.WatchParty => FontAwesomeIcon.Tv,
        HangoutCategory.AetherParty => FontAwesomeIcon.UserFriends,
        _ => HangoutFields.CategoryIcon(category),
    };
}
