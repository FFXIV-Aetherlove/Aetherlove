namespace AetherLove.Services.Localization;

internal static class HubErrorsEn
{
    public static readonly System.Collections.Generic.Dictionary<string, string> Strings = new()
    {
        ["huberror.generic"] = "An unexpected server error occurred.",
        ["huberror.generic_detail"] = "An error has occurred: {0}",
        ["huberror.invalid_request"] = "The server rejected the request. If this keeps happening, please update the plugin.",
        ["huberror.unauthenticated"] = "Your session is no longer valid. Please log in again.",
        ["huberror.banned"] = "Your account has been banned.",
        ["huberror.rate_limited"] = "You're doing that too often. Please try again shortly.",
        ["huberror.profile_not_found"] = "Profile not found.",
        ["huberror.profile_not_visible"] = "This profile is not available.",
        ["huberror.deck_expired"] = "This profile is no longer in your deck. Refresh your deck and try again.",
        ["huberror.no_active_match"] = "You are no longer matched with this player.",
        ["huberror.peer_keys_missing"] = "This user has not set up E2E encryption yet and can't chat, please try again later.",
        ["huberror.key_bundle_exists"] = "Encryption keys are already set up for this account.",
        ["huberror.message_too_large"] = "This message is too long to send.",
        ["huberror.bio_too_long"] = "Your bio exceeds the {0}-character limit.",
        ["huberror.lalafell_erp"] = "Adult roleplay is not available for Lalafell characters.",
        ["huberror.lalafell_nsfw"] = "NSFW features are not available for Lalafell characters.",
        ["huberror.lalafell_nsfw_photo"] = "NSFW photos are not available for Lalafell characters.",
        ["huberror.nsfw_disable_blocked"] = "Remove your NSFW photos and turn off 18+ roleplay before disabling NSFW.",
        ["huberror.img_too_large"] = "Image is too large ({0} MB). Max is {1} MB.",
        ["huberror.img_dimensions_too_large"] = "Image is too large ({0}×{1}). The longest side may be {2}px.",
        ["huberror.img_crop_too_small"] = "The crop area is too small (min {0}px per side).",
        ["huberror.img_decode_failed"] = "Could not read the image. Supported formats: PNG, JPEG, WebP, GIF.",
        ["huberror.img_payload_invalid"] = "The photo could not be uploaded. Please pick the image again.",
        ["huberror.report_self"] = "You cannot report yourself.",
        ["huberror.report_reason_required"] = "Please describe the problem.",
        ["huberror.report_reason_too_long"] = "The reason is too long (max {0} characters).",
        ["huberror.report_target_gone"] = "That profile no longer exists.",
        ["huberror.report_duplicate"] = "You've already reported this user recently. Our team is reviewing it.",
        ["huberror.feedback_required"] = "Please enter a message before sending.",
        // added after update (1.3.1)
        ["huberror.reswipe_nothing_to_undo"] = "There's nothing to undo.",
        ["huberror.reswipe_already_matched"] = "You cannot reswipe on a matched profile.",
        ["huberror.reswipe_quota_exhausted"] = "You've used your reswipe for today.",
        ["huberror.superlike_quota_exhausted"] = "You're out of superlikes for today.",

        // added after update 1.4.3
        ["huberror.character_limit_reached"] = "You can have at most {0} RP characters.",
        ["huberror.character_name_invalid"] = "Character names must be 3-50 characters.",
        ["huberror.character_not_found"] = "That character no longer exists. Reload and try again.",

        // added after update 1.5.0
        ["huberror.patreon_disabled"] = "Patreon linking is currently unavailable.",
        ["huberror.patreon_already_linked"] = "A Patreon account is already linked to your profile.",
        ["huberror.patreon_not_linked"] = "No Patreon account is linked to your profile.",
        ["huberror.patreon_account_taken"] = "That Patreon account is already linked to another AetherLove account.",
        ["huberror.patreon_link_failed"] = "We couldn't complete the Patreon link. Please try again.",
        ["huberror.places_disabled"] = "Places is currently unavailable.",
        ["huberror.venue_not_found"] = "This venue no longer exists.",
        ["huberror.venue_limit_reached"] = "You've reached the limit of {0} venues.",
        ["huberror.venue_name_invalid"] = "The venue name must be 3 to 60 characters.",
        ["huberror.venue_description_too_long"] = "The description exceeds the {0}-character limit.",
        ["huberror.venue_times_invalid"] = "One of the opening times is invalid.",
        ["huberror.venue_times_too_many"] = "A venue can have at most {0} opening times.",
        ["huberror.venue_review_own"] = "You can't review your own venue.",
        ["huberror.venue_review_too_long"] = "Your review exceeds the {0}-character limit.",
        ["huberror.venue_review_rating_invalid"] = "Pick a rating from 1 to 5 stars.",
        ["huberror.venue_rsvp_invalid"] = "That opening is no longer available for RSVPs.",
    };
}
