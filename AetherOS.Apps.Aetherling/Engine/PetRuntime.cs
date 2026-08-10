using System;
using System.Numerics;
using AetherOS.Apps.Aetherling.Rendering;
using AetherOS.Apps.Aetherling.Screens;
using AetherOS.Apps.Aetherling.Ui;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;

namespace AetherOS.Apps.Aetherling.Engine;

/// <summary>The one live creature. Its page inside the phone and its floating window outside both draw it,
/// so it cannot belong to either: two owners each advancing it would have it blinking and hopping at double
/// speed on every frame they are both up.</summary>
internal sealed class PetRuntime
{
    private readonly ParticleFx _fx = new();
    private readonly MoodTracker _mood = new();

    private CoreAssets? _assets;
    private CoreDraw? _draw;
    private AnimationController? _animator;
    private bool _loadAttempted;
    private int _lastTickFrame = -1;
    private double _lastTickTime;

    public bool Ready => _draw is not null && _animator is not null;

    public bool Napping => _animator?.Napping ?? false;

    public void EnsureLoaded(string assetRoot)
    {
        if (_loadAttempted)
        {
            return;
        }
        _loadAttempted = true;
        CoreAssets.AssetRootHint = assetRoot;
        _assets = CoreAssets.Load(CoreAssets.HatchlingFolder);
        if (_assets is null)
        {
            return;
        }
        _draw = new CoreDraw(_assets);
        _animator = new AnimationController(_assets.Manifest);
    }

    /// <summary>Advances everything, at most once per rendered frame however many surfaces ask.</summary>
    public void Tick(bool reduceMotion)
    {
        var frame = ImGui.GetFrameCount();
        if (frame == _lastTickFrame)
        {
            return;
        }
        _lastTickFrame = frame;

        var now = ImGui.GetTime();
        var dt = Math.Clamp((float)(now - _lastTickTime), 0f, 0.25f);
        _lastTickTime = now;

        if (_animator is not null)
        {
            _animator.ReduceMotion = reduceMotion;
            _animator.Update(dt);
        }
        _mood.Update(dt);
        _fx.Update(dt);
    }

    public PetPose Pose => _animator?.GetPose() ?? new PetPose { Scale = Vector2.One };

    public MoodLevel Mood => _mood.Current(_animator?.SinceInteraction ?? 0f, Napping);

    /// <summary>A poke, from wherever it was poked.</summary>
    public void Boop()
    {
        _animator?.Boop();
        _mood.Lift();
        _fx.Burst(ParticleKind.Sparkle, new Vector2(128f, 150f), 8, Look.CrystalPale, 60f);
    }

    public void Celebrate()
    {
        _mood.Lift();
        _fx.Burst(ParticleKind.Sparkle, new Vector2(128f, 150f), 16, Look.CrystalPale, 90f);
    }

    /// <summary>Plays the hop frames without moving anything, for a caller carrying the sprite itself.</summary>
    public void PlayHopClip() => _animator?.PlayHopClip();

    public void Draw(ImDrawListPtr dl, ITextureCache textures, Vector2 bottomCentre, float size, PetPose pose)
    {
        if (_draw is null)
        {
            return;
        }
        _fx.Draw(dl, bottomCentre, size, behind: true);
        _draw.Draw(dl, textures, bottomCentre, size, pose.CellIndex, PetTints.Dawn, pose.Scale, pose.Offset,
            null, pose.FlipX);
        _fx.Draw(dl, bottomCentre, size, behind: false);
    }
}
