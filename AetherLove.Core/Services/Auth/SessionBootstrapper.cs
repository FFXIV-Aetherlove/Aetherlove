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

    /// <summary>Tokens present but the server can't be reached; tokens are kept and the Offline screen retries.</summary>
    ServerUnreachable = 6,
}

/// <summary>Plugin-startup orchestrator: refreshes tokens, opens SignalR, resolves profile lifecycle.</summary>
public sealed class SessionBootstrapper : IDisposable
{
    private readonly IPluginLog _log;
    private readonly TokenService _tokens;
    private readonly AetherSignalService _signal;
    private readonly AetherHubContext _hub;
    private readonly Configuration _config;
    private readonly NotificationCenter _notifications;
    private readonly Crypto.KeyStorageService _keys;
    private readonly Crypto.CryptoService _crypto;
    private readonly ScreenRouter _router;
    private readonly Chat.ChatCacheStore _chatCache;
    private readonly Hangouts.HangoutStateService _hangouts;
    private readonly Messenger.MessengerSyncService _messengerSync;
    private readonly SiblingBadgeStore _siblingBadges;
    private readonly OwnAvatarCache _ownAvatar;
    private readonly OsAvatarCache _osAvatar;

    private readonly object _gate = new();
    private readonly CancellationTokenSource _retryCts = new();
    private int _offlineRetryActive;
    private static readonly TimeSpan OfflineBootRetryInterval = TimeSpan.FromSeconds(10);
    private Task<SessionBootstrapResult>? _inflight;
    private SessionBootstrapResult _lastResult = SessionBootstrapResult.Pending;
    private string? _lastDisplayName;
    private OnboardingStateDto? _lastOnboardingState;
    private AetherConnectionDto? _lastConnection;
    private AetherAccountInfoDto? _lastAccount;

    public SessionBootstrapper(
        IPluginLog log,
        TokenService tokens,
        AetherSignalService signal,
        AetherHubContext hub,
        Configuration config,
        NotificationCenter notifications,
        Crypto.KeyStorageService keys,
        Crypto.CryptoService crypto,
        ScreenRouter router,
        Chat.ChatCacheStore chatCache,
        Hangouts.HangoutStateService hangouts,
        Messenger.MessengerSyncService messengerSync,
        SiblingBadgeStore siblingBadges,
        OwnAvatarCache ownAvatar,
        OsAvatarCache osAvatar)
    {
        _log = log;
        _tokens = tokens;
        _signal = signal;
        _hub = hub;
        _config = config;
        _notifications = notifications;
        _keys = keys;
        _crypto = crypto;
        _router = router;
        _chatCache = chatCache;
        _hangouts = hangouts;
        _messengerSync = messengerSync;
        _siblingBadges = siblingBadges;
        _ownAvatar = ownAvatar;
        _osAvatar = osAvatar;
    }

    public SessionBootstrapResult LastResult => _lastResult;

    public string? LastDisplayName => _lastDisplayName;

    /// <summary>One-shot read: returns the cached state and nulls the slot.</summary>
    public OnboardingStateDto? ConsumeOnboardingState()
    {
        var s = _lastOnboardingState;
        _lastOnboardingState = null;
        return s;
    }

    public AetherConnectionDto? LastConnection => _lastConnection;

    /// <summary>Account-level snapshot for the OS shell, fetched alongside the connection info. Null until the
    /// first successful bootstrap, or if the account fetch failed (non-fatal to the AetherLove session).</summary>
    public AetherAccountInfoDto? LastAccount => _lastAccount;

    public void ReplaceConnectionSnapshot(AetherConnectionDto updated)
    {
        _lastConnection = updated;
    }

    /// <summary>Clears the OS-onboarding gate in the cached account snapshot once the flow completes, so the
    /// re-run of the startup ladder falls through to Home/Deck instead of looping.</summary>
    public void MarkOsOnboardedInSnapshot()
    {
        if (_lastAccount is { } account)
        {
            _lastAccount = account with { OsOnboarded = true };
        }
    }

