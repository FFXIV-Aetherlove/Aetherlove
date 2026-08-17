using System;
using AetherLove.Shared.Arcade;
using AetherOS.Apps.Aetherling.Engine;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;

namespace AetherOS.Apps.Aetherling.Screens.Games;

/// <summary>Everything a game needs for one frame, built by the games screen. The runtime is the shared
/// pet body: games draw it with poses of their own making and never call its interaction methods, so a
/// round of play leaves the creature's mood and habits exactly as it found them.</summary>
internal readonly struct GameStage(
    System.Numerics.Vector2 origin,
    System.Numerics.Vector2 size,
    PetRuntime runtime,
    AtlasManifest manifest,
    string assetRoot,
    bool reduceMotion,
    bool inputActive,
    Action<GameSound> sound)
{
    public System.Numerics.Vector2 Origin { get; } = origin;

    public System.Numerics.Vector2 Size { get; } = size;

    public PetRuntime Runtime { get; } = runtime;

    public AtlasManifest Manifest { get; } = manifest;

    /// <summary>Where the app's loose art lives, for the per-game pieces the player can override with
    /// their own files under games/.</summary>
    public string AssetRoot { get; } = assetRoot;

    public bool ReduceMotion { get; } = reduceMotion;

    /// <summary>False during countdown, pause and results, so a click on a chrome button never also
    /// steers or throttles for a frame.</summary>
    public bool InputActive { get; } = inputActive;

    /// <summary>Say what happened; <see cref="GameSounds"/> owns the file and the level, the screen owns
    /// the mute, and the audio capability owns whether anything is audible at all.</summary>
    public Action<GameSound> Sound { get; } = sound;
}

/// <summary>One of the companion's minigames. The screen owns the phases around a run (countdown, pause,
/// results, submission); the game owns only its own little world.</summary>
internal interface IPetGame
{
    ArcadeGame Id { get; }

    bool Over { get; }

    int Score { get; }

    int Metric1 { get; }

    int Metric2 { get; }

    void Reset(Random rng);

    void UpdateAndDraw(OsAppContext ctx, ImDrawListPtr dl, GameStage stage, float dt);
}
