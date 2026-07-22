using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Shared;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherLove.Widgets;

/// <summary>Shown when a picked image can't be used: it doesn't decode or is smaller than the server's
/// crop target.</summary>
public sealed class ImageRequirementsModal
{
    private string _message = "";
    private string _title = "";
    private FontAwesomeIcon _icon = FontAwesomeIcon.Image;
    private bool _showSizes = true;

    public void ShowInvalid() =>
        Show(Loc.T("common.img_invalid"), Loc.T("common.img_requirements_title"), FontAwesomeIcon.Image, true);

    public void ShowTooSmall(int actualW, int actualH) =>
        Show(Loc.T("common.img_too_small", actualW, actualH), Loc.T("common.img_requirements_title"), FontAwesomeIcon.Image, true);

    /// <summary>The picked file is a cloud placeholder whose bytes aren't on disk.</summary>
    public void ShowCloudUnavailable() =>
        Show(Loc.T("common.img_cloud_unavailable"), Loc.T("common.img_cloud_title"), FontAwesomeIcon.CloudDownloadAlt, false);

    private void Show(string message, string title, FontAwesomeIcon icon, bool showSizes)
    {
        _message = message;
        _title = title;
        _icon = icon;
        _showSizes = showSizes;
        ModalHost.Instance?.Open(320f, DrawBody);
    }

    private void DrawBody(float availW)
    {
        ModalUi.Header(availW, _icon, _title, UiColors.Amber);

        ImGui.PushTextWrapPos(availW);
        ImGui.TextColored(UiColors.Body, _message);
        ImGui.PopTextWrapPos();
        ImGui.Spacing();
        if (_showSizes)
        {
            ImGui.TextColored(UiColors.Subtle,
                Loc.T("common.img_requirements_sizes",
                    PhotoSpec.AvatarSize, PhotoSpec.AvatarSize, PhotoSpec.PortraitWidth, PhotoSpec.PortraitHeight));
            ImGui.Spacing();
        }
        ImGui.Spacing();

        if (ModalUi.Button($"{Loc.T("common.close")}##imgReqClose", availW))
        {
            ModalHost.Instance?.Close();
        }
    }
}
