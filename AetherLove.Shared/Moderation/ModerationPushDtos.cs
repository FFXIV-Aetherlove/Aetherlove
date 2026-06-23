using System;
using AetherLove.Shared.Profile;
using MessagePack;

namespace AetherLove.Shared.Moderation;

/// <summary>Push to the affected user when a moderator issues a new warning.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record WarningIssuedPushDto(
    WarningDto Warning);

/// <summary>Push to the affected user when a moderator sends them an informational message.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record ModeratorMessageIssuedPushDto(
    ModeratorMessageDto Message);

/// <summary>Push to the affected user when a moderator bans the account.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record AccountBannedPushDto(
    string? Reason);
