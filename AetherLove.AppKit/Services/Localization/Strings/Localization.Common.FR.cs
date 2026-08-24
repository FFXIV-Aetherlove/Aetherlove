namespace AetherLove.Services.Localization;

internal static class CommonFr
{
    public static readonly System.Collections.Generic.Dictionary<string, string> Strings = new()
    {
        // Common
        ["common.ok"] = "OK",
        ["common.cancel"] = "Annuler",
        ["common.loading"] = "Chargement…",
        ["common.try_again"] = "Réessayer",
        ["common.i_understand"] = "J'ai compris",
        ["common.sign_out"] = "Se déconnecter",
        ["common.got_it"] = "Compris !",
        ["common.server_unreachable_detail"] = "Impossible de joindre le serveur : {0}",

        ["common.banned_title"] = "Profil banni",
        ["common.banned_body"] = "Un profil AetherLove banni signifie que vous ne pouvez plus utiliser AetherLove avec ce profil. Vous pouvez toujours utiliser nos autres applications. Pour plus d'informations, ouvrez un ticket de support sur notre Discord.",
        ["common.banned_reason_label"] = "Raison",
        ["common.banned_uninstall_hint"] = "Utilisez le bouton d'accueil ci-dessous pour revenir à l'écran d'accueil.",

        // Outdated-plugin screen
        ["common.outdated_title"] = "Mise à jour requise",
        ["common.outdated_body"] = "Vous utilisez une version obsolète d'AetherLove. Le serveur ne prend plus en charge cette version, le plugin ne peut donc pas se connecter.",
        ["common.outdated_hint"] = "Veuillez mettre à jour le plugin dans l'installateur de plugins de Dalamud, puis rouvrir AetherLove.",

        ["common.offline_title"] = "Les services AetherOS sont actuellement hors ligne",
        ["common.offline_body"] = "Les serveurs sont très probablement hors ligne pour cause de mises à jour ou de maintenance. Cela ne devrait pas prendre plus de 2 minutes !",
        ["common.offline_reconnecting"] = "Reconnexion…",
        ["common.offline_taking_long"] = "Cela prend plus de temps que d'habitude. Rejoignez notre Discord pour connaître les dernières informations.",
        ["common.offline_join_discord"] = "Rejoindre le Discord",

        ["common.passphrase_title"] = "Saisissez votre phrase secrète de chiffrement",
        ["common.passphrase_intro"] = "Nous reconnaissons ce compte, mais cet appareil n'a pas encore votre clé de chat. Saisissez la phrase secrète que vous avez définie sur votre premier appareil pour déverrouiller votre historique de discussion.",
        ["common.passphrase_forgot"] = "Vous avez oublié votre phrase secrète ? Vous pouvez réinitialiser vos clés de chiffrement ci-dessous. Tout ce qui a été envoyé avant la réinitialisation deviendra illisible pour vous.",

        // Passphrase reset (added after update 1.5.1)
        ["common.passphrase_reset_button"] = "Réinitialiser les clés de chiffrement…",
        ["common.passphrase_reset_title"] = "Réinitialiser vos clés de chiffrement",
        ["common.passphrase_reset_warning"] = "Ceci crée une phrase secrète et des clés de chiffrement entièrement nouvelles. Vous perdrez DÉFINITIVEMENT l''accès à tous les messages antérieurs à la réinitialisation, et vos matchs et contacts Messenger verront un avis indiquant que vous avez réinitialisé vos clés.",
        ["common.passphrase_reset_new"] = "Nouvelle phrase secrète",
        ["common.passphrase_reset_repeat"] = "Répétez la nouvelle phrase secrète",
        ["common.passphrase_reset_mismatch"] = "Les phrases secrètes ne correspondent pas.",
        ["common.passphrase_reset_go"] = "Réinitialiser mes clés",
        ["common.passphrase_reset_running"] = "Réinitialisation…",
        ["common.passphrase_bundle_load_failed"] = "Impossible de charger le paquet de chiffrement depuis le serveur.",
        ["common.passphrase_empty"] = "Veuillez saisir votre phrase secrète.",
        ["common.passphrase_incorrect"] = "Phrase secrète incorrecte. Réessayez.",
        ["common.passphrase_unlock_failed"] = "Échec du déverrouillage : {0}",
        ["common.unlock"] = "Déverrouiller",
        ["common.unlocking"] = "Déverrouillage…",

        // Encryption recovery screen
        ["common.recovery_title"] = "Configurer la messagerie sécurisée",
        ["common.recovery_intro"] = "Votre compte n'a pas de clés de chiffrement, vous ne pouvez donc pas encore envoyer ni recevoir de messages. Choisissez une phrase secrète pour les configurer. Conservez-la précieusement, elle est irrécupérable.",
        ["common.recovery_button"] = "Activer la messagerie sécurisée",
        ["common.recovery_support"] = "Toujours bloqué ? Déconnectez-vous ci-dessous ou contactez-nous sur Discord.",

        ["common.warnings_heading_one"] = "Vous avez un avertissement de modération",
        ["common.warnings_heading_many"] = "Vous avez {0} avertissements de modération",
        ["common.warnings_body"] = "Veuillez lire le ou les avertissements suivants de l'équipe de modération. Les récidives peuvent entraîner une suspension de compte.",
        ["common.warnings_submit_error"] = "Impossible de joindre le serveur : {0}. Touchez pour réessayer.",
        ["common.acknowledging"] = "Prise en compte…",

        // Moderator message screen
        ["common.modmsg_heading_one"] = "Vous avez un message de l'équipe de modération",
        ["common.modmsg_heading_many"] = "Vous avez {0} messages de l'équipe de modération",
        ["common.modmsg_body"] = "L'équipe de modération vous a envoyé ceci :",
        ["common.modmsg_got_it"] = "Compris",

        ["common.nsfw_decl_unselected"] = "sélectionnez une option ci-dessous",
        ["common.nsfw_decl_sfw"] = "cette photo est SFW",
        ["common.nsfw_decl_nsfw"] = "cette photo est NSFW",
        ["common.lalafell_nsfw_title"] = "NSFW non disponible",
        ["common.lalafell_nsfw_body"] = "Nous n'autorisons pas les photos NSFW de personnages Lalafell. Comme les Lalafells ont une apparence enfantine, nous appliquons cette politique de manière uniforme à chaque compte Lalafell et ne faisons aucune exception au cas par cas.\n\nVotre photo a été remise en SFW. Si cette photo n'est pas safe-for-work, veuillez la retirer et en envoyer une autre.",
        ["common.undeclared_photo_title"] = "Déclaration requise",
        ["common.undeclared_photo_body"] = "Vous devez indiquer si votre autre photo est SFW ou NSFW dans la zone de sélection avant d'en envoyer une autre.",

        ["common.changelog_window_title"] = "AetherLove : Nouveautés",
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

        ["common.emoji_none_found"] = "Aucun emoji trouvé.",

        // Close-plugin confirmation modal
        ["common.close_plugin_tooltip"] = "Fermer AetherOS",
        ["common.minimize_tooltip"] = "Réduire AetherOS",
        ["common.close_plugin_title"] = "Fermer AetherLove ?",
        ["common.close_plugin_body"] = "Cela masque simplement la fenêtre. Vous resterez connecté et continuerez à recevoir de nouvelles correspondances et des messages tant que le plugin est activé.\n\nRouvrez la fenêtre à tout moment en tapant {0} dans le chat.",
        ["common.close_plugin_tip"] = "Astuce : utilisez plutôt le bouton Réduire en bas pour garder la petite bulle flottante visible avec son badge de notification.",
        ["common.close_plugin_dont_ask"] = "Ne plus afficher cette fenêtre",
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

        // SFW-image gate modal (main avatar + first profile photo must be SFW)
        ["common.sfw_gate_title"] = "Profil + Avatar - SFW UNIQUEMENT",
        ["common.sfw_gate_subtitle"] = "Ce qui n'est PAS SFW :",
        ["common.sfw_gate_b1"] = "Nudité intégrale, quel que soit le genre.",
        ["common.sfw_gate_b2"] = "Tétons visibles, quel que soit le genre.",
        ["common.sfw_gate_b3"] = "Poils pubiens ou zones génitales visibles.",
        ["common.sfw_gate_b4"] = "Représentations graphiques de sang, de blessures, de plaies ou de dommages corporels.",
        ["common.sfw_gate_b5"] = "Tatouages, marquages, symboles ou textes obscènes, discriminatoires ou haineux, ou visant des personnes ou des groupes en raison de leur race, origine ethnique, nationalité, religion, genre, orientation sexuelle ou d'autres caractéristiques protégées.",
        ["common.sfw_gate_b6"] = "Gestes, poses ou références visuelles à caractère sexuel qui sous-entendent ou simulent des actes sexuels, y compris le sexe oral, la masturbation ou toute autre activité sexuelle.",
        ["common.sfw_gate_secondary"] = "Vous pouvez toujours ajouter du contenu NSFW dans vos images de profil secondaires.",
        ["common.sfw_gate_ack"] = "Je comprends les règles SFW",

        // added after update (1.3.1)
        ["common.sfw_gate_race_gender"] = "Veuillez vous assurer que votre image principale montre la race et le genre de votre personnage tels qu'indiqués dans votre profil.",

        // added after update 1.4.3
        ["common.img_cloud_title"] = "Fichier non téléchargé",
        ["common.img_cloud_unavailable"] = "Cette image est stockée uniquement en ligne dans le cloud (par exemple OneDrive) et n'a pas été téléchargée sur votre PC, elle ne peut donc pas être ouverte. Dans l'Explorateur de fichiers, faites un clic droit dessus, choisissez 'Toujours conserver sur cet appareil', attendez la coche verte, puis réessayez. Ou choisissez un fichier enregistré localement sur votre PC.",
        ["common.emoji_favorites"] = "Favoris",
        ["common.emoji_favorite_hint"] = "clic droit pour ajouter ou retirer des favoris",
        ["common.emoji_add_favorite"] = "Ajouter aux favoris",
        ["common.emoji_remove_favorite"] = "Retirer des favoris",
        ["common.selfie"] = "Selfie",
        ["common.selfie_instructions"] = "Déplace ou redimensionne le cadre sur ton personnage, puis prends la photo.",
        ["common.selfie_take"] = "Prendre la photo",
        ["common.selfie_capturing"] = "Capture...",
        ["common.offline_maintenance"] = "Le serveur est en maintenance.",

        // added after update 1.5.0
        ["common.nav_places"] = "Lieux",

        // Multi-profile switch nav slot (added after update 1.5.1)
        ["common.nav_switch"] = "Changer",

        // Recovery gate, enter-existing-passphrase mode (added after update 1.5.1)
        ["common.recovery_enter_intro"] = "Ce profil n'a pas encore ses clés de chiffrement. Saisissez votre phrase secrète de chiffrement pour les configurer.",

        // account moderation reconcile (added after update 2.0.0)
        ["common.moderation_warning_for"] = "Avertissement pour {0}",
        ["common.moderation_message_for"] = "Message pour {0}",
        ["common.account_disabled_title"] = "Compte banni",
        ["common.account_disabled_body"] = "Cette fonctionnalité est indisponible tant que votre compte est banni.",

        // added after update 2.0.1
        ["common.passphrase_correct_unrecoverable"] = "Votre phrase secrète est correcte, mais aucune de vos clés enregistrées n'a pu être ouverte avec. Contactez le support avant d'envisager une réinitialisation ; celle-ci rendrait vos anciens messages définitivement illisibles.",

        // added after update 2.1.3
        ["common.staff_notice_heading_one"] = "Vous avez un avis de l'équipe",
        ["common.staff_notice_heading_many"] = "Vous avez {0} avis de l'équipe",
        ["common.staff_notice_body"] = "L'équipe AetherOS vous a envoyé ceci au sujet de votre compte :",
        ["common.staff_notice_ack"] = "J'ai compris",

        // added after update 2.2.3
        ["common.travel_teleport_with"] = "Téléportation ({0})",
        ["common.travel_tooltip"] = "Voyager ici avec {0}",

        // added after update 2.4.0
        ["common.session_expired_title"] = "Tu dois te reconnecter",
        ["common.session_expired_body"] = "Ta session est terminée, le téléphone ne peut donc plus joindre AetherOS. Ce n'est pas une panne : tout fonctionne, il ne sait simplement plus qui tu es.",
        ["common.session_expired_button"] = "Se reconnecter",
        ["common.session_expired_hint"] = "Le téléphone redémarre et te ramène à l'écran de connexion.",
    
        // the file picker (added after update 2.4.0)
        ["picker.quick_links"] = "Emplacements",
        ["picker.favorites"] = "Favoris",
        ["picker.drives"] = "Disques",
        ["picker.place_desktop"] = "Bureau",
        ["picker.place_documents"] = "Documents",
        ["picker.place_downloads"] = "Téléchargements",
        ["picker.place_pictures"] = "Images",
        ["picker.place_screenshots"] = "Captures du jeu",
        ["picker.search_hint"] = "Rechercher dans ce dossier...",
        ["picker.empty"] = "Ce dossier est vide.",
        ["picker.open"] = "Ouvrir",
        ["picker.nothing_selected"] = "Rien de sélectionné",
        ["picker.new_folder_hint"] = "Nom du dossier...",
        ["picker.new_folder_create"] = "Créer",
        ["picker.show_hidden"] = "Fichiers cachés",
        ["picker.sort_name"] = "Nom",
        ["picker.sort_date"] = "Date",
        ["picker.sort_size"] = "Taille",
        ["picker.tip_star"] = "Ajouter ou retirer ce dossier des favoris (clic droit sur un favori pour le retirer)",
        ["picker.tip_edit_path"] = "Saisir un chemin",
        ["picker.preview_loading"] = "Chargement de l'aperçu...",
        ["picker.save"] = "Enregistrer",
        ["picker.file_name_hint"] = "Nom du fichier...",
        ["picker.selected_count"] = "{0} sélectionnés",
    };
}