    /// <summary>Applies an AccountDisabled push to the cached account snapshot so the shell gate on server-backed
    /// apps flips on the next frame. A null reason lifts the ban.</summary>
    public void ApplyAccountDisabledToSnapshot(string? reason)
    {
        if (_lastAccount is { } account)
        {
            _lastAccount = account with { AccountDisabled = reason is not null, AccountDisabledReason = reason };
        }
    }

    public async Task RefreshConnectionInfoAsync(CancellationToken ct = default)
    {
        try
        {
            var status = await _hub.GetConnectionInfoAsync(ct).ConfigureAwait(false);
            _lastConnection = status;
            _notifications.NewMatches = status.NewMatchCount;
            await SyncHangoutsAsync(status, ct).ConfigureAwait(false);
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

    /// <summary>True when signed in and Active but the server has no key bundle at all. A tombstoned acting
    /// profile (account with no live profiles) never matches; the profile picker handles that state.</summary>
    public bool NeedsEncryptionRecovery
    {
        get
        {
            var c = _lastConnection;
            if (c is null || c.HasKeyBundle || c.Status == ProfileLifecycle.Deleted)
            {
                return false;
            }
            return _lastResult == SessionBootstrapResult.SignedInActive;
        }
    }

    /// <summary>True when a signed-in account still owes the OS onboarding flow. When the account snapshot is
    /// available it is authoritative (<c>OsOnboarded</c> is stamped after the passphrase step, and a second
    /// profile legitimately has no key bundle yet, so the bundle must not gate an onboarded account). Without a
    /// snapshot (the account fetch is best-effort and can fail transiently or on server version skew) it falls
    /// back to the per-profile HasKeyBundle heuristic: a set-up user has a bundle and is never dragged back in,
    /// while a genuinely un-set-up user has none and is caught. Consulted only after the passphrase-unlock and
    /// encryption-recovery gates, which cover a set-up account arriving on a new device.</summary>
    public bool NeedsOsSetup =>
        _lastResult is SessionBootstrapResult.SignedInActive or SessionBootstrapResult.SignedInOnboarding
        && (_lastAccount is { } account
            ? !account.OsOnboarded
            : _lastConnection?.HasKeyBundle != true);

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

    /// <summary>The next screen in the startup gate order; each gate clears its condition in the cached snapshot, then calls this again.</summary>
    public Screen ResolveNextStartupScreen()
    {
        // Offline boot on a set-up device (stored tokens) lands on the usable Home: the OS and its
        // offline-capable apps keep working, connection-needing apps gate on the offline panel, and the
        // background retry brings the session up on its own. Without tokens there is nothing to use
        // offline, so a still-Pending splash timeout or unreachable server holds on Offline.
        if (_lastResult is SessionBootstrapResult.ServerUnreachable or SessionBootstrapResult.Pending)
        {
            if (string.IsNullOrEmpty(_config.Auth.RefreshToken))
            {
                return Screen.Offline;
            }
            EnsureOfflineBootRetry();
            return Screen.Home;
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
        if (NeedsOsSetup)
        {
            return Screen.OsOnboarding;
        }
        if (_lastResult is SessionBootstrapResult.SignedInActive
                        or SessionBootstrapResult.SignedInOnboarding)
        {
            return Screen.Home;
        }
        // No local session (fresh install or signed out): OS onboarding hosts the XIVAuth sign-in.
        return Screen.OsOnboarding;
    }

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

    public void AppendNewsToSnapshot(NewsSummaryDto summary)
    {
        var c = _lastConnection;
        if (c is null || c.UnseenNews.Any(n => n.Id == summary.Id))
        {
            return;
        }
        _lastConnection = c with { UnseenNews = c.UnseenNews.Append(summary).ToArray() };
    }

    /// <summary>Applies the startup ladder after a deferred (offline-boot) bootstrap completes: gate screens
    /// always take over, but a plain Home resolution leaves the user wherever they are, unless they still sit
    /// on a startup route that must move on.</summary>
    public void ApplyDeferredStartupRouting()
    {
        var next = ResolveNextStartupScreen();
        if (next == Screen.Home && _router.Current is not (Screen.Offline or Screen.Splash))
        {
            return;
        }
        _router.Navigate(next);
    }

    /// <summary>Keeps re-running the bootstrap after an offline boot landed on Home, so the session comes up
    /// on its own without the user ever opening a gated app.</summary>
    private void EnsureOfflineBootRetry()
    {
        if (Interlocked.Exchange(ref _offlineRetryActive, 1) == 1)
        {
            return;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                while (!_retryCts.IsCancellationRequested)
                {
                    await Task.Delay(OfflineBootRetryInterval, _retryCts.Token).ConfigureAwait(false);
                    var result = _lastResult is SessionBootstrapResult.ServerUnreachable or SessionBootstrapResult.Pending
                        ? await RunAsync(_retryCts.Token).ConfigureAwait(false)
                        : _lastResult;
                    if (result is SessionBootstrapResult.ServerUnreachable or SessionBootstrapResult.Pending)
                    {
                        continue;
                    }
                    ApplyDeferredStartupRouting();
                    return;
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "[SessionBootstrapper] Offline-boot retry loop failed.");
            }
            finally
            {
                Interlocked.Exchange(ref _offlineRetryActive, 0);
            }
        });
    }

    public void Dispose() => _retryCts.Cancel();

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

    public void Reset()
    {
        lock (_gate)
        {
            _inflight = null;
            _lastResult = SessionBootstrapResult.Pending;
            _lastDisplayName = null;
            _lastOnboardingState = null;
            _lastConnection = null;
            _lastAccount = null;
        }
        // The clearcache flow wipes the on-disk avatar caches before re-bootstrapping; drop the in-memory copies
        // too so the connect-time refresh above re-fetches instead of keeping a texture whose file is now gone.
        _ownAvatar.Invalidate();
        _osAvatar.Invalidate();
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
                    _log.Warning("[SessionBootstrapper] Refresh failed; server unreachable, keeping tokens.");
                    return Settle(SessionBootstrapResult.ServerUnreachable, null);
                }
            }

