namespace AetherLove.Services.Localization;

internal static class SettingsFr
{
    public static readonly System.Collections.Generic.Dictionary<string, string> Strings = new()
    {
        // ===== Settings =====
        ["settings.title"] = "Paramètres",

        ["settings.section_appearance"] = "Apparence",
        ["settings.section_phone_size"] = "Taille du téléphone",
        ["settings.section_plugin_language"] = "Langue du plugin",
        ["settings.section_privacy"] = "Confidentialité",
        ["settings.section_general"] = "Général",
        ["settings.section_notifications"] = "Notifications",
        ["settings.section_moderation"] = "Modération",
        ["settings.section_other"] = "Autre",
        ["settings.section_danger_zone"] = "Zone de danger",

        ["settings.phone_size_small"] = "Petite",
        ["settings.phone_size_medium"] = "Moyenne",
        ["settings.phone_size_large"] = "Grande",
        ["settings.phone_size_caption"] = "Met à l'échelle l'ensemble du téléphone. Les grandes tailles conviennent aux écrans haute résolution ; la taille Grande peut ne pas tenir sur un écran 1080p.",

        ["settings.disable_startup_heartbeat"] = "Désactiver le son de battement de cœur au démarrage",
        ["settings.confirm_before_close"] = "Confirmer avant de fermer AetherLove",

        ["settings.view_changelog"] = "Voir le journal des modifications",
        ["settings.send_feedback"] = "Envoyer un commentaire",
        ["settings.terms_of_service"] = "Conditions d'utilisation",
        ["settings.delete_account"] = "Supprimer le compte",
        ["settings.create_new_profile"] = "Créer un nouveau profil",
        ["settings.cancel"] = "Annuler",
        ["settings.back"] = "Retour",

        ["settings.always_blur_nsfw"] = "Toujours flouter le NSFW",
        ["settings.always_blur_nsfw_tooltip"] = "Lorsque cette option est activée, les photos supplémentaires marquées NSFW dans les autres profils sont floutées jusqu'à ce que vous cliquiez pour les révéler une par une. Les avatars et les portraits principaux sont toujours safe-for-work, quoi qu'il arrive. Désactiver cette option affiche chaque photo telle quelle.",
        ["settings.nsfw_profile"] = "Mon profil est NSFW (18+)",
        ["settings.nsfw_profile_tooltip"] = "Marque votre profil comme adulte/NSFW afin qu'il ne soit visible que par les personnes ayant activé le NSFW. Il s'active automatiquement lorsque vous ajoutez des photos NSFW ou choisissez le roleplay 18+, et reste activé jusqu'à ce que vous les retiriez.",
        ["settings.nsfw_profile_locked"] = "Vous ne pouvez pas le désactiver tant que vous avez des photos NSFW ou le roleplay 18+ (ERP) sélectionné. Retirez d'abord vos images NSFW et désélectionnez le roleplay 18+.",

        ["settings.enable_notifications"] = "Activer les notifications",
        ["settings.enable_notifications_tooltip"] = "Interrupteur principal pour toutes les notifications. Désactivez-le pour faire taire toutes les annonces dans le chat de jeu, les fenêtres contextuelles et les sons ci-dessous.",
        ["settings.enable_notification_sounds"] = "Activer les sons de notification",
        ["settings.enable_notification_sounds_tooltip"] = "Les sons de notification ne seront diffusés que si l'audio de votre jeu et l'audio des effets spéciaux ne sont pas coupés. Le réglage du volume se fait via le volume de Windows.",
        ["settings.announce_messages_chat"] = "Annoncer les nouveaux messages dans le chat de jeu",
        ["settings.announce_matches_chat"] = "Annoncer les nouvelles correspondances dans le chat de jeu",
        ["settings.popup_messages"] = "Afficher une fenêtre contextuelle pour les nouveaux messages",
        ["settings.popup_matches"] = "Afficher une fenêtre contextuelle pour les nouvelles correspondances",
        ["settings.hide_notifications_in_combat"] = "Masquer les notifications en combat",
        ["settings.hide_notifications_in_combat_tooltip"] = "Lorsque cette option est activée, vous ne recevez aucune notification — annonces dans le chat de jeu, fenêtres contextuelles ou sons — tant que vous êtes en combat.",
        ["settings.auto_open_minimized"] = "Ouvrir automatiquement en mode réduit à la connexion",
        ["settings.pulse_optout"] = "Reçois d'incroyables messages de l'équipe Aethernet pour te rappeler de swiper",
        ["settings.pulse_optout_tooltip"] = "De temps en temps, AetherLove peut glisser un message amusant dans votre chat de jeu. Désactivez pour les arrêter.",
        ["settings.combat_behavior"] = "En combat",
        ["settings.combat_behavior_hide"] = "Masquer AetherLove",
        ["settings.combat_behavior_minimize"] = "Réduire en bulle",
        ["settings.combat_behavior_leave_open"] = "Laisser ouvert",
        ["settings.notification_sound"] = "Son de notification",
        ["settings.play"] = "Écouter",

        ["settings.delete_warning_intro"] = "Cette action est définitive et irréversible. Veuillez lire attentivement ce qui suit avant de continuer :",
        ["settings.delete_bullet_account"] = "Votre compte sera définitivement supprimé.",
        ["settings.delete_bullet_matches"] = "Toutes vos correspondances seront supprimées.",
        ["settings.delete_bullet_preferences"] = "Vos préférences de correspondance seront effacées.",
        ["settings.delete_bullet_pictures"] = "Vos photos de profil seront supprimées.",
        ["settings.delete_reregister"] = "Vous pouvez toujours vous réinscrire à tout moment.",
        ["settings.delete_previous_failed"] = "La tentative précédente a échoué : {0}",

        ["settings.deleting_title"] = "Suppression du compte",
        ["settings.deleting_body"] = "Suppression de vos données et annulation des correspondances",
        ["settings.deleted_title"] = "Compte supprimé",
        ["settings.deleted_body"] = "Votre compte a été supprimé, vos données et vos photos ont été retirées, et vos correspondances ont été annulées. Vous pouvez maintenant supprimer le plugin, ou recommencer la configuration et créer un nouveau profil.",

        ["settings.warnings_button_unseen"] = "Avertissements ({0} non lus / {1})",
        ["settings.warnings_button"] = "Avertissements ({0})",
        ["settings.warnings_title"] = "Avertissements",
        ["settings.no_warnings"] = "Aucun avertissement enregistré.",

        // Moderator messages
        ["settings.modmsg_button_unseen"] = "Messages du modérateur ({0} non lus / {1})",
        ["settings.modmsg_button"] = "Messages du modérateur ({0})",
        ["settings.modmsg_title"] = "Messages du modérateur",
        ["settings.no_modmsg"] = "Aucun message enregistré.",
        ["settings.back_to_settings_arrow"] = "← Retour aux paramètres",

        ["settings.back_to_settings"] = "Retour aux paramètres",
        ["settings.feedback_thanks"] = "Merci ! Votre commentaire a été envoyé à l'équipe d'AetherLove.",
        ["settings.feedback_intro"] = "Vous avez trouvé un bug, vous avez une idée ou une suggestion ? Faites-le-nous savoir.",
        ["settings.feedback_note"] = "À noter : les commentaires ne peuvent pas servir à contester un bannissement ou un avertissement.",
        ["settings.feedback_type"] = "Type",
        ["settings.feedback_kind_bug"] = "Bug",
        ["settings.feedback_kind_improvement"] = "Amélioration",
        ["settings.feedback_kind_other"] = "Autre",
        ["settings.feedback_your_message"] = "Votre message",
        ["settings.sending"] = "Envoi…",
        ["settings.submit"] = "Envoyer",
        ["settings.feedback_rate_limited"] = "Vous ne pouvez envoyer un commentaire que {0} fois par heure. Veuillez réessayer plus tard.",
        ["settings.feedback_send_failed"] = "Impossible d'envoyer votre commentaire. Veuillez réessayer.",

        ["settings.contributors"] = "Contributeurs",
        ["settings.contributors_thanks_title"] = "Merci",
        ["settings.contributors_intro"] = "AetherLove ne serait pas possible sans :",
        ["settings.contributors_leads"] = "Responsables du projet : Astraea & Nihal",
        ["settings.contributors_council"] = "The Chon-Chon Council",
        ["settings.contributors_moderation"] = "Modération : Su",
        ["settings.contributors_translators"] = "Traducteurs : Tears, Mufami, Terashi, Su, Astraea",
        ["settings.contributors_xivauth"] = "XIVAuth by KazWolfe",
        ["settings.contributors_punish"] = "Puni.sh",
        ["settings.contributors_dalamud"] = "The Dalamud project",
        ["settings.contributors_testers"] = "Tous les merveilleux bêta-testeurs à travers Éorzéa.",
    };
}
