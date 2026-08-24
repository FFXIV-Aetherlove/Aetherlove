using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AetherLove;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Services.Market;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Widgets = AetherLove.Widgets;

namespace AetherOS.Apps.Market;

internal enum ItemListKind
{
    Watchlist,
    Selection,
    NewPatch,
    Treasury,
    RareFinds,
}

/// <summary>One list screen for every item collection: the watchlist, custom selections, and the
/// showcase lists. Rows carry live min prices and sale velocity from a single batched lookup.</summary>
internal sealed class ItemListScreen
{
    private const float PadX = 16f;

    private readonly record struct Row(
        uint Id, string Name, ushort Icon, byte Rarity, long MinPrice, double Velocity, int? WorldId);

    /// <summary>World names by id, resolved on the draw thread and kept: the list asks for the same handful
    /// of worlds on every frame, and the sheet lookup is not free.</summary>
    private static readonly Dictionary<int, string> WorldNames = [];

    private readonly MarketDataService _data;
    private readonly MarketItemIndex _index;
    private readonly MarketUserStore _store;
    private readonly Action _back;
    private readonly Action<uint> _openItem;
    private readonly EntranceAnimation _entrance = new();

    private enum SortMode
    {
        Price,
        Server,
        Name,
    }

    private ItemListKind _kind;
    private Guid _selectionId;
    private volatile IReadOnlyList<Row>? _rows;
    private volatile bool _loading;
    private int _generation;
    private bool _confirmDelete;
    private SortMode _sort = SortMode.Price;
    private IReadOnlyList<Row>? _sortedCache;
    private IReadOnlyList<Row>? _sortedSource;
    private SortMode _sortedBy;

    /// <summary>The item a right-click menu or its add-to-list follow-up is about.</summary>
    private uint _menuItemId;
    private string _menuItemName = "";
    private bool _showNewList;
    private float _newListPanelH;
    private string _newListName = "";

    /// <summary>The world a pressed teleport chip is waiting to be confirmed for, null when nothing is
    /// pending. Held rather than acted on immediately: moving somebody's character is not an undo-able
    /// click.</summary>
    private string? _confirmTravelWorld;
    private float _confirmTravelPanelH;
    private bool _confirmTravelSkipNext;
    private float _confirmPanelH;
    private string _addQuery = "";

    public ItemListScreen(MarketDataService data, MarketItemIndex index, MarketUserStore store,
        Action back, Action<uint> openItem)
    {
        _data = data;
        _index = index;
        _store = store;
        _back = back;
        _openItem = openItem;
    }

    public void Open(ItemListKind kind, Guid? selectionId = null)
    {
        _kind = kind;
        _selectionId = selectionId ?? Guid.Empty;
        _rows = null;
        _confirmDelete = false;
        _addQuery = "";
        _sort = SortMode.Price;
        _showNewList = false;
        _newListName = "";
        _entrance.Arm();
        StartFetch();
    }

    public void OnReturn()
    {
        _entrance.Arm();
        StartFetch();
    }

    private string Title => _kind switch
    {
        ItemListKind.Watchlist => Loc.T("os.market_menu_watchlist"),
        ItemListKind.NewPatch => Loc.T("os.market_tile_new"),
        ItemListKind.Treasury => Loc.T("os.market_tile_treasury"),
        ItemListKind.RareFinds => Loc.T("os.market_tile_rare"),
        _ => _store.TryGetSelection(_selectionId, out var selection) ? selection.Name : "",
    };

    private string EmptyHint => _kind switch
    {
        ItemListKind.Watchlist => Loc.T("os.market_watchlist_empty"),
        ItemListKind.Selection => Loc.T("os.market_selection_empty"),
        _ => Loc.T("os.market_list_empty"),
    };

    private void StartFetch()
    {
        var generation = Interlocked.Increment(ref _generation);
        var kind = _kind;
        var selectionId = _selectionId;
        var detected = MarketScopes.DetectCurrent();
        _loading = true;
        _index.EnsureBuildStarted();

        _ = Task.Run(async () =>
        {
            try
            {
                var scopes = detected;
                if (scopes is null)
                {
                    return;
                }
                var waited = 0;
                while (!_index.Ready && waited < 60)
                {
                    await Task.Delay(250).ConfigureAwait(false);
                    waited++;
                }
                if (!_index.Ready || generation != _generation)
                {
                    return;
                }

                var rows = kind is ItemListKind.Treasury or ItemListKind.RareFinds
                    ? await BuildShowcaseAsync(kind, scopes.Value).ConfigureAwait(false)
                    : await BuildPlainAsync(kind, selectionId, scopes.Value).ConfigureAwait(false);
                if (generation == _generation)
                {
                    _rows = rows;
                }
            }
            catch (Exception ex)
            {
                UiHost.Log.Debug($"[Market] List fetch failed: {ex.Message}");
                if (generation == _generation)
                {
                    _rows = [];
                }
            }
            finally
            {
                if (generation == _generation)
                {
                    _loading = false;
                }
            }
        });
    }

