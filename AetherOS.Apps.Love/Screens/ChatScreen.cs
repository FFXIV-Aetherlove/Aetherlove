using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Emoji;
using AetherLove.Emoji.Segments;
using AetherLove.Services;
using AetherLove.Services.Crypto;
using AetherLove.Services.Chat;
using AetherLove.Services.Hub;
using AetherLove.Services.Localization;
using AetherLove.Shared.Messaging;
using AetherLove.Shared.Moderation;
using AetherLove.Shared.Profile.Enums;
using AetherLove.UI;
using AetherLove.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;

namespace AetherLove.Screens;

/// <summary>E2E-encrypted chat conversation.</summary>
public partial class ChatScreen
{
    private readonly LoveShell _shell;
    private readonly LoveRouter _router;
    private readonly ChatListScreen _chatListScreen;
    private readonly ProfileScreen _profileScreen;
    private readonly EncryptionVerificationScreen _verifyScreen;
    private readonly AetherHubContext _hub;
    private readonly CryptoService _crypto;
    private readonly KeyStorageService _keys;
    private readonly ChatEventBus _events;
    private readonly ChatSyncService _sync;

    private sealed record DisplayedMessage(
        Guid Id,
        string Text,
        bool IsOwn,
        DateTimeOffset SentAt,
        DateTimeOffset? ReadByOtherAtUtc,
        bool IsDeleted = false,
        AetherLove.Shared.Messaging.ChatImageDto? Image = null);

    private readonly List<DisplayedMessage> _messages = new();
    /// <summary>Guards <see cref="_messages"/>: rendered on the UI thread, mutated from worker threads.</summary>
    private readonly object _messagesLock = new();

    // Valid for one inner-width + line-height; _msgRowH is estimated until the row is first drawn.
    private readonly Dictionary<Guid, float> _msgContentH = new();
    private readonly Dictionary<Guid, float> _msgRowH = new();

    /// <summary>Live-arrival entrance progress (0→1) per message id.</summary>
    private readonly Dictionary<Guid, float> _entryAnim = new();
    private const float EntranceDuration = 0.3f;
    private float _msgCacheWidth = -1f;
    private float _msgCacheLineH = -1f;
    private Guid _peerId;
    private string _peerName = string.Empty;
    private byte[] _peerAvatar = [];
    private bool _peerIsSupporter;
    private NameStyle _peerNameStyle;
    private bool _peerHolidayMode;
    private string? _peerFrameRef;
    private byte[]? _peerPublicKey;
    private byte[]? _messageKey;
    private string _inputText = string.Empty;

    /// <summary>Per-era message keys derived from the peer's key timeline (a reset splits the conversation
    /// into eras). Messages decrypt with the era covering their timestamp; failures render the unreadable
    /// placeholder (which is exactly what an own reset leaves behind).</summary>
    private readonly List<(DateTimeOffset From, DateTimeOffset? Until, byte[] Key)> _eraKeys = new();

    /// <summary>Timestamps that render a "keys were reset" divider in the message list.</summary>
    private readonly List<(DateTimeOffset At, string Text)> _keyResetNotices = new();
    private DateTimeOffset? _myKeysCreatedAt;

    /// <summary>Per-peer unsent input; in-memory for the session only.</summary>
    private readonly Dictionary<Guid, string> _drafts = new();

    /// <summary>Inner width of the chat input box, refreshed each frame for the wrap callback to measure.</summary>
    private float _chatWrapWidth;

    private const int ChatInputMaxLines = 5;

    /// <summary>Emoji whose favorite menu is open (right-clicked in a message); drives ##chatEmojiFavMenu.</summary>
    private string? _chatFavName;

    private const int AutocompleteMax = 5;
    private const float AutocompleteRowH = 40f;

    /// <summary>Emoji shortcodes matching the in-progress ":query" at the input; drives the autocomplete strip.</summary>
    private List<string>? _acMatches;
    private string? _acQuery;
    private bool _acCursorToEnd;
    private bool _reclaimInputFocus;

    private ISharedImmediateTexture? _headerAvatarTex;
    private volatile bool _loading;
    private volatile string? _loadError;

    /// <summary>True while the load error is the keyless-profile one, so the bubble offers the recovery
    /// screen instead of a dead end.</summary>
    private bool _e2eSetupOffered;
    private double _loadStartedAt;
    /// <summary>Delay before the loading hint shows, so a cache-backed open never flashes a spinner.</summary>
    private const double LoadIndicatorDelay = 0.25;
    private CancellationTokenSource _cts = new();

    /// <summary>Reset to -1 on show; the first drawn frame starts the open fade.</summary>
    private double _openFadeAt = -1;
    private const double OpenFadeDuration = 0.20;

    private float _scrollToBottom;

    /// <summary>Message a search jumped to; empty for a normal open.</summary>
    private Guid _scrollTargetMessageId;
    private float _scrollToMessageTimer;
    private float _flashTimer;
    private const float FlashDuration = 3.0f;
    private const int FlashPulses = 3;

    private bool _msgSearchOpen;
    private bool _msgSearchFocus;
    private string _msgSearchQuery = string.Empty;
    private string _msgSearchApplied = string.Empty;
    private readonly List<Guid> _msgSearchHits = new();
    private int _msgSearchIndex;
    private bool _msgSearchArmed;
    private const float SearchBarH = 34f;
    private const int MinSearchLen = 3;

    private bool _systemNoticeDismissed;

    private readonly EmojiPickerPopup _chatEmojiPicker = new();
    private readonly PeerActionConfirm _peerConfirm = new();

    private bool _reportPendingOpen;
    private string _reportReason = string.Empty;
    private bool _reportCheckAgree;
    private bool _reportCheckContents;
    private volatile bool _reportSubmitting;
    private volatile string? _reportError;
    private float _reportSubmittedTimer;

    private const float AvatarR = 22f;
    private const float HeaderH = AvatarR * 2f + 10f;
    private const float MenuBtnW = 36f;

    private readonly NotificationCenter _notifications;

    public ChatScreen(
        LoveShell shell,
        LoveRouter router,
        ChatListScreen chatListScreen,
        ProfileScreen profileScreen,
        EncryptionVerificationScreen verifyScreen,
        AetherHubContext hub,
        CryptoService crypto,
        KeyStorageService keys,
        ChatEventBus events,
        NotificationCenter notifications,
        ChatSyncService sync,
        SettingsScreen settingsScreen,
        VenueShareContext shareCtx,
        PartyInviteShareContext partyInviteShareCtx,
        HangoutShareContext hangoutShareCtx,
        NewsShareContext newsShareCtx,
        CalendarShareContext calendarShareCtx,
        LevemeteShareContext levemeteShareCtx,
        MarketShareContext marketShareCtx,
        HangoutOpener hangoutOpener,
        Services.Messenger.MessengerStore messengerStore,
        Services.Market.MarketDataService marketData,
        Services.Market.MarketItemIndex marketIndex,
        AetherOS.Sdk.IAppCapabilities caps)
    {
        _shell = shell;
        _router = router;
        _chatListScreen = chatListScreen;
        _profileScreen = profileScreen;
        _verifyScreen = verifyScreen;
        _hub = hub;
        _crypto = crypto;
        _keys = keys;
        _events = events;
        _notifications = notifications;
        _sync = sync;
        _settingsScreen = settingsScreen;
        _shareCtx = shareCtx;
        _partyInviteShareCtx = partyInviteShareCtx;
        _hangoutShareCtx = hangoutShareCtx;
        _newsShareCtx = newsShareCtx;
        _calendarShareCtx = calendarShareCtx;
        _levemeteShareCtx = levemeteShareCtx;
        _marketShareCtx = marketShareCtx;
        _hangoutOpener = hangoutOpener;
        _messengerStore = messengerStore;
        _marketData = marketData;
        _marketIndex = marketIndex;
        _caps = caps;
        _translate = new TranslateUi("lovechat", caps.Translation,
            () => shell.Shell?.SendIntent("settings", AetherOS.Sdk.OsIntents.CreateReturn(
                AetherOS.Sdk.OsIntents.OpenTranslationSettings, "aetherlove")));
    }

    private readonly Services.Messenger.MessengerStore _messengerStore;

    private readonly SettingsScreen _settingsScreen;
    private readonly VenueShareContext _shareCtx;
    private readonly PartyInviteShareContext _partyInviteShareCtx;
    private readonly HangoutShareContext _hangoutShareCtx;
    private readonly NewsShareContext _newsShareCtx;
    private readonly CalendarShareContext _calendarShareCtx;
    private readonly LevemeteShareContext _levemeteShareCtx;
    private readonly MarketShareContext _marketShareCtx;
    private readonly HangoutOpener _hangoutOpener;
    private readonly Services.Market.MarketDataService _marketData;
    private readonly Services.Market.MarketItemIndex _marketIndex;
    private readonly AetherOS.Sdk.IAppCapabilities _caps;
    private readonly TranslateUi _translate;
    private long _translateVersion;

    // Queued share body, auto-sent once the message key is ready; the app to return to when the chat was opened
    // from a cross-app share (Places/Hangouts), else null for the normal in-app back.
    private string? _pendingShareSend;
    private string? _backOverrideApp;

    private void OpenSupporterSettings()
    {
        _settingsScreen.RequestSupporterView();
    }

    public void OnShow()
    {
        _events.MessageReceived += OnMessageReceived;
        _events.ChatImageRemoved += OnChatImageRemoved;
        _events.MessageRead += OnMessageRead;
        _events.Unmatched += OnUnmatched;
        _events.BlockedByPeer += OnBlockedByPeer;
        _events.ReactionsChanged += OnReactionsChanged;
        _events.PinChanged += OnPinChanged;
        _events.MessageDeleted += OnMessageDeleted;
        _events.PeerKeysReset += OnPeerKeysReset;

        _cts.Cancel();
        _cts.Dispose();
        _cts = new CancellationTokenSource();

        _peerId = _chatListScreen.SelectedPeerId;
        _peerName = _chatListScreen.SelectedPeerName;
        _peerAvatar = _chatListScreen.SelectedPeerAvatar;
        _peerIsSupporter = _chatListScreen.SelectedPeerIsSupporter;
        _peerNameStyle = _chatListScreen.SelectedPeerNameStyle;
        _peerHolidayMode = _chatListScreen.SelectedPeerHolidayMode;
        _peerFrameRef = _chatListScreen.SelectedPeerFrameRef;
        if (_partyInviteShareCtx.PendingParty is { } pendingParty)
        {
            _partyInviteShareCtx.PendingParty = null;
            _pendingShareSend = PartyShare.Compose(pendingParty.PartyId, pendingParty.Code);
        }
        else if (_shareCtx.PendingShareVenueId is { } shareVenueId)
        {
            _shareCtx.PendingShareVenueId = null;
            _pendingShareSend = VenueShare.Compose(shareVenueId);
            _backOverrideApp = "places";
        }
        else if (_hangoutShareCtx.PendingShareHangoutId is { } shareHangoutId)
        {
            _hangoutShareCtx.PendingShareHangoutId = null;
            _pendingShareSend = HangoutShare.Compose(shareHangoutId);
            _backOverrideApp = "hangouts";
        }
        else if (_newsShareCtx.PendingShareNewsId is { } shareNewsId)
        {
            _newsShareCtx.PendingShareNewsId = null;
            _pendingShareSend = NewsShare.Compose(shareNewsId);
            _backOverrideApp = "news";
        }
        else if (_marketShareCtx.PendingShareItemId is { } shareMarketItemId)
        {
            _marketShareCtx.PendingShareItemId = null;
            _pendingShareSend = MarketShare.Compose(shareMarketItemId);
            _backOverrideApp = "market";
        }
        else if (_calendarShareCtx.PendingShareToken is { } shareCalToken)
        {
            _calendarShareCtx.PendingShareToken = null;
            _pendingShareSend = shareCalToken;
            _backOverrideApp = "calendar";
        }
        else if (_levemeteShareCtx.PendingShareLevemeteId is { } shareLevemeteId)
        {
            _levemeteShareCtx.PendingShareLevemeteId = null;
            _pendingShareSend = LevemeteShare.Compose(shareLevemeteId);
            _backOverrideApp = "levemetes";
        }
        else
        {
            _pendingShareSend = null;
            _backOverrideApp = null;
        }
        ResetFailedVenueCards();
        ResetFailedHangoutCards();
        ResetFailedNewsCards();
        ResetFailedLevemeteCards();
        ResetFailedEchoCards();
        ResetFailedPartyCards();
        lock (_messagesLock)
        {
            _messages.Clear();
            _entryAnim.Clear();
        }
        _msgContentH.Clear();
        _msgRowH.Clear();
        _peerPublicKey = null;
        _messageKey = null;
        _inputText = _drafts.TryGetValue(_peerId, out var draft) ? draft : string.Empty;
        // Drop the previous chat's queued work here; ResetEnhancements must not, since hydrate enqueues it ahead of the per-message seed actions.
        _uiActions.Clear();
        ResetEnhancements();
        _scrollTargetMessageId = _chatListScreen.SelectedScrollMessageId;
        _scrollToMessageTimer = 0f;
        _flashTimer = 0f;
        CloseMsgSearch();
        _scrollToBottom = _scrollTargetMessageId == Guid.Empty ? 1f : 0f;
        _systemNoticeDismissed = false;
        _openFadeAt = -1;
        _notifications.ActiveChatPeerId = _peerId;
        _shell.Shell?.DismissByTag(NotificationCenter.ChatTag(_peerId));
        // Clears the match's "needs a first hello" highlight in the list.
        if (_peerId != Guid.Empty && UiHost.Configuration.OpenedChats.Add(_peerId))
        {
            UiHost.Configuration.Save();
        }
        LoadHeaderAvatar();
        StartLoadConversation();
    }

