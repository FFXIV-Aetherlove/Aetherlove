using System;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Services;
using AetherLove.Services.Hub;
using AetherLove.Shared.Hangouts;

namespace AetherLove.Os;

/// <summary>Bridges the AetherOS hangouts app onto the plugin's hangout hub client and the chat-card deep link.
/// Sharing goes through the OS share sheet; match data and chat/profile navigation come from the shared social
/// bridge.</summary>
public sealed class HangoutsHostService : AetherOS.Apps.Hangouts.IHangoutsHost
{
    private readonly AetherHubContext _hub;
    private readonly HangoutShareContext _shareCtx;

    public HangoutsHostService(
        AetherHubContext hub,
        HangoutShareContext shareCtx)
    {
        _hub = hub;
        _shareCtx = shareCtx;
    }

    public Task<HangoutDirectoryPageDto> GetHangoutDirectoryAsync(HangoutDirectoryFilterDto filter, int skip, CancellationToken ct = default) =>
        _hub.GetHangoutDirectoryAsync(filter, skip, ct);

    public Task<HangoutCardDto> GetHangoutCardAsync(Guid hangoutId, CancellationToken ct = default) =>
        _hub.GetHangoutCardAsync(hangoutId, ct);

    public Task<HangoutRsvpResultDto> SetHangoutRsvpAsync(Guid hangoutId, bool going, CancellationToken ct = default) =>
        _hub.SetHangoutRsvpAsync(hangoutId, going, ct);

    public Task<Guid> ReportHangoutAsync(ReportHangoutRequest req, CancellationToken ct = default) =>
        _hub.ReportHangoutAsync(req, ct);

    public Task<HangoutSummaryDto> CreateHangoutAsync(CreateHangoutRequest req, CancellationToken ct = default) =>
        _hub.CreateHangoutAsync(req, ct);

    public Task EndMyHangoutAsync(CancellationToken ct = default) =>
        _hub.EndMyHangoutAsync(ct);

    public (HangoutSummaryDto Hangout, bool FromChat)? TakePendingOpenHangout()
    {
        if (_shareCtx.PendingOpenHangout is not { } hangout)
        {
            return null;
        }
        var fromChat = _shareCtx.PendingOpenFromChat;
        _shareCtx.PendingOpenHangout = null;
        _shareCtx.PendingOpenFromChat = false;
        return (hangout, fromChat);
    }
}
