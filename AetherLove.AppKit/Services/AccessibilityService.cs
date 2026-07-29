namespace AetherLove.Services;

/// <summary>Wrapper around Dalamud accessibility APIs.</summary>
public static class AccessibilityService
{
    public static bool ReduceMotion =>
        UiHost.PluginInterface.UiBuilder.ShouldUseReducedMotion;
}
