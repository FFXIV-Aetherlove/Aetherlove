using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Shared.Store;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Store;

/// <summary>One product: big art, price block, description, bundle contents with the savings line, tags,
/// and the buying block, where a quantity stepper sits beside the pulsing add-to-cart CTA with its bolt
/// flight to the cart, over a rule from the wishlist toggle.</summary>
internal sealed class DetailScreen(
    IStoreHost host, StoreState state, StoreMediaCache media, StoreMediaCache backgrounds, StoreCart cart,
    StoreWishlist wishlist, Action addedToCart, Action<BrowseScreen.Seed> openBrowse)
{
    private const float PadX = 16f;

    private readonly EntranceAnimation _entrance = new();
    private readonly List<Guid> _trail = [];
    private Guid _productId;
    private StoreProductDto? _product;
    private int _generation;
    private int _quantity = 1;
    private double _inCartFlashStamp = -10.0;
    private double _boltFlightStamp = -10.0;
    private StoreProductDto[] _related = [];
    private int _relatedGeneration;
    private Vector2 _boltFrom;

    /// <summary>Opens a product as a fresh errand, from anywhere outside this screen.</summary>
    public void Show(Guid productId)
    {
        _trail.Clear();
        Load(productId);
    }

    /// <summary>Opens a product reached from the one on screen, remembering where the user came from so the
    /// header's back control returns there rather than dropping them out of the detail page entirely.</summary>
    public void ShowChild(Guid productId)
    {
        _trail.Add(_productId);
        Load(productId);
    }

    /// <summary>True while the back control owes the user a step back up the trail.</summary>
    public bool CanGoBack => _trail.Count > 0;

    public bool TryGoBack()
    {
        if (_trail.Count == 0)
        {
            return false;
        }
        var parent = _trail[^1];
        _trail.RemoveAt(_trail.Count - 1);
        Load(parent);
        return true;
    }

    private void Load(Guid productId)
    {
        _entrance.Arm();
        _productId = productId;
        _product = state.Find(productId);
        _quantity = 1;
        _inCartFlashStamp = -10.0;
        _related = [];
        Refresh();
        RefreshRelated();
    }

    private void RefreshRelated()
    {
        var generation = System.Threading.Interlocked.Increment(ref _relatedGeneration);
        var id = _productId;
        _ = Task.Run(async () =>
        {
            var items = await host.GetStoreRelatedAsync(id).ConfigureAwait(false);
            if (generation == System.Threading.Volatile.Read(ref _relatedGeneration) && items is not null)
            {
                _related = items;
            }
        });
    }

    private void Refresh()
    {
        var generation = System.Threading.Interlocked.Increment(ref _generation);
        var id = _productId;
        _ = Task.Run(async () =>
        {
            var fresh = await state.FindFreshAsync(id).ConfigureAwait(false);
            if (generation != System.Threading.Volatile.Read(ref _generation))
            {
                return;
            }
            if (fresh is not null)
            {
                _product = fresh;
            }
        });
    }

    public void Draw(OsAppContext ctx)
    {
        _entrance.BeginFrame();
        var winW = ImGui.GetWindowSize().X;
        var dl = ImGui.GetWindowDrawList();

        if (_product is not { } product)
        {
            ImGui.Dummy(new Vector2(0f, Px(70f)));
            AetherLove.Widgets.LoadingSpinner.Draw(
                new Vector2(ImGui.GetWindowPos().X + winW * 0.5f, ImGui.GetCursorScreenPos().Y + Px(16f)),
                Px(14f), Px(3f), StorePalette.BlueU32);
            _entrance.EndFrame();
            return;
        }

        // Art block: image or accent gradient, badges over it, the countdown floating bottom-right. A ring is
        // a shape rather than a picture, so its block goes square and shows the whole asset instead of a
        // letterbox slice through the middle of the band.
        var whole = StoreImageSpec.KeepsAlpha(product.ItemKind) && product.HasImage;
        var artH = whole ? MathF.Min(winW, Px(300f)) : Px(190f);
        var artTl = ImGui.GetCursorScreenPos() with { X = ImGui.GetWindowPos().X };
        var artBr = artTl + new Vector2(winW, artH);
        var (top, bottom, accent) = StoreFx.CardColors(product.AccentColor);
        var visual = product.HasImage ? media.Get(product.Id, product.ImageVersion) : null;
        if (whole && visual?.Tex?.GetWrapOrDefault() is { } ringWrap)
        {
            OsDrawShared.RoundedGradient(dl, artTl, artBr, 0f, top, bottom);
            var fit = MathF.Min(winW, artH) - Px(24f);
            var side = new Vector2(
                ringWrap.Width >= ringWrap.Height ? fit : fit * ringWrap.Width / ringWrap.Height,
                ringWrap.Height >= ringWrap.Width ? fit : fit * ringWrap.Height / ringWrap.Width);
            var center = artTl + new Vector2(winW, artH) * 0.5f;
            dl.AddImage(ringWrap.Handle, center - side * 0.5f, center + side * 0.5f);
        }
        else if (visual?.Tex?.GetWrapOrDefault() is { } wrap)
        {
            var (uv0, uv1) = StoreArtCrop.Uv(product.ItemKind, wrap.Width, wrap.Height, winW, artH);
            dl.AddImage(wrap.Handle, artTl, artBr, uv0, uv1);
            dl.AddRectFilledMultiColor(artTl, artBr,
                OsDrawShared.Black(0.05f), OsDrawShared.Black(0.05f),
                OsDrawShared.Black(0.45f), OsDrawShared.Black(0.45f));
        }
        else if (BundleArt.Draw(dl, media, product, artTl, new Vector2(winW, artH), 0f, ImDrawFlags.RoundCornersNone))
        {
            dl.AddRectFilledMultiColor(artTl, artBr,
                OsDrawShared.Black(0.05f), OsDrawShared.Black(0.05f),
                OsDrawShared.Black(0.45f), OsDrawShared.Black(0.45f));
        }
        else
        {
            OsDrawShared.RoundedGradient(dl, artTl, artBr, 0f, top, bottom);
            IconDraw.AddCentered(dl, StoreCard.KindGlyph(product.ItemKind), Px(52f),
                artTl + new Vector2(winW * 0.5f, artH * 0.5f), OsDrawShared.White(0.25f));
        }
        StoreFx.Sweep(dl, artTl, artBr, 0.9f, ctx.ReduceMotion, strength: 0.7f);
        if (StoreCard.IsNew(product))
        {
            StoreChips.NewRibbon(dl, artTl + new Vector2(Px(PadX), Px(10f)));
        }
        if (product.DiscountPercent > 0)
        {
            StoreChips.SaleBadge(dl, new Vector2(artBr.X - Px(40f), artTl.Y + Px(22f)), product.DiscountPercent);
        }
        if (product.DiscountEndsAtUtc is { } endsAt)
        {
            StoreChips.Countdown(dl, new Vector2(artBr.X - Px(120f), artBr.Y - Px(30f)), endsAt);
        }
        ImGui.SetCursorScreenPos(new Vector2(ImGui.GetWindowPos().X, artBr.Y));
        ImGui.Dummy(new Vector2(0f, Px(12f)));

        // Title + owned chip.
        ImGui.SetCursorPosX(Px(PadX));
        using (ctx.HeadingFont?.Push())
        {
            ImGui.TextColored(UiColors.Body, StoreLoc.Name(product));
        }
        var owned = product.MaxPerAccount is { } max && product.OwnedQuantity >= max;
        if (product.OwnedQuantity > 0)
        {
            ImGui.SameLine();
            var chipLabel = product.MaxPerAccount is { } cap
                ? Loc.T("os.store_owned_of", product.OwnedQuantity, cap)
                : Loc.T("os.store_owned_n", product.OwnedQuantity);
            var chipSz = ImGui.CalcTextSize(chipLabel);
            var chipTl = ImGui.GetCursorScreenPos() + new Vector2(Px(4f), Px(1f));
            dl.AddRectFilled(chipTl, chipTl + chipSz + new Vector2(Px(14f), Px(4f)),
                ImGui.GetColorU32(new Vector4(0.2f, 0.5f, 0.3f, 0.5f)), Px(10f));
            dl.AddText(chipTl + new Vector2(Px(7f), Px(2f)),
                ImGui.GetColorU32(new Vector4(0.55f, 0.95f, 0.65f, 1f)), chipLabel);
            ImGui.Dummy(new Vector2(chipSz.X + Px(18f), chipSz.Y));
        }

        // Price block.
        ImGui.SetCursorPosX(Px(PadX));
        var priceTl = ImGui.GetCursorScreenPos();
        StoreChips.Price(dl, priceTl, product.DiscountedPriceSparks * _quantity,
            product.PriceSparks * _quantity, 1.35f);
        ImGui.Dummy(new Vector2(0f, ImGui.GetTextLineHeight() * 1.4f + Px(6f)));

        // Description.
        ImGui.SetCursorPosX(Px(PadX));
        ImGui.PushTextWrapPos(winW - Px(PadX));
        ImGui.TextColored(UiColors.Hint, StoreLoc.Description(product));
        ImGui.PopTextWrapPos();
        ImGui.Dummy(new Vector2(0f, Px(8f)));

        if (product.BundleItems.Length > 0)
        {
            DrawBundleContents(ctx, dl, winW, product);
        }
        if (product.Tags.Length > 0)
        {
            DrawTags(winW, product);
        }

        var maxAddable = MaxAddable(product);
        if (product.MaxPerAccount is { } perAccount && perAccount > 1)
        {
            ImGui.SetCursorPosX(Px(PadX));
            ImGui.TextColored(UiColors.Hint, Loc.T("os.store_max_per_account", perAccount));
            ImGui.Dummy(new Vector2(0f, Px(4f)));
        }

        DrawRingTryOn(ctx, winW, product);
        DrawCta(ctx, dl, winW, product, owned, maxAddable);
        DrawThemeIncluded(winW, product);
        DrawSkinPreviewButton(winW, product);
        DrawInformation(winW, product);
        DrawRelated(ctx, winW);
        ImGui.Dummy(new Vector2(0f, Px(46f)));
        DrawBoltFlight(ctx, dl);
        _entrance.EndFrame();
        _ = accent;
    }

    /// <summary>Phone skins get a try-before-you-buy: a second phone beside the real one, wearing the
    /// server's watermarked copy of the frame.</summary>
    private void DrawSkinPreviewButton(float winW, StoreProductDto product)
    {
        if (!StoreImageSpec.IsPhoneSkin(product.ItemKind) || !product.HasImage)
        {
            return;
        }
        ImGui.Dummy(new Vector2(0f, Px(8f)));
        ImGui.SetCursorPosX(Px(PadX));
        if (StoreUi.Button(Loc.T("os.store_preview_skin"), winW - Px(PadX) * 2f))
        {
            host.ShowSkinPreview(StoreLoc.Name(product), product.Id);
        }
    }

    /// <summary>A theme is a set, so it gets a manifest card: one row per thing the pack installs, with the
    /// palette carried as swatch dots on its own row. The whole look is judged in the preview window, so no
    /// full-size wallpaper is laid out on the page.</summary>
    private void DrawThemeIncluded(float winW, StoreProductDto product)
    {
        if (product.ThemeColors is not { } colors)
        {
            return;
        }
        ImGui.Dummy(new Vector2(0f, Px(16f)));
        ImGui.SetCursorPosX(Px(PadX));
        using (UiFonts.H3?.Push())
        {
            ImGui.TextColored(UiColors.Body, Loc.T("os.store_theme_included"));
        }
        ImGui.Dummy(new Vector2(0f, Px(6f)));

        var rows = new List<(FontAwesomeIcon Icon, string Label)>
        {
            (FontAwesomeIcon.MobileAlt, Loc.T("os.store_included_skin")),
        };
        if (product.HasBackground)
        {
            rows.Add((FontAwesomeIcon.Image, Loc.T("os.store_theme_background")));
        }
        rows.Add((FontAwesomeIcon.Palette, Loc.T("os.store_theme_colors")));

        var dl = ImGui.GetWindowDrawList();
        var cardW = winW - Px(PadX) * 2f;
        var rowH = Px(38f);
        ImGui.SetCursorPosX(Px(PadX));
        var tl = ImGui.GetCursorScreenPos();
        dl.AddRectFilled(tl, tl + new Vector2(cardW, rowH * rows.Count), OsDrawShared.White(0.05f), Px(14f));
        dl.AddRect(tl, tl + new Vector2(cardW, rowH * rows.Count),
            StorePalette.BlueWithAlpha(0.25f), Px(14f), ImDrawFlags.RoundCornersAll, Px(1.2f));
        for (var i = 0; i < rows.Count; i++)
        {
            var (icon, label) = rows[i];
            var y = tl.Y + rowH * i;
            IconDraw.AddCentered(dl, icon, Px(14f),
                new Vector2(tl.X + Px(22f), y + rowH * 0.5f), StorePalette.BlueLightU32);
            dl.AddText(new Vector2(tl.X + Px(40f), y + (rowH - ImGui.GetTextLineHeight()) * 0.5f),
                ImGui.GetColorU32(UiColors.Body), label);
            if (icon == FontAwesomeIcon.Palette)
            {
                DrawSwatchDots(dl, colors, new Vector2(tl.X + cardW - Px(16f), y + rowH * 0.5f));
            }
            if (i < rows.Count - 1)
            {
                dl.AddLine(new Vector2(tl.X + Px(14f), y + rowH), new Vector2(tl.X + cardW - Px(14f), y + rowH),
                    OsDrawShared.White(0.06f), 1f);
            }
        }
        ImGui.SetCursorScreenPos(new Vector2(ImGui.GetWindowPos().X, tl.Y + rowH * rows.Count));
        ImGui.Dummy(new Vector2(0f, Px(4f)));
        _ = backgrounds;
    }

    /// <summary>The palette as a right-aligned run of dots, growing leftward from the given right edge.</summary>
    private static void DrawSwatchDots(ImDrawListPtr dl, StoreThemeColorsDto colors, Vector2 rightCenter)
    {
        var swatches = new[]
        {
            colors.Accent, colors.AccentLight, colors.AccentDark, colors.ChipFill,
            colors.SecondaryStart, colors.SecondaryEnd,
        };
        var dot = Px(7f);
        var step = dot * 2f + Px(5f);
        for (var i = 0; i < swatches.Length; i++)
        {
            var center = new Vector2(rightCenter.X - dot - (swatches.Length - 1 - i) * step, rightCenter.Y);
            dl.AddCircleFilled(center, dot, Argb(swatches[i]));
            dl.AddCircle(center, dot, OsDrawShared.White(0.35f), 0, Px(1.1f));
        }
    }

    /// <summary>Theme colors ride the wire as 0xAARRGGBB; ImGui wants 0xAABBGGRR.</summary>
    private static uint Argb(uint argb) =>
        (argb & 0xFF00FF00u) | ((argb & 0x00FF0000u) >> 16) | ((argb & 0x000000FFu) << 16);

    private byte[]? _yapperAvatar;
    private bool _yapperAvatarRequested;
    private Dalamud.Interface.Textures.ISharedImmediateTexture? _yapperAvatarTex;
    private int _yapperAvatarTexBytes;

    /// <summary>Rings try on: the product's own shelf art drawn at 1.3x over the user's real avatars
    /// (OS, active Love profile, Yapper). The shelf texture IS the ring asset, so this costs no fetch
    /// beyond the one-time Yapper avatar lookup.</summary>
    private void DrawRingTryOn(OsAppContext ctx, float winW, StoreProductDto product)
    {
        // Rings only. It used to ask KeepsAlpha, which was the same question until the Aetherling kinds
        // started keeping their alpha too, and then a feeding crystal was being tried on as jewellery.
        if (product.ItemKind != StoreItemKind.AvatarFrame || !product.HasImage)
        {
            return;
        }
        var ringWrap = media.Get(product.Id, product.ImageVersion)?.Tex?.GetWrapOrDefault();
        if (ringWrap is null)
        {
            return;
        }
        if (!_yapperAvatarRequested)
        {
            _yapperAvatarRequested = true;
            _ = Task.Run(async () => _yapperAvatar = await host.GetYapperAvatarAsync().ConfigureAwait(false));
        }
        if (_yapperAvatar is { Length: > 0 } bytes && _yapperAvatarTexBytes != bytes.Length)
        {
            _yapperAvatarTexBytes = bytes.Length;
            var dir = System.IO.Path.Combine(ctx.Capabilities.Storage("store").Directory, "AvatarCache");
            _yapperAvatarTex = AetherLove.Services.AvatarDiskCache.Store(dir, "yapper_self", bytes);
        }

        ImGui.Dummy(new Vector2(0f, Px(18f)));
        ImGui.SetCursorPosX(Px(PadX));
        using (UiFonts.H3?.Push())
        {
            ImGui.TextColored(UiColors.Body, Loc.T("os.store_ring_preview_title"));
        }
        ImGui.SetCursorPosX(Px(PadX));
        ImGui.PushTextWrapPos(winW - Px(PadX));
        ImGui.TextColored(UiColors.Muted, Loc.T("os.store_ring_preview_hint"));
        ImGui.PopTextWrapPos();
        ImGui.Dummy(new Vector2(0f, Px(6f)));

        var dl = ImGui.GetWindowDrawList();
        var faces = new[]
        {
            host.OsAvatarTexture?.GetWrapOrDefault(),
            host.LoveAvatarTexture?.GetWrapOrDefault(),
            _yapperAvatarTex?.GetWrapOrDefault(),
        };

        // Sized from the width rather than a fixed radius, so the three always fit however wide the phone
        // is. The ring is drawn at 1.3x the avatar, so the radius comes back out of the slot, not into it.
        var slot = (winW - (Px(PadX) * 2f)) / faces.Length;
        var ringDiameter = MathF.Min(slot - Px(12f), Px(132f));
        var r = ringDiameter / (2f * 1.3f);
        var startX = ImGui.GetWindowPos().X + (winW - (faces.Length * slot)) * 0.5f;
        var cy = ImGui.GetCursorScreenPos().Y + r * 1.3f + Px(2f);
        for (var i = 0; i < faces.Length; i++)
        {
            var center = new Vector2(startX + slot * i + slot * 0.5f, cy);
            if (faces[i] is { } face)
            {
                dl.AddImageRounded(face.Handle, center - new Vector2(r), center + new Vector2(r),
                    Vector2.Zero, Vector2.One, 0xFFFFFFFFu, r, ImDrawFlags.RoundCornersAll);
            }
            else
            {
                dl.AddCircleFilled(center, r, UiColors.AvatarFallback);
            }
            var half = new Vector2(r * 1.3f);
            dl.AddImage(ringWrap.Handle, center - half, center + half);
        }
        ImGui.Dummy(new Vector2(0f, r * 2.6f + Px(8f)));
    }

    /// <summary>The App Store's information table: the facts that do not fit in a description, each on its
    /// own hairline-separated row.</summary>
    private void DrawInformation(float winW, StoreProductDto product)
    {
        ImGui.Dummy(new Vector2(0f, Px(18f)));
        ImGui.SetCursorPosX(Px(PadX));
        using (UiFonts.H3?.Push())
        {
            ImGui.TextColored(UiColors.Body, Loc.T("os.store_information"));
        }
        ImGui.Dummy(new Vector2(0f, Px(6f)));

        var category = state.Front?.Categories.FirstOrDefault(c => c.Id == product.CategoryId);
        var rows = new List<(string Label, string Value)>
        {
            (Loc.T("os.store_info_kind"), Loc.T($"os.store_kind_{(short)product.ItemKind}")),
            (Loc.T("os.store_info_category"), category is null ? "-" : StoreLoc.Name(category)),
            (Loc.T("os.store_info_limit"), product.MaxPerAccount is { } max
                ? Loc.T("os.store_info_limit_value", max)
                : Loc.T("os.store_info_limit_stacks")),
            (Loc.T("os.store_info_owned"), product.OwnedQuantity.ToString("N0")),
            (Loc.T("os.store_info_added"), product.CreatedAtUtc.ToLocalTime().ToString("d")),
        };
        if (product.Tags.Length > 0)
        {
            rows.Add((Loc.T("os.store_info_tags"), string.Join(", ", product.Tags)));
        }

        var dl = ImGui.GetWindowDrawList();
        var cardW = winW - Px(PadX) * 2f;
        ImGui.SetCursorPosX(Px(PadX));
        var tl = ImGui.GetCursorScreenPos();
        var rowH = Px(34f);
        dl.AddRectFilled(tl, tl + new Vector2(cardW, rowH * rows.Count), OsDrawShared.White(0.05f), Px(14f));
        for (var i = 0; i < rows.Count; i++)
        {
            var (label, value) = rows[i];
            var y = tl.Y + rowH * i;
            var textY = y + (rowH - ImGui.GetTextLineHeight()) * 0.5f;
            dl.AddText(new Vector2(tl.X + Px(14f), textY), ImGui.GetColorU32(UiColors.Hint), label);
            var shown = OsDrawShared.Ellipsize(value, 1f, cardW * 0.55f);
            var valueSz = ImGui.CalcTextSize(shown);
            dl.AddText(new Vector2(tl.X + cardW - Px(14f) - valueSz.X, textY),
                ImGui.GetColorU32(UiColors.Body), shown);
            if (i < rows.Count - 1)
            {
                dl.AddLine(new Vector2(tl.X + Px(14f), y + rowH), new Vector2(tl.X + cardW - Px(14f), y + rowH),
                    OsDrawShared.White(0.06f), 1f);
            }
        }
        ImGui.SetCursorScreenPos(new Vector2(ImGui.GetWindowPos().X, tl.Y + rowH * rows.Count));
    }

    /// <summary>More like this: a horizontal scroller of siblings, drag-scrollable like the storefront rails.</summary>
    private void DrawRelated(OsAppContext ctx, float winW)
    {
        if (_related.Length == 0)
        {
            return;
        }
        ImGui.Dummy(new Vector2(0f, Px(18f)));
        RailHeader.Draw("related", winW, FontAwesomeIcon.ThLarge, Loc.T("os.store_more_like_this"),
            StorePalette.CrimsonLight, ctx.ReduceMotion, seeAll: false);

        var cardW = Px(108f);
        var cardH = Px(150f);
        using var child = ImRaii.Child("##relatedShelf", new Vector2(winW, cardH + Px(6f)), false,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.HorizontalScrollbar);
        if (!child)
        {
            return;
        }
        for (var i = 0; i < _related.Length; i++)
        {
            var product = _related[i];
            var x = Px(PadX) + i * (cardW + Px(10f));
            ImGui.SetCursorPos(new Vector2(x, 0f));
            var tl = ImGui.GetCursorScreenPos();
            if (StoreCard.Draw(ctx, media, product, tl, new Vector2(cardW, cardH), i))
            {
                Show(product.Id);
            }
        }
        if (ImGui.IsWindowHovered() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            ImGui.SetScrollX(ImGui.GetScrollX() - ImGui.GetIO().MouseDelta.X);
        }
    }

    private int MaxAddable(StoreProductDto product)
    {
        var inCart = cart.QuantityOf(product.Id);
        return product.MaxPerAccount is { } max
            ? Math.Max(0, max - product.OwnedQuantity - inCart)
            : StoreLimits.MaxQuantityPerCheckout;
    }

    private void DrawBundleContents(OsAppContext ctx, ImDrawListPtr dl, float winW, StoreProductDto product)
    {
        ImGui.SetCursorPosX(Px(PadX));
        ImGui.TextColored(StorePalette.BlueLight, Loc.T("os.store_bundle_inside"));
        ImGui.Dummy(new Vector2(0f, Px(4f)));

        foreach (var item in product.BundleItems)
        {
            ImGui.SetCursorPosX(Px(PadX));
            var tl = ImGui.GetCursorScreenPos();
            var rowH = Px(34f);
            var rowW = winW - Px(PadX) * 2f;

            ImGui.SetCursorScreenPos(tl);
            var pressed = ImGui.InvisibleButton($"##bundleItem{item.ChildProductId:N}", new Vector2(rowW, rowH));
            var hovered = ImGui.IsItemHovered();
            if (hovered)
            {
                HandOnHover();
                ImGui.SetTooltip(Loc.T("os.store_bundle_open_item"));
            }
            if (pressed)
            {
                ShowChild(item.ChildProductId);
            }
            ImGui.SetCursorScreenPos(tl);

            dl.AddRectFilled(tl, tl + new Vector2(rowW, rowH),
                OsDrawShared.White(hovered ? 0.11f : 0.05f), Px(10f));
            var visual = media.Get(item.ChildProductId, item.ImageVersion);
            if (visual?.Tex?.GetWrapOrDefault() is { } wrap)
            {
                var (uv0, uv1) = OsDrawShared.CoverUv(wrap.Width, wrap.Height, rowH - Px(8f), rowH - Px(8f));
                dl.AddImageRounded(wrap.Handle, tl + new Vector2(Px(4f), Px(4f)),
                    tl + new Vector2(rowH - Px(4f), rowH - Px(4f)), uv0, uv1, 0xFFFFFFFFu, Px(7f));
            }
            else
            {
                IconDraw.AddCentered(dl, StoreCard.KindGlyph(item.ItemKind), Px(12f),
                    tl + new Vector2(rowH * 0.5f, rowH * 0.5f), OsDrawShared.White(0.3f));
            }
            var label = item.Quantity > 1 ? $"{StoreLoc.Name(item)} x{item.Quantity}" : StoreLoc.Name(item);
            dl.AddText(tl + new Vector2(rowH + Px(6f), (rowH - ImGui.GetTextLineHeight()) * 0.5f),
                ImGui.GetColorU32(UiColors.Body), TruncateToWidth(label, rowW - rowH - Px(62f)));
            IconDraw.AddCentered(dl, FontAwesomeIcon.ChevronRight, Px(10f),
                new Vector2(tl.X + rowW - Px(14f), tl.Y + rowH * 0.5f),
                OsDrawShared.White(hovered ? 0.75f : 0.35f));
            if (item.ChildOwned)
            {
                IconDraw.AddCentered(dl, FontAwesomeIcon.CheckCircle, Px(12f),
                    new Vector2(tl.X + rowW - Px(34f), tl.Y + rowH * 0.5f),
                    ImGui.GetColorU32(new Vector4(0.35f, 0.85f, 0.5f, 1f)));
            }
            ImGui.Dummy(new Vector2(0f, rowH + Px(5f)));
        }

        if (product.BundleWorthSparks > product.DiscountedPriceSparks)
        {
            var savePercent = 100 - product.DiscountedPriceSparks * 100 / product.BundleWorthSparks;
            ImGui.SetCursorPosX(Px(PadX));
            ImGui.TextColored(StoreChips.GoldColor,
                Loc.T("os.store_bundle_worth", product.BundleWorthSparks.ToString("N0"), savePercent));
            ImGui.Dummy(new Vector2(0f, Px(4f)));
        }
        if (product.BundleItems.Any(b => b.ChildOwned))
        {
            ImGui.SetCursorPosX(Px(PadX));
            ImGui.PushTextWrapPos(winW - Px(PadX));
            ImGui.TextColored(UiColors.Amber, Loc.T("os.store_bundle_partial"));
            ImGui.PopTextWrapPos();
        }
        ImGui.Dummy(new Vector2(0f, Px(8f)));
        _ = ctx;
    }

    private void DrawTags(float winW, StoreProductDto product)
    {
        ImGui.SetCursorPosX(Px(PadX));
        var x = Px(PadX);
        foreach (var tag in product.Tags)
        {
            var label = $"#{tag}";
            var labelSz = ImGui.CalcTextSize(label);
            var pillW = labelSz.X + Px(16f);
            if (x + pillW > winW - Px(PadX))
            {
                ImGui.NewLine();
                ImGui.SetCursorPosX(Px(PadX));
                x = Px(PadX);
            }
            if (SharedUiHelpers.Button($"{label}##tag{tag}", Vector2.Zero))
            {
                openBrowse(new BrowseScreen.Seed(null, tag, null, StoreSort.Featured));
            }
            ImGui.SameLine();
            x += pillW + Px(6f);
        }
        ImGui.NewLine();
        ImGui.Dummy(new Vector2(0f, Px(8f)));
    }

    /// <summary>The buying block: the quantity picker and the cart button share one row, then a rule, then
    /// the wishlist button. The picker sits beside the button rather than above it because the two are one
    /// decision, and the wishlist is the other answer to the same question.</summary>
    private void DrawCta(OsAppContext ctx, ImDrawListPtr dl, float winW, StoreProductDto product, bool owned, int maxAddable)
    {
        var fullW = winW - Px(PadX) * 2f;
        var stackable = product.MaxPerAccount != 1 && maxAddable > 1;
        if (!stackable)
        {
            _quantity = 1;
        }

        var stepperW = QuantityStepper.Size().X;
        var gap = Px(10f);
        if (stackable)
        {
            ImGui.SetCursorPosX(Px(PadX));
            ImGui.TextColored(UiColors.Hint, Loc.T("os.store_quantity"));
            ImGui.Dummy(new Vector2(0f, Px(2f)));
        }

        ImGui.SetCursorPosX(Px(PadX));
        var rowTl = ImGui.GetCursorScreenPos();
        var size = new Vector2(stackable ? fullW - stepperW - gap : fullW, Px(42f));
        var tl = rowTl + new Vector2(stackable ? stepperW + gap : 0f, 0f);
        var justAdded = ImGui.GetTime() - _inCartFlashStamp < 1.2;
        var enabled = !owned && maxAddable > 0 && !justAdded;

        if (stackable)
        {
            _quantity = Math.Clamp(_quantity, 1, maxAddable);
            var stepperTl = rowTl + new Vector2(0f, (size.Y - QuantityStepper.Size().Y) * 0.5f);
            QuantityStepper.Draw("##detailQty", stepperTl, 1, maxAddable, ctx.ReduceMotion, ref _quantity);
        }

        ImGui.SetCursorScreenPos(tl);
        var clicked = ImGui.InvisibleButton("##addToCart", size) && enabled;
        var hovered = enabled && ImGui.IsItemHovered();
        if (hovered)
        {
            HandOnHover();
        }

        if (enabled)
        {
            OsDrawShared.RoundedGradient(dl, tl, tl + size, Px(13f),
                hovered ? StorePalette.BlueLight : StorePalette.Blue, StorePalette.BlueDark);
            // A slow pulse ring invites the tap.
            if (!ctx.ReduceMotion)
            {
                var pulse = 0.5f + 0.5f * MathF.Sin((float)ImGui.GetTime() * 1.6f);
                dl.AddRect(tl - new Vector2(Px(2f), Px(2f)), tl + size + new Vector2(Px(2f), Px(2f)),
                    StorePalette.BlueWithAlpha(0.35f * pulse), Px(15f),
                    ImDrawFlags.RoundCornersAll, Px(2f));
            }
        }
        else
        {
            dl.AddRectFilled(tl, tl + size, OsDrawShared.White(0.08f), Px(13f));
        }
        var label = owned
            ? Loc.T("os.store_owned")
            : justAdded
                ? Loc.T("os.store_in_cart")
                : maxAddable <= 0
                    ? Loc.T("os.store_cart_holds_max")
                    : Loc.T("os.store_add_to_cart");
        var labelSz = ImGui.CalcTextSize(label);
        dl.AddText(tl + (size - labelSz) * 0.5f,
            ImGui.GetColorU32(enabled ? UiColors.Body : UiColors.Hint), label);

        if (clicked)
        {
            cart.Add(product.Id, _quantity);
            _inCartFlashStamp = ImGui.GetTime();
            _boltFlightStamp = ImGui.GetTime();
            _boltFrom = tl + size * 0.5f;
            addedToCart();
        }

        ImGui.SetCursorScreenPos(new Vector2(ImGui.GetWindowPos().X, rowTl.Y + size.Y));
        ImGui.Dummy(new Vector2(0f, Px(12f)));
        var ruleY = ImGui.GetCursorScreenPos().Y;
        dl.AddLine(new Vector2(rowTl.X, ruleY), new Vector2(rowTl.X + fullW, ruleY), OsDrawShared.White(0.1f), Px(1f));
        ImGui.Dummy(new Vector2(0f, Px(12f)));

        DrawWishlistButton(dl, fullW, product);
    }

    /// <summary>The other answer to "do I want this": keep it for later. A tap toggles, so the same button
    /// takes the product back off the list.</summary>
    private void DrawWishlistButton(ImDrawListPtr dl, float fullW, StoreProductDto product)
    {
        ImGui.SetCursorPosX(Px(PadX));
        var tl = ImGui.GetCursorScreenPos();
        var size = new Vector2(fullW, Px(38f));
        var saved = wishlist.Contains(product.Id);

        ImGui.SetCursorScreenPos(tl);
        var clicked = ImGui.InvisibleButton("##addToWishlist", size);
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            HandOnHover();
        }

        dl.AddRectFilled(tl, tl + size, OsDrawShared.White(hovered ? 0.12f : 0.06f), Px(12f));
        dl.AddRect(tl, tl + size,
            saved
                ? ImGui.GetColorU32(StoreChips.GoldColor with { W = 0.75f })
                : StorePalette.BlueWithAlpha(0.45f),
            Px(12f), ImDrawFlags.RoundCornersAll, Px(1.2f));

        var label = Loc.T(saved ? "os.store_in_wishlist" : "os.store_add_to_wishlist");
        var labelSz = ImGui.CalcTextSize(label);
        var iconPx = Px(13f);
        var contentW = iconPx + Px(8f) + labelSz.X;
        var contentX = tl.X + (size.X - contentW) * 0.5f;
        var tint = ImGui.GetColorU32(saved ? StoreChips.GoldColor : UiColors.Body);
        IconDraw.AddCentered(dl, FontAwesomeIcon.Star, iconPx,
            new Vector2(contentX + iconPx * 0.5f, tl.Y + size.Y * 0.5f), tint);
        dl.AddText(new Vector2(contentX + iconPx + Px(8f), tl.Y + (size.Y - labelSz.Y) * 0.5f), tint, label);

        ImGui.SetCursorScreenPos(new Vector2(ImGui.GetWindowPos().X, tl.Y + size.Y));
        ImGui.Dummy(new Vector2(0f, Px(6f)));
        if (clicked)
        {
            wishlist.Toggle(product.Id);
        }
    }

    /// <summary>The half-second bolt that arcs from the CTA toward the cart icon in the header.</summary>
    private void DrawBoltFlight(OsAppContext ctx, ImDrawListPtr dl)
    {
        var t = (float)(ImGui.GetTime() - _boltFlightStamp) / 0.5f;
        if (ctx.ReduceMotion || t is < 0f or > 1f)
        {
            return;
        }
        var target = ImGui.GetWindowPos() + new Vector2(ImGui.GetWindowSize().X - Px(28f), Px(6f) - ImGui.GetScrollY());
        var mid = (_boltFrom + target) * 0.5f - new Vector2(0f, Px(60f));
        var eased = StoreFx.EaseOut(t);
        var a = Vector2.Lerp(_boltFrom, mid, eased);
        var b = Vector2.Lerp(mid, target, eased);
        var pos = Vector2.Lerp(a, b, eased);
        IconDraw.AddCentered(dl, FontAwesomeIcon.Bolt, Px(12f) * (1f - t * 0.4f), pos,
            ImGui.GetColorU32(StoreChips.GoldColor with { W = 1f - t * 0.6f }));
    }

}
