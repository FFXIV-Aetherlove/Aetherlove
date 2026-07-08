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
    public const string RateLimited = "rate_limited";

    public const string ProfileNotFound = "profile_not_found";
    public const string ProfileNotVisible = "profile_not_visible";
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

    /// <summary>Arg 0: the caller's max RP characters.</summary>
    public const string CharacterLimitReached = "character_limit_reached";
    public const string CharacterNameInvalid = "character_name_invalid";
    public const string CharacterNotFound = "character_not_found";

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
