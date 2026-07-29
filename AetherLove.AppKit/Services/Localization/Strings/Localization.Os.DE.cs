namespace AetherLove.Services.Localization;

internal static class OsDe
{
    public static readonly System.Collections.Generic.Dictionary<string, string> Strings = new()
    {
        // added after update 1.5.1
        // App names
        ["os.app_clock"] = "Uhr",
        ["os.app_camera"] = "Kamera",

        // Notification shade
        ["os.notifications"] = "Benachrichtigungen",
        ["os.notifications_empty"] = "Keine Benachrichtigungen",
        ["os.notifications_clear"] = "Alle löschen",
        ["os.notif_remove"] = "Entfernen",
        ["os.time_now"] = "jetzt",
        ["os.qt_notifications"] = "Benachrichtigungen",
        ["os.qt_sounds"] = "Benachrichtigungstöne",
        ["os.qt_lock"] = "Handy-Position sperren",
        ["os.qt_theme"] = "Nächstes Design",
        ["os.qt_wallpaper"] = "Nächster Hintergrund",

        // Home screen
        ["os.edit_done"] = "Fertig",
        ["os.greeting_morning"] = "Guten Morgen",
        ["os.greeting_afternoon"] = "Guten Tag",
        ["os.greeting_evening"] = "Guten Abend",
        ["os.greeting_night"] = "Noch wach?",
        ["os.widget_eorzea"] = "Eorzea",
        ["os.widget_status"] = "Verbindung",
        ["os.widget_connected"] = "Verbunden",
        ["os.widget_offline"] = "Offline",
        ["os.widget_unread"] = "{0} ungelesen",

        // OS settings
        ["os.group_appearance"] = "Darstellung",
        ["os.group_wallpaper"] = "Homescreen & Hintergrund",
        ["os.group_home"] = "Startbildschirm",
        ["os.group_apps"] = "App-spezifische Einstellungen",
        ["os.group_about"] = "Über",
        ["os.wallpaper_gradient"] = "Design",
        ["os.wallpaper_custom"] = "Deins",
        ["os.wallpaper_upload"] = "Bild hochladen…",
        ["os.wallpaper_dim"] = "Hintergrund abdunkeln",
        ["os.home_edit_hint"] = "Halte ein Symbol auf dem Startbildschirm gedrückt, um es zu verschieben. Ziehe Symbole ins Dock oder zwischen Seiten.",
        ["os.home_reset"] = "Layout zurücksetzen",
        ["os.about_demo"] = "AetherOS-UI-Demo",

        // Love settings hub after the appearance move
        ["settings.menu_language"] = "Sprache & Zeit",

        // Weatherman app
        ["os.app_weather"] = "Wetter",

        // Messenger app
        ["os.app_messenger"] = "Messenger",

        // Photos app
        ["os.app_photos"] = "Fotos",

        // Home screen app launcher
        ["os.add_apps"] = "Apps hinzufügen",
        ["os.add_apps_title"] = "Apps hinzufügen",
        ["os.add_apps_hint"] = "Hefte andere installierte Dalamud-Plugins an deinen Startbildschirm.",
        ["os.add_apps_none"] = "Keine anderen Plugins bieten ein Hauptfenster an.",
        ["os.add_apps_add"] = "Hinzufügen",
        ["os.add_apps_added"] = "Hinzugefügt",
        ["os.add_apps_search"] = "Apps suchen...",
        ["os.remove_app_title"] = "App entfernen",
        ["os.remove_app_body"] = "Willst du {0} vom Startbildschirm entfernen?",
        ["os.remove_app_confirm"] = "Entfernen",
        // added after update 1.5.1
        ["os_onboarding.header_welcome"] = "Willkommen",
        ["os_onboarding.header_design"] = "Mach es zu deinem",
        ["os_onboarding.header_name"] = "Dein Name",
        ["os_onboarding.header_terms"] = "Nutzungsbedingungen",
        ["os_onboarding.welcome_title"] = "Willkommen bei AetherOS",
        ["os_onboarding.welcome_body"] = "Das ist dein Handy. Richten wir es ein: Wähle einen Look, einen Namen und stimme den Grundregeln zu. Dauert nur einen Moment.",
        ["os_onboarding.design_title"] = "Wähle ein Design",
        ["os_onboarding.name_title"] = "Wie sollen wir dich nennen?",
        ["os_onboarding.name_body"] = "Dieser Name wird in ganz AetherOS verwendet. Für jede App kannst du später einen eigenen Anzeigenamen wählen.",
        ["os_onboarding.name_hint"] = "Name",
        ["os_onboarding.terms_title"] = "Die Grundregeln",
        ["os_onboarding.terms_agree"] = "Ich stimme den Nutzungsbedingungen zu",
        ["os_onboarding.finish"] = "Fertig",
        ["os_onboarding.header_signin"] = "Anmelden",
        ["os_onboarding.header_passphrase"] = "Passphrase",
        ["os_onboarding.tos_1"] = "AetherOS ist ein von Fans erstelltes Plugin, ohne jegliche Gewährleistung bereitgestellt. Es kann jederzeit ausfallen, Daten verlieren oder aufhören zu funktionieren, und die Nutzung erfolgt auf eigene Gefahr.",
        ["os_onboarding.tos_2"] = "AetherOS steht in keiner Verbindung zu Square Enix und wird von Square Enix weder unterstützt noch gefördert.",
        ["os_onboarding.tos_3"] = "FINAL FANTASY und FINAL FANTASY XIV sind eingetragene Marken der Square Enix Holdings Co., Ltd. Alle anderen Marken sind Eigentum ihrer jeweiligen Inhaber.",
        ["os_onboarding.tos_4"] = "Wenn du fortfährst, akzeptierst du diese Bedingungen.",
        // OS account avatar (added after update 1.5.1)
        ["os_onboarding.header_avatar"] = "Avatar",
        ["os_onboarding.avatar_title"] = "Foto hinzufügen",
        ["os_onboarding.avatar_body"] = "Wähle ein Bild für dein AetherOS-Profil. Es ist optional und nur du siehst es. Du kannst es jederzeit ändern.",
        ["os_onboarding.avatar_choose"] = "Foto wählen",
        ["os_onboarding.avatar_change"] = "Foto ändern",
        // two-onboarding split (added after update 1.5.1)
        ["os_onboarding.header_done"] = "Fertig",
        ["os_onboarding.done_title"] = "Alles bereit!",
        ["os_onboarding.done_body"] = "Dein AetherOS-Konto ist eingerichtet. Viel Spaß mit deinem neuen Handy!",
        ["os_onboarding.done_start"] = "Plugin starten",
        ["os_onboarding.design_language"] = "Sprache",
        // combined OS profile step (added after update 1.5.1)
        ["os_onboarding.header_profile"] = "Dein Profil",
        ["os_onboarding.profile_name_label"] = "Dein Name",
        ["os_onboarding.profile_photo_label"] = "Dein Foto",
        // home button tooltip (added after update 1.5.1)
        ["os.home"] = "Startbildschirm",
        // settings-consolidation categories + reset-home confirm + profile edit (added after update 1.5.1)
        ["os.cat_general"] = "Allgemein",
        ["os.cat_appearance"] = "Darstellung",
        ["os.cat_other"] = "Sonstiges",
        ["os.menu_general"] = "Allgemein",
        ["os.menu_appearance"] = "Handy-Darstellung",
        ["os.menu_reset_home"] = "Startbildschirm zurücksetzen",
        ["os.reset_home_confirm_title"] = "Startbildschirm zurücksetzen?",
        ["os.reset_home_confirm_body"] = "Dadurch werden deine eigene Symbolanordnung und das Dock gelöscht und die Standardanordnung wiederhergestellt. Deine Apps und Einstellungen bleiben unberührt.",
        ["os.edit_profile"] = "Bearbeiten",
        // Daily Eorzean news app (added after update 1.5.1)
        ["os.app_news"] = "Eorzäer Tagblatt",
        ["os.news_notifications"] = "Nachrichten-Benachrichtigungen",
        ["os.news_tagline"] = "Gegr. · Alle Neuigkeiten des Reichs",
        ["os.news_latest"] = "Aktuell",
        ["os.news_more"] = "Weitere Meldungen",
        ["os.news_edition"] = "Ausgabe · {0}",
        ["os.news_refresh"] = "Aktualisieren",
        ["os.news_new"] = "Neu",
        // Feedback app (added after update 1.5.1)
        ["os.app_feedback"] = "Feedback",
        ["os.feedback_app_label"] = "Um welche App geht es?",
        ["os.feedback_general"] = "Allgemein",
        ["os.feedback_send_another"] = "Weiteres Feedback senden",
        // generic share system (added after update 1.5.1)
        ["os.share_sheet_title"] = "Teilen mit",
        // home grid + folders
        ["os.settings_home_screen"] = "Startbildschirm",
        ["os.settings_home_grid_caption"] = "Die Symbole ordnen sich automatisch an das neue Raster an.",
        ["os.new_folder"] = "Neuer Ordner",
        ["os.folder_name_hint"] = "Ordnername…",
        ["os.folder_default_name"] = "Ordner",
        ["os.folder_edit"] = "Bearbeiten",
        ["os.folder_empty"] = "Noch keine Apps. Zieh ein Symbol auf diesen Ordner, um es hinzuzufügen.",
        // photo filters + screenshot import
        ["os.filter_original"] = "Original",
        ["os.filter_mono"] = "Mono",
        ["os.filter_noir"] = "Noir",
        ["os.filter_sepia"] = "Sepia",
        ["os.filter_retro"] = "Retro",
        ["os.filter_cool"] = "Kühl",
        ["os.filter_vivid"] = "Kräftig",
        ["os.filter_fade"] = "Verblasst",
        ["os.filter_applying"] = "Wird angewendet…",
        ["os.screenshots_album"] = "Screenshots",
        // calendar app
        ["os.app_calendar"] = "Kalender",
        ["os.edit_filters"] = "Filter",
        ["os.edit_adjust"] = "Anpassen",
        ["os.edit_brightness"] = "Helligkeit",
        ["os.edit_contrast"] = "Kontrast",
        ["os.edit_hue"] = "Farbton",
        ["os.edit_tint"] = "Tönung",
        // guided OS tour
        ["os.settings_tour"] = "Tour ansehen",
        ["os.tour_next"] = "Weiter",
        ["os.tour_back"] = "Zurück",
        ["os.tour_skip"] = "Überspringen",
        ["os.tour_finish"] = "Fertig",
        ["os.tour_welcome_title"] = "Willkommen bei AetherOS",
        ["os.tour_welcome_body"] = "Das ist dein Telefon: ein Homescreen voller Apps, und AetherLove ist nur eine davon. Schauen wir uns kurz um, das dauert nur eine Minute.",
        ["os.tour_homebtn_title"] = "Dein Home-Button",
        ["os.tour_homebtn_body"] = "Mit dem Home-Button kommst du jederzeit zurück zum Homescreen. Er leuchtet gerade unten am Telefon auf.",
        ["os.tour_pages_title"] = "Dein Homescreen",
        ["os.tour_pages_body"] = "Deine Apps liegen auf dem Homescreen. Wische nach links und rechts, um zwischen den Seiten zu wechseln; die hervorgehobenen Punkte zeigen, auf welcher Seite du bist.",
        ["os.tour_widgets_title"] = "Die Widget-Seite",
        ["os.tour_widgets_body"] = "Wir sind gerade für dich zur Widget-Seite gewischt. Sie liegt links neben deinen Apps und zeigt die Uhr mit Eorzea-Zeit, deinen Verbindungsstatus und die neueste Benachrichtigung.",
        ["os.tour_statusbar_title"] = "Die Statusleiste",
        ["os.tour_statusbar_body"] = "Der hervorgehobene Streifen ganz oben zeigt die Uhrzeit, die Verbindung zu den AetherLove-Servern, wartende Benachrichtigungen und den Akku. Bei Verbindungsabbruch erscheint dort ein rotes Warnsymbol.",
        ["os.tour_shade_title"] = "Benachrichtigungen",
        ["os.tour_shade_body"] = "Tippe oben auf das Telefon (oder zieh es herunter), um deine Benachrichtigungen zu sehen, genau wie wir es gerade gemacht haben. Hier findest du auch Schnelleinstellungen und einen Helligkeitsregler. Antippen öffnet eine Benachrichtigung, Wegwischen verwirft sie.",
        ["os.tour_fake_notif_title"] = "Hallo!",
        ["os.tour_fake_notif_body"] = "So sieht eine Benachrichtigung aus.",
        ["os.tour_badges_title"] = "Badges",
        ["os.tour_badges_body"] = "App-Symbole zeigen einen kleinen Zähler, wenn etwas auf dich wartet, etwa ungelesene Nachrichten oder neue Matches. Beim Öffnen der App verschwindet er. (Keine Sorge, du hast nicht wirklich 67 davon.)",
        ["os.tour_edit_title"] = "Mach es zu deinem",
        ["os.tour_edit_body"] = "Klicke auf ein Symbol und halte es gedrückt, dann wackelt alles, so wie jetzt. Zieh Symbole, wohin du willst: an eine andere Stelle, auf eine andere Seite oder in die untere Reihe. Tippe auf den hervorgehobenen Fertig-Button, wenn alles passt.",
        ["os.tour_dock_title"] = "Das Dock",
        ["os.tour_dock_body"] = "Die untere Reihe bleibt auf jeder Seite sichtbar. Sie fasst bis zu vier Favoriten; zieh Symbole beim Anordnen hinein und heraus.",
        ["os.tour_folders_title"] = "Ordner",
        ["os.tour_folders_body"] = "Symbole lassen sich in Ordnern gruppieren, wie in dem Beispiel, das gerade erschienen ist. Erstelle eigene über die Plus-Kachel und zieh dann Symbole darauf, um aufzuräumen. Antippen öffnet den Ordner; dort kannst du ihn umbenennen oder Apps wieder herausnehmen.",
        ["os.tour_addapps_title"] = "Mehr Apps",
        ["os.tour_addapps_body"] = "Die Plus-Kachel am Ende deiner Apps öffnet diese Liste. Hier kannst du Ordner erstellen und sogar deine anderen Dalamud-Plugins auf den Homescreen legen. Entfernen geht über das X beim Anordnen.",
        ["os.tour_share_title"] = "Teilen",
        ["os.tour_share_body"] = "Du kannst Venues, Treffen und mehr mit deinen Matches und Freunden teilen. Das Teilen funktioniert zwischen den verschiedenen Apps: Wähle aus, was du teilen willst und wohin, und es kommt als antippbare Karte an.",
        ["os.tour_look_title"] = "Aussehen und Layout",
        ["os.tour_look_body"] = "In der hervorgehobenen Einstellungen-App wählst du ein Telefon-Design, änderst das Hintergrundbild (oder lädst ein eigenes hoch), stellst das Raster um und passt die Telefongröße an.",
        ["os.tour_offline_title"] = "Offline",
        ["os.tour_offline_body"] = "Du kannst das Telefon weiter benutzen, selbst wenn AetherLove oder andere eingebaute Apps gerade nicht erreichbar sind. Apps, die die Server brauchen, werden leicht abgedunkelt und markiert; alles andere funktioniert ganz normal weiter.",
        ["os.tour_done_title"] = "Das war die Tour!",
        ["os.tour_done_body"] = "Du kannst sie jederzeit in den Einstellungen erneut starten. Und jetzt viel Spaß beim Entdecken der Apps!",
        ["os.tour_demo_folder"] = "Beispiel",
        ["os.photos_search_hint"] = "Nach Name oder Ort suchen…",
        ["os.folder_eject_title"] = "Aus dem Ordner entfernen?",
        ["os.folder_eject_body"] = "{0} wandert zurück auf deinen Homescreen.",
        ["os.tt_prev"] = "Zurück",
        ["os.tt_next"] = "Weiter",
        ["os.tt_edit"] = "Bearbeiten",
        ["os.tt_move"] = "In Album verschieben",
        ["os.tt_share"] = "Teilen",
        ["os.tt_open_folder"] = "Ordner auf der Festplatte öffnen",
        ["os.share_wallpaper"] = "Hintergrundbild",
        ["os.profile_edit_title"] = "Profil bearbeiten",
        ["os.profile_save"] = "Speichern",
        // home right-click menu (added after update 2.0.0)
        ["os.home_menu_adjust"] = "Symbole anordnen",
        ["os.home_menu_wallpaper"] = "Hintergrund ändern",
        // news app tile rename (added after update 2.0.0)
        ["os.app_news_tile"] = "Nachrichten",

        // battery easter egg (added after update 2.0.1)
        ["os.battery_grass_body"] = "Uns liegt deine Gesundheit am Herzen: Vergiss nicht rauszugehen, zu trinken und mal echtes Gras anzufassen ;-)",
        ["os.battery_grass_hint"] = "Bitte fasse unten das Gras an, um fortzufahren",
        ["os.settings_hide_battery_grass"] = "Gras-Hinweis bei leerem Akku ausblenden",
        // levemetes (added after update 2.0.1)
        ["os.app_levemetes"] = "Levemetes",
        ["os.battery_grass_settings_hint"] = "(Du kannst diesen Scherz in der Einstellungen-App unter Allgemein deaktivieren)",

        // added after update 2.0.1
        ["os.menu_audio"] = "Audio-Einstellungen",
        ["os.app_market"] = "Markt",
        ["os.app_realtor"] = "Makler",
    };
}
