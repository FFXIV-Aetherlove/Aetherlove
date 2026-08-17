using System;

namespace AetherOS.Apps.Notes;

/// <summary>One stored note. Plain properties so the app-storage JSON round trip needs no converter.</summary>
public sealed class Note
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public bool Pinned { get; set; }
    public int ColorIndex { get; set; }
}
