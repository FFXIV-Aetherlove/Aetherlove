using System;
using MessagePack;

namespace AetherLove.Shared.Moderation;

/// <summary>One row in the reporter's plaintext conversation snapshot.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record ConversationSnapshotEntry(
    bool FromMe,
    string Text,
    DateTimeOffset SentAtUtc);

/// <summary>Plugin → server payload for a user report. <see cref="ConversationKey"/> is the reporter's derived
/// 32-byte per-conversation AES-GCM key, disclosed (with consent) so the server can decrypt its own stored
/// ciphertext and produce a tamper-evident transcript; the server uses it once and discards it. Trailing-optional
/// so older clients that only send <see cref="ConversationSnapshot"/> keep working.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record ReportUserRequest(
    Guid ReportedProfileId,
    string Reason,
    bool IncludeConversation,
    ConversationSnapshotEntry[]? ConversationSnapshot,
    byte[]? ConversationKey = null);
