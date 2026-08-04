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
    };
}
