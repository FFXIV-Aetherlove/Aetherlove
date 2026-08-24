using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove;
using AetherLove.Services;
using AetherLove.Services.Hangouts;
using AetherLove.Services.Localization;
using AetherLove.Shared.Hangouts;
using AetherLove.UI;
using AetherLove.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;

namespace AetherOS.Apps.Hangouts;

/// <summary>In-app hangout detail overlay. Drawn on top of the app surface each frame; when opened from a
/// chat card its dismiss returns to that chat via the host.</summary>
public sealed class HangoutDetailOverlay
{
    private readonly IHangoutsHost _host;
    private readonly AetherLove.Os.ISocialBridge _social;
    private readonly HangoutStateService _state;

    private bool _open;
    private float _panelH;
    private HangoutSummaryDto? _hangout;
    private bool _fromChat;
    private ISharedImmediateTexture? _avatarTex;
    private volatile bool _busy;
    private volatile string? _error;
    private bool _reportMode;
    private string _reportReason = "";
    private volatile bool _reportSent;
    private double _copiedUntil;

    public HangoutDetailOverlay(IHangoutsHost host, AetherLove.Os.ISocialBridge social, HangoutStateService state)
    {
        _host = host;
        _social = social;
        _state = state;
    }

    public bool IsOpen => _open;

    /// <summary>Wired by the app to the in-app share picker.</summary>
    public Action<HangoutSummaryDto>? ShareHandler { get; set; }

    /// <summary>Wired by the app to the Echo deep link; takes the room id and its share code.</summary>
    public Action<Guid, string>? JoinWatchRoomHandler { get; set; }

    /// <summary>Join the together party a card was published from, by code. Same shape as the watch-room
    /// handler: the card carries a dead code once the party has ended, and the button disappears with it.</summary>
    public Action<string>? JoinPartyHandler { get; set; }

    public void Open(HangoutSummaryDto hangout, bool fromChat = false)
    {
        _hangout = hangout;
        _fromChat = fromChat;
        _open = true;
        _panelH = 0f;
        _error = null;
        _reportMode = false;
        _reportReason = "";
        _reportSent = false;
        ResolveAvatar(hangout);
    }

    /// <summary>The owner's OS avatar: last cached copy first, then a card fetch to refresh it.</summary>
    private void ResolveAvatar(HangoutSummaryDto hangout)
    {
        _avatarTex = null;
        var ownerAccountId = hangout.OwnerAccountId;
        if (ownerAccountId == Guid.Empty)
        {
            return;
        }
        var cacheDir = Path.Combine(UiHost.PluginInterface.ConfigDirectory.FullName, "HangoutAvatarCache");
        try
        {
            if (Directory.Exists(cacheDir))
            {
                var file = Directory.EnumerateFiles(cacheDir, $"{ownerAccountId}_*").FirstOrDefault();
                if (file is not null)
                {
                    _avatarTex = UiHost.TextureProvider.GetFromFile(file);
                }
            }
        }
        catch (Exception ex)
        {
            UiHost.Log.Warning(ex, "[HangoutDetailOverlay] Could not resolve the host avatar.");
        }
        _ = Task.Run(async () =>
        {
            try
            {
                var card = await _host.GetHangoutCardAsync(hangout.Id).ConfigureAwait(false);
                if (card.OwnerAvatarWebp is { Length: > 0 } bytes && _hangout?.Id == hangout.Id)
                {
                    _avatarTex = AvatarDiskCache.Store(cacheDir, ownerAccountId.ToString(), bytes);
                }
            }
            catch (Exception ex)
            {
                UiHost.Log.Debug(ex, "[HangoutDetailOverlay] Card avatar fetch failed.");
            }
        });
    }

    public void Close() => _open = false;

    /// <summary>Closes the overlay, returning to the originating chat when it was opened from a chat card.</summary>
    private void Dismiss()
    {
        _open = false;
        if (_fromChat)
        {
            _fromChat = false;
            _social.OpenChat();
        }
    }

