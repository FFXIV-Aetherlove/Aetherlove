using System;
using System.Collections.Concurrent;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Shared.EchoVidya;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherLove.Screens;

public partial class ChatScreen
{
    private const float EchoCardH = 108f;

    // One fetched card per room id shared into any chat (session cache); a null value marks a room that
    // could not be loaded (ended, or not visible to this user) so the row renders the tombstone.
    private readonly ConcurrentDictionary<Guid, EchoRoomCardDto?> _echoCards = new();
    private readonly ConcurrentDictionary<Guid, byte> _echoCardFetches = new();

    /// <summary>Failed card fetches retry on the next chat open.</summary>
    private void ResetFailedEchoCards()
    {
        foreach (var kv in _echoCards)
        {
            if (kv.Value is null)
            {
                _echoCards.TryRemove(kv.Key, out _);
            }
        }
    }

    private void StartEchoCardFetch(Guid roomId)
    {
        if (_echoCards.ContainsKey(roomId) || !_echoCardFetches.TryAdd(roomId, 0))
        {
            return;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                _echoCards[roomId] = await _hub.GetEchoRoomCardAsync(roomId, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, $"[ChatScreen] Echo room card fetch failed for {roomId}.");
                _echoCards[roomId] = null;
            }
            finally
            {
                _echoCardFetches.TryRemove(roomId, out _);
            }
        });
    }

    /// <summary>A shared Echo room rendered as a card in place of a bubble; clicking deep-links into the Echo
    /// app with the join prefilled.</summary>
    private void DrawEchoCardMessage(DisplayedMessage msg, Guid roomId, string code, float windowWidth, bool isGroupEnd)
    {
        StartEchoCardFetch(roomId);
        _echoCards.TryGetValue(roomId, out var card);

        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var cursorPos = ImGui.GetCursorScreenPos();
        var cardW = windowWidth * 0.72f;
        var cardH = Px(EchoCardH);

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
        var clicked = ImGui.InvisibleButton($"##echoCard{msg.Id:N}", new Vector2(cardW, cardH));
        var hovered = ImGui.IsItemHovered();

        if (card is { } room)
        {
            SharedUiHelpers.HandOnHover();
            dl.AddRectFilled(tl, br, ImGui.GetColorU32(t.Accent with { W = 0.12f }), Px(14f));
            dl.AddRect(tl, br, ImGui.GetColorU32(t.Accent with { W = hovered ? 0.90f : 0.55f }), Px(14f),
                ImDrawFlags.None, Px(1.5f));

            IconDraw.AddCentered(dl, FontAwesomeIcon.Film, Px(20f),
                new Vector2(tl.X + Px(26f), tl.Y + Px(28f)), ImGui.GetColorU32(t.Accent));

            var textX = tl.X + Px(48f);
            var textMaxW = br.X - textX - Px(12f);
            dl.AddText(new Vector2(textX, tl.Y + Px(12f)), 0xFFFFFFFFu, TruncateToWidth(room.Name, textMaxW));
            var host = Loc.T("chat.echo_card_label") + "  ·  " + Loc.T("chat.echo_card_host", room.OwnerName);
            dl.AddText(new Vector2(textX, tl.Y + Px(31f)), ImGui.GetColorU32(t.Accent), TruncateToWidth(host, textMaxW));

            var bodyX = tl.X + Px(14f);
            var bodyMaxW = cardW - Px(28f);
            var playing = string.IsNullOrWhiteSpace(room.NowPlayingTitle)
                ? Loc.T("chat.echo_card_idle")
                : room.NowPlayingTitle;
            dl.AddText(new Vector2(bodyX, tl.Y + Px(56f)), ImGui.GetColorU32(UiColors.Body),
                TruncateToWidth(playing, bodyMaxW));
            dl.AddText(new Vector2(bodyX, tl.Y + Px(80f)), UiColors.TextMuted,
                TruncateToWidth(Loc.T("chat.echo_card_members", room.MemberCount), bodyMaxW));

            if (hovered)
            {
                ImGui.SetTooltip(Loc.T("chat.echo_card_view"));
            }
            if (clicked)
            {
                _shell.Shell?.SendIntent("echo",
                    AetherOS.Sdk.OsIntents.CreateRoomJoin(AetherOS.Sdk.OsIntents.EchoJoin, roomId, code, "aetherlove"));
            }
        }
        else
        {
            dl.AddRectFilled(tl, br, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.05f)), Px(14f));
            dl.AddRect(tl, br, ImGui.GetColorU32(t.Accent with { W = 0.35f }), Px(14f), ImDrawFlags.None, Px(1.5f));
            var text = _echoCards.ContainsKey(roomId)
                ? Loc.T("chat.echo_card_unavailable")
                : Loc.T("chat.echo_card_loading");
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
