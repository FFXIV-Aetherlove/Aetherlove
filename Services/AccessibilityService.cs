using System;
using Dalamud.Game.Config;

namespace AetherLove.Services;

/// <summary>Wrapper around game-engine audio settings and Dalamud accessibility APIs.</summary>
public static class AccessibilityService
{
    /// <summary>False when SE or Master is muted/zero in the in-game Volume Settings.</summary>
    public static bool SoundEffectsEnabled
    {
        get
        {
            try
            {
                Plugin.GameConfig.TryGet(SystemConfigOption.IsSndMaster, out uint masterMuted);
                Plugin.GameConfig.TryGet(SystemConfigOption.IsSndSe, out uint seMuted);
                Plugin.GameConfig.TryGet(SystemConfigOption.SoundSe, out uint seVol);

                // IsSnd* uint: 0 = playing, 1 = muted.
                if (masterMuted == 1)
                {
                    return false;
                }
                if (seMuted == 1)
                {
                    return false;
                }
                if (seVol == 0)
                {
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "[AccessibilityService] Failed to read game sound config; defaulting to enabled");
                return true;
            }
        }
    }

    /// <summary>True when Dalamud's Reduce Motion accessibility setting is enabled.</summary>
    public static bool ReduceMotion =>
        Plugin.PluginInterface.UiBuilder.ShouldUseReducedMotion;
}
