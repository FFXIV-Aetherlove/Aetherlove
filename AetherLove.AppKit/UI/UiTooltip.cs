using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace AetherLove.UI;

/// <summary>The phone's tooltip: ImGui's own tooltip window with room around the text, padding on every side,
/// rounded corners and a small gap between lines, so it stays legible over busy art. Use it instead of
/// <c>ImGui.SetTooltip</c> and <c>ImGui.BeginTooltip</c>. It keeps the colours already pushed, so a surface that
/// needs its own tooltip fill and ink pushes <c>ImGuiCol.PopupBg</c> and <c>ImGuiCol.Text</c> around the call.</summary>
public static class UiTooltip
{
    private const float PadX = 12f;
    private const float PadY = 9f;
    private const float Rounding = 6f;
    private const float LineGap = 4f;
    private const int StyleVars = 3;

    /// <summary>Shows a tooltip for this frame with one text item per line.</summary>
    public static void Show(params string[] lines)
    {
        using var tooltip = Begin();
        foreach (var line in lines)
        {
            ImGui.TextUnformatted(line);
        }
    }

    /// <summary>Opens a tooltip for hand-built content: draw its items, then dispose the scope, which ends the
    /// tooltip and pops its spacing.</summary>
    public static Scope Begin()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(Px(PadX), Px(PadY)));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, Px(Rounding));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(ImGui.GetStyle().ItemSpacing.X, Px(LineGap)));
        ImGui.BeginTooltip();
        return new Scope();
    }

    /// <summary>Ends the tooltip <see cref="Begin"/> opened and pops the spacing it pushed.</summary>
    public readonly struct Scope : IDisposable
    {
        public void Dispose()
        {
            ImGui.EndTooltip();
            ImGui.PopStyleVar(StyleVars);
        }
    }
}
