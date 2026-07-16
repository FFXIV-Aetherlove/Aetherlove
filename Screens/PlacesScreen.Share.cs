using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove.Navigation;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Shared.Messaging;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherLove.Screens;

public partial class PlacesScreen
{
    private bool _detailFromChat;

    private bool _shareOpen;
    private float _sharePanelH;
    private string _shareSearch = "";
    private Guid _shareSelectedPeer;
    private volatile MatchSummaryDto[]? _shareMatches;
    private volatile bool _shareLoading;
    private volatile string? _shareError;

    private void OpenDetailFromChat(Guid venueId)
    {
        _detailVenueId = venueId;
        _detailName = "";
        _detail = null;
        _detailError = null;
        _extraReviews = [];
        _reviewsExhausted = false;
        _confirmDeleteReview = false;
        _reviewError = null;
        _tagsExpanded = false;
        _detailFromChat = true;
        _section = Section.Detail;
        StartDetailFetch();
    }

    private void DrawSharePill(float winW, Vector2 rowTop)
    {
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var label = Loc.T("places.share");
        var labelSz = ImGui.CalcTextSize(label);
        var iconPx = ImGui.GetFontSize() * 0.85f;
        var iconSz = IconDraw.Measure(FontAwesomeIcon.Share, iconPx);
        var padX = Px(11f);
        var gap = Px(6f);
        var pillH = labelSz.Y + Px(9f);
        var pillW = padX * 2f + iconSz.X + gap + labelSz.X;
        var tl = new Vector2(rowTop.X + winW - Px(PadX) - pillW, rowTop.Y - Px(2f));

        ImGui.SetCursorScreenPos(tl);
        var clicked = ImGui.InvisibleButton("##venueShareBtn", new Vector2(pillW, pillH));
        var hovered = ImGui.IsItemHovered();

        dl.AddRectFilled(tl, tl + new Vector2(pillW, pillH),
            ImGui.GetColorU32(t.Accent with { W = hovered ? 0.45f : 0.22f }), pillH * 0.5f);
        dl.AddRect(tl, tl + new Vector2(pillW, pillH),
            ImGui.GetColorU32(t.Accent with { W = hovered ? 0.95f : 0.60f }), pillH * 0.5f, ImDrawFlags.None, Px(1f));
        IconDraw.Add(dl, FontAwesomeIcon.Share, iconPx,
            new Vector2(tl.X + padX, tl.Y + (pillH - iconSz.Y) * 0.5f), ImGui.GetColorU32(t.AccentLight));
        dl.AddText(new Vector2(tl.X + padX + iconSz.X + gap, tl.Y + (pillH - labelSz.Y) * 0.5f),
            0xFFFFFFFFu, label);

        if (clicked)
        {
            _shareOpen = true;
            _sharePanelH = 0f;
            _shareSearch = "";
            _shareSelectedPeer = Guid.Empty;
            StartShareMatchesFetch();
        }
    }

    private void StartShareMatchesFetch()
    {
        if (_shareLoading)
        {
            return;
        }
        _shareLoading = true;
        _shareError = null;
        _shareMatches = null;
        var ct = _cts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                var dto = await _hubClient.GetMyMatchesAsync(ct).ConfigureAwait(false);
                _shareMatches = dto.Matches
                    .OrderBy(m => m.PeerDisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _shareError = HubErrorText.Localize(ex);
                Plugin.Log.Warning(ex, "[PlacesScreen] GetMyMatchesAsync failed for the share picker.");
            }
            finally
            {
                _shareLoading = false;
            }
        }, ct);
    }

    private void DrawShareOverlay()
    {
        if (!_shareOpen)
        {
            return;
        }
        if (ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            _shareOpen = false;
            return;
        }

        var dismissed = DrawPageOverlayPanel("venueShare", ImGui.GetWindowPos(), ImGui.GetWindowSize(),
            ref _sharePanelH, Px(330f), innerW =>
        {
            Widgets.ModalUi.Header(innerW, FontAwesomeIcon.Share, Loc.T("places.share_title"), ThemeService.Current.AccentLight);

            ImGui.SetNextItemWidth(innerW);
            ImGui.InputTextWithHint("##shareSearch", Loc.T("places.share_search_hint"), ref _shareSearch, 64);
            ImGui.Spacing();

            using (var list = ImRaii.Child("##shareMatchList", new Vector2(innerW, Px(190f)), false))
            {
                if (list.Success)
                {
                    if (_shareLoading && _shareMatches is null)
                    {
                        Widgets.LoadingIndicator.Draw();
                    }
                    else if (_shareError is not null)
                    {
                        ImGui.PushTextWrapPos(innerW);
                        ImGui.TextColored(UiColors.Danger, _shareError);
                        ImGui.PopTextWrapPos();
                    }
                    else if (_shareMatches is { } matches)
                    {
                        var query = _shareSearch.Trim();
                        var any = false;
                        foreach (var m in matches)
                        {
                            if (query.Length > 0
                                && !m.PeerDisplayName.Contains(query, StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }
                            any = true;
                            if (ImGui.Selectable($"{m.PeerDisplayName}##share{m.PeerProfileId:N}",
                                    _shareSelectedPeer == m.PeerProfileId))
                            {
                                _shareSelectedPeer = m.PeerProfileId;
                            }
                        }
                        if (!any)
                        {
                            ImGui.TextColored(UiColors.Muted, Loc.T("places.share_no_matches"));
                        }
                    }
                }
            }
            ImGui.Spacing();

            var gap = Px(8f);
            var half = (innerW - gap) * 0.5f;
            if (Widgets.ModalUi.Button($"{Loc.T("common.cancel")}##shareCancel", half))
            {
                _shareOpen = false;
            }
            ImGui.SameLine(0f, gap);
            var target = _shareMatches?.FirstOrDefault(m => m.PeerProfileId == _shareSelectedPeer);
            if (target is null)
            {
                ImGui.BeginDisabled();
            }
            if (Widgets.ModalUi.Button($"{Loc.T("places.share_send")}##shareSend", half) && target is not null)
            {
                _shareOpen = false;
                _chatList.SelectPeer(target);
                _shareCtx.PendingShareVenueId = _detailVenueId;
                _router.Navigate(Screen.Chat);
            }
            if (target is null)
            {
                ImGui.EndDisabled();
            }
        });
        if (dismissed)
        {
            _shareOpen = false;
        }
    }
}
