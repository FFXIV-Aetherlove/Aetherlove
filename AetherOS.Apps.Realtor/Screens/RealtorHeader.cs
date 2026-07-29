using System.Numerics;
using AetherLove;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Realtor;

/// <summary>The shared sub-screen header: back pill plus a vertically centred title on one row.
/// The layout cursor ends below the row.</summary>
internal static class RealtorHeader
{
    public static bool Draw(string title)
    {
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.SetCursorPosX(Px(16f));
        var pos = ImGui.GetCursorScreenPos();
        var rowTop = pos.Y;

        ImGui.PushFont(UiHost.PluginInterface.UiBuilder.FontIcon);
        var iconH = ImGui.CalcTextSize(FontAwesomeIcon.ArrowLeft.ToIconString()).Y;
        ImGui.PopFont();
        var pillH = iconH + Px(14f);

        var back = DrawFloatingBackPill(pos, Loc.T("settings.back_arrow"), FontAwesomeIcon.Home);
        var pillRight = ImGui.GetItemRectMax().X;

        if (title.Length > 0)
        {
            var dl = ImGui.GetWindowDrawList();
            using (UiFonts.H3?.Push())
            {
                var fitted = TruncateToWidth(title, ImGui.GetWindowSize().X - (pillRight - ImGui.GetWindowPos().X) - Px(70f));
                var sz = ImGui.CalcTextSize(fitted);
                dl.AddText(new Vector2(pillRight + Px(10f), rowTop + (pillH - sz.Y) * 0.5f),
                    ImGui.GetColorU32(ThemeService.Current.AccentLight), fitted);
            }
        }

        ImGui.SetCursorScreenPos(new Vector2(pos.X, rowTop + pillH + Px(6f)));
        return back;
    }
}
