using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Shared.EchoVidya;

namespace AetherLove.Services.Echo;

/// <summary>The one HTTP mouth Echo uses to read YouTube's public pages client-side, and the single-video
/// probe that answers whether an id is broadcasting right now. Reading these pages from the client is
/// deliberate: it costs the AetherLove server nothing, and the answer is only ever a badge.</summary>
public static class EchoYouTube
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Cap on a response we will parse, so a hostile or broken reply cannot be walked forever.</summary>
    internal const int MaxResponseBytes = 8 * 1024 * 1024;

    internal static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
        var client = new HttpClient(handler) { Timeout = RequestTimeout };
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
        // Without a recorded consent choice the EU edge serves a consent interstitial instead of the
        // page we parse, and what we are looking for is never in the response at all.
        client.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", "SOCS=CAI");
        return client;
    }

    internal static async Task<string?> ReadAsync(Func<Task<HttpResponseMessage>> send, CancellationToken ct)
    {
        using var response = await send().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength is > MaxResponseBytes)
        {
            return null;
        }
        var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        return bytes.Length > MaxResponseBytes ? null : Encoding.UTF8.GetString(bytes);
    }

    /// <summary>True when the id is broadcasting right now, false when it is an ordinary video, and null
    /// when the page could not be read or did not carry the answer. Null is not false: an unknown badge is
    /// left alone rather than cleared, because the player corrects it at play time anyway. Never throws.</summary>
    public static async Task<bool?> IsLiveAsync(string? videoId, CancellationToken ct = default)
    {
        if (!EchoVideoIds.TryParse(videoId, out var id))
        {
            return null;
        }
        try
        {
            var url = "https://www.youtube.com/watch?v=" + Uri.EscapeDataString(id) + "&hl=en&persist_hl=1";
            var html = await ReadAsync(
                () => Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct), ct).ConfigureAwait(false);
            return html is null ? null : ReadIsLive(html, id);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Reads the answer out of the watch page's own <c>videoDetails</c> block. The page also
    /// describes the videos in the sidebar, so a bare search for the flag would answer for somebody else's
    /// video; the block is located and parsed as JSON, and its id must be the one we asked about.</summary>
    internal static bool? ReadIsLive(string html, string videoId)
    {
        const string Marker = "\"videoDetails\":{";
        var at = html.IndexOf(Marker, StringComparison.Ordinal);
        while (at >= 0)
        {
            var start = at + Marker.Length - 1;
            if (Slice(html, start) is { } json)
            {
                try
                {
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("videoId", out var id)
                        && id.ValueKind == JsonValueKind.String
                        && string.Equals(id.GetString(), videoId, StringComparison.Ordinal))
                    {
                        return root.TryGetProperty("isLive", out var live)
                            && live.ValueKind is JsonValueKind.True or JsonValueKind.False
                            ? live.GetBoolean()
                            : false;
                    }
                }
                catch (JsonException)
                {
                }
            }
            at = html.IndexOf(Marker, at + Marker.Length, StringComparison.Ordinal);
        }
        return null;
    }

    /// <summary>The object starting at <paramref name="start"/>, brace-counted with string and escape
    /// awareness so a brace inside a title cannot end it early.</summary>
    private static string? Slice(string html, int start)
    {
        const int MaxObjectChars = 64 * 1024;
        var depth = 0;
        var inString = false;
        var escaped = false;
        var limit = Math.Min(html.Length, start + MaxObjectChars);
        for (var i = start; i < limit; i++)
        {
            var c = html[i];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (c == '\\')
                {
                    escaped = true;
                }
                else if (c == '"')
                {
                    inString = false;
                }
                continue;
            }
            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    if (depth == 0)
                    {
                        return html[start..(i + 1)];
                    }
                    break;
            }
        }
        return null;
    }
}
