namespace AetherLove.Services.Localization;

internal static class ProfileEn
{
    public static readonly System.Collections.Generic.Dictionary<string, string> Strings = new()
    {
        // ProfileScreen — load / empty states
        ["profile.load_failed"] = "Couldn't load profile: {0}",
        ["profile.none_loaded"] = "No profile loaded.",

        // ProfileScreen — sections
        ["profile.about"] = "About",
        ["profile.looking_for"] = "Looking for",
        ["profile.info"] = "Info",
        ["profile.gender"] = "Gender",
        ["profile.languages"] = "Languages",
        ["profile.timezone"] = "Timezone",
        ["profile.favourite_job"] = "Favourite job",
        ["profile.favourite_location"] = "Favourite location",
        ["profile.favourite_expansion"] = "Favourite expansion",
        ["profile.favourite_spotify_song"] = "Favourite Spotify song",
        ["profile.favourite_movie"] = "Favourite movie",
        ["profile.favourite_anime"] = "Favourite anime",
        ["profile.favourite_ff_character"] = "Favourite FF character",
        ["profile.sync_tool"] = "Sync tool",
        ["profile.uses_sync_tool"] = "Uses sync tool",
        ["profile.preferred"] = "Preferred",
        ["profile.yes"] = "Yes",
        ["profile.no"] = "No",
        ["profile.weekday_playtimes"] = "Weekday playtimes  (Mon–Fri)",
        ["profile.weekend_playtimes"] = "Weekend playtimes  (Sat–Sun)",
        ["profile.timezone_value"] = "{0} (current time: {1})",

        // ProfileScreen — Spotify / NSFW pill
        ["profile.spotify_open_tooltip"] = "Click to open in Spotify",
        ["profile.nsfw_reveal"] = "Click to show NSFW image",

        // ProfileScreen — back pill
        ["profile.back_to_chat"] = "Back to chat",
        ["profile.back_to_swiping"] = "Back to swiping",

        // ProfileScreen — report flow
        ["profile.report_profile"] = "Report profile",
        ["profile.report_warning"] = "False or malicious reports lead to warnings on your own account, and repeated abuse can result in suspension. Only report profiles that actually violate the rules.",
        ["profile.report_prompt"] = "Tell our moderators what's wrong with {0}:",
        ["profile.this_profile"] = "this profile",
        ["profile.report_agree"] = "I understand false reports may result in warnings against my account.",
        ["profile.submitting"] = "Submitting…",
        ["profile.cancel"] = "Cancel",
        ["profile.submit_report"] = "Submit report",
        ["profile.report_submitted"] = "Report submitted",
        ["profile.report_thanks"] = "Thanks — our moderators will take a look. You won't see this profile again until you pull a fresh one from the deck.",
        ["profile.closing"] = "Closing…",
        ["profile.closing_in"] = "Closing in {0} seconds",
        ["profile.close"] = "Close",

        // MyProfileScreen — tabs
        ["profile.tab_view"] = "View Profile",
        ["profile.tab_edit"] = "Edit Profile",
        ["profile.tab_images"] = "Change Images",

        // MyProfileScreen — edit tab load / save
        ["profile.load_profile_failed"] = "Couldn't load your profile: {0}",
        ["profile.retry"] = "Retry",
        ["profile.save_failed"] = "Save failed: {0}",
        ["profile.saving"] = "Saving…",
        ["profile.saved"] = "Saved  ✓",
        ["profile.save_changes"] = "Save Changes",

        // MyProfileScreen — edit form section headings
        ["profile.heading_identity"] = "Identity",
        ["profile.heading_character"] = "Character",
        ["profile.heading_location"] = "Location",
        ["profile.heading_languages"] = "Languages I Speak",
        ["profile.heading_content"] = "I Enjoy the Following Content",
        ["profile.heading_looking_for"] = "I Am Looking For",
        ["profile.heading_nsfw"] = "NSFW",
        ["profile.heading_optional"] = "Optional",
        ["profile.heading_playtime"] = "Playtime",
        ["profile.heading_timezone"] = "Timezone",
        ["profile.heading_sync_tool"] = "Sync Tool",
        ["profile.heading_match_prefs"] = "Match Preferences",

        // MyProfileScreen — edit form labels / hints
        ["profile.display_name"] = "Display Name",
        ["profile.display_name_hint"] = "First name or alias, no spaces.",
        ["profile.about_me"] = "About Me",
        ["profile.char_count"] = "{0} / 500 characters",
        ["profile.preview"] = "Preview",
        ["profile.bio_placeholder"] = "Your bio will appear here…",
        ["profile.race"] = "Race",
        ["profile.region"] = "Region",
        ["profile.languages_hint"] = "Select every language you are comfortable chatting in.",
        ["profile.content_hint"] = "Select everything that applies.",
        ["profile.looking_for_hint"] = "Being honest helps find better matches.",
        ["profile.nsfw_lalafell"] = "Adult and NSFW features are not available while your race is set to Lalafell. See the Terms of Service for details.",
        ["profile.nsfw_explainer"] = "NSFW stands for \"Not Safe For Work\": content containing nudity or sexual themes. Opt in to see and be matched with NSFW profiles.",
        ["profile.nsfw_optin"] = "NSFW Profiles: YES",
        ["profile.favourite_job_tooltip"] = "The job or role you enjoy the most. Type to filter.",
        ["profile.favourite_spotify"] = "Favourite Spotify Song",
        ["profile.spotify_tooltip"] = "Paste a Spotify track URL or track ID.",
        ["profile.track_id"] = "Track ID: {0}",
        ["profile.favourite_ff_character_full"] = "Favourite Final Fantasy Character",
        ["profile.weekday_playtimes_edit"] = "Weekday Playtimes (Mon–Fri)",
        ["profile.weekend_playtimes_edit"] = "Weekend Playtimes (Sat–Sun)",
        ["profile.sync_tool_hint"] = "Sync tools let matched users share mod appearances.",
        ["profile.match_prefs_body"] = "Tell us who you'd like to match with. These preferences help surface the right people for you.",
        ["profile.all"] = "All",
        ["profile.none"] = "None",
        ["profile.clear"] = "Clear",
        ["profile.filter_any_race"] = "  No selection: any race",
        ["profile.filter_any_gender"] = "  No selection: any gender",
        ["profile.filter_any_region"] = "  No selection: any region",
        ["profile.filter_any_language"] = "  No selection: no language preference",
        ["profile.spoken_language"] = "Spoken Language",
        ["profile.spoken_language_tooltip"] = "Leave all unchecked to match regardless of language.",

        // MyProfileScreen.Images — tab text
        ["profile.load_photos_failed"] = "Couldn't load your photos: {0}",
        ["profile.profile_picture"] = "Profile Picture",
        ["profile.profile_picture_desc"] = "Your profile picture is shown in the chat list and on match cards. Use a square close-up portrait of your FFXIV character.",
        ["profile.profile_photos"] = "Profile Photos",
        ["profile.profile_photos_desc"] = "Add portrait photos to your profile (10:16 ratio). The first slot is required; slots 2–4 are optional.",
        ["profile.declare_before_save"] = "Mark every extra photo as SFW or NSFW before saving.",

        // MyProfileScreen.Images — avatar section
        ["profile.new_photo_ready"] = "New photo ready, not yet saved.",
        ["profile.change_photo"] = "Change Photo",
        ["profile.profile_picture_set"] = "Profile picture: Set  ✓",
        ["profile.no_profile_picture"] = "No profile picture set.",
        ["profile.upload_avatar"] = "Upload Avatar…",

        // MyProfileScreen.Images — slot grid + active slot controls
        ["profile.slot_main"] = "Main",
        ["profile.tap_slot"] = "Tap a slot above to add or change a photo.",
        ["profile.main_photo"] = "Main photo",
        ["profile.extra_photo"] = "Extra photo {0}",
        ["profile.photo_will_be_removed"] = "Photo will be removed.",
        ["profile.undo"] = "Undo",
        ["profile.main_must_be_sfw"] = "Your main profile picture MUST be SFW. Uploading an NSFW picture is grounds for account suspension or deletion.",
        ["profile.sfw_or_nsfw"] = "Is this picture SFW or NSFW?",
        ["profile.sfw_mismatch_warning"] = "If our system detects you uploaded NSFW while SFW is selected, your photo will be held for moderation and you risk account suspension.",
        ["profile.photo_ready"] = "Photo ready, not yet saved.",
        ["profile.replace"] = "Replace",
        ["profile.photo_set"] = "Photo set  ✓",
        ["profile.currently_nsfw"] = "Currently: NSFW",
        ["profile.currently_sfw"] = "Currently: SFW",
        ["profile.remove"] = "Remove",
        ["profile.photo_required"] = "This photo is required.",
        ["profile.photo_optional"] = "This photo is optional.",
        ["profile.upload_photo"] = "Upload Photo…",

        // MyProfileScreen.Images — file picker / crop popup
        ["profile.select_image"] = "Select Image",
        ["profile.image_files_filter"] = "Image files",
        ["profile.crop_avatar"] = "Crop Avatar",
        ["profile.crop_main_photo"] = "Crop Main Photo",
        ["profile.crop_extra_photo"] = "Crop Extra Photo {0}",
    };
}
