using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Shared.Messaging;

namespace AetherLove.Os;

/// <summary>Shared social navigation and match data any AetherOS app can reuse instead of re-declaring it on
/// its own host bridge: reading the signed-in account's matches and opening the AetherLove chat. Declared in
/// AetherLove.AppKit so every app can reference it; implemented plugin-side. Deliberately no profile-preview
/// member: dating profiles are private and no other app may open one.</summary>
public interface ISocialBridge
{
    /// <summary>Fetches the account's match list from the hub (for share pickers).</summary>
    Task<MatchListDto> GetMyMatchesAsync(CancellationToken ct = default);

    /// <summary>Cached match summaries (display names, avatars) without a hub round-trip.</summary>
    IReadOnlyList<MatchSummaryDto> GetCachedMatches();

    /// <summary>Returns to the AetherLove chat screen (the "back to chat" affordance on a deep-linked view).</summary>
    void OpenChat();
}