    private async Task<IReadOnlyList<Row>> BuildPlainAsync(ItemListKind kind, Guid selectionId,
        (MarketScope World, MarketScope DataCenter, MarketScope Region) scopes)
    {
        IReadOnlyList<uint> ids = kind switch
        {
            ItemListKind.Watchlist => _store.Watchlist,
            ItemListKind.Selection => _store.TryGetSelection(selectionId, out var selection)
                ? [.. selection.ItemIds]
                : [],
            _ => _index.HighestIds(UniversalisClient.MaxIdsPerCall),
        };
        if (ids.Count == 0)
        {
            return [];
        }
        var agg = await _data.GetAggregatedAsync(scopes.DataCenter, ids, CancellationToken.None).ConfigureAwait(false);
        var rows = new List<Row>(ids.Count);
        foreach (var id in ids)
        {
            if (!_index.TryGet(id, out var entry))
            {
                continue;
            }
            agg.TryGetValue(id, out var result);
            rows.Add(new Row(id, entry.Name, entry.Icon, entry.Rarity,
                BestMinPrice(result, MarketScopeKind.DataCenter), Velocity(result, MarketScopeKind.DataCenter),
                CheapestWorldId(result, MarketScopeKind.DataCenter)));
        }
        if (kind == ItemListKind.NewPatch)
        {
            return [.. rows.OrderByDescending(r => r.MinPrice)];
        }
        return rows;
    }

    /// <summary>Client-side showcase approximation over the recently-active pool on the player's DC:
    /// Treasury ranks by price, Rare finds by low sale velocity among non-trivial prices.</summary>
    private async Task<IReadOnlyList<Row>> BuildShowcaseAsync(ItemListKind kind,
        (MarketScope World, MarketScope DataCenter, MarketScope Region) scopes)
    {
        var recent = await _data.GetRecentlyUpdatedAsync(scopes.DataCenter, CancellationToken.None).ConfigureAwait(false);
        recent ??= await _data.GetRecentlyUpdatedAsync(scopes.World, CancellationToken.None).ConfigureAwait(false);
        if (recent is null || recent.Length == 0)
        {
            return [];
        }
        var ids = new List<uint>();
        foreach (var raw in recent)
        {
            if (_index.TryGet((uint)raw, out _))
            {
                ids.Add((uint)raw);
            }
            if (ids.Count >= UniversalisClient.MaxIdsPerCall)
            {
                break;
            }
        }
        var agg = await _data.GetAggregatedAsync(scopes.DataCenter, ids, CancellationToken.None).ConfigureAwait(false);

        var rows = new List<Row>();
        foreach (var id in ids)
        {
            if (!agg.TryGetValue(id, out var result) || !_index.TryGet(id, out var entry))
            {
                continue;
            }
            // Treasury ranks by what actually SOLD recently; the row's price is that sale price.
            // Rare finds keeps ranking by the current cheapest listing.
            if (kind == ItemListKind.Treasury)
            {
                var (salePrice, soldAt) = BestRecentSale(result, MarketScopeKind.DataCenter);
                if (salePrice <= 0 || soldAt is null || DateTimeOffset.UtcNow - soldAt > TimeSpan.FromDays(7))
                {
                    continue;
                }
                rows.Add(new Row(id, entry.Name, entry.Icon, entry.Rarity, salePrice,
                    Velocity(result, MarketScopeKind.DataCenter),
                    CheapestWorldId(result, MarketScopeKind.DataCenter)));
                continue;
            }
            var price = BestMinPrice(result, MarketScopeKind.DataCenter);
            if (price <= 0)
            {
                continue;
            }
            rows.Add(new Row(id, entry.Name, entry.Icon, entry.Rarity, price,
                Velocity(result, MarketScopeKind.DataCenter),
                CheapestWorldId(result, MarketScopeKind.DataCenter)));
        }

        if (kind == ItemListKind.Treasury)
        {
            return [.. rows.OrderByDescending(r => r.MinPrice).Take(25)];
        }
        // Membership stays scarcity-based (lowest sale velocity qualifies), the pick then presents
        // expensive first.
        return [.. rows.Where(r => r.MinPrice >= 10_000)
            .OrderBy(r => r.Velocity).ThenByDescending(r => r.MinPrice)
            .Take(25)
            .OrderByDescending(r => r.MinPrice)];
    }

