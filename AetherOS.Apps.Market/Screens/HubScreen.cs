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

namespace AetherOS.Apps.Market;

internal enum HubTarget
{
    Search,
    Watchlist,
    MySales,
    Alerts,
    Treasury,
    RareFinds,
    NewPatch,
    NewList,
    Tour,
}

/// <summary>The Market home: a two-column bento masonry of showcase tiles under a shared search pill,
/// the user's custom list chips, and horizontal shelves for price alerts and the watchlist.</summary>
internal sealed class HubScreen
{
    private const float PadX = 16f;
    private const float Gap = 10f;

    private static readonly Vector4 TreasuryTop = new(0.98f, 0.80f, 0.36f, 1f);
    private static readonly Vector4 TreasuryBottom = new(0.62f, 0.40f, 0.10f, 1f);
    private static readonly Vector4 SalesTop = new(0.62f, 0.48f, 0.92f, 1f);
    private static readonly Vector4 SalesBottom = new(0.32f, 0.20f, 0.58f, 1f);
    private static readonly Vector4 RareTop = new(0.93f, 0.48f, 0.72f, 1f);
    private static readonly Vector4 RareBottom = new(0.55f, 0.18f, 0.40f, 1f);
    private static readonly Vector4 NewTop = new(0.32f, 0.82f, 0.66f, 1f);
    private static readonly Vector4 NewBottom = new(0.10f, 0.44f, 0.36f, 1f);
    private static readonly Vector4 AlertsTop = new(0.38f, 0.55f, 0.95f, 1f);
    private static readonly Vector4 AlertsBottom = new(0.14f, 0.26f, 0.58f, 1f);

    private readonly Action<HubTarget> _navigate;
    private readonly Action<uint> _openItem;
    private readonly Action<Guid> _openSelection;
    private readonly MarketDataService _data;
    private readonly MarketItemIndex _index;
    private readonly MarketUserStore _store;
    private readonly MarketAlertStore _alerts;
    private readonly IMarketDesk _desk;
    private readonly EntranceAnimation _entrance = new();
    private double _shownAt;
    private bool _showNewList;
    private float _newListPanelH;
    private string _newListName = "";
    private string? _mySalesSub;
    private string? _alertsSub;

    public HubScreen(MarketDataService data, MarketItemIndex index, MarketUserStore store, MarketAlertStore alerts,
        IMarketDesk desk, Action<HubTarget> navigate, Action<uint> openItem, Action<Guid> openSelection)
    {
        _data = data;
        _index = index;
        _store = store;
        _alerts = alerts;
        _desk = desk;
        _navigate = navigate;
        _openItem = openItem;
        _openSelection = openSelection;
    }

    public void OnShow()
    {
        _entrance.Arm();
        _shownAt = ImGui.GetTime();
        _index.EnsureBuildStarted();
        RefreshMySalesSub();
        RefreshAlertsSub();
        if (_watch is null || _watch.Count != _store.Watchlist.Count
            || DateTimeOffset.UtcNow - _watchFetchedAt > TimeSpan.FromMinutes(3))
        {
            StartWatchFetch();
        }
        if (_alertMet is null || _alertMetFor != _alerts.Alerts.Count
            || DateTimeOffset.UtcNow - _alertFetchedAt > TimeSpan.FromMinutes(3))
        {
            StartAlertsFetch();
        }
    }

    private void RefreshMySalesSub()
    {
        var snapshots = _desk.Snapshots;
        if (snapshots.Count == 0)
        {
            _mySalesSub = null;
            return;
        }
        long total = 0;
        foreach (var snapshot in snapshots)
        {
            foreach (var listing in snapshot.Listings)
            {
                total += listing.UnitPrice * listing.Quantity;
            }
        }
        var text = Loc.T("os.market_tile_mysales_value", MarketFormat.Gil(total));
        if (_desk.UndercutCount > 0)
        {
            text += " · " + Loc.T("os.market_tile_mysales_undercut", _desk.UndercutCount);
        }
        _mySalesSub = text;
    }

