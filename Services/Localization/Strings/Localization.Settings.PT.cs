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

        // Phone size picker (AppearancePicker)
        ["settings.phone_size_small"] = "Pequeno",
        ["settings.phone_size_medium"] = "Médio",
        ["settings.phone_size_large"] = "Grande",
        ["settings.phone_size_caption"] = "Escala todo o telefone. Tamanhos maiores combinam com telas de resolução mais alta; Grande pode não caber em uma tela 1080p.",

        // General
        ["settings.disable_startup_heartbeat"] = "Desativar som de batimento na inicialização",

        // Buttons
        ["settings.view_changelog"] = "Ver registro de alterações",
        ["settings.send_feedback"] = "Enviar feedback",
        ["settings.delete_account"] = "Excluir Conta",
        ["settings.create_new_profile"] = "Criar um novo perfil",
        ["settings.cancel"] = "Cancelar",
        ["settings.back"] = "Voltar",

        // Privacy
        ["settings.always_blur_nsfw"] = "Sempre desfocar NSFW",
        ["settings.always_blur_nsfw_tooltip"] = "Quando ativado, as fotos extras marcadas como NSFW em outros perfis ficam desfocadas até você clicar para revelar cada uma. Avatares e retratos principais são sempre safe-for-work, independentemente disso. Desativar isto mostra todas as fotos como são.",
        ["settings.nsfw_profile"] = "Meu perfil é NSFW (18+)",
        ["settings.nsfw_profile_tooltip"] = "Marca seu perfil como adulto/NSFW para que só seja mostrado a pessoas que ativaram o NSFW. É ativado automaticamente quando você adiciona fotos NSFW ou escolhe roleplay 18+, e permanece ativo até você removê-los.",
        ["settings.nsfw_profile_locked"] = "Você não pode desativar isto enquanto tiver fotos NSFW ou roleplay 18+ (ERP) selecionado. Remova primeiro suas imagens NSFW e desmarque o roleplay 18+.",

        // Notifications
        ["settings.enable_notifications"] = "Ativar notificações",
        ["settings.enable_notifications_tooltip"] = "Interruptor principal de todas as notificações. Desative isto para silenciar todos os avisos no chat do jogo, popups e sons abaixo.",
        ["settings.enable_notification_sounds"] = "Ativar sons de notificação",
        ["settings.enable_notification_sounds_tooltip"] = "Os sons de notificação só tocam se o áudio do jogo e o áudio de efeitos especiais não estiverem mudos. O controle de volume é feito pelo volume do Windows.",
        ["settings.announce_messages_chat"] = "Anunciar novas mensagens no chat do jogo",
        ["settings.announce_matches_chat"] = "Anunciar novos matches no chat do jogo",
        ["settings.popup_messages"] = "Mostrar um popup para novas mensagens",
        ["settings.popup_matches"] = "Mostrar um popup para novos matches",
        ["settings.auto_open_minimized"] = "Abrir minimizado automaticamente ao fazer login",
        ["settings.pulse_optout"] = "Mensagens ocasionais no jogo",
        ["settings.pulse_optout_tooltip"] = "De vez em quando, o AetherLove pode deixar uma mensagem divertida no seu chat do jogo. Desative para parar.",
        ["settings.combat_behavior"] = "Ao entrar em combate",
        ["settings.combat_behavior_hide"] = "Ocultar AetherLove",
        ["settings.combat_behavior_minimize"] = "Minimizar para bolha",
        ["settings.combat_behavior_leave_open"] = "Deixar aberto",
        ["settings.notification_sound"] = "Som de notificação",
        ["settings.play"] = "Tocar",

        // Delete account confirmation
        ["settings.delete_warning_intro"] = "Esta ação é permanente e não pode ser desfeita. Por favor, leia o seguinte com atenção antes de continuar:",
        ["settings.delete_bullet_account"] = "Sua conta será excluída permanentemente.",
        ["settings.delete_bullet_matches"] = "Todos os seus matches serão removidos.",
        ["settings.delete_bullet_preferences"] = "Suas preferências de match serão apagadas.",
        ["settings.delete_bullet_pictures"] = "Suas fotos de perfil serão removidas.",
        ["settings.delete_reregister"] = "Você sempre pode se cadastrar novamente a qualquer momento.",
        ["settings.delete_previous_failed"] = "A tentativa anterior falhou: {0}",

        // Deleting / deleted views
        ["settings.deleting_title"] = "Excluindo conta",
        ["settings.deleting_body"] = "Removendo seus dados e desfazendo matches com os contatos",
        ["settings.deleted_title"] = "Conta excluída",
        ["settings.deleted_body"] = "Sua conta foi excluída, seus dados e fotos foram removidos, e seus matches foram desfeitos. Agora você pode remover o plugin ou fazer o onboarding e criar um novo perfil.",

        // Warnings
        ["settings.warnings_button_unseen"] = "Advertências ({0} não vistas / {1})",
        ["settings.warnings_button"] = "Advertências ({0})",
        ["settings.warnings_title"] = "Advertências",
        ["settings.no_warnings"] = "Nenhuma advertência registrada.",
        ["settings.back_to_settings_arrow"] = "← Voltar às configurações",

        // Feedback flow
        ["settings.back_to_settings"] = "Voltar às configurações",
        ["settings.feedback_thanks"] = "Obrigado! Seu feedback foi enviado à equipe do AetherLove.",
        ["settings.feedback_intro"] = "Encontrou um bug, tem uma ideia ou quer sugerir algo? Conte para a gente.",
        ["settings.feedback_note"] = "Observação: o feedback não pode ser usado para recorrer de um banimento ou advertência.",
        ["settings.feedback_type"] = "Tipo",
        ["settings.feedback_kind_bug"] = "Bug",
        ["settings.feedback_kind_improvement"] = "Melhoria",
        ["settings.feedback_kind_other"] = "Outro",
        ["settings.feedback_your_message"] = "Sua mensagem",
        ["settings.sending"] = "Enviando…",
        ["settings.submit"] = "Enviar",
        ["settings.feedback_rate_limited"] = "Você só pode enviar feedback {0} vezes por hora. Por favor, tente novamente mais tarde.",
        ["settings.feedback_send_failed"] = "Não foi possível enviar seu feedback. Por favor, tente novamente.",
    };
}
