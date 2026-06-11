using System;
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

/// <summary>One moderation warning issued against a profile.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record WarningDto(
    Guid Id,
    string Reason,
    bool Seen,
    DateTimeOffset CreatedAtUtc);

/// <summary>Snapshot the server hands the plugin right after a hub connection comes up.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record AetherConnectionDto(
    ProfileLifecycle Status,
    string DisplayName,
    string? BanReason,
    string? ModerationNotes,
    WarningDto[] Warnings,
    int NewMatchCount,
    bool HasKeyBundle);
