using AetherLove.Services.Localization;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherLove.UI;

internal static class PluginUiHelpers
{
    internal static void CopyTextWithLinkWarning(string text)
    {
        ImGui.SetClipboardText(text ?? string.Empty);
        if (!UiHost.Configuration.AcknowledgedProfileCopyTextWarning)
        {
            Widgets.ModalHost.Instance?.Open(320f, DrawCopyTextWarningBody);
        }
    }

    private static void DrawCopyTextWarningBody(float availW)
    {
        Widgets.ModalUi.Header(availW, FontAwesomeIcon.ExclamationTriangle,
            Loc.T("profile.copy_warning_title"), UiColors.Amber);

        ImGui.TextColored(UiColors.Body, Loc.T("profile.copy_warning_body"));
        ImGui.Spacing();
        ImGui.Spacing();

        if (Widgets.ModalUi.Button($"{Loc.T("profile.copy_warning_agree")}##copyWarnAgree", availW))
        {
            UiHost.Configuration.AcknowledgedProfileCopyTextWarning = true;
            UiHost.Configuration.Save();
            Widgets.ModalHost.Instance?.Close();
        }
    }
}