    private static (long Price, DateTimeOffset? When) BestRecentSale(AggregatedResult? result, MarketScopeKind kind)
    {
        var nq = result?.Nq.RecentPurchase?.At(kind);
        var hq = result?.Hq.RecentPurchase?.At(kind);
        var best = (nq?.Price ?? 0) >= (hq?.Price ?? 0) ? nq : hq;
        return best is { Price: > 0 } ? (best.Price, best.When) : (0, null);
    }

    private static long BestMinPrice(AggregatedResult? result, MarketScopeKind kind)
    {
        if (result is null)
        {
            return 0;
        }
        var nq = result.Nq.MinListing?.At(kind)?.Price ?? 0;
        if (nq > 0)
        {
            return nq;
        }
        return result.Hq.MinListing?.At(kind)?.Price ?? 0;
    }

    /// <summary>The world label rides a little smaller than the item name: it is an answer, not a heading.</summary>
    private const float WorldScale = 0.86f;

    /// <summary>One-tap world travel for the row's cheapest listing, offered only with a provider installed.
    /// Inert mid-flight, because a second request would abandon the first trip.</summary>
    private static (bool Hovered, bool Pressed) SubmitTeleportChip(
        uint itemId, AetherOS.Sdk.ITravelBridge travel, string world, Vector2 centre, float radius)
    {
        ImGui.SetCursorScreenPos(centre - new Vector2(radius, radius));
        var pressed = ImGui.InvisibleButton($"##marketTravel{itemId}", new Vector2(radius * 2f, radius * 2f));
        var busy = travel.IsBusy;
        var hovered = !busy && ImGui.IsItemHovered();
        if (hovered)
        {
            HandOnHover();
            ImGui.SetTooltip(Loc.T("os.market_travel_to", world, travel.ProviderName ?? string.Empty));
        }
        return (hovered, pressed && !busy);
    }

    private static void PaintTeleportChip(ImDrawListPtr dl, Vector2 centre, float radius, bool hovered, bool busy)
    {
        var t = ThemeService.Current;
        var alpha = busy ? 0.3f : 1f;
        dl.AddCircleFilled(centre, radius,
            ImGui.GetColorU32(t.Accent with { W = (hovered ? 0.34f : 0.16f) * alpha }));
        IconDraw.AddCentered(dl, FontAwesomeIcon.LocationArrow, radius * 0.95f, centre,
            ImGui.GetColorU32(t.AccentLight with { W = alpha }));
    }

    /// <summary>Which world the row's price is actually on, read off the SAME listing
    /// <see cref="BestMinPrice"/> picked so the world and the number can never describe different sales.</summary>
    private static int? CheapestWorldId(AggregatedResult? result, MarketScopeKind kind)
    {
        if (result is null)
        {
            return null;
        }
        var nq = result.Nq.MinListing?.At(kind);
        if (nq is { Price: > 0 })
        {
            return nq.WorldId;
        }
        return result.Hq.MinListing?.At(kind)?.WorldId;
    }

    /// <summary>The world's name, cached per id. Empty for an unknown id, which reads as "no world to show"
    /// everywhere it is used.</summary>
    private static string WorldName(int? worldId)
    {
        if (worldId is not > 0)
        {
            return string.Empty;
        }
        if (WorldNames.TryGetValue(worldId.Value, out var cached))
        {
            return cached;
        }
        var name = string.Empty;
        try
        {
            name = UiHost.DataManager.GetExcelSheet<Lumina.Excel.Sheets.World>()
                .GetRow((uint)worldId.Value).Name.ExtractText();
        }
        catch (Exception)
        {
            // An id the client's sheet does not know is simply not shown.
        }
        WorldNames[worldId.Value] = name;
        return name;
    }

    private static double Velocity(AggregatedResult? result, MarketScopeKind kind) =>
        (result?.Nq.DailySaleVelocity?.At(kind)?.Quantity ?? 0)
        + (result?.Hq.DailySaleVelocity?.At(kind)?.Quantity ?? 0);

