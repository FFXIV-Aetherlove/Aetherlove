namespace AetherLove.Shared.EchoVidya;

/// <summary>Shared Echo limits enforced on both the client (UI gating) and the server (authoritative).</summary>
public static class EchoLimits
{
    public const int RoomNameMaxLength = 40;

    /// <summary>Length of the share code a room is joined by.</summary>
    public const int RoomCodeLength = 6;

    /// <summary>People in one room, owner included.</summary>
    public const int MaxMembers = 16;

    /// <summary>The whole queue ships inline in every room snapshot, so this is a payload bound as much as
    /// a product one: roughly 130 bytes an entry, re-sent to each member on every join and reconnect.</summary>
    public const int MaxPlaylistEntries = 500;

    /// <summary>Ceiling on one bulk playlist add. The queue cap is what actually lands; this only stops an
    /// oversized payload reaching the server at all.</summary>
    public const int MaxPlaylistImportItems = MaxPlaylistEntries;

    public const int ChatMaxLength = 300;

    /// <summary>Client-side ring buffer cap: in-room chat is never persisted, older lines are dropped.</summary>
    public const int MaxChatHistory = 200;
}
