using System;

namespace AetherLove.Shared.EchoVidya;

/// <summary>Turns whatever a user pasted into a source plus a reference the playback host can load. The
/// reference is the only thing stored and sent, so its shape is a wire format: a YouTube entry is the bare
/// 11-character video id it always was, and a Twitch entry is <c>c:channel</c> for a live channel or
/// <c>v:12345</c> for one of its recordings.</summary>
public static class EchoMediaRefs
{
    /// <summary>Longest reference any source may produce; the stored column is sized from this.</summary>
    public const int RefMaxLength = 64;

    /// <summary>Twitch logins are 4 to 25 characters of letters, digits and underscores. Three is allowed
    /// because a handful of legacy accounts predate the minimum.</summary>
    private const int TwitchNameMinLength = 3;
    private const int TwitchNameMaxLength = 25;

    private static readonly string[] TwitchHostSuffixes = ["twitch.tv"];

    /// <summary>First path segments on twitch.tv that are site furniture rather than a channel. A link to
    /// one of these is rejected instead of queueing a channel that does not exist.</summary>
    private static readonly string[] TwitchReservedPaths =
    [
        "videos", "video", "directory", "settings", "subscriptions", "wallet", "downloads", "friends",
        "inventory", "payments", "prime", "store", "turbo", "drops", "moderator", "popout", "team",
        "u", "p", "products", "jobs", "about", "login", "signup", "search", "following", "collections",
        "clips", "embed", "broadcast", "dashboard",
    ];

    /// <summary>Parses a paste into the source and the reference to store. Anything unrecognised is
    /// rejected, so only references a player can actually load ever reach a playlist.</summary>
    public static bool TryParse(string? input, out EchoMediaSource source, out string reference)
    {
        source = EchoMediaSource.YouTube;
        reference = string.Empty;
        var trimmed = (input ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        // YouTube first, and that includes the bare id: a naked 11-character string is a YouTube video by
        // long-standing convention here, and Twitch is only ever reached through a link.
        if (EchoVideoIds.TryParse(trimmed, out var videoId))
        {
            source = EchoMediaSource.YouTube;
            reference = videoId;
            return true;
        }

        if (TryParseTwitch(trimmed, out reference))
        {
            source = EchoMediaSource.Twitch;
            return true;
        }

        reference = string.Empty;
        return false;
    }

    /// <summary>The name the playback host and the watch page know a source by. Unknown numbers answer
    /// youtube, which is what every entry written before sources existed is.</summary>
    public static string WireName(EchoMediaSource source) => source switch
    {
        EchoMediaSource.Twitch => "twitch",
        _ => "youtube",
    };

    /// <summary>True when the reference names a broadcast that is live by its nature. A Twitch channel is
    /// whoever is streaming on it right now, so it needs no lookup to be badged; a recording does.</summary>
    public static bool IsAlwaysLive(EchoMediaSource source, string? reference) =>
        source == EchoMediaSource.Twitch && (reference ?? string.Empty).StartsWith("c:", StringComparison.Ordinal);

    /// <summary>The channel login inside a <c>c:</c> reference, or null for anything else.</summary>
    public static string? TwitchChannel(string? reference) =>
        (reference ?? string.Empty).StartsWith("c:", StringComparison.Ordinal) ? reference![2..] : null;

    /// <summary>The video id inside a <c>v:</c> reference, or null for anything else.</summary>
    public static string? TwitchVideo(string? reference) =>
        (reference ?? string.Empty).StartsWith("v:", StringComparison.Ordinal) ? reference![2..] : null;

    /// <summary>A label to show before anything has resolved a real title: the channel or video as pasted,
    /// rather than the raw stored reference with its prefix.</summary>
    public static string? DisplayHint(EchoMediaSource source, string? reference)
    {
        if (source != EchoMediaSource.Twitch)
        {
            return null;
        }
        if (TwitchChannel(reference) is { Length: > 0 } channel)
        {
            return channel;
        }
        return TwitchVideo(reference) is { Length: > 0 } video ? "twitch/" + video : null;
    }

    private static bool TryParseTwitch(string input, out string reference)
    {
        reference = string.Empty;
        var withScheme = input.Contains("://", StringComparison.Ordinal) ? input : "https://" + input;
        if (!Uri.TryCreate(withScheme, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || !IsTwitchHost(uri.Host))
        {
            return false;
        }

        // The popout player carries its subject in the query rather than the path.
        var query = uri.Query;
        if (query.Length > 1)
        {
            if (ReadQuery(query, "channel") is { Length: > 0 } queryChannel && IsChannelName(queryChannel))
            {
                reference = "c:" + queryChannel.ToLowerInvariant();
                return true;
            }
            if (ReadQuery(query, "video") is { Length: > 0 } queryVideo && IsVideoId(queryVideo.TrimStart('v')))
            {
                reference = "v:" + queryVideo.TrimStart('v');
                return true;
            }
        }

        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return false;
        }

        if (segments.Length >= 2
            && (segments[0].Equals("videos", StringComparison.OrdinalIgnoreCase)
                || segments[0].Equals("video", StringComparison.OrdinalIgnoreCase))
            && IsVideoId(segments[1]))
        {
            reference = "v:" + segments[1];
            return true;
        }

        // A channel link may carry trailing furniture (/about, /videos, a clip); the channel is segment one.
        if (IsChannelName(segments[0]) && !IsReserved(segments[0]))
        {
            reference = "c:" + segments[0].ToLowerInvariant();
            return true;
        }

        return false;
    }

    private static bool IsTwitchHost(string host)
    {
        foreach (var suffix in TwitchHostSuffixes)
        {
            if (host.Equals(suffix, StringComparison.OrdinalIgnoreCase)
                || host.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsReserved(string segment)
    {
        foreach (var reserved in TwitchReservedPaths)
        {
            if (segment.Equals(reserved, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsChannelName(string value)
    {
        if (value.Length is < TwitchNameMinLength or > TwitchNameMaxLength)
        {
            return false;
        }
        foreach (var c in value)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '_')
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsVideoId(string value)
    {
        if (value.Length is 0 or > 20)
        {
            return false;
        }
        foreach (var c in value)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }
        return true;
    }

    private static string? ReadQuery(string query, string key)
    {
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var split = pair.IndexOf('=');
            if (split <= 0)
            {
                continue;
            }
            if (pair[..split].Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(pair[(split + 1)..]);
            }
        }
        return null;
    }
}
