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
    byte[] PortraitWebp,
    Guid[] FlairIds,
    // Supporter cosmetics; the server sends None/false unless the profile currently holds the flag.
    NameStyle NameStyle = NameStyle.None,
    bool IsSupporter = false,
    // True when this card was injected because its profile superliked the caller.
    bool SuperlikedYou = false);

/// <summary>One slot's worth of candidates plus the next-slot timestamp.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record MatchDeckDto(
    DeckCardDto[] Cards,
    DateTimeOffset NextPullAtUtc,
    int RemainingInSlot,
    bool NoPoolForPreferences = false,
    int ReswipesRemaining = 0,
    int SuperlikesRemaining = 0,
    int SuperlikesPerDay = 0);

/// <summary>Server's response to a single swipe.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record SwipeResultDto(
    bool IsMatch,
    Guid? MatchedProfileId);

/// <summary>Server's response to an undo-last-swipe (reswipe), carrying the caller's updated daily allowance.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record ReswipeResultDto(
    int ReswipesRemaining);

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
