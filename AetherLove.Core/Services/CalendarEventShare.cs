using System;
using System.Text;
using System.Text.Json;
using AetherOS.Sdk;

namespace AetherLove.Services;

/// <summary>Hand-off context for calendar-event sharing (venue occurrences or personal events with base64url JSON payloads).
/// Tokens are composed up front because the full share item is needed, not just an id.</summary>
public sealed class CalendarShareContext
{
    /// <summary>The composed [calevent=...] token to auto-send; consumed by the chat screen.</summary>
    public string? PendingShareToken { get; set; }
}

public static class CalendarEventShare
{
    public sealed record Payload(bool IsVenue, Guid VenueId, string Title, string Note, DateTimeOffset StartUtc);

    private sealed record PersonalJson(string T, string N, long S);

    public static string ComposeVenue(Guid venueId, DateTimeOffset startUtc) =>
        $"[calevent=v:{venueId:D}:{startUtc.ToUnixTimeSeconds()}]";

    public static string ComposePersonal(string title, string note, DateTimeOffset startUtc)
    {
        var json = JsonSerializer.Serialize(new PersonalJson(title, note, startUtc.ToUnixTimeSeconds()));
        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"[calevent=p:{b64}]";
    }

    /// <summary>Builds the token from a share-sheet item (the calendar app stages the item; both chat
    /// targets call this so the format lives in one place). Null when the item isn't a calendar event.</summary>
    public static string? TryComposeFromShareItem(ShareItem item)
    {
        if (item.Type != ShareTypes.CalendarEvent)
        {
            return null;
        }
        try
        {
            using var doc = JsonDocument.Parse(item.Extras);
            var start = DateTimeOffset.FromUnixTimeSeconds(doc.RootElement.GetProperty("start").GetInt64());
            var kind = doc.RootElement.GetProperty("kind").GetString();
            if (kind == "venue" && Guid.TryParse(item.RefId, out var venueId))
            {
                return ComposeVenue(venueId, start);
            }
            if (kind == "personal" && item.Title.Length > 0)
            {
                return ComposePersonal(item.Title, item.Subtitle, start);
            }
        }
        catch (Exception)
        {
        }
        return null;
    }

    public static bool TryParse(string text, out Payload payload)
    {
        payload = new Payload(false, Guid.Empty, "", "", DateTimeOffset.MinValue);
        var s = text.Trim();
        if (s.Length < 13
            || !s.StartsWith("[calevent=", StringComparison.OrdinalIgnoreCase)
            || !s.EndsWith("]", StringComparison.Ordinal))
        {
            return false;
        }
        var body = s[10..^1];
        if (body.StartsWith("v:", StringComparison.Ordinal))
        {
            var parts = body[2..].Split(':');
            if (parts.Length == 2 && Guid.TryParse(parts[0], out var venueId) && long.TryParse(parts[1], out var unix))
            {
                payload = new Payload(true, venueId, "", "", DateTimeOffset.FromUnixTimeSeconds(unix));
                return true;
            }
            return false;
        }
        if (!body.StartsWith("p:", StringComparison.Ordinal))
        {
            return false;
        }
        try
        {
            var b64 = body[2..].Replace('-', '+').Replace('_', '/');
            b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
            var personal = JsonSerializer.Deserialize<PersonalJson>(Encoding.UTF8.GetString(Convert.FromBase64String(b64)));
            if (personal is { T.Length: > 0 })
            {
                payload = new Payload(false, Guid.Empty, personal.T, personal.N ?? "", DateTimeOffset.FromUnixTimeSeconds(personal.S));
                return true;
            }
        }
        catch (Exception)
        {
        }
        return false;
    }
}
