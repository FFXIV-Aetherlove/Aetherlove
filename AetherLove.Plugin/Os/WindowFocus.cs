using System;
using System.Runtime.InteropServices;

namespace AetherLove.Os;

/// <summary>Whether the game window is the one the player is actually looking at. Asked of the OS, because
/// nothing in the client's own state says it, and the game's "play sounds when window is not active" options
/// only govern the game's own mixer, not audio a plugin plays itself.</summary>
internal static class WindowFocus
{
    public static bool GameHasFocus()
    {
        try
        {
            var foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero)
            {
                return false;
            }
            _ = GetWindowThreadProcessId(foreground, out var pid);
            return pid == (uint)Environment.ProcessId;
        }
        catch (Exception)
        {
            // No answer means assume focused: silence is the worse guess to make wrongly.
            return true;
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}
