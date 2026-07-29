using System.Numerics;
using AetherLove;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Market;

/// <summary>The shared sub-screen header: back pill plus a vertically centred title on one row, with the
/// row geometry exposed so right-side actions can align to it. The layout cursor ends below the row.</summary>
internal static class MarketHeader
{
    public static bool Draw(string title, out float rowTop, out float pillH)
    {
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.SetCursorPosX(Px(16f));
        var pos = ImGui.GetCursorScreenPos();
        rowTop = pos.Y;

        ImGui.PushFont(UiHost.PluginInterface.UiBuilder.FontIcon);
        var iconH = ImGui.CalcTextSize(FontAwesomeIcon.ArrowLeft.ToIconString()).Y;
        ImGui.PopFont();
        pillH = iconH + Px(14f);

        var back = DrawFloatingBackPill(pos, Loc.T("settings.back_arrow"), FontAwesomeIcon.Bell);
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
