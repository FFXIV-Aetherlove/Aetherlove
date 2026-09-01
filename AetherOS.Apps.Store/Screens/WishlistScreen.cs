using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Shared.Store;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Store;

/// <summary>The wishlist: everything the user parked for later, one row each. A row opens the product;
/// the star beside it takes the product back off the list. Nothing here is bought, so there is no total
/// and no checkout, only the live price so a sale is visible from the list.</summary>
internal sealed class WishlistScreen(
    StoreState state, StoreMediaCache media, StoreWishlist wishlist, Action backHome, Action<Guid> openDetail)
{
    private const float PadX = 16f;

    private readonly EntranceAnimation _entrance = new();
    private readonly Dictionary<Guid, StoreProductDto> _products = [];
    private int _generation;

    public void Show()
    {
        _entrance.Arm();
        Refresh();
    }

    /// <summary>Fetches a fresh DTO per entry so prices and owned states are live, and drops entries whose
    /// products have since vanished from the catalog.</summary>
    private void Refresh()
    {
        var generation = Interlocked.Increment(ref _generation);
        var ids = wishlist.Ids.ToArray();
        _ = Task.Run(async () =>
        {
            var fetched = new Dictionary<Guid, StoreProductDto>();
            var gone = new List<Guid>();
            foreach (var id in ids)
            {
                var product = await state.FindFreshAsync(id).ConfigureAwait(false);
                if (product is null)
                {
                    gone.Add(id);
                }
                else
                {
                    fetched[id] = product;
                }
            }
            if (generation != Volatile.Read(ref _generation))
            {
                return;
            }
            _products.Clear();
            foreach (var (id, product) in fetched)
            {
                _products[id] = product;
            }
            if (gone.Count > 0)
            {
                wishlist.RemoveRange(gone);
            }
        });
    }

    public void Draw(OsAppContext ctx)
    {
        _entrance.BeginFrame();
        var winW = ImGui.GetWindowSize().X;
        var ids = wishlist.Ids;

        ImGui.Dummy(new Vector2(0f, Px(4f)));

        if (ids.Count == 0)
        {
            DrawEmpty(winW);
            _entrance.EndFrame();
            return;
        }

        foreach (var id in ids.ToArray())
        {
            DrawRow(ctx, winW, id);
        }
        ImGui.Dummy(new Vector2(0f, Px(16f)));
        _entrance.EndFrame();
    }

    private void DrawEmpty(float winW)
    {
        var dl = ImGui.GetWindowDrawList();
        ImGui.Dummy(new Vector2(0f, Px(56f)));
        IconDraw.AddCentered(dl, FontAwesomeIcon.Star, Px(38f),
            new Vector2(ImGui.GetWindowPos().X + winW * 0.5f, ImGui.GetCursorScreenPos().Y + Px(10f)),
            OsDrawShared.White(0.14f));
        ImGui.Dummy(new Vector2(0f, Px(44f)));
        StoreFx.CenterLine(Loc.T("os.store_wishlist_empty"), winW, UiColors.Body);
        ImGui.Dummy(new Vector2(0f, Px(4f)));
        StoreFx.CenterWrapped(Loc.T("os.store_wishlist_empty_hint"), winW, UiColors.Hint, winW - (Px(PadX) * 2f));
        ImGui.Dummy(new Vector2(0f, Px(14f)));
        var btnW = Px(180f);
        ImGui.SetCursorPosX((winW - btnW) * 0.5f);
        if (StoreUi.Button(Loc.T("os.store_cart_browse"), btnW))
        {
            backHome();
        }
    }

    private void DrawRow(OsAppContext ctx, float winW, Guid productId)
    {
        var dl = ImGui.GetWindowDrawList();
        ImGui.SetCursorPosX(Px(PadX));
        var tl = ImGui.GetCursorScreenPos();
        var rowW = winW - Px(PadX) * 2f;
        var rowH = Px(64f);

        _products.TryGetValue(productId, out var product);

        // The remove star is submitted before the row's own open target: first-submitted-wins.
        var starC = new Vector2(tl.X + rowW - Px(20f), tl.Y + rowH * 0.5f);
        ImGui.SetCursorScreenPos(starC - new Vector2(Px(12f), Px(12f)));
        var removed = ImGui.InvisibleButton($"##wishRemove{productId:N}", new Vector2(Px(24f), Px(24f)));
        var starHovered = ImGui.IsItemHovered();
        if (starHovered)
        {
            HandOnHover();
            ImGui.SetTooltip(Loc.T("os.store_wishlist_remove"));
        }

        ImGui.SetCursorScreenPos(tl);
        var opened = ImGui.InvisibleButton($"##wishRow{productId:N}", new Vector2(rowW, rowH));
        var rowHovered = ImGui.IsItemHovered();
        if (rowHovered)
        {
            HandOnHover();
        }

        dl.AddRectFilled(tl, tl + new Vector2(rowW, rowH),
            OsDrawShared.White(rowHovered ? 0.09f : 0.05f), Px(12f));

        var thumbSide = rowH - Px(12f);
        var thumbTl = tl + new Vector2(Px(6f), Px(6f));
        var visual = product is { HasImage: true } ? media.Get(productId, product.ImageVersion) : null;
        if (visual?.Tex?.GetWrapOrDefault() is { } wrap)
        {
            var (uv0, uv1) = OsDrawShared.CoverUv(wrap.Width, wrap.Height, thumbSide, thumbSide);
            dl.AddImageRounded(wrap.Handle, thumbTl, thumbTl + new Vector2(thumbSide, thumbSide),
                uv0, uv1, 0xFFFFFFFFu, Px(9f));
        }
        else if (product is not null
            && BundleArt.Draw(dl, media, product, thumbTl, new Vector2(thumbSide, thumbSide), Px(9f)))
        {
        }
        else
        {
            dl.AddRectFilled(thumbTl, thumbTl + new Vector2(thumbSide, thumbSide), OsDrawShared.White(0.07f), Px(9f));
            if (product is not null)
            {
                IconDraw.AddCentered(dl, StoreCard.KindGlyph(product.ItemKind), Px(16f),
                    thumbTl + new Vector2(thumbSide * 0.5f, thumbSide * 0.5f), OsDrawShared.White(0.3f));
            }
        }

        var textX = tl.X + thumbSide + Px(14f);
        dl.AddText(new Vector2(textX, tl.Y + Px(8f)), ImGui.GetColorU32(UiColors.Body),
            TruncateToWidth(product is null ? "…" : StoreLoc.Name(product), rowW - thumbSide - Px(64f)));
        if (product is not null)
        {
            StoreChips.Price(dl, new Vector2(textX, tl.Y + Px(28f)),
                product.DiscountedPriceSparks, product.PriceSparks, 0.95f);
        }

        IconDraw.AddCentered(dl, FontAwesomeIcon.Star, Px(13f), starC,
            ImGui.GetColorU32(starHovered ? StoreChips.GoldColor : UiColors.Hint));

        ImGui.SetCursorScreenPos(new Vector2(ImGui.GetWindowPos().X, tl.Y + rowH));
        ImGui.Dummy(new Vector2(0f, Px(8f)));

        if (removed)
        {
            wishlist.Remove(productId);
        }
        else if (opened)
        {
            openDetail(productId);
        }
        _ = ctx;
    }
}
