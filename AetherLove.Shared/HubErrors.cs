using System;
using System.Globalization;
using System.Text;

namespace AetherLove.Shared;

/// <summary>
/// Machine-readable protocol for user-facing hub errors. The server throws
/// <c>HubException(HubErrors.Format(code, args))</c>, producing <c>AL_ERR|code|arg1|...</c>;
/// the plugin parses the payload out of the (SignalR-wrapped) message and localizes it via the
/// <c>huberror.&lt;code&gt;</c> string key. Unknown codes render as a generic error, so adding a
/// code server-side is always safe for older clients. <c>RATE_LIMITED</c> and
/// <c>API_VERSION_MISMATCH</c> predate this protocol and keep their own sentinels.
/// </summary>
public static class HubErrors
{
    public const string Sentinel = "AL_ERR|";

    public const string InvalidRequest = "invalid_request";
    public const string Unauthenticated = "unauthenticated";
    public const string Banned = "banned";
    /// <summary>The whole account is banned (AetherAccount.DisabledAtUtc): every non-info hub method aborts with this,
    /// and the shell gates all server-backed apps off AetherAccountInfoDto.AccountDisabled.</summary>
    public const string AccountBanned = "account_banned";
    public const string RateLimited = "rate_limited";

    public const string ProfileNotFound = "profile_not_found";
    public const string ProfileNotVisible = "profile_not_visible";
    /// <summary>The target profile sits past the account's allowance (supporter lapsed); selecting it is refused.</summary>
    public const string ProfileLocked = "profile_locked";
    /// <summary>The account is at its profile allowance; creating another needs supporter status (or is capped).</summary>
    public const string ProfileLimitReached = "profile_limit_reached";
    public const string DeckExpired = "deck_expired";
    public const string NoActiveMatch = "no_active_match";
    public const string PeerKeysMissing = "peer_keys_missing";
    public const string KeyBundleExists = "key_bundle_exists";
    public const string MessageTooLarge = "message_too_large";

    /// <summary>Arg 0: max visible characters.</summary>
    public const string BioTooLong = "bio_too_long";
    public const string LalafellErp = "lalafell_erp";
    public const string LalafellNsfw = "lalafell_nsfw";
    public const string LalafellNsfwPhoto = "lalafell_nsfw_photo";
    public const string NsfwDisableBlocked = "nsfw_disable_blocked";

    /// <summary>Args: actual MB, max MB.</summary>
    public const string ImgTooLarge = "img_too_large";
    /// <summary>Args: width, height, max dimension px.</summary>
    public const string ImgDimensionsTooLarge = "img_dimensions_too_large";
    /// <summary>Arg 0: min crop px per side.</summary>
    public const string ImgCropTooSmall = "img_crop_too_small";
    public const string ImgDecodeFailed = "img_decode_failed";
    public const string ImgPayloadInvalid = "img_payload_invalid";

    public const string ReportSelf = "report_self";
    public const string ReportReasonRequired = "report_reason_required";
    /// <summary>Arg 0: max characters.</summary>
    public const string ReportReasonTooLong = "report_reason_too_long";
    public const string ReportTargetGone = "report_target_gone";
    public const string ReportDuplicate = "report_duplicate";
    public const string FeedbackRequired = "feedback_required";

    public const string ReswipeNothingToUndo = "reswipe_nothing_to_undo";
    public const string ReswipeAlreadyMatched = "reswipe_already_matched";
    public const string ReswipeQuotaExhausted = "reswipe_quota_exhausted";
    public const string SuperlikeQuotaExhausted = "superlike_quota_exhausted";

    /// <summary>Arg 0: the caller's max RP characters.</summary>
    public const string CharacterLimitReached = "character_limit_reached";
    public const string CharacterNameInvalid = "character_name_invalid";
    public const string CharacterNotFound = "character_not_found";

    public const string PlacesDisabled = "places_disabled";
    public const string VenueNotFound = "venue_not_found";
    /// <summary>Arg 0: the caller's max venues.</summary>
    public const string VenueLimitReached = "venue_limit_reached";
    public const string VenueNameInvalid = "venue_name_invalid";
    /// <summary>Arg 0: max visible characters.</summary>
    public const string VenueDescriptionTooLong = "venue_description_too_long";
    public const string VenueTimesInvalid = "venue_times_invalid";
    /// <summary>Arg 0: max opening-time rules per venue.</summary>
    public const string VenueTimesTooMany = "venue_times_too_many";
    public const string VenueReviewOwn = "venue_review_own";
    /// <summary>Arg 0: max visible characters.</summary>
    public const string VenueReviewTooLong = "venue_review_too_long";
    public const string VenueReviewRatingInvalid = "venue_review_rating_invalid";
    public const string VenueRsvpInvalid = "venue_rsvp_invalid";

