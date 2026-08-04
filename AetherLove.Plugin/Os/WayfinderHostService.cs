using System;
using System.IO;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Services;
using AetherLove.Services.Auth;
using AetherLove.Services.Hub;
using AetherLove.Shared.Profile;
using AetherLove.Shared.Wayfinder;
using AetherOS.Apps.Wayfinder;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;

namespace AetherLove.Os;

/// <summary>Plugin-side implementation of the Wayfinder app's host bridge. Owns the one game-touching step:
/// the position snapshot taken on the framework thread at the selfie moment. Coordinates go straight to the
/// hub call; the app never sees them.</summary>
public sealed class WayfinderHostService : IWayfinderHost
{
    private readonly AetherHubContext _hubClient;
    private readonly SessionBootstrapper _bootstrap;
    private readonly IFramework _framework;
    private readonly CameraLibraryService _camera;

    public WayfinderHostService(AetherHubContext hubClient, SessionBootstrapper bootstrap,
        IFramework framework, CameraLibraryService camera)
    {
        _hubClient = hubClient;
        _bootstrap = bootstrap;
        _framework = framework;
        _camera = camera;
    }

    public async Task<WayfinderStateDto> GetStateAsync(CancellationToken ct = default) =>
        await _hubClient.GetWayfinderStateAsync(await UnlockedThroughAsync().ConfigureAwait(false), ct)
            .ConfigureAwait(false);

    public async Task<WayfinderStartResultDto> StartAsync(CancellationToken ct = default) =>
        await _hubClient.StartWayfinderChallengeAsync(await UnlockedThroughAsync().ConfigureAwait(false), ct)
            .ConfigureAwait(false);

    public async Task<WayfinderSubmitResultDto> SubmitAttemptAsync(Guid assignmentId, CancellationToken ct = default)
    {
        var position = await _framework.RunOnFrameworkThread(CapturePosition).ConfigureAwait(false)
            ?? throw new InvalidOperationException("No player position available.");
        var dto = new WayfinderSubmitDto(assignmentId, position.TerritoryId, position.X, position.Y, position.Z);
        return await _hubClient.SubmitWayfinderAttemptAsync(dto, ct).ConfigureAwait(false);
    }

    public async Task<WayfinderStateDto> AbandonAsync(CancellationToken ct = default) =>
        await _hubClient.AbandonWayfinderChallengeAsync(await UnlockedThroughAsync().ConfigureAwait(false), ct)
            .ConfigureAwait(false);

    /// <summary>How far the player has actually travelled, taken from their attuned aetherytes rather than
    /// what they own: someone who bought every expansion but is still in A Realm Reborn has only ARR
    /// aetherytes, so they are gated to ARR. Zero on any failure, which tells the server to skip the gate.</summary>
    private Task<short> UnlockedThroughAsync() =>
        _framework.RunOnFrameworkThread(ReadUnlockedExpansion);

    private static unsafe short ReadUnlockedExpansion()
    {
        var telepo = Telepo.Instance();
        if (telepo is null)
        {
            return 0;
        }

        telepo->UpdateAetheryteList();
        var territories = UiHost.DataManager.GetExcelSheet<TerritoryType>();
        short furthest = 0;
        ushort furthestTerritory = 0;
        foreach (var entry in telepo->TeleportList)
        {
            // Estates ride the teleport list without being attunements: a level 1 character whose free
            // company owns an Empyreum plot can teleport to Ishgard without ever having set foot there.
            if (entry.IsSharedHouse || entry.IsApartment || entry.Ward != 0 || entry.Plot != 0)
            {
                continue;
            }
            if (territories.GetRowOrDefault(entry.TerritoryId) is not { } row)
            {
                continue;
            }
            // ExVersion counts from 0 for A Realm Reborn; the shared enum reserves 0 for "unknown".
            var expansion = (short)(row.ExVersion.RowId + 1);
            if (expansion > furthest)
            {
                furthest = expansion;
                furthestTerritory = entry.TerritoryId;
            }
        }

        UiHost.Log.Debug(
            "[Wayfinder] Unlocked through expansion {Expansion} (territory {Territory}) from {Count} teleport entries.",
            furthest, furthestTerritory, telepo->TeleportList.Count);
        return furthest;
    }

    public string? SaveSelfie(string sourcePath, Vector4 crop) => _camera.AddCapture(sourcePath, crop)?.Path;

    public bool IsSupporter => _bootstrap.LastConnection is { IsSupporter: true };

    public bool IsScout => _bootstrap.LastConnection is { IsWayfinderScout: true };

    public async Task<string?> CaptureWaypointAsync(CancellationToken ct = default)
    {
        var stamped = await _framework.RunOnFrameworkThread(CapturePosition).ConfigureAwait(false);
        _pendingWaypoint = stamped;
        return stamped is null ? null : LocationShare.ZoneName((uint)stamped.TerritoryId);
    }

    public async Task<WayfinderNewChallengeResultDto> SubmitWaypointAsync(
        string name, short expansion, string photoPath, Vector4 crop, CancellationToken ct = default)
    {
        if (_pendingWaypoint is not { } position)
        {
            throw new InvalidOperationException("No waypoint position was stamped.");
        }

        // The crop is baked here so the upload carries the framed shot rather than the whole screen.
        var baked = _camera.BakeCrop(photoPath, crop) ?? photoPath;
        try
        {
            var bytes = await File.ReadAllBytesAsync(baked, ct).ConfigureAwait(false);
            var upload = new PhotoUploadDto(Convert.ToBase64String(bytes), 0, 0, 0, 0, false);
            var dto = new WayfinderNewChallengeDto(
                name, expansion, position.TerritoryId, position.X, position.Y, position.Z,
                LocationShare.ZoneName((uint)position.TerritoryId), upload);
            var result = await _hubClient.SubmitWayfinderChallengeAsync(dto, ct).ConfigureAwait(false);
            _pendingWaypoint = null;
            return result;
        }
        finally
        {
            if (!string.Equals(baked, photoPath, StringComparison.Ordinal))
            {
                try
                {
                    File.Delete(baked);
                }
                catch (IOException)
                {
                }
            }
        }
    }

    private PositionSnapshot? _pendingWaypoint;

    private sealed record PositionSnapshot(int TerritoryId, float X, float Y, float Z);

    private static PositionSnapshot? CapturePosition()
    {
        var player = UiHost.ObjectTable.LocalPlayer;
        if (player is null)
        {
            return null;
        }
        var pos = player.Position;
        return new PositionSnapshot((int)UiHost.ClientState.TerritoryType, pos.X, pos.Y, pos.Z);
    }
}
