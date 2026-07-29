namespace AetherLove.Services.Localization;

internal static class SettingsDe
{
    public static readonly System.Collections.Generic.Dictionary<string, string> Strings = new()
    {
        // Settings

        // Section labels + hub menu
        ["settings.section_plugin_settings"] = "Plugin-Einstellungen",
        ["settings.section_phone_size"] = "Telefongröße",
        ["settings.section_plugin_language"] = "Plugin-Sprache",
        ["settings.section_general"] = "Allgemeine Einstellungen",
        ["settings.section_notifications"] = "Benachrichtigungen",
        ["settings.section_other"] = "Sonstiges",
        ["settings.section_danger_zone"] = "Gefahrenzone",
        ["settings.menu_language_theme"] = "Sprache & Design",
        ["settings.menu_appearance"] = "Telefon-Darstellung",
        ["settings.menu_chat_colors"] = "Chat-Darstellung",
        ["settings.section_theme"] = "Design",
        ["settings.back_arrow"] = "← Zurück",
        ["settings.chat_own_bg"] = "Eigener Chat-Hintergrund",
        ["settings.chat_own_fg"] = "Eigener Chat-Text",
        ["settings.chat_peer_bg"] = "Chat-Hintergrund des Partners",
        ["settings.chat_peer_fg"] = "Chat-Text des Partners",
        ["settings.chat_reset"] = "Zurücksetzen",

        // Phone size picker (AppearancePicker)
        ["settings.phone_size_small"] = "Klein",
        ["settings.phone_size_medium"] = "Mittel",
        ["settings.phone_size_large"] = "Groß",
        ["settings.phone_size_xl"] = "XL",
        ["settings.phone_size_xxl"] = "XXL",
        ["settings.phone_size_caption"] = "Skaliert das gesamte Telefon. Größere Größen eignen sich für höher auflösende Bildschirme; XL und XXL sind für 4K ausgelegt und passen möglicherweise nicht auf kleinere Displays.",
        ["settings.section_mini_phone_size"] = "Größe des Mini-Telefons",
        ["settings.mini_phone_size_caption"] = "Größe der minimierten Blase (wird angezeigt, wenn das Telefon minimiert ist). Die Vorschau unten zeigt die gewählte Größe.",

        // General
        ["settings.confirm_before_close"] = "Vor dem Schließen des Telefons bestätigen",

        // Buttons
        ["settings.view_changelog"] = "Änderungsprotokoll ansehen",
        ["settings.send_feedback"] = "Feedback senden",
        ["settings.terms_of_service"] = "Nutzungsbedingungen",
        ["settings.cancel"] = "Abbrechen",

        // Privacy
        ["settings.always_blur_nsfw"] = "NSFW immer unkenntlich machen",
        ["settings.always_blur_nsfw_tooltip"] = "Wenn aktiviert, werden NSFW-markierte Zusatzfotos in anderen Profilen unkenntlich gemacht, bis du klickst, um jedes einzelne aufzudecken. Avatare und Hauptporträts sind unabhängig davon immer safe-for-work. Wenn du dies deaktivierst, wird jedes Foto unverändert angezeigt.",
        ["settings.nsfw_profile"] = "Mein Profil ist NSFW (18+)",
        ["settings.nsfw_profile_tooltip"] = "Markiert dein Profil als nicht jugendfrei/NSFW, sodass es nur Personen sehen, die NSFW aktiviert haben. Es wird automatisch aktiviert, wenn du NSFW-Fotos hinzufügst oder 18+-Rollenspiel auswählst, und bleibt aktiv, bis du diese entfernst.",
        ["settings.nsfw_profile_locked"] = "Du kannst dies nicht deaktivieren, solange du NSFW-Fotos oder 18+-Rollenspiel (ERP) ausgewählt hast. Entferne zuerst deine NSFW-Bilder und wähle 18+-Rollenspiel ab.",

        // Notifications
        ["settings.enable_notifications"] = "Benachrichtigungen aktivieren",
        ["settings.enable_notifications_tooltip"] = "Hauptschalter für alle Benachrichtigungen. Deaktiviere dies, um jede In-Game-Chat-Ankündigung, jedes Popup und jeden Sound unten stummzuschalten.",
        ["settings.enable_notification_sounds"] = "Benachrichtigungstöne aktivieren",
        ["settings.enable_notification_sounds_tooltip"] = "Benachrichtigungstöne werden nur abgespielt, wenn dein Spiel-Audio und das Audio für Spezialeffekte nicht stummgeschaltet sind. Die Lautstärke wird über deine Windows-Lautstärke geregelt.",
        ["settings.announce_messages_chat"] = "Neue Nachrichten im Spiel-Chat ankündigen",
        ["settings.announce_matches_chat"] = "Neue Matches im Spiel-Chat ankündigen",
        ["settings.popup_messages"] = "Popup für neue Nachrichten anzeigen",
        ["settings.popup_matches"] = "Popup für neue Matches anzeigen",
        ["settings.hide_notifications_in_combat"] = "Benachrichtigungen im Kampf ausblenden",
        ["settings.hide_notifications_in_combat_tooltip"] = "Wenn aktiviert, erhältst du im Kampf keine Benachrichtigungen, weder Chat-Ankündigungen noch Popups oder Sounds.",
        ["settings.auto_open_minimized"] = "Beim Anmelden automatisch minimiert öffnen",
        ["settings.pulse_optout"] = "Erhalte fantastische Nachrichten vom Aethernet-Team, die dich ans Swipen erinnern",
        ["settings.pulse_optout_tooltip"] = "Ab und zu lässt AetherLove eine spielerische Zeile in deinem Spielchat fallen. Deaktiviere dies, um sie zu stoppen.",
        ["settings.combat_behavior"] = "Im Kampf",
        ["settings.combat_behavior_hide"] = "Telefon ausblenden",
        ["settings.combat_behavior_minimize"] = "Als Blase minimieren",
        ["settings.combat_behavior_leave_open"] = "Geöffnet lassen",
        ["settings.notification_sound"] = "Benachrichtigungston",
        ["settings.play"] = "Abspielen",

        // Delete account confirmation
        ["settings.delete_bullet_matches"] = "Alle deine Matches werden entfernt.",
        ["settings.delete_bullet_preferences"] = "Deine Match-Einstellungen werden gelöscht.",
        ["settings.delete_bullet_pictures"] = "Deine Profilbilder werden entfernt.",
        ["settings.delete_previous_failed"] = "Vorheriger Versuch fehlgeschlagen: {0}",

        // Deleting / deleted views
        ["settings.deleting_title"] = "Konto wird gelöscht",
        ["settings.deleting_body"] = "Deine Daten werden entfernt und Kontakte werden ausgematcht",

        // Warnings
        ["settings.warnings_title"] = "Kontoverwarnungen",
        ["settings.no_warnings"] = "Keine Verwarnungen vorhanden.",

        // Moderator messages
        ["settings.modmsg_title"] = "Moderator-Nachrichten",
        ["settings.no_modmsg"] = "Keine Nachrichten vorhanden.",
        ["settings.back_to_settings_arrow"] = "← Zurück zu den Einstellungen",

        // Feedback flow
        ["settings.back_to_settings"] = "Zurück zu den Einstellungen",
        ["settings.feedback_thanks"] = "Danke! Dein Feedback wurde an das AetherLove-Team gesendet.",
        ["settings.feedback_intro"] = "Einen Fehler gefunden, eine Idee oder möchtest du etwas vorschlagen? Lass es uns wissen.",
        ["settings.feedback_note"] = "Bitte beachte: Feedback kann nicht genutzt werden, um gegen einen Bann oder eine Verwarnung Einspruch zu erheben.",
        ["settings.feedback_type"] = "Typ",
        ["settings.feedback_kind_bug"] = "Fehler",
        ["settings.feedback_kind_improvement"] = "Verbesserung",
        ["settings.feedback_kind_other"] = "Sonstiges",
        ["settings.feedback_your_message"] = "Deine Nachricht",
        ["settings.sending"] = "Wird gesendet…",
        ["settings.submit"] = "Einreichen",
        ["settings.feedback_rate_limited"] = "Du kannst nur {0} Mal pro Stunde Feedback senden. Bitte versuche es später erneut.",
        ["settings.feedback_send_failed"] = "Dein Feedback konnte nicht gesendet werden. Bitte versuche es erneut.",

        ["settings.contributors"] = "Mitwirkende",
        ["settings.contributors_thanks_title"] = "Vielen Dank",
        ["settings.contributors_intro"] = "AetherLove wäre ohne diese Menschen nicht möglich:",
        ["settings.contributors_leads"] = "Projektleitung: Astraea & Nihal",
        ["settings.contributors_council"] = "The Chon-Chon Council",
        ["settings.contributors_moderation"] = "Moderation: Su",
        ["settings.contributors_translators"] = "Übersetzer: Tears, Mufami, Terashi, Su, Astraea",
        ["settings.contributors_xivauth"] = "XIVAuth by KazWolfe",
        ["settings.contributors_punish"] = "Puni.sh",
        ["settings.contributors_dalamud"] = "The Dalamud project",
        ["settings.contributors_testers"] = "Allen wunderbaren Betatestern in ganz Eorzea.",

        // added after update 1.4.0
        ["settings.lock_position"] = "Position sperren",
        ["settings.lock_position_caption"] = "Wenn du die Position sperrst, kannst du das Telefon (groß und mini) nicht mehr bewegen; sie bleiben an ihrem Platz.",

        // added after update 1.4.3
        ["settings.show_during_gpose"] = "Telefon während der Gruppenpose anzeigen",
        ["settings.show_during_gpose_tooltip"] = "Hält das Telefon während der Gruppenpose (/gpose) sichtbar und überschreibt Dalamuds Einstellung, die Plugin-Fenster in der Gruppenpose ausblendet.",
        ["settings.hide_during_cutscene"] = "Telefon in Zwischensequenzen anzeigen",
        ["settings.hide_during_cutscene_tooltip"] = "Hält das Telefon während einer Zwischensequenz sichtbar. Aus blendet es in Zwischensequenzen aus (Standard).",
        ["settings.tomestone_emote"] = "Steintafel-Emote beim Nutzen der App zeigen",
        ["settings.tomestone_emote_tooltip"] = "Solange das Telefon offen ist, spielt dein Charakter das /tomescroll-Emote, als würde er darin lesen. Startet nur in sicheren Gebieten und unterbricht nie ein anderes Emote.",

        // added after update 1.5.0
        ["settings.menu_supporter"] = "Unterstützer",
        ["settings.supporter_link_button"] = "Patreon-Konto verbinden",
        ["settings.supporter_contacting"] = "Verbindung zum Server...",
        ["settings.supporter_awaiting_browser"] = "Schließe die Verknüpfung in deinem Browser ab und komm dann hierher zurück.",
        ["settings.supporter_open_again"] = "Browser erneut öffnen",
        ["settings.supporter_cancel"] = "Abbrechen",
        ["settings.supporter_you_are_title"] = "Du bist Supporter",
        ["settings.supporter_you_are_body"] = "Dein Patreon-Konto wurde erfolgreich verknüpft und dein Supporter-Status ist aktiviert.",
        ["settings.supporter_nomember_title"] = "Keine Mitgliedschaft gefunden",
        ["settings.supporter_nomember_body"] = "Dein Patreon-Konto wurde verknüpft, aber es wurde keine aktive Mitgliedschaft gefunden. Wenn du gerade erst gepledged hast, wird dein Supporter-Status innerhalb weniger Stunden automatisch vergeben. Du kannst die Verknüpfung auch aufheben und es erneut versuchen, sobald deine Mitgliedschaft auf Patreon aktiv ist.",
        ["settings.supporter_not_entitled"] = "Noch keine aktive Mitgliedschaft gefunden. Wenn du gerade beigetreten bist, wird deine Rolle innerhalb weniger Stunden automatisch vergeben.",
        ["settings.supporter_unlink_button"] = "Patreon trennen",
        ["settings.supporter_unlink_confirm"] = "Dieses Patreon-Konto trennen? Deine Unterstützer-Rolle wird entfernt.",
        ["settings.supporter_failed"] = "Verknüpfung fehlgeschlagen. Bitte versuche es erneut.",
        ["settings.supporter_retry"] = "Erneut versuchen",
        ["settings.supporter_unavailable"] = "Die Unterstützer-Verknüpfung ist derzeit nicht verfügbar. Bitte schau später wieder vorbei.",
        ["settings.supporter_link_expired"] = "Die Verknüpfungsanfrage ist abgelaufen. Bitte versuche es erneut.",

        // added after update 1.5.1
        ["settings.supporter_linked"] = "Verbunden",
        ["settings.supporter_title"] = "Werde Unterstützer",
        ["settings.supporter_intro"] = "Du kannst das Projekt finanziell über unser Patreon unterstützen. Jede Funktion in AetherLove ist für alle kostenlos, und nichts ist oder wird jemals hinter einer Bezahlschranke liegen. Unterstützer bekommen einfach ein herzliches Dankeschön: sanftere Limits und ein paar funkelnde Extras, unsere Art zu sagen, dass wir das ohne dich nicht schaffen würden.",
        ["settings.supporter_perks_header"] = "AetherLove-Vorteile",
        ["settings.supporter_msgr_perks_header"] = "Messenger-Vorteile",
        ["settings.supporter_perk_msgr_groups_title"] = "Riesige Gruppenchats",
        ["settings.supporter_perk_msgr_groups_body"] = "Füge unbegrenzt Freunde per Code hinzu, führe bis zu 10 Gruppenchats gleichzeitig und hol bis zu 15 Leute in jeden.",
        ["settings.supporter_perk_msgr_storage_title"] = "Mehr Messenger-Speicher",
        ["settings.supporter_perk_msgr_storage_body"] = "Mehr Platz für die Selfies und Bilder, die du teilst, und längere Aufbewahrung, bevor sie verschwinden.",
        ["settings.supporter_perk_profiles_title"] = "Zwei Profile",
        ["settings.supporter_perk_profiles_body"] = "Nutze zwei AetherLove-Profile nebeneinander, ideal für mehrere OCs oder um RP und OOC zu trennen.",
        ["settings.supporter_perk_photos_title"] = "Mehr Raum zum Glänzen",
        ["settings.supporter_perk_photos_body"] = "Bis zu 5 zusätzliche Profilbilder und 2 weitere pro RP-Charakter.",
        ["settings.supporter_perk_superlike_title"] = "Superlike",
        ["settings.supporter_perk_superlike_body"] = "Zeig jemandem, dass er dir wirklich aufgefallen ist. Die Person wird benachrichtigt, dass du ein Superlike gesendet hast, und wenn sie zurück-liked, matcht ihr sofort.",
        ["settings.supporter_perk_rewinds_title"] = "5 Rewinds pro Tag",
        ["settings.supporter_perk_rewinds_body"] = "Zu früh gewischt? Spule bis zu 5 Mal am Tag zurück statt nur einmal.",
        ["settings.supporter_perk_analytics_title"] = "Tiefere Einblicke",
        ["settings.supporter_perk_analytics_body"] = "Zusätzliche Auswertungen und Statistiken, wer dich mag und wie dein Profil wirklich ankommt.",
        ["settings.supporter_perk_colors_title"] = "Lebendige Farben",
        ["settings.supporter_perk_colors_body"] = "Dein Name schimmert wie ein Regenbogen und wechselt langsam durch Farben, an denen niemand vorbeiscrollt.",
        ["settings.supporter_perk_badge_title"] = "Unterstützer-Abzeichen",
        ["settings.supporter_perk_badge_body"] = "Ein Unterstützer-Tag und ein kleiner Stern neben deinem Namen. Dieser leise Flex, verdient.",
        ["settings.supporter_how_heading"] = "So funktioniert's",
        ["settings.supporter_how_intro"] = "Wir bieten drei Stufen zu unterschiedlichen Preisen, damit du wählen kannst, was dir angenehm ist, und jede Stufe schaltet genau dieselben Vorteile frei. Keine Stufe bekommt mehr als eine andere.",
        ["settings.supporter_step1_title"] = "1. Auf Patreon abonnieren",
        ["settings.supporter_step1_body"] = "Erstelle ein Patreon-Konto und abonniere eine unserer Unterstützer-Stufen, sie alle schalten dieselben Vorteile frei.",
        ["settings.supporter_step2_title"] = "2. Mit AetherLove verbinden",
        ["settings.supporter_step2_body"] = "Verbinde dein Patreon-Konto über den Button unten. Wenn du eine aktive Mitgliedschaft hast, wird dein Konto sofort Premium.",
        ["settings.supporter_become"] = "Werde Unterstützer auf Patreon",
        ["settings.supporter_data_note"] = "Wir speichern nur deine Patreon-Benutzer-ID und ob du Mitglied unserer Kampagne bist. Wir speichern niemals deinen Namen, deine E-Mail oder deine sozialen Konten.",
        ["settings.sup_learn_title"] = "Unterstütze AetherLove",
        ["settings.sup_learn_body"] = "Diese Person unterstützt AetherLove. Willst du sehen, was dir das Unterstützen des Projekts bringt? Zusätzliche Fotos, Superlikes, Namensstile, Bonus-Statistiken und mehr.",
        ["settings.sup_learn_more"] = "Mehr Infos",

        ["settings.sup_thanks_title"] = "Du bist jetzt Supporter!",
        ["settings.sup_thanks_sub"] = "Danke, dass du AetherLove unterstützt!",
        ["settings.sup_thanks_body"] = "Deine Unterstützung hält die Server am Laufen. Viel Spaß mit deinen neuen Vorteilen!",
        ["settings.sup_thanks_continue"] = "Weiter",
        // AetherLove-app notification master (added after update 1.5.1)
        ["settings.enable_aetherlove_notifications"] = "AetherLove-Benachrichtigungen aktivieren",
        ["settings.menu_nsfw"] = "NSFW-Einstellungen",
        // DTR server-info-bar entries (added after update 1.5.1)
        ["dtr.chats"] = "Chats",
        ["dtr.matches"] = "Matches",
        ["dtr.news"] = "News",
        ["settings.show_dtr_count"] = "Ungelesene Zähler in der Serverinfo-Leiste anzeigen",
        ["settings.show_dtr_count_tooltip"] = "Fügt AetherLove-Einträge (Chats, Matches, News) zur Serverinfo-Leiste des Spiels hinzu, neben FPS und Ping. Jeder zeigt die Anzahl ungelesener Elemente und wird ausgeblendet, wenn es nichts Neues gibt. Einzelne Einträge kannst du auch in den Serverinfo-Leisten-Einstellungen von Dalamud ausblenden.",

        // Multi-profile: the profile picker, switching, and delete-profile (added after update 1.5.1)
        ["settings.menu_switch_profile"] = "Profil wechseln",
        ["settings.delete_profile"] = "Profil löschen",
        ["settings.delete_profile_warning_intro"] = "Das Löschen dieses AetherLove-Profils ist endgültig. Bevor du fortfährst, passiert genau Folgendes:",
        ["settings.delete_bullet_profile"] = "Dieses Dating-Profil wird entfernt und kann nicht wiederhergestellt werden.",
        ["settings.delete_profile_account_stays"] = "Dein AetherOS-Konto und deine anderen Apps bleiben unberührt. Du kannst jederzeit ein neues Profil erstellen.",
        ["picker.title"] = "Profil auswählen",
        ["picker.subtitle"] = "Wer swipet heute?",
        ["picker.current"] = "Aktuelles Profil",
        ["picker.banned"] = "Gesperrt",
        ["picker.finish_setup"] = "Einrichtung abschließen",
        ["picker.locked"] = "Supporter-Slot",
        ["picker.create"] = "Neues Profil",
        ["picker.create_sub"] = "Ein neues Dating-Profil beginnen",
        ["picker.create_secondary"] = "Zweites Profil erstellen",
        ["picker.create_supporter_pitch"] = "Ein zweites AetherLove-Profil ist ein Supporter-Vorteil. Unterstütze das Projekt, um eine weitere Dating-Persona freizuschalten, mit eigenen Matches und Chats, komplett getrennt von deinem ersten Profil.",
        ["picker.locked_supporter_pitch"] = "Dieses Profil liegt in einem Supporter-Slot. Erneuere deinen Supporter-Status, um es wieder zu öffnen; es wurde nichts gelöscht, es ist nur gesperrt.",
        ["picker.switching"] = "Profil wird gewechselt...",
        ["picker.switch_failed"] = "Der Profilwechsel ist fehlgeschlagen. Bitte versuche es erneut.",
        ["picker.share_as_title"] = "Teilen als...",
        ["picker.share_as_body"] = "Welches Profil soll das teilen?",
        ["picker.share_as_current"] = "{0} (aktuell)",

        // Messenger (added after update 1.5.1)
        ["dtr.messenger"] = "Messenger",

        // added after update 2.0.0.0
        ["settings.section_time_format"] = "Zeitformat",
        ["settings.time_24h"] = "24-Stunden",
        ["settings.time_12h"] = "12-Stunden",

        // added after update 2.0.1
        ["settings.audio_sounds_off"] = "BenachrichtigungstÃ¶ne sind derzeit deaktiviert.",
        ["settings.audio_sounds_enable"] = "BenachrichtigungstÃ¶ne aktivieren",
        ["settings.audio_volume"] = "BenachrichtigungslautstÃ¤rke",
        ["settings.audio_device"] = "AusgabegerÃ¤t",
        ["settings.audio_device_default"] = "Systemstandard",
        ["settings.audio_test"] = "Testton abspielen",
        ["settings.audio_test_ok"] = "LÃ¤uft! Wenn du nichts hÃ¶rst, probiere oben ein anderes AusgabegerÃ¤t.",
        ["settings.font_header"] = "Schriftart",
        ["settings.font_caption"] = "Gilt für den gesamten Text auf dem Handy. Zeichen, die einer Schrift fehlen, nutzen die Standardschrift.",
        ["settings.font_default"] = "Standard",
        ["settings.phone_size_header"] = "Handy-Größe",
        ["settings.mini_phone_size_header"] = "Miniatur-Größe",
    };
}
