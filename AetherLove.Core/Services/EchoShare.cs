using System;
using System.Linq;
using AetherLove.Shared.EchoVidya;

namespace AetherLove.Services;

/// <summary>The Echo room-share chat payload. Only a message whose entire body is "[echo=guid:CODE]" renders
/// as a room card; mixed into other text it deliberately stays plain. The code rides along so the card can
/// join without a second lookup.</summary>
public static class EchoShare
{
    private const string Prefix = "[echo=";

    /// <summary>Length of a guid in "D" form.</summary>
    private const int GuidTextLength = 36;

    private static readonly int MinLength = Prefix.Length + GuidTextLength + 1 + EchoLimits.RoomCodeLength + 1;

    public static string Compose(Guid roomId, string code) => $"{Prefix}{roomId:D}:{Normalize(code)}]";

    public static bool TryParse(string text, out Guid roomId, out string code)
    {
        roomId = Guid.Empty;
        code = string.Empty;
        var s = text.Trim();
        if (s.Length < MinLength
            || !s.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
            || !s.EndsWith("]", StringComparison.Ordinal))
        {
            return false;
        }
        var body = s[Prefix.Length..^1];
        var split = body.IndexOf(':');
        if (split < 0 || !Guid.TryParse(body.AsSpan(0, split), out roomId))
        {
            roomId = Guid.Empty;
            return false;
        }
        var raw = Normalize(body[(split + 1)..]);
        if (raw.Length != EchoLimits.RoomCodeLength)
        {
            roomId = Guid.Empty;
            return false;
        }
        code = raw;
        return true;
    }

    private static string Normalize(string code) =>
        new(code.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
}

/// <summary>Hand-off slots for the Echo share flow; set right before navigating, consumed by the target's
/// first frame.</summary>
public sealed class EchoShareContext
{
    /// <summary>Set when a room card is tapped; consumed by the Echo app, which joins the room.</summary>
    public Guid? PendingJoinRoomId { get; set; }

    /// <summary>The share code that rode along with <see cref="PendingJoinRoomId"/>.</summary>
    public string? PendingJoinCode { get; set; }

    /// <summary>Origin app id of the pending join ("messenger", ...); null means the AetherLove chat, whose
    /// back leg goes through the social bridge instead.</summary>
    public string? PendingOpenReturnApp { get; set; }
}
