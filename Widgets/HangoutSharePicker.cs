using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove.Navigation;
using AetherLove.Services;
using AetherLove.Services.Hub;
using AetherLove.Services.Localization;
using AetherLove.Shared.Hangouts;
using AetherLove.Shared.Messaging;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Microsoft.Extensions.DependencyInjection;

namespace AetherLove.Widgets;

/// <summary>In-phone "share this hangout with a match" picker. Wires itself as the
/// <see cref="HangoutOverlay.ShareHandler"/>; hosts draw it right after the overlay.</summary>
public sealed class HangoutSharePicker
{
    private readonly AetherLoveHubClient _hub;
    private readonly ScreenRouter _router;
    private readonly HangoutShareContext _shareCtx;
    // Lazy-resolved: ChatListScreen depends on the overlay/picker pair, so a ctor dependency would cycle.
    private readonly IServiceProvider _services;

    private bool _open;
    private float _panelH;
    private Guid _hangoutId;
    private string _search = "";
    private Guid _selectedPeer;
    private volatile MatchSummaryDto[]? _matches;
    private volatile bool _loading;
    private volatile string? _error;

    public HangoutSharePicker(
        AetherLoveHubClient hub,
        ScreenRouter router,
        HangoutShareContext shareCtx,
        HangoutOverlay overlay,
        IServiceProvider services)
    {
        _hub = hub;
        _router = router;
        _shareCtx = shareCtx;
        _services = services;
        overlay.ShareHandler = h => Open(h.Id);
    }

    public void Open(Guid hangoutId)
    {
        _hangoutId = hangoutId;
        _open = true;
        _panelH = 0f;
        _search = "";
        _selectedPeer = Guid.Empty;
        StartMatchesFetch();
    }

    private void StartMatchesFetch()
    {
        if (_loading)
        {
            return;
        }
        _loading = true;
        _error = null;
        _matches = null;
        _ = Task.Run(async () =>
        {
            try
            {
                var dto = await _hub.GetMyMatchesAsync().ConfigureAwait(false);
                _matches = dto.Matches
                    .OrderBy(m => m.PeerDisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (Exception ex)
            {
                _error = HubErrorText.Localize(ex);
                Plugin.Log.Warning(ex, "[HangoutSharePicker] GetMyMatchesAsync failed.");
            }
            finally
            {
                _loading = false;
            }
        });
    }

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

        var dismissed = DrawPageOverlayPanel("hgShare", windowPos, windowSize, ref _panelH, Px(330f), innerW =>
        {
            ModalUi.Header(innerW, FontAwesomeIcon.Share, Loc.T("hangout.share_title"), ThemeService.Current.AccentLight);

            ImGui.SetNextItemWidth(innerW);
            ImGui.InputTextWithHint("##hgShareSearch", Loc.T("places.share_search_hint"), ref _search, 64);
            ImGui.Spacing();

            using (var list = ImRaii.Child("##hgShareList", new Vector2(innerW, Px(190f)), false))
            {
                if (list.Success)
                {
                    if (_loading && _matches is null)
                    {
                        LoadingIndicator.Draw();
                    }
                    else if (_error is not null)
                    {
                        ImGui.PushTextWrapPos(innerW);
                        ImGui.TextColored(UiColors.Danger, _error);
                        ImGui.PopTextWrapPos();
                    }
                    else if (_matches is { } matches)
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
                            if (ImGui.Selectable($"{m.PeerDisplayName}##hgShare{m.PeerProfileId:N}",
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
            }
            ImGui.Spacing();

            var gap = Px(8f);
            var half = (innerW - gap) * 0.5f;
            if (ModalUi.Button($"{Loc.T("common.cancel")}##hgShareCancel", half))
            {
                _open = false;
            }
            ImGui.SameLine(0f, gap);
            var target = _matches?.FirstOrDefault(m => m.PeerProfileId == _selectedPeer);
            using (ImRaii.Disabled(target is null))
            {
                if (ModalUi.Button($"{Loc.T("places.share_send")}##hgShareSend", half) && target is not null)
                {
                    _open = false;
                    _shareCtx.PendingShareHangoutId = _hangoutId;
                    _services.GetRequiredService<Screens.ChatListScreen>().SelectPeer(target);
                    if (_router.Current == Screen.Chat)
                    {
                        // Same-screen navigation never re-fires OnShow, so a share sent from inside a chat
                        // would sit queued until the chat is reopened.
                        _shareCtx.PendingShareReturn = Screen.ChatList;
                        var chat = _services.GetRequiredService<Screens.ChatScreen>();
                        chat.OnHide();
                        chat.OnShow();
                    }
                    else
                    {
                        _shareCtx.PendingShareReturn = _router.Current;
                        _router.Navigate(Screen.Chat);
                    }
                }
            }
        });
        if (dismissed)
        {
            _open = false;
        }
    }
}
