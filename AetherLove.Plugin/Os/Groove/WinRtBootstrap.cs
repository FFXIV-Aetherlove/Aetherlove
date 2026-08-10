using System;
using System.Reflection;

namespace AetherLove.Os.Groove;

/// <summary>CsWinRT lazily hands its ComWrappers to the runtime's tracker slot the first time anything is
/// marshalled, and that slot is process-global and write-once. Dalamud reloads the plugin into a fresh ALC
/// carrying a fresh CsWinRT whose own slot looks unset, so every load after the first threw "Attempt to
/// update previously set global instance" and left Groove permanently empty. Seeding the backing field
/// skips that registration entirely; it exists for XAML reference tracking, which nothing here uses.</summary>
internal static class WinRtBootstrap
{
    private static bool _done;

    public static void EnsureComWrappers()
    {
        if (_done)
        {
            return;
        }
        _done = true;
        try
        {
            var support = Type.GetType("WinRT.ComWrappersSupport, WinRT.Runtime");
            var field = support?.GetField("_comWrappers", BindingFlags.Static | BindingFlags.NonPublic);
            if (field is null || field.GetValue(null) is not null)
            {
                return;
            }
            var defaults = Type.GetType("WinRT.DefaultComWrappers, WinRT.Runtime");
            if (defaults is null)
            {
                return;
            }
            field.SetValue(null, Activator.CreateInstance(defaults, nonPublic: true));
        }
        catch (Exception ex)
        {
            UiHost.Log.Warning(ex, "[Groove] Could not pre-seed CsWinRT ComWrappers; media may only work until the first plugin reload.");
        }
    }
}