            await _signal.EnsureConnectedAsync(ct).ConfigureAwait(false);
            if (!_signal.IsConnected && _signal.LastFailureWasUnauthorized)
            {
                // A 401 with a locally-fresh token can be clock skew or a rotated signing key; retry once before wiping.
                _log.Information("[SessionBootstrapper] Hub returned 401; forcing a token refresh and retrying.");
                var refreshed = await _tokens.TryRefreshAsync(ct, force: true).ConfigureAwait(false);
                if (refreshed)
                {
                    await _signal.EnsureConnectedAsync(ct).ConfigureAwait(false);
                }
                else if (!_tokens.LastRefreshFailedUnauthorized)
                {
                    _log.Warning("[SessionBootstrapper] Forced refresh failed; server unreachable, keeping tokens.");
                    return Settle(SessionBootstrapResult.ServerUnreachable, null);
                }
            }
            if (!_signal.IsConnected)
            {
                if (_signal.LastFailureWasUnauthorized)
                {
                    _log.Information("[SessionBootstrapper] Hub returned 401; wiping tokens and routing to sign-in.");
                    _tokens.Clear();
                    _keys.Clear();
                    return Settle(SessionBootstrapResult.NoSession, null);
                }

                _log.Warning("[SessionBootstrapper] Hub unreachable; keeping tokens, showing offline.");
                return Settle(SessionBootstrapResult.ServerUnreachable, null);
            }

            var status = await _hub.GetConnectionInfoAsync(ct).ConfigureAwait(false);

