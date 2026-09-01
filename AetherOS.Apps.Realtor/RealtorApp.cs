using System;
using System.Numerics;
using AetherLove.Services.Realtor;
using AetherLove.Services.Localization;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Realtor;

/// <summary>The housing app: open residential plots per world and district, with prices and lottery
/// state. Talks to the public PaissaDB API (crowdsourced by PaissaHouse scouts) and works without the
/// AetherLove server.</summary>
public sealed class RealtorApp : IAetherApp, IAppSettings
{
    private enum View { Home, District, WorldPick, Tour, Settings, Realty }

    private readonly Func<string> _name;
    private readonly IHousingLotteryWatch _lottery;
    private readonly IEstateWatch _estates;
    private readonly IRealtorAlerts _alerts;
    private readonly RealtorSettings _settings;
    private readonly SettingsScreen _settingsScreen;
    private LotteryClock _clock = null!;
    private readonly IAppStorage _storage;
    private readonly HomeScreen _home;
    private readonly DistrictScreen _district;
    private readonly WorldPickScreen _worldPick;
    private readonly TourScreen _tour;
    private readonly OwnedRealtyScreen _realty;
    private View _view = View.Home;
    private bool _tourSeen;
    private bool _tourSeenLoaded;

    public RealtorApp(Func<string> name, IAppCapabilities caps, RealtorDataService data, IHousingLotteryWatch lottery,
        IEstateWatch estates, IRealtorAlerts alerts)
    {
        _name = name;
        _lottery = lottery;
        _estates = estates;
        _alerts = alerts;
        _storage = caps.Storage("realtor");
        var filters = new RealtorFilters(_storage);
        _settings = new RealtorSettings(_storage);
        _settingsScreen = new SettingsScreen(_settings);
        var clock = new LotteryClock(_storage);
        _clock = clock;
        _home = new HomeScreen(data, filters, clock, _settings, estates, OpenWorldPick, OpenDistrict, OpenTour,
            () => _view = View.Settings, OpenRealty);
        _district = new DistrictScreen(data, filters, clock, _settings, BackToHome);
        _worldPick = new WorldPickScreen(data, BackToHome, PickWorld);
        _tour = new TourScreen(FinishTour);
        _realty = new OwnedRealtyScreen(estates, BackToHome);
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
    public int Badge => _estates.AtRiskCount;
    public bool HasSurface => true;

    public System.Collections.Generic.IReadOnlyDictionary<string, System.Collections.Generic.IReadOnlyDictionary<string, string>> Strings => Localization.AppStrings.Packs;

    public void Open()
    {
    }

    public void OnForeground()
    {
        _alerts.ClearNotifications();
        _estates.DismissWarnings();
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

        // Above every screen, but not over the tour or settings, which own their whole region. The estate
        // warning goes first: a lottery entry is an opportunity, a demolition is a loss.
        if (_view is not (View.Tour or View.Settings or View.Realty))
        {
            DrawEstateBanner(ctx);
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
            case View.Realty:
                _realty.Draw(ctx);
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

    /// <summary>A character that has not been home to its own private estate in long enough that the game is
    /// warning about it. Built as the same card the district and realty rows use rather than a coloured slab,
    /// so it reads as part of the app; the tint and the icon carry the alarm on their own.</summary>
    private void DrawEstateBanner(OsAppContext ctx)
    {
        var now = DateTime.UtcNow;
        var estates = _estates.Estates;
        if (EstateRisk.Worst(estates, now) is not { } worst)
        {
            return;
        }

        var daysAway = EstateRisk.DaysAway(worst, now);
        var others = EstateRisk.AtRiskCount(estates, now) - 1;
        var tint = RealtorUi.RiskRed;

        var who = worst.World.Length > 0 ? $"{worst.Character} ({worst.World})" : worst.Character;
        var title = Loc.T("os.realtor_estate_title", EstateRisk.DaysLeft(daysAway));
        var detail = others > 0
            ? Loc.T("os.realtor_estate_detail_more", who, daysAway, others)
            : Loc.T("os.realtor_estate_detail", who, daysAway);

        var winW = ImGui.GetWindowSize().X;
        var padX = ctx.Px(16f);
        var cardW = winW - (padX * 2f);
        var cardH = ctx.Px(58f);
        ImGui.SetCursorPosX(padX);
        var tl = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new Vector2(cardW, cardH));

        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(tl, tl + new Vector2(cardW, cardH),
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.06f)), ctx.Px(14f));
        dl.AddRect(tl, tl + new Vector2(cardW, cardH), ImGui.GetColorU32(tint with { W = 0.5f }),
            ctx.Px(14f), ImDrawFlags.None, ctx.Px(1.2f));

        var circle = ctx.Px(36f);
        var circleTl = new Vector2(tl.X + ctx.Px(11f), tl.Y + (cardH - circle) * 0.5f);
        dl.AddCircleFilled(circleTl + new Vector2(circle * 0.5f, circle * 0.5f), circle * 0.5f,
            ImGui.GetColorU32(tint with { W = 0.22f }));
        var iconPx = circle * 0.48f;
        var iconSz = IconDraw.Measure(FontAwesomeIcon.ExclamationTriangle, iconPx);
        IconDraw.Add(dl, FontAwesomeIcon.ExclamationTriangle, iconPx,
            circleTl + new Vector2((circle - iconSz.X) * 0.5f, (circle - iconSz.Y) * 0.5f),
            ImGui.GetColorU32(tint));

        var textX = circleTl.X + circle + ctx.Px(11f);
        var limit = tl.X + cardW - textX - ctx.Px(12f);
        var lineH = ImGui.GetTextLineHeight();
        dl.AddText(new Vector2(textX, tl.Y + (cardH * 0.5f) - lineH - ctx.Px(1f)),
            ImGui.GetColorU32(tint), TruncateToWidth(title, limit));
        dl.AddText(new Vector2(textX, tl.Y + (cardH * 0.5f) + ctx.Px(2f)),
            ImGui.GetColorU32(UiColors.Hint), TruncateToWidth(detail, limit));

        ImGui.Dummy(new Vector2(0f, ctx.Px(8f)));
    }

    /// <summary>The player's own lottery entry, shouted at the top of every screen while entries are still
    /// open. Deliberately loud: missing the window is the whole failure mode this guards against.</summary>
    private void DrawLotteryBanner(OsAppContext ctx)
    {
        if (_home.LotteryPhase != PaissaLottoPhase.Accepting || _lottery.Current is not { } entry)
        {
            return;
        }
        // An entry from a cycle that has already resolved is not a live bid. The results-phase watcher
        // normally clears it, but it cannot run for a player who was away for that whole phase.
        if (_home.LotteryPhaseStart is { } started && entry.CapturedAt < started)
        {
            _lottery.Clear();
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

    private void OpenRealty()
    {
        _view = View.Realty;
        _realty.OnShow();
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
