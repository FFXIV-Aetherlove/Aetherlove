using System;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Store;

/// <summary>The store's small draw-list pieces: prices with strikethrough, the sale sticker, the NEW
/// ribbon, and the live countdown chip. All pure draws; the caller owns layout.</summary>
internal static class StoreChips
{
    internal static readonly Vector4 SaleColor = new(0.96f, 0.33f, 0.55f, 1f);
    internal static readonly Vector4 GoldColor = new(0.95f, 0.78f, 0.34f, 1f);

    /// <summary>Bolt + effective price; when discounted the original price sits struck through behind
    /// it. Returns the drawn width. <paramref name="plate"/> puts the price on its own dark capsule, which
    /// is what keeps it legible when it lands on artwork rather than on a flat card.</summary>
    public static float Price(
        ImDrawListPtr dl, Vector2 tl, int paid, int original, float scale = 1f, bool plate = false)
    {
        var font = ImGui.GetFont();
        var fontSize = ImGui.GetFontSize() * scale;
        var iconPx = Px(11f) * scale;
        var x = tl.X;

        if (plate)
        {
            var plateW = Measure(paid, original, scale);
            var padX = Px(7f) * scale;
            var padY = Px(3f) * scale;
            var plateTl = tl - new Vector2(padX, padY);
            var plateBr = tl + new Vector2(plateW + padX, fontSize + padY);
            dl.AddRectFilled(plateTl, plateBr, OsDrawShared.Black(0.55f), (plateBr.Y - plateTl.Y) * 0.5f);
        }

        IconDraw.AddCentered(dl, FontAwesomeIcon.Bolt, iconPx,
            new Vector2(x + iconPx * 0.5f, tl.Y + fontSize * 0.52f), ImGui.GetColorU32(GoldColor));
        x += iconPx + Px(4f) * scale;

        var paidText = paid.ToString("N0");
        dl.AddText(font, fontSize, new Vector2(x, tl.Y), ImGui.GetColorU32(GoldColor), paidText);
        x += StoreChips.MeasureAt(paidText, fontSize).X;

        if (original > paid)
        {
            x += Px(6f) * scale;
            var wasText = original.ToString("N0");
            var wasSize = fontSize * 0.82f;
            var wasY = tl.Y + (fontSize - wasSize) * 0.7f;
            var wasW = StoreChips.MeasureAt(wasText, wasSize).X;
            dl.AddText(font, wasSize, new Vector2(x, wasY), ImGui.GetColorU32(UiColors.Hint), wasText);
            dl.AddLine(new Vector2(x - Px(1f), wasY + wasSize * 0.55f),
                new Vector2(x + wasW + Px(1f), wasY + wasSize * 0.45f),
                ImGui.GetColorU32(UiColors.Hint), Px(1f));
            x += wasW;
        }
        return x - tl.X;
    }

    /// <summary>The width <see cref="Price"/> will occupy, so a plate can be sized before it draws.</summary>
    public static float Measure(int paid, int original, float scale = 1f)
    {
        var fontSize = ImGui.GetFontSize() * scale;
        var width = Px(11f) * scale + Px(4f) * scale + MeasureAt(paid.ToString("N0"), fontSize).X;
        if (original > paid)
        {
            width += Px(6f) * scale + MeasureAt(original.ToString("N0"), fontSize * 0.82f).X;
        }
        return width;
    }

    /// <summary>The tilted "-N%" sticker.</summary>
    public static void SaleBadge(ImDrawListPtr dl, Vector2 center, int percent)
    {
        var text = $"-{percent}%";
        var fontSize = ImGui.GetFontSize() * 0.8f;
        var textSz = StoreChips.MeasureAt(text, fontSize);
        var half = new Vector2(textSz.X * 0.5f + Px(6f), textSz.Y * 0.5f + Px(3f));

        // A hand-rotated quad (-6 degrees) reads as a stuck-on sticker.
        const float angle = -0.105f;
        var (sin, cos) = MathF.SinCos(angle);
        Vector2 Rotate(Vector2 p) => center + new Vector2(p.X * cos - p.Y * sin, p.X * sin + p.Y * cos);
        dl.AddQuadFilled(
            Rotate(new Vector2(-half.X, -half.Y)), Rotate(new Vector2(half.X, -half.Y)),
            Rotate(new Vector2(half.X, half.Y)), Rotate(new Vector2(-half.X, half.Y)),
            ImGui.GetColorU32(SaleColor));
        dl.AddText(ImGui.GetFont(), fontSize, center - textSz * 0.5f - new Vector2(0f, Px(1f)), 0xFFFFFFFFu, text);
    }

    /// <summary>The corner "NEW" ribbon.</summary>
    public static void NewRibbon(ImDrawListPtr dl, Vector2 tl)
    {
        var text = Loc.T("os.store_badge_new");
        var fontSize = ImGui.GetFontSize() * 0.72f;
        var textSz = StoreChips.MeasureAt(text, fontSize);
        var size = new Vector2(textSz.X + Px(10f), textSz.Y + Px(4f));
        dl.AddRectFilled(tl, tl + size, StorePalette.BlueU32, Px(6f),
            ImDrawFlags.RoundCornersBottomRight | ImDrawFlags.RoundCornersTopLeft);
        dl.AddText(ImGui.GetFont(), fontSize, tl + new Vector2(Px(5f), Px(2f)), 0xFFFFFFFFu, text);
    }

    /// <summary>Hourglass + a live D HH:MM:SS pill; hides itself at zero, turns hot under an hour.
    /// Fed exclusively with real server end times; there is no fake urgency to render.</summary>
    public static void Countdown(ImDrawListPtr dl, Vector2 tl, DateTimeOffset endsAtUtc)
    {
        var text = StoreFx.FormatCountdown(endsAtUtc - DateTimeOffset.UtcNow);
        if (text.Length == 0)
        {
            return;
        }
        var urgent = endsAtUtc - DateTimeOffset.UtcNow < TimeSpan.FromHours(1);
        var fontSize = ImGui.GetFontSize() * 0.78f;
        var iconPx = Px(9f);
        var textSz = StoreChips.MeasureAt(text, fontSize);
        var size = new Vector2(iconPx + Px(5f) + textSz.X + Px(14f), textSz.Y + Px(6f));
        dl.AddRectFilled(tl, tl + size, OsDrawShared.Black(0.5f), size.Y * 0.5f);
        var tint = ImGui.GetColorU32(urgent ? SaleColor : UiColors.Body);
        IconDraw.AddCentered(dl, FontAwesomeIcon.Hourglass, iconPx,
            tl + new Vector2(Px(7f) + iconPx * 0.5f, size.Y * 0.5f), tint);
        dl.AddText(ImGui.GetFont(), fontSize, tl + new Vector2(Px(7f) + iconPx + Px(5f), Px(3f)), tint, text);
    }

    /// <summary>Measures text as it will render at an explicit pixel font size.</summary>
    internal static Vector2 MeasureAt(string text, float fontSize) =>
        ImGui.CalcTextSize(text) * (fontSize / ImGui.GetFontSize());

    /// <summary>Shared reveal-stagger scalar for section entrances.</summary>
    public static float Reveal(double stamp, int index, OsAppContext ctx)
    {
        if (ctx.ReduceMotion || stamp <= 0.0)
        {
            return 1f;
        }
        var elapsed = (float)(ImGui.GetTime() - stamp) - index * 0.06f;
        return StoreFx.EaseOut(Math.Clamp(elapsed / 0.35f, 0f, 1f));
    }
}
