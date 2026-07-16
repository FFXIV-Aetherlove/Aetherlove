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
        // added after update (1.3.1)
        ["huberror.reswipe_nothing_to_undo"] = "No hay nada que deshacer.",
        ["huberror.reswipe_already_matched"] = "No puedes deshacer en un perfil con el que ya tienes match.",
        ["huberror.reswipe_quota_exhausted"] = "Ya has usado tu deshacer de hoy.",
        ["huberror.superlike_quota_exhausted"] = "Te has quedado sin superlikes por hoy.",

        // added after update 1.4.3
        ["huberror.character_limit_reached"] = "Puedes tener como máximo {0} personajes de rol.",
        ["huberror.character_name_invalid"] = "Los nombres de personaje deben tener entre 3 y 50 caracteres.",
        ["huberror.character_not_found"] = "Ese personaje ya no existe. Recarga e inténtalo de nuevo.",

        // added after update 1.5.0
        ["huberror.patreon_disabled"] = "La vinculación con Patreon no está disponible ahora mismo.",
        ["huberror.patreon_already_linked"] = "Ya hay una cuenta de Patreon vinculada a tu perfil.",
        ["huberror.patreon_not_linked"] = "No hay ninguna cuenta de Patreon vinculada a tu perfil.",
        ["huberror.patreon_account_taken"] = "Esa cuenta de Patreon ya está vinculada a otra cuenta de AetherLove.",
        ["huberror.patreon_link_failed"] = "No pudimos completar la vinculación con Patreon. Inténtalo de nuevo.",
        ["huberror.places_disabled"] = "Lugares no está disponible ahora mismo.",
        ["huberror.venue_not_found"] = "Este local ya no existe.",
        ["huberror.venue_limit_reached"] = "Has alcanzado el límite de {0} locales.",
        ["huberror.venue_name_invalid"] = "El nombre del local debe tener entre 3 y 60 caracteres.",
        ["huberror.venue_description_too_long"] = "La descripción supera el límite de {0} caracteres.",
        ["huberror.venue_times_invalid"] = "Uno de los horarios de apertura no es válido.",
        ["huberror.venue_times_too_many"] = "Un local puede tener como máximo {0} horarios de apertura.",
        ["huberror.venue_review_own"] = "No puedes reseñar tu propio local.",
        ["huberror.venue_review_too_long"] = "Tu reseña supera el límite de {0} caracteres.",
        ["huberror.venue_review_rating_invalid"] = "Elige una valoración de 1 a 5 estrellas.",
        ["huberror.venue_rsvp_invalid"] = "Esa apertura ya no admite asistencias.",
    };
}
