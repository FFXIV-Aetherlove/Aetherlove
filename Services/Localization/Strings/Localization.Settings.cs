namespace AetherLove.Services.Localization;

internal static class SettingsEn
{
    public static readonly System.Collections.Generic.Dictionary<string, string> Strings = new()
    {
        ["settings.title"] = "Settings",

        // Section labels
        ["settings.section_appearance"] = "Appearance",
        ["settings.section_phone_size"] = "Phone size",
        ["settings.section_plugin_language"] = "Plugin language",
        ["settings.section_privacy"] = "Privacy",
        ["settings.section_general"] = "General",
        ["settings.section_notifications"] = "Notifications",
        ["settings.section_moderation"] = "Moderation",
        ["settings.section_other"] = "Other",
        ["settings.section_danger_zone"] = "Danger zone",

        // Phone size picker (AppearancePicker)
        ["settings.phone_size_small"] = "Small",
        ["settings.phone_size_medium"] = "Medium",
        ["settings.phone_size_large"] = "Large",
        ["settings.phone_size_caption"] = "Scales the whole phone. Larger sizes suit higher-resolution screens; Large may not fit a 1080p display.",

        // General
        ["settings.disable_startup_heartbeat"] = "Disable startup heartbeat sound",
        ["settings.confirm_before_close"] = "Confirm before closing AetherLove",

        // Buttons
        ["settings.view_changelog"] = "View changelog",
        ["settings.send_feedback"] = "Send feedback",
        ["settings.terms_of_service"] = "Terms of Service",
        ["settings.delete_account"] = "Delete Account",
        ["settings.create_new_profile"] = "Create a new profile",
        ["settings.cancel"] = "Cancel",
        ["settings.back"] = "Back",

        // Privacy
        ["settings.always_blur_nsfw"] = "Always blur NSFW",
        ["settings.always_blur_nsfw_tooltip"] = "When on, NSFW-tagged extra photos in other profiles are blurred until you click to reveal each one. Avatars and main portraits are always safe-for-work regardless. Turning this off shows every photo as-is.",
        ["settings.nsfw_profile"] = "My profile is NSFW (18+)",
        ["settings.nsfw_profile_tooltip"] = "Marks your profile as adult/NSFW so it's only shown to people who have NSFW enabled. It turns on automatically when you add NSFW photos or pick 18+ roleplay, and stays on until you remove them.",
        ["settings.nsfw_profile_locked"] = "You can't turn this off while you have NSFW photos or 18+ roleplay (ERP) selected. Remove your NSFW images and deselect 18+ roleplay first.",

        // Notifications
        ["settings.enable_notifications"] = "Enable notifications",
        ["settings.enable_notifications_tooltip"] = "Master switch for all notifications. Turn this off to silence every in-game chat announcement, popup, and sound below.",
        ["settings.enable_notification_sounds"] = "Enable notification sounds",
        ["settings.enable_notification_sounds_tooltip"] = "Notification sounds will only play if your game audio and special effects audio are not muted. Volume control is done through your Windows volume.",
        ["settings.announce_messages_chat"] = "Announce new messages in game chat",
        ["settings.announce_matches_chat"] = "Announce new matches in game chat",
        ["settings.popup_messages"] = "Show a popup for new messages",
        ["settings.popup_matches"] = "Show a popup for new matches",
        ["settings.hide_notifications_in_combat"] = "Hide notifications during combat",
        ["settings.hide_notifications_in_combat_tooltip"] = "When enabled, you won't receive any notifications — chat announcements, popups, or sounds — while you're in combat.",
        ["settings.auto_open_minimized"] = "Open minimized automatically when you log in",
        ["settings.pulse_optout"] = "Receive amazing messages from the Aethernet team to remind you to swipe",
        ["settings.pulse_optout_tooltip"] = "Every so often, AetherLove may drop a playful line into your game chat. Turn this off to stop them.",
        ["settings.combat_behavior"] = "When entering combat",
        ["settings.combat_behavior_hide"] = "Hide AetherLove",
        ["settings.combat_behavior_minimize"] = "Minimize to bubble",
        ["settings.combat_behavior_leave_open"] = "Leave open",
        ["settings.notification_sound"] = "Notification sound",
        ["settings.play"] = "Play",

        // Delete account confirmation
        ["settings.delete_warning_intro"] = "This action is permanent and cannot be undone. Please read the following carefully before continuing:",
        ["settings.delete_bullet_account"] = "Your account will be permanently deleted.",
        ["settings.delete_bullet_matches"] = "All your matches will be removed.",
        ["settings.delete_bullet_preferences"] = "Your match preferences will be cleared.",
        ["settings.delete_bullet_pictures"] = "Your profile pictures will be removed.",
        ["settings.delete_reregister"] = "You can always re-register at any time.",
        ["settings.delete_previous_failed"] = "Previous attempt failed: {0}",

        // Deleting / deleted views
        ["settings.deleting_title"] = "Deleting account",
        ["settings.deleting_body"] = "Removing your data and unmatching contacts",
        ["settings.deleted_title"] = "Account deleted",
        ["settings.deleted_body"] = "Your account has been deleted, your data and pictures have been removed, and your matches have been unmatched. You can now remove the plugin, or onboard and create a new profile.",

        // Warnings
        ["settings.warnings_button_unseen"] = "Warnings ({0} unseen / {1})",
        ["settings.warnings_button"] = "Warnings ({0})",
        ["settings.warnings_title"] = "Warnings",
        ["settings.no_warnings"] = "No warnings on file.",

        // Moderator messages
        ["settings.modmsg_button_unseen"] = "Moderator messages ({0} unseen / {1})",
        ["settings.modmsg_button"] = "Moderator messages ({0})",
        ["settings.modmsg_title"] = "Moderator messages",
        ["settings.no_modmsg"] = "No messages on file.",
        ["settings.back_to_settings_arrow"] = "← Back to settings",

        // Feedback flow
        ["settings.back_to_settings"] = "Back to settings",
        ["settings.feedback_thanks"] = "Thank you! Your feedback has been sent to the AetherLove team.",
        ["settings.feedback_intro"] = "Found a bug, have an idea, or want to suggest something? Let us know.",
        ["settings.feedback_note"] = "Please note: feedback can't be used to appeal a ban or a warning.",
        ["settings.feedback_type"] = "Type",
        ["settings.feedback_kind_bug"] = "Bug",
        ["settings.feedback_kind_improvement"] = "Improvement",
        ["settings.feedback_kind_other"] = "Other",
        ["settings.feedback_your_message"] = "Your message",
        ["settings.sending"] = "Sending…",
        ["settings.submit"] = "Submit",
        ["settings.feedback_rate_limited"] = "You can only send feedback {0} times per hour. Please try again later.",
        ["settings.feedback_send_failed"] = "Couldn't send your feedback. Please try again.",

        ["settings.contributors"] = "Contributors",
        ["settings.contributors_thanks_title"] = "Thank you",
        ["settings.contributors_intro"] = "AetherLove could not be possible without:",
        ["settings.contributors_leads"] = "Project leads: Astraea & Nihal",
        ["settings.contributors_council"] = "The Chon-Chon Council",
        ["settings.contributors_moderation"] = "Moderation: Su",
        ["settings.contributors_translators"] = "Translators: Tears, Mufami, Terashi, Su, Astraea",
        ["settings.contributors_xivauth"] = "XIVAuth by KazWolfe",
        ["settings.contributors_punish"] = "Puni.sh",
        ["settings.contributors_dalamud"] = "The Dalamud project",
        ["settings.contributors_testers"] = "All the wonderful beta testers across Eorzea.",
    };
}
