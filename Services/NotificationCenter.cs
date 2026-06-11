using System;

namespace AetherLove.Services;

/// <summary>Unread message and new-match counts for the badge UI, plus cross-screen pub/sub signals.</summary>
public sealed class NotificationCenter
{
    public int UnreadChatMessages { get; set; }
    public int NewMatches { get; set; }
    public int TotalBadge => UnreadChatMessages + NewMatches;

    /// <summary>Peer ID of the active chat (or <see cref="Guid.Empty"/>); used to suppress self-notifications.</summary>
    public Guid ActiveChatPeerId { get; set; }

    /// <summary>Raised when an incoming chat message increments <see cref="UnreadChatMessages"/>.</summary>
    public event Action? UnreadChatMessageArrived;

    public void NotifyUnreadChatMessageArrived() => UnreadChatMessageArrived?.Invoke();

    /// <summary>Raised when the server asks the client to re-fetch its deck.</summary>
    public event Action? DeckRefreshRequested;

    public void NotifyDeckRefreshRequested() => DeckRefreshRequested?.Invoke();
}
