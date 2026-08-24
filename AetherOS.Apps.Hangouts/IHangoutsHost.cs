using System;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Shared.Hangouts;

namespace AetherOS.Apps.Hangouts;

/// <summary>Host bridge into the plugin: hangout hub passthroughs and the chat-card deep link. Sharing goes
/// through the OS share sheet; match data and chat/profile navigation come from the shared social bridge.</summary>
public interface IHangoutsHost
{
    Task<HangoutDirectoryPageDto> GetHangoutDirectoryAsync(HangoutDirectoryFilterDto filter, int skip, CancellationToken ct = default);
    Task<HangoutCardDto> GetHangoutCardAsync(Guid hangoutId, CancellationToken ct = default);

    Task<HangoutRsvpResultDto> SetHangoutRsvpAsync(Guid hangoutId, bool going, CancellationToken ct = default);
    Task<Guid> ReportHangoutAsync(ReportHangoutRequest req, CancellationToken ct = default);
    Task<HangoutSummaryDto> CreateHangoutAsync(CreateHangoutRequest req, CancellationToken ct = default);

    /// <summary>Publishes the together party the caller hosts as an AetherParty hangout. The server forces
    /// the category and verifies the party is live and hosted by this account.</summary>
    Task<HangoutSummaryDto> PublishTogetherPartyHangoutAsync(
        Guid partyId, CreateHangoutRequest req, CancellationToken ct = default);
    Task EndMyHangoutAsync(CancellationToken ct = default);

    /// <summary>Read-and-clear of the chat hangout-card deep link; polled at the start of every app frame.
    /// <c>FromChat</c> marks the tap as originating in a chat so the detail's back returns there.</summary>
    (HangoutSummaryDto Hangout, bool FromChat)? TakePendingOpenHangout();
}
