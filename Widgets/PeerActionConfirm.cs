using System;
using System.Numerics;
using AetherLove.Services.Localization;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherLove.Widgets;

public enum PeerAction
{
    Unmatch,
    Block,
}

/// <summary>In-page confirm for unmatching or blocking a peer; the host owns the hub call via the
/// <c>onConfirm</c> callback.</summary>
public sealed class PeerActionConfirm
{
    private bool _open;
    private PeerAction _action;
    private Guid _peerId;
    private float _panelH;

    public bool IsOpen => _open;

    public void Open(PeerAction action, Guid peerId)
    {
        _open = true;
        _action = action;
        _peerId = peerId;
        _panelH = 0f;
    }

    public void Close() => _open = false;

    public void Draw(Vector2 winPos, Vector2 winSize, Action<PeerAction, Guid> onConfirm)
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

        var confirmed = false;
        var dismissed = DrawPageOverlayPanel("peerAction", winPos, winSize, ref _panelH, Px(190f), innerW =>
        {
            var (icon, title, body, confirmLabel) = _action == PeerAction.Unmatch
                ? (FontAwesomeIcon.Unlink, Loc.T("chat.unmatch_title"), Loc.T("chat.unmatch_body"), Loc.T("chat.unmatch_confirm"))
                : (FontAwesomeIcon.Ban, Loc.T("chat.block_title"), Loc.T("chat.block_body"), Loc.T("chat.block_confirm"));

            ModalUi.Header(innerW, icon, title, UiColors.Danger);

            ImGui.PushTextWrapPos(innerW);
            ImGui.TextColored(UiColors.Body, body);
            ImGui.PopTextWrapPos();
            ImGui.Spacing();
            ImGui.Spacing();

            var btnW = (innerW - Px(10f)) * 0.5f;
            PushDangerButton();
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
            if (ImGui.Button($"{confirmLabel}##peerActOk", new Vector2(btnW, Px(32f))))
            {
                confirmed = true;
            }
            ImGui.PopStyleVar();
            ImGui.PopStyleColor(3);
            ImGui.SameLine(0f, Px(10f));
            if (ModalUi.Button($"{Loc.T("common.cancel")}##peerActCancel", btnW))
            {
                _open = false;
            }
        });

        if (confirmed)
        {
            var action = _action;
            var peer = _peerId;
            _open = false;
            onConfirm(action, peer);
        }
        else if (dismissed)
        {
            _open = false;
        }
    }
}