    public void Draw(OsAppContext ctx)
    {
        var winW = ImGui.GetContentRegionAvail().X;

        if (MarketHeader.Draw(Title, out var rowTop, out var pillH))
        {
            _back();
            return;
        }
        if (_kind == ItemListKind.Selection)
        {
            DrawDeleteButton(winW, rowTop, pillH);
            DrawAddSearch(winW);
        }
        ImGui.Spacing();

        var rows = _rows;
        if (rows is null || rows.Count == 0)
        {
            if (rows is null)
            {
                Widgets.LoadingIndicator.Draw(Loc.T("os.market_loading"));
            }
            else
            {
                ImGui.Dummy(new Vector2(0f, Px(24f)));
                ImGui.SetCursorPosX(Px(PadX));
                ImGui.PushTextWrapPos(winW - Px(PadX));
                ImGui.TextColored(UiColors.Hint, _loading ? Loc.T("os.market_loading") : EmptyHint);
                ImGui.PopTextWrapPos();
            }
            // The delete confirm must still draw on an empty list: the header's trash is how an empty
            // list dies.
            DrawDeleteConfirm(ctx);
            DrawNewListOverlay();
            return;
        }

        if (_kind is ItemListKind.Watchlist or ItemListKind.Selection)
        {
            DrawSortBar(winW);
            rows = Sorted(rows);
        }

        _entrance.BeginFrame();
        PushScrollbarStyle();
        using (var scroll = ImRaii.Child("##marketListScroll", ImGui.GetContentRegionAvail(), false))
        {
            if (scroll.Success)
            {
                if (_kind is ItemListKind.Watchlist or ItemListKind.Selection)
                {
                    ImGui.SetCursorPosX(Px(PadX));
                    ImGui.TextColored(UiColors.Hint, Loc.T("os.market_ctx_hint"));
                    ImGui.Dummy(new Vector2(0f, Px(6f)));
                }
                foreach (var row in rows)
                {
                    DrawRow(ctx, row, ImGui.GetWindowSize().X);
                }
                ImGui.Dummy(new Vector2(0f, Px(12f)));
                DrawItemMenu();
            }
        }
        PopScrollbarStyle();
        _entrance.EndFrame();

        DrawDeleteConfirm(ctx);
        DrawTravelConfirm(ctx);
        DrawNewListOverlay();
    }

    private void DrawDeleteButton(float winW, float rowTop, float pillH)
    {
        var restore = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        var size = Px(26f);
        var winPos = ImGui.GetWindowPos();
        var tl = new Vector2(winPos.X + winW - Px(PadX) - size, rowTop + (pillH - size) * 0.5f);
        ImGui.SetCursorScreenPos(tl);
        if (ImGui.InvisibleButton("##marketDeleteList", new Vector2(size, size)))
        {
            _confirmDelete = true;
        }
        HandOnHover();
        var hovered = ImGui.IsItemHovered();
        IconDraw.AddCentered(dl, FontAwesomeIcon.Trash, Px(12f), tl + new Vector2(size, size) * 0.5f,
            ImGui.GetColorU32(hovered ? UiColors.Danger : UiColors.Hint));
        if (hovered)
        {
            ImGui.SetTooltip(Loc.T("os.market_delete_list"));
        }
        ImGui.SetCursorScreenPos(restore);
    }

    private void DrawAddSearch(float winW)
    {
        ImGui.Dummy(new Vector2(0f, Px(2f)));
        ImGui.SetCursorPosX(Px(PadX));
        ImGui.SetNextItemWidth(winW - Px(PadX) * 2f);
        ImGui.InputTextWithHint("##marketListAddSearch", Loc.T("os.market_add_search_hint"), ref _addQuery, 64);
        var query = _addQuery.Trim();
        if (query.Length == 0 || !_index.Ready)
        {
            return;
        }

        _store.TryGetSelection(_selectionId, out var selection);
        foreach (var entry in _index.Search(query, 6))
        {
            var contains = selection?.ItemIds.Contains(entry.Id) == true;
            var rowH = Px(34f);
            ImGui.SetCursorPosX(Px(PadX));
            var clicked = ImGui.InvisibleButton($"##marketAdd{entry.Id}", new Vector2(winW - Px(PadX) * 2f, rowH));
            HandOnHover();
            var hovered = ImGui.IsItemHovered();
            var dl = ImGui.GetWindowDrawList();
            var tl = ImGui.GetItemRectMin();
            if (hovered)
            {
                dl.AddRectFilled(tl, tl + new Vector2(winW - Px(PadX) * 2f, rowH),
                    ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.05f)), Px(8f));
            }

