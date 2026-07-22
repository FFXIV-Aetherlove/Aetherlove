using System;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove.Services;
using AetherLove.Services.Hub;
using AetherLove.Services.Localization;
using AetherLove.Shared.Profile;
using AetherLove.UI;
using AetherLove.Widgets;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherLove.Screens;

/// <summary>Level-1.5 of the share flow on a multi-profile account: before the match picker, choose WHICH
/// AetherLove profile shares the content. Locked profiles are not offered.</summary>
public sealed class ShareProfileChoiceView
{
    private readonly AetherHubContext _hub;

    private bool _open;
    private float _panelH;
    private ProfileSummaryDto[] _profiles = [];
    private Guid _activeId;
    private volatile bool _loading;
    private ShareItem? _item;
    private Action<ShareItem, ProfileSummaryDto>? _onPick;

    public ShareProfileChoiceView(AetherHubContext hub) => _hub = hub;

    public void Open(ShareItem item, Action<ShareItem, ProfileSummaryDto> onPick)
    {
        _open = true;
        _panelH = 0f;
        _item = item;
        _onPick = onPick;
        _profiles = [];
        _loading = true;
        _ = Task.Run(async () =>
        {
            try
            {
                var list = await _hub.ListProfilesAsync().ConfigureAwait(false);
                _profiles = Array.FindAll(list.Profiles, p => !p.Locked);
                _activeId = list.ActiveProfileId;
            }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, "[ShareProfileChoice] ListProfilesAsync failed.");
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
        var dismissed = DrawPageOverlayPanel("shareProfile", windowPos, windowSize, ref _panelH, Px(230f), innerW =>
        {
            ModalUi.Header(innerW, FontAwesomeIcon.UserFriends, Loc.T("picker.share_as_title"),
                ThemeService.Current.AccentLight);
            ImGui.PushTextWrapPos(innerW);
            ImGui.TextColored(UiColors.Body, Loc.T("picker.share_as_body"));
            ImGui.PopTextWrapPos();
            ImGui.Spacing();
            if (_loading)
            {
                ImGui.TextColored(UiColors.Muted, Loc.T("common.loading"));
                return;
            }
            foreach (var p in _profiles)
            {
                var label = p.ProfileId == _activeId
                    ? Loc.T("picker.share_as_current", p.DisplayName)
                    : p.DisplayName;
                if (ModalUi.Button($"{label}##shareAs_{p.ProfileId}", innerW) && _item is { } item)
                {
                    _open = false;
                    _onPick?.Invoke(item, p);
                }
                ImGui.Spacing();
            }
        });
        if (dismissed)
        {
            _open = false;
        }
    }
}
