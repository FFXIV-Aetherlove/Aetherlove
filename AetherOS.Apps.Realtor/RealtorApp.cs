using System;
using System.Numerics;
using AetherLove.Services.Realtor;
using AetherOS.Sdk;
using Dalamud.Interface;

namespace AetherOS.Apps.Realtor;

/// <summary>The housing app: open residential plots per world and district, with prices and lottery
/// state. Talks to the public PaissaDB API (crowdsourced by PaissaHouse scouts) and works without the
/// AetherLove server.</summary>
public sealed class RealtorApp : IAetherApp
{
    private enum View { Home, District, WorldPick, Tour }

    private readonly Func<string> _name;
    private readonly IAppStorage _storage;
    private readonly HomeScreen _home;
    private readonly DistrictScreen _district;
    private readonly WorldPickScreen _worldPick;
    private readonly TourScreen _tour;
    private View _view = View.Home;
    private bool _tourSeen;
    private bool _tourSeenLoaded;

    public RealtorApp(Func<string> name, IAppCapabilities caps, RealtorDataService data)
    {
        _name = name;
        _storage = caps.Storage("realtor");
        var filters = new RealtorFilters(_storage);
        _home = new HomeScreen(data, filters, OpenWorldPick, OpenDistrict, OpenTour);
        _district = new DistrictScreen(data, filters, BackToHome);
        _worldPick = new WorldPickScreen(data, BackToHome, PickWorld);
        _tour = new TourScreen(FinishTour);
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

    private void OpenTour()
    {
        _view = View.Tour;
        _tour.OnShow();
    }

    private void FinishTour()
    {
        _tourSeen = true;
        _storage.Set("tourSeen", (bool?)true);
        BackToHome();
    }

    public string Id => "realtor";
    public string Name => _name();
    public FontAwesomeIcon Icon => FontAwesomeIcon.Home;
    public Vector4 TileTop => new(0.86f, 0.51f, 0.26f, 1f);
    public Vector4 TileBottom => new(0.52f, 0.22f, 0.10f, 1f);
    public int Badge => 0;
    public bool HasSurface => true;

    public System.Collections.Generic.IReadOnlyDictionary<string, System.Collections.Generic.IReadOnlyDictionary<string, string>> Strings => Localization.AppStrings.Packs;

    public void Open()
    {
    }

    public void OnForeground()
    {
        switch (_view)
        {
            case View.Home:
                _home.OnShow(_storage.Get<string>("world"));
                break;
            case View.District:
                _district.OnShow();
                break;
            case View.WorldPick:
                _worldPick.OnShow(_home.WorldName);
                break;
        }
    }

    public void Draw(OsAppContext ctx)
    {
        if (_view == View.Home && ShouldAutoRunTour())
        {
            _view = View.Tour;
            _tour.OnShow();
        }

        switch (_view)
        {
            case View.Home:
                _home.Draw(ctx);
                break;
            case View.District:
                _district.Draw(ctx);
                break;
            case View.WorldPick:
                _worldPick.Draw(ctx);
                break;
            case View.Tour:
                _tour.Draw(ctx);
                break;
            default:
                _view = View.Home;
                _home.OnShow(_storage.Get<string>("world"));
                _home.Draw(ctx);
                break;
        }
    }

    public void OnIntent(OsIntent intent)
    {
    }

    private void OpenWorldPick()
    {
        _view = View.WorldPick;
        _worldPick.OnShow(_home.WorldName);
    }

    private void OpenDistrict(int worldId, string worldName, PaissaDistrict district)
    {
        _view = View.District;
        _district.Open(worldId, worldName, district);
    }

    private void PickWorld(string worldName)
    {
        _storage.Set("world", worldName);
        _view = View.Home;
        _home.SetWorld(worldName);
    }

    private void BackToHome()
    {
        _view = View.Home;
        _home.OnShow(_storage.Get<string>("world"));
    }
}
