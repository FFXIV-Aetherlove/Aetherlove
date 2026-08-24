namespace AetherLove.Services.Localization;

internal static class SettingsPt
{
    public static readonly System.Collections.Generic.Dictionary<string, string> Strings = new()
    {
        // Section labels + hub menu
        ["settings.section_plugin_settings"] = "Configurações do plugin",
        ["settings.section_phone_size"] = "Tamanho do telefone",
        ["settings.section_plugin_language"] = "Idioma do plugin",
        ["settings.section_general"] = "Configurações gerais",
        ["settings.section_notifications"] = "Notificações",
        ["settings.section_other"] = "Outros",
        ["settings.section_danger_zone"] = "Zona de perigo",
        ["settings.menu_language_theme"] = "Idioma e tema",
        ["settings.menu_appearance"] = "Aparência do telefone",
        ["settings.menu_chat_colors"] = "Aparência do chat",
        ["settings.section_theme"] = "Tema",
        ["settings.back_arrow"] = "← Voltar",
        ["settings.chat_own_bg"] = "Fundo do seu chat",
        ["settings.chat_own_fg"] = "Texto do seu chat",
        ["settings.chat_peer_bg"] = "Fundo do chat do outro",
        ["settings.chat_peer_fg"] = "Texto do chat do outro",
        ["settings.chat_reset"] = "Redefinir",

        // Phone size picker (AppearancePicker)
        ["settings.phone_size_small"] = "Pequeno",
        ["settings.phone_size_medium"] = "Médio",
        ["settings.phone_size_large"] = "Grande",
        ["settings.phone_size_xl"] = "XL",
        ["settings.phone_size_xxl"] = "XXL",
        ["settings.phone_size_caption"] = "Escala todo o telefone. XS é para telas pequenas ou no modo janela; tamanhos maiores combinam com telas de resolução mais alta, e XL e XXL são feitos para 4K.",
        ["settings.section_mini_phone_size"] = "Tamanho do telefone em miniatura",
        ["settings.mini_phone_size_caption"] = "Tamanho da bolha minimizada (exibida quando o telefone está minimizado). A pré-visualização abaixo mostra o tamanho selecionado.",

        // General
        ["settings.confirm_before_close"] = "Confirmar antes de fechar o telefone",

        // Buttons
        ["settings.terms_of_service"] = "Termos de Serviço",
        ["settings.view_changelog"] = "Ver registro de alterações",
        ["settings.send_feedback"] = "Enviar feedback",
        ["settings.cancel"] = "Cancelar",

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
        ["settings.hide_notifications_in_combat"] = "Ocultar notificações em combate",
        ["settings.hide_notifications_in_combat_tooltip"] = "Quando ativado, você não recebe nenhuma notificação (avisos no chat do jogo, popups ou sons) enquanto estiver em combate.",
        ["settings.auto_open_minimized"] = "Abrir minimizado automaticamente ao fazer login",
        ["settings.pulse_optout"] = "Mensagens ocasionais no jogo",
        ["settings.pulse_optout_tooltip"] = "Ocasionalmente, o AetherLove pode deixar uma mensagem divertida no chat do jogo. Desative para parar.",
        ["settings.combat_behavior"] = "Ao entrar em combate",
        ["settings.combat_behavior_hide"] = "Ocultar o telefone",
        ["settings.combat_behavior_minimize"] = "Minimizar para bolha",
        ["settings.combat_behavior_leave_open"] = "Deixar aberto",
        ["settings.notification_sound"] = "Som de notificação",
        ["settings.play"] = "Tocar",

        // Delete account confirmation
        ["settings.delete_bullet_matches"] = "Todos os teus matches serão removidos.",
        ["settings.delete_bullet_preferences"] = "As Tuas preferências de match serão apagadas.",
        ["settings.delete_bullet_pictures"] = "As Tuas fotos de perfil serão removidas.",
        ["settings.delete_previous_failed"] = "A tentativa anterior falhou: {0}",

        // Deleting / deleted views
        ["settings.deleting_title"] = "Excluindo conta",
        ["settings.deleting_body"] = "A remover os teus dados e a desfazer matches com os contatos",

        // Warnings
        ["settings.warnings_title"] = "Advertências da conta",
        ["settings.no_warnings"] = "Nenhuma advertência registrada.",

        // Moderator messages
        ["settings.modmsg_title"] = "Mensagens do moderador",
        ["settings.no_modmsg"] = "Nenhuma mensagem registrada.",
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
        ["settings.contributors_vavenn"] = "Vavenn (o modo moderno do Stacker)",
        ["settings.contributors_testers"] = "Todos os maravilhosos beta testers por toda a Eorzea.",

        // added after update 1.4.0
        ["settings.lock_position"] = "Bloquear posição",
        ["settings.lock_position_caption"] = "Ao bloquear a posição, não será possível mover o telefone (grande e mini); eles ficarão fixos no lugar.",

        // added after update 1.4.3
        ["settings.show_during_gpose"] = "Mostrar o telefone durante a pose de grupo",
        ["settings.show_during_gpose_tooltip"] = "Mantém o telefone visível durante a pose de grupo (/gpose), substituindo a definição do Dalamud que oculta as janelas dos plugins durante a pose de grupo.",
        ["settings.hide_during_cutscene"] = "Mostrar o telefone durante as cenas",
        ["settings.hide_during_cutscene_tooltip"] = "Mantém o telefone visível enquanto uma cena está a decorrer. Desativado, oculta-o durante as cenas (predefinição).",
        ["settings.tomestone_emote"] = "Mostrar o gesto de tomo ao usar a app",
        ["settings.tomestone_emote_tooltip"] = "Enquanto o telefone está aberto, a tua personagem usa o gesto /tomescroll, como se o estivesse a ler. Só começa numa zona segura e nunca interrompe outro gesto.",

        // added after update 1.5.0
        ["settings.menu_supporter"] = "Apoiante",
        ["settings.supporter_link_button"] = "Ligar conta do Patreon",
        ["settings.supporter_contacting"] = "A contactar o servidor...",
        ["settings.supporter_awaiting_browser"] = "Conclui a ligação no teu navegador e depois volta aqui.",
        ["settings.supporter_open_again"] = "Abrir o navegador novamente",
        ["settings.supporter_cancel"] = "Cancelar",
        ["settings.supporter_you_are_title"] = "Você é Supporter",
        ["settings.supporter_you_are_body"] = "Sua conta do Patreon foi vinculada com sucesso e seu status de Supporter foi ativado.",
        ["settings.supporter_nomember_title"] = "Nenhuma assinatura encontrada",
        ["settings.supporter_nomember_body"] = "Sua conta do Patreon foi vinculada, mas nenhuma assinatura ativa foi encontrada. Se você acabou de assinar, seu status de Supporter é concedido automaticamente em algumas horas. Você também pode desvincular e tentar novamente quando sua assinatura estiver ativa no Patreon.",
        ["settings.supporter_not_entitled"] = "Ainda não foi encontrada uma subscrição ativa. Se acabaste de subscrever, o teu cargo é atribuído automaticamente dentro de algumas horas.",
        ["settings.supporter_unlink_button"] = "Desligar Patreon",
        ["settings.supporter_unlink_confirm"] = "Desligar esta conta do Patreon? O teu cargo de Apoiante será removido.",
        ["settings.supporter_failed"] = "A ligação falhou. Tenta novamente.",
        ["settings.supporter_retry"] = "Tentar novamente",
        ["settings.supporter_unavailable"] = "A ligação de apoiante está indisponível de momento. Volta mais tarde.",
        ["settings.supporter_link_expired"] = "O pedido de ligação expirou. Tenta novamente.",

        // added after update 1.5.1
        ["settings.supporter_linked"] = "Ligado",
        ["settings.supporter_title"] = "Torna-te apoiante",
        ["settings.supporter_intro"] = "Podes apoiar o projeto financeiramente através do nosso Patreon. Todas as funcionalidades do AetherLove são gratuitas para todos, e nada está nem alguma vez estará bloqueado atrás de um paywall. Os apoiantes recebem apenas um agradecimento sincero: limites mais generosos e alguns extras brilhantes, a nossa forma de dizer que não conseguiríamos fazer isto sem ti.",
        ["settings.supporter_perks_header"] = "Vantagens do AetherLove",
        ["settings.supporter_msgr_perks_header"] = "Vantagens do Messenger",
        ["settings.supporter_perk_msgr_groups_title"] = "Chats de grupo enormes",
        ["settings.supporter_perk_msgr_groups_body"] = "Adiciona amigos ilimitados por código, tem até 10 chats de grupo ao mesmo tempo e junta até 15 pessoas em cada um.",
        ["settings.supporter_perk_msgr_storage_title"] = "Mais armazenamento no Messenger",
        ["settings.supporter_perk_msgr_storage_body"] = "Mais espaço para as selfies e imagens que partilhas, e prazos mais longos antes de desaparecerem.",
        ["settings.supporter_perk_profiles_title"] = "Dois perfis",
        ["settings.supporter_perk_profiles_body"] = "Usa dois perfis do AetherLove ao mesmo tempo, perfeito para gerir vários OCs ou separar o teu RP do OOC.",
        ["settings.supporter_perk_photos_title"] = "Mais espaço para brilhar",
        ["settings.supporter_perk_photos_body"] = "Até 5 fotos de perfil extra, e mais 2 por cada personagem de RP.",
        ["settings.supporter_perk_superlike_title"] = "Superlike",
        ["settings.supporter_perk_superlike_body"] = "Mostra a alguém que te chamou mesmo à atenção. A pessoa é notificada de que lhe deste um Superlike, e se te der like de volta, ficam em par de imediato.",
        ["settings.supporter_perk_rewinds_title"] = "5 recuos por dia",
        ["settings.supporter_perk_rewinds_body"] = "Deslizaste cedo demais? Recua até 5 vezes por dia em vez de apenas uma.",
        ["settings.supporter_perk_analytics_title"] = "Análises mais profundas",
        ["settings.supporter_perk_analytics_body"] = "Análises e estatísticas extra sobre quem gosta de ti e como o teu perfil se sai na realidade.",
        ["settings.supporter_perk_colors_title"] = "Cores vivas",
        ["settings.supporter_perk_colors_body"] = "O teu nome brilha como um arco-íris, mudando lentamente de cores impossíveis de ignorar.",
        ["settings.supporter_perk_badge_title"] = "Selo de Apoiante",
        ["settings.supporter_perk_badge_body"] = "Uma etiqueta de Apoiante e uma pequena estrela ao lado do teu nome. Aquele encanto a mais, merecido.",
        ["settings.supporter_how_heading"] = "Como funciona",
        ["settings.supporter_how_intro"] = "Temos três níveis a preços diferentes para escolheres o que te for confortável, e todos desbloqueiam exatamente as mesmas vantagens. Nenhum nível recebe mais do que outro.",
        ["settings.supporter_step1_title"] = "1. Subscreve no Patreon",
        ["settings.supporter_step1_body"] = "Cria uma conta no Patreon e subscreve qualquer um dos nossos níveis de apoiante, todos desbloqueiam as mesmas vantagens.",
        ["settings.supporter_step2_title"] = "2. Liga à AetherLove",
        ["settings.supporter_step2_body"] = "Liga a tua conta do Patreon clicando no botão abaixo. Se tiveres uma subscrição ativa, a tua conta fica premium de imediato.",
        ["settings.supporter_become"] = "Torna-te Apoiante no Patreon",
        ["settings.supporter_data_note"] = "Só guardamos o teu id de utilizador do Patreon e se és membro da nossa campanha. Nunca guardamos o teu nome, email ou contas sociais.",
        ["settings.sup_learn_title"] = "Apoia o AetherLove",
        ["settings.sup_learn_body"] = "Esta pessoa apoia o AetherLove. Queres ver o que apoiar o projeto te dá? Fotos extra, superlikes, estilos de nome, estatísticas bónus e muito mais.",
        ["settings.sup_learn_more"] = "Mais informações",

        ["settings.sup_thanks_title"] = "Você agora é Supporter!",
        ["settings.sup_thanks_sub"] = "Obrigado por apoiar o AetherLove!",
        ["settings.sup_thanks_body"] = "Seu apoio mantém os servidores no ar. Aproveite seus novos benefícios!",
        ["settings.sup_thanks_continue"] = "Continuar",
        // AetherLove-app notification master (added after update 1.5.1)
        ["settings.enable_aetherlove_notifications"] = "Ativar notificações do AetherLove",
        ["settings.menu_nsfw"] = "Definições NSFW",
        // DTR server-info-bar entries (added after update 1.5.1)
        ["dtr.chats"] = "Conversas",
        ["dtr.matches"] = "Matches",
        ["dtr.news"] = "Notícias",
        ["settings.show_dtr_count"] = "Mostrar contadores de não lidos na barra de informações do servidor",
        ["settings.show_dtr_count_tooltip"] = "Adiciona entradas do AetherLove (conversas, matches, notícias) à barra de informações do servidor do jogo, ao lado dos FPS e do ping. Cada uma mostra seu contador de não lidos e se oculta quando não há nada novo. Você também pode ocultar entradas individuais nas configurações da barra de informações do servidor do Dalamud.",

        // Multi-profile: the profile picker, switching, and delete-profile (added after update 1.5.1)
        ["settings.menu_switch_profile"] = "Trocar de perfil",
        ["settings.delete_profile"] = "Excluir perfil",
        ["settings.delete_profile_warning_intro"] = "Excluir este perfil do AetherLove é permanente. Antes de continuar, isto é exatamente o que acontece:",
        ["settings.delete_bullet_profile"] = "Este perfil de namoro é removido e não pode ser restaurado.",
        ["settings.delete_profile_account_stays"] = "Sua conta AetherOS e seus outros aplicativos não são afetados. Você pode criar um novo perfil a qualquer momento.",
        ["picker.title"] = "Escolher perfil",
        ["picker.subtitle"] = "Quem vai dar swipe hoje?",
        ["picker.current"] = "Perfil atual",
        ["picker.banned"] = "Banido",
        ["picker.finish_setup"] = "Concluir a configuração",
        ["picker.locked"] = "Vaga de supporter",
        ["picker.create"] = "Novo perfil",
        ["picker.create_sub"] = "Começar um novo perfil de namoro",
        ["picker.create_secondary"] = "Criar um perfil secundário",
        ["picker.create_supporter_pitch"] = "Um segundo perfil do AetherLove é um benefício de supporter. Apoie o projeto para desbloquear outra persona de namoro, com seus próprios matches e conversas, totalmente separada da primeira.",
        ["picker.locked_supporter_pitch"] = "Este perfil ocupa uma vaga de supporter. Renove seu status de supporter para abri-lo novamente; nada foi excluído, ele está apenas bloqueado.",
        ["picker.switching"] = "Trocando de perfil...",
        ["picker.switch_failed"] = "Não foi possível trocar de perfil. Tente novamente.",
        ["picker.share_as_title"] = "Compartilhar como...",
        ["picker.share_as_body"] = "Qual perfil deve compartilhar isto?",
        ["picker.share_as_current"] = "{0} (atual)",

        // Messenger (added after update 1.5.1)
        ["dtr.messenger"] = "Messenger",

        // added after update 2.0.0.0
        ["settings.section_time_format"] = "Formato da hora",
        ["settings.time_24h"] = "24 horas",
        ["settings.time_12h"] = "12 horas",

        // added after update 2.0.1
        ["settings.audio_sounds_off"] = "Os sons de notificação estão desativados.",
        ["settings.audio_sounds_enable"] = "Ativar sons de notificação",
        ["settings.audio_volume"] = "Volume das notificações",
        ["settings.audio_device"] = "Dispositivo de saída",
        ["settings.audio_device_default"] = "Padrão do sistema",
        ["settings.audio_test"] = "Tocar som de teste",
        ["settings.audio_test_ok"] = "A tocar! Se não ouvir nada, experimente outro dispositivo de saída acima.",
        ["settings.font_header"] = "Fonte",
        ["settings.font_caption"] = "Aplica-se a todo o texto do telemóvel. Caracteres em falta numa fonte usam a fonte padrão.",
        ["settings.font_default"] = "Padrão",
        ["settings.phone_size_header"] = "Tamanho do telemóvel",
        ["settings.mini_phone_size_header"] = "Tamanho em miniatura",

        // added after update 2.1.3
        ["settings.staff_notices_title"] = "Mensagens da equipe",
        ["settings.staff_notices_empty"] = "Ainda não há nada da equipe.",
        ["settings.staff_warnings_section"] = "Advertências",
        ["settings.staff_messages_section"] = "Mensagens",

        // added after update 2.2.3
        ["settings.premium_themes"] = "Temas premium",
        ["settings.premium_refresh"] = "Atualizar este tema",
        ["settings.premium_backgrounds"] = "Fundos premium",
        ["settings.premium_enable_failed"] = "Não foi possível mudar",
        ["settings.phone_size_xs"] = "XS",
        ["settings.supporter_store_perks_header"] = "Na loja",
        ["settings.supporter_perk_store_title"] = "10% de desconto na loja",
        ["settings.supporter_perk_store_body"] = "Supporters pagam 10% a menos em cada compra da loja, além de qualquer promoção ativa.",

        // Timers (added after update 2.3.3)
        ["dtr.timers"] = "{0} {1}",
        // text translation (added after update 2.4.0)
        ["settings.section_translation"] = "Tradução de textos",
        ["settings.translation_enable"] = "Ativar traduções",
        ["settings.translation_language"] = "Traduzir para",
        ["settings.translation_search"] = "Pesquisar idiomas...",
    };
}
