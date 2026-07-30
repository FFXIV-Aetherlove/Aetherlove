using System;
using System.Numerics;
using AetherLove;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Services.Market;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Market;

/// <summary>The player's own market activity: one card per retainer with listings, undercut markers,
/// expiry warnings and last-scanned age, plus the inferred recent sales. Data appears after a summoning
/// bell visit; summoning a retainer captures its listings and prices.</summary>
internal sealed class MySalesScreen
{
    private const float PadX = 16f;

    private static readonly Vector4 Gold = new(0.98f, 0.80f, 0.36f, 1f);
    private static readonly Vector4 SaleGreen = new(0.55f, 0.85f, 0.55f, 1f);

    private readonly IMarketDesk _desk;
    private readonly Action _back;
    private readonly Action<uint> _openItem;
    private readonly EntranceAnimation _entrance = new();

    public MySalesScreen(IMarketDesk desk, Action back, Action<uint> openItem)
    {
        _desk = desk;
        _back = back;
        _openItem = openItem;
    }

    public void OnShow()
    {
        _entrance.Arm();
        _ = _desk.RefreshUndercutsAsync();
    }

    public void Draw(OsAppContext ctx)
    {
        var winW = ImGui.GetContentRegionAvail().X;

        if (MarketHeader.Draw(Loc.T("os.market_tile_mysales"), out _, out _))
        {
            _back();
            return;
        }
        ImGui.Spacing();

        var characters = _desk.Characters;
        if (characters.Count == 0)
        {
            DrawEmptyState(winW);
            return;
        }

        _entrance.BeginFrame();
        PushScrollbarStyle();
        using (var scroll = ImRaii.Child("##marketMySalesScroll", ImGui.GetContentRegionAvail(), false))
        {
            if (scroll.Success)
            {
                var innerW = ImGui.GetWindowSize().X;
                DrawLegend(innerW);
                // One section per character: the log spans every character that has visited a bell, so a
                // heading is only noise when there is a single one.
                var showHeadings = characters.Count > 1;
                foreach (var character in characters)
                {
                    if (showHeadings)
                    {
                        DrawCharacterHeading(character, innerW);
                    }
                    foreach (var snapshot in character.Retainers)
                    {
                        DrawRetainerCard(snapshot, innerW, ctx.ReduceMotion);
                        ImGui.Dummy(new Vector2(0f, Px(10f)));
                    }
                }
                DrawSales(innerW);
                ImGui.Dummy(new Vector2(0f, Px(14f)));
            }
        }
        PopScrollbarStyle();
        _entrance.EndFrame();
    }

    private static void DrawCharacterHeading(MarketCharacterRetainers character, float winW)
    {
        var t = ThemeService.Current;
        var name = character.Name.Length > 0 ? character.Name : Loc.T("os.market_character_unknown");
        var label = character.World.Length > 0 ? $"{name} · {character.World}" : name;

        ImGui.SetCursorPosX(Px(PadX));
        using (UiFonts.H3?.Push())
        {
            ImGui.TextColored(character.IsCurrent ? t.AccentLight : UiColors.Body,
                TruncateToWidth(label, winW - Px(PadX) * 2f));
        }
        ImGui.Dummy(new Vector2(0f, Px(4f)));
    }

