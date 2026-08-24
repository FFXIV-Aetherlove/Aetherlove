namespace AetherOS.Sdk;

/// <summary>The canonical share content-type keys. One home so sources and targets never disagree on a literal.</summary>
public static class ShareTypes
{
    public const string Venue = "venue";
    public const string Hangout = "hangout";
    public const string Photo = "photo";
    public const string News = "news";
    public const string CalendarEvent = "calendar_event";
    public const string Levemete = "levemete";
    public const string MarketItem = "market_item";
    public const string Echo = "echo";

    /// <summary>A together-mode party invite (code-joined).</summary>
    public const string Party = "party";
}
