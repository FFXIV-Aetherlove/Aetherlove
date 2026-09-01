using System;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Shared.Store;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Store;

/// <summary>An editorial shelf as one opaque card: the collection's banner with its eyebrow and headline
/// over a scrim, then a row per pick with art, name and a price pill that drops it straight in the cart.
/// Modelled on the App Store's curated lists.</summary>
internal static class CollectionCard
{
    private const float PadX = 16f;
    private const float BannerH = 116f;
    private const float RowH = 54f;

    internal sealed record Result(Guid? OpenProduct, StoreProductDto? AddToCart);

    public static Result Draw(
        OsAppContext ctx, StoreCollectionDto collection, StoreMediaCache media, StoreCart cart, float winW)
    {
        var dl = ImGui.GetWindowDrawList();
        var cardW = winW - Px(PadX) * 2f;
        var cardH = Px(BannerH) + Px(RowH) * collection.Products.Length + Px(10f);
        ImGui.SetCursorPosX(Px(PadX));
        var tl = ImGui.GetCursorScreenPos();
        var (top, bottom, accent) = StoreFx.CardColors(collection.AccentColor);

        dl.AddRectFilled(tl, tl + new Vector2(cardW, cardH), OsDrawShared.White(0.055f), Px(18f));
        DrawBanner(ctx, dl, collection, media, tl, cardW, top, bottom, accent);

        Guid? open = null;
        StoreProductDto? add = null;
        var y = tl.Y + Px(BannerH);
        for (var i = 0; i < collection.Products.Length; i++)
        {
            var product = collection.Products[i];
            var rowTl = new Vector2(tl.X, y);
            var result = DrawRow(ctx, dl, product, media, cart, rowTl, cardW, i, collection.Products.Length);
            open ??= result.OpenProduct;
            add ??= result.AddToCart;
            y += Px(RowH);
        }

        ImGui.SetCursorScreenPos(new Vector2(ImGui.GetWindowPos().X, tl.Y + cardH));
        ImGui.Dummy(new Vector2(0f, Px(14f)));
        return new Result(open, add);
    }

    private static void DrawBanner(
        OsAppContext ctx, ImDrawListPtr dl, StoreCollectionDto collection, StoreMediaCache media,
        Vector2 tl, float cardW, Vector4 top, Vector4 bottom, Vector4 accent)
    {
        var br = tl + new Vector2(cardW, Px(BannerH));
        var visual = collection.HasImage ? media.GetCollection(collection.Id, collection.ImageVersion) : null;
        if (visual?.Tex?.GetWrapOrDefault() is { } wrap)
        {
            var (uv0, uv1) = OsDrawShared.CoverUv(wrap.Width, wrap.Height, cardW, Px(BannerH));
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
