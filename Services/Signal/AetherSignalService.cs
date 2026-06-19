using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Config;
using AetherLove.Navigation;
using AetherLove.Screens;
using AetherLove.Services.Auth;
using AetherLove.Services.Hub;
using AetherLove.Windows;
using AetherLove.Shared.Matching;
using AetherLove.Shared.Messaging;
using AetherLove.Shared.Moderation;
using AetherLove.Shared.News;
using AetherLove.Shared.Profile;
using Dalamud.Plugin.Services;
using MessagePack;
using MessagePack.Resolvers;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace AetherLove.Services.Signal;

/// <summary>Current liveness of the hub connection.</summary>
public enum SignalConnectionState
{
    Disconnected = 0,
    Connecting = 1,
    Connected = 2,
    Reconnecting = 3,
}

/// <summary>Owns the SignalR <see cref="HubConnection"/> to <c>/hubs/aetherlove</c>.</summary>
public sealed class AetherSignalService : IAsyncDisposable
{
    private readonly IPluginLog _log;
    private readonly Configuration _config;
    private readonly TokenService _tokens;
    private readonly NotificationCenter _notifications;
    private readonly NotificationDispatcher _notifier;
    private readonly ChatEventBus _chatEvents;
    private readonly ScreenRouter _router;
    // Lazy-resolved to break the SessionBootstrapper <-> AetherSignalService ctor cycle.
    private readonly IServiceProvider _services;

    private HubConnection? _hub;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile int _stateRaw;
    private bool _disposed;

    private volatile bool _lastFailureWasUnauthorized;

    private CancellationTokenSource? _offlineDebounceCts;
    private static readonly TimeSpan OfflineGrace = TimeSpan.FromSeconds(5);

    /// <summary>Screen to restore once the connection returns, captured when we drop to Offline.</summary>
    private Screen _screenBeforeOffline = Screen.Deck;

    private const string HubPath = "hubs/aetherlove";

    public AetherSignalService(
        IPluginLog log,
        Configuration config,
        TokenService tokens,
        NotificationCenter notifications,
        NotificationDispatcher notifier,
        ChatEventBus chatEvents,
        ScreenRouter router,
        IServiceProvider services)
    {
        _log = log;
        _config = config;
        _tokens = tokens;
        _notifications = notifications;
        _notifier = notifier;
        _chatEvents = chatEvents;
        _router = router;
        _services = services;
    }

    public SignalConnectionState State => (SignalConnectionState)_stateRaw;
    public bool IsConnected => State == SignalConnectionState.Connected;

    /// <summary>True if the last connect attempt failed with HTTP 401.</summary>
    public bool LastFailureWasUnauthorized => _lastFailureWasUnauthorized;

    /// <summary>Returns the live hub connection. Throws if not connected.</summary>
    public HubConnection RequireConnection()
    {
        var hub = _hub;
        if (hub is null || hub.State != HubConnectionState.Connected)
        {
            throw new InvalidOperationException(
                "AetherSignal hub connection is not established. Sign in first.");
        }
        return hub;
    }

    /// <summary>Opens the hub connection if not already open. Idempotent.</summary>
    public async Task EnsureConnectedAsync(CancellationToken ct = default)
    {
        if (_disposed)
        {
            return;
        }

        if (string.IsNullOrEmpty(_config.Auth.AccessToken))
        {
            _log.Debug("[AetherSignalService] No access token stored; skipping connect.");
            return;
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            if (_hub is not null && _hub.State != HubConnectionState.Disconnected)
            {
                return;
            }

            _hub ??= BuildHubConnection();

            SetState(SignalConnectionState.Connecting);
            _lastFailureWasUnauthorized = false;
            await _hub.StartAsync(ct).ConfigureAwait(false);
            SetState(SignalConnectionState.Connected);
            CancelOfflineDebounce();
            RestoreOnline();
            InvalidateProfileCaches();
            RefreshConnectionInfo();
            _log.Information("[AetherSignalService] Connected to AetherLove hub.");
        }
        catch (OperationCanceledException)
        {
            SetState(SignalConnectionState.Disconnected);
        }
        catch (System.Net.Http.HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            SetState(SignalConnectionState.Disconnected);
            _lastFailureWasUnauthorized = true;
            _log.Warning("[AetherSignalService] Hub rejected token (401).");
        }
        catch (Exception ex)
        {
            SetState(SignalConnectionState.Disconnected);
            _log.Warning(ex, "[AetherSignalService] Initial hub connection failed.");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Stops the hub connection. Idempotent.</summary>
    public async Task DisconnectAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_hub is null)
            {
                return;
            }

            try
            {
                await _hub.StopAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "[AetherSignalService] StopAsync failed.");
            }

