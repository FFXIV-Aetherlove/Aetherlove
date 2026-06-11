using System;
using AetherLove.Shared.Profile.Enums;
using MessagePack;

namespace AetherLove.Shared.Matching;

/// <summary>One profile card in the deck. Photo bytes are inlined.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record DeckCardDto(
    Guid ProfileId,
    string DisplayName,
    string Bio,
    Race Race,
    Gender Gender,
    Region Region,
    LookingFor LookingForMask,
    ContentInterest ContentInterestMask,
    byte[] AvatarWebp,
    byte[] PortraitWebp);

/// <summary>One slot's worth of candidates plus the next-slot timestamp.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record MatchDeckDto(
    DeckCardDto[] Cards,
    DateTimeOffset NextPullAtUtc,
    int RemainingInSlot,
    bool NoPoolForPreferences = false);

/// <summary>Server's response to a single swipe.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record SwipeResultDto(
    bool IsMatch,
    Guid? MatchedProfileId);

/// <summary>SignalR push to the other side of a fresh match.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record MatchCreatedPushDto(
    Guid MatchId,
    Guid OtherProfileId,
    string OtherDisplayName,
    DateTimeOffset CreatedAtUtc);

/// <summary>SignalR push telling the client to re-fetch its deck (e.g. a moderator reset the user's swipes).</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record DeckRefreshPushDto(string Reason);
