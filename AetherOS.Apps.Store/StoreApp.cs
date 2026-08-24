using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using System.Threading;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Shared.Store;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Store;

/// <summary>The Store: browse the catalog, fill the bag, spend sparks. The server owns every price and
/// every grant; this app is the shop window and the ceremony around handing coins over.</summary>
public sealed class StoreApp : IAetherApp
{
    internal static readonly Vector4 TileTopColor = new(0.93f, 0.36f, 0.62f, 1f);
    internal static readonly Vector4 TileBottomColor = new(0.42f, 0.12f, 0.55f, 1f);

    internal enum View { Home, Browse, Detail, Bag, Success }

    private const float PadX = 16f;

    private readonly Func<string> _name;
    private readonly IStoreHost _host;
    private readonly StoreState _state;
    private readonly StoreBag _bag;
    private readonly StoreMediaCache _media;
    private readonly StoreMediaCache _backgrounds;
    private readonly HomeScreen _home;
    private readonly BrowseScreen _browse;
    private readonly DetailScreen _detail;
    private readonly BagScreen _bagScreen;
    private readonly SuccessScreen _success;

    private View _view = View.Home;
    private View _detailReturn = View.Home;

    // The animated balance chip: the shown value chases the real one.
    private long _shownBalance;
    private double _balanceAnimStamp = -1.0;
    private long _balanceAnimFrom;
    private double _bagBounceStamp = -10.0;

    public StoreApp(Func<string> name, IStoreHost host, IAppCapabilities caps)
    {
        _name = name;
        _host = host;
        _state = new StoreState(host);
        _bag = new StoreBag(caps.Storage("store"));
        _media = new StoreMediaCache(host, System.IO.Path.Combine(caps.Storage("store").Directory, "MediaCache"));
        // Theme wallpapers key on the same product id as the shelf art, so they need their own cache.
        _backgrounds = new StoreMediaCache(
            host, System.IO.Path.Combine(caps.Storage("store").Directory, "MediaCache", "bg"));
        _home = new HomeScreen(_state, _media, _bag, caps.Storage("store"), OpenDetail, OpenBrowse, AddedToBag);
        _browse = new BrowseScreen(_state, _media, OpenDetail);
        _detail = new DetailScreen(_host, _state, _media, _backgrounds, _bag, AddedToBag, OpenBrowse);
        _bagScreen = new BagScreen(_host, _state, _media, _bag, BackHome, ShowSuccess, OpenWaysToEarn);
        _success = new SuccessScreen(_host, _media, BackHome);
    }

    public string Id => "store";
    public string Name => _name();
    public FontAwesomeIcon Icon => FontAwesomeIcon.ShoppingBag;
    public Vector4 TileTop => TileTopColor;
    public Vector4 TileBottom => TileBottomColor;
    public int Badge => 0;
    public bool HasSurface => true;

    /// <summary>A shop with no server is just a sad room; the shell's offline gate says it better.</summary>
    public bool RequiresConnection => true;

    public bool UsesAccount => true;

    public System.Collections.Generic.IReadOnlyDictionary<string, System.Collections.Generic.IReadOnlyDictionary<string, string>> Strings => Localization.AppStrings.Packs;

    public void Open()
    {
    }

    public void OnForeground()
    {
        _state.RefreshFrontIfStale();
        switch (_view)
        {
            case View.Home:
                _home.OnShow();
                break;
            case View.Browse:
                _browse.OnShow();
                break;
            case View.Bag:
                // Coming back from the wallet, the balance and any freshly-owned items must be re-read.
                _bagScreen.Show();
                break;
        }
    }

