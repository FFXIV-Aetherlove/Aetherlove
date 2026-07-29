using System;
using System.Numerics;
using AetherLove.Services.Market;
using AetherOS.Sdk;
using Dalamud.Interface;

namespace AetherOS.Apps.Market;

/// <summary>The market board app: live prices for every marketable item, watchlists, price alerts, and the
/// player's own retainer sales. Talks to the public Universalis API and works without the AetherLove server.</summary>
public sealed class MarketApp : IAetherApp
{
    private enum View { Hub, Search, Detail, ListView, MySales, Alerts, Tour }

    private readonly Func<string> _name;
    private readonly IAppCapabilities _caps;
    private readonly MarketAlertStore _alertStore;
    private readonly IMarketDesk _desk;
    private readonly HubScreen _hub;
    private readonly SearchScreen _search;
    private readonly ItemDetailScreen _detail;
    private readonly ItemListScreen _list;
    private readonly AlertsScreen _alerts;
    private readonly MySalesScreen _mySales;
    private readonly TourScreen _tour;
    private readonly IAppStorage _storage;
    private IOsShell? _shell;
    private View _view = View.Hub;
    private View _detailReturn = View.Hub;
    private string? _detailReturnApp;
    private uint _pendingOpenItem;
    private bool _tourSeen;
    private bool _tourSeenLoaded;

    public MarketApp(Func<string> name, IAppCapabilities caps, MarketDataService data, MarketItemIndex index,
        MarketUserStore store, MarketAlertStore alertStore, IMarketDesk desk)
    {
        _name = name;
        _caps = caps;
        _alertStore = alertStore;
        _desk = desk;
        _hub = new HubScreen(data, index, store, alertStore, desk, NavigateTo, OpenItem, OpenSelection);
        _search = new SearchScreen(index, store, BackToHub, OpenItem);
        _detail = new ItemDetailScreen(data, index, store, alertStore, caps.Storage("market"), BackFromDetail);
        _list = new ItemListScreen(data, index, store, BackToHub, OpenItem);
        _alerts = new AlertsScreen(alertStore, index, BackToHub, OpenItem);
        _mySales = new MySalesScreen(desk, BackToHub, OpenItem);
        _tour = new TourScreen(FinishTour);
        _storage = caps.Storage("market");
    }

    private void FinishTour()
    {
        _tourSeen = true;
        _storage.Set("tourSeen", (bool?)true);
        BackToHub();
    }

    private bool ShouldAutoRunTour()
    {
        if (!_tourSeenLoaded)
        {
            _tourSeen = _storage.Get<bool?>("tourSeen") ?? false;
            _tourSeenLoaded = true;
        }
        return !_tourSeen;
    }

    public string Id => "market";
    public string Name => _name();
    public FontAwesomeIcon Icon => FontAwesomeIcon.Bell;
    public Vector4 TileTop => new(0.55f, 0.42f, 0.88f, 1f);
    public Vector4 TileBottom => new(0.26f, 0.14f, 0.50f, 1f);
    public int Badge => _alertStore.UnacknowledgedCount();
    public bool HasSurface => true;

    public System.Collections.Generic.IReadOnlyDictionary<string, System.Collections.Generic.IReadOnlyDictionary<string, string>> Strings => Localization.AppStrings.Packs;

    public void Open()
    {
    }

    public void OnForeground()
    {
        switch (_view)
        {
            case View.Hub:
                _hub.OnShow();
                break;
            case View.Search:
                _search.OnShow();
                break;
            case View.Detail:
                _detail.OnForeground();
                break;
            case View.ListView:
                _list.OnReturn();
                break;
            case View.Alerts:
                _alerts.OnShow();
                break;
            case View.MySales:
                _mySales.OnShow();
                break;
        }
    }

    public void Draw(OsAppContext ctx)
    {
        _shell = ctx.Shell;
        if (_pendingOpenItem != 0)
        {
            var itemId = _pendingOpenItem;
            _pendingOpenItem = 0;
            OpenItem(itemId);
        }
        if (_view == View.Hub && ShouldAutoRunTour())
        {
            _view = View.Tour;
            _tour.OnShow();
        }

        switch (_view)
        {
            case View.Hub:
                _hub.Draw(ctx);
                break;
            case View.Search:
                _search.Draw(ctx);
                break;
            case View.Detail:
                _detail.Draw(ctx);
                break;
            case View.ListView:
                _list.Draw(ctx);
                break;
            case View.Alerts:
                _alerts.Draw(ctx);
                break;
            case View.MySales:
                _mySales.Draw(ctx);
                break;
            case View.Tour:
                _tour.Draw(ctx);
                break;
            default:
                _view = View.Hub;
                _hub.OnShow();
                _hub.Draw(ctx);
                break;
        }
    }

    public void OnIntent(OsIntent intent)
    {
        if (intent.Type == OsIntents.OpenMarketItem && OsIntents.TryGetMarketItem(intent, out var itemId))
        {
            _pendingOpenItem = itemId;
            _detailReturnApp = OsIntents.TryGetReturnApp(intent, out var returnApp) ? returnApp : null;
        }
    }

    private void NavigateTo(HubTarget target)
    {
        switch (target)
        {
            case HubTarget.Search:
                _view = View.Search;
                _search.OnShow();
                break;
            case HubTarget.Watchlist:
                _view = View.ListView;
                _list.Open(ItemListKind.Watchlist);
                break;
            case HubTarget.Treasury:
                _view = View.ListView;
                _list.Open(ItemListKind.Treasury);
                break;
            case HubTarget.RareFinds:
                _view = View.ListView;
                _list.Open(ItemListKind.RareFinds);
                break;
            case HubTarget.NewPatch:
                _view = View.ListView;
                _list.Open(ItemListKind.NewPatch);
                break;
            case HubTarget.Alerts:
                _view = View.Alerts;
                _alerts.OnShow();
                break;
            case HubTarget.MySales:
                _view = View.MySales;
                _mySales.OnShow();
                break;
            case HubTarget.Tour:
                _view = View.Tour;
                _tour.OnShow();
                break;
        }
    }

    private void OpenSelection(Guid selectionId)
    {
        _view = View.ListView;
        _list.Open(ItemListKind.Selection, selectionId);
    }

    private void OpenItem(uint itemId)
    {
        if (_view != View.Detail)
        {
            _detailReturn = _view;
        }
        _view = View.Detail;
        _detail.Open(itemId);
        _alertStore.AcknowledgeItem(itemId);
        _shell?.DismissByTag($"market:alert:{itemId}");
    }

    private void BackFromDetail()
    {
        if (_detailReturnApp is { Length: > 0 } returnApp)
        {
            _detailReturnApp = null;
            _view = View.Hub;
            _hub.OnShow();
            _shell?.OpenApp(returnApp);
            return;
        }
        _view = _detailReturn;
        switch (_view)
        {
            case View.Search:
                _search.OnShow();
                break;
            case View.ListView:
                _list.OnReturn();
                break;
            case View.Alerts:
                _alerts.OnShow();
                break;
            case View.MySales:
                _mySales.OnShow();
                break;
            default:
                _view = View.Hub;
                _hub.OnShow();
                break;
        }
    }

    private void BackToHub()
    {
        _view = View.Hub;
        _hub.OnShow();
    }
}
