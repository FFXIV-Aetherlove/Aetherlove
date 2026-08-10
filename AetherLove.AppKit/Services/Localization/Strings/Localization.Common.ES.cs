namespace AetherLove.Services.Localization;

internal static class CommonEs
{
    public static readonly System.Collections.Generic.Dictionary<string, string> Strings = new()
    {
        ["common.ok"] = "Aceptar",
        ["common.cancel"] = "Cancelar",
        ["common.loading"] = "Cargando…",
        ["common.try_again"] = "Intentar de nuevo",
        ["common.i_understand"] = "Entiendo",
        ["common.sign_out"] = "Cerrar sesión",
        ["common.got_it"] = "¡Entendido!",
        ["common.server_unreachable_detail"] = "No se pudo conectar con el servidor: {0}",

        ["common.banned_title"] = "Perfil baneado",
        ["common.banned_body"] = "Un perfil de AetherLove baneado significa que ya no puedes usar AetherLove con este perfil. Todavía puedes usar nuestras otras apps. Para más información, abre un ticket de soporte en nuestro Discord.",
        ["common.banned_reason_label"] = "Motivo",
        ["common.banned_uninstall_hint"] = "Usa el botón de inicio de abajo para volver a la pantalla de inicio.",

        // Outdated-plugin screen
        ["common.outdated_title"] = "Actualización necesaria",
        ["common.outdated_body"] = "Estás usando una versión desactualizada de AetherLove. El servidor ya no admite esta versión, por lo que el complemento no puede conectarse.",
        ["common.outdated_hint"] = "Actualiza el complemento en el instalador de complementos de Dalamud y vuelve a abrir AetherLove.",

        ["common.offline_title"] = "Los servicios de AetherOS están fuera de línea",
        ["common.offline_body"] = "El servidor seguramente está fuera de línea por actualizaciones o mantenimiento. ¡Esto no debería tardar más de 2 minutos!",
        ["common.offline_reconnecting"] = "Reconectando…",
        ["common.offline_taking_long"] = "Esto está tardando más de lo normal. Únete a nuestro Discord para ver el estado más reciente.",
        ["common.offline_join_discord"] = "Únete a Discord",

        ["common.passphrase_title"] = "Introduce tu frase de contraseña de cifrado",
        ["common.passphrase_intro"] = "Reconocemos esta cuenta, pero este dispositivo aún no tiene tu clave de chat. Introduce la frase de contraseña que definiste en tu primer dispositivo para desbloquear tu historial de chat.",
        ["common.passphrase_forgot"] = "¿Olvidaste tu frase de contraseña? Puedes restablecer tus claves de cifrado abajo. Todo lo enviado antes del restablecimiento será ilegible para ti.",

        // Passphrase reset (added after update 1.5.1)
        ["common.passphrase_reset_button"] = "Restablecer claves de cifrado…",
        ["common.passphrase_reset_title"] = "Restablecer tus claves de cifrado",
        ["common.passphrase_reset_warning"] = "Esto crea una frase de contraseña y claves de cifrado completamente nuevas. Perderás PERMANENTEMENTE el acceso a todos los mensajes anteriores al restablecimiento, y tus matches y contactos de Messenger verán un aviso de que restableciste tus claves.",
        ["common.passphrase_reset_new"] = "Nueva frase de contraseña",
        ["common.passphrase_reset_repeat"] = "Repite la nueva frase de contraseña",
        ["common.passphrase_reset_mismatch"] = "Las frases de contraseña no coinciden.",
        ["common.passphrase_reset_go"] = "Restablecer mis claves",
        ["common.passphrase_reset_running"] = "Restableciendo…",
        ["common.passphrase_bundle_load_failed"] = "No se pudo cargar el paquete de cifrado desde el servidor.",
        ["common.passphrase_empty"] = "Por favor, introduce tu frase de contraseña.",
        ["common.passphrase_incorrect"] = "Frase de contraseña incorrecta. Inténtalo de nuevo.",
        ["common.passphrase_unlock_failed"] = "Error al desbloquear: {0}",
        ["common.unlock"] = "Desbloquear",
        ["common.unlocking"] = "Desbloqueando…",

        // Encryption recovery screen
        ["common.recovery_title"] = "Configurar mensajería segura",
        ["common.recovery_intro"] = "A tu cuenta le faltan las claves de cifrado, así que todavía no puedes enviar ni recibir mensajes. Elige una contraseña para configurarlas. Guárdala bien, no se puede recuperar.",
        ["common.recovery_button"] = "Activar mensajería segura",
        ["common.recovery_support"] = "¿Sigues con problemas? Cierra sesión abajo o escríbenos en Discord.",

        ["common.warnings_heading_one"] = "Tienes una advertencia de moderación",
        ["common.warnings_heading_many"] = "Tienes {0} advertencias de moderación",
        ["common.warnings_body"] = "Por favor, lee la(s) siguiente(s) advertencia(s) del equipo de moderación. Las infracciones reiteradas pueden conllevar la suspensión de la cuenta.",
        ["common.warnings_submit_error"] = "No se pudo conectar con el servidor: {0}. Toca para reintentar.",
        ["common.acknowledging"] = "Confirmando…",

        // Moderator message screen
        ["common.modmsg_heading_one"] = "Tienes un mensaje del equipo de moderación",
        ["common.modmsg_heading_many"] = "Tienes {0} mensajes del equipo de moderación",
        ["common.modmsg_body"] = "El equipo de moderación te ha enviado lo siguiente:",
        ["common.modmsg_got_it"] = "Entendido",

        ["common.nsfw_decl_unselected"] = "selecciona una opción abajo",
        ["common.nsfw_decl_sfw"] = "esta foto es SFW",
        ["common.nsfw_decl_nsfw"] = "esta foto es NSFW",
        ["common.lalafell_nsfw_title"] = "NSFW no disponible",
        ["common.lalafell_nsfw_body"] = "No permitimos fotos NSFW de personajes Lalafell. Como los Lalafell tienen aspecto infantil, aplicamos esta política de manera uniforme a todas las cuentas Lalafell y no hacemos excepciones caso por caso.\n\nTu foto se ha vuelto a marcar como SFW. Si esta foto no es apta para el trabajo, elimínala y sube una diferente.",
        ["common.undeclared_photo_title"] = "Se requiere declaración",
        ["common.undeclared_photo_body"] = "Debes seleccionar si tu otra foto es SFW o NSFW en el cuadro de selección antes de subir otra.",

        ["common.changelog_window_title"] = "AetherLove: Novedades",
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
        ["common.nav_settings"] = "Ajustes",

        ["common.emoji_none_found"] = "No se encontró ningún emoji.",

        // Close-plugin confirmation modal
        ["common.close_plugin_tooltip"] = "Cerrar AetherOS",
        ["common.minimize_tooltip"] = "Minimizar AetherOS",
        ["common.close_plugin_title"] = "¿Cerrar AetherLove?",
        ["common.close_plugin_body"] = "Esto solo oculta la ventana. Seguirás conectado y recibirás nuevas coincidencias y mensajes mientras el plugin esté habilitado.\n\nVuelve a abrir la ventana en cualquier momento escribiendo {0} en el chat.",
        ["common.close_plugin_tip"] = "Consejo: usa el botón Minimizar en la parte inferior para mantener visible la pequeña burbuja flotante con su indicador de notificaciones.",
        ["common.close_plugin_dont_ask"] = "No volver a mostrar esta ventana",
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

        // SFW-image gate modal (main avatar + first profile photo must be SFW)
        ["common.sfw_gate_title"] = "Perfil + Avatar - SOLO SFW",
        ["common.sfw_gate_subtitle"] = "Qué NO es SFW:",
        ["common.sfw_gate_b1"] = "Desnudez total de cualquier género.",
        ["common.sfw_gate_b2"] = "Pezones visibles de cualquier género.",
        ["common.sfw_gate_b3"] = "Vello púbico o zonas genitales visibles.",
        ["common.sfw_gate_b4"] = "Representaciones gráficas de sangre, lesiones, heridas o daño corporal.",
        ["common.sfw_gate_b5"] = "Tatuajes, marcas, símbolos o texto que sean obscenos, discriminatorios o que inciten al odio, o que ataquen a personas o grupos por motivos de raza, etnia, nacionalidad, religión, género, orientación sexual u otras características protegidas.",
        ["common.sfw_gate_b6"] = "Gestos, posturas o referencias visuales de carácter sexual que impliquen o simulen actos sexuales, incluidos el sexo oral, la masturbación u otra actividad sexual.",
        ["common.sfw_gate_secondary"] = "Aún puedes subir material NSFW en tus imágenes de perfil secundarias.",
        ["common.sfw_gate_ack"] = "Entiendo las reglas para SFW",

        // added after update (1.3.1)
        ["common.sfw_gate_race_gender"] = "Asegúrate de que tu imagen principal muestre la raza y el género de tu personaje tal como aparecen en tu perfil.",

        // added after update 1.4.3
        ["common.img_cloud_title"] = "Archivo no descargado",
        ["common.img_cloud_unavailable"] = "Esta imagen está guardada solo en línea en la nube (por ejemplo, OneDrive) y no se ha descargado a tu PC, así que no se puede abrir. En el Explorador de archivos, haz clic derecho sobre ella, elige 'Mantener siempre en este dispositivo', espera a que aparezca la marca verde e inténtalo de nuevo. O elige un archivo guardado localmente en tu PC.",
        ["common.emoji_favorites"] = "Favoritos",
        ["common.emoji_favorite_hint"] = "clic derecho para marcar o quitar de favoritos",
        ["common.emoji_add_favorite"] = "Añadir a favoritos",
        ["common.emoji_remove_favorite"] = "Quitar de favoritos",
        ["common.selfie"] = "Selfie",
        ["common.selfie_instructions"] = "Arrastra o ajusta el marco sobre tu personaje y luego haz la foto.",
        ["common.selfie_take"] = "Hacer foto",
        ["common.selfie_capturing"] = "Capturando...",
        ["common.offline_maintenance"] = "El servidor está en mantenimiento.",

        // added after update 1.5.0
        ["common.nav_places"] = "Lugares",

        // Multi-profile switch nav slot (added after update 1.5.1)
        ["common.nav_switch"] = "Cambiar",

        // Recovery gate, enter-existing-passphrase mode (added after update 1.5.1)
        ["common.recovery_enter_intro"] = "Este perfil aún no tiene sus claves de cifrado. Introduce tu frase de contraseña de cifrado para configurarlas.",

        // account moderation reconcile (added after update 2.0.0)
        ["common.moderation_warning_for"] = "Advertencia para {0}",
        ["common.moderation_message_for"] = "Mensaje para {0}",
        ["common.account_disabled_title"] = "Cuenta bloqueada",
        ["common.account_disabled_body"] = "Esta función no está disponible mientras tu cuenta esté bloqueada.",

        // added after update 2.0.1
        ["common.passphrase_correct_unrecoverable"] = "Tu frase de contraseña es correcta, pero no se pudo abrir ninguna de tus claves guardadas con ella. Contacta con soporte antes de plantearte un restablecimiento; con él tus mensajes antiguos quedarían ilegibles para siempre.",

        // added after update 2.1.3
        ["common.staff_notice_heading_one"] = "Tienes un aviso del equipo",
        ["common.staff_notice_heading_many"] = "Tienes {0} avisos del equipo",
        ["common.staff_notice_body"] = "El equipo de AetherOS te ha enviado lo siguiente sobre tu cuenta:",
        ["common.staff_notice_ack"] = "Entendido",

        // added after update 2.2.3
        ["common.travel_teleport_with"] = "Teletransporte ({0})",
        ["common.travel_tooltip"] = "Viajar aquí con {0}",
    };
}
