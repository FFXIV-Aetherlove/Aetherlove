using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Shared.Messenger;
using AetherLove.UI;
using AetherLove.Widgets;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Messenger;

/// <summary>The chat list, styled after the AetherLove match list: request rows on top, colored category
/// rows, then pinned and recent chats, with the search row and every secondary action behind the header
/// menu.</summary>
public sealed partial class MessengerApp
{
    private sealed record ChatRow(
        Guid Id,
        MessengerChatKind Kind,
        string Title,
        byte[]? Avatar,
        bool Pinned,
        int Unread,
        DateTimeOffset? LastAt,
        string? Preview,
        bool LastFromMe,
        bool HasMessages,
        bool RemovedByPeer,
        MessengerGroupDto? Group);

    private const float ChatRowHeight = 80f;
    private const float CategoryRowHeight = 64f;
    private const float DragStartThreshold = 6f;
    private const float DepartDuration = 0.30f;
    private const float FlightDuration = 0.38f;
    private const float PulseDuration = 0.55f;
    private const float SpringBackDuration = 0.18f;
    private const float PickupDuration = 0.12f;

    /// <summary>Dark veil over the source row while it is being dragged.</summary>
    private const uint DragDim = 0xAA101010u;

    /// <summary>Drag-ghost pill fill.</summary>
    private const uint GhostPillBg = 0xF01A1420u;

    private enum RowContext
    {
        TopLevel,
        Category,
        Search,
    }

    private enum DragKind
    {
        None,
        Chat,
        Category,
    }

    /// <summary>A row collapsing out of the current view; the move applies when the collapse completes.</summary>
    private sealed class DepartAnim
    {
        public float T;
        public Guid TargetCategoryId;
    }

    private sealed class Flight
    {
        public float T;
        public Vector2 From;
        public Guid TargetCategoryId;
        public Vector2 LastTarget;
        public ISharedImmediateTexture? Avatar;
    }

    private struct Burst
    {
        public float T;
        public Vector2 Pos;
        public Vector2 Vel;
        public uint Color;
    }

    private sealed class SpringBack
    {
        public float T;
        public Vector2 From;
        public ISharedImmediateTexture? Avatar;
    }

    private readonly Dictionary<Guid, DepartAnim> _departing = new();
    private readonly List<Flight> _flights = new();
    private readonly List<Burst> _bursts = new();
    private readonly Dictionary<Guid, float> _catPulse = new();
    private SpringBack? _springBack;

    private DragKind _dragKind;
    private bool _dragActive;
    private float _dragT;
    private Guid _pressChatId;
    private Guid _pressCategoryId;
    private Guid _dragChatId;
    private ChatRow? _dragRow;
    private MsgrCategoryData? _dragCategory;
    private Vector2 _dragSourceRowCenter;
    private Guid _hoverDropCategoryId;
    private int _reorderInsertIndex = -1;

    private readonly Dictionary<Guid, Vector2> _catAvatarCenters = new();

    private string _listQuery = string.Empty;
    private string _listApplied = string.Empty;
    private bool _listSearchActive;
    private Dictionary<Guid, Guid> _listHits = new();

    private sealed record MsgrCategoryData(Guid Id, string Name, uint Color);

    private List<MsgrCategoryData> _categories = new();
    private Dictionary<string, Guid> _categoryMembers = new();
    private bool _categoriesLoaded;
    private bool _showSearch;
    private Guid _openCategoryId;

    private bool _catEditorOpen;
    private Guid _catEditorId;
    private string _catEditorName = string.Empty;
    private int _catEditorColorIdx;
    private bool _catEditorFocus;
    private float _catEditorPanelH;
    private Guid _catEditorPendingMoveChat;
    private bool _catEditorPendingMoveInstant;
    private Guid _catDeleteId;
    private string _catDeleteName = string.Empty;
    private float _catDeletePanelH;

