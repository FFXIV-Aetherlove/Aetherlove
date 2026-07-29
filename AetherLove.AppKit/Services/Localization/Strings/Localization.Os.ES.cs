namespace AetherLove.Services.Localization;

internal static class OsEs
{
    public static readonly System.Collections.Generic.Dictionary<string, string> Strings = new()
    {
        // added after update 1.5.1
        // App names
        ["os.app_clock"] = "Reloj",
        ["os.app_camera"] = "Cámara",

        // Notification shade
        ["os.notifications"] = "Notificaciones",
        ["os.notifications_empty"] = "Sin notificaciones",
        ["os.notifications_clear"] = "Borrar todo",
        ["os.notif_remove"] = "Quitar",
        ["os.time_now"] = "ahora",
        ["os.qt_notifications"] = "Notificaciones",
        ["os.qt_sounds"] = "Sonidos de notificación",
        ["os.qt_lock"] = "Bloquear posición del teléfono",
        ["os.qt_theme"] = "Siguiente tema",
        ["os.qt_wallpaper"] = "Siguiente fondo",

        // Home screen
        ["os.edit_done"] = "Listo",
        ["os.greeting_morning"] = "Buenos días",
        ["os.greeting_afternoon"] = "Buenas tardes",
        ["os.greeting_evening"] = "Buenas noches",
        ["os.greeting_night"] = "¿Sigues en pie?",
        ["os.widget_eorzea"] = "Eorzea",
        ["os.widget_status"] = "Conexión",
        ["os.widget_connected"] = "Conectado",
        ["os.widget_offline"] = "Sin conexión",
        ["os.widget_unread"] = "{0} sin leer",

        // OS settings
        ["os.group_appearance"] = "Apariencia",
        ["os.group_wallpaper"] = "Pantalla de inicio y fondo",
        ["os.group_home"] = "Pantalla de inicio",
        ["os.group_apps"] = "Ajustes de cada app",
        ["os.group_about"] = "Acerca de",
        ["os.wallpaper_gradient"] = "Tema",
        ["os.wallpaper_custom"] = "Tuyo",
        ["os.wallpaper_upload"] = "Subir imagen…",
        ["os.wallpaper_dim"] = "Oscurecer fondo",
        ["os.home_edit_hint"] = "Mantén pulsado un icono en la pantalla de inicio para reordenarlo. Arrastra iconos al dock o entre páginas.",
        ["os.home_reset"] = "Restablecer diseño",
        ["os.about_demo"] = "Demo de la interfaz de AetherOS",

        // Love settings hub after the appearance move
        ["settings.menu_language"] = "Idioma y hora",

        // Weatherman app
        ["os.app_weather"] = "Tiempo",

        // Messenger app
        ["os.app_messenger"] = "Messenger",

        // Photos app
        ["os.app_photos"] = "Fotos",

        // Home screen app launcher
        ["os.add_apps"] = "Añadir apps",
        ["os.add_apps_title"] = "Añadir apps",
        ["os.add_apps_hint"] = "Fija otros plugins de Dalamud instalados en tu pantalla de inicio.",
        ["os.add_apps_none"] = "Ningún otro plugin ofrece una ventana principal.",
        ["os.add_apps_add"] = "Añadir",
        ["os.add_apps_added"] = "Añadida",
        ["os.add_apps_search"] = "Buscar apps...",
        ["os.remove_app_title"] = "Quitar app",
        ["os.remove_app_body"] = "¿Quieres quitar {0} de tu pantalla de inicio?",
        ["os.remove_app_confirm"] = "Quitar",
        // added after update 1.5.1
        ["os_onboarding.header_welcome"] = "Bienvenida",
        ["os_onboarding.header_design"] = "Hazlo tuyo",
        ["os_onboarding.header_name"] = "Tu nombre",
        ["os_onboarding.header_terms"] = "Términos del servicio",
        ["os_onboarding.welcome_title"] = "Bienvenida a AetherOS",
        ["os_onboarding.welcome_body"] = "Este es tu teléfono. Vamos a configurarlo: elige un estilo, un nombre y acepta las normas básicas. Solo lleva un momento.",
        ["os_onboarding.design_title"] = "Elige un diseño",
        ["os_onboarding.name_title"] = "¿Cómo te llamamos?",
        ["os_onboarding.name_body"] = "Este nombre se usa en todo AetherOS. Más adelante puedes elegir un nombre distinto para cada app.",
        ["os_onboarding.name_hint"] = "Nombre",
        ["os_onboarding.terms_title"] = "Las normas básicas",
        ["os_onboarding.terms_agree"] = "Acepto los Términos del servicio",
        ["os_onboarding.finish"] = "Finalizar",
        ["os_onboarding.header_signin"] = "Iniciar sesión",
        ["os_onboarding.header_passphrase"] = "Frase de contraseña",
        ["os_onboarding.tos_1"] = "AetherOS es un plugin hecho por fans, proporcionado tal cual, sin ninguna garantía. Puede fallar, perder datos o dejar de funcionar en cualquier momento, y se usa bajo tu propia responsabilidad.",
        ["os_onboarding.tos_2"] = "AetherOS no está afiliado a Square Enix ni cuenta con su respaldo o patrocinio.",
        ["os_onboarding.tos_3"] = "FINAL FANTASY y FINAL FANTASY XIV son marcas registradas de Square Enix Holdings Co., Ltd. Todas las demás marcas pertenecen a sus respectivos propietarios.",
        ["os_onboarding.tos_4"] = "Al continuar, aceptas estos términos.",
        // OS account avatar (added after update 1.5.1)
        ["os_onboarding.header_avatar"] = "Avatar",
        ["os_onboarding.avatar_title"] = "Añade una foto",
        ["os_onboarding.avatar_body"] = "Elige una imagen para tu perfil de AetherOS. Es opcional y solo tú la ves; puedes cambiarla cuando quieras.",
        ["os_onboarding.avatar_choose"] = "Elegir foto",
        ["os_onboarding.avatar_change"] = "Cambiar foto",
        // two-onboarding split (added after update 1.5.1)
        ["os_onboarding.header_done"] = "Listo",
        ["os_onboarding.done_title"] = "¡Todo listo!",
        ["os_onboarding.done_body"] = "Tu cuenta de AetherOS está lista. ¡Disfruta de tu nuevo teléfono!",
        ["os_onboarding.done_start"] = "Empezar a usar el plugin",
        ["os_onboarding.design_language"] = "Idioma",
        // combined OS profile step (added after update 1.5.1)
        ["os_onboarding.header_profile"] = "Tu perfil",
        ["os_onboarding.profile_name_label"] = "Tu nombre",
        ["os_onboarding.profile_photo_label"] = "Tu foto",
        // home button tooltip (added after update 1.5.1)
        ["os.home"] = "Inicio",
        // settings-consolidation categories + reset-home confirm + profile edit (added after update 1.5.1)
        ["os.cat_general"] = "General",
        ["os.cat_appearance"] = "Apariencia",
        ["os.cat_other"] = "Otros",
        ["os.menu_general"] = "General",
        ["os.menu_appearance"] = "Apariencia del teléfono",
        ["os.menu_reset_home"] = "Restablecer pantalla de inicio",
        ["os.reset_home_confirm_title"] = "¿Restablecer la pantalla de inicio?",
        ["os.reset_home_confirm_body"] = "Esto borra tu disposición de iconos y el dock personalizados y restaura la disposición predeterminada. Tus apps y ajustes no se ven afectados.",
        ["os.edit_profile"] = "Editar",
        // Daily Eorzean news app (added after update 1.5.1)
        ["os.app_news"] = "El Diario de Eorzea",
        ["os.news_notifications"] = "Notificaciones de noticias",
        ["os.news_tagline"] = "Est. · Todas las noticias del reino",
        ["os.news_latest"] = "Lo último",
        ["os.news_more"] = "Más noticias",
        ["os.news_edition"] = "Edición · {0}",
        ["os.news_refresh"] = "Actualizar",
        ["os.news_new"] = "Nuevo",
        // Feedback app (added after update 1.5.1)
        ["os.app_feedback"] = "Comentarios",
        ["os.feedback_app_label"] = "¿Sobre qué aplicación es?",
        ["os.feedback_general"] = "General",
        ["os.feedback_send_another"] = "Enviar más comentarios",
        // generic share system (added after update 1.5.1)
        ["os.share_sheet_title"] = "Compartir con",
        // home grid + folders
        ["os.settings_home_screen"] = "Pantalla de inicio",
        ["os.settings_home_grid_caption"] = "Los iconos se reorganizan automáticamente para ajustarse a la nueva cuadrícula.",
        ["os.new_folder"] = "Nueva carpeta",
        ["os.folder_name_hint"] = "Nombre de la carpeta…",
        ["os.folder_default_name"] = "Carpeta",
        ["os.folder_edit"] = "Editar",
        ["os.folder_empty"] = "Aún no hay apps. Arrastra un icono sobre esta carpeta para añadirlo.",
        // photo filters + screenshot import
        ["os.filter_original"] = "Original",
        ["os.filter_mono"] = "Mono",
        ["os.filter_noir"] = "Noir",
        ["os.filter_sepia"] = "Sepia",
        ["os.filter_retro"] = "Retro",
        ["os.filter_cool"] = "Frío",
        ["os.filter_vivid"] = "Vívido",
        ["os.filter_fade"] = "Desvaído",
        ["os.filter_applying"] = "Aplicando…",
        ["os.screenshots_album"] = "Capturas",
        // calendar app
        ["os.app_calendar"] = "Calendario",
        ["os.edit_filters"] = "Filtros",
        ["os.edit_adjust"] = "Ajustes",
        ["os.edit_brightness"] = "Brillo",
        ["os.edit_contrast"] = "Contraste",
        ["os.edit_hue"] = "Tono",
        ["os.edit_tint"] = "Intensidad",
        // guided OS tour
        ["os.settings_tour"] = "Hacer el recorrido",
        ["os.tour_next"] = "Siguiente",
        ["os.tour_back"] = "Atrás",
        ["os.tour_skip"] = "Omitir",
        ["os.tour_finish"] = "Listo",
        ["os.tour_welcome_title"] = "Bienvenido a AetherOS",
        ["os.tour_welcome_body"] = "Este es tu teléfono: una pantalla de inicio llena de apps, y AetherLove es solo una de ellas. Demos una vuelta rápida, solo toma un minuto.",
        ["os.tour_homebtn_title"] = "Tu botón de inicio",
        ["os.tour_homebtn_body"] = "Siempre puedes volver a la pantalla de inicio con el botón de inicio, el que brilla en la parte de abajo del teléfono.",
        ["os.tour_pages_title"] = "Tu pantalla de inicio",
        ["os.tour_pages_body"] = "Tus apps viven en la pantalla de inicio. Desliza a izquierda y derecha para moverte entre páginas; los puntos resaltados indican en qué página estás.",
        ["os.tour_widgets_title"] = "La página de widgets",
        ["os.tour_widgets_body"] = "Acabamos de deslizarnos a la página de widgets por ti. Está a la izquierda de tus apps y muestra el reloj con la hora de Eorzea, el estado de la conexión y tu última notificación.",
        ["os.tour_statusbar_title"] = "La barra de estado",
        ["os.tour_statusbar_body"] = "La franja resaltada de arriba del todo muestra la hora, la conexión con los servidores de AetherLove, cuántas notificaciones esperan y la batería. Si se pierde la conexión, ahí aparece un icono rojo de aviso.",
        ["os.tour_shade_title"] = "Notificaciones",
        ["os.tour_shade_body"] = "Toca la parte de arriba del teléfono (o arrástrala hacia abajo) para ver tus notificaciones, como acabamos de hacer. Ahí también tienes ajustes rápidos y un control de brillo. Toca una notificación para abrirla o deslízala para descartarla.",
        ["os.tour_fake_notif_title"] = "¡Hola!",
        ["os.tour_fake_notif_body"] = "Así es como se ve una notificación.",
        ["os.tour_badges_title"] = "Insignias",
        ["os.tour_badges_body"] = "Los iconos muestran un pequeño contador cuando algo te espera, como mensajes sin leer o nuevos matches. Se borra al abrir la app. (Tranquilo, no tienes 67 de verdad.)",
        ["os.tour_edit_title"] = "Hazlo tuyo",
        ["os.tour_edit_body"] = "Haz clic y mantén pulsado un icono y todo empezará a moverse, como ahora mismo. Arrastra los iconos a donde quieras: a otro sitio, a otra página o a la fila de abajo. Toca el botón Listo resaltado cuando termines.",
        ["os.tour_dock_title"] = "El dock",
        ["os.tour_dock_body"] = "La fila inferior se ve en todas las páginas. Admite hasta cuatro favoritos; arrastra iconos dentro y fuera mientras reorganizas.",
        ["os.tour_folders_title"] = "Carpetas",
        ["os.tour_folders_body"] = "Los iconos se pueden agrupar en carpetas, como la de ejemplo que acaba de aparecer. Crea las tuyas desde la casilla + y arrastra iconos sobre ellas para ordenar. Toca una carpeta para abrirla, renombrarla o sacar apps.",
        ["os.tour_addapps_title"] = "Más apps",
        ["os.tour_addapps_body"] = "La casilla + al final de tus apps abre esta lista. Aquí puedes crear carpetas e incluso añadir tus otros plugins de Dalamud a la pantalla de inicio. Se quitan con la X al reorganizar.",
        ["os.tour_share_title"] = "Compartir",
        ["os.tour_share_body"] = "Puedes compartir locales, quedadas y mucho más con tus matches y amigos. Compartir funciona entre las distintas apps: elige qué compartir y a dónde va, y llegará como una tarjeta interactiva.",
        ["os.tour_look_title"] = "Apariencia y diseño",
        ["os.tour_look_body"] = "En la app de Ajustes resaltada puedes elegir un tema para el teléfono, cambiar el fondo (o subir el tuyo), ajustar el tamaño de la cuadrícula y hasta el del propio teléfono.",
        ["os.tour_offline_title"] = "Sin conexión",
        ["os.tour_offline_body"] = "Puedes seguir usando el teléfono aunque AetherLove u otras apps integradas no estén disponibles. Las apps que necesitan los servidores se atenúan con una pequeña marca, pero el resto sigue funcionando con normalidad.",
        ["os.tour_done_title"] = "¡Fin del recorrido!",
        ["os.tour_done_body"] = "Puedes repetirlo cuando quieras desde Ajustes. Ahora explora las apps y pásalo bien.",
        ["os.tour_demo_folder"] = "Ejemplo",
        ["os.photos_search_hint"] = "Buscar por nombre o lugar…",
        ["os.folder_eject_title"] = "¿Quitar de la carpeta?",
        ["os.folder_eject_body"] = "{0} volverá a tu pantalla de inicio.",
        ["os.tt_prev"] = "Anterior",
        ["os.tt_next"] = "Siguiente",
        ["os.tt_edit"] = "Editar",
        ["os.tt_move"] = "Mover a un álbum",
        ["os.tt_share"] = "Compartir",
        ["os.tt_open_folder"] = "Abrir carpeta en el disco",
        ["os.share_wallpaper"] = "Fondo de pantalla",
        ["os.profile_edit_title"] = "Editar perfil",
        ["os.profile_save"] = "Guardar",
        // home right-click menu (added after update 2.0.0)
        ["os.home_menu_adjust"] = "Ajustar iconos",
        ["os.home_menu_wallpaper"] = "Cambiar fondo",
        // news app tile rename (added after update 2.0.0)
        ["os.app_news_tile"] = "Noticias",

        // battery easter egg (added after update 2.0.1)
        ["os.battery_grass_body"] = "Nos importa tu salud: no olvides salir, hidratarte y tocar un poco de hierba ;-)",
        ["os.battery_grass_hint"] = "Toca la hierba de abajo para continuar",
        ["os.settings_hide_battery_grass"] = "Ocultar el aviso de tocar hierba cuando se agote la batería",
        // levemetes (added after update 2.0.1)
        ["os.app_levemetes"] = "Levemetes",
        ["os.battery_grass_settings_hint"] = "(Puedes desactivar esta broma en General, dentro de la app de Ajustes)",

        // added after update 2.0.1
        ["os.menu_audio"] = "Ajustes de audio",
        ["os.app_market"] = "Mercado",
        ["os.app_realtor"] = "Inmobiliaria",
    };
}
