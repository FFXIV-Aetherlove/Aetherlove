using System;
using System.Linq;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Chat;
using AetherLove.Services.Localization;
using AetherLove.Shared.Messaging;
using AetherLove.UI;
using AetherLove.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherLove.Screens;

/// <summary>The single level-2 recipient picker for the AetherLove share target: the OS share sheet picks the
/// app, this in-page overlay picks which match chat to share into. Replaces the copy-pasted venue/hangout
/// pickers the source apps used to draw.</summary>
public sealed class ShareMatchPickerView
{
    private readonly ChatCacheStore _chatCache;

    private bool _open;
    private float _panelH;
    private string _search = "";
    private Guid _selectedPeer;
    private Action<MatchSummaryDto>? _onPick;

    public ShareMatchPickerView(ChatCacheStore chatCache) => _chatCache = chatCache;

    public bool IsOpen => _open;

    public void Open(Action<MatchSummaryDto> onPick)
    {
        _open = true;
        _panelH = 0f;
        _search = "";
        _selectedPeer = Guid.Empty;
        _onPick = onPick;
    }

    public void Close() => _open = false;

    public void Draw(Vector2 windowPos, Vector2 windowSize)
    {
        if (!_open)
        {
            return;
        }
        if (ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            _open = false;
            return;
        }

        var matches = _chatCache.GetMatches();
        var dismissed = DrawPageOverlayPanel("shareMatch", windowPos, windowSize, ref _panelH, Px(330f), innerW =>
        {
            ModalUi.Header(innerW, FontAwesomeIcon.Share, Loc.T("places.share_title"), ThemeService.Current.AccentLight);

            ImGui.SetNextItemWidth(innerW);
            ImGui.InputTextWithHint("##shareMatchSearch", Loc.T("places.share_search_hint"), ref _search, 64);
            ImGui.Spacing();

            using (var list = ImRaii.Child("##shareMatchList", new Vector2(innerW, Px(190f)), false))
            {
                if (list.Success)
                {
                    var query = _search.Trim();
                    var any = false;
                    foreach (var m in matches)
                    {
                        if (query.Length > 0
                            && !m.PeerDisplayName.Contains(query, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        any = true;
                        if (ImGui.Selectable($"{m.PeerDisplayName}##shareMatch{m.PeerProfileId:N}",
                                _selectedPeer == m.PeerProfileId))
                        {
                            _selectedPeer = m.PeerProfileId;
                        }
                    }
                    if (!any)
                    {
                        ImGui.TextColored(UiColors.Muted, Loc.T("places.share_no_matches"));
                    }
                }
            }
            ImGui.Spacing();

            var gap = Px(8f);
            var half = (innerW - gap) * 0.5f;
            if (ModalUi.Button($"{Loc.T("common.cancel")}##shareMatchCancel", half))
            {
                _open = false;
            }
            ImGui.SameLine(0f, gap);
            var target = matches.FirstOrDefault(m => m.PeerProfileId == _selectedPeer);
            using (ImRaii.Disabled(target is null))
            {
                if (ModalUi.Button($"{Loc.T("places.share_send")}##shareMatchSend", half) && target is not null)
                {
                    _open = false;
                    var cb = _onPick;
                    _onPick = null;
                    cb?.Invoke(target);
                }
            }
        });
        if (dismissed)
        {
            _open = false;
        }
    }
}
