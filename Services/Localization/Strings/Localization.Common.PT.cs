namespace AetherLove.Services.Localization;

internal static class CommonPt
{
    public static readonly System.Collections.Generic.Dictionary<string, string> Strings = new()
    {
        // Generic
        ["common.ok"] = "OK",
        ["common.confirm"] = "Confirmar",
        ["common.cancel"] = "Cancelar",
        ["common.loading"] = "Carregando…",
        ["common.try_again"] = "Tentar Novamente",
        ["common.i_understand"] = "Eu entendi",
        ["common.sign_out"] = "Sair",
        ["common.got_it"] = "Entendi!",
        ["common.moderator_notes_label"] = "Notas do moderador",
        ["common.server_unreachable_detail"] = "Não foi possível acessar o servidor: {0}",

        // Banned screen
        ["common.banned_title"] = "Conta banida",
        ["common.banned_body"] = "Sua conta do AetherLove foi banida. Você não pode mais usar o serviço.",
        ["common.banned_reason_label"] = "Motivo",
        ["common.banned_uninstall_hint"] = "Você pode fechar esta janela e desinstalar o plugin a qualquer momento.",

        // Offline screen
        // Outdated-plugin screen
        ["common.outdated_title"] = "Atualização necessária",
        ["common.outdated_body"] = "Você está usando uma versão desatualizada do AetherLove. O servidor não oferece mais suporte a esta versão, então o plugin não consegue se conectar.",
        ["common.outdated_hint"] = "Atualize o plugin no instalador de plugins do Dalamud e reabra o AetherLove.",

        ["common.offline_title"] = "O AetherLove está offline",
        ["common.offline_body"] = "Não conseguimos acessar os servidores do AetherLove agora. O app precisa de uma conexão ativa para explorar, dar match e conversar, então ele fica pausado até voltarmos a ficar online.",
        ["common.offline_reconnecting"] = "Reconectando…",
        ["common.offline_keep_trying"] = "Continuaremos tentando automaticamente.",

        // Passphrase unlock screen
        ["common.passphrase_title"] = "Digite sua frase-senha de criptografia",
        ["common.passphrase_intro"] = "Reconhecemos esta conta, mas este dispositivo ainda não tem sua chave de chat. Digite a frase-senha que você definiu no seu primeiro dispositivo para desbloquear seu histórico de conversas.",
        ["common.passphrase_forgot"] = "Esqueceu sua frase-senha? Não há recuperação, mas você pode sair abaixo e criar uma conta nova. Seu histórico de conversas desta conta será perdido.",
        ["common.passphrase_bundle_load_failed"] = "Não foi possível carregar o pacote de criptografia do servidor.",
        ["common.passphrase_empty"] = "Por favor, digite sua frase-senha.",
        ["common.passphrase_incorrect"] = "Frase-senha incorreta. Tente novamente.",
        ["common.passphrase_unlock_failed"] = "Falha ao desbloquear: {0}",
        ["common.unlock"] = "Desbloquear",
        ["common.unlocking"] = "Desbloqueando…",

        // Warning acknowledge screen
        ["common.warnings_heading_one"] = "Você tem uma advertência de moderação",
        ["common.warnings_heading_many"] = "Você tem {0} advertências de moderação",
        ["common.warnings_body"] = "Por favor, leia a(s) seguinte(s) advertência(s) da equipe de moderação. Reincidências podem resultar em suspensão da conta.",
        ["common.warnings_submit_error"] = "Não foi possível acessar o servidor: {0}. Toque para tentar de novo.",
        ["common.acknowledging"] = "Confirmando…",

        // Photo moderation
        ["common.nsfw_decl_unselected"] = "selecione uma opção abaixo",
        ["common.nsfw_decl_sfw"] = "esta foto é SFW",
        ["common.nsfw_decl_nsfw"] = "esta foto é NSFW",
        ["common.lalafell_nsfw_title"] = "NSFW não disponível",
        ["common.lalafell_nsfw_body"] = "Não permitimos fotos NSFW de personagens Lalafell. Como os Lalafell têm aparência infantil, aplicamos essa política de forma uniforme a todas as contas Lalafell e não fazemos exceções caso a caso.\n\nSua foto foi redefinida para SFW. Se esta foto não for safe-for-work, por favor remova-a e envie outra.",
        ["common.undeclared_photo_title"] = "Declaração obrigatória",
        ["common.undeclared_photo_body"] = "Você precisa selecionar se sua outra foto é SFW ou NSFW na caixa de seleção antes de enviar outra.",

        // Changelog window
        ["common.changelog_window_title"] = "AetherLove — Novidades",
        ["common.whats_new"] = "Novidades",
        ["common.changelog_empty"] = "Nenhuma entrada de registro de alterações disponível.",
        ["common.changelog_latest"] = "Mais recente",
        ["common.changelog_important"] = "Importante",
        ["common.changelog_new_features"] = "Novos recursos",
        ["common.changelog_bug_fixes"] = "Correções de bugs",

        // Rate limit modal
        ["common.rate_limit_title"] = "Calma aí",
        ["common.rate_limit_noun_profile"] = "perfil",
        ["common.rate_limit_noun_images"] = "imagens",
        ["common.rate_limit_body"] = "Você só pode alterar seu(s) {0} {1} vezes por hora. Por favor, tente novamente em {2}.",
        ["common.rate_limit_retry_moment"] = "um instante",
        ["common.rate_limit_retry_one_second"] = "1 segundo",
        ["common.rate_limit_retry_seconds"] = "{0} segundos",
        ["common.rate_limit_retry_one_minute"] = "1 minuto",
        ["common.rate_limit_retry_minutes"] = "{0} minutos",

        // Emoji picker
        ["common.emoji_search_hint"] = "Buscar emoji...",
        // Bottom navigation bar
        ["common.nav_swipe"] = "Swipe",
        ["common.nav_matches"] = "Matches",
        ["common.nav_profile"] = "Perfil",
        ["common.nav_settings"] = "Config.",
        ["common.nav_minimize"] = "Recolher",

        ["common.emoji_none_found"] = "Nenhum emoji encontrado.",

        // Close-plugin confirmation modal
        ["common.close_plugin_tooltip"] = "Fechar AetherLove",
        ["common.close_plugin_title"] = "Fechar AetherLove?",
        ["common.close_plugin_body"] = "Isso apenas oculta a janela. Você permanecerá conectado e continuará recebendo novos matches e mensagens enquanto o plugin estiver ativado.\n\nReabra a janela a qualquer momento digitando {0} no chat.",
        ["common.close_plugin_tip"] = "Dica: use o botão Minimizar na parte inferior para manter a pequena bolha flutuante visível com seu indicador de notificações.",
        ["common.close"] = "Fechar",

        // Save-error modal
        ["common.save_error_title"] = "Algo deu errado",
        ["common.save_error_intro"] = "Não foi possível salvar as suas alterações:",
        ["common.save_error_report"] = "Se isto continuar a acontecer, reporte o erro no nosso Discord.",
        ["common.save_error_unknown"] = "Ocorreu um erro inesperado.",

        // Image requirements modal
        ["common.img_requirements_title"] = "Não é possível usar a imagem",
        ["common.img_invalid"] = "Esse arquivo não é uma imagem válida ou o formato não é compatível.",
        ["common.img_too_small"] = "Essa imagem tem apenas {0}×{1} px, o que é pequeno demais.",
        ["common.img_requirements_sizes"] = "Avatares precisam de pelo menos {0}×{1} px e fotos de perfil de pelo menos {2}×{3} px. Escolha uma imagem maior.",

        // Image crop window
        ["common.loading_image"] = "Carregando imagem...",
        ["common.use_this_crop"] = "Usar este recorte",
    };
}
