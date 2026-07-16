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

    public event Action? UnreadChatMessageArrived;

    public void NotifyUnreadChatMessageArrived() => UnreadChatMessageArrived?.Invoke();

    public event Action? DeckRefreshRequested;

    public void NotifyDeckRefreshRequested() => DeckRefreshRequested?.Invoke();

    /// <summary>Set when a warning arrives while the phone is closed; cleared on the next phone open.</summary>
    public bool HasPendingWarning { get; private set; }

    public event Action? PendingWarningRaised;

    public void RaisePendingWarning()
    {
        HasPendingWarning = true;
        PendingWarningRaised?.Invoke();
    }

    public void ClearPendingWarning() => HasPendingWarning = false;

    /// <summary>Like <see cref="HasPendingWarning"/>, but deliberately raises no event; moderator messages surface in fewer places.</summary>
    public bool HasPendingModeratorMessage { get; private set; }

    public void RaisePendingModeratorMessage() => HasPendingModeratorMessage = true;

    public void ClearPendingModeratorMessage() => HasPendingModeratorMessage = false;

    /// <summary>Set when news publishes while the phone is closed; cleared on the next phone open.</summary>
    public bool HasPendingNews { get; private set; }

    public event Action? PendingNewsRaised;

    public void RaisePendingNews()
    {
        HasPendingNews = true;
        PendingNewsRaised?.Invoke();
    }

    public void ClearPendingNews() => HasPendingNews = false;
}
