using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Shared;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherLove.Widgets;

/// <summary>Shown when a picked image can't be used — it doesn't decode, or it's smaller than the size the
/// server crops to. Always spells out the minimum sizes. Routes through the shared <see cref="ModalHost"/>.</summary>
public sealed class ImageRequirementsModal
{
    private string _message = "";

    public void ShowInvalid() => Show(Loc.T("common.img_invalid"));

    public void ShowTooSmall(int actualW, int actualH) =>
        Show(Loc.T("common.img_too_small", actualW, actualH));

    private void Show(string message)
    {
        _message = message;
        ModalHost.Instance?.Open(320f, DrawBody);
    }

    private void DrawBody(float availW)
    {
        ModalUi.Header(availW, FontAwesomeIcon.Image, Loc.T("common.img_requirements_title"), UiColors.Amber);

        ImGui.TextColored(UiColors.Body, _message);
        ImGui.Spacing();
        ImGui.TextColored(UiColors.Subtle,
            Loc.T("common.img_requirements_sizes",
                PhotoSpec.AvatarSize, PhotoSpec.AvatarSize, PhotoSpec.PortraitWidth, PhotoSpec.PortraitHeight));
        ImGui.Spacing();
        ImGui.Spacing();

        if (ModalUi.Button($"{Loc.T("common.close")}##imgReqClose", availW))
        {
            ModalHost.Instance?.Close();
        }
    }
}
