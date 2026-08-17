using System;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Shared.Sparks;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Wallet;

/// <summary>The Wallet app: the sparks balance, weekly cap and earnings timeline, plus a second tab with
/// the character's in-game currencies. Read-only by design; the server owns every spark amount.</summary>
public sealed class WalletApp : IAetherApp
{
    internal static readonly Vector4 TileTopColor = new(0.95f, 0.71f, 0.24f, 1f);
    internal static readonly Vector4 TileBottomColor = new(0.62f, 0.35f, 0.07f, 1f);

    private enum View { Sparks, Currencies, Earn, History, Tour }

    private const float PadX = 16f;

    private readonly Func<string> _name;
    private readonly IWalletHost _host;
    private readonly IAppStorage _storage;
    private readonly SparksScreen _sparks;
    private readonly CurrenciesScreen _currencies;
    private readonly EarnScreen _earn;
    private readonly HistoryScreen _history;
    private readonly TourScreen _tour;
    private View _view = View.Sparks;
    private bool _tourSeen;
    private bool _tourSeenLoaded;

    // Set while another app deep-linked here, so every page offers one click back to it.
    private string? _returnApp;
    private IOsShell? _shell;

    public WalletApp(Func<string> name, IWalletHost host, IAppCapabilities caps)
    {
        _name = name;
        _host = host;
        _storage = caps.Storage("wallet");
        var favorites = new WalletFavorites(_storage);
        _sparks = new SparksScreen(host, favorites, OpenEarn, OpenHistory);
        _currencies = new CurrenciesScreen(host, favorites);
        _earn = new EarnScreen(BackToSparks);
        _history = new HistoryScreen(host, BackToSparks);
        _tour = new TourScreen(FinishTour);
    }

    public string Id => "wallet";
    public string Name => _name();
    public FontAwesomeIcon Icon => FontAwesomeIcon.Wallet;
    public Vector4 TileTop => TileTopColor;
    public Vector4 TileBottom => TileBottomColor;
    /// <summary>Deliberately never badges. At-cap was the only candidate, and a retired currency parked at its
    /// cap cannot be spent down, so the count would never return to zero.</summary>
    public int Badge => 0;

    public bool HasSurface => true;

    /// <summary>False even though Sparks needs the server: the Currencies tab reads nothing but the game
    /// client, so an outage must not blank the whole app. Sparks shows its own offline card.</summary>
    public bool RequiresConnection => false;

    /// <summary>The sparks half is account-backed, so a disabled account still gets the ban card.</summary>
    public bool UsesAccount => true;

    public System.Collections.Generic.IReadOnlyDictionary<string, System.Collections.Generic.IReadOnlyDictionary<string, string>> Strings => Localization.AppStrings.Packs;

    public void Open()
    {
    }

    /// <summary>Leaving by any route other than the return pill drops the deep-link context, so a later
    /// visit from the home screen never shows a stale way back.</summary>
    public void OnBackground()
    {
        _returnApp = null;
        _earn.BackTooltipOverride = null;
        _earn.BackIconOverride = FontAwesomeIcon.Bolt;
    }

    public void OnForeground()
    {
        switch (_view)
        {
            case View.Sparks:
                _sparks.OnShow();
                break;
            case View.Currencies:
                _currencies.OnShow();
                break;
            case View.History:
                _history.Show();
                break;
            case View.Earn:
                // The page renders off the sparks tab's snapshot and never fetches, so coming back to a
                // phone left on it has to refresh the tab underneath. Refresh rather than OnShow: the tab
                // is not the one being looked at and re-arming its entrance would replay a reveal nobody
                // sees.
                _sparks.Refresh();
                break;
        }
    }

    private void OpenEarn(SparkWalletDto wallet)
    {
        _view = View.Earn;
        _earn.Show(wallet);
    }

    private void OpenHistory()
    {
        _view = View.History;
        _history.Show();
    }

    private void BackToSparks()
    {
        if (_returnApp is not null)
        {
            GoBackToCaller();
            return;
        }
        _view = View.Sparks;
        _sparks.OnShow();
    }

    private void GoBackToCaller()
    {
        var target = _returnApp;
        _returnApp = null;
        _earn.BackTooltipOverride = null;
        _earn.BackIconOverride = FontAwesomeIcon.Bolt;
        _view = View.Sparks;
        if (target is not null)
        {
            _shell?.OpenApp(target);
        }
    }

    private (string Name, FontAwesomeIcon Icon)? CallerApp()
    {
        if (_returnApp is not { } id || _shell is null)
        {
            return null;
        }
        foreach (var app in _shell.Apps)
        {
            if (app.Id == id)
            {
                return (app.Name, app.Icon);
            }
        }
        return null;
    }

