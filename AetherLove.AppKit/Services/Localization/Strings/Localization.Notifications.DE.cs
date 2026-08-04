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
        ["notif.market_below"] = "{0} ist auf {1} gefallen",
        ["notif.market_above"] = "{0} ist auf {1} gestiegen",
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

        // added after update 2.1.3
        ["notif.realtor_accepting"] = "Die Haus-Lotterie nimmt jetzt Bewerbungen an. Freie Grundstücke findest du in der Makler-App.",
        ["notif.realtor_results"] = "Die Haus-Lotterie ist in der Ergebnisphase. Wie es für dich ausgegangen ist, siehst du in der Makler-App.",
        ["notif.staff_notice_title"] = "Nachricht vom Team",
        ["notif.staff_notice_body"] = "Öffne die Einstellungen, um sie zu lesen.",
    };
}
