using System;
using System.Globalization;
using Microsoft.AspNetCore.SignalR;

namespace AetherLove.Services;

/// <summary>Typed rate-limit denial thrown from <see cref="Hub.AetherHubContext"/> after
/// parsing a server-side <c>RATE_LIMITED|bucket|max|unixSeconds</c> payload.</summary>
public sealed class RateLimitException : Exception
{
    public string Bucket { get; }
    public int Limit { get; }
    public DateTimeOffset RetryAtUtc { get; }

    public RateLimitException(string bucket, int limit, DateTimeOffset retryAtUtc)
        : base($"Rate limited ({bucket}); retry after {retryAtUtc:O}.")
    {
        Bucket = bucket;
        Limit = limit;
        RetryAtUtc = retryAtUtc;
    }

    public static RateLimitException? TryParse(HubException ex)
    {
        var msg = ex.Message;
        if (string.IsNullOrEmpty(msg))
        {
            return null;
        }
        // SignalR wraps with "HubException: " in newer versions; tolerate both.
        var idx = msg.IndexOf("RATE_LIMITED|", StringComparison.Ordinal);
        if (idx < 0)
        {
            return null;
        }
        var parts = msg.Substring(idx).Split('|');
        if (parts.Length < 4)
        {
            return null;
        }
        if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var max))
        {
            return null;
        }
        if (!long.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix))
        {
            return null;
        }
        return new RateLimitException(parts[1], max, DateTimeOffset.FromUnixTimeSeconds(unix));
    }
}
