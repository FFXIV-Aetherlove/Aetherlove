using System;
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

/// <summary>Acknowledge screen for unseen moderation warnings.</summary>
public sealed class WarningAcknowledgeScreen
{
    private readonly ScreenRouter _router;
    private readonly SessionBootstrapper _bootstrap;
    private readonly AetherLoveHubClient _hub;

    private static float PadX => Px(16f);

    private WarningDto[] _toAcknowledge = [];
    private volatile bool _submitting;
    private volatile string? _submitError;
    private bool _pendingLive;
    private bool _returnToDeck;

    public WarningAcknowledgeScreen(ScreenRouter router,
                                    SessionBootstrapper bootstrap,
                                    AetherLoveHubClient hub)
    {
        _router = router;
        _bootstrap = bootstrap;
        _hub = hub;
    }

    /// <summary>Marks the next showing as a live mid-session push (not a startup gate): on acknowledge it
    /// returns to the deck instead of re-running the startup ladder, which mid-session can mis-route to the
    /// passphrase / onboarding gates.</summary>
    public void RequestLiveAcknowledge() => _pendingLive = true;

    public void OnShow()
    {
        _returnToDeck = _pendingLive;
        _pendingLive = false;

        var conn = _bootstrap.LastConnection;
        _toAcknowledge = conn?.Warnings.Where(w => !w.Seen).ToArray() ?? [];
        _submitting = false;
        _submitError = null;

        if (_toAcknowledge.Length == 0)
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

        using (var scroll = ImRaii.Child("##warnAck", new Vector2(0f, scrollH), false))
        {
            PopScrollbarStyle();

            if (!scroll.Success)
            {
                return;
            }

            ImGui.Spacing();
            ImGui.Spacing();

            var heading = _toAcknowledge.Length == 1
                ? Loc.T("common.warnings_heading_one")
                : Loc.T("common.warnings_heading_many", _toAcknowledge.Length);
            ImGui.SetCursorPosX(PadX);
            ImGui.TextColored(new Vector4(0.95f, 0.78f, 0.30f, 1f), heading);
            ImGui.Spacing();

            var dl = ImGui.GetWindowDrawList();
            ImGui.SetCursorPosX(PadX);
            var p = ImGui.GetCursorScreenPos();
            var endX = p.X + winW - PadX * 2f;
            dl.AddLine(p, new Vector2(endX, p.Y), 0x88FFBB33u, 1f);
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + Px(6f));
            ImGui.Spacing();

            ImGui.SetCursorPosX(PadX);
            ImGui.PushTextWrapPos(winW - PadX);
            ImGui.TextColored(UiColors.Body,
                Loc.T("common.warnings_body"));
            ImGui.PopTextWrapPos();
            ImGui.Spacing();
            ImGui.Spacing();

            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0f, Px(10f)));
            foreach (var w in _toAcknowledge.OrderByDescending(w => w.CreatedAtUtc))
            {
                DrawNoticeCard(winW, FontAwesomeIcon.ExclamationTriangle, UiColors.WarningAccent,
                    w.CreatedAtUtc, w.Reason, w.Seen, 16f);
            }
            ImGui.PopStyleVar();

            ImGui.Spacing();
            ImGui.SetCursorPosX(PadX);
            ImGui.PushStyleColor(ImGuiCol.Button, t.ButtonNormal);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, t.ButtonHovered);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, t.ButtonActive);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));

            var btnLabel = _submitting ? Loc.T("common.acknowledging") : Loc.T("common.i_understand");
            if (_submitting)
            {
                ImGui.BeginDisabled();
            }
            if (ImGui.Button(btnLabel, new Vector2(winW - PadX * 2f, Px(36f))))
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

    private void StartAcknowledge()
    {
        if (_submitting)
        {
            return;
        }
        _submitting = true;
        _submitError = null;

        var ids = _toAcknowledge.Select(w => w.Id).ToArray();

        _ = Task.Run(async () =>
        {
            try
            {
                await _hub.MarkWarningsSeenAsync(ids, CancellationToken.None).ConfigureAwait(false);

                MarkSeenInCachedSnapshot(ids);

                NavigateToTarget();
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "[Warnings] MarkWarningsSeenAsync failed.");
                _submitError = HubErrorText.Localize(ex);
            }
            finally
            {
                _submitting = false;
            }
        });
    }

    private void MarkSeenInCachedSnapshot(Guid[] ids)
    {
        var conn = _bootstrap.LastConnection;
        if (conn is null)
        {
            return;
        }
        for (int i = 0; i < conn.Warnings.Length; i++)
        {
            if (!conn.Warnings[i].Seen && ids.Contains(conn.Warnings[i].Id))
            {
                conn.Warnings[i] = conn.Warnings[i] with { Seen = true };
            }
        }
    }

    private void NavigateToTarget()
    {
        // A live (mid-session) warning returns to the deck — the user is already active, so re-running the
        // startup ladder would wrongly route to the passphrase / onboarding gates. A startup warning instead
        // chains on through the ladder (passphrase → news → the regular target); warnings are flipped to Seen
        // in the cached snapshot first so the resolver advances past them.
        if (_returnToDeck)
        {
            _returnToDeck = false;
            _router.Navigate(Screen.Deck);
            return;
        }

        _router.Navigate(_bootstrap.ResolveNextStartupScreen());
    }
}
