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
        ["settings.disable_startup_heartbeat"] = "Herzschlag-Sound beim Start deaktivieren",
        ["settings.confirm_before_close"] = "Vor dem Schließen von AetherLove bestätigen",

        // Buttons
        ["settings.view_changelog"] = "Änderungsprotokoll ansehen",
        ["settings.send_feedback"] = "Feedback senden",
        ["settings.terms_of_service"] = "Nutzungsbedingungen",
        ["settings.delete_account"] = "Konto löschen",
        ["settings.create_new_profile"] = "Ein neues Profil erstellen",
        ["settings.cancel"] = "Abbrechen",
        ["settings.back"] = "Zurück",

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
        ["settings.hide_notifications_in_combat_tooltip"] = "Wenn aktiviert, erhältst du im Kampf keine Benachrichtigungen — weder Chat-Ankündigungen noch Popups oder Sounds.",
        ["settings.auto_open_minimized"] = "Beim Anmelden automatisch minimiert öffnen",
        ["settings.pulse_optout"] = "Erhalte fantastische Nachrichten vom Aethernet-Team, die dich ans Swipen erinnern",
        ["settings.pulse_optout_tooltip"] = "Ab und zu lässt AetherLove eine spielerische Zeile in deinem Spielchat fallen. Deaktiviere dies, um sie zu stoppen.",
        ["settings.combat_behavior"] = "Im Kampf",
        ["settings.combat_behavior_hide"] = "AetherLove ausblenden",
        ["settings.combat_behavior_minimize"] = "Als Blase minimieren",
        ["settings.combat_behavior_leave_open"] = "Geöffnet lassen",
        ["settings.notification_sound"] = "Benachrichtigungston",
        ["settings.play"] = "Abspielen",

        // Delete account confirmation
        ["settings.delete_warning_intro"] = "Diese Aktion ist dauerhaft und kann nicht rückgängig gemacht werden. Bitte lies das Folgende sorgfältig, bevor du fortfährst:",
        ["settings.delete_bullet_account"] = "Dein Konto wird dauerhaft gelöscht.",
        ["settings.delete_bullet_matches"] = "Alle deine Matches werden entfernt.",
        ["settings.delete_bullet_preferences"] = "Deine Match-Einstellungen werden gelöscht.",
        ["settings.delete_bullet_pictures"] = "Deine Profilbilder werden entfernt.",
        ["settings.delete_reregister"] = "Du kannst dich jederzeit neu registrieren.",
        ["settings.delete_previous_failed"] = "Vorheriger Versuch fehlgeschlagen: {0}",

        // Deleting / deleted views
        ["settings.deleting_title"] = "Konto wird gelöscht",
        ["settings.deleting_body"] = "Deine Daten werden entfernt und Kontakte werden ausgematcht",
        ["settings.deleted_title"] = "Konto gelöscht",
        ["settings.deleted_body"] = "Dein Konto wurde gelöscht, deine Daten und Bilder wurden entfernt und deine Matches wurden aufgelöst. Du kannst das Plugin jetzt entfernen oder die Einrichtung durchlaufen und ein neues Profil erstellen.",

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
    };
}
