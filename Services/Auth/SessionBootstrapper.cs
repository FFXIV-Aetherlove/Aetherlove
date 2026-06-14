using System;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Config;
using AetherLove.Services.Hub;
using AetherLove.Services.Signal;
using AetherLove.Shared.Profile;
using Dalamud.Plugin.Services;

namespace AetherLove.Services.Auth;

/// <summary>Outcome of a session-resumption attempt.</summary>
public enum SessionBootstrapResult
{
    Pending = 0,
    NoSession = 1,
    SignedInOnboarding = 2,
    SignedInActive = 3,
    Banned = 4,
    OutdatedClient = 5,
}

/// <summary>Plugin-startup orchestrator: refreshes tokens, opens SignalR, resolves profile lifecycle.</summary>
public sealed class SessionBootstrapper
{
    private readonly IPluginLog _log;
    private readonly TokenService _tokens;
    private readonly AetherSignalService _signal;
    private readonly AetherLoveHubClient _hub;
    private readonly Configuration _config;
    private readonly NotificationCenter _notifications;
    private readonly Crypto.KeyStorageService _keys;

    private readonly object _gate = new();
    private Task<SessionBootstrapResult>? _inflight;
    private SessionBootstrapResult _lastResult = SessionBootstrapResult.Pending;
    private string? _lastDisplayName;
    private OnboardingStateDto? _lastOnboardingState;
    private AetherConnectionDto? _lastConnection;

    public SessionBootstrapper(
        IPluginLog log,
        TokenService tokens,
        AetherSignalService signal,
        AetherLoveHubClient hub,
        Configuration config,
        NotificationCenter notifications,
        Crypto.KeyStorageService keys)
    {
        _log = log;
        _tokens = tokens;
        _signal = signal;
        _hub = hub;
        _config = config;
        _notifications = notifications;
        _keys = keys;
    }

    public SessionBootstrapResult LastResult => _lastResult;

    public string? LastDisplayName => _lastDisplayName;

    public OnboardingStateDto? LastOnboardingState => _lastOnboardingState;

    /// <summary>One-shot read: returns the cached state and nulls the slot.</summary>
    public OnboardingStateDto? ConsumeOnboardingState()
    {
        var s = _lastOnboardingState;
        _lastOnboardingState = null;
        return s;
    }

    public AetherConnectionDto? LastConnection => _lastConnection;

    /// <summary>Updates the cached connection snapshot so push handlers (warning/ban) take effect without a reconnect.</summary>
    public void ReplaceConnectionSnapshot(AetherConnectionDto updated)
    {
        _lastConnection = updated;
    }

    /// <summary>Re-fetches the connection snapshot (warnings, new-match count, lifecycle/ban state) after a
    /// (re)connect, back-filling a snapshot the startup bootstrap missed on a flaky link. Best-effort: a
    /// failed fetch leaves the previous snapshot in place. Refreshes the cache only — it does not re-route.</summary>
    public async Task RefreshConnectionInfoAsync(CancellationToken ct = default)
    {
        try
        {
            var status = await _hub.GetConnectionInfoAsync(ct).ConfigureAwait(false);
            _lastConnection = status;
            _notifications.NewMatches = status.NewMatchCount;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[SessionBootstrapper] Connection-info refresh failed.");
        }
    }

    /// <summary>True when the server has a key bundle but this machine doesn't have the unwrapped private key.</summary>
    public bool NeedsPassphraseUnlock
    {
        get
        {
            var c = _lastConnection;
            if (c is null || !c.HasKeyBundle)
            {
                return false;
            }
            if (_lastResult is not (SessionBootstrapResult.SignedInActive
                                  or SessionBootstrapResult.SignedInOnboarding))
            {
                return false;
            }
            return !_keys.HasLocalKey;
        }
    }

