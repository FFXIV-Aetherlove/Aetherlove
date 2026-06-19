namespace AetherLove.Services.Localization;

internal static class SettingsPt
{
    public static readonly System.Collections.Generic.Dictionary<string, string> Strings = new()
    {
        ["settings.title"] = "Configurações",

        // Section labels
        ["settings.section_appearance"] = "Aparência",
        ["settings.section_phone_size"] = "Tamanho do telefone",
        ["settings.section_plugin_language"] = "Idioma do plugin",
        ["settings.section_privacy"] = "Privacidade",
        ["settings.section_general"] = "Geral",
        ["settings.section_notifications"] = "Notificações",
        ["settings.section_moderation"] = "Moderação",
        ["settings.section_other"] = "Outros",
        ["settings.section_danger_zone"] = "Zona de perigo",

        // Phone size picker (AppearancePicker)
        ["settings.phone_size_small"] = "Pequeno",
        ["settings.phone_size_medium"] = "Médio",
        ["settings.phone_size_large"] = "Grande",
        ["settings.phone_size_caption"] = "Escala todo o telefone. Tamanhos maiores combinam com telas de resolução mais alta; Tamanhos grandes podem não caber em uma tela 1080p.",

        // General
        ["settings.disable_startup_heartbeat"] = "Desativar o som de coração a bater na inicialização",
        ["settings.confirm_before_close"] = "Confirmar antes de fechar o AetherLove",

        // Buttons
        ["settings.terms_of_service"] = "Termos de Serviço",
        ["settings.view_changelog"] = "Ver registro de alterações",
        ["settings.send_feedback"] = "Enviar feedback",
        ["settings.delete_account"] = "Excluir Conta",
        ["settings.create_new_profile"] = "Criar um novo perfil",
        ["settings.cancel"] = "Cancelar",
        ["settings.back"] = "Voltar",

        // Privacy
        ["settings.always_blur_nsfw"] = "Sempre desfocar NSFW",
        ["settings.always_blur_nsfw_tooltip"] = "Quando ativado, as fotos extras marcadas como NSFW em outros perfis ficam desfocadas até clicar para revelar cada uma. Avatares e retratos principais são sempre safe-for-work, independentemente disso. Desativar isto mostra todas as fotos como são.",
        ["settings.nsfw_profile"] = "Meu perfil é NSFW (18+)",
        ["settings.nsfw_profile_tooltip"] = "Marca seu perfil como adulto/NSFW para que só seja mostrado a pessoas que ativaram o NSFW. É ativado automaticamente quando adicionas fotos NSFW ou escolhes roleplay 18+, e permanece ativo até removê-los.",
        ["settings.nsfw_profile_locked"] = "Não podes desativar isto enquanto tiveres fotos NSFW ou roleplay 18+ (ERP) selecionado. Remove primeiro as imagens NSFW e desmarque o roleplay 18+.",

        // Notifications
        ["settings.enable_notifications"] = "Ativar notificações",
        ["settings.enable_notifications_tooltip"] = "Opção mestre de todas as notificações. Desativa isto para silenciar todos os avisos no chat do jogo, popups e sons abaixo.",
        ["settings.enable_notification_sounds"] = "Ativar sons de notificação",
        ["settings.enable_notification_sounds_tooltip"] = "Os sons de notificação só tocam se o áudio do jogo e o áudio de efeitos especiais não estiverem silenciados. O controle de volume é feito pelo volume do Windows.",
        ["settings.announce_messages_chat"] = "Anunciar novas mensagens no chat do jogo",
        ["settings.announce_matches_chat"] = "Anunciar novos matches no chat do jogo",
        ["settings.popup_messages"] = "Mostrar um popup para novas mensagens",
        ["settings.popup_matches"] = "Mostrar um popup para novos matches",
        ["settings.auto_open_minimized"] = "Abrir minimizado automaticamente ao fazer login",
        ["settings.pulse_optout"] = "Mensagens ocasionais no jogo",
        ["settings.pulse_optout_tooltip"] = "Ocasionalmente, o AetherLove pode deixar uma mensagem divertida no chat do jogo. Desative para parar.",
        ["settings.combat_behavior"] = "Ao entrar em combate",
        ["settings.combat_behavior_hide"] = "Ocultar AetherLove",
        ["settings.combat_behavior_minimize"] = "Minimizar para bolha",
        ["settings.combat_behavior_leave_open"] = "Deixar aberto",
        ["settings.notification_sound"] = "Som de notificação",
        ["settings.play"] = "Tocar",

        // Delete account confirmation
        ["settings.delete_warning_intro"] = "Esta ação é permanente e não pode ser desfeita. Por favor, lê o seguinte com atenção antes de continuar:",
        ["settings.delete_bullet_account"] = "A tua conta será excluída permanentemente.",
        ["settings.delete_bullet_matches"] = "Todos os teus matches serão removidos.",
        ["settings.delete_bullet_preferences"] = "As Tuas preferências de match serão apagadas.",
        ["settings.delete_bullet_pictures"] = "As Tuas fotos de perfil serão removidas.",
        ["settings.delete_reregister"] = "Podes sempre te cadastrar novamente a qualquer momento.",
        ["settings.delete_previous_failed"] = "A tentativa anterior falhou: {0}",

        // Deleting / deleted views
        ["settings.deleting_title"] = "Excluindo conta",
        ["settings.deleting_body"] = "A remover os teus dados e a desfazer matches com os contatos",
        ["settings.deleted_title"] = "Conta excluída",
        ["settings.deleted_body"] = "A tua conta foi excluída, dados e fotos foram removidos, e matches foram desfeitos. Agora podes remover o plugin ou seguir para criar um novo perfil.",

        // Warnings
        ["settings.warnings_button_unseen"] = "Advertências ({0} não vistas / {1})",
        ["settings.warnings_button"] = "Advertências ({0})",
        ["settings.warnings_title"] = "Advertências",
        ["settings.no_warnings"] = "Nenhuma advertência registrada.",
        ["settings.back_to_settings_arrow"] = "← Voltar às configurações",

        // Feedback flow
        ["settings.back_to_settings"] = "Voltar às configurações",
        ["settings.feedback_thanks"] = "Obrigado! O teu feedback foi enviado à equipa do AetherLove.",
        ["settings.feedback_intro"] = "Encontrou um bug, tem uma ideia ou quer sugerir algo? Conta-nos tudo.",
        ["settings.feedback_note"] = "Observação: o feedback não pode ser usado para recorrer de um banimento ou advertência.",
        ["settings.feedback_type"] = "Tipo",
        ["settings.feedback_kind_bug"] = "Bug",
        ["settings.feedback_kind_improvement"] = "Melhoria",
        ["settings.feedback_kind_other"] = "Outro",
        ["settings.feedback_your_message"] = "Sua mensagem",
        ["settings.sending"] = "Enviando…",
        ["settings.submit"] = "Enviar",
        ["settings.feedback_rate_limited"] = "Só podes enviar feedback {0} vezes por hora. Por favor, tenta novamente mais tarde.",
        ["settings.feedback_send_failed"] = "Não foi possível enviar o feedback. Por favor, tenta novamente.",

        // Contributors
        ["settings.contributors"] = "Colaboradores",
        ["settings.contributors_thanks_title"] = "Obrigado",
        ["settings.contributors_intro"] = "O AetherLove não seria possível sem:",
        ["settings.contributors_leads"] = "Líderes do projeto: Astraea & Nihal",
        ["settings.contributors_council"] = "The Chon-Chon Council",
        ["settings.contributors_moderation"] = "Moderação: Su",
        ["settings.contributors_translators"] = "Tradutores: Tears, Mufami, Terashi, Su, Astraea",
        ["settings.contributors_xivauth"] = "XIVAuth por KazWolfe",
        ["settings.contributors_punish"] = "Puni.sh",
        ["settings.contributors_dalamud"] = "O projeto Dalamud",
        ["settings.contributors_testers"] = "Todos os maravilhosos beta testers por toda a Eorzea.",
    };
}
