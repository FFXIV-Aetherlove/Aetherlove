using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Shared.Hangouts;
using AetherLove.UI;
using AetherLove.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Messenger;

/// <summary>The in-messenger hangout detail popup, a fork of the Hangouts app's overlay: tapping a hangout
/// card or a hosting marker shows the details right here, so closing it never strands the user in another
/// app.</summary>
public sealed partial class MessengerApp
{
    private HangoutSummaryDto? _hgPopup;
    private float _hgPopupH;
    private ISharedImmediateTexture? _hgAvatarTex;
    private volatile bool _hgBusy;
    private volatile string? _hgError;
    private bool _hgReportMode;
    private string _hgReportReason = "";
    private volatile bool _hgReportSent;
    private double _hgCopiedUntil;

    private void OpenHangoutPopup(HangoutSummaryDto hangout)
    {
        _hgPopup = hangout;
        _hgPopupH = 0f;
        _hgError = null;
        _hgReportMode = false;
        _hgReportReason = "";
        _hgReportSent = false;
        ResolveHangoutAvatar(hangout);
    }

    /// <summary>The owner's OS avatar: last cached copy first, then a card fetch to refresh it.</summary>
    private void ResolveHangoutAvatar(HangoutSummaryDto hangout)
    {
        _hgAvatarTex = null;
        var ownerAccountId = hangout.OwnerAccountId;
        if (ownerAccountId == Guid.Empty)
        {
            return;
        }
        var cacheDir = Path.Combine(AetherLove.UiHost.PluginInterface.ConfigDirectory.FullName, "HangoutAvatarCache");
        try
        {
            if (Directory.Exists(cacheDir))
            {
                var file = Directory.EnumerateFiles(cacheDir, $"{ownerAccountId}_*").FirstOrDefault();
                if (file is not null)
                {
                    _hgAvatarTex = AetherLove.UiHost.TextureProvider.GetFromFile(file);
                }
            }
        }
        catch (Exception ex)
        {
            AetherLove.UiHost.Log.Warning(ex, "[MessengerApp] Could not resolve the host avatar.");
        }
        _ = Task.Run(async () =>
        {
            try
            {
                var card = await _hub.GetHangoutCardAsync(hangout.Id).ConfigureAwait(false);
                if (card.OwnerAvatarWebp is { Length: > 0 } bytes && _hgPopup?.Id == hangout.Id)
                {
                    _hgAvatarTex = AvatarDiskCache.Store(cacheDir, ownerAccountId.ToString(), bytes);
                }
            }
            catch (Exception ex)
            {
                AetherLove.UiHost.Log.Debug(ex, "[MessengerApp] Hangout card avatar fetch failed.");
            }
        });
    }

    private void DrawHangoutPopup()
    {
        if (_hgPopup is null)
        {
            return;
        }
        if (ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            _hgPopup = null;
            return;
        }
        var dismissed = DrawPageOverlayPanel("msgrHgPopup", ImGui.GetWindowPos(), ImGui.GetWindowSize(),
            ref _hgPopupH, Px(320f), innerW =>
        {
            if (_hgReportMode)
            {
                DrawHangoutReportContent(innerW);
            }
            else
            {
                DrawHangoutDetailContent(innerW);
            }
        });
        if (dismissed && !_hgBusy)
        {
            _hgPopup = null;
        }
    }

