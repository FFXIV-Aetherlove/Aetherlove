namespace AetherLove.Services.Localization;

internal static class CommonPt
{
    public static readonly System.Collections.Generic.Dictionary<string, string> Strings = new()
    {
        // Generic
        ["common.ok"] = "OK",
        ["common.cancel"] = "Cancelar",
        ["common.loading"] = "Carregando…",
        ["common.try_again"] = "Tentar Novamente",
        ["common.i_understand"] = "Eu entendi",
        ["common.sign_out"] = "Sair",
        ["common.got_it"] = "Entendi!",
        ["common.server_unreachable_detail"] = "Não foi possível acessar o servidor: {0}",

        // Banned screen
        ["common.banned_title"] = "Perfil banido",
        ["common.banned_body"] = "Um perfil do AetherLove banido significa que você não pode mais usar o AetherLove com este perfil. Você ainda pode usar os nossos outros apps. Para mais informações, abra um ticket de suporte no nosso Discord.",
        ["common.banned_reason_label"] = "Motivo",
        ["common.banned_uninstall_hint"] = "Use o botão de início abaixo para voltar à tela inicial.",

        // Offline screen
        // Outdated-plugin screen
        ["common.outdated_title"] = "Atualização necessária",
        ["common.outdated_body"] = "Está a usar uma versão desatualizada do AetherLove. O servidor não oferece mais suporte a esta versão, o plugin não consegue conectar-se.",
        ["common.outdated_hint"] = "Atualiza o plugin no instalador de plugins no Dalamud e reabre o AetherLove.",

        ["common.offline_title"] = "Os serviços AetherOS estão offline de momento",
        ["common.offline_body"] = "O servidor provavelmente está offline por causa de atualizações ou manutenção. Isso não deve demorar mais de 2 minutos!",
        ["common.offline_reconnecting"] = "Reconectando…",
        ["common.offline_taking_long"] = "Isso está demorando mais do que o normal. Entre no nosso Discord para saber o status mais recente.",
        ["common.offline_join_discord"] = "Entrar no Discord",

        // Passphrase unlock screen
        ["common.passphrase_title"] = "Digite a tua frase-senha de criptografia",
        ["common.passphrase_intro"] = "Reconhecemos esta conta, mas este dispositivo ainda não tem a tua chave de chat. Digite a frase-senha que definiu no seu primeiro dispositivo para desbloquear o histórico de conversas.",
        ["common.passphrase_forgot"] = "Esqueceu sua frase-senha? Você pode redefinir suas chaves de criptografia abaixo. Tudo o que foi enviado antes da redefinição ficará ilegível para você.",

        // Passphrase reset (added after update 1.5.1)
        ["common.passphrase_reset_button"] = "Redefinir chaves de criptografia…",
        ["common.passphrase_reset_title"] = "Redefinir suas chaves de criptografia",
        ["common.passphrase_reset_warning"] = "Isso cria uma frase-senha e chaves de criptografia totalmente novas. Você perderá PERMANENTEMENTE o acesso a todas as mensagens anteriores à redefinição, e seus matches e contatos do Messenger verão um aviso de que você redefiniu suas chaves.",
        ["common.passphrase_reset_new"] = "Nova frase-senha",
        ["common.passphrase_reset_repeat"] = "Repita a nova frase-senha",
        ["common.passphrase_reset_mismatch"] = "As frases-senha não coincidem.",
        ["common.passphrase_reset_go"] = "Redefinir minhas chaves",
        ["common.passphrase_reset_running"] = "Redefinindo…",
        ["common.passphrase_bundle_load_failed"] = "Não foi possível carregar o pacote de criptografia do servidor.",
        ["common.passphrase_empty"] = "Por favor, digite sua frase-senha.",
        ["common.passphrase_incorrect"] = "Frase-senha incorreta. Tente novamente.",
        ["common.passphrase_unlock_failed"] = "Falha ao desbloquear: {0}",
        ["common.unlock"] = "Desbloquear",
        ["common.unlocking"] = "Desbloqueando…",

        // Encryption recovery screen
        ["common.recovery_title"] = "Configurar mensagens seguras",
        ["common.recovery_intro"] = "À tua conta faltam as chaves de encriptação, por isso ainda não podes enviar nem receber mensagens. Escolhe uma palavra-passe para as configurar. Guarda-a bem, não é possível recuperá-la.",
        ["common.recovery_button"] = "Ativar mensagens seguras",
        ["common.recovery_support"] = "Ainda com problemas? Termina sessão abaixo ou fala connosco no Discord.",

        // Warning acknowledge screen
        ["common.warnings_heading_one"] = "Tem uma advertência da moderação",
        ["common.warnings_heading_many"] = "Tem {0} advertências da moderação",
        ["common.warnings_body"] = "Por favor, leia a(s) seguinte(s) advertência(s) da equipe de moderação. Reincidências podem resultar em suspensão da conta.",
        ["common.warnings_submit_error"] = "Não foi possível acessar o servidor: {0}. Toque para tentar de novo.",
        ["common.acknowledging"] = "Confirmando…",

        // Moderator message screen
        ["common.modmsg_heading_one"] = "Você tem uma mensagem da equipe de moderação",
        ["common.modmsg_heading_many"] = "Você tem {0} mensagens da equipe de moderação",
        ["common.modmsg_body"] = "A equipe de moderação enviou o seguinte:",
        ["common.modmsg_got_it"] = "Entendi",

        // Photo moderation
        ["common.nsfw_decl_unselected"] = "seleciona uma opção abaixo",
        ["common.nsfw_decl_sfw"] = "esta foto é SFW",
        ["common.nsfw_decl_nsfw"] = "esta foto é NSFW",
        ["common.lalafell_nsfw_title"] = "NSFW não disponível",
        ["common.lalafell_nsfw_body"] = "Não permitimos fotos NSFW de personagens Lalafell. Como os Lalafell têm aparência infantil, aplicamos essa política de forma uniforme a todas as contas Lalafell e não fazemos exceções caso a caso.\n\nA foto foi redefinida para SFW. Se esta foto não for safe-for-work, por favor remova-a e envie outra.",
        ["common.undeclared_photo_title"] = "Declaração obrigatória",
        ["common.undeclared_photo_body"] = "Você precisa selecionar se a tua outra foto é SFW ou NSFW na caixa de seleção antes de enviar outra.",

        // Changelog window
        ["common.changelog_window_title"] = "AetherLove: Novidades",
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
        ["common.rate_limit_body"] = "Só podes alterar teu(s) {0} {1} vezes por hora. Por favor, tente novamente em {2}.",
        ["common.rate_limit_retry_moment"] = "um momento",
        ["common.rate_limit_retry_one_second"] = "1 segundo",
        ["common.rate_limit_retry_seconds"] = "{0} segundos",
        ["common.rate_limit_retry_one_minute"] = "1 minuto",
        ["common.rate_limit_retry_minutes"] = "{0} minutos",

        // Emoji picker
        ["common.emoji_search_hint"] = "Procurar emoji...",
        // Bottom navigation bar
        ["common.nav_swipe"] = "Swipe",
        ["common.nav_matches"] = "Matches",
        ["common.nav_settings"] = "Config.",

        ["common.emoji_none_found"] = "Nenhum emoji encontrado.",

        // Close-plugin confirmation modal
        ["common.close_plugin_tooltip"] = "Fechar AetherOS",
        ["common.minimize_tooltip"] = "Minimizar o AetherOS",
        ["common.close_plugin_title"] = "Fechar AetherLove?",
        ["common.close_plugin_body"] = "Isto apenas oculta a janela. Vais permanecerá conectado e vais continuar a receber novos matches e mensagens enquanto o plugin estiver ativado.\n\nAbre a janela a qualquer momento digitando {0} no chat.",
        ["common.close_plugin_tip"] = "Dica: usa o botão Minimizar na parte inferior para manter a pequena bolha flutuante visível com o indicador de notificações.",
        ["common.close_plugin_dont_ask"] = "Não voltar a mostrar esta janela",
        ["common.close"] = "Fechar",
        ["common.back"] = "Voltar",

        // Save-error modal
        ["common.save_error_title"] = "Algo deu errado",
        ["common.save_error_intro"] = "Não foi possível salvar as alterações:",
        ["common.save_error_report"] = "Se isto continuar a acontecer, reporta o erro no nosso Discord.",
        ["common.save_error_unknown"] = "Ocorreu um erro inesperado.",

        // Image requirements modal
        ["common.img_requirements_title"] = "Não é possível usar a imagem",
        ["common.img_invalid"] = "Este arquivo não é uma imagem válida ou o formato não é compatível.",
        ["common.img_too_small"] = "Esta imagem tem apenas {0}×{1} px, é muito pequena.",
        ["common.img_requirements_sizes"] = "Avatares precisam de pelo menos {0}×{1} px e fotos de perfil de pelo menos {2}×{3} px. Escolha uma imagem maior.",

        // Image crop window
        ["common.loading_image"] = "Carregando imagem...",
        ["common.use_this_crop"] = "Usar este recorte",

        // SFW gate (avatar / first photo)
        ["common.sfw_gate_title"] = "Perfil + Avatar: APENAS SFW",
        ["common.sfw_gate_subtitle"] = "O que NÃO é SFW:",
        ["common.sfw_gate_b1"] = "Nudez total de qualquer género.",
        ["common.sfw_gate_b2"] = "Mamilos visíveis de qualquer género.",
        ["common.sfw_gate_b3"] = "Pelos púbicos ou zonas genitais visíveis.",
        ["common.sfw_gate_b4"] = "Representações gráficas de sangue, ferimentos, feridas ou danos corporais.",
        ["common.sfw_gate_b5"] = "Tatuagens, marcas, símbolos ou texto que sejam obscenos, discriminatórios, de ódio, ou que visem indivíduos ou grupos com base em raça, etnia, nacionalidade, religião, género, orientação sexual ou outras características protegidas.",
        ["common.sfw_gate_b6"] = "Gestos, poses ou referências visuais sexuais que impliquem ou simulem atos sexuais, incluindo sexo oral, masturbação ou outra atividade sexual.",
        ["common.sfw_gate_secondary"] = "Podes na mesma carregar conteúdo NSFW nas tuas imagens de perfil secundárias.",
        ["common.sfw_gate_ack"] = "Compreendo as regras de SFW",

        // added after update (1.3.1)
        ["common.sfw_gate_race_gender"] = "Certifica-te de que a tua imagem principal mostra a raça e o género do teu personagem tal como definidos no teu perfil.",

        // added after update 1.4.3
        ["common.img_cloud_title"] = "Ficheiro não transferido",
        ["common.img_cloud_unavailable"] = "Esta imagem está guardada apenas online na nuvem (por exemplo, OneDrive) e não foi transferida para o teu PC, por isso não pode ser aberta. No Explorador de Ficheiros, clica com o botão direito, escolhe 'Manter sempre neste dispositivo', aguarda o visto verde e tenta novamente. Ou escolhe um ficheiro guardado localmente no teu PC.",
        ["common.emoji_favorites"] = "Favoritos",
        ["common.emoji_favorite_hint"] = "clica com o botão direito para adicionar ou remover dos favoritos",
        ["common.emoji_add_favorite"] = "Adicionar aos favoritos",
        ["common.emoji_remove_favorite"] = "Remover dos favoritos",
        ["common.selfie"] = "Selfie",
        ["common.selfie_instructions"] = "Arrasta ou redimensiona o quadro sobre o teu personagem e depois tira a foto.",
        ["common.selfie_take"] = "Tirar foto",
        ["common.selfie_capturing"] = "A capturar...",
        ["common.offline_maintenance"] = "O servidor está em manutenção.",

        // added after update 1.5.0
        ["common.nav_places"] = "Lugares",

        // Multi-profile switch nav slot (added after update 1.5.1)
        ["common.nav_switch"] = "Trocar",

        // Recovery gate, enter-existing-passphrase mode (added after update 1.5.1)
        ["common.recovery_enter_intro"] = "Este perfil ainda não tem suas chaves de criptografia. Digite sua frase-senha de criptografia para configurá-las.",

        // account moderation reconcile (added after update 2.0.0)
        ["common.moderation_warning_for"] = "Aviso para {0}",
        ["common.moderation_message_for"] = "Mensagem para {0}",
        ["common.account_disabled_title"] = "Conta banida",
        ["common.account_disabled_body"] = "Este recurso não está disponível enquanto a sua conta estiver banida.",

        // added after update 2.0.1
        ["common.passphrase_correct_unrecoverable"] = "A sua frase-senha está correta, mas nenhuma das suas chaves guardadas pôde ser aberta com ela. Contacte o suporte antes de considerar uma redefinição; ela deixaria as suas mensagens antigas ilegíveis para sempre.",

        // added after update 2.1.3
        ["common.staff_notice_heading_one"] = "Você tem um aviso da equipe",
        ["common.staff_notice_heading_many"] = "Você tem {0} avisos da equipe",
        ["common.staff_notice_body"] = "A equipe do AetherOS enviou o seguinte sobre a sua conta:",
        ["common.staff_notice_ack"] = "Entendi",

        // added after update 2.2.3
        ["common.travel_teleport_with"] = "Teleporte ({0})",
        ["common.travel_tooltip"] = "Viajar até aqui com {0}",

        // added after update 2.4.0
        ["common.session_expired_title"] = "Precisas de iniciar sessão outra vez",
        ["common.session_expired_body"] = "A tua sessão terminou, por isso o telemóvel já não consegue chegar ao AetherOS. Não é uma falha: está tudo a funcionar, apenas já não sabe quem tu és.",
        ["common.session_expired_button"] = "Iniciar sessão de novo",
        ["common.session_expired_hint"] = "O telemóvel reinicia e leva-te de volta ao ecrã de início de sessão.",
    
        // the file picker (added after update 2.4.0)
        ["picker.quick_links"] = "Locais",
        ["picker.favorites"] = "Favoritos",
        ["picker.drives"] = "Unidades",
        ["picker.place_desktop"] = "Área de trabalho",
        ["picker.place_documents"] = "Documentos",
        ["picker.place_downloads"] = "Downloads",
        ["picker.place_pictures"] = "Imagens",
        ["picker.place_screenshots"] = "Capturas do jogo",
        ["picker.search_hint"] = "Pesquisar nesta pasta...",
        ["picker.empty"] = "Esta pasta está vazia.",
        ["picker.open"] = "Abrir",
        ["picker.nothing_selected"] = "Nada selecionado",
        ["picker.new_folder_hint"] = "Nome da pasta...",
        ["picker.new_folder_create"] = "Criar",
        ["picker.show_hidden"] = "Arquivos ocultos",
        ["picker.sort_name"] = "Nome",
        ["picker.sort_date"] = "Data",
        ["picker.sort_size"] = "Tamanho",
        ["picker.tip_star"] = "Marcar ou desmarcar esta pasta como favorita (clique direito em um favorito para removê-lo)",
        ["picker.tip_edit_path"] = "Digitar um caminho",
        ["picker.preview_loading"] = "Carregando pré-visualização...",
        ["picker.save"] = "Salvar",
        ["picker.file_name_hint"] = "Nome do arquivo...",
        ["picker.selected_count"] = "{0} selecionados",
    };
}
