namespace AetherLove.Services.Localization;

internal static class CommonFr
{
    public static readonly System.Collections.Generic.Dictionary<string, string> Strings = new()
    {
        // ===== Common =====
        ["common.ok"] = "OK",
        ["common.confirm"] = "Confirmer",
        ["common.cancel"] = "Annuler",
        ["common.loading"] = "Chargement…",
        ["common.try_again"] = "Réessayer",
        ["common.i_understand"] = "J'ai compris",
        ["common.sign_out"] = "Se déconnecter",
        ["common.got_it"] = "Compris !",
        ["common.moderator_notes_label"] = "Notes du modérateur",
        ["common.server_unreachable_detail"] = "Impossible de joindre le serveur : {0}",

        ["common.banned_title"] = "Compte banni",
        ["common.banned_body"] = "Votre compte AetherLove a été banni. Vous ne pouvez plus utiliser le service.",
        ["common.banned_reason_label"] = "Raison",
        ["common.banned_uninstall_hint"] = "Vous pouvez fermer cette fenêtre et désinstaller le plugin à tout moment.",

        // Outdated-plugin screen
        ["common.outdated_title"] = "Mise à jour requise",
        ["common.outdated_body"] = "Vous utilisez une version obsolète d'AetherLove. Le serveur ne prend plus en charge cette version, le plugin ne peut donc pas se connecter.",
        ["common.outdated_hint"] = "Veuillez mettre à jour le plugin dans l'installateur de plugins de Dalamud, puis rouvrir AetherLove.",

        ["common.offline_title"] = "AetherLove est hors ligne",
        ["common.offline_body"] = "Nous ne parvenons pas à joindre les serveurs d'AetherLove pour le moment. L'application a besoin d'une connexion active pour parcourir, correspondre et discuter, elle est donc en pause jusqu'à notre retour en ligne.",
        ["common.offline_reconnecting"] = "Reconnexion…",
        ["common.offline_keep_trying"] = "Nous continuerons d'essayer automatiquement.",

        ["common.passphrase_title"] = "Saisissez votre phrase secrète de chiffrement",
        ["common.passphrase_intro"] = "Nous reconnaissons ce compte, mais cet appareil n'a pas encore votre clé de chat. Saisissez la phrase secrète que vous avez définie sur votre premier appareil pour déverrouiller votre historique de discussion.",
        ["common.passphrase_forgot"] = "Vous avez oublié votre phrase secrète ? Il n'y a pas de récupération possible, mais vous pouvez vous déconnecter ci-dessous et créer un nouveau compte. Votre historique de discussion lié à ce compte sera perdu.",
        ["common.passphrase_bundle_load_failed"] = "Impossible de charger le paquet de chiffrement depuis le serveur.",
        ["common.passphrase_empty"] = "Veuillez saisir votre phrase secrète.",
        ["common.passphrase_incorrect"] = "Phrase secrète incorrecte. Réessayez.",
        ["common.passphrase_unlock_failed"] = "Échec du déverrouillage : {0}",
        ["common.unlock"] = "Déverrouiller",
        ["common.unlocking"] = "Déverrouillage…",

        ["common.warnings_heading_one"] = "Vous avez un avertissement de modération",
        ["common.warnings_heading_many"] = "Vous avez {0} avertissements de modération",
        ["common.warnings_body"] = "Veuillez lire le ou les avertissements suivants de l'équipe de modération. Les récidives peuvent entraîner une suspension de compte.",
        ["common.warnings_submit_error"] = "Impossible de joindre le serveur : {0}. Touchez pour réessayer.",
        ["common.acknowledging"] = "Prise en compte…",

        ["common.nsfw_decl_unselected"] = "sélectionnez une option ci-dessous",
        ["common.nsfw_decl_sfw"] = "cette photo est SFW",
        ["common.nsfw_decl_nsfw"] = "cette photo est NSFW",
        ["common.lalafell_nsfw_title"] = "NSFW non disponible",
        ["common.lalafell_nsfw_body"] = "Nous n'autorisons pas les photos NSFW de personnages Lalafell. Comme les Lalafells ont une apparence enfantine, nous appliquons cette politique de manière uniforme à chaque compte Lalafell et ne faisons aucune exception au cas par cas.\n\nVotre photo a été remise en SFW. Si cette photo n'est pas safe-for-work, veuillez la retirer et en téléverser une autre.",
        ["common.undeclared_photo_title"] = "Déclaration requise",
        ["common.undeclared_photo_body"] = "Vous devez indiquer si votre autre photo est SFW ou NSFW dans la zone de sélection avant d'en téléverser une autre.",

        ["common.changelog_window_title"] = "AetherLove — Nouveautés",
        ["common.whats_new"] = "Nouveautés",
        ["common.changelog_empty"] = "Aucune entrée de journal des modifications disponible.",
        ["common.changelog_latest"] = "Dernière",
        ["common.changelog_important"] = "Important",
        ["common.changelog_new_features"] = "Nouvelles fonctionnalités",
        ["common.changelog_bug_fixes"] = "Corrections de bugs",

        ["common.rate_limit_title"] = "Doucement",
        ["common.rate_limit_noun_profile"] = "profil",
        ["common.rate_limit_noun_images"] = "images",
        ["common.rate_limit_body"] = "Vous ne pouvez modifier votre {0} que {1} fois par heure. Veuillez réessayer dans {2}.",
        ["common.rate_limit_retry_moment"] = "un instant",
        ["common.rate_limit_retry_one_second"] = "1 seconde",
        ["common.rate_limit_retry_seconds"] = "{0} secondes",
        ["common.rate_limit_retry_one_minute"] = "1 minute",
        ["common.rate_limit_retry_minutes"] = "{0} minutes",

        ["common.emoji_search_hint"] = "Rechercher un emoji...",
        // Bottom navigation bar
        ["common.nav_swipe"] = "Swipe",
        ["common.nav_matches"] = "Matches",
        ["common.nav_settings"] = "Réglages",
        ["common.nav_minimize"] = "Réduire",

        ["common.emoji_none_found"] = "Aucun emoji trouvé.",

        // Close-plugin confirmation modal
        ["common.close_plugin_tooltip"] = "Fermer AetherLove",
        ["common.close_plugin_title"] = "Fermer AetherLove ?",
        ["common.close_plugin_body"] = "Cela masque simplement la fenêtre. Vous resterez connecté et continuerez à recevoir de nouvelles correspondances et des messages tant que le plugin est activé.\n\nRouvrez la fenêtre à tout moment en tapant {0} dans le chat.",
        ["common.close_plugin_tip"] = "Astuce : utilisez plutôt le bouton Réduire en bas pour garder la petite bulle flottante visible avec son badge de notification.",
        ["common.close"] = "Fermer",

        // Save-error modal
        ["common.save_error_title"] = "Une erreur s'est produite",
        ["common.save_error_intro"] = "Nous n'avons pas pu enregistrer vos modifications :",
        ["common.save_error_report"] = "Si le problème persiste, signalez le bug sur notre Discord.",
        ["common.save_error_unknown"] = "Une erreur inattendue s'est produite.",

        // Image requirements modal
        ["common.img_requirements_title"] = "Image inutilisable",
        ["common.img_invalid"] = "Ce fichier n'est pas une image valide ou son format n'est pas pris en charge.",
        ["common.img_too_small"] = "Cette image ne fait que {0}×{1} px, elle est trop petite.",
        ["common.img_requirements_sizes"] = "Les avatars doivent faire au moins {0}×{1} px et les photos de profil au moins {2}×{3} px. Choisissez une image plus grande.",

        // Image crop window
        ["common.loading_image"] = "Chargement de l'image...",
        ["common.use_this_crop"] = "Utiliser ce recadrage",
    };
}