    public void Draw(Vector2 windowPos, Vector2 windowSize)
    {
        if (!_open || _hangout is null)
        {
            return;
        }
        if (ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            Dismiss();
            return;
        }
        var dismissed = DrawPageOverlayPanel("hgOverlay", windowPos, windowSize, ref _panelH, Px(320f), innerW =>
        {
            if (_reportMode)
            {
                DrawReportContent(innerW);
            }
            else
            {
                DrawDetailContent(innerW);
            }
        });
        if (dismissed && !_busy)
        {
            Dismiss();
        }
    }

    private void DrawDetailContent(float w)
    {
        var h = _hangout!;
        var t = ThemeService.Current;
        var live = HangoutFields.IsLiveNow(h);
        var accent = live ? UiColors.LiveGreen : t.Accent;
        var isMine = _state.MyHangout?.Id == h.Id;

        DrawCloseCircleButton(w);
        DrawCollabHeader(w, h, accent);

        ImGui.TextColored(accent, live ? Loc.T("hangout.status_live") : Loc.T("hangout.status_upcoming"));
        ImGui.SameLine();
        ImGui.TextColored(UiColors.Muted, HangoutFields.TimeLabel(h));
        ImGui.TextColored(UiColors.Body, Loc.T("hangout.hosted_by", h.OwnerDisplayName));
        if (h.OwnerIsSupporter)
        {
            ImGui.SameLine(0f, Px(5f));
            ImGui.PushFont(UiHost.PluginInterface.UiBuilder.FontIcon);
            ImGui.TextColored(ImGui.ColorConvertU32ToFloat4(UiColors.FavoriteStar),
                FontAwesomeIcon.Star.ToIconString());
            ImGui.PopFont();
        }
        ImGui.Spacing();

        ImGui.PushTextWrapPos(w);
        ImGui.TextUnformatted(h.Description);
        ImGui.PopTextWrapPos();
        ImGui.Spacing();

        var copied = ImGui.GetTime() < _copiedUntil;
        ImGui.PushFont(UiHost.PluginInterface.UiBuilder.FontIcon);
        ImGui.TextColored(copied ? UiColors.LiveGreen : accent,
            (copied ? FontAwesomeIcon.Check : FontAwesomeIcon.Copy).ToIconString());
        ImGui.PopFont();
        if (ImGui.IsItemClicked())
        {
            ImGui.SetClipboardText(HangoutFields.FormatAddress(h));
            _copiedUntil = ImGui.GetTime() + 1.5;
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            ImGui.SetTooltip(Loc.T(copied ? "hangout.copied" : "hangout.copy_address"));
        }
        ImGui.SameLine(0f, Px(7f));
        ImGui.PushTextWrapPos(w);
        ImGui.TextColored(UiColors.Muted, HangoutFields.FormatAddress(h));
        ImGui.PopTextWrapPos();

        if (h.RsvpCount > 0)
        {
            var countText = Loc.T("hangout.coming_count", HangoutFields.CountLabel(h));
            if (HangoutFields.IsAtCapacity(h))
            {
                countText += " " + Loc.T("hangout.at_capacity");
            }
            ImGui.TextColored(HangoutFields.IsAtCapacity(h) ? UiColors.WarningAccent : UiColors.Muted, countText);
        }
        if (_error is { } err)
        {
            ImGui.PushTextWrapPos(w);
            ImGui.TextColored(UiColors.Danger, err);
            ImGui.PopTextWrapPos();
        }
        ImGui.Spacing();
        ImGui.Spacing();

        var buttons = new List<(string Id, string Label, bool Disabled, Action OnClick)>();
        if (h.EchoRoomId is { } roomId && h.EchoRoomCode is { Length: > 0 } roomCode
            && JoinWatchRoomHandler is { } joinWatch)
        {
            buttons.Add(("Watch", Loc.T("hangout.join_watch_room"), false, () =>
            {
                _open = false;
                joinWatch(roomId, roomCode);
            }));
        }
        if (h.PartyCode is { Length: > 0 } partyCode && JoinPartyHandler is { } joinParty)
        {
            buttons.Add(("Party", Loc.T("hangout.join_party"), false, () =>
            {
                _open = false;
                joinParty(partyCode);
            }));
        }
        if (!isMine)
        {
            var going = _state.IsRsvped(h.Id);
            buttons.Add(("Rsvp", going ? Loc.T("hangout.on_my_way_undo") : Loc.T("hangout.on_my_way"),
                _busy, () => ToggleRsvp(h, !going)));
        }
        if (ShareHandler is { } share)
        {
            buttons.Add(("Share", Loc.T("hangout.share"), false, () =>
            {
                _open = false;
                share(h);
            }));
        }
        if (!isMine)
        {
            buttons.Add(("Report", Loc.T("hangout.report"), _busy, () =>
            {
                _reportMode = true;
                _panelH = 0f;
            }));
        }

        var gap = Px(8f);
        var half = (w - gap) * 0.5f;
        for (var i = 0; i < buttons.Count; i += 2)
        {
            var paired = i + 1 < buttons.Count;
            DrawActionButton(buttons[i], paired ? half : w);
            if (paired)
            {
                ImGui.SameLine(0f, gap);
                DrawActionButton(buttons[i + 1], half);
            }
        }
    }

