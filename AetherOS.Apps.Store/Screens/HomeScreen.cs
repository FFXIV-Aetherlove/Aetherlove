using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Shared.Store;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Store;

/// <summary>The storefront: hero carousel, the New, Most-bought and Bundles rails, the curated collection
/// cards, and a picture menu of categories to close. Deliberately dense; the shop should feel like walking
/// into the Gold Saucer.</summary>
internal sealed class HomeScreen(
    StoreState state, StoreMediaCache media, StoreCart cart, IAppStorage storage,
    Action<Guid> openDetail, Action<BrowseScreen.Seed> openBrowse, Action addedToCart, Action openBoosts)
{
    private const float PadX = 16f;
    private const float CardW = 120f;
    private const float CardH = 158f;
    private const float WideCardW = 196f;
    private const string SupporterCardDismissedKey = "supporterCardDismissed";

    private readonly EntranceAnimation _entrance = new();
    private readonly HeroCarousel _carousel = new(media);
    private double _revealStamp = -1.0;
    private StoreFrontDto? _lastFront;
    private bool? _supporterCardDismissed;

    public void OnShow()
    {
        _entrance.Arm();
        _revealStamp = -1.0;
    }

    public void Draw(OsAppContext ctx)
    {
        _entrance.BeginFrame();
        var winW = ImGui.GetWindowSize().X;
        var front = state.Front;

        // Submitted before anything else, so it lands under every card and rail on this draw list.
        StoreFx.StarField(ImGui.GetWindowDrawList(), ImGui.GetWindowPos(), ImGui.GetWindowSize(), ctx.ReduceMotion);

        if (front is null)
        {
            DrawLoadingOrOffline(ctx, winW);
            _entrance.EndFrame();
            return;
        }
        if (!ReferenceEquals(front, _lastFront))
        {
            _lastFront = front;
            _carousel.SetContent(front);
            _revealStamp = ImGui.GetTime();
        }

        DrawSupporterCard(ctx, winW, front.SupporterDiscountPercent);
        DrawBoostsCard(winW);

        var section = 0;
        WithReveal(ctx, section++, () =>
        {
            var clicked = _carousel.Draw(ctx, winW);
            switch (clicked)
            {
                case HeroCarousel.FeaturedBanner featured:
                    openDetail(featured.Product.Id);
                    break;
                case HeroCarousel.SaleBanner:
                    openBrowse(new BrowseScreen.Seed(null, null, null, StoreSort.Featured, OnSaleOnly: true));
                    break;
            }
        });

        if (front.NewItems.Length > 0)
        {
            WithReveal(ctx, section++, () =>
            {
                if (RailHeader.Draw("new", winW, FontAwesomeIcon.Star, Loc.T("os.store_rail_new"),
                        StorePalette.Blue, ctx.ReduceMotion))
                {
                    openBrowse(new BrowseScreen.Seed(null, null, null, StoreSort.Newest));
                }
                DrawShelf(ctx, "##shelfNew", winW, front.NewItems, wideFirst: false);
            });
        }

        if (front.MostBought.Length > 0)
        {
            WithReveal(ctx, section++, () =>
            {
                if (RailHeader.Draw("hot", winW, FontAwesomeIcon.Fire, Loc.T("os.store_rail_popular"),
                        StoreChips.SaleColor, ctx.ReduceMotion))
                {
                    openBrowse(new BrowseScreen.Seed(null, null, null, StoreSort.MostBought));
                }
                DrawShelf(ctx, "##shelfHot", winW, front.MostBought, wideFirst: true);
            });
        }

        if (front.Bundles.Length > 0)
        {
            WithReveal(ctx, section++, () =>
            {
                if (RailHeader.Draw("bundles", winW, FontAwesomeIcon.Gifts, Loc.T("os.store_rail_bundles"),
                        StoreChips.GoldColor, ctx.ReduceMotion))
                {
                    openBrowse(new BrowseScreen.Seed(null, "bundle", null, StoreSort.Featured));
                }
                DrawShelf(ctx, "##shelfBundles", winW, front.Bundles, wideFirst: true);
            });
        }

        // The editorial layer: hand-picked shelves, each its own card.
        foreach (var collection in front.Collections)
        {
            var pinned = collection;
            WithReveal(ctx, section++, () =>
            {
                var result = CollectionCard.Draw(ctx, pinned, media, cart, winW);
                if (result.AddToCart is { } product)
                {
                    cart.Add(product.Id, 1);
                    addedToCart();
                }
                else if (result.OpenProduct is { } id)
                {
                    openDetail(id);
                }
                else if (result.OpenCollection)
                {
                    openBrowse(new BrowseScreen.Seed(null, null, null, StoreSort.Featured, CollectionId: pinned.Id));
                }
            });
        }

        // No category menu here: the bottom bar owns category switching, and a second copy on this page
        // would be one more place to keep in step with it.
        ImGui.Dummy(new Vector2(0f, Px(16f)));
        _entrance.EndFrame();
    }

    /// <summary>The supporter pitch strip above the carousel, shown to everyone until dismissed. Tapping it
    /// opens the supporter page in OS Settings with a back pill straight back to the store.</summary>
    private void DrawSupporterCard(OsAppContext ctx, float winW, int percent)
    {
        _supporterCardDismissed ??= storage.Get<bool>(SupporterCardDismissedKey);
        if (_supporterCardDismissed == true || percent <= 0)
        {
            return;
        }

        var dl = ImGui.GetWindowDrawList();
        var pad = Px(PadX);
        var w = winW - pad * 2f;
        var iconInset = Px(34f);
        var closeInset = Px(30f);
        var text = string.Format(Loc.T("os.store_sup_card"), percent);
        var textW = w - iconInset - closeInset;
        var textH = ImGui.CalcTextSize(text, false, textW).Y;
        var cardH = Math.Max(Px(38f), textH + Px(16f));

        var cursorBefore = ImGui.GetCursorPos();
        var tl = ImGui.GetCursorScreenPos() + new Vector2(pad, 0f);
        var br = tl + new Vector2(w, cardH);
        dl.AddRectFilled(tl, br, ImGui.GetColorU32(UiColors.Patreon with { W = 0.16f }), Px(10f));
        dl.AddRect(tl, br, ImGui.GetColorU32(UiColors.Patreon with { W = 0.45f }), Px(10f), ImDrawFlags.None, Px(1f));
        IconDraw.AddCentered(dl, FontAwesomeIcon.HandHoldingHeart, Px(12f),
            new Vector2(tl.X + Px(18f), (tl.Y + br.Y) * 0.5f), ImGui.GetColorU32(UiColors.Patreon));

        ImGui.SetCursorScreenPos(new Vector2(tl.X + iconInset, tl.Y + (cardH - textH) * 0.5f));
        ImGui.PushTextWrapPos(tl.X + iconInset + textW);
        ImGui.TextColored(UiColors.Body, text);
        ImGui.PopTextWrapPos();

        // Dismiss first, the whole-card open target last: first-submitted-wins.
        var closeCenter = new Vector2(br.X - Px(16f), (tl.Y + br.Y) * 0.5f);
        ImGui.SetCursorScreenPos(closeCenter - new Vector2(Px(11f), Px(11f)));
        if (ImGui.InvisibleButton("##supCardClose", new Vector2(Px(22f), Px(22f))))
        {
            _supporterCardDismissed = true;
            storage.Set(SupporterCardDismissedKey, true);
        }
        HandOnHover();
        IconDraw.AddCentered(dl, FontAwesomeIcon.Times, Px(9f), closeCenter, ImGui.GetColorU32(UiColors.Hint));

        ImGui.SetCursorScreenPos(tl);
        if (ImGui.InvisibleButton("##supCardOpen", new Vector2(w, cardH)))
        {
            ctx.Shell.SendIntent("settings", OsIntents.CreateReturn(OsIntents.OpenSupporter, "store"));
        }
        HandOnHover();

        ImGui.SetCursorPos(new Vector2(cursorBefore.X, cursorBefore.Y + cardH + Px(10f)));
    }

    /// <summary>A one-line way into the boosts sheet, shown only while the account is actually holding one.
    /// A boost is bought here and spent on something in another app, so the shop owes the shortcut.</summary>
    private void DrawBoostsCard(float winW)
    {
        var boosts = state.Boosts;
        var owned = (boosts?.VenueBoosts ?? 0) + (boosts?.LevemeteBoosts ?? 0);
        if (owned <= 0)
        {
            return;
        }

        var dl = ImGui.GetWindowDrawList();
        var pad = Px(PadX);
        var w = winW - pad * 2f;
        var cardH = Px(38f);
        var accent = BoostFx.KeyColor(BoostStyle.Aurora);
        var cursorBefore = ImGui.GetCursorPos();
        var tl = ImGui.GetCursorScreenPos() + new Vector2(pad, 0f);
        var br = tl + new Vector2(w, cardH);

        ImGui.SetCursorScreenPos(tl);
        var clicked = ImGui.InvisibleButton("##storeBoostsCard", new Vector2(w, cardH));
        HandOnHover();

        dl.AddRectFilled(tl, br, ImGui.GetColorU32(accent with { W = 0.16f }), Px(10f));
        dl.AddRect(tl, br, ImGui.GetColorU32(accent with { W = 0.45f }), Px(10f), ImDrawFlags.None, Px(1f));
        IconDraw.AddCentered(dl, FontAwesomeIcon.Bolt, Px(12f),
            new Vector2(tl.X + Px(18f), (tl.Y + br.Y) * 0.5f), ImGui.GetColorU32(accent));
        var text = $"{Loc.T("os.boost_shelf")} · {owned}";
        dl.AddText(new Vector2(tl.X + Px(34f), (tl.Y + br.Y) * 0.5f - ImGui.GetTextLineHeight() * 0.5f),
            ImGui.GetColorU32(UiColors.Body), text);

        if (clicked)
        {
            openBoosts();
        }
        ImGui.SetCursorPos(new Vector2(cursorBefore.X, cursorBefore.Y + cardH + Px(10f)));
    }

    private void WithReveal(OsAppContext ctx, int index, Action draw)
    {
        var reveal = StoreChips.Reveal(_revealStamp, index, ctx);
        if (reveal >= 1f)
        {
            draw();
            return;
        }
        // Fade + a small upward slide per section as fresh data lands.
        using (ImRaii.PushStyle(ImGuiStyleVar.Alpha, Math.Max(0.01f, reveal)))
        {
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + Px(8f) * (1f - reveal));
            draw();
        }
    }

    private void DrawShelf(OsAppContext ctx, string childId, float winW, StoreProductDto[] items, bool wideFirst)
    {
        var cardH = Px(CardH);
        using var child = ImRaii.Child(childId, new Vector2(winW, cardH + Px(18f)), false,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        if (!child)
        {
            return;
        }

        var x = Px(PadX);
        var origin = ImGui.GetWindowPos();
        for (var i = 0; i < items.Length; i++)
        {
            var wide = wideFirst && i == 0;
            var w = Px(wide ? WideCardW : CardW);
            var tl = new Vector2(origin.X + x - ImGui.GetScrollX(), origin.Y + Px(2f));
            if (StoreCard.Draw(ctx, media, items[i], tl, new Vector2(w, cardH), i, wide))
            {
                openDetail(items[i].Id);
            }
            if (wide)
            {
                // The #1 ribbon on the lead card of the popular rail.
                var ribbon = "#1";
                var ribbonSz = ImGui.CalcTextSize(ribbon);
                var dl = ImGui.GetWindowDrawList();
                var pillTl = tl + new Vector2(w - ribbonSz.X - Px(18f), Px(6f));
                dl.AddRectFilled(pillTl, pillTl + ribbonSz + new Vector2(Px(10f), Px(4f)),
                    ImGui.GetColorU32(StoreChips.GoldColor), Px(8f));
                dl.AddText(pillTl + new Vector2(Px(5f), Px(2f)), OsDrawShared.Black(0.85f), ribbon);
            }
            x += w + Px(8f);
        }
        ImGui.SetCursorPos(new Vector2(x, Px(2f)));
        ImGui.Dummy(new Vector2(Px(1f), cardH));

        if (ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows | ImGuiHoveredFlags.AllowWhenBlockedByActiveItem)
            && ImGui.IsMouseDragging(ImGuiMouseButton.Left, 4f))
        {
            ImGui.SetScrollX(ImGui.GetScrollX() - ImGui.GetIO().MouseDelta.X);
        }
    }

    private void DrawLoadingOrOffline(OsAppContext ctx, float winW)
    {
        ImGui.Dummy(new Vector2(0f, Px(70f)));
        if (state.FrontLoading || !state.FrontFailed)
        {
            var center = new Vector2(ImGui.GetWindowPos().X + winW * 0.5f, ImGui.GetCursorScreenPos().Y + Px(16f));
            AetherLove.Widgets.LoadingSpinner.Draw(center, Px(14f), Px(3f), StorePalette.BlueU32);
            if (!state.FrontLoading)
            {
                state.RefreshFront();
            }
            return;
        }
        StoreFx.CenterWrapped(Loc.T("os.store_offline"), winW, UiColors.Body, winW - (Px(PadX) * 2f));
        ImGui.Dummy(new Vector2(0f, Px(10f)));
        var btnW = Px(150f);
        ImGui.SetCursorPosX((winW - btnW) * 0.5f);
        if (StoreUi.Button(Loc.T("os.store_retry"), btnW))
        {
            state.RefreshFront();
        }
        _ = ctx;
    }
}
