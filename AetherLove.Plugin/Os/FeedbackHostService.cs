using System;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Services.Hub;
using AetherLove.Shared.Feedback;
using AetherOS.Apps.Feedback;

namespace AetherLove.Os;

/// <summary>The feedback desk's bridge: forwards a submission to the AetherLove hub.</summary>
public sealed class FeedbackHostService : IFeedbackHost
{
    private readonly AetherHubContext _hub;

    public FeedbackHostService(AetherHubContext hub)
    {
        _hub = hub;
    }

    public Task<Guid> SubmitAsync(SubmitFeedbackRequest req, CancellationToken ct = default) =>
        _hub.SubmitFeedbackAsync(req, ct);
}
