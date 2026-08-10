using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Shared.Store;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Store;

/// <summary>The full catalog browser. Categories live on the app's bottom bar, so this screen is only the
/// search, an honest control row (the live sort, and one removable pill per filter that is actually on) and
/// a two-column grid with infinite scroll and fixed-pitch culling so two hundred cards cost nothing. Nothing
/// hides behind a button that only says "Filters": if a filter is narrowing the results it is on screen with
/// its own way off.</summary>
internal sealed class BrowseScreen(
    StoreState state, StoreMediaCache media, Action<Guid> openDetail)
{
    internal sealed record Seed(
        Guid? CategoryId, string? Tag, string? SearchText, StoreSort Sort,
        bool OnSaleOnly = false);

    private const float PadX = 16f;
    private const float CardH = 168f;

    private readonly EntranceAnimation _entrance = new();
    private Guid? _categoryId;
    private Guid? _rootId;
    private string? _tag;
    private string _search = string.Empty;
    private string _appliedSearch = string.Empty;
    private double _searchEditStamp = -1.0;
    private StoreSort _sort = StoreSort.Featured;
    private bool _onSaleOnly;
    private bool _hideOwned;
    private int _minPrice;
    private int _maxPrice;
    private bool _filterSheetOpen;
    private bool _sortSheetOpen;
    private string? _pendingCategoryKey;

    /// <summary>The root the bottom bar should light up, so the bar and the grid never disagree.</summary>
    public Guid? RootCategoryId => _rootId;

    public void Open(Seed seed)
    {
        _categoryId = seed.CategoryId;
        _rootId = seed.CategoryId;
        _tag = seed.Tag;
        _search = seed.SearchText ?? string.Empty;
        _appliedSearch = _search;
        _sort = seed.Sort;
        _onSaleOnly = seed.OnSaleOnly;
        _hideOwned = false;
        _minPrice = 0;
        _maxPrice = 0;
        _pendingCategoryKey = null;
        OnShow();
    }

    /// <summary>A store.open intent: the category arrives as a KEY, resolved once the tree is here.</summary>
    public void OpenDeepLink(string categoryKey, string? searchSeed)
    {
        Open(new Seed(null, null, searchSeed, StoreSort.Featured));
        _pendingCategoryKey = categoryKey;
    }

    public void OnShow()
    {
        _entrance.Arm();
        state.RefreshFrontIfStale();
    }

    public void Draw(OsAppContext ctx)
    {
        _entrance.BeginFrame();
        var winW = ImGui.GetWindowSize().X;
        ResolvePendingCategory();

        DrawSearchField(winW);
        DrawControlRow(ctx, winW);

        // The debounce: edits settle for 0.35s before the query fires.
        if (_searchEditStamp >= 0.0 && ImGui.GetTime() - _searchEditStamp > 0.35)
        {
            _searchEditStamp = -1.0;
            _appliedSearch = _search.Trim();
        }
        state.Browse(CurrentFilter());

        DrawGrid(ctx, winW);
        if (_filterSheetOpen)
        {
            DrawFilterSheet(ctx, winW);
        }
        if (_sortSheetOpen)
        {
            DrawSortSheet(ctx, winW);
        }
        _entrance.EndFrame();
    }

    private StoreState.BrowseFilter CurrentFilter() => new(
        _categoryId, _tag,
        _appliedSearch.Length == 0 ? null : _appliedSearch,
        _minPrice > 0 ? _minPrice : null,
        _maxPrice > 0 ? _maxPrice : null,
        _onSaleOnly, _sort);

    private void ResolvePendingCategory()
    {
        if (_pendingCategoryKey is not { } key || state.Front is not { } front)
        {
            return;
        }
        _pendingCategoryKey = null;
        // Category keys aren't on the DTO; match by English name slug, which the seeded keys mirror.
        var hit = front.Categories.FirstOrDefault(c =>
            c.NameEnglish.Replace(" ", "-").Equals(key, StringComparison.OrdinalIgnoreCase)
            || c.NameEnglish.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (hit is not null)
        {
            _categoryId = hit.Id;
            _rootId = hit.ParentId ?? hit.Id;
        }
        else
        {
            // No such category: keep the seed as a search instead, so "crystals/fire" still lands well.
            _appliedSearch = _search = key;
        }
    }

    private void DrawSearchField(float winW)
    {
        ImGui.SetCursorPosX(Px(PadX));
        var fieldW = winW - Px(PadX) * 2f;
        using (ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, Px(15f)))
        using (ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(Px(30f), Px(7f))))
        using (ImRaii.PushColor(ImGuiCol.FrameBg, OsDrawShared.White(0.06f)))
        {
            ImGui.SetNextItemWidth(fieldW);
            if (ImGui.InputTextWithHint("##storeSearch", Loc.T("os.store_search_hint"), ref _search, 80))
            {
                _searchEditStamp = ImGui.GetTime();
            }
        }
        var rect = ImGui.GetItemRectMin();
        var dl = ImGui.GetWindowDrawList();
        IconDraw.AddCentered(dl, FontAwesomeIcon.Search, Px(11f),
            new Vector2(rect.X + Px(16f), rect.Y + (ImGui.GetItemRectSize().Y * 0.5f)),
            ImGui.GetColorU32(UiColors.Hint));
        if (_search.Length > 0)
        {
            var clearC = new Vector2(rect.X + fieldW - Px(16f), rect.Y + ImGui.GetItemRectSize().Y * 0.5f);
            ImGui.SetCursorScreenPos(clearC - new Vector2(Px(9f), Px(9f)));
            if (ImGui.InvisibleButton("##storeSearchClear", new Vector2(Px(18f), Px(18f))))
            {
                _search = string.Empty;
                _appliedSearch = string.Empty;
                _searchEditStamp = -1.0;
            }
            if (ImGui.IsItemHovered())
            {
                HandOnHover();
            }
            IconDraw.AddCentered(dl, FontAwesomeIcon.TimesCircle, Px(10f), clearC, ImGui.GetColorU32(UiColors.Hint));
        }
        ImGui.Dummy(new Vector2(0f, Px(6f)));
    }

    private bool HasAnyFilter() =>
        _onSaleOnly || _hideOwned || _minPrice > 0 || _maxPrice > 0 || _tag is not null
        || _appliedSearch.Length > 0;

    /// <summary>Takes every narrowing off at once, which is the only useful thing to offer when a search
    /// has produced nothing.</summary>
    private void ClearFilters()
    {
        _onSaleOnly = false;
        _hideOwned = false;
        _minPrice = 0;
        _maxPrice = 0;
        _tag = null;
        _search = string.Empty;
        _appliedSearch = string.Empty;
        _searchEditStamp = -1.0;
    }

    private string SortLabel() => Loc.T(_sort switch
    {
        StoreSort.Newest => "os.store_sort_newest",
        StoreSort.PriceAscending => "os.store_sort_price_up",
        StoreSort.PriceDescending => "os.store_sort_price_down",
        StoreSort.MostBought => "os.store_sort_popular",
        _ => "os.store_sort_featured",
    });

    /// <summary>The sort pill, the filters pill, and one removable pill per live filter, wrapped over as
    /// many rows as they need. The result count closes the row so the grid is never a mystery.</summary>
    private void DrawControlRow(OsAppContext ctx, float winW)
    {
        var padX = Px(PadX);
        var right = ImGui.GetWindowPos().X + winW - padX;
        var x = ImGui.GetWindowPos().X + padX;
        var y = ImGui.GetCursorScreenPos().Y;
        var lineH = Px(30f);

        void Wrap(float width)
        {
            if (x + width > right && x > ImGui.GetWindowPos().X + padX)
            {
                x = ImGui.GetWindowPos().X + padX;
                y += lineH;
            }
        }

        var sortLabel = $"{Loc.T("os.store_sort_prefix")} {SortLabel()}";
        Wrap(StoreUi.MeasurePill(sortLabel, hasIcon: true));
        if (StoreUi.Pill("##storeSort", new Vector2(x, y), sortLabel, _sort != StoreSort.Featured,
            icon: FontAwesomeIcon.SortAmountDown))
        {
            _sortSheetOpen = true;
        }
        x += StoreUi.LastPillWidth + Px(6f);

        var filterLabel = Loc.T("os.store_filters");
        Wrap(StoreUi.MeasurePill(filterLabel, hasIcon: true));
        if (StoreUi.Pill("##storeFilters", new Vector2(x, y), filterLabel, false,
            icon: FontAwesomeIcon.SlidersH))
        {
            _filterSheetOpen = true;
        }
        x += StoreUi.LastPillWidth + Px(6f);

        foreach (var (id, label, clear) in ActiveFilterPills())
        {
            var width = StoreChips.MeasureAt(label, ImGui.GetFontSize() * 0.82f).X + Px(9f) * 2f + Px(14f);
            Wrap(width);
            if (StoreUi.RemovablePill(id, new Vector2(x, y + Px(1f)), label))
            {
                clear();
            }
            x += StoreUi.LastPillWidth + Px(6f);
        }

        ImGui.SetCursorScreenPos(new Vector2(ImGui.GetWindowPos().X, y + lineH));
        ImGui.SetCursorPosX(padX);
        var total = state.BrowseTotal;
        ImGui.TextColored(StorePalette.Hint,
            total > 0 ? Loc.T("os.store_result_count", total) : string.Empty);
        ImGui.Dummy(new Vector2(0f, Px(4f)));
        _ = ctx;
    }

    /// <summary>One entry per filter that is actually narrowing the results, each with the action that
    /// takes it off again.</summary>
    private IEnumerable<(string Id, string Label, Action Clear)> ActiveFilterPills()
    {
        if (_onSaleOnly)
        {
            yield return ("##fpSale", Loc.T("os.store_filter_sale"), () => _onSaleOnly = false);
        }
        if (_hideOwned)
        {
            yield return ("##fpOwned", Loc.T("os.store_filter_hide_owned"), () => _hideOwned = false);
        }
        if (_minPrice > 0)
        {
            yield return ("##fpMin", Loc.T("os.store_filter_min_pill", _minPrice), () => _minPrice = 0);
        }
        if (_maxPrice > 0)
        {
            yield return ("##fpMax", Loc.T("os.store_filter_max_pill", _maxPrice), () => _maxPrice = 0);
        }
        if (_tag is { } tag)
        {
            yield return ("##fpTag", $"#{tag}", () => _tag = null);
        }
    }

    /// <summary>Picking a sort straight from a list, rather than clicking a pill five times to get back to
    /// where it started.</summary>
    private void DrawSortSheet(OsAppContext ctx, float winW)
    {
        var origin = ImGui.GetWindowPos() + new Vector2(0f, ImGui.GetScrollY());
        var avail = ImGui.GetWindowSize();
        ImGui.SetCursorScreenPos(origin);
        using var layer = ImRaii.Child("##storeSortLayer", avail, false,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoBackground);
        if (!layer)
        {
            return;
        }
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(origin, origin + avail, OsDrawShared.Black(0.62f));

        StoreSort[] options =
        [
            StoreSort.Featured, StoreSort.Newest, StoreSort.MostBought,
            StoreSort.PriceAscending, StoreSort.PriceDescending,
        ];
        var rowH = Px(38f);
        var panelW = avail.X - Px(60f);
        var panelH = Px(46f) + rowH * options.Length + Px(10f);
        var panelTl = origin + new Vector2((avail.X - panelW) * 0.5f, (avail.Y - panelH) * 0.5f);
        dl.AddRectFilled(panelTl, panelTl + new Vector2(panelW, panelH),
            ImGui.ColorConvertFloat4ToU32(StorePalette.Surface), Px(16f));
        dl.AddRect(panelTl, panelTl + new Vector2(panelW, panelH), StorePalette.BlueWithAlpha(0.3f), Px(16f),
            ImDrawFlags.RoundCornersAll, Px(1.2f));

        ImGui.SetCursorScreenPos(panelTl + new Vector2(Px(14f), Px(14f)));
        AetherLove.Widgets.ModalUi.Header(panelW - Px(28f), Loc.T("os.store_sort_title"), StorePalette.Blue);

        for (var i = 0; i < options.Length; i++)
        {
            var option = options[i];
            var rowTl = panelTl + new Vector2(Px(8f), Px(46f) + rowH * i);
            var rowSize = new Vector2(panelW - Px(16f), rowH);
            ImGui.SetCursorScreenPos(rowTl);
            var pressed = ImGui.InvisibleButton($"##sortOpt{i}", rowSize);
            var hovered = ImGui.IsItemHovered();
            if (hovered)
            {
                HandOnHover();
            }
            var chosen = option == _sort;
            if (chosen || hovered)
            {
                dl.AddRectFilled(rowTl, rowTl + rowSize,
                    StorePalette.BlueWithAlpha(chosen ? 0.24f : 0.10f), Px(10f));
            }
            var label = Loc.T(option switch
            {
                StoreSort.Newest => "os.store_sort_newest",
                StoreSort.PriceAscending => "os.store_sort_price_up",
                StoreSort.PriceDescending => "os.store_sort_price_down",
                StoreSort.MostBought => "os.store_sort_popular",
                _ => "os.store_sort_featured",
            });
            dl.AddText(rowTl + new Vector2(Px(14f), (rowH - ImGui.GetTextLineHeight()) * 0.5f),
                chosen ? StorePalette.BodyU32 : StorePalette.HintU32, label);
            if (chosen)
            {
                IconDraw.AddCentered(dl, FontAwesomeIcon.Check, Px(12f),
                    rowTl + new Vector2(rowSize.X - Px(18f), rowH * 0.5f), StorePalette.BlueLightU32);
            }
            if (pressed)
            {
                _sort = option;
                _sortSheetOpen = false;
            }
        }

        ImGui.SetCursorScreenPos(origin);
        if (ImGui.InvisibleButton("##storeSortScrim", avail))
        {
            _sortSheetOpen = false;
        }
        _ = ctx;
    }

    private void DrawGrid(OsAppContext ctx, float winW)
    {
        var pad = Px(PadX);
        var gap = Px(10f);
        var cardW = (winW - pad * 2f - gap) * 0.5f;
        var cardH = Px(CardH);
        var pitch = cardH + gap;
        // Filtered before the positions are computed; skipping mid-loop leaves holes in the grid.
        var items = _hideOwned
            ? state.BrowseItems
                .Where(p => p.MaxPerAccount is not { } max || p.OwnedQuantity < max)
                .ToList()
            : state.BrowseItems;

        if (items.Count == 0)
        {
            ImGui.Dummy(new Vector2(0f, Px(40f)));
            if (state.BrowseLoading)
            {
                var center = new Vector2(ImGui.GetWindowPos().X + winW * 0.5f, ImGui.GetCursorScreenPos().Y + Px(16f));
                AetherLove.Widgets.LoadingSpinner.Draw(center, Px(14f), Px(3f), StorePalette.BlueU32);
            }
            else
            {
                var dl = ImGui.GetWindowDrawList();
                IconDraw.AddCentered(dl, FontAwesomeIcon.ShoppingBag, Px(30f),
                    new Vector2(ImGui.GetWindowPos().X + winW * 0.5f, ImGui.GetCursorScreenPos().Y + Px(10f)),
                    OsDrawShared.White(0.14f));
                ImGui.Dummy(new Vector2(0f, Px(34f)));
                StoreFx.CenterWrapped(Loc.T("os.store_browse_empty"), winW, StorePalette.Hint, winW - (Px(PadX) * 2f));
                if (HasAnyFilter())
                {
                    ImGui.Dummy(new Vector2(0f, Px(12f)));
                    var btnW = Px(170f);
                    ImGui.SetCursorPosX((winW - btnW) * 0.5f);
                    if (StoreUi.Button(Loc.T("os.store_filter_reset"), btnW))
                    {
                        ClearFilters();
                    }
                }
            }
            return;
        }

        var startY = ImGui.GetCursorPosY();
        var rows = (items.Count + 1) / 2;

        // Fixed-pitch culling: only cards inside the viewport (one pitch of margin) draw their body.
        var scrollY = ImGui.GetScrollY();
        var viewH = ImGui.GetWindowSize().Y;
        for (var i = 0; i < items.Count; i++)
        {
            var row = i / 2;
            var col = i % 2;
            var localY = startY + row * pitch;
            if (localY + cardH < scrollY - pitch || localY > scrollY + viewH + pitch)
            {
                continue;
            }
            var tl = new Vector2(
                ImGui.GetWindowPos().X + pad + col * (cardW + gap),
                ImGui.GetWindowPos().Y + localY - scrollY);
            var product = items[i];
            if (StoreCard.Draw(ctx, media, product, tl, new Vector2(cardW, cardH), i))
            {
                openDetail(product.Id);
            }
        }
        ImGui.SetCursorPosY(startY + rows * pitch);

        if (state.BrowseLoading)
        {
            var center = new Vector2(ImGui.GetWindowPos().X + winW * 0.5f,
                ImGui.GetCursorScreenPos().Y + Px(14f));
            AetherLove.Widgets.LoadingSpinner.Draw(center, Px(10f), Px(2.5f), StorePalette.BlueU32);
            ImGui.Dummy(new Vector2(0f, Px(30f)));
        }
        else if (state.BrowseEndReached && items.Count > 6)
        {
            StoreFx.CenterLine(Loc.T("os.store_browse_end"), winW, UiColors.Hint);
            ImGui.Dummy(new Vector2(0f, Px(10f)));
        }

        if (ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - Px(300f))
        {
            state.LoadMore(CurrentFilter());
        }
    }

    /// <summary>The in-page filter sheet, per the overlay doctrine: its own child layer, controls first,
    /// the scrim's click-catcher last.</summary>
    private void DrawFilterSheet(OsAppContext ctx, float winW)
    {
        var origin = ImGui.GetWindowPos() + new Vector2(0f, ImGui.GetScrollY());
        var avail = ImGui.GetWindowSize();
        ImGui.SetCursorScreenPos(origin);
        using var layer = ImRaii.Child("##storeFilterSheet", avail, false,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoBackground);
        if (!layer)
        {
            return;
        }
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(origin, origin + avail, OsDrawShared.Black(0.62f));

        var panelW = avail.X - Px(40f);
        var panelH = Px(238f);
        var panelTl = origin + new Vector2((avail.X - panelW) * 0.5f, (avail.Y - panelH) * 0.5f);
        dl.AddRectFilled(panelTl, panelTl + new Vector2(panelW, panelH), ImGui.GetColorU32(new Vector4(0.09f, 0.08f, 0.12f, 1f)), Px(16f));
        dl.AddRect(panelTl, panelTl + new Vector2(panelW, panelH), OsDrawShared.White(0.12f), Px(16f), ImDrawFlags.RoundCornersAll, Px(1f));

        ImGui.SetCursorScreenPos(panelTl + new Vector2(Px(14f), Px(12f)));
        AetherLove.Widgets.ModalUi.Header(panelW - Px(28f), Loc.T("os.store_filters"), StorePalette.Blue);

        var innerX = panelTl.X + Px(16f);
        ImGui.SetCursorScreenPos(new Vector2(innerX, panelTl.Y + Px(48f)));
        if (DrawToggleSwitch("##fltSale", Loc.T("os.store_filter_sale"), _onSaleOnly))
        {
            _onSaleOnly = !_onSaleOnly;
        }
        ImGui.SetCursorScreenPos(new Vector2(innerX, panelTl.Y + Px(78f)));
        if (DrawToggleSwitch("##fltOwned", Loc.T("os.store_filter_hide_owned"), _hideOwned))
        {
            _hideOwned = !_hideOwned;
        }

        ImGui.SetCursorScreenPos(new Vector2(innerX, panelTl.Y + Px(112f)));
        ImGui.TextColored(UiColors.Hint, Loc.T("os.store_filter_price"));
        var sliderW = (panelW - Px(48f)) * 0.5f;
        ImGui.SetCursorScreenPos(new Vector2(innerX, panelTl.Y + Px(134f)));
        ImGui.SetNextItemWidth(sliderW);
        ImGui.DragInt("##fltMin", ref _minPrice, 5f, 0, 5000, _minPrice == 0 ? Loc.T("os.store_filter_min") : "%d");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(sliderW);
        ImGui.DragInt("##fltMax", ref _maxPrice, 5f, 0, 5000, _maxPrice == 0 ? Loc.T("os.store_filter_max") : "%d");
        // A max under the min matches nothing and reads as a broken store rather than as a bad filter.
        if (_maxPrice > 0 && _minPrice > _maxPrice)
        {
            _minPrice = _maxPrice;
        }

        ImGui.SetCursorScreenPos(new Vector2(innerX, panelTl.Y + panelH - Px(52f)));
        var halfW = (panelW - Px(44f)) * 0.5f;
        if (StoreUi.Button(Loc.T("os.store_filter_reset"), halfW))
        {
            _onSaleOnly = false;
            _hideOwned = false;
            _minPrice = 0;
            _maxPrice = 0;
            _tag = null;
        }
        ImGui.SameLine();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Px(6f));
        if (StoreUi.Button(Loc.T("os.store_filter_apply"), halfW))
        {
            _filterSheetOpen = false;
        }

        // Click-outside-to-close: the scrim is submitted last so the panel's controls win.
        ImGui.SetCursorScreenPos(origin);
        if (ImGui.InvisibleButton("##storeFilterScrim", avail)
            && !new Vector4(panelTl.X, panelTl.Y, panelTl.X + panelW, panelTl.Y + panelH)
                .Contains(ImGui.GetMousePos()))
        {
            _filterSheetOpen = false;
        }
        _ = ctx;
        return;

        static bool DrawToggleSwitch(string id, string label, bool value)
        {
            var dl2 = ImGui.GetWindowDrawList();
            var tl = ImGui.GetCursorScreenPos();
            var trackW = Px(34f);
            var trackH = Px(18f);
            ImGui.SetCursorScreenPos(tl);
            var clicked = ImGui.InvisibleButton(id, new Vector2(trackW + Px(200f), trackH + Px(4f)));
            if (ImGui.IsItemHovered())
            {
                HandOnHover();
            }
            dl2.AddRectFilled(tl, tl + new Vector2(trackW, trackH),
                value ? StorePalette.BlueU32 : OsDrawShared.White(0.14f), trackH * 0.5f);
            dl2.AddCircleFilled(
                tl + new Vector2(value ? trackW - trackH * 0.5f : trackH * 0.5f, trackH * 0.5f),
                trackH * 0.5f - Px(2f), 0xFFFFFFFFu);
            dl2.AddText(tl + new Vector2(trackW + Px(8f), 0f), ImGui.GetColorU32(UiColors.Body), label);
            return clicked;
        }
    }
}

internal static class Vector4Extensions
{
    /// <summary>Treats the vector as a rect (x0, y0, x1, y1).</summary>
    public static bool Contains(this Vector4 rect, Vector2 point) =>
        point.X >= rect.X && point.Y >= rect.Y && point.X <= rect.Z && point.Y <= rect.W;
}
