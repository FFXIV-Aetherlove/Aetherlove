using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Config;
using AetherLove.Navigation;
using AetherLove.Services.Auth;
using AetherLove.Services.Hangouts;
using AetherLove.Services.Hub;
using AetherLove.Services.Localization;
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
    private readonly INotifier _notifier;
    private readonly ISignalHost _host;
    private readonly ChatEventBus _chatEvents;
    private readonly HangoutStateService _hangouts;
    private readonly Messenger.MessengerStore _messenger;
    private readonly SiblingBadgeStore _siblingBadges;
    private readonly ScreenRouter _router;
    private readonly Yapper.YapperNotificationRelay _yapperRelay;
    // Lazy-resolved to break the SessionBootstrapper <-> AetherSignalService ctor cycle.
    private readonly IServiceProvider _services;

    private HubConnection? _hub;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile int _stateRaw;
    private bool _disposed;

    private volatile bool _lastFailureWasUnauthorized;

    private CancellationTokenSource? _offlineDebounceCts;
    private static readonly TimeSpan OfflineGrace = TimeSpan.FromSeconds(5);

    /// <summary>Set once a drop outlasts the grace window, cleared on reconnect. The OS never navigates on
    /// it: the host gates connection-needing surface apps on this flag and everything else keeps running.</summary>
    private volatile bool _debouncedOffline;

    private const string HubPath = "hubs/aetherlove";

    public AetherSignalService(
        IPluginLog log,
        Configuration config,
        TokenService tokens,
        NotificationCenter notifications,
        INotifier notifier,
        ISignalHost host,
        ChatEventBus chatEvents,
        HangoutStateService hangouts,
        Messenger.MessengerStore messenger,
        SiblingBadgeStore siblingBadges,
        ScreenRouter router,
        Yapper.YapperNotificationRelay yapperRelay,
        IServiceProvider services)
    {
        _yapperRelay = yapperRelay;
        _log = log;
        _config = config;
        _tokens = tokens;
        _notifications = notifications;
        _notifier = notifier;
        _host = host;
        _chatEvents = chatEvents;
        _hangouts = hangouts;
        _messenger = messenger;
        _siblingBadges = siblingBadges;
        _router = router;
        _services = services;
    }

    public SignalConnectionState State => (SignalConnectionState)_stateRaw;
    public bool IsConnected => State == SignalConnectionState.Connected;

    public bool LastFailureWasUnauthorized => _lastFailureWasUnauthorized;

    public bool DebouncedOffline => _debouncedOffline;

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
            AfterConnected();
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

            // A 401 on a token this client still thinks is fresh can be clock skew or a rotated signing key,
            // so force one refresh and try again. A 401 on THAT refresh is the session itself ending, which
            // TokenService records stickily: the phone stops pretending to be offline and asks for a sign-in.
            if (!_tokens.SessionExpired
                && await _tokens.TryRefreshAsync(ct, force: true).ConfigureAwait(false)
                && _hub is not null)
            {
                try
                {
                    SetState(SignalConnectionState.Connecting);
                    await _hub.StartAsync(ct).ConfigureAwait(false);
                    _lastFailureWasUnauthorized = false;
                    AfterConnected();
                }
                catch (Exception retry)
                {
                    SetState(SignalConnectionState.Disconnected);
                    _log.Warning(retry, "[AetherSignalService] Retry after a forced refresh failed.");
                }
            }
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

    private void AfterConnected()
    {
        SetState(SignalConnectionState.Connected);
        CancelOfflineDebounce();
        RestoreOnline();
        InvalidateProfileCaches();
        RefreshConnectionInfo();
        _log.Information("[AetherSignalService] Connected to AetherLove hub.");
    }

    public async Task DisconnectAsync()
    {
        try
        {
            await _gate.WaitAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // A plugin unload disposed the gate while a bootstrap was in flight; nothing left to disconnect.
            return;
        }
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
        var url = new UriBuilder(new Uri(new Uri(AetherLove.Shared.AetherConstants.ServerBaseUrl), HubPath))
        {
            Query = $"apiVersion={AetherLove.Shared.ApiVersion.Current}&acceptsWebp={(AcceptsWebp() ? "true" : "false")}",
        }.Uri;

        var hub = new HubConnectionBuilder()
            .WithUrl(url, options =>
            {
                // Refresh happens before each (re)connect, not after expiry mid-connection.
                options.AccessTokenProvider = async () =>
                {
                    // A refresh the server has already rejected outright will be rejected again; asking once
                    // per reconnect attempt only fills the log with 401s while the phone waits for the user
                    // to sign in.
                    if (_tokens.IsAccessTokenStale() && !_tokens.SessionExpired)
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
            _ = RefreshStaffNoticesAsync();
            _ = _services.GetRequiredService<Messenger.MessengerSyncService>().SyncAsync();
            _ = _services.GetRequiredService<Yapper.YapperDmCryptoService>().EnsureProvisionedAsync();
            _log.Information($"[AetherSignalService] Hub reconnected (connectionId={id}).");
            return Task.CompletedTask;
        };

        // ForProfileId routes active-profile pushes to live chat/notifications; sibling pushes only bump badges.
        hub.On<MatchCreatedPushDto>("MatchCreated", payload =>
        {
            var forActive = IsForActive(payload.ForProfileId);
            if (forActive)
            {
                _notifications.NewMatches++;
                _chatEvents.RaiseMatchCreated(payload);
            }
            else
            {
                _siblingBadges.Bump(payload.ForProfileId, 1, 0);
            }
            _notifier.NotifyNewMatch(payload.OtherDisplayName);
            PostLoveMatchNotification(payload, forActive, payload.ForProfileId);
        });

        hub.On<SuperlikeReceivedPushDto>("SuperlikeReceived", payload =>
        {
            if (!IsForActive(payload.ForProfileId))
            {
                return;
            }
            _notifications.NotifyDeckRefreshRequested();
            _log.Information("[AetherSignalService] Superlike received push; deck refresh requested.");
        });

        hub.On<DeckRefreshPushDto>("DeckRefresh", payload =>
        {
            if (!IsForActive(payload.ForProfileId))
            {
                return;
            }
            _log.Information($"[AetherSignalService] DeckRefresh push: {payload.Reason}");
            _notifications.NotifyDeckRefreshRequested();
        });

        hub.On<MessageReceivedPushDto>("MessageReceived", payload =>
        {
            var forActive = IsForActive(payload.ForProfileId);
            if (forActive)
            {
                _chatEvents.RaiseMessageReceived(payload);
                // ActiveChatPeerId stays set when the phone is minimised or closed, so gate on the window too.
                var phoneOpen = _host.IsPhoneOpen;
                if (phoneOpen && payload.FromProfileId == _notifications.ActiveChatPeerId)
                {
                    return;
                }
                _notifications.UnreadChatMessages++;
                _notifications.NotifyUnreadChatMessageArrived();
            }
            else
            {
                _siblingBadges.Bump(payload.ForProfileId, 0, 1);
            }
            _notifier.NotifyChatMessage();
            PostLoveMessageNotification(payload.FromProfileId, forActive, payload.ForProfileId);
        });

        hub.On<ChatImageRemovedPushDto>("ChatImageRemoved", payload =>
        {
            if (IsForActive(payload.ForProfileId))
            {
                _chatEvents.RaiseChatImageRemoved(payload);
            }
        });

        hub.On<MessageReadPushDto>("MessageRead", payload =>
        {
            if (!IsForActive(payload.ForProfileId))
            {
                return;
            }
            _chatEvents.RaiseMessageRead(payload);
        });

        hub.On<UnmatchedPushDto>("Unmatched", payload =>
        {
            _log.Information($"[AetherSignalService] Unmatched push from {payload.OtherProfileId} (for {payload.ForProfileId:N}).");
            if (IsForActive(payload.ForProfileId))
            {
                _chatEvents.RaiseUnmatched(payload);
            }
            else
            {
                _siblingBadges.Bump(payload.ForProfileId, -1, 0);
            }
        });

        hub.On<BlockedByPeerPushDto>("BlockedByPeer", payload =>
        {
            if (!IsForActive(payload.ForProfileId))
            {
                return;
            }
            _chatEvents.RaiseBlockedByPeer(payload);
            _log.Information($"[AetherSignalService] BlockedByPeer push from {payload.OtherProfileId}.");
        });

        hub.On<HangoutStartedPushDto>("HangoutStarted", payload =>
        {
            var newContactHangout = _hangouts.ApplyStarted(payload.Hangout);
            _log.Information($"[AetherSignalService] HangoutStarted push from account {payload.Hangout.OwnerAccountId}.");
            if (newContactHangout && _config.Hangouts.NotifyMatchStarted)
            {
                _notifier.NotifyFriendHangout(payload.Hangout);
            }
        });

        hub.On<HangoutEndedPushDto>("HangoutEnded", payload =>
        {
            var wasRsvped = _hangouts.ApplyEnded(payload.HangoutId, payload.OwnerAccountId);
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
            if (!IsForActive(payload.ForProfileId))
            {
                return;
            }
            _chatEvents.RaiseReactionsChanged(payload);
        });

        hub.On<MessagePinChangedPushDto>("MessagePinChanged", payload =>
        {
            if (!IsForActive(payload.ForProfileId))
            {
                return;
            }
            _chatEvents.RaisePinChanged(payload);
        });

        hub.On<MessageDeletedPushDto>("MessageDeleted", payload =>
        {
            if (!IsForActive(payload.ForProfileId))
            {
                return;
            }
            _services.GetRequiredService<Chat.ChatSyncService>().Cache
                .ApplyMessageDeleted(payload.PeerProfileId, payload.MessageId);
            _chatEvents.RaiseMessageDeleted(payload);
        });

        hub.On<PeerKeysResetPushDto>("PeerKeysReset", payload =>
        {
            if (!IsForActive(payload.ForProfileId))
            {
                return;
            }
            _log.Information($"[AetherSignalService] PeerKeysReset push from {payload.PeerProfileId}.");
            _chatEvents.RaisePeerKeysReset(payload);
            // The reset bumped the match rows server-side; a delta sync pulls the refreshed summaries
            // (new public key + key history) into the cache.
            _ = _services.GetRequiredService<Chat.ChatSyncService>().SyncAsync(CancellationToken.None);
        });

        hub.On<WarningIssuedPushDto>("WarningIssued", payload =>
        {
            // Account-scoped: warnings follow the human and show on whichever profile is active, so no
            // active-profile filter (ForProfileId carries the account id, not a profile id).
            _log.Information($"[AetherSignalService] WarningIssued push: {payload.Warning.Id}.");
            AppendWarningToCachedSnapshot(payload.Warning);

            // While minimised, the acknowledge screen and the seen-mark are deferred to the next phone open.
            if (_host.IsPhoneOpen)
            {
                _host.RequestWarningLiveAcknowledge();
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
            // Account-scoped like warnings: show on whichever profile is active, no active-profile filter.
            _log.Information($"[AetherSignalService] ModeratorMessageIssued push: {payload.Message.Id}.");
            AppendModeratorMessageToCachedSnapshot(payload.Message);

            // Unlike warnings, no mini-phone surface; while minimised this defers to the next phone open.
            if (_host.IsPhoneOpen)
            {
                _host.RequestModeratorLiveAcknowledge();
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

        hub.On<AccountWarningPushDto>("AccountWarningIssued", payload =>
        {
            _log.Information($"[AetherSignalService] AccountWarningIssued push: {payload.Warning.Id}.");
            _services.GetRequiredService<SessionBootstrapper>().AppendStaffWarningToSnapshot(payload.Warning);
            RaiseStaffNotice();
        });

        hub.On<AccountModeratorMessagePushDto>("AccountModeratorMessageIssued", payload =>
        {
            _log.Information($"[AetherSignalService] AccountModeratorMessageIssued push: {payload.Message.Id}.");
            _services.GetRequiredService<SessionBootstrapper>().AppendStaffMessageToSnapshot(payload.Message);
            RaiseStaffNotice();
        });

        hub.On<NewsPublishedPushDto>("NewsPublished", payload =>
        {
            _log.Information($"[AetherSignalService] NewsPublished push: {payload.Summary.Id}.");
            _services.GetRequiredService<SessionBootstrapper>().AppendNewsToSnapshot(payload.Summary);
            PostNewsNotification(payload.Summary.Id, payload.Summary.Title, preview: false);

            if (!_host.IsPhoneOpen)
            {
                _notifier.NotifyNews(payload.Summary.Title);
            }
        });

        // Staff shouting at everyone at once. No profile routing and no phone-open gate: it is announced to
        // the human, in the game's own chat, whether or not the phone is out.
        hub.On<string>("ServerNotice", text =>
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }
            _log.Information($"[AetherSignalService] ServerNotice: {text}");
            _notifier.NotifyServerNotice(text);
        });

        hub.On<NewsTestPushDto>("NewsTestPush", payload =>
        {
            if (!IsForActive(payload.ForProfileId))
            {
                return;
            }
            _log.Information($"[AetherSignalService] NewsTestPush (staff preview): {payload.Summary.Id}.");
            if (_host.IsPhoneOpen)
            {
                _services.GetRequiredService<AetherOS.Sdk.IOsShell>()
                    .SendIntent("news", NewsIntent(payload.Summary.Id, preview: true));
            }
            else
            {
                PostNewsNotification(payload.Summary.Id, payload.Summary.Title, preview: true);
                _notifier.NotifyNews(payload.Summary.Title);
            }
        });

        hub.On<AccountBannedPushDto>("AccountBanned", payload =>
        {
            // A sibling profile's ban must not tear down the active session; it surfaces when the user switches to
            // that profile (GetConnectionInfo returns Banned). Only a ban of the ACTIVE profile shows the screen.
            if (!IsForActive(payload.ForProfileId))
            {
                return;
            }
            _log.Information("[AetherSignalService] AccountBanned push.");
            ApplyBanToCachedSnapshot(payload.Reason);
            _router.Navigate(Screen.Banned);
        });

        hub.On<AccountDisabledPushDto>("AccountDisabled", payload =>
        {
            // Account-wide ban: the shell gates every server-backed app off the account snapshot. A null reason
            // lifts the ban. No navigation, the user stays on Home with the local apps still usable.
            _log.Information("[AetherSignalService] AccountDisabled push.");
            var bootstrap = _services.GetRequiredService<SessionBootstrapper>();
            bootstrap.ApplyAccountDisabledToSnapshot(payload.Reason);
            // Re-pull the authoritative account snapshot; the info fetch stays reachable while banned.
            _ = bootstrap.RefreshAccountInfoAsync(CancellationToken.None);
        });

        RegisterMessengerHandlers(hub);
        RegisterYapperHandlers(hub);
        RegisterEchoHandlers(hub);
        RegisterTogetherHandlers(hub);

        return hub;
    }

    /// <summary>Together-party pushes. Every handler only mutates the state service, which queues its own
    /// events for the draw thread to drain; nothing here may touch ImGui. A reconnect re-syncs the party
    /// (SignalR drops group membership with the connection), and a failed re-sync treats it as ended so
    /// the shell never shows a phantom party.</summary>
    private void RegisterTogetherHandlers(HubConnection hub)
    {
        var state = _services.GetRequiredService<Services.Together.TogetherStateService>();
        var runState = _services.GetRequiredService<Services.Together.WayfinderRunStateService>();

        hub.On<Shared.Together.TogetherMemberDto, Guid>("TogetherMemberJoined", (member, partyId) =>
            state.ApplyMemberJoined(partyId, member));
        hub.On<Shared.Together.TogetherMemberLeftDto>("TogetherMemberLeft", state.ApplyMemberLeft);
        hub.On<Shared.Together.TogetherMemberPresenceDto>("TogetherMemberPresence", state.ApplyMemberPresence);
        hub.On<Shared.Together.TogetherActivityChangedDto>("TogetherActivityChanged", state.ApplyActivityChanged);
        hub.On<Shared.Together.TogetherChatLineDto>("TogetherChat", state.ApplyChat);
        hub.On<Shared.Wayfinder.WayfinderPartyRunDto>("WayfinderPartyRunChanged", runState.ApplyRun);
        var raceRunState = _services.GetRequiredService<Services.Together.LumiRaceRunStateService>();
        hub.On<Shared.Racing.LumiRacePartyRunDto>("LumiRaceRunChanged", raceRunState.ApplyRun);
        hub.On<Shared.Together.TogetherPartyEndedDto>("TogetherPartyEnded", push =>
        {
            state.ApplyPartyEnded(push);
            runState.Clear();
            raceRunState.Clear();
        });
        hub.On<Shared.Together.TogetherKickedDto>("TogetherKicked", push =>
        {
            state.ApplyKicked(push);
            runState.Clear();
            raceRunState.Clear();
        });

        hub.Reconnected += connectionId =>
        {
            if (state.CurrentPartyId is not { } partyId)
            {
                return Task.CompletedTask;
            }
            _ = Task.Run(async () =>
            {
                try
                {
                    var hubClient = _services.GetRequiredService<Services.Hub.AetherHubContext>();
                    var snapshot = await hubClient.GetTogetherPartySyncAsync(partyId).ConfigureAwait(false);
                    state.ApplySnapshot(snapshot);
                    if (snapshot.Activity?.AppId == "wayfinder"
                        && await hubClient.GetWayfinderPartyRunAsync(true).ConfigureAwait(false) is { } run)
                    {
                        runState.ApplyRun(run);
                    }
                }
                catch (Exception ex)
                {
                    _log.Warning(ex, "[AetherSignalService] Together re-sync failed; treating the party as gone.");
                    state.ApplyPartyEnded(new Shared.Together.TogetherPartyEndedDto(
                        partyId, Shared.Together.TogetherEndReason.Empty));
                    runState.Clear();
                }
            });
            return Task.CompletedTask;
        };
    }

    /// <summary>Echo watch-room pushes. Every handler only mutates the state service, which queues its own
    /// events for the draw thread to drain; nothing here may touch ImGui.
    ///
    /// SignalR drops group membership when a connection dies, so a reconnect must re-join the room rather
    /// than assume it is still in the group. That is what the reconnect re-sync below is for.</summary>
    private void RegisterEchoHandlers(HubConnection hub)
    {
        var state = _services.GetRequiredService<Services.Echo.EchoStateService>();

        hub.On<Shared.EchoVidya.EchoMemberDto, Guid>("EchoMemberJoined", (member, roomId) =>
            state.ApplyMemberJoined(roomId, member));
        hub.On<Shared.EchoVidya.EchoMemberLeftDto>("EchoMemberLeft", state.ApplyMemberLeft);
        hub.On<Guid, Shared.EchoVidya.EchoPlaylistEntryDto[]>("EchoPlaylistChanged", (roomId, playlist) =>
            state.ApplyPlaylistChanged(roomId, playlist));
        hub.On<Shared.EchoVidya.EchoPlaybackDto>("EchoPlaybackChanged", state.ApplyPlaybackChanged);
        hub.On<Guid, bool>("EchoHostOnlyChanged", (roomId, hostOnly) =>
            state.ApplyHostOnlyChanged(roomId, hostOnly));
        hub.On<Shared.EchoVidya.EchoChatLineDto>("EchoChat", state.ApplyChat);
        hub.On<Shared.EchoVidya.EchoRoomEndedDto>("EchoRoomEnded", state.ApplyRoomEnded);
        hub.On<Shared.EchoVidya.EchoKickedDto>("EchoKicked", state.ApplyKicked);

        hub.Reconnected += connectionId =>
        {
            if (state.CurrentRoomId is not { } roomId)
            {
                return Task.CompletedTask;
            }
            _ = Task.Run(async () =>
            {
                try
                {
                    var hubClient = _services.GetRequiredService<Services.Hub.AetherHubContext>();
                    state.ApplySnapshot(await hubClient.GetEchoRoomSyncAsync(roomId).ConfigureAwait(false));
                }
                catch (Exception ex)
                {
                    _log.Warning(ex, "[AetherSignalService] Echo re-join failed; treating the room as gone.");
                    state.ApplyRoomEnded(new Shared.EchoVidya.EchoRoomEndedDto(
                        roomId, Shared.EchoVidya.EchoEndReason.Empty));
                }
            });
            return Task.CompletedTask;
        };
    }

    private void RegisterYapperHandlers(HubConnection hub)
    {
        hub.On<Shared.Yapper.YapperNotificationPushDto>("YapperNotification", payload =>
        {
            _yapperRelay.Raise(payload);
        });
        hub.On<Shared.Yapper.YapperDmPushDto>("YapperDm", payload =>
        {
            _yapperRelay.RaiseDm(payload);
        });
        hub.On<Shared.Yapper.YapperDmReadPushDto>("YapperDmRead", payload =>
        {
            _yapperRelay.RaiseDmRead(payload);
        });
        hub.On<Shared.Yapper.YapperDmReactionPushDto>("YapperDmReaction", payload =>
        {
            _yapperRelay.RaiseDmReaction(payload);
        });
        hub.On<Shared.Yapper.YapperDmPinPushDto>("YapperDmPin", payload =>
        {
            _yapperRelay.RaiseDmPinned(payload);
        });
        hub.On<Shared.Yapper.YapperDmDeletedPushDto>("YapperDmDeleted", payload =>
        {
            _yapperRelay.RaiseDmDeleted(payload);
        });
        hub.On<Shared.Yapper.YapperDmImageRemovedPushDto>("YapperDmImageRemoved", payload =>
        {
            _yapperRelay.RaiseDmImageRemoved(payload);
        });
    }

    private void RegisterMessengerHandlers(HubConnection hub)
    {
        hub.On<Shared.Messenger.MessengerRequestPushDto>("MessengerRequest", payload =>
        {
            _messenger.ApplyRequest(payload.Request);
            if (payload.Request.Incoming)
            {
                if (_config.Messenger.NotifyRequest)
                {
                    _notifier.NotifyMessengerRequest(payload.Request.PeerName);
                }
                PostMessengerNotification(
                    Loc.T("notif.msgr_request_title"), payload.Request.PeerName, chatId: null);
            }
        });

        hub.On<Shared.Messenger.MessengerContactChangedPushDto>("MessengerContactChanged", payload =>
        {
            _messenger.ApplyContactChanged(payload.Contact);
        });

        hub.On<Shared.Messenger.MessengerContactRemovedPushDto>("MessengerContactRemoved", payload =>
        {
            // The contact audience is gone, so their hangout marker goes with them.
            if (_messenger.Contact(payload.ContactId) is { } removed)
            {
                _hangouts.RemoveContact(removed.PeerAccountId);
            }
            _messenger.ApplyContactRemoved(payload.ContactId, payload.PeerName);
        });

        hub.On<Shared.Messenger.MessengerMessagePushDto>("MessengerMessage", payload =>
        {
            var fromMe = payload.Message.SenderAccountId == _messenger.MyAccountId;
            // Both legs must hold: the chat stamp says WHICH chat, the foreground check says it is actually
            // on screen. The stamp alone is not enough, because it survives a trip to the home screen.
            var viewing = _host.IsAppInForeground("messenger")
                && _messenger.ActiveChatId == payload.Message.ChatId;
            _messenger.ApplyMessage(payload.Message, viewing);
            if (!fromMe && !viewing)
            {
                if (_config.Messenger.NotifyMessage)
                {
                    _notifier.NotifyMessengerMessage();
                }
                PostMessengerNotification(
                    Loc.T("os.app_messenger"), Loc.T("notif.msgr_message_body"), payload.Message.ChatId);
            }
            if (!fromMe && viewing)
            {
                // The user is looking at the chat: confirm the read server-side so the unread counter and
                // the sender's receipt stay live, mirroring the match chat's on-arrival mark-read.
                _messenger.MarkReadLocal(payload.Message.ChatId);
                var hubContext = _services.GetRequiredService<AetherHubContext>();
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await hubContext.MarkMessengerReadAsync(payload.Message.ChatId, payload.Message.Kind)
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _log.Warning(ex, "[AetherSignalService] messenger live mark-read failed.");
                    }
                });
            }
        });

        hub.On<Shared.Messenger.MessengerImageRemovedPushDto>("MessengerImageRemoved", payload =>
        {
            _messenger.ApplyImageRemoved(payload.ImageId);
        });

        hub.On<Shared.Messenger.MessengerReadPushDto>("MessengerRead", payload =>
        {
            _messenger.ApplyRead(payload.ContactId, payload.ReadAtUtc, payload.MessageIds);
        });

        hub.On<Shared.Messenger.MessengerReactionPushDto>("MessengerReaction", payload =>
        {
            _messenger.ApplyReactions(payload.ChatId, payload.MessageId, payload.Reactions);
        });

        hub.On<Shared.Messenger.MessengerPinPushDto>("MessengerPin", payload =>
        {
            _messenger.ApplyPin(payload.ChatId, payload.MessageId, payload.PinnedAtUtc);
        });

        hub.On<Shared.Messenger.MessengerMessageDeletedPushDto>("MessengerMessageDeleted", payload =>
        {
            // Without the cached conversation the chat-list preview can't be recomputed locally; a sync
            // pulls the server's recomputed denormal instead.
            if (_messenger.ApplyMessageDeleted(payload.ChatId, payload.MessageId))
            {
                _ = _services.GetRequiredService<Messenger.MessengerSyncService>().SyncAsync();
            }
        });

        hub.On<Shared.Messenger.MessengerGroupChangedPushDto>("MessengerGroupChanged", payload =>
        {
            var known = _messenger.Group(payload.Group.GroupId) is not null;
            _messenger.ApplyGroupChanged(payload.Group);
            var sync = _services.GetRequiredService<Messenger.MessengerSyncService>();
            _ = Task.Run(async () =>
            {
                await sync.EnsureGroupKeysAsync(payload.Group.GroupId).ConfigureAwait(false);
                await sync.MaintainOwnedGroupKeysAsync().ConfigureAwait(false);
            });
            if (!known)
            {
                PostMessengerNotification(
                    Loc.T("os.app_messenger"),
                    Loc.T("notif.msgr_group_added", payload.Group.Name),
                    payload.Group.GroupId);
            }
        });

        hub.On<Shared.Messenger.MessengerGroupKeyDto>("MessengerGroupKeys", payload =>
        {
            _ = _services.GetRequiredService<Messenger.MessengerSyncService>().EnsureGroupKeysAsync(payload.GroupId);
        });

        hub.On<Shared.Messenger.MessengerRemovedFromGroupPushDto>("MessengerRemovedFromGroup", payload =>
        {
            _messenger.ApplyRemovedFromGroup(payload.GroupId);
        });

        hub.On<Shared.Messenger.MessengerMemberKeysResetPushDto>("MessengerMemberKeysReset", payload =>
        {
            _log.Information($"[AetherSignalService] MessengerMemberKeysReset push in group {payload.GroupId}.");
            PostMessengerNotification(
                payload.GroupName,
                Loc.T("notif.msgr_keys_reset", payload.Name),
                payload.GroupId);
        });
    }

    /// <summary>Whether a push whose recipient is <paramref name="forProfileId"/> is for the profile the user is
    /// currently viewing. A default (empty) id is a pre-field server and is treated as the active profile. The
    /// connection receives pushes for every profile of the account, so callers gate active-profile-only side
    /// effects (chat cache, deck, ban screen) on this and treat the rest as a sibling notification.</summary>
    private static bool IsForActive(Guid forProfileId)
    {
        var active = UiHost.Configuration.Auth.ActiveProfileId ?? Guid.Empty;
        return forProfileId == Guid.Empty || forProfileId == active;
    }

    /// <summary>Opens a match chat from a notification tap. For the active profile, a plain OpenChat intent. For a
    /// sibling profile, the peer is not in the active profile's cache, so switch to that profile first (on a
    /// background task) and only then open the chat, so the tap lands the user in the right conversation.</summary>
    private void OpenLoveChatFromNotification(bool forActive, Guid forProfileId, Guid peerId)
    {
        var shell = _services.GetRequiredService<AetherOS.Sdk.IOsShell>();
        if (forActive || forProfileId == Guid.Empty)
        {
            shell.SendIntent("aetherlove", AetherOS.Sdk.OsIntents.Create(AetherOS.Sdk.OsIntents.OpenChat, peerId));
            return;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                await _services.GetRequiredService<SessionBootstrapper>().SwitchProfileAsync(forProfileId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "[AetherSignalService] Switching to profile {Profile} for a notification tap failed.", forProfileId);
            }
            shell.SendIntent("aetherlove", AetherOS.Sdk.OsIntents.Create(AetherOS.Sdk.OsIntents.OpenChat, peerId));
        });
    }

    /// <summary>Posts a new-match OS notification (notification center + status-bar bell), tagged per peer so it
    /// shares the chat tag: opening that match's chat clears it, and a later message from the peer replaces it. A
    /// sibling profile's notification is titled with that profile's name and its tap switches to it first.</summary>
    private void PostLoveMatchNotification(MatchCreatedPushDto payload, bool forActive, Guid forProfileId)
    {
        var shell = _services.GetRequiredService<AetherOS.Sdk.IOsShell>();
        shell.PostNotification(
            appId: "aetherlove",
            title: forActive ? "AetherLove" : (_siblingBadges.NameFor(forProfileId) ?? "AetherLove"),
            body: Loc.T("notif.matched_with_popup", payload.OtherDisplayName),
            onTap: () => OpenLoveChatFromNotification(forActive, forProfileId, payload.OtherProfileId),
            tag: NotificationCenter.ChatTag(payload.OtherProfileId));
    }

    /// <summary>Posts a match-chat OS notification tagged per peer; opening that chat clears it everywhere.
    /// The body stays generic (no sender name), matching the native chat line. A sibling profile's notification is
    /// titled with that profile's name and its tap switches to it first.</summary>
    private void PostLoveMessageNotification(Guid peerId, bool forActive, Guid forProfileId)
    {
        var shell = _services.GetRequiredService<AetherOS.Sdk.IOsShell>();
        shell.PostNotification(
            appId: "aetherlove",
            title: forActive ? "AetherLove" : (_siblingBadges.NameFor(forProfileId) ?? "AetherLove"),
            body: Loc.T("notif.new_message"),
            onTap: () => OpenLoveChatFromNotification(forActive, forProfileId, peerId),
            tag: NotificationCenter.ChatTag(peerId));
    }

    /// <summary>Posts a messenger OS notification tagged per chat, so opening the chat clears it everywhere.
    /// Requests share one tag; opening the messenger clears it.</summary>
    private void PostMessengerNotification(string title, string body, Guid? chatId)
    {
        var shell = _services.GetRequiredService<AetherOS.Sdk.IOsShell>();
        var intent = chatId is { } id
            ? AetherOS.Sdk.OsIntents.Create(AetherOS.Sdk.OsIntents.OpenChat, id)
            : AetherOS.Sdk.OsIntents.Create(AetherOS.Sdk.OsIntents.OpenChat);
        shell.PostNotification(
            appId: "messenger",
            title: title,
            body: body,
            onTap: () => shell.SendIntent("messenger", intent),
            tag: chatId is { } cid ? $"msgr:chat:{cid:N}" : "msgr:requests");
    }

    /// <summary>Posts a Daily Eorzean OS notification whose tap deep-links into the news app (published entry
    /// or staff-preview draft). The app clears it by tag when the entry is opened.</summary>
    private void PostNewsNotification(Guid id, string title, bool preview)
    {
        // The tile unread badge (AppendNewsToSnapshot) is independent; only the interrupting notification is gated.
        if (!_config.NewsNotificationsEnabled)
        {
            return;
        }
        var shell = _services.GetRequiredService<AetherOS.Sdk.IOsShell>();
        shell.PostNotification(
            appId: "news",
            title: Loc.T("os.app_news"),
            body: title,
            onTap: () => shell.SendIntent("news", NewsIntent(id, preview)),
            tag: $"news:{id:N}");
    }

    private static AetherOS.Sdk.OsIntent NewsIntent(Guid id, bool preview) =>
        AetherOS.Sdk.OsIntents.Create(preview ? AetherOS.Sdk.OsIntents.OpenPreview : AetherOS.Sdk.OsIntents.OpenEntry, id);

    private void InvalidateProfileCaches() => _notifications.NotifyProfileCachesInvalidated();

    /// <summary>Not awaited: the connect paths hold <c>_gate</c> and the fetch re-enters <see cref="EnsureConnectedAsync"/>.</summary>
    private void RefreshConnectionInfo()
    {
        _ = _services.GetRequiredService<SessionBootstrapper>().RefreshConnectionInfoAsync();
        _ = _services.GetRequiredService<FlairCatalog>().RefreshAsync();
        // A superlike that landed while disconnected sent no push; only a fresh pull can surface it.
        _notifications.NotifyDeckRefreshRequested();
    }

    /// <summary>Re-pulls the account snapshot after a reconnect and surfaces anything issued during the outage:
    /// staff notices do not ride the connection-info refresh the way the AetherLove track does, so without this
    /// a notice pushed while the client was away would wait for the next plugin restart.</summary>
    private async Task RefreshStaffNoticesAsync()
    {
        var bootstrap = _services.GetRequiredService<SessionBootstrapper>();
        await bootstrap.RefreshAccountInfoAsync().ConfigureAwait(false);
        if (bootstrap.HasUnseenStaffNotices)
        {
            RaiseStaffNotice();
        }
    }

    /// <summary>Shows the OS staff-notice gate, or defers it to the next phone open. Never raises an AetherLove
    /// notification, badge or chat line, and never auto-acknowledges: only the gate's button acks.</summary>
    private void RaiseStaffNotice()
    {
        if (!_host.IsPhoneOpen)
        {
            _notifications.RaisePendingStaffNotice();
            return;
        }
        // Navigating to the screen the router is already on skips OnShow, so a notice arriving mid-gate has to
        // be folded into the displayed batch instead.
        if (_router.Current == Screen.StaffNotice)
        {
            _host.RefreshStaffNoticeGate();
            return;
        }
        _host.RequestStaffNoticeLiveAcknowledge();
        _router.Navigate(Screen.StaffNotice);
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

    private void GoOffline() => _debouncedOffline = true;

    private void RestoreOnline() => _debouncedOffline = false;

    private void SetState(SignalConnectionState s) =>
        Interlocked.Exchange(ref _stateRaw, (int)s);
}
