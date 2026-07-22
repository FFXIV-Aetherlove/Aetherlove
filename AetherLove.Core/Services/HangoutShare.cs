using System;
using AetherLove.Shared.Hangouts;

namespace AetherLove.Services;

/// <summary>The hangout-share chat payload. Only a message whose entire body is "[hangout=guid]" renders as
/// a hangout card; mixed into other text it deliberately stays plain.</summary>
public static class HangoutShare
{
    public static string Compose(Guid hangoutId) => $"[hangout={hangoutId:D}]";

    public static bool TryParse(string text, out Guid hangoutId)
    {
        hangoutId = Guid.Empty;
        var s = text.Trim();
        if (s.Length < 11
            || !s.StartsWith("[hangout=", StringComparison.OrdinalIgnoreCase)
            || !s.EndsWith("]", StringComparison.Ordinal))
        {
            return false;
        }
        return Guid.TryParse(s.AsSpan(9, s.Length - 10), out hangoutId);
    }
}

/// <summary>Hand-off slots for the hangout share flow; set right before navigating, consumed by the chat's OnShow.</summary>
public sealed class HangoutShareContext
{
    /// <summary>The hangout the user picked a match to share with; consumed by the chat screen.</summary>
    public Guid? PendingShareHangoutId { get; set; }

    /// <summary>Set when a hangout card (or banner/row) is tapped; consumed by the Hangouts app to open the
    /// detail overlay. <see cref="PendingOpenFromChat"/> marks a tap that should return to the chat on back.</summary>
    public HangoutSummaryDto? PendingOpenHangout { get; set; }

    public bool PendingOpenFromChat { get; set; }
}
