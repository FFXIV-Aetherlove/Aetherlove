namespace AetherLove.Shared.Together;

/// <summary>Append-only: values are stored in the database.</summary>
public enum TogetherPartyStatus : short
{
    Live = 0,
    Ended = 1,
}

/// <summary>Why a party closed. Append-only: values are stored in the database.</summary>
public enum TogetherEndReason : short
{
    HostEnded = 0,
    HostLeft = 1,
    Empty = 2,
    Moderation = 3,
}