    public const string HangoutsDisabled = "hangouts_disabled";
    public const string HangoutNotFound = "hangout_not_found";
    public const string HangoutAlreadyActive = "hangout_already_active";
    public const string HangoutRsvpOwn = "hangout_rsvp_own";
    public const string HangoutTimesInvalid = "hangout_times_invalid";
    /// <summary>Arg 0: max visible characters.</summary>
    public const string HangoutDescriptionTooLong = "hangout_description_too_long";

    public const string LevemetesDisabled = "levemetes_disabled";
    public const string LeveNotFound = "leve_not_found";
    public const string LeveInvalid = "leve_invalid";
    /// <summary>Arg 0: the live-ad cap.</summary>
    public const string LeveLimitReached = "leve_limit_reached";
    /// <summary>Arg 0: max visible characters.</summary>
    public const string LeveTooLong = "leve_too_long";
    public const string LeveImageRejected = "leve_image_rejected";
    public const string LeveReviewOwn = "leve_review_own";
    /// <summary>Arg 0: max visible characters.</summary>
    public const string LeveReviewTooLong = "leve_review_too_long";
    public const string LeveReviewRatingInvalid = "leve_review_rating_invalid";
    public const string LeveReviewsDisabled = "leve_reviews_disabled";
    public const string LeveRenewTooSoon = "leve_renew_too_soon";

    public const string HolidayMessageTooLong = "holiday_message_too_long";
    public const string HolidayMessageInvalid = "holiday_message_invalid";

    public const string MessengerDisabled = "messenger_disabled";
    public const string MsgrCodeNotFound = "msgr_code_not_found";
    public const string MsgrSelf = "msgr_self";
    public const string MsgrAlreadyContact = "msgr_already_contact";
    public const string MsgrRequestPending = "msgr_request_pending";
    /// <summary>Arg 0: the contact cap.</summary>
    public const string MsgrContactLimit = "msgr_contact_limit";
    /// <summary>Arg 0: the outstanding-request cap.</summary>
    public const string MsgrPendingLimit = "msgr_pending_limit";
    public const string MsgrNotContact = "msgr_not_contact";
    /// <summary>Arg 0: the created-groups cap.</summary>
    public const string MsgrGroupLimit = "msgr_group_limit";
    /// <summary>Arg 0: the group size cap.</summary>
    public const string MsgrGroupFull = "msgr_group_full";
    public const string MsgrNotOwner = "msgr_not_owner";
    public const string MsgrNotMember = "msgr_not_member";
    public const string MsgrGroupNameInvalid = "msgr_group_name_invalid";
    public const string MsgrAddsDisabled = "msgr_adds_disabled";
    public const string MsgrKeysMissing = "msgr_keys_missing";
    /// <summary>An uploaded image was rejected by automated moderation (CSAM).</summary>
    public const string MsgrImageRejected = "msgr_image_rejected";
    /// <summary>The account's concurrent image-storage quota is full.</summary>
    public const string MsgrStorageFull = "msgr_storage_full";
    public const string MsgrImageTooLarge = "msgr_image_too_large";
    public const string MsgrImageInvalid = "msgr_image_invalid";

    public const string PatreonDisabled = "patreon_disabled";
    public const string PatreonAlreadyLinked = "patreon_already_linked";
    public const string PatreonNotLinked = "patreon_not_linked";
    /// <summary>The Patreon account is already linked to a different AetherLove profile.</summary>
    public const string PatreonAccountTaken = "patreon_account_taken";
    /// <summary>Generic OAuth exchange / identity read failure during linking.</summary>
    public const string PatreonLinkFailed = "patreon_link_failed";

    /// <summary>Builds the wire payload. Args are serialized invariant-culture; they must not contain '|'.</summary>
    public static string Format(string code, params object?[] args)
    {
        if (args is null || args.Length == 0)
        {
            return Sentinel + code;
        }
        var sb = new StringBuilder(Sentinel).Append(code);
        foreach (var arg in args)
        {
            sb.Append('|').Append(Convert.ToString(arg, CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }
}
