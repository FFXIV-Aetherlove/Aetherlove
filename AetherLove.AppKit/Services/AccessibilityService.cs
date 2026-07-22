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
                UiHost.GameConfig.TryGet(SystemConfigOption.IsSndMaster, out uint masterMuted);
                UiHost.GameConfig.TryGet(SystemConfigOption.IsSndSe, out uint seMuted);
                UiHost.GameConfig.TryGet(SystemConfigOption.SoundSe, out uint seVol);

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
                UiHost.Log.Warning(ex, "[AccessibilityService] Failed to read game sound config; defaulting to enabled");
                return true;
            }
        }
    }

    /// <summary>Raw in-game audio switches behind <see cref="SoundEffectsEnabled"/>, for the diagnostics tool.
    /// The <c>*Muted</c> flags are 1 when muted; the volumes are 0-100.</summary>
    public static (uint MasterMuted, uint SeMuted, uint SeVolume, uint MasterVolume) ReadSoundConfig()
    {
        uint masterMuted = 0;
        uint seMuted = 0;
        uint seVolume = 0;
        uint masterVolume = 0;
        try
        {
            UiHost.GameConfig.TryGet(SystemConfigOption.IsSndMaster, out masterMuted);
            UiHost.GameConfig.TryGet(SystemConfigOption.IsSndSe, out seMuted);
            UiHost.GameConfig.TryGet(SystemConfigOption.SoundSe, out seVolume);
            UiHost.GameConfig.TryGet(SystemConfigOption.SoundMaster, out masterVolume);
        }
        catch (Exception ex)
        {
            UiHost.Log.Warning(ex, "[AccessibilityService] Failed to read game sound config.");
        }
        return (masterMuted, seMuted, seVolume, masterVolume);
    }

    public static bool ReduceMotion =>
        UiHost.PluginInterface.UiBuilder.ShouldUseReducedMotion;
}
