using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AetherLove.Services.Hub;
using AetherOS.Apps.Calendar;

namespace AetherLove.Os;

/// <summary>Host side of the calendar app: the caller's venue RSVP feed from the hub, empty on any
/// failure so the app keeps working offline with local events only.</summary>
public sealed class CalendarHostService : ICalendarHost
{
    private readonly AetherHubContext _hub;

    public CalendarHostService(AetherHubContext hub)
    {
        _hub = hub;
    }

    public async Task<IReadOnlyList<VenueVisit>> GetVenueVisitsAsync()
    {
        try
        {
            var rsvps = await _hub.GetMyVenueRsvpsAsync().ConfigureAwait(false);
            return rsvps.Select(r => new VenueVisit(r.VenueId, r.VenueName, r.StartUtc.UtcDateTime)).ToArray();
        }
        catch (Exception)
        {
            return Array.Empty<VenueVisit>();
        }
    }
}
