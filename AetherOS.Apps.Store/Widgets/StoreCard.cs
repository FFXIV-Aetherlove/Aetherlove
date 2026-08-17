using System;
using System.Numerics;
using AetherLove.Services.Localization;
using AetherLove.Shared.Store;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Store;

/// <summary>The product card, in the standard grid size and a wide featured variant. Accent-tinted
/// gradient, cover-fit art on the top, name and price beneath, badges over the art, an OWNED veil when
/// the account holds its limit, and a staggered idle sweep so shelves glint. Returns true on click.</summary>
internal static class StoreCard
{
    public static bool Draw(
        OsAppContext ctx, StoreMediaCache media, StoreProductDto product,
        Vector2 tl, Vector2 size, int index, bool wide = false)
    {
        var dl = ImGui.GetWindowDrawList();
        var rounding = Px(14f);
        var br = tl + size;
        var (top, bottom, accent) = StoreFx.CardColors(product.AccentColor);
        var owned = product.MaxPerAccount is { } max && product.OwnedQuantity >= max;

        ImGui.SetCursorScreenPos(tl);
        var clicked = ImGui.InvisibleButton($"##card_{product.Id:N}_{index}", size);
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            HandOnHover();
        }

        OsDrawShared.RoundedGradient(dl, tl, br, rounding, top, bottom, hovered ? 1f : 0.94f);

        // Art fills the top ~58%; a shimmer stands in while it loads, the accent + a big watermark
        // glyph when the product has none.
        var artH = size.Y * 0.58f;
        var artBr = new Vector2(br.X, tl.Y + artH);
        var visual = product.HasImage
            ? media.Get(product.Id, product.ImageVersion)
            : new StoreMediaCache.Visual(null, true);
        var handle = visual?.Tex?.GetWrapOrDefault()?.Handle;
        if (handle is null
            && BundleArt.Draw(dl, media, product, tl, new Vector2(size.X, artH), rounding,
                ImDrawFlags.RoundCornersTop))
        {
            dl.AddRectFilledMultiColor(new Vector2(tl.X, artBr.Y - Px(22f)), artBr,
                OsDrawShared.Black(0f), OsDrawShared.Black(0f), OsDrawShared.Black(0.35f), OsDrawShared.Black(0.35f));
        }
        else if (handle is { } tex)
        {
            var wrap = visual!.Tex!.GetWrapOrDefault()!;
            // A skin's frame edge starts below the NEW ribbon's band, so the badge sits on gradient
            // rather than on top of the art it is supposed to announce.
            var imgTl = product.ItemKind == StoreItemKind.ThemePack
                ? tl with { Y = tl.Y + artH * 0.30f }
                : tl;
            // A worn-pet render is composed for a big card, so on a rail card or a grid tile the creature is
            // a speck in a lot of transparent room: the same zoom the category tiles use gives it the tile.
            var (uv0, uv1) = StoreArtCrop.PetCardUv(
                product.ItemKind, wrap.Width, wrap.Height, size.X, artBr.Y - imgTl.Y);
            dl.AddImageRounded(tex, imgTl, artBr, uv0, uv1, 0xFFFFFFFFu, rounding,
                imgTl == tl ? ImDrawFlags.RoundCornersTop : ImDrawFlags.RoundCornersNone);
            dl.AddRectFilledMultiColor(new Vector2(tl.X, artBr.Y - Px(22f)), artBr,
                OsDrawShared.Black(0f), OsDrawShared.Black(0f), OsDrawShared.Black(0.35f), OsDrawShared.Black(0.35f));
        }
        else if (visual is null && !ctx.ReduceMotion)
        {
            var shimmer = 0.08f + 0.05f * MathF.Sin((float)ImGui.GetTime() * 2.2f + index);
            dl.AddRectFilled(tl, artBr, OsDrawShared.White(shimmer), rounding, ImDrawFlags.RoundCornersTop);
        }
        else
        {
            IconDraw.AddCentered(dl, KindGlyph(product.ItemKind), MathF.Min(artH * 0.44f, Px(34f)),
                tl + new Vector2(size.X * 0.5f, artH * 0.52f), OsDrawShared.White(0.22f));
        }

