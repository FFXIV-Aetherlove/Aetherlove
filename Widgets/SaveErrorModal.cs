using System;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherLove.Widgets;

/// <summary>Shown when a server save fails so the user isn't left staring at a small red error with no way
/// out. Names the error and offers a Discord report link. Routes through the shared <see cref="ModalHost"/>.</summary>
public sealed class SaveErrorModal
{
    private const string DiscordInvite = "https://discord.gg/SkyQmpxWhB";

    private string _message = "";

    /// <summary>Opens the modal with an error message. Safe to call from a background task — it only sets
    /// fields and flips the host's <c>IsOpen</c>; no ImGui calls happen until the next UI frame.</summary>
    public void Show(string? message)
    {
        _message = string.IsNullOrWhiteSpace(message) ? Loc.T("common.save_error_unknown") : message.Trim();
        ModalHost.Instance?.Open(320f, DrawBody);
    }

    private void DrawBody(float availW)
    {
        ModalUi.Header(availW, FontAwesomeIcon.ExclamationTriangle, Loc.T("common.save_error_title"), ModalUi.Danger);

        ImGui.TextColored(ModalUi.Body, Loc.T("common.save_error_intro"));
        ImGui.Spacing();
        ImGui.TextColored(ModalUi.Subtle, _message);
        ImGui.Spacing();
        ImGui.TextColored(ModalUi.Body, Loc.T("common.save_error_report"));
        ImGui.Spacing();
        ImGui.Spacing();

        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.345f, 0.396f, 0.949f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.42f, 0.47f, 1.0f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.28f, 0.32f, 0.80f, 1f));
        if (ImGui.Button("Discord##saveErrDiscord", new Vector2(availW, Px(32f))))
        {
            OpenDiscord();
        }
        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar();

        ImGui.Spacing();

        if (ModalUi.Button($"{Loc.T("common.close")}##saveErrClose", availW))
        {
            ModalHost.Instance?.Close();
        }
    }

    private static void OpenDiscord()
    {
        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(DiscordInvite) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[SaveErrorModal] Failed to open Discord invite.");
        }
    }
}