    private static void DrawLegend(float winW)
    {
        var dl = ImGui.GetWindowDrawList();
        dl.ChannelsSplit(2);
        dl.ChannelsSetCurrent(1);

        ImGui.SetCursorPosX(Px(PadX));
        ImGui.BeginGroup();
        (Vector4 Color, string Key)[] rows =
        [
            (Gold, "os.market_legend_price"),
            (UiColors.Danger, "os.market_legend_undercut"),
            (SaleGreen, "os.market_legend_sale"),
        ];
        foreach (var (color, key) in rows)
        {
            var lineTop = ImGui.GetCursorScreenPos();
            dl.AddCircleFilled(new Vector2(lineTop.X + Px(14f), lineTop.Y + ImGui.GetTextLineHeight() * 0.55f),
                Px(4f), ImGui.GetColorU32(color));
            ImGui.SetCursorPosX(Px(PadX) + Px(26f));
            ImGui.PushTextWrapPos(winW - Px(PadX) - Px(10f));
            ImGui.TextColored(UiColors.Hint, Loc.T(key));
            ImGui.PopTextWrapPos();
        }
        ImGui.EndGroup();

        var groupTl = ImGui.GetItemRectMin();
        var groupBr = ImGui.GetItemRectMax();
        dl.ChannelsSetCurrent(0);
        dl.AddRectFilled(groupTl - new Vector2(Px(2f), Px(7f)),
            new Vector2(groupTl.X + winW - Px(PadX) * 2f - Px(2f), groupBr.Y + Px(7f)),
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.035f)), Px(10f));
        dl.ChannelsMerge();

        ImGui.Dummy(new Vector2(0f, Px(14f)));
    }

    private static void DrawEmptyState(float winW)
    {
        ImGui.Dummy(new Vector2(0f, Px(30f)));
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var center = new Vector2(ImGui.GetWindowPos().X + winW * 0.5f, ImGui.GetCursorScreenPos().Y + Px(24f));
        dl.AddCircleFilled(center, Px(26f), ImGui.GetColorU32(t.Accent with { W = 0.16f }));
        IconDraw.AddCentered(dl, FontAwesomeIcon.ConciergeBell, Px(20f), center, ImGui.GetColorU32(t.AccentLight));
        ImGui.Dummy(new Vector2(0f, Px(60f)));
        ImGui.SetCursorPosX(Px(PadX * 1.5f));
        ImGui.PushTextWrapPos(winW - Px(PadX * 1.5f));
        ImGui.TextColored(UiColors.Body, Loc.T("os.market_sales_empty"));
        ImGui.PopTextWrapPos();
    }

    private const float MarketColW = 74f;
    private const float YoursColW = 66f;

    private void DrawRetainerCard(MarketRetainerSnapshot snapshot, float winW, bool reduceMotion)
    {
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var cardW = winW - Px(PadX) * 2f;
        var headerH = Px(54f);
        var rowH = Px(30f);
        var colHeadH = Px(20f);

        ImGui.SetCursorPosX(Px(PadX));
        var headTl = ImGui.GetCursorScreenPos();
        OsDrawShared.RoundedGradient(dl, headTl, headTl + new Vector2(cardW, headerH), Px(12f),
            t.Accent, t.AccentDark);
        using (UiFonts.H3?.Push())
        {
            dl.AddText(headTl + new Vector2(Px(12f), Px(8f)), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.98f)),
                TruncateToWidth(snapshot.Name, cardW * 0.55f));
        }

        var gilText = $"{MarketFormat.Gil(snapshot.Gil)} gil";
        var gilSz = ImGui.CalcTextSize(gilText);
        var pillPad = new Vector2(Px(9f), Px(3f));
        var pillBr = headTl + new Vector2(cardW - Px(10f), Px(8f) + gilSz.Y + pillPad.Y * 2f);
        var pillTl = new Vector2(pillBr.X - gilSz.X - pillPad.X * 2f, headTl.Y + Px(8f));
        dl.AddRectFilled(pillTl, pillBr, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.32f)), (pillBr.Y - pillTl.Y) * 0.5f);
        dl.AddText(pillTl + pillPad, ImGui.GetColorU32(Gold), gilText);

        var meta = Loc.T("os.market_sales_meta", snapshot.Listings.Count, ScannedAgo(snapshot.LastScanned));
        dl.AddText(headTl + new Vector2(Px(12f), Px(32f)), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.75f)), meta);
        if (ExpiryText(snapshot.MarketExpireUnix) is { } exp)
        {
            var expSz = ImGui.CalcTextSize(exp.Text);
            dl.AddText(headTl + new Vector2(cardW - Px(12f) - expSz.X, Px(32f)),
                ImGui.GetColorU32(exp.Urgent ? new Vector4(1f, 0.62f, 0.55f, 1f) : new Vector4(1f, 1f, 1f, 0.75f)), exp.Text);
        }
        MarketFx.DrawShine(dl, headTl, headTl + new Vector2(cardW, headerH), (int)(snapshot.RetainerId % 97), reduceMotion);

        if (snapshot.Listings.Count == 0)
        {
            ImGui.SetCursorScreenPos(new Vector2(headTl.X, headTl.Y + headerH));
            ImGui.NewLine();
            return;
        }

        var tableTl = new Vector2(headTl.X, headTl.Y + headerH + Px(4f));
        var tableH = colHeadH + snapshot.Listings.Count * rowH + Px(4f);
        dl.AddRectFilled(tableTl, tableTl + new Vector2(cardW, tableH), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.03f)), Px(10f));
        dl.AddRect(tableTl, tableTl + new Vector2(cardW, tableH), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.10f)), Px(10f));

        var yoursRight = tableTl.X + cardW - Px(10f);
        var marketRight = yoursRight - Px(YoursColW);
        DrawColumnLabel(dl, Loc.T("os.market_col_item"), new Vector2(tableTl.X + Px(10f), tableTl.Y + Px(3f)), false);
        DrawColumnLabel(dl, Loc.T("os.market_col_market"), new Vector2(marketRight, tableTl.Y + Px(3f)), true);
        DrawColumnLabel(dl, Loc.T("os.market_col_yours"), new Vector2(yoursRight, tableTl.Y + Px(3f)), true);
        dl.AddLine(new Vector2(tableTl.X + Px(6f), tableTl.Y + colHeadH),
            new Vector2(tableTl.X + cardW - Px(6f), tableTl.Y + colHeadH),
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.10f)), Px(1f));

        var y = tableTl.Y + colHeadH + Px(2f);
        for (var i = 0; i < snapshot.Listings.Count; i++)
        {
            if (i > 0)
            {
                dl.AddLine(new Vector2(tableTl.X + Px(6f), y), new Vector2(tableTl.X + cardW - Px(6f), y),
                    ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.05f)), Px(1f));
            }
            DrawListingRow(snapshot.Listings[i], tableTl.X, y, cardW, rowH, marketRight, yoursRight);
            y += rowH;
        }
        ImGui.SetCursorScreenPos(new Vector2(tableTl.X, tableTl.Y + tableH));
        ImGui.NewLine();
    }

    private static void DrawColumnLabel(ImDrawListPtr dl, string text, Vector2 pos, bool rightAligned)
    {
        var sz = ImGui.CalcTextSize(text);
        dl.AddText(rightAligned ? pos with { X = pos.X - sz.X } : pos, ImGui.GetColorU32(UiColors.Hint), text);
    }

    private void DrawListingRow(MarketRetainerListing listing, float cardX, float y, float cardW, float rowH,
        float marketRight, float yoursRight)
    {
        ImGui.SetCursorScreenPos(new Vector2(cardX + Px(4f), y));
        var clicked = ImGui.InvisibleButton($"##marketOwn{listing.ItemId}_{y}", new Vector2(cardW - Px(8f), rowH));
        HandOnHover();
        var hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        var tl = ImGui.GetItemRectMin();
        if (hovered)
        {
            dl.AddRectFilled(tl, tl + new Vector2(cardW - Px(8f), rowH), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.05f)), Px(6f));
        }

        var iconSize = Px(22f);
        var iconTl = new Vector2(cardX + Px(10f), y + (rowH - iconSize) * 0.5f);
        if (ItemIconHandle(listing.ItemId) is { } handle)
        {
            dl.AddImageRounded(handle, iconTl, iconTl + new Vector2(iconSize, iconSize),
                Vector2.Zero, Vector2.One, ImGui.GetColorU32(Vector4.One), Px(4f));
        }

        var undercut = _desk.TryGetUndercut(listing.ItemId, out var marketMin);
        var lineH = ImGui.GetTextLineHeight();
        var textMidY = y + (rowH - lineH) * 0.5f;

        var yoursText = MarketFormat.Gil(listing.UnitPrice);
        var yoursSz = ImGui.CalcTextSize(yoursText);
        dl.AddText(new Vector2(yoursRight - yoursSz.X, textMidY),
            ImGui.GetColorU32(undercut ? UiColors.Danger : Gold), yoursText);

        var marketText = undercut ? MarketFormat.Gil(marketMin) : "-";
        var marketSz = ImGui.CalcTextSize(marketText);
        dl.AddText(new Vector2(marketRight - marketSz.X, textMidY),
            ImGui.GetColorU32(undercut ? UiColors.Danger with { W = 0.85f } : UiColors.Hint), marketText);

        var textX = iconTl.X + iconSize + Px(8f);
        var qty = listing.Quantity > 1 ? $" x{listing.Quantity}" : "";
        var hq = listing.Hq ? " HQ" : "";
        var name = TruncateToWidth($"{listing.ItemName}{qty}{hq}", marketRight - Px(MarketColW) - Px(10f) - textX);
        dl.AddText(new Vector2(textX, textMidY), ImGui.GetColorU32(UiColors.Body), name);

        if (clicked)
        {
            _openItem(listing.ItemId);
        }
    }

    private static Dalamud.Bindings.ImGui.ImTextureID? ItemIconHandle(uint itemId)
    {
        return MarketItemIcons.Get(ItemIcon(itemId));
    }

    private static ushort ItemIcon(uint itemId)
    {
        try
        {
            return UiHost.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>().GetRow(itemId).Icon;
        }
        catch
        {
            return 0;
        }
    }

    private void DrawSales(float winW)
    {
        var sales = _desk.RecentSales;
        if (sales.Count == 0)
        {
            return;
        }
        ImGui.Dummy(new Vector2(0f, Px(4f)));
        ImGui.SetCursorPosX(Px(PadX));
        using (UiFonts.H3?.Push())
        {
            ImGui.TextColored(ThemeService.Current.AccentLight, Loc.T("os.market_sales_recent"));
        }
        ImGui.Spacing();

        var dl = ImGui.GetWindowDrawList();
        foreach (var sale in sales)
        {
            var rowH = Px(30f);
            ImGui.SetCursorPosX(Px(PadX));
            ImGui.Dummy(new Vector2(winW - Px(PadX) * 2f, rowH));
            var tl = ImGui.GetItemRectMin();
            var midY = tl.Y + rowH * 0.5f;
            var lineH = ImGui.GetTextLineHeight();

            var iconSize = Px(22f);
            var iconTl = new Vector2(tl.X, midY - iconSize * 0.5f);
            if (ItemIconHandle(sale.ItemId) is { } handle)
            {
                dl.AddImageRounded(handle, iconTl, iconTl + new Vector2(iconSize, iconSize),
                    Vector2.Zero, Vector2.One, ImGui.GetColorU32(Vector4.One), Px(4f));
            }

            var total = $"+{MarketFormat.Gil(sale.UnitPrice * sale.Quantity)}";
            var totalSz = ImGui.CalcTextSize(total);
            var rightX = tl.X + winW - Px(PadX) * 2f;
            dl.AddText(new Vector2(rightX - totalSz.X, midY - totalSz.Y * 0.5f), ImGui.GetColorU32(SaleGreen), total);

            var ago = Ago(sale.ObservedAt);
            var agoSz = ImGui.CalcTextSize(ago);
            dl.AddText(new Vector2(rightX - totalSz.X - agoSz.X - Px(10f), midY - agoSz.Y * 0.5f),
                ImGui.GetColorU32(UiColors.Hint), ago);

            var qty = sale.Quantity > 1 ? $" x{sale.Quantity}" : "";
            var name = TruncateToWidth($"{sale.ItemName}{qty}", rightX - totalSz.X - agoSz.X - Px(50f) - iconTl.X - iconSize);
            dl.AddText(new Vector2(iconTl.X + iconSize + Px(8f), midY - lineH * 0.5f),
                ImGui.GetColorU32(UiColors.Body), name);
        }
    }

    private static string ScannedAgo(DateTimeOffset? scanned) =>
        scanned is { } at ? Ago(at) : Loc.T("os.market_never_scanned");

    private static string Ago(DateTimeOffset when)
    {
        var span = DateTimeOffset.UtcNow - when;
        if (span.TotalMinutes < 1)
        {
            return "<1m";
        }
        if (span.TotalHours < 1)
        {
            return $"{(int)span.TotalMinutes}m";
        }
        if (span.TotalDays < 1)
        {
            return $"{(int)span.TotalHours}h";
        }
        return $"{(int)span.TotalDays}d";
    }

    private static (string Text, bool Urgent)? ExpiryText(long expireUnix)
    {
        if (expireUnix <= 0)
        {
            return null;
        }
        var remaining = DateTimeOffset.FromUnixTimeSeconds(expireUnix) - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            return (Loc.T("os.market_expired_listings"), true);
        }
        var text = remaining.TotalDays >= 1
            ? Loc.T("os.market_expires_in", $"{(int)remaining.TotalDays}d")
            : Loc.T("os.market_expires_in", $"{(int)Math.Max(remaining.TotalHours, 1)}h");
        return (text, remaining < TimeSpan.FromHours(24));
    }
}