    public bool HasUnseenWarnings
    {
        get
        {
            var c = _lastConnection;
            if (c is null)
            {
                return false;
            }
            for (int i = 0; i < c.Warnings.Length; i++)
            {
                if (!c.Warnings[i].Seen)
                {
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>Starts or returns the in-flight bootstrap task.</summary>
    public Task<SessionBootstrapResult> RunAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_inflight is not null && !_inflight.IsCompleted)
            {
                return _inflight;
            }
            _lastResult = SessionBootstrapResult.Pending;
            _inflight = Task.Run(() => RunCoreAsync(ct), ct);
            return _inflight;
        }
    }

    /// <summary>Drops the cached result so the next <see cref="RunAsync"/> call hits the server again.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _inflight = null;
            _lastResult = SessionBootstrapResult.Pending;
            _lastDisplayName = null;
            _lastOnboardingState = null;
            _lastConnection = null;
        }
    }

    private async Task<SessionBootstrapResult> RunCoreAsync(CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrEmpty(_config.Auth.RefreshToken))
            {
                return Settle(SessionBootstrapResult.NoSession, null);
            }

            if (_tokens.IsAccessTokenStale())
            {
                var refreshed = await _tokens.TryRefreshAsync(ct).ConfigureAwait(false);
                if (!refreshed)
                {
                    _log.Information("[SessionBootstrapper] Refresh failed; wiping tokens.");
                    _tokens.Clear();
                    return Settle(SessionBootstrapResult.NoSession, null);
                }
            }

            await _signal.EnsureConnectedAsync(ct).ConfigureAwait(false);
            if (!_signal.IsConnected)
            {
                if (_signal.LastFailureWasUnauthorized)
                {
                    // Token rejected (e.g. profile deleted server-side); force a fresh sign-in.
                    _log.Information("[SessionBootstrapper] Hub returned 401; wiping tokens and routing to sign-in.");
                    _tokens.Clear();
                    _keys.Clear();
                    return Settle(SessionBootstrapResult.NoSession, null);
                }

                // Network-level failure; keep tokens so a later retry can succeed.
                _log.Warning("[SessionBootstrapper] Hub failed to connect; falling back to onboarding.");
                return Settle(SessionBootstrapResult.NoSession, null);
            }

            var status = await _hub.GetConnectionInfoAsync(ct).ConfigureAwait(false);
            _lastConnection = status;
            _notifications.NewMatches = status.NewMatchCount;

            if (status.Status == ProfileLifecycle.Onboarding)
            {
                try
                {
                    _lastOnboardingState = await _hub.GetOnboardingStateAsync(ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _log.Warning(ex, "[SessionBootstrapper] GetOnboardingStateAsync failed; wizard will start blank.");
                    _lastOnboardingState = null;
                }
            }

            if (status.Status == ProfileLifecycle.Deleted)
            {
                _log.Information("[SessionBootstrapper] Profile tombstoned server-side; clearing local tokens.");
                _tokens.Clear();
                _keys.Clear();
                return Settle(SessionBootstrapResult.NoSession, null);
            }
            if (status.Status == ProfileLifecycle.Banned)
            {
                _log.Information("[SessionBootstrapper] Profile banned server-side; clearing local tokens.");
                _tokens.Clear();
                _keys.Clear();
                return Settle(SessionBootstrapResult.Banned, status.DisplayName);
            }

            return status.Status switch
            {
                ProfileLifecycle.Active => Settle(SessionBootstrapResult.SignedInActive, status.DisplayName),
                ProfileLifecycle.Onboarding => Settle(SessionBootstrapResult.SignedInOnboarding, status.DisplayName),
                ProfileLifecycle.ShadowBanned => Settle(SessionBootstrapResult.SignedInActive, status.DisplayName),
                _ => Settle(SessionBootstrapResult.SignedInOnboarding, status.DisplayName),
            };
        }
        catch (OperationCanceledException)
        {
            return Settle(SessionBootstrapResult.Pending, null);
        }
        catch (OutdatedClientException)
        {
            // Server rejected our API version. Drop the connection so nothing keeps talking to it; the
            // outdated screen is terminal. Tokens/keys are kept — this is a plugin-update problem, not auth.
            _log.Warning("[SessionBootstrapper] Server rejected plugin API version; client is outdated.");
            await _signal.DisconnectAsync().ConfigureAwait(false);
            return Settle(SessionBootstrapResult.OutdatedClient, null);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[SessionBootstrapper] Bootstrap failed.");
            return Settle(SessionBootstrapResult.NoSession, null);
        }
    }

    private SessionBootstrapResult Settle(SessionBootstrapResult result, string? displayName)
    {
        _lastResult = result;
        _lastDisplayName = displayName;
        return result;
    }
}