    private void EnsureCategoriesLoaded()
    {
        if (_categoriesLoaded)
        {
            return;
        }
        _categoriesLoaded = true;
        var storage = _caps.Storage(Id);
        _showSearch = storage.Get<bool?>("showSearch") ?? false;
        _categories = storage.Get<List<MsgrCategoryData>>("categories2") ?? new List<MsgrCategoryData>();
        _categoryMembers = storage.Get<Dictionary<string, Guid>>("categoryMembers") ?? new Dictionary<string, Guid>();
        // One-time migration of the pre-overhaul name-keyed categories; the marker (not emptiness) gates
        // it, so deleting every category later doesn't resurrect the legacy set next session.
        if (storage.Get<bool?>("categoriesMigrated") == true)
        {
            return;
        }
        storage.Set("categoriesMigrated", (bool?)true);
        if (_categories.Count > 0 || _categoryMembers.Count > 0)
        {
            return;
        }
        var legacyNames = storage.Get<List<string>>("categories");
        var legacyMembers = storage.Get<Dictionary<string, string>>("chatCategories");
        if (legacyNames is not { Count: > 0 })
        {
            return;
        }
        var byName = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < legacyNames.Count; i++)
        {
            var id = Guid.NewGuid();
            var color = UiColors.CategoryPalette[i % UiColors.CategoryPalette.Length];
            _categories.Add(new MsgrCategoryData(id, legacyNames[i], color));
            byName[legacyNames[i]] = id;
        }
        if (legacyMembers is not null)
        {
            foreach (var (chat, name) in legacyMembers)
            {
                if (byName.TryGetValue(name, out var catId))
                {
                    _categoryMembers[chat] = catId;
                }
            }
        }
        SaveCategories();
    }

    private void SaveCategories()
    {
        var storage = _caps.Storage(Id);
        storage.Set("categories2", _categories);
        storage.Set("categoryMembers", _categoryMembers);
    }

    private void SaveShowSearch()
    {
        _caps.Storage(Id).Set("showSearch", (bool?)_showSearch);
    }

    private Guid? CategoryOf(Guid chatId)
        => _categoryMembers.TryGetValue(chatId.ToString("N"), out var catId)
           && _categories.Any(c => c.Id == catId)
            ? catId
            : null;

    private void SetCategory(Guid chatId, Guid? catId)
    {
        var key = chatId.ToString("N");
        if (catId is { } id)
        {
            _categoryMembers[key] = id;
        }
        else
        {
            _categoryMembers.Remove(key);
        }
        SaveCategories();
    }

    private List<ChatRow> BuildRows()
    {
        var rows = new List<ChatRow>();
        foreach (var c in _store.Contacts)
        {
            var preview = DecryptPreview(c.ContactId, MessengerChatKind.Direct,
                c.LastMessageAtUtc, c.LastMessageCiphertext, c.LastMessageNonce, 0);
            rows.Add(new ChatRow(c.ContactId, MessengerChatKind.Direct, c.PeerName, c.PeerAvatar,
                c.PinnedByMe, c.Unread, c.LastMessageAtUtc ?? c.AddedAtUtc, preview, c.LastMessageFromMe,
                c.LastMessageAtUtc is not null, c.RemovedByPeer, null));
        }
        foreach (var g in _store.Groups)
        {
            rows.Add(new ChatRow(g.GroupId, MessengerChatKind.Group, g.Name, g.Avatar,
                g.PinnedByMe, g.Unread, g.LastMessageAtUtc ?? g.CreatedAtUtc, DecryptGroupPreview(g),
                g.LastMessageSenderId == _store.MyAccountId, g.LastMessageAtUtc is not null, false, g));
        }
        return rows
            .OrderByDescending(r => r.Pinned)
            .ThenByDescending(r => r.LastAt ?? DateTimeOffset.MinValue)
            .ToList();
    }

    private void DrawChatList(OsAppContext ctx, bool overlayActive)
    {
        EnsureCategoriesLoaded();
        var winPos = ImGui.GetWindowPos();
        var winSize = ImGui.GetWindowSize();
        UpdateCategoryAnimations(ImGui.GetIO().DeltaTime);

        DrawListHeader();
        DrawShareBanner(ImGui.GetContentRegionAvail().X);

        var rows = BuildRows();
        var searchOn = _listSearchActive;
        var hits = _listHits;

        Guid? CatOf(Guid chatId) => CategoryOf(chatId);
        var visible = searchOn
            ? rows.Where(r => hits.ContainsKey(r.Id)).ToList()
            : rows.Where(r => CatOf(r.Id) is null).ToList();

        var unreadByCat = new Dictionary<Guid, int>();
        var countByCat = new Dictionary<Guid, int>();
        foreach (var r in rows)
        {
            if (CatOf(r.Id) is not { } catId)
            {
                continue;
            }
            countByCat[catId] = countByCat.GetValueOrDefault(catId) + 1;
            unreadByCat[catId] = unreadByCat.GetValueOrDefault(catId) + r.Unread;
        }

        var showRequests = _pendingShare is null && !searchOn;
        var incoming = showRequests ? _store.Requests.Where(r => r.Incoming).ToArray() : [];
        var outgoing = showRequests ? OutgoingRequests() : [];

        PushScrollbarStyle();
        using (var child = ImRaii.Child("MsgrChatList", Vector2.Zero, false))
        {
            PopScrollbarStyle();
            if (!child.Success)
            {
                return;
            }

            if (incoming.Length > 0)
            {
                SectionLabel(Loc.T("os.msgr_requests"));
                foreach (var request in incoming)
                {
                    DrawRequestRow(request, ImGui.GetContentRegionAvail().X, overlayActive);
                }
                ImGui.Dummy(new Vector2(0f, Px(4f)));
            }

            if (outgoing.Length > 0)
            {
                SectionLabel(Loc.T("os.msgr_pending_out"));
                foreach (var request in outgoing)
                {
                    DrawOutgoingRequestRow(request, ImGui.GetContentRegionAvail().X, overlayActive);
                }
                ImGui.Dummy(new Vector2(0f, Px(4f)));
            }

            if (!searchOn)
            {
                DrawCategoryRows(countByCat, unreadByCat);
                if (_categories.Count > 0 && visible.Count > 0)
                {
                    var dl = ImGui.GetWindowDrawList();
                    var p = ImGui.GetCursorScreenPos();
                    dl.AddLine(p, p + new Vector2(ImGui.GetContentRegionAvail().X, 0f), UiColors.Divider, Px(1f));
                    ImGui.Dummy(new Vector2(0f, Px(4f)));
                }
            }

            if (searchOn && visible.Count == 0)
            {
                DrawCenteredListHint(Loc.T("chat.search_no_results"));
            }
            else if (rows.Count == 0 && incoming.Length == 0 && outgoing.Length == 0)
            {
                DrawEmptyState(ImGui.GetContentRegionAvail().X);
            }
            else
            {
                foreach (var row in visible)
                {
                    _departing.TryGetValue(row.Id, out var depart);
                    var visH = depart is null
                        ? Px(ChatRowHeight)
                        : Px(ChatRowHeight) * (1f - EaseInCubicF(depart.T));
                    if (visH < 0.5f)
                    {
                        continue;
                    }
                    DrawChatRow(row, overlayActive, searchOn ? hits.GetValueOrDefault(row.Id) : Guid.Empty,
                        searchOn ? RowContext.Search : RowContext.TopLevel, depart, visH);
                }
            }

            if (_busyError is { } err)
            {
                ImGui.Spacing();
                ImGui.SetCursorPosX(Px(10f));
                ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X - Px(10f));
                ImGui.TextColored(UiColors.Danger, err);
                ImGui.PopTextWrapPos();
            }
        }

        ResolveDragAndDrop();
        DrawDragOverlays(winPos, winSize);
        DrawCategoryEditor(winPos, winSize);
        DrawCategoryDeleteConfirm(winPos, winSize);
    }

    private void DrawCategoryView(OsAppContext ctx, bool overlayActive)
    {
        EnsureCategoriesLoaded();
        var cat = _categories.FirstOrDefault(c => c.Id == _openCategoryId);
        if (cat is null)
        {
            _view = View.List;
            return;
        }
        var winPos = ImGui.GetWindowPos();
        var winSize = ImGui.GetWindowSize();
        UpdateCategoryAnimations(ImGui.GetIO().DeltaTime);

        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.SetCursorPosX(Px(16f));
        if (DrawFloatingBackPill(ImGui.GetCursorScreenPos(), Loc.T("os.msgr_back"), FontAwesomeIcon.CommentDots))
        {
            _view = View.List;
            _openFadeAt = -1;
        }
        DrawSubpageHeading(cat.Name, 16f);

        var rows = BuildRows().Where(r => CategoryOf(r.Id) == cat.Id).ToList();
        if (rows.Count == 0)
        {
            DrawCenteredListHint(Loc.T("chat.category_empty"));
        }
        else
        {
            PushScrollbarStyle();
            using (var child = ImRaii.Child("MsgrCategoryList", Vector2.Zero, false))
            {
                PopScrollbarStyle();
                if (child.Success)
                {
                    foreach (var row in rows)
                    {
                        _departing.TryGetValue(row.Id, out var depart);
                        var visH = depart is null
                            ? Px(ChatRowHeight)
                            : Px(ChatRowHeight) * (1f - EaseInCubicF(depart.T));
                        if (visH < 0.5f)
                        {
                            continue;
                        }
                        DrawChatRow(row, overlayActive, Guid.Empty, RowContext.Category, depart, visH);
                    }
                }
            }
        }

        DrawCategoryEditor(winPos, winSize);
        DrawCategoryDeleteConfirm(winPos, winSize);
    }

    private void DrawListHeader()
    {
        var winW = ImGui.GetWindowSize().X;

        AppHeader.DrawTitle(Loc.T("os.msgr_title"), 16f);
        var afterTitleY = ImGui.GetCursorPosY();

        var menuTL = AppHeader.DrawMenuButton(winW, 16f, "##msgrListMenu",
            badge: _store.IncomingRequestCount() > 0);
        var menuOpen = AppHeader.BeginMenuPopup(menuTL, "##msgrListMenu");
        if (menuOpen)
        {
            string? myCode = _store.Sync?.MyCode;
            var hasCode = myCode is { Length: > 0 };
            var codeLabel = hasCode ? Loc.T("os.msgr_my_code", MessengerCodeDisplay(myCode!)) : string.Empty;
            var searchLabel = Loc.T(_showSearch ? "chat.hide_search" : "chat.show_search");

            var labels = new List<string>
            {
                Loc.T("os.msgr_add_contact"),
                Loc.T("os.msgr_new_group"),
                Loc.T("chat.category_new"),
                searchLabel,
                Loc.T("os.msgr_datalimits_title"),
                Loc.T("os.msgr_become_supporter"),
                Loc.T("os.msgr_blocked"),
                Loc.T("os.msgr_view_tour"),
                Loc.T("os.msgr_settings"),
            };
            if (hasCode)
            {
                labels.Add(codeLabel);
            }
            var w = AppHeader.MenuWidth(labels.ToArray());
            var rowH = AppHeader.MenuRowHeight();

            if (hasCode && AppHeader.MenuRow(FontAwesomeIcon.Copy, codeLabel, w, rowH))
            {
                ImGui.CloseCurrentPopup();
                _caps.System.CopyToClipboard(myCode!);
            }
            if (AppHeader.MenuRow(FontAwesomeIcon.UserPlus, Loc.T("os.msgr_add_contact"), w, rowH))
            {
                ImGui.CloseCurrentPopup();
                OpenAddContact();
            }
            if (AppHeader.MenuRow(FontAwesomeIcon.Users, Loc.T("os.msgr_new_group"), w, rowH))
            {
                ImGui.CloseCurrentPopup();
                OpenNewGroup();
            }
            if (AppHeader.MenuRow(FontAwesomeIcon.FolderPlus, Loc.T("chat.category_new"), w, rowH))
            {
                ImGui.CloseCurrentPopup();
                OpenCategoryEditor(null);
            }
            if (AppHeader.MenuRow(_showSearch ? FontAwesomeIcon.SearchMinus : FontAwesomeIcon.Search,
                    searchLabel, w, rowH))
            {
                ImGui.CloseCurrentPopup();
                _showSearch = !_showSearch;
                SaveShowSearch();
                if (!_showSearch)
                {
                    ClearListSearch();
                }
            }
            if (AppHeader.MenuRow(FontAwesomeIcon.Database, Loc.T("os.msgr_datalimits_title"), w, rowH))
            {
                ImGui.CloseCurrentPopup();
                OpenDataLimits();
            }
            if (AppHeader.MenuRow(FontAwesomeIcon.Star, Loc.T("os.msgr_become_supporter"), w, rowH, UiColors.FavoriteStar))
            {
                ImGui.CloseCurrentPopup();
                _shell?.SendIntent("settings", OsIntents.CreateReturn(OsIntents.OpenSupporter, Id));
            }
            if (AppHeader.MenuRow(FontAwesomeIcon.Compass, Loc.T("os.msgr_view_tour"), w, rowH))
            {
                ImGui.CloseCurrentPopup();
                OpenTour();
            }
            if (AppHeader.MenuRow(FontAwesomeIcon.Ban, Loc.T("os.msgr_blocked"), w, rowH))
            {
                ImGui.CloseCurrentPopup();
                OpenBlocked();
            }
            if (AppHeader.MenuRow(FontAwesomeIcon.Cog, Loc.T("os.msgr_settings"), w, rowH))
            {
                ImGui.CloseCurrentPopup();
                _view = View.Settings;
            }
        }
        AppHeader.EndMenuPopup(menuOpen);

        ImGui.SetCursorPosY(afterTitleY);
        ImGui.Spacing();

        if (_showSearch)
        {
            DrawListSearchRow();
            ImGui.Spacing();
        }
    }

    private static string MessengerCodeDisplay(string code)
        => code.Length == 8 ? $"{code[..4]}@{code[4..]}" : code;

    private void DrawListSearchRow()
    {
        var t = ThemeService.Current;

        var btnW = Px(32f);
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var avail = ImGui.GetContentRegionAvail().X;
        var inputW = MathF.Max(Px(60f), avail - (btnW * 2f + spacing * 2f));

        ImGui.SetNextItemWidth(inputW);
        var submit = ImGui.InputTextWithHint("##msgrSearchInput", Loc.T("chat.search_hint"),
            ref _listQuery, 100, ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.SameLine();

        PushThemeButton(t);
        ImGui.PushFont(AetherLove.UiHost.PluginInterface.UiBuilder.FontIcon);
        var search = ImGui.Button(FontAwesomeIcon.Search.ToIconString() + "##msgrDoSearch", new Vector2(btnW, 0f));
        ImGui.SameLine();
        var clear = ImGui.Button(FontAwesomeIcon.Times.ToIconString() + "##msgrClearSearch", new Vector2(btnW, 0f));
        ImGui.PopFont();
        PopThemeButton();

        if (search || submit)
        {
            RunListSearch();
        }
        if (clear)
        {
            ClearListSearch();
        }
        ImGui.Spacing();
    }

    private void ClearListSearch()
    {
        _listSearchActive = false;
        _listHits = new Dictionary<Guid, Guid>();
        _listApplied = string.Empty;
        _listQuery = string.Empty;
    }

    /// <summary>Fully offline: names and locally-decrypted conversation contents (the server only holds
    /// ciphertext). The hit value is the first matching message id, or Empty for a name-only match.</summary>
    private void RunListSearch()
    {
        var query = _listQuery.Trim();
        if (query.Length == 0)
        {
            ClearListSearch();
            return;
        }
        var hits = new Dictionary<Guid, Guid>();
        foreach (var row in BuildRows())
        {
            var nameMatch = row.Title.Contains(query, StringComparison.OrdinalIgnoreCase);
            var contentMsg = Guid.Empty;
            foreach (var m in _store.Conversation(row.Id))
            {
                if (Decrypt(m) is { } text && text.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    contentMsg = m.Id;
                    break;
                }
            }
            if (nameMatch || contentMsg != Guid.Empty)
            {
                hits[row.Id] = contentMsg;
            }
        }
        _listHits = hits;
        _listApplied = query;
        _listSearchActive = true;
    }

    private static uint WithAlpha(uint color, float alpha)
        => (color & 0x00FFFFFFu) | ((uint)(Math.Clamp(alpha, 0f, 1f) * 255f) << 24);

    /// <summary>The uppercased first text element of the category name (surrogate/emoji safe).</summary>
    private static string CategoryLetter(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            return "?";
        }
        return new System.Globalization.StringInfo(trimmed).SubstringByTextElements(0, 1).ToUpperInvariant();
    }

    private void DrawCategoryRows(Dictionary<Guid, int> countByCat, Dictionary<Guid, int> unreadByCat)
    {
        _catAvatarCenters.Clear();
        _hoverDropCategoryId = Guid.Empty;
        _reorderInsertIndex = -1;
        if (_categories.Count == 0)
        {
            return;
        }

        var rowH = Px(CategoryRowHeight);
        var listTop = ImGui.GetCursorScreenPos().Y;
        var mouse = ImGui.GetMousePos();

        // Reorder target: rows sit at fixed offsets, so the insertion slot falls out of the mouse Y directly.
        if (_dragActive && _dragKind == DragKind.Category)
        {
            var slot = (int)MathF.Floor((mouse.Y - listTop + rowH * 0.5f) / rowH);
            _reorderInsertIndex = Math.Clamp(slot, 0, _categories.Count);
        }

        foreach (var cat in _categories.ToArray())
        {
            DrawCategoryRow(cat, countByCat.GetValueOrDefault(cat.Id), unreadByCat.GetValueOrDefault(cat.Id));
        }

        if (_reorderInsertIndex >= 0)
        {
            var y = listTop + _reorderInsertIndex * rowH;
            var dl = ImGui.GetWindowDrawList();
            var w = ImGui.GetContentRegionAvail().X;
            var x0 = ImGui.GetCursorScreenPos().X;
            dl.AddLine(new Vector2(x0 + Px(8f), y), new Vector2(x0 + w - Px(8f), y),
                ThemeService.Current.AccentLightU32, Px(3f));
        }
    }

    private void DrawCategoryRow(MsgrCategoryData cat, int chatCount, int unread)
    {
        var dl = ImGui.GetWindowDrawList();
        var cursorStart = ImGui.GetCursorScreenPos();
        var rowH = Px(CategoryRowHeight);
        var width = ImGui.GetContentRegionAvail().X;
        var rowMax = cursorStart + new Vector2(width, rowH);
        var beingDragged = _dragActive && _dragKind == DragKind.Category && _dragCategory?.Id == cat.Id;
        var t = ThemeService.Current;

        ImGui.InvisibleButton($"##msgrCat_{cat.Id}", new Vector2(width, rowH));
        var hovered = ImGui.IsItemHovered() && !_dragActive;

        if (ImGui.IsItemActivated())
        {
            _pressCategoryId = cat.Id;
        }
        if (ImGui.IsItemActive() && _pressCategoryId == cat.Id && !_dragActive
            && ImGui.GetMouseDragDelta(ImGuiMouseButton.Left).Length() > Px(DragStartThreshold))
        {
            StartCategoryDrag(cat);
        }
        if (ImGui.IsItemDeactivated() && _pressCategoryId == cat.Id)
        {
            _pressCategoryId = Guid.Empty;
            if (!_dragActive && ImGui.IsItemHovered())
            {
                _openCategoryId = cat.Id;
                _view = View.Category;
                _openFadeAt = -1;
            }
        }

        if (ImGui.BeginPopupContextItem($"##msgrCatCtx_{cat.Id}", ImGuiPopupFlags.MouseButtonRight))
        {
            ImGui.TextDisabled(cat.Name);
            ImGui.Separator();
            if (DrawIconMenuItem(FontAwesomeIcon.Pen, Loc.T("chat.category_edit")))
            {
                ImGui.CloseCurrentPopup();
                OpenCategoryEditor(cat);
            }
            if (DrawIconMenuItem(FontAwesomeIcon.Trash, Loc.T("chat.category_delete"), UiColors.MenuDanger))
            {
                ImGui.CloseCurrentPopup();
                _catDeleteId = cat.Id;
                _catDeleteName = cat.Name;
                _catDeletePanelH = 0f;
            }
            ImGui.EndPopup();
        }

        var dropHover = _dragActive && _dragKind == DragKind.Chat
                        && ImGui.IsMouseHoveringRect(cursorStart, rowMax);
        if (dropHover)
        {
            _hoverDropCategoryId = cat.Id;
        }

        if (hovered)
        {
            dl.AddRectFilled(cursorStart, rowMax, 0x20FFFFFF);
        }
        if (dropHover)
        {
            dl.AddRectFilled(cursorStart, rowMax, WithAlpha(cat.Color, 0.14f));
            dl.AddRect(cursorStart + new Vector2(Px(2f), Px(2f)), rowMax - new Vector2(Px(2f), Px(2f)),
                WithAlpha(cat.Color, 0.9f), Px(6f), ImDrawFlags.None, Px(2f));
        }

        var avatarCenter = cursorStart + new Vector2(Px(40f), rowH * 0.5f);
        var avatarR = Px(22f);
        _catAvatarCenters[cat.Id] = avatarCenter;

        // Arrival pulse: the avatar swells briefly and emits an expanding ring.
        if (_catPulse.TryGetValue(cat.Id, out var pulseT))
        {
            avatarR *= 1f + 0.28f * MathF.Sin(MathF.PI * pulseT);
            var ringR = Px(22f) * (1f + 1.3f * EaseOutCubicF(pulseT));
            dl.AddCircle(avatarCenter, ringR, WithAlpha(cat.Color, 1f - pulseT), 0, Px(2.5f));
        }

        dl.AddCircleFilled(avatarCenter, avatarR, cat.Color);
        dl.AddCircle(avatarCenter, avatarR, 0xFFFFFFFF, 0, Px(1.5f));
        if (dropHover)
        {
            var pulse = 0.5f + 0.5f * MathF.Sin((float)ImGui.GetTime() * 6f);
            dl.AddCircle(avatarCenter, avatarR + Px(4f) + pulse * Px(2f), WithAlpha(cat.Color, 0.9f), 0, Px(2.5f));
        }

        var letter = CategoryLetter(cat.Name);
        var letterSize = ImGui.GetFontSize() * 1.25f;
        var letterSz = ImGui.CalcTextSize(letter) * (letterSize / ImGui.GetFontSize());
        dl.AddText(ImGui.GetFont(), letterSize, avatarCenter - letterSz * 0.5f, 0xFFFFFFFF, letter);

        if (unread > 0)
        {
            var badgeR = Px(9f);
            var badgeCenter = avatarCenter + new Vector2(avatarR - Px(5f), -avatarR + Px(5f));
            dl.AddCircleFilled(badgeCenter, badgeR, UiColors.UnreadBadge);
            var label = unread > 9 ? "9+" : unread.ToString();
            var fsz = ImGui.GetFontSize() * 0.74f;
            var tsize = ImGui.CalcTextSize(label) * (fsz / ImGui.GetFontSize());
            dl.AddText(ImGui.GetFont(), fsz, badgeCenter - tsize * 0.5f, 0xFFFFFFFF, label);
        }

        dl.AddText(cursorStart + Px(80, 11), 0xFFFFFFFF, cat.Name);
        dl.AddText(cursorStart + Px(80, 34), UiColors.TextMuted, Loc.T("chat.category_count", chatCount));

        ImGui.PushFont(AetherLove.UiHost.PluginInterface.UiBuilder.FontIcon);
        var chevron = FontAwesomeIcon.ChevronRight.ToIconString();
        var chevSz = ImGui.CalcTextSize(chevron);
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.85f,
            new Vector2(cursorStart.X + width - chevSz.X - Px(12f), avatarCenter.Y - chevSz.Y * 0.42f),
            hovered ? t.AccentLightU32 : UiColors.TextMuted, chevron);
        ImGui.PopFont();

        if (beingDragged)
        {
            dl.AddRectFilled(cursorStart, rowMax, DragDim);
        }

        dl.AddLine(cursorStart + new Vector2(Px(80), rowH), cursorStart + new Vector2(width, rowH), 0xFF333333);
        ImGui.SetCursorScreenPos(cursorStart + new Vector2(0f, rowH));
    }

    private void OpenCategoryEditor(MsgrCategoryData? existing, Guid pendingMoveChat = default,
                                    bool pendingMoveInstant = false)
    {
        _catEditorOpen = true;
        _catEditorId = existing?.Id ?? Guid.Empty;
        _catEditorName = existing?.Name ?? string.Empty;
        _catEditorColorIdx = existing is null ? 0 : Math.Max(0, Array.IndexOf(UiColors.CategoryPalette, existing.Color));
        _catEditorPendingMoveChat = pendingMoveChat;
        _catEditorPendingMoveInstant = pendingMoveInstant;
        _catEditorFocus = true;
        _catEditorPanelH = 0f;
    }

    private void DrawCategoryEditor(Vector2 winPos, Vector2 winSize)
    {
        if (!_catEditorOpen)
        {
            return;
        }
        if (ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            _catEditorOpen = false;
            return;
        }
        if (DrawPageOverlayPanel("msgrCatEditor", winPos, winSize, ref _catEditorPanelH, Px(240f),
                DrawCategoryEditorContent))
        {
            _catEditorOpen = false;
        }
    }

    private void DrawCategoryEditorContent(float innerW)
    {
        var t = ThemeService.Current;
        var creating = _catEditorId == Guid.Empty;
        ModalUi.Header(innerW, creating ? FontAwesomeIcon.FolderPlus : FontAwesomeIcon.Pen,
            creating ? Loc.T("chat.category_new") : Loc.T("chat.category_edit"), t.AccentLight);

        var color = UiColors.CategoryPalette[Math.Clamp(_catEditorColorIdx, 0, UiColors.CategoryPalette.Length - 1)];
        var dl = ImGui.GetWindowDrawList();
        var avatarR = Px(16f);
        var rowStart = ImGui.GetCursorScreenPos();
        var avatarC = rowStart + new Vector2(avatarR, ImGui.GetFrameHeight() * 0.5f);
        dl.AddCircleFilled(avatarC, avatarR, color);
        dl.AddCircle(avatarC, avatarR, 0xFFFFFFFF, 0, Px(1.5f));
        var letter = CategoryLetter(_catEditorName);
        var lsz = ImGui.CalcTextSize(letter);
        dl.AddText(avatarC - lsz * 0.5f, 0xFFFFFFFF, letter);

        ImGui.SetCursorScreenPos(rowStart + new Vector2(avatarR * 2f + Px(10f), 0f));
        ImGui.SetNextItemWidth(innerW - avatarR * 2f - Px(10f));
        if (_catEditorFocus)
        {
            ImGui.SetKeyboardFocusHere();
            _catEditorFocus = false;
        }
        var submit = ImGui.InputTextWithHint("##msgrCatName", Loc.T("chat.category_name_hint"),
            ref _catEditorName, 30, ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.Spacing();
        ImGui.Spacing();

        var swatch = Px(24f);
        var gap = Px(7f);
        const int PerRow = 8;
        var gridStart = ImGui.GetCursorScreenPos();
        for (var i = 0; i < UiColors.CategoryPalette.Length; i++)
        {
            var col = i % PerRow;
            var row = i / PerRow;
            var min = gridStart + new Vector2(col * (swatch + gap), row * (swatch + gap));
            ImGui.SetCursorScreenPos(min);
            if (ImGui.InvisibleButton($"##msgrSwatch{i}", new Vector2(swatch, swatch)))
            {
                _catEditorColorIdx = i;
            }
            var hovered = ImGui.IsItemHovered();
            dl.AddRectFilled(min, min + new Vector2(swatch, swatch), UiColors.CategoryPalette[i], Px(6f));
            if (i == _catEditorColorIdx)
            {
                dl.AddRect(min - new Vector2(Px(2f), Px(2f)), min + new Vector2(swatch + Px(2f), swatch + Px(2f)),
                    0xFFFFFFFF, Px(7f), ImDrawFlags.None, Px(2f));
            }
            else if (hovered)
            {
                dl.AddRect(min, min + new Vector2(swatch, swatch), 0xAAFFFFFF, Px(6f), ImDrawFlags.None, Px(1.5f));
            }
        }
        var rowsUsed = (UiColors.CategoryPalette.Length + PerRow - 1) / PerRow;
        ImGui.SetCursorScreenPos(gridStart + new Vector2(0f, rowsUsed * (swatch + gap)));
        ImGui.Spacing();

        var btnW = (innerW - Px(10f)) * 0.5f;
        var confirm = ModalUi.Button(
            $"{(creating ? Loc.T("chat.category_create") : Loc.T("chat.category_save"))}##msgrCatOk", btnW);
        ImGui.SameLine(0f, Px(10f));
        if (ModalUi.Button($"{Loc.T("common.cancel")}##msgrCatCancel", btnW))
        {
            _catEditorOpen = false;
        }

        if ((confirm || submit) && _catEditorName.Trim().Length > 0)
        {
            var name = _catEditorName.Trim();
            if (_catEditorId == Guid.Empty)
            {
                var created = new MsgrCategoryData(Guid.NewGuid(), name, color);
                _categories.Add(created);
                if (_catEditorPendingMoveChat != Guid.Empty)
                {
                    RequestMove(_catEditorPendingMoveChat, created.Id, _catEditorPendingMoveInstant);
                }
            }
            else
            {
                var idx = _categories.FindIndex(c => c.Id == _catEditorId);
                if (idx >= 0)
                {
                    _categories[idx] = _categories[idx] with { Name = name, Color = color };
                }
            }
            SaveCategories();
            _catEditorOpen = false;
            _catEditorPendingMoveChat = Guid.Empty;
        }
    }

    private void DrawCategoryDeleteConfirm(Vector2 winPos, Vector2 winSize)
    {
        if (_catDeleteId == Guid.Empty)
        {
            return;
        }
        if (ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            _catDeleteId = Guid.Empty;
            return;
        }
        if (DrawPageOverlayPanel("msgrCatDelete", winPos, winSize, ref _catDeletePanelH, Px(200f),
                DrawCategoryDeleteConfirmContent))
        {
            _catDeleteId = Guid.Empty;
        }
    }

    private void DrawCategoryDeleteConfirmContent(float innerW)
    {
        ModalUi.Header(innerW, FontAwesomeIcon.Trash, Loc.T("chat.category_delete"), UiColors.Danger);

        ImGui.PushTextWrapPos(innerW);
        ImGui.TextColored(UiColors.Body, Loc.T("chat.category_delete_body", _catDeleteName));
        ImGui.PopTextWrapPos();
        ImGui.Spacing();
        ImGui.Spacing();

        var btnW = (innerW - Px(10f)) * 0.5f;
        PushDangerButton();
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
        var confirm = ImGui.Button($"{Loc.T("chat.category_delete_confirm")}##msgrCatDelOk", new Vector2(btnW, Px(32f)));
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);
        ImGui.SameLine(0f, Px(10f));
        if (ModalUi.Button($"{Loc.T("common.cancel")}##msgrCatDelCancel", btnW))
        {
            _catDeleteId = Guid.Empty;
        }
        if (confirm)
        {
            var id = _catDeleteId;
            _categories.RemoveAll(c => c.Id == id);
            foreach (var key in _categoryMembers.Where(kv => kv.Value == id).Select(kv => kv.Key).ToArray())
            {
                _categoryMembers.Remove(key);
            }
            SaveCategories();
            if (_openCategoryId == id)
            {
                _view = View.List;
            }
            _catDeleteId = Guid.Empty;
        }
    }

    private static void SectionLabel(string text)
    {
        ImGui.SetCursorPosX(Px(8f));
        ImGui.PushStyleColor(ImGuiCol.Text, MutedText);
        ImGui.TextUnformatted(text.ToUpperInvariant());
        ImGui.PopStyleColor();
    }

    /// <summary>An incoming add: accept/decline inline. The accept/decline buttons submit before the row
    /// spacer: the first-submitted overlapping item wins clicks.</summary>
    private void DrawRequestRow(MessengerRequestDto request, float width, bool overlayActive)
    {
        var rowH = Px(64f);
        var tl = ImGui.GetCursorScreenPos();
        var br = tl + new Vector2(width, rowH);
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(tl + new Vector2(Px(4f), 0f), br - new Vector2(Px(4f), 0f),
            Col(new Vector4(1f, 1f, 1f, 0.03f)), Px(10f));

        var avatarR = Px(21f);
        var avatarC = new Vector2(tl.X + Px(12f) + avatarR, (tl.Y + br.Y) * 0.5f);
        DrawAvatar(dl, request.PeerAccountId, request.PeerName, request.PeerAvatar, false, avatarC, avatarR);

        var btnSize = Px(32f);
        var gap = Px(8f);
        var declineX = br.X - Px(12f) - btnSize;
        var acceptX = declineX - gap - btnSize;
        var btnY = (tl.Y + br.Y) * 0.5f - btnSize * 0.5f;

        ImGui.SetCursorScreenPos(new Vector2(acceptX, btnY));
        if (ImGui.InvisibleButton($"##msgrAcc_{request.ContactId}", new Vector2(btnSize, btnSize)) && !overlayActive)
        {
            var id = request.ContactId;
            RunHub(async () =>
            {
                await _hub.AcceptMessengerRequestAsync(id).ConfigureAwait(false);
                _store.RemoveRequest(id);
                await _sync.SyncAsync().ConfigureAwait(false);
            });
        }
        var accHover = !overlayActive && ImGui.IsItemHovered();
        dl.AddCircleFilled(new Vector2(acceptX + btnSize * 0.5f, btnY + btnSize * 0.5f), btnSize * 0.5f,
            Col(accHover ? UnreadGreen : new Vector4(UnreadGreen.X, UnreadGreen.Y, UnreadGreen.Z, 0.75f)));
        IconCentered(dl, FontAwesomeIcon.Check, Px(14f),
            new Vector2(acceptX + btnSize * 0.5f, btnY + btnSize * 0.5f), 0xFFFFFFFFu);

        ImGui.SetCursorScreenPos(new Vector2(declineX, btnY));
        if (ImGui.InvisibleButton($"##msgrDec_{request.ContactId}", new Vector2(btnSize, btnSize)) && !overlayActive)
        {
            var id = request.ContactId;
            RunHub(async () =>
            {
                await _hub.DeclineMessengerRequestAsync(id).ConfigureAwait(false);
                _store.RemoveRequest(id);
            });
        }
        var decHover = !overlayActive && ImGui.IsItemHovered();
        dl.AddCircleFilled(new Vector2(declineX + btnSize * 0.5f, btnY + btnSize * 0.5f), btnSize * 0.5f,
            Col(decHover ? DangerRed : new Vector4(1f, 1f, 1f, 0.10f)));
        IconCentered(dl, FontAwesomeIcon.Times, Px(14f),
            new Vector2(declineX + btnSize * 0.5f, btnY + btnSize * 0.5f), 0xFFFFFFFFu);

        var textX = avatarC.X + avatarR + Px(12f);
        dl.PushClipRect(new Vector2(textX, tl.Y), new Vector2(acceptX - Px(6f), br.Y), true);
        dl.AddText(new Vector2(textX, tl.Y + Px(12f)), Col(BodyText), request.PeerName);
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.86f,
            new Vector2(textX, tl.Y + Px(12f) + ImGui.GetFontSize() + Px(3f)),
            Col(MutedText), Loc.T("os.msgr_wants_to_chat"));
        dl.PopClipRect();

        ImGui.SetCursorScreenPos(tl);
        ImGui.Dummy(new Vector2(width, rowH));
        ImGui.Dummy(new Vector2(0f, Px(3f)));
    }

    /// <summary>An add this user sent that is still unanswered. Read-only apart from cancelling it; the peer
    /// name and avatar are the snapshot taken when the request went out.</summary>
    private void DrawOutgoingRequestRow(MessengerRequestDto request, float width, bool overlayActive)
    {
        var rowH = Px(64f);
        var tl = ImGui.GetCursorScreenPos();
        var br = tl + new Vector2(width, rowH);
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(tl + new Vector2(Px(4f), 0f), br - new Vector2(Px(4f), 0f),
            Col(new Vector4(1f, 1f, 1f, 0.03f)), Px(10f));

        var avatarR = Px(21f);
        var avatarC = new Vector2(tl.X + Px(12f) + avatarR, (tl.Y + br.Y) * 0.5f);
        DrawAvatar(dl, request.PeerAccountId, request.PeerName, request.PeerAvatar, false, avatarC, avatarR);
        // Unanswered, so the avatar sits behind a veil rather than reading as a live contact.
        dl.AddCircleFilled(avatarC, avatarR, 0x66101010u);
        IconCentered(dl, FontAwesomeIcon.Clock, Px(15f), avatarC, 0xCCFFFFFFu);

        var btnSize = Px(32f);
        var cancelX = br.X - Px(12f) - btnSize;
        var btnY = (tl.Y + br.Y) * 0.5f - btnSize * 0.5f;

        ImGui.SetCursorScreenPos(new Vector2(cancelX, btnY));
        if (ImGui.InvisibleButton($"##msgrCancelReq_{request.ContactId}", new Vector2(btnSize, btnSize))
            && !overlayActive)
        {
            var id = request.ContactId;
            RunHub(async () =>
            {
                await _hub.CancelMessengerRequestAsync(id).ConfigureAwait(false);
                _store.RemoveRequest(id);
            });
        }
        var cancelHover = !overlayActive && ImGui.IsItemHovered();
        if (cancelHover)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            ImGui.SetTooltip(Loc.T("os.msgr_cancel_request"));
        }
        dl.AddCircleFilled(new Vector2(cancelX + btnSize * 0.5f, btnY + btnSize * 0.5f), btnSize * 0.5f,
            Col(cancelHover ? DangerRed : new Vector4(1f, 1f, 1f, 0.10f)));
        IconCentered(dl, FontAwesomeIcon.Times, Px(14f),
            new Vector2(cancelX + btnSize * 0.5f, btnY + btnSize * 0.5f), 0xFFFFFFFFu);

        var textX = avatarC.X + avatarR + Px(12f);
        dl.PushClipRect(new Vector2(textX, tl.Y), new Vector2(cancelX - Px(6f), br.Y), true);
        dl.AddText(new Vector2(textX, tl.Y + Px(12f)), Col(MutedText), request.PeerName);
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.86f,
            new Vector2(textX, tl.Y + Px(12f) + ImGui.GetFontSize() + Px(3f)),
            Col(MutedText), Loc.T("os.msgr_awaiting_reply"));
        dl.PopClipRect();

        ImGui.SetCursorScreenPos(tl);
        ImGui.Dummy(new Vector2(width, rowH));
        ImGui.Dummy(new Vector2(0f, Px(3f)));
    }

    private void DrawChatRow(ChatRow row, bool overlayActive, Guid contentHit, RowContext ctx,
                             DepartAnim? depart, float visH)
    {
        var drawList = ImGui.GetWindowDrawList();
        var cursorStart = ImGui.GetCursorScreenPos();
        var rowHeight = Px(ChatRowHeight);
        var windowWidth = ImGui.GetContentRegionAvail().X;
        var rowMax = cursorStart + new Vector2(windowWidth, visH);
        var beingDragged = _dragActive && _dragKind == DragKind.Chat && _dragChatId == row.Id;
        var hosting = HostingFor(row);

        var isHovered = false;
        var markerClicked = false;
        Vector2 markerC = default;
        if (depart is null)
        {
            // The hosting marker's hit area submits before the row button so its click wins.
            if (hosting is not null)
            {
                var titleW = ImGui.CalcTextSize(row.Title).X;
                markerC = cursorStart + new Vector2(Px(80) + MathF.Min(titleW, windowWidth * 0.5f) + Px(16f), Px(12) + ImGui.GetFontSize() * 0.5f);
                ImGui.SetCursorScreenPos(markerC - new Vector2(Px(10f), Px(10f)));
                markerClicked = ImGui.InvisibleButton($"##msgrHost_{row.Id}", new Vector2(Px(20f), Px(20f)));
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                    ImGui.SetTooltip(Loc.T("os.msgr_hosting_tooltip"));
                }
                ImGui.SetCursorScreenPos(cursorStart);
            }

            ImGui.InvisibleButton($"##msgrChat_{row.Id}", new Vector2(windowWidth, visH));
            isHovered = ImGui.IsItemHovered() && !_dragActive;

            if (ImGui.IsItemActivated())
            {
                _pressChatId = row.Id;
            }
            if (ctx == RowContext.TopLevel && _pendingShare is null && !overlayActive
                && ImGui.IsItemActive() && _pressChatId == row.Id && !_dragActive
                && ImGui.GetMouseDragDelta(ImGuiMouseButton.Left).Length() > Px(DragStartThreshold))
            {
                StartChatDrag(row);
            }
            var clicked = false;
            if (ImGui.IsItemDeactivated() && _pressChatId == row.Id)
            {
                _pressChatId = Guid.Empty;
                clicked = !_dragActive && ImGui.IsItemHovered() && !overlayActive;
            }

            if (beingDragged)
            {
                _dragSourceRowCenter = cursorStart + new Vector2(Px(40f), visH * 0.5f);
            }

            if (ImGui.BeginPopupContextItem($"##msgrChatCtx_{row.Id}", ImGuiPopupFlags.MouseButtonRight))
            {
                ImGui.TextDisabled(row.Title);
                ImGui.Separator();
                DrawChatRowMenu(row, ctx, cursorStart + new Vector2(Px(40f), rowHeight * 0.5f));
                ImGui.EndPopup();
            }

            if (markerClicked && hosting is not null)
            {
                OpenHangoutPopup(hosting);
            }
            else if (clicked)
            {
                if (_pendingShare is not null)
                {
                    SendPendingShareTo(row);
                }
                else
                {
                    OpenChat(row.Id, row.Kind);
                    if (contentHit != Guid.Empty)
                    {
                        JumpToMessage(contentHit);
                    }
                }
            }
        }
        else
        {
            ImGui.Dummy(new Vector2(windowWidth, visH));
        }

        // Departing rows slide right and fade behind a shrinking clip; everything below draws through these.
        var slide = depart is null ? 0f : EaseInCubicF(depart.T) * windowWidth * 0.55f;
        var contentStart = cursorStart + new Vector2(slide, 0f);
        drawList.PushClipRect(cursorStart, new Vector2(rowMax.X, MathF.Max(rowMax.Y, cursorStart.Y + 1f)), true);

        if (isHovered)
        {
            drawList.AddRectFilled(cursorStart, rowMax, 0x20FFFFFF);
        }

        var avatarCenter = contentStart + new Vector2(Px(40), rowHeight * 0.5f);
        var avatarRadius = Px(25f);
        DrawAvatar(drawList, row.Id, row.Title, row.Avatar, row.Kind == MessengerChatKind.Group,
            avatarCenter, avatarRadius);
        drawList.AddCircle(avatarCenter, avatarRadius, 0xFFFFFFFF, 0, Px(2f));

        if (row.Unread > 0)
        {
            var badgeR = Px(9f);
            var badgeCenter = avatarCenter + new Vector2(avatarRadius - Px(6f), -avatarRadius + Px(6f));
            drawList.AddCircleFilled(badgeCenter, badgeR, UiColors.UnreadBadge);
            var label = row.Unread > 9 ? "9+" : row.Unread.ToString();
            var fsz = ImGui.GetFontSize() * 0.74f;
            var tsize = ImGui.CalcTextSize(label) * (fsz / ImGui.GetFontSize());
            drawList.AddText(ImGui.GetFont(), fsz, badgeCenter - tsize * 0.5f, 0xFFFFFFFF, label);
        }

        var textPos = contentStart + Px(80, 12);
        if (_listSearchActive && _listApplied.Length > 0
            && row.Title.Contains(_listApplied, StringComparison.OrdinalIgnoreCase))
        {
            DrawTitleHighlighted(drawList, textPos, row.Title, _listApplied);
        }
        else
        {
            drawList.AddText(textPos, 0xFFFFFFFF, row.Title);
        }
        var afterNameX = ImGui.CalcTextSize(row.Title).X + Px(6f);
        if (row.Kind == MessengerChatKind.Group)
        {
            ImGui.PushFont(AetherLove.UiHost.PluginInterface.UiBuilder.FontIcon);
            drawList.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.72f,
                textPos + new Vector2(afterNameX, Px(3f)), Col(MutedText), FontAwesomeIcon.Users.ToIconString());
            afterNameX += ImGui.CalcTextSize(FontAwesomeIcon.Users.ToIconString()).X * 0.72f + Px(5f);
            ImGui.PopFont();
        }
        if (row.Pinned)
        {
            ImGui.PushFont(AetherLove.UiHost.PluginInterface.UiBuilder.FontIcon);
            drawList.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.8f,
                textPos + new Vector2(afterNameX, Px(1f)),
                ThemeService.Current.AccentU32, FontAwesomeIcon.Thumbtack.ToIconString());
            ImGui.PopFont();
        }
        if (hosting is not null && depart is null)
        {
            var pulse = 0.55f + 0.45f * MathF.Sin((float)ImGui.GetTime() * 4f);
            IconCentered(drawList, FontAwesomeIcon.Bullhorn, Px(12f), markerC,
                ImGui.GetColorU32(UiColors.LiveGreen with { W = pulse }));
        }

        var timeAgo = row.LastAt is { } lastAt ? GetTimeAgo(lastAt.LocalDateTime) : string.Empty;
        if (timeAgo.Length > 0)
        {
            var timeSize = ImGui.CalcTextSize(timeAgo);
            drawList.AddText(
                contentStart + new Vector2(windowWidth - timeSize.X - Px(10), Px(12)),
                UiColors.TextMuted, timeAgo);
        }

        var previewText = row.RemovedByPeer
            ? Loc.T("os.msgr_removed_preview")
            : PreviewLine(row);
        if (!string.IsNullOrEmpty(previewText) && rowMax.Y > cursorStart.Y + Px(38f))
        {
            var previewCol = row.RemovedByPeer
                ? ImGui.GetColorU32(WarnAmber)
                : row.Unread > 0 ? 0xFFDDDDDDu : 0xFF999999u;
            drawList.PushClipRect(contentStart + Px(80, 38), new Vector2(rowMax.X - Px(8f), rowMax.Y), true);
            drawList.AddText(contentStart + Px(80, 38), previewCol, previewText);
            drawList.PopClipRect();
        }

        if (beingDragged)
        {
            drawList.AddRectFilled(cursorStart, rowMax, DragDim);
        }

        drawList.PopClipRect();

        drawList.AddLine(
            cursorStart + new Vector2(Px(80), visH),
            cursorStart + new Vector2(windowWidth, visH),
            0xFF333333);

        ImGui.SetCursorScreenPos(cursorStart + new Vector2(0, visH));
    }

    private string PreviewLine(ChatRow row)
    {
        if (row.Preview is not { Length: > 0 } preview)
        {
            // No message has ever been sent here, versus one that exists but this device can't open.
            return row.HasMessages ? Loc.T("os.msgr_encrypted") : Loc.T("os.msgr_say_hello");
        }
        var body = preview switch
        {
            _ when VenueShare.TryParse(preview, out _) => Loc.T("chat.preview_venue"),
            _ when HangoutShare.TryParse(preview, out _) => Loc.T("chat.preview_hangout"),
            _ when NewsShare.TryParse(preview, out _) => Loc.T("chat.preview_news"),
            _ when CalendarEventShare.TryParse(preview, out _) => Loc.T("chat.preview_calevent"),
            _ when LocationShare.TryParse(preview, out _) => Loc.T("chat.preview_location"),
            _ => AetherLove.Emoji.ParsedMessage.Parse(preview).PlainText.Replace('\n', ' '),
        };
        return row.LastFromMe ? Loc.T("os.msgr_you_prefix") + body : body;
    }

    private static void DrawTitleHighlighted(ImDrawListPtr dl, Vector2 pos, string title, string query)
    {
        var idx = title.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            dl.AddText(pos, 0xFFFFFFFF, title);
            return;
        }
        var before = title[..idx];
        var match = title.Substring(idx, query.Length);
        var after = title[(idx + query.Length)..];
        var x = pos.X;
        if (before.Length > 0)
        {
            dl.AddText(new Vector2(x, pos.Y), 0xFFFFFFFF, before);
            x += ImGui.CalcTextSize(before).X;
        }
        dl.AddText(new Vector2(x, pos.Y), ImGui.GetColorU32(UiColors.Amber), match);
        x += ImGui.CalcTextSize(match).X;
        if (after.Length > 0)
        {
            dl.AddText(new Vector2(x, pos.Y), 0xFFFFFFFF, after);
        }
    }

    private static string GetTimeAgo(DateTime time)
    {
        var diff = DateTime.Now - time;
        if (diff.TotalMinutes < 60)
        {
            return Loc.T("chat.time_ago_minutes", Math.Max(0, (int)diff.TotalMinutes));
        }
        if (diff.TotalHours < 24)
        {
            return Loc.T("chat.time_ago_hours", (int)diff.TotalHours);
        }
        if (diff.TotalDays < 7)
        {
            return Loc.T("chat.time_ago_days", (int)diff.TotalDays);
        }
        return time.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
    }

    private void DrawChatRowMenu(ChatRow row, RowContext ctx, Vector2 rowAvatarCenter)
    {
        if (DrawIconMenuItem(row.Pinned ? FontAwesomeIcon.ThumbtackSlash : FontAwesomeIcon.Thumbtack,
                Loc.T(row.Pinned ? "os.msgr_unpin_chat" : "os.msgr_pin_chat")))
        {
            ImGui.CloseCurrentPopup();
            var pin = !row.Pinned;
            RunHub(async () =>
            {
                await _hub.SetMessengerChatPinAsync(row.Id, row.Kind, pin).ConfigureAwait(false);
                await _sync.SyncAsync().ConfigureAwait(false);
            });
        }
        DrawCategoryMenuItems(row.Id, ctx, rowAvatarCenter);
        ImGui.Separator();
        if (row.Kind == MessengerChatKind.Direct)
        {
            if (row.RemovedByPeer)
            {
                if (DrawIconMenuItem(FontAwesomeIcon.TrashAlt, Loc.T("os.msgr_delete_chat"), UiColors.MenuDanger))
                {
                    ImGui.CloseCurrentPopup();
                    OpenConfirm(
                        Loc.T("os.msgr_delete_chat"),
                        string.Format(CultureInfo.InvariantCulture, Loc.T("os.msgr_delete_chat_confirm"), row.Title),
                        () => RunHub(() => _hub.RemoveMessengerContactAsync(row.Id)));
                }
            }
            else if (DrawIconMenuItem(FontAwesomeIcon.UserMinus, Loc.T("os.msgr_remove_contact")))
            {
                ImGui.CloseCurrentPopup();
                OpenConfirm(
                    Loc.T("os.msgr_remove_contact"),
                    string.Format(CultureInfo.InvariantCulture, Loc.T("os.msgr_remove_confirm"), row.Title),
                    () => RunHub(() => _hub.RemoveMessengerContactAsync(row.Id)));
            }
            if (DrawIconMenuItem(FontAwesomeIcon.Ban, Loc.T("os.msgr_block")))
            {
                ImGui.CloseCurrentPopup();
                OpenBlockConfirm(row.Id, row.Title);
            }
            if (DrawIconMenuItem(FontAwesomeIcon.ExclamationTriangle, Loc.T("os.msgr_report"), UiColors.MenuReport))
            {
                ImGui.CloseCurrentPopup();
                OpenReport(row.Id, row.Title);
            }
        }
        else if (row.Group is { } group)
        {
            if (DrawIconMenuItem(FontAwesomeIcon.Users, Loc.T("os.msgr_group_info")))
            {
                ImGui.CloseCurrentPopup();
                OpenGroupInfo(group.GroupId);
            }
            if (DrawIconMenuItem(FontAwesomeIcon.SignOutAlt, Loc.T("os.msgr_leave_group"), UiColors.MenuDanger))
            {
                ImGui.CloseCurrentPopup();
                OpenConfirm(
                    Loc.T("os.msgr_leave_group"),
                    string.Format(CultureInfo.InvariantCulture, Loc.T("os.msgr_leave_confirm"), group.Name),
                    () => RunHub(() => _hub.LeaveMessengerGroupAsync(group.GroupId)));
            }
        }
    }

    private void DrawEmptyState(float width)
    {
        ImGui.Dummy(new Vector2(0f, Px(40f)));
        var dl = ImGui.GetWindowDrawList();
        var center = ImGui.GetCursorScreenPos() + new Vector2(width * 0.5f, Px(30f));
        IconCentered(dl, FontAwesomeIcon.CommentDots, Px(34f), center, Col(new Vector4(1f, 1f, 1f, 0.18f)));
        ImGui.Dummy(new Vector2(0f, Px(70f)));
        var hint = Loc.T("os.msgr_empty");
        using (UiFonts.H3?.Push())
        {
            var wrapW = width - Px(48f);
            var sz = ImGui.CalcTextSize(hint, wrapWidth: wrapW);
            ImGui.SetCursorPosX(MathF.Max(Px(24f), (width - sz.X) * 0.5f));
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + wrapW);
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), hint);
            ImGui.PopTextWrapPos();
        }

        if (_store.Sync?.MyCode is { Length: > 0 } code)
        {
            var theme = ThemeService.Current;
            var display = MessengerCodeDisplay(code);

            ImGui.Dummy(new Vector2(0f, Px(26f)));

            // "Your code" label, centred + muted.
            var yourCode = Loc.T("os.msgr_your_code");
            var lblSz = ImGui.CalcTextSize(yourCode);
            ImGui.SetCursorPosX(MathF.Max(Px(24f), (width - lblSz.X) * 0.5f));
            ImGui.TextColored(new Vector4(0.55f, 0.55f, 0.55f, 1f), yourCode);
            ImGui.Dummy(new Vector2(0f, Px(8f)));

            // Tappable code pill with a copy icon; a click copies the raw code.
            var codeSz = ImGui.CalcTextSize(display);
            var iconPx = ImGui.GetFontSize();
            var iconSz = IconDraw.Measure(FontAwesomeIcon.Copy, iconPx);
            var padH = Px(16f);
            var gap = Px(10f);
            var pillW = padH * 2f + codeSz.X + gap + iconSz.X;
            var pillH = MathF.Max(codeSz.Y, iconSz.Y) + Px(16f);
            ImGui.SetCursorPosX(MathF.Max(Px(24f), (width - pillW) * 0.5f));
            var pillTL = ImGui.GetCursorScreenPos();
            var copied = ImGui.InvisibleButton("##msgrYourCode", new Vector2(pillW, pillH));
            var pillHov = ImGui.IsItemHovered();
            dl.AddRectFilled(pillTL, pillTL + new Vector2(pillW, pillH),
                Col(new Vector4(1f, 1f, 1f, pillHov ? 0.10f : 0.06f)), Px(10f));
            dl.AddRect(pillTL, pillTL + new Vector2(pillW, pillH),
                Col(theme.Accent with { W = 0.40f }), Px(10f), ImDrawFlags.None, Px(1f));
            dl.AddText(new Vector2(pillTL.X + padH, pillTL.Y + (pillH - codeSz.Y) * 0.5f),
                Col(new Vector4(0.95f, 0.95f, 0.95f, 1f)), display);
            IconDraw.Add(dl, FontAwesomeIcon.Copy, iconPx,
                new Vector2(pillTL.X + padH + codeSz.X + gap, pillTL.Y + (pillH - iconSz.Y) * 0.5f),
                Col(theme.AccentLight));
            if (copied)
            {
                _caps.System.CopyToClipboard(code);
            }

            // Divider.
            ImGui.Dummy(new Vector2(0f, Px(18f)));
            var divLeft = ImGui.GetCursorScreenPos();
            var divPad = Px(44f);
            dl.AddLine(new Vector2(divLeft.X + divPad, divLeft.Y),
                new Vector2(divLeft.X + width - divPad, divLeft.Y), Col(new Vector4(1f, 1f, 1f, 0.10f)), 1f);
            ImGui.Dummy(new Vector2(0f, Px(18f)));

            // "Or you could..." caption above an accent "Add a friend" button.
            var caption = Loc.T("os.msgr_or_you_could");
            var capSz = ImGui.CalcTextSize(caption);
            ImGui.SetCursorPosX(MathF.Max(Px(24f), (width - capSz.X) * 0.5f));
            ImGui.TextColored(new Vector4(0.55f, 0.55f, 0.55f, 1f), caption);
            ImGui.Dummy(new Vector2(0f, Px(10f)));

            var btnLabel = Loc.T("os.msgr_add_friend");
            var btnTextSz = ImGui.CalcTextSize(btnLabel);
            var btnW = btnTextSz.X + Px(40f);
            var btnH = btnTextSz.Y + Px(16f);
            ImGui.SetCursorPosX(MathF.Max(Px(24f), (width - btnW) * 0.5f));
            var btnTL = ImGui.GetCursorScreenPos();
            var btnClicked = ImGui.InvisibleButton("##msgrAddFriendEmpty", new Vector2(btnW, btnH));
            dl.AddRectFilled(btnTL, btnTL + new Vector2(btnW, btnH),
                Col(ImGui.IsItemHovered() ? theme.ButtonHovered : theme.ButtonNormal), Px(10f));
            dl.AddText(new Vector2(btnTL.X + (btnW - btnTextSz.X) * 0.5f, btnTL.Y + (btnH - btnTextSz.Y) * 0.5f),
                Col(new Vector4(1f, 1f, 1f, 1f)), btnLabel);
            if (btnClicked)
            {
                OpenAddContact();
            }
        }
    }

    private static void DrawCenteredListHint(string message)
    {
        ImGui.Dummy(new Vector2(0f, Px(30f)));
        var width = ImGui.GetContentRegionAvail().X;
        using (UiFonts.H3?.Push())
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.6f, 1f));
            ImGui.PushTextWrapPos(width - Px(24f));
            var sz = ImGui.CalcTextSize(message, wrapWidth: width - Px(48f));
            ImGui.SetCursorPosX(MathF.Max(Px(24f), (width - sz.X) * 0.5f));
            ImGui.TextUnformatted(message);
            ImGui.PopTextWrapPos();
            ImGui.PopStyleColor();
        }
    }

    /// <summary>"Move to category" submenu plus "Remove from category"; list-view moves animate the row
    /// away, search-result moves apply instantly.</summary>
    private void DrawCategoryMenuItems(Guid chatId, RowContext ctx, Vector2 rowAvatarCenter)
    {
        var current = CategoryOf(chatId);
        var instant = ctx == RowContext.Search;
        var flightFrom = ctx == RowContext.TopLevel ? rowAvatarCenter : (Vector2?)null;
        var dl = ImGui.GetWindowDrawList();

        // The visible label is blank padding sized to DrawIconMenuItem's layout; icon and text are drawn at that helper's offsets.
        var style = ImGui.GetStyle();
        var moveLabel = Loc.T("chat.menu_move_to_category");
        var moveLabelSz = ImGui.CalcTextSize(moveLabel);
        var spaceW = MathF.Max(1f, ImGui.CalcTextSize(" ").X);
        var fontSize = ImGui.GetFontSize();
        var padCount = (int)MathF.Ceiling(MathF.Max(0f, Px(38f) - style.FramePadding.X + moveLabelSz.X) / spaceW);
        // Anchor on the pre-submit cursor: the item's own rect is unreliable once BeginMenu opens the child window.
        var itemPos = ImGui.GetCursorScreenPos();
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing,
            new Vector2(style.ItemSpacing.X, style.FramePadding.Y * 2f));
        var open = ImGui.BeginMenu($"{new string(' ', padCount)}##mvcat");
        ImGui.PopStyleVar();

        ImGui.PushFont(AetherLove.UiHost.PluginInterface.UiBuilder.FontIcon);
        var folderIcon = FontAwesomeIcon.FolderOpen.ToIconString();
        var folderSz = ImGui.CalcTextSize(folderIcon);
        dl.AddText(ImGui.GetFont(), fontSize,
            new Vector2(itemPos.X + Px(10f) + (Px(20f) - folderSz.X) * 0.5f,
                        itemPos.Y + (moveLabelSz.Y - folderSz.Y) * 0.5f),
            0xFFEEEEEE, folderIcon);
        ImGui.PopFont();
        dl.AddText(new Vector2(itemPos.X + Px(38f), itemPos.Y), 0xFFEEEEEE, moveLabel);

        if (open)
        {
            var subDl = ImGui.GetWindowDrawList();
            var textInset = Px(28f);
            var newLabel = $"{Loc.T("chat.category_new")}…";
            var rowW = textInset + ImGui.CalcTextSize(newLabel).X + Px(12f);
            foreach (var cat in _categories)
            {
                rowW = MathF.Max(rowW, textInset + ImGui.CalcTextSize(cat.Name).X + Px(12f));
            }

            foreach (var cat in _categories)
            {
                var isCurrent = current == cat.Id;
                var pos = ImGui.GetCursorScreenPos();
                var rowH = ImGui.GetFrameHeight();
                if (ImGui.Selectable($"##mv_{cat.Id}", isCurrent, ImGuiSelectableFlags.None, new Vector2(rowW, rowH))
                    && !isCurrent)
                {
                    RequestMove(chatId, cat.Id, instant, flightFrom);
                }
                var dotC = new Vector2(pos.X + Px(12f), pos.Y + rowH * 0.5f);
                subDl.AddCircleFilled(dotC, Px(5.5f), cat.Color);
                subDl.AddCircle(dotC, Px(5.5f), 0xFFFFFFFF, 0, Px(1.2f));
                var nameSz = ImGui.CalcTextSize(cat.Name);
                subDl.AddText(new Vector2(pos.X + textInset, pos.Y + (rowH - nameSz.Y) * 0.5f),
                    0xFFEEEEEE, cat.Name);
            }
            if (_categories.Count > 0)
            {
                ImGui.Separator();
            }
            {
                var pos = ImGui.GetCursorScreenPos();
                var rowH = ImGui.GetFrameHeight();
                if (ImGui.Selectable("##mvnew", false, ImGuiSelectableFlags.None, new Vector2(rowW, rowH)))
                {
                    OpenCategoryEditor(null, chatId, instant);
                }
                ImGui.PushFont(AetherLove.UiHost.PluginInterface.UiBuilder.FontIcon);
                var plus = FontAwesomeIcon.Plus.ToIconString();
                var plusFsz = ImGui.GetFontSize() * 0.8f;
                var plusSz = ImGui.CalcTextSize(plus) * 0.8f;
                subDl.AddText(ImGui.GetFont(), plusFsz,
                    new Vector2(pos.X + Px(12f) - plusSz.X * 0.5f, pos.Y + (rowH - plusSz.Y) * 0.5f),
                    0xFFBBBBBB, plus);
                ImGui.PopFont();
                var nlSz = ImGui.CalcTextSize(newLabel);
                subDl.AddText(new Vector2(pos.X + textInset, pos.Y + (rowH - nlSz.Y) * 0.5f),
                    0xFFEEEEEE, newLabel);
            }
            ImGui.EndMenu();
        }

        if (current is not null
            && DrawIconMenuItem(FontAwesomeIcon.FolderMinus, Loc.T("chat.menu_remove_from_category")))
        {
            ImGui.CloseCurrentPopup();
            RequestMove(chatId, Guid.Empty, instant);
        }
    }

    /// <summary>Moves a chat into a category (or out of one when <paramref name="targetCategoryId"/> is Empty),
    /// animating the row's departure unless <paramref name="instant"/> or reduce-motion is set.</summary>
    private void RequestMove(Guid chatId, Guid targetCategoryId, bool instant, Vector2? flightFrom = null)
    {
        if (CategoryOf(chatId) == (targetCategoryId == Guid.Empty ? null : (Guid?)targetCategoryId))
        {
            return;
        }
        if (instant || AccessibilityService.ReduceMotion)
        {
            SetCategory(chatId, targetCategoryId == Guid.Empty ? null : targetCategoryId);
            return;
        }

        _departing[chatId] = new DepartAnim { TargetCategoryId = targetCategoryId };

        if (flightFrom is { } from && targetCategoryId != Guid.Empty
            && _catAvatarCenters.TryGetValue(targetCategoryId, out var target))
        {
            _avatars.TryGetValue(chatId, out var av);
            _flights.Add(new Flight
            {
                From = from,
                TargetCategoryId = targetCategoryId,
                LastTarget = target,
                Avatar = av.Tex,
            });
        }
    }

    private void StartChatDrag(ChatRow row)
    {
        _dragKind = DragKind.Chat;
        _dragActive = true;
        _dragT = 0f;
        _dragChatId = row.Id;
        _dragRow = row;
        _dragCategory = null;
    }

    private void StartCategoryDrag(MsgrCategoryData cat)
    {
        _dragKind = DragKind.Category;
        _dragActive = true;
        _dragT = 0f;
        _dragCategory = cat;
        _dragRow = null;
        _dragChatId = Guid.Empty;
    }

    private void EndDrag()
    {
        _dragKind = DragKind.None;
        _dragActive = false;
        _dragRow = null;
        _dragCategory = null;
        _dragChatId = Guid.Empty;
        _reorderInsertIndex = -1;
        _hoverDropCategoryId = Guid.Empty;
        // A surviving press candidate would re-arm the drag on the next frame after an Escape cancel.
        _pressChatId = Guid.Empty;
        _pressCategoryId = Guid.Empty;
    }

    /// <summary>Finishes a drag on release: drop into the hovered category, apply a reorder, or spring back.
    /// Escape cancels.</summary>
    private void ResolveDragAndDrop()
    {
        if (!_dragActive)
        {
            return;
        }
        if (ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            if (_dragKind == DragKind.Chat)
            {
                SpawnSpringBack();
            }
            EndDrag();
            return;
        }
        if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            return;
        }

        // A search finishing mid-drag hides the category rows, leaving the hover/insert state one frame stale.
        if (_dragKind == DragKind.Chat && _dragRow is not null)
        {
            if (_hoverDropCategoryId != Guid.Empty && !_listSearchActive)
            {
                RequestMove(_dragChatId, _hoverDropCategoryId, instant: false, flightFrom: ImGui.GetMousePos());
            }
            else
            {
                SpawnSpringBack();
            }
        }
        else if (_dragKind == DragKind.Category && _dragCategory is not null && _reorderInsertIndex >= 0
                 && !_listSearchActive)
        {
            var curIdx = _categories.FindIndex(c => c.Id == _dragCategory.Id);
            if (curIdx >= 0)
            {
                var final = _reorderInsertIndex > curIdx ? _reorderInsertIndex - 1 : _reorderInsertIndex;
                var cat = _categories[curIdx];
                _categories.RemoveAt(curIdx);
                _categories.Insert(Math.Clamp(final, 0, _categories.Count), cat);
                SaveCategories();
            }
        }
        EndDrag();
    }

    private void SpawnSpringBack()
    {
        if (AccessibilityService.ReduceMotion || _dragRow is null)
        {
            return;
        }
        _avatars.TryGetValue(_dragChatId, out var av);
        _springBack = new SpringBack { From = ImGui.GetMousePos(), Avatar = av.Tex };
    }

    private void UpdateCategoryAnimations(float dt)
    {
        dt = Math.Clamp(dt, 0f, 0.1f);

        if (_dragActive && _dragT < 1f)
        {
            _dragT = MathF.Min(1f, _dragT + dt / PickupDuration);
        }

        foreach (var (chatId, anim) in _departing.ToList())
        {
            anim.T += dt / DepartDuration;
            if (anim.T >= 1f)
            {
                _departing.Remove(chatId);
                SetCategory(chatId, anim.TargetCategoryId == Guid.Empty ? null : anim.TargetCategoryId);
            }
        }

        for (var i = _flights.Count - 1; i >= 0; i--)
        {
            var f = _flights[i];
            f.T += dt / FlightDuration;
            if (_catAvatarCenters.TryGetValue(f.TargetCategoryId, out var live))
            {
                f.LastTarget = live;
            }
            if (f.T >= 1f)
            {
                _catPulse[f.TargetCategoryId] = 0f;
                SpawnBurst(f.LastTarget, f.TargetCategoryId);
                _flights.RemoveAt(i);
            }
        }

        foreach (var catId in _catPulse.Keys.ToList())
        {
            var v = _catPulse[catId] + dt / PulseDuration;
            if (v >= 1f)
            {
                _catPulse.Remove(catId);
            }
            else
            {
                _catPulse[catId] = v;
            }
        }

        for (var i = _bursts.Count - 1; i >= 0; i--)
        {
            var b = _bursts[i];
            b.T += dt / PulseDuration;
            b.Pos += b.Vel * dt;
            if (b.T >= 1f)
            {
                _bursts.RemoveAt(i);
            }
            else
            {
                _bursts[i] = b;
            }
        }

        if (_springBack is { } sb)
        {
            sb.T += dt / SpringBackDuration;
            if (sb.T >= 1f)
            {
                _springBack = null;
            }
        }
    }

    private void SpawnBurst(Vector2 center, Guid categoryId)
    {
        var color = _categories.FirstOrDefault(c => c.Id == categoryId)?.Color ?? ThemeService.Current.AccentU32;
        const int Count = 10;
        for (var i = 0; i < Count; i++)
        {
            var angle = i * (MathF.Tau / Count);
            var speed = Px(46f) * (0.7f + 0.3f * (i % 3) / 2f);
            _bursts.Add(new Burst
            {
                Pos = center,
                Vel = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed,
                Color = color,
            });
        }
    }

    /// <summary>Applies in-flight moves immediately and clears transient animation/drag state; called on hide.</summary>
    private void FinalizeCategoryAnimations()
    {
        foreach (var (chatId, anim) in _departing)
        {
            SetCategory(chatId, anim.TargetCategoryId == Guid.Empty ? null : anim.TargetCategoryId);
        }
        _departing.Clear();
        _flights.Clear();
        _bursts.Clear();
        _catPulse.Clear();
        _springBack = null;
        EndDrag();
        _catEditorOpen = false;
        _catEditorPendingMoveChat = Guid.Empty;
        _catDeleteId = Guid.Empty;
    }

    /// <summary>Drag ghost, fly-to-category avatars, arrival bursts, and the failed-drop spring-back.</summary>
    private void DrawDragOverlays(Vector2 winPos, Vector2 winSize)
    {
        var fg = ImGui.GetForegroundDrawList();
        fg.PushClipRect(winPos, winPos + winSize, true);

        foreach (var f in _flights)
        {
            var t = EaseInCubicF(f.T);
            var pos = Vector2.Lerp(f.From, f.LastTarget, t);
            pos.Y -= MathF.Sin(MathF.PI * f.T) * Px(28f);
            var r = Px(22f) * (1f - 0.7f * t);
            var alpha = 1f - 0.55f * t;
            DrawGhostAvatar(fg, pos, r, alpha, f.Avatar);
        }

        foreach (var b in _bursts)
        {
            fg.AddCircleFilled(b.Pos, Px(3f) * (1f - b.T), WithAlpha(b.Color, 1f - b.T));
        }

        if (_springBack is { } sb)
        {
            var t = EaseOutCubicF(sb.T);
            var pos = Vector2.Lerp(sb.From, _dragSourceRowCenter, t);
            DrawGhostAvatar(fg, pos, Px(20f), 0.9f - 0.5f * t, sb.Avatar);
        }

        if (_dragActive)
        {
            DrawDragGhost(fg);
        }

        fg.PopClipRect();
    }

    private static void DrawGhostAvatar(ImDrawListPtr dl, Vector2 center, float radius, float alpha,
                                        ISharedImmediateTexture? avatar)
    {
        var wrap = avatar?.GetWrapOrDefault();
        var tint = WithAlpha(0xFFFFFFFFu, alpha);
        if (wrap != null)
        {
            dl.AddImageRounded(wrap.Handle, center - new Vector2(radius, radius), center + new Vector2(radius, radius),
                Vector2.Zero, Vector2.One, tint, radius, ImDrawFlags.RoundCornersAll);
        }
        else
        {
            dl.AddCircleFilled(center, radius, WithAlpha(UiColors.AvatarFallback, alpha));
        }
        dl.AddCircle(center, radius, tint, 0, Px(1.5f));
    }

    private void DrawDragGhost(ImDrawListPtr fg)
    {
        var t = ThemeService.Current;
        var scale = 0.9f + 0.1f * EaseOutCubicF(_dragT);
        var mouse = ImGui.GetMousePos();
        var pillSize = new Vector2(Px(180f), Px(44f)) * scale;
        var pillMin = mouse + Px(14f, 10f);
        var pillMax = pillMin + pillSize;
        var accent = _dragKind == DragKind.Category && _dragCategory is not null
            ? _dragCategory.Color
            : t.AccentU32;

        // Soft layered shadow behind the pill.
        for (var i = 3; i >= 1; i--)
        {
            fg.AddRectFilled(pillMin + new Vector2(0f, Px(2f) * i), pillMax + new Vector2(0f, Px(2f) * i),
                WithAlpha(0xFF000000u, 0.10f), Px(12f));
        }
        fg.AddRectFilled(pillMin, pillMax, GhostPillBg, Px(12f));
        fg.AddRect(pillMin, pillMax, WithAlpha(accent, 0.95f), Px(12f), ImDrawFlags.None, Px(1.5f));

        var avatarC = new Vector2(pillMin.X + pillSize.Y * 0.5f, (pillMin.Y + pillMax.Y) * 0.5f);
        var avatarR = pillSize.Y * 0.34f;
        string label;
        if (_dragKind == DragKind.Chat && _dragRow is not null)
        {
            _avatars.TryGetValue(_dragChatId, out var av);
            DrawGhostAvatar(fg, avatarC, avatarR, 1f, av.Tex);
            label = _dragRow.Title;
        }
        else if (_dragCategory is not null)
        {
            fg.AddCircleFilled(avatarC, avatarR, _dragCategory.Color);
            fg.AddCircle(avatarC, avatarR, 0xFFFFFFFF, 0, Px(1.5f));
            var letter = CategoryLetter(_dragCategory.Name);
            var lsz = ImGui.CalcTextSize(letter);
            fg.AddText(avatarC - lsz * 0.5f, 0xFFFFFFFF, letter);
            label = _dragCategory.Name;
        }
        else
        {
            return;
        }

        var textX = avatarC.X + avatarR + Px(8f);
        var textMaxW = pillMax.X - Px(10f) - textX;
        fg.PushClipRect(new Vector2(textX, pillMin.Y), new Vector2(textX + textMaxW, pillMax.Y), true);
        fg.AddText(new Vector2(textX, (pillMin.Y + pillMax.Y) * 0.5f - ImGui.GetTextLineHeight() * 0.5f),
            0xFFFFFFFF, label);
        fg.PopClipRect();
    }
}