            var iconSize = Px(22f);
            var iconTl = new Vector2(tl.X + Px(6f), tl.Y + (rowH - iconSize) * 0.5f);
            if (MarketItemIcons.Get(entry.Icon) is { } handle)
            {
                dl.AddImageRounded(handle, iconTl, iconTl + new Vector2(iconSize, iconSize),
                    Vector2.Zero, Vector2.One, ImGui.GetColorU32(Vector4.One), Px(4f));
            }
            var name = TruncateToWidth(entry.Name, winW - Px(PadX) * 2f - Px(70f));
            var nameSz = ImGui.CalcTextSize(name);
            dl.AddText(new Vector2(iconTl.X + iconSize + Px(8f), tl.Y + (rowH - nameSz.Y) * 0.5f),
                ImGui.GetColorU32(SearchScreen.RarityColor(entry.Rarity)), name);
            IconDraw.AddCentered(dl, contains ? FontAwesomeIcon.CheckCircle : FontAwesomeIcon.PlusCircle, Px(13f),
                new Vector2(tl.X + winW - Px(PadX) * 2f - Px(16f), tl.Y + rowH * 0.5f),
                ImGui.GetColorU32(contains ? new Vector4(0.55f, 0.85f, 0.55f, 1f) : ThemeService.Current.AccentLight));

