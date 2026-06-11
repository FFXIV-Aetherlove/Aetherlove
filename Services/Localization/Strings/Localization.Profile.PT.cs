namespace AetherLove.Services.Localization;

internal static class ProfilePt
{
    public static readonly System.Collections.Generic.Dictionary<string, string> Strings = new()
    {
        // ProfileScreen — load / empty states
        ["profile.load_failed"] = "Não foi possível carregar o perfil: {0}",
        ["profile.none_loaded"] = "Nenhum perfil carregado.",

        // ProfileScreen — sections
        ["profile.about"] = "Sobre",
        ["profile.looking_for"] = "Procurando por",
        ["profile.info"] = "Informações",
        ["profile.gender"] = "Gênero",
        ["profile.languages"] = "Idiomas",
        ["profile.timezone"] = "Fuso horário",
        ["profile.favourite_job"] = "Job favorito",
        ["profile.favourite_location"] = "Lugar favorito",
        ["profile.favourite_expansion"] = "Expansão favorita",
        ["profile.favourite_spotify_song"] = "Música favorita no Spotify",
        ["profile.favourite_movie"] = "Filme favorito",
        ["profile.favourite_anime"] = "Anime favorito",
        ["profile.favourite_ff_character"] = "Personagem favorito de FF",
        ["profile.sync_tool"] = "Ferramenta de sync",
        ["profile.uses_sync_tool"] = "Usa ferramenta de sync",
        ["profile.preferred"] = "Preferido",
        ["profile.yes"] = "Sim",
        ["profile.no"] = "Não",
        ["profile.weekday_playtimes"] = "Horários de jogo nos dias de semana  (Seg–Sex)",
        ["profile.weekend_playtimes"] = "Horários de jogo no fim de semana  (Sáb–Dom)",
        ["profile.timezone_value"] = "{0} (hora atual: {1})",

        // ProfileScreen — Spotify / NSFW pill
        ["profile.spotify_open_tooltip"] = "Clique para abrir no Spotify",
        ["profile.nsfw_reveal"] = "Clique para mostrar a imagem NSFW",

        // ProfileScreen — back pill
        ["profile.back_to_chat"] = "Voltar ao chat",
        ["profile.back_to_swiping"] = "Voltar a deslizar",

        // ProfileScreen — report flow
        ["profile.report_profile"] = "Denunciar perfil",
        ["profile.report_warning"] = "Denúncias falsas ou maliciosas geram advertências na sua própria conta, e abuso repetido pode resultar em suspensão. Só denuncie perfis que realmente violam as regras.",
        ["profile.report_prompt"] = "Diga aos nossos moderadores o que há de errado com {0}:",
        ["profile.this_profile"] = "este perfil",
        ["profile.report_agree"] = "Eu entendo que denúncias falsas podem resultar em advertências contra minha conta.",
        ["profile.submitting"] = "Enviando…",
        ["profile.cancel"] = "Cancelar",
        ["profile.submit_report"] = "Enviar denúncia",
        ["profile.report_submitted"] = "Denúncia enviada",
        ["profile.report_thanks"] = "Obrigado — nossos moderadores vão dar uma olhada. Você não verá este perfil de novo até puxar um novo do deck.",
        ["profile.closing"] = "Fechando…",
        ["profile.closing_in"] = "Fechando em {0} segundos",
        ["profile.close"] = "Fechar",

        // MyProfileScreen — tabs
        ["profile.tab_view"] = "Ver Perfil",
        ["profile.tab_edit"] = "Editar Perfil",
        ["profile.tab_images"] = "Trocar Imagens",

        // MyProfileScreen — edit tab load / save
        ["profile.load_profile_failed"] = "Não foi possível carregar seu perfil: {0}",
        ["profile.retry"] = "Tentar de novo",
        ["profile.save_failed"] = "Falha ao salvar: {0}",
        ["profile.saving"] = "Salvando…",
        ["profile.saved"] = "Salvo  ✓",
        ["profile.save_changes"] = "Salvar Alterações",

        // MyProfileScreen — edit form section headings
        ["profile.heading_identity"] = "Identidade",
        ["profile.heading_character"] = "Personagem",
        ["profile.heading_location"] = "Localização",
        ["profile.heading_languages"] = "Idiomas que Eu Falo",
        ["profile.heading_content"] = "Eu Curto os Seguintes Conteúdos",
        ["profile.heading_looking_for"] = "Estou Procurando Por",
        ["profile.heading_nsfw"] = "NSFW",
        ["profile.heading_optional"] = "Opcional",
        ["profile.heading_playtime"] = "Horário de Jogo",
        ["profile.heading_timezone"] = "Fuso horário",
        ["profile.heading_sync_tool"] = "Ferramenta de Sync",
        ["profile.heading_match_prefs"] = "Preferências de Match",

        // MyProfileScreen — edit form labels / hints
        ["profile.display_name"] = "Nome de Exibição",
        ["profile.display_name_hint"] = "Primeiro nome ou apelido, sem espaços.",
        ["profile.about_me"] = "Sobre Mim",
        ["profile.char_count"] = "{0} / 500 caracteres",
        ["profile.preview"] = "Prévia",
        ["profile.bio_placeholder"] = "Sua bio vai aparecer aqui…",
        ["profile.race"] = "Race",
        ["profile.region"] = "Região",
        ["profile.languages_hint"] = "Selecione todos os idiomas em que você se sente à vontade para conversar.",
        ["profile.content_hint"] = "Selecione tudo o que se aplica.",
        ["profile.looking_for_hint"] = "Ser honesto ajuda a encontrar matches melhores.",
        ["profile.nsfw_lalafell"] = "Os recursos adultos e NSFW não estão disponíveis enquanto sua race estiver definida como Lalafell. Consulte os Termos de Serviço para mais detalhes.",
        ["profile.nsfw_explainer"] = "NSFW significa \"Not Safe For Work\" (impróprio para o trabalho): conteúdo que contém nudez ou temas sexuais. Adira para ver e dar match com perfis NSFW.",
        ["profile.nsfw_optin"] = "Perfis NSFW: SIM",
        ["profile.favourite_job_tooltip"] = "O job ou papel que você mais curte. Digite para filtrar.",
        ["profile.favourite_spotify"] = "Música Favorita no Spotify",
        ["profile.spotify_tooltip"] = "Cole a URL de uma faixa do Spotify ou o ID da faixa.",
        ["profile.track_id"] = "ID da faixa: {0}",
        ["profile.favourite_ff_character_full"] = "Personagem Favorito de Final Fantasy",
        ["profile.weekday_playtimes_edit"] = "Horários de Jogo nos Dias de Semana (Seg–Sex)",
        ["profile.weekend_playtimes_edit"] = "Horários de Jogo no Fim de Semana (Sáb–Dom)",
        ["profile.sync_tool_hint"] = "Ferramentas de sync permitem que usuários que deram match compartilhem aparências de mods.",
        ["profile.match_prefs_body"] = "Conte com quem você gostaria de dar match. Estas preferências ajudam a mostrar as pessoas certas para você.",
        ["profile.all"] = "Todos",
        ["profile.none"] = "Nenhum",
        ["profile.clear"] = "Limpar",
        ["profile.filter_any_race"] = "  Sem seleção: qualquer race",
        ["profile.filter_any_gender"] = "  Sem seleção: qualquer gênero",
        ["profile.filter_any_region"] = "  Sem seleção: qualquer região",
        ["profile.filter_any_language"] = "  Sem seleção: sem preferência de idioma",
        ["profile.spoken_language"] = "Idioma Falado",
        ["profile.spoken_language_tooltip"] = "Deixe tudo desmarcado para dar match independentemente do idioma.",

        // MyProfileScreen.Images — tab text
        ["profile.load_photos_failed"] = "Não foi possível carregar suas fotos: {0}",
        ["profile.profile_picture"] = "Foto de Perfil",
        ["profile.profile_picture_desc"] = "Sua foto de perfil é exibida na lista de chats e nos cartões de match. Use um retrato quadrado em close do seu personagem de FFXIV.",
        ["profile.profile_photos"] = "Fotos do Perfil",
        ["profile.profile_photos_desc"] = "Adicione fotos de retrato ao seu perfil (proporção 10:16). O primeiro espaço é obrigatório; os espaços 2 a 4 são opcionais.",
        ["profile.declare_before_save"] = "Marque cada foto extra como SFW ou NSFW antes de salvar.",

        // MyProfileScreen.Images — avatar section
        ["profile.new_photo_ready"] = "Nova foto pronta, ainda não salva.",
        ["profile.change_photo"] = "Trocar Foto",
        ["profile.profile_picture_set"] = "Foto de perfil: Definida  ✓",
        ["profile.no_profile_picture"] = "Nenhuma foto de perfil definida.",
        ["profile.upload_avatar"] = "Enviar Avatar…",

        // MyProfileScreen.Images — slot grid + active slot controls
        ["profile.slot_main"] = "Principal",
        ["profile.tap_slot"] = "Toque em um espaço acima para adicionar ou trocar uma foto.",
        ["profile.main_photo"] = "Foto principal",
        ["profile.extra_photo"] = "Foto extra {0}",
        ["profile.photo_will_be_removed"] = "A foto será removida.",
        ["profile.undo"] = "Desfazer",
        ["profile.main_must_be_sfw"] = "Sua foto de perfil principal PRECISA ser SFW. Enviar uma foto NSFW é motivo para suspensão ou exclusão da conta.",
        ["profile.sfw_or_nsfw"] = "Esta foto é SFW ou NSFW?",
        ["profile.sfw_mismatch_warning"] = "Se nosso sistema detectar que você enviou conteúdo NSFW enquanto SFW está selecionado, sua foto ficará retida para moderação e você corre o risco de ter a conta suspensa.",
        ["profile.photo_ready"] = "Foto pronta, ainda não salva.",
        ["profile.replace"] = "Substituir",
        ["profile.photo_set"] = "Foto definida  ✓",
        ["profile.currently_nsfw"] = "Atualmente: NSFW",
        ["profile.currently_sfw"] = "Atualmente: SFW",
        ["profile.remove"] = "Remover",
        ["profile.photo_required"] = "Esta foto é obrigatória.",
        ["profile.photo_optional"] = "Esta foto é opcional.",
        ["profile.upload_photo"] = "Enviar Foto…",

        // MyProfileScreen.Images — file picker / crop popup
        ["profile.select_image"] = "Selecionar Imagem",
        ["profile.image_files_filter"] = "Arquivos de imagem",
        ["profile.crop_avatar"] = "Recortar Avatar",
        ["profile.crop_main_photo"] = "Recortar Foto Principal",
        ["profile.crop_extra_photo"] = "Recortar Foto Extra {0}",
    };
}
