namespace AetherLove.Services.Localization;

internal static class NotificationsEn
{
    public static readonly System.Collections.Generic.Dictionary<string, string> Strings = new()
    {
        ["notif.new_message"] = "You have a new message.",
        ["notif.match_title"] = "New match!",
        ["notif.matched_with_popup"] = "You matched with {0}.",
        ["notif.matched_with_chat"] = "You matched with {0}!",
        ["notif.someone_new"] = "someone new",
        ["notif.open_messages"] = "Open messages",
        ["notif.pulse_link"] = "Open AetherLove",

        // Messenger (added after update 1.5.1)
        ["notif.msgr_message"] = "You received a Messenger message.",
        ["notif.market_below"] = "{0} dropped to {1}",
        ["notif.market_above"] = "{0} climbed to {1}",
        ["notif.msgr_request"] = "{0} wants to add you on Messenger.",
        ["notif.msgr_open_link"] = "Open Messenger",
        ["notif.msgr_request_title"] = "Messenger request",
        ["notif.msgr_message_body"] = "New message",
        ["notif.msgr_group_added"] = "You were added to \"{0}\"",
        ["notif.msgr_keys_reset"] = "{0} has reset their E2E encryption keys",

        // Clock timers (added after update 2.0.0.0)
        ["notif.clock_chat"] = "Timer \"{0}\" finished.",
        ["notif.clock_notif_title"] = "Timer finished",
        ["notif.clock_notif_body"] = "\"{0}\" is up.",
        ["notif.clock_untitled"] = "Timer",
        ["notif.clock_open"] = "Open",

        // Chat notification link (added after update 2.0.0.0)
        ["notif.view_link"] = "View",

        // added after update 2.1.3
        ["notif.realtor_accepting"] = "The housing lottery is now accepting entries. Check the Realtor app for open plots.",
        ["notif.realtor_results"] = "The housing lottery has moved to its results period. Check the Realtor app to see where you stand.",
        ["notif.staff_notice_title"] = "Message from staff",
        ["notif.staff_notice_body"] = "Open Settings to read it.",

        // Timers (added after update 2.3.3)
        ["notif.timers_title"] = "Timers",
        ["notif.timers_daily"] = "Daily reset",
        ["notif.timers_gc"] = "Grand Company reset",
        ["notif.timers_weekly"] = "Weekly reset",
        ["notif.timers_fr"] = "Fashion Report is open",
        ["notif.timers_cactpot"] = "Jumbo Cactpot drawing",
        ["notif.timers_ocean"] = "Ocean Fishing registration is open",
        ["notif.timers_venture"] = "{0} finished the venture: {1}",
        ["notif.timers_fleet"] = "{0} has returned from its voyage",
        ["notif.timers_custom"] = "\"{0}\" is up",
        ["notif.timers_lead"] = "in {0} min",
        ["notif.timers_chat"] = "A Timers reminder went off.",
        ["notif.calendar_alert_title"] = "Calendar",
        ["notif.calendar_alert_body"] = "Coming up: {0}",

        // added after update 2.3.4
        ["notif.realtor_estate"] = "{0}: {2} days without going home. A private estate is demolished after 45 days away, so that leaves about {1}.",

        // added after update 2.5.3
        ["notif.realtor_entry_results"] = "The lottery results are out. Go and see if you won Plot {0}, Ward {1}, {2}.",
    };
}
