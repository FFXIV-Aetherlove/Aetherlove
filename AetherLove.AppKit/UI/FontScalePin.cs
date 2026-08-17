using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace AetherLove.UI;

/// <summary>Pins ImGui's global font scale to 1 while the phone draws, and puts it back afterwards without
/// leaving every plugin drawn after us rendering at the pinned size.
///
/// The phone sizes everything itself, so Dalamud's global font scale would multiply each glyph a second time
/// and overflow a fixed-size window. Pinning is the cure; restoring is where it went wrong. ImGui never reads
/// the field while drawing text: it folds the value into a derived base size that only NewFrame and a font
/// change recompute. Writing the field back therefore restored the setting and left the size, so the next
/// plugin that did not push its own font inherited ours. Measured at a 1.5 setting: 17.33 px where 26 was
/// correct, on every frame.
///
/// At a scale of exactly 1 every write here is 1 over 1, which is why this was invisible to most people and
/// why both calls do nothing at all in that case.</summary>
public static class FontScalePin
{
    private const ImGuiWindowFlags RecomputeFlags =
        ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoBackground
        | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav
        | ImGuiWindowFlags.NoMouseInputs;

    /// <summary>Pins the scale to 1 and returns the value to hand back to <see cref="Restore"/>.</summary>
    public static float Pin()
    {
        var io = ImGui.GetIO();
        var saved = io.FontGlobalScale;
        Apply(1f);
        return saved;
    }

    /// <summary>Restores the scale, and the size ImGui actually draws at.</summary>
    public static void Restore(float saved)
    {
        Apply(saved);
    }

    private static void Apply(float scale)
    {
        var io = ImGui.GetIO();
        if (io.FontGlobalScale == scale)
        {
            return;
        }
        io.FontGlobalScale = scale;

        // A font change is the only thing that recomputes the size text comes out at, and doing it inside a
        // window is what keeps the per-window size right as well: outside one ImGui zeroes that, which would
        // blank any plugin that writes text to a draw list without opening a window of its own.
        ImGui.SetNextWindowPos(new Vector2(-8000f, -8000f), ImGuiCond.Always);
        ImGui.SetNextWindowSize(Vector2.One, ImGuiCond.Always);
        ImGui.Begin("##aetherloveFontScale", RecomputeFlags);
        ImGui.PushFont(ImGui.GetFont());
        ImGui.PopFont();
        ImGui.End();
    }
}
