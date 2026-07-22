using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AetherOS.Apps.Calendar;

public sealed record VenueVisit(Guid VenueId, string VenueName, DateTime StartUtc);

/// <summary>Host bridge: the caller's venue RSVPs (past and upcoming) from the server. Returns an empty
/// list when offline or the feature is unavailable, so the calendar keeps working with local events.</summary>
public interface ICalendarHost
{
    Task<IReadOnlyList<VenueVisit>> GetVenueVisitsAsync();
}
