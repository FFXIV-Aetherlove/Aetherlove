using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Shared.Store;
using AetherLove.UI;
using AetherLove.Widgets;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Store;

/// <summary>The purchase celebration, staged like a match scene: radial glow, rotating rays, an
/// overshooting bolt, confetti, a coin fountain, the bought items cascading in, and the balance ticking
/// down before the Done button fades up. Under reduce-motion everything snaps to its final frame.</summary>
internal sealed class SuccessScreen(IStoreHost host, StoreMediaCache media, Action done)
{
    /// <summary>Something a bought line can switch on right here. A bundle contributes one per wearable
    /// child, and each gets its own row, because "enable all" hides what the bundle actually contained.</summary>
    internal sealed record Enableable(
        Guid ProductId, StoreItemKind Kind, string ItemRef, string Name, bool HasImage, int ImageVersion = 0);

    internal sealed record BoughtLine(
        Guid ProductId, string Name, int Quantity, uint AccentColor, bool HasImage, StoreItemKind Kind,
        IReadOnlyList<Enableable> Enableables, int ImageVersion = 0);

    internal sealed record Celebration(
        IReadOnlyList<BoughtLine> Lines, int Spent, long OldBalance, long NewBalance);

    private enum EnableState { Idle, Busy, Done, Failed }

    private sealed record Coin(float Angle, float Speed, float Size, float Spin);

    private readonly ConfettiBurst _confetti = new();
    private readonly List<Coin> _coins = [];
    // Written from the enable task, read by the draw thread.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, EnableState> _enableStates = new();
    private Celebration? _celebration;
    private double _startStamp;
    private bool _confettiFired;

    public void Show(Celebration celebration)
    {
        _celebration = celebration;
        _startStamp = ImGui.GetTime();
        _confettiFired = false;
        _enableStates.Clear();
        _coins.Clear();
        // A deterministic fan of coins; per-index phases make it read as random.
        for (var i = 0; i < 14; i++)
        {
            var angle = -MathF.PI * 0.5f + (i - 7f) * 0.19f + MathF.Sin(i * 3.7f) * 0.08f;
            _coins.Add(new Coin(angle, Px(120f) + (i * 37 % 60), Px(5f) + i % 3, i * 1.3f));
        }
    }