            if (clicked)
            {
                _store.ToggleInSelection(_selectionId, entry.Id);
                StartFetch();
            }
        }
        ImGui.Spacing();
    }

    private void DrawDeleteConfirm(OsAppContext ctx)
    {
        if (!_confirmDelete)
        {
            return;
        }
        var winPos = ImGui.GetWindowPos();
        var winSize = ImGui.GetWindowSize();
        if (DrawPageOverlayPanel("marketDeleteList", winPos, winSize, ref _confirmPanelH, Px(150f), innerW =>
        {
            Widgets.ModalUi.Header(innerW, FontAwesomeIcon.Trash, Loc.T("os.market_delete_list"), ThemeService.Current.AccentLight);
            ImGui.PushTextWrapPos(innerW);
            ImGui.TextColored(UiColors.Body, Loc.T("os.market_delete_list_confirm"));
            ImGui.PopTextWrapPos();
            ImGui.Spacing();
            if (Widgets.ModalUi.Button($"{Loc.T("os.market_delete_list")}##confirm", innerW))
            {
                _store.DeleteSelection(_selectionId);
                _confirmDelete = false;
                _back();
            }
            ImGui.Spacing();
            if (Widgets.ModalUi.Button($"{Loc.T("common.cancel")}##marketDelCancel", innerW))
            {
                _confirmDelete = false;
            }
        }))
        {
            _confirmDelete = false;
        }
    }

    /// <summary>The teleport confirmation, with the box that retires it. Modelled on the delete confirm
    /// beside it: an in-page overlay panel, never a full-viewport modal.</summary>
    private void DrawTravelConfirm(OsAppContext ctx)
    {
        if (_confirmTravelWorld is not { Length: > 0 } world)
        {
            return;
        }
        var travel = ctx.Capabilities.Travel;
        var provider = travel.ProviderName ?? string.Empty;
        var winPos = ImGui.GetWindowPos();
        var winSize = ImGui.GetWindowSize();
        if (DrawPageOverlayPanel("marketTravelConfirm", winPos, winSize, ref _confirmTravelPanelH, Px(180f), innerW =>
        {
            Widgets.ModalUi.Header(innerW, FontAwesomeIcon.LocationArrow,
                Loc.T("os.market_travel_confirm_title"), ThemeService.Current.AccentLight);
            ImGui.PushTextWrapPos(innerW);
            ImGui.TextColored(UiColors.Body, Loc.T("os.market_travel_confirm_body", world, provider));
            ImGui.PopTextWrapPos();
            ImGui.Spacing();

            ImGui.Checkbox(Loc.T("common.close_plugin_dont_ask"), ref _confirmTravelSkipNext);
            HandOnHover();
            ImGui.Spacing();

            using (ImRaii.Disabled(travel.IsBusy))
            {
                if (Widgets.ModalUi.Button($"{Loc.T("os.market_travel_confirm_go")}##marketTravelGo", innerW))
                {
                    // The preference is only kept once the travel is actually asked for: ticking the box and
                    // then cancelling means "not this time", not "never ask me again".
                    if (_confirmTravelSkipNext)
                    {
                        _store.ConfirmTravel = false;
                    }
                    travel.GoToWorld(world);
                    _confirmTravelWorld = null;
                }
            }
            ImGui.Spacing();
            if (Widgets.ModalUi.Button($"{Loc.T("common.cancel")}##marketTravelCancel", innerW))
            {
                _confirmTravelWorld = null;
            }
        }))
        {
            _confirmTravelWorld = null;
        }
    }

    /// <summary>Rows sorted by the picked mode, recomputed only when the source list or the mode changes.
    /// Unknown worlds and missing prices sink to the bottom of their orderings.</summary>
    private IReadOnlyList<Row> Sorted(IReadOnlyList<Row> rows)
    {
        if (ReferenceEquals(_sortedSource, rows) && _sortedBy == _sort && _sortedCache is not null)
        {
            return _sortedCache;
        }
        _sortedSource = rows;
        _sortedBy = _sort;
        _sortedCache = _sort switch
        {
            SortMode.Server => [.. rows
                .OrderBy(r => WorldName(r.WorldId).Length == 0 ? 1 : 0)
                .ThenBy(r => WorldName(r.WorldId), StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)],
            SortMode.Name => [.. rows.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)],
            _ => [.. rows.OrderByDescending(r => r.MinPrice)],
        };
        return _sortedCache;
    }

    /// <summary>The sort picker: a small sort glyph and three standalone chips, sized to their labels and
    /// spaced apart rather than fused into one bar. The active chip carries the accent.</summary>
    private void DrawSortBar(float winW)
    {
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var chipH = Px(24f);
        ImGui.Dummy(new Vector2(0f, Px(2f)));
        ImGui.SetCursorPosX(Px(PadX));
        var origin = ImGui.GetCursorScreenPos();

        var iconSize = Px(12f);
        IconDraw.AddCentered(dl, FontAwesomeIcon.SortAmountDown, iconSize,
            new Vector2(origin.X + iconSize * 0.5f, origin.Y + chipH * 0.5f), ImGui.GetColorU32(UiColors.Hint));
        var x = origin.X + iconSize + Px(10f);

        string[] labels = [Loc.T("os.market_sort_price"), Loc.T("os.market_sort_server"), Loc.T("os.market_sort_name")];
        for (var i = 0; i < 3; i++)
        {
            var labelSz = ImGui.CalcTextSize(labels[i]);
            var chipW = labelSz.X + Px(24f);
            ImGui.SetCursorScreenPos(new Vector2(x, origin.Y));
            var clicked = ImGui.InvisibleButton($"##marketSort{i}", new Vector2(chipW, chipH));
            HandOnHover();
            var hovered = ImGui.IsItemHovered();
            var selected = (int)_sort == i;
            var tl = ImGui.GetItemRectMin();
            var fill = selected
                ? t.Accent with { W = 0.55f }
                : new Vector4(1f, 1f, 1f, hovered ? 0.11f : 0.05f);
            dl.AddRectFilled(tl, tl + new Vector2(chipW, chipH), ImGui.GetColorU32(fill), chipH * 0.5f);
            if (selected)
            {
                dl.AddRect(tl, tl + new Vector2(chipW, chipH),
                    ImGui.GetColorU32(t.AccentLight with { W = 0.35f }), chipH * 0.5f);
            }
            dl.AddText(new Vector2(tl.X + Px(12f), tl.Y + (chipH - labelSz.Y) * 0.5f),
                ImGui.GetColorU32(selected ? new Vector4(1f, 1f, 1f, 0.98f) : UiColors.Hint), labels[i]);
            if (clicked)
            {
                _sort = (SortMode)i;
            }
            x += chipW + Px(6f);
        }
        if (ImGui.IsMouseHoveringRect(origin, new Vector2(origin.X + iconSize + Px(4f), origin.Y + chipH)))
        {
            ImGui.SetTooltip(Loc.T("os.market_sort"));
        }
        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + chipH + Px(10f)));
    }

    private const string ItemMenuId = "##marketItemCtx";

    /// <summary>The right-click menu for a row: add the item to any list (or start a new one), copy its
    /// name, and on the user's own lists remove it. Styled like the hub's dropdown.</summary>
    private void DrawItemMenu()
    {
        ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(0.13f, 0.12f, 0.15f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Border, ThemeService.Current.Accent with { W = 0.5f });
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, Px(10f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(Px(4f), Px(4f)));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0f, Px(2f)));
        if (ImGui.BeginPopup(ItemMenuId))
        {
            var labels = new List<string> { Loc.T("os.market_ctx_copy"), Loc.T("os.market_ctx_new_list") };
            var targets = new List<MarketSelection>();
            foreach (var selection in _store.Selections)
            {
                if (_kind == ItemListKind.Selection && selection.Id == _selectionId)
                {
                    continue;
                }
                targets.Add(selection);
                labels.Add(selection.Name);
            }
            if (_kind == ItemListKind.Watchlist)
            {
                labels.Add(Loc.T("os.market_ctx_remove_watch"));
            }
            if (_kind == ItemListKind.Selection)
            {
                labels.Add(Loc.T("os.market_ctx_remove_list"));
            }
            var w = AppHeader.MenuWidth([.. labels]);
            var rowH = AppHeader.MenuRowHeight();

            foreach (var selection in targets)
            {
                var contains = selection.ItemIds.Contains(_menuItemId);
                if (AppHeader.MenuRow(contains ? FontAwesomeIcon.CheckSquare : FontAwesomeIcon.Square,
                        selection.Name, w, rowH))
                {
                    _store.ToggleInSelection(selection.Id, _menuItemId);
                    ImGui.CloseCurrentPopup();
                }
            }
            if (AppHeader.MenuRow(FontAwesomeIcon.Plus, Loc.T("os.market_ctx_new_list"), w, rowH))
            {
                _showNewList = true;
                _newListName = "";
                _newListPanelH = 0f;
                ImGui.CloseCurrentPopup();
            }
            ImGui.Separator();
            if (AppHeader.MenuRow(FontAwesomeIcon.Copy, Loc.T("os.market_ctx_copy"), w, rowH))
            {
                ImGui.SetClipboardText(_menuItemName);
                ImGui.CloseCurrentPopup();
            }
            if (_kind == ItemListKind.Watchlist
                && AppHeader.MenuRow(FontAwesomeIcon.Trash, Loc.T("os.market_ctx_remove_watch"), w, rowH))
            {
                RemoveShownRow(_menuItemId);
                _store.ToggleWatch(_menuItemId);
                ImGui.CloseCurrentPopup();
            }
            if (_kind == ItemListKind.Selection
                && AppHeader.MenuRow(FontAwesomeIcon.Trash, Loc.T("os.market_ctx_remove_list"), w, rowH))
            {
                RemoveShownRow(_menuItemId);
                _store.RemoveFromSelection(_selectionId, _menuItemId);
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
        ImGui.PopStyleVar(3);
        ImGui.PopStyleColor(2);
    }

    private void RemoveShownRow(uint itemId)
    {
        _rows = _rows?.Where(r => r.Id != itemId).ToArray() ?? [];
    }

    /// <summary>Names the list started from the right-click menu; the item the menu was about becomes its
    /// first entry. Mirror of the hub's overlay, staying on this screen.</summary>
    private void DrawNewListOverlay()
    {
        if (!_showNewList)
        {
            return;
        }
        var winPos = ImGui.GetWindowPos();
        var winSize = ImGui.GetWindowSize();
        if (DrawPageOverlayPanel("marketListNewList", winPos, winSize, ref _newListPanelH, Px(140f), innerW =>
        {
            Widgets.ModalUi.Header(innerW, FontAwesomeIcon.ListUl, Loc.T("os.market_new_list_title"),
                ThemeService.Current.AccentLight);
            ImGui.SetNextItemWidth(innerW);
            ImGui.InputTextWithHint("##marketListNewListName", Loc.T("os.market_new_list_hint"), ref _newListName, 40);
            ImGui.Spacing();
            if (Widgets.ModalUi.Button($"{Loc.T("os.market_create")}##marketListCreate", innerW)
                && _newListName.Trim().Length > 0)
            {
                var selection = _store.CreateSelection(_newListName);
                _store.ToggleInSelection(selection.Id, _menuItemId);
                _newListName = "";
                _showNewList = false;
            }
        }))
        {
            _showNewList = false;
        }
    }

    private void DrawRow(OsAppContext ctx, in Row row, float winW)
    {
        var rowH = Px(46f);
        var world = WorldName(row.WorldId);
        var travel = world.Length > 0 && ctx.Capabilities.Travel.IsAvailable ? ctx.Capabilities.Travel : null;

        // The teleport chip is submitted BEFORE the row's own target, or the row would take its press:
        // first-submitted wins, and the whole row is one button.
        ImGui.SetCursorPosX(Px(6f));
        var rowTl = ImGui.GetCursorScreenPos();
        var chipR = Px(13f);
        var chipC = new Vector2(rowTl.X + (winW - Px(12f)) - Px(10f) - chipR, rowTl.Y + (rowH * 0.5f));
        // Input now, PAINT later: the row's hover fill is added to the same draw list further down and
        // would otherwise cover a chip drawn here.
        var chip = travel is null ? default : SubmitTeleportChip(row.Id, travel, world, chipC, chipR);
        // A pressed chip owns the press whatever it decides to do with it, so the row never also opens the
        // item underneath it.
        var chipTook = chip.Pressed && travel is not null;
        if (chipTook)
        {
            if (_store.ConfirmTravel)
            {
                _confirmTravelWorld = world;
                _confirmTravelPanelH = 0f;
                _confirmTravelSkipNext = false;
            }
            else
            {
                travel!.GoToWorld(world);
            }
        }

        ImGui.SetCursorScreenPos(rowTl);
        var clicked = ImGui.InvisibleButton($"##marketListRow{row.Id}", new Vector2(winW - Px(12f), rowH));
        HandOnHover();
        var hovered = ImGui.IsItemHovered();
        var rightClicked = ImGui.IsItemClicked(ImGuiMouseButton.Right);
        var dl = ImGui.GetWindowDrawList();
        var tl = ImGui.GetItemRectMin();
        var br = ImGui.GetItemRectMax();
        if (hovered)
        {
            dl.AddRectFilled(tl, br, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.06f)), Px(10f));
        }

        var iconSize = Px(32f);
        var iconTl = new Vector2(tl.X + Px(10f), tl.Y + (rowH - iconSize) * 0.5f);
        if (MarketItemIcons.Get(row.Icon) is { } handle)
        {
            dl.AddImageRounded(handle, iconTl, iconTl + new Vector2(iconSize, iconSize),
                Vector2.Zero, Vector2.One, ImGui.GetColorU32(Vector4.One), Px(6f));
        }

        var priceText = row.MinPrice > 0 ? $"{MarketFormat.Gil(row.MinPrice)} gil" : "-";
        var priceSz = ImGui.CalcTextSize(priceText);
        var rightX = tl.X + (winW - Px(12f)) - Px(10f) - (travel is not null ? (chipR * 2f) + Px(6f) : 0f);

        // The world the price is on, beside the price rather than a drill-down away: the whole point of a
        // cheapest-in-the-data-centre list is knowing where you are about to fly.
        var worldSz = Vector2.Zero;
        if (world.Length > 0)
        {
            worldSz = ImGui.CalcTextSize(world) * WorldScale;
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * WorldScale,
                new Vector2(rightX - priceSz.X - Px(10f) - worldSz.X, tl.Y + (rowH - worldSz.Y) * 0.5f),
                ImGui.GetColorU32(UiColors.WarningAccent), world);
        }

        var textX = iconTl.X + iconSize + Px(10f);
        var name = TruncateToWidth(row.Name,
            MathF.Max(Px(40f), rightX - priceSz.X - worldSz.X - Px(22f) - textX));
        dl.AddText(new Vector2(textX, tl.Y + Px(6f)), ImGui.GetColorU32(SearchScreen.RarityColor(row.Rarity)), name);
        if (row.Velocity > 0)
        {
            dl.AddText(new Vector2(textX, tl.Y + Px(6f) + ImGui.GetTextLineHeight() + Px(1f)),
                ImGui.GetColorU32(UiColors.Hint), Loc.T("os.market_row_velocity", row.Velocity.ToString(row.Velocity >= 10 ? "F0" : "F1")));
        }

        dl.AddText(new Vector2(rightX - priceSz.X, tl.Y + (rowH - priceSz.Y) * 0.5f),
            ImGui.GetColorU32(new Vector4(0.98f, 0.80f, 0.36f, 1f)), priceText);

        if (travel is not null)
        {
            PaintTeleportChip(dl, chipC, chipR, chip.Hovered, travel.IsBusy);
        }
        if (chipTook)
        {
            return;
        }

        if (rightClicked)
        {
            _menuItemId = row.Id;
            _menuItemName = row.Name;
            ImGui.OpenPopup(ItemMenuId);
            return;
        }
        if (clicked)
        {
            _openItem(row.Id);
        }
    }
}
