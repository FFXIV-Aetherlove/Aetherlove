namespace AetherOS.Sdk;

/// <summary>A piece of content being shared. Staged client-side and delivered to a target app; it never crosses
/// the hub. It carries several representations (Android ClipData style) so each target consumes whichever it
/// understands: <see cref="RefId"/> for a target that live-fetches and deep-links, <see cref="Title"/>/
/// <see cref="Subtitle"/> for a self-contained snapshot, <see cref="LocalPath"/> for a file.</summary>
public sealed record ShareItem
{
    /// <summary>Payload version, for the future cross-plugin IPC boundary.</summary>
    public int V { get; init; } = 1;

    /// <summary>Content-type key; see <see cref="ShareTypes"/>.</summary>
    public required string Type { get; init; }

    /// <summary>Stable server id (guid "D" form) for content the target re-fetches live.</summary>
    public string RefId { get; init; } = "";

    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";

    /// <summary>A local file path (e.g. a photo), the "stream" representation.</summary>
    public string LocalPath { get; init; } = "";

    /// <summary>Type-specific extra JSON, an escape hatch.</summary>
    public string Extras { get; init; } = "";

    /// <summary>The app the content came from; the chooser excludes it and a target may offer "back to source".</summary>
    public string? SourceAppId { get; init; }
}
