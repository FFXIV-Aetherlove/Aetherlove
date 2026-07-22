using System;
using System.Globalization;

namespace AetherLove.UI;

/// <summary>Wall-clock formatting honouring the user's 12/24-hour preference. Both forms are digit-only (no
/// AM/PM) so they render with the digits-only baked Clock font; 12-hour just drops the 24h hour (22:23 -> 10:23).
/// Invariant culture keeps the glyphs ASCII (some locales substitute non-Latin digits the font lacks).</summary>
public static class OsClock
{
    public static string Format(DateTime time, bool use24Hour) =>
        time.ToString(use24Hour ? "HH:mm" : "h:mm", CultureInfo.InvariantCulture);

    public static string Format(DateTime time) =>
        Format(time, UiHost.Configuration.OsSettings.Use24HourClock);
}
