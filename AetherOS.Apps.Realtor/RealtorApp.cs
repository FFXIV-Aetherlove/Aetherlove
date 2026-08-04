using System;
using System.Numerics;
using AetherLove.Services.Realtor;
using AetherLove.Services.Localization;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Realtor;

/// <summary>The housing app: open residential plots per world and district, with prices and lottery
/// state. Talks to the public PaissaDB API (crowdsourced by PaissaHouse scouts) and works without the
/// AetherLove server.</summary>
public sealed class RealtorApp : IAetherApp, IAppSettings
{
    private enum View { Home, District, WorldPick, Tour, Settings }

    private readonly Func<string> _name;
    private readonly IHousingLotteryWatch _lottery;
    private readonly IRealtorAlerts _alerts;
    private readonly RealtorSettings _settings;
    private readonly SettingsScreen _settingsScreen;
    private LotteryClock _clock = null!;
    private readonly IAppStorage _storage;
    private readonly HomeScreen _home;
    private readonly DistrictScreen _district;
    private readonly WorldPickScreen _worldPick;
    private readonly TourScreen _tour;
    private View _view = View.Home;
    private bool _tourSeen;
    private bool _tourSeenLoaded;

    public RealtorApp(Func<string> name, IAppCapabilities caps, RealtorDataService data, IHousingLotteryWatch lottery,
        IRealtorAlerts alerts)
    {
        _name = name;
        _lottery = lottery;
        _alerts = alerts;
        _storage = caps.Storage("realtor");
        var filters = new RealtorFilters(_storage);
        _settings = new RealtorSettings(_storage);
        _settingsScreen = new SettingsScreen(_settings);
        var clock = new LotteryClock(_storage);
        _clock = clock;
        _home = new HomeScreen(data, filters, clock, _settings, OpenWorldPick, OpenDistrict, OpenTour, () => _view = View.Settings);
        _district = new DistrictScreen(data, filters, clock, _settings, BackToHome);
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
        _alerts.ClearNotifications();
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

        // Above every screen, but not over the tour or settings, which own their whole region.
        if (_view is not (View.Tour or View.Settings))
        {
            DrawLotteryBanner(ctx);
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
            case View.Settings:
                DrawSettings(ctx, BackToHome);
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

    public void DrawSettings(OsAppContext ctx, Action? onBack) => _settingsScreen.Draw(ctx, onBack);

    /// <summary>The player's own lottery entry, shouted at the top of every screen while entries are still
    /// open. Deliberately loud: missing the window is the whole failure mode this guards against.</summary>
    private void DrawLotteryBanner(OsAppContext ctx)
    {
        if (_home.LotteryPhase != PaissaLottoPhase.Accepting || _lottery.Current is not { } entry)
        {
            return;
        }

        var winW = ImGui.GetWindowSize().X;
        var pad = ctx.Px(10f);
        var lineH = ImGui.GetTextLineHeight();
        var bannerH = (lineH * 2f) + ctx.Px(16f);
        var tl = ImGui.GetCursorScreenPos() + new Vector2(pad, ctx.Px(6f));
        var br = tl + new Vector2(winW - (pad * 2f), bannerH);
        var dl = ImGui.GetWindowDrawList();

        dl.AddRectFilled(tl, br, ImGui.GetColorU32(new Vector4(0.72f, 0.13f, 0.16f, 0.92f)), ctx.Px(12f));
        dl.AddRect(tl, br, ImGui.GetColorU32(new Vector4(1f, 0.42f, 0.42f, 0.85f)), ctx.Px(12f),
            ImDrawFlags.None, ctx.Px(1.4f));

        var white = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 1f));
        dl.AddText(tl + new Vector2(ctx.Px(12f), ctx.Px(7f)), white, Loc.T("os.realtor_bid_title"));
        dl.AddText(tl + new Vector2(ctx.Px(12f), ctx.Px(7f) + lineH),
            ImGui.GetColorU32(new Vector4(1f, 0.88f, 0.88f, 1f)),
            Loc.T("os.realtor_bid_detail", entry.Plot, entry.Ward, entry.District, entry.Number));

        ImGui.Dummy(new Vector2(0f, bannerH + ctx.Px(10f)));
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
