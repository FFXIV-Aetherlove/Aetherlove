using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AetherOS.Apps.Aetherling.Ui;
using AetherLove.UI;
using AetherLove.Widgets;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Aetherling.Screens;

/// <summary>The way in. One button, one price, and not a word about what it buys.</summary>
internal sealed class AdoptScreen(IAetherlingHost host)
{
    private long? _balance;
    private bool _busy;
    private bool _purchased;
    private string? _error;
    private string? _pendingError;
    private double _errorUntil;
    private double _shakeUntil;
    private double _shown;

    public void OnShow()
    {
        _shown = ImGui.GetTime();
        _error = null;
        _ = RefreshBalanceAsync();
    }

    /// <summary>True once, on the frame after the server has confirmed the purchase. The caller swaps the
    /// view; doing it from the round trip's continuation would drive ImGui off the draw thread.</summary>
    public bool TryTakePurchased()
    {
        if (!_purchased)
        {
            return false;
        }
        _purchased = false;
        return true;
    }

    private async Task RefreshBalanceAsync() => _balance = await host.GetSparkBalanceAsync().ConfigureAwait(false);

    public void Draw(OsAppContext ctx, int price)
    {
        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var time = ImGui.GetTime();
        var t = (float)(time - _shown);
        var fade = ctx.ReduceMotion ? 1f : Look.EaseOut(t / 0.9f);

        if (Interlocked.Exchange(ref _pendingError, null) is { } message)
        {
            _error = message;
            _errorUntil = time + 5.0;
        }

        dl.AddRectFilled(origin, origin + size, Look.U32(Look.Void));
        Look.Motes(dl, origin, size, 34, Look.CrystalPale, 0.55f * fade, time, ctx.ReduceMotion);

        var centre = origin + new Vector2(size.X * 0.5f, size.Y * 0.42f);
        var breath = ctx.ReduceMotion ? 0.5f : Look.Breathe(time, 6.5f);
        var radius = MathF.Min(size.X * 0.26f, size.Y * 0.20f) * (0.97f + (0.03f * breath));
        Look.Halo(dl, centre, radius * 2.6f, Look.Crystal, 0.05f * fade * (0.6f + (0.4f * breath)), 6);
        dl.AddCircleFilled(centre, radius, Look.U32(new Vector4(0.05f, 0.07f, 0.12f, 1f), fade), 48);
        dl.AddCircle(centre, radius, Look.U32(Look.Crystal, 0.10f * fade), 48, Px(1f));

        var noise = Garble.Wrap(Garble.Block(4242, 6, 0.05f), 22);
        Look.CentredBlock(dl, noise, origin.X + (size.X * 0.5f), origin.Y + (size.Y * 0.63f),
            Look.U32(Look.Whisper, 0.5f * fade), 0.85f, ImGui.GetTextLineHeight());

        DrawBalance(dl, origin, size, price, fade);
        DrawBuy(ctx, dl, origin, size, price, fade, time);

        if (_error is { Length: > 0 } && time < _errorUntil)
        {
            Look.Centred(dl, _error, origin.X + (size.X * 0.5f), origin.Y + size.Y - Px(26f),
                Look.U32(new Vector4(0.95f, 0.45f, 0.45f, 1f), fade), 0.85f);
        }
    }

    private void DrawBalance(ImDrawListPtr dl, Vector2 origin, Vector2 size, int price, float fade)
    {
        var label = _balance?.ToString("N0") ?? "···";
        var enough = _balance is null || _balance >= price;
        var textWidth = ImGui.CalcTextSize(label).X;
        var chipW = textWidth + Px(34f);
        var chipH = Px(24f);
        var tl = new Vector2(origin.X + ((size.X - chipW) * 0.5f), origin.Y + Px(22f));

        dl.AddRectFilled(tl, tl + new Vector2(chipW, chipH), Look.U32(Look.Spark with { W = 0.12f }, fade), chipH * 0.5f);
        IconDraw.AddCentered(dl, FontAwesomeIcon.Bolt, Px(11f),
            tl + new Vector2(Px(13f), chipH * 0.5f), Look.U32(Look.Spark, fade));
        dl.AddText(tl + new Vector2(Px(24f), (chipH - ImGui.GetTextLineHeight()) * 0.5f),
            Look.U32(enough ? Look.Spark : new Vector4(0.95f, 0.45f, 0.45f, 1f), fade), label);
    }

