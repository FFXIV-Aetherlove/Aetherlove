using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AetherLove.Services.Translation;

/// <summary>The one translation backend: the keyless Google gtx endpoint, called directly from this
/// client (ADR 9; never proxied through our server, or one aggregating IP gets throttled for everyone).
/// Results are cached per (language, text) so re-toggling a bubble never re-fetches, and failures return
/// null rather than throwing: translation is a nicety, not a feature that may error at the user.</summary>
public sealed class TranslationService
{
    private const string Endpoint = "https://translate.googleapis.com/translate_a/single";
    private const int MaxTextLength = 3000;
    private const int CacheCap = 500;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private readonly object _lock = new();
    private readonly Dictionary<(string Language, string Text), CachedTranslation> _cache = new();

    public sealed record CachedTranslation(string Text, string? SourceLanguage);

    public async Task<CachedTranslation?> TranslateAsync(string text, string targetLanguage, CancellationToken ct = default)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0 || trimmed.Length > MaxTextLength || string.IsNullOrWhiteSpace(targetLanguage))
        {
            return null;
        }
        var key = (targetLanguage, trimmed);
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var hit))
            {
                return hit;
            }
        }

        try
        {
            var url = $"{Endpoint}?client=gtx&sl=auto&tl={Uri.EscapeDataString(targetLanguage)}&dt=t&q={Uri.EscapeDataString(trimmed)}";
            using var response = await Http.GetAsync(url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                UiHost.Log.Debug("[Translation] Endpoint answered {Status}.", (int)response.StatusCode);
                return null;
            }
            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var parsed = Parse(json);
            if (parsed is null)
            {
                return null;
            }
            lock (_lock)
            {
                if (_cache.Count >= CacheCap)
                {
                    _cache.Clear();
                }
                _cache[key] = parsed;
            }
            return parsed;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            UiHost.Log.Debug(ex, "[Translation] Request failed.");
            return null;
        }
    }

    /// <summary>The gtx shape is a bare nested array: element 0 is a list of segments whose element 0 is
    /// the translated run, element 2 of the root is the detected source language.</summary>
    private static CachedTranslation? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0
                || root[0].ValueKind != JsonValueKind.Array)
            {
                return null;
            }
            var sb = new StringBuilder();
            foreach (var segment in root[0].EnumerateArray())
            {
                if (segment.ValueKind == JsonValueKind.Array && segment.GetArrayLength() > 0
                    && segment[0].ValueKind == JsonValueKind.String)
                {
                    sb.Append(segment[0].GetString());
                }
            }
            if (sb.Length == 0)
            {
                return null;
            }
            string? source = null;
            if (root.GetArrayLength() > 2 && root[2].ValueKind == JsonValueKind.String)
            {
                source = root[2].GetString();
            }
            return new CachedTranslation(sb.ToString(), source);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