    private static void DrawActionButton((string Id, string Label, bool Disabled, Action OnClick) btn, float width)
    {
        using (Dalamud.Interface.Utility.Raii.ImRaii.Disabled(btn.Disabled))
        {
            if (ModalUi.Button($"{btn.Label}##hg{btn.Id}", width))
            {
                btn.OnClick();
            }
        }
    }

    private void DrawCollabHeader(float w, HangoutSummaryDto h, Vector4 accent)
    {
        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var icon = HangoutCategories.Icon(h.Category);
        var avatarR = Px(27f);
        var timesPx = Px(18f);
        var catPx = Px(44f);
        var timesSz = IconDraw.Measure(FontAwesomeIcon.Times, timesPx);
        var catSz = IconDraw.Measure(icon, catPx);
        var gap = Px(22f);
        var rowH = MathF.Max(avatarR * 2f, catSz.Y);
        var x = origin.X + Px(2f);
        var midY = origin.Y + rowH * 0.5f;

        var avatarCenter = new Vector2(x + avatarR, midY);
        if (_avatarTex?.GetWrapOrDefault() is { } tex)
        {
            dl.AddImageRounded(tex.Handle, avatarCenter - new Vector2(avatarR), avatarCenter + new Vector2(avatarR),
                Vector2.Zero, Vector2.One, 0xFFFFFFFFu, avatarR, ImDrawFlags.RoundCornersAll);
        }
        else
        {
            dl.AddCircleFilled(avatarCenter, avatarR, UiColors.AvatarFallback);
        }
        dl.AddCircle(avatarCenter, avatarR, ImGui.GetColorU32(accent), 0, Px(2f));
        AvatarRings.Draw(dl, avatarCenter, avatarR, h.OwnerFrameRef);
        if (h.OwnerIsSupporter)
        {
            var badgeCenter = avatarCenter + new Vector2(avatarR * 0.74f, -avatarR * 0.74f);
            var badgeR = Px(7f);
            dl.AddCircleFilled(badgeCenter, badgeR, 0xFF1E1E24u, 24);
            dl.AddCircle(badgeCenter, badgeR, UiColors.FavoriteStar, 24, Px(1.2f));
            IconDraw.AddCentered(dl, FontAwesomeIcon.Star, badgeR * 1.2f,
                badgeCenter + new Vector2(0f, Px(0.5f)), UiColors.FavoriteStar);
        }

        x += avatarR * 2f + gap;
        IconDraw.Add(dl, FontAwesomeIcon.Times, timesPx,
            new Vector2(x, midY - timesSz.Y * 0.5f), ImGui.GetColorU32(UiColors.Muted));
        x += timesSz.X + gap;
        var catPos = new Vector2(x, midY - catSz.Y * 0.5f);
        IconDraw.Add(dl, icon, catPx, catPos, ImGui.GetColorU32(accent));

        var title = HangoutCategories.Label(h.Category);
        var titleSz = ImGui.CalcTextSize(title);
        var titleX = MathF.Max(origin.X,
            MathF.Min(catPos.X + catSz.X * 0.5f - titleSz.X * 0.5f, origin.X + w - titleSz.X));
        dl.AddText(new Vector2(titleX, origin.Y + rowH + Px(6f)), ImGui.GetColorU32(accent), title);

        ImGui.Dummy(new Vector2(w, rowH + Px(6f) + titleSz.Y));
        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.Separator, accent with { W = 0.35f });
        ImGui.Separator();
        ImGui.PopStyleColor();
        ImGui.Spacing();
    }

    private void DrawCloseCircleButton(float w)
    {
        if (ModalUi.CloseButton("##hgCloseCircle", w, back: _fromChat,
                tooltip: Loc.T(_fromChat ? "places.back_to_chat" : "common.close")))
        {
            Dismiss();
        }
    }

    private void DrawReportContent(float w)
    {
        var h = _hangout!;
        ModalUi.Header(w, FontAwesomeIcon.Flag, Loc.T("hangout.report_title"), UiColors.Danger);

        if (_reportSent)
        {
            ImGui.PushTextWrapPos(w);
            ImGui.TextColored(UiColors.Body, Loc.T("hangout.report_thanks"));
            ImGui.PopTextWrapPos();
            ImGui.Spacing();
            if (ModalUi.Button($"{Loc.T("common.close")}##hgRepClose", w))
            {
                Dismiss();
            }
            return;
        }

        ImGui.PushTextWrapPos(w);
        ImGui.TextColored(UiColors.Body, Loc.T("hangout.report_body"));
        ImGui.PopTextWrapPos();
        ImGui.Spacing();
        InputTextMultilineWithPaste("##hgRepReason", ref _reportReason, 1000, new Vector2(w, Px(70f)));
        if (_error is { } err)
        {
            ImGui.PushTextWrapPos(w);
            ImGui.TextColored(UiColors.Danger, err);
            ImGui.PopTextWrapPos();
        }
        ImGui.Spacing();

        var gap = Px(8f);
        var half = (w - gap) * 0.5f;
        if (ModalUi.Button($"{Loc.T("common.cancel")}##hgRepCancel", half))
        {
            _reportMode = false;
            _panelH = 0f;
        }
        ImGui.SameLine(0f, gap);
        using (Dalamud.Interface.Utility.Raii.ImRaii.Disabled(_busy || _reportReason.Trim().Length == 0))
        {
            if (ModalUi.Button($"{Loc.T("hangout.report_submit")}##hgRepSend", half))
            {
                SubmitReport(h);
            }
        }
    }

    private void ToggleRsvp(HangoutSummaryDto h, bool going)
    {
        _busy = true;
        _error = null;
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await _host.SetHangoutRsvpAsync(h.Id, going).ConfigureAwait(false);
                _state.SetMyRsvp(h.Id, result.Going, result.RsvpCount);
                if (_hangout?.Id == h.Id)
                {
                    _hangout = _hangout with { RsvpCount = result.RsvpCount };
                }
            }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, "[HangoutDetailOverlay] SetHangoutRsvpAsync failed.");
                _error = HubErrorText.Localize(ex);
            }
            finally
            {
                _busy = false;
            }
        });
    }

    private void SubmitReport(HangoutSummaryDto h)
    {
        _busy = true;
        _error = null;
        var reason = _reportReason.Trim();
        _ = Task.Run(async () =>
        {
            try
            {
                await _host.ReportHangoutAsync(new ReportHangoutRequest(h.Id, reason)).ConfigureAwait(false);
                _reportSent = true;
                _panelH = 0f;
            }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, "[HangoutDetailOverlay] ReportHangoutAsync failed.");
                _error = HubErrorText.Localize(ex);
            }
            finally
            {
                _busy = false;
            }
        });
    }
}