            // The acting profile can be a tombstone (deleted from another device, or just deleted here).
            // Force a refresh (the server ignores a deleted selection and falls back to the account's free
            // profile) and retry once; a still-Deleted status means the account has no live profiles left.
            // The stale ActiveProfileId is kept so AdoptActiveProfile below stashes flat state under it.
            if (status.Status == ProfileLifecycle.Deleted)
            {
                _log.Information("[SessionBootstrapper] Acting profile is tombstoned; retrying as the account fallback.");
                if (await _tokens.TryRefreshAsync(ct, force: true).ConfigureAwait(false))
                {
                    await _signal.DisconnectAsync().ConfigureAwait(false);
                    await _signal.EnsureConnectedAsync(ct).ConfigureAwait(false);
                    if (_signal.IsConnected)
                    {
                        status = await _hub.GetConnectionInfoAsync(ct).ConfigureAwait(false);
                    }
                }
            }

            _lastConnection = status;
            _notifications.NewMatches = status.NewMatchCount;
            _log.Debug("[PSW] RunCore: server resolved acting profile {Profile:N} ({Name}, status {Status}); ActiveProfileId={Active:N}, cache owner {Owner:N}.", status.ProfileId, status.DisplayName, status.Status, _config.Auth.ActiveProfileId ?? Guid.Empty, _chatCache.Owner);
            AdoptActiveProfile(status.ProfileId);
            _chatCache.EnsureOwner(status.ProfileId);
            await FetchAccountInfoAsync(ct).ConfigureAwait(false);
            await SyncHangoutsAsync(status, ct).ConfigureAwait(false);
            await TryAutoUnlockAsync(ct).ConfigureAwait(false);
            await TryAutoProvisionAsync(ct).ConfigureAwait(false);
            // Fire-and-forget: messenger state is account-level and non-blocking for the profile session.
            _ = _messengerSync.SyncAsync(CancellationToken.None);

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
                // No live profiles left. The account session stays signed in; the AetherLove entry surfaces
                // the profile picker with just the create slot.
                _log.Information("[SessionBootstrapper] Account has no live profiles; keeping the session.");
                return Settle(SessionBootstrapResult.SignedInOnboarding, null);
            }
            if (status.Status == ProfileLifecycle.Banned)
            {
                // Per-profile ban: keep the account session and keys so the user can switch to a sibling profile
                // (the server enforces the ban via BanAwareHubFilter). The banned screen keeps the home indicator
                // so the user can leave and pick another profile, rather than being locked out of the whole OS.
                _log.Information("[SessionBootstrapper] Active profile banned server-side; keeping the account session.");
                return Settle(SessionBootstrapResult.Banned, status.DisplayName);
            }

            // Connected on a real profile: pull the nav + OS avatars if they're cold. This covers a same-profile
            // relog (where AdoptActiveProfile skips its refresh) and a clearcache (where the disk was wiped and
            // the in-memory textures were invalidated), so the avatars never linger on the generic fallback.
            _ownAvatar.Refresh(onlyIfCold: true);
            _osAvatar.Refresh(onlyIfCold: true);

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
            // Tokens and keys stay: an outdated client is an update problem, not an auth failure.
            _log.Warning("[SessionBootstrapper] Server rejected plugin API version; client is outdated.");
            await _signal.DisconnectAsync().ConfigureAwait(false);
            return Settle(SessionBootstrapResult.OutdatedClient, null);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[SessionBootstrapper] Bootstrap failed; treating as server-unreachable.");
            return Settle(SessionBootstrapResult.ServerUnreachable, null);
        }
    }

    /// <summary>Switches the session to another of the account's profiles WITHOUT reconnecting: the hub
    /// <c>SelectProfile</c> flips the live connection's acting profile in place (re-scopes its push group + sets a
    /// per-connection override), so the socket stays up. Re-running the ladder re-reads the new profile's
    /// connection info and swaps the per-profile config state + caches; <c>EnsureConnected</c> no-ops while
    /// connected. The connection is never torn down and <c>_lastConnection</c> is never nulled, so the offline
    /// screen never flashes. Throws the hub error (e.g. profile_locked) on refusal. Cross-profile badges keep
    /// arriving over the account group throughout.</summary>
    public async Task<SessionBootstrapResult> SwitchProfileAsync(Guid profileId, CancellationToken ct = default)
    {
        var from = _config.Auth.ActiveProfileId ?? Guid.Empty;
        _log.Debug("[PSW] SwitchProfileAsync: requested {From:N} -> {To:N}.", from, profileId);
        var token = await _hub.SelectProfileAsync(profileId, ct).ConfigureAwait(false);
        _log.Debug("[PSW] SwitchProfileAsync: SelectProfile ok, minted access token for {To:N}.", profileId);
        // Adopt the target BEFORE ApplyAccessToken stamps ActiveProfileId: ApplyAccessToken sets
        // ActiveProfileId = target, which would otherwise front-run AdoptActiveProfile's "already active" guard so
        // the per-profile local-state swap + nav-avatar refresh never run. Doing it here (while ActiveProfileId is
        // still the old profile) lets AdoptActiveProfile see the real transition; RunCoreAsync's later call then
        // correctly no-ops.
        AdoptActiveProfile(profileId);
        _tokens.ApplyAccessToken(token, profileId);
        // Re-own the chat cache to the target profile IMMEDIATELY (before RunAsync, which can no-op on its
        // single-flight guard): the server's acting profile already flipped, so any sync from now on must land in
        // the new profile's cache. Combined with the delta's ForProfileId stamp this closes the switch race that
        // merged both profiles' chats. Non-blocking; RunCoreAsync's later EnsureOwner is then a no-op.
        _chatCache.EnsureOwner(profileId);
        var result = await RunAsync(ct).ConfigureAwait(false);
        _log.Debug("[PSW] SwitchProfileAsync: ladder result {Result} for {To:N}; cache owner now {Owner:N}.", result, profileId, _chatCache.Owner);
        _notifications.NotifyProfileSwitched();
        _notifications.NotifyProfileCachesInvalidated();
        _notifications.NotifyDeckRefreshRequested();
        return result;
    }

    /// <summary>Reconciles the local active-profile selection with what the server resolved: swaps the flat
    /// per-profile config state and stamps the selection that token refresh sends.</summary>
    private void AdoptActiveProfile(Guid profileId)
    {
        if (profileId == Guid.Empty || _config.Auth.ActiveProfileId == profileId)
        {
            _log.Debug("[PSW] AdoptActiveProfile: SKIP (already active {Active:N}, requested {Requested:N}).", _config.Auth.ActiveProfileId ?? Guid.Empty, profileId);
            return;
        }
        var from = _config.Auth.ActiveProfileId;
        _config.SwitchProfileLocalState(from, profileId);
        _config.Auth.ActiveProfileId = profileId;
        _config.Save();
        // The nav avatar must never carry over from the previous profile: drop the texture (the next read
        // probes the new profile's disk key) and pull the fresh one.
        _ownAvatar.OnProfileSwitched();
        _ownAvatar.Refresh();
        _log.Debug("[PSW] AdoptActiveProfile: SWAP {From:N} -> {To:N} (local state stashed, nav avatar refreshed).", from ?? Guid.Empty, profileId);
    }

    /// <summary>Unwraps the active profile's key bundle with the stored account KEK, so a profile switch (or a
    /// sibling created elsewhere) never re-prompts for the passphrase. A missing/stale KEK simply leaves the
    /// passphrase-unlock gate to prompt.</summary>
    private async Task TryAutoUnlockAsync(CancellationToken ct)
    {
        if (_lastConnection is not { HasKeyBundle: true } || _keys.HasLocalKey || _keys.Kek is not { } kek)
        {
            return;
        }
        try
        {
            var bundle = await _hub.GetMyKeyBundleAsync(ct).ConfigureAwait(false);
            if (bundle is null)
            {
                return;
            }
            var priv = _crypto.UnwrapPrivateKey(bundle.EncryptedPrivateKey, bundle.WrapNonce, kek);
            if (priv is null)
            {
                _log.Warning("[SessionBootstrapper] Stored KEK failed to unwrap the profile bundle; the unlock screen will prompt.");
                return;
            }
            _keys.Store(bundle.PublicKey, priv);
            _log.Information("[SessionBootstrapper] Profile key unwrapped with the stored account KEK.");
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[SessionBootstrapper] Auto-unlock failed; the unlock screen will prompt.");
        }
    }

    /// <summary>Re-pulls the account snapshot on demand, e.g. after a Patreon link/unlink flips supporter
    /// status, which gates the second profile slot in the picker.</summary>
    public Task RefreshAccountInfoAsync(CancellationToken ct = default) => FetchAccountInfoAsync(ct);

    /// <summary>Creates and publishes the active profile's key bundle under the STORED account KEK when the
    /// server has none (a profile created before the KEK was captured, or whose upload was lost). Local keys
    /// without a server bundle are orphans (peers can never fetch their public half), so a fresh keypair is
    /// always generated. Silent; without a stored KEK the recovery gate prompts for the account passphrase.</summary>
    private async Task TryAutoProvisionAsync(CancellationToken ct)
    {
        if (_lastConnection is not { HasKeyBundle: false } conn
            || conn.Status is ProfileLifecycle.Deleted or ProfileLifecycle.Banned
            || _keys.Kek is not { } kek)
        {
            return;
        }
        try
        {
            var pass = await _hub.GetAccountPassphraseAsync(ct).ConfigureAwait(false);
            if (pass is null || !_crypto.CheckPassphraseVerifier(pass.Verifier, pass.VerifierNonce, kek))
            {
                // No published parameters, or a stale KEK: the recovery gate handles it interactively.
                return;
            }
            var (pubKey, privKey) = _crypto.GenerateIdentityKeyPair();
            var (wrapped, wrapNonce) = _crypto.WrapPrivateKey(privKey, kek);
            await _hub.UploadKeyBundleAsync(new Shared.Messaging.KeyBundleDto(
                    pubKey, wrapped, pass.KdfSalt, pass.KdfMemoryKb, pass.KdfIterations, pass.KdfParallelism, wrapNonce), ct)
                .ConfigureAwait(false);
            _keys.Store(pubKey, privKey);
            _lastConnection = conn with { HasKeyBundle = true };
            _log.Information("[SessionBootstrapper] Profile key bundle provisioned under the stored account KEK.");
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[SessionBootstrapper] Auto-provisioning failed; the recovery gate will prompt.");
        }
    }

    /// <summary>Best-effort account snapshot fetch. Non-fatal: the AetherLove session stands on its own during
    /// the migration window even if the account read fails or the server predates the method. A multi-profile
    /// account also seeds the sibling badge store so the app tile total is right before the picker ever opens.</summary>
    private async Task FetchAccountInfoAsync(CancellationToken ct)
    {
        try
        {
            _lastAccount = await _hub.GetAccountInfoAsync(ct).ConfigureAwait(false);
            if (_lastAccount is { ProfileCount: > 1 })
            {
                var list = await _hub.ListProfilesAsync(ct).ConfigureAwait(false);
                _siblingBadges.ReplaceAll(
                    Array.ConvertAll(list.Profiles, p => (p.ProfileId, p.DisplayName, p.NewMatches, p.UnreadChats)));
            }
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[SessionBootstrapper] GetAccountInfoAsync failed; continuing without the account snapshot.");
        }
    }

    private async Task SyncHangoutsAsync(AetherConnectionDto status, CancellationToken ct)
    {
        // Hangouts are account-level; the profile-id fallback only covers a failed account-info fetch.
        _hangouts.SetOwner(_lastAccount?.AccountId ?? status.ProfileId);
        if (!status.HangoutsEnabled)
        {
            _hangouts.Clear();
            return;
        }
        try
        {
            _hangouts.ApplySync(await _hub.GetHangoutSyncAsync(ct).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[SessionBootstrapper] Hangout sync failed.");
        }
    }

    private SessionBootstrapResult Settle(SessionBootstrapResult result, string? displayName)
    {
        _lastResult = result;
        _lastDisplayName = displayName;
        return result;
    }
}
