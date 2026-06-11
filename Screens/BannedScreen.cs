using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Auth;
using AetherLove.Services.Localization;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace AetherLove.Screens;

/// <summary>Terminal screen for banned accounts.</summary>
public sealed class BannedScreen
{
    private readonly SessionBootstrapper _bootstrap;

    public BannedScreen(SessionBootstrapper bootstrap)
    {
        _bootstrap = bootstrap;
    }

    public void OnShow() { }

    public void Draw()
    {
        var winW = ImGui.GetWindowSize().X;
        var scrollH = ImGui.GetContentRegionAvail().Y;
        var PadX = Px(16f);

        PushScrollbarStyle();

        using (var scroll = ImRaii.Child("##bannedScroll", new Vector2(0f, scrollH), false))
        {
            PopScrollbarStyle();

            if (!scroll.Success)
            {
                return;
            }

            ImGui.Spacing();
            ImGui.Spacing();

            ImGui.SetCursorPosX(PadX);
            ImGui.TextColored(new Vector4(0.95f, 0.40f, 0.40f, 1f), Loc.T("common.banned_title"));
            ImGui.Spacing();

            var dl = ImGui.GetWindowDrawList();
            ImGui.SetCursorPosX(PadX);
            var p = ImGui.GetCursorScreenPos();
            var endX = p.X + winW - PadX * 2f;
            dl.AddLine(p, new Vector2(endX, p.Y), 0x88FF3333u, 1f);
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + Px(6f));
            ImGui.Spacing();

            ImGui.SetCursorPosX(PadX);
            ImGui.PushTextWrapPos(winW - PadX);
            ImGui.TextColored(new Vector4(0.92f, 0.92f, 0.92f, 1f),
                Loc.T("common.banned_body"));
            ImGui.PopTextWrapPos();
            ImGui.Spacing();
            ImGui.Spacing();

            var conn = _bootstrap.LastConnection;
            var reason = conn?.BanReason;
            if (!string.IsNullOrWhiteSpace(reason))
            {
                ImGui.SetCursorPosX(PadX);
                ImGui.TextColored(new Vector4(0.55f, 0.55f, 0.55f, 1f), Loc.T("common.banned_reason_label"));
                ImGui.SetCursorPosX(PadX);
                ImGui.PushTextWrapPos(winW - PadX);
                ImGui.TextColored(new Vector4(0.88f, 0.88f, 0.88f, 1f), reason);
                ImGui.PopTextWrapPos();
                ImGui.Spacing();
                ImGui.Spacing();
            }

            var notes = conn?.ModerationNotes;
            if (!string.IsNullOrWhiteSpace(notes))
            {
                ImGui.SetCursorPosX(PadX);
                ImGui.TextColored(new Vector4(0.55f, 0.55f, 0.55f, 1f), Loc.T("common.moderator_notes_label"));
                ImGui.SetCursorPosX(PadX);
                ImGui.PushTextWrapPos(winW - PadX);
                ImGui.TextColored(new Vector4(0.80f, 0.80f, 0.80f, 1f), notes);
                ImGui.PopTextWrapPos();
                ImGui.Spacing();
                ImGui.Spacing();
            }

            ImGui.SetCursorPosX(PadX);
            ImGui.PushTextWrapPos(winW - PadX);
            ImGui.TextColored(new Vector4(0.60f, 0.60f, 0.60f, 1f),
                Loc.T("common.banned_uninstall_hint"));
            ImGui.PopTextWrapPos();
        }
    }
}
