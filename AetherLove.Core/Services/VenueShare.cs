using System;

namespace AetherLove.Services;

/// <summary>The venue-share chat payload. Only a message whose entire body is "[venue=guid]" renders as a
/// venue card; mixed into other text it deliberately stays plain.</summary>
public static class VenueShare
{
    public static string Compose(Guid venueId) => $"[venue={venueId:D}]";

    public static bool TryParse(string text, out Guid venueId)
    {
        venueId = Guid.Empty;
        var s = text.Trim();
        if (s.Length < 9
            || !s.StartsWith("[venue=", StringComparison.OrdinalIgnoreCase)
            || !s.EndsWith("]", StringComparison.Ordinal))
        {
            return false;
        }
        return Guid.TryParse(s.AsSpan(7, s.Length - 8), out venueId);
    }
}

/// <summary>Hand-off slots between Places and the chat; set right before navigating, consumed by the target's OnShow.</summary>
public sealed class VenueShareContext
{
    /// <summary>Set by Places when the user picked a match to share with; consumed by the chat screen.</summary>
    public Guid? PendingShareVenueId { get; set; }

    /// <summary>Set by the chat when a venue card is clicked; consumed by Places.</summary>
    public Guid? PendingOpenVenueId { get; set; }

    /// <summary>Origin app id of the pending open ("messenger", ...); null means the AetherLove chat, whose
    /// back leg goes through the social bridge instead.</summary>
    public string? PendingOpenReturnApp { get; set; }
}
