using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Navigation;
using AetherLove.Services;
using AetherLove.Services.Crypto;
using AetherLove.Services.Chat;
using AetherLove.Services.Hub;
using AetherLove.Services.Localization;
using AetherLove.Shared.Matching;
using AetherLove.Shared.Messaging;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;

namespace AetherLove.Screens;

/// <summary>List of active conversations.</summary>
public class ChatListScreen
{
    private readonly ScreenRouter _router;
    private readonly AetherLoveHubClient _hub;
    private readonly ChatEventBus _events;
    private readonly CryptoService _crypto;
    private readonly KeyStorageService _keys;
    private readonly NotificationCenter _notifications;
    private readonly ChatArchiveStore _archive;
    private readonly ChatSyncService _sync;

    private readonly List<MatchSummaryDto> _matches = new();
    // Guards _matches: mutated off the UI thread (fetch Task, push handlers) while Draw() enumerates it.
    private readonly object _matchesLock = new();
    private readonly Dictionary<Guid, ISharedImmediateTexture?> _avatarTexCache = new();
    // Decrypted last-message previews, keyed by peer. Derived keys cached so re-renders don't re-do ECDH.
    private readonly ConcurrentDictionary<Guid, string> _previewByPeer = new();
    private readonly ConcurrentDictionary<Guid, byte[]> _keyByPeer = new();
    private volatile bool _fetching;
    private volatile string? _fetchError;
    private volatile bool _connectivityError;
    private CancellationTokenSource _cts = new();

    private Guid _selectedPeerId;
    private string _selectedPeerName = string.Empty;
    private byte[] _selectedPeerAvatar = [];
    private Guid _selectedScrollMessageId;

