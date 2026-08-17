using System;

namespace AetherOS.Apps.Timers;

/// <summary>Notification tags shared by the app (dismiss-on-open) and the plugin reminder service (post),
/// so opening the app clears every timers notification from the OS surfaces. Schedule tags are stable per
/// kind, so a later occurrence replaces the previous notification rather than stacking.</summary>
public static class TimersTags
{
    public const string Prefix = "timers:";

    public static string ForKind(ReminderKind kind) => Prefix + (int)kind;

    public static string ForCustom(Guid id) => $"{Prefix}custom:{id:N}";

    public static string ForVenture(ulong contentId) => $"{Prefix}venture:{contentId:X}";

    public static string ForFleet(ulong contentId) => $"{Prefix}fleet:{contentId:X}";
}
