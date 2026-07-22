using System;

namespace AetherLove.Services;

/// <summary>The news-share chat payload. Only a message whose entire body is "[news=guid]" renders as a news
/// card; mixed into other text it deliberately stays plain.</summary>
public static class NewsShare
{
    public static string Compose(Guid newsId) => $"[news={newsId:D}]";

    public static bool TryParse(string text, out Guid newsId)
    {
        newsId = Guid.Empty;
        var s = text.Trim();
        if (s.Length < 8
            || !s.StartsWith("[news=", StringComparison.OrdinalIgnoreCase)
            || !s.EndsWith("]", StringComparison.Ordinal))
        {
            return false;
        }
        return Guid.TryParse(s.AsSpan(6, s.Length - 7), out newsId);
    }
}

/// <summary>Hand-off slot: set right before navigating to a chat, consumed by the chat screen's OnShow.</summary>
public sealed class NewsShareContext
{
    /// <summary>Set when the user picked a match to share a news entry with; consumed by the chat screen.</summary>
    public Guid? PendingShareNewsId { get; set; }
}
