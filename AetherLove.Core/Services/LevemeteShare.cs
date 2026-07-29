using System;

namespace AetherLove.Services;

/// <summary>The classified-ad share chat payload. Only a message whose entire body is "[levemete=guid]"
/// renders as an ad card; mixed into other text it deliberately stays plain.</summary>
public static class LevemeteShare
{
    public static string Compose(Guid adId) => $"[levemete={adId:D}]";

    public static bool TryParse(string text, out Guid adId)
    {
        adId = Guid.Empty;
        var s = text.Trim();
        if (s.Length < 12
            || !s.StartsWith("[levemete=", StringComparison.OrdinalIgnoreCase)
            || !s.EndsWith("]", StringComparison.Ordinal))
        {
            return false;
        }
        return Guid.TryParse(s.AsSpan(10, s.Length - 11), out adId);
    }
}

/// <summary>Hand-off slots between Levemetes and the chats; set right before navigating, consumed by the
/// target's OnShow or draw poll.</summary>
public sealed class LevemeteShareContext
{
    /// <summary>Set by Levemetes when the user picked a chat to share into; consumed by the chat screen.</summary>
    public Guid? PendingShareLevemeteId { get; set; }

    /// <summary>Set by a chat when an ad card is clicked; consumed by the Levemetes app.</summary>
    public Guid? PendingOpenLevemeteId { get; set; }

    /// <summary>Origin app id of the pending open ("messenger", ...); null means the AetherLove chat, whose
    /// back leg goes through the social bridge instead.</summary>
    public string? PendingOpenReturnApp { get; set; }
}
