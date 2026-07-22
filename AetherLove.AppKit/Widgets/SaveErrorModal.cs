using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherLove.Widgets;

/// <summary>Shown when a server save fails; names the error and offers a Discord report link.</summary>
public sealed class SaveErrorModal
{
    private string _message = "";

    /// <summary>Safe to call from a background task; no ImGui calls happen until the next UI frame.</summary>
    public void Show(string? message)
    {
        _message = string.IsNullOrWhiteSpace(message) ? Loc.T("common.save_error_unknown") : message.Trim();
        ModalHost.Instance?.Open(320f, DrawBody);
    }

    private void DrawBody(float availW)
    {
        ModalUi.Header(availW, FontAwesomeIcon.ExclamationTriangle, Loc.T("common.save_error_title"), UiColors.Danger);

        ImGui.TextColored(UiColors.Body, Loc.T("common.save_error_intro"));
        ImGui.Spacing();
        ImGui.TextColored(UiColors.Subtle, _message);
        ImGui.Spacing();
        ImGui.TextColored(UiColors.Body, Loc.T("common.save_error_report"));
        ImGui.Spacing();
        ImGui.Spacing();

        DrawDiscordButton("Discord##saveErrDiscord", new Vector2(availW, Px(32f)));

        ImGui.Spacing();

        if (ModalUi.Button($"{Loc.T("common.close")}##saveErrClose", availW))
        {
            ModalHost.Instance?.Close();
        }
    }
}
