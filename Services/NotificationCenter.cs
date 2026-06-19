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

    /// <summary>True from when a moderation warning arrives while the phone is minimised/closed until the
    /// user next opens the phone (which routes them to the acknowledge screen).</summary>
    public bool HasPendingWarning { get; private set; }

    /// <summary>Raised when a warning arrives while the phone isn't open, so the mini phone can react.</summary>
    public event Action? PendingWarningRaised;

    public void RaisePendingWarning()
    {
        HasPendingWarning = true;
        PendingWarningRaised?.Invoke();
    }

    public void ClearPendingWarning() => HasPendingWarning = false;

    /// <summary>True from when a news item is published while the phone is minimised/closed until the user
    /// next opens the phone (which routes them to the news screen).</summary>
    public bool HasPendingNews { get; private set; }

    /// <summary>Raised when news arrives while the phone isn't open, so the mini phone can react.</summary>
    public event Action? PendingNewsRaised;

    public void RaisePendingNews()
    {
        HasPendingNews = true;
        PendingNewsRaised?.Invoke();
    }

    public void ClearPendingNews() => HasPendingNews = false;
}
