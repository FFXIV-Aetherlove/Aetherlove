namespace AetherLove.Shared.EchoVidya;

/// <summary>Lifecycle of an Echo room. Append-only: values are stored in the database and are never
/// renumbered.</summary>
public enum EchoRoomStatus : short
{
    Live = 0,
    Ended = 1,
}

/// <summary>Why an Echo room closed, as stored and as pushed to its members. Append-only: values are
/// stored in the database and are never renumbered.</summary>
public enum EchoEndReason : short
{
    OwnerEnded = 0,
    OwnerLeft = 1,
    Empty = 2,
    Moderation = 3,
}
