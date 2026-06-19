using System;
using MessagePack;

namespace AetherLove.Shared.Diagnostics;

/// <summary>Server-sourced fields for the in-game debug/support screen: the caller's partial account id, the IP
/// and transport the server sees, the server clock, and the sample image encoded to BOTH JPEG and WebP (sent
/// regardless of OS) so the client can render each and reveal which format it can decode — the "gray box"
/// diagnostic.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record DebugInfoDto(
    string PartialAccountId,
    string IpAddress,
    string Transport,
    DateTimeOffset ServerTimeUtc,
    byte[] SampleJpeg,
    byte[] SampleWebp);