    public void OnIntent(OsIntent intent)
    {
        if (intent.Type == OsIntents.StoreProduct
            && OsIntents.TryGetStoreProduct(intent, out var itemKind, out var itemRef))
        {
            OpenProductByRef((StoreItemKind)itemKind, itemRef);
            return;
        }
        if (intent.Type != OsIntents.StoreOpen || !OsIntents.TryGetPath(intent, out var path))
        {
            return;
        }
        // "categorykey" or "categorykey/searchseed"; unknown keys land on Home.
        var slash = path.IndexOf('/');
        var categoryKey = slash < 0 ? path : path[..slash];
        var seed = slash < 0 ? null : path[(slash + 1)..];
        _view = View.Browse;
        _browse.OpenDeepLink(categoryKey, seed);
    }

    /// <summary>Only the server knows which row carries a given kind and ref, so the page opens once the
    /// lookup lands. The answer is parked rather than acted on, because it arrives on a thread-pool
    /// continuation and every view switch belongs to the draw thread.</summary>
    private void OpenProductByRef(StoreItemKind kind, string itemRef)
    {
        _ = Task.Run(async () =>
        {
            var product = await _host.GetStoreProductByRefAsync(kind, itemRef).ConfigureAwait(false);
            if (product is not null)
            {
                Interlocked.Exchange(ref _pendingDeepLink, product);
            }
        });
    }

    private StoreProductDto? _pendingDeepLink;

    private void OpenDetail(Guid productId)
    {
        _detailReturn = _view == View.Browse ? View.Browse : View.Home;
        _view = View.Detail;
        _detail.Show(productId);
    }

    private void OpenBrowse(BrowseScreen.Seed seed)
    {
        _view = View.Browse;
        _browse.Open(seed);
    }

    private void BackHome()
    {
        _view = View.Home;
        _home.OnShow();
        _state.RefreshFrontIfStale();
    }

    private void BackFromDetail()
    {
        // A product opened from inside another one (a bundle's contents) owes a step back to its parent
        // before it owes one to the shelf that started the errand.
        if (_detail.TryGoBack())
        {
            return;
        }
        _view = _detailReturn;
        if (_view == View.Home)
        {
            _home.OnShow();
        }
        else
        {
            _browse.OnShow();
        }
    }

    private void OpenBag()
    {
        _view = View.Bag;
        _bagScreen.Show();
    }

    /// <summary>Both wallet hops carry this app's id, so the wallet can offer one click back to the exact
    /// view the user left (the bag keeps its lines while they go earn).</summary>
    private void OpenWallet(OsAppContext ctx) =>
        ctx.Shell.SendIntent("wallet", OsIntents.CreateReturn(OsIntents.WalletOpen, Id));

    private void OpenWaysToEarn(OsAppContext ctx) =>
        ctx.Shell.SendIntent("wallet", OsIntents.CreateReturn(OsIntents.WalletEarn, Id));

    private void AddedToBag() => _bagBounceStamp = ImGui.GetTime();

    private void ShowSuccess(SuccessScreen.Celebration celebration)
    {
        _view = View.Success;
        _success.Show(celebration);
        _state.MarkFrontStale();
    }

    public void Draw(OsAppContext ctx)
    {
        if (Interlocked.Exchange(ref _pendingDeepLink, null) is { } deepLink)
        {
            _detailReturn = View.Home;
            _view = View.Detail;
            _detail.Show(deepLink.Id);
        }

        if (_state.Balance is { } balance)
        {
            AnimateBalance(ctx, balance);
        }

        if (_view == View.Success)
        {
            _success.Draw(ctx);
            return;
        }

        DrawHeader(ctx);

        // The bar owns navigation on the two browsing views; Detail and Bag are their own errands and keep
        // their back pills, so giving them a bar would offer two conflicting ways out of the same screen.
        var showBar = _view is View.Home or View.Browse;
        var barH = showBar ? Px(StoreBottomBar.Height) : 0f;

        PushScrollbarStyle(StorePalette.Blue with { W = 0.85f }, StorePalette.BlueLight, StorePalette.BlueDark);
        using (var body = ImRaii.Child("##storeBody", new Vector2(0f, -barH), false))
        {
            if (body)
            {
                switch (_view)
                {
                    case View.Home:
                        _home.Draw(ctx);
                        break;
                    case View.Browse:
                        _browse.Draw(ctx);
                        break;
                    case View.Detail:
                        _detail.Draw(ctx);
                        break;
                    case View.Bag:
                        _bagScreen.Draw(ctx);
                        break;
                }
            }
        }
        PopScrollbarStyle();

        if (showBar
            && StoreBottomBar.Draw(ctx, _state.Front, _view == View.Home, _browse.RootCategoryId) is { } pick)
        {
            if (pick.Home)
            {
                BackHome();
            }
            else
            {
                OpenBrowse(new BrowseScreen.Seed(pick.CategoryId, null, null, StoreSort.Featured));
            }
        }
    }

