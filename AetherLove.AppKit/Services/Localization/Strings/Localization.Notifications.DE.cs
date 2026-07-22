namespace AetherLove.Services.Localization;

internal static class NotificationsDe
{
    public static readonly System.Collections.Generic.Dictionary<string, string> Strings = new()
    {
        ["notif.new_message"] = "Du hast eine neue Nachricht.",
        ["notif.match_title"] = "Neues Match!",
        ["notif.matched_with_popup"] = "Du hast ein Match mit {0}.",
        ["notif.matched_with_chat"] = "Du hast ein Match mit {0}!",
        ["notif.someone_new"] = "jemand Neuem",
        ["notif.open_messages"] = "Nachrichten öffnen",
        ["notif.pulse_link"] = "AetherLove öffnen",

        // Messenger (added after update 1.5.1)
        ["notif.msgr_message"] = "Du hast eine Messenger-Nachricht erhalten.",
        ["notif.msgr_request"] = "{0} möchte dich im Messenger hinzufügen.",
        ["notif.msgr_open_link"] = "Messenger öffnen",
        ["notif.msgr_request_title"] = "Messenger-Anfrage",
        ["notif.msgr_message_body"] = "Neue Nachricht",
        ["notif.msgr_group_added"] = "Du wurdest zu \"{0}\" hinzugefügt",
        ["notif.msgr_keys_reset"] = "{0} hat die E2E-Verschlüsselungsschlüssel zurückgesetzt",

        // Clock timers (added after update 2.0.0.0)
        ["notif.clock_chat"] = "Timer \"{0}\" ist abgelaufen.",
        ["notif.clock_notif_title"] = "Timer abgelaufen",
        ["notif.clock_notif_body"] = "\"{0}\" ist fertig.",
        ["notif.clock_untitled"] = "Timer",
        ["notif.clock_open"] = "Öffnen",

        // Chat notification link (added after update 2.0.0.0)
        ["notif.view_link"] = "Ansehen",
    };
}
