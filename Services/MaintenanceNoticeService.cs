using System;
using System.Globalization;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AetherLove.Services;

/// <summary>Fetches the optional maintenance notice (a plain-text file) shown on the offline screen; empty or
/// unreachable means no notice. Any &lt;unixSeconds&gt; token in the text is rewritten to the viewer's local time.</summary>
public sealed class MaintenanceNoticeService
{
    private const string MaintenanceUrl = "https://aetherlove.space/maintenance.txt";
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);
    private static readonly Regex TimestampToken = new(@"<(\d+)>", RegexOptions.Compiled);

    private readonly IHttpClientFactory _httpFactory;
    private readonly object _lock = new();
    private long _lastFetchMs;
    private bool _fetchedOnce;
    private bool _fetching;
    private volatile string? _notice;

    public MaintenanceNoticeService(IHttpClientFactory httpFactory)
    {
        _httpFactory = httpFactory;
    }

    /// <summary>The parsed maintenance notice, or null when there is none. Read on the UI thread.</summary>
    public string? Notice => _notice;

    /// <summary>Kicks a fetch if one hasn't run recently. Safe to call every frame.</summary>
    public void Refresh()
    {
        lock (_lock)
        {
            if (_fetching)
            {
                return;
            }
            if (_fetchedOnce && Environment.TickCount64 - _lastFetchMs < RefreshInterval.TotalMilliseconds)
            {
                return;
            }
            _lastFetchMs = Environment.TickCount64;
            _fetchedOnce = true;
            _fetching = true;
        }
        _ = Task.Run(FetchAsync);
    }

    private async Task FetchAsync()
    {
        try
        {
            using var http = _httpFactory.CreateClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var text = await http.GetStringAsync(MaintenanceUrl, cts.Token).ConfigureAwait(false);
            _notice = string.IsNullOrWhiteSpace(text) ? null : FormatTimestamps(text.Trim());
        }
        catch (Exception ex)
        {
            // A missing file is the normal "no maintenance" case, so keep it quiet.
            Plugin.Log.Debug(ex, "[Maintenance] notice fetch failed.");
            _notice = null;
        }
        finally
        {
            lock (_lock)
            {
                _fetching = false;
            }
        }
    }

    private static string FormatTimestamps(string text) => TimestampToken.Replace(text, m =>
    {
        if (long.TryParse(m.Groups[1].Value, out var unix))
        {
            try
            {
                return DateTimeOffset.FromUnixTimeSeconds(unix).ToLocalTime().ToString("f", CultureInfo.CurrentCulture);
            }
            catch (ArgumentOutOfRangeException)
            {
            }
        }
        return m.Value;
    });
}
