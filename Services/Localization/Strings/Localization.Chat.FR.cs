namespace AetherLove.Services.Localization;

internal static class ChatFr
{
    public static readonly System.Collections.Generic.Dictionary<string, string> Strings = new()
    {
        // ===== Chat =====
        ["chat.no_conversation_selected"] = "Aucune conversation sélectionnée.",
        ["chat.unreadable_message"] = "[message illisible]",
        ["chat.report_submitted_toast"] = "Signalement envoyé. Notre équipe de modération l'examinera.",
        ["chat.view_profile"] = "Voir le profil",
        ["chat.menu_open_profile"] = "Ouvrir le profil",
        ["chat.menu_pin"] = "Épingler le chat",
        ["chat.menu_unpin"] = "Désépingler le chat",
        ["chat.menu_unmatch"] = "Annuler la correspondance",
        ["chat.menu_block"] = "Bloquer",
        ["chat.menu_report_user"] = "Signaler l'utilisateur",
        ["chat.unmatch_title"] = "Annuler la correspondance",
        ["chat.unmatch_body"] = "Lorsque vous annulez la correspondance avec ce joueur, le chat sera masqué pour les deux personnes. Si vous correspondez à nouveau plus tard, votre historique réapparaîtra.",
        ["chat.unmatch_confirm"] = "Annuler la correspondance",
        ["chat.block_title"] = "Bloquer l'utilisateur",
        ["chat.block_body"] = "Lorsque vous bloquez un utilisateur, le chat est masqué pour les deux personnes et il n'apparaîtra plus dans vos correspondances à l'avenir.",
        ["chat.block_confirm"] = "Bloquer",
        ["chat.cancel"] = "Annuler",
        ["chat.loading_messages"] = "Chargement des messages…",
        ["chat.error"] = "Erreur : {0}",
        ["chat.seen_suffix"] = " · vu",
        ["chat.send"] = "Envoyer",
        ["chat.emoji_button"] = ":)",
        ["chat.system_notice_line1"] = "Ceci est un chat privé entre vous et {0}. Ce chat est chiffré de bout en bout, l'équipe d'AetherLove ne peut pas lire vos messages. En haut à droite, vous trouverez un menu pour annuler la correspondance, bloquer ou signaler votre correspondance.",
        ["chat.system_notice_line2"] = "N'oubliez pas : {0} ne connaît pas votre nom complet ni votre localisation tant que vous ne les partagez pas. Prenez soin de votre vie privée et ne partagez davantage que lorsque vous êtes prêt.",
        ["chat.i_understand"] = "J'ai compris",
        ["chat.report_title"] = "Signaler l'utilisateur",
        ["chat.report_reason_prompt"] = "Veuillez décrire la raison de votre signalement :",
        ["chat.report_agree"] = "J'accepte de soumettre un signalement à l'encontre de {0}",
        ["chat.report_agree_contents"] = "J'accepte d'envoyer le contenu de cette conversation à l'équipe d'AetherLove",
        ["chat.submitting"] = "Envoi…",
        ["chat.submit"] = "Envoyer",

        // ChatListScreen
        ["chat.preview_me_prefix"] = "Moi : ",
        ["chat.matches_title"] = "Correspondances",
        ["chat.archive_title"] = "Archivés",
        ["chat.menu_archive"] = "Archiver le chat",
        ["chat.menu_unarchive"] = "Désarchiver le chat",
        ["chat.no_archived"] = "Aucun chat archivé",
        ["chat.all_archived"] = "Tous vos chats sont archivés",
        ["chat.loading"] = "Chargement…",
        ["chat.matches_load_error"] = "Impossible de charger les correspondances : {0}",
        ["chat.connectivity_error"] = "Impossible de se connecter au serveur AetherLove. Veuillez réessayer ou consulter Discord pour voir l'état du serveur.",
        ["chat.empty_state"] = "Vous n'avez pas encore de correspondances — mais cela changera bientôt. Continuez à swiper !",
        ["chat.new_match"] = "nouvelle correspondance",
        ["chat.say_hi"] = "Vous avez un match — dites bonjour !",
        ["chat.time_ago_minutes"] = "il y a {0} min",
        ["chat.time_ago_hours"] = "il y a {0} h",
        ["chat.time_ago_days"] = "il y a {0} j",
    };
}
