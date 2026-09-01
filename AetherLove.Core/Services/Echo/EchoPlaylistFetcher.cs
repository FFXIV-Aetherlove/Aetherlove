using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Shared.EchoVidya;

namespace AetherLove.Services.Echo;

/// <summary>What a playlist read produced. <see cref="TotalCount"/> is the length the playlist claims,
/// which runs ahead of <see cref="Items"/> when it holds videos that have since been deleted or made
/// private: those are counted in the total and served to nobody.</summary>
public sealed record EchoPlaylistFetchResult(
    string PlaylistId,
    string? Title,
    int TotalCount,
    IReadOnlyList<EchoPlaylistImportItem> Items);

/// <summary>Reads a public YouTube playlist's videos client-side, so filling a room's queue costs the
/// AetherLove server nothing but the one bulk add. The page serves its first hundred videos inline and the
/// rest behind continuation tokens, which are followed until the import cap is reached.</summary>
public static class EchoPlaylistFetcher
{
    /// <summary>Continuation requests allowed per import. A round returns a hundred videos, and one round is
    /// spent on the recommended-playlists shelf whose token sits on the page beside the real one.</summary>
    private const int MaxContinuationRounds = 8;

    private static readonly Regex OgTitle = new(
        "<meta[^>]+property=\"og:title\"[^>]+content=\"([^\"]*)\"",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex ApiKey = new(
        "\"INNERTUBE_API_KEY\":\"([^\"]+)\"", RegexOptions.CultureInvariant);

    private static readonly Regex ClientVersion = new(
        "\"INNERTUBE_CLIENT_VERSION\":\"([^\"]+)\"", RegexOptions.CultureInvariant);

    /// <summary>Rows and continuation tokens gathered across the page and its continuations.</summary>
    private sealed class Harvest
    {
        public List<EchoPlaylistImportItem> Items { get; } = new();

        public Queue<string> Tokens { get; } = new();

        public int Total { get; set; }

        public bool Full => Items.Count >= EchoLimits.MaxPlaylistImportItems;
    }

    /// <summary>Reads the playlist, or returns null when it cannot be read (private, deleted, or a page
    /// shape we do not recognise). Never throws.</summary>
    public static async Task<EchoPlaylistFetchResult?> FetchAsync(string playlistId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(playlistId))
        {
            return null;
        }
        try
        {
            var url = "https://www.youtube.com/playlist?list="
                + Uri.EscapeDataString(playlistId) + "&hl=en&persist_hl=1";
            var html = await EchoYouTube.ReadAsync(() => EchoYouTube.Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct), ct)
                .ConfigureAwait(false);
            if (html is null)
            {
                return null;
            }

            var harvest = HarvestPage(html);
            if (harvest is null || harvest.Items.Count == 0)
            {
                return null;
            }
            await FollowContinuationsAsync(html, harvest, ct).ConfigureAwait(false);

            return new EchoPlaylistFetchResult(playlistId, PlaylistTitle(html),
                Math.Max(harvest.Total, harvest.Items.Count), harvest.Items);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Spends the page's tokens for more rows. A token can belong to the recommended-playlists
    /// shelf rather than the video list, which is why an empty round is not the end of the walk.</summary>
    private static async Task FollowContinuationsAsync(string html, Harvest harvest, CancellationToken ct)
    {
        var key = ApiKey.Match(html);
        var version = ClientVersion.Match(html);
        if (!key.Success || !version.Success)
        {
            return;
        }
        var url = "https://www.youtube.com/youtubei/v1/browse?key="
            + Uri.EscapeDataString(key.Groups[1].Value) + "&prettyPrint=false";

        for (var round = 0; round < MaxContinuationRounds && !harvest.Full && harvest.Tokens.Count > 0; round++)
        {
            var body = ContinuationBody(version.Groups[1].Value, harvest.Tokens.Dequeue());
            var json = await EchoYouTube.ReadAsync(
                () => EchoYouTube.Http.PostAsync(url, new StringContent(body, Encoding.UTF8, "application/json"), ct), ct)
                .ConfigureAwait(false);
            if (json is null)
            {
                return;
            }
            using var doc = JsonDocument.Parse(json);
            Collect(doc.RootElement, harvest);
        }
    }

    private static string ContinuationBody(string clientVersion, string token)
    {
        var buffer = new System.IO.MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteStartObject("context");
            writer.WriteStartObject("client");
            writer.WriteString("clientName", "WEB");
            writer.WriteString("clientVersion", clientVersion);
            writer.WriteString("hl", "en");
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteString("continuation", token);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static Harvest? HarvestPage(string html)
    {
        var json = ExtractInitialData(html);
        if (json is null)
        {
            return null;
        }
        using var doc = JsonDocument.Parse(json);
        var harvest = new Harvest();
        Collect(doc.RootElement, harvest);
        return harvest;
    }

    /// <summary>Walks the whole tree for renderers rather than a fixed path: YouTube reshapes the wrapping
    /// containers regularly, but a video row and the video count keep their own names. Every row shape is
    /// read, because the page has been served as any of them while the view-model rewrite rolls out.</summary>
    private static void Collect(JsonElement node, Harvest harvest)
    {
        switch (node.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in node.EnumerateObject())
                {
                    if (property.NameEquals("lockupViewModel"))
                    {
                        AddLockup(property.Value, harvest);
                        continue;
                    }
                    if (property.NameEquals("shortsLockupViewModel"))
                    {
                        AddShortsLockup(property.Value, harvest);
                        continue;
                    }
                    if (property.NameEquals("playlistVideoRenderer"))
                    {
                        AddRenderer(property.Value, harvest);
                        continue;
                    }
                    if (property.NameEquals("continuationCommand"))
                    {
                        AddToken(property.Value, harvest);
                        continue;
                    }
                    if (harvest.Total == 0 && IsCountProperty(property))
                    {
                        // First one wins: that is this playlist's own header. Later counts belong to the
                        // recommended playlists further down the page.
                        harvest.Total = ParseCount(Text(property.Value));
                        continue;
                    }
                    if (harvest.Total == 0 && property.NameEquals("playlistSidebarPrimaryInfoRenderer"))
                    {
                        harvest.Total = SidebarCount(property.Value);
                        continue;
                    }
                    Collect(property.Value, harvest);
                }
                break;
            case JsonValueKind.Array:
                foreach (var child in node.EnumerateArray())
                {
                    Collect(child, harvest);
                }
                break;
        }
    }

    private static void AddToken(JsonElement command, Harvest harvest)
    {
        if (command.ValueKind == JsonValueKind.Object
            && command.TryGetProperty("token", out var token)
            && token.ValueKind == JsonValueKind.String
            && token.GetString() is { Length: > 0 } value)
        {
            harvest.Tokens.Enqueue(value);
            return;
        }
        // The page nests the real token one command deeper, inside the click handler of the row that
        // triggers the load; the continuation reply carries it bare.
        Collect(command, harvest);
    }

    private static bool IsCountProperty(JsonProperty property)
        => property.NameEquals("numVideosText") || property.NameEquals("videoCountText");

    /// <summary>The sidebar's stats read "N videos", "N views", "Last updated on ..."; only the first is a
    /// video count, and taking any of the others would report a view count as a playlist length.</summary>
    private static int SidebarCount(JsonElement info)
    {
        if (info.ValueKind != JsonValueKind.Object
            || !info.TryGetProperty("stats", out var stats)
            || stats.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }
        foreach (var stat in stats.EnumerateArray())
        {
            return ParseCount(Text(stat));
        }
        return 0;
    }

    /// <summary>The current playlist row: one view model per item, videos identified by their content type
    /// so a playlist recommendation on the same page is never queued.</summary>
    private static void AddLockup(JsonElement lockup, Harvest harvest)
    {
        if (lockup.ValueKind != JsonValueKind.Object
            || !lockup.TryGetProperty("contentType", out var type)
            || type.ValueKind != JsonValueKind.String
            || type.GetString() is not "LOCKUP_CONTENT_TYPE_VIDEO"
            || !lockup.TryGetProperty("contentId", out var idNode))
        {
            return;
        }
        var title = lockup.TryGetProperty("metadata", out var metadata)
                    && metadata.TryGetProperty("lockupMetadataViewModel", out var model)
                    && model.TryGetProperty("title", out var titleNode)
            ? Text(titleNode)
            : null;
        Add(idNode, title, LockupIsLive(lockup), harvest);
    }

    /// <summary>The row's live badge, which rides on the thumbnail rather than the metadata: the same
    /// overlay that draws the red LIVE corner on youtube.com. Absent means an ordinary video, and a row
    /// whose stream ends before it is played is corrected by whoever plays it.</summary>
    private static bool LockupIsLive(JsonElement lockup)
    {
        if (!lockup.TryGetProperty("contentImage", out var image)
            || !image.TryGetProperty("thumbnailViewModel", out var thumbnail)
            || !thumbnail.TryGetProperty("overlays", out var overlays)
            || overlays.ValueKind != JsonValueKind.Array)
        {
            return false;
        }
        foreach (var overlay in overlays.EnumerateArray())
        {
            if (!overlay.TryGetProperty("thumbnailBottomOverlayViewModel", out var bottom)
                || !bottom.TryGetProperty("badges", out var badges)
                || badges.ValueKind != JsonValueKind.Array)
            {
                continue;
            }
            foreach (var badge in badges.EnumerateArray())
            {
                if (badge.TryGetProperty("thumbnailBadgeViewModel", out var model)
                    && model.TryGetProperty("badgeStyle", out var style)
                    && style.ValueKind == JsonValueKind.String
                    && style.GetString() is "THUMBNAIL_OVERLAY_BADGE_STYLE_LIVE")
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>A Shorts playlist's row, which keeps its id inside the reel endpoint rather than on the
    /// view model. A Short is an ordinary video to the player, so these queue like any other.</summary>
    private static void AddShortsLockup(JsonElement lockup, Harvest harvest)
    {
        if (lockup.ValueKind != JsonValueKind.Object
            || !lockup.TryGetProperty("onTap", out var tap)
            || !tap.TryGetProperty("innertubeCommand", out var command)
            || !command.TryGetProperty("reelWatchEndpoint", out var reel)
            || !reel.TryGetProperty("videoId", out var idNode))
        {
            return;
        }
        var title = lockup.TryGetProperty("overlayMetadata", out var overlay)
                    && overlay.TryGetProperty("primaryText", out var primary)
            ? Text(primary)
            : null;
        Add(idNode, title, false, harvest);
    }

    /// <summary>The older playlist row, still served to some clients.</summary>
    private static void AddRenderer(JsonElement renderer, Harvest harvest)
    {
        if (renderer.ValueKind != JsonValueKind.Object
            || !renderer.TryGetProperty("videoId", out var idNode))
        {
            return;
        }
        var title = renderer.TryGetProperty("title", out var titleNode) ? Text(titleNode) : null;
        Add(idNode, title, RendererIsLive(renderer), harvest);
    }

    /// <summary>The older row's live badge: the time-status overlay that carries the running time on an
    /// ordinary video and the word LIVE on a broadcast.</summary>
    private static bool RendererIsLive(JsonElement renderer)
    {
        if (!renderer.TryGetProperty("thumbnailOverlays", out var overlays)
            || overlays.ValueKind != JsonValueKind.Array)
        {
            return false;
        }
        foreach (var overlay in overlays.EnumerateArray())
        {
            if (overlay.TryGetProperty("thumbnailOverlayTimeStatusRenderer", out var status)
                && status.TryGetProperty("style", out var style)
                && style.ValueKind == JsonValueKind.String
                && style.GetString() is "LIVE")
            {
                return true;
            }
        }
        return false;
    }

    private static void Add(JsonElement idNode, string? title, bool isLive, Harvest harvest)
    {
        if (harvest.Full
            || idNode.ValueKind != JsonValueKind.String
            || !EchoVideoIds.TryParse(idNode.GetString(), out var videoId))
        {
            return;
        }
        harvest.Items.Add(new EchoPlaylistImportItem(videoId,
            string.IsNullOrWhiteSpace(title) ? null : title.Trim(), isLive));
    }

    /// <summary>Flattens YouTube's three text shapes: a view model's <c>content</c> string, a renderer's
    /// <c>simpleText</c> string, or a list of <c>runs</c>.</summary>
    private static string? Text(JsonElement node)
    {
        if (node.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        if (node.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
        {
            return content.GetString();
        }
        if (node.TryGetProperty("simpleText", out var simple) && simple.ValueKind == JsonValueKind.String)
        {
            return simple.GetString();
        }
        if (!node.TryGetProperty("runs", out var runs) || runs.ValueKind != JsonValueKind.Array)
        {
            return null;
        }
        var sb = new StringBuilder();
        foreach (var run in runs.EnumerateArray())
        {
            if (run.ValueKind == JsonValueKind.Object
                && run.TryGetProperty("text", out var text)
                && text.ValueKind == JsonValueKind.String)
            {
                sb.Append(text.GetString());
            }
        }
        return sb.Length == 0 ? null : sb.ToString();
    }

    /// <summary>"1,234 videos" to 1234. Digits are taken from wherever they sit so a localized page that
    /// slipped past the language hint still yields a number.</summary>
    private static int ParseCount(string? text)
    {
        if (text is null)
        {
            return 0;
        }
        var digits = new StringBuilder();
        foreach (var c in text)
        {
            if (char.IsAsciiDigit(c))
            {
                digits.Append(c);
            }
            else if (digits.Length > 0 && c is not (',' or '.') && !char.IsWhiteSpace(c))
            {
                break;
            }
        }
        return digits.Length > 0
               && int.TryParse(digits.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
    }

    private static string? PlaylistTitle(string html)
    {
        var match = OgTitle.Match(html);
        if (!match.Success)
        {
            return null;
        }
        var title = WebUtility.HtmlDecode(match.Groups[1].Value).Trim();
        return title.Length == 0 ? null : title;
    }

    /// <summary>Lifts the <c>ytInitialData</c> object out of the page by matching braces, because the
    /// assignment is one line of a script tag with no delimiter a split could rely on.</summary>
    internal static string? ExtractInitialData(string html)
    {
        foreach (var marker in new[] { "var ytInitialData = ", "window[\"ytInitialData\"] = ", "ytInitialData\"] = " })
        {
            var at = html.IndexOf(marker, StringComparison.Ordinal);
            if (at < 0)
            {
                continue;
            }
            var start = html.IndexOf('{', at + marker.Length);
            if (start < 0)
            {
                continue;
            }
            var end = MatchingBrace(html, start);
            if (end > start)
            {
                return html[start..(end + 1)];
            }
        }
        return null;
    }

    private static int MatchingBrace(string html, int start)
    {
        var depth = 0;
        var inString = false;
        var escaped = false;
        for (var i = start; i < html.Length; i++)
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
                        return i;
                    }
                    break;
            }
        }
        return -1;
    }
}
