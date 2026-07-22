using System;
using System.Linq;

namespace AetherLove.Services;

/// <summary>The messenger-invite chat payload. Only a message whose entire body is "[messenger=CODE]" renders
/// as an invite card; mixed into other text it deliberately stays plain.</summary>
public static class MessengerShare
{
    public static string Compose(string code) => $"[messenger={Strip(code)}]";

    public static bool TryParse(string text, out string code)
    {
        code = string.Empty;
        var s = text.Trim();
        if (s.Length < 12
            || !s.StartsWith("[messenger=", StringComparison.OrdinalIgnoreCase)
            || !s.EndsWith("]", StringComparison.Ordinal))
        {
            return false;
        }
        var raw = Strip(s[11..^1]);
        if (raw.Length is < 6 or > 12 || !raw.All(char.IsLetterOrDigit))
        {
            return false;
        }
        code = raw;
        return true;
    }

    /// <summary>The friendly XXXX@XXXX form for rendering.</summary>
    public static string Display(string code) =>
        code.Length == 8 ? $"{code[..4]}@{code[4..]}" : code;

    private static string Strip(string code) =>
        new(code.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
}
