namespace AetherLove.Shared.Matching;

/// <summary>Direction of a single swipe action. Wire + storage values, append-only.</summary>
public enum SwipeDirection : short
{
    Pass = 1,
    Like = 2,

    /// <summary>Supporter-only like variant: notifies the recipient, who can like back for an instant
    /// match. Counts as a Like everywhere reciprocity is checked.</summary>
    Superlike = 3,
}
