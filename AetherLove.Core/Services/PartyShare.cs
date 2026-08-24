using System;
using System.Linq;
using AetherLove.Shared.Together;

namespace AetherLove.Services;

/// <summary>The party-invite chat payload. A message whose LAST line is "[party=guid:CODE]" renders as a
/// join card; anything above that line is the sender's own invitation and is drawn on the card, so an
/// invite can be written rather than only sent. The code rides along so the card joins without a lookup.
/// <para>Modelled on <see cref="EchoShare"/>, with the one difference that the token may follow text: an
/// echo card is the whole message by decision, while an invite is somebody saying come and play.</para></summary>
public static class PartyShare
{
    private const string Prefix = "[party=";

    /// <summary>Length of a guid in "D" form.</summary>
    private const int GuidTextLength = 36;

    private static readonly int TokenMinLength =
        Prefix.Length + GuidTextLength + 1 + TogetherLimits.PartyCodeLength + 1;

    public static string Compose(Guid partyId, string code) => $"{Prefix}{partyId:D}:{Normalize(code)}]";

    /// <summary>Composes an invite carrying the sender's own words above the token.</summary>
    public static string Compose(Guid partyId, string code, string message)
    {
        var text = message.Trim();
        return text.Length == 0 ? Compose(partyId, code) : $"{text}\n{Compose(partyId, code)}";
    }

    /// <summary>Splits a message into its invitation text and the party it points at. False for anything
    /// that does not end in a well-formed token.</summary>
    public static bool TryParse(string text, out Guid partyId, out string code, out string message)
    {
        partyId = Guid.Empty;
        code = string.Empty;
        message = string.Empty;
        var body = text.TrimEnd();
        var lastBreak = body.LastIndexOf('\n');
        var token = (lastBreak < 0 ? body : body[(lastBreak + 1)..]).Trim();
        if (token.Length < TokenMinLength
            || !token.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
            || !token.EndsWith("]", StringComparison.Ordinal))
        {
            return false;
        }
        var inner = token[Prefix.Length..^1];
        var split = inner.IndexOf(':');
        if (split < 0 || !Guid.TryParse(inner.AsSpan(0, split), out partyId))
        {
            partyId = Guid.Empty;
            return false;
        }
        var raw = Normalize(inner[(split + 1)..]);
        if (raw.Length != TogetherLimits.PartyCodeLength)
        {
            partyId = Guid.Empty;
            return false;
        }
        code = raw;
        message = lastBreak < 0 ? string.Empty : body[..lastBreak].Trim();
        return true;
    }

    private static string Normalize(string code) =>
        new(code.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
}
