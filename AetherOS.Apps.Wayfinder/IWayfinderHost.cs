using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Shared.Wayfinder;

namespace AetherOS.Apps.Wayfinder;

/// <summary>Plugin-side services the Wayfinder app needs; implemented in the plugin (dependency inversion,
/// so the app never references it).</summary>
public interface IWayfinderHost
{
    Task<WayfinderStateDto> GetStateAsync(CancellationToken ct = default);

    Task<WayfinderStartResultDto> StartAsync(CancellationToken ct = default);

    /// <summary>Captures the player's territory and world position itself (framework thread) and submits
    /// them; the app never touches game objects and never sees coordinates.</summary>
    Task<WayfinderSubmitResultDto> SubmitAttemptAsync(Guid assignmentId, CancellationToken ct = default);

    /// <summary>Gives up the active challenge; the start it used stays spent.</summary>
    Task<WayfinderStateDto> AbandonAsync(CancellationToken ct = default);

    /// <summary>Bakes the crop and imports the selfie into the Photos camera roll; returns the stored path,
    /// or null when the import failed.</summary>
    string? SaveSelfie(string sourcePath, Vector4 crop);

    bool IsSupporter { get; }

    /// <summary>Whether this account may author waypoints (the Wayfinder scout right, or staff).</summary>
    bool IsScout { get; }

    /// <summary>Stamps the author's current position for the waypoint being created and returns the zone
    /// name for display; null when there is no player to read. The coordinates stay inside the host.</summary>
    Task<string?> CaptureWaypointAsync(CancellationToken ct = default);

    /// <summary>Submits the waypoint stamped by the last <see cref="CaptureWaypointAsync"/> together with
    /// its picture. Throws when nothing was stamped.</summary>
    Task<WayfinderNewChallengeResultDto> SubmitWaypointAsync(
        string name, short expansion, string photoPath, Vector4 crop, CancellationToken ct = default);

    /// <summary>The party hunt the client currently knows about, updated by hub pushes. Null while the
    /// party has none. A terminal run stays until <see cref="DismissPartyResults"/>.</summary>
    WayfinderPartyRunDto? PartyRun { get; }

    /// <summary>Drops a finished run's results; a live run is never dropped.</summary>
    void DismissPartyResults();

    /// <summary>Host only: opens the gathering, stamping the host's current world server-side.</summary>
    Task<WayfinderPartyRunDto> StartPartyGatherAsync(CancellationToken ct = default);

    /// <summary>Joins the gathering. Captures the caller's world itself; the server refuses a world other
    /// than the host's with <c>WayfinderRunWrongWorld</c> carrying the host's world id.</summary>
    Task<WayfinderPartyRunDto> JoinPartyRunAsync(Guid runId, CancellationToken ct = default);

    Task<WayfinderPartyRunDto> BeginPartyRunAsync(Guid runId, CancellationToken ct = default);

    Task CancelPartyRunAsync(Guid runId, CancellationToken ct = default);

    /// <summary>Captures position and world plugin-side and submits them; the app never sees either.</summary>
    Task<WayfinderGroupSubmitResultDto> SubmitPartyAttemptAsync(Guid assignmentId, CancellationToken ct = default);

    /// <summary>Re-pulls the run with its image, for recovery after a reload.</summary>
    Task<WayfinderPartyRunDto?> RefreshPartyRunAsync(CancellationToken ct = default);

    /// <summary>Display name of a game world, for the wrong-world explainer.</summary>
    string? WorldName(int worldId);
}
