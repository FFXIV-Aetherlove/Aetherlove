using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace AetherLove.Shared.Yapper;

/// <summary>Shared yap-text analysis: the emoji-aware effective length with flat-cost links, and
/// mention/hashtag extraction. Client (char ring) and server (authoritative validation) both use this
/// so the two can never disagree.</summary>
public static partial class YapTextParser
{
    [GeneratedRegex(@"https?://\S+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();

    [GeneratedRegex(@"(?<![A-Za-z0-9_])@([A-Za-z0-9_]{3,20})")]
    private static partial Regex MentionPattern();

    [GeneratedRegex(@"(?<![A-Za-z0-9_&])#([A-Za-z0-9_]{2,100})")]
    private static partial Regex TagPattern();

    /// <summary>Visible characters: emoji shortcodes count as one and every link costs a flat
    /// <see cref="YapperLimits.LinkCharCost"/> regardless of its real length.</summary>
    public static int EffectiveLength(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }
        var links = 0;
        var withoutLinks = UrlPattern().Replace(text, _ =>
        {
            links++;
            return string.Empty;
        });
        return EmojiText.EffectiveLength(withoutLinks) + links * YapperLimits.LinkCharCost;
    }

    /// <summary>Distinct lowercase @handles in order of first appearance, capped at
    /// <see cref="YapperLimits.MaxMentionsPerYap"/>.</summary>
    public static IReadOnlyList<string> ExtractMentions(string? text) =>
        Extract(text, MentionPattern(), YapperLimits.MaxMentionsPerYap);

    /// <summary>Distinct lowercase #tags in order of first appearance, capped at
    /// <see cref="YapperLimits.MaxTagsPerYap"/>.</summary>
    public static IReadOnlyList<string> ExtractTags(string? text) =>
        Extract(text, TagPattern(), YapperLimits.MaxTagsPerYap);

    private static IReadOnlyList<string> Extract(string? text, Regex pattern, int cap)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (Match match in pattern.Matches(text))
        {
            var normalized = match.Groups[1].Value.ToLowerInvariant();
            if (seen.Add(normalized))
            {
                result.Add(normalized);
                if (result.Count >= cap)
                {
                    break;
                }
            }
        }
        return result;
    }
}
