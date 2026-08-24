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

    /// <summary>True when the last refresh was actively rejected (HTTP 401), as opposed to the server being unreachable.</summary>
    public bool LastRefreshFailedUnauthorized { get; private set; }

    /// <summary>The refresh token itself is dead: the server rejected it outright, so nothing this client
    /// holds can reach the hub again and no amount of retrying will change that. Unlike
    /// <see cref="LastRefreshFailedUnauthorized"/> this STICKS, because the reconnect loop calls refresh
    /// again and again and each call clears that flag on entry; without a sticky answer the phone spends
    /// the rest of the session showing "offline" for a session that simply ended. Cleared by a successful
    /// refresh, a fresh sign-in, and <see cref="Clear"/>.</summary>
    public bool SessionExpired { get; private set; }

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

    public void ApplyTokens(TokenPairDto tokens)
    {
        SessionExpired = false;
        _config.Auth = new AuthState
        {
            AccessToken = tokens.AccessToken,
            AccessTokenExpiresAtUtc = tokens.AccessTokenExpiresAtUtc,
            RefreshToken = tokens.RefreshToken,
            RefreshTokenExpiresAtUtc = tokens.RefreshTokenExpiresAtUtc,
            ActiveProfileId = _config.Auth.ActiveProfileId,
        };
        _config.Save();
    }

    /// <summary>Applies a profile-switch access token (from <c>SelectProfileAsync</c>): swaps the access token
    /// and the active-profile selection, keeping the refresh session untouched.</summary>
    public void ApplyAccessToken(AccessTokenDto token, Guid activeProfileId)
    {
        _config.Auth.AccessToken = token.AccessToken;
        _config.Auth.AccessTokenExpiresAtUtc = token.AccessTokenExpiresAtUtc;
        _config.Auth.ActiveProfileId = activeProfileId;
        _config.Save();
    }

    public void Clear()
    {
        SessionExpired = false;
        _config.Auth = new AuthState();
        _config.Save();
    }

    /// <summary>Exchanges the refresh token for a fresh pair. <paramref name="force"/> refreshes even when the
    /// token still looks fresh locally, for callers that just saw the server reject it (e.g. clock skew).</summary>
    public async Task<bool> TryRefreshAsync(CancellationToken ct, bool force = false)
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
            if (!force && !IsAccessTokenStale())
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
                new RefreshRequest(refreshToken, _config.Auth.ActiveProfileId),
                JsonOptions,
                ct).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                if (resp.StatusCode == HttpStatusCode.Unauthorized)
                {
                    LastRefreshFailedUnauthorized = true;
                    SessionExpired = true;
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
