namespace AetherLove.Services.Localization;

internal static class CommonDe
{
    public static readonly System.Collections.Generic.Dictionary<string, string> Strings = new()
    {
        // ===== Common =====
        // Generic
        ["common.ok"] = "OK",
        ["common.confirm"] = "Bestätigen",
        ["common.cancel"] = "Abbrechen",
        ["common.loading"] = "Wird geladen…",
        ["common.try_again"] = "Erneut versuchen",
        ["common.i_understand"] = "Ich verstehe",
        ["common.sign_out"] = "Abmelden",
        ["common.got_it"] = "Verstanden!",
        ["common.moderator_notes_label"] = "Moderatoren-Notizen",
        ["common.server_unreachable_detail"] = "Server konnte nicht erreicht werden: {0}",

        // Banned screen
        ["common.banned_title"] = "Konto gesperrt",
        ["common.banned_body"] = "Dein AetherLove-Konto wurde gesperrt. Du kannst den Dienst nicht mehr nutzen.",
        ["common.banned_reason_label"] = "Grund",
        ["common.banned_uninstall_hint"] = "Du kannst dieses Fenster schließen und das Plugin jederzeit deinstallieren.",

        // Offline screen
        // Outdated-plugin screen
        ["common.outdated_title"] = "Update erforderlich",
        ["common.outdated_body"] = "Du verwendest eine veraltete Version von AetherLove. Der Server unterstützt diese Version nicht mehr, daher kann sich das Plugin nicht verbinden.",
        ["common.outdated_hint"] = "Bitte aktualisiere das Plugin im Plugin-Installer von Dalamud und öffne AetherLove dann erneut.",

        ["common.offline_title"] = "AetherLove ist offline",
        ["common.offline_body"] = "Wir können die AetherLove-Server derzeit nicht erreichen. Die App benötigt eine aktive Verbindung, um zu stöbern, zu matchen und zu chatten, daher ist sie pausiert, bis wir wieder online sind.",
        ["common.offline_reconnecting"] = "Verbindung wird wiederhergestellt…",
        ["common.offline_keep_trying"] = "Wir versuchen es automatisch weiter.",

        // Passphrase unlock screen
        ["common.passphrase_title"] = "Gib deine Verschlüsselungs-Passphrase ein",
        ["common.passphrase_intro"] = "Wir erkennen dieses Konto, aber dieses Gerät hat deinen Chat-Schlüssel noch nicht. Gib die Passphrase ein, die du auf deinem ersten Gerät festgelegt hast, um deinen Chatverlauf zu entsperren.",
        ["common.passphrase_forgot"] = "Passphrase vergessen? Es gibt keine Wiederherstellung, aber du kannst dich unten abmelden und ein neues Konto erstellen. Dein Chatverlauf mit diesem Konto geht verloren.",
        ["common.passphrase_bundle_load_failed"] = "Verschlüsselungspaket konnte nicht vom Server geladen werden.",
        ["common.passphrase_empty"] = "Bitte gib deine Passphrase ein.",
        ["common.passphrase_incorrect"] = "Falsche Passphrase. Versuche es erneut.",
        ["common.passphrase_unlock_failed"] = "Entsperren fehlgeschlagen: {0}",
        ["common.unlock"] = "Entsperren",
        ["common.unlocking"] = "Wird entsperrt…",

        // Warning acknowledge screen
        ["common.warnings_heading_one"] = "Du hast eine Moderationsverwarnung",
        ["common.warnings_heading_many"] = "Du hast {0} Moderationsverwarnungen",
        ["common.warnings_body"] = "Bitte lies die folgende(n) Verwarnung(en) des Moderationsteams. Wiederholte Verstöße können zur Sperrung des Kontos führen.",
        ["common.warnings_submit_error"] = "Server konnte nicht erreicht werden: {0}. Tippen, um es erneut zu versuchen.",
        ["common.acknowledging"] = "Wird bestätigt…",

        // Photo moderation
        ["common.nsfw_decl_unselected"] = "wähle unten eine Option",
        ["common.nsfw_decl_sfw"] = "dieses Bild ist SFW",
        ["common.nsfw_decl_nsfw"] = "dieses Bild ist NSFW",
        ["common.lalafell_nsfw_title"] = "NSFW nicht verfügbar",
        ["common.lalafell_nsfw_body"] = "Wir erlauben keine NSFW-Bilder von Lalafell-Charakteren. Da Lalafell ein kindliches Aussehen haben, wenden wir diese Richtlinie einheitlich auf jedes Lalafell-Konto an und machen keine Einzelfallausnahmen.\n\nDein Foto wurde wieder auf SFW gesetzt. Wenn dieses Foto nicht safe-for-work ist, entferne es bitte und lade ein anderes hoch.",
        ["common.undeclared_photo_title"] = "Angabe erforderlich",
        ["common.undeclared_photo_body"] = "Du musst im Auswahlfeld angeben, ob dein anderes Bild SFW oder NSFW ist, bevor du ein weiteres hochlädst.",

        // Changelog window
        ["common.changelog_window_title"] = "AetherLove — Neuigkeiten",
        ["common.whats_new"] = "Neuigkeiten",
        ["common.changelog_empty"] = "Keine Einträge im Änderungsprotokoll verfügbar.",
        ["common.changelog_latest"] = "Neueste",
        ["common.changelog_important"] = "Wichtig",
        ["common.changelog_new_features"] = "Neue Funktionen",
        ["common.changelog_bug_fixes"] = "Fehlerbehebungen",

        // Rate limit modal
        ["common.rate_limit_title"] = "Immer langsam",
        ["common.rate_limit_noun_profile"] = "Profil",
        ["common.rate_limit_noun_images"] = "Bilder",
        ["common.rate_limit_body"] = "Du kannst dein {0} nur {1} Mal pro Stunde ändern. Bitte versuche es in {2} erneut.",
        ["common.rate_limit_retry_moment"] = "einem Moment",
        ["common.rate_limit_retry_one_second"] = "1 Sekunde",
        ["common.rate_limit_retry_seconds"] = "{0} Sekunden",
        ["common.rate_limit_retry_one_minute"] = "1 Minute",
        ["common.rate_limit_retry_minutes"] = "{0} Minuten",

        // Emoji picker
        ["common.emoji_search_hint"] = "Emoji suchen...",
        // Bottom navigation bar
        ["common.nav_swipe"] = "Swipe",
        ["common.nav_matches"] = "Matches",
        ["common.nav_settings"] = "Einst.",
        ["common.nav_minimize"] = "Minim.",

        ["common.emoji_none_found"] = "Kein Emoji gefunden.",

        // Close-plugin confirmation modal
        ["common.close_plugin_tooltip"] = "AetherLove schließen",
        ["common.close_plugin_title"] = "AetherLove schließen?",
        ["common.close_plugin_body"] = "Das schließt nur das Fenster. Du bleibst verbunden und erhältst weiterhin neue Matches und Nachrichten, solange das Plugin aktiv ist.\n\nÖffne das Fenster jederzeit wieder, indem du {0} im Chat eingibst.",
        ["common.close_plugin_tip"] = "Tipp: Nutze stattdessen die Minimieren-Schaltfläche unten, damit die kleine schwebende Blase mit ihrem Benachrichtigungsabzeichen sichtbar bleibt.",
        ["common.close"] = "Schließen",

        // Save-error modal
        ["common.save_error_title"] = "Etwas ist schiefgelaufen",
        ["common.save_error_intro"] = "Deine Änderungen konnten nicht gespeichert werden:",
        ["common.save_error_report"] = "Falls das öfter passiert, melde den Fehler bitte auf unserem Discord.",
        ["common.save_error_unknown"] = "Ein unerwarteter Fehler ist aufgetreten.",

        // Image requirements modal
        ["common.img_requirements_title"] = "Bild kann nicht verwendet werden",
        ["common.img_invalid"] = "Diese Datei ist kein gültiges Bild oder das Format wird nicht unterstützt.",
        ["common.img_too_small"] = "Dieses Bild ist nur {0}×{1} px groß und damit zu klein.",
        ["common.img_requirements_sizes"] = "Avatare brauchen mindestens {0}×{1} px und Profilfotos mindestens {2}×{3} px. Bitte wähle ein größeres Bild.",

        // Image crop window
        ["common.loading_image"] = "Bild wird geladen…",
        ["common.use_this_crop"] = "Diesen Ausschnitt verwenden",
    };
}
