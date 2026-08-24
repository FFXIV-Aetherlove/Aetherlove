using System;
using System.Collections.Concurrent;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Shared.Together;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherLove.Screens;

public partial class ChatScreen
{
    private const float PartyCardH = 96f;

    // One fetched card per party shared into any chat (session cache); a null value is the party being
    // over, which is what the card says rather than offering a join that goes nowhere.
    private readonly ConcurrentDictionary<Guid, TogetherPartyCardDto?> _partyCards = new();
    private readonly ConcurrentDictionary<Guid, byte> _partyCardFetches = new();

    /// <summary>Failed card fetches retry on the next chat open.</summary>
    private void ResetFailedPartyCards()
    {
        foreach (var kv in _partyCards)
        {
            if (kv.Value is null)
            {
                _partyCards.TryRemove(kv.Key, out _);
            }
        }
    }

    private void StartPartyCardFetch(Guid partyId)
    {
        if (_partyCards.ContainsKey(partyId) || !_partyCardFetches.TryAdd(partyId, 0))
        {
            return;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                _partyCards[partyId] = await _hub.GetTogetherPartyCardAsync(partyId, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, $"[ChatScreen] Party card fetch failed for {partyId}.");
                _partyCards[partyId] = null;
            }
            finally
            {
                _partyCardFetches.TryRemove(partyId, out _);
            }
        });
    }

    /// <summary>How much room the sender's own invitation needs above the card body.</summary>
    internal static float PartyInviteMessageHeight(string message, float windowWidth)
    {
        if (message.Length == 0)
        {
            return 0f;
        }
        return ImGui.CalcTextSize(message, false, (windowWidth * 0.72f) - Px(28f)).Y + Px(8f);
    }

    /// <summary>A party invite: the sender's own words, then the party with a one-tap join. The join goes
    /// through the shell, because every party surface belongs to it.</summary>
    private void DrawPartyCardMessage(DisplayedMessage msg, Guid partyId, string code, string invite,
        float windowWidth, bool isGroupEnd)
    {
        StartPartyCardFetch(partyId);
        _partyCards.TryGetValue(partyId, out var card);
        var known = _partyCards.ContainsKey(partyId);

        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var cursorPos = ImGui.GetCursorScreenPos();
        var cardW = windowWidth * 0.72f;
        var messageH = PartyInviteMessageHeight(invite, windowWidth);
        var cardH = Px(PartyCardH) + messageH;

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
        var clicked = ImGui.InvisibleButton($"##partyCard{msg.Id:N}", new Vector2(cardW, cardH));
        var hovered = ImGui.IsItemHovered();
        var live = card is not null;

        dl.AddRectFilled(tl, br, ImGui.GetColorU32(live
            ? t.Accent with { W = 0.12f }
            : new Vector4(1f, 1f, 1f, 0.05f)), Px(14f));
        dl.AddRect(tl, br, ImGui.GetColorU32(t.Accent with { W = live ? (hovered ? 0.90f : 0.55f) : 0.30f }),
            Px(14f), ImDrawFlags.None, Px(1.5f));

        var y = tl.Y + Px(12f);
        if (invite.Length > 0)
        {
            ImGui.SetCursorScreenPos(new Vector2(tl.X + Px(14f), y));
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + cardW - Px(28f));
            ImGui.TextUnformatted(invite);
            ImGui.PopTextWrapPos();
            y += messageH;
            dl.AddLine(new Vector2(tl.X + Px(14f), y - Px(4f)), new Vector2(br.X - Px(14f), y - Px(4f)),
                ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.10f)));
        }

        IconDraw.AddCentered(dl, FontAwesomeIcon.UserFriends, Px(20f),
            new Vector2(tl.X + Px(26f), y + Px(20f)),
            ImGui.GetColorU32(live ? t.Accent : UiColors.Muted));

        var textX = tl.X + Px(48f);
        var textMaxW = br.X - textX - Px(12f);
        if (card is { } party)
        {
            SharedUiHelpers.HandOnHover();
            dl.AddText(new Vector2(textX, y + Px(6f)), 0xFFFFFFFFu,
                TruncateToWidth(Loc.T("chat.party_card_title", party.HostName), textMaxW));
            dl.AddText(new Vector2(textX, y + Px(25f)), ImGui.GetColorU32(t.Accent),
                TruncateToWidth(Loc.T("chat.party_card_members", party.MemberCount, party.MaxMembers), textMaxW));
            dl.AddText(new Vector2(tl.X + Px(14f), y + Px(48f)), UiColors.TextMuted,
                TruncateToWidth(Loc.T("chat.party_card_join"), cardW - Px(28f)));
            if (hovered)
            {
                ImGui.SetTooltip(Loc.T("chat.party_card_join"));
            }
            if (clicked)
            {
                _shell.Shell?.JoinParty(code);
            }
        }
        else
        {
            var text = known ? Loc.T("chat.party_card_over") : Loc.T("chat.party_card_loading");
            dl.AddText(new Vector2(textX, y + Px(16f)), ImGui.GetColorU32(UiColors.Muted),
                TruncateToWidth(text, textMaxW));
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
