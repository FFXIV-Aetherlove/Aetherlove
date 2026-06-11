using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Config;
using AetherLove.Services.Signal;
using AetherLove.Shared.Auth;
using Dalamud.Plugin.Services;

namespace AetherLove.Services.Auth;

/// <summary>State of the XIVAuth sign-in polling flow.</summary>
public enum AuthFlowState
{
    Idle = 0,
    Starting = 1,
    AwaitingBrowser = 2,
    Completed = 3,
    Failed = 4,
}

/// <summary>Drives the XIVAuth sign-in flow: start transaction, open browser, poll, persist tokens.</summary>
public sealed class AuthService
{
    private readonly IPluginLog _log;
    private readonly Configuration _config;
    private readonly HttpClient _http;
    private readonly TokenService _tokens;
    private readonly AetherSignalService _signal;

    private volatile int _stateRaw;
    private volatile string? _loginUrl;
    private volatile string? _errorMessage;
    private volatile bool _lastFailureWasExpiry;

    private CancellationTokenSource? _cts;
    private Task? _flowTask;
    private readonly object _flowLock = new();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    public AuthService(IPluginLog log, Configuration config, HttpClient http,
                       TokenService tokens, AetherSignalService signal)
    {
        _log = log;
        _config = config;
        _http = http;
        _tokens = tokens;
        _signal = signal;
    }

    public AuthFlowState State => (AuthFlowState)_stateRaw;
    public string? LoginUrl => _loginUrl;
    public string? ErrorMessage => _errorMessage;
    public bool LastFailureWasExpiry => _lastFailureWasExpiry;

