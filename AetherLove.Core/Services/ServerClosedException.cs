using System;
using AetherLove.Shared;
using Microsoft.AspNetCore.SignalR;

namespace AetherLove.Services;

/// <summary>Thrown when the server is up but closed to players. Distinct from a dropped connection: the hub
/// answered, and it answered with a reason worth showing.</summary>
public sealed class ServerClosedException : Exception
{
    /// <summary>The operator's own wording, or empty when they left it blank.</summary>
    public string Notice { get; }

    public ServerClosedException(string notice)
        : base("The server is closed to players.")
    {
        Notice = notice;
    }

    /// <summary>Parses the server's <c>AL_ERR|server_closed|notice</c> payload, else null.</summary>
    public static ServerClosedException? TryParse(HubException ex)
    {
        var message = ex.Message;
        if (string.IsNullOrEmpty(message))
        {
            return null;
        }
        var idx = message.IndexOf(HubErrors.Sentinel + HubErrors.ServerClosed, StringComparison.Ordinal);
        if (idx < 0)
        {
            return null;
        }
        // The notice is the rest of the payload, pipes and all: an operator writing one is not thinking
        // about the wire format.
        var parts = message[(idx + HubErrors.Sentinel.Length)..].Split('|', 2);
        return new ServerClosedException(parts.Length > 1 ? parts[1].Trim() : string.Empty);
    }
}