    private void RefreshAlertsSub()
    {
        var alerts = _alerts.Alerts;
        if (alerts.Count == 0)
        {
            _alertsSub = null;
            return;
        }
        var active = alerts.Count(a => a.Enabled);
        var hit = _alerts.UnacknowledgedCount();
        var parts = new List<string>();
        if (active > 0)
        {
            parts.Add(Loc.T("os.market_tile_alerts_active", active));
        }
        if (hit > 0)
        {
            parts.Add(Loc.T("os.market_tile_alerts_hit", hit));
        }
        _alertsSub = parts.Count > 0 ? string.Join(" · ", parts) : null;
    }

    public void Draw(OsAppContext ctx)
    {
        var t = ThemeService.Current;
        var winW = ImGui.GetContentRegionAvail().X;

        ImGui.Spacing();
        ImGui.SetCursorPosX(Px(PadX));
        using (UiFonts.H3?.Push())
        {
            ImGui.TextColored(t.AccentLight, Loc.T("os.app_market"));
        }
        var menuTL = DrawMenuButton(winW);
        ImGui.Spacing();

        _entrance.BeginFrame();
        PushScrollbarStyle();
        using (var scroll = ImRaii.Child("##marketHubScroll", ImGui.GetContentRegionAvail(), false))
        {
            if (scroll.Success)
            {
                DrawContent(ctx, ImGui.GetWindowSize().X);
            }
        }
        PopScrollbarStyle();
        _entrance.EndFrame();

        DrawMenuDropdown(menuTL);
        DrawNewListOverlay();
    }

    private void DrawNewListOverlay()
    {
        if (!_showNewList)
        {
            return;
        }
        var winPos = ImGui.GetWindowPos();
        var winSize = ImGui.GetWindowSize();
        if (DrawPageOverlayPanel("marketNewList", winPos, winSize, ref _newListPanelH, Px(140f), innerW =>
        {
            AetherLove.Widgets.ModalUi.Header(innerW, FontAwesomeIcon.ListUl, Loc.T("os.market_new_list_title"),
                ThemeService.Current.AccentLight);
            ImGui.SetNextItemWidth(innerW);
            ImGui.InputTextWithHint("##marketHubNewList", Loc.T("os.market_new_list_hint"), ref _newListName, 40);
            ImGui.Spacing();
            if (AetherLove.Widgets.ModalUi.Button($"{Loc.T("os.market_create")}##marketHubCreate", innerW)
                && _newListName.Trim().Length > 0)
            {
                var selection = _store.CreateSelection(_newListName);
                _newListName = "";
                _showNewList = false;
                _openSelection(selection.Id);
            }
        }))
        {
            _showNewList = false;
        }
    }

    private void DrawContent(OsAppContext ctx, float winW)
    {
        DrawSearchPill(winW);
        ImGui.Dummy(new Vector2(0f, Px(2f)));

        var colW = (winW - Px(PadX) * 2f - Px(Gap)) / 2f;
        var top = ImGui.GetCursorPosY();
        var colY = new float[2];
        var tileIndex = 0;

        PlaceTile(ref tileIndex, colY, colW, top, Px(132f), TreasuryTop, TreasuryBottom, FontAwesomeIcon.Crown,
            Loc.T("os.market_tile_treasury"), Loc.T("os.market_tile_treasury_sub"), ctx.ReduceMotion, HubTarget.Treasury);
        PlaceTile(ref tileIndex, colY, colW, top, Px(92f), SalesTop, SalesBottom, FontAwesomeIcon.Store,
            Loc.T("os.market_tile_mysales"), _mySalesSub ?? Loc.T("os.market_tile_mysales_sub"), ctx.ReduceMotion, HubTarget.MySales);
        PlaceTile(ref tileIndex, colY, colW, top, Px(112f), RareTop, RareBottom, FontAwesomeIcon.Gem,
            Loc.T("os.market_tile_rare"), Loc.T("os.market_tile_rare_sub"), ctx.ReduceMotion, HubTarget.RareFinds);
        PlaceTile(ref tileIndex, colY, colW, top, Px(100f), NewTop, NewBottom, FontAwesomeIcon.Seedling,
            Loc.T("os.market_tile_new"), Loc.T("os.market_tile_new_sub"), ctx.ReduceMotion, HubTarget.NewPatch);
        PlaceTile(ref tileIndex, colY, colW, top, Px(92f), AlertsTop, AlertsBottom, FontAwesomeIcon.Bell,
            Loc.T("os.market_menu_alerts"), _alertsSub ?? Loc.T("os.market_tile_alerts_sub"), ctx.ReduceMotion, HubTarget.Alerts);

        ImGui.SetCursorPosY(top + MathF.Max(colY[0], colY[1]) + Px(6f));

        DrawSectionLabel(Loc.T("os.market_lists_title"));
        DrawListChips();

        if (_alerts.Alerts.Count > 0)
        {
            DrawSectionLabel(Loc.T("os.market_menu_alerts"));
            DrawAlertsShelf(winW, ctx.ReduceMotion);
        }
        if (_store.Watchlist.Count > 0)
        {
            DrawSectionLabel(Loc.T("os.market_menu_watchlist"));
            DrawWatchShelf(winW);
        }

        ImGui.Dummy(new Vector2(0f, Px(16f)));
    }