    /// <summary>Begins a new sign-in flow, cancelling any in-flight one.</summary>
    public void StartSignIn()
    {
        lock (_flowLock)
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            _loginUrl = null;
            _errorMessage = null;
            _lastFailureWasExpiry = false;
            SetState(AuthFlowState.Starting);

            var deviceId = GetOrCreateDeviceId();
            _log.Information("[XIVAuth] Starting sign-in flow. DeviceId={DeviceId}", deviceId);

            var ct = _cts.Token;
            _flowTask = Task.Run(() => RunFlowAsync(ct), ct);
        }
    }

    /// <summary>Cancels the in-flight sign-in flow and resets state to Idle. Idempotent.</summary>
    public void Cancel()
    {
        lock (_flowLock)
        {
            _cts?.Cancel();
            _cts = null;
        }

        _loginUrl = null;
        _errorMessage = null;
        _lastFailureWasExpiry = false;
        _log.Information("[XIVAuth] Sign-in cancelled by user.");
        SetState(AuthFlowState.Idle);
    }

    /// <summary>Reopens the browser at the current <see cref="LoginUrl"/>. No-op if not set.</summary>
    public void ReopenBrowser()
    {
        var url = _loginUrl;
        if (string.IsNullOrEmpty(url))
        {
            _log.Information("[XIVAuth] ReopenBrowser called but no login URL is set.");
            return;
        }
        _log.Information("[XIVAuth] User requested browser re-open. Url={Url}", url);
        TryOpenBrowser(url);
    }

    private async Task RunFlowAsync(CancellationToken ct)
    {
        try
        {
            _log.Information("[XIVAuth] Contacting server to start transaction.");
            LoginStartResponse start;
            try
            {
                using var resp = await _http.PostAsJsonAsync(
                    "auth/login/start",
                    new LoginStartRequest(GetOrCreateDeviceId()),
                    JsonOptions,
                    ct);

                resp.EnsureSuccessStatusCode();
                start = (await resp.Content.ReadFromJsonAsync<LoginStartResponse>(JsonOptions, ct))
                        ?? throw new InvalidOperationException("Empty body from /auth/login/start");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.Warning(ex, "[XIVAuth] Failed to reach server during login/start.");
                Fail($"Could not reach the AetherLove server: {ex.Message}", expiry: false);
                return;
            }

            _log.Information("[XIVAuth] Transaction started. TransactionId={TransId}, LoginUrl={Url}, ExpiresAt={Expires}",
                start.TransactionId, start.LoginUrl, start.ExpiresAtUtc);

            _loginUrl = start.LoginUrl;
            SetState(AuthFlowState.AwaitingBrowser);
            _log.Information("[XIVAuth] Opening browser for user authentication.");
            TryOpenBrowser(start.LoginUrl);

            _log.Information("[XIVAuth] Entering poll loop. Poll interval={Interval}s, Expiry={Expiry}",
                PollInterval.TotalSeconds, start.ExpiresAtUtc);

            while (!ct.IsCancellationRequested)
            {
                if (DateTimeOffset.UtcNow >= start.ExpiresAtUtc)
                {
                    _log.Information("[XIVAuth] Transaction expired. Failing flow.");
                    Fail("Sign-in took too long. Please try again.", expiry: true);
                    return;
                }

                try
                {
                    using var resp = await _http.PostAsJsonAsync(
                        "auth/login/poll",
                        new LoginPollRequest(start.TransactionId, start.TransactionSecret),
                        JsonOptions,
                        ct);

                    if (resp.StatusCode == HttpStatusCode.Accepted)
                    {
                        // 202 = still awaiting the user's XIVAuth flow. Fall through to the poll delay.
                        _log.Information("[XIVAuth] Poll: still awaiting user action in browser (202).");
                    }
                    else if (resp.IsSuccessStatusCode)
                    {
                        var body = await resp.Content.ReadFromJsonAsync<LoginPollResponse>(JsonOptions, ct);
                        if (body?.Status == "completed" && body.Tokens is not null)
                        {
                            _log.Information("[XIVAuth] Poll: authentication completed. Applying tokens.");
                            ApplyTokens(body.Tokens);
                            SetState(AuthFlowState.Completed);
                            return;
                        }

                        _log.Warning("[XIVAuth] Poll: unexpected completed response. Status={Status}, Error={Error}",
                            body?.Status ?? "(null)", body?.Error ?? "(none)");
                        Fail(body?.Error ?? "Unexpected response from the server.", expiry: false);
                        return;
                    }
                    else
                    {
                        string? serverError = null;
                        try
                        {
                            var body = await resp.Content.ReadFromJsonAsync<LoginPollResponse>(JsonOptions, ct);
                            serverError = body?.Error;
                        }
                        catch
                        {
                        }

                        _log.Warning("[XIVAuth] Poll: server returned error. StatusCode={StatusCode}, Error={Error}",
                            (int)resp.StatusCode, serverError ?? "(none)");
                        Fail(serverError ?? $"Sign-in failed ({(int)resp.StatusCode}).", expiry: false);
                        return;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _log.Warning(ex, "[XIVAuth] Poll: lost contact with server.");
                    Fail($"Lost contact with the AetherLove server: {ex.Message}", expiry: false);
                    return;
                }

                try
                {
                    await Task.Delay(PollInterval, ct);
                }
                catch (OperationCanceledException)
                {
                }
            }

            _log.Information("[XIVAuth] Poll loop exited due to cancellation.");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log.Error(ex, "[AuthService] Unexpected flow error.");
            Fail($"Unexpected error: {ex.Message}", expiry: false);
        }
    }

    private void ApplyTokens(TokenPairDto tokens)
    {
        _tokens.ApplyTokens(tokens);
        _log.Information("[XIVAuth] Tokens stored. Triggering SignalR connection.");
        _ = Task.Run(() => _signal.EnsureConnectedAsync());
    }

    private void Fail(string message, bool expiry)
    {
        _log.Warning("[XIVAuth] Flow failed. Expiry={Expiry}, Message={Message}", expiry, message);
        _errorMessage = message;
        _lastFailureWasExpiry = expiry;
        SetState(AuthFlowState.Failed);
    }

    private void SetState(AuthFlowState state) => Interlocked.Exchange(ref _stateRaw, (int)state);

    private void TryOpenBrowser(string url)
    {
        _log.Information("[XIVAuth] Attempting to open browser. Url={Url}", url);
        try
        {
            var psi = new ProcessStartInfo(url) { UseShellExecute = true };
            _log.Information("[XIVAuth] ProcessStartInfo created. UseShellExecute={UseShellExecute}", psi.UseShellExecute);
            var proc = Process.Start(psi);
            if (proc is not null)
            {
                _log.Information("[XIVAuth] Browser process started. PID={Pid}", proc.Id);
            }
            else
            {
                _log.Warning("[XIVAuth] Process.Start returned null — browser may not have opened.");
            }
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[XIVAuth] Failed to launch browser. Url={Url}", url);
        }
    }

    /// <summary>Returns the persisted device id, generating one if missing.</summary>
    private string GetOrCreateDeviceId()
    {
        if (!string.IsNullOrEmpty(_config.DeviceId))
        {
            return _config.DeviceId;
        }

        // Crockford alphabet (no 0/O/1/I).
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        Span<byte> bytes = stackalloc byte[6];
        RandomNumberGenerator.Fill(bytes);

        var sb = new StringBuilder("AetherLove-Plugin-", 6 + 18);
        for (int i = 0; i < bytes.Length; i++)
        {
            sb.Append(alphabet[bytes[i] % alphabet.Length]);
        }

        _config.DeviceId = sb.ToString();
        _config.Save();
        return _config.DeviceId;
    }
}
