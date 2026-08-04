using System;
using System.Numerics;
using AetherOS.Sdk;
using AetherLove.Shared.Wayfinder;
using Dalamud.Interface;

namespace AetherOS.Apps.Wayfinder;

/// <summary>The Wayfinder app: a daily location-guessing game. The server authors the challenges and judges
/// positions; this app shows the picture, runs the countdown, and takes the selfie that doubles as the
/// attempt trigger. Selfies never leave the device.</summary>
public sealed class WayfinderApp : IAetherApp
{
    private enum View { Home, Challenge, Create, Tour }

    private readonly Func<string> _name;
    private readonly Func<bool> _available;
    private readonly IAppStorage _storage;
    private readonly HomeScreen _home;
    private readonly ChallengeScreen _challenge;
    private readonly CreateScreen _create;
    private readonly TourScreen _tour;
    private View _view = View.Home;
    private bool _tourSeen;
    private bool _tourSeenLoaded;

    public WayfinderApp(Func<string> name, Func<bool> available, IWayfinderHost host, IAppCapabilities caps)
    {
        _name = name;
        _available = available;
        _storage = caps.Storage("wayfinder");
        var history = new WayfinderHistory(_storage);
        _home = new HomeScreen(host, history, OpenChallenge, OpenTour, OpenCreate);
        _challenge = new ChallengeScreen(host, history, BackToHome);
        _create = new CreateScreen(host, BackToHome);
        _tour = new TourScreen(FinishTour);
    }

    public string Id => "wayfinder";
    public string Name => _name();
    public FontAwesomeIcon Icon => FontAwesomeIcon.Compass;
    public Vector4 TileTop => new(0.20f, 0.62f, 0.52f, 1f);
    public Vector4 TileBottom => new(0.07f, 0.30f, 0.28f, 1f);
    public int Badge => 0;
    public bool HasSurface => true;
    public bool Available => _available();
    public bool RequiresConnection => true;

    public System.Collections.Generic.IReadOnlyDictionary<string, System.Collections.Generic.IReadOnlyDictionary<string, string>> Strings => Localization.AppStrings.Packs;

    public void Open()
    {
    }

    public void OnForeground()
    {
        if (_view == View.Home)
        {
            _home.OnShow();
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
            case View.Challenge:
                _challenge.Draw(ctx);
                break;
            case View.Create:
                _create.Draw(ctx);
                break;
            case View.Tour:
                _tour.Draw(ctx);
                break;
        }
    }

    public void OnIntent(OsIntent intent)
    {
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

    private void OpenChallenge(WayfinderAssignmentDto assignment)
    {
        _view = View.Challenge;
        _challenge.OnShow(assignment);
    }

    private void OpenCreate()
    {
        _view = View.Create;
        _create.OnShow();
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

    private void BackToHome()
    {
        _view = View.Home;
        _home.OnShow();
    }
}