    private void DrawBuy(
        OsAppContext ctx, ImDrawListPtr dl, Vector2 origin, Vector2 size, int price, float fade, double time)
    {
        var enough = _balance is null || _balance >= price;
        var live = enough && !_busy;
        var label = ctx.Localize("os.aetherling_purchase");
        var priceText = price.ToString("N0");
        var height = Px(44f);
        var width = size.X - (Px(30f) * 2f);
        var shake = time < _shakeUntil && !ctx.ReduceMotion
            ? MathF.Sin((float)(time * 46f)) * Px(3f)
            : 0f;
        var tl = new Vector2(origin.X + ((size.X - width) * 0.5f) + shake, origin.Y + size.Y - height - Px(52f));

        ImGui.SetCursorScreenPos(tl);
        var pressed = ImGui.InvisibleButton("##aetherlingBuy", new Vector2(width, height));
        var hovered = ImGui.IsItemHovered();
        if (hovered && !_busy)
        {
            HandOnHover();
        }
        if (pressed && !_busy)
        {
            if (live)
            {
                Purchase();
            }
            else
            {
                _shakeUntil = time + 0.35f;
            }
        }

        var pulse = ctx.ReduceMotion || !live ? 0f : Look.Breathe(time, 2.6f);
        var radius = height * 0.5f;
        var body = live ? 0.16f + (0.08f * pulse) : 0.06f;
        dl.AddRectFilled(tl, tl + new Vector2(width, height), Look.U32(Look.Crystal with { W = body }, fade), radius);
        dl.AddRect(tl, tl + new Vector2(width, height),
            Look.U32(Look.Crystal, ((live ? 0.55f : 0.2f) + (0.35f * pulse)) * fade),
            radius, ImDrawFlags.RoundCornersAll, Px(1.4f));

        if (_busy)
        {
            LoadingSpinner.Draw(
                tl + new Vector2(width * 0.5f, height * 0.5f), Px(9f), Px(2.5f), Look.U32(Look.CrystalPale, fade));
            return;
        }

        var pillW = ImGui.CalcTextSize(priceText).X + Px(26f);
        var gap = Px(10f);
        var labelW = ImGui.CalcTextSize(label).X;
        var startX = tl.X + ((width - labelW - gap - pillW) * 0.5f);
        var textY = tl.Y + ((height - ImGui.GetTextLineHeight()) * 0.5f);
        dl.AddText(new Vector2(startX, textY), Look.U32(Look.CrystalPale, (live ? 1f : 0.45f) * fade), label);

        var pillTl = new Vector2(startX + labelW + gap, tl.Y + ((height - Px(22f)) * 0.5f));
        dl.AddRectFilled(pillTl, pillTl + new Vector2(pillW, Px(22f)),
            Look.U32(Look.Spark with { W = live ? 0.20f : 0.10f }, fade), Px(11f));
        IconDraw.AddCentered(dl, FontAwesomeIcon.Bolt, Px(10f),
            pillTl + new Vector2(Px(11f), Px(11f)), Look.U32(Look.Spark, (live ? 1f : 0.5f) * fade));
        dl.AddText(pillTl + new Vector2(Px(20f), (Px(22f) - ImGui.GetTextLineHeight()) * 0.5f),
            Look.U32(Look.Spark, (live ? 1f : 0.5f) * fade), priceText);
    }

    private void Purchase()
    {
        _busy = true;
        _error = null;
        _ = Task.Run(async () =>
        {
            try
            {
                await host.PurchaseAsync().ConfigureAwait(false);
                _busy = false;
                _purchased = true;
            }
            catch (Exception ex)
            {
                // The reply can be lost after the server has already committed the debit, so ask what
                // actually happened before calling it a failure: a real core means the purchase landed.
                if (await host.RefreshAsync().ConfigureAwait(false) is not null)
                {
                    _busy = false;
                    _purchased = true;
                    return;
                }
                Interlocked.Exchange(ref _pendingError, host.DescribeError(ex));
                _busy = false;
                await RefreshBalanceAsync().ConfigureAwait(false);
            }
        });
    }
}