    private void DrawHangoutDetailContent(float w)
    {
        var h = _hgPopup!;
        var t = ThemeService.Current;
        var live = HangoutFields.IsLiveNow(h);
        var accent = live ? UiColors.LiveGreen : t.Accent;
        var isMine = _hangoutState.MyHangout?.Id == h.Id;

        DrawHangoutCloseButton(w);
        DrawHangoutHeader(w, h, accent);

        ImGui.TextColored(accent, live ? Loc.T("hangout.status_live") : Loc.T("hangout.status_upcoming"));
        ImGui.SameLine();
        ImGui.TextColored(UiColors.Muted, HangoutFields.TimeLabel(h));
        ImGui.TextColored(UiColors.Body, Loc.T("hangout.hosted_by", h.OwnerDisplayName));
        if (h.OwnerIsSupporter)
        {
            ImGui.SameLine(0f, Px(5f));
            ImGui.PushFont(AetherLove.UiHost.PluginInterface.UiBuilder.FontIcon);
            ImGui.TextColored(ImGui.ColorConvertU32ToFloat4(UiColors.FavoriteStar),
                FontAwesomeIcon.Star.ToIconString());
            ImGui.PopFont();
        }
        ImGui.Spacing();

        ImGui.PushTextWrapPos(w);
        ImGui.TextUnformatted(h.Description);
        ImGui.PopTextWrapPos();
        ImGui.Spacing();

        var copied = ImGui.GetTime() < _hgCopiedUntil;
        ImGui.PushFont(AetherLove.UiHost.PluginInterface.UiBuilder.FontIcon);
        ImGui.TextColored(copied ? UiColors.LiveGreen : accent,
            (copied ? FontAwesomeIcon.Check : FontAwesomeIcon.Copy).ToIconString());
        ImGui.PopFont();
        if (ImGui.IsItemClicked())
        {
            ImGui.SetClipboardText(HangoutFields.FormatAddress(h));
            _hgCopiedUntil = ImGui.GetTime() + 1.5;
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
        if (_hgError is { } err)
        {
            ImGui.PushTextWrapPos(w);
            ImGui.TextColored(UiColors.Danger, err);
            ImGui.PopTextWrapPos();
        }
        ImGui.Spacing();
        ImGui.Spacing();

        if (!isMine)
        {
            var going = _hangoutState.IsRsvped(h.Id);
            var gap = Px(8f);
            var half = (w - gap) * 0.5f;
            using (ImRaii.Disabled(_hgBusy))
            {
                if (ModalUi.Button($"{Loc.T(going ? "hangout.on_my_way_undo" : "hangout.on_my_way")}##msgrHgRsvp", half))
                {
                    ToggleHangoutRsvp(h, !going);
                }
            }
            ImGui.SameLine(0f, gap);
            using (ImRaii.Disabled(_hgBusy))
            {
                if (ModalUi.Button($"{Loc.T("hangout.report_title")}##msgrHgReport", half))
                {
                    _hgReportMode = true;
                    _hgPopupH = 0f;
                }
            }
        }
    }

    private void DrawHangoutHeader(float w, HangoutSummaryDto h, Vector4 accent)
    {
        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var icon = HangoutFields.CategoryIcon(h.Category);
        var avatarR = Px(27f);
        var catPx = Px(44f);
        var catSz = IconDraw.Measure(icon, catPx);
        var gap = Px(22f);
        var rowH = MathF.Max(avatarR * 2f, catSz.Y);
        var x = origin.X + Px(2f);
        var midY = origin.Y + rowH * 0.5f;

        var avatarCenter = new Vector2(x + avatarR, midY);
        if (_hgAvatarTex?.GetWrapOrDefault() is { } tex)
        {
            dl.AddImageRounded(tex.Handle, avatarCenter - new Vector2(avatarR), avatarCenter + new Vector2(avatarR),
                Vector2.Zero, Vector2.One, 0xFFFFFFFFu, avatarR, ImDrawFlags.RoundCornersAll);
        }
        else
        {
            dl.AddCircleFilled(avatarCenter, avatarR, UiColors.AvatarFallback);
        }
        dl.AddCircle(avatarCenter, avatarR, ImGui.GetColorU32(accent), 0, Px(2f));
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
        var catPos = new Vector2(x, midY - catSz.Y * 0.5f);
        IconDraw.Add(dl, icon, catPx, catPos, ImGui.GetColorU32(accent));

        var title = HangoutFields.CategoryLabel(h.Category);
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

    private void DrawHangoutCloseButton(float w)
    {
        if (ModalUi.CloseButton("##msgrHgClose", w, tooltip: Loc.T("common.close")))
        {
            _hgPopup = null;
        }
    }

    private void DrawHangoutReportContent(float w)
    {
        var h = _hgPopup!;
        ModalUi.Header(w, FontAwesomeIcon.Flag, Loc.T("hangout.report_title"), UiColors.Danger);

        if (_hgReportSent)
        {
            ImGui.PushTextWrapPos(w);
            ImGui.TextColored(UiColors.Body, Loc.T("hangout.report_thanks"));
            ImGui.PopTextWrapPos();
            ImGui.Spacing();
            if (ModalUi.Button($"{Loc.T("common.close")}##msgrHgRepClose", w))
            {
                _hgPopup = null;
            }
            return;
        }

        ImGui.PushTextWrapPos(w);
        ImGui.TextColored(UiColors.Body, Loc.T("hangout.report_body"));
        ImGui.PopTextWrapPos();
        ImGui.Spacing();
        InputTextMultilineWithPaste("##msgrHgRepReason", ref _hgReportReason, 1000, new Vector2(w, Px(70f)));
        if (_hgError is { } err)
        {
            ImGui.PushTextWrapPos(w);
            ImGui.TextColored(UiColors.Danger, err);
            ImGui.PopTextWrapPos();
        }
        ImGui.Spacing();

        var gap = Px(8f);
        var half = (w - gap) * 0.5f;
        if (ModalUi.Button($"{Loc.T("common.cancel")}##msgrHgRepCancel", half))
        {
            _hgReportMode = false;
            _hgPopupH = 0f;
        }
        ImGui.SameLine(0f, gap);
        using (ImRaii.Disabled(_hgBusy || _hgReportReason.Trim().Length == 0))
        {
            if (ModalUi.Button($"{Loc.T("hangout.report_submit")}##msgrHgRepSend", half))
            {
                SubmitHangoutReport(h);
            }
        }
    }

    private void ToggleHangoutRsvp(HangoutSummaryDto h, bool going)
    {
        _hgBusy = true;
        _hgError = null;
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await _hub.SetHangoutRsvpAsync(h.Id, going).ConfigureAwait(false);
                _hangoutState.SetMyRsvp(h.Id, result.Going, result.RsvpCount);
                if (_hgPopup?.Id == h.Id)
                {
                    _hgPopup = _hgPopup with { RsvpCount = result.RsvpCount };
                }
            }
            catch (Exception ex)
            {
                AetherLove.UiHost.Log.Warning(ex, "[MessengerApp] SetHangoutRsvpAsync failed.");
                _hgError = HubErrorText.Localize(ex);
            }
            finally
            {
                _hgBusy = false;
            }
        });
    }

    private void SubmitHangoutReport(HangoutSummaryDto h)
    {
        _hgBusy = true;
        _hgError = null;
        var reason = _hgReportReason.Trim();
        _ = Task.Run(async () =>
        {
            try
            {
                await _hub.ReportHangoutAsync(new ReportHangoutRequest(h.Id, reason)).ConfigureAwait(false);
                _hgReportSent = true;
                _hgPopupH = 0f;
            }
            catch (Exception ex)
            {
                AetherLove.UiHost.Log.Warning(ex, "[MessengerApp] ReportHangoutAsync failed.");
                _hgError = HubErrorText.Localize(ex);
            }
            finally
            {
                _hgBusy = false;
            }
        });
    }
}
