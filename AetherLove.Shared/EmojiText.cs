using System.Text.RegularExpressions;

namespace AetherLove.Shared;

/// <summary>Emoji-aware text helpers shared by the plugin and the server so the bio length limit is
/// computed identically on both sides. A <c>:shortcode:</c> counts as one user-visible character even
/// though its raw text is longer.</summary>
public static class EmojiText
{
    /// <summary>Max user-visible bio length (each emoji shortcode counts as one).</summary>
    public const int MaxBioLength = 500;

    /// <summary>Hard cap on the raw stored/typed bio. Emoji shortcodes make the raw far longer than the
    /// visible length, but this bounds abuse from a tampered client. Matches the plugin's input buffer.</summary>
    public const int MaxBioRawLength = 4096;

    private static readonly Regex EmojiPattern =
        new(@":[a-z0-9_-]+:", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Length where each <c>:shortcode:</c> counts as exactly one character — the count the user
    /// sees rendered. Leading/trailing whitespace is trimmed (mirrors the plugin's parser).</summary>
    public static int EffectiveLength(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var trimmed = text.Trim();
        var count = trimmed.Length;
        foreach (Match m in EmojiPattern.Matches(trimmed))
        {
            count -= m.Length - 1;
        }
        return count;
    }
}
