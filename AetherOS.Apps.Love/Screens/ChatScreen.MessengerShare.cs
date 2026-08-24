using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherLove.Screens;

public partial class ChatScreen
{
    private const float MessengerCardH = 76f;

    /// <summary>A messenger invite rendered as a card; clicking it sends the pair request there and then, so
    /// accepting an invite is one tap and never a trip through another app with a code to retype.</summary>
    private void DrawMessengerInviteCard(DisplayedMessage msg, string code, float windowWidth, bool isGroupEnd)
    {
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var cursorPos = ImGui.GetCursorScreenPos();
        var cardW = windowWidth * 0.72f;
        var cardH = Px(MessengerCardH);

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
        var clicked = ImGui.InvisibleButton($"##msgrInvite{msg.Id:N}", new Vector2(cardW, cardH));
        var hovered = ImGui.IsItemHovered();

        dl.AddRectFilled(tl, br, ImGui.GetColorU32(t.Accent with { W = 0.14f }), Px(14f));
        dl.AddRect(tl, br, ImGui.GetColorU32(t.Accent with { W = hovered ? 0.90f : 0.55f }), Px(14f),
            ImDrawFlags.None, Px(1.5f));

        var iconR = Px(18f);
        var iconC = new Vector2(tl.X + Px(14f) + iconR, (tl.Y + br.Y) * 0.5f);
        dl.AddCircleFilled(iconC, iconR, ImGui.GetColorU32(t.Accent));
        var glyph = FontAwesomeIcon.CommentDots.ToIconString();
        ImGui.PushFont(UiBuilder.IconFont);
        var glyphSz = ImGui.CalcTextSize(glyph) * (Px(16f) / ImGui.GetFontSize());
        dl.AddText(ImGui.GetFont(), Px(16f), iconC - glyphSz * 0.5f, 0xFFFFFFFFu, glyph);
        ImGui.PopFont();

        var textX = iconC.X + iconR + Px(12f);
        var lineH = ImGui.GetTextLineHeight();
        dl.AddText(new Vector2(textX, tl.Y + Px(12f)), 0xFFFFFFFFu, Loc.T("chat.msgr_card_title"));
        dl.AddText(new Vector2(textX, tl.Y + Px(12f) + lineH + Px(2f)), ImGui.GetColorU32(t.Accent),
            MessengerShare.Display(code));
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.82f,
            new Vector2(textX, tl.Y + Px(12f) + (lineH + Px(2f)) * 2f), ImGui.GetColorU32(UiColors.Muted),
            Loc.T("chat.msgr_card_hint"));

        if (clicked && !msg.IsOwn)
        {
            SendMessengerPairRequest(code);
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

    /// <summary>Fires the contact request the card is for. An already-pending or already-paired code comes
    /// back as an error the toast swallows: from the inviter's side the invitation stands either way.</summary>
    private void SendMessengerPairRequest(string code)
    {
        if (_msgrInviteBusy)
        {
            return;
        }
        _msgrInviteBusy = true;
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                await _hub.AddMessengerContactAsync(code).ConfigureAwait(false);
                _uiActions.Enqueue(() => _msgrInviteToast = 4f);
            }
            catch (System.Exception ex)
            {
                UiHost.Log.Warning(ex, "[ChatScreen] messenger pair request failed.");
                _uiActions.Enqueue(() => _shell.Shell?.SendIntent("messenger",
                    AetherOS.Sdk.OsIntents.CreateCode(AetherOS.Sdk.OsIntents.MessengerAdd, code)));
            }
            finally
            {
                _msgrInviteBusy = false;
            }
        });
    }

    private volatile bool _msgrInviteBusy;
    private float _msgrInviteToast;
}
