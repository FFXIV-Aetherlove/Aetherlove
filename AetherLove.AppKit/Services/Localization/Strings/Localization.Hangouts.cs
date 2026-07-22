namespace AetherLove.Services.Localization;

internal static class HangoutsEn
{
    public static readonly System.Collections.Generic.Dictionary<string, string> Strings = new()
    {
        // added after update 1.5.1
        ["common.nav_hangouts"] = "Hangouts",
        ["chat.preview_hangout"] = "Shared a hangout",
        ["hangout.starts_at"] = "Starts at {0}",
        ["hangout.ends_in"] = "Ends in {0}",
        ["hangout.ended"] = "Ended",
        ["hangout.coming_count"] = "{0} coming",
        ["hangout.chip_live"] = "LIVE",
        ["hangout.menu_view"] = "View hangout",
        ["hangout.notif_rsvp"] = "{0} is on their way to your hangout!",
        ["hangout.notif_rsvp_link"] = "View hangout",
        ["hangout.notif_cancelled"] = "A hangout you signed up for was cancelled.",
        ["hangout.notif_ended_early"] = "A hangout you signed up for has ended early.",
        ["hangout.notif_browse_link"] = "Browse hangouts",
        ["hangout.notif_friend_started"] = "Your friend {0} started a hangout!",
        ["hangout.notif_friend_title"] = "Hangout started!",
        ["hangout.share_view"] = "View this hangout",
        ["hangout.share_unavailable"] = "This hangout has ended",
        ["hangout.cat_pve"] = "PvE",
        ["hangout.cat_pvp"] = "PvP",
        ["hangout.cat_rp"] = "Roleplay",
        ["hangout.cat_social"] = "Social",
        ["hangout.cat_fishing"] = "Fishing",
        ["hangout.cat_gposing"] = "GPosing",
        ["hangout.cat_goldsaucer"] = "Gold Saucer",
        ["hangout.cat_deepdungeon"] = "Deep dungeon",
        ["hangout.cat_mahjong"] = "Mahjong",
        ["hangout.cat_tripletriad"] = "Triple Triad",
        ["hangout.cat_treasure"] = "Treasure hunting",
        ["hangout.cat_fates"] = "FATEs",
        ["huberror.hangouts_disabled"] = "Hangouts are currently unavailable.",
        ["huberror.hangout_not_found"] = "This hangout has ended or is unavailable.",
        ["huberror.hangout_already_active"] = "You already have a live hangout. End it before starting a new one.",
        ["huberror.hangout_rsvp_own"] = "You can't sign up for your own hangout.",
        ["huberror.hangout_times_invalid"] = "The start or duration is out of range.",
        ["huberror.hangout_description_too_long"] = "The description exceeds the {0}-character limit.",

        // added after update 2.0.0 (promoted from the Hangouts app pack for the messenger popup)
        ["hangout.status_live"] = "LIVE",
        ["hangout.status_upcoming"] = "Upcoming",
        ["hangout.hosted_by"] = "Hosted by {0}",
        ["hangout.copy_address"] = "Copy address",
        ["hangout.copied"] = "Copied!",
        ["hangout.at_capacity"] = "(at capacity)",
        ["hangout.on_my_way"] = "Sign up",
        ["hangout.on_my_way_undo"] = "Cancel sign-up",
        ["hangout.report_title"] = "Report hangout",
        ["hangout.report_body"] = "Tell us what's wrong with this hangout (venue advertising, paid services, NSFW content...).",
        ["hangout.report_submit"] = "Send report",
        ["hangout.report_thanks"] = "Thank you. Our team will take a look.",
    };
}
