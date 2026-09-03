using System;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Shared.Store;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures.TextureWraps;

namespace AetherOS.Apps.Store;

/// <summary>An editorial shelf as one opaque card: the collection's banner with its eyebrow and headline
/// over a scrim, then a row per pick with art, name and a price pill that drops it straight in the cart.
/// Modelled on the App Store's curated lists.</summary>
internal static class CollectionCard
{
    private const float PadX = 16f;
    private const float BannerH = 116f;
    private const float RowH = 54f;

    /// <summary>How wide-to-tall a banner may be drawn: a strip can be no flatter than 4:1 and no squarer
    /// than 1.6:1, so a square upload cannot turn the card into a poster.</summary>
    private const float FlattestBanner = 4f;
    private const float SquarestBanner = 1.6f;

    /// <summary>How many picks the card lists before it hands over to the browse screen; a card that lists
    /// thirty rows is a page, not a shelf.</summary>
    private const int MaxRows = 5;

    internal sealed record Result(Guid? OpenProduct, StoreProductDto? AddToCart, bool OpenCollection = false);

    public static Result Draw(
        OsAppContext ctx, StoreCollectionDto collection, StoreMediaCache media, StoreCart cart, float winW)
    {
        var dl = ImGui.GetWindowDrawList();
        var cardW = winW - Px(PadX) * 2f;
        var visual = collection.HasImage ? media.GetCollection(collection.Id, collection.ImageVersion) : null;
        var wrap = visual?.Tex?.GetWrapOrDefault();
        var bannerH = wrap is null ? Px(BannerH) : BannerHeight(wrap.Width, wrap.Height, cardW);
        var shown = Math.Min(collection.Products.Length, MaxRows);
        var overflow = collection.Products.Length > MaxRows;
        var cardH = bannerH + Px(RowH) * (shown + (overflow ? 1 : 0)) + Px(10f);
        ImGui.SetCursorPosX(Px(PadX));
        var tl = ImGui.GetCursorScreenPos();
        var (top, bottom, accent) = StoreFx.CardColors(collection.AccentColor);

        dl.AddRectFilled(tl, tl + new Vector2(cardW, cardH), OsDrawShared.White(0.055f), Px(18f));
        var openCollection = DrawBanner(ctx, dl, collection, wrap, tl, cardW, bannerH, top, bottom, accent);

        Guid? open = null;
        StoreProductDto? add = null;
        var y = tl.Y + bannerH;
        for (var i = 0; i < shown; i++)
        {
            var product = collection.Products[i];
            var rowTl = new Vector2(tl.X, y);
            var result = DrawRow(ctx, dl, product, media, cart, rowTl, cardW, i, shown + (overflow ? 1 : 0));
            open ??= result.OpenProduct;
            add ??= result.AddToCart;
            y += Px(RowH);
        }
        if (overflow && DrawViewMore(dl, collection, new Vector2(tl.X, y), cardW, accent))
        {
            openCollection = true;
        }

        ImGui.SetCursorScreenPos(new Vector2(ImGui.GetWindowPos().X, tl.Y + cardH));
        ImGui.Dummy(new Vector2(0f, Px(14f)));
        return new Result(open, add, openCollection);
    }

    /// <summary>The last row when the card could not list everything: one wide button into the browse
    /// screen filtered to this collection, with the count the card left out.</summary>
    private static bool DrawViewMore(
        ImDrawListPtr dl, StoreCollectionDto collection, Vector2 tl, float cardW, Vector4 accent)
    {
        var size = new Vector2(cardW, Px(RowH));
        ImGui.SetCursorScreenPos(tl);
        var pressed = ImGui.InvisibleButton($"##collMore{collection.Id:N}", size);
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            HandOnHover();
            dl.AddRectFilled(tl, tl + size, OsDrawShared.White(0.04f), Px(18f), ImDrawFlags.RoundCornersBottom);
        }