    private void PlaceTile(ref int tileIndex, float[] colY, float colW, float top, float height,
        Vector4 gradTop, Vector4 gradBottom, FontAwesomeIcon icon, string title, string sub,
        bool reduceMotion, HubTarget target)
    {
        var col = colY[0] <= colY[1] ? 0 : 1;
        var pos = new Vector2(Px(PadX) + col * (colW + Px(Gap)), top + colY[col]);
        var ease = TileEase(tileIndex, reduceMotion);
        if (DrawTile($"##marketTile{tileIndex}", tileIndex, pos, new Vector2(colW, height), ease, gradTop, gradBottom,
                icon, title, sub, reduceMotion))
        {
            _navigate(target);
        }
        colY[col] += height + Px(Gap);
        tileIndex++;
    }

    private float TileEase(int index, bool reduceMotion)
    {
        if (reduceMotion)
        {
            return 1f;
        }
        var p = Math.Clamp((ImGui.GetTime() - _shownAt - index * 0.05) / 0.28, 0.0, 1.0);
        var inv = 1f - (float)p;
        return 1f - inv * inv * inv;
    }

    private static bool DrawTile(string id, int tileIndex, Vector2 pos, Vector2 size, float ease,
        Vector4 gradTop, Vector4 gradBottom, FontAwesomeIcon icon, string title, string sub, bool reduceMotion)
    {
        ImGui.SetCursorPos(pos with { Y = pos.Y + Px(10f) * (1f - ease) });
        var clicked = ImGui.InvisibleButton(id, size);
        HandOnHover();
        var hovered = ImGui.IsItemHovered();

        var dl = ImGui.GetWindowDrawList();
        var tl = ImGui.GetItemRectMin();
        var br = ImGui.GetItemRectMax();
        var rounding = Px(14f);
        OsDrawShared.RoundedGradient(dl, tl, br, rounding, gradTop, gradBottom, ease * (hovered ? 1f : 0.94f));

        var watermarkPx = MathF.Min(size.Y * 0.52f, Px(46f));
        var watermarkSz = IconDraw.Measure(icon, watermarkPx);
        IconDraw.Add(dl, icon, watermarkPx,
            new Vector2(br.X - watermarkSz.X - Px(8f), tl.Y + Px(8f)),
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.09f * ease)));

        var chipR = Px(15f);
        var chipC = tl + new Vector2(Px(12f) + chipR, Px(12f) + chipR);
        dl.AddCircleFilled(chipC, chipR * 1.9f, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.05f * ease)));
        dl.AddCircleFilled(chipC, chipR, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.22f * ease)));
        IconDraw.AddCentered(dl, icon, chipR * 1.1f, chipC, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.95f * ease)));

        DrawTileShine(dl, tl, br, tileIndex, ease, reduceMotion);
        if (hovered)
        {
            dl.AddRect(tl, br, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.30f)), rounding, ImDrawFlags.None, Px(1.2f));
        }

        var subCol = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.72f * ease));
        var titleCol = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.98f * ease));
        var innerW = size.X - Px(24f);
        var (sub1, sub2) = WrapTwoLines(sub, innerW);
        var lineH = ImGui.GetTextLineHeight();
        var subBlockH = sub2 is null ? lineH : lineH * 2f + Px(1f);
        if (sub2 is not null)
        {
            dl.AddText(new Vector2(tl.X + Px(12f), br.Y - Px(10f) - lineH), subCol, sub2);
        }
        dl.AddText(new Vector2(tl.X + Px(12f), br.Y - Px(10f) - subBlockH), subCol, sub1);
        using (UiFonts.H3?.Push())
        {
            var titleText = TruncateToWidth(title, innerW);
            var titleSz = ImGui.CalcTextSize(titleText);
            dl.AddText(new Vector2(tl.X + Px(12f), br.Y - Px(12f) - subBlockH - titleSz.Y), titleCol, titleText);
        }
        return clicked;
    }

    /// <summary>Greedy two-line word wrap for tile subtitles; the second line ellipsizes if even two
    /// lines cannot hold the text.</summary>
    private static (string Line1, string? Line2) WrapTwoLines(string text, float maxW)
    {
        if (ImGui.CalcTextSize(text).X <= maxW)
        {
            return (text, null);
        }
        var words = text.Split(' ');
        var line1 = "";
        var index = 0;
        while (index < words.Length)
        {
            var candidate = line1.Length == 0 ? words[index] : $"{line1} {words[index]}";
            if (line1.Length > 0 && ImGui.CalcTextSize(candidate).X > maxW)
            {
                break;
            }
            line1 = candidate;
            index++;
        }
        if (index >= words.Length)
        {
            return (TruncateToWidth(line1, maxW), null);
        }
        var line2 = string.Join(' ', words[index..]);
        return (line1, TruncateToWidth(line2, maxW));
    }

    private static void DrawTileShine(ImDrawListPtr dl, Vector2 tl, Vector2 br, int tileIndex, float ease,
        bool reduceMotion)
    {
        if (ease >= 1f)
        {
            MarketFx.DrawShine(dl, tl, br, tileIndex, reduceMotion);
        }
    }

    private void DrawSearchPill(float winW)
    {
        var t = ThemeService.Current;
        ImGui.SetCursorPosX(Px(PadX));
        var size = new Vector2(winW - Px(PadX) * 2f, Px(34f));
        var clicked = ImGui.InvisibleButton("##marketSearchPill", size);
        HandOnHover();
        var hovered = ImGui.IsItemHovered();

        var dl = ImGui.GetWindowDrawList();
        var tl = ImGui.GetItemRectMin();
        var br = ImGui.GetItemRectMax();
        dl.AddRectFilled(tl, br, ImGui.GetColorU32(hovered ? t.Accent with { W = 0.22f } : new Vector4(1f, 1f, 1f, 0.07f)), size.Y * 0.5f);
        IconDraw.AddCentered(dl, FontAwesomeIcon.Search, Px(13f),
            new Vector2(tl.X + Px(18f), (tl.Y + br.Y) * 0.5f), ImGui.GetColorU32(t.AccentLight));
        var hint = Loc.T("os.market_search_hint");
        var hintSz = ImGui.CalcTextSize(hint);
        dl.AddText(new Vector2(tl.X + Px(32f), (tl.Y + br.Y) * 0.5f - hintSz.Y * 0.5f),
            ImGui.GetColorU32(UiColors.Hint), hint);

        if (clicked)
        {
            _navigate(HubTarget.Search);
        }
    }

    private static void DrawSectionLabel(string text)
    {
        ImGui.Dummy(new Vector2(0f, Px(6f)));
        ImGui.SetCursorPosX(Px(PadX));
        ImGui.TextColored(UiColors.Hint, text);
    }

    private const float ShelfCardW = 92f;
    private const float ShelfCardH = 96f;

    private void DrawHorizontalShelf(string childId, float winW, int count, Action<int, Vector2> drawCard)
    {
        using var child = ImRaii.Child(childId, new Vector2(winW, Px(ShelfCardH) + Px(6f)), false,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        if (!child.Success)
        {
            return;
        }

        for (var i = 0; i < count; i++)
        {
            drawCard(i, new Vector2(Px(PadX) + i * (Px(ShelfCardW) + Px(8f)), Px(2f)));
        }
        ImGui.SetCursorPos(new Vector2(Px(PadX) + count * (Px(ShelfCardW) + Px(8f)), Px(2f)));
        ImGui.Dummy(new Vector2(Px(1f), Px(ShelfCardH)));

        if (ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows | ImGuiHoveredFlags.AllowWhenBlockedByActiveItem) &&
            ImGui.IsMouseDragging(ImGuiMouseButton.Left, 4f))
        {
            ImGui.SetScrollX(ImGui.GetScrollX() - ImGui.GetIO().MouseDelta.X);
        }
    }

    private bool DrawShelfCardCore(string id, ushort icon, string name, string line2, Vector4 line2Color, bool warnRing,
        bool goodRing = false, bool reduceMotion = false)
    {
        var t = ThemeService.Current;
        var cardW = Px(ShelfCardW);
        var cardH = Px(ShelfCardH);
        var clicked = ImGui.InvisibleButton(id, new Vector2(cardW, cardH));
        HandOnHover();
        var hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        var tl = ImGui.GetItemRectMin();
        var br = ImGui.GetItemRectMax();
        dl.AddRectFilled(tl, br, ImGui.GetColorU32(hovered ? t.Accent with { W = 0.20f } : new Vector4(1f, 1f, 1f, 0.07f)), Px(12f));
        if (goodRing)
        {
            var pulse = reduceMotion ? 0.65f : 0.55f + 0.25f * (float)Math.Sin(ImGui.GetTime() * 2.4);
            dl.AddRect(tl, br, ImGui.GetColorU32(UiColors.LiveGreen with { W = pulse }), Px(12f), ImDrawFlags.None, Px(1.6f));
            dl.AddRect(tl - new Vector2(Px(1.5f), Px(1.5f)), br + new Vector2(Px(1.5f), Px(1.5f)),
                ImGui.GetColorU32(UiColors.LiveGreen with { W = pulse * 0.35f }), Px(13.5f), ImDrawFlags.None, Px(3f));
        }
        else if (warnRing)
        {
            dl.AddRect(tl, br, ImGui.GetColorU32(UiColors.Danger with { W = 0.55f }), Px(12f), ImDrawFlags.None, Px(1.2f));
        }

        var iconSize = Px(40f);
        var iconTl = new Vector2(tl.X + (cardW - iconSize) * 0.5f, tl.Y + Px(8f));
        if (MarketItemIcons.Get(icon) is { } handle)
        {
            dl.AddImageRounded(handle, iconTl, iconTl + new Vector2(iconSize, iconSize),
                Vector2.Zero, Vector2.One, ImGui.GetColorU32(Vector4.One), Px(8f));
        }

        var fitted = TruncateToWidth(name, cardW - Px(12f));
        var nameSz = ImGui.CalcTextSize(fitted);
        dl.AddText(new Vector2(tl.X + (cardW - nameSz.X) * 0.5f, iconTl.Y + iconSize + Px(4f)),
            ImGui.GetColorU32(UiColors.Body), fitted);

        var line2Fitted = TruncateToWidth(line2, cardW - Px(12f));
        var line2Sz = ImGui.CalcTextSize(line2Fitted);
        dl.AddText(new Vector2(tl.X + (cardW - line2Sz.X) * 0.5f, br.Y - Px(8f) - line2Sz.Y),
            ImGui.GetColorU32(line2Color), line2Fitted);

        return clicked;
    }

    private void DrawAlertsShelf(float winW, bool reduceMotion)
    {
        var alerts = _alerts.Alerts.OrderBy(a => a.Acknowledged).ThenBy(a => a.ItemName).ToList();
        var met = _alertMet;
        DrawHorizontalShelf("##marketAlertShelf", winW, alerts.Count, (i, pos) =>
        {
            ImGui.SetCursorPos(pos);
            var alert = alerts[i];
            _index.TryGet(alert.ItemId, out var entry);
            var threshold = alert.IsPercent ? $"{alert.Threshold}%" : MarketFormat.Gil(alert.Threshold);
            var condition = (alert.TriggerAbove ? "> " : "< ") + threshold;
            var name = alert.ItemName.Length > 0 ? alert.ItemName : $"#{alert.ItemId}";
            var hit = !alert.Acknowledged;
            var good = met is not null && met.TryGetValue(alert.Id, out var m) && m;
            if (DrawShelfCardCore($"##marketAlertCard{alert.Id:N}", entry.Icon, name, condition,
                    good ? UiColors.LiveGreen : hit ? UiColors.Danger : UiColors.Hint, hit, good, reduceMotion))
            {
                _openItem(alert.ItemId);
            }
        });
    }

    private volatile IReadOnlyDictionary<Guid, bool>? _alertMet;
    private volatile bool _alertLoading;
    private int _alertMetFor;
    private DateTimeOffset _alertFetchedAt;

    /// <summary>Evaluates every alert's condition against live prices (same math as the poll loop) so the
    /// shelf can glow the cards whose target price has arrived, including fired-and-disabled ones.</summary>
    private void StartAlertsFetch()
    {
        if (_alertLoading)
        {
            return;
        }
        var alerts = _alerts.Alerts.Where(a => a.ItemId > 0 && a.ScopeName.Length > 0).ToList();
        _alertMetFor = _alerts.Alerts.Count;
        if (alerts.Count == 0)
        {
            _alertMet = new Dictionary<Guid, bool>();
            return;
        }
        _alertLoading = true;
        _ = Task.Run(async () =>
        {
            try
            {
                var met = new Dictionary<Guid, bool>();
                foreach (var group in alerts.GroupBy(a => (a.ScopeKind, a.ScopeName)))
                {
                    var scope = new MarketScope(group.Key.ScopeKind, group.Key.ScopeName);
                    var ids = group.Select(a => a.ItemId).Distinct().ToArray();
                    var agg = await _data.GetAggregatedAsync(scope, ids, CancellationToken.None).ConfigureAwait(false);
                    foreach (var alert in group)
                    {
                        if (!agg.TryGetValue(alert.ItemId, out var result))
                        {
                            continue;
                        }
                        var quality = result.Quality(alert.HqOnly);
                        var price = quality.MinListing?.At(alert.ScopeKind)?.Price ?? 0;
                        if (price <= 0)
                        {
                            continue;
                        }
                        var threshold = (double)alert.Threshold;
                        if (alert.IsPercent)
                        {
                            var average = quality.AverageSalePrice?.At(alert.ScopeKind)?.Price ?? 0;
                            if (average <= 0)
                            {
                                continue;
                            }
                            threshold = average * alert.Threshold / 100.0;
                        }
                        met[alert.Id] = alert.TriggerAbove ? price >= threshold : price <= threshold;
                    }
                }
                _alertMet = met;
                _alertFetchedAt = DateTimeOffset.UtcNow;
            }
            catch (Exception ex)
            {
                UiHost.Log.Debug($"[Market] Alert shelf fetch failed: {ex.Message}");
            }
            finally
            {
                _alertLoading = false;
            }
        });
    }

    private readonly record struct WatchEntry(uint Id, string Name, ushort Icon, long MinPrice);

    private volatile IReadOnlyList<WatchEntry>? _watch;
    private volatile bool _watchLoading;
    private DateTimeOffset _watchFetchedAt;

    private void StartWatchFetch()
    {
        if (_watchLoading)
        {
            return;
        }
        var ids = _store.Watchlist;
        if (ids.Count == 0)
        {
            _watch = [];
            return;
        }
        var detected = MarketScopes.DetectCurrent();
        if (detected is null)
        {
            return;
        }
        _watchLoading = true;
        var world = detected.Value.DataCenter;
        _ = Task.Run(async () =>
        {
            try
            {
                var waited = 0;
                while (!_index.Ready && waited < 60)
                {
                    await Task.Delay(250).ConfigureAwait(false);
                    waited++;
                }
                if (!_index.Ready)
                {
                    return;
                }
                var agg = await _data.GetAggregatedAsync(world, ids, CancellationToken.None).ConfigureAwait(false);
                var entries = new List<WatchEntry>(ids.Count);
                foreach (var id in ids)
                {
                    if (!_index.TryGet(id, out var entry))
                    {
                        continue;
                    }
                    long price = 0;
                    if (agg.TryGetValue(id, out var result))
                    {
                        price = result.Nq.MinListing?.At(MarketScopeKind.DataCenter)?.Price
                            ?? result.Hq.MinListing?.At(MarketScopeKind.DataCenter)?.Price
                            ?? 0;
                    }
                    entries.Add(new WatchEntry(id, entry.Name, entry.Icon, price));
                }
                _watch = entries;
                _watchFetchedAt = DateTimeOffset.UtcNow;
            }
            catch (Exception ex)
            {
                UiHost.Log.Debug($"[Market] Watch shelf fetch failed: {ex.Message}");
            }
            finally
            {
                _watchLoading = false;
            }
        });
    }

    private void DrawWatchShelf(float winW)
    {
        var watch = _watch;
        if (watch is null)
        {
            DrawHorizontalShelf("##marketWatchShelf", winW, Math.Min(_store.Watchlist.Count, 3), (i, pos) =>
            {
                ImGui.SetCursorPos(pos);
                ImGui.Dummy(new Vector2(Px(ShelfCardW), Px(ShelfCardH)));
                var dl = ImGui.GetWindowDrawList();
                var pulse = 0.05f + 0.025f * (float)Math.Sin(ImGui.GetTime() * 2.0 + i * 0.9);
                dl.AddRectFilled(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(),
                    ImGui.GetColorU32(new Vector4(1f, 1f, 1f, pulse)), Px(12f));
            });
            return;
        }
        DrawHorizontalShelf("##marketWatchShelf", winW, watch.Count, (i, pos) =>
        {
            ImGui.SetCursorPos(pos);
            var entry = watch[i];
            var price = entry.MinPrice > 0 ? $"{MarketFormat.Gil(entry.MinPrice)} gil" : "-";
            if (DrawShelfCardCore($"##marketWatchCard{entry.Id}", entry.Icon, entry.Name, price,
                    entry.MinPrice > 0 ? TreasuryTop : UiColors.Hint, false))
            {
                _openItem(entry.Id);
            }
        });
    }

    private void DrawListChips()
    {
        var winW = ImGui.GetWindowSize().X;
        var x = Px(PadX);
        foreach (var selection in _store.Selections)
        {
            var label = TruncateToWidth(selection.Name, Px(96f));
            var chipW = ImGui.CalcTextSize(label).X + Px(22f);
            if (x + chipW > winW - Px(PadX))
            {
                ImGui.NewLine();
                x = Px(PadX);
            }
            ImGui.SetCursorPosX(x);
            if (DrawChip($"##marketSel{selection.Id:N}", label, chipW, false))
            {
                _openSelection(selection.Id);
            }
            x += chipW + Px(6f);
            ImGui.SameLine(0f, Px(6f));
        }

        var newLabel = $"+ {Loc.T("os.market_lists_new")}";
        var newW = ImGui.CalcTextSize(newLabel).X + Px(22f);
        if (x + newW > winW - Px(PadX))
        {
            ImGui.NewLine();
            ImGui.SetCursorPosX(Px(PadX));
        }
        if (DrawChip("##marketNewListChip", newLabel, newW, true))
        {
            _newListName = "";
            _showNewList = true;
        }
        ImGui.NewLine();
    }

    private static bool DrawChip(string id, string label, float chipW, bool accented)
    {
        var t = ThemeService.Current;
        var size = new Vector2(chipW, Px(26f));
        var clicked = ImGui.InvisibleButton(id, size);
        HandOnHover();
        var hovered = ImGui.IsItemHovered();

        var dl = ImGui.GetWindowDrawList();
        var tl = ImGui.GetItemRectMin();
        var br = ImGui.GetItemRectMax();
        dl.AddRectFilled(tl, br, ImGui.GetColorU32(hovered ? t.Accent with { W = 0.25f } : new Vector4(1f, 1f, 1f, 0.07f)), size.Y * 0.5f);
        var labelSz = ImGui.CalcTextSize(label);
        dl.AddText(new Vector2(tl.X + Px(11f), (tl.Y + br.Y) * 0.5f - labelSz.Y * 0.5f),
            ImGui.GetColorU32(accented ? t.AccentLight : UiColors.Body), label);
        return clicked;
    }

    private const string MenuPopupId = "##marketMenuPopup";

    private Vector2 DrawMenuButton(float winW)
    {
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var iconFont = UiHost.PluginInterface.UiBuilder.FontIcon;
        var size = Px(30f);
        var winPos = ImGui.GetWindowPos();
        var tl = new Vector2(winPos.X + winW - Px(PadX) - size,
            ImGui.GetCursorScreenPos().Y - ImGui.GetTextLineHeight() - Px(6f));

        ImGui.SetCursorScreenPos(tl);
        if (ImGui.InvisibleButton("##marketMenuBtn", new Vector2(size, size)))
        {
            ImGui.OpenPopup(MenuPopupId);
        }
        HandOnHover();
        var active = ImGui.IsItemHovered() || ImGui.IsPopupOpen(MenuPopupId);
        dl.AddRectFilled(tl, tl + new Vector2(size, size),
            ImGui.GetColorU32(active ? t.Accent with { W = 0.30f } : new Vector4(1f, 1f, 1f, 0.06f)), Px(8f));

        ImGui.PushFont(iconFont);
        var glyph = FontAwesomeIcon.Bars.ToIconString();
        var glyphSz = ImGui.CalcTextSize(glyph);
        dl.AddText(tl + (new Vector2(size, size) - glyphSz) * 0.5f, ImGui.GetColorU32(t.AccentLight), glyph);
        ImGui.PopFont();
        return tl;
    }

    private void DrawMenuDropdown(Vector2 menuTL)
    {
        var size = Px(30f);
        ImGui.SetNextWindowPos(new Vector2(menuTL.X + size, menuTL.Y + size + Px(4f)),
            ImGuiCond.Always, new Vector2(1f, 0f));
        ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(0.13f, 0.12f, 0.15f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Border, ThemeService.Current.Accent with { W = 0.5f });
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, Px(10f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(Px(4f), Px(4f)));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0f, Px(2f)));
        if (ImGui.BeginPopup(MenuPopupId))
        {
            var watchlist = Loc.T("os.market_menu_watchlist");
            var mySales = Loc.T("os.market_tile_mysales");
            var alerts = Loc.T("os.market_menu_alerts");
            var tour = Loc.T("os.market_menu_tour");
            var w = AppHeader.MenuWidth(watchlist, mySales, alerts, tour);
            var rowH = AppHeader.MenuRowHeight();
            if (AppHeader.MenuRow(FontAwesomeIcon.Star, watchlist, w, rowH))
            {
                _navigate(HubTarget.Watchlist);
                ImGui.CloseCurrentPopup();
            }
            if (AppHeader.MenuRow(FontAwesomeIcon.Store, mySales, w, rowH))
            {
                _navigate(HubTarget.MySales);
                ImGui.CloseCurrentPopup();
            }
            if (AppHeader.MenuRow(FontAwesomeIcon.Bell, alerts, w, rowH))
            {
                _navigate(HubTarget.Alerts);
                ImGui.CloseCurrentPopup();
            }
            if (AppHeader.MenuRow(FontAwesomeIcon.Compass, tour, w, rowH))
            {
                _navigate(HubTarget.Tour);
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
        ImGui.PopStyleVar(3);
        ImGui.PopStyleColor(2);
    }
}
