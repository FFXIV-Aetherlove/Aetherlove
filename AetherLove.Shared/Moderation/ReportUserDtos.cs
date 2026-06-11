using System;
using MessagePack;

namespace AetherLove.Shared.Moderation;

/// <summary>One row in the reporter's plaintext conversation snapshot.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record ConversationSnapshotEntry(
    bool FromMe,
    string Text,
    DateTimeOffset SentAtUtc);

/// <summary>Plugin → server payload for a user report.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record ReportUserRequest(
    Guid ReportedProfileId,
    string Reason,
    bool IncludeConversation,
    ConversationSnapshotEntry[]? ConversationSnapshot);
