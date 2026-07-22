using System;

namespace AetherOS.Sdk;

/// <summary>A notification in the OS notification center.</summary>
public sealed class OsNotification
{
    public Guid Id { get; } = Guid.NewGuid();
    public required string AppId { get; init; }
    public required string Title { get; init; }
    public required string Body { get; init; }
    public DateTime PostedAtUtc { get; init; } = DateTime.UtcNow;

    /// <summary>Optional tap action; when null the shell opens the posting app.</summary>
    public Action? OnTap { get; init; }

    /// <summary>Optional grouping key. Reposting with the same tag replaces the prior one, and the app can
    /// clear it globally (center, widget, badge) once the underlying content is read via
    /// <see cref="IOsShell.DismissByTag"/>.</summary>
    public string? Tag { get; init; }
}