    public void Draw(OsAppContext ctx)
    {
        if (_celebration is not { } celebration)
        {
            done();
            return;
        }
        var origin = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var dl = ImGui.GetWindowDrawList();
        var t = ctx.ReduceMotion ? 10f : (float)(ImGui.GetTime() - _startStamp);
        var accent = StoreFx.CardColors(celebration.Lines.Count > 0 ? celebration.Lines[0].AccentColor : 0xFF7C5CDB).Accent;
        var center = origin + new Vector2(size.X * 0.5f, size.Y * 0.30f);

        // Backdrop + pulsing radial glow.
        dl.AddRectFilled(origin, origin + size, ImGui.GetColorU32(new Vector4(0.04f, 0.03f, 0.07f, 1f)));
        var pulse = ctx.ReduceMotion ? 1f : 0.85f + 0.15f * MathF.Sin(t * MathF.Tau * 0.5f);
        for (var i = 6; i >= 1; i--)
        {
            dl.AddCircleFilled(center, Px(26f) * i * pulse,
                ImGui.GetColorU32(accent with { W = 0.05f * (7 - i) / 6f }));
        }

        // Rotating golden rays.
        if (!ctx.ReduceMotion && t > 0.15f)
        {
            var rayAlpha = Math.Clamp((t - 0.15f) / 0.5f, 0f, 1f) * 0.1f;
            var rotation = t * 0.4f;
            for (var i = 0; i < 12; i++)
            {
                var angle = rotation + i * MathF.Tau / 12f;
                var dir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                var normal = new Vector2(-dir.Y, dir.X) * Px(14f);
                dl.AddTriangleFilled(center, center + dir * Px(220f) + normal, center + dir * Px(220f) - normal,
                    ImGui.GetColorU32(StoreChips.GoldColor with { W = rayAlpha }));
            }
        }

        // The overshooting bolt.
        if (t > 0.2f)
        {
            var boltT = Math.Clamp((t - 0.2f) / 0.45f, 0f, 1f);
            var scale = ctx.ReduceMotion ? 1f : StoreFx.Overshoot(boltT);
            IconDraw.AddCentered(dl, FontAwesomeIcon.Bolt, Px(46f) * scale, center,
                ImGui.GetColorU32(StoreChips.GoldColor));
        }

        // Confetti, one burst.
        if (!ctx.ReduceMotion && t > 0.25f)
        {
            if (!_confettiFired)
            {
                _confettiFired = true;
                _confetti.Reset();
            }
            _confetti.Draw(origin, origin + size);
        }

        // The coin fountain, arcing out under fake gravity and fading by ~1.8s.
        if (!ctx.ReduceMotion && t is > 0.3f and < 2.1f)
        {
            var coinT = t - 0.3f;
            foreach (var coin in _coins)
            {
                var dir = new Vector2(MathF.Cos(coin.Angle), MathF.Sin(coin.Angle));
                var pos = center + dir * coin.Speed * coinT
                    + new Vector2(0f, Px(90f)) * coinT * coinT;
                var alpha = Math.Clamp(1.8f - coinT, 0f, 1f);
                var wobble = 0.6f + 0.4f * MathF.Sin(coinT * 9f + coin.Spin);
                StoreFx.Ellipse(dl, pos, new Vector2(Px(coin.Size) * wobble, Px(coin.Size)),
                    ImGui.GetColorU32(StoreChips.GoldColor with { W = alpha }));
            }
        }

        // Headline.
        var headline = Loc.T("os.store_success_title");
        using (UiFonts.H2?.Push())
        {
            var headSz = ImGui.CalcTextSize(headline);
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
                center + new Vector2(-headSz.X * 0.5f, Px(56f)), 0xFFFFFFFFu, headline);
        }

