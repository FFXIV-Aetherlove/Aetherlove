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

        // added after update 2.1.3
        ["notif.realtor_accepting"] = "La loterie immobilière accepte les candidatures. Les parcelles libres sont dans l'appli Immobilier.",
        ["notif.realtor_results"] = "La loterie immobilière est passée à la période des résultats. Voyez où vous en êtes dans l'appli Immobilier.",
        ["notif.staff_notice_title"] = "Message de l'équipe",
        ["notif.staff_notice_body"] = "Ouvrez les Réglages pour le lire.",

        // Timers (added after update 2.3.3)
        ["notif.timers_title"] = "Minuteurs",
        ["notif.timers_daily"] = "Réinitialisation quotidienne",
        ["notif.timers_gc"] = "Réinitialisation de la Grande Compagnie",
        ["notif.timers_weekly"] = "Réinitialisation hebdomadaire",
        ["notif.timers_fr"] = "Fashion Report est ouvert",
        ["notif.timers_cactpot"] = "Tirage du Jumbo Cactpot",
        ["notif.timers_ocean"] = "Pêche en haute mer : inscriptions ouvertes",
        ["notif.timers_venture"] = "{0} a terminé sa mission : {1}",
        ["notif.timers_fleet"] = "{0} est revenu de son expédition",
        ["notif.timers_custom"] = "« {0} » est écoulé",
        ["notif.timers_lead"] = "dans {0} min",
        ["notif.timers_chat"] = "Un rappel des Minuteurs vient de sonner.",
        ["notif.calendar_alert_title"] = "Calendrier",
        ["notif.calendar_alert_body"] = "À venir : {0}",

        // added after update 2.3.4
        ["notif.realtor_estate"] = "{0} : {2} jours sans passer à la maison. Un terrain privé est démoli après 45 jours d’absence, il reste donc environ {1} jours.",
    };
}
