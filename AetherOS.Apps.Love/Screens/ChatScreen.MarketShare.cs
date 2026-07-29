using System;
using System.Collections.Concurrent;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Services.Market;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherLove.Screens;

public partial class ChatScreen
{
    private const float MarketCardH = 86f;

    // Home-world min price per shared item id (session cache); 0 marks a fetch that found no listings.
    private readonly ConcurrentDictionary<uint, long> _marketPrices = new();
    private readonly ConcurrentDictionary<uint, byte> _marketPriceFetches = new();

    private void StartMarketPriceFetch(uint itemId)
    {
        if (_marketPrices.ContainsKey(itemId) || !_marketPriceFetches.TryAdd(itemId, 0))
        {
            return;
        }
        var detected = MarketScopes.DetectCurrent();
        _ = Task.Run(async () =>
        {
            try
            {
                var scopes = detected;
                if (scopes is null)
                {
                    return;
                }
                var agg = await _marketData.GetAggregatedAsync(scopes.Value.DataCenter, [itemId], CancellationToken.None)
                    .ConfigureAwait(false);
                long price = 0;
                if (agg.TryGetValue(itemId, out var result))
                {
                    price = result.Nq.MinListing?.At(MarketScopeKind.DataCenter)?.Price
                        ?? result.Hq.MinListing?.At(MarketScopeKind.DataCenter)?.Price
                        ?? 0;
                }
                _marketPrices[itemId] = price;
            }
            catch (Exception ex)
            {
                UiHost.Log.Debug($"[ChatScreen] Market card price fetch failed: {ex.Message}");
            }
            finally
            {
                _marketPriceFetches.TryRemove(itemId, out _);
            }
        });
    }

    /// <summary>A shared market item rendered as a card; clicking deep-links into the Market app's item page.</summary>
    private void DrawMarketCardMessage(DisplayedMessage msg, uint itemId, float windowWidth, bool isGroupEnd)
    {
        StartMarketPriceFetch(itemId);
        _marketIndex.EnsureBuildStarted();
        _marketIndex.TryGet(itemId, out var entry);

        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var cursorPos = ImGui.GetCursorScreenPos();
        var cardW = windowWidth * 0.72f;
        var cardH = Px(MarketCardH);

        var (entryDy, entryAlpha) = MessageEntrance(msg.Id);
        var fading = entryAlpha < 0.999f;
        if (fading)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, entryAlpha * ImGui.GetStyle().Alpha);
        }

        var left = msg.IsOwn ? cursorPos.X + windowWidth - cardW - Px(10) : cursorPos.X + Px(10);
        var tl = new Vector2(left, cursorPos.Y + entryDy);
        var br = tl + new Vector2(cardW, cardH);

        ImGui.SetCursorScreenPos(tl);
        var clicked = ImGui.InvisibleButton($"##marketCard{msg.Id:N}", new Vector2(cardW, cardH));
        var hovered = ImGui.IsItemHovered();

        dl.AddRectFilled(tl, br, ImGui.GetColorU32(t.Accent with { W = 0.13f }), Px(14f));
        dl.AddRect(tl, br, ImGui.GetColorU32(t.Accent with { W = hovered ? 0.90f : 0.55f }), Px(14f),
            ImDrawFlags.None, Px(1.5f));

        var padX = Px(12f);
        var eyebrow = Loc.T("chat.market_card_label");
        var iconPx = ImGui.GetFontSize() * 0.82f;
        IconDraw.Add(dl, FontAwesomeIcon.Coins, iconPx, new Vector2(tl.X + padX, tl.Y + Px(10f)),
            ImGui.GetColorU32(t.AccentLight));
        dl.AddText(ImGui.GetFont(), iconPx,
            new Vector2(tl.X + padX + IconDraw.Measure(FontAwesomeIcon.Coins, iconPx).X + Px(7f), tl.Y + Px(10f)),
            ImGui.GetColorU32(t.AccentLight), eyebrow);

        var itemIconSize = Px(36f);
        var itemIconTl = new Vector2(tl.X + padX, tl.Y + Px(32f));
        if (MarketItemIcons.Get(entry.Icon) is { } handle)
        {
            dl.AddImageRounded(handle, itemIconTl, itemIconTl + new Vector2(itemIconSize, itemIconSize),
                Vector2.Zero, Vector2.One, 0xFFFFFFFFu, Px(6f));
        }
        else
        {
            dl.AddRectFilled(itemIconTl, itemIconTl + new Vector2(itemIconSize, itemIconSize),
                ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.08f)), Px(6f));
        }

        var textX = itemIconTl.X + itemIconSize + Px(10f);
        var textMaxW = br.X - padX - textX;
        var name = entry.Name.Length > 0 ? entry.Name : $"#{itemId}";
        dl.AddText(new Vector2(textX, tl.Y + Px(34f)), 0xFFFFFFFFu, TruncateToWidth(name, textMaxW));

        var priceText = _marketPrices.TryGetValue(itemId, out var price) && price > 0
            ? $"{Services.Market.MarketFormat.GilFull(price)} gil"
            : Loc.T("places.share_loading");
        dl.AddText(new Vector2(textX, tl.Y + Px(34f) + ImGui.GetTextLineHeight() + Px(3f)),
            ImGui.GetColorU32(new Vector4(0.98f, 0.80f, 0.36f, 1f)), TruncateToWidth(priceText, textMaxW));

        if (hovered)
        {
            ImGui.SetTooltip(Loc.T("chat.market_card_view"));
        }
        if (clicked)
        {
            _shell.Shell?.SendIntent("market", AetherOS.Sdk.OsIntents.CreateMarketItem(itemId, "aetherlove"));
        }

        if (isGroupEnd)
        {
            var local = msg.SentAt.LocalDateTime;
            var seenSuffix = msg.IsOwn && msg.ReadByOtherAtUtc is not null ? Loc.T("chat.seen_suffix") : string.Empty;
            var timeStr = local.ToString("HH:mm") + seenSuffix;
            var timeSize = ImGui.CalcTextSize(timeStr);
            var timeX = msg.IsOwn ? tl.X + cardW - timeSize.X : tl.X;
            ImGui.SetCursorScreenPos(new Vector2(timeX, br.Y + Px(2f)));
            ImGui.TextColored(new Vector4(0.75f, 0.75f, 0.75f, 0.40f), timeStr);
            ImGui.SetCursorScreenPos(cursorPos + new Vector2(0, cardH + timeSize.Y + Px(8f)));
        }
        else
        {
            ImGui.SetCursorScreenPos(cursorPos + new Vector2(0, cardH + Px(2f)));
        }

        if (fading)
        {
            ImGui.PopStyleVar();
        }
    }
}
