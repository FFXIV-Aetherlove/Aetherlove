namespace AetherLove.Services.Localization;

internal static class NotificationsFr
{
    public static readonly System.Collections.Generic.Dictionary<string, string> Strings = new()
    {
        ["notif.new_message"] = "Vous avez un nouveau message.",
        ["notif.match_title"] = "Nouveau match !",
        ["notif.matched_with_popup"] = "Vous avez un match avec {0}.",
        ["notif.matched_with_chat"] = "Vous avez un match avec {0} !",
        ["notif.someone_new"] = "quelqu'un de nouveau",
        ["notif.open_messages"] = "Ouvrir les messages",
        ["notif.pulse_link"] = "Ouvrir AetherLove",

        // Messenger (added after update 1.5.1)
        ["notif.msgr_message"] = "Vous avez reçu un message Messenger.",
        ["notif.market_below"] = "{0} est descendu à {1}",
        ["notif.market_above"] = "{0} est monté à {1}",
        ["notif.msgr_request"] = "{0} veut vous ajouter sur Messenger.",
        ["notif.msgr_open_link"] = "Ouvrir Messenger",
        ["notif.msgr_request_title"] = "Demande Messenger",
        ["notif.msgr_message_body"] = "Nouveau message",
        ["notif.msgr_group_added"] = "Vous avez été ajouté à « {0} »",
        ["notif.msgr_keys_reset"] = "{0} a réinitialisé ses clés de chiffrement E2E",

        // Clock timers (added after update 2.0.0.0)
        ["notif.clock_chat"] = "Le minuteur « {0} » est terminé.",
        ["notif.clock_notif_title"] = "Minuteur terminé",
        ["notif.clock_notif_body"] = "« {0} » est écoulé.",
        ["notif.clock_untitled"] = "Minuteur",
        ["notif.clock_open"] = "Ouvrir",

        // Chat notification link (added after update 2.0.0.0)
        ["notif.view_link"] = "Voir",
    };
}
