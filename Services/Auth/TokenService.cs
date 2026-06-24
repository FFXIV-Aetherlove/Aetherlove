using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Config;
using AetherLove.Shared.Auth;
using Dalamud.Plugin.Services;

namespace AetherLove.Services.Auth;

/// <summary>Token storage and refresh against <c>POST /auth/refresh</c>.</summary>
public sealed class TokenService
{
    public static readonly TimeSpan RefreshSkew = TimeSpan.FromMinutes(1);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IPluginLog _log;
    private readonly Configuration _config;
    private readonly HttpClient _http;

    // Single-flight gate so concurrent callers don't rotate each other out.
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    public TokenService(IPluginLog log, Configuration config, HttpClient http)
    {
        _log = log;
        _config = config;
        _http = http;
    }

    /// <summary>True when the most recent <see cref="TryRefreshAsync"/> failed because the server actively
    /// rejected the refresh token (HTTP 401), as opposed to the server being unreachable. Lets the startup
    /// bootstrapper wipe tokens only on a real auth rejection, not when the server is merely down.</summary>
    public bool LastRefreshFailedUnauthorized { get; private set; }

    /// <summary>True when there is no access token or it expires within <see cref="RefreshSkew"/>.</summary>
    public bool IsAccessTokenStale()
    {
        var a = _config.Auth;
        if (string.IsNullOrEmpty(a.AccessToken))
        {
            return true;
        }
        return a.AccessTokenExpiresAtUtc - DateTimeOffset.UtcNow < RefreshSkew;
    }

    /// <summary>Persists a token pair and saves the config.</summary>
    public void ApplyTokens(TokenPairDto tokens)
    {
        _config.Auth = new AuthState
        {
            AccessToken = tokens.AccessToken,
            AccessTokenExpiresAtUtc = tokens.AccessTokenExpiresAtUtc,
            RefreshToken = tokens.RefreshToken,
            RefreshTokenExpiresAtUtc = tokens.RefreshTokenExpiresAtUtc,
        };
        _config.Save();
    }

    /// <summary>Wipes the stored tokens locally.</summary>
    public void Clear()
    {
        _config.Auth = new AuthState();
        _config.Save();
    }

    /// <summary>Exchanges the refresh token for a fresh pair. Returns false on failure or no stored token.</summary>
    public async Task<bool> TryRefreshAsync(CancellationToken ct)
    {
        LastRefreshFailedUnauthorized = false;
        var refreshToken = _config.Auth.RefreshToken;
        if (string.IsNullOrEmpty(refreshToken))
        {
            return false;
        }

        await _refreshGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!IsAccessTokenStale())
            {
                return true;
            }

            refreshToken = _config.Auth.RefreshToken;
            if (string.IsNullOrEmpty(refreshToken))
            {
                return false;
            }

            using var resp = await _http.PostAsJsonAsync(
                "auth/refresh",
                new RefreshRequest(refreshToken),
                JsonOptions,
                ct).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                if (resp.StatusCode == HttpStatusCode.Unauthorized)
                {
                    LastRefreshFailedUnauthorized = true;
                }
                _log.Warning($"[TokenService] /auth/refresh returned {(int)resp.StatusCode}.");
                return false;
            }

            var tokens = await resp.Content
                .ReadFromJsonAsync<TokenPairDto>(JsonOptions, ct)
                .ConfigureAwait(false);

            if (tokens is null)
            {
                return false;
            }

            ApplyTokens(tokens);
            _log.Information("[TokenService] Access token refreshed.");
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[TokenService] Refresh failed.");
            return false;
        }
        finally
        {
            _refreshGate.Release();
        }
    }
}
