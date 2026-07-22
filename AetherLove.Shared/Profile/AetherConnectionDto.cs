using System;
using AetherLove.Shared.News;
using MessagePack;

namespace AetherLove.Shared.Profile;

/// <summary>Mirror of the server's <c>ProfileStatus</c> exposed over the wire.</summary>
public enum ProfileLifecycle : short
{
    Onboarding = 0,
    Active = 1,
    ShadowBanned = 2,
    Banned = 3,
    Deleted = 4,
}

/// <summary>Mirror of the server's <c>ProfileRole</c> exposed over the wire, so the client can tell staff from
/// regular users. The server still authorizes every privileged action itself; this is for display/UX only.</summary>
public enum UserRole : short
{
    User = 0,
    Moderator = 1,
    Admin = 2,
    Translator = 3,
}

/// <summary>One moderation warning that follows the human (account-owned). <see cref="SourceProfileName"/> names
/// the profile it was issued against, if any, so the client can show "regarding your profile X"; null means it
/// was issued at the account level.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record WarningDto(
    Guid Id,
    string Reason,
    bool Seen,
    DateTimeOffset CreatedAtUtc,
    string? SourceProfileName = null);

/// <summary>One informational message a moderator sent to the human (no warning sentiment). Account-owned; see
/// <see cref="SourceProfileName"/> for the profile context.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record ModeratorMessageDto(
    Guid Id,
    string Body,
    bool Seen,
    DateTimeOffset CreatedAtUtc,
    string? SourceProfileName = null);

/// <summary>Snapshot the server hands the plugin right after a hub connection comes up.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record AetherConnectionDto(
    ProfileLifecycle Status,
    string DisplayName,
    UserRole Role,
    string? BanReason,
    WarningDto[] Warnings,
    ModeratorMessageDto[] ModeratorMessages,
    int NewMatchCount,
    bool HasKeyBundle,
    NewsSummaryDto[] UnseenNews,
    // Trailing default for MessagePack wire-safety; player consent for NSFW visibility.
    bool NsfwEnabled = false,
    bool IsVenueOwner = false,
    bool PlacesEnabled = true,
    bool IsSupporter = false,
    Enums.NameStyle NameStyle = Enums.NameStyle.None,
    bool ShowSupporterBadge = true,
    Guid ProfileId = default,
    bool HangoutsEnabled = true);
