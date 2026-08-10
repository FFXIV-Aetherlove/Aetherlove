using System;

namespace AetherLove.Services;

/// <summary>Carries the just-matched peer between <c>DeckScreen</c> and <c>MatchScreen</c>.</summary>
public sealed class PendingMatchContext
{
    public Guid PeerProfileId { get; private set; }
    public string PeerDisplayName { get; private set; } = string.Empty;
    public byte[] PeerAvatarWebp { get; private set; } = [];
    public string? PeerFrameRef { get; private set; }
    public bool HasPending => PeerProfileId != Guid.Empty;

    public void Set(Guid peerId, string displayName, byte[] avatarWebp, string? peerFrameRef = null)
    {
        PeerProfileId = peerId;
        PeerDisplayName = displayName;
        PeerAvatarWebp = avatarWebp;
        PeerFrameRef = peerFrameRef;
    }

    public void Clear()
    {
        PeerProfileId = Guid.Empty;
        PeerDisplayName = string.Empty;
        PeerAvatarWebp = [];
        PeerFrameRef = null;
    }
}