        // The bought items cascade in with a stagger, each sweeping once as it lands.
        var rows = BuildRows(celebration);
        var listY = center.Y + Px(100f);
        var shownRows = Math.Min(rows.Count, MaxRows);
        var y = listY;
        for (var i = 0; i < shownRows; i++)
        {
            var row = rows[i];
            var rowT = Math.Clamp((t - 0.6f - i * 0.12f) / 0.3f, 0f, 1f);
            var rowH = row.Child ? Px(32f) : Px(38f);
            if (rowT <= 0f)
            {
                y += rowH + Px(6f);
                continue;
            }
            var eased = StoreFx.EaseOut(rowT);
            var indent = row.Child ? Px(18f) : 0f;
            var rowW = size.X - Px(72f) - indent;
            var rowTl = new Vector2(origin.X + Px(36f) + indent, y + Px(14f) * (1f - eased));
            var alpha = eased;
            dl.AddRectFilled(rowTl, rowTl + new Vector2(rowW, rowH),
                OsDrawShared.White((row.Child ? 0.05f : 0.07f) * alpha), Px(10f));
            var visual = row.HasImage ? media.Get(row.ProductId, row.ImageVersion) : null;
            if (visual?.Tex?.GetWrapOrDefault() is { } wrap)
            {
                var (uv0, uv1) = StoreArtCrop.Uv(row.Kind, wrap.Width, wrap.Height, rowH - Px(8f), rowH - Px(8f));
                dl.AddImageRounded(wrap.Handle, rowTl + new Vector2(Px(4f), Px(4f)),
                    rowTl + new Vector2(rowH - Px(4f), rowH - Px(4f)), uv0, uv1,
                    ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, alpha)), Px(7f));
            }
            else
            {
                IconDraw.AddCentered(dl, StoreCard.KindGlyph(row.Kind), Px(13f),
                    rowTl + new Vector2(rowH * 0.5f, rowH * 0.5f), OsDrawShared.White(0.35f * alpha));
            }
            var pillW = row.Enable is null ? 0f : Px(78f);
            var textX = rowTl.X + rowH + Px(8f);
            var textW = rowW - rowH - Px(20f) - pillW;
            if (row.Sub is { } sub)
            {
                dl.AddText(new Vector2(textX, rowTl.Y + Px(5f)),
                    ImGui.GetColorU32(UiColors.Body with { W = alpha }), TruncateToWidth(row.Label, textW));
                dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.82f,
                    new Vector2(textX, rowTl.Y + Px(5f) + ImGui.GetTextLineHeight() * 0.92f),
                    ImGui.GetColorU32(StoreChips.GoldColor with { W = alpha }), sub);
            }
            else
            {
                dl.AddText(new Vector2(textX, rowTl.Y + (rowH - ImGui.GetTextLineHeight()) * 0.5f),
                    ImGui.GetColorU32(UiColors.Body with { W = alpha }), TruncateToWidth(row.Label, textW));
            }
            if (row.Enable is { } enable && rowT >= 1f)
            {
                DrawEnablePill(dl, enable, row.AccentColor,
                    new Vector2(rowTl.X + rowW - pillW - Px(6f), rowTl.Y + (rowH - Px(24f)) * 0.5f),
                    new Vector2(pillW, Px(24f)));
            }
            if (rowT >= 1f)
            {
                StoreFx.Sweep(dl, rowTl, rowTl + new Vector2(rowW, rowH), i * 0.4f, ctx.ReduceMotion, 0.7f);
            }
            y += rowH + Px(6f);
        }
        if (rows.Count > MaxRows)
        {
            dl.AddText(new Vector2(origin.X + Px(36f), y + Px(2f)),
                ImGui.GetColorU32(UiColors.Hint), Loc.T("os.store_success_more", rows.Count - MaxRows));
        }

        // The balance ticking down, with the spent floater drifting up.
        if (t > 0.8f)
        {
            var tickT = Math.Clamp((t - 0.8f) / 0.9f, 0f, 1f);
            var shown = celebration.OldBalance
                + (long)((celebration.NewBalance - celebration.OldBalance) * StoreFx.EaseOut(tickT));
            var balanceLabel = shown.ToString("N0");
            using (UiFonts.H3?.Push())
            {
                var balSz = ImGui.CalcTextSize(balanceLabel);
                var balPos = new Vector2(origin.X + (size.X - balSz.X) * 0.5f, origin.Y + size.Y - Px(118f));
                IconDraw.AddCentered(dl, FontAwesomeIcon.Bolt, Px(14f),
                    balPos + new Vector2(-Px(14f), balSz.Y * 0.5f), ImGui.GetColorU32(StoreChips.GoldColor));
                dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), balPos, ImGui.GetColorU32(StoreChips.GoldColor), balanceLabel);
            }
            if (!ctx.ReduceMotion && tickT < 1f)
            {
                var floater = $"-{celebration.Spent:N0}";
                var floaterSz = ImGui.CalcTextSize(floater);
                dl.AddText(new Vector2(
                        origin.X + (size.X - floaterSz.X) * 0.5f + Px(58f),
                        origin.Y + size.Y - Px(118f) - Px(26f) * tickT),
                    ImGui.GetColorU32(StoreChips.SaleColor with { W = 1f - tickT }), floater);
            }
        }

        DrawDone(ctx, origin, size, t);
    }

    /// <summary>How many rows fit above the balance before the list runs into it.</summary>
    private const int MaxRows = 5;

    /// <summary>A drawn row: a purchase line, or one of a bundle's contents indented beneath its header.</summary>
    private sealed record Row(
        Guid ProductId, string Label, string? Sub, StoreItemKind Kind, bool HasImage, uint AccentColor,
        Enableable? Enable, bool Child, int ImageVersion);

    /// <summary>Flattens the purchase into rows. A bundle announces itself and then lists what came in it,
    /// each with its own switch, rather than collapsing into one "enable everything" button that leaves the
    /// buyer guessing what they just turned on.</summary>
    private static List<Row> BuildRows(Celebration celebration)
    {
        var rows = new List<Row>();
        foreach (var line in celebration.Lines)
        {
            if (line.Kind == StoreItemKind.Bundle)
            {
                rows.Add(new Row(
                    line.ProductId, line.Name, Loc.T("os.store_bundle_purchased"), line.Kind, line.HasImage,
                    line.AccentColor, null, false, line.ImageVersion));
                foreach (var child in line.Enableables)
                {
                    rows.Add(new Row(
                        child.ProductId, child.Name, null, child.Kind, child.HasImage, line.AccentColor,
                        child, true, child.ImageVersion));
                }
                continue;
            }
            var label = line.Quantity > 1 ? $"{line.Name} x{line.Quantity}" : line.Name;
            rows.Add(new Row(
                line.ProductId, label, null, line.Kind, line.HasImage, line.AccentColor,
                line.Enableables.Count > 0 ? line.Enableables[0] : null, false, line.ImageVersion));
        }
        return rows;
    }

    /// <summary>One tap wears what was just bought. A theme switches the phone; a ring goes on every
    /// identity the account has at once, so there is nothing to choose here.</summary>
    private void DrawEnablePill(ImDrawListPtr dl, Enableable target, uint accentColor, Vector2 tl, Vector2 size)
    {
        var state = _enableStates.GetValueOrDefault(target.ProductId, EnableState.Idle);
        var interactive = state is EnableState.Idle or EnableState.Failed;

        ImGui.SetCursorScreenPos(tl);
        if (ImGui.InvisibleButton($"##enable{target.ProductId:N}", size) && interactive)
        {
            RunEnable(target);
        }
        var hovered = ImGui.IsItemHovered();
        if (hovered && interactive)
        {
            HandOnHover();
        }

        var radius = size.Y * 0.5f;
        if (state == EnableState.Done)
        {
            dl.AddRectFilled(tl, tl + size, ImGui.GetColorU32(new Vector4(0.2f, 0.5f, 0.3f, 0.55f)), radius);
        }
        else
        {
            var (top, bottom, _) = StoreFx.CardColors(accentColor);
            OsDrawShared.RoundedGradient(dl, tl, tl + size, radius,
                top with { W = hovered && interactive ? 1f : 0.9f }, bottom);
        }

        if (state == EnableState.Busy)
        {
            AetherLove.Widgets.LoadingSpinner.Draw(tl + size * 0.5f, Px(7f), Px(2f), 0xFFFFFFFFu);
            return;
        }
        var caption = state switch
        {
            EnableState.Done => Loc.T("os.store_enabled"),
            EnableState.Failed => Loc.T("os.store_enable_failed"),
            _ => Loc.T("os.store_enable"),
        };
        var captionSz = ImGui.CalcTextSize(caption);
        dl.AddText(tl + (size - captionSz) * 0.5f, 0xFFFFFFFFu, caption);
    }

    private void RunEnable(Enableable target)
    {
        _enableStates[target.ProductId] = EnableState.Busy;
        _ = Task.Run(async () =>
        {
            var ok = target.Kind switch
            {
                StoreItemKind.ThemePack => await host.EnableThemeAsync(target.ProductId).ConfigureAwait(false),
                StoreItemKind.AvatarFrame => await host.EnableRingEverywhereAsync(target.ItemRef).ConfigureAwait(false),
                _ => true,
            };
            _enableStates[target.ProductId] = ok ? EnableState.Done : EnableState.Failed;
        });
    }

    private void DrawDone(OsAppContext ctx, Vector2 origin, Vector2 size, float t)
    {
        if (t > 1.4f)
        {
            var btnAlpha = ctx.ReduceMotion ? 1f : Math.Clamp((t - 1.4f) / 0.3f, 0f, 1f);
            using (Dalamud.Interface.Utility.Raii.ImRaii.PushStyle(ImGuiStyleVar.Alpha, btnAlpha))
            {
                var btnW = size.X - Px(72f);
                ImGui.SetCursorScreenPos(new Vector2(origin.X + Px(36f), origin.Y + size.Y - Px(74f)));
                if (StoreUi.Button(Loc.T("os.store_success_done"), btnW))
                {
                    _celebration = null;
                    done();
                }
            }
        }
    }
}
