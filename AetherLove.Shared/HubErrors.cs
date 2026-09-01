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

    /// <summary>The server is up but closed to players (staff excepted). Carries the operator's own notice
    /// as its one argument, which the client shows instead of a generic offline line.</summary>
    public const string ServerClosed = "server_closed";

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
    /// <summary>A watch-party hangout was created without a live Echo room the caller owns, or an ordinary
    /// hangout tried to carry a room.</summary>
    public const string HangoutWatchRoomInvalid = "hangout_watch_room_invalid";

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

    public const string WayfinderDisabled = "wayfinder_disabled";
    public const string WayfinderNoChallenges = "wayfinder_no_challenges";
    public const string WayfinderNoReachable = "wayfinder_no_reachable";
    /// <summary>Arg 0: the daily start cap.</summary>
    public const string WayfinderDailyLimit = "wayfinder_daily_limit";
    public const string WayfinderActiveExists = "wayfinder_active_exists";
    public const string WayfinderNoActive = "wayfinder_no_active";
    public const string WayfinderExpired = "wayfinder_expired";
    public const string WayfinderNotScout = "wayfinder_not_scout";
    /// <summary>Arg 0: the daily submission cap.</summary>
    public const string WayfinderInvalid = "wayfinder_invalid";
    public const string WayfinderImageRejected = "wayfinder_image_rejected";

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

    public const string YapperDisabled = "yapper_disabled";
    public const string YapperNoProfile = "yapper_no_profile";
    public const string YapperBannedFromPosting = "yapper_banned";
    public const string YapperHandleTaken = "yapper_handle_taken";
    public const string YapperHandleInvalid = "yapper_handle_invalid";
    public const string YapperHandleRejected = "yapper_handle_rejected";
    public const string YapperProfileRejected = "yapper_profile_rejected";
    /// <summary>Arg 0: days until the next rename is allowed.</summary>
    public const string YapperRenameTooSoon = "yapper_rename_too_soon";
    public const string YapperBlocked = "yapper_blocked";
    public const string YapNotFound = "yap_not_found";
    /// <summary>Arg 0: max visible characters.</summary>
    public const string YapTooLong = "yap_too_long";
    /// <summary>Arg 0: the image-count cap.</summary>
    public const string YapTooManyImages = "yap_too_many_images";
    /// <summary>An uploaded image was rejected by automated moderation (CSAM).</summary>
    public const string YapImageRejected = "yap_image_rejected";
    public const string YapImageTooLarge = "yap_image_too_large";
    public const string YapImageInvalid = "yap_image_invalid";
    public const string YapEmbedInvalid = "yap_embed_invalid";
    public const string YapEditWindowClosed = "yap_edit_window_closed";
    public const string YapNsfwMismatch = "yap_nsfw_mismatch";
    public const string YapInvalid = "yap_invalid";
    public const string YapDmNotAllowed = "yap_dm_not_allowed";
    public const string YapDmKeysMissing = "yap_dm_keys_missing";

    public const string EchoDisabled = "echo_disabled";
    public const string EchoRoomNotFound = "echo_room_not_found";
    /// <summary>Arg 0: the member cap.</summary>
    public const string EchoRoomFull = "echo_room_full";
    public const string EchoNotMember = "echo_not_member";
    public const string EchoNotOwner = "echo_not_owner";
    /// <summary>The room is host-only: playback and playlist control belong to the owner.</summary>
    public const string EchoHostOnly = "echo_host_only";
    /// <summary>The caller was kicked from this room and cannot rejoin it.</summary>
    public const string EchoKicked = "echo_kicked";
    /// <summary>Arg 0: the playlist cap.</summary>
    public const string EchoPlaylistFull = "echo_playlist_full";
    /// <summary>The caller already owns a live room; ending it is the way to start another.</summary>
    public const string EchoLiveRoomExists = "echo_live_room_exists";
    public const string EchoInvalidVideo = "echo_invalid_video";
    public const string EchoNameInvalid = "echo_name_invalid";

    public const string TogetherDisabled = "together_disabled";
    public const string TogetherPartyNotFound = "together_party_not_found";
    /// <summary>Arg 0: the member cap.</summary>
    public const string TogetherPartyFull = "together_party_full";
    public const string TogetherNotHost = "together_not_host";
    /// <summary>The caller was kicked from this party and cannot rejoin it.</summary>
    public const string TogetherKicked = "together_kicked";
    /// <summary>The caller already hosts a live party; ending it is the way to start another.</summary>
    public const string TogetherLivePartyExists = "together_live_party_exists";
    /// <summary>The caller is already in a party; leaving it is the way to join another.</summary>
    public const string TogetherAlreadyInParty = "together_already_in_party";

    public const string WayfinderRunExists = "wayfinder_run_exists";
    public const string WayfinderRunNotFound = "wayfinder_run_not_found";
    /// <summary>Arg 0: the host's world id. Join refused: the caller's attested world differs.</summary>
    public const string WayfinderRunWrongWorld = "wayfinder_run_wrong_world";
    public const string WayfinderRunNotGathering = "wayfinder_run_not_gathering";
    /// <summary>A hunt needs the host plus at least one joined member.</summary>
    public const string WayfinderRunTooFew = "wayfinder_run_too_few";

    public const string PatreonDisabled = "patreon_disabled";
    public const string PatreonAlreadyLinked = "patreon_already_linked";
    public const string PatreonNotLinked = "patreon_not_linked";
    /// <summary>The Patreon account is already linked to a different AetherLove profile.</summary>
    public const string PatreonAccountTaken = "patreon_account_taken";
    /// <summary>Generic OAuth exchange / identity read failure during linking.</summary>
    public const string PatreonLinkFailed = "patreon_link_failed";

    public const string StoreDisabled = "store_disabled";
    public const string StoreProductNotFound = "store_product_not_found";
    /// <summary>The product (or its category chain, or a bundle constituent) is disabled.</summary>
    public const string StoreProductUnavailable = "store_product_unavailable";
    /// <summary>Arg 0: the total price. Arg 1: the caller's balance.</summary>
    public const string StoreInsufficientSparks = "store_insufficient_sparks";
    /// <summary>Arg 0: the product's per-account limit.</summary>
    public const string StoreLimitReached = "store_limit_reached";
    public const string StoreQuantityInvalid = "store_quantity_invalid";
    /// <summary>Two same-instant checkouts collided twice; the client should simply retry.</summary>
    public const string StoreConflict = "store_conflict";

    /// <summary>The account holds no boost of the kind the target needs.</summary>
    public const string BoostNoneOwned = "boost_none_owned";
    /// <summary>No such venue or ad, or it is not the caller's.</summary>
    public const string BoostTargetNotFound = "boost_target_not_found";
    /// <summary>Arg 0: the maximum days a boost window may reach.</summary>
    public const string BoostCapReached = "boost_cap_reached";
    public const string BoostStyleInvalid = "boost_style_invalid";
    /// <summary>The venue or ad exists but is not on the board yet, so a boost would burn on nothing.</summary>
    public const string BoostTargetNotLive = "boost_target_not_live";

    public const string AetherlingDisabled = "aetherling_disabled";
    /// <summary>The account already has an Aethercore; there is only ever one.</summary>
    public const string AetherlingExists = "aetherling_exists";
    /// <summary>The account has no Aethercore to charge.</summary>
    public const string AetherlingNone = "aetherling_none";
    /// <summary>Arg 0: the price. Arg 1: the caller's balance.</summary>
    public const string AetherlingInsufficient = "aetherling_insufficient";
    /// <summary>Arg 0: whole minutes still to wait.</summary>
    public const string AetherlingGated = "aetherling_gated";
    /// <summary>The core is already at the last stage a charge can reach.</summary>
    public const string AetherlingComplete = "aetherling_complete";
    /// <summary>Asked for something the core has not reached yet: a hatch below the last rung, or a name
    /// before the hatch.</summary>
    public const string AetherlingUnready = "aetherling_unready";
    /// <summary>The name was already chosen; changing it is not free.</summary>
    public const string AetherlingNamed = "aetherling_named";
    /// <summary>Covers empty, too long, and moderation-flagged alike, so the moderator cannot be probed by
    /// watching which names come back with a different answer.</summary>
    public const string AetherlingNameInvalid = "aetherling_name_invalid";
    /// <summary>No crystal of the requested element in the account's inventory.</summary>
    public const string AetherlingNoCrystal = "aetherling_no_crystal";
    /// <summary>A rename was asked for with no Name change in the account's inventory.</summary>
    public const string AetherlingNoRename = "aetherling_no_rename";
    /// <summary>The daily wheel is switched off.</summary>
    public const string AetherlingWheelDisabled = "aetherling_wheel_disabled";
    /// <summary>Today's spin was already used and could not be returned.</summary>
    public const string AetherlingWheelSpun = "aetherling_wheel_spun";
    /// <summary>Arg 0: adult feeds allowed per day. The appetite resets at UTC midnight.</summary>
    public const string AetherlingFull = "aetherling_full";
    /// <summary>The look names an item the account does not own.</summary>
    public const string AetherlingNotOwned = "aetherling_not_owned";

    public const string LumiRaceDisabled = "lumirace_disabled";
    /// <summary>Racing needs a hatched Aetherling.</summary>
    public const string LumiRaceNoPet = "lumirace_no_pet";
    /// <summary>The Aetherling is hatched but has not grown up: racing is adults only.</summary>
    public const string LumiRaceNotAdult = "lumirace_not_adult";
    /// <summary>Arg 0: races allowed per UTC day.</summary>
    /// <summary>Arg 0: whole minutes still to wait.</summary>
    public const string LumiRaceGated = "lumirace_gated";
    /// <summary>No pack with that id belongs to the caller.</summary>
    public const string LumiRacePackNone = "lumirace_pack_none";
    /// <summary>The party already has a race gathering or running.</summary>
    public const string LumiRaceRunExists = "lumirace_run_exists";
    public const string LumiRaceRunNotFound = "lumirace_run_not_found";
    public const string LumiRaceRunNotGathering = "lumirace_not_gathering";
    /// <summary>A party race needs at least two eligible joined members.</summary>
    public const string LumiRaceRunTooFew = "lumirace_too_few";
    /// <summary>The course the caller asked for is not one of the offers on the board.</summary>
    public const string LumiRaceNoOffer = "lumirace_no_offer";

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
