using System;
using System.Numerics;
using AetherLove.Services;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;

namespace AetherLove.Widgets;

/// <summary>Re-usable confirmation dialog. Routes through the shared <see cref="ModalHost"/>.</summary>
public class ConfirmModal
{
    public void Open(string title, string body,
                     string confirmLabel, string cancelLabel,
                     Action onConfirm)
    {
        ModalHost.Instance?.Open(280f, w => DrawBody(title, body, confirmLabel, cancelLabel, onConfirm, w));
    }

    private static void DrawBody(string title, string body,
                                 string confirmLabel, string cancelLabel,
                                 Action onConfirm, float availW)
    {
        var t = ThemeService.Current;

        ModalUi.Header(availW, title, t.AccentLight);

        ImGui.TextColored(UiColors.Body, body);
        ImGui.Spacing();
        ImGui.Spacing();

        var btnGap = Px(8f);
        var btnW = (availW - btnGap) * 0.5f;

        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.22f, 0.22f, 0.22f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.38f, 0.38f, 0.38f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.14f, 0.14f, 0.14f, 1f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
        if (ImGui.Button(cancelLabel + "##modalCancel", new Vector2(btnW, Px(32f))))
        {
            ModalHost.Instance?.Close();
        }
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);

        ImGui.SameLine(0f, btnGap);

        if (ModalUi.Button(confirmLabel + "##modalConfirm", btnW))
        {
            ModalHost.Instance?.Close();
            onConfirm();
        }
    }
}
