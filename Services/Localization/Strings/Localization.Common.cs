namespace AetherLove.Services.Localization;

internal static class CommonEn
{
    public static readonly System.Collections.Generic.Dictionary<string, string> Strings = new()
    {
        // Generic
        ["common.ok"] = "OK",
        ["common.confirm"] = "Confirm",
        ["common.cancel"] = "Cancel",
        ["common.loading"] = "Loading…",
        ["common.try_again"] = "Try Again",
        ["common.i_understand"] = "I understand",
        ["common.sign_out"] = "Sign out",
        ["common.got_it"] = "Got it!",
        ["common.moderator_notes_label"] = "Moderator notes",
        ["common.server_unreachable_detail"] = "Couldn't reach the server: {0}",

        // Banned screen
        ["common.banned_title"] = "Account banned",
        ["common.banned_body"] = "Your AetherLove account has been banned. You can no longer use the service.",
        ["common.banned_reason_label"] = "Reason",
        ["common.banned_uninstall_hint"] = "You can close this window and uninstall the plugin at any time.",

        // Outdated-plugin screen
        ["common.outdated_title"] = "Update required",
        ["common.outdated_body"] = "You are using an outdated version of AetherLove. The server no longer supports this version, so the plugin can't connect.",
        ["common.outdated_hint"] = "Please update the plugin in Dalamud's plugin installer, then reopen AetherLove.",

        // Offline screen
        ["common.offline_title"] = "AetherLove is offline",
        ["common.offline_body"] = "We can't reach the AetherLove servers right now. The app needs a live connection to browse, match, and chat, so it's paused until we're back online.",
        ["common.offline_reconnecting"] = "Reconnecting…",
        ["common.offline_keep_trying"] = "We'll keep trying automatically.",

        // Passphrase unlock screen
        ["common.passphrase_title"] = "Enter your encryption passphrase",
        ["common.passphrase_intro"] = "We recognise this account, but this device doesn't have your chat key yet. Enter the passphrase you set on your first device to unlock your chat history.",
        ["common.passphrase_forgot"] = "Forgot your passphrase? There is no recovery, but you can sign out below and create a fresh account. Your chat history with this account will be lost.",
        ["common.passphrase_bundle_load_failed"] = "Couldn't load encryption bundle from server.",
        ["common.passphrase_empty"] = "Please enter your passphrase.",
        ["common.passphrase_incorrect"] = "Incorrect passphrase. Try again.",
        ["common.passphrase_unlock_failed"] = "Unlock failed: {0}",
        ["common.unlock"] = "Unlock",
        ["common.unlocking"] = "Unlocking…",

        // Warning acknowledge screen
        ["common.warnings_heading_one"] = "You have a moderation warning",
        ["common.warnings_heading_many"] = "You have {0} moderation warnings",
        ["common.warnings_body"] = "Please read the following warning(s) from the moderation team. Repeat offenses can result in account suspension.",
        ["common.warnings_submit_error"] = "Couldn't reach the server: {0}. Tap to retry.",
        ["common.acknowledging"] = "Acknowledging…",

        // Photo moderation
        ["common.nsfw_decl_unselected"] = "select an option below",
        ["common.nsfw_decl_sfw"] = "this picture is SFW",
        ["common.nsfw_decl_nsfw"] = "this picture is NSFW",
        ["common.lalafell_nsfw_title"] = "NSFW not available",
        ["common.lalafell_nsfw_body"] = "We do not allow NSFW pictures of Lalafell characters. Because Lalafells are child-like in appearance, we apply this policy uniformly to every Lalafell account and make no case-by-case exceptions.\n\nYour photo has been set back to SFW. If this photo isn't safe-for-work, please remove it and upload a different one.",
        ["common.undeclared_photo_title"] = "Declaration required",
        ["common.undeclared_photo_body"] = "You must select whether your other picture is SFW or NSFW in the selection box before uploading another.",

        // Changelog window
        ["common.changelog_window_title"] = "AetherLove — What's New",
        ["common.whats_new"] = "What's New",
        ["common.changelog_empty"] = "No changelog entries available.",
        ["common.changelog_latest"] = "Latest",
        ["common.changelog_important"] = "Important",
        ["common.changelog_new_features"] = "New features",
        ["common.changelog_bug_fixes"] = "Bug fixes",

        // Rate limit modal
        ["common.rate_limit_title"] = "Slow down",
        ["common.rate_limit_noun_profile"] = "profile",
        ["common.rate_limit_noun_images"] = "images",
        ["common.rate_limit_body"] = "You can only change your {0} {1} times per hour. Please try again in {2}.",
        ["common.rate_limit_retry_moment"] = "a moment",
        ["common.rate_limit_retry_one_second"] = "1 second",
        ["common.rate_limit_retry_seconds"] = "{0} seconds",
        ["common.rate_limit_retry_one_minute"] = "1 minute",
        ["common.rate_limit_retry_minutes"] = "{0} minutes",

        // Bottom navigation bar
        ["common.nav_swipe"] = "Swipe",
        ["common.nav_matches"] = "Matches",
        ["common.nav_profile"] = "Profile",
        ["common.nav_settings"] = "Settings",
        ["common.nav_minimize"] = "Minimize",

        // Emoji picker
        ["common.emoji_search_hint"] = "Search emoji...",
        ["common.emoji_none_found"] = "No emoji found.",

        // Close-plugin confirmation modal
        ["common.close_plugin_tooltip"] = "Close AetherLove",
        ["common.close_plugin_title"] = "Close AetherLove?",
        ["common.close_plugin_body"] = "This just hides the window. You'll stay connected and still receive new matches and messages while the plugin is enabled.\n\nReopen the window any time by typing {0} in chat.",
        ["common.close_plugin_tip"] = "Tip: use the Minimize button at the bottom instead to keep the small floating bubble visible with its notification badge.",
        ["common.close"] = "Close",

        // Save-error modal
        ["common.save_error_title"] = "Something went wrong",
        ["common.save_error_intro"] = "We couldn't save your changes:",
        ["common.save_error_report"] = "If this keeps happening, please report the bug on our Discord.",
        ["common.save_error_unknown"] = "An unexpected error occurred.",

        // Image requirements modal
        ["common.img_requirements_title"] = "Image can't be used",
        ["common.img_invalid"] = "That file isn't a valid image, or its format isn't supported.",
        ["common.img_too_small"] = "That image is only {0}×{1}px, which is too small.",
        ["common.img_requirements_sizes"] = "Avatars need at least {0}×{1}px and profile photos at least {2}×{3}px. Please choose a larger image.",

        // Image crop window
        ["common.loading_image"] = "Loading image...",
        ["common.use_this_crop"] = "Use this crop",
    };
}
