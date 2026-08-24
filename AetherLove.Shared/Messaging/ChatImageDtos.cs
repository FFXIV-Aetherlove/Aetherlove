using System;
using AetherLove.Shared.Profile;
using MessagePack;

namespace AetherLove.Shared.Messaging;

/// <summary>An image attached to a match-chat message. The bytes are NOT end-to-end encrypted, unlike the
/// message body beside them: they are screened for CSAM on upload and can be reported, and neither is
/// possible on something the server cannot read. The caption, if any, stays encrypted.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record ChatImageDto(
    Guid ImageId,
    int Width,
    int Height,
    long ByteSize,
    DateTimeOffset ExpiresAtUtc);

/// <summary>Camera-only: the server rejects anything else, so a picked file cannot reach a match chat even
/// from a tampered client. <see cref="ExpiryHours"/> is snapped to the caller's tier by the server.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record SendChatImageRequest(
    Guid PeerProfileId,
    PhotoUploadDto Image,
    byte[]? CaptionCiphertext = null,
    byte[]? CaptionNonce = null,
    bool FromCamera = false,
    int ExpiryHours = 72,
    Guid? ReplyToMessageId = null);

/// <summary>Push to both peers when an image leaves: owner delete or moderator removal. The expiry sweep
/// stays silent, because clients expire an image on their own clock.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record ChatImageRemovedPushDto(Guid ImageId, Guid ForProfileId = default);
