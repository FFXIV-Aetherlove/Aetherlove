using System;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Shared.Feedback;

namespace AetherOS.Apps.Feedback;

/// <summary>Host bridge into the plugin: the single feedback hub passthrough. The app can't see the hub client,
/// so submission routes through here.</summary>
public interface IFeedbackHost
{
    Task<Guid> SubmitAsync(SubmitFeedbackRequest req, CancellationToken ct = default);
}
