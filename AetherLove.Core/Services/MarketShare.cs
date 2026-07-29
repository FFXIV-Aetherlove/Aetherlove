using System;
using AetherOS.Sdk;

namespace AetherLove.Services;

/// <summary>The market-item chat payload. Only a message whose entire body is "[market=itemId]" renders as
/// a card; mixed into other text it deliberately stays plain. Self-contained: the item id resolves against
/// the local game sheets and Universalis, so no server is involved.</summary>
public static class MarketShare
{
    public static string Compose(uint itemId) => $"[market={itemId}]";

    public static bool TryParse(string text, out uint itemId)
    {
        itemId = 0;
        var s = text.Trim();
        if (s.Length < 10
            || !s.StartsWith("[market=", StringComparison.OrdinalIgnoreCase)
            || !s.EndsWith("]", StringComparison.Ordinal))
        {
            return false;
        }
        return uint.TryParse(s.AsSpan(8, s.Length - 9), out itemId) && itemId > 0;
    }

    public static string? TryComposeFromShareItem(ShareItem item) =>
        item.Type == ShareTypes.MarketItem && uint.TryParse(item.RefId, out var itemId) && itemId > 0
            ? Compose(itemId)
            : null;
}

/// <summary>Hand-off slot: set right before navigating to a chat, consumed by the chat screen's OnShow.</summary>
public sealed class MarketShareContext
{
    /// <summary>Set when the user picked a match to share a market item with; consumed by the chat screen.</summary>
    public uint? PendingShareItemId { get; set; }
}
