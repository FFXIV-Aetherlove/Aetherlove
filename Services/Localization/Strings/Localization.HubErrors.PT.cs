namespace AetherLove.Services.Localization;

internal static class HubErrorsPt
{
    public static readonly System.Collections.Generic.Dictionary<string, string> Strings = new()
    {
        ["huberror.generic"] = "Ocorreu um erro inesperado do servidor.",
        ["huberror.generic_detail"] = "Ocorreu um erro: {0}",
        ["huberror.invalid_request"] = "O servidor rejeitou a solicitação. Se isso continuar acontecendo, atualize o plugin.",
        ["huberror.unauthenticated"] = "Sua sessão não é mais válida. Faça login novamente.",
        ["huberror.banned"] = "Sua conta foi banida.",
        ["huberror.rate_limited"] = "Você está fazendo isso com muita frequência. Tente novamente em instantes.",
        ["huberror.profile_not_found"] = "Perfil não encontrado.",
        ["huberror.profile_not_visible"] = "Este perfil não está disponível.",
        ["huberror.deck_expired"] = "Este perfil não está mais no seu baralho. Atualize o baralho e tente novamente.",
        ["huberror.no_active_match"] = "Você não tem mais match com este jogador.",
        ["huberror.peer_keys_missing"] = "Seu match ainda não terminou de configurar o chat criptografado. Tente novamente mais tarde.",
        ["huberror.key_bundle_exists"] = "As chaves de criptografia já estão configuradas para esta conta.",
        ["huberror.message_too_large"] = "Esta mensagem é longa demais para ser enviada.",
        ["huberror.bio_too_long"] = "Sua bio excede o limite de {0} caracteres.",
        ["huberror.lalafell_erp"] = "Roleplay adulto não está disponível para personagens Lalafell.",
        ["huberror.lalafell_nsfw"] = "Recursos NSFW não estão disponíveis para personagens Lalafell.",
        ["huberror.lalafell_nsfw_photo"] = "Fotos NSFW não estão disponíveis para personagens Lalafell.",
        ["huberror.nsfw_disable_blocked"] = "Remova suas fotos NSFW e desative o roleplay 18+ antes de desativar o NSFW.",
        ["huberror.img_too_large"] = "A imagem é grande demais ({0} MB). O máximo é {1} MB.",
        ["huberror.img_dimensions_too_large"] = "A imagem é grande demais ({0}×{1}). O lado mais longo pode ter {2}px.",
        ["huberror.img_crop_too_small"] = "A área de corte é pequena demais (mín. {0}px por lado).",
        ["huberror.img_decode_failed"] = "Não foi possível ler a imagem. Formatos compatíveis: PNG, JPEG, WebP, GIF.",
        ["huberror.img_payload_invalid"] = "Não foi possível enviar a foto. Selecione a imagem novamente.",
        ["huberror.report_self"] = "Você não pode denunciar a si mesmo.",
        ["huberror.report_reason_required"] = "Descreva o problema, por favor.",
        ["huberror.report_reason_too_long"] = "O motivo é longo demais (máx. {0} caracteres).",
        ["huberror.report_target_gone"] = "Esse perfil não existe mais.",
        ["huberror.report_duplicate"] = "Você já denunciou este usuário recentemente. Nossa equipe está analisando.",
        ["huberror.feedback_required"] = "Digite uma mensagem antes de enviar.",
    };
}
