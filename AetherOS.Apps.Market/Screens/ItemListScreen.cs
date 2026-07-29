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

    private readonly record struct Row(uint Id, string Name, ushort Icon, byte Rarity, long MinPrice, double Velocity);

    private readonly MarketDataService _data;
    private readonly MarketItemIndex _index;
    private readonly MarketUserStore _store;
    private readonly Action _back;
    private readonly Action<uint> _openItem;
    private readonly EntranceAnimation _entrance = new();

    private ItemListKind _kind;
    private Guid _selectionId;
    private volatile IReadOnlyList<Row>? _rows;
    private volatile bool _loading;
    private int _generation;
    private bool _confirmDelete;
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
                BestMinPrice(result, MarketScopeKind.DataCenter), Velocity(result, MarketScopeKind.DataCenter)));
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
                rows.Add(new Row(id, entry.Name, entry.Icon, entry.Rarity, salePrice, Velocity(result, MarketScopeKind.DataCenter)));
                continue;
            }
            var price = BestMinPrice(result, MarketScopeKind.DataCenter);
            if (price <= 0)
            {
                continue;
            }
            rows.Add(new Row(id, entry.Name, entry.Icon, entry.Rarity, price, Velocity(result, MarketScopeKind.DataCenter)));
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
        if (rows is null)
        {
            Widgets.LoadingIndicator.Draw(Loc.T("os.market_loading"));
            return;
        }
        if (rows.Count == 0)
        {
            ImGui.Dummy(new Vector2(0f, Px(24f)));
            ImGui.SetCursorPosX(Px(PadX));
            ImGui.PushTextWrapPos(winW - Px(PadX));
            ImGui.TextColored(UiColors.Hint, _loading ? Loc.T("os.market_loading") : EmptyHint);
            ImGui.PopTextWrapPos();
            return;
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
                    ImGui.TextColored(UiColors.Hint, Loc.T("os.market_remove_hint"));
                    ImGui.Spacing();
                }
                foreach (var row in rows)
                {
                    DrawRow(row, ImGui.GetWindowSize().X);
                }
                ImGui.Dummy(new Vector2(0f, Px(12f)));
            }
        }
        PopScrollbarStyle();
        _entrance.EndFrame();

        DrawDeleteConfirm(ctx);
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

    private void DrawRow(in Row row, float winW)
    {
        var rowH = Px(46f);
        ImGui.SetCursorPosX(Px(6f));
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
        var rightX = tl.X + (winW - Px(12f)) - Px(10f);

        var textX = iconTl.X + iconSize + Px(10f);
        var name = TruncateToWidth(row.Name, rightX - priceSz.X - Px(16f) - textX);
        dl.AddText(new Vector2(textX, tl.Y + Px(6f)), ImGui.GetColorU32(SearchScreen.RarityColor(row.Rarity)), name);
        if (row.Velocity > 0)
        {
            dl.AddText(new Vector2(textX, tl.Y + Px(6f) + ImGui.GetTextLineHeight() + Px(1f)),
                ImGui.GetColorU32(UiColors.Hint), Loc.T("os.market_row_velocity", row.Velocity.ToString(row.Velocity >= 10 ? "F0" : "F1")));
        }

        dl.AddText(new Vector2(rightX - priceSz.X, tl.Y + (rowH - priceSz.Y) * 0.5f),
            ImGui.GetColorU32(new Vector4(0.98f, 0.80f, 0.36f, 1f)), priceText);

        if (rightClicked && _kind is ItemListKind.Watchlist or ItemListKind.Selection)
        {
            var removedId = row.Id;
            if (_kind == ItemListKind.Watchlist)
            {
                _store.ToggleWatch(removedId);
            }
            else
            {
                _store.RemoveFromSelection(_selectionId, removedId);
            }
            _rows = _rows?.Where(r => r.Id != removedId).ToArray() ?? [];
            return;
        }
        if (clicked)
        {
            _openItem(row.Id);
        }
    }
}
