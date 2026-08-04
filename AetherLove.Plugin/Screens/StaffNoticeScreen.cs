using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Navigation;
using AetherLove.Services;
using AetherLove.Services.Auth;
using AetherLove.Services.Hub;
using AetherLove.Services.Localization;
using AetherLove.Shared.Profile;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherLove.Screens;

/// <summary>Full-phone gate for account-level staff notices (the OS moderation track): unseen warnings first,
/// then unseen messages, acknowledged together. Reads the account snapshot, so it works for an account with no
/// dating profile.</summary>
public sealed class StaffNoticeScreen
{
    /// <summary>OS-notification tag for the staff-notice batch, shared with the Settings staff page that clears it.</summary>
    public const string NotificationTag = "staff:notice";

    private readonly ScreenRouter _router;
    private readonly SessionBootstrapper _bootstrap;
    private readonly AetherHubContext _hub;
    private readonly Os.OsShell _osShell;

    private static float PadX => Px(16f);

    private WarningDto[] _warnings = [];
    private ModeratorMessageDto[] _messages = [];
    private volatile bool _submitting;
    private volatile string? _submitError;
    private bool _pendingLive;
    private bool _liveReturn;

    public StaffNoticeScreen(ScreenRouter router,
                             SessionBootstrapper bootstrap,
                             AetherHubContext hub,
                             Os.OsShell osShell)
    {
        _router = router;
        _bootstrap = bootstrap;
        _hub = hub;
        _osShell = osShell;
    }

    /// <summary>Marks the next showing as a live mid-session push: on acknowledge it returns to where the user
    /// was instead of re-running the startup ladder. Called before the navigation to this screen, so the router
    /// still points at the interrupted location.</summary>
    public void RequestLiveAcknowledge()
    {
        _pendingLive = true;
        LiveGateReturn.Capture(_router, _osShell);
    }

    public void OnShow()
    {
        _liveReturn = _pendingLive;
        _pendingLive = false;
        LoadBatch();
    }

    /// <summary>Re-reads the snapshot while the gate is already showing, so a notice that arrived mid-gate joins
    /// the displayed batch. Leaves the return target alone: the batch grew, the entry route did not change.</summary>
    public void RefreshBatch()
    {
        if (_submitting)
        {
            return;
        }
        LoadBatch();
    }

    private void LoadBatch()
    {
        var account = _bootstrap.LastAccount;
        _warnings = account?.StaffWarnings?.Where(w => !w.Seen).OrderByDescending(w => w.CreatedAtUtc).ToArray() ?? [];
        _messages = account?.StaffMessages?.Where(m => !m.Seen).OrderByDescending(m => m.CreatedAtUtc).ToArray() ?? [];
        _submitting = false;
        _submitError = null;

        if (_warnings.Length == 0 && _messages.Length == 0)
        {
            NavigateToTarget();
        }
    }

