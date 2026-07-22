using System;
using System.Collections.Concurrent;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Shared.News;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherLove.Screens;

public partial class ChatScreen
{
    private const float NewsCardH = 122f;

    private sealed record NewsCardVisual(NewsCardDto? Card);

    // One fetched card per news id (session cache); null Card marks an unavailable entry.
    private readonly ConcurrentDictionary<Guid, NewsCardVisual> _newsCards = new();
    private readonly ConcurrentDictionary<Guid, byte> _newsCardFetches = new();

    /// <summary>Failed card fetches retry on the next chat open.</summary>
    private void ResetFailedNewsCards()
    {
        foreach (var kv in _newsCards)
        {
            if (kv.Value.Card is null)
            {
                _newsCards.TryRemove(kv.Key, out _);
            }
        }
    }

    private void StartNewsCardFetch(Guid newsId)
    {
        if (_newsCards.ContainsKey(newsId) || !_newsCardFetches.TryAdd(newsId, 0))
        {
            return;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                var dto = await _hub.GetNewsCardAsync(newsId, CancellationToken.None).ConfigureAwait(false);
                _newsCards[newsId] = new NewsCardVisual(dto);
            }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, $"[ChatScreen] News card fetch failed for {newsId}.");
                _newsCards[newsId] = new NewsCardVisual(null);
            }
            finally
            {
                _newsCardFetches.TryRemove(newsId, out _);
            }
        });
    }

    /// <summary>A shared news entry rendered as a card in place of a bubble; clicking deep-links into the News
    /// app's entry view.</summary>
    private void DrawNewsCardMessage(DisplayedMessage msg, Guid newsId, float windowWidth, bool isGroupEnd)
    {
        StartNewsCardFetch(newsId);
        _newsCards.TryGetValue(newsId, out var visual);

        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var cursorPos = ImGui.GetCursorScreenPos();
        var cardW = windowWidth * 0.72f;
        var cardH = Px(NewsCardH);

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
        var clicked = ImGui.InvisibleButton($"##newsCard{msg.Id:N}", new Vector2(cardW, cardH));
        var hovered = ImGui.IsItemHovered();

        dl.AddRectFilled(tl, br, ImGui.GetColorU32(t.Accent with { W = 0.13f }), Px(14f));
        dl.AddRect(tl, br, ImGui.GetColorU32(t.Accent with { W = hovered ? 0.90f : 0.55f }), Px(14f),
            ImDrawFlags.None, Px(1.5f));

        var padX = Px(14f);
        var textX = tl.X + padX;
        var textMaxW = cardW - padX * 2f;

        // Eyebrow row: a newspaper glyph + the "News" label in the accent tint.
        var eyebrow = Loc.T("chat.news_card_label");
        var iconPx = ImGui.GetFontSize() * 0.82f;
        IconDraw.Add(dl, FontAwesomeIcon.Newspaper, iconPx, new Vector2(textX, tl.Y + Px(11f)),
            ImGui.GetColorU32(t.AccentLight));
        dl.AddText(ImGui.GetFont(), iconPx, new Vector2(textX + IconDraw.Measure(FontAwesomeIcon.Newspaper, iconPx).X + Px(7f),
            tl.Y + Px(11f)), ImGui.GetColorU32(t.AccentLight), eyebrow);

        if (visual?.Card is { } card)
        {
            float titleH;
            using (UiFonts.H3?.Push())
            {
                titleH = ImGui.GetFontSize();
                dl.AddText(ImGui.GetFont(), titleH, new Vector2(textX, tl.Y + Px(36f)),
                    0xFFFFFFFFu, TruncateToWidth(card.Title, textMaxW));
            }
            var previewY = tl.Y + Px(36f) + titleH + Px(6f);
            dl.PushClipRect(new Vector2(textX, previewY), new Vector2(br.X - padX, br.Y - Px(8f)), true);
            dl.AddText(new Vector2(textX, previewY), ImGui.GetColorU32(UiColors.Body),
                TruncateToWidth(card.Preview, textMaxW));
            dl.PopClipRect();

            if (hovered)
            {
                ImGui.SetTooltip(Loc.T("chat.news_card_view"));
            }
            if (clicked)
            {
                _shell.Shell?.SendIntent("news",
                    AetherOS.Sdk.OsIntents.CreateReturn(AetherOS.Sdk.OsIntents.OpenEntry, newsId, "aetherlove"));
            }
        }
        else
        {
            var text = visual is null ? Loc.T("places.share_loading") : Loc.T("chat.news_card_unavailable");
            var textSz = ImGui.CalcTextSize(text);
            dl.AddText(new Vector2(tl.X + (cardW - textSz.X) * 0.5f, tl.Y + Px(58f)),
                ImGui.GetColorU32(UiColors.Muted), text);
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
