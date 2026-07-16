namespace AetherLove.Services.Localization;

internal static class HubErrorsDe
{
    public static readonly System.Collections.Generic.Dictionary<string, string> Strings = new()
    {
        ["huberror.generic"] = "Ein unerwarteter Serverfehler ist aufgetreten.",
        ["huberror.generic_detail"] = "Ein Fehler ist aufgetreten: {0}",
        ["huberror.invalid_request"] = "Der Server hat die Anfrage abgelehnt. Falls das wiederholt passiert, aktualisiere bitte das Plugin.",
        ["huberror.unauthenticated"] = "Deine Sitzung ist nicht mehr gültig. Bitte melde dich erneut an.",
        ["huberror.banned"] = "Dein Konto wurde gesperrt.",
        ["huberror.rate_limited"] = "Du machst das zu oft. Bitte versuche es gleich noch einmal.",
        ["huberror.profile_not_found"] = "Profil nicht gefunden.",
        ["huberror.profile_not_visible"] = "Dieses Profil ist nicht verfügbar.",
        ["huberror.deck_expired"] = "Dieses Profil ist nicht mehr in deinem Deck. Aktualisiere dein Deck und versuche es erneut.",
        ["huberror.no_active_match"] = "Du bist mit diesem Spieler nicht mehr gematcht.",
        ["huberror.peer_keys_missing"] = "Dieser Nutzer hat die E2E-Verschlüsselung noch nicht eingerichtet und kann nicht chatten. Bitte versuche es später erneut.",
        ["huberror.key_bundle_exists"] = "Für dieses Konto sind bereits Verschlüsselungsschlüssel eingerichtet.",
        ["huberror.message_too_large"] = "Diese Nachricht ist zu lang zum Senden.",
        ["huberror.bio_too_long"] = "Deine Biografie überschreitet das Limit von {0} Zeichen.",
        ["huberror.lalafell_erp"] = "Erwachsenen-Rollenspiel ist für Lalafell-Charaktere nicht verfügbar.",
        ["huberror.lalafell_nsfw"] = "NSFW-Funktionen sind für Lalafell-Charaktere nicht verfügbar.",
        ["huberror.lalafell_nsfw_photo"] = "NSFW-Fotos sind für Lalafell-Charaktere nicht verfügbar.",
        ["huberror.nsfw_disable_blocked"] = "Entferne deine NSFW-Fotos und deaktiviere 18+-Rollenspiel, bevor du NSFW ausschaltest.",
        ["huberror.img_too_large"] = "Das Bild ist zu groß ({0} MB). Maximal sind {1} MB erlaubt.",
        ["huberror.img_dimensions_too_large"] = "Das Bild ist zu groß ({0}×{1}). Die längste Seite darf {2}px betragen.",
        ["huberror.img_crop_too_small"] = "Der Zuschnittbereich ist zu klein (mind. {0}px pro Seite).",
        ["huberror.img_decode_failed"] = "Das Bild konnte nicht gelesen werden. Unterstützte Formate: PNG, JPEG, WebP, GIF.",
        ["huberror.img_payload_invalid"] = "Das Foto konnte nicht hochgeladen werden. Bitte wähle das Bild erneut aus.",
        ["huberror.report_self"] = "Du kannst dich nicht selbst melden.",
        ["huberror.report_reason_required"] = "Bitte beschreibe das Problem.",
        ["huberror.report_reason_too_long"] = "Die Begründung ist zu lang (max. {0} Zeichen).",
        ["huberror.report_target_gone"] = "Dieses Profil existiert nicht mehr.",
        ["huberror.report_duplicate"] = "Du hast diesen Nutzer kürzlich bereits gemeldet. Unser Team prüft den Fall.",
        ["huberror.feedback_required"] = "Bitte gib vor dem Senden eine Nachricht ein.",
        // added after update (1.3.1)
        ["huberror.reswipe_nothing_to_undo"] = "Es gibt nichts rückgängig zu machen.",
        ["huberror.reswipe_already_matched"] = "Bei einem Match kannst du nicht rückgängig machen.",
        ["huberror.reswipe_quota_exhausted"] = "Du hast dein Rückgängig für heute bereits genutzt.",
        ["huberror.superlike_quota_exhausted"] = "Deine Superlikes für heute sind aufgebraucht.",

        // added after update 1.4.3
        ["huberror.character_limit_reached"] = "Du kannst höchstens {0} RP-Charaktere haben.",
        ["huberror.character_name_invalid"] = "Charakternamen müssen 3-50 Zeichen lang sein.",
        ["huberror.character_not_found"] = "Diesen Charakter gibt es nicht mehr. Lade neu und versuche es erneut.",

        // added after update 1.5.0
        ["huberror.patreon_disabled"] = "Die Patreon-Verknüpfung ist derzeit nicht verfügbar.",
        ["huberror.patreon_already_linked"] = "Mit deinem Profil ist bereits ein Patreon-Konto verbunden.",
        ["huberror.patreon_not_linked"] = "Mit deinem Profil ist kein Patreon-Konto verbunden.",
        ["huberror.patreon_account_taken"] = "Dieses Patreon-Konto ist bereits mit einem anderen AetherLove-Konto verbunden.",
        ["huberror.patreon_link_failed"] = "Die Patreon-Verknüpfung konnte nicht abgeschlossen werden. Bitte versuche es erneut.",
        ["huberror.places_disabled"] = "Orte ist derzeit nicht verfügbar.",
        ["huberror.venue_not_found"] = "Diese Venue existiert nicht mehr.",
        ["huberror.venue_limit_reached"] = "Du hast das Limit von {0} Venues erreicht.",
        ["huberror.venue_name_invalid"] = "Der Venue-Name muss 3 bis 60 Zeichen lang sein.",
        ["huberror.venue_description_too_long"] = "Die Beschreibung überschreitet das Limit von {0} Zeichen.",
        ["huberror.venue_times_invalid"] = "Eine der Öffnungszeiten ist ungültig.",
        ["huberror.venue_times_too_many"] = "Eine Venue kann höchstens {0} Öffnungszeiten haben.",
        ["huberror.venue_review_own"] = "Du kannst deine eigene Venue nicht bewerten.",
        ["huberror.venue_review_too_long"] = "Deine Bewertung überschreitet das Limit von {0} Zeichen.",
        ["huberror.venue_review_rating_invalid"] = "Wähle eine Wertung von 1 bis 5 Sternen.",
        ["huberror.venue_rsvp_invalid"] = "Für diese Öffnung sind keine Zusagen mehr möglich.",
    };
}