    public void Draw()
    {
        var t = ThemeService.Current;
        var winW = ImGui.GetWindowSize().X;
        var scrollH = ImGui.GetContentRegionAvail().Y;

        PushScrollbarStyle();

        using (var scroll = ImRaii.Child("##staffNotice", new Vector2(0f, scrollH), false))
        {
            PopScrollbarStyle();

            if (!scroll.Success)
            {
                return;
            }

            ImGui.Spacing();
            ImGui.Spacing();

            var total = _warnings.Length + _messages.Length;
            var heading = total == 1
                ? Loc.T("common.staff_notice_heading_one")
                : Loc.T("common.staff_notice_heading_many", total);
            ImGui.SetCursorPosX(PadX);
            ImGui.TextColored(t.AccentLight, heading);
            ImGui.Spacing();

            var dl = ImGui.GetWindowDrawList();
            ImGui.SetCursorPosX(PadX);
            var p = ImGui.GetCursorScreenPos();
            var endX = p.X + winW - PadX * 2f;
            dl.AddLine(p, new Vector2(endX, p.Y),
                ImGui.ColorConvertFloat4ToU32(t.AccentLight with { W = 0.53f }), 1f);
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + Px(6f));
            ImGui.Spacing();

            ImGui.SetCursorPosX(PadX);
            ImGui.PushTextWrapPos(winW - PadX);
            ImGui.TextColored(UiColors.Body, Loc.T("common.staff_notice_body"));
            ImGui.PopTextWrapPos();
            ImGui.Spacing();
            ImGui.Spacing();

            if (_warnings.Length > 0)
            {
                DrawSectionLabel(Loc.T("settings.staff_warnings_section"), UiColors.WarningAccent);
                ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0f, Px(10f)));
                foreach (var w in _warnings)
                {
                    DrawNoticeCard(winW, FontAwesomeIcon.ExclamationTriangle, UiColors.WarningAccent,
                        w.CreatedAtUtc, w.Reason, w.Seen, 16f);
                }
                ImGui.PopStyleVar();
                ImGui.Spacing();
            }

            if (_messages.Length > 0)
            {
                DrawSectionLabel(Loc.T("settings.staff_messages_section"), UiColors.MessageAccent);
                ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0f, Px(10f)));
                foreach (var m in _messages)
                {
                    DrawNoticeCard(winW, FontAwesomeIcon.Envelope, UiColors.MessageAccent,
                        m.CreatedAtUtc, m.Body, m.Seen, 16f);
                }
                ImGui.PopStyleVar();
                ImGui.Spacing();
            }

            ImGui.Spacing();
            ImGui.SetCursorPosX(PadX);
            ImGui.PushStyleColor(ImGuiCol.Button, t.ButtonNormal);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, t.ButtonHovered);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, t.ButtonActive);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));

            var btnLabel = _submitting ? Loc.T("common.acknowledging") : Loc.T("common.staff_notice_ack");
            if (_submitting)
            {
                ImGui.BeginDisabled();
            }
            if (SharedUiHelpers.Button(btnLabel, new Vector2(winW - PadX * 2f, Px(36f))))
            {
                StartAcknowledge();
            }
            if (_submitting)
            {
                ImGui.EndDisabled();
            }
            ImGui.PopStyleVar();
            ImGui.PopStyleColor(3);

            if (_submitError is not null)
            {
                ImGui.Spacing();
                ImGui.SetCursorPosX(PadX);
                ImGui.PushTextWrapPos(winW - PadX);
                ImGui.TextColored(UiColors.Danger,
                    Loc.T("common.warnings_submit_error", _submitError));
                ImGui.PopTextWrapPos();
            }
        }
    }

    private static void DrawSectionLabel(string label, Vector4 accent)
    {
        ImGui.SetCursorPosX(PadX);
        ImGui.TextColored(accent, label);
        ImGui.Spacing();
    }

    private void StartAcknowledge()
    {
        if (_submitting)
        {
            return;
        }
        _submitting = true;
        _submitError = null;

        var warningIds = _warnings.Select(w => w.Id).ToArray();
        var messageIds = _messages.Select(m => m.Id).ToArray();

        _ = Task.Run(async () =>
        {
            try
            {
                if (warningIds.Length > 0)
                {
                    await _hub.MarkStaffWarningsSeenAsync(warningIds, CancellationToken.None).ConfigureAwait(false);
                }
                if (messageIds.Length > 0)
                {
                    await _hub.MarkStaffMessagesSeenAsync(messageIds, CancellationToken.None).ConfigureAwait(false);
                }

                _bootstrap.MarkStaffNoticesSeenInSnapshot(new HashSet<Guid>(warningIds.Concat(messageIds)));

                NavigateToTarget();
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "[StaffNotice] Marking account staff notices seen failed.");
                _submitError = HubErrorText.Localize(ex);
            }
            finally
            {
                _submitting = false;
            }
        });
    }

    private void NavigateToTarget()
    {
        // A live mid-session notice returns to the interrupted location; re-running the startup ladder would
        // mis-route to gates.
        if (_liveReturn)
        {
            _liveReturn = false;
            LiveGateReturn.Return(_router, _osShell);
            return;
        }

        var next = _bootstrap.ResolveNextStartupScreen();
        // A notice that landed while the gate was up leaves the ladder pointing back here, where Navigate is a
        // no-op that would never re-run OnShow and would redraw the acknowledged batch forever.
        if (next == Screen.StaffNotice && _router.Current == Screen.StaffNotice)
        {
            LoadBatch();
            return;
        }
        _router.Navigate(next);
    }
}
