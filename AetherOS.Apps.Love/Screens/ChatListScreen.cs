using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Services;
using AetherLove.Services.Crypto;
using AetherLove.Services.Chat;
using AetherLove.Services.Hub;
using AetherLove.Services.Localization;
using AetherLove.Shared.Matching;
using AetherLove.Shared.Messaging;
using AetherLove.Shared.Profile.Enums;
using AetherLove.UI;
using AetherLove.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;

namespace AetherLove.Screens;

/// <summary>List of active conversations, grouped by user-created categories.</summary>
public partial class ChatListScreen
{
    private readonly LoveRouter _router;
    private readonly AetherHubContext _hub;
    private readonly ChatEventBus _events;
    private readonly CryptoService _crypto;
    private readonly KeyStorageService _keys;
    private readonly NotificationCenter _notifications;
    private readonly ChatCategoryStore _categories;
    private readonly ChatSyncService _sync;

    private readonly PeerActionConfirm _peerConfirm = new();

    private readonly List<MatchSummaryDto> _matches = new();
    // Guards _matches: mutated off the UI thread (fetch Task, push handlers) while Draw() enumerates it.
    private readonly object _matchesLock = new();
    private readonly Dictionary<Guid, ISharedImmediateTexture?> _avatarTexCache = new();
    // Decrypted last-message previews; derived keys cached so re-renders don't re-do ECDH.
    private readonly ConcurrentDictionary<Guid, string> _previewByPeer = new();
    // Derived conversation key per peer, tagged with the peer public key it was derived from so a peer's key
    // reset (their public key changes) invalidates it instead of decrypting new messages with the stale key.
    private readonly ConcurrentDictionary<Guid, (byte[] Key, byte[] Pub)> _keyByPeer = new();
    private volatile bool _fetching;
    private volatile string? _fetchError;
    private volatile bool _connectivityError;
    private CancellationTokenSource _cts = new();

    private Guid _selectedPeerId;
    private string _selectedPeerName = string.Empty;
    private byte[] _selectedPeerAvatar = [];
    private bool _selectedPeerIsSupporter;
    private NameStyle _selectedPeerNameStyle;
    private bool _selectedPeerHolidayMode;
    private string? _selectedPeerFrameRef;
    private Guid _selectedScrollMessageId;

    /// <summary>The category the category view is showing; kept across chat round-trips so back returns here.</summary>
    private Guid _openCategoryId;
    private bool _openedChatFromCategory;

    /// <summary>Which view a row is rendered in; drives its context menu and animations.</summary>
    private enum RowContext
    {
        TopLevel,
        Category,
        Search,
    }

    /// <summary>Whether the name matched, and the id of the first message whose text matched
    /// (Empty if only the name matched).</summary>
    private sealed record SearchHit(bool NameMatch, Guid ContentMessageId);

    private string _searchQuery = string.Empty;
    private volatile bool _searching;
    private volatile bool _searchActive;
    private volatile string _appliedQuery = string.Empty;
    private volatile Dictionary<Guid, SearchHit> _searchHits = new();
    private CancellationTokenSource _searchCts = new();

    /// <summary>Fixed height of one match row; shared by the list's virtualization and the row draw.</summary>
    private const float MatchRowHeight = 80f;

    public ChatListScreen(
        LoveRouter router,
        AetherHubContext hub,
        ChatEventBus events,
        CryptoService crypto,
        KeyStorageService keys,
        NotificationCenter notifications,
        ChatCategoryStore categories,
        ChatSyncService sync)
    {
        _router = router;
        _hub = hub;
        _events = events;
        _crypto = crypto;
        _keys = keys;
        _notifications = notifications;
        _categories = categories;
        _sync = sync;
    }

    public Guid SelectedPeerId => _selectedPeerId;
    public string SelectedPeerName => _selectedPeerName;
    public byte[] SelectedPeerAvatar => _selectedPeerAvatar;
    public bool SelectedPeerIsSupporter => _selectedPeerIsSupporter;
    public NameStyle SelectedPeerNameStyle => _selectedPeerNameStyle;
    public bool SelectedPeerHolidayMode => _selectedPeerHolidayMode;
    public string? SelectedPeerFrameRef => _selectedPeerFrameRef;

    /// <summary>Message to scroll to when a search result is opened by content; Empty for a normal open.</summary>
    public Guid SelectedScrollMessageId => _selectedScrollMessageId;

    private readonly EntranceAnimation _entrance = new();

    public void OnShow()
    {
        _events.Unmatched += OnUnmatched;
        _events.BlockedByPeer += OnBlockedByPeer;
        _events.MessageReceived += OnMessageReceived;
        _events.MatchCreated += OnMatchCreated;
        _events.PeerKeysReset += OnPeerKeysReset;
        _notifications.ProfileCachesInvalidated += OnProfileCachesInvalidated;
        ClearSearch();
        _entrance.Arm();
        StartFetch();
    }

    public void OnHide()
    {
        _events.Unmatched -= OnUnmatched;
        _events.BlockedByPeer -= OnBlockedByPeer;
        _events.MessageReceived -= OnMessageReceived;
        _events.MatchCreated -= OnMatchCreated;
        _events.PeerKeysReset -= OnPeerKeysReset;
        _notifications.ProfileCachesInvalidated -= OnProfileCachesInvalidated;
        _cts.Cancel();
        _cts.Dispose();
        _cts = new CancellationTokenSource();
        // The applied filter is kept so returning from a chat keeps it.
        _searchCts.Cancel();
        FinalizeCategoryAnimations();
    }

    /// <summary>A profile switch swapped the chat cache under us. Drop the previous profile's rows and derived
    /// state so the list can never show a sibling's matches, then re-fetch against the new cache owner.</summary>
    private void OnProfileCachesInvalidated()
    {
        _previewByPeer.Clear();
        _keyByPeer.Clear();
        lock (_matchesLock)
        {
            _matches.Clear();
            _avatarTexCache.Clear();
        }
        StartFetch();
    }