        var textX = tl.X + Px(10f);
        var nameY = artBr.Y + Px(7f);
        dl.PushClipRect(tl, br, true);
        var fullName = StoreLoc.Name(product);
        var shownName = TruncateToWidth(fullName, size.X - Px(20f));
        dl.AddText(new Vector2(textX, nameY), ImGui.GetColorU32(UiColors.Body), shownName);
        // A cut name is unreadable, so hovering the card spells it out rather than leaving the buyer to guess.
        if (hovered && shownName != fullName)
        {
            ImGui.SetTooltip(fullName);
        }
        StoreChips.Price(dl, new Vector2(textX + Px(7f), nameY + ImGui.GetTextLineHeight() + Px(7f)),
            product.DiscountedPriceSparks, product.PriceSparks, wide ? 1f : 0.9f, plate: true);
        if (wide)
        {
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.82f,
                new Vector2(textX, nameY + ImGui.GetTextLineHeight() * 2f + Px(8f)),
                ImGui.GetColorU32(UiColors.Hint),
                TruncateToWidth(StoreLoc.Description(product), size.X - Px(20f)));
        }
        dl.PopClipRect();

        if (IsNew(product))
        {
            StoreChips.NewRibbon(dl, tl);
        }
        if (product.DiscountPercent > 0)
        {
            StoreChips.SaleBadge(dl, new Vector2(br.X - Px(22f), tl.Y + Px(14f)), product.DiscountPercent);
        }
        if (product.ItemKind == StoreItemKind.Bundle)
        {
            var label = Loc.T("os.store_badge_bundle", product.BundleItems.Length);
            var fontSize = ImGui.GetFontSize() * 0.72f;
            var textSz = StoreChips.MeasureAt(label, fontSize);
            var pillTl = new Vector2(tl.X + Px(6f), artBr.Y - textSz.Y - Px(10f));
            dl.AddRectFilled(pillTl, pillTl + textSz + new Vector2(Px(10f), Px(4f)),
                OsDrawShared.Black(0.55f), (textSz.Y + Px(4f)) * 0.5f);
            dl.AddText(ImGui.GetFont(), fontSize, pillTl + new Vector2(Px(5f), Px(2f)),
                ImGui.GetColorU32(StoreChips.GoldColor), label);
        }

        if (owned)
        {
            dl.AddRectFilled(tl, br, OsDrawShared.Black(0.5f), rounding);
            var check = Loc.T("os.store_owned");
            var checkSz = ImGui.CalcTextSize(check);
            var center = tl + size * 0.5f;
            IconDraw.AddCentered(dl, FontAwesomeIcon.CheckCircle, Px(18f),
                center - new Vector2(0f, Px(12f)), ImGui.GetColorU32(new Vector4(0.35f, 0.85f, 0.5f, 1f)));
            dl.AddText(center + new Vector2(-checkSz.X * 0.5f, Px(4f)), ImGui.GetColorU32(UiColors.Body), check);
        }
        else
        {
            StoreFx.Sweep(dl, tl, br, index * 0.6f, ctx.ReduceMotion);
        }

        if (hovered)
        {
            dl.AddRect(tl, br, OsDrawShared.White(0.35f), rounding, ImDrawFlags.RoundCornersAll, Px(1.2f));
        }
        _ = accent;
        return clicked;
    }

    public static bool IsNew(StoreProductDto product) =>
        DateTimeOffset.UtcNow - product.CreatedAtUtc < TimeSpan.FromDays(14);

    public static FontAwesomeIcon KindGlyph(StoreItemKind kind) => kind switch
    {
        StoreItemKind.AvatarFrame => FontAwesomeIcon.UserCircle,
        StoreItemKind.ThemePack => FontAwesomeIcon.Palette,
        StoreItemKind.Powerup => FontAwesomeIcon.Rocket,
        StoreItemKind.Bundle => FontAwesomeIcon.Gifts,
        StoreItemKind.AetherlingPalette => FontAwesomeIcon.Fill,
        StoreItemKind.AetherlingAspect => FontAwesomeIcon.Gem,
        StoreItemKind.AetherlingAccessory => FontAwesomeIcon.HatWizard,
        StoreItemKind.AetherlingArms => FontAwesomeIcon.Khanda,
        StoreItemKind.AetherlingConsumable => FontAwesomeIcon.Cookie,
        StoreItemKind.AetherlingIdentity => FontAwesomeIcon.Splotch,
        StoreItemKind.AetherlingReaction => FontAwesomeIcon.Grin,
        StoreItemKind.AetherlingShell => FontAwesomeIcon.Egg,
        _ => FontAwesomeIcon.ShoppingBag,
    };
}
