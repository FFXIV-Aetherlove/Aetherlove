using MessagePack;

namespace AetherLove.Shared.Feedback;

/// <summary>What kind of feedback the user is leaving.</summary>
public enum FeedbackKind : byte
{
    Bug = 0,
    Improvement = 1,
    Other = 2,
}

/// <summary>Plugin → server payload for a piece of user feedback.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record SubmitFeedbackRequest(
    FeedbackKind Kind,
    string Message);