    private void StartFetch()
    {
        HydrateMatchesFromCache();
        if (_fetching)
        {
            return;
        }
        _fetching = true;
        _fetchError = null;
        _connectivityError = false;
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
                HydrateMatchesFromCache();
                if (_sync.Cache.GetMatches().Count == 0)
                {
                    if (_hub.IsConnected)
                    {
                        // Delta yielded nothing (transient error / partial rollout); fall back to the direct list.
                        var dto = await _hub.GetMyMatchesAsync(ct).ConfigureAwait(false);
                        if (ct.IsCancellationRequested)
                        {
                            return;
                        }
                        lock (_matchesLock)
                        {
                            _matches.Clear();
                            _matches.AddRange(dto.Matches);
                            SortMatches();
                        }
                        CacheAvatars(dto.Matches);
                        BuildPreviews(dto.Matches);
                        _notifications.UnreadChatMessages = dto.Matches.Sum(m => m.UnreadCount);
                    }
                    else
                    {
                        _connectivityError = true;
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (ct.IsCancellationRequested)
                {
                    return;
                }
                if (!_hub.IsConnected)
                {
                    _connectivityError = true;
                }
                else
                {
                    _fetchError = HubErrorText.Localize(ex);
                }
                UiHost.Log.Warning(ex, "[ChatListScreen] chat sync failed.");
            }
            finally
            {
                _fetching = false;
            }
        });
    }

    /// <summary>The cache owner whose derived conversation keys / previews are currently held; a change means we
    /// switched profile and must drop them (they were derived with the previous profile's private key).</summary>
    private Guid _previewOwner;

    private void HydrateMatchesFromCache()
    {
        var owner = _sync.Cache.Owner;
        if (owner != _previewOwner)
        {
            // Profile switched: the cached ECDH-derived conversation keys and decrypted previews belong to the
            // previous profile and would decrypt this profile's messages with the wrong key (peers can overlap
            // across profiles). Drop them so previews re-derive with the now-active profile's key.
            _previewOwner = owner;
            _keyByPeer.Clear();
            _previewByPeer.Clear();
        }
        var cached = _sync.Cache.GetMatches();
        lock (_matchesLock)
        {
            _matches.Clear();
            _matches.AddRange(cached);
            SortMatches();
        }
        CacheAvatars(cached);
        BuildPreviews(cached);
        _categories.PruneTo(cached.Select(m => m.PeerProfileId).ToHashSet());
        _notifications.UnreadChatMessages = cached.Sum(m => m.UnreadCount);
    }

    private void CacheAvatars(IEnumerable<MatchSummaryDto> matches)
    {
        var cacheDir = Path.Combine(UiHost.PluginInterface.ConfigDirectory.FullName, "ChatAvatarCache");
        foreach (var m in matches)
        {
            _avatarTexCache[m.PeerProfileId] = AvatarDiskCache.Store(cacheDir, m.PeerProfileId.ToString(), m.PeerAvatarWebp);
        }
    }

    private void OnUnmatched(UnmatchedPushDto p) => RemovePeer(p.OtherProfileId);
    private void OnBlockedByPeer(BlockedByPeerPushDto p) => RemovePeer(p.OtherProfileId);

    private void OnMatchCreated(MatchCreatedPushDto _) => StartFetch();

    /// <summary>A peer reset their E2E keys: drop that peer's cached derived key + preview and re-fetch, so the
    /// list re-decrypts the last message with the peer's new key (pulled by the sync) instead of showing blank.</summary>
    private void OnPeerKeysReset(PeerKeysResetPushDto p)
    {
        _keyByPeer.TryRemove(p.PeerProfileId, out _);
        _previewByPeer.TryRemove(p.PeerProfileId, out _);
        StartFetch();
    }

    private void RemovePeer(Guid peerId)
    {
        lock (_matchesLock)
        {
            _matches.RemoveAll(m => m.PeerProfileId == peerId);
        }
        _categories.RemovePeer(peerId);
        _sync.Cache.RemovePeer(peerId);
    }

    /// <summary>Fires the confirmed action, then optimistically drops the row; the server also pushes the removal.</summary>
    private void ConfirmPeerAction(PeerAction action, Guid peerId)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                if (action == PeerAction.Unmatch)
                {
                    await _hub.UnmatchAsync(peerId).ConfigureAwait(false);
                }
                else
                {
                    await _hub.BlockUserAsync(peerId).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, "[ChatListScreen] Peer action failed.");
            }
        });
        RemovePeer(peerId);
    }

    /// <summary>Pinned first, then most-recent activity within each group. Call under <see cref="_matchesLock"/>.</summary>
    private void SortMatches()
    {
        _matches.Sort((x, y) =>
        {
            if (x.IsPinned != y.IsPinned)
            {
                return x.IsPinned ? -1 : 1;
            }
            var xa = x.LastMessageAtUtc ?? x.MatchedAtUtc;
            var ya = y.LastMessageAtUtc ?? y.MatchedAtUtc;
            return ya.CompareTo(xa);
        });
    }

    /// <summary>Current pin state for a peer's match (false if not in the list yet).</summary>
    public bool IsPinned(Guid peerId)
    {
        lock (_matchesLock)
        {
            var idx = _matches.FindIndex(m => m.PeerProfileId == peerId);
            return idx >= 0 && _matches[idx].IsPinned;
        }
    }

    /// <summary>Optimistic pin/unpin, persisted to the server; reverted on failure.</summary>
    public void SetPinned(Guid peerId, bool pinned)
    {
        lock (_matchesLock)
        {
            var idx = _matches.FindIndex(m => m.PeerProfileId == peerId);
            if (idx < 0 || _matches[idx].IsPinned == pinned)
            {
                return;
            }
            _matches[idx] = _matches[idx] with { IsPinned = pinned };
            SortMatches();
        }

        var ct = _cts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await _hub.SetMatchPinnedAsync(peerId, pinned, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, "[ChatListScreen] SetMatchPinnedAsync failed; reverting.");
                lock (_matchesLock)
                {
                    var i = _matches.FindIndex(m => m.PeerProfileId == peerId);
                    if (i >= 0)
                    {
                        _matches[i] = _matches[i] with { IsPinned = !pinned };
                        SortMatches();
                    }
                }
            }
        });
    }

    /// <summary>Selects a peer for the chat screen without going through the list UI.</summary>
    public void SelectPeer(MatchSummaryDto m)
    {
        _selectedPeerId = m.PeerProfileId;
        _selectedPeerName = m.PeerDisplayName;
        _selectedPeerAvatar = m.PeerAvatarWebp;
        _selectedPeerIsSupporter = m.PeerIsSupporter;
        _selectedPeerNameStyle = m.PeerNameStyle;
        _selectedPeerHolidayMode = m.PeerHolidayMode;
        _selectedPeerFrameRef = m.PeerFrameRef;
        _selectedScrollMessageId = Guid.Empty;
        _openedChatFromCategory = false;
    }

    /// <summary>The category the chat was opened from if it still exists, otherwise the matches overview.</summary>
    public LoveView ChatBackTarget =>
        _openedChatFromCategory && _categories.Get(_openCategoryId) is not null
            ? LoveView.ChatCategory
            : LoveView.ChatList;

    /// <summary>The global unread badge is owned by the signal handler, not touched here.</summary>
    private void OnMessageReceived(MessageReceivedPushDto p)
    {
        MatchSummaryDto updated;
        lock (_matchesLock)
        {
            var idx = _matches.FindIndex(m => m.PeerProfileId == p.FromProfileId);
            if (idx < 0)
            {
                return;
            }
            var existing = _matches[idx];
            updated = existing with
            {
                UnreadCount = existing.UnreadCount + 1,
                LastMessageAtUtc = p.CreatedAtUtc,
                LastMessageCiphertext = p.Ciphertext,
                LastMessageNonce = p.Nonce,
                LastMessageFromMe = false,
            };
            _matches[idx] = updated;
            SortMatches();
        }

        var preview = DecryptPreview(updated);
        if (preview != null)
        {
            _previewByPeer[updated.PeerProfileId] = preview;
        }
    }

    private void BuildPreviews(IEnumerable<MatchSummaryDto> matches)
    {
        _previewByPeer.Clear();
        foreach (var m in matches)
        {
            var preview = DecryptPreview(m);
            if (preview != null)
            {
                _previewByPeer[m.PeerProfileId] = preview;
            }
        }
    }

    /// <summary>Decrypts the last-message preview. Null when there's no message or the key is unavailable.</summary>
    private string? DecryptPreview(MatchSummaryDto m)
    {
        if (m.LastMessageCiphertext.Length == 0)
        {
            // A picture with no caption is a message with no ciphertext, not an unreadable one.
            return m.LastMessageAtUtc is null ? null : Loc.T("chat.preview_image");
        }
        var key = KeyForPeer(m);
        if (key is null)
        {
            return null;
        }
        try
        {
            var bytes = _crypto.Decrypt(key, m.LastMessageNonce, m.LastMessageCiphertext);
            var text = Encoding.UTF8.GetString(bytes).Replace('\n', ' ').Replace('\r', ' ').Trim();
            if (VenueShare.TryParse(text, out _))
            {
                text = Loc.T("chat.preview_venue");
            }
            else if (HangoutShare.TryParse(text, out _))
            {
                text = Loc.T("chat.preview_hangout");
            }
            else if (NewsShare.TryParse(text, out _))
            {
                text = Loc.T("chat.preview_news");
            }
            else if (MessengerShare.TryParse(text, out _))
            {
                text = Loc.T("chat.preview_messenger");
            }
            else if (CalendarEventShare.TryParse(text, out _))
            {
                text = Loc.T("chat.preview_calevent");
            }
            else if (LevemeteShare.TryParse(text, out _))
            {
                text = Loc.T("chat.preview_levemete");
            }
            else if (MarketShare.TryParse(text, out _))
            {
                text = Loc.T("chat.preview_market");
            }
            var full = (m.LastMessageFromMe ? Loc.T("chat.preview_me_prefix") : string.Empty) + text;
            return full.Length > 42 ? full[..41] + "…" : full;
        }
        catch
        {
            return null;
        }
    }

    private byte[]? KeyForPeer(MatchSummaryDto m)
    {
        if (m.PeerPublicKey.Length == 0)
        {
            return null;
        }
        if (_keyByPeer.TryGetValue(m.PeerProfileId, out var cached) && cached.Pub.AsSpan().SequenceEqual(m.PeerPublicKey))
        {
            return cached.Key.Length == 0 ? null : cached.Key;
        }
        var myPriv = _keys.GetPrivateKey();
        var myPub = _keys.GetPublicKey();
        if (myPriv is null || myPub is null)
        {
            _keyByPeer[m.PeerProfileId] = ([], m.PeerPublicKey);
            return null;
        }
        try
        {
            var shared = _crypto.DeriveSharedSecret(myPriv, m.PeerPublicKey);
            var salt = CryptoService.DeriveConversationSalt(myPub, m.PeerPublicKey);
            var key = _crypto.DeriveMessageKey(shared, salt);
            _keyByPeer[m.PeerProfileId] = (key, m.PeerPublicKey);
            return key;
        }
        catch
        {
            _keyByPeer[m.PeerProfileId] = ([], m.PeerPublicKey);
            return null;
        }
    }

    public void Draw()
    {
        var winPos = ImGui.GetWindowPos();
        var winSize = ImGui.GetWindowSize();
        UpdateCategoryAnimations(ImGui.GetIO().DeltaTime);
        DrawMatchesHeader();
        DrawTopLevelList();
        ResolveDragAndDrop();
        DrawDragOverlays(winPos, winSize);
        DrawCategoryEditor(winPos, winSize);
        DrawCategoryDeleteConfirm(winPos, winSize);
        _peerConfirm.Draw(winPos, winSize, ConfirmPeerAction);
    }

    public void DrawCategoryView()
    {
        var cat = _categories.Get(_openCategoryId);
        if (cat is null)
        {
            _router.Navigate(LoveView.ChatList);
            return;
        }
        var winPos = ImGui.GetWindowPos();
        var winSize = ImGui.GetWindowSize();
        var contentTL = ImGui.GetCursorScreenPos();
        var contentSize = ImGui.GetContentRegionAvail();
        UpdateCategoryAnimations(ImGui.GetIO().DeltaTime);
        ImGui.Spacing();
        if (DrawFloatingBackPill(ImGui.GetCursorScreenPos(), Loc.T("chat.back_to_matches"), FontAwesomeIcon.Comment))
        {
            _router.Navigate(LoveView.ChatList);
        }
        DrawSubpageHeading(cat.Name, 16f);
        DrawCategoryChatList(cat);
        DrawCategoryOpenFade(contentTL, contentSize);
        DrawCategoryEditor(winPos, winSize);
        DrawCategoryDeleteConfirm(winPos, winSize);
        _peerConfirm.Draw(winPos, winSize, ConfirmPeerAction);
    }

    private void DrawMatchesHeader()
    {
        var winW = ImGui.GetWindowSize().X;
        var t = ThemeService.Current;
        var headerTop = ImGui.GetCursorPosY();

        var title = Loc.T("chat.matches_title");
        ImGui.SetCursorPosX((winW - ImGui.CalcTextSize(title).X) * 0.5f);
        ImGui.Text(title);
        var afterTitleY = ImGui.GetCursorPosY();

        var btnW = Px(26f);
        var btnH = ImGui.GetTextLineHeight() + Px(4f);
        ImGui.SetCursorPos(new Vector2(winW - btnW - Px(10f), headerTop - Px(2f)));
        ImGui.InvisibleButton("##matchesMenuBtn", new Vector2(btnW, btnH));
        var menuHovered = ImGui.IsItemHovered();
        if (menuHovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }
        var menuClicked = ImGui.IsItemClicked();
        var rectMin = ImGui.GetItemRectMin();
        ImGui.PushFont(UiHost.PluginInterface.UiBuilder.FontIcon);
        var icon = FontAwesomeIcon.EllipsisV.ToIconString();
        var iconSz = ImGui.CalcTextSize(icon);
        ImGui.GetWindowDrawList().AddText(ImGui.GetFont(), ImGui.GetFontSize(),
            rectMin + new Vector2((btnW - iconSz.X) * 0.5f, (btnH - iconSz.Y) * 0.5f),
            menuHovered ? t.AccentLightU32 : t.AccentU32, icon);
        ImGui.PopFont();

        if (menuClicked)
        {
            ImGui.OpenPopup("##matchesMenu");
        }
        if (ImGui.BeginPopup("##matchesMenu"))
        {
            if (DrawIconMenuItem(FontAwesomeIcon.FolderPlus, Loc.T("chat.category_new")))
            {
                ImGui.CloseCurrentPopup();
                OpenCategoryEditor(null, Guid.Empty);
            }
            var showing = UiHost.Configuration.ShowChatSearch;
            if (DrawIconMenuItem(showing ? FontAwesomeIcon.SearchMinus : FontAwesomeIcon.Search,
                    showing ? Loc.T("chat.hide_search") : Loc.T("chat.show_search")))
            {
                ImGui.CloseCurrentPopup();
                ToggleSearchVisible();
            }
            if (DrawIconMenuItem(FontAwesomeIcon.Ban, Loc.T("chat.menu_view_blocked")))
            {
                ImGui.CloseCurrentPopup();
                _router.Navigate(LoveView.Blocked);
            }
            ImGui.EndPopup();
        }

        ImGui.SetCursorPosY(afterTitleY);
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (UiHost.Configuration.ShowChatSearch)
        {
            DrawSearchRow();
            ImGui.Separator();
            ImGui.Spacing();
        }
    }

    private void DrawSearchRow()
    {
        var t = ThemeService.Current;

        var btnW = Px(32f);
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var avail = ImGui.GetContentRegionAvail().X;
        var inputW = MathF.Max(Px(60f), avail - (btnW * 2f + spacing * 2f));

        ImGui.SetNextItemWidth(inputW);
        var submit = ImGui.InputTextWithHint("##chatSearchInput", Loc.T("chat.search_hint"),
            ref _searchQuery, 100, ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.SameLine();

        PushThemeButton(t);
        ImGui.PushFont(UiHost.PluginInterface.UiBuilder.FontIcon);
        var search = ImGui.Button(FontAwesomeIcon.Search.ToIconString() + "##doSearch", new Vector2(btnW, 0f));
        ImGui.SameLine();
        var clear = ImGui.Button(FontAwesomeIcon.Times.ToIconString() + "##clearSearch", new Vector2(btnW, 0f));
        ImGui.PopFont();
        PopThemeButton();

        if (search || submit)
        {
            RunSearch();
        }
        if (clear)
        {
            ClearSearch();
        }

        if (_searching)
        {
            var r = Px(7f);
            var spinTL = ImGui.GetCursorScreenPos();
            ImGui.Dummy(new Vector2(r * 2f + Px(4f), r * 2f + Px(2f)));
            Widgets.LoadingSpinner.Draw(spinTL + new Vector2(r, r + Px(1f)), r, Px(2.2f), t.AccentU32);
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(UiColors.Subtle, Loc.T("chat.searching"));
        }
        ImGui.Spacing();
    }

    private void ToggleSearchVisible()
    {
        var now = !UiHost.Configuration.ShowChatSearch;
        UiHost.Configuration.ShowChatSearch = now;
        UiHost.Configuration.Save();
        if (!now)
        {
            ClearSearch();
        }
    }

    private void ClearSearch()
    {
        _searchCts.Cancel();
        _searching = false;
        _searchActive = false;
        _searchHits = new Dictionary<Guid, SearchHit>();
        _appliedQuery = string.Empty;
        _searchQuery = string.Empty;
    }

    /// <summary>Fully offline search: the server only holds ciphertext, so names and message contents are
    /// matched by decrypting the local cache in memory.</summary>
    private void RunSearch()
    {
        var query = _searchQuery.Trim();
        if (query.Length == 0)
        {
            ClearSearch();
            return;
        }

        _searchCts.Cancel();
        _searchCts.Dispose();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;
        _searching = true;

        MatchSummaryDto[] snapshot;
        lock (_matchesLock)
        {
            // Archived conversations are included in search.
            snapshot = _matches.ToArray();
        }

        _ = Task.Run(() =>
        {
            var hits = new Dictionary<Guid, SearchHit>();
            try
            {
                foreach (var m in snapshot)
                {
                    if (ct.IsCancellationRequested)
                    {
                        return;
                    }
                    var nameMatch = m.PeerDisplayName.Contains(query, StringComparison.OrdinalIgnoreCase);
                    var contentMsg = FindContentMatch(m, query);
                    if (nameMatch || contentMsg != Guid.Empty)
                    {
                        hits[m.PeerProfileId] = new SearchHit(nameMatch, contentMsg);
                    }
                }

                if (ct.IsCancellationRequested)
                {
                    return;
                }
                _searchHits = hits;
                _appliedQuery = query;
                _searchActive = true;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, "[ChatListScreen] search failed.");
            }
            finally
            {
                _searching = false;
            }
        }, ct);
    }

    /// <summary>Id of the first cached message containing <paramref name="query"/>, or Empty;
    /// decrypts locally, no server round-trip.</summary>
    private Guid FindContentMatch(MatchSummaryDto m, string query)
    {
        var key = KeyForPeer(m);
        if (key is null)
        {
            return Guid.Empty;
        }
        foreach (var em in _sync.Cache.GetConversation(m.PeerProfileId))
        {
            try
            {
                var text = Encoding.UTF8.GetString(_crypto.Decrypt(key, em.Nonce, em.Ciphertext));
                if (text.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    return em.Id;
                }
            }
            catch
            {
                // Skip an undecryptable message; keep scanning the rest.
            }
        }
        return Guid.Empty;
    }

    /// <summary>Returns null when a loading/error/empty state was rendered and the list should not draw.</summary>
    private MatchSummaryDto[]? SnapshotMatchesOrDrawState()
    {
        MatchSummaryDto[] all;
        lock (_matchesLock)
        {
            all = _matches.ToArray();
        }

        if (_fetching && all.Length == 0)
        {
            Widgets.LoadingIndicator.Draw();
            return null;
        }
        if (_connectivityError && all.Length == 0)
        {
            DrawConnectivityError();
            return null;
        }
        if (_fetchError is not null && all.Length == 0)
        {
            ImGui.PushTextWrapPos(0f);
            ImGui.TextColored(UiColors.Danger,
                Loc.T("chat.matches_load_error", _fetchError));
            ImGui.PopTextWrapPos();
            return null;
        }
        return all;
    }

    /// <summary>During an active search the categories are hidden and hits from every category are shown flat.</summary>
    private void DrawTopLevelList()
    {
        if (SnapshotMatchesOrDrawState() is not { } all)
        {
            return;
        }

        var hits = _searchHits;
        var searchOn = _searchActive;
        var cats = _categories.GetCategories();
        var membership = _categories.GetMembership();
        var catIds = cats.Select(c => c.Id).ToHashSet();

        Guid? CatOf(Guid peerId) =>
            membership.TryGetValue(peerId, out var catId) && catIds.Contains(catId) ? catId : null;

        var rows = searchOn
            ? all.Where(m => hits.ContainsKey(m.PeerProfileId)).ToArray()
            : all.Where(m => CatOf(m.PeerProfileId) is null).ToArray();

        if (searchOn && rows.Length == 0)
        {
            DrawCenteredHint(Loc.T("chat.search_no_results"));
            return;
        }
        if (!searchOn && all.Length == 0)
        {
            DrawEmptyState();
            return;
        }

        var unreadByCat = new Dictionary<Guid, int>();
        var countByCat = new Dictionary<Guid, int>();
        foreach (var m in all)
        {
            if (CatOf(m.PeerProfileId) is not { } catId)
            {
                continue;
            }
            countByCat[catId] = countByCat.GetValueOrDefault(catId) + 1;
            unreadByCat[catId] = unreadByCat.GetValueOrDefault(catId) + m.UnreadCount;
        }

        PushScrollbarStyle();
        using (var child = ImRaii.Child("MatchList", Vector2.Zero, false))
        {
            PopScrollbarStyle();
            if (!child.Success)
            {
                return;
            }

            if (!searchOn)
            {
                DrawCategoryRows(cats, countByCat, unreadByCat);
                if (cats.Count > 0 && rows.Length > 0)
                {
                    var dl = ImGui.GetWindowDrawList();
                    var p = ImGui.GetCursorScreenPos();
                    dl.AddLine(p, p + new Vector2(ImGui.GetContentRegionAvail().X, 0f), UiColors.Divider, Px(1f));
                    ImGui.Dummy(new Vector2(0f, Px(4f)));
                }
                if (rows.Length == 0 && cats.Count > 0)
                {
                    ImGui.Dummy(new Vector2(0f, Px(14f)));
                    var hint = Loc.T("chat.all_categorized");
                    var hintSz = ImGui.CalcTextSize(hint);
                    ImGui.SetCursorPosX((ImGui.GetWindowSize().X - hintSz.X) * 0.5f);
                    ImGui.TextColored(UiColors.Hint, hint);
                }
            }

            DrawChatRows(rows, searchOn ? RowContext.Search : RowContext.TopLevel, searchOn ? hits : null);
        }
    }

    private void DrawCategoryChatList(Config.ChatCategoryConfig cat)
    {
        if (SnapshotMatchesOrDrawState() is not { } all)
        {
            return;
        }

        var membership = _categories.GetMembership();
        var rows = all.Where(m =>
            membership.TryGetValue(m.PeerProfileId, out var catId) && catId == cat.Id).ToArray();

        if (rows.Length == 0)
        {
            DrawCenteredHint(Loc.T("chat.category_empty"));
            return;
        }

        PushScrollbarStyle();
        using (var child = ImRaii.Child("CategoryChatList", Vector2.Zero, false))
        {
            PopScrollbarStyle();
            if (!child.Success)
            {
                return;
            }
            DrawChatRows(rows, RowContext.Category, null);
        }
    }

    /// <summary>Virtualized rows: off-screen rows advance the cursor so scroll extent stays correct;
    /// departing rows' shrinking height participates in both branches.</summary>
    private void DrawChatRows(MatchSummaryDto[] rows, RowContext ctx, Dictionary<Guid, SearchHit>? hits)
    {
        _entrance.BeginFrame();
        var rowHeight = Px(MatchRowHeight);
        var viewH = ImGui.GetWindowSize().Y;
        var bandTop = ImGui.GetScrollY() - viewH;
        var bandBot = ImGui.GetScrollY() + viewH * 2f;

        for (int i = 0; i < rows.Length; i++)
        {
            var m = rows[i];
            _departing.TryGetValue(m.PeerProfileId, out var depart);
            var visH = depart is null ? rowHeight : rowHeight * (1f - EaseInCubic(depart.T));
            if (visH < 0.5f)
            {
                continue;
            }

            var y0 = ImGui.GetCursorPosY();
            if (y0 + visH < bandTop || y0 > bandBot)
            {
                ImGui.SetCursorPosY(y0 + visH);
                continue;
            }


            // Mark the row that sits on the pinned/unpinned boundary so it gets an accent divider.
            var isPinnedBoundary = ctx != RowContext.Search && m.IsPinned && i + 1 < rows.Length && !rows[i + 1].IsPinned;
            SearchHit? hit = null;
            hits?.TryGetValue(m.PeerProfileId, out hit);
            DrawMatchRow(m, isPinnedBoundary, ctx, hit, depart, visH);
        }
        _entrance.EndFrame();
    }

    private static void DrawCenteredHint(string message)
    {
        var winSize = ImGui.GetWindowSize();
        var winPos = ImGui.GetWindowPos();
        var wrapWidth = winSize.X - Px(24f) * 2f;
        using (UiFonts.H3?.Push())
        {
            var lines = WrapLines(message, wrapWidth);
            var lineHeight = ImGui.GetTextLineHeight();
            var top = winPos.Y + (winSize.Y - lineHeight * lines.Length) * 0.40f;
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.6f, 1f));
            for (int i = 0; i < lines.Length; i++)
            {
                var lineSz = ImGui.CalcTextSize(lines[i]);
                ImGui.SetCursorScreenPos(new Vector2(winPos.X + (winSize.X - lineSz.X) * 0.5f, top + i * lineHeight));
                ImGui.TextUnformatted(lines[i]);
            }
            ImGui.PopStyleColor();
        }
    }

    private void DrawConnectivityError()
    {
        var t = ThemeService.Current;
        var winSize = ImGui.GetWindowSize();
        var winPos = ImGui.GetWindowPos();

        var Padding = Px(24f);
        var Gap = Px(22f);
        var ButtonGap = Px(20f);
        var buttonH = Px(32f);

        var iconPx = Px(44f);
        var iconSz = IconDraw.Measure(FontAwesomeIcon.Unlink, iconPx);

        var msg = Loc.T("chat.connectivity_error");
        var wrapWidth = winSize.X - Padding * 2f;

        string[] lines;
        float lineHeight, textBlockH;
        using (UiFonts.H3?.Push())
        {
            lines = WrapLines(msg, wrapWidth);
            lineHeight = ImGui.GetTextLineHeight();
            textBlockH = lineHeight * lines.Length;
        }

        var totalH = iconSz.Y + Gap + textBlockH + ButtonGap + buttonH;
        var blockTop = winPos.Y + (winSize.Y - totalH) * 0.40f;

        var iconX = winPos.X + (winSize.X - iconSz.X) * 0.5f;
        IconDraw.Add(ImGui.GetWindowDrawList(), FontAwesomeIcon.Unlink, iconPx,
            new Vector2(iconX, blockTop), ImGui.GetColorU32(new Vector4(0.92f, 0.46f, 0.46f, 0.85f)));

        // Centre each wrapped line individually; TextWrapped left-aligns.
        var textY = blockTop + iconSz.Y + Gap;
        using (UiFonts.H3?.Push())
        {
            ImGui.PushStyleColor(ImGuiCol.Text, t.AccentLight);
            for (int i = 0; i < lines.Length; i++)
            {
                var lineSz = ImGui.CalcTextSize(lines[i]);
                ImGui.SetCursorScreenPos(new Vector2(
                    winPos.X + (winSize.X - lineSz.X) * 0.5f,
                    textY + i * lineHeight));
                ImGui.TextUnformatted(lines[i]);
            }
            ImGui.PopStyleColor();
        }

        var retryLabel = Loc.T("common.try_again");
        const string discordLabel = "Discord";
        var style = ImGui.GetStyle();
        var retryW = ImGui.CalcTextSize(retryLabel).X + style.FramePadding.X * 2f + Px(10f);
        var discordW = ImGui.CalcTextSize(discordLabel).X + style.FramePadding.X * 2f + Px(10f);
        var buttonsX = winPos.X + (winSize.X - (retryW + style.ItemSpacing.X + discordW)) * 0.5f;
        var buttonsY = textY + textBlockH + ButtonGap;

        ImGui.SetCursorScreenPos(new Vector2(buttonsX, buttonsY));
        PushThemeButton(t);
        if (ImGui.Button(retryLabel + "##connRetry", new Vector2(retryW, buttonH)))
        {
            StartFetch();
        }
        ImGui.SameLine();
        if (ImGui.Button(discordLabel + "##connDiscord", new Vector2(discordW, buttonH)))
        {
            OpenDiscord();
        }
        PopThemeButton();
    }

    private static void DrawEmptyState()
    {
        var t = ThemeService.Current;
        var winSize = ImGui.GetWindowSize();
        var winPos = ImGui.GetWindowPos();

        var Padding = Px(24f);
        var Gap = Px(22f);

        var iconPx = Px(44f);
        var iconSz = IconDraw.Measure(FontAwesomeIcon.HeartBroken, iconPx);

        var msg = Loc.T("chat.empty_state");
        var wrapWidth = winSize.X - Padding * 2f;

        string[] lines;
        float lineHeight, textBlockH;
        using (UiFonts.H3?.Push())
        {
            lines = WrapLines(msg, wrapWidth);
            lineHeight = ImGui.GetTextLineHeight();
            textBlockH = lineHeight * lines.Length;
        }

        var totalH = iconSz.Y + Gap + textBlockH;
        var blockTop = winPos.Y + (winSize.Y - totalH) * 0.42f;

        var iconX = winPos.X + (winSize.X - iconSz.X) * 0.5f;
        IconDraw.Add(ImGui.GetWindowDrawList(), FontAwesomeIcon.HeartBroken, iconPx,
            new Vector2(iconX, blockTop), ImGui.GetColorU32(new Vector4(t.AccentLight.X, t.AccentLight.Y, t.AccentLight.Z, 0.55f)));

        // Centre each wrapped line individually; TextWrapped left-aligns.
        var textY = blockTop + iconSz.Y + Gap;
        using (UiFonts.H3?.Push())
        {
            ImGui.PushStyleColor(ImGuiCol.Text, t.AccentLight);
            for (int i = 0; i < lines.Length; i++)
            {
                var lineSz = ImGui.CalcTextSize(lines[i]);
                ImGui.SetCursorScreenPos(new Vector2(
                    winPos.X + (winSize.X - lineSz.X) * 0.5f,
                    textY + i * lineHeight));
                ImGui.TextUnformatted(lines[i]);
            }
            ImGui.PopStyleColor();
        }
    }

    private static string[] WrapLines(string text, float maxWidth)
    {
        var words = text.Split(' ');
        var lines = new List<string>();
        var current = string.Empty;
        for (int i = 0; i < words.Length; i++)
        {
            var word = words[i];
            var candidate = current.Length == 0 ? word : current + " " + word;
            var size = ImGui.CalcTextSize(candidate);
            if (size.X <= maxWidth || current.Length == 0)
            {
                current = candidate;
            }
            else
            {
                lines.Add(current);
                current = word;
            }
        }
        if (current.Length > 0)
        {
            lines.Add(current);
        }
        return lines.ToArray();
    }

    private void DrawMatchRow(MatchSummaryDto m, bool isPinnedBoundary, RowContext ctx, SearchHit? hit,
                              DepartAnim? depart, float visH)
    {
        var drawList = ImGui.GetWindowDrawList();
        var cursorStart = ImGui.GetCursorScreenPos();
        var rowHeight = Px(MatchRowHeight);
        var windowWidth = ImGui.GetContentRegionAvail().X;
        var rowMax = cursorStart + new Vector2(windowWidth, visH);
        var beingDragged = _dragActive && _dragKind == DragKind.Chat && _dragPeerId == m.PeerProfileId;

        var isHovered = false;
        if (depart is null)
        {
            ImGui.InvisibleButton($"##match_{m.PeerProfileId}", new Vector2(windowWidth, visH));
            isHovered = ImGui.IsItemHovered() && !_dragActive;

            if (ImGui.IsItemActivated())
            {
                _pressPeerId = m.PeerProfileId;
            }
            if (ctx == RowContext.TopLevel && ImGui.IsItemActive() && _pressPeerId == m.PeerProfileId
                && !_dragActive && ImGui.GetMouseDragDelta(ImGuiMouseButton.Left).Length() > Px(6f))
            {
                StartChatDrag(m);
            }
            if (ImGui.IsItemDeactivated() && _pressPeerId == m.PeerProfileId)
            {
                _pressPeerId = Guid.Empty;
                if (!_dragActive && ImGui.IsItemHovered())
                {
                    _selectedPeerId = m.PeerProfileId;
                    _selectedPeerName = m.PeerDisplayName;
                    _selectedPeerAvatar = m.PeerAvatarWebp;
                    _selectedPeerIsSupporter = m.PeerIsSupporter;
                    _selectedPeerNameStyle = m.PeerNameStyle;
                    _selectedPeerHolidayMode = m.PeerHolidayMode;
                    _selectedPeerFrameRef = m.PeerFrameRef;
                    _selectedScrollMessageId = hit?.ContentMessageId ?? Guid.Empty;
                    _openedChatFromCategory = ctx == RowContext.Category;
                    _router.Navigate(LoveView.Chat);
                }
            }

            if (beingDragged)
            {
                _dragSourceRowCenter = cursorStart + new Vector2(Px(40f), visH * 0.5f);
            }

            if (ImGui.BeginPopupContextItem($"##matchctx_{m.PeerProfileId}", ImGuiPopupFlags.MouseButtonRight))
            {
                ImGui.TextDisabled(m.PeerDisplayName);
                ImGui.Separator();
                if (DrawIconMenuItem(
                        m.IsPinned ? FontAwesomeIcon.ThumbtackSlash : FontAwesomeIcon.Thumbtack,
                        m.IsPinned ? Loc.T("chat.menu_unpin") : Loc.T("chat.menu_pin")))
                {
                    ImGui.CloseCurrentPopup();
                    SetPinned(m.PeerProfileId, !m.IsPinned);
                }
                DrawCategoryMenuItems(m.PeerProfileId, ctx,
                    cursorStart + new Vector2(Px(40f), rowHeight * 0.5f));
                ImGui.Separator();
                if (DrawIconMenuItem(FontAwesomeIcon.Unlink, Loc.T("chat.menu_unmatch")))
                {
                    ImGui.CloseCurrentPopup();
                    _peerConfirm.Open(PeerAction.Unmatch, m.PeerProfileId);
                }
                if (DrawIconMenuItem(FontAwesomeIcon.Ban, Loc.T("chat.menu_block")))
                {
                    ImGui.CloseCurrentPopup();
                    _peerConfirm.Open(PeerAction.Block, m.PeerProfileId);
                }
                ImGui.EndPopup();
            }
        }
        else
        {
            ImGui.Dummy(new Vector2(windowWidth, visH));
        }

        // Departing rows slide right and fade behind a shrinking clip; everything below draws through these.
        var slide = depart is null ? 0f : EaseInCubic(depart.T) * windowWidth * 0.55f;
        var contentStart = cursorStart + new Vector2(slide, 0f);
        drawList.PushClipRect(cursorStart, new Vector2(rowMax.X, MathF.Max(rowMax.Y, cursorStart.Y + 1f)), true);

        // "Needs a first hello" highlight; clears once a message is sent or the chat has been opened.
        if (m.LastMessageAtUtc is null
            && !UiHost.Configuration.OpenedChats.Contains(m.PeerProfileId))
        {
            drawList.AddRectFilled(cursorStart, rowMax, ThemeService.Current.AccentWithAlpha(0.14f));
            DrawAttentionShine(drawList, cursorStart, rowMax);
        }

        if (isHovered)
        {
            drawList.AddRectFilled(cursorStart, rowMax, 0x20FFFFFF);
        }

        var avatarCenter = contentStart + new Vector2(Px(40), rowHeight * 0.5f);
        var avatarRadius = Px(25f);

        _avatarTexCache.TryGetValue(m.PeerProfileId, out var tex);
        var avatarWrap = tex?.GetWrapOrDefault();
        if (avatarWrap != null)
        {
            var tl = avatarCenter - new Vector2(avatarRadius, avatarRadius);
            var br = avatarCenter + new Vector2(avatarRadius, avatarRadius);
            drawList.AddImageRounded(avatarWrap.Handle, tl, br,
                Vector2.Zero, Vector2.One, 0xFFFFFFFF, avatarRadius, ImDrawFlags.RoundCornersAll);
        }
        else
        {
            // Neutral fill for the brief window before the texture finishes decoding.
            drawList.AddCircleFilled(avatarCenter, avatarRadius, UiColors.AvatarFallback);
        }
        drawList.AddCircle(avatarCenter, avatarRadius, 0xFFFFFFFF, 0, Px(2f));
        AvatarRings.Draw(drawList, avatarCenter, avatarRadius, m.PeerFrameRef);

        if (m.PeerHolidayMode)
        {
            var awayR = Px(8f);
            var awayCenter = avatarCenter + new Vector2(avatarRadius - Px(6f), avatarRadius - Px(6f));
            drawList.AddCircleFilled(awayCenter, awayR, ImGui.GetColorU32(UiColors.HolidayPurple));
            drawList.AddCircle(awayCenter, awayR, 0xFFFFFFFF, 0, Px(1.5f));
            IconDraw.AddCentered(drawList, FontAwesomeIcon.Clock, awayR * 1.15f, awayCenter, 0xFFFFFFFFu);
        }

        if (m.UnreadCount > 0)
        {
            var BadgeR = Px(9f);
            var badgeCenter = avatarCenter + new Vector2(avatarRadius - Px(6f), -avatarRadius + Px(6f));
            drawList.AddCircleFilled(badgeCenter, BadgeR, UiColors.UnreadBadge);
            var label = m.UnreadCount > 9 ? "9+" : m.UnreadCount.ToString();
            var fsz = ImGui.GetFontSize() * 0.74f;
            var tsize = ImGui.CalcTextSize(label) * (fsz / ImGui.GetFontSize());
            drawList.AddText(ImGui.GetFont(), fsz, badgeCenter - tsize * 0.5f, 0xFFFFFFFF, label);
        }

        var textPos = contentStart + Px(80, 12);
        if (hit is { NameMatch: true } && _appliedQuery.Length > 0)
        {
            DrawNameHighlighted(drawList, textPos, m.PeerDisplayName, _appliedQuery);
        }
        else
        {
            drawList.AddText(textPos, SupporterStyle.NameColor(m.PeerNameStyle, 0xFFFFFFFF), m.PeerDisplayName);
        }
        var afterNameX = ImGui.CalcTextSize(m.PeerDisplayName).X + Px(6f);
        if (m.PeerIsSupporter)
        {
            var starPx = ImGui.GetFontSize() * 0.6f;
            IconDraw.Add(drawList, FontAwesomeIcon.Star, starPx,
                textPos + new Vector2(afterNameX, Px(3f)), UiColors.FavoriteStar);
            afterNameX += IconDraw.Measure(FontAwesomeIcon.Star, starPx).X + Px(4f);
        }
        if (m.IsPinned)
        {
            ImGui.PushFont(UiHost.PluginInterface.UiBuilder.FontIcon);
            drawList.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.8f,
                textPos + new Vector2(afterNameX, Px(1f)),
                ThemeService.Current.AccentU32, FontAwesomeIcon.Thumbtack.ToIconString());
            ImGui.PopFont();
        }

        var timeAgo = m.LastMessageAtUtc is not null
            ? GetTimeAgo(m.LastMessageAtUtc.Value.LocalDateTime)
            : Loc.T("chat.new_match");
        var timeSize = ImGui.CalcTextSize(timeAgo);
        drawList.AddText(
            contentStart + new Vector2(windowWidth - timeSize.X - Px(10), Px(12)),
            UiColors.TextMuted,
            timeAgo);

        _previewByPeer.TryGetValue(m.PeerProfileId, out var preview);
        var previewText = !string.IsNullOrEmpty(preview)
            ? preview
            : (m.LastMessageAtUtc is null ? Loc.T("chat.say_hi") : string.Empty);
        if (!string.IsNullOrEmpty(previewText))
        {
            var previewCol = m.UnreadCount > 0 ? 0xFFDDDDDDu : 0xFF999999u;
            drawList.AddText(contentStart + Px(80, 38), previewCol, previewText);
        }

        if (beingDragged)
        {
            drawList.AddRectFilled(cursorStart, rowMax, DragDim);
        }

        drawList.PopClipRect();

        if (isPinnedBoundary)
        {
            drawList.AddLine(
                cursorStart + new Vector2(0f, visH),
                cursorStart + new Vector2(windowWidth, visH),
                ThemeService.Current.AccentU32, Px(2f));
        }
        else
        {
            drawList.AddLine(
                cursorStart + new Vector2(Px(80), visH),
                cursorStart + new Vector2(windowWidth, visH),
                0xFF333333);
        }

        ImGui.SetCursorScreenPos(cursorStart + new Vector2(0, visH));
    }

    /// <summary>Driven by the global clock so every awaiting-reply row shines in unison; skipped under reduce-motion.</summary>
    private static void DrawAttentionShine(ImDrawListPtr dl, Vector2 min, Vector2 max)
    {
        if (AccessibilityService.ReduceMotion)
        {
            return;
        }
        const double period = 3.5; // seconds between shines
        const double sweep = 0.9;  // seconds the glint takes to cross the row
        var phase = ImGui.GetTime() % period;
        if (phase > sweep)
        {
            return;
        }

        var t = (float)(phase / sweep);
        var rowH = max.Y - min.Y;
        var bandW = rowH * 1.3f;
        var centerX = min.X - bandW + t * (max.X - min.X + bandW * 2f);

        var theme = ThemeService.Current;
        var peak = theme.AccentLightWithAlpha(0.30f);
        var edge = theme.AccentLightWithAlpha(0f);

        dl.PushClipRect(min, max, true);
        dl.AddRectFilledMultiColor(new Vector2(centerX - bandW, min.Y), new Vector2(centerX, max.Y), edge, peak, peak, edge);
        dl.AddRectFilledMultiColor(new Vector2(centerX, min.Y), new Vector2(centerX + bandW, max.Y), peak, edge, edge, peak);
        dl.PopClipRect();
    }

    private static string GetTimeAgo(DateTime time)
    {
        var diff = DateTime.Now - time;
        if (diff.TotalMinutes < 60)
        {
            return Loc.T("chat.time_ago_minutes", (int)diff.TotalMinutes);
        }
        if (diff.TotalHours < 24)
        {
            return Loc.T("chat.time_ago_hours", (int)diff.TotalHours);
        }
        return Loc.T("chat.time_ago_days", (int)diff.TotalDays);
    }

    private static void DrawNameHighlighted(ImDrawListPtr dl, Vector2 pos, string name, string query)
    {
        var idx = name.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            dl.AddText(pos, 0xFFFFFFFF, name);
            return;
        }
        var highlight = ImGui.GetColorU32(UiColors.Amber);
        var x = pos.X;
        if (idx > 0)
        {
            var before = name[..idx];
            dl.AddText(new Vector2(x, pos.Y), 0xFFFFFFFF, before);
            x += ImGui.CalcTextSize(before).X;
        }
        var match = name.Substring(idx, query.Length);
        dl.AddText(new Vector2(x, pos.Y), highlight, match);
        x += ImGui.CalcTextSize(match).X;
        var afterIdx = idx + query.Length;
        if (afterIdx < name.Length)
        {
            dl.AddText(new Vector2(x, pos.Y), 0xFFFFFFFF, name[afterIdx..]);
        }
    }

}
