using System;

namespace AetherLove.Services;

/// <summary>Helpers for re-basing time-of-day data between timezones for display.</summary>
public static class TimeZoneShift
{
    /// <summary>
    /// Re-bases a 24-bit hours-of-day mask from the source's local timezone into the viewer's
    /// local timezone. Bit <c>h</c> (0–23) set means "active during hour h" in the source's local
    /// time. Each active hour is shifted by the offset delta and wraps around midnight, so an
    /// 23:00 activity for someone two hours ahead of the viewer correctly lands at 01:00.
    /// </summary>
    /// <param name="hoursMask">The source's 24-bit activity mask (in their local time).</param>
    /// <param name="sourceOffsetMinutes">The source's UTC offset, in minutes.</param>
    /// <param name="viewerOffsetMinutes">The viewer's UTC offset, in minutes.</param>
    public static int ShiftHoursMask(int hoursMask, int sourceOffsetMinutes, int viewerOffsetMinutes)
    {
        if (hoursMask == 0)
        {
            return 0;
        }

        // Fractional (e.g. :30/:45) offsets can't map onto whole-hour bits, so round to the
        // nearest hour. Normalise into [0, 24) so the modulo below never sees a negative.
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
