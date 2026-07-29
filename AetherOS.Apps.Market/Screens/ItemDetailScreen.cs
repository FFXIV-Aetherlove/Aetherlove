using System;
using System.Collections.Generic;
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

/// <summary>The exchange-style item page: hero price, scope and quality switchers, the history chart,
/// cross-scope context, tax-aware net, and the live listings and sales tables.</summary>
internal sealed class ItemDetailScreen
{
    private const float PadX = 16f;
    private const string ScopePrefKey = "scopePref";

    private readonly MarketDataService _data;
    private readonly MarketItemIndex _index;
    private readonly MarketUserStore _store;
    private readonly MarketAlertStore _alerts;
    private readonly IAppStorage _storage;
    private readonly Action _back;
    private readonly PriceChart _chart = new();
    private readonly EntranceAnimation _entrance = new();
    private bool _showAddToList;
    private float _addToListPanelH;
    private string _newListName = "";
    private bool _showAlertEditor;
    private float _alertPanelH;
    private string _alertThresholdText = "";
    private bool _alertAbove;
    private bool _alertPercent;
    private bool _alertHq;

    private uint _itemId;
    private MarketItemIndex.Entry _entry;
    private MarketScopeKind _scopeKind = MarketScopeKind.DataCenter;
    private (MarketScope World, MarketScope DataCenter, MarketScope Region)? _scopes;
    private bool _hq;
    private bool _hqAutoPicked;
    private int _rangeDays = 30;
    private bool _chartStale;

    private CancellationTokenSource _cts = new();
    private volatile MarketCurrentData? _current;
    private volatile AggregatedResult? _aggregated;
    private volatile MarketHistory? _history;
    private volatile Dictionary<string, int>? _tax;
    private volatile bool _loadingCurrent;
    private volatile bool _loadingHistory;
    private volatile bool _failed;

    public ItemDetailScreen(MarketDataService data, MarketItemIndex index, MarketUserStore store,
        MarketAlertStore alerts, IAppStorage storage, Action back)
    {
        _data = data;
        _index = index;
        _store = store;
        _alerts = alerts;
        _storage = storage;
        _back = back;
    }

    public uint ItemId => _itemId;

    public void Open(uint itemId)
    {
        _itemId = itemId;
        _index.EnsureBuildStarted();
        _index.TryGet(itemId, out _entry);
        _scopes = MarketScopes.DetectCurrent();
        var pref = _storage.Get<int?>(ScopePrefKey);
        if (pref is >= 0 and <= 2)
        {
            _scopeKind = (MarketScopeKind)pref.Value;
        }
        _hq = false;
        _hqAutoPicked = false;
        _current = null;
        _aggregated = null;
        _history = null;
        _failed = false;
        _showAddToList = false;
        _chart.SetData([]);
        _store.PushRecent(itemId);
        _entrance.Arm();
        StartFetch(historyOnly: false);
    }

    public void OnForeground()
    {
        _entrance.Arm();
    }

    private MarketScope? CurrentScope
    {
        get
        {
            if (_scopes is not { } scopes)
            {
                return null;
            }
            return _scopeKind switch
            {
                MarketScopeKind.World => scopes.World,
                MarketScopeKind.DataCenter => scopes.DataCenter,
                _ => scopes.Region,
            };
        }
    }

