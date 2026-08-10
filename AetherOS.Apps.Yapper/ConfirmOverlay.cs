using System;
using System.Numerics;
using AetherLove.Services.Localization;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Yapper;

/// <summary>The in-phone "are you sure" for the actions that are awkward to undo. Drawn last by the app so it
/// layers over whatever screen opened it, like the report dialog.</summary>
internal sealed class ConfirmOverlay
{
    private bool _open;
    private string _title = string.Empty;
    private string _body = string.Empty;
    private string _confirmLabel = string.Empty;
    private bool _danger;
    private Action? _onConfirm;

    public void Open(string title, string body, string confirmLabel, bool danger, Action onConfirm)
    {
        _title = title;
        _body = body;
        _confirmLabel = confirmLabel;
        _danger = danger;
        _onConfirm = onConfirm;
        _open = true;
    }

    public void Draw(OsAppContext ctx)
    {
        if (!_open)
        {
            return;
        }
        if (ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            Close();
            return;
        }

        var origin = ImGui.GetWindowPos();
        var avail = ImGui.GetWindowSize();
        ImGui.SetCursorScreenPos(origin);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        using var layer = ImRaii.Child("##yapConfirmOverlay", avail, false,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoBackground);
        ImGui.PopStyleVar();
        if (!layer)
        {
            return;
        }
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(origin, origin + avail, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.72f)));

        var pad = Px(14f);
        var panelW = MathF.Min(avail.X - Px(28f), Px(300f));
        var innerW = panelW - pad * 2f;
        ImGui.PushTextWrapPos(0f);
        var bodyH = ImGui.CalcTextSize(_body, false, innerW).Y;
        ImGui.PopTextWrapPos();
        var panelH = Px(46f) + bodyH + Px(80f);
        var panelPos = origin + (avail - new Vector2(panelW, panelH)) * 0.5f;

        ImGui.SetCursorScreenPos(panelPos);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.11f, 0.10f, 0.13f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.32f, 0.30f, 0.38f, 0.65f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, Px(12f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(pad, pad));
        using (var panel = ImRaii.Child("##yapConfirmPanel", new Vector2(panelW, panelH), true,
                   ImGuiWindowFlags.AlwaysUseWindowPadding | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            if (panel)
            {
                DrawPanel(ctx, innerW);
            }
        }
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(2);

        // Scrim click cancels; submitted last so the panel's buttons win input.
        ImGui.SetCursorScreenPos(origin);
        if (ImGui.InvisibleButton("##yapConfirmScrim", avail)
            && !ImGui.IsMouseHoveringRect(panelPos, panelPos + new Vector2(panelW, panelH)))
        {
            Close();
        }
    }

    private void DrawPanel(OsAppContext ctx, float innerW)
    {
        var accent = _danger ? new Vector4(0.95f, 0.45f, 0.40f, 1f) : ctx.Theme.Accent;
        var dl = ImGui.GetWindowDrawList();
        IconDraw.AddCentered(dl, _danger ? FontAwesomeIcon.Ban : FontAwesomeIcon.VolumeMute, Px(13f),
            ImGui.GetCursorScreenPos() + new Vector2(Px(8f), Px(9f)), ImGui.GetColorU32(accent));
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Px(22f));
        ImGui.TextUnformatted(_title);
        ImGui.Dummy(new Vector2(0f, Px(6f)));

        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + innerW);
        ImGui.TextColored(new Vector4(1f, 1f, 1f, 0.65f), _body);
        ImGui.PopTextWrapPos();
        ImGui.Dummy(new Vector2(0f, Px(10f)));

        var half = (innerW - Px(8f)) * 0.5f;
        if (Button($"{Loc.T("os.yapper_confirm_cancel")}##yapConfirmNo", new Vector2(half, Px(30f))))
        {
            Close();
        }
        ImGui.SameLine(0f, Px(8f));
        ImGui.PushStyleColor(ImGuiCol.Button, accent with { W = 0.30f });
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, accent with { W = 0.48f });
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, accent with { W = 0.62f });
        var confirmed = Button($"{_confirmLabel}##yapConfirmYes", new Vector2(half, Px(30f)));
        ImGui.PopStyleColor(3);
        if (confirmed)
        {
            var action = _onConfirm;
            Close();
            action?.Invoke();
        }
    }

    private void Close()
    {
        _open = false;
        _onConfirm = null;
    }
}
