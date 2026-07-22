using System;
using AetherLove.Services;
using Dalamud.Bindings.ImGui;

namespace AetherLove.UI;

/// <summary>Soft fade-in for content that (re)appears: <see cref="Arm"/> when it appears,
/// <see cref="BeginFrame"/> before the content, <see cref="EndFrame"/> after it in the same window.</summary>
internal sealed class EntranceAnimation
{
    private const double Duration = 0.20;

    private volatile bool _pending;
    private double _shownAt = double.MinValue;

    /// <summary>Restarts the fade on the next frame. Safe to call off the UI thread.</summary>
    public void Arm() => _pending = true;

    /// <summary>Call once before the content each frame; (re)starts the fade timer when armed.</summary>
    public void BeginFrame()
    {
        if (_pending)
        {
            _pending = false;
            _shownAt = ImGui.GetTime();
        }
    }

    /// <summary>Call once after the content, in the same window.</summary>
    public void EndFrame()
    {
        if (AccessibilityService.ReduceMotion)
        {
            return;
        }
        var t = (ImGui.GetTime() - _shownAt) / Duration;
        if (t >= 1.0)
        {
            return;
        }
        var a = (uint)(Math.Clamp(1.0 - t, 0.0, 1.0) * 255.0);
        var bg = ImGui.GetColorU32(ImGuiCol.WindowBg) & 0x00FFFFFFu;
        var col = bg | (a << 24);
        var tl = ImGui.GetWindowPos();
        ImGui.GetWindowDrawList().AddRectFilled(tl, tl + ImGui.GetWindowSize(), col);
    }
}