    private void DrawHeader(OsAppContext ctx)
    {
        ImGui.Dummy(new Vector2(0f, Px(10f)));
        var winW = ImGui.GetWindowSize().X;
        var originX = ImGui.GetWindowPos().X;
        var rowTop = ImGui.GetCursorScreenPos().Y;

        var title = Loc.T(_view switch
        {
            View.Browse => "os.store_browse_title",
            View.Detail => "os.store_detail_title",
            View.Bag => "os.store_bag_title",
            _ => "os.app_store",
        });
        float titleH;
        using (ctx.HeadingFont?.Push())
        {
            titleH = ImGui.CalcTextSize(title).Y;
        }

        // One row, measured up front. Everything centres on it, because deriving the right-hand controls'
        // Y by walking back from the cursor breaks the moment anything else joins the row.
        var rowH = MathF.Max(titleH, Px(StoreUi.BackPillHeight));
        var centerY = rowTop + rowH * 0.5f;

        var titleX = Px(PadX);
        if (_view is View.Detail or View.Bag)
        {
            var pillPos = new Vector2(originX + Px(PadX), centerY - Px(StoreUi.BackPillHeight) * 0.5f);
            var upToBundle = _view == View.Detail && _detail.CanGoBack;
            if (StoreUi.BackPill(
                pillPos,
                Loc.T(upToBundle ? "os.store_back_bundle" : "os.store_back"),
                upToBundle ? FontAwesomeIcon.Gifts : FontAwesomeIcon.ShoppingBag,
                out var pillW))
            {
                if (_view == View.Detail)
                {
                    BackFromDetail();
                }
                else
                {
                    BackHome();
                }
            }
            titleX += pillW + Px(10f);
        }

        ImGui.SetCursorScreenPos(new Vector2(originX + titleX, centerY - titleH * 0.5f));
        using (ctx.HeadingFont?.Push())
        {
            ImGui.TextColored(StorePalette.BlueLight, title);
        }

        DrawBalanceChip(ctx, winW, centerY);
        DrawBagButton(ctx, winW, centerY);

        ImGui.SetCursorScreenPos(new Vector2(originX, rowTop + rowH));
        ImGui.Dummy(new Vector2(0f, Px(6f)));
    }

