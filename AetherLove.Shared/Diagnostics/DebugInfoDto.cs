using System;
using MessagePack;

namespace AetherLove.Shared.Diagnostics;

/// <summary>Debug info including caller id, server IP/transport/time, and sample images in JPEG and WebP for format detection.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record DebugInfoDto(
    string PartialAccountId,
    string IpAddress,
    string Transport,
    DateTimeOffset ServerTimeUtc,
    byte[] SampleJpeg,
    byte[] SampleWebp);
