using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AetherLove.Services.Realtor;

/// <summary>Raw PaissaDB HTTP client (the crowdsourced housing database behind PaissaHouse). One request
/// at a time with enforced spacing, a cooldown after repeated failures, and null (never throw) on any
/// error. Callers should prefer <see cref="RealtorDataService"/>, which adds caching.</summary>
public sealed class PaissaClient
{
    private const string BaseUrl = "https://paissadb.zhu.codes";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan MinSpacing = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan FailureCooldown = TimeSpan.FromSeconds(60);

    private static readonly HttpClient Http = CreateClient();
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _lastRequestAt = DateTimeOffset.MinValue;
    private int _consecutiveFailures;
    private DateTimeOffset _pausedUntil = DateTimeOffset.MinValue;

    public bool IsPaused => DateTimeOffset.UtcNow < _pausedUntil;

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        var version = "dev";
        try
        {
            version = UiHost.PluginInterface.Manifest.AssemblyVersion.ToString();
        }
        catch
        {
            // UiHost may not be initialised in tests; the fallback UA is fine there.
        }
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", $"AetherOS-Realtor/{version}");
        return client;
    }

    public Task<List<PaissaWorldSummary>?> GetWorldsAsync(CancellationToken ct) =>
        GetAsync<List<PaissaWorldSummary>>("/worlds", ct);

    public Task<PaissaWorldDetail?> GetWorldAsync(int worldId, CancellationToken ct) =>
        GetAsync<PaissaWorldDetail>($"/worlds/{worldId}", ct);

    public Task<PaissaDistrict?> GetDistrictAsync(int worldId, int districtId, CancellationToken ct) =>
        GetAsync<PaissaDistrict>($"/worlds/{worldId}/{districtId}", ct);

    private async Task<T?> GetAsync<T>(string path, CancellationToken ct) where T : class
    {
        if (IsPaused)
        {
            return null;
        }
        try
        {
            await _gate.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        try
        {
            var wait = _lastRequestAt + MinSpacing - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, ct).ConfigureAwait(false);
            }
            _lastRequestAt = DateTimeOffset.UtcNow;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(RequestTimeout);
            using var response = await Http.GetAsync(BaseUrl + path, cts.Token).ConfigureAwait(false);
            if ((int)response.StatusCode == 429)
            {
                _pausedUntil = DateTimeOffset.UtcNow + FailureCooldown;
                return null;
            }
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
            {
                _consecutiveFailures = 0;
                return null;
            }
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
            var value = await JsonSerializer.DeserializeAsync<T>(stream, JsonOpts, cts.Token).ConfigureAwait(false);
            _consecutiveFailures = 0;
            return value;
        }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
            {
                if (++_consecutiveFailures >= 3)
                {
                    _pausedUntil = DateTimeOffset.UtcNow + FailureCooldown;
                    _consecutiveFailures = 0;
                }
                UiHost.Log.Debug($"[Paissa] GET {path} failed: {ex.Message}");
            }
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }
}
