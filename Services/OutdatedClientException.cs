using System;
using AetherLove.Shared;
using Microsoft.AspNetCore.SignalR;

namespace AetherLove.Services;

/// <summary>Thrown from <see cref="Hub.AetherLoveHubClient"/> when the server rejects this plugin's
/// <see cref="ApiVersion.Current"/>. Surfaced as a terminal "update the plugin" screen.</summary>
public sealed class OutdatedClientException : Exception
{
    /// <summary>The version the server expects, if it was included in the payload.</summary>
    public int? ServerVersion { get; }

    public OutdatedClientException(int? serverVersion)
        : base("Plugin is outdated; the server requires a newer version.")
    {
        ServerVersion = serverVersion;
    }

    /// <summary>Returns a typed exception if the hub error matches the server's
    /// <c>API_VERSION_MISMATCH|serverVersion</c> sentinel, else <c>null</c>.</summary>
    public static OutdatedClientException? TryParse(HubException ex)
    {
        var msg = ex.Message;
        if (string.IsNullOrEmpty(msg))
        {
            return null;
        }
        // SignalR prefixes with "HubException: " in newer versions; match the sentinel anywhere.
        var idx = msg.IndexOf(ApiVersion.MismatchError, StringComparison.Ordinal);
        if (idx < 0)
        {
            return null;
        }
        var parts = msg.Substring(idx).Split('|');
        int? serverVersion = parts.Length >= 2 && int.TryParse(parts[1], out var v) ? v : null;
        return new OutdatedClientException(serverVersion);
    }
}