    /// <summary>One match that satisfied the current search: whether its name matched, and the id of the
    /// first message whose text matched (Empty if only the name matched).</summary>
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
        ScreenRouter router,
        AetherLoveHubClient hub,
        ChatEventBus events,
        CryptoService crypto,
        KeyStorageService keys,
        NotificationCenter notifications,
        ChatArchiveStore archive,
        ChatSyncService sync)
    {
        _router = router;
        _hub = hub;
        _events = events;
        _crypto = crypto;
        _keys = keys;
        _notifications = notifications;
        _archive = archive;
        _sync = sync;
    }

    public Guid SelectedPeerId => _selectedPeerId;
    public string SelectedPeerName => _selectedPeerName;
    public byte[] SelectedPeerAvatar => _selectedPeerAvatar;

    /// <summary>When a search result is opened by content, the message to scroll to and highlight in the chat
    /// (Empty for a normal open). Read by <see cref="ChatScreen"/> on show.</summary>
    public Guid SelectedScrollMessageId => _selectedScrollMessageId;

    public void OnShow()
    {
        _events.Unmatched += OnUnmatched;
        _events.BlockedByPeer += OnBlockedByPeer;
        _events.MessageReceived += OnMessageReceived;
        _events.MatchCreated += OnMatchCreated;
        ClearSearch();
        StartFetch();
    }

    public void OnHide()
    {
        _events.Unmatched -= OnUnmatched;
        _events.BlockedByPeer -= OnBlockedByPeer;
        _events.MessageReceived -= OnMessageReceived;
        _events.MatchCreated -= OnMatchCreated;
        _cts.Cancel();
        _cts.Dispose();
        _cts = new CancellationTokenSource();
        // Stop any in-flight content search; the applied filter is kept so returning from a chat keeps it.
        _searchCts.Cancel();
    }

    private void StartFetch()
    {
        // Instant render from the persisted cache, then a cheap delta sync (no full match-list refetch).
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
                        // Resilience: the delta yielded nothing (transient error / partial rollout); fall back to
                        // the direct list so the screen is never stuck empty.
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
                Plugin.Log.Warning(ex, "[ChatListScreen] chat sync failed.");
            }
            finally
            {
                _fetching = false;
            }
        });
    }

    private void HydrateMatchesFromCache()
    {
        var cached = _sync.Cache.GetMatches();
        lock (_matchesLock)
        {
            _matches.Clear();
            _matches.AddRange(cached);
            SortMatches();
        }
        CacheAvatars(cached);
        BuildPreviews(cached);
        // Resync the unread badge to the cached per-conversation counts.
        _notifications.UnreadChatMessages = cached.Sum(m => m.UnreadCount);
    }

    private void CacheAvatars(IEnumerable<MatchSummaryDto> matches)
    {
        var cacheDir = Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, "ChatAvatarCache");
        foreach (var m in matches)
        {
            _avatarTexCache[m.PeerProfileId] = AvatarDiskCache.Store(cacheDir, m.PeerProfileId.ToString(), m.PeerAvatarWebp);
        }
    }

    private void OnUnmatched(UnmatchedPushDto p) => RemovePeer(p.OtherProfileId);
    private void OnBlockedByPeer(BlockedByPeerPushDto p) => RemovePeer(p.OtherProfileId);

    /// <summary>New match while the list is open — re-fetch so it appears live with avatar/preview.</summary>
    private void OnMatchCreated(MatchCreatedPushDto _) => StartFetch();

    private void RemovePeer(Guid peerId)
    {
        lock (_matchesLock)
        {
            _matches.RemoveAll(m => m.PeerProfileId == peerId);
        }
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

    /// <summary>Pins/unpins a match: updates the list immediately (optimistic, re-sorted) and persists to the
    /// server. On failure the local change is reverted. Safe to call from either screen.</summary>
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
                Plugin.Log.Warning(ex, "[ChatListScreen] SetMatchPinnedAsync failed; reverting.");
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

    public bool IsArchived(Guid peerId) => _archive.IsArchived(peerId);

    public void SetArchived(Guid peerId, bool archived) => _archive.SetArchived(peerId, archived);

    /// <summary>Bumps the row's unread count + time, refreshes its preview, and floats it to the top.
    /// The global unread badge is owned by the signal handler, not touched here.</summary>
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
            return null;
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
        if (_keyByPeer.TryGetValue(m.PeerProfileId, out var cached))
        {
            return cached.Length == 0 ? null : cached;
        }
        var myPriv = _keys.GetPrivateKey();
        var myPub = _keys.GetPublicKey();
        if (myPriv is null || myPub is null)
        {
            _keyByPeer[m.PeerProfileId] = [];
            return null;
        }
        try
        {
            var shared = _crypto.DeriveSharedSecret(myPriv, m.PeerPublicKey);
            var salt = CryptoService.DeriveConversationSalt(myPub, m.PeerPublicKey);
            var key = _crypto.DeriveMessageKey(shared, salt);
            _keyByPeer[m.PeerProfileId] = key;
            return key;
        }
        catch
        {
            _keyByPeer[m.PeerProfileId] = [];
            return null;
        }
    }

    public void Draw()
    {
        DrawMatchesHeader();
        DrawList(archived: false);
    }

    public void DrawArchiveView()
    {
        DrawHeader(Loc.T("chat.archive_title"), Loc.T("chat.matches_title"), () => _router.Navigate(Screen.ChatList));
        DrawList(archived: true);
    }

    private static void DrawHeader(string title, string linkLabel, Action onLink)
    {
        var winW = ImGui.GetWindowSize().X;
        var headerTop = ImGui.GetCursorPosY();

        ImGui.SetCursorPosX((winW - ImGui.CalcTextSize(title).X) * 0.5f);
        ImGui.Text(title);
        var afterTitleY = ImGui.GetCursorPosY();

        var linkSize = ImGui.CalcTextSize(linkLabel);
        ImGui.SetCursorPos(new Vector2(winW - linkSize.X - Px(12f), headerTop));
        ImGui.InvisibleButton($"##hdrlink_{linkLabel}", linkSize);
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }
        if (ImGui.IsItemClicked())
        {
            onLink();
        }
        ImGui.GetWindowDrawList().AddText(ImGui.GetItemRectMin(),
            hovered ? ThemeService.Current.AccentLightU32 : ThemeService.Current.AccentU32, linkLabel);

        ImGui.SetCursorPosY(afterTitleY);
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
    }

    /// <summary>Matches-view header: centred title, an overflow menu (Archived + show/hide search), and the
    /// search row when enabled.</summary>
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
        ImGui.PushFont(Plugin.PluginInterface.UiBuilder.FontIcon);
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
            if (ChatScreen.DrawIconMenuItem(FontAwesomeIcon.Archive, Loc.T("chat.archive_title")))
            {
                ImGui.CloseCurrentPopup();
                _router.Navigate(Screen.ChatArchive);
            }
            var showing = Plugin.Configuration.ShowChatSearch;
            if (ChatScreen.DrawIconMenuItem(showing ? FontAwesomeIcon.SearchMinus : FontAwesomeIcon.Search,
                    showing ? Loc.T("chat.hide_search") : Loc.T("chat.show_search")))
            {
                ImGui.CloseCurrentPopup();
                ToggleSearchVisible();
            }
            ImGui.EndPopup();
        }

        ImGui.SetCursorPosY(afterTitleY);
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (Plugin.Configuration.ShowChatSearch)
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
        ImGui.PushFont(Plugin.PluginInterface.UiBuilder.FontIcon);
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
        var now = !Plugin.Configuration.ShowChatSearch;
        Plugin.Configuration.ShowChatSearch = now;
        Plugin.Configuration.Save();
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

    /// <summary>Runs the current query over names and, by decrypting each conversation from the local cache,
    /// message contents. Builds the per-peer hit map the list filters on. Fully offline: the server only ever
    /// holds ciphertext, so content search reads the cached ciphertext and decrypts in memory.</summary>
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
            snapshot = _matches.Where(m => !_archive.IsArchived(m.PeerProfileId)).ToArray();
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
                Plugin.Log.Warning(ex, "[ChatListScreen] search failed.");
            }
            finally
            {
                _searching = false;
            }
        }, ct);
    }

    /// <summary>Id of the first message in the peer's cached conversation whose text contains
    /// <paramref name="query"/>, or Empty. Reads ciphertext from the local cache and decrypts in memory with
    /// the per-conversation key; no server round-trip.</summary>
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

    private void DrawList(bool archived)
    {
        MatchSummaryDto[] all;
        lock (_matchesLock)
        {
            all = _matches.ToArray();
        }

        if (_fetching && all.Length == 0)
        {
            Widgets.LoadingIndicator.Draw();
            return;
        }
        if (_connectivityError && all.Length == 0)
        {
            DrawConnectivityError();
            return;
        }
        if (_fetchError is not null && all.Length == 0)
        {
            ImGui.PushTextWrapPos(0f);
            ImGui.TextColored(UiColors.Danger,
                Loc.T("chat.matches_load_error", _fetchError));
            ImGui.PopTextWrapPos();
            return;
        }

        var rows = all.Where(m => _archive.IsArchived(m.PeerProfileId) == archived).ToArray();

        var hits = _searchHits;
        var searchOn = !archived && _searchActive;
        if (searchOn)
        {
            rows = rows.Where(m => hits.ContainsKey(m.PeerProfileId)).ToArray();
        }

        if (rows.Length == 0)
        {
            if (searchOn)
            {
                DrawCenteredHint(Loc.T("chat.search_no_results"));
            }
            else if (archived)
            {
                DrawCenteredHint(Loc.T("chat.no_archived"));
            }
            else if (all.Length == 0)
            {
                DrawEmptyState();
            }
            else
            {
                DrawCenteredHint(Loc.T("chat.all_archived"));
            }
            return;
        }

        PushScrollbarStyle();

        using (var child = ImRaii.Child(archived ? "ArchiveList" : "MatchList", Vector2.Zero, false))
        {
            PopScrollbarStyle();
            if (!child.Success)
            {
                return;
            }

            // Virtualize: rows are a fixed height, so only those intersecting the viewport (plus a one-screen
            // margin) are drawn. Off-screen rows just advance the cursor, so scroll extent and position stay correct.
            var rowHeight = Px(MatchRowHeight);
            var viewH = ImGui.GetWindowSize().Y;
            var bandTop = ImGui.GetScrollY() - viewH;
            var bandBot = ImGui.GetScrollY() + viewH * 2f;

            for (int i = 0; i < rows.Length; i++)
            {
                var y0 = ImGui.GetCursorPosY();
                if (y0 + rowHeight < bandTop || y0 > bandBot)
                {
                    ImGui.SetCursorPosY(y0 + rowHeight);
                    continue;
                }

                var m = rows[i];
                // Mark the row that sits on the pinned/unpinned boundary so it gets an accent divider.
                var isPinnedBoundary = !archived && m.IsPinned && i + 1 < rows.Length && !rows[i + 1].IsPinned;
                SearchHit? hit = null;
                if (searchOn)
                {
                    hits.TryGetValue(m.PeerProfileId, out hit);
                }
                DrawMatchRow(m, isPinnedBoundary, archived, hit);
            }
        }
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

    /// <summary>Full-panel "couldn't reach the server" state: a disconnected icon, the localized message,
    /// and Try-again / Discord actions. Shown when a matches fetch fails while the hub is not connected.</summary>
    private void DrawConnectivityError()
    {
        var t = ThemeService.Current;
        var winSize = ImGui.GetWindowSize();
        var winPos = ImGui.GetWindowPos();

        const float IconScale = 2.6f;
        var Padding = Px(24f);
        var Gap = Px(22f);
        var ButtonGap = Px(20f);
        var buttonH = Px(32f);

        var icon = FontAwesomeIcon.Unlink.ToIconString();
        ImGui.PushFont(Plugin.PluginInterface.UiBuilder.FontIconFixedWidth);
        ImGui.SetWindowFontScale(IconScale * UiScale.S);
        var iconSz = ImGui.CalcTextSize(icon);
        ImGui.SetWindowFontScale(1.0f);
        ImGui.PopFont();

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
        ImGui.SetCursorScreenPos(new Vector2(iconX, blockTop));
        ImGui.PushFont(Plugin.PluginInterface.UiBuilder.FontIconFixedWidth);
        ImGui.SetWindowFontScale(IconScale * UiScale.S);
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.92f, 0.46f, 0.46f, 0.85f));
        ImGui.TextUnformatted(icon);
        ImGui.PopStyleColor();
        ImGui.SetWindowFontScale(1.0f);
        ImGui.PopFont();

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

        const float IconScale = 2.6f;
        var Padding = Px(24f);
        var Gap = Px(22f);

        var icon = FontAwesomeIcon.HeartBroken.ToIconString();
        ImGui.PushFont(Plugin.PluginInterface.UiBuilder.FontIconFixedWidth);
        ImGui.SetWindowFontScale(IconScale * UiScale.S);
        var iconSz = ImGui.CalcTextSize(icon);
        ImGui.SetWindowFontScale(1.0f);
        ImGui.PopFont();

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
        ImGui.SetCursorScreenPos(new Vector2(iconX, blockTop));
        ImGui.PushFont(Plugin.PluginInterface.UiBuilder.FontIconFixedWidth);
        ImGui.SetWindowFontScale(IconScale * UiScale.S);
        ImGui.PushStyleColor(ImGuiCol.Text,
            new Vector4(t.AccentLight.X, t.AccentLight.Y, t.AccentLight.Z, 0.55f));
        ImGui.TextUnformatted(icon);
        ImGui.PopStyleColor();
        ImGui.SetWindowFontScale(1.0f);
        ImGui.PopFont();

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

    /// <summary>Word-wraps <paramref name="text"/> into lines fitting within <paramref name="maxWidth"/>.</summary>
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

    private void DrawMatchRow(MatchSummaryDto m, bool isPinnedBoundary, bool archivedView, SearchHit? hit = null)
    {
        var drawList = ImGui.GetWindowDrawList();
        var cursorStart = ImGui.GetCursorScreenPos();
        var rowHeight = Px(MatchRowHeight);
        var windowWidth = ImGui.GetContentRegionAvail().X;

        ImGui.InvisibleButton($"##match_{m.PeerProfileId}", new Vector2(windowWidth, rowHeight));
        var isHovered = ImGui.IsItemHovered();
        var isClicked = ImGui.IsItemClicked();

        // A match with no messages yet still needs the first hello: tint it with the theme accent and
        // sweep a periodic shine across it so it stands out as needing attention. Both clear as soon as
        // either side sends a message, or once the user has opened the chat (acknowledged it).
        if (!archivedView && m.LastMessageAtUtc is null
            && !Plugin.Configuration.OpenedChats.Contains(m.PeerProfileId))
        {
            var rowMax = cursorStart + new Vector2(windowWidth, rowHeight);
            drawList.AddRectFilled(cursorStart, rowMax, ThemeService.Current.AccentWithAlpha(0.14f));
            DrawAttentionShine(drawList, cursorStart, rowMax);
        }

        if (isHovered)
        {
            drawList.AddRectFilled(
                cursorStart,
                cursorStart + new Vector2(windowWidth, rowHeight),
                0x20FFFFFF);
        }

        if (isClicked)
        {
            _selectedPeerId = m.PeerProfileId;
            _selectedPeerName = m.PeerDisplayName;
            _selectedPeerAvatar = m.PeerAvatarWebp;
            _selectedScrollMessageId = hit?.ContentMessageId ?? Guid.Empty;
            _router.Navigate(Screen.Chat);
        }

        if (ImGui.BeginPopupContextItem($"##matchctx_{m.PeerProfileId}", ImGuiPopupFlags.MouseButtonRight))
        {
            ImGui.TextDisabled(m.PeerDisplayName);
            ImGui.Separator();
            if (archivedView)
            {
                if (ChatScreen.DrawIconMenuItem(FontAwesomeIcon.BoxOpen, Loc.T("chat.menu_unarchive")))
                {
                    ImGui.CloseCurrentPopup();
                    SetArchived(m.PeerProfileId, false);
                }
            }
            else
            {
                if (ChatScreen.DrawIconMenuItem(
                        m.IsPinned ? FontAwesomeIcon.ThumbtackSlash : FontAwesomeIcon.Thumbtack,
                        m.IsPinned ? Loc.T("chat.menu_unpin") : Loc.T("chat.menu_pin")))
                {
                    ImGui.CloseCurrentPopup();
                    SetPinned(m.PeerProfileId, !m.IsPinned);
                }
                if (ChatScreen.DrawIconMenuItem(FontAwesomeIcon.Archive, Loc.T("chat.menu_archive")))
                {
                    ImGui.CloseCurrentPopup();
                    SetArchived(m.PeerProfileId, true);
                }
            }
            ImGui.EndPopup();
        }

        var avatarCenter = cursorStart + new Vector2(Px(40), rowHeight * 0.5f);
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

        var textPos = cursorStart + Px(80, 12);
        if (hit is { NameMatch: true } && _appliedQuery.Length > 0)
        {
            DrawNameHighlighted(drawList, textPos, m.PeerDisplayName, _appliedQuery);
        }
        else
        {
            drawList.AddText(textPos, 0xFFFFFFFF, m.PeerDisplayName);
        }
        if (m.IsPinned)
        {
            var nameW = ImGui.CalcTextSize(m.PeerDisplayName).X;
            ImGui.PushFont(Plugin.PluginInterface.UiBuilder.FontIcon);
            drawList.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.8f,
                textPos + new Vector2(nameW + Px(6f), Px(1f)),
                ThemeService.Current.AccentU32, FontAwesomeIcon.Thumbtack.ToIconString());
            ImGui.PopFont();
        }

        var timeAgo = m.LastMessageAtUtc is not null
            ? GetTimeAgo(m.LastMessageAtUtc.Value.LocalDateTime)
            : Loc.T("chat.new_match");
        var timeSize = ImGui.CalcTextSize(timeAgo);
        drawList.AddText(
            cursorStart + new Vector2(windowWidth - timeSize.X - Px(10), Px(12)),
            UiColors.TextMuted,
            timeAgo);

        // Last-message preview ("Me: …" for outgoing, the text itself for incoming).
        _previewByPeer.TryGetValue(m.PeerProfileId, out var preview);
        var previewText = !string.IsNullOrEmpty(preview)
            ? preview
            : (m.LastMessageAtUtc is null ? Loc.T("chat.say_hi") : string.Empty);
        if (!string.IsNullOrEmpty(previewText))
        {
            var previewCol = m.UnreadCount > 0 ? 0xFFDDDDDDu : 0xFF999999u;
            drawList.AddText(cursorStart + Px(80, 38), previewCol, previewText);
        }

        if (isPinnedBoundary)
        {
            // Accent, full-width separator between the pinned group and the rest.
            drawList.AddLine(
                cursorStart + new Vector2(0f, rowHeight),
                cursorStart + new Vector2(windowWidth, rowHeight),
                ThemeService.Current.AccentU32, Px(2f));
        }
        else
        {
            drawList.AddLine(
                cursorStart + new Vector2(Px(80), rowHeight),
                cursorStart + new Vector2(windowWidth, rowHeight),
                0xFF333333);
        }

        ImGui.SetCursorScreenPos(cursorStart + new Vector2(0, rowHeight));
    }

    /// <summary>A theme-tinted glint that sweeps across an attention row every few seconds (skipped under
    /// reduce-motion). Driven by the global clock, so every awaiting-reply row shines in unison.</summary>
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

    /// <summary>Draws a match's name, colouring the first occurrence of <paramref name="query"/> with the
    /// search-highlight accent so the matched text stands out in the row.</summary>
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