    public void OnHide()
    {
        StashDraft();
        _events.MessageReceived -= OnMessageReceived;
        _events.ChatImageRemoved -= OnChatImageRemoved;
        _events.MessageRead -= OnMessageRead;
        _events.Unmatched -= OnUnmatched;
        _events.BlockedByPeer -= OnBlockedByPeer;
        _events.ReactionsChanged -= OnReactionsChanged;
        _events.PinChanged -= OnPinChanged;
        _events.MessageDeleted -= OnMessageDeleted;
        _events.PeerKeysReset -= OnPeerKeysReset;
        if (_notifications.ActiveChatPeerId == _peerId)
        {
            _notifications.ActiveChatPeerId = Guid.Empty;
        }
        SaveReactionUsageIfDirty();
        _chatListScreen.CloseCategoryEditor();
        _cts.Cancel();
    }

    /// <summary>The Love app left the foreground with this chat still the current view: the user is no
    /// longer reading it, so messages from this peer must notify again.</summary>
    public void OnAppBackground()
    {
        if (_notifications.ActiveChatPeerId == _peerId)
        {
            _notifications.ActiveChatPeerId = Guid.Empty;
        }
    }

    /// <summary>Warm resume straight back into this chat (no navigation, so OnShow does not re-run).</summary>
    public void OnAppForeground()
    {
        if (_peerId != Guid.Empty)
        {
            _notifications.ActiveChatPeerId = _peerId;
            _shell.Shell?.DismissByTag(NotificationCenter.ChatTag(_peerId));
        }
    }

    private void StashDraft()
    {
        if (_peerId == Guid.Empty)
        {
            return;
        }
        if (string.IsNullOrEmpty(_inputText))
        {
            _drafts.Remove(_peerId);
        }
        else
        {
            _drafts[_peerId] = _inputText;
        }
    }

    private void LoadHeaderAvatar()
    {
        var cacheDir = Path.Combine(UiHost.PluginInterface.ConfigDirectory.FullName, "ChatAvatarCache");
        _headerAvatarTex = AvatarDiskCache.Store(cacheDir, _peerId.ToString(), _peerAvatar);
    }

