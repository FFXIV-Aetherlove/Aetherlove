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
using AetherLove.Navigation;
using AetherLove.Services;
using AetherLove.Services.Crypto;
using AetherLove.Services.Chat;
using AetherLove.Services.Hub;
using AetherLove.Services.Localization;
using AetherLove.Shared.Messaging;
using AetherLove.Shared.Moderation;
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
    private readonly ScreenRouter _router;
    private readonly ChatListScreen _chatListScreen;
    private readonly ProfileScreen _profileScreen;
    private readonly EncryptionVerificationScreen _verifyScreen;
    private readonly AetherLoveHubClient _hub;
    private readonly CryptoService _crypto;
    private readonly KeyStorageService _keys;
    private readonly ChatEventBus _events;
    private readonly ChatSyncService _sync;

    private sealed record DisplayedMessage(
        Guid Id,
        string Text,
        bool IsOwn,
        DateTimeOffset SentAt,
        DateTimeOffset? ReadByOtherAtUtc);

    private readonly List<DisplayedMessage> _messages = new();
    /// <summary>Guards <see cref="_messages"/>: rendered on the UI thread, mutated from worker threads.</summary>
    private readonly object _messagesLock = new();

    // Virtualization caches, valid for one inner-width + line-height (cleared on resize / UI-scale
    // change). _msgContentH = wrapped text height inside a bubble; _msgRowH = total row advance
    // (day divider + bubble + timestamp + gap) — exact once drawn, estimated until then.
    private readonly Dictionary<Guid, float> _msgContentH = new();
    private readonly Dictionary<Guid, float> _msgRowH = new();

    /// <summary>Live-arrival entrance progress (0→1) per message id; present only while a freshly received
    /// message is sliding and fading into place.</summary>
    private readonly Dictionary<Guid, float> _entryAnim = new();
    private const float EntranceDuration = 0.3f;
    private float _msgCacheWidth = -1f;
    private float _msgCacheLineH = -1f;
    private Guid _peerId;
    private string _peerName = string.Empty;
    private byte[] _peerAvatar = [];
    private byte[]? _peerPublicKey;
    private byte[]? _messageKey;
    private string _inputText = string.Empty;

    /// <summary>Per-peer unsent input, so a typed draft survives leaving and returning to a conversation
    /// (including the offline round-trip during maintenance). In-memory for the session only.</summary>
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
    private double _loadStartedAt;
    /// <summary>Only surface the "loading" hint once the async load has run this long, so an instant cache-backed
    /// open never flashes a spinner.</summary>
    private const double LoadIndicatorDelay = 0.25;
    private CancellationTokenSource _cts = new();

    /// <summary>Reset to -1 on show; the whole chat page eases in over <see cref="OpenFadeDuration"/> from the
    /// first drawn frame, so an instant cache-backed open fades in instead of snapping onto screen.</summary>
    private double _openFadeAt = -1;
    private const double OpenFadeDuration = 0.20;

    private float _scrollToBottom;

    /// <summary>A message a search jumped to: scrolled into view after load, then its border flashes once and
    /// fades back to normal. Empty for a normal open.</summary>
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
    private readonly ConfirmModal _modal = new();

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
        ScreenRouter router,
        ChatListScreen chatListScreen,
        ProfileScreen profileScreen,
        EncryptionVerificationScreen verifyScreen,
        AetherLoveHubClient hub,
        CryptoService crypto,
        KeyStorageService keys,
        ChatEventBus events,
        NotificationCenter notifications,
        ChatSyncService sync)
    {
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
    }

    public void OnShow()
    {
        _events.MessageReceived += OnMessageReceived;
        _events.MessageRead += OnMessageRead;
        _events.Unmatched += OnUnmatched;
        _events.BlockedByPeer += OnBlockedByPeer;
        _events.ReactionsChanged += OnReactionsChanged;
        _events.PinChanged += OnPinChanged;

        _cts.Cancel();
        _cts.Dispose();
        _cts = new CancellationTokenSource();

        _peerId = _chatListScreen.SelectedPeerId;
        _peerName = _chatListScreen.SelectedPeerName;
        _peerAvatar = _chatListScreen.SelectedPeerAvatar;
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
        // Opening a chat acknowledges the match: drop its "needs a first hello" highlight from the list.
        if (_peerId != Guid.Empty && Plugin.Configuration.OpenedChats.Add(_peerId))
        {
            Plugin.Configuration.Save();
        }
        LoadHeaderAvatar();
        StartLoadConversation();
    }

    public void OnHide()
    {
        StashDraft();
        _events.MessageReceived -= OnMessageReceived;
        _events.MessageRead -= OnMessageRead;
        _events.Unmatched -= OnUnmatched;
        _events.BlockedByPeer -= OnBlockedByPeer;
        _events.ReactionsChanged -= OnReactionsChanged;
        _events.PinChanged -= OnPinChanged;
        if (_notifications.ActiveChatPeerId == _peerId)
        {
            _notifications.ActiveChatPeerId = Guid.Empty;
        }
        SaveReactionUsageIfDirty();
        _chatListScreen.CloseCategoryEditor();
        _cts.Cancel();
    }

    /// <summary>Remembers the active peer's unsent input (or forgets it when empty) so returning to the
    /// conversation restores the draft. In-memory for the session only.</summary>
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
        var cacheDir = Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, "ChatAvatarCache");
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
            // No local E2E identity: this account's encryption was never established. The startup recovery
            // gate normally fixes this before chat is reachable; surface it here as a defensive fallback.
            _loadError = Loc.T("chat.e2e_self_broken");
            _loading = false;
            return;
        }
        // Instant render from the persisted ciphertext cache, then a cheap delta sync (no full history refetch).
        HydrateConversationFromCache();

        _loading = true;
        _loadStartedAt = ImGui.GetTime();
        _loadError = null;
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
                if (!_sync.Cache.HasConversation(_peerId))
                {
                    // Not covered by the delta yet (e.g. a brand-new match): one-shot full fetch, then cache it.
                    var dto = await _hub.GetConversationAsync(_peerId, ct).ConfigureAwait(false);
                    if (ct.IsCancellationRequested)
                    {
                        return;
                    }
                    _peerPublicKey = dto.PeerPublicKey;
                    _sync.Cache.SeedConversation(_peerId, dto.Messages);
                }
                HydrateConversationFromCache();
                if (_scrollTargetMessageId != Guid.Empty)
                {
                    _scrollToMessageTimer = 0.6f;
                    _flashTimer = AccessibilityService.ReduceMotion ? 0f : FlashDuration;
                }
                else
                {
                    _scrollToBottom = 1f;
                }
                // Reading clears unread; drop it from the global badge immediately, not just after a list refetch.
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
                Plugin.Log.Warning(ex, "[ChatScreen] conversation load failed.");
            }
            finally
            {
                _loading = false;
            }
        }, ct);
    }

    /// <summary>Rebuilds the message list from the local ciphertext cache (decrypted in memory). Message-list and
    /// entry-anim state are guarded by the message lock; enhancement reset + reseed are marshalled to the UI thread
    /// via the action queue so they never race the draw.</summary>
    private void HydrateConversationFromCache()
    {
        var pub = _sync.Cache.GetPeerPublicKey(_peerId) ?? _peerPublicKey;
        if (pub is null)
        {
            return;
        }
        _peerPublicKey = pub;
        EnsureMessageKey();
        var msgs = _sync.Cache.GetConversation(_peerId);
        lock (_messagesLock)
        {
            _messages.Clear();
            _entryAnim.Clear();
        }
        _uiActions.Enqueue(ResetEnhancements);
        DecryptAndAppend(msgs);
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
        // Salt must match on both sides; derive from the two public keys (both peers hold both),
        // not profile IDs, which a client doesn't know for itself.
        var salt = CryptoService.DeriveConversationSalt(myPub, _peerPublicKey);
        _messageKey = _crypto.DeriveMessageKey(shared, salt);
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
        if (_messageKey is null)
        {
            return null;
        }
        try
        {
            var bytes = _crypto.Decrypt(_messageKey, m.Nonce, m.Ciphertext);
            var text = Encoding.UTF8.GetString(bytes);
            return new DisplayedMessage(
                m.Id, text, m.SenderProfileId != _peerId, m.CreatedAtUtc, m.ReadByOtherAtUtc);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, $"[ChatScreen] Decrypt failed for {m.Id}.");
            return new DisplayedMessage(m.Id, Loc.T("chat.unreadable_message"), m.SenderProfileId != _peerId, m.CreatedAtUtc, m.ReadByOtherAtUtc);
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
            var bytes = _crypto.Decrypt(_messageKey, p.Nonce, p.Ciphertext);
            var text = Encoding.UTF8.GetString(bytes);
            lock (_messagesLock)
            {
                _messages.Add(new DisplayedMessage(p.MessageId, text, false, p.CreatedAtUtc, null));
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
            Plugin.Log.Warning(ex, "[ChatScreen] Live decrypt failed.");
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

        DrawOpenFade(contentTL, contentSize);

        // Hosts the category create overlay so "New category…" from the overflow menu works inside the chat.
        _chatListScreen.DrawCategoryEditorOverlay();

        if (_reportPendingOpen)
        {
            _reportPendingOpen = false;
            Widgets.ModalHost.Instance?.Open(310f, DrawReportBody);
        }
    }

    /// <summary>Eases the whole chat page in on open: covers the content with the window background and fades that
    /// cover out over <see cref="OpenFadeDuration"/>, so an instant cache-backed open fades in rather than
    /// flashing. Drawn last so it sits above the chat content; the bottom nav (drawn afterwards) stays crisp.
    /// No-op under reduce-motion.</summary>
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

    private void DrawReportSubmittedToast()
    {
        if (_reportSubmittedTimer <= 0f)
        {
            return;
        }
        var alpha = Math.Clamp(_reportSubmittedTimer / 4f, 0f, 1f);
        var col = new Vector4(0.18f, 0.62f, 0.30f, alpha);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, col);
        using (var c = ImRaii.Child("##reportToast", new Vector2(ImGui.GetContentRegionAvail().X, Px(26f)), false))
        {
            if (c.Success)
            {
                var msg = Loc.T("chat.report_submitted_toast");
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
        var iconFont = Plugin.PluginInterface.UiBuilder.FontIcon;

        var backBtnX = screenPos.X + Px(2f);
        ImGui.SetCursorScreenPos(new Vector2(backBtnX, btnTop));
        ImGui.InvisibleButton("##chatBackBtn", Px(MenuBtnW, MenuBtnW));
        var backHovered = ImGui.IsItemHovered();
        if (backHovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            ImGui.SetTooltip(Loc.T("chat.back_to_matches"));
        }
        if (ImGui.IsItemClicked())
        {
            _router.Navigate(_chatListScreen.ChatBackTarget);
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

        string shownName;
        Vector2 nameSz;
        using (UiFonts.H3?.Push())
        {
            shownName = TruncateToWidth(_peerName, maxNameW);
            nameSz = ImGui.CalcTextSize(shownName);
        }

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

        using (UiFonts.H3?.Push())
        {
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
                new Vector2(groupLeft + avatarD + gap, centerY - nameSz.Y * 0.5f), 0xFFFFFFFF, shownName);
        }

        ImGui.SetCursorScreenPos(new Vector2(groupLeft, centerY - avatarD * 0.5f));
        ImGui.InvisibleButton("##chatPeerGroup", new Vector2(groupW, avatarD));
        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            ImGui.SetTooltip(Loc.T("chat.view_profile"));
        }
        if (ImGui.IsItemClicked())
        {
            OpenPeerProfile();
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
            ImGui.Separator();
            if (DrawIconMenuItem(FontAwesomeIcon.Unlink, Loc.T("chat.menu_unmatch")))
            {
                ImGui.CloseCurrentPopup();
                _modal.Open(Loc.T("chat.unmatch_title"),
                    Loc.T("chat.unmatch_body"),
                    Loc.T("chat.unmatch_confirm"), Loc.T("chat.cancel"),
                    () => FireUnmatch());
            }
            if (DrawIconMenuItem(FontAwesomeIcon.Times, Loc.T("chat.menu_block")))
            {
                ImGui.CloseCurrentPopup();
                _modal.Open(Loc.T("chat.block_title"),
                    Loc.T("chat.block_body"),
                    Loc.T("chat.block_confirm"), Loc.T("chat.cancel"),
                    () => FireBlock());
            }
            ImGui.Separator();
            if (DrawIconMenuItem(FontAwesomeIcon.ExclamationTriangle, Loc.T("chat.menu_report_user"), 0xFF23A6F5u))
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
            catch (Exception ex) { Plugin.Log.Warning(ex, "[ChatScreen] UnmatchAsync failed."); }
        });
        _router.Navigate(_chatListScreen.ChatBackTarget);
    }

    private void FireBlock()
    {
        var peer = _peerId;
        _ = Task.Run(async () =>
        {
            try { await _hub.BlockUserAsync(peer); }
            catch (Exception ex) { Plugin.Log.Warning(ex, "[ChatScreen] BlockUserAsync failed."); }
        });
        _router.Navigate(_chatListScreen.ChatBackTarget);
    }

    private void OpenPeerProfile()
    {
        _profileScreen.SetProfile(_peerId, ProfileSource.Chat);
        _router.Navigate(Screen.Profile);
    }

    private void OpenVerify()
    {
        _verifyScreen.SetContext(_peerName, _peerPublicKey);
        _router.Navigate(Screen.EncryptionVerification);
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

        // Plaintext snapshot only included on explicit consent; chats are E2E encrypted otherwise.
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
            // Disclose the per-conversation key so the server can produce a tamper-evident transcript by
            // decrypting its own stored ciphertext (a forged key simply fails to decrypt). Used once, never stored.
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
                Plugin.Log.Warning(ex, "[ChatScreen] ReportUserAsync failed.");
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

    /// <summary>Recomputes the in-chat search hits (this conversation only) whenever the query changes.</summary>
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

    /// <summary>Jumps to a search hit; <paramref name="dir"/> is -1 for older, +1 for newer. The first
    /// navigation after a query lands on the most recent hit.</summary>
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

    /// <summary>Search bar shown between the header and the messages: a query field, a hit count, older/newer
    /// nav, and a close button. Consumes exactly <see cref="SearchBarH"/> so the message area sizing matches.</summary>
    private void DrawMessageSearchBar()
    {
        if (!_msgSearchOpen)
        {
            return;
        }
        var start = ImGui.GetCursorPos();
        var t = ThemeService.Current;
        var iconFont = Plugin.PluginInterface.UiBuilder.FontIcon;
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

            // Reset the emoji right-click capture before drawing bubbles (covers the no-bubbles frame and any
            // cross-screen leak); each bubble arms it around its own text and consumes it right after.
            SegmentEmoji.CaptureRightClick = false;
            SegmentEmoji.RightClickedName = null;

            if (!_systemNoticeDismissed && !_loading && _loadError is null
                && !messages.Any(m => m.IsOwn))
            {
                DrawSystemNotice();
            }

            // Deferred + content-aware: only hint at loading when nothing is rendered yet and the fetch is
            // actually taking a moment, so an instant cache-backed open never flashes the indicator.
            if (_loading && messages.Length == 0 && ImGui.GetTime() - _loadStartedAt > LoadIndicatorDelay)
            {
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), Loc.T("chat.loading_messages"));
            }
            if (_loadError is not null)
            {
                DrawErrorBubble(_loadError);
            }

            var lineH = ImGui.GetTextLineHeight();
            if (_msgCacheWidth != windowWidth || _msgCacheLineH != lineH)
            {
                _msgContentH.Clear();
                _msgRowH.Clear();
                _msgCacheWidth = windowWidth;
                _msgCacheLineH = lineH;
            }

            // Virtualize: only draw rows whose band intersects the viewport (plus a one-screen margin).
            // Off-screen rows still reserve their height via the cursor so the scrollbar extent — and
            // auto-scroll — stay correct.
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
                // Consecutive same-sender messages within a short window render as one tight, merged group.
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
                    ImGui.SetCursorPosY(y0 + rowH); // reserve the row's space, draw nothing
                    continue;
                }

                if (needsDivider)
                {
                    DrawDayDivider(msg.SentAt.LocalDateTime);
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

            // A manual wheel scroll takes over immediately instead of fighting the post-open auto-scroll.
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
                // Works with virtualization: the full height is reserved, so ScrollMaxY is the true bottom.
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

    /// <summary>True when <paramref name="cur"/> begins a new visual group relative to <paramref name="prev"/>.</summary>
    private static bool StartsNewGroup(DisplayedMessage cur, DisplayedMessage? prev)
        => prev is null
           || prev.IsOwn != cur.IsOwn
           || cur.SentAt.Date != prev.SentAt.Date
           || cur.SentAt - prev.SentAt > GroupWindow;

    /// <summary>Corner rounding for a bubble at a given position in its group. The side away from the
    /// sender's edge stays fully rounded; the edge side only rounds at the group's outer corners, so stacked
    /// bubbles read as one cluster.</summary>
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
        var parsed = ParsedMessage.Parse(msg.Text);
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

        // Height from the real wrapped segment layout (not a PlainText estimate), so it's correct for
        // any text/emoji mix — e.g. a long ":shortcode:" that's wide as text but renders one square.
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
            // Border flash on a search jump (the bubble keeps its own colour): |sin| peaks FlashPulses times, each at full alpha.
            var p = 1f - _flashTimer / FlashDuration;
            var a = MathF.Abs(MathF.Sin(p * FlashPulses * MathF.PI));
            drawList.AddRect(bubbleTL, bubbleTL + new Vector2(maxBubW, bubbleH),
                ImGui.GetColorU32(ThemeService.Current.AccentDark with { W = a }), Px(10f), corners, Px(4f));
        }

        // Render inside a child sized to the measured interior so the inline word/emoji wrapper (which
        // reads GetContentRegionAvail) and ImGui's wrap agree on the bubble boundary, and overflow clips.
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

        // A right-click that landed on an emoji favorites it and suppresses the bubble menu; anywhere else on
        // the bubble opens the normal message menu.
        var emojiClicked = SegmentEmoji.RightClickedName;
        if (emojiClicked != null)
        {
            SegmentEmoji.RightClickedName = null;
            _chatFavName = emojiClicked;
            ImGui.OpenPopup("##chatEmojiFavMenu");
        }
        else if (ImGui.BeginPopupContextItem($"##msgCtx{msg.Id}", ImGuiPopupFlags.MouseButtonRight))
        {
            DrawMessageContextMenu(msg);
            ImGui.EndPopup();
        }

        var reactionsH = DrawReactions(msg, bubbleLeft, bubbleTL.Y + bubbleH, maxBubW);

        if (isGroupEnd)
        {
            // One timestamp (+ "seen") per group, under the last bubble.
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
            // Tight gap to the next bubble in the same group; no timestamp.
            ImGui.SetCursorScreenPos(cursorPos + new Vector2(0, quoteH + bubbleH + reactionsH + Px(2f)));
        }

        if (fading)
        {
            ImGui.PopStyleVar();
        }
    }

    /// <summary>Slide-up + fade-in for a just-arrived message. Returns the vertical draw offset and alpha for
    /// this frame; (0, 1) once the entrance finishes or for messages that were already present.</summary>
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

    /// <summary>Height estimate for an undrawn row (to reserve off-screen space), replaced by the exact
    /// advance once drawn. Mirrors DrawMessageBubble plus an approximate day divider when present.</summary>
    private float EstimateRowHeight(DisplayedMessage msg, bool needsDivider, bool isGroupEnd, float windowWidth)
    {
        var padding = Px(12, 8);
        var innerW = windowWidth * 0.72f - padding.X * 2f;
        var lineH = ImGui.GetTextLineHeight();

        var contentH = _msgContentH.TryGetValue(msg.Id, out var cached)
            ? cached
            : ParsedMessage.Parse(msg.Text).MeasureHeight(innerW);

        var bubbleH = MathF.Max(contentH, lineH) + padding.Y * 2f;
        // Group-end rows carry the timestamp line + a full gap; group-internal rows use a tight gap.
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

    /// <summary>Day-divider label in the selected plugin language. English keeps the ordinal style
    /// ("Friday, 19th June 2026"); every other language uses its own long-date pattern, so the English
    /// "th" suffix never leaks into e.g. French ("vendredi 19 juin 2026").</summary>
    private static string BuildDayLabel(DateTime date)
    {
        if (string.Equals(LanguageProvider.Current.LanguageName, "English", StringComparison.Ordinal))
        {
            var culture = LanguageProvider.CurrentCulture;
            return $"{date.ToString("dddd", culture)}, {Ordinal(date.Day)} {date.ToString("MMMM yyyy", culture)}";
        }
        return LanguageProvider.FormatDate(date, "D");
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

    // Space reserved for the input bar: separator + spacing + the frame-height input row. Derived from
    // the live frame height so the row stays fully visible at every UI scale and font size.
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

    /// <summary>Chat input box height, grown per wrapped line up to a cap so the bar rises with the text.</summary>
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

    /// <summary>Refreshes the emoji autocomplete matches from the in-progress ":query" at the input; cached
    /// until the query changes. Prefix matches rank ahead of substring matches, shorter names first.</summary>
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
        foreach (var name in Plugin.EmojiService.All.Keys)
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

    /// <summary>Detects an in-progress ":query" at the input: a colon starting the text or following
    /// whitespace, then one or more shortcode characters with no closing colon.</summary>
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

    /// <summary>Replaces the in-progress ":query" with the picked emoji's full ":name: " and re-wraps.</summary>
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

    /// <summary>Row of up to five emoji suggestions above the input; clicking one completes the shortcode.</summary>
    private void DrawEmojiAutocompleteRow()
    {
        if (_acMatches is not { Count: > 0 } matches)
        {
            return;
        }
        // Consume exactly the height InputBarHeight reserved, so the input bar stays put whether or not
        // suggestions show (only the message area above makes room).
        var start = ImGui.GetCursorPos();
        var sz = Px(24f);
        ImGui.SetCursorPos(new Vector2(Px(8f), start.Y + Px(3f)));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Px(3f, 3f));
        ImGui.PushStyleColor(ImGuiCol.Button, 0u);
        for (var i = 0; i < matches.Count; i++)
        {
            var name = matches[i];
            var tex = Plugin.EmojiService.GetEmoji(name)?.GetWrapOrDefault();
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
        var inputWidth = windowWidth - Px(EmojiBtn) - Px(SendBtn) - Px(Gap * 3f);

        ImGui.SetCursorPosY(ImGui.GetWindowSize().Y - InputBarHeight());
        DrawEmojiAutocompleteRow();
        ImGui.Separator();
        ImGui.Spacing();

        DrawReplyComposeBar();

        {
            var frameH = ImGui.GetFrameHeight();
            var grinTex = Plugin.EmojiService.GetEmoji("grinning")?.GetWrapOrDefault();
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
        // EnterReturnsTrue deactivates the input after sending; re-grab focus next frame so the user
        // can send back-to-back without clicking back into the box.
        if (_reclaimInputFocus)
        {
            ImGui.SetKeyboardFocusHere();
            _reclaimInputFocus = false;
        }
        _chatWrapWidth = inputWidth - ImGui.GetStyle().FramePadding.X * 2f;
        var enterPressed = ImGui.InputTextMultiline("##messageInput", ref _inputText, 500,
            new Vector2(inputWidth, ChatInputBoxHeight()),
            ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.CtrlEnterForNewLine
            | ImGuiInputTextFlags.CallbackEdit | ImGuiInputTextFlags.CallbackAlways,
            ChatWrapCallback);
        ImGui.SameLine(0, Px(Gap));
        if ((ImGui.Button(Loc.T("chat.send"), new Vector2(Px(SendBtn), 0)) || enterPressed) && _inputText.Length > 0)
        {
            SendMessage();
            _reclaimInputFocus = true;
        }
    }

    /// <summary>Reflows the chat input on each edit so long text wraps to the box width. Swapping only spaces
    /// and newlines keeps the length (and cursor) stable; the breaks become spaces again on send.</summary>
    private unsafe int ChatWrapCallback(ImGuiInputTextCallbackDataPtr data)
    {
        try
        {
            ImGuiInputTextCallbackData* p = data;
            if (p->EventFlag == ImGuiInputTextFlags.CallbackAlways)
            {
                // After an autocomplete insert we re-focus the input, which parks the cursor at the start;
                // move it to the end so the user keeps typing after the shortcode.
                if (_acCursorToEnd)
                {
                    _acCursorToEnd = false;
                    p->CursorPos = p->BufTextLen;
                    p->SelectionStart = p->BufTextLen;
                    p->SelectionEnd = p->BufTextLen;
                }
                return 0;
            }
            if (_chatWrapWidth <= 0f || p->BufTextLen <= 0)
            {
                return 0;
            }
            var current = Encoding.UTF8.GetString(p->Buf, p->BufTextLen);
            var wrapped = WrapForInput(current, _chatWrapWidth);
            if (wrapped == current)
            {
                return 0;
            }
            var bytes = Encoding.UTF8.GetBytes(wrapped);
            if (bytes.Length != p->BufTextLen)
            {
                return 0;
            }
            for (var i = 0; i < bytes.Length; i++)
            {
                p->Buf[i] = bytes[i];
            }
            p->BufDirty = 1;
        }
        catch
        {
            // A managed exception must not cross into the native ImGui call.
        }
        return 0;
    }

    /// <summary>Greedy word-wrap by swapping spaces and newlines only (length-preserving): flattens stale
    /// breaks, then breaks the last space on any line wider than <paramref name="width"/>. Long words aren't split.</summary>
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
        // Only unknown :shortcodes: / whitespace render as nothing — don't send an empty bubble.
        if (!ParsedMessage.Parse(_inputText).HasVisibleContent)
        {
            return;
        }
        if (_messageKey is null)
        {
            Plugin.Log.Warning("[ChatScreen] Cannot send: message key not derived yet.");
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
        // Don't send a not-yet-acknowledged target id to the server (it wouldn't resolve for the peer); the
        // local quote still shows for this user, and DrainIdMigrations rewrites the value once it lands.
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
                Plugin.Log.Warning(ex, "[ChatScreen] SendMessageAsync failed.");
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

    /// <summary>A wrapped, rounded red notice box for a load error (peer has no E2E, own E2E missing, server
    /// unreachable), so the message reads cleanly instead of overflowing the phone width as one clipped line.</summary>
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

    internal static bool DrawIconMenuItem(FontAwesomeIcon icon, string label,
                                         uint textColor = 0xFFEEEEEE, bool enabled = true)
    {
        var dl = ImGui.GetWindowDrawList();
        var itemH = ImGui.GetFrameHeight();
        var cursor = ImGui.GetCursorScreenPos();
        var fontSize = ImGui.GetFontSize();
        const float ItemW = 200f;
        const float IconOffX = 10f;
        const float IconAreaW = 20f;
        ImGui.InvisibleButton($"##mi_{label}", new Vector2(Px(ItemW), itemH));
        var clicked = enabled && ImGui.IsItemClicked();
        if (enabled && ImGui.IsItemHovered())
        {
            dl.AddRectFilled(cursor, cursor + new Vector2(Px(ItemW), itemH), 0x30FFFFFF);
        }
        var drawColor = enabled ? textColor : 0x66AAAAAAu;
        ImGui.PushFont(Plugin.PluginInterface.UiBuilder.FontIcon);
        var iconStr = icon.ToIconString();
        var iconSz = ImGui.CalcTextSize(iconStr);
        dl.AddText(ImGui.GetFont(), fontSize,
            new Vector2(cursor.X + Px(IconOffX) + (Px(IconAreaW) - iconSz.X) * 0.5f,
                        cursor.Y + (itemH - iconSz.Y) * 0.5f),
            drawColor, iconStr);
        ImGui.PopFont();
        var labelSz = ImGui.CalcTextSize(label);
        dl.AddText(
            new Vector2(cursor.X + Px(IconOffX) + Px(IconAreaW) + Px(8f),
                        cursor.Y + (itemH - labelSz.Y) * 0.5f),
            drawColor, label);
        return clicked;
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
        ImGui.InputTextMultiline("##reportReason", ref _reportReason, 500, new Vector2(availW, Px(80f)));
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
