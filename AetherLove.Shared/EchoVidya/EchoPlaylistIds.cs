using System;

namespace AetherLove.Shared.EchoVidya;

/// <summary>Pulls the playlist id out of a pasted YouTube link, and the video id alongside it when the link
/// carries both. Auto-generated and private lists are rejected here rather than failing later: a mix has no
/// fixed contents and a personal list is not readable without the owner's session.</summary>
public static class EchoPlaylistIds
{
    public const int MinIdLength = 2;

    public const int MaxIdLength = 64;

    /// <summary>Mixes YouTube builds on the fly; there is no stable list of videos to import.</summary>
    private const string MixPrefix = "RD";

    /// <summary>Watch later and liked videos; only readable while signed in as their owner.</summary>
    private static readonly string[] PersonalIds = ["WL", "LL", "LM"];

    /// <summary>True when <paramref name="input"/> names a playlist Echo can read.
    /// <paramref name="videoId"/> is the video the same link points at, empty when it has none.</summary>
    public static bool TryParse(string? input, out string playlistId, out string videoId)
    {
        playlistId = string.Empty;
        EchoVideoIds.TryParse(input, out videoId);

        var trimmed = (input ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        var withScheme = trimmed.Contains("://", StringComparison.Ordinal) ? trimmed : "https://" + trimmed;
        if (!Uri.TryCreate(withScheme, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || !EchoVideoIds.IsYouTubeHost(uri.Host))
        {
            return false;
        }

        var candidate = EchoVideoIds.QueryValue(uri.Query, "list");
        if (!IsImportableId(candidate))
        {
            return false;
        }
        playlistId = candidate!;
        return true;
    }

    private static bool IsImportableId(string? value)
    {
        if (value is null || value.Length < MinIdLength || value.Length > MaxIdLength)
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
        if (value.StartsWith(MixPrefix, StringComparison.Ordinal))
        {
            return false;
        }
        foreach (var personal in PersonalIds)
        {
            if (value.Equals(personal, StringComparison.Ordinal))
            {
                return false;
            }
        }
        return true;
    }
}
