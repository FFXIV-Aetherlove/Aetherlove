using System;

namespace AetherLove.Services;

/// <summary>Helpers for re-basing time-of-day data between timezones for display.</summary>
public static class TimeZoneShift
{
    /// <summary>Re-bases a 24-bit hours-of-day mask from the source's local timezone into the viewer's.
    /// Bit <c>h</c> (0–23) set means "active during hour h" in the source's local time.</summary>
    public static int ShiftHoursMask(int hoursMask, int sourceOffsetMinutes, int viewerOffsetMinutes)
    {
        if (hoursMask == 0)
        {
            return 0;
        }

        // Round fractional offsets to whole hours, then normalise into [0, 24) so the modulo never sees a negative.
        var deltaHours = (int)Math.Round((viewerOffsetMinutes - sourceOffsetMinutes) / 60.0);
        deltaHours = ((deltaHours % 24) + 24) % 24;
        if (deltaHours == 0)
        {
            return hoursMask;
        }

        var result = 0;
        for (var h = 0; h < 24; h++)
        {
            if ((hoursMask & (1 << h)) == 0)
            {
                continue;
            }
            var shifted = (h + deltaHours) % 24;
            result |= 1 << shifted;
        }
        return result;
    }
}
