using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Config;
using AetherLove.Navigation;
using AetherLove.Services.Hub;
using AetherLove.Services.Signal;
using AetherLove.Shared.News;
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

    /// <summary>Have a session (tokens present) but the server can't be reached. Tokens are kept; the user is
    /// shown the Offline screen, which retries the bootstrap rather than dropping them into onboarding.</summary>
    ServerUnreachable = 6,
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
    private readonly ScreenRouter _router;

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
        Crypto.KeyStorageService keys,
        ScreenRouter router)
    {
        _log = log;
        _tokens = tokens;
        _signal = signal;
        _hub = hub;
        _config = config;
        _notifications = notifications;
        _keys = keys;
        _router = router;
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
    /// failed fetch leaves the previous snapshot in place. If the server rejects the client's API version (it
    /// bumped while the client was connected), this drops the connection and routes to the terminal update
    /// screen, so a reconnected old client is evicted instead of lingering unprompted.</summary>
    public async Task RefreshConnectionInfoAsync(CancellationToken ct = default)
    {
        try
        {
            var status = await _hub.GetConnectionInfoAsync(ct).ConfigureAwait(false);
            _lastConnection = status;
            _notifications.NewMatches = status.NewMatchCount;
        }
        catch (OutdatedClientException)
        {
            _log.Warning("[SessionBootstrapper] Server rejected plugin API version on refresh; client is outdated.");
            await _signal.DisconnectAsync().ConfigureAwait(false);
            Settle(SessionBootstrapResult.OutdatedClient, null);
            _router.Navigate(Screen.Outdated);
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

    /// <summary>True when the user is signed in and Active but the server has no key bundle at all (e.g. an
    /// account that re-registered after deletion, completing onboarding without establishing encryption). They
    /// can't message and have no in-app way to fix it, so the startup ladder routes them to a one-time setup.</summary>
    public bool NeedsEncryptionRecovery
    {
        get
        {
            var c = _lastConnection;
            if (c is null || c.HasKeyBundle)
            {
                return false;
            }
            return _lastResult == SessionBootstrapResult.SignedInActive;
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

    public bool HasUnseenModeratorMessages
    {
        get
        {
            var c = _lastConnection;
            // Null-guarded for version skew: a new client briefly talking to a server without this field.
            var messages = c?.ModeratorMessages;
            if (messages is null)
            {
                return false;
            }
            for (int i = 0; i < messages.Length; i++)
            {
                if (!messages[i].Seen)
                {
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>True when the connection snapshot carries at least one unseen published news item.</summary>
    public bool HasUnseenNews => (_lastConnection?.UnseenNews?.Length ?? 0) > 0;

    /// <summary>The next screen in the startup gate order — outdated/banned (terminal) → warnings → moderator
    /// messages → passphrase → news → the regular target. Every startup gate screen funnels onward through this,
    /// so the order lives in one place; each gate clears its own condition in the cached snapshot before
    /// calling it again.</summary>
    public Screen ResolveNextStartupScreen()
    {
        // ServerUnreachable (and a still-Pending result reached via the splash timeout) hold on the Offline
        // screen, which retries the bootstrap, rather than dropping a returning user into onboarding.
        if (_lastResult is SessionBootstrapResult.ServerUnreachable or SessionBootstrapResult.Pending)
        {
            return Screen.Offline;
        }
        if (_lastResult == SessionBootstrapResult.OutdatedClient)
        {
            return Screen.Outdated;
        }
        if (_lastResult == SessionBootstrapResult.Banned)
        {
            return Screen.Banned;
        }
        if (HasUnseenWarnings && _lastResult is SessionBootstrapResult.SignedInActive
                                              or SessionBootstrapResult.SignedInOnboarding)
        {
            return Screen.WarningsAcknowledge;
        }
        if (HasUnseenModeratorMessages && _lastResult is SessionBootstrapResult.SignedInActive
                                                       or SessionBootstrapResult.SignedInOnboarding)
        {
            return Screen.ModeratorMessages;
        }
        if (NeedsPassphraseUnlock)
        {
            return Screen.PassphraseUnlock;
        }
        if (NeedsEncryptionRecovery)
        {
            return Screen.EncryptionRecovery;
        }
        if (HasUnseenNews && _lastResult == SessionBootstrapResult.SignedInActive)
        {
            return Screen.News;
        }
        return _lastResult == SessionBootstrapResult.SignedInActive
            ? Screen.Deck
            : Screen.Onboarding;
    }

    /// <summary>Drops the given news ids from the cached unseen list so the news gate clears without a reconnect.</summary>
    public void MarkNewsSeenInSnapshot(IReadOnlyCollection<Guid> seenIds)
    {
        var c = _lastConnection;
        if (c is null || c.UnseenNews.Length == 0 || seenIds.Count == 0)
        {
            return;
        }
        var remaining = c.UnseenNews.Where(n => !seenIds.Contains(n.Id)).ToArray();
        if (remaining.Length != c.UnseenNews.Length)
        {
            _lastConnection = c with { UnseenNews = remaining };
        }
    }

    /// <summary>Adds a freshly-published news item to the cached unseen list (idempotent) so a live push
    /// surfaces it without a reconnect.</summary>
    public void AppendNewsToSnapshot(NewsSummaryDto summary)
    {
        var c = _lastConnection;
        if (c is null || c.UnseenNews.Any(n => n.Id == summary.Id))
        {
            return;
        }
        _lastConnection = c with { UnseenNews = c.UnseenNews.Append(summary).ToArray() };
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
                    if (_tokens.LastRefreshFailedUnauthorized)
                    {
                        _log.Information("[SessionBootstrapper] Refresh rejected (401); wiping tokens.");
                        _tokens.Clear();
                        return Settle(SessionBootstrapResult.NoSession, null);
                    }
                    // Server down/unreachable, not a real auth rejection: keep tokens so a retry resumes the session.
                    _log.Warning("[SessionBootstrapper] Refresh failed; server unreachable, keeping tokens.");
                    return Settle(SessionBootstrapResult.ServerUnreachable, null);
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

                // Network-level failure; keep tokens and show Offline so a later retry resumes the session.
                _log.Warning("[SessionBootstrapper] Hub unreachable; keeping tokens, showing offline.");
                return Settle(SessionBootstrapResult.ServerUnreachable, null);
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
            // A refresh token is still present here (the no-token case returned earlier), so treat any
            // unexpected failure as the server being unreachable rather than wiping the session to onboarding.
            _log.Warning(ex, "[SessionBootstrapper] Bootstrap failed; treating as server-unreachable.");
            return Settle(SessionBootstrapResult.ServerUnreachable, null);
        }
    }

    private SessionBootstrapResult Settle(SessionBootstrapResult result, string? displayName)
    {
        _lastResult = result;
        _lastDisplayName = displayName;
        return result;
    }
}
