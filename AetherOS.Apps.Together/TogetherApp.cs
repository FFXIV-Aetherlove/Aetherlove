using System;
using System.Collections.Generic;
using System.Numerics;
using AetherOS.Apps.Together.Screens;
using AetherOS.Sdk;
using Dalamud.Interface;

namespace AetherOS.Apps.Together;

/// <summary>The front door to party mode. The widget card, the status light and the edge dock keep doing
/// what they do; this app is the place a player can FIND the feature, learn what it is worth, and run a
/// party from. Its tour is the party explainer, and finishing it sets the shell's own seen flag so the
/// widget card stops intercepting.</summary>
public sealed class TogetherApp : IAetherApp
{
    private const string TourSeenKey = "tourSeen";

    private enum View
    {
        Home,
        Tour,
    }

    private readonly Func<string> _name;
    private readonly ITogetherHost _host;
    private readonly IAppStorage _storage;
    private readonly HomeScreen _home;
    private readonly TourScreen _tour;
    private View _view = View.Home;

    public TogetherApp(Func<string> name, ITogetherHost host, IAppCapabilities caps)
    {
        _name = name;
        _host = host;
        _storage = caps.Storage(Id);
        _tour = new TourScreen(host, FinishTour);
        _home = new HomeScreen(host, OpenTour, OpenSettingsTour);
    }

    public string Id => "together";
    public string Name => _name();
    public FontAwesomeIcon Icon => FontAwesomeIcon.UserFriends;
    // Sampled off the tile art: a warm orange, light at the top, deep at the bottom.
    public Vector4 TileTop => new(0.99f, 0.72f, 0.40f, 1f);
    public Vector4 TileBottom => new(0.86f, 0.35f, 0.02f, 1f);
    public int Badge => 0;
    public bool HasSurface => true;
    public bool RequiresConnection => true;
    public bool UsesAccount => true;

    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Strings => Localization.AppStrings.Packs;

    public void Open()
    {
    }

    public void OnForeground()
    {
        if (_storage.Get<bool?>(TourSeenKey) != true && !_host.OnboardingSeen)
        {
            OpenTour();
        }
    }

    public void Draw(OsAppContext ctx)
    {
        switch (_view)
        {
            case View.Tour:
                _tour.Draw(ctx);
                break;
            default:
                _home.Draw(ctx);
                break;
        }
    }

    public void OnIntent(OsIntent intent)
    {
    }

    private void OpenTour()
    {
        _tour.OnShow(settingsOnly: false);
        _view = View.Tour;
    }

    private void OpenSettingsTour()
    {
        _tour.OnShow(settingsOnly: true);
        _view = View.Tour;
    }

    private void FinishTour()
    {
        _storage.Set(TourSeenKey, (bool?)true);
        _host.OnboardingSeen = true;
        _view = View.Home;
    }
}
