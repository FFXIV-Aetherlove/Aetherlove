using System;
using System.Collections.Generic;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Shared.Store;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Store;

/// <summary>The auto-advancing hero banner strip: one gradient banner per live sale (with a real
/// countdown) plus featured product banners built from the New rail. Swipeable with a direction lock so
/// vertical flicks still scroll the page; page dots are clickable; auto-advance pauses on hover.</summary>
internal sealed class HeroCarousel(StoreMediaCache media)
{
    private const float AutoAdvanceSeconds = 5f;
    private const float SlideSeconds = 0.28f;

    internal abstract record Banner;

    internal sealed record SaleBanner(StoreSaleBannerDto Sale) : Banner;

    internal sealed record FeaturedBanner(StoreProductDto Product) : Banner;

    private readonly List<Banner> _banners = [];
    private int _page;
    private float _animOffset;
    private float _animFrom;
    private double _animStamp = -1.0;
    private double _autoStamp;
    private bool _dragging;
    private bool _directionLocked;
    private bool _horizontal;
    private float _dragPx;
    private Vector2 _dragOrigin;

    public void SetContent(StoreFrontDto front)
    {
        _banners.Clear();
        foreach (var sale in front.ActiveSales)
        {
            _banners.Add(new SaleBanner(sale));
        }
        var featured = 0;
        foreach (var product in front.NewItems)
        {
            if (featured >= 2)
            {
                break;
            }
            if (product.HasImage)
            {
                _banners.Add(new FeaturedBanner(product));
                featured++;
            }
        }
        _page = Math.Clamp(_page, 0, Math.Max(0, _banners.Count - 1));
        _animOffset = _page;
    }