            await _hub.DisposeAsync().ConfigureAwait(false);
            _hub = null;
            SetState(SignalConnectionState.Disconnected);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        CancelOfflineDebounce();
        await DisconnectAsync().ConfigureAwait(false);
        _gate.Dispose();
    }

    private HubConnection BuildHubConnection()
    {
        // apiVersion rides the connection query string (like access_token). The server defaults a missing
        // value to 1, so clients from before versioning keep working until the server's version moves on.
        var url = new UriBuilder(new Uri(new Uri(Plugin.ServerBaseUrl), HubPath))
        {
            Query = $"apiVersion={AetherLove.Shared.ApiVersion.Current}&acceptsWebp={(Dalamud.Utility.Util.IsWine() ? "false" : "true")}",
        }.Uri;

        var hub = new HubConnectionBuilder()
            .WithUrl(url, options =>
            {
                // Refresh stale tokens on every (re)connect.
                options.AccessTokenProvider = async () =>
                {
                    if (_tokens.IsAccessTokenStale())
                    {
                        var ok = await _tokens.TryRefreshAsync(CancellationToken.None)
                                              .ConfigureAwait(false);
                        if (!ok)
                        {
                            _log.Warning("[AetherSignalService] Token refresh failed; using stale access token.");
                        }
                    }
                    return _config.Auth.AccessToken;
                };
            })
            .AddMessagePackProtocol(opts =>
            {
                opts.SerializerOptions = MessagePackSerializerOptions.Standard
                    .WithResolver(ContractlessStandardResolver.Instance)
                    .WithSecurity(MessagePackSecurity.UntrustedData);
            })
            .WithAutomaticReconnect()
            .Build();

        hub.Closed += ex =>
        {
            SetState(SignalConnectionState.Disconnected);
            CancelOfflineDebounce();
            if (ex is not null)
            {
                // Non-null ex = unexpected drop (reconnect exhausted); null = graceful StopAsync, leave the user alone.
                _log.Warning(ex, "[AetherSignalService] Hub closed with error.");
                GoOffline();
            }
            else
            {
                _log.Information("[AetherSignalService] Hub closed.");
            }
            return Task.CompletedTask;
        };

        hub.Reconnecting += ex =>
        {
            SetState(SignalConnectionState.Reconnecting);
            _log.Information("[AetherSignalService] Hub reconnecting…");
            ScheduleGoOffline();
            return Task.CompletedTask;
        };

        hub.Reconnected += id =>
        {
            SetState(SignalConnectionState.Connected);
            CancelOfflineDebounce();
            RestoreOnline();
            InvalidateProfileCaches();
            RefreshConnectionInfo();
            _log.Information($"[AetherSignalService] Hub reconnected (connectionId={id}).");
            return Task.CompletedTask;
        };

        hub.On<MatchCreatedPushDto>("MatchCreated", payload =>
        {
            _notifications.NewMatches++;
            _log.Information($"[AetherSignalService] MatchCreated push: {payload.OtherDisplayName} ({payload.OtherProfileId}).");
            _chatEvents.RaiseMatchCreated(payload);
            _notifier.NotifyNewMatch(payload.OtherDisplayName);
        });

        hub.On<DeckRefreshPushDto>("DeckRefresh", payload =>
        {
            _log.Information($"[AetherSignalService] DeckRefresh push: {payload.Reason}");
            _notifications.NotifyDeckRefreshRequested();
        });

        hub.On<MessageReceivedPushDto>("MessageReceived", payload =>
        {
            // An incoming message un-archives that chat so it returns to the active list.
            _services.GetRequiredService<ChatArchiveStore>().SetArchived(payload.FromProfileId, false);

            _chatEvents.RaiseMessageReceived(payload);

            // Suppress badge and notifications when already viewing this conversation.
            if (payload.FromProfileId == _notifications.ActiveChatPeerId)
            {
                return;
            }

            _notifications.UnreadChatMessages++;
            _notifications.NotifyUnreadChatMessageArrived();
            _notifier.NotifyChatMessage();
        });

        hub.On<MessageReadPushDto>("MessageRead", payload =>
        {
            _chatEvents.RaiseMessageRead(payload);
        });

        hub.On<UnmatchedPushDto>("Unmatched", payload =>
        {
            _chatEvents.RaiseUnmatched(payload);
            _log.Information($"[AetherSignalService] Unmatched push from {payload.OtherProfileId}.");
        });

        hub.On<BlockedByPeerPushDto>("BlockedByPeer", payload =>
        {
            _chatEvents.RaiseBlockedByPeer(payload);
            _log.Information($"[AetherSignalService] BlockedByPeer push from {payload.OtherProfileId}.");
        });

        hub.On<WarningIssuedPushDto>("WarningIssued", payload =>
        {
            _log.Information($"[AetherSignalService] WarningIssued push: {payload.Warning.Id}.");
            AppendWarningToCachedSnapshot(payload.Warning);

            // Phone open: show it now (and auto-ack, as before). Minimised/closed: badge and buzz the mini
            // phone, and defer the acknowledge screen and the seen-mark until the user opens the phone — so a
            // warning that lands while minimised isn't silently swallowed.
            if (_services.GetRequiredService<MainPluginWindow>().IsOpen)
            {
                _router.Navigate(Screen.WarningsAcknowledge);

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await hub.InvokeAsync("MarkWarningsSeenAsync", new[] { payload.Warning.Id })
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _log.Warning(ex, "[AetherSignalService] MarkWarningsSeenAsync (auto-ack) failed.");
                    }
                });
            }
            else
            {
                _notifications.RaisePendingWarning();
            }
        });

        hub.On<NewsPublishedPushDto>("NewsPublished", payload =>
        {
            _log.Information($"[AetherSignalService] NewsPublished push: {payload.Summary.Id}.");
            _services.GetRequiredService<SessionBootstrapper>().AppendNewsToSnapshot(payload.Summary);

            // Phone open: interrupt and show it now. Minimised/closed: badge + a chat-line notification,
            // and defer the news screen until the user opens the phone.
            if (_services.GetRequiredService<MainPluginWindow>().IsOpen)
            {
                _router.Navigate(Screen.News);
            }
            else
            {
                _notifications.RaisePendingNews();
                _notifier.NotifyNews(payload.Summary.Title);
            }
        });

        hub.On<NewsTestPushDto>("NewsTestPush", payload =>
        {
            _log.Information($"[AetherSignalService] NewsTestPush (staff preview): {payload.Summary.Id}.");
            _services.GetRequiredService<NewsScreen>().QueuePreview(payload.Summary.Id);

            if (_services.GetRequiredService<MainPluginWindow>().IsOpen)
            {
                _router.Navigate(Screen.News);
            }
            else
            {
                _notifications.RaisePendingNews();
                _notifier.NotifyNews(payload.Summary.Title);
            }
        });

        hub.On<AccountBannedPushDto>("AccountBanned", payload =>
        {
            _log.Information("[AetherSignalService] AccountBanned push.");
            ApplyBanToCachedSnapshot(payload.Reason);
            _router.Navigate(Screen.Banned);
        });

        return hub;
    }

    /// <summary>Drops cached profile copies on every (re)connect so server-side changes made while away are re-fetched.</summary>
    private void InvalidateProfileCaches()
    {
        _services.GetRequiredService<ProfileScreen>().InvalidateMyProfileCache();
        _services.GetRequiredService<MyProfileScreen>().InvalidateEditCache();
    }

    /// <summary>Fire-and-forget re-fetch of the connection snapshot so warnings / match count / ban state
    /// self-heal after a (re)connect — the startup bootstrap fetches it only once, which a flaky link can
    /// miss. Fire-and-forget (not awaited) because the connect paths hold <c>_gate</c> and the fetch
    /// re-enters <c>EnsureConnectedAsync</c>.</summary>
    private void RefreshConnectionInfo()
    {
        _ = _services.GetRequiredService<SessionBootstrapper>().RefreshConnectionInfoAsync();
        _ = _services.GetRequiredService<FlairCatalog>().RefreshAsync();
    }

    private void AppendWarningToCachedSnapshot(WarningDto warning)
    {
        var bootstrap = _services.GetRequiredService<SessionBootstrapper>();
        var conn = bootstrap.LastConnection;
        if (conn is null)
        {
            return;
        }
        // Idempotent.
        if (conn.Warnings.Any(w => w.Id == warning.Id))
        {
            return;
        }
        var grown = new WarningDto[conn.Warnings.Length + 1];
        Array.Copy(conn.Warnings, grown, conn.Warnings.Length);
        grown[^1] = warning;
        bootstrap.ReplaceConnectionSnapshot(conn with { Warnings = grown });
    }

    private void ApplyBanToCachedSnapshot(string? reason)
    {
        var bootstrap = _services.GetRequiredService<SessionBootstrapper>();
        var conn = bootstrap.LastConnection;
        if (conn is null)
        {
            return;
        }
        bootstrap.ReplaceConnectionSnapshot(conn with
        {
            Status = ProfileLifecycle.Banned,
            BanReason = reason ?? conn.BanReason,
        });
    }

    /// <summary>Screens where losing the hub connection should surface the Offline screen.</summary>
    private static bool IsAppScreen(Screen s) => s is Screen.Deck or Screen.Match
        or Screen.ChatList or Screen.Chat or Screen.Profile or Screen.Settings or Screen.MyProfile;

    /// <summary>Defers the Offline screen so brief drops that recover within the grace window stay invisible.</summary>
    private void ScheduleGoOffline()
    {
        CancelOfflineDebounce();
        var cts = new CancellationTokenSource();
        _offlineDebounceCts = cts;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(OfflineGrace, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            if (State != SignalConnectionState.Connected)
            {
                GoOffline();
            }
        });
    }

    private void CancelOfflineDebounce()
    {
        var cts = Interlocked.Exchange(ref _offlineDebounceCts, null);
        cts?.Cancel();
        cts?.Dispose();
    }

    private void GoOffline()
    {
        var current = _router.Current;
        if (current == Screen.Offline || !IsAppScreen(current))
        {
            return;
        }
        _screenBeforeOffline = current == Screen.Match ? Screen.Deck : current;
        _router.Navigate(Screen.Offline);
    }

    private void RestoreOnline()
    {
        if (_router.Current != Screen.Offline)
        {
            return;
        }
        var target = _screenBeforeOffline == Screen.Offline ? Screen.Deck : _screenBeforeOffline;
        _router.Navigate(target);
    }

    private void SetState(SignalConnectionState s) =>
        Interlocked.Exchange(ref _stateRaw, (int)s);
}
