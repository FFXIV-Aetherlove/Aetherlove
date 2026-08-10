using System;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Wallet;

/// <summary>The star affordance on a currency row. Omit it to render the row read-only, which is what the
/// Sparks tab's favorites list does.</summary>
internal readonly record struct CurrencyStar(bool Starred, Action OnToggle);

/// <summary>One currency row, shared by the Currencies tab and the Sparks tab's favorites card so the two
/// can never drift apart.</summary>
internal static class CurrencyRowDraw
{
    public const float CardRounding = 14f;

    private const float RowHeightSimple = 46f;
    private const float RowHeightWithBar = 58f;
    private const float IconSize = 30f;
    private const float StarSize = 15f;
    private const float StarSlotWidth = 30f;

    public static float RowHeight(WalletCurrencyRow row) =>
        row.HasCap || row.HasWeekly ? Px(RowHeightWithBar) : Px(RowHeightSimple);

    public static void Draw(OsAppContext ctx, ImDrawListPtr dl, WalletCurrencyRow row, Vector2 tl, float cardW,
        float rowH, float rounding, bool first, bool last, bool flying, Func<uint, ImTextureID?> lookupIcon,
        CurrencyStar? star)
    {
        var t = ThemeService.Current;
        var br = tl + new Vector2(cardW, rowH);
        if (flying)
        {
            // Off its card for the moment, so it carries its own backing and reads as lifted rather than
            // as loose text sliding over the sections it passes.
            dl.AddRectFilled(tl, br, OsDrawShared.Black(0.45f), rounding);
            dl.AddRectFilled(tl, br, OsDrawShared.White(0.10f), rounding);
            dl.AddRect(tl, br, ImGui.GetColorU32(t.Accent with { W = 0.45f }), rounding, ImDrawFlags.None, Px(1.2f));
        }
        else if (star is not null && ImGui.IsMouseHoveringRect(tl, br))
        {
            var flags = first && last ? ImDrawFlags.RoundCornersAll
                : first ? ImDrawFlags.RoundCornersTop
                : last ? ImDrawFlags.RoundCornersBottom
                : ImDrawFlags.RoundCornersNone;
            dl.AddRectFilled(tl, br, OsDrawShared.White(0.05f), rounding, flags);
        }

        var iconSz = Px(IconSize);
        var iconTl = new Vector2(tl.X + Px(12f), tl.Y + Px(9f));
        dl.AddRectFilled(iconTl, iconTl + new Vector2(iconSz, iconSz), OsDrawShared.White(0.05f), Px(8f));
        if (lookupIcon(row.IconId) is { } icon)
        {
            dl.AddImageRounded(icon, iconTl, iconTl + new Vector2(iconSz, iconSz), Vector2.Zero, Vector2.One,
                0xFFFFFFFFu, Px(8f));
        }

        var atCap = row.AtCap;
        var accent = atCap ? UiColors.FavoriteStar : ImGui.GetColorU32(t.AccentLight);
        var lineH = ImGui.GetTextLineHeight();
        var textX = iconTl.X + iconSz + Px(11f);
        var starSlot = star is null ? Px(4f) : Px(StarSlotWidth);
        var rightEdge = tl.X + cardW - Px(14f) - starSlot;
        var topLineY = tl.Y + Px(9f) + (iconSz - lineH) * 0.5f;

        if (star is { } spec)
        {
            DrawStar(dl, row.ItemId, spec, tl, cardW, rowH, flying);
        }

        // The amount block is measured first so the name gets whatever is left; item names come from the
        // game client's language, so a long one would otherwise run straight through the number.
        var amount = row.Amount.ToString("N0", ctx.Culture);
        var capText = row.HasCap ? " / " + row.Cap.ToString("N0", ctx.Culture) : string.Empty;
        var capSz = capText.Length > 0 ? ImGui.CalcTextSize(capText) : Vector2.Zero;
        Vector2 amountSz;
        using (UiFonts.H3?.Push())
        {
            amountSz = ImGui.CalcTextSize(amount);
        }
        var amountX = rightEdge - capSz.X - amountSz.X;
        // A bar-less row has only this line, so the taller H3 amount centres on the icon rather than
        // sharing the body text's top edge.
        var amountY = row.HasCap || row.HasWeekly
            ? tl.Y + Px(10f)
            : tl.Y + Px(9f) + (iconSz - amountSz.Y) * 0.5f;
        using (UiFonts.H3?.Push())
        {
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), new Vector2(amountX, amountY), accent, amount);
        }
        if (capText.Length > 0)
        {
            dl.AddText(new Vector2(rightEdge - capSz.X, amountY + (amountSz.Y - capSz.Y) * 0.5f),
                ImGui.GetColorU32(UiColors.Hint), capText);
        }

        var nameW = MathF.Max(Px(20f), amountX - Px(10f) - textX);
        var name = TruncateToWidth(row.Name, nameW);
        var nameY = row.HasCap || row.HasWeekly ? amountY + (amountSz.Y - lineH) * 0.5f : topLineY;
        dl.AddText(new Vector2(textX, nameY), ImGui.GetColorU32(UiColors.Body), name);

        if (!row.HasCap && !row.HasWeekly)
        {
            return;
        }

        // A weekly allowance is the number that actually decides the week, so it owns the bar when present
        // and the holding cap stays as the "/ 2,000" beside the amount.
        var barH = Px(5f);
        var barY = tl.Y + rowH - Px(13f);
        var barLeft = textX;
        var barRight = rightEdge;
        string? trailing = null;
        float fraction;
        bool barFull;
        if (row.HasWeekly)
        {
            var count = row.WeeklyCount!.Value;
            var weekCap = row.WeeklyCap!.Value;
            trailing = Loc.T("os.wallet_cur_weekly", count.ToString("N0", ctx.Culture),
                weekCap.ToString("N0", ctx.Culture));
            fraction = Math.Clamp(count / (float)weekCap, 0f, 1f);
            barFull = count >= weekCap;
        }
        else
        {
            fraction = Math.Clamp(row.Amount / (float)row.Cap, 0f, 1f);
            barFull = row.AtCap;
        }

        if (trailing is not null)
        {
            var trailingSz = ImGui.CalcTextSize(trailing);
            dl.AddText(new Vector2(rightEdge - trailingSz.X, barY - trailingSz.Y * 0.5f + barH * 0.5f),
                ImGui.GetColorU32(UiColors.Hint), trailing);
            barRight = rightEdge - trailingSz.X - Px(10f);
        }

        var barW = MathF.Max(Px(20f), barRight - barLeft);
        var barTl = new Vector2(barLeft, barY);
        dl.AddRectFilled(barTl, barTl + new Vector2(barW, barH), OsDrawShared.White(0.10f), barH * 0.5f);
        if (fraction > 0f)
        {
            // Coloured by what the bar plots, which on the weekly tomestone is the allowance, not the stack.
            var barColor = barFull ? UiColors.FavoriteStar : ImGui.GetColorU32(t.AccentLight);
            dl.AddRectFilled(barTl, barTl + new Vector2(barW * fraction, barH), barColor, barH * 0.5f);
        }
    }

    /// <summary>The star that lifts a currency into Favorites. Submitted before the rest of the row so it
    /// wins the click, and skipped while the row is in flight so a double toggle cannot chase itself.</summary>
    private static void DrawStar(ImDrawListPtr dl, uint itemId, CurrencyStar spec, Vector2 tl, float cardW,
        float rowH, bool flying)
    {
        var starred = spec.Starred;
        var slotW = Px(StarSlotWidth);
        var starTl = new Vector2(tl.X + cardW - Px(10f) - slotW, tl.Y + (rowH - slotW) * 0.5f);
        var center = starTl + new Vector2(slotW * 0.5f, slotW * 0.5f);

        var hovered = false;
        if (!flying)
        {
            var cursor = ImGui.GetCursorScreenPos();
            ImGui.SetCursorScreenPos(starTl);
            var clicked = ImGui.InvisibleButton($"##walletFav{itemId}", new Vector2(slotW, slotW));
            HandOnHover();
            hovered = ImGui.IsItemHovered();
            if (hovered)
            {
                ImGui.SetTooltip(Loc.T(starred ? "os.wallet_cur_unfavorite" : "os.wallet_cur_favorite"));
            }
            ImGui.SetCursorScreenPos(cursor);
            if (clicked)
            {
                spec.OnToggle();
                starred = !starred;
            }
        }

        var color = starred ? UiColors.FavoriteStar : OsDrawShared.White(hovered ? 0.55f : 0.22f);
        IconDraw.AddCentered(dl, FontAwesomeIcon.Star, Px(StarSize), center, color);
    }
}
