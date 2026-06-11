namespace AetherLove.Services.Localization;

internal static class SettingsDe
{
    public static readonly System.Collections.Generic.Dictionary<string, string> Strings = new()
    {
        // ===== Settings =====
        ["settings.title"] = "Einstellungen",

        // Section labels
        ["settings.section_appearance"] = "Darstellung",
        ["settings.section_phone_size"] = "Telefongröße",
        ["settings.section_plugin_language"] = "Plugin-Sprache",
        ["settings.section_privacy"] = "Privatsphäre",
        ["settings.section_general"] = "Allgemein",
        ["settings.section_notifications"] = "Benachrichtigungen",
        ["settings.section_moderation"] = "Moderation",

        // Phone size picker (AppearancePicker)
        ["settings.phone_size_small"] = "Klein",
        ["settings.phone_size_medium"] = "Mittel",
        ["settings.phone_size_large"] = "Groß",
        ["settings.phone_size_caption"] = "Skaliert das gesamte Telefon. Größere Größen eignen sich für höher auflösende Bildschirme; Groß passt möglicherweise nicht auf ein 1080p-Display.",

        // General
        ["settings.disable_startup_heartbeat"] = "Herzschlag-Sound beim Start deaktivieren",

        // Buttons
        ["settings.view_changelog"] = "Änderungsprotokoll ansehen",
        ["settings.send_feedback"] = "Feedback senden",
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
        ["settings.auto_open_minimized"] = "Beim Anmelden automatisch minimiert öffnen",
        ["settings.pulse_optout"] = "Gelegentliche Nachrichten im Spiel",
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
        ["settings.warnings_button_unseen"] = "Verwarnungen ({0} ungesehen / {1})",
        ["settings.warnings_button"] = "Verwarnungen ({0})",
        ["settings.warnings_title"] = "Verwarnungen",
        ["settings.no_warnings"] = "Keine Verwarnungen vorhanden.",
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
    };
}
