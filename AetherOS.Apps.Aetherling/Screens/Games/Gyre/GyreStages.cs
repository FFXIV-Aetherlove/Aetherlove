using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace AetherOS.Apps.Aetherling.Screens.Games.Gyre;

internal sealed class GyreShooterDto
{
    public float X { get; set; }

    public float Y { get; set; }
}

internal sealed class GyrePathDto
{
    public string Id { get; set; } = "a";

    public float SpawnDelay { get; set; }

    /// <summary>The colours this lane deals, as indices into the stage's palette. Empty means the whole
    /// palette. Splitting six colours across three lanes is what makes a three-chain stage readable.</summary>
    public int[] Colours { get; set; } = [];

    /// <summary>Hold this lane shut until this fraction of the stage's marbles has been fed. A clock
    /// cannot say "once the first lane is nearly done", because how long that takes is the player's.</summary>
    public float SpawnAfter { get; set; }

    /// <summary>Close this lane's mouth at this many seconds, 0 to leave it open. What is already on
    /// the track still has to be cleared; the lane simply stops being fed, which is how a stage hands
    /// the player from one chain to the next instead of piling them up.</summary>
    public float SpawnUntil { get; set; }

    public float[][] Points { get; set; } = [];

    public float[][] Tunnels { get; set; } = [];

    public float[][] Overpasses { get; set; } = [];
}

internal sealed class GyreStageDto
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int Chapter { get; set; }

    public string Background { get; set; } = "sky";

    public GyreShooterDto Shooter { get; set; } = new();

    public int Colours { get; set; } = 3;

    public int Marbles { get; set; }

    public float Speed { get; set; }

    public float SurgeSpeed { get; set; }

    public float PowerupChance { get; set; }

    public float DudChance { get; set; }

    public List<GyrePathDto> Paths { get; set; } = [];
}

/// <summary>The twenty authored stages, loaded once from the shipped stages.json. The JSON is the
/// source of truth for geometry (authored and validated outside the repo); this class only parses it.
/// Stage 20 is The Core, the endless finale: its pool never empties and its speed ramps on a clock.</summary>
internal static class GyreStages
{
    public const float CanvasWidth = 1000f;

    public const float CanvasHeight = 1540f;

    public const float MarbleDiameter = 80f;

    public const float MarbleSpacing = 76f;

    public const int EndlessStage = 20;

    /// <summary>The Core speeds up per MARBLE FED, not on a clock: a player who is clearing well earns
    /// the same pace as one who is drowning, and a step small enough to be unnoticeable never reads as
    /// the game lurching. 2 units every 25 marbles is about 2 percent a step.</summary>
    public const float EndlessSpeedStep = 2f;

    public const int EndlessStepMarbles = 25;

    /// <summary>The Core opens on two colours and earns one more at every speed step, so its first
    /// minute is a warm-up and its last is the whole palette at pace.</summary>
    public const int EndlessStartColours = 2;

    public const float EndlessSpeedCap = 150f;

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    private static IReadOnlyList<GyreStageDto>? _stages;

    public static IReadOnlyList<GyreStageDto> Load(string assetRoot)
    {
        if (_stages is not null)
        {
            return _stages;
        }
        try
        {
            var path = Path.Combine(assetRoot, "games", "gyre", "stages.json");
            var parsed = JsonSerializer.Deserialize<List<GyreStageDto>>(File.ReadAllText(path), Options);
            _stages = parsed is { Count: > 0 } ? parsed : [];
        }
        catch (Exception)
        {
            _stages = [];
        }
        return _stages;
    }
}
