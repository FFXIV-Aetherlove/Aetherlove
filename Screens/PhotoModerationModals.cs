using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;

namespace AetherLove.Screens;

internal enum PhotoNsfwDecl
{
    Unselected = 0,
    Sfw = 1,
    Nsfw = 2,
}

internal static class PhotoModerationLabels
{
    public static string[] NsfwDeclOptions =>
    [
        Loc.T("common.nsfw_decl_unselected"),
        Loc.T("common.nsfw_decl_sfw"),
        Loc.T("common.nsfw_decl_nsfw"),
    ];
}

/// <summary>Shared photo-moderation modals used by onboarding and My Profile. Each caller owns its
/// `pending` flag; when it goes true the modal opens via the shared <see cref="Widgets.ModalHost"/>.</summary>
internal static class PhotoModerationModals
{
    public static void DrawLalafellNsfwModal(ref bool pending)
    {
        if (pending)
        {
            pending = false;
            Widgets.ModalHost.Instance?.Open(360f, DrawLalafellBody);
        }
    }

    private static void DrawLalafellBody(float availW)
    {
        using (UiFonts.H3?.Push())
        {
            var Title = Loc.T("common.lalafell_nsfw_title");
            var titleSz = ImGui.CalcTextSize(Title);
            ImGui.SetCursorPosX((availW - titleSz.X) * 0.5f);
            ImGui.TextColored(new Vector4(0.95f, 0.55f, 0.30f, 1f), Title);
        }

        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.Separator, new Vector4(0.95f, 0.55f, 0.30f, 0.35f));
        ImGui.Separator();
        ImGui.PopStyleColor();
        ImGui.Spacing();

        ImGui.TextColored(new Vector4(0.88f, 0.88f, 0.88f, 1f), Loc.T("common.lalafell_nsfw_body"));
        ImGui.Spacing();
        ImGui.Spacing();

        var t = ThemeService.Current;
        ImGui.PushStyleColor(ImGuiCol.Button, t.ButtonNormal);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, t.ButtonHovered);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, t.ButtonActive);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
        if (ImGui.Button(Loc.T("common.i_understand"), new Vector2(availW, Px(32f))))
        {
            Widgets.ModalHost.Instance?.Close();
        }
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);
    }

    public static void DrawUndeclaredModal(ref bool pending)
    {
        if (pending)
        {
            pending = false;
            Widgets.ModalHost.Instance?.Open(360f, DrawUndeclaredBody);
        }
    }

    private static void DrawUndeclaredBody(float availW)
    {
        using (UiFonts.H3?.Push())
        {
            var Title = Loc.T("common.undeclared_photo_title");
            var titleSz = ImGui.CalcTextSize(Title);
            ImGui.SetCursorPosX((availW - titleSz.X) * 0.5f);
            ImGui.TextColored(new Vector4(0.95f, 0.55f, 0.30f, 1f), Title);
        }

        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.Separator, new Vector4(0.95f, 0.55f, 0.30f, 0.35f));
        ImGui.Separator();
        ImGui.PopStyleColor();
        ImGui.Spacing();

        ImGui.TextColored(new Vector4(0.88f, 0.88f, 0.88f, 1f), Loc.T("common.undeclared_photo_body"));
        ImGui.Spacing();
        ImGui.Spacing();

        var t = ThemeService.Current;
        ImGui.PushStyleColor(ImGuiCol.Button, t.ButtonNormal);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, t.ButtonHovered);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, t.ButtonActive);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
        if (ImGui.Button(Loc.T("common.ok"), new Vector2(availW, Px(32f))))
        {
            Widgets.ModalHost.Instance?.Close();
        }
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);
    }
}
