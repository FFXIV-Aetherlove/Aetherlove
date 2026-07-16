using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Config;
using AetherLove.Navigation;
using AetherLove.Screens;
using AetherLove.Services.Auth;
using AetherLove.Services.Hangouts;
using AetherLove.Services.Hub;
using AetherLove.Windows;
using AetherLove.Shared.Hangouts;
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
    private readonly HangoutStateService _hangouts;
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

    /// <summary>Armed only by a mid-session drop; startup routing to Offline leaves this false so the bootstrap retry owns routing.</summary>
    private bool _armedRestore;

    private const string HubPath = "hubs/aetherlove";

    public AetherSignalService(
        IPluginLog log,
        Configuration config,
        TokenService tokens,
        NotificationCenter notifications,
        NotificationDispatcher notifier,
        ChatEventBus chatEvents,
        HangoutStateService hangouts,
        ScreenRouter router,
        IServiceProvider services)
    {
        _log = log;
        _config = config;
        _tokens = tokens;
        _notifications = notifications;
        _notifier = notifier;
        _chatEvents = chatEvents;
        _hangouts = hangouts;
        _router = router;
        _services = services;
    }

    public SignalConnectionState State => (SignalConnectionState)_stateRaw;
    public bool IsConnected => State == SignalConnectionState.Connected;

    public bool LastFailureWasUnauthorized => _lastFailureWasUnauthorized;

    public bool RestoreArmed => _armedRestore;

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

    /// <summary>Driven by the startup WebP-decode probe; null (not yet probed) falls back to JPEG, which always renders.</summary>
    public bool AcceptsWebp() => !_config.ForceJpegImages && (_config.WebpSupported ?? false);

    private HubConnection BuildHubConnection()
    {
        var url = new UriBuilder(new Uri(new Uri(Plugin.ServerBaseUrl), HubPath))
        {
            Query = $"apiVersion={AetherLove.Shared.ApiVersion.Current}&acceptsWebp={(AcceptsWebp() ? "true" : "false")}",
        }.Uri;

        var hub = new HubConnectionBuilder()
            .WithUrl(url, options =>
            {
                // Refresh happens before each (re)connect, not after expiry mid-connection.
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
                // Non-null ex means reconnect exhausted; null is a graceful StopAsync.
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

        hub.On("SuperlikeReceived", () =>
        {
            _notifications.NotifyDeckRefreshRequested();
            _log.Information("[AetherSignalService] Superlike received push; deck refresh requested.");
        });

        hub.On<DeckRefreshPushDto>("DeckRefresh", payload =>
        {
            _log.Information($"[AetherSignalService] DeckRefresh push: {payload.Reason}");
            _notifications.NotifyDeckRefreshRequested();
        });

        hub.On<MessageReceivedPushDto>("MessageReceived", payload =>
        {
            _chatEvents.RaiseMessageReceived(payload);

            // ActiveChatPeerId stays set when the phone is minimised or closed, so gate on the window too.
            var phoneOpen = _services.GetRequiredService<MainPluginWindow>().IsOpen;
            if (phoneOpen && payload.FromProfileId == _notifications.ActiveChatPeerId)
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
            _hangouts.RemoveMatchPeer(payload.OtherProfileId);
            _log.Information($"[AetherSignalService] Unmatched push from {payload.OtherProfileId}.");
        });

        hub.On<BlockedByPeerPushDto>("BlockedByPeer", payload =>
        {
            _chatEvents.RaiseBlockedByPeer(payload);
            _hangouts.RemoveMatchPeer(payload.OtherProfileId);
            _log.Information($"[AetherSignalService] BlockedByPeer push from {payload.OtherProfileId}.");
        });

        hub.On<HangoutStartedPushDto>("HangoutStarted", payload =>
        {
            var newMatchHangout = _hangouts.ApplyStarted(payload.Hangout);
            _log.Information($"[AetherSignalService] HangoutStarted push from {payload.Hangout.OwnerProfileId}.");
            if (newMatchHangout && _config.Hangouts.NotifyMatchStarted)
            {
                _notifier.NotifyMatchHangout(payload.Hangout);
            }
        });

        hub.On<HangoutEndedPushDto>("HangoutEnded", payload =>
        {
            var wasRsvped = _hangouts.ApplyEnded(payload.HangoutId, payload.OwnerProfileId);
            _log.Information($"[AetherSignalService] HangoutEnded push: {payload.HangoutId} ({payload.Kind}).");
            if (wasRsvped && payload.Kind != HangoutEndKind.Expired && _config.Hangouts.NotifyEnded)
            {
                _notifier.NotifyHangoutEnded(payload.Kind == HangoutEndKind.Cancelled);
            }
        });

        hub.On<HangoutRsvpChangedPushDto>("HangoutRsvpChanged", payload =>
        {
            _hangouts.ApplyRsvpChanged(payload);
            if (payload.Going && _config.Hangouts.NotifyRsvp)
            {
                _notifier.NotifyHangoutRsvp(payload.RsvperDisplayName);
            }
        });

        hub.On<MessageReactionsChangedPushDto>("MessageReactionsChanged", payload =>
        {
            _chatEvents.RaiseReactionsChanged(payload);
        });

        hub.On<MessagePinChangedPushDto>("MessagePinChanged", payload =>
        {
            _chatEvents.RaisePinChanged(payload);
        });

        hub.On<WarningIssuedPushDto>("WarningIssued", payload =>
        {
            _log.Information($"[AetherSignalService] WarningIssued push: {payload.Warning.Id}.");
            AppendWarningToCachedSnapshot(payload.Warning);

            // While minimised, the acknowledge screen and the seen-mark are deferred to the next phone open.
            if (_services.GetRequiredService<MainPluginWindow>().IsOpen)
            {
                _services.GetRequiredService<WarningAcknowledgeScreen>().RequestLiveAcknowledge();
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

        hub.On<ModeratorMessageIssuedPushDto>("ModeratorMessageIssued", payload =>
        {
            _log.Information($"[AetherSignalService] ModeratorMessageIssued push: {payload.Message.Id}.");
            AppendModeratorMessageToCachedSnapshot(payload.Message);

            // Unlike warnings, no mini-phone surface; while minimised this defers to the next phone open.
            if (_services.GetRequiredService<MainPluginWindow>().IsOpen)
            {
                _services.GetRequiredService<ModeratorMessageScreen>().RequestLiveAcknowledge();
                _router.Navigate(Screen.ModeratorMessages);

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await hub.InvokeAsync("MarkModeratorMessagesSeenAsync", new[] { payload.Message.Id })
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _log.Warning(ex, "[AetherSignalService] MarkModeratorMessagesSeenAsync (auto-ack) failed.");
                    }
                });
            }
            else
            {
                _notifications.RaisePendingModeratorMessage();
            }
        });

        hub.On<NewsPublishedPushDto>("NewsPublished", payload =>
        {
            _log.Information($"[AetherSignalService] NewsPublished push: {payload.Summary.Id}.");
            _services.GetRequiredService<SessionBootstrapper>().AppendNewsToSnapshot(payload.Summary);

            if (_services.GetRequiredService<MainPluginWindow>().IsOpen)
            {
                _services.GetRequiredService<NewsScreen>().RequestLiveUnseenFlow();
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

    private void InvalidateProfileCaches()
    {
        _services.GetRequiredService<ProfileScreen>().InvalidateMyProfileCache();
        _services.GetRequiredService<MyProfileScreen>().InvalidateEditCache();
    }

    /// <summary>Not awaited: the connect paths hold <c>_gate</c> and the fetch re-enters <see cref="EnsureConnectedAsync"/>.</summary>
    private void RefreshConnectionInfo()
    {
        _ = _services.GetRequiredService<SessionBootstrapper>().RefreshConnectionInfoAsync();
        _ = _services.GetRequiredService<FlairCatalog>().RefreshAsync();
        // A superlike that landed while disconnected sent no push; only a fresh pull can surface it.
        _notifications.NotifyDeckRefreshRequested();
    }

    private void AppendWarningToCachedSnapshot(WarningDto warning)
    {
        var bootstrap = _services.GetRequiredService<SessionBootstrapper>();
        var conn = bootstrap.LastConnection;
        if (conn is null)
        {
            return;
        }
        if (conn.Warnings.Any(w => w.Id == warning.Id))
        {
            return;
        }
        var grown = new WarningDto[conn.Warnings.Length + 1];
        Array.Copy(conn.Warnings, grown, conn.Warnings.Length);
        grown[^1] = warning;
        bootstrap.ReplaceConnectionSnapshot(conn with { Warnings = grown });
    }

    private void AppendModeratorMessageToCachedSnapshot(ModeratorMessageDto message)
    {
        var bootstrap = _services.GetRequiredService<SessionBootstrapper>();
        var conn = bootstrap.LastConnection;
        if (conn?.ModeratorMessages is null)
        {
            return;
        }
        if (conn.ModeratorMessages.Any(m => m.Id == message.Id))
        {
            return;
        }
        var grown = new ModeratorMessageDto[conn.ModeratorMessages.Length + 1];
        Array.Copy(conn.ModeratorMessages, grown, conn.ModeratorMessages.Length);
        grown[^1] = message;
        bootstrap.ReplaceConnectionSnapshot(conn with { ModeratorMessages = grown });
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
        _armedRestore = true;
        _router.Navigate(Screen.Offline);
    }

    private void RestoreOnline()
    {
        if (_router.Current != Screen.Offline || !_armedRestore)
        {
            return;
        }
        _armedRestore = false;
        var target = _screenBeforeOffline == Screen.Offline ? Screen.Deck : _screenBeforeOffline;
        _router.Navigate(target);
    }

    private void SetState(SignalConnectionState s) =>
        Interlocked.Exchange(ref _stateRaw, (int)s);
}