    /// <summary>The gold sparks pill, always visible: the number chases the real balance with a count
    /// animation so spending feels like spending.</summary>
    private void DrawBalanceChip(OsAppContext ctx, float winW, float centerY)
    {
        var dl = ImGui.GetWindowDrawList();
        var label = _state.Balance is null ? "···" : _shownBalance.ToString("N0");
        var labelSz = ImGui.CalcTextSize(label);
        var iconPx = Px(12f);
        var padIn = Px(9f);
        var chipH = Px(24f);
        var chipW = padIn * 2f + iconPx + Px(5f) + labelSz.X;
        var tl = new Vector2(ImGui.GetWindowPos().X + winW - Px(PadX) - Px(30f) - chipW, centerY - chipH * 0.5f);

        ImGui.SetCursorScreenPos(tl);
        if (ImGui.InvisibleButton("##storeBalance", new Vector2(chipW, chipH)))
        {
            OpenWallet(ctx);
        }
        if (ImGui.IsItemHovered())
        {
            HandOnHover();
            ImGui.SetTooltip(Loc.T("os.store_balance_tip"));
        }

        OsDrawShared.RoundedGradient(dl, tl, tl + new Vector2(chipW, chipH), chipH * 0.5f,
            new Vector4(0.95f, 0.71f, 0.24f, 1f), new Vector4(0.62f, 0.35f, 0.07f, 1f));
        IconDraw.AddCentered(dl, FontAwesomeIcon.Bolt, iconPx,
            tl + new Vector2(padIn + iconPx * 0.5f, chipH * 0.5f), 0xFFFFFFFFu);
        dl.AddText(tl + new Vector2(padIn + iconPx + Px(5f), (chipH - labelSz.Y) * 0.5f), 0xFFFFFFFFu, label);
        StoreFx.Sweep(dl, tl, tl + new Vector2(chipW, chipH), 1.7f, ctx.ReduceMotion);
    }

    private void DrawBagButton(OsAppContext ctx, float winW, float centerY)
    {
        var dl = ImGui.GetWindowDrawList();
        var side = Px(24f);
        var tl = new Vector2(ImGui.GetWindowPos().X + winW - Px(PadX) - side, centerY - side * 0.5f);

        ImGui.SetCursorScreenPos(tl);
        if (ImGui.InvisibleButton("##storeBag", new Vector2(side, side)))
        {
            OpenBag();
        }
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            HandOnHover();
            ImGui.SetTooltip(Loc.T("os.store_bag_title"));
        }

        var center = tl + new Vector2(side * 0.5f, side * 0.5f);
        dl.AddCircleFilled(center, side * 0.5f, OsDrawShared.White(hovered ? 0.16f : 0.08f));
        IconDraw.AddCentered(dl, FontAwesomeIcon.ShoppingBag, Px(13f), center,
            ImGui.GetColorU32(UiColors.Body));

        var count = _bag.Count;
        if (count > 0)
        {
            // A short scale-bounce whenever something lands in the bag.
            var sinceBounce = (float)(ImGui.GetTime() - _bagBounceStamp);
            var scale = !ctx.ReduceMotion && sinceBounce < 0.3f
                ? 1f + 0.5f * MathF.Sin(sinceBounce / 0.3f * MathF.PI)
                : 1f;
            var badgeC = tl + new Vector2(side - Px(2f), Px(2f));
            var badgeR = Px(7f) * scale;
            dl.AddCircleFilled(badgeC, badgeR, ImGui.GetColorU32(new Vector4(0.9f, 0.22f, 0.33f, 1f)));
            var text = count > 9 ? "9+" : count.ToString();
            var textSz = ImGui.CalcTextSize(text);
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.72f * scale,
                badgeC - new Vector2(textSz.X * 0.36f * scale, textSz.Y * 0.36f * scale), 0xFFFFFFFFu, text);
        }
    }

    private void AnimateBalance(OsAppContext ctx, long balance)
    {
        if (_balanceAnimStamp < 0.0)
        {
            _shownBalance = balance;
            _balanceAnimStamp = 0.0;
            return;
        }
        if (_shownBalance == balance)
        {
            return;
        }
        if (_balanceAnimStamp == 0.0 || ctx.ReduceMotion)
        {
            if (ctx.ReduceMotion)
            {
                _shownBalance = balance;
                return;
            }
            _balanceAnimStamp = ImGui.GetTime();
            _balanceAnimFrom = _shownBalance;
        }
        var progress = StoreFx.EaseOut(Math.Clamp((float)(ImGui.GetTime() - _balanceAnimStamp) / 0.6f, 0f, 1f));
        _shownBalance = _balanceAnimFrom + (long)((balance - _balanceAnimFrom) * progress);
        if (progress >= 1f)
        {
            _shownBalance = balance;
            _balanceAnimStamp = 0.0;
        }
    }
}
