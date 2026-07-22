using System;
using System.Collections.Concurrent;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Shared.Hangouts;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;

namespace AetherLove.Screens;

public partial class ChatScreen
{
    private const float HangoutCardH = 110f;

    // One fetched card per hangout id shared into any chat (session cache); a null value marks a hangout
    // that could not be loaded (ended, or not visible to this user) so the row renders the tombstone.
    private readonly ConcurrentDictionary<Guid, HangoutCardDto?> _hangoutCards = new();
    private readonly ConcurrentDictionary<Guid, byte> _hangoutCardFetches = new();

    /// <summary>Failed card fetches retry on the next chat open.</summary>
    private void ResetFailedHangoutCards()
    {
        foreach (var kv in _hangoutCards)
        {
            if (kv.Value is null)
            {
                _hangoutCards.TryRemove(kv.Key, out _);
            }
        }
    }

    private void StartHangoutCardFetch(Guid hangoutId)
    {
        if (_hangoutCards.ContainsKey(hangoutId) || !_hangoutCardFetches.TryAdd(hangoutId, 0))
        {
            return;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                _hangoutCards[hangoutId] = await _hub.GetHangoutCardAsync(hangoutId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, $"[ChatScreen] Hangout card fetch failed for {hangoutId}.");
                _hangoutCards[hangoutId] = null;
            }
            finally
            {
                _hangoutCardFetches.TryRemove(hangoutId, out _);
            }
        });
    }

    /// <summary>A shared hangout rendered as a card in place of a bubble; clicking opens the hangout overlay in the chat.</summary>
    private void DrawHangoutCardMessage(DisplayedMessage msg, Guid hangoutId, float windowWidth, bool isGroupEnd)
    {
        StartHangoutCardFetch(hangoutId);
        _hangoutCards.TryGetValue(hangoutId, out var card);

        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var cursorPos = ImGui.GetCursorScreenPos();
        var cardW = windowWidth * 0.72f;
        var cardH = Px(HangoutCardH);

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
        var clicked = ImGui.InvisibleButton($"##hgCardMsg{msg.Id:N}", new Vector2(cardW, cardH));
        var hovered = ImGui.IsItemHovered();

        if (card is { Summary: { } h } && h.EndUtc > DateTimeOffset.UtcNow)
        {
            var live = HangoutFields.IsLiveNow(h);
            var accent = live ? UiColors.LiveGreen : t.Accent;

            dl.AddRectFilled(tl, br, ImGui.GetColorU32(accent with { W = 0.12f }), Px(14f));
            dl.AddRect(tl, br, ImGui.GetColorU32(accent with { W = hovered ? 0.90f : 0.55f }), Px(14f),
                ImDrawFlags.None, Px(1.5f));

            IconDraw.AddCentered(dl, HangoutFields.CategoryIcon(h.Category), Px(20f),
                new Vector2(tl.X + Px(26f), tl.Y + Px(28f)), ImGui.GetColorU32(accent));

            var textX = tl.X + Px(48f);
            var textMaxW = br.X - textX - Px(12f);
            dl.AddText(new Vector2(textX, tl.Y + Px(12f)), 0xFFFFFFFFu, TruncateToWidth(h.OwnerDisplayName, textMaxW));
            var status = (live ? Loc.T("hangout.chip_live") : HangoutFields.TimeLabel(h))
                + "  ·  " + HangoutFields.CategoryLabel(h.Category);
            dl.AddText(new Vector2(textX, tl.Y + Px(31f)), ImGui.GetColorU32(accent), TruncateToWidth(status, textMaxW));

            var bodyX = tl.X + Px(14f);
            var bodyMaxW = cardW - Px(28f);
            dl.AddText(new Vector2(bodyX, tl.Y + Px(56f)), ImGui.GetColorU32(UiColors.Body),
                TruncateToWidth(h.Description, bodyMaxW));
            var footer = HangoutFields.FormatAddress(h) + "  ·  " + Loc.T("hangout.coming_count", HangoutFields.CountLabel(h));
            dl.AddText(new Vector2(bodyX, tl.Y + Px(80f)), UiColors.TextMuted, TruncateToWidth(footer, bodyMaxW));

            if (hovered)
            {
                ImGui.SetTooltip(Loc.T("hangout.share_view"));
            }
            if (clicked)
            {
                _hangoutOpener.Open(h, fromChat: true);
            }
        }
        else
        {
            dl.AddRectFilled(tl, br, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.05f)), Px(14f));
            dl.AddRect(tl, br, ImGui.GetColorU32(t.Accent with { W = 0.35f }), Px(14f), ImDrawFlags.None, Px(1.5f));
            var text = card is null && !_hangoutCards.ContainsKey(hangoutId)
                ? Loc.T("places.share_loading")
                : Loc.T("hangout.share_unavailable");
            var textSz = ImGui.CalcTextSize(text);
            dl.AddText(tl + (new Vector2(cardW, cardH) - textSz) * 0.5f, ImGui.GetColorU32(UiColors.Muted), text);
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
