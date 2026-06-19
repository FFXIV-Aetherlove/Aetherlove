namespace AetherLove.Services.Localization;

internal static class HubErrorsPt
{
    public static readonly System.Collections.Generic.Dictionary<string, string> Strings = new()
    {
        ["huberror.generic"] = "Ocorreu um erro inesperado do servidor.",
        ["huberror.generic_detail"] = "Ocorreu um erro: {0}",
        ["huberror.invalid_request"] = "O servidor rejeitou a solicitação. Se isso continuar acontecendo, atualize o plugin.",
        ["huberror.unauthenticated"] = "Tua sessão não é mais válida. Faz login novamente.",
        ["huberror.banned"] = "A Tua conta foi banida.",
        ["huberror.rate_limited"] = "Estás a faz isso com muita frequência. Tenta novamente mais tarde.",
        ["huberror.profile_not_found"] = "Perfil não encontrado.",
        ["huberror.profile_not_visible"] = "Este perfil não está disponível.",
        ["huberror.deck_expired"] = "Este perfil não está mais no teu baralho. Atualiza o baralho e tenta novamente.",
        ["huberror.no_active_match"] = "Não tem mais match com este jogador.",
        ["huberror.peer_keys_missing"] = "O teu match ainda não terminou de configurar o chat criptografado. Tente novamente mais tarde.",
        ["huberror.key_bundle_exists"] = "As chaves de criptografia já estão configuradas para esta conta.",
        ["huberror.message_too_large"] = "Esta mensagem é longa demais para ser enviada.",
        ["huberror.bio_too_long"] = "Bio excede o limite de {0} caracteres.",
        ["huberror.lalafell_erp"] = "Roleplay adulto não está disponível para personagens Lalafell.",
        ["huberror.lalafell_nsfw"] = "Recursos NSFW não estão disponíveis para personagens Lalafell.",
        ["huberror.lalafell_nsfw_photo"] = "Fotos NSFW não estão disponíveis para personagens Lalafell.",
        ["huberror.nsfw_disable_blocked"] = "Remove as fotos NSFW e desativa o roleplay 18+ antes de desativar o NSFW.",
        ["huberror.img_too_large"] = "A imagem é grande demais ({0} MB). O máximo é {1} MB.",
        ["huberror.img_dimensions_too_large"] = "A imagem é grande demais ({0}×{1}). O lado mais longo pode ter {2}px.",
        ["huberror.img_crop_too_small"] = "A área de corte é pequena demais (mín. {0}px por lado).",
        ["huberror.img_decode_failed"] = "Não foi possível ler a imagem. Formatos compatíveis: PNG, JPEG, WebP, GIF.",
        ["huberror.img_payload_invalid"] = "Não foi possível enviar a foto. Seleciona a imagem novamente.",
        ["huberror.report_self"] = "Não podes denunciar a ti mesmo.",
        ["huberror.report_reason_required"] = "Descreva o problema, por favor.",
        ["huberror.report_reason_too_long"] = "O motivo é longo demais (máx. {0} caracteres).",
        ["huberror.report_target_gone"] = "Este perfil não existe mais.",
        ["huberror.report_duplicate"] = "Já denuncias-te este usuário recentemente. Nossa equipa está analisando.",
        ["huberror.feedback_required"] = "Digita uma mensagem antes de enviar.",
    };
}