        var label = Loc.T("os.store_collection_view_more", collection.Products.Length - MaxRows);
        var labelSz = ImGui.CalcTextSize(label);
        var iconPx = Px(11f);
        var gap = Px(6f);
        var startX = tl.X + (cardW - labelSz.X - iconPx - gap) * 0.5f;
        var color = ImGui.GetColorU32(accent with { W = hovered ? 1f : 0.9f });
        dl.AddText(new Vector2(startX, tl.Y + (size.Y - labelSz.Y) * 0.5f), color, label);
        IconDraw.AddCentered(dl, FontAwesomeIcon.ChevronRight, iconPx,
            new Vector2(startX + labelSz.X + gap + iconPx * 0.5f, tl.Y + size.Y * 0.5f), color);
        return pressed;
    }

    /// <summary>The banner is drawn whole, at the art's own ratio, rather than cover-cropped into a fixed
    /// strip: a banner is composed for its frame and a crop takes the edges off it. Bounded at both ends,
    /// and within the bounds the cover crop below is a no-op.</summary>
    private static float BannerHeight(int artW, int artH, float cardW)
    {
        if (artW <= 0 || artH <= 0)
        {
            return Px(BannerH);
        }
        var natural = cardW * artH / artW;
        return Math.Clamp(natural, cardW / FlattestBanner, cardW / SquarestBanner);
    }

    /// <summary>The banner is the card's door: pressing it opens the browse screen on this collection. The
    /// button goes first so it wins the click over nothing, and the rows below never overlap it.</summary>
    private static bool DrawBanner(
        OsAppContext ctx, ImDrawListPtr dl, StoreCollectionDto collection, IDalamudTextureWrap? wrap,
        Vector2 tl, float cardW, float bannerH, Vector4 top, Vector4 bottom, Vector4 accent)
    {
        var br = tl + new Vector2(cardW, bannerH);
        ImGui.SetCursorScreenPos(tl);
        var pressed = ImGui.InvisibleButton($"##collHead{collection.Id:N}", new Vector2(cardW, bannerH));
        if (ImGui.IsItemHovered())
        {
            HandOnHover();
        }
        if (wrap is not null)
        {
            var (uv0, uv1) = OsDrawShared.CoverUv(wrap.Width, wrap.Height, cardW, bannerH);
            dl.AddImageRounded(wrap.Handle, tl, br, uv0, uv1, 0xFFFFFFFFu, Px(18f),
                ImDrawFlags.RoundCornersTop);
        }
        else
        {
            OsDrawShared.RoundedGradient(dl, tl, br, Px(18f), top, bottom);
        }
        // The text sits on its own scrim, so any uploaded art stays readable underneath it.
        dl.AddRectFilled(new Vector2(tl.X, br.Y - Px(64f)), br, OsDrawShared.Black(0.5f));
        dl.AddRectFilled(new Vector2(tl.X, br.Y - Px(34f)), br, OsDrawShared.Black(0.28f));

        var eyebrow = StoreLoc.Subtitle(collection).ToUpperInvariant();
        var eyebrowSize = ImGui.GetFontSize() * 0.78f;
        dl.AddText(ImGui.GetFont(), eyebrowSize, new Vector2(tl.X + Px(16f), br.Y - Px(52f)),
            ImGui.GetColorU32(accent with { W = 0.95f }), eyebrow);
        using (UiFonts.H3?.Push())
        {
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
                new Vector2(tl.X + Px(16f), br.Y - Px(36f)), 0xFFFFFFFFu,
                OsDrawShared.Ellipsize(StoreLoc.Title(collection), 1f, cardW - Px(32f)));
        }
        StoreFx.Sweep(dl, tl, br, 2.4f, ctx.ReduceMotion, 0.55f);
        return pressed;
    }

    private static Result DrawRow(
        OsAppContext ctx, ImDrawListPtr dl, StoreProductDto product, StoreMediaCache media, StoreCart cart,
        Vector2 tl, float cardW, int index, int count)
    {
        var art = Px(38f);
        var center = tl + new Vector2(Px(16f) + art * 0.5f, Px(RowH) * 0.5f);
        var owned = product.MaxPerAccount is { } max && product.OwnedQuantity >= max;
        var inCart = cart.QuantityOf(product.Id) > 0;

        // The pill is submitted before the row, so the first-submitted-wins rule gives it the click.
        var pillW = Px(66f);
        var pillH = Px(26f);
        var pillTl = new Vector2(tl.X + cardW - Px(16f) - pillW, tl.Y + (Px(RowH) - pillH) * 0.5f);
        ImGui.SetCursorScreenPos(pillTl);
        var pillPressed = ImGui.InvisibleButton($"##collAdd{product.Id:N}", new Vector2(pillW, pillH))
            && !owned && !inCart;
        var pillHovered = ImGui.IsItemHovered();
        if (pillHovered && !owned && !inCart)
        {
            HandOnHover();
        }

        ImGui.SetCursorScreenPos(tl);
        var rowPressed = ImGui.InvisibleButton($"##collRow{product.Id:N}", new Vector2(cardW, Px(RowH)));
        var rowHovered = ImGui.IsItemHovered();
        if (rowHovered)
        {
            HandOnHover();
            dl.AddRectFilled(tl, tl + new Vector2(cardW, Px(RowH)), OsDrawShared.White(0.04f));
        }

        var visual = product.HasImage ? media.Get(product.Id, product.ImageVersion) : null;
        var artTl = new Vector2(tl.X + Px(16f), tl.Y + (Px(RowH) - art) * 0.5f);
        if (visual?.Tex?.GetWrapOrDefault() is { } wrap)
        {
            var (uv0, uv1) = StoreArtCrop.PetThumbnailUv(product.ItemKind, wrap.Width, wrap.Height, art, art);
            dl.AddImageRounded(wrap.Handle, artTl, artTl + new Vector2(art, art), uv0, uv1, 0xFFFFFFFFu, Px(9f));
        }
        else if (!BundleArt.Draw(dl, media, product, artTl, new Vector2(art, art), Px(9f)))
        {
            var (top, bottom, _) = StoreFx.CardColors(product.AccentColor);
            OsDrawShared.RoundedGradient(dl, artTl, artTl + new Vector2(art, art), Px(9f), top, bottom);
            IconDraw.AddCentered(dl, StoreCard.KindGlyph(product.ItemKind), Px(15f), center, OsDrawShared.White(0.75f));
        }

        var textX = artTl.X + art + Px(11f);
        var textW = pillTl.X - textX - Px(10f);
        var nameY = tl.Y + Px(RowH) * 0.5f - ImGui.GetTextLineHeight() - Px(1f);
        dl.AddText(new Vector2(textX, nameY), ImGui.GetColorU32(UiColors.Body),
            OsDrawShared.Ellipsize(StoreLoc.Name(product), 1f, textW));
        var sub = product.BundleItems.Length > 0
            ? Loc.T("os.store_bundle_count", product.BundleItems.Length)
            : StoreLoc.Description(product);
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.85f,
            new Vector2(textX, tl.Y + Px(RowH) * 0.5f + Px(1f)), ImGui.GetColorU32(UiColors.Hint),
            OsDrawShared.Ellipsize(sub, 0.85f, textW));

        DrawPill(dl, pillTl, pillW, pillH, product, owned, inCart, pillHovered);

        if (index < count - 1)
        {
            dl.AddLine(new Vector2(textX, tl.Y + Px(RowH)), new Vector2(tl.X + cardW - Px(16f), tl.Y + Px(RowH)),
                OsDrawShared.White(0.06f), 1f);
        }
        _ = ctx;
        return new Result(rowPressed ? product.Id : null, pillPressed ? product : null);
    }

    private static void DrawPill(
        ImDrawListPtr dl, Vector2 tl, float w, float h, StoreProductDto product,
        bool owned, bool inCart, bool hovered)
    {
        var label = owned
            ? Loc.T("os.store_owned")
            : inCart
                ? Loc.T("os.store_in_cart")
                : product.DiscountedPriceSparks.ToString("N0");
        var enabled = !owned && !inCart;
        dl.AddRectFilled(tl, tl + new Vector2(w, h),
            enabled ? OsDrawShared.White(hovered ? 0.22f : 0.14f) : OsDrawShared.White(0.06f), h * 0.5f);

        var labelSz = ImGui.CalcTextSize(label);
        var iconPx = enabled ? Px(10f) : 0f;
        var gap = enabled ? Px(3f) : 0f;
        var startX = tl.X + (w - labelSz.X - iconPx - gap) * 0.5f;
        if (enabled)
        {
            IconDraw.AddCentered(dl, FontAwesomeIcon.Bolt, iconPx,
                new Vector2(startX + iconPx * 0.5f, tl.Y + h * 0.5f), ImGui.GetColorU32(StoreChips.GoldColor));
        }
        dl.AddText(new Vector2(startX + iconPx + gap, tl.Y + (h - labelSz.Y) * 0.5f),
            ImGui.GetColorU32(enabled ? StoreChips.GoldColor : UiColors.Hint), label);
    }
}
