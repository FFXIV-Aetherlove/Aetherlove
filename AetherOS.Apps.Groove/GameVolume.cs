using System;
using AetherLove;
using Dalamud.Game.Config;

namespace AetherOS.Apps.Groove;

/// <summary>The game's own master and background-music levels plus their mute flags, read and written
/// through the game config the same way the in-game options window does. Values are 0..100; every access is
/// guarded so a config shape change can never take the phone down with it. Public because the plugin's
/// auto-mute service drives the same flags from its framework tick.</summary>
public static class GameVolume
{
    public static bool TryGet(SystemConfigOption option, out float value01)
    {
        value01 = 0f;
        try
        {
            if (!UiHost.GameConfig.TryGet(option, out uint raw))
            {
                return false;
            }
            value01 = Math.Clamp(raw / 100f, 0f, 1f);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static void Set(SystemConfigOption option, float value01)
    {
        try
        {
            var raw = (uint)Math.Clamp(MathF.Round(value01 * 100f), 0f, 100f);
            UiHost.GameConfig.Set(option, raw);
        }
        catch (Exception)
        {
        }
    }

    /// <summary>Reads one of the IsSnd* flags, the same checkboxes the game's sound options expose.</summary>
    public static bool TryGetMuted(SystemConfigOption option, out bool muted)
    {
        muted = false;
        try
        {
            return UiHost.GameConfig.TryGet(option, out muted);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static void SetMuted(SystemConfigOption option, bool muted)
    {
        try
        {
            UiHost.GameConfig.Set(option, muted);
        }
        catch (Exception)
        {
        }
    }
}
