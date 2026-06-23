namespace AetherLove.Services.Localization;

internal static class HubErrorsEs
{
    public static readonly System.Collections.Generic.Dictionary<string, string> Strings = new()
    {
        ["huberror.generic"] = "Se produjo un error inesperado del servidor.",
        ["huberror.generic_detail"] = "Se ha producido un error: {0}",
        ["huberror.invalid_request"] = "El servidor rechazó la solicitud. Si esto sigue ocurriendo, actualiza el plugin.",
        ["huberror.unauthenticated"] = "Tu sesión ya no es válida. Inicia sesión de nuevo.",
        ["huberror.banned"] = "Tu cuenta ha sido suspendida.",
        ["huberror.rate_limited"] = "Lo estás haciendo demasiado seguido. Inténtalo de nuevo en un momento.",
        ["huberror.profile_not_found"] = "Perfil no encontrado.",
        ["huberror.profile_not_visible"] = "Este perfil no está disponible.",
        ["huberror.deck_expired"] = "Este perfil ya no está en tu baraja. Actualiza tu baraja e inténtalo de nuevo.",
        ["huberror.no_active_match"] = "Ya no tienes match con este jugador.",
        ["huberror.peer_keys_missing"] = "Este usuario aún no ha configurado el cifrado E2E y no puede chatear, inténtalo de nuevo más tarde.",
        ["huberror.key_bundle_exists"] = "Las claves de cifrado ya están configuradas para esta cuenta.",
        ["huberror.message_too_large"] = "Este mensaje es demasiado largo para enviarlo.",
        ["huberror.bio_too_long"] = "Tu biografía supera el límite de {0} caracteres.",
        ["huberror.lalafell_erp"] = "El rol adulto no está disponible para personajes Lalafell.",
        ["huberror.lalafell_nsfw"] = "Las funciones NSFW no están disponibles para personajes Lalafell.",
        ["huberror.lalafell_nsfw_photo"] = "Las fotos NSFW no están disponibles para personajes Lalafell.",
        ["huberror.nsfw_disable_blocked"] = "Elimina tus fotos NSFW y desactiva el rol 18+ antes de desactivar el NSFW.",
        ["huberror.img_too_large"] = "La imagen es demasiado grande ({0} MB). El máximo es {1} MB.",
        ["huberror.img_dimensions_too_large"] = "La imagen es demasiado grande ({0}×{1}). El lado más largo puede ser de {2}px.",
        ["huberror.img_crop_too_small"] = "El área de recorte es demasiado pequeña (mín. {0}px por lado).",
        ["huberror.img_decode_failed"] = "No se pudo leer la imagen. Formatos compatibles: PNG, JPEG, WebP, GIF.",
        ["huberror.img_payload_invalid"] = "No se pudo subir la foto. Vuelve a seleccionar la imagen.",
        ["huberror.report_self"] = "No puedes denunciarte a ti mismo.",
        ["huberror.report_reason_required"] = "Describe el problema, por favor.",
        ["huberror.report_reason_too_long"] = "El motivo es demasiado largo (máx. {0} caracteres).",
        ["huberror.report_target_gone"] = "Ese perfil ya no existe.",
        ["huberror.report_duplicate"] = "Ya has denunciado a este usuario recientemente. Nuestro equipo lo está revisando.",
        ["huberror.feedback_required"] = "Escribe un mensaje antes de enviar.",
    };
}
