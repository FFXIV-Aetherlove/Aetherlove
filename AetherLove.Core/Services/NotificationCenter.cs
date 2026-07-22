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

    /// <summary>OS-notification tag for a match chat, shared by the poster and the dismiss-on-open path.</summary>
    public static string ChatTag(Guid peerProfileId) => $"love:chat:{peerProfileId:N}";

    public event Action? UnreadChatMessageArrived;

    public void NotifyUnreadChatMessageArrived() => UnreadChatMessageArrived?.Invoke();

    public event Action? DeckRefreshRequested;

    public void NotifyDeckRefreshRequested() => DeckRefreshRequested?.Invoke();

    /// <summary>Raised after a (re)connect or a moderation edit so profile view/edit caches refetch.</summary>
    public event Action? ProfileCachesInvalidated;

    public void NotifyProfileCachesInvalidated() => ProfileCachesInvalidated?.Invoke();

    /// <summary>Raised ONLY when the acting profile actually changes (a profile switch), never on a reconnect or a
    /// deck push. Surfaces that carry per-profile in-memory state that a plain refresh must preserve (the deck's
    /// card-in-hand and reswipe history) key their full reset off this, not the broader signals above.</summary>
    public event Action? ProfileSwitched;

    public void NotifyProfileSwitched() => ProfileSwitched?.Invoke();

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
}
