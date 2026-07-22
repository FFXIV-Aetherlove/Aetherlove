using System;
using AetherLove.Shared.Profile;
using MessagePack;

namespace AetherLove.Shared.Moderation;

/// <summary>Push to the affected user when a moderator issues a new warning. Delivered on the account channel;
/// <see cref="ForProfileId"/> is the profile the warning is against.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record WarningIssuedPushDto(
    WarningDto Warning,
    Guid ForProfileId = default);

/// <summary>Push to the affected user when a moderator sends them an informational message.
/// <see cref="ForProfileId"/> is the profile the message is for.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record ModeratorMessageIssuedPushDto(
    ModeratorMessageDto Message,
    Guid ForProfileId = default);

/// <summary>Push to the affected user when a moderator bans one of the account's profiles.
/// <see cref="ForProfileId"/> is the banned profile (a sibling ban must not tear down the whole session).</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record AccountBannedPushDto(
    string? Reason,
    Guid ForProfileId = default);

/// <summary>Push on the account channel when a moderator bans (or unbans) the whole human. The client gates every
/// server-backed app; <see cref="Reason"/> null means the ban was lifted.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record AccountDisabledPushDto(
    string? Reason);
