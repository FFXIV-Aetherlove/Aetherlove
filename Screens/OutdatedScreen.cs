using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherLove.Screens;

/// <summary>Terminal screen shown when the server rejects this plugin's API version.</summary>
public sealed class OutdatedScreen
{
    public void OnShow() { }

    public void Draw()
    {
        var winW = ImGui.GetWindowSize().X;
        var scrollH = ImGui.GetContentRegionAvail().Y;
        var PadX = Px(16f);

        PushScrollbarStyle();

        using (var scroll = ImRaii.Child("##outdatedScroll", new Vector2(0f, scrollH), false))
        {
            PopScrollbarStyle();

            if (!scroll.Success)
            {
                return;
            }

            ImGui.Spacing();
            ImGui.Spacing();

            var iconPx = Px(40f);
            var iconSz = IconDraw.Measure(FontAwesomeIcon.CloudDownloadAlt, iconPx);
            var iconOrigin = ImGui.GetCursorScreenPos();
            IconDraw.Add(ImGui.GetWindowDrawList(), FontAwesomeIcon.CloudDownloadAlt, iconPx,
                new Vector2(iconOrigin.X + (winW - iconSz.X) * 0.5f, iconOrigin.Y), ImGui.GetColorU32(UiColors.Amber));
            ImGui.Dummy(new Vector2(winW, iconSz.Y));
            ImGui.Spacing();

            using (UiFonts.H2?.Push())
            {
                var title = Loc.T("common.outdated_title");
                var titleSz = ImGui.CalcTextSize(title);
                ImGui.SetCursorPosX((winW - titleSz.X) * 0.5f);
                ImGui.TextColored(UiColors.Amber, title);
            }
            ImGui.Spacing();

            var dl = ImGui.GetWindowDrawList();
            ImGui.SetCursorPosX(PadX);
            var p = ImGui.GetCursorScreenPos();
            var endX = p.X + winW - PadX * 2f;
            dl.AddLine(p, new Vector2(endX, p.Y), 0x88FFA526u, 1f);
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + Px(6f));
            ImGui.Spacing();

            ImGui.SetCursorPosX(PadX);
            ImGui.PushTextWrapPos(winW - PadX);
            ImGui.TextColored(new Vector4(0.92f, 0.92f, 0.92f, 1f), Loc.T("common.outdated_body"));
            ImGui.PopTextWrapPos();
            ImGui.Spacing();
            ImGui.Spacing();

            ImGui.SetCursorPosX(PadX);
            ImGui.PushTextWrapPos(winW - PadX);
            ImGui.TextColored(new Vector4(0.60f, 0.60f, 0.60f, 1f), Loc.T("common.outdated_hint"));
            ImGui.PopTextWrapPos();
        }
    }
}
