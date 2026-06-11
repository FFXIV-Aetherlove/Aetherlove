namespace AetherLove.Services.Localization;

internal static class CommonEs
{
    public static readonly System.Collections.Generic.Dictionary<string, string> Strings = new()
    {
        ["common.ok"] = "Aceptar",
        ["common.confirm"] = "Confirmar",
        ["common.cancel"] = "Cancelar",
        ["common.loading"] = "Cargando…",
        ["common.try_again"] = "Intentar de nuevo",
        ["common.i_understand"] = "Entiendo",
        ["common.sign_out"] = "Cerrar sesión",
        ["common.got_it"] = "¡Entendido!",
        ["common.moderator_notes_label"] = "Notas del moderador",
        ["common.server_unreachable_detail"] = "No se pudo conectar con el servidor: {0}",

        ["common.banned_title"] = "Cuenta baneada",
        ["common.banned_body"] = "Tu cuenta de AetherLove ha sido baneada. Ya no puedes usar el servicio.",
        ["common.banned_reason_label"] = "Motivo",
        ["common.banned_uninstall_hint"] = "Puedes cerrar esta ventana y desinstalar el plugin en cualquier momento.",

        // Outdated-plugin screen
        ["common.outdated_title"] = "Actualización necesaria",
        ["common.outdated_body"] = "Estás usando una versión desactualizada de AetherLove. El servidor ya no admite esta versión, por lo que el complemento no puede conectarse.",
        ["common.outdated_hint"] = "Actualiza el complemento en el instalador de complementos de Dalamud y vuelve a abrir AetherLove.",

        ["common.offline_title"] = "AetherLove está fuera de línea",
        ["common.offline_body"] = "No podemos conectar con los servidores de AetherLove en este momento. La app necesita una conexión activa para explorar, coincidir y chatear, así que está en pausa hasta que volvamos a estar en línea.",
        ["common.offline_reconnecting"] = "Reconectando…",
        ["common.offline_keep_trying"] = "Seguiremos intentándolo automáticamente.",

        ["common.passphrase_title"] = "Introduce tu frase de contraseña de cifrado",
        ["common.passphrase_intro"] = "Reconocemos esta cuenta, pero este dispositivo aún no tiene tu clave de chat. Introduce la frase de contraseña que definiste en tu primer dispositivo para desbloquear tu historial de chat.",
        ["common.passphrase_forgot"] = "¿Olvidaste tu frase de contraseña? No hay recuperación, pero puedes cerrar sesión abajo y crear una cuenta nueva. Perderás el historial de chat de esta cuenta.",
        ["common.passphrase_bundle_load_failed"] = "No se pudo cargar el paquete de cifrado desde el servidor.",
        ["common.passphrase_empty"] = "Por favor, introduce tu frase de contraseña.",
        ["common.passphrase_incorrect"] = "Frase de contraseña incorrecta. Inténtalo de nuevo.",
        ["common.passphrase_unlock_failed"] = "Error al desbloquear: {0}",
        ["common.unlock"] = "Desbloquear",
        ["common.unlocking"] = "Desbloqueando…",

        ["common.warnings_heading_one"] = "Tienes una advertencia de moderación",
        ["common.warnings_heading_many"] = "Tienes {0} advertencias de moderación",
        ["common.warnings_body"] = "Por favor, lee la(s) siguiente(s) advertencia(s) del equipo de moderación. Las infracciones reiteradas pueden conllevar la suspensión de la cuenta.",
        ["common.warnings_submit_error"] = "No se pudo conectar con el servidor: {0}. Toca para reintentar.",
        ["common.acknowledging"] = "Confirmando…",

        ["common.nsfw_decl_unselected"] = "selecciona una opción abajo",
        ["common.nsfw_decl_sfw"] = "esta foto es SFW",
        ["common.nsfw_decl_nsfw"] = "esta foto es NSFW",
        ["common.lalafell_nsfw_title"] = "NSFW no disponible",
        ["common.lalafell_nsfw_body"] = "No permitimos fotos NSFW de personajes Lalafell. Como los Lalafell tienen aspecto infantil, aplicamos esta política de manera uniforme a todas las cuentas Lalafell y no hacemos excepciones caso por caso.\n\nTu foto se ha vuelto a marcar como SFW. Si esta foto no es apta para el trabajo, elimínala y sube una diferente.",
        ["common.undeclared_photo_title"] = "Se requiere declaración",
        ["common.undeclared_photo_body"] = "Debes seleccionar si tu otra foto es SFW o NSFW en el cuadro de selección antes de subir otra.",

        ["common.changelog_window_title"] = "AetherLove — Novedades",
        ["common.whats_new"] = "Novedades",
        ["common.changelog_empty"] = "No hay entradas en el registro de cambios.",
        ["common.changelog_latest"] = "Lo último",
        ["common.changelog_important"] = "Importante",
        ["common.changelog_new_features"] = "Nuevas funciones",
        ["common.changelog_bug_fixes"] = "Correcciones de errores",

        ["common.rate_limit_title"] = "Más despacio",
        ["common.rate_limit_noun_profile"] = "perfil",
        ["common.rate_limit_noun_images"] = "imágenes",
        ["common.rate_limit_body"] = "Solo puedes cambiar tu {0} {1} veces por hora. Inténtalo de nuevo en {2}.",
        ["common.rate_limit_retry_moment"] = "un momento",
        ["common.rate_limit_retry_one_second"] = "1 segundo",
        ["common.rate_limit_retry_seconds"] = "{0} segundos",
        ["common.rate_limit_retry_one_minute"] = "1 minuto",
        ["common.rate_limit_retry_minutes"] = "{0} minutos",

        ["common.emoji_search_hint"] = "Buscar emoji...",
        // Bottom navigation bar
        ["common.nav_swipe"] = "Swipe",
        ["common.nav_matches"] = "Matches",
        ["common.nav_profile"] = "Perfil",
        ["common.nav_settings"] = "Ajustes",
        ["common.nav_minimize"] = "Ocultar",

        ["common.emoji_none_found"] = "No se encontró ningún emoji.",

        // Close-plugin confirmation modal
        ["common.close_plugin_tooltip"] = "Cerrar AetherLove",
        ["common.close_plugin_title"] = "¿Cerrar AetherLove?",
        ["common.close_plugin_body"] = "Esto solo oculta la ventana. Seguirás conectado y recibirás nuevas coincidencias y mensajes mientras el plugin esté habilitado.\n\nVuelve a abrir la ventana en cualquier momento escribiendo {0} en el chat.",
        ["common.close_plugin_tip"] = "Consejo: usa el botón Minimizar en la parte inferior para mantener visible la pequeña burbuja flotante con su indicador de notificaciones.",
        ["common.close"] = "Cerrar",

        // Save-error modal
        ["common.save_error_title"] = "Algo salió mal",
        ["common.save_error_intro"] = "No pudimos guardar tus cambios:",
        ["common.save_error_report"] = "Si esto sigue ocurriendo, informa del error en nuestro Discord.",
        ["common.save_error_unknown"] = "Se produjo un error inesperado.",

        // Image requirements modal
        ["common.img_requirements_title"] = "No se puede usar la imagen",
        ["common.img_invalid"] = "Ese archivo no es una imagen válida o su formato no es compatible.",
        ["common.img_too_small"] = "Esa imagen solo mide {0}×{1} px, es demasiado pequeña.",
        ["common.img_requirements_sizes"] = "Los avatares necesitan al menos {0}×{1} px y las fotos de perfil al menos {2}×{3} px. Elige una imagen más grande.",

        // Image crop window
        ["common.loading_image"] = "Cargando imagen...",
        ["common.use_this_crop"] = "Usar este recorte",
    };
}
