using System;

namespace AetherLove.Shared.EchoVidya;

/// <summary>Pulls a YouTube video id out of whatever a user pasted: a full watch/embed/shorts URL, a
/// youtu.be short link, or the bare id. Anything else is rejected, so only ids the playback host can
/// actually load ever reach a playlist.</summary>
public static class EchoVideoIds
{
    /// <summary>YouTube ids are exactly this long.</summary>
    public const int IdLength = 11;

    private static readonly string[] AllowedHostSuffixes = ["youtube.com", "youtu.be"];

    private static readonly string[] PathPrefixes = ["/embed/", "/shorts/", "/v/", "/live/"];

    public static bool TryParse(string? input, out string videoId)
    {
        videoId = string.Empty;
        var trimmed = (input ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }
        if (IsId(trimmed))
        {
            videoId = trimmed;
            return true;
        }

        var withScheme = trimmed.Contains("://", StringComparison.Ordinal) ? trimmed : "https://" + trimmed;
        if (!Uri.TryCreate(withScheme, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || !IsYouTubeHost(uri.Host))
        {
            return false;
        }

        var candidate = uri.Host.EndsWith("youtu.be", StringComparison.OrdinalIgnoreCase)
            ? uri.AbsolutePath.Trim('/')
            : FromYouTubePath(uri);
        if (candidate is null || !IsId(candidate))
        {
            return false;
        }
        videoId = candidate;
        return true;
    }

    private static string? FromYouTubePath(Uri uri)
    {
        foreach (var prefix in PathPrefixes)
        {
            if (uri.AbsolutePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return uri.AbsolutePath[prefix.Length..].Trim('/');
            }
        }
        return QueryValue(uri.Query, "v");
    }

    internal static string? QueryValue(string query, string key)
    {
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0 || !pair.AsSpan(0, eq).Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            return Uri.UnescapeDataString(pair[(eq + 1)..]);
        }
        return null;
    }

    internal static bool IsYouTubeHost(string host)
    {
        foreach (var suffix in AllowedHostSuffixes)
        {
            if (host.Equals(suffix, StringComparison.OrdinalIgnoreCase)
                || host.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsId(string value)
    {
        if (value.Length != IdLength)
        {
            return false;
        }
        foreach (var c in value)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-'))
            {
                return false;
            }
        }
        return true;
    }
}