    public void Draw(OsAppContext ctx)
    {
        _shell = ctx.Shell;
        if (_view == View.Earn)
        {
            // Whatever the sparks tab last loaded, not just the first thing it loaded: the page has no fetch
            // of its own, so a snapshot that lands while it is open (a refetch on foreground, a task
            // finished elsewhere) has to reach it here or the ticks stay as they were when it opened.
            if (_sparks.Wallet is { } loaded && !ReferenceEquals(_earn.Wallet, loaded))
            {
                _earn.SetWallet(loaded);
            }
            // Resolved per frame: the caller's name is localized and can change under the user.
            var caller = _returnApp is null ? null : CallerApp();
            _earn.BackTooltipOverride = _returnApp is null
                ? null
                : caller is { } c ? Loc.T("os.wallet_back_app", c.Name) : Loc.T("os.wallet_back_app_generic");
            _earn.BackIconOverride = caller?.Icon ?? FontAwesomeIcon.Bolt;
        }

        if (_view != View.Tour && ShouldAutoRunTour())
        {
            _view = View.Tour;
            _tour.OnShow();
        }

        if (_view == View.Tour)
        {
            _tour.Draw(ctx);
            return;
        }

        DrawHeader(ctx);
        if (_view is View.Sparks or View.Currencies)
        {
            DrawTabs(ctx);
        }

        PushScrollbarStyle();
        using (var body = ImRaii.Child("##walletBody", new Vector2(0f, 0f), false))
        {
            if (body)
            {
                switch (_view)
                {
                    case View.Sparks:
                        _sparks.Draw(ctx);
                        break;
                    case View.Currencies:
                        _currencies.Draw(ctx);
                        break;
                    case View.Earn:
                        _earn.Draw(ctx);
                        break;
                    case View.History:
                        _history.Draw(ctx);
                        break;
                }
            }
        }
        PopScrollbarStyle();
    }

    public void OnIntent(OsIntent intent)
    {
        if (intent.Type is not (OsIntents.WalletOpen or OsIntents.WalletEarn))
        {
            return;
        }
        _returnApp = OsIntents.TryGetReturnApp(intent, out var caller) ? caller : null;
        if (intent.Type == OsIntents.WalletEarn)
        {
            _view = View.Earn;
            _earn.Show(_sparks.Wallet);
            if (_sparks.Wallet is null)
            {
                _sparks.Refresh();
            }
        }
        else
        {
            _view = View.Sparks;
            _sparks.OnShow();
        }
    }

    /// <summary>The one-click way home when another app sent the user here.</summary>
    /// <summary>The Store's blue, repeated here on purpose. Apps never reference each other, so coming
    /// back from a store errand means wearing the caller's colour by hand; it is used ONLY for that pill and
    /// only when the Store is who sent us.</summary>
    private static readonly Vector4 StoreBlue = new(0.45f, 0.65f, 1.00f, 1f);
    private static readonly Vector4 StoreSurface = new(0.063f, 0.094f, 0.180f, 1f);

    private void DrawHeader(OsAppContext ctx)
    {
        var t = ThemeService.Current;
        ImGui.Dummy(new Vector2(0f, Px(10f)));
        var originX = ImGui.GetWindowPos().X;
        var rowTop = ImGui.GetCursorScreenPos().Y;

        var title = Loc.T(_view switch
        {
            View.Earn => "os.wallet_earn_title",
            View.History => "os.wallet_history_title",
            _ => "os.app_wallet",
        });
        float titleH;
        using (ctx.TitleFont?.Push())
        {
            titleH = ImGui.CalcTextSize(title).Y;
        }
        var rowH = MathF.Max(titleH, Px(PillHeight));
        var centerY = rowTop + rowH * 0.5f;

        // Coming back from an errand leads the header, in the same spot the caller put it, so the way out
        // does not move under the user as they hop between the two apps.
        var titleX = Px(PadX);
        if (_returnApp is not null && _view is View.Sparks or View.Currencies)
        {
            var caller = CallerApp();
            var tooltip = caller is { } c
                ? Loc.T("os.wallet_back_app", c.Name)
                : Loc.T("os.wallet_back_app_generic");
            var pos = new Vector2(originX + Px(PadX), centerY - Px(PillHeight) * 0.5f);
            if (DrawReturnPill(pos, tooltip, caller?.Icon ?? FontAwesomeIcon.ArrowLeft, out var pillW))
            {
                GoBackToCaller();
            }
            titleX += pillW + Px(10f);
        }

        ImGui.SetCursorScreenPos(new Vector2(originX + titleX, centerY - titleH * 0.5f));
        using (ctx.TitleFont?.Push())
        {
            ImGui.TextColored(t.AccentLight, title);
        }

        DrawMenu(centerY);
        ImGui.SetCursorScreenPos(new Vector2(originX, rowTop + rowH));
        ImGui.Dummy(new Vector2(0f, Px(6f)));
    }

    private const float PillHeight = 30f;

    /// <summary>A chevron plus the caller's own glyph, shaped and placed exactly like the pill the Store
    /// draws, so the control does not jump when the user crosses between the apps.</summary>
    private bool DrawReturnPill(Vector2 pos, string tooltip, FontAwesomeIcon icon, out float width)
    {
        var dl = ImGui.GetWindowDrawList();
        var height = Px(PillHeight);
        width = Px(54f);
        ImGui.SetCursorScreenPos(pos);
        var pressed = ImGui.InvisibleButton("##walletReturn", new Vector2(width, height));
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            HandOnHover();
            ImGui.SetTooltip(tooltip);
        }