    private void StartLoadConversation()
    {
        if (_peerId == Guid.Empty)
        {
            return;
        }
        if (!_keys.HasLocalKey)
        {
            // Reached when silent provisioning could not mint this profile's keys (a recreated profile on
            // a device with nothing to wrap under). The recovery screen fixes it; the bubble carries a door.
            _loadError = Loc.T("chat.e2e_self_broken");
            _e2eSetupOffered = true;
            _loading = false;
            return;
        }
        HydrateConversationFromCache();

        _loading = true;
        _loadStartedAt = ImGui.GetTime();
        _loadError = null;
        _e2eSetupOffered = false;
        var ct = _cts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await _sync.SyncAsync(ct).ConfigureAwait(false);
                if (ct.IsCancellationRequested)
                {
                    return;
                }
                KeyHistoryEntryDto[]? fetchedHistory = null;
                DateTimeOffset? fetchedMyKeys = null;
                if (!_sync.Cache.HasConversation(_peerId))
                {
                    // Not covered by the delta yet (e.g. a brand-new match).
                    var dto = await _hub.GetConversationAsync(_peerId, ct).ConfigureAwait(false);
                    if (ct.IsCancellationRequested)
                    {
                        return;
                    }
                    _peerPublicKey = dto.PeerPublicKey;
                    fetchedHistory = dto.PeerKeyHistory;
                    fetchedMyKeys = dto.MyKeysCreatedAtUtc;
                    _sync.Cache.SeedConversation(_peerId, dto.Messages);
                }
                HydrateConversationFromCache(fetchedHistory, fetchedMyKeys);
                if (_scrollTargetMessageId != Guid.Empty)
                {
                    _scrollToMessageTimer = 0.6f;
                    _flashTimer = AccessibilityService.ReduceMotion ? 0f : FlashDuration;
                }
                else
                {
                    _scrollToBottom = 1f;
                }
                var readIds = await _hub.MarkConversationReadAsync(_peerId, ct).ConfigureAwait(false);
                if (readIds.Length > 0)
                {
                    _notifications.UnreadChatMessages =
                        Math.Max(0, _notifications.UnreadChatMessages - readIds.Length);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (ct.IsCancellationRequested)
                {
                    return;
                }
                _loadError = HubErrorText.Localize(ex);
                UiHost.Log.Warning(ex, "[ChatScreen] conversation load failed.");
            }
            finally
            {
                _loading = false;
            }
        }, ct);
    }

    /// <summary>Rebuilds the message list from the local cache; enhancement reset and reseed are marshalled
    /// to the UI thread so they never race the draw.</summary>
    private void HydrateConversationFromCache(
        KeyHistoryEntryDto[]? fetchedHistory = null, DateTimeOffset? fetchedMyKeys = null)
    {
        var pub = _sync.Cache.GetPeerPublicKey(_peerId) ?? _peerPublicKey;
        if (pub is null)
        {
            return;
        }
        _peerPublicKey = pub;
        EnsureMessageKey();
        RebuildKeyEras(fetchedHistory ?? _sync.Cache.GetPeerKeyHistory(_peerId));
        var msgs = _sync.Cache.GetConversation(_peerId);
        lock (_messagesLock)
        {
            _messages.Clear();
            _entryAnim.Clear();
        }
        _uiActions.Enqueue(ResetEnhancements);
        DecryptAndAppend(msgs);
        ApplyMyKeysCreated(fetchedMyKeys ?? _myKeysCreatedAt);
    }

    private void EnsureMessageKey()
    {
        if (_messageKey is not null)
        {
            return;
        }
        var myPriv = _keys.GetPrivateKey();
        if (myPriv is null || _peerPublicKey is null)
        {
            return;
        }
        var myPub = _keys.GetPublicKey() ?? [];
        var shared = _crypto.DeriveSharedSecret(myPriv, _peerPublicKey);
        // Salt must match on both sides: derive from the two public keys, not profile IDs (a client doesn't know its own).
        var salt = CryptoService.DeriveConversationSalt(myPub, _peerPublicKey);
        _messageKey = _crypto.DeriveMessageKey(shared, salt);
    }

    /// <summary>Rebuilds the per-era keys and the reset-notice dividers from the peer's key timeline.</summary>
    private void RebuildKeyEras(KeyHistoryEntryDto[]? history)
    {
        _eraKeys.Clear();
        _keyResetNotices.RemoveAll(n => n.Text != Loc.T("chat.keys_reset_you"));
        var myPriv = _keys.GetPrivateKey();
        var myPub = _keys.GetPublicKey();
        if (history is not { Length: > 0 } || myPriv is null || myPub is null)
        {
            return;
        }
        foreach (var era in history)
        {
            if (era.PublicKey is not { Length: > 0 })
            {
                continue;
            }
            var shared = _crypto.DeriveSharedSecret(myPriv, era.PublicKey);
            var salt = CryptoService.DeriveConversationSalt(myPub, era.PublicKey);
            _eraKeys.Add((era.FromUtc, era.UntilUtc, _crypto.DeriveMessageKey(shared, salt)));
            if (era.FromUtc != history[0].FromUtc)
            {
                _keyResetNotices.Add((era.FromUtc, Loc.T("chat.keys_reset_peer", _peerName)));
            }
        }
    }

    private void ApplyMyKeysCreated(DateTimeOffset? createdAt)
    {
        _myKeysCreatedAt = createdAt;
        _keyResetNotices.RemoveAll(n => n.Text == Loc.T("chat.keys_reset_you"));
        // Only an actual reset earns the divider: a first-ever bundle predates every message.
        if (createdAt is { } at)
        {
            lock (_messagesLock)
            {
                if (_messages.Count > 0 && _messages[0].SentAt < at)
                {
                    _keyResetNotices.Add((at, Loc.T("chat.keys_reset_you")));
                }
            }
        }
    }

    private byte[]? KeyForTimestamp(DateTimeOffset at)
    {
        foreach (var era in _eraKeys)
        {
            if (at >= era.From && (era.Until is null || at < era.Until))
            {
                return era.Key;
            }
        }
        return _messageKey;
    }

    private void DecryptAndAppend(EncryptedMessageDto[] dtos)
    {
        if (_messageKey is null)
        {
            return;
        }
        lock (_messagesLock)
        {
            foreach (var m in dtos)
            {
                DisplayedMessage? d = TryDecrypt(m);
                if (d is not null)
                {
                    _messages.Add(d);
                    var dto = m;
                    _uiActions.Enqueue(() => SeedEnhancements(dto));
                }
            }
        }
    }

    private DisplayedMessage? TryDecrypt(EncryptedMessageDto m)
    {
        if (m.DeletedAtUtc is not null)
        {
            return new DisplayedMessage(m.Id, Loc.T("chat.deleted_by_author"),
                m.SenderProfileId != _peerId, m.CreatedAtUtc, m.ReadByOtherAtUtc, IsDeleted: true);
        }
        if (_messageKey is null)
        {
            return null;
        }
        var era = KeyForTimestamp(m.CreatedAtUtc);
        var key = era ?? _messageKey;
        try
        {
            // An image with no caption carries no ciphertext at all; decrypting nothing is not a failure.
            var text = m.Ciphertext.Length == 0 && m.Image is not null
                ? string.Empty
                : Encoding.UTF8.GetString(_crypto.Decrypt(key, m.Nonce, m.Ciphertext));
            return new DisplayedMessage(
                m.Id, text, m.SenderProfileId != _peerId, m.CreatedAtUtc, m.ReadByOtherAtUtc, Image: m.Image);
        }
        catch (Exception ex)
        {
            // A message from before an OWN key reset can never decrypt again; anything else is a real fault.
            var ownReset = _myKeysCreatedAt is { } at && m.CreatedAtUtc < at;
            if (!ownReset)
            {
                UiHost.Log.Warning(ex, $"[ChatScreen] Decrypt failed for {m.Id}.");
            }
            return new DisplayedMessage(m.Id, Loc.T("chat.unreadable_message"), m.SenderProfileId != _peerId,
                m.CreatedAtUtc, m.ReadByOtherAtUtc, Image: m.Image);
        }
    }

    private void OnMessageReceived(MessageReceivedPushDto p)
    {
        if (p.FromProfileId != _peerId)
        {
            return;
        }
        if (_messageKey is null)
        {
            return;
        }
        try
        {
            var text = p.Ciphertext.Length == 0 && p.Image is not null
                ? string.Empty
                : Encoding.UTF8.GetString(_crypto.Decrypt(_messageKey, p.Nonce, p.Ciphertext));
            lock (_messagesLock)
            {
                _messages.Add(new DisplayedMessage(p.MessageId, text, false, p.CreatedAtUtc, null, Image: p.Image));
                _entryAnim[p.MessageId] = 0f;
            }
            if (p.ReplyToMessageId is { } replyId)
            {
                _uiActions.Enqueue(() => _replyTo[p.MessageId] = replyId);
            }
            _scrollToBottom = 1f;
            _ = _hub.MarkConversationReadAsync(_peerId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            UiHost.Log.Warning(ex, "[ChatScreen] Live decrypt failed.");
        }
    }

    private void OnMessageRead(MessageReadPushDto p)
    {
        if (p.ByProfileId != _peerId)
        {
            return;
        }
        var ids = new HashSet<Guid>(p.MessageIds);
        lock (_messagesLock)
        {
            for (int i = 0; i < _messages.Count; i++)
            {
                if (ids.Contains(_messages[i].Id))
                {
                    _messages[i] = _messages[i] with { ReadByOtherAtUtc = p.ReadAtUtc };
                }
            }
        }
    }

    /// <summary>The open peer reset their keys: adopt the new public key and rebuild the conversation with
    /// the fresh key timeline so the reset divider appears live.</summary>
    private void OnPeerKeysReset(PeerKeysResetPushDto p)
    {
        if (p.PeerProfileId != _peerId)
        {
            return;
        }
        _peerPublicKey = p.NewPublicKey;
        _messageKey = null;
        _ = Task.Run(async () =>
        {
            try
            {
                var dto = await _hub.GetConversationAsync(_peerId, CancellationToken.None).ConfigureAwait(false);
                _peerPublicKey = dto.PeerPublicKey;
                _sync.Cache.SeedConversation(_peerId, dto.Messages);
                HydrateConversationFromCache(dto.PeerKeyHistory, dto.MyKeysCreatedAtUtc);
            }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, "[ChatScreen] Refetch after PeerKeysReset failed.");
            }
        });
    }

    private void OnUnmatched(UnmatchedPushDto p)
    {
        if (p.OtherProfileId != _peerId)
        {
            return;
        }
        _router.Navigate(_chatListScreen.ChatBackTarget);
    }

    private void OnBlockedByPeer(BlockedByPeerPushDto p)
    {
        if (p.OtherProfileId != _peerId)
        {
            return;
        }
        _router.Navigate(_chatListScreen.ChatBackTarget);
    }

    public void Draw()
    {
        if (_peerId == Guid.Empty)
        {
            ImGui.Text(Loc.T("chat.no_conversation_selected"));
            return;
        }

        DrainUiActions();
        DrainIdMigrations();

        // A translation swap changes a bubble's text, so any height computed from the old text is stale.
        if (_translate.Version != _translateVersion)
        {
            _translateVersion = _translate.Version;
            _msgContentH.Clear();
            _msgRowH.Clear();
        }

        // Gated on the load FINISHING, not just the key: the loader clears _messages after the key becomes
        // ready, so sending in that window would wipe the share's optimistic bubble until the next reopen.
        if (_pendingShareSend is { } shareText && _messageKey is not null && !_loading)
        {
            _pendingShareSend = null;
            var stashedDraft = _inputText;
            _inputText = shareText;
            SendMessage();
            _inputText = stashedDraft;
        }

        if (_pendingReactionPickerId is { } reactRid)
        {
            _pendingReactionPickerId = null;
            _chatEmojiPicker.Open(name => ToggleMyReaction(reactRid, name));
        }
        if (_pinnedListPendingOpen)
        {
            _pinnedListPendingOpen = false;
            ImGui.OpenPopup("##pinnedOverlay");
        }

        if (_reportSubmittedTimer > 0f)
        {
            _reportSubmittedTimer -= ImGui.GetIO().DeltaTime;
        }

        var contentTL = ImGui.GetCursorScreenPos();
        var contentSize = ImGui.GetContentRegionAvail();

        DrawHeader();
        DrawReportSubmittedToast();
        RefreshAutocomplete();
        DrawMessages();
        DrawInput();
        DrawPinnedOverlay();
        DrawCalendarEventPrompt();
        DrawUserNoteOverlay();
        DrawImageComposeOverlay();
        DrawImageReportOverlay();
        DrawImageViewer();

        DrawOpenFade(contentTL, contentSize);

        // Hosts the chat list's category create overlay so the overflow menu can open it here.
        _chatListScreen.DrawCategoryEditorOverlay();

        _peerConfirm.Draw(ImGui.GetWindowPos(), ImGui.GetWindowSize(),
            (action, _) =>
            {
                if (action == PeerAction.Unmatch)
                {
                    FireUnmatch();
                }
                else
                {
                    FireBlock();
                }
            });

        DrawDeleteMessageConfirm(ImGui.GetWindowPos(), ImGui.GetWindowSize());

        _translate.DrawConsentOverlay(ImGui.GetWindowPos(), ImGui.GetWindowSize());

        SupporterInfoPopup.Draw(ImGui.GetWindowPos(), ImGui.GetWindowSize(), OpenSupporterSettings);

        if (_reportPendingOpen)
        {
            _reportPendingOpen = false;
            Widgets.ModalHost.Instance?.Open(310f, DrawReportBody);
        }
    }

    /// <summary>Fades a window-background cover out on open; drawn last so it covers the chat content
    /// but not the bottom nav.</summary>
    private void DrawOpenFade(Vector2 contentTL, Vector2 contentSize)
    {
        if (AccessibilityService.ReduceMotion)
        {
            return;
        }
        if (_openFadeAt < 0)
        {
            _openFadeAt = ImGui.GetTime();
        }
        var t = (ImGui.GetTime() - _openFadeAt) / OpenFadeDuration;
        if (t >= 1.0)
        {
            return;
        }
        var a = (uint)(Math.Clamp(1.0 - t, 0.0, 1.0) * 255.0);
        var bg = ImGui.GetColorU32(ImGuiCol.WindowBg) & 0x00FFFFFFu;
        var col = bg | (a << 24);
        ImGui.GetWindowDrawList().AddRectFilled(contentTL, contentTL + contentSize, col);
    }

    private bool _noteOpen;
    private float _notePanelH;
    private string _noteText = string.Empty;

    /// <summary>Local-only note about this match; saving an empty text removes it.</summary>
    private void DrawUserNoteOverlay()
    {
        if (!_noteOpen)
        {
            return;
        }
        var dismissed = DrawPageOverlayPanel("chatUserNote", ImGui.GetWindowPos(), ImGui.GetWindowSize(),
            ref _notePanelH, Px(300f), w =>
        {
            Widgets.ModalUi.Header(w, FontAwesomeIcon.StickyNote, Loc.T("chat.menu_user_note"),
                ThemeService.Current.AccentLight);
            ImGui.PushTextWrapPos(w);
            ImGui.TextColored(UiColors.Hint, Loc.T("chat.note_hint"));
            ImGui.PopTextWrapPos();
            ImGui.Spacing();
            ImGui.SetNextItemWidth(w);
            InputTextMultilineWithPaste("##chatNoteText", ref _noteText, 1000, new Vector2(w, Px(90f)));
            ImGui.Spacing();
            if (Widgets.ModalUi.Button($"{Loc.T("chat.note_save")}##chatNoteSave", w))
            {
                var trimmed = _noteText.Trim();
                if (trimmed.Length == 0)
                {
                    UiHost.Configuration.MatchNotes.Remove(_peerId);
                }
                else
                {
                    UiHost.Configuration.MatchNotes[_peerId] = trimmed;
                }
                UiHost.Configuration.Save();
                _noteOpen = false;
            }
        });
        if (dismissed)
        {
            _noteOpen = false;
        }
    }

    private void DrawReportSubmittedToast()
    {
        if (_msgrInviteToast > 0f)
        {
            _msgrInviteToast -= ImGui.GetIO().DeltaTime;
            DrawToastBar(Loc.T("chat.msgr_request_sent"), Math.Clamp(_msgrInviteToast / 4f, 0f, 1f), "##msgrInviteToast");
        }
        if (_reportSubmittedTimer <= 0f)
        {
            return;
        }
        DrawToastBar(Loc.T("chat.report_submitted_toast"), Math.Clamp(_reportSubmittedTimer / 4f, 0f, 1f), "##reportToast");
    }

    private static void DrawToastBar(string message, float alpha, string id)
    {
        var col = new Vector4(0.18f, 0.62f, 0.30f, alpha);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, col);
        using (var c = ImRaii.Child(id, new Vector2(ImGui.GetContentRegionAvail().X, Px(26f)), false))
        {
            if (c.Success)
            {
                var msg = message;
                var sz = ImGui.CalcTextSize(msg);
                ImGui.SetCursorPosX((ImGui.GetContentRegionAvail().X - sz.X) * 0.5f);
                ImGui.SetCursorPosY((Px(26f) - sz.Y) * 0.5f);
                ImGui.TextColored(new Vector4(1f, 1f, 1f, alpha), msg);
            }
        }
        ImGui.PopStyleColor();
        ImGui.Spacing();
    }

    private void DrawHeader()
    {
        var t = ThemeService.Current;
        var winW = ImGui.GetWindowSize().X;
        var dl = ImGui.GetWindowDrawList();
        var screenPos = ImGui.GetCursorScreenPos();

        var headerH = Px(HeaderH);
        var centerY = screenPos.Y + headerH * 0.5f;
        var btnTop = screenPos.Y + (headerH - Px(MenuBtnW)) * 0.5f;
        var iconFont = UiHost.PluginInterface.UiBuilder.FontIcon;

        var backBtnX = screenPos.X + Px(2f);
        ImGui.SetCursorScreenPos(new Vector2(backBtnX, btnTop));
        ImGui.InvisibleButton("##chatBackBtn", Px(MenuBtnW, MenuBtnW));
        var backHovered = ImGui.IsItemHovered();
        if (backHovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            ImGui.SetTooltip(Loc.T(_backOverrideApp is null ? "chat.back_to_matches" : "places.back"));
        }
        if (ImGui.IsItemClicked())
        {
            if (_backOverrideApp is { } backApp)
            {
                _shell.Shell?.OpenApp(backApp);
            }
            else
            {
                _router.Navigate(_chatListScreen.ChatBackTarget);
            }
        }
        ImGui.PushFont(iconFont);
        var backIcon = FontAwesomeIcon.ArrowLeft.ToIconString();
        var backIconSz = ImGui.CalcTextSize(backIcon);
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
            new Vector2(backBtnX + (Px(MenuBtnW) - backIconSz.X) * 0.5f,
                        btnTop + (Px(MenuBtnW) - backIconSz.Y) * 0.5f),
            backHovered ? t.AccentLightU32 : t.AccentU32, backIcon);
        ImGui.PopFont();

        var menuBtnX = screenPos.X + winW - Px(MenuBtnW + 2f);
        ImGui.SetCursorScreenPos(new Vector2(menuBtnX, btnTop));
        ImGui.InvisibleButton("##chatMenuBtn", Px(MenuBtnW, MenuBtnW));
        var menuHovered = ImGui.IsItemHovered();
        var menuClicked = ImGui.IsItemClicked();
        ImGui.PushFont(iconFont);
        var menuIcon = FontAwesomeIcon.Cog.ToIconString();
        var menuIconSz = ImGui.CalcTextSize(menuIcon);
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
            new Vector2(menuBtnX + (Px(MenuBtnW) - menuIconSz.X) * 0.5f,
                        btnTop + (Px(MenuBtnW) - menuIconSz.Y) * 0.5f),
            menuHovered ? t.AccentLightU32 : t.AccentU32, menuIcon);
        ImGui.PopFont();

        var avatarD = Px(AvatarR * 2f);
        var gap = Px(8f);
        var groupLeft = backBtnX + Px(MenuBtnW) + Px(6f);
        var maxNameW = MathF.Max(0f, menuBtnX - Px(6f) - (groupLeft + avatarD + gap));

        var isSupporter = _peerIsSupporter;
        string shownName;
        Vector2 nameSz;
        var starPx = 0f;
        using (UiFonts.H3?.Push())
        {
            starPx = ImGui.GetFontSize() * 0.55f;
            var nameRoom = isSupporter ? MathF.Max(0f, maxNameW - starPx - Px(6f)) : maxNameW;
            shownName = TruncateToWidth(_peerName, nameRoom);
            nameSz = ImGui.CalcTextSize(shownName);
        }

        // The peer group fires on press; overlapping the star would navigate before its release-click.
        var groupW = avatarD + gap + nameSz.X;
        var avatarCenter = new Vector2(groupLeft + avatarD * 0.5f, centerY);

        var headerAvatarWrap = _headerAvatarTex?.GetWrapOrDefault();
        if (headerAvatarWrap != null)
        {
            dl.AddImageRounded(headerAvatarWrap.Handle,
                avatarCenter - Px(AvatarR, AvatarR), avatarCenter + Px(AvatarR, AvatarR),
                Vector2.Zero, Vector2.One, 0xFFFFFFFF, Px(AvatarR), ImDrawFlags.RoundCornersAll);
        }
        else
        {
            dl.AddCircleFilled(avatarCenter, Px(AvatarR), UiColors.AvatarFallback);
        }
        dl.AddCircle(avatarCenter, Px(AvatarR), t.AccentWithAlpha(0.65f), 0, 1.5f);
        AvatarRings.Draw(dl, avatarCenter, Px(AvatarR), _peerFrameRef);

        if (_peerHolidayMode)
        {
            var awayR = Px(7f);
            var awayCenter = avatarCenter + new Vector2(Px(AvatarR) - Px(5f), Px(AvatarR) - Px(5f));
            dl.AddCircleFilled(awayCenter, awayR, ImGui.GetColorU32(UiColors.HolidayPurple));
            dl.AddCircle(awayCenter, awayR, 0xFFFFFFFF, 0, Px(1.5f));
            IconDraw.AddCentered(dl, FontAwesomeIcon.Clock, awayR * 1.15f, awayCenter, 0xFFFFFFFFu);
        }

        using (UiFonts.H3?.Push())
        {
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
                new Vector2(groupLeft + avatarD + gap, centerY - nameSz.Y * 0.5f),
                SupporterStyle.NameColor(_peerNameStyle, 0xFFFFFFFF), shownName);
        }

        ImGui.SetCursorScreenPos(new Vector2(groupLeft, centerY - avatarD * 0.5f));
        ImGui.InvisibleButton("##chatPeerGroup", new Vector2(groupW, avatarD));
        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            var overAvatar = ImGui.GetMousePos().X <= groupLeft + avatarD;
            if (overAvatar && UiHost.Configuration.MatchNotes.TryGetValue(_peerId, out var note)
                && note.Length > 0)
            {
                ImGui.SetTooltip($"{Loc.T("chat.note_tooltip")}\n{note}");
            }
            else
            {
                ImGui.SetTooltip(Loc.T("chat.view_profile"));
            }
        }
        if (ImGui.IsItemClicked())
        {
            OpenPeerProfile();
        }

        // Submitted after the peer-group button so the star's click wins.
        if (isSupporter)
        {
            var starTL = new Vector2(groupLeft + avatarD + gap + nameSz.X + Px(6f),
                centerY - nameSz.Y * 0.5f + nameSz.Y * 0.24f);
            IconDraw.Add(dl, FontAwesomeIcon.Star, starPx, starTL, UiColors.FavoriteStar);
            ImGui.SetCursorScreenPos(starTL - Px(2f, 2f));
            if (ImGui.InvisibleButton("##chatSupStar", new Vector2(starPx + Px(4f), starPx + Px(4f))))
            {
                SupporterInfoPopup.Open();
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(Loc.T("profile.section_supporter"));
            }
        }

        if (menuClicked)
        {
            ImGui.OpenPopup("##chatMenu");
        }

        if (ImGui.BeginPopup("##chatMenu"))
        {
            if (DrawIconMenuItem(FontAwesomeIcon.User, Loc.T("chat.menu_open_profile")))
            {
                ImGui.CloseCurrentPopup();
                OpenPeerProfile();
            }
            if (DrawIconMenuItem(FontAwesomeIcon.Qrcode, Loc.T("verify.title")))
            {
                ImGui.CloseCurrentPopup();
                OpenVerify();
            }
            if (DrawIconMenuItem(FontAwesomeIcon.Search, Loc.T("chat.menu_search")))
            {
                ImGui.CloseCurrentPopup();
                OpenMsgSearch();
            }
            if (DrawIconMenuItem(FontAwesomeIcon.Thumbtack, Loc.T("chat.pinned_messages_menu", _pinned.Count),
                    enabled: _pinned.Count > 0))
            {
                ImGui.CloseCurrentPopup();
                _pinnedListPendingOpen = true;
            }
            var pinned = _chatListScreen.IsPinned(_peerId);
            if (DrawIconMenuItem(pinned ? FontAwesomeIcon.ThumbtackSlash : FontAwesomeIcon.Thumbtack,
                    pinned ? Loc.T("chat.menu_unpin") : Loc.T("chat.menu_pin")))
            {
                ImGui.CloseCurrentPopup();
                _chatListScreen.SetPinned(_peerId, !pinned);
            }
            _chatListScreen.DrawChatOverflowCategoryItems(_peerId);
            if (DrawIconMenuItem(FontAwesomeIcon.StickyNote, Loc.T("chat.menu_user_note")))
            {
                ImGui.CloseCurrentPopup();
                _noteText = UiHost.Configuration.MatchNotes.GetValueOrDefault(_peerId, string.Empty);
                _notePanelH = 0f;
                _noteOpen = true;
            }
            if (DrawIconMenuItem(FontAwesomeIcon.CommentDots, Loc.T("chat.menu_invite_messenger"),
                    enabled: _messengerStore.Sync?.MyCode is { Length: > 0 }))
            {
                ImGui.CloseCurrentPopup();
                if (_messengerStore.Sync?.MyCode is { Length: > 0 } myCode)
                {
                    _pendingShareSend = MessengerShare.Compose(myCode);
                }
            }
            ImGui.Separator();
            if (DrawIconMenuItem(FontAwesomeIcon.Unlink, Loc.T("chat.menu_unmatch")))
            {
                ImGui.CloseCurrentPopup();
                _peerConfirm.Open(PeerAction.Unmatch, _peerId);
            }
            if (DrawIconMenuItem(FontAwesomeIcon.Ban, Loc.T("chat.menu_block")))
            {
                ImGui.CloseCurrentPopup();
                _peerConfirm.Open(PeerAction.Block, _peerId);
            }
            ImGui.Separator();
            if (DrawIconMenuItem(FontAwesomeIcon.ExclamationTriangle, Loc.T("chat.menu_report_user"), UiColors.MenuReport))
            {
                ImGui.CloseCurrentPopup();
                _reportReason = string.Empty;
                _reportCheckAgree = false;
                _reportCheckContents = false;
                _reportSubmitting = false;
                _reportError = null;
                _reportPendingOpen = true;
            }
            ImGui.EndPopup();
        }

        ImGui.SetCursorScreenPos(new Vector2(screenPos.X, screenPos.Y + Px(HeaderH)));
        ImGui.Separator();
        ImGui.Spacing();
    }

    private void FireUnmatch()
    {
        var peer = _peerId;
        _ = Task.Run(async () =>
        {
            try { await _hub.UnmatchAsync(peer); }
            catch (Exception ex) { UiHost.Log.Warning(ex, "[ChatScreen] UnmatchAsync failed."); }
        });
        _router.Navigate(_chatListScreen.ChatBackTarget);
    }

    private void FireBlock()
    {
        var peer = _peerId;
        _ = Task.Run(async () =>
        {
            try { await _hub.BlockUserAsync(peer); }
            catch (Exception ex) { UiHost.Log.Warning(ex, "[ChatScreen] BlockUserAsync failed."); }
        });
        _router.Navigate(_chatListScreen.ChatBackTarget);
    }

    private void OpenPeerProfile()
    {
        _profileScreen.SetProfile(_peerId, ProfileSource.Chat);
        _router.Navigate(LoveView.Profile);
    }

    private void OpenVerify()
    {
        _verifyScreen.SetContext(_peerName, _peerPublicKey);
        _router.Navigate(LoveView.EncryptionVerification);
    }

    private void FireReport()
    {
        if (_reportSubmitting)
        {
            return;
        }
        _reportSubmitting = true;
        _reportError = null;

        var peer = _peerId;
        var reason = _reportReason;
        var includeConvo = _reportCheckContents;

        ConversationSnapshotEntry[]? snapshot = null;
        byte[]? convKey = null;
        if (includeConvo)
        {
            lock (_messagesLock)
            {
                snapshot = _messages
                    .Select(m => new ConversationSnapshotEntry(
                        FromMe: m.IsOwn,
                        Text: m.Text,
                        SentAtUtc: m.SentAt.ToUniversalTime()))
                    .ToArray();
            }
            // The key lets the server decrypt its own stored ciphertext into a tamper-evident transcript; used once, never stored.
            EnsureMessageKey();
            convKey = _messageKey;
        }

        var req = new ReportUserRequest(
            ReportedProfileId: peer,
            Reason: reason,
            IncludeConversation: includeConvo,
            ConversationSnapshot: snapshot,
            ConversationKey: convKey);

        _ = Task.Run(async () =>
        {
            try
            {
                await _hub.ReportUserAsync(req).ConfigureAwait(false);
                _reportSubmitting = false;
                _reportSubmittedTimer = 4f;
                _closeReportPopup = true;
            }
            catch (Exception ex)
            {
                _reportError = HubErrorText.Localize(ex);
                _reportSubmitting = false;
                UiHost.Log.Warning(ex, "[ChatScreen] ReportUserAsync failed.");
            }
        });
    }

    private volatile bool _closeReportPopup;

    private void OpenMsgSearch()
    {
        _msgSearchOpen = true;
        _msgSearchFocus = true;
    }

    private void CloseMsgSearch()
    {
        _msgSearchOpen = false;
        _msgSearchFocus = false;
        _msgSearchQuery = string.Empty;
        _msgSearchApplied = string.Empty;
        _msgSearchHits.Clear();
        _msgSearchIndex = 0;
        _msgSearchArmed = false;
    }

    private void RecomputeMsgSearch()
    {
        var q = _msgSearchQuery.Trim();
        if (q == _msgSearchApplied)
        {
            return;
        }
        _msgSearchApplied = q;
        _msgSearchHits.Clear();
        if (q.Length >= MinSearchLen)
        {
            lock (_messagesLock)
            {
                foreach (var m in _messages)
                {
                    if (!string.IsNullOrEmpty(m.Text) && m.Text.Contains(q, StringComparison.OrdinalIgnoreCase))
                    {
                        _msgSearchHits.Add(m.Id);
                    }
                }
            }
        }
        _msgSearchIndex = _msgSearchHits.Count - 1;
        _msgSearchArmed = _msgSearchHits.Count > 0;
    }

    /// <summary><paramref name="dir"/> is -1 for older, +1 for newer; the first navigation after a query
    /// lands on the most recent hit.</summary>
    private void MsgSearchNavigate(int dir)
    {
        if (_msgSearchHits.Count == 0)
        {
            return;
        }
        if (_msgSearchArmed)
        {
            _msgSearchArmed = false;
        }
        else
        {
            _msgSearchIndex = (_msgSearchIndex + dir + _msgSearchHits.Count) % _msgSearchHits.Count;
        }
        JumpToMessage(_msgSearchHits[_msgSearchIndex]);
    }

    /// <summary>Consumes exactly <see cref="SearchBarH"/> so the message-area sizing matches.</summary>
    private void DrawMessageSearchBar()
    {
        if (!_msgSearchOpen)
        {
            return;
        }
        var start = ImGui.GetCursorPos();
        var t = ThemeService.Current;
        var iconFont = UiHost.PluginInterface.UiBuilder.FontIcon;
        var avail = ImGui.GetContentRegionAvail().X;
        var btnW = Px(28f);
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var countW = Px(52f);
        var inputW = MathF.Max(Px(60f), avail - Px(16f) - btnW * 3f - countW - spacing * 4f);

        ImGui.SetCursorPos(new Vector2(Px(8f), start.Y + Px(4f)));
        if (_msgSearchFocus)
        {
            ImGui.SetKeyboardFocusHere();
            _msgSearchFocus = false;
        }
        ImGui.SetNextItemWidth(inputW);
        var submit = ImGui.InputTextWithHint("##msgSearchInput", Loc.T("chat.search_messages_hint"),
            ref _msgSearchQuery, 100, ImGuiInputTextFlags.EnterReturnsTrue);

        RecomputeMsgSearch();

        var hits = _msgSearchHits.Count;
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(hits > 0 ? UiColors.Body : UiColors.Muted, $"{(hits > 0 ? _msgSearchIndex + 1 : 0)}/{hits}");

        ImGui.SameLine();
        PushThemeButton(t);
        ImGui.PushFont(iconFont);
        var prev = ImGui.Button(FontAwesomeIcon.ChevronUp.ToIconString() + "##msgSearchPrev", new Vector2(btnW, 0f));
        ImGui.SameLine();
        var next = ImGui.Button(FontAwesomeIcon.ChevronDown.ToIconString() + "##msgSearchNext", new Vector2(btnW, 0f));
        ImGui.SameLine();
        var close = ImGui.Button(FontAwesomeIcon.Times.ToIconString() + "##msgSearchClose", new Vector2(btnW, 0f));
        ImGui.PopFont();
        PopThemeButton();

        if (submit || prev)
        {
            MsgSearchNavigate(-1);
        }
        if (next)
        {
            MsgSearchNavigate(+1);
        }
        if (close)
        {
            CloseMsgSearch();
        }

        ImGui.SetCursorPos(new Vector2(start.X, start.Y + Px(SearchBarH)));
    }

    private void DrawMessages()
    {
        DrawMessageSearchBar();
        var availableHeight = ImGui.GetWindowSize().Y - Px(HeaderH + 20f) - InputBarHeight()
            - (_msgSearchOpen ? Px(SearchBarH) : 0f);
        PushScrollbarStyle();

        using (var child = ImRaii.Child("MessageArea", new Vector2(0, availableHeight), false))
        {
            PopScrollbarStyle();
            if (!child.Success)
            {
                return;
            }
            var windowWidth = ImGui.GetContentRegionAvail().X;

            DisplayedMessage[] messages;
            lock (_messagesLock)
            {
                messages = _messages.ToArray();
            }

            // Reset before drawing bubbles; each bubble arms the capture around its own text.
            SegmentEmoji.CaptureRightClick = false;
            SegmentEmoji.RightClickedName = null;

            if (!_systemNoticeDismissed && !_loading && _loadError is null
                && !messages.Any(m => m.IsOwn))
            {
                DrawSystemNotice();
            }

            if (_loading && messages.Length == 0 && ImGui.GetTime() - _loadStartedAt > LoadIndicatorDelay)
            {
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), Loc.T("chat.loading_messages"));
            }
            if (_loadError is not null)
            {
                DrawErrorBubble(_loadError);
                if (_e2eSetupOffered)
                {
                    ImGui.Spacing();
                    ImGui.SetCursorPosX(Px(8f));
                    if (SharedUiHelpers.Button(Loc.T("chat.e2e_setup_button"),
                            new Vector2(ImGui.GetContentRegionAvail().X - Px(8f), Px(32f)))
                        && _shell.OpenEncryptionRecovery is { } openRecovery)
                    {
                        openRecovery();
                    }
                }
            }

            var lineH = ImGui.GetTextLineHeight();
            if (_msgCacheWidth != windowWidth || _msgCacheLineH != lineH)
            {
                _msgContentH.Clear();
                _msgRowH.Clear();
                _msgCacheWidth = windowWidth;
                _msgCacheLineH = lineH;
            }

            // Off-screen rows still reserve their height so the scrollbar extent and auto-scroll stay correct.
            var bandTop = ImGui.GetScrollY() - availableHeight;
            var bandBot = ImGui.GetScrollY() + availableHeight * 2f;

            var drawnSlot = 0; // frame-local child-id slot, so ImGui retains only ~visible-count windows
            var targetY = -1f; // content-Y of the search-jump message, captured even when virtualized away
            for (int i = 0; i < messages.Length; i++)
            {
                var msg = messages[i];
                var prev = i > 0 ? messages[i - 1] : null;
                var next = i < messages.Length - 1 ? messages[i + 1] : null;

                var needsDivider = prev is null || msg.SentAt.Date != prev.SentAt.Date;
                var isGroupStart = StartsNewGroup(msg, prev);
                var isGroupEnd = next is null || StartsNewGroup(next, msg);

                if (!_msgRowH.TryGetValue(msg.Id, out var rowH))
                {
                    rowH = EstimateRowHeight(msg, needsDivider, isGroupEnd, windowWidth);
                    _msgRowH[msg.Id] = rowH;
                }

                var y0 = ImGui.GetCursorPosY();
                if (msg.Id == _scrollTargetMessageId)
                {
                    targetY = y0;
                }
                if (y0 + rowH < bandTop || y0 > bandBot)
                {
                    ImGui.SetCursorPosY(y0 + rowH);
                    continue;
                }

                if (needsDivider)
                {
                    DrawDayDivider(msg.SentAt.LocalDateTime);
                }
                foreach (var notice in _keyResetNotices)
                {
                    if (notice.At <= msg.SentAt && (prev is null || notice.At > prev.SentAt))
                    {
                        DrawKeyResetDivider(notice.Text);
                    }
                }
                DrawMessageBubble(msg, windowWidth, drawnSlot++, isGroupStart, isGroupEnd);
                _msgRowH[msg.Id] = ImGui.GetCursorPosY() - y0; // exact drawn advance
            }

            if (ImGui.BeginPopup("##chatEmojiFavMenu"))
            {
                if (_chatFavName is { } cfav)
                {
                    var label = EmojiFavorites.Contains(cfav)
                        ? Loc.T("common.emoji_remove_favorite")
                        : Loc.T("common.emoji_add_favorite");
                    if (ImGui.MenuItem(label) && EmojiFavorites.Toggle(cfav))
                    {
                        EmojiFavoriteFx.Trigger(ImGui.GetMousePos());
                    }
                }
                ImGui.EndPopup();
            }

            if (ImGui.GetIO().MouseWheel != 0f)
            {
                _scrollToBottom = 0f;
                _scrollToMessageTimer = 0f;
            }

            if (_scrollToMessageTimer > 0f && targetY >= 0f)
            {
                // Re-driven for a few frames so it settles as virtualized rows above resolve their exact height.
                var dest = Math.Clamp(targetY - availableHeight * 0.35f, 0f, ImGui.GetScrollMaxY());
                ImGui.SetScrollY(dest);
                _scrollToMessageTimer -= ImGui.GetIO().DeltaTime;
            }
            else if (_scrollToBottom > 0)
            {
                ImGui.SetScrollY(ImGui.GetScrollMaxY());
                _scrollToBottom -= ImGui.GetIO().DeltaTime;
            }

            if (_flashTimer > 0f)
            {
                _flashTimer -= ImGui.GetIO().DeltaTime;
            }
        }
    }

    /// <summary>Consecutive messages from the same sender within this window render as one tight group.</summary>
    private static readonly TimeSpan GroupWindow = TimeSpan.FromMinutes(5);

    private static bool StartsNewGroup(DisplayedMessage cur, DisplayedMessage? prev)
        => prev is null
           || prev.IsOwn != cur.IsOwn
           || cur.SentAt.Date != prev.SentAt.Date
           || cur.SentAt - prev.SentAt > GroupWindow;

    /// <summary>The side away from the sender's edge stays fully rounded; the edge side only rounds at the
    /// group's outer corners.</summary>
    private static ImDrawFlags BubbleCorners(bool isOwn, bool isStart, bool isEnd)
    {
        if (isStart && isEnd)
        {
            return ImDrawFlags.RoundCornersAll;
        }
        if (isOwn)
        {
            var flags = ImDrawFlags.RoundCornersLeft;
            if (isStart)
            {
                flags |= ImDrawFlags.RoundCornersTopRight;
            }
            if (isEnd)
            {
                flags |= ImDrawFlags.RoundCornersBottomRight;
            }
            return flags;
        }
        else
        {
            var flags = ImDrawFlags.RoundCornersRight;
            if (isStart)
            {
                flags |= ImDrawFlags.RoundCornersTopLeft;
            }
            if (isEnd)
            {
                flags |= ImDrawFlags.RoundCornersBottomLeft;
            }
            return flags;
        }
    }

    private void DrawMessageBubble(DisplayedMessage msg, float windowWidth, int slot, bool isGroupStart, bool isGroupEnd)
    {
        if (msg.Image is not null)
        {
            DrawImageMessage(msg, windowWidth, isGroupEnd);
            return;
        }
        if (VenueShare.TryParse(msg.Text, out var sharedVenueId))
        {
            DrawVenueCardMessage(msg, sharedVenueId, windowWidth, isGroupEnd);
            return;
        }
        if (HangoutShare.TryParse(msg.Text, out var sharedHangoutId))
        {
            DrawHangoutCardMessage(msg, sharedHangoutId, windowWidth, isGroupEnd);
            return;
        }
        if (NewsShare.TryParse(msg.Text, out var sharedNewsId))
        {
            DrawNewsCardMessage(msg, sharedNewsId, windowWidth, isGroupEnd);
            return;
        }
        if (MarketShare.TryParse(msg.Text, out var sharedMarketItemId))
        {
            DrawMarketCardMessage(msg, sharedMarketItemId, windowWidth, isGroupEnd);
            return;
        }
        if (MessengerShare.TryParse(msg.Text, out var inviteCode))
        {
            DrawMessengerInviteCard(msg, inviteCode, windowWidth, isGroupEnd);
            return;
        }
        if (LevemeteShare.TryParse(msg.Text, out var sharedLevemeteId))
        {
            DrawLevemeteCardMessage(msg, sharedLevemeteId, windowWidth, isGroupEnd);
            return;
        }
        if (CalendarEventShare.TryParse(msg.Text, out var sharedCalEvent))
        {
            DrawCalendarEventCard(msg, sharedCalEvent, windowWidth, isGroupEnd);
            return;
        }
        if (EchoShare.TryParse(msg.Text, out var sharedRoomId, out var sharedRoomCode))
        {
            DrawEchoCardMessage(msg, sharedRoomId, sharedRoomCode, windowWidth, isGroupEnd);
            return;
        }
        if (PartyShare.TryParse(msg.Text, out var sharedPartyId, out var sharedPartyCode, out var partyInvite))
        {
            DrawPartyCardMessage(msg, sharedPartyId, sharedPartyCode, partyInvite, windowWidth, isGroupEnd);
            return;
        }

        var parsed = ParsedMessage.Parse(_translate.Display(msg.Id, msg.Text));
        var maxBubW = windowWidth * 0.72f;
        var padding = Px(12, 8);
        var drawList = ImGui.GetWindowDrawList();
        var cursorPos = ImGui.GetCursorScreenPos();

        var (entryDy, entryAlpha) = MessageEntrance(msg.Id);
        var fading = entryAlpha < 0.999f;
        if (fading)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, entryAlpha * ImGui.GetStyle().Alpha);
        }

        uint bubbleColor;
        float bubbleLeft;
        if (msg.IsOwn)
        {
            bubbleColor = ImGui.ColorConvertFloat4ToU32(ChatColors.OwnBg);
            bubbleLeft = cursorPos.X + windowWidth - maxBubW - Px(10);
        }
        else
        {
            bubbleColor = ImGui.ColorConvertFloat4ToU32(ChatColors.PeerBg);
            bubbleLeft = cursorPos.X + Px(10);
        }

        if (fading)
        {
            var bakedAlpha = (uint)(((bubbleColor >> 24) & 0xFFu) * entryAlpha);
            bubbleColor = (bubbleColor & 0x00FFFFFFu) | (bakedAlpha << 24);
        }

        var innerW = maxBubW - padding.X * 2f;

        // Measure via the segment layout, not plain text: a long ":shortcode:" is wide as text but renders one square.
        if (!_msgContentH.TryGetValue(msg.Id, out var contentH))
        {
            contentH = parsed.MeasureHeight(innerW);
            _msgContentH[msg.Id] = contentH;
        }
        var innerH = MathF.Max(contentH, ImGui.GetTextLineHeight());
        var bubbleH = innerH + padding.Y * 2f;

        var quoteH = ReplyQuoteHeight(msg.Id);
        if (quoteH > 0f)
        {
            DrawReplyQuote(msg.Id, bubbleLeft, cursorPos.Y + entryDy, maxBubW);
        }

        var bubbleTL = new Vector2(bubbleLeft, cursorPos.Y + entryDy + quoteH);
        var corners = BubbleCorners(msg.IsOwn, isGroupStart, isGroupEnd);
        drawList.AddRectFilled(bubbleTL, bubbleTL + new Vector2(maxBubW, bubbleH), bubbleColor, Px(10f), corners);
        if (_pinned.Contains(msg.Id))
        {
            DrawPinMarker(bubbleTL, maxBubW, msg.Id, msg.IsOwn);
        }
        if (msg.Id == _scrollTargetMessageId && _flashTimer > 0f)
        {
            // |sin| peaks FlashPulses times, each at full alpha.
            var p = 1f - _flashTimer / FlashDuration;
            var a = MathF.Abs(MathF.Sin(p * FlashPulses * MathF.PI));
            drawList.AddRect(bubbleTL, bubbleTL + new Vector2(maxBubW, bubbleH),
                ImGui.GetColorU32(ThemeService.Current.AccentDark with { W = a }), Px(10f), corners, Px(4f));
        }

        // The child sizes GetContentRegionAvail so the inline word/emoji wrapper and ImGui's wrap agree on the boundary.
        ImGui.SetCursorScreenPos(bubbleTL + padding);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        using (var body = ImRaii.Child($"##msgBody{slot}", new Vector2(innerW, innerH), false,
                   ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            if (body.Success)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, msg.IsOwn ? ChatColors.OwnFg : ChatColors.PeerFg);
                ImGui.PushTextWrapPos(innerW);
                SegmentEmoji.CaptureRightClick = true;
                parsed.Draw();
                SegmentEmoji.CaptureRightClick = false;
                ImGui.PopTextWrapPos();
                ImGui.PopStyleColor();
            }
        }
        ImGui.PopStyleVar();

        var emojiClicked = SegmentEmoji.RightClickedName;
        if (emojiClicked != null)
        {
            SegmentEmoji.RightClickedName = null;
            _chatFavName = emojiClicked;
            ImGui.OpenPopup("##chatEmojiFavMenu");
        }
        else if (!msg.IsDeleted && ImGui.BeginPopupContextItem($"##msgCtx{msg.Id}", ImGuiPopupFlags.MouseButtonRight))
        {
            DrawMessageContextMenu(msg);
            ImGui.EndPopup();
        }

        var reactionsH = DrawReactions(msg, bubbleLeft, bubbleTL.Y + bubbleH, maxBubW);

        if (isGroupEnd)
        {
            var local = msg.SentAt.LocalDateTime;
            var seenSuffix = msg.IsOwn && msg.ReadByOtherAtUtc is not null ? Loc.T("chat.seen_suffix") : string.Empty;
            var timeStr = local.ToString("HH:mm") + seenSuffix;
            var timeSize = ImGui.CalcTextSize(timeStr);
            var timeX = msg.IsOwn ? bubbleTL.X + maxBubW - timeSize.X : bubbleTL.X;
            ImGui.SetCursorScreenPos(new Vector2(timeX, bubbleTL.Y + bubbleH + reactionsH + Px(2f)));
            ImGui.TextColored(new Vector4(0.75f, 0.75f, 0.75f, 0.40f), timeStr);
            ImGui.SetCursorScreenPos(cursorPos + new Vector2(0, quoteH + bubbleH + reactionsH + timeSize.Y + Px(8f)));
        }
        else
        {
            ImGui.SetCursorScreenPos(cursorPos + new Vector2(0, quoteH + bubbleH + reactionsH + Px(2f)));
        }

        if (fading)
        {
            ImGui.PopStyleVar();
        }
    }

    /// <summary>Returns this frame's vertical offset and alpha for a just-arrived message; (0, 1) once finished.</summary>
    private (float dy, float alpha) MessageEntrance(Guid id)
    {
        if (AccessibilityService.ReduceMotion)
        {
            lock (_messagesLock)
            {
                _entryAnim.Remove(id);
            }
            return (0f, 1f);
        }

        float p;
        lock (_messagesLock)
        {
            if (!_entryAnim.TryGetValue(id, out p))
            {
                return (0f, 1f);
            }
            p += ImGui.GetIO().DeltaTime / EntranceDuration;
            if (p >= 1f)
            {
                _entryAnim.Remove(id);
                return (0f, 1f);
            }
            _entryAnim[id] = p;
        }

        var eased = 1f - MathF.Pow(1f - p, 3f);
        return (Px(16f) * (1f - eased), eased);
    }

    /// <summary>Reserve height for an undrawn row; must mirror DrawMessageBubble's layout.</summary>
    private float EstimateRowHeight(DisplayedMessage msg, bool needsDivider, bool isGroupEnd, float windowWidth)
    {
        var padding = Px(12, 8);
        var innerW = windowWidth * 0.72f - padding.X * 2f;
        var lineH = ImGui.GetTextLineHeight();

        if (msg.Image is not null)
        {
            var imageRow = ImageThumbSize(msg.Image, windowWidth).Y + (isGroupEnd ? lineH + Px(8f) : Px(2f));
            if (needsDivider)
            {
                imageRow += lineH + Px(16f);
            }
            return imageRow;
        }
        if (VenueShare.TryParse(msg.Text, out _))
        {
            var cardRow = Px(VenueCardH) + (isGroupEnd ? lineH + Px(8f) : Px(2f));
            if (needsDivider)
            {
                cardRow += lineH + Px(16f);
            }
            return cardRow;
        }
        if (MarketShare.TryParse(msg.Text, out _))
        {
            var cardRow = Px(MarketCardH) + (isGroupEnd ? lineH + Px(8f) : Px(2f));
            if (needsDivider)
            {
                cardRow += lineH + Px(16f);
            }
            return cardRow;
        }
        if (NewsShare.TryParse(msg.Text, out _))
        {
            var cardRow = Px(NewsCardH) + (isGroupEnd ? lineH + Px(8f) : Px(2f));
            if (needsDivider)
            {
                cardRow += lineH + Px(16f);
            }
            return cardRow;
        }
        if (MessengerShare.TryParse(msg.Text, out _))
        {
            var cardRow = Px(MessengerCardH) + (isGroupEnd ? lineH + Px(8f) : Px(2f));
            if (needsDivider)
            {
                cardRow += lineH + Px(16f);
            }
            return cardRow;
        }
        if (LevemeteShare.TryParse(msg.Text, out _))
        {
            var cardRow = Px(LevemeteCardH) + (isGroupEnd ? lineH + Px(8f) : Px(2f));
            if (needsDivider)
            {
                cardRow += lineH + Px(16f);
            }
            return cardRow;
        }
        if (EchoShare.TryParse(msg.Text, out _, out _))
        {
            var cardRow = Px(EchoCardH) + (isGroupEnd ? lineH + Px(8f) : Px(2f));
            if (needsDivider)
            {
                cardRow += lineH + Px(16f);
            }
            return cardRow;
        }
        if (PartyShare.TryParse(msg.Text, out _, out _, out var partyInviteText))
        {
            var cardRow = Px(PartyCardH) + PartyInviteMessageHeight(partyInviteText, windowWidth)
                + (isGroupEnd ? lineH + Px(8f) : Px(2f));
            if (needsDivider)
            {
                cardRow += lineH + Px(16f);
            }
            return cardRow;
        }

        var contentH = _msgContentH.TryGetValue(msg.Id, out var cached)
            ? cached
            : ParsedMessage.Parse(_translate.Display(msg.Id, msg.Text)).MeasureHeight(innerW);

        var bubbleH = MathF.Max(contentH, lineH) + padding.Y * 2f;
        var rowH = isGroupEnd
            ? bubbleH + lineH + Px(8f)
            : bubbleH + Px(2f);
        rowH += ReplyQuoteHeight(msg.Id) + ReactionsHeight(msg.Id);
        if (needsDivider)
        {
            rowH += lineH + Px(16f);
        }
        return rowH;
    }

    private static string Ordinal(int n) => (n % 100) switch
    {
        11 or 12 or 13 => $"{n}th",
        _ => (n % 10) switch
        {
            1 => $"{n}st",
            2 => $"{n}nd",
            3 => $"{n}rd",
            _ => $"{n}th",
        }
    };

    /// <summary>English keeps the ordinal style; other languages use their own long-date pattern so the
    /// English "th" suffix never leaks in.</summary>
    private static string BuildDayLabel(DateTime date)
    {
        if (string.Equals(LanguageProvider.Current.LanguageName, "English", StringComparison.Ordinal))
        {
            var culture = LanguageProvider.CurrentCulture;
            return $"{date.ToString("dddd", culture)}, {Ordinal(date.Day)} {date.ToString("MMMM yyyy", culture)}";
        }
        return LanguageProvider.FormatDate(date, "D");
    }

    /// <summary>The "keys were reset" notice, styled like the day divider but in warning amber.</summary>
    private static void DrawKeyResetDivider(string label)
    {
        var drawList = ImGui.GetWindowDrawList();
        var avail = ImGui.GetContentRegionAvail().X;
        var textSize = ImGui.CalcTextSize(label);
        ImGui.Spacing();
        var origin = ImGui.GetCursorScreenPos();
        var lineY = origin.Y + MathF.Round(textSize.Y * 0.5f);
        var textX = origin.X + MathF.Round((avail - textSize.X) * 0.5f);
        const float Pad = 10f;
        var linCol = ImGui.GetColorU32(new Vector4(0.95f, 0.75f, 0.30f, 0.25f));
        var txtCol = ImGui.GetColorU32(new Vector4(0.95f, 0.75f, 0.30f, 0.85f));
        drawList.AddLine(new Vector2(origin.X + Px(Pad), lineY), new Vector2(textX - Px(Pad), lineY), linCol, 1f);
        drawList.AddText(new Vector2(textX, origin.Y), txtCol, label);
        drawList.AddLine(new Vector2(textX + textSize.X + Px(Pad), lineY), new Vector2(origin.X + avail - Px(Pad), lineY), linCol, 1f);
        ImGui.Dummy(new Vector2(avail, textSize.Y));
        ImGui.Spacing();
    }

    private static void DrawDayDivider(DateTime date)
    {
        var label = BuildDayLabel(date);
        var drawList = ImGui.GetWindowDrawList();
        var avail = ImGui.GetContentRegionAvail().X;
        var textSize = ImGui.CalcTextSize(label);
        ImGui.Spacing();
        var origin = ImGui.GetCursorScreenPos();
        var lineY = origin.Y + MathF.Round(textSize.Y * 0.5f);
        var textX = origin.X + MathF.Round((avail - textSize.X) * 0.5f);
        const float Pad = 10f;
        var linCol = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.10f));
        var txtCol = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.28f));
        drawList.AddLine(new Vector2(origin.X + Px(Pad), lineY), new Vector2(textX - Px(Pad), lineY), linCol, 1f);
        drawList.AddText(new Vector2(textX, origin.Y), txtCol, label);
        drawList.AddLine(new Vector2(textX + textSize.X + Px(Pad), lineY), new Vector2(origin.X + avail - Px(Pad), lineY), linCol, 1f);
        ImGui.Dummy(new Vector2(avail, textSize.Y));
        ImGui.Spacing();
    }

    private float InputBarHeight()
    {
        var h = ChatInputBoxHeight() + Px(14f);
        if (_replyingToId is not null)
        {
            h += ImGui.GetTextLineHeight() + Px(12f);
        }
        if (_acMatches is { Count: > 0 })
        {
            h += Px(AutocompleteRowH);
        }
        return h;
    }

    private float ChatInputBoxHeight()
    {
        var lines = 1;
        for (var i = 0; i < _inputText.Length; i++)
        {
            if (_inputText[i] == '\n')
            {
                lines++;
            }
        }
        lines = Math.Clamp(lines, 1, ChatInputMaxLines);
        return lines * ImGui.GetTextLineHeight() + ImGui.GetStyle().FramePadding.Y * 2f;
    }

    private void RefreshAutocomplete()
    {
        if (!TryDetectShortcode(_inputText, out var query))
        {
            _acMatches = null;
            _acQuery = null;
            return;
        }
        if (query == _acQuery)
        {
            return;
        }
        _acQuery = query;

        var starts = new List<string>();
        var contains = new List<string>();
        foreach (var name in UiHost.EmojiService.All.Keys)
        {
            if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            {
                starts.Add(name);
            }
            else if (name.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                contains.Add(name);
            }
        }
        starts.Sort(RankShortcode);
        contains.Sort(RankShortcode);
        var top = starts.Concat(contains).Take(AutocompleteMax).ToList();
        _acMatches = top.Count > 0 ? top : null;
    }

    private static int RankShortcode(string a, string b)
        => a.Length != b.Length ? a.Length - b.Length : string.CompareOrdinal(a, b);

    /// <summary>An in-progress ":query": a colon at the start or after whitespace, then shortcode
    /// characters with no closing colon.</summary>
    private static bool TryDetectShortcode(string text, out string query)
    {
        query = "";
        var ci = text.LastIndexOf(':');
        if (ci < 0 || (ci > 0 && !char.IsWhiteSpace(text[ci - 1])))
        {
            return false;
        }
        var q = text[(ci + 1)..];
        if (q.Length == 0)
        {
            return false;
        }
        foreach (var c in q)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '_' && c != '-')
            {
                return false;
            }
        }
        query = q;
        return true;
    }

    private void CompleteShortcode(string fullName)
    {
        var ci = _inputText.LastIndexOf(':');
        if (ci < 0)
        {
            return;
        }
        _inputText = _inputText[..ci] + $":{fullName}: ";
        if (_chatWrapWidth > 0f)
        {
            _inputText = WrapForInput(_inputText, _chatWrapWidth);
        }
        _acMatches = null;
        _acQuery = null;
        _reclaimInputFocus = true;
        _acCursorToEnd = true;
    }

    private void DrawEmojiAutocompleteRow()
    {
        if (_acMatches is not { Count: > 0 } matches)
        {
            return;
        }
        // Consume exactly the height InputBarHeight reserved so the input bar stays put.
        var start = ImGui.GetCursorPos();
        var sz = Px(24f);
        ImGui.SetCursorPos(new Vector2(Px(8f), start.Y + Px(3f)));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Px(3f, 3f));
        ImGui.PushStyleColor(ImGuiCol.Button, 0u);
        for (var i = 0; i < matches.Count; i++)
        {
            var name = matches[i];
            var tex = UiHost.EmojiService.GetEmoji(name)?.GetWrapOrDefault();
            ImGui.PushID(i);
            var clicked = tex != null
                ? ImGui.ImageButton(tex.Handle, new Vector2(sz))
                : ImGui.Button(name, new Vector2(sz + Px(6f)));
            ImGui.PopID();
            if (ImGui.IsItemHovered())
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                ImGui.SetTooltip($":{name}:");
            }
            if (clicked)
            {
                CompleteShortcode(name);
            }
            ImGui.SameLine(0f, Px(4f));
        }
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();
        ImGui.SetCursorPos(new Vector2(start.X, start.Y + Px(AutocompleteRowH)));
    }

    private void DrawInput()
    {
        var windowWidth = ImGui.GetWindowSize().X;
        const float EmojiBtn = 28f;
        const float SendBtn = 56f;
        const float Gap = 4f;
        var inputWidth = windowWidth - Px(EmojiBtn) * 2f - Px(SendBtn) - Px(Gap * 4f);

        ImGui.SetCursorPosY(ImGui.GetWindowSize().Y - InputBarHeight());
        DrawEmojiAutocompleteRow();
        ImGui.Separator();
        ImGui.Spacing();

        DrawReplyComposeBar();

        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            if (SharedUiHelpers.Button($"{FontAwesomeIcon.Plus.ToIconString()}##chatAttach",
                    new Vector2(Px(EmojiBtn), 0f)))
            {
                ImGui.OpenPopup("##chatAttach");
            }
        }
        DrawAttachMenu();
        ImGui.SameLine(0, Px(Gap));

        {
            var frameH = ImGui.GetFrameHeight();
            var grinTex = UiHost.EmojiService.GetEmoji("grinning")?.GetWrapOrDefault();
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Px(4f, 4f));
            var clicked = grinTex != null
                ? ImGui.ImageButton(grinTex.Handle, new Vector2(frameH - Px(8f)))
                : ImGui.Button($"{Loc.T("chat.emoji_button")}##chatEmoji", new Vector2(Px(EmojiBtn), 0));
            ImGui.PopStyleVar();
            _chatEmojiPicker.Draw();
            if (clicked)
            {
                _chatEmojiPicker.Open(name =>
                {
                    // The edit callback never sees this external append, so wrap it by hand.
                    _inputText += $":{name}: ";
                    if (_chatWrapWidth > 0f)
                    {
                        _inputText = WrapForInput(_inputText, _chatWrapWidth);
                    }
                });
            }
        }

        ImGui.SameLine(0, Px(Gap));
        // EnterReturnsTrue deactivates the input after sending; re-grab focus next frame.
        if (_reclaimInputFocus)
        {
            ImGui.SetKeyboardFocusHere();
            _reclaimInputFocus = false;
        }
        _chatWrapWidth = inputWidth - ImGui.GetStyle().FramePadding.X * 2f;
        var inputBefore = _inputText;
        var enterPressed = ImGui.InputTextMultiline("##messageInput", ref _inputText, 500,
            new Vector2(inputWidth, ChatInputBoxHeight()),
            ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.CtrlEnterForNewLine
            | ImGuiInputTextFlags.CallbackEdit | ImGuiInputTextFlags.CallbackAlways,
            ChatWrapCallback);
        ArmPasteIfIgnored(textChanged: _inputText != inputBefore);
        ImGui.SameLine(0, Px(Gap));
        if ((ImGui.Button(Loc.T("chat.send"), new Vector2(Px(SendBtn), 0)) || enterPressed) && _inputText.Length > 0)
        {
            SendMessage();
            _reclaimInputFocus = true;
        }
    }

    /// <summary>Reflows the input on each edit; swapping only spaces and newlines keeps the length and cursor stable.</summary>
    private unsafe int ChatWrapCallback(ImGuiInputTextCallbackDataPtr data)
    {
        try
        {
            ImGuiInputTextCallbackData* p = data;
            if (p->EventFlag == ImGuiInputTextFlags.CallbackAlways)
            {
                // Re-focusing after an autocomplete insert parks the cursor at the start; move it to the end.
                if (_acCursorToEnd)
                {
                    _acCursorToEnd = false;
                    p->CursorPos = p->BufTextLen;
                    p->SelectionStart = p->BufTextLen;
                    p->SelectionEnd = p->BufTextLen;
                }
                if (TryConsumeArmedPaste(p))
                {
                    RewrapBuffer(p);
                }
                return 0;
            }
            RewrapBuffer(p);
        }
        catch
        {
            // A managed exception must not cross into the native ImGui call.
        }
        return 0;
    }

    private unsafe void RewrapBuffer(ImGuiInputTextCallbackData* p)
    {
        if (_chatWrapWidth <= 0f || p->BufTextLen <= 0)
        {
            return;
        }
        var current = Encoding.UTF8.GetString(p->Buf, p->BufTextLen);
        var wrapped = WrapForInput(current, _chatWrapWidth);
        if (wrapped == current)
        {
            return;
        }
        var bytes = Encoding.UTF8.GetBytes(wrapped);
        if (bytes.Length != p->BufTextLen)
        {
            return;
        }
        for (var i = 0; i < bytes.Length; i++)
        {
            p->Buf[i] = bytes[i];
        }
        p->BufDirty = 1;
    }

    /// <summary>Length-preserving greedy wrap: swaps spaces and newlines only; long words aren't split.</summary>
    private static string WrapForInput(string text, float width)
    {
        var chars = text.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] == '\n')
            {
                chars[i] = ' ';
            }
        }
        var lineStart = 0;
        var lastSpace = -1;
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] == ' ')
            {
                lastSpace = i;
            }
            var line = new string(chars, lineStart, i - lineStart + 1);
            if (ImGui.CalcTextSize(line).X > width && lastSpace > lineStart)
            {
                chars[lastSpace] = '\n';
                lineStart = lastSpace + 1;
                lastSpace = -1;
            }
        }
        return new string(chars);
    }

    private void SendMessage()
    {
        if (string.IsNullOrWhiteSpace(_inputText))
        {
            return;
        }
        // Slash input mimics the game chat box: a known emote command runs on the character, anything
        // else is dropped; either way nothing is sent to the peer.
        var slashInput = _inputText.Trim();
        if (slashInput.StartsWith('/'))
        {
            _caps.System.TryExecuteEmote(slashInput);
            _inputText = string.Empty;
            _drafts.Remove(_peerId);
            return;
        }
        if (!ParsedMessage.Parse(_inputText).HasVisibleContent)
        {
            return;
        }
        if (_messageKey is null)
        {
            UiHost.Log.Warning("[ChatScreen] Cannot send: message key not derived yet.");
            return;
        }
        var text = _inputText.Replace('\n', ' ');
        _inputText = string.Empty;
        _drafts.Remove(_peerId);
        _scrollToBottom = 1f;
        var replyTarget = _replyingToId;
        _replyingToId = null;

        var key = _messageKey;
        var peer = _peerId;
        var plaintext = Encoding.UTF8.GetBytes(text);
        var (ciphertext, nonce) = _crypto.Encrypt(key, plaintext);

        // Optimistic local append; server returns the final Id+CreatedAt.
        var tempId = Guid.NewGuid();
        var nowLocal = DateTimeOffset.UtcNow;
        lock (_messagesLock)
        {
            _messages.Add(new DisplayedMessage(tempId, text, true, nowLocal, null));
            _entryAnim[tempId] = 0f;
        }
        _unsentTempIds.Add(tempId);
        if (replyTarget is { } rt)
        {
            _replyTo[tempId] = rt;
        }
        // A not-yet-acknowledged target id wouldn't resolve for the peer; DrainIdMigrations rewrites it once it lands.
        var replyForServer = replyTarget is { } target && !_unsentTempIds.Contains(target) ? replyTarget : null;

        _ = Task.Run(async () =>
        {
            try
            {
                var response = await _hub.SendMessageAsync(
                    new SendMessageRequest(peer, ciphertext, nonce, replyForServer), CancellationToken.None)
                    .ConfigureAwait(false);
                lock (_messagesLock)
                {
                    for (int i = 0; i < _messages.Count; i++)
                    {
                        if (_messages[i].Id == tempId)
                        {
                            _messages[i] = _messages[i] with { Id = response.MessageId, SentAt = response.CreatedAtUtc };
                            if (_entryAnim.Remove(tempId, out var entryP))
                            {
                                _entryAnim[response.MessageId] = entryP;
                            }
                            _pendingIdMigrations.Add((tempId, response.MessageId));
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, "[ChatScreen] SendMessageAsync failed.");
                lock (_messagesLock)
                {
                    _messages.RemoveAll(m => m.Id == tempId);
                    _entryAnim.Remove(tempId);
                }
                _uiActions.Enqueue(() =>
                {
                    _unsentTempIds.Remove(tempId);
                    _deferredByTempId.Remove(tempId);
                    _replyTo.Remove(tempId);
                });
            }
        });
    }

    private void DrawErrorBubble(string text)
    {
        var winW = ImGui.GetContentRegionAvail().X;
        const float MarginX = 8f;
        const float PadX = 14f;
        const float PadY = 12f;
        var boxW = winW - Px(MarginX * 2f);
        var wrapW = boxW - Px(PadX * 2f);

        var textH = ImGui.CalcTextSize(text, wrapWidth: wrapW).Y;
        var boxH = Px(PadY) * 2f + textH;

        var scrCursor = ImGui.GetCursorScreenPos();
        var boxTL = scrCursor + Px(MarginX, 0f);
        var boxBR = boxTL + new Vector2(boxW, boxH);
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(boxTL, boxBR, ImGui.GetColorU32(UiColors.Danger with { W = 0.14f }), Px(8f));
        dl.AddRect(boxTL, boxBR, ImGui.GetColorU32(UiColors.Danger with { W = 0.55f }), Px(8f), 0, 1.5f);

        ImGui.SetCursorScreenPos(new Vector2(boxTL.X + Px(PadX), boxTL.Y + Px(PadY)));
        var wrapEnd = ImGui.GetCursorPos().X + wrapW;
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.Danger);
        ImGui.PushTextWrapPos(wrapEnd);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
        ImGui.PopStyleColor();

        ImGui.SetCursorScreenPos(new Vector2(scrCursor.X, boxBR.Y + Px(10f)));
    }

    private void DrawSystemNotice()
    {
        var winW = ImGui.GetContentRegionAvail().X;
        const float MarginX = 8f;
        const float PadX = 16f;
        const float PadY = 16f;
        const float LineGap = 12f;
        const float BtnGap = 16f;
        const float BtnH = 36f;
        const float BtnW = 170f;
        var noticeW = winW - Px(MarginX * 2f);
        var wrapW = noticeW - Px(PadX * 2f);
        var theme = ThemeService.Current;

        var line1 = Loc.T("chat.system_notice_line1", _peerName);
        var line2 = Loc.T("chat.system_notice_line2", _peerName);

        float t1H, t2H;
        using (UiFonts.H3?.Push())
        {
            t1H = ImGui.CalcTextSize(line1, wrapWidth: wrapW).Y;
            t2H = ImGui.CalcTextSize(line2, wrapWidth: wrapW).Y;
        }

        var boxH = Px(PadY) + t1H + Px(LineGap) + t2H + Px(BtnGap) + Px(BtnH) + Px(PadY);
        var scrCursor = ImGui.GetCursorScreenPos();
        var boxTL = scrCursor + Px(MarginX, 0f);
        var boxBR = boxTL + new Vector2(noticeW, boxH);
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(boxTL, boxBR, 0x22D4A84A, Px(8f));
        dl.AddRect(boxTL, boxBR, 0x55D4A84A, Px(8f), 0, 1.5f);

        ImGui.SetCursorScreenPos(new Vector2(boxTL.X + Px(PadX), boxTL.Y + Px(PadY)));
        var wrapEnd = ImGui.GetCursorPos().X + wrapW;
        using (UiFonts.H3?.Push())
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.93f, 0.85f, 0.60f, 0.96f));
            ImGui.PushTextWrapPos(wrapEnd);
            ImGui.TextUnformatted(line1);
            ImGui.PopTextWrapPos();
            ImGui.PopStyleColor();

            ImGui.SetCursorScreenPos(new Vector2(boxTL.X + Px(PadX), boxTL.Y + Px(PadY) + t1H + Px(LineGap)));
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.86f, 0.79f, 0.62f, 0.90f));
            ImGui.PushTextWrapPos(wrapEnd);
            ImGui.TextUnformatted(line2);
            ImGui.PopTextWrapPos();
            ImGui.PopStyleColor();
        }

        ImGui.SetCursorScreenPos(new Vector2(
            boxTL.X + (noticeW - Px(BtnW)) * 0.5f,
            boxTL.Y + Px(PadY) + t1H + Px(LineGap) + t2H + Px(BtnGap)));
        ImGui.PushStyleColor(ImGuiCol.Button, theme.ButtonNormal);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, theme.ButtonHovered);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, theme.ButtonActive);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
        if (ImGui.Button($"{Loc.T("chat.i_understand")}##dismissNotice", Px(BtnW, BtnH)))
        {
            _systemNoticeDismissed = true;
        }
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);

        ImGui.SetCursorScreenPos(new Vector2(scrCursor.X, boxBR.Y + Px(10f)));
    }

    private void DrawReportBody(float availW)
    {
        if (_closeReportPopup)
        {
            _closeReportPopup = false;
            Widgets.ModalHost.Instance?.Close();
            return;
        }

        var t = ThemeService.Current;

        using (UiFonts.H3?.Push())
        {
            var Title = Loc.T("chat.report_title");
            var titleSz = ImGui.CalcTextSize(Title);
            ImGui.SetCursorPosX((availW - titleSz.X) * 0.5f);
            ImGui.TextColored(UiColors.Amber, Title);
        }
        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.Separator, UiColors.Amber with { W = 0.35f });
        ImGui.Separator();
        ImGui.PopStyleColor();
        ImGui.Spacing();
        ImGui.PushTextWrapPos(availW);
        ImGui.TextColored(new Vector4(0.82f, 0.82f, 0.82f, 1f), Loc.T("chat.report_reason_prompt"));
        ImGui.PopTextWrapPos();
        ImGui.Spacing();
        ImGui.SetNextItemWidth(availW);
        InputTextMultilineWithPaste("##reportReason", ref _reportReason, 500, new Vector2(availW, Px(80f)));
        ImGui.Spacing();

        ImGui.Checkbox("##chkAgree", ref _reportCheckAgree);
        ImGui.SameLine(0f, Px(6f));
        ImGui.PushTextWrapPos(0f);
        ImGui.Text(Loc.T("chat.report_agree", _peerName));
        ImGui.PopTextWrapPos();
        ImGui.Spacing();
        ImGui.Checkbox("##chkContents", ref _reportCheckContents);
        ImGui.SameLine(0f, Px(6f));
        ImGui.PushTextWrapPos(0f);
        ImGui.Text(Loc.T("chat.report_agree_contents"));
        ImGui.PopTextWrapPos();
        ImGui.Spacing();
        ImGui.Spacing();

        if (_reportError is not null)
        {
            ImGui.PushTextWrapPos(0f);
            ImGui.TextColored(UiColors.Danger, _reportError);
            ImGui.PopTextWrapPos();
            ImGui.Spacing();
        }
        if (_reportSubmitting)
        {
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), Loc.T("chat.submitting"));
            ImGui.Spacing();
        }

        const float BtnGap = 8f;
        var btnW = (availW - Px(BtnGap)) * 0.5f;
        var canSubmit = _reportCheckAgree && _reportCheckContents
                     && !string.IsNullOrWhiteSpace(_reportReason)
                     && !_reportSubmitting;

        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.22f, 0.22f, 0.22f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.38f, 0.38f, 0.38f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.14f, 0.14f, 0.14f, 1f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
        if (ImGui.Button($"{Loc.T("chat.cancel")}##reportCancel", new Vector2(btnW, Px(32f))) && !_reportSubmitting)
        {
            Widgets.ModalHost.Instance?.Close();
        }
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);
        ImGui.SameLine(0f, Px(BtnGap));

        if (!canSubmit)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.40f);
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3f, 0.3f, 0.3f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.3f, 0.3f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.3f, 0.3f, 0.3f, 1f));
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Button, t.ButtonNormal);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, t.ButtonHovered);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, t.ButtonActive);
        }
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
        if (ImGui.Button($"{Loc.T("chat.submit")}##reportSubmit", new Vector2(btnW, Px(32f))) && canSubmit)
        {
            FireReport();
        }
        ImGui.PopStyleVar(canSubmit ? 1 : 2);
        ImGui.PopStyleColor(3);
    }

}
