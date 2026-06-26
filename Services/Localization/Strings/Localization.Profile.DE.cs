namespace AetherLove.Services.Localization;

internal static class ProfileDe
{
    public static readonly System.Collections.Generic.Dictionary<string, string> Strings = new()
    {
        // ===== Profile =====
        // ProfileScreen — load / empty states
        ["profile.load_failed"] = "Profil konnte nicht geladen werden: {0}",
        ["profile.none_loaded"] = "Kein Profil geladen.",

        // ProfileScreen — sections
        ["profile.about"] = "Über",
        ["profile.flairs"] = "Abzeichen",
        ["profile.looking_for"] = "Sucht nach",
        ["profile.content_interests"] = "Bevorzugte Inhalte",
        ["profile.info"] = "Info",
        ["profile.gender"] = "Geschlecht",
        ["profile.languages"] = "Sprachen",
        ["profile.timezone"] = "Zeitzone",
        ["profile.favourite_job"] = "Lieblingsjob",
        ["profile.favourite_location"] = "Lieblingsort",
        ["profile.favourite_expansion"] = "Lieblingserweiterung",
        ["profile.favourite_spotify_song"] = "Spotify-Lieblingslied",
        ["profile.favourite_song"] = "Lieblingslied",
        ["music.fetching"] = "Songname wird abgerufen…",
        ["music.saved"] = "Link gespeichert (Name ausstehend)",
        ["music.invalid"] = "Dieser Link konnte nicht gelesen werden — füge einen Songlink von Spotify, SoundCloud, Apple Music oder YouTube Music ein.",
        ["music.open_tooltip"] = "Zum Öffnen klicken",
        ["profile.favourite_movie"] = "Lieblingsfilm",
        ["profile.favourite_anime"] = "Lieblings-Anime",
        ["profile.favourite_ff_character"] = "Lieblings-FF-Charakter",
        ["profile.sync_tool"] = "Sync-Tool",
        ["profile.uses_sync_tool"] = "Nutzt Sync-Tool",
        ["profile.preferred"] = "Bevorzugt",
        ["profile.yes"] = "Ja",
        ["profile.no"] = "Nein",
        ["profile.weekday_playtimes"] = "Spielzeiten unter der Woche  (Mo–Fr)",
        ["profile.weekend_playtimes"] = "Spielzeiten am Wochenende  (Sa–So)",
        ["profile.timezone_value"] = "{0} (aktuelle Zeit: {1})",

        // ProfileScreen — Spotify / NSFW pill
        ["profile.spotify_open_tooltip"] = "Klicken, um in Spotify zu öffnen",
        ["profile.nsfw_reveal"] = "Klicken, um NSFW-Bild anzuzeigen",

        // ProfileScreen — back pill
        ["profile.back_to_chat"] = "Zurück zum Chat",
        ["profile.back_to_swiping"] = "Zurück zum Wischen",

        // ProfileScreen — report flow
        ["profile.report_profile"] = "Profil melden",
        ["profile.report_warning"] = "Falsche oder böswillige Meldungen führen zu Verwarnungen für dein eigenes Konto, und wiederholter Missbrauch kann zu einer Sperrung führen. Melde nur Profile, die tatsächlich gegen die Regeln verstoßen.",
        ["profile.report_prompt"] = "Sag unseren Moderatoren, was mit {0} nicht stimmt:",
        ["profile.this_profile"] = "diesem Profil",
        ["profile.report_agree"] = "Mir ist bewusst, dass falsche Meldungen zu Verwarnungen gegen mein Konto führen können.",
        ["profile.submitting"] = "Wird eingereicht…",
        ["profile.cancel"] = "Abbrechen",
        ["profile.submit_report"] = "Meldung einreichen",
        ["profile.report_submitted"] = "Meldung eingereicht",
        ["profile.report_thanks"] = "Danke — unsere Moderatoren werden sich das ansehen. Du siehst dieses Profil erst wieder, wenn du ein neues aus dem Deck ziehst.",
        ["profile.closing"] = "Wird geschlossen…",
        ["profile.closing_in"] = "Schließt in {0} Sekunden",
        ["profile.close"] = "Schließen",

        // ProfileScreen — copy profile text (viewing others only)
        ["profile.copy_text"] = "Profiltext kopieren",
        ["profile.copy_warning_title"] = "Warnung",
        ["profile.copy_warning_body"] = "Du hast den Text einer anderen Person kopiert. Sei vorsichtig, wenn du darin enthaltene Links anklickst oder ihnen folgst — das geschieht auf eigene Gefahr.",
        ["profile.copy_warning_agree"] = "Ich habe verstanden und stimme zu",

        // MyProfileScreen — tabs
        ["profile.tab_view"] = "Profil ansehen",
        ["profile.tab_edit"] = "Profil bearbeiten",
        ["profile.tab_images"] = "Bilder ändern",

        // MyProfileScreen hub: stats + menu
        ["profile.section_myprofile"] = "Mein Profil",
        ["profile.section_service"] = "Service",
        ["profile.menu_view"] = "Mein Profil ansehen",
        ["profile.menu_edit"] = "Profil ändern",
        ["profile.menu_images"] = "Profilbilder",
        ["profile.stat_loves_you"] = "lieben dich",
        ["profile.stat_matches"] = "Matches",
        ["profile.stat_match_rate"] = "Match-Rate",
        ["profile.back_to_my"] = "← Zurück",

        // MyProfileScreen — edit tab load / save
        ["profile.load_profile_failed"] = "Dein Profil konnte nicht geladen werden: {0}",
        ["profile.retry"] = "Erneut versuchen",
        ["profile.save_failed"] = "Speichern fehlgeschlagen: {0}",
        ["profile.saving"] = "Wird gespeichert…",
        ["profile.saved"] = "Gespeichert  ✓",
        ["profile.save_changes"] = "Änderungen speichern",

        // MyProfileScreen — edit form section headings
        ["profile.heading_character"] = "Charakter",
        ["profile.heading_location"] = "Standort",
        ["profile.heading_languages"] = "Sprachen, die ich spreche",
        ["profile.heading_content"] = "Folgende Inhalte machen mir Spaß",
        ["profile.heading_looking_for"] = "Ich suche nach",
        ["profile.heading_nsfw"] = "NSFW",
        ["profile.heading_optional"] = "Optional",
        ["profile.heading_playtime"] = "Spielzeit",
        ["profile.heading_timezone"] = "Zeitzone",
        ["profile.heading_sync_tool"] = "Sync-Tool",
        ["profile.heading_match_prefs"] = "Match-Einstellungen",

        // MyProfileScreen — edit form labels / hints
        ["profile.display_name"] = "Anzeigename",
        ["profile.display_name_hint"] = "Vorname oder Alias, keine Leerzeichen.",
        ["profile.about_me"] = "Über mich",
        ["profile.char_count"] = "{0} / 500 Zeichen",
        ["profile.preview"] = "Vorschau",
        ["profile.bio_placeholder"] = "Deine Biografie erscheint hier…",
        ["profile.race"] = "Rasse",
        ["profile.region"] = "Region",
        ["profile.languages_hint"] = "Wähle jede Sprache, in der du dich beim Chatten wohlfühlst.",
        ["profile.content_hint"] = "Wähle alles aus, was zutrifft.",
        ["profile.looking_for_hint"] = "Ehrlichkeit hilft, bessere Matches zu finden.",
        ["profile.nsfw_lalafell"] = "Erwachsenen- und NSFW-Funktionen stehen nicht zur Verfügung, solange deine Rasse auf Lalafell eingestellt ist. Details findest du in den Nutzungsbedingungen.",
        ["profile.nsfw_explainer"] = "NSFW steht für \"Not Safe For Work\": Inhalte mit Nacktheit oder sexuellen Themen. Aktiviere dies, um NSFW-Profile zu sehen und mit ihnen gematcht zu werden.",
        ["profile.nsfw_optin"] = "NSFW-Profile: JA",
        ["profile.favourite_job_tooltip"] = "Der Job oder die Rolle, die dir am meisten Spaß macht. Tippe zum Filtern.",
        ["profile.favourite_spotify"] = "Spotify-Lieblingslied",
        ["profile.spotify_tooltip"] = "Füge eine Spotify-Track-URL oder Track-ID ein.",
        ["profile.track_id"] = "Track-ID: {0}",
        ["profile.favourite_ff_character_full"] = "Lieblings-Final-Fantasy-Charakter",
        ["profile.weekday_playtimes_edit"] = "Spielzeiten unter der Woche (Mo–Fr)",
        ["profile.weekend_playtimes_edit"] = "Spielzeiten am Wochenende (Sa–So)",
        ["profile.sync_tool_hint"] = "Mit Sync-Tools können gematchte Nutzer Mod-Erscheinungsbilder teilen.",
        ["profile.match_prefs_body"] = "Sag uns, mit wem du dich matchen möchtest. Diese Einstellungen helfen, die richtigen Leute für dich anzuzeigen.",
        ["profile.all"] = "Alle",
        ["profile.none"] = "Keine",
        ["profile.clear"] = "Zurücksetzen",
        ["profile.filter_any_race"] = "  Keine Auswahl: beliebige Rasse",
        ["profile.filter_any_gender"] = "  Keine Auswahl: beliebiges Geschlecht",
        ["profile.filter_any_region"] = "  Keine Auswahl: beliebige Region",
        ["profile.filter_any_language"] = "  Keine Auswahl: keine Sprachpräferenz",
        ["profile.spoken_language"] = "Gesprochene Sprache",
        ["profile.spoken_language_tooltip"] = "Lass alles deaktiviert, um unabhängig von der Sprache zu matchen.",

        // MyProfileScreen.Images — tab text
        ["profile.load_photos_failed"] = "Deine Fotos konnten nicht geladen werden: {0}",
        ["profile.profile_picture"] = "Profilbild",
        ["profile.profile_picture_desc"] = "Dein Profilbild wird in der Chatliste und auf Match-Karten angezeigt. Verwende ein quadratisches Nahaufnahme-Porträt deines FFXIV-Charakters.",
        ["profile.profile_photos"] = "Profilfotos",
        ["profile.profile_photos_desc"] = "Füge deinem Profil Porträtfotos hinzu (Verhältnis 10:16). Der erste Platz ist erforderlich; die Plätze 2–4 sind optional.",
        ["profile.declare_before_save"] = "Kennzeichne jedes Zusatzfoto vor dem Speichern als SFW oder NSFW.",

        // MyProfileScreen.Images — avatar section
        ["profile.new_photo_ready"] = "Neues Foto bereit, noch nicht gespeichert.",
        ["profile.change_photo"] = "Foto ändern",
        ["profile.profile_picture_set"] = "Profilbild: Festgelegt  ✓",
        ["profile.no_profile_picture"] = "Kein Profilbild festgelegt.",
        ["profile.upload_avatar"] = "Avatar hochladen…",

        // MyProfileScreen.Images — slot grid + active slot controls
        ["profile.slot_main"] = "Haupt",
        ["profile.tap_slot"] = "Tippe oben auf einen Platz, um ein Foto hinzuzufügen oder zu ändern.",
        ["profile.main_photo"] = "Hauptfoto",
        ["profile.extra_photo"] = "Zusatzfoto {0}",
        ["profile.photo_will_be_removed"] = "Foto wird entfernt.",
        ["profile.undo"] = "Rückgängig",
        ["profile.main_must_be_sfw"] = "Dein Hauptprofilbild MUSS SFW sein. Das Hochladen eines NSFW-Bildes ist ein Grund für die Sperrung oder Löschung des Kontos.",
        ["profile.sfw_or_nsfw"] = "Ist dieses Bild SFW oder NSFW?",
        ["profile.sfw_mismatch_warning"] = "Wenn unser System feststellt, dass du NSFW hochgeladen hast, während SFW ausgewählt ist, wird dein Foto zur Moderation zurückgehalten und du riskierst eine Kontosperrung.",
        ["profile.photo_ready"] = "Foto bereit, noch nicht gespeichert.",
        ["profile.replace"] = "Ersetzen",
        ["profile.photo_set"] = "Foto festgelegt  ✓",
        ["profile.currently_nsfw"] = "Aktuell: NSFW",
        ["profile.currently_sfw"] = "Aktuell: SFW",
        ["profile.remove"] = "Entfernen",
        ["profile.photo_required"] = "Dieses Foto ist erforderlich.",
        ["profile.photo_optional"] = "Dieses Foto ist optional.",
        ["profile.upload_photo"] = "Foto hochladen…",

        // MyProfileScreen.Images — file picker / crop popup
        ["profile.select_image"] = "Bild auswählen",
        ["profile.image_files_filter"] = "Bilddateien",
        ["profile.crop_avatar"] = "Avatar zuschneiden",
        ["profile.crop_main_photo"] = "Hauptfoto zuschneiden",
        ["profile.crop_extra_photo"] = "Zusatzfoto {0} zuschneiden",
    };
}
