using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Shared;
using AetherLove.Shared.Store;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Store;

/// <summary>The bag: line items with steppers, the totals card, the insufficient-funds state with a way
/// to the Wallet, and the in-page confirm overlay that runs the sequential checkout loop. A failed line
/// stays in the bag with its typed error; bought lines leave it, because each purchase was real.</summary>
internal sealed class BagScreen(
    IStoreHost host, StoreState state, StoreMediaCache media, StoreBag bag,
    Action backHome, Action<SuccessScreen.Celebration> showSuccess, Action<OsAppContext> openWallet)
{
    private const float PadX = 16f;

    private readonly EntranceAnimation _entrance = new();
    private readonly Dictionary<Guid, StoreProductDto> _products = [];
    private bool _loading;
    private int _generation;
    private bool _confirmOpen;
    private bool _checkoutRunning;
    private string? _lineErrorCode;
    private string[] _lineErrorArgs = [];
    private Guid _lineErrorProduct;
    private double _totalAnimStamp = -1.0;
    private int _shownTotal;
    private int _totalAnimFrom;

    public void Show()
    {
        _entrance.Arm();
        _confirmOpen = false;
        _lineErrorCode = null;
        Refresh();
    }

    /// <summary>Fetches a fresh DTO per line so prices, discounts and owned states are live, then clamps
    /// quantities against MaxPerAccount and drops lines whose products vanished.</summary>
    private void Refresh()
    {
        _loading = true;
        var generation = System.Threading.Interlocked.Increment(ref _generation);
        var lines = bag.Lines.ToArray();
        _ = Task.Run(async () =>
        {
            var fetched = new Dictionary<Guid, StoreProductDto>();
            var gone = new List<Guid>();
            foreach (var line in lines)
            {
                var product = await state.FindFreshAsync(line.ProductId).ConfigureAwait(false);
                if (product is null)
                {
                    gone.Add(line.ProductId);
                }
                else
                {
                    fetched[line.ProductId] = product;
                }
            }
            if (generation != System.Threading.Volatile.Read(ref _generation))
            {
                return;
            }
            _loading = false;
            _products.Clear();
            foreach (var (id, product) in fetched)
            {
                _products[id] = product;
            }
            if (gone.Count > 0)
            {
                bag.RemoveRange(gone);
            }
            foreach (var line in bag.Lines.ToArray())
            {
                if (_products.TryGetValue(line.ProductId, out var product)
                    && product.MaxPerAccount is { } max
                    && line.Quantity > max - product.OwnedQuantity)
                {
                    bag.SetQuantity(line.ProductId, Math.Max(0, max - product.OwnedQuantity));
                }
            }
        });
    }

    public void Draw(OsAppContext ctx)
    {
        _entrance.BeginFrame();
        var winW = ImGui.GetWindowSize().X;
        var lines = bag.Lines;

        ImGui.Dummy(new Vector2(0f, Px(4f)));

        if (lines.Count == 0)
        {
            DrawEmpty(ctx, winW);
            _entrance.EndFrame();
            return;
        }

        foreach (var line in lines.ToArray())
        {
            DrawLine(ctx, winW, line);
        }
        DrawTotals(ctx, winW);
        if (_confirmOpen)
        {
            DrawConfirmOverlay(ctx, winW);
        }
        ImGui.Dummy(new Vector2(0f, Px(16f)));
        _entrance.EndFrame();
    }

    private void DrawEmpty(OsAppContext ctx, float winW)
    {
        var dl = ImGui.GetWindowDrawList();
        ImGui.Dummy(new Vector2(0f, Px(56f)));
        IconDraw.AddCentered(dl, FontAwesomeIcon.ShoppingBag, Px(38f),
            new Vector2(ImGui.GetWindowPos().X + winW * 0.5f, ImGui.GetCursorScreenPos().Y + Px(10f)),
            OsDrawShared.White(0.14f));
        ImGui.Dummy(new Vector2(0f, Px(44f)));
        StoreFx.CenterLine(Loc.T("os.store_bag_empty"), winW, UiColors.Body);
        ImGui.Dummy(new Vector2(0f, Px(4f)));
        StoreFx.CenterWrapped(Loc.T("os.store_bag_empty_hint"), winW, UiColors.Hint, winW - (Px(PadX) * 2f));
        ImGui.Dummy(new Vector2(0f, Px(14f)));
        var btnW = Px(180f);
        ImGui.SetCursorPosX((winW - btnW) * 0.5f);
        if (StoreUi.Button(Loc.T("os.store_bag_browse"), btnW))
        {
            backHome();
        }
        _ = ctx;
    }

    private void DrawLine(OsAppContext ctx, float winW, StoreBag.Line line)
    {
        var dl = ImGui.GetWindowDrawList();
        ImGui.SetCursorPosX(Px(PadX));
        var tl = ImGui.GetCursorScreenPos();
        var rowW = winW - Px(PadX) * 2f;
        var rowH = Px(64f);
        var hasError = _lineErrorCode is not null && _lineErrorProduct == line.ProductId;
        dl.AddRectFilled(tl, tl + new Vector2(rowW, rowH), OsDrawShared.White(0.05f), Px(12f));
        if (hasError)
        {
            dl.AddRect(tl, tl + new Vector2(rowW, rowH), ImGui.GetColorU32(StoreChips.SaleColor with { W = 0.7f }),
                Px(12f), ImDrawFlags.RoundCornersAll, Px(1.2f));
        }

        _products.TryGetValue(line.ProductId, out var product);

        // Thumb.
        var thumbSide = rowH - Px(12f);
        var thumbTl = tl + new Vector2(Px(6f), Px(6f));
        var visual = product is { HasImage: true } ? media.Get(line.ProductId, product.ImageVersion) : null;
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
            TruncateToWidth(product is null ? "…" : StoreLoc.Name(product), rowW - thumbSide - Px(140f)));
        if (product is not null)
        {
            StoreChips.Price(dl, new Vector2(textX, tl.Y + Px(28f)),
                product.DiscountedPriceSparks * line.Quantity, product.PriceSparks * line.Quantity, 0.95f);
        }
        if (hasError)
        {
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.8f, new Vector2(textX, tl.Y + Px(46f)),
                ImGui.GetColorU32(StoreChips.SaleColor), LineErrorText());
        }

        // Stepper + trash, right-aligned.
        var stepperTl = new Vector2(tl.X + rowW - QuantityStepper.Size().X - Px(34f), tl.Y + (rowH - Px(28f)) * 0.5f);
        var quantity = line.Quantity;
        var maxAddable = product?.MaxPerAccount is { } max
            ? Math.Max(1, max - product.OwnedQuantity)
            : 10;
        if (QuantityStepper.Draw($"##bag{line.ProductId:N}", stepperTl, 1, maxAddable, ctx.ReduceMotion, ref quantity)
            && quantity != line.Quantity)
        {
            bag.SetQuantity(line.ProductId, quantity);
            _lineErrorCode = null;
        }
        var trashC = new Vector2(tl.X + rowW - Px(18f), tl.Y + rowH * 0.5f);
        ImGui.SetCursorScreenPos(trashC - new Vector2(Px(10f), Px(10f)));
        if (ImGui.InvisibleButton($"##trash{line.ProductId:N}", new Vector2(Px(20f), Px(20f))))
        {
            bag.Remove(line.ProductId);
            _lineErrorCode = null;
        }
        var trashHovered = ImGui.IsItemHovered();
        if (trashHovered)
        {
            HandOnHover();
        }
        IconDraw.AddCentered(dl, FontAwesomeIcon.Trash, Px(11f), trashC,
            ImGui.GetColorU32(trashHovered ? StoreChips.SaleColor : UiColors.Hint));

        ImGui.SetCursorScreenPos(new Vector2(ImGui.GetWindowPos().X, tl.Y + rowH));
        ImGui.Dummy(new Vector2(0f, Px(8f)));
    }

    private (int Total, int Worth) Totals()
    {
        var total = 0;
        var worth = 0;
        foreach (var line in bag.Lines)
        {
            if (_products.TryGetValue(line.ProductId, out var product))
            {
                total += product.DiscountedPriceSparks * line.Quantity;
                worth += product.PriceSparks * line.Quantity;
            }
        }
        return (total, worth);
    }

    private void DrawTotals(OsAppContext ctx, float winW)
    {
        var dl = ImGui.GetWindowDrawList();
        var (total, worth) = Totals();
        AnimateTotal(ctx, total);

        ImGui.SetCursorPosX(Px(PadX));
        var tl = ImGui.GetCursorScreenPos();
        var cardW = winW - Px(PadX) * 2f;
        var balance = state.Balance ?? 0;
        var short_ = total - balance;
        var cardH = short_ > 0 ? Px(148f) : Px(118f);
        dl.AddRectFilled(tl, tl + new Vector2(cardW, cardH), OsDrawShared.White(0.06f), Px(14f));

        var x = tl.X + Px(14f);
        var y = tl.Y + Px(12f);
        if (worth > total)
        {
            dl.AddText(new Vector2(x, y), ImGui.GetColorU32(UiColors.Hint), Loc.T("os.store_bag_discounts"));
            var saved = $"-{(worth - total):N0}";
            var savedSz = ImGui.CalcTextSize(saved);
            dl.AddText(new Vector2(tl.X + cardW - Px(14f) - savedSz.X, y),
                ImGui.GetColorU32(new Vector4(0.45f, 0.9f, 0.55f, 1f)), saved);
            y += ImGui.GetTextLineHeight() + Px(6f);
        }
        dl.AddText(new Vector2(x, y), ImGui.GetColorU32(UiColors.Body), Loc.T("os.store_bag_total"));
        var totalLabel = _shownTotal.ToString("N0");
        using (UiFonts.H3?.Push())
        {
            var totalSz = ImGui.CalcTextSize(totalLabel);
            StoreChips.Price(dl, new Vector2(tl.X + cardW - Px(20f) - totalSz.X - Px(14f), y - Px(2f)),
                _shownTotal, _shownTotal, 1.15f);
        }
        y += ImGui.GetTextLineHeight() + Px(8f);

        var afterText = short_ > 0
            ? Loc.T("os.store_bag_short", short_.ToString("N0"))
            : Loc.T("os.store_bag_after", (balance - total).ToString("N0"));
        dl.AddText(new Vector2(x, y), ImGui.GetColorU32(short_ > 0 ? StoreChips.SaleColor : UiColors.Hint), afterText);
        y += ImGui.GetTextLineHeight() + Px(10f);

        if (short_ > 0)
        {
            ImGui.SetCursorScreenPos(new Vector2(x, y));
            if (StoreUi.Button(Loc.T("os.store_bag_earn"), cardW - Px(28f)))
            {
                openWallet(ctx);
            }
            y += Px(38f);
        }

        ImGui.SetCursorScreenPos(new Vector2(ImGui.GetWindowPos().X, tl.Y + cardH));
        ImGui.Dummy(new Vector2(0f, Px(12f)));

        // The checkout CTA.
        ImGui.SetCursorPosX(Px(PadX));
        var ctaTl = ImGui.GetCursorScreenPos();
        var ctaSize = new Vector2(cardW, Px(42f));
        var canCheckout = total > 0 && short_ <= 0 && !_loading;
        ImGui.SetCursorScreenPos(ctaTl);
        var clicked = ImGui.InvisibleButton("##checkout", ctaSize) && canCheckout;
        var hovered = canCheckout && ImGui.IsItemHovered();
        if (hovered)
        {
            HandOnHover();
        }
        if (canCheckout)
        {
            OsDrawShared.RoundedGradient(dl, ctaTl, ctaTl + ctaSize, Px(13f),
                hovered ? StorePalette.BlueLight : StorePalette.Blue, StorePalette.BlueDark);
            StoreFx.Sweep(dl, ctaTl, ctaTl + ctaSize, 3.1f, ctx.ReduceMotion);
        }
        else
        {
            dl.AddRectFilled(ctaTl, ctaTl + ctaSize, OsDrawShared.White(0.08f), Px(13f));
        }
        var ctaLabel = Loc.T("os.store_checkout");
        var ctaSz = ImGui.CalcTextSize(ctaLabel);
        dl.AddText(ctaTl + (ctaSize - ctaSz) * 0.5f,
            ImGui.GetColorU32(canCheckout ? UiColors.Body : UiColors.Hint), ctaLabel);
        if (clicked)
        {
            _confirmOpen = true;
            _lineErrorCode = null;
        }
        ImGui.SetCursorScreenPos(new Vector2(ImGui.GetWindowPos().X, ctaTl.Y + ctaSize.Y));
    }

    private void AnimateTotal(OsAppContext ctx, int total)
    {
        if (ctx.ReduceMotion || _totalAnimStamp < 0.0)
        {
            _shownTotal = total;
            _totalAnimStamp = 0.0;
            return;
        }
        if (_shownTotal == total)
        {
            return;
        }
        if (_totalAnimStamp == 0.0)
        {
            _totalAnimStamp = ImGui.GetTime();
            _totalAnimFrom = _shownTotal;
        }
        var progress = StoreFx.EaseOut(Math.Clamp((float)(ImGui.GetTime() - _totalAnimStamp) / 0.4f, 0f, 1f));
        _shownTotal = _totalAnimFrom + (int)((total - _totalAnimFrom) * progress);
        if (progress >= 1f)
        {
            _shownTotal = total;
            _totalAnimStamp = 0.0;
        }
    }

    /// <summary>The in-page confirm sheet, drawn per the overlay doctrine (own child layer, controls
    /// before the scrim). Confirm runs the sequential per-line checkout; a mid-bag failure keeps the
    /// failed and remaining lines with a typed error, because the bought lines were each real purchases.</summary>
    private void DrawConfirmOverlay(OsAppContext ctx, float winW)
    {
        var origin = ImGui.GetWindowPos() + new Vector2(0f, ImGui.GetScrollY());
        var avail = ImGui.GetWindowSize();
        ImGui.SetCursorScreenPos(origin);
        using var layer = ImRaii.Child("##storeConfirm", avail, false,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoBackground);
        if (!layer)
        {
            return;
        }
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(origin, origin + avail, OsDrawShared.Black(0.66f));

        var (total, _) = Totals();
        var balance = state.Balance ?? 0;
        var panelW = avail.X - Px(44f);
        var panelH = Px(196f);
        var panelTl = origin + new Vector2((avail.X - panelW) * 0.5f, (avail.Y - panelH) * 0.5f);
        dl.AddRectFilled(panelTl, panelTl + new Vector2(panelW, panelH),
            ImGui.GetColorU32(new Vector4(0.09f, 0.08f, 0.12f, 1f)), Px(16f));
        dl.AddRect(panelTl, panelTl + new Vector2(panelW, panelH), OsDrawShared.White(0.12f), Px(16f),
            ImDrawFlags.RoundCornersAll, Px(1f));

        ImGui.SetCursorScreenPos(panelTl + new Vector2(Px(14f), Px(12f)));
        AetherLove.Widgets.ModalUi.Header(panelW - Px(28f), Loc.T("os.store_confirm_title"), StorePalette.Blue);

        var x = panelTl.X + Px(16f);
        dl.AddText(new Vector2(x, panelTl.Y + Px(48f)), ImGui.GetColorU32(UiColors.Body),
            Loc.T("os.store_confirm_items", bag.Count));
        StoreChips.Price(dl, new Vector2(x, panelTl.Y + Px(70f)), total, total, 1.2f);
        dl.AddText(new Vector2(x, panelTl.Y + Px(98f)), ImGui.GetColorU32(UiColors.Hint),
            Loc.T("os.store_confirm_after", balance.ToString("N0"), (balance - total).ToString("N0")));

        var btnY = panelTl.Y + panelH - Px(52f);
        var halfW = (panelW - Px(44f)) * 0.5f;
        if (_checkoutRunning)
        {
            AetherLove.Widgets.LoadingSpinner.Draw(
                new Vector2(panelTl.X + panelW * 0.5f, btnY + Px(16f)), Px(12f), Px(3f),
                StorePalette.BlueU32);
        }
        else
        {
            ImGui.SetCursorScreenPos(new Vector2(x, btnY));
            if (StoreUi.Button(Loc.T("os.store_confirm_cancel"), halfW))
            {
                _confirmOpen = false;
            }
            ImGui.SameLine();
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Px(6f));
            if (StoreUi.Button(Loc.T("os.store_confirm_buy"), halfW))
            {
                RunCheckout();
            }
        }

        ImGui.SetCursorScreenPos(origin);
        if (ImGui.InvisibleButton("##storeConfirmScrim", avail) && !_checkoutRunning)
        {
            _confirmOpen = false;
        }
        _ = ctx;
    }

    private void RunCheckout()
    {
        _checkoutRunning = true;
        var lines = bag.Lines.ToArray();
        var snapshot = _products.ToDictionary(kv => kv.Key, kv => kv.Value);
        _ = Task.Run(async () =>
        {
            var boughtLines = new List<SuccessScreen.BoughtLine>();
            var spent = 0;
            long newBalance = state.Balance ?? 0;
            string? errorCode = null;
            string[] errorArgs = [];
            var errorProduct = Guid.Empty;

            foreach (var line in lines)
            {
                var result = await host.PurchaseAsync(line.ProductId, line.Quantity).ConfigureAwait(false);
                if (result.Success && result.Result is { } purchase)
                {
                    spent += purchase.TotalPaidSparks;
                    newBalance = purchase.NewBalance;
                    bag.Remove(line.ProductId);
                    if (snapshot.TryGetValue(line.ProductId, out var product))
                    {
                        boughtLines.Add(new SuccessScreen.BoughtLine(
                            product.Id, StoreLoc.Name(product), line.Quantity, product.AccentColor,
                            product.HasImage, product.ItemKind, Enableables(product), product.ImageVersion));
                    }
                }
                else
                {
                    errorCode = result.ErrorCode;
                    errorArgs = result.ErrorArgs;
                    errorProduct = line.ProductId;
                    break;
                }
            }

            _checkoutRunning = false;
            _confirmOpen = false;
            state.SetBalance(newBalance);
            if (errorCode is not null)
            {
                _lineErrorCode = errorCode;
                _lineErrorArgs = errorArgs;
                _lineErrorProduct = errorProduct;
                Refresh();
                return;
            }
            if (boughtLines.Count > 0)
            {
                var oldBalance = newBalance + spent;
                showSuccess(new SuccessScreen.Celebration(boughtLines, spent, oldBalance, newBalance));
            }
        });
    }

    /// <summary>What a bought line can switch on from the success scene. A bundle contributes one entry per
    /// wearable child, and the scene gives each its own row.</summary>
    private static IReadOnlyList<SuccessScreen.Enableable> Enableables(StoreProductDto product)
    {
        if (Wearable(product.ItemKind))
        {
            return [new SuccessScreen.Enableable(
                product.Id, product.ItemKind, product.ItemRef, StoreLoc.Name(product), product.HasImage,
                product.ImageVersion)];
        }
        return product.BundleItems
            .Where(i => Wearable(i.ItemKind))
            .Select(i => new SuccessScreen.Enableable(
                i.ChildProductId, i.ItemKind, i.ItemRef, StoreLoc.Name(i), true, i.ImageVersion))
            .DistinctBy(e => e.ProductId)
            .ToArray();
    }

    private static bool Wearable(StoreItemKind kind) =>
        kind is StoreItemKind.ThemePack or StoreItemKind.AvatarFrame;

    private string LineErrorText()
    {
        var key = $"huberror.{_lineErrorCode}";
        var template = Loc.T(key);
        if (template == key)
        {
            return Loc.T("os.store_checkout_failed");
        }
        try
        {
            return string.Format(template, _lineErrorArgs);
        }
        catch (FormatException)
        {
            return template;
        }
    }
}
