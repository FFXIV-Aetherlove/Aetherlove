using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherLove.UI;

/// <summary>Draws FontAwesome icons from the large <see cref="UiFonts.Icon"/> atlas so big icons downscale
/// and stay sharp (Dalamud's default icon font is only ~17px); falls back to it until the atlas is ready.</summary>
internal static class IconDraw
{
    public static void Add(ImDrawListPtr dl, FontAwesomeIcon icon, float px, Vector2 pos, uint col)
    {
        var glyph = icon.ToIconString();
        if (UiFonts.Icon is { Available: true } handle)
        {
            using (handle.Push())
            {
                dl.AddText(ImGui.GetFont(), px, pos, col, glyph);
            }
            return;
        }
        ImGui.PushFont(Plugin.PluginInterface.UiBuilder.FontIcon);
        dl.AddText(ImGui.GetFont(), px, pos, col, glyph);
        ImGui.PopFont();
    }

    public static void AddCentered(ImDrawListPtr dl, FontAwesomeIcon icon, float px, Vector2 center, uint col)
    {
        var glyph = icon.ToIconString();
        if (UiFonts.Icon is { Available: true } handle)
        {
            using (handle.Push())
            {
                var sz = ImGui.CalcTextSize(glyph) * (px / ImGui.GetFontSize());
                dl.AddText(ImGui.GetFont(), px, center - sz * 0.5f, col, glyph);
            }
            return;
        }
        ImGui.PushFont(Plugin.PluginInterface.UiBuilder.FontIcon);
        var fsz = ImGui.CalcTextSize(glyph) * (px / ImGui.GetFontSize());
        dl.AddText(ImGui.GetFont(), px, center - fsz * 0.5f, col, glyph);
        ImGui.PopFont();
    }

    public static Vector2 Measure(FontAwesomeIcon icon, float px)
    {
        var glyph = icon.ToIconString();
        if (UiFonts.Icon is { Available: true } handle)
        {
            using (handle.Push())
            {
                return ImGui.CalcTextSize(glyph) * (px / ImGui.GetFontSize());
            }
        }
        ImGui.PushFont(Plugin.PluginInterface.UiBuilder.FontIcon);
        var s = ImGui.CalcTextSize(glyph) * (px / ImGui.GetFontSize());
        ImGui.PopFont();
        return s;
    }
}