    /// <summary>Draws the strip; returns a clicked banner, if any.</summary>
    public Banner? Draw(OsAppContext ctx, float winW)
    {
        if (_banners.Count == 0)
        {
            return null;
        }
        const float padX = 16f;
        var height = Px(150f);
        var bannerW = winW - Px(padX) * 2f;
        ImGui.SetCursorPosX(Px(padX));
        var tl = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();

        ImGui.SetCursorScreenPos(tl);
        ImGui.InvisibleButton("##storeHero", new Vector2(bannerW, height));
        var hovered = ImGui.IsItemHovered();
        var active = ImGui.IsItemActive();
        var clicked = false;
        if (hovered)
        {
            HandOnHover();
        }

        // Direction lock: the carousel only claims the gesture once horizontal motion wins, so a
        // vertical flick starting on the banner still scrolls the page.
        if (active && !_dragging)
        {
            _dragging = true;
            _directionLocked = false;
            _horizontal = false;
            _dragPx = 0f;
            _dragOrigin = ImGui.GetMousePos();
        }
        if (_dragging)
        {
            var delta = ImGui.GetMousePos() - _dragOrigin;
            if (!_directionLocked && (MathF.Abs(delta.X) > Px(8f) || MathF.Abs(delta.Y) > Px(8f)))
            {
                _directionLocked = true;
                _horizontal = MathF.Abs(delta.X) >= MathF.Abs(delta.Y);
            }
            if (_horizontal)
            {
                _dragPx = delta.X;
            }
            if (!active)
            {
                if (_horizontal)
                {
                    if (MathF.Abs(_dragPx) < Px(4f))
                    {
                        clicked = true;
                    }
                    else if (MathF.Abs(_dragPx) > bannerW * 0.25f)
                    {
                        Advance(_dragPx < 0 ? 1 : -1);
                    }
                    else
                    {
                        StartSlide();
                    }
                }
                else if (!_directionLocked)
                {
                    clicked = true;
                }
                _dragging = false;
                _dragPx = 0f;
            }
        }

        if (!ctx.ReduceMotion && !_dragging && !hovered
            && ImGui.GetTime() - _autoStamp > AutoAdvanceSeconds && _banners.Count > 1)
        {
            Advance(1);
        }

        // The rendered offset eases toward the page; a live drag shifts it directly.
        var target = (float)_page;
        if (_animStamp >= 0.0)
        {
            var t = StoreFx.EaseOut(Math.Clamp((float)(ImGui.GetTime() - _animStamp) / SlideSeconds, 0f, 1f));
            _animOffset = _animFrom + (target - _animFrom) * t;
            if (t >= 1f)
            {
                _animStamp = -1.0;
            }
        }
        else
        {
            _animOffset = target;
        }
        if (ctx.ReduceMotion)
        {
            _animOffset = target;
            _animStamp = -1.0;
        }
        var renderOffset = _animOffset - (_dragging && _horizontal ? _dragPx / bannerW : 0f);

        dl.PushClipRect(tl, tl + new Vector2(bannerW, height), true);
        for (var i = 0; i < _banners.Count; i++)
        {
            var x = tl.X + (i - renderOffset) * (bannerW + Px(10f));
            if (x > tl.X + bannerW || x + bannerW < tl.X)
            {
                continue;
            }
            DrawBanner(ctx, dl, _banners[i], new Vector2(x, tl.Y), new Vector2(bannerW, height), i);
        }
        dl.PopClipRect();

        // Clickable page dots; the active one stretches into a bar and eases between slots.
        if (_banners.Count > 1)
        {
            var dotY = tl.Y + height + Px(8f);
            var dotSpace = Px(16f);
            var dotsW = _banners.Count * dotSpace;
            var dotsX = tl.X + (bannerW - dotsW) * 0.5f;
            for (var i = 0; i < _banners.Count; i++)
            {
                var center = new Vector2(dotsX + i * dotSpace + dotSpace * 0.5f, dotY + Px(4f));
                ImGui.SetCursorScreenPos(center - new Vector2(dotSpace * 0.5f, Px(6f)));
                if (ImGui.InvisibleButton($"##heroDot{i}", new Vector2(dotSpace, Px(12f))))
                {
                    _page = i;
                    StartSlide();
                    _autoStamp = ImGui.GetTime();
                }
                if (ImGui.IsItemHovered())
                {
                    HandOnHover();
                }
                dl.AddCircleFilled(center, Px(3f), OsDrawShared.White(0.3f));
            }
            var activeCenter = new Vector2(dotsX + _animOffset * dotSpace + dotSpace * 0.5f, dotY + Px(4f));
            dl.AddRectFilled(activeCenter - new Vector2(Px(7f), Px(3f)), activeCenter + new Vector2(Px(7f), Px(3f)),
                StorePalette.BlueU32, Px(3f));
            ImGui.SetCursorScreenPos(new Vector2(ImGui.GetWindowPos().X, dotY + Px(14f)));
        }
        else
        {
            ImGui.SetCursorScreenPos(new Vector2(ImGui.GetWindowPos().X, tl.Y + height + Px(10f)));
        }

        return clicked && _page < _banners.Count ? _banners[_page] : null;
    }

    private void Advance(int direction)
    {
        _page = _banners.Count == 0 ? 0 : ((_page + direction) % _banners.Count + _banners.Count) % _banners.Count;
        StartSlide();
        _autoStamp = ImGui.GetTime();
    }

    private void StartSlide()
    {
        _animFrom = _animOffset - (_dragging && _horizontal ? _dragPx / MathF.Max(1f, Px(300f)) : 0f);
        _animFrom = _animOffset;
        _animStamp = ImGui.GetTime();
    }

