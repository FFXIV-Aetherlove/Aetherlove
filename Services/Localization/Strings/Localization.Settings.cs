namespace AetherLove.Services.Localization;

internal static class SettingsEn
{
    public static readonly System.Collections.Generic.Dictionary<string, string> Strings = new()
    {
        // Section labels + hub menu
        ["settings.section_plugin_settings"] = "Plugin settings",
        ["settings.section_phone_size"] = "Phone size",
        ["settings.section_plugin_language"] = "Plugin language",
        ["settings.section_general"] = "General settings",
        ["settings.section_notifications"] = "Notifications",
        ["settings.section_other"] = "Other",
        ["settings.section_danger_zone"] = "Danger zone",
        ["settings.menu_language_theme"] = "Language & Theme",
        ["settings.menu_appearance"] = "Phone appearance",
        ["settings.menu_chat_colors"] = "Chat appearance",
        ["settings.section_theme"] = "Theme",
        ["settings.back_arrow"] = "← Back",
        ["settings.chat_own_bg"] = "Own chat background",
        ["settings.chat_own_fg"] = "Own chat text",
        ["settings.chat_peer_bg"] = "Peer chat background",
        ["settings.chat_peer_fg"] = "Peer chat text",
        ["settings.chat_reset"] = "Reset",

        // Phone size picker (AppearancePicker)
        ["settings.phone_size_small"] = "Small",
        ["settings.phone_size_medium"] = "Medium",
        ["settings.phone_size_large"] = "Large",
        ["settings.phone_size_xl"] = "XL",
        ["settings.phone_size_xxl"] = "XXL",
        ["settings.phone_size_caption"] = "Scales the whole phone. Larger sizes suit higher-resolution screens; XL and XXL are sized for 4K and may not fit smaller displays.",
        ["settings.section_mini_phone_size"] = "Miniature phone size",
        ["settings.mini_phone_size_caption"] = "Size of the minimised bubble (shown when the phone is minimised). The preview below shows the selected size.",

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
        ["settings.warnings_title"] = "Account Warnings",
        ["settings.no_warnings"] = "No warnings on file.",

        // Moderator messages
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

        // added after update 1.4.0
        ["settings.lock_position"] = "Lock position",
        ["settings.lock_position_caption"] = "By locking the position you will be unable to move the phone (large and mini), they will be stuck in place.",

        // added after update 1.4.3
        ["settings.show_during_gpose"] = "Show AetherLove during group pose",
        ["settings.show_during_gpose_tooltip"] = "Keeps AetherLove visible while you're in group pose (/gpose), overriding Dalamud's setting that hides plugin windows during gpose.",
        ["settings.hide_during_cutscene"] = "Hide AetherLove during cutscenes",
        ["settings.hide_during_cutscene_tooltip"] = "Hides AetherLove while a cutscene is playing (the default). Turn this off to keep it visible through cutscenes.",

        // added after update 1.5.0
        ["settings.menu_supporter"] = "Supporter",
        ["settings.supporter_link_button"] = "Link Patreon account",
        ["settings.supporter_contacting"] = "Contacting the server...",
        ["settings.supporter_awaiting_browser"] = "Finish linking in your browser, then come back here.",
        ["settings.supporter_open_again"] = "Open browser again",
        ["settings.supporter_cancel"] = "Cancel",
        ["settings.supporter_you_are_title"] = "You are a supporter",
        ["settings.supporter_you_are_body"] = "Your Patreon account was linked successfully and your supporter status has been enabled.",
        ["settings.supporter_nomember_title"] = "No membership found",
        ["settings.supporter_nomember_body"] = "Your Patreon account was linked, but no active membership was found on it. If you just pledged, your supporter status is granted automatically within a few hours. You can also unlink now and try again once your membership is active on Patreon.",
        ["settings.supporter_not_entitled"] = "No active membership found yet. If you just pledged, your role is granted automatically within a few hours.",
        ["settings.supporter_unlink_button"] = "Unlink Patreon",
        ["settings.supporter_unlink_confirm"] = "Unlink this Patreon account? Your Supporter role will be removed.",
        ["settings.supporter_failed"] = "Linking failed. Please try again.",
        ["settings.supporter_retry"] = "Try again",
        ["settings.supporter_unavailable"] = "Supporter linking is currently unavailable. Please check back later.",
        ["settings.supporter_link_expired"] = "The link request timed out. Please try again.",

        // added after update 1.5.1
        ["settings.supporter_linked"] = "Linked",
        ["settings.supporter_title"] = "Become a supporter",
        ["settings.supporter_intro"] = "You can support the project financially through our Patreon. Every feature in AetherLove is free for everyone, and nothing is or ever will be locked behind a paywall. Supporters just get a heartfelt thank-you: gentler limits and a few sparkly extras, our way of saying we couldn't do this without you.",
        ["settings.supporter_perks_header"] = "Supporter perks",
        ["settings.supporter_perk_photos_title"] = "More room to shine",
        ["settings.supporter_perk_photos_body"] = "Up to 5 extra profile photos, plus 2 more for every RP character.",
        ["settings.supporter_perk_superlike_title"] = "Superlike",
        ["settings.supporter_perk_superlike_body"] = "Let someone know they really caught your eye. They get a notification that you Superliked them, and if they like you back, you match right away.",
        ["settings.supporter_perk_rewinds_title"] = "5 rewinds a day",
        ["settings.supporter_perk_rewinds_body"] = "Swiped too soon? Rewind up to 5 times a day instead of just once.",
        ["settings.supporter_perk_analytics_title"] = "Deeper insights",
        ["settings.supporter_perk_analytics_body"] = "Extra analytics and statistics on who loves you and how your profile really performs.",
        ["settings.supporter_perk_colors_title"] = "Living colors",
        ["settings.supporter_perk_colors_body"] = "Your name shimmers like a rainbow, slowly cycling through colours that are impossible to scroll past.",
        ["settings.supporter_perk_badge_title"] = "Supporter mark",
        ["settings.supporter_perk_badge_body"] = "A Supporter tag and a little star beside your name. That quiet flex, earned.",
        ["settings.supporter_how_heading"] = "How it works",
        ["settings.supporter_how_intro"] = "We offer three tiers at different prices so you can pick what you're comfortable giving, and every tier unlocks the exact same perks. No tier gets more than another.",
        ["settings.supporter_step1_title"] = "1. Subscribe on Patreon",
        ["settings.supporter_step1_body"] = "Create a Patreon account and subscribe to any of our supporter tiers, they all unlock the same rewards.",
        ["settings.supporter_step2_title"] = "2. Link it to AetherLove",
        ["settings.supporter_step2_body"] = "Link your Patreon account by clicking the button below. If you have an active membership, your account will become premium right away.",
        ["settings.supporter_become"] = "Become a Supporter on Patreon",
        ["settings.supporter_data_note"] = "We only store your Patreon user id and whether you're a member of our campaign. We never store your name, email or social accounts.",
        ["settings.sup_learn_title"] = "Support AetherLove",
        ["settings.sup_learn_body"] = "This player supports AetherLove. Want to see what supporting the project gets you? Extra photos, superlikes, name styles, bonus statistics and more.",
        ["settings.sup_learn_more"] = "More info",

        ["settings.sup_thanks_title"] = "You're a Supporter!",
        ["settings.sup_thanks_sub"] = "Thank you for supporting AetherLove!",
        ["settings.sup_thanks_body"] = "Your support keeps the servers running and the love flowing. Enjoy your new perks!",
        ["settings.sup_thanks_continue"] = "Continue",
    };
}