        var fromStore = _returnApp == "store";
        var accent = fromStore ? StoreBlue : ThemeService.Current.AccentLight;
        var fill = fromStore ? StoreSurface : ThemeService.Current.AccentDark;
        var br = pos + new Vector2(width, height);
        dl.AddRectFilled(pos, br,
            ImGui.ColorConvertFloat4ToU32(fill with { W = hovered ? 0.98f : 0.86f }), height * 0.5f);
        dl.AddRect(pos, br, ImGui.ColorConvertFloat4ToU32(accent with { W = hovered ? 0.9f : 0.4f }),
            height * 0.5f, ImDrawFlags.RoundCornersAll, Px(1.3f));

        var tint = hovered
            ? ImGui.ColorConvertFloat4ToU32(accent)
            : ImGui.GetColorU32(UiColors.Body);
        IconDraw.AddCentered(dl, FontAwesomeIcon.ChevronLeft, Px(11f),
            pos + new Vector2(Px(16f), height * 0.5f), tint);
        IconDraw.AddCentered(dl, icon, Px(13f), pos + new Vector2(Px(37f), height * 0.5f), tint);
        return pressed;
    }

    private void DrawMenu(float centerY)
    {
        const string popupId = "##walletMenu";
        var menuTL = AppHeader.DrawMenuButton(ImGui.GetWindowSize().X, PadX, popupId, centerY: centerY);
        var open = AppHeader.BeginMenuPopup(menuTL, popupId);
        if (open)
        {
            var tour = Loc.T("os.wallet_menu_tour");
            var refresh = Loc.T("os.wallet_menu_refresh");
            var w = AppHeader.MenuWidth(tour, refresh);
            var rowH = AppHeader.MenuRowHeight();

            if (AppHeader.MenuRow(FontAwesomeIcon.Wallet, tour, w, rowH))
            {
                _view = View.Tour;
                _tour.OnShow();
                ImGui.CloseCurrentPopup();
            }
            if (AppHeader.MenuRow(FontAwesomeIcon.SyncAlt, refresh, w, rowH))
            {
                RefreshActive();
                ImGui.CloseCurrentPopup();
            }
        }
        AppHeader.EndMenuPopup(open);
    }

    private void DrawTabs(OsAppContext ctx)
    {
        var t = ThemeService.Current;
        var winW = ImGui.GetWindowSize().X;
        var barW = winW - Px(PadX) * 2f;
        var barH = Px(34f);
        ImGui.SetCursorPosX(Px(PadX));
        var tl = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(tl, tl + new Vector2(barW, barH), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.06f)),
            barH * 0.5f);

        var halfW = barW * 0.5f;
        DrawTab(dl, tl, new Vector2(halfW, barH), Loc.T("os.wallet_tab_sparks"), _view == View.Sparks, () =>
        {
            if (_view != View.Sparks)
            {
                _view = View.Sparks;
                _sparks.OnShow();
            }
        });
        DrawTab(dl, tl + new Vector2(halfW, 0f), new Vector2(halfW, barH), Loc.T("os.wallet_tab_currencies"),
            _view == View.Currencies, () =>
        {
            if (_view != View.Currencies)
            {
                _view = View.Currencies;
                _currencies.OnShow();
            }
        });

        ImGui.SetCursorScreenPos(new Vector2(ImGui.GetWindowPos().X, tl.Y + barH));
        ImGui.Dummy(new Vector2(0f, Px(8f)));
    }

    private static void DrawTab(ImDrawListPtr dl, Vector2 tl, Vector2 size, string label, bool active, Action select)
    {
        var t = ThemeService.Current;
        ImGui.SetCursorScreenPos(tl);
        var clicked = ImGui.InvisibleButton($"##walletTab{label}", size);
        HandOnHover();
        var hovered = ImGui.IsItemHovered();
        if (active)
        {
            dl.AddRectFilled(tl + new Vector2(Px(3f), Px(3f)), tl + size - new Vector2(Px(3f), Px(3f)),
                ImGui.GetColorU32(t.Accent with { W = 0.28f }), (size.Y - Px(6f)) * 0.5f);
        }
        var labelSz = ImGui.CalcTextSize(label);
        dl.AddText(tl + (size - labelSz) * 0.5f,
            ImGui.GetColorU32(active ? UiColors.Body : hovered ? UiColors.Body with { W = 0.8f } : UiColors.Hint),
            label);
        if (clicked)
        {
            select();
        }
    }

    private void RefreshActive()
    {
        switch (_view)
        {
            case View.Currencies:
                _currencies.Refresh();
                break;
            case View.History:
                _history.Refresh();
                break;
            default:
                _sparks.Refresh();
                break;
        }
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

    private void FinishTour()
    {
        _tourSeen = true;
        _storage.Set("tourSeen", (bool?)true);
        _view = View.Sparks;
        _sparks.OnShow();
    }
}
