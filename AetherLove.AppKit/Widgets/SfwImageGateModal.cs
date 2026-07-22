using System;
using System.Numerics;
using AetherLove.Services.Localization;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherLove.Widgets;

/// <summary>Blocking SFW acknowledgement gate before the avatar or first profile photo pick; click-outside
/// is disabled so it can't be dismissed without acknowledging.</summary>
public sealed class SfwImageGateModal
{
    private Action? _onAccept;

    public void Open(Action onAccept)
    {
        _onAccept = onAccept;
        ModalHost.Instance?.Open(380f, DrawBody, closeOnClickOutside: false);
    }

    private void DrawBody(float availW)
    {
        DrawHeader(availW);

        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.Danger);
        ImGui.TextWrapped(Loc.T("common.sfw_gate_subtitle"));
        ImGui.PopStyleColor();
        ImGui.Spacing();

        var bulletColor = UiColors.Danger with { W = 0.82f };
        DrawBullet(Loc.T("common.sfw_gate_b1"), bulletColor);
        DrawBullet(Loc.T("common.sfw_gate_b2"), bulletColor);
        DrawBullet(Loc.T("common.sfw_gate_b3"), bulletColor);
        DrawBullet(Loc.T("common.sfw_gate_b4"), bulletColor);
        DrawBullet(Loc.T("common.sfw_gate_b5"), bulletColor);
        DrawBullet(Loc.T("common.sfw_gate_b6"), bulletColor);

        ImGui.Spacing();
        DrawWarningCard(Loc.T("common.sfw_gate_race_gender"), availW);
        ImGui.Spacing();
        DrawSecondaryCallout(availW);
        ImGui.Spacing();
        ImGui.Spacing();

        if (ModalUi.Button($"{Loc.T("common.sfw_gate_ack")}##sfwGateAck", availW))
        {
            var cb = _onAccept;
            _onAccept = null;
            ModalHost.Instance?.Close();
            cb?.Invoke();
        }
    }

    private static void DrawHeader(float availW)
    {
        var danger = UiColors.Danger;

        var iconPx = Px(26f);
        var iconSz = IconDraw.Measure(FontAwesomeIcon.ExclamationTriangle, iconPx);
        var origin = ImGui.GetCursorScreenPos();
        IconDraw.Add(ImGui.GetWindowDrawList(), FontAwesomeIcon.ExclamationTriangle, iconPx,
            new Vector2(origin.X + (availW - iconSz.X) * 0.5f, origin.Y), ImGui.GetColorU32(danger));
        ImGui.Dummy(new Vector2(availW, iconSz.Y));
        ImGui.Spacing();

        using (UiFonts.H3?.Push())
        {
            var title = Loc.T("common.sfw_gate_title");
            var titleSz = ImGui.CalcTextSize(title);
            if (titleSz.X <= availW)
            {
                ImGui.SetCursorPosX((availW - titleSz.X) * 0.5f);
                ImGui.TextColored(danger, title);
            }
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Text, danger);
                ImGui.TextWrapped(title);
                ImGui.PopStyleColor();
            }
        }
        ImGui.Spacing();

        ImGui.PushStyleColor(ImGuiCol.Separator, danger with { W = 0.35f });
        ImGui.Separator();
        ImGui.PopStyleColor();
        ImGui.Spacing();
    }

    private static void DrawBullet(string text, Vector4 color)
    {
        var indent = Px(18f);
        var start = ImGui.GetCursorScreenPos();
        var lineH = ImGui.GetTextLineHeight();
        ImGui.GetWindowDrawList().AddCircleFilled(
            new Vector2(start.X + Px(6f), start.Y + lineH * 0.5f),
            Px(2.5f), ImGui.ColorConvertFloat4ToU32(color));

        ImGui.Indent(indent);
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
        ImGui.Unindent(indent);
        ImGui.Spacing();
    }

    private static void DrawSecondaryCallout(float availW)
    {
        var green = UiColors.Success;
        var dl = ImGui.GetWindowDrawList();
        var boxTL = ImGui.GetCursorScreenPos();
        var pad = Px(10f, 9f);
        var textW = availW - pad.X * 2f;

        ImGui.SetCursorScreenPos(boxTL + pad);
        ImGui.PushTextWrapPos(ImGui.GetCursorPos().X + textW);
        var preY = ImGui.GetCursorScreenPos().Y;
        ImGui.PushStyleColor(ImGuiCol.Text, green);
        ImGui.TextWrapped(Loc.T("common.sfw_gate_secondary"));
        ImGui.PopStyleColor();
        ImGui.PopTextWrapPos();
        var boxH = (ImGui.GetCursorScreenPos().Y - preY) + pad.Y * 2f;

        dl.AddRectFilled(boxTL, boxTL + new Vector2(availW, boxH),
            ImGui.ColorConvertFloat4ToU32(green with { W = 0.15f }), Px(6f));
        dl.AddRect(boxTL, boxTL + new Vector2(availW, boxH),
            ImGui.ColorConvertFloat4ToU32(green with { W = 0.75f }), Px(6f), ImDrawFlags.None, 1.5f);
        ImGui.SetCursorScreenPos(new Vector2(boxTL.X, boxTL.Y + boxH));
    }
}
