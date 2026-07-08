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

/// <summary>One moderation warning issued against a profile.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record WarningDto(
    Guid Id,
    string Reason,
    bool Seen,
    DateTimeOffset CreatedAtUtc);

/// <summary>One informational message a moderator sent to a profile (no warning sentiment).</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record ModeratorMessageDto(
    Guid Id,
    string Body,
    bool Seen,
    DateTimeOffset CreatedAtUtc);

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
    // Trailing default keeps this wire-safe: the map-keyed MessagePack payload simply carries an extra key
    // that older clients ignore. The caller's consent to see NSFW content, used client-side to hide NSFW
    // RP characters from non-consenting viewers.
    bool NsfwEnabled = false);