    private void DrawBanner(OsAppContext ctx, ImDrawListPtr dl, Banner banner, Vector2 tl, Vector2 size, int index)
    {
        var rounding = Px(16f);
        var br = tl + size;
        switch (banner)
        {
            case SaleBanner sale:
            {
                OsDrawShared.RoundedGradient(dl, tl, br, rounding,
                    StoreChips.SaleColor with { W = 1f } * new Vector4(0.85f, 0.85f, 0.85f, 1f),
                    new Vector4(0.25f, 0.07f, 0.2f, 1f));
                using (UiFonts.H2?.Push())
                {
                    dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), tl + new Vector2(Px(16f), Px(18f)),
                        0xFFFFFFFFu, TruncateToWidth(StoreLoc.Name(sale.Sale), size.X - Px(120f)));
                }
                using (UiFonts.H1?.Push())
                {
                    dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), tl + new Vector2(Px(16f), Px(50f)),
                        ImGui.GetColorU32(StoreChips.GoldColor), $"-{sale.Sale.Percent}%");
                }
                dl.AddText(tl + new Vector2(Px(16f), Px(96f)), OsDrawShared.White(0.85f),
                    Loc.T("os.store_banner_sale_sub"));
                StoreChips.Countdown(dl, new Vector2(br.X - Px(118f), tl.Y + Px(12f)), sale.Sale.EndsAtUtc);
                DrawCta(dl, br, Loc.T("os.store_banner_cta_sale"));
                break;
            }
            case FeaturedBanner featured:
            {
                var (top, bottom, _) = StoreFx.CardColors(featured.Product.AccentColor);
                OsDrawShared.RoundedGradient(dl, tl, br, rounding, top, bottom);
                var visual = media.Get(featured.Product.Id, featured.Product.ImageVersion);
                if (visual?.Tex?.GetWrapOrDefault() is { } wrap)
                {
                    // A theme's bezel starts below the title band, so its own top border sits under the
                    // name instead of hiding behind it; the shorter box also zooms the detail in.
                    var imgTl = tl;
                    var corners = ImDrawFlags.RoundCornersAll;
                    if (featured.Product.ItemKind == StoreItemKind.ThemePack)
                    {
                        imgTl = tl with { Y = tl.Y + size.Y * 0.34f };
                        corners = ImDrawFlags.RoundCornersBottom;
                    }
                    var (uv0, uv1) = StoreArtCrop.Uv(
                        featured.Product.ItemKind, wrap.Width, wrap.Height, size.X, br.Y - imgTl.Y);
                    dl.AddImageRounded(wrap.Handle, imgTl, br, uv0, uv1, 0xFFFFFFFFu, rounding, corners);
                    dl.AddRectFilledMultiColor(tl, br,
                        OsDrawShared.Black(0.55f), OsDrawShared.Black(0.15f),
                        OsDrawShared.Black(0.15f), OsDrawShared.Black(0.6f));
                }
                StoreChips.NewRibbon(dl, tl);
                using (UiFonts.H2?.Push())
                {
                    dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), tl + new Vector2(Px(16f), Px(18f)),
                        0xFFFFFFFFu, TruncateToWidth(StoreLoc.Name(featured.Product), size.X - Px(40f)));
                }
                StoreChips.Price(dl, tl + new Vector2(Px(23f), Px(55f)),
                    featured.Product.DiscountedPriceSparks, featured.Product.PriceSparks, 1.1f, plate: true);
                DrawCta(dl, br, Loc.T("os.store_banner_cta_look"));
                break;
            }
        }
        StoreFx.Sweep(dl, tl, br, index * 1.3f, ctx.ReduceMotion, strength: 0.8f);
    }

    private static void DrawCta(ImDrawListPtr dl, Vector2 br, string label)
    {
        var labelSz = ImGui.CalcTextSize(label);
        var pillSize = labelSz + new Vector2(Px(30f), Px(10f));
        var tl = br - pillSize - new Vector2(Px(12f), Px(12f));
        var radius = pillSize.Y * 0.5f;
        // Opaque and dark, wearing the blue as an edge and a label rather than as a fill: a call to
        // action has to be findable, not loud.
        dl.AddRectFilled(tl, tl + pillSize, ImGui.ColorConvertFloat4ToU32(StorePalette.Surface), radius);
        dl.AddRect(tl, tl + pillSize, StorePalette.BlueWithAlpha(0.65f), radius,
            ImDrawFlags.RoundCornersAll, Px(1.2f));
        dl.AddText(tl + new Vector2(Px(11f), Px(5f)), StorePalette.BlueLightU32, label);
        IconDraw.AddCentered(dl, FontAwesomeIcon.ChevronRight, Px(9f),
            new Vector2(tl.X + pillSize.X - Px(11f), tl.Y + radius), StorePalette.BlueLightU32);
    }
}
