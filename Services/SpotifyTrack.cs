using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AetherLove.Services;

/// <summary>Resolves a Spotify track from a pasted URL / <c>spotify:track:</c> URI / bare id and builds a
/// "Title — Artist" label. The title comes from the stable oEmbed endpoint; the artist is a best-effort scrape
/// of the track page's OpenGraph tags (oEmbed carries no artist). Shared by onboarding + the edit profile screen.</summary>
public static class SpotifyTrack
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(6) };

    private static readonly Regex UrlRegex =
        new(@"(?:spotify\.com/track/|spotify:track:)([A-Za-z0-9]+)", RegexOptions.Compiled);
    private static readonly Regex BareIdRegex =
        new(@"^[A-Za-z0-9]{22}$", RegexOptions.Compiled);

    /// <summary>Canonical Spotify track URL prefix, plus the bare display form shown next to the input box.</summary>
    internal const string UrlPrefix = "https://open.spotify.com/track/";
    internal const string DisplayPrefix = "open.spotify.com/track/";
    private static readonly Regex MusicianRegex =
        new(@"<meta\s+name=""music:musician_description""\s+content=""([^""]*)""", RegexOptions.Compiled);
    private static readonly Regex OgDescRegex =
        new(@"<meta\s+property=""og:description""\s+content=""([^""]*)""", RegexOptions.Compiled);

    private const int MaxLabelLength = 200; // matches the SpotifyTrackName column

    static SpotifyTrack()
    {
        // The track page serves its OpenGraph tags only to a browser-like client.
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; AetherLove)");
    }

    /// <summary>Extracts a track id from a Spotify URL, a <c>spotify:track:</c> URI, or a bare 22-char id.</summary>
    public static bool TryParseId(string? input, out string trackId)
    {
        trackId = string.Empty;
        if (string.IsNullOrEmpty(input))
        {
            return false;
        }
        var m = UrlRegex.Match(input);
        if (m.Success)
        {
            trackId = m.Groups[1].Value;
            return true;
        }
        if (BareIdRegex.IsMatch(input))
        {
            trackId = input;
            return true;
        }
        return false;
    }

    /// <summary>"Title — Artist" for the track (just the title if the artist can't be resolved), or null if the
    /// title itself couldn't be fetched. Throws only when the required title request fails.</summary>
    public static async Task<string?> FetchTrackLabelAsync(string trackId)
    {
        var title = await FetchTitleAsync(trackId).ConfigureAwait(false);
        if (string.IsNullOrEmpty(title))
        {
            return null;
        }

        string? artist = null;
        try { artist = await FetchArtistAsync(trackId).ConfigureAwait(false); }
        catch { /* artist is best-effort; the title alone is fine */ }

        var label = string.IsNullOrWhiteSpace(artist) ? title : $"{title} — {artist}";
        return label.Length > MaxLabelLength ? label[..MaxLabelLength] : label;
    }

    private static async Task<string?> FetchTitleAsync(string trackId)
    {
        var url = $"https://open.spotify.com/oembed?url={UrlPrefix}{trackId}";
        var json = await Http.GetStringAsync(url).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("title", out var titleEl) ? titleEl.GetString() : null;
    }

    private static async Task<string?> FetchArtistAsync(string trackId)
    {
        var html = await Http.GetStringAsync($"{UrlPrefix}{trackId}").ConfigureAwait(false);

        var musician = MusicianRegex.Match(html);
        if (musician.Success)
        {
            return WebUtility.HtmlDecode(musician.Groups[1].Value).Trim();
        }

        // Fallback: og:description is "Artists · Album · Song · Year"; the first segment is the artist list.
        var og = OgDescRegex.Match(html);
        if (og.Success)
        {
            var desc = WebUtility.HtmlDecode(og.Groups[1].Value);
            var sep = desc.IndexOf(" · ", StringComparison.Ordinal);
            return sep > 0 ? desc[..sep].Trim() : null;
        }
        return null;
    }
}