    private void StartFetch(bool historyOnly)
    {
        if (CurrentScope is not { } scope || _scopes is not { } scopes)
        {
            _failed = true;
            return;
        }
        _cts.Cancel();
        _cts.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        var itemId = _itemId;
        var rangeDays = _rangeDays;
        _loadingHistory = true;
        if (!historyOnly)
        {
            _loadingCurrent = true;
            _failed = false;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                if (!historyOnly)
                {
                    var current = await _data.GetItemAsync(scope, itemId, ct).ConfigureAwait(false);
                    var agg = await _data.GetAggregatedAsync(scopes.World, [itemId], ct).ConfigureAwait(false);
                    if (ct.IsCancellationRequested)
                    {
                        return;
                    }
                    _current = current;
                    _aggregated = agg.TryGetValue(itemId, out var result) ? result : null;
                    _failed = current is null && _aggregated is null;
                    if (!_hqAutoPicked && current is not null)
                    {
                        if (current.MinPriceNQ <= 0 && current.MinPriceHQ > 0)
                        {
                            _hq = true;
                        }
                        _hqAutoPicked = true;
                    }
                    if (_tax is null)
                    {
                        _tax = await _data.GetTaxRatesAsync(scopes.World.ApiName, ct).ConfigureAwait(false);
                    }
                }
                var history = await _data.GetHistoryAsync(scope, itemId, TimeSpan.FromDays(rangeDays), ct)
                    .ConfigureAwait(false);
                if (!ct.IsCancellationRequested)
                {
                    _history = history;
                    _chartStale = true;
                }
            }
            catch (Exception ex)
            {
                UiHost.Log.Debug($"[Market] Detail fetch failed: {ex.Message}");
            }
            finally
            {
                _loadingCurrent = false;
                _loadingHistory = false;
            }
        }, ct);
    }

    private bool CanBeHq()
    {
        var current = _current;
        if (current is not null && (current.MinPriceHQ > 0 || current.MaxPriceHQ > 0))
        {
            return true;
        }
        var history = _history;
        if (history is not null)
        {
            foreach (var sale in history.Entries)
            {
                if (sale.Hq)
                {
                    return true;
                }
            }
        }
        return _aggregated?.Hq.MinListing?.At(_scopeKind) is { Price: > 0 };
    }

    private long DisplayMinPrice()
    {
        var current = _current;
        if (current is not null && current.MinPrice(_hq) > 0)
        {
            return current.MinPrice(_hq);
        }
        return _aggregated?.Quality(_hq).MinListing?.At(_scopeKind)?.Price ?? 0;
    }

    private static string WorldName(int? worldId)
    {
        if (worldId is not > 0)
        {
            return "";
        }
        try
        {
            return UiHost.DataManager.GetExcelSheet<Lumina.Excel.Sheets.World>()
                .GetRow((uint)worldId.Value).Name.ExtractText();
        }
        catch
        {
            return "";
        }
    }

    public void Draw(OsAppContext ctx)
    {
        var winW = ImGui.GetContentRegionAvail().X;

        if (MarketHeader.Draw("", out var rowTop, out var pillH))
        {
            _back();
            return;
        }

        if (_chartStale && _history is { } history)
        {
            _chart.SetData(BuildChartPoints(history));
            _chartStale = false;
        }

        DrawActionButtons(ctx, winW, rowTop, pillH);

        _entrance.BeginFrame();
        PushScrollbarStyle();
        using (var scroll = ImRaii.Child("##marketDetailScroll", ImGui.GetContentRegionAvail(), false))
        {
            if (scroll.Success)
            {
                DrawContent(ctx, ImGui.GetWindowSize().X);
            }
        }
        PopScrollbarStyle();
        _entrance.EndFrame();

        DrawAddToListOverlay();
        DrawAlertEditorOverlay();
    }

    private void OpenAlertEditor()
    {
        var existing = _alerts.ForItem(_itemId);
        if (existing is not null)
        {
            _alertThresholdText = existing.Threshold.ToString();
            _alertAbove = existing.TriggerAbove;
            _alertPercent = existing.IsPercent;
            _alertHq = existing.HqOnly;
        }
        else
        {
            var min = DisplayMinPrice();
            _alertThresholdText = min > 0 ? min.ToString() : "";
            _alertAbove = false;
            _alertPercent = false;
            _alertHq = _hq && CanBeHq();
        }
        _showAlertEditor = true;
    }

    private void DrawAlertEditorOverlay()
    {
        if (!_showAlertEditor)
        {
            return;
        }
        var winPos = ImGui.GetWindowPos();
        var winSize = ImGui.GetWindowSize();
        if (DrawPageOverlayPanel("marketAlertEditor", winPos, winSize, ref _alertPanelH, Px(230f), innerW =>
        {
            var t = ThemeService.Current;
            Widgets.ModalUi.Header(innerW, FontAwesomeIcon.Bell, Loc.T("os.market_alert_title"), t.AccentLight);

            var dl = ImGui.GetWindowDrawList();
            var pillW = (innerW - Px(6f)) / 2f;
            var pillH = Px(24f);
            string[] labels = [Loc.T("os.market_alert_below"), Loc.T("os.market_alert_above")];
            for (var i = 0; i < 2; i++)
            {
                if (i > 0)
                {
                    ImGui.SameLine(0f, Px(6f));
                }
                var clicked = ImGui.InvisibleButton($"##marketAlertDir{i}", new Vector2(pillW, pillH));
                HandOnHover();
                var selected = (_alertAbove ? 1 : 0) == i;
                var tl = ImGui.GetItemRectMin();
                dl.AddRectFilled(tl, tl + new Vector2(pillW, pillH),
                    ImGui.GetColorU32(selected ? t.Accent with { W = 0.55f } : new Vector4(1f, 1f, 1f, 0.07f)), pillH * 0.5f);
                var labelSz = ImGui.CalcTextSize(labels[i]);
                dl.AddText(tl + (new Vector2(pillW, pillH) - labelSz) * 0.5f,
                    ImGui.GetColorU32(selected ? new Vector4(1f, 1f, 1f, 0.98f) : UiColors.Hint), labels[i]);
                if (clicked)
                {
                    _alertAbove = i == 1;
                }
            }
            ImGui.Spacing();

            ImGui.TextColored(UiColors.Hint, Loc.T(_alertPercent ? "os.market_alert_percent_label" : "os.market_alert_threshold"));
            ImGui.SetNextItemWidth(innerW);
            ImGui.InputText("##marketAlertThreshold", ref _alertThresholdText, 12,
                ImGuiInputTextFlags.CharsDecimal);

            var percent = _alertPercent;
            if (ImGui.Checkbox(Loc.T("os.market_alert_percent"), ref percent))
            {
                _alertPercent = percent;
            }
            HandOnHover();
            if (CanBeHq())
            {
                var hqOnly = _alertHq;
                if (ImGui.Checkbox(Loc.T("os.market_alert_hq"), ref hqOnly))
                {
                    _alertHq = hqOnly;
                }
                HandOnHover();
            }
            ImGui.Spacing();

            var canSave = long.TryParse(_alertThresholdText.Trim(), out var threshold) && threshold > 0
                && CurrentScope is not null;
            if (Widgets.ModalUi.Button($"{Loc.T("os.market_alert_save")}##marketAlertSave", innerW) && canSave)
            {
                SaveAlert(threshold);
                _showAlertEditor = false;
            }
            if (_alerts.ForItem(_itemId) is { } existing)
            {
                ImGui.Spacing();
                if (Widgets.ModalUi.Button($"{Loc.T("os.market_alert_delete")}##marketAlertDelete", innerW))
                {
                    _alerts.Remove(existing.Id);
                    _showAlertEditor = false;
                }
            }
        }))
        {
            _showAlertEditor = false;
        }
    }

    private void SaveAlert(long threshold)
    {
        if (CurrentScope is not { } scope)
        {
            return;
        }
        var existing = _alerts.ForItem(_itemId);
        var alert = existing ?? new MarketAlert { Id = Guid.NewGuid(), ItemId = _itemId };
        alert.ItemName = _entry.Name.Length > 0 ? _entry.Name : $"#{_itemId}";
        alert.ScopeKind = scope.Kind;
        alert.ScopeName = scope.ApiName;
        alert.HqOnly = _alertHq;
        alert.Threshold = threshold;
        alert.IsPercent = _alertPercent;
        alert.TriggerAbove = _alertAbove;
        alert.Enabled = true;
        alert.Armed = true;
        _alerts.Upsert(alert);
    }

    private void DrawActionButtons(OsAppContext ctx, float winW, float rowTop, float pillH)
    {
        var restore = ImGui.GetCursorScreenPos();
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var size = Px(28f);
        var gap = Px(6f);
        var winPos = ImGui.GetWindowPos();
        var rowY = rowTop + (pillH - size) * 0.5f;
        var watched = _store.IsWatched(_itemId);

        var hasAlert = _alerts.ForItem(_itemId) is { Enabled: true };
        (FontAwesomeIcon Icon, string Tooltip, Action OnClick, bool Active)[] buttons =
        [
            (FontAwesomeIcon.Star, Loc.T(watched ? "os.market_unwatch" : "os.market_watch"),
                () => _store.ToggleWatch(_itemId), watched),
            (FontAwesomeIcon.Bell, Loc.T(hasAlert ? "os.market_alert_active" : "os.market_alert_title"),
                OpenAlertEditor, hasAlert),
            (FontAwesomeIcon.Plus, Loc.T("os.market_add_to_list"), () => _showAddToList = true, false),
            (FontAwesomeIcon.Share, Loc.T("os.market_share_tip"), () => OfferShare(ctx), false),
        ];

        for (var i = 0; i < buttons.Length; i++)
        {
            var tl = new Vector2(winPos.X + winW - Px(PadX) - (buttons.Length - i) * (size + gap) + gap, rowY);
            ImGui.SetCursorScreenPos(tl);
            var clicked = ImGui.InvisibleButton($"##marketDetailAction{i}", new Vector2(size, size));
            HandOnHover();
            var hovered = ImGui.IsItemHovered();
            var active = buttons[i].Active;
            dl.AddRectFilled(tl, tl + new Vector2(size, size),
                ImGui.GetColorU32(active ? t.Accent with { W = 0.45f } : hovered ? t.Accent with { W = 0.28f } : new Vector4(1f, 1f, 1f, 0.06f)), Px(8f));
            IconDraw.AddCentered(dl, buttons[i].Icon, Px(12f), tl + new Vector2(size, size) * 0.5f,
                ImGui.GetColorU32(active ? new Vector4(0.98f, 0.80f, 0.36f, 1f) : t.AccentLight));
            if (hovered)
            {
                ImGui.SetTooltip(buttons[i].Tooltip);
            }
            if (clicked)
            {
                buttons[i].OnClick();
            }
        }
        ImGui.SetCursorScreenPos(restore);
    }

    private void OfferShare(OsAppContext ctx)
    {
        var price = DisplayMinPrice();
        ctx.Capabilities.Share.Offer(new ShareItem
        {
            Type = ShareTypes.MarketItem,
            RefId = _itemId.ToString(),
            Title = _entry.Name.Length > 0 ? _entry.Name : $"#{_itemId}",
            Subtitle = price > 0 ? $"{MarketFormat.GilFull(price)} gil" : "",
            SourceAppId = "market",
        }, title: _entry.Name);
    }

    private void DrawAddToListOverlay()
    {
        if (!_showAddToList)
        {
            return;
        }
        var winPos = ImGui.GetWindowPos();
        var winSize = ImGui.GetWindowSize();
        if (DrawPageOverlayPanel("marketAddToList", winPos, winSize, ref _addToListPanelH, Px(200f), innerW =>
        {
            Widgets.ModalUi.Header(innerW, FontAwesomeIcon.ListUl, Loc.T("os.market_add_to_list"),
                ThemeService.Current.AccentLight);
            foreach (var selection in _store.Selections)
            {
                var contains = selection.ItemIds.Contains(_itemId);
                if (DrawIconMenuItem(contains ? FontAwesomeIcon.CheckSquare : FontAwesomeIcon.Square,
                        TruncateToWidth(selection.Name, innerW - Px(40f))))
                {
                    _store.ToggleInSelection(selection.Id, _itemId);
                }
            }
            ImGui.Spacing();
            ImGui.SetNextItemWidth(innerW - Px(70f));
            ImGui.InputTextWithHint("##marketNewListName", Loc.T("os.market_new_list_hint"), ref _newListName, 40);
            ImGui.SameLine(0f, Px(6f));
            var canCreate = _newListName.Trim().Length > 0;
            if (SharedUiHelpers.Button($"{Loc.T("os.market_create")}##marketCreateList", new Vector2(Px(58f), 0f)) && canCreate)
            {
                var selection = _store.CreateSelection(_newListName);
                _store.ToggleInSelection(selection.Id, _itemId);
                _newListName = "";
            }
            ImGui.Spacing();
            if (Widgets.ModalUi.Button($"{Loc.T("common.ok")}##marketAddDone", innerW))
            {
                _showAddToList = false;
            }
        }))
        {
            _showAddToList = false;
        }
    }

    private void DrawContent(OsAppContext ctx, float winW)
    {
        var t = ThemeService.Current;

        DrawHero(winW);
        ImGui.Dummy(new Vector2(0f, Px(4f)));

        if (_scopes is null)
        {
            ImGui.SetCursorPosX(Px(PadX));
            ImGui.PushTextWrapPos(winW - Px(PadX));
            ImGui.TextColored(UiColors.Hint, Loc.T("os.market_offline"));
            ImGui.PopTextWrapPos();
            return;
        }

        DrawScopeSwitcher(winW);
        if (CanBeHq())
        {
            ImGui.Dummy(new Vector2(0f, Px(2f)));
            DrawQualitySwitcher();
        }
        ImGui.Dummy(new Vector2(0f, Px(6f)));

        if (_failed && _current is null)
        {
            ImGui.SetCursorPosX(Px(PadX));
            ImGui.PushTextWrapPos(winW - Px(PadX));
            ImGui.TextColored(UiColors.Hint, Loc.T("os.market_detail_error"));
            ImGui.PopTextWrapPos();
            return;
        }

        DrawStatsRow(winW);
        DrawCheaperOn(winW);
        ImGui.Dummy(new Vector2(0f, Px(6f)));
        DrawChartCard(ctx, winW);
        ImGui.Dummy(new Vector2(0f, Px(8f)));
        DrawListings(winW);
        ImGui.Dummy(new Vector2(0f, Px(8f)));
        DrawSales(winW);
        ImGui.Dummy(new Vector2(0f, Px(18f)));
    }

    private void DrawHero(float winW)
    {
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var iconSize = Px(54f);

        ImGui.SetCursorPosX(Px(PadX));
        var rowTl = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new Vector2(winW - Px(PadX) * 2f, iconSize));

        if (MarketItemIcons.Get(_entry.Icon) is { } handle)
        {
            dl.AddImageRounded(handle, rowTl, rowTl + new Vector2(iconSize, iconSize),
                Vector2.Zero, Vector2.One, ImGui.GetColorU32(Vector4.One), Px(10f));
        }
        else
        {
            dl.AddRectFilled(rowTl, rowTl + new Vector2(iconSize, iconSize),
                ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.08f)), Px(10f));
        }

        var textX = rowTl.X + iconSize + Px(12f);
        var name = _entry.Name.Length > 0 ? _entry.Name : $"#{_itemId}";
        using (UiFonts.H3?.Push())
        {
            var fitted = TruncateToWidth(name, winW - Px(PadX) * 2f - iconSize - Px(12f));
            dl.AddText(new Vector2(textX, rowTl.Y + Px(2f)), ImGui.GetColorU32(SearchScreen.RarityColor(_entry.Rarity)), fitted);
        }

        var price = DisplayMinPrice();
        var priceText = price > 0 ? $"{MarketFormat.GilFull(price)} gil" : Loc.T("os.market_loading");
        using (UiFonts.H2?.Push())
        {
            dl.AddText(new Vector2(textX, rowTl.Y + Px(24f)), ImGui.GetColorU32(t.AccentLight), priceText);
        }

        if (price > 0 && _tax is { Count: > 0 } tax)
        {
            var lowest = int.MaxValue;
            foreach (var rate in tax.Values)
            {
                lowest = Math.Min(lowest, rate);
            }
            var net = price * (100 - lowest) / 100;
            ImGui.SetCursorPosX(Px(PadX));
            ImGui.TextColored(UiColors.Hint, Loc.T("os.market_net_after_tax", MarketFormat.GilFull(net), lowest));
        }
    }

    private void DrawScopeSwitcher(float winW)
    {
        if (_scopes is not { } scopes)
        {
            return;
        }
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var barW = winW - Px(PadX) * 2f;
        var barH = Px(28f);
        ImGui.SetCursorPosX(Px(PadX));
        var barTl = ImGui.GetCursorScreenPos();
        dl.AddRectFilled(barTl, barTl + new Vector2(barW, barH), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.06f)), barH * 0.5f);

        string[] labels = [scopes.World.ApiName, scopes.DataCenter.ApiName, scopes.Region.ApiName];
        var segW = barW / 3f;
        for (var i = 0; i < 3; i++)
        {
            ImGui.SetCursorScreenPos(new Vector2(barTl.X + segW * i, barTl.Y));
            var clicked = ImGui.InvisibleButton($"##marketScope{i}", new Vector2(segW, barH));
            HandOnHover();
            var selected = (int)_scopeKind == i;
            var segTl = ImGui.GetItemRectMin();
            var segBr = ImGui.GetItemRectMax();
            if (selected)
            {
                dl.AddRectFilled(segTl + new Vector2(Px(2f), Px(2f)), segBr - new Vector2(Px(2f), Px(2f)),
                    ImGui.GetColorU32(t.Accent with { W = 0.55f }), (barH - Px(4f)) * 0.5f);
            }
            var label = TruncateToWidth(labels[i], segW - Px(10f));
            var labelSz = ImGui.CalcTextSize(label);
            dl.AddText(segTl + (new Vector2(segW, barH) - labelSz) * 0.5f,
                ImGui.GetColorU32(selected ? new Vector4(1f, 1f, 1f, 0.98f) : UiColors.Hint), label);

            if (clicked && !selected)
            {
                _scopeKind = (MarketScopeKind)i;
                _storage.Set(ScopePrefKey, (int?)i);
                StartFetch(historyOnly: false);
            }
        }
        ImGui.SetCursorScreenPos(barTl + new Vector2(0f, barH + Px(2f)));
        ImGui.NewLine();
    }

    private void DrawQualitySwitcher()
    {
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var pillW = Px(44f);
        var pillH = Px(22f);
        string[] labels = ["NQ", "HQ"];
        for (var i = 0; i < 2; i++)
        {
            if (i == 0)
            {
                ImGui.SetCursorPosX(Px(PadX));
            }
            else
            {
                ImGui.SameLine(0f, Px(6f));
            }
            var clicked = ImGui.InvisibleButton($"##marketQuality{i}", new Vector2(pillW, pillH));
            HandOnHover();
            var selected = (_hq ? 1 : 0) == i;
            var tl = ImGui.GetItemRectMin();
            var br = ImGui.GetItemRectMax();
            dl.AddRectFilled(tl, br,
                ImGui.GetColorU32(selected ? t.Accent with { W = 0.55f } : new Vector4(1f, 1f, 1f, 0.06f)), pillH * 0.5f);
            var labelSz = ImGui.CalcTextSize(labels[i]);
            dl.AddText(tl + (new Vector2(pillW, pillH) - labelSz) * 0.5f,
                ImGui.GetColorU32(selected ? new Vector4(1f, 1f, 1f, 0.98f) : UiColors.Hint), labels[i]);

            if (clicked && !selected)
            {
                _hq = i == 1;
                _chartStale = true;
            }
        }
    }

    private void DrawStatsRow(float winW)
    {
        var agg = _aggregated;
        var quality = agg?.Quality(_hq);
        var min = DisplayMinPrice();
        var avg = quality?.AverageSalePrice?.At(_scopeKind)?.Price ?? 0;
        var velocity = quality?.DailySaleVelocity?.At(_scopeKind)?.Quantity ?? 0;

        var cellW = (winW - Px(PadX) * 2f - Px(8f) * 2f) / 3f;
        DrawStatCell(0, cellW, Loc.T("os.market_stat_min"), min > 0 ? MarketFormat.Gil(min) : "-");
        DrawStatCell(1, cellW, Loc.T("os.market_stat_avg"), avg > 0 ? MarketFormat.Gil((long)avg) : "-");
        DrawStatCell(2, cellW, Loc.T("os.market_stat_velocity"),
            velocity > 0 ? velocity >= 10 ? velocity.ToString("F0") : velocity.ToString("F1") : "-");
        ImGui.NewLine();
    }

    private static void DrawStatCell(int index, float cellW, string label, string value)
    {
        var dl = ImGui.GetWindowDrawList();
        var cellH = Px(46f);
        if (index == 0)
        {
            ImGui.SetCursorPosX(Px(PadX));
        }
        else
        {
            ImGui.SameLine(0f, Px(8f));
        }
        ImGui.Dummy(new Vector2(cellW, cellH));
        var tl = ImGui.GetItemRectMin();
        dl.AddRectFilled(tl, tl + new Vector2(cellW, cellH), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.05f)), Px(10f));
        dl.AddText(tl + new Vector2(Px(8f), Px(6f)), ImGui.GetColorU32(UiColors.Hint), TruncateToWidth(label, cellW - Px(16f)));
        using (UiFonts.H3?.Push())
        {
            dl.AddText(tl + new Vector2(Px(8f), Px(20f)), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.95f)),
                TruncateToWidth(value, cellW - Px(16f)));
        }
    }

    private void DrawCheaperOn(float winW)
    {
        if (_scopeKind == MarketScopeKind.Region || _aggregated is not { } agg)
        {
            return;
        }
        var quality = agg.Quality(_hq);
        var world = quality.MinListing?.World?.Price ?? 0;
        var dc = quality.MinListing?.Dc;
        var region = quality.MinListing?.Region;

        string? line = null;
        if (_scopeKind == MarketScopeKind.World && world > 0)
        {
            if (dc is { Price: > 0 } && dc.Price < world && WorldName(dc.WorldId) is { Length: > 0 } dcWorld)
            {
                line = Loc.T("os.market_cheaper_on", dcWorld, $"{MarketFormat.Gil(dc.Price)} gil");
            }
            else if (region is { Price: > 0 } && region.Price < world && WorldName(region.WorldId) is { Length: > 0 } regionWorld)
            {
                line = Loc.T("os.market_cheaper_on", regionWorld, $"{MarketFormat.Gil(region.Price)} gil");
            }
        }
        else if (_scopeKind == MarketScopeKind.DataCenter && dc is { Price: > 0 }
            && region is { Price: > 0 } && region.Price < dc.Price
            && WorldName(region.WorldId) is { Length: > 0 } regionWorld)
        {
            line = Loc.T("os.market_cheaper_on", regionWorld, $"{MarketFormat.Gil(region.Price)} gil");
        }
        if (line is null)
        {
            return;
        }
        ImGui.Dummy(new Vector2(0f, Px(4f)));
        ImGui.SetCursorPosX(Px(PadX));
        ImGui.PushTextWrapPos(winW - Px(PadX));
        ImGui.TextColored(new Vector4(0.45f, 0.85f, 0.70f, 1f), line);
        ImGui.PopTextWrapPos();
    }

    private void DrawChartCard(OsAppContext ctx, float winW)
    {
        var dl = ImGui.GetWindowDrawList();
        var cardW = winW - Px(PadX) * 2f;
        var headH = Px(26f);
        var chartH = Px(140f);
        ImGui.SetCursorPosX(Px(PadX));
        var cardTl = ImGui.GetCursorScreenPos();
        dl.AddRectFilled(cardTl, cardTl + new Vector2(cardW, headH + chartH + Px(10f)),
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.045f)), Px(12f));

        dl.AddText(cardTl + new Vector2(Px(10f), Px(6f)), ImGui.GetColorU32(UiColors.Hint), Loc.T("os.market_chart_title"));

        int[] ranges = [7, 30, 90];
        var pillW = Px(34f);
        var pillH = Px(18f);
        for (var i = 0; i < ranges.Length; i++)
        {
            var pillTl = new Vector2(cardTl.X + cardW - Px(8f) - (ranges.Length - i) * (pillW + Px(4f)), cardTl.Y + Px(4f));
            ImGui.SetCursorScreenPos(pillTl);
            var clicked = ImGui.InvisibleButton($"##marketRange{ranges[i]}", new Vector2(pillW, pillH));
            HandOnHover();
            var selected = _rangeDays == ranges[i];
            var t = ThemeService.Current;
            dl.AddRectFilled(pillTl, pillTl + new Vector2(pillW, pillH),
                ImGui.GetColorU32(selected ? t.Accent with { W = 0.50f } : new Vector4(1f, 1f, 1f, 0.06f)), pillH * 0.5f);
            var label = $"{ranges[i]}d";
            var labelSz = ImGui.CalcTextSize(label);
            dl.AddText(pillTl + (new Vector2(pillW, pillH) - labelSz) * 0.5f,
                ImGui.GetColorU32(selected ? new Vector4(1f, 1f, 1f, 0.95f) : UiColors.Hint), label);
            if (clicked && !selected)
            {
                _rangeDays = ranges[i];
                StartFetch(historyOnly: true);
            }
        }

        ImGui.SetCursorScreenPos(cardTl + new Vector2(Px(4f), headH));
        if (_loadingHistory && !_chart.HasData)
        {
            ImGui.Dummy(new Vector2(cardW - Px(8f), chartH));
            Widgets.LoadingSpinner.Draw(cardTl + new Vector2(cardW * 0.5f, headH + chartH * 0.5f), Px(12f), Px(3f),
                ThemeService.Current.AccentU32);
        }
        else if (!_chart.HasData)
        {
            ImGui.Dummy(new Vector2(cardW - Px(8f), chartH));
            var hint = Loc.T("os.market_chart_empty");
            var hintSz = ImGui.CalcTextSize(hint);
            dl.AddText(cardTl + new Vector2((cardW - hintSz.X) * 0.5f, headH + (chartH - hintSz.Y) * 0.5f),
                ImGui.GetColorU32(UiColors.Hint), hint);
        }
        else
        {
            _chart.Draw(new Vector2(cardW - Px(8f), chartH), ctx.ReduceMotion);
        }
        ImGui.SetCursorScreenPos(cardTl + new Vector2(0f, headH + chartH + Px(12f)));
        ImGui.NewLine();
    }

    private IReadOnlyList<PriceChart.PricePoint> BuildChartPoints(MarketHistory history)
    {
        var bucketHours = _rangeDays switch
        {
            7 => 6,
            30 => 24,
            _ => 72,
        };
        var buckets = new SortedDictionary<long, (double PriceTimesQty, long Qty)>();
        foreach (var sale in history.Entries)
        {
            if (sale.Hq != _hq)
            {
                continue;
            }
            var bucket = sale.Timestamp / (bucketHours * 3600L);
            buckets.TryGetValue(bucket, out var acc);
            buckets[bucket] = (acc.PriceTimesQty + (double)sale.PricePerUnit * sale.Quantity, acc.Qty + sale.Quantity);
        }
        var points = new List<PriceChart.PricePoint>(buckets.Count);
        foreach (var (bucket, acc) in buckets)
        {
            if (acc.Qty <= 0)
            {
                continue;
            }
            points.Add(new PriceChart.PricePoint(
                DateTimeOffset.FromUnixTimeSeconds(bucket * bucketHours * 3600L),
                (float)(acc.PriceTimesQty / acc.Qty),
                (int)Math.Min(acc.Qty, int.MaxValue)));
        }
        return points;
    }

    private void DrawListings(float winW)
    {
        DrawSectionHeading(winW, Loc.T("os.market_listings_title"));
        var current = _current;
        if (_loadingCurrent && current is null)
        {
            Widgets.LoadingIndicator.Draw();
            return;
        }
        if (current is null)
        {
            return;
        }

        var listings = new List<MarketListing>();
        foreach (var listing in current.Listings)
        {
            if (listing.Hq != _hq && CanBeHq())
            {
                continue;
            }
            listings.Add(listing);
            if (listings.Count >= 20)
            {
                break;
            }
        }
        if (listings.Count == 0)
        {
            ImGui.SetCursorPosX(Px(PadX));
            ImGui.TextColored(UiColors.Hint, Loc.T("os.market_no_listings"));
            return;
        }

        var dl = ImGui.GetWindowDrawList();
        var cardW = winW - Px(PadX) * 2f;
        var colHeadH = Px(20f);
        var rowH = Px(26f);
        var tableH = colHeadH + listings.Count * rowH + Px(6f);

        ImGui.SetCursorPosX(Px(PadX));
        var tableTl = ImGui.GetCursorScreenPos();
        dl.AddRectFilled(tableTl, tableTl + new Vector2(cardW, tableH), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.03f)), Px(10f));
        dl.AddRect(tableTl, tableTl + new Vector2(cardW, tableH), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.10f)), Px(10f));

        var priceX = tableTl.X + Px(10f);
        var qtyX = tableTl.X + Px(96f);
        var worldX = tableTl.X + Px(134f);
        var retainerRight = tableTl.X + cardW - Px(10f);

        DrawTableLabel(dl, Loc.T("os.market_col_price"), new Vector2(priceX, tableTl.Y + Px(3f)), false);
        DrawTableLabel(dl, Loc.T("os.market_col_qty"), new Vector2(qtyX, tableTl.Y + Px(3f)), false);
        DrawTableLabel(dl, Loc.T("os.market_col_world"), new Vector2(worldX, tableTl.Y + Px(3f)), false);
        DrawTableLabel(dl, Loc.T("os.market_col_retainer"), new Vector2(retainerRight, tableTl.Y + Px(3f)), true);
        dl.AddLine(new Vector2(tableTl.X + Px(6f), tableTl.Y + colHeadH),
            new Vector2(tableTl.X + cardW - Px(6f), tableTl.Y + colHeadH),
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.10f)), Px(1f));

        var homeWorld = _scopes?.World.ApiName ?? "";
        var y = tableTl.Y + colHeadH + Px(3f);
        for (var i = 0; i < listings.Count; i++)
        {
            if (i > 0)
            {
                dl.AddLine(new Vector2(tableTl.X + Px(6f), y), new Vector2(tableTl.X + cardW - Px(6f), y),
                    ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.05f)), Px(1f));
            }
            DrawListingTableRow(dl, listings[i], y, rowH, priceX, qtyX, worldX, retainerRight, homeWorld);
            y += rowH;
        }

        ImGui.SetCursorScreenPos(new Vector2(tableTl.X, tableTl.Y + tableH));
        ImGui.NewLine();
    }

    private static void DrawTableLabel(ImDrawListPtr dl, string text, Vector2 pos, bool rightAligned)
    {
        var sz = ImGui.CalcTextSize(text);
        dl.AddText(rightAligned ? pos with { X = pos.X - sz.X } : pos, ImGui.GetColorU32(UiColors.Hint), text);
    }

    private void DrawListingTableRow(ImDrawListPtr dl, MarketListing listing, float y, float rowH,
        float priceX, float qtyX, float worldX, float retainerRight, string homeWorld)
    {
        var lineH = ImGui.GetTextLineHeight();
        var midY = y + (rowH - lineH) * 0.5f;

        dl.AddText(new Vector2(priceX, midY), ImGui.GetColorU32(TreasuryGold),
            MarketFormat.GilFull(listing.PricePerUnit));
        if (listing.Hq)
        {
            DrawHqChip(dl, new Vector2(priceX + Px(68f), y + rowH * 0.5f));
        }

        dl.AddText(new Vector2(qtyX, midY), ImGui.GetColorU32(UiColors.Hint), $"x{listing.Quantity}");

        var world = listing.WorldName is { Length: > 0 } listingWorld ? listingWorld : homeWorld;
        dl.AddText(new Vector2(worldX, midY), ImGui.GetColorU32(UiColors.Body),
            TruncateToWidth(world, retainerRight - Px(78f) - worldX));

        var retainer = TruncateToWidth(listing.RetainerName, Px(88f));
        var retainerSz = ImGui.CalcTextSize(retainer);
        dl.AddText(new Vector2(retainerRight - retainerSz.X, midY), ImGui.GetColorU32(UiColors.Hint), retainer);
    }

    private void DrawSales(float winW)
    {
        DrawSectionHeading(winW, Loc.T("os.market_sales_title"));
        var current = _current;
        if (current is null)
        {
            return;
        }

        var shown = 0;
        foreach (var sale in current.RecentHistory)
        {
            if (sale.Hq != _hq && CanBeHq())
            {
                continue;
            }
            DrawSaleRow(sale, winW);
            shown++;
            if (shown >= 15)
            {
                break;
            }
        }
        if (shown == 0)
        {
            ImGui.SetCursorPosX(Px(PadX));
            ImGui.TextColored(UiColors.Hint, Loc.T("os.market_no_sales"));
        }
    }

    private void DrawSaleRow(MarketSale sale, float winW)
    {
        var dl = ImGui.GetWindowDrawList();
        var rowH = Px(26f);
        ImGui.SetCursorPosX(Px(PadX));
        ImGui.Dummy(new Vector2(winW - Px(PadX) * 2f, rowH));
        var tl = ImGui.GetItemRectMin();
        var midY = tl.Y + rowH * 0.5f;
        var lineH = ImGui.GetTextLineHeight();

        dl.AddText(new Vector2(tl.X, midY - lineH * 0.5f),
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.90f)), $"{MarketFormat.GilFull(sale.PricePerUnit)}g");
        dl.AddText(new Vector2(tl.X + Px(92f), midY - lineH * 0.5f),
            ImGui.GetColorU32(UiColors.Hint), $"x{sale.Quantity}");
        if (sale.Hq)
        {
            DrawHqChip(dl, new Vector2(tl.X + Px(134f), midY));
        }

        var right = sale.WorldName is { Length: > 0 } world
            ? $"{TimeAgo(sale.When)} · {world}"
            : TimeAgo(sale.When);
        var rightText = TruncateToWidth(right, Px(110f));
        var rightSz = ImGui.CalcTextSize(rightText);
        dl.AddText(new Vector2(tl.X + (winW - Px(PadX) * 2f) - rightSz.X, midY - rightSz.Y * 0.5f),
            ImGui.GetColorU32(UiColors.Hint), rightText);
    }

    private static readonly Vector4 TreasuryGold = new(0.98f, 0.80f, 0.36f, 1f);

    private static void DrawHqChip(ImDrawListPtr dl, Vector2 leftMid)
    {
        var sz = ImGui.CalcTextSize("HQ");
        var pad = new Vector2(Px(4f), Px(1f));
        var tl = new Vector2(leftMid.X, leftMid.Y - sz.Y * 0.5f) - pad;
        dl.AddRectFilled(tl, tl + sz + pad * 2f, ImGui.GetColorU32(TreasuryGold with { W = 0.18f }), Px(4f));
        dl.AddText(new Vector2(leftMid.X, leftMid.Y - sz.Y * 0.5f), ImGui.GetColorU32(TreasuryGold), "HQ");
    }

    private static void DrawSectionHeading(float winW, string text)
    {
        ImGui.Dummy(new Vector2(0f, Px(4f)));
        ImGui.SetCursorPosX(Px(PadX));
        using (UiFonts.H3?.Push())
        {
            ImGui.TextColored(ThemeService.Current.AccentLight, text);
        }
    }

    private static string TimeAgo(DateTimeOffset when)
    {
        var span = DateTimeOffset.UtcNow - when;
        if (span.TotalMinutes < 1)
        {
            return "<1m";
        }
        if (span.TotalHours < 1)
        {
            return $"{(int)span.TotalMinutes}m";
        }
        if (span.TotalDays < 1)
        {
            return $"{(int)span.TotalHours}h";
        }
        return $"{(int)span.TotalDays}d";
    }
}
