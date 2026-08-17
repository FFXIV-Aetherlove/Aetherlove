using System;
using System.Collections.Generic;
using System.Numerics;
using AetherLove.Shared.Aetherling;
using AetherOS.Apps.Aetherling.Rendering;
using AetherOS.Apps.Aetherling.Ui;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;

namespace AetherOS.Apps.Aetherling.Engine;

/// <summary>The one live creature. Its page inside the phone and its floating window outside both draw it,
/// so it cannot belong to either: two owners each advancing it would have it blinking and hopping at double
/// speed on every frame they are both up. Since the growth release it also owns the worn look, the dynamic
/// mouth and the flourish playback, for the same reason: every surface must read the same pet.</summary>
internal sealed class PetRuntime
{
    private const string DefaultPaletteName = "Dawn";

    private readonly ParticleFx _fx = new();
    private readonly MoodTracker _mood = new();
    private readonly MouthController _mouth = new();
    private readonly List<(float Delay, Action Fire)> _fxQueue = [];

    private CoreAssets? _assets;
    private CoreDraw? _draw;
    private AnimationController? _animator;
    private PetCatalogue? _catalogue;
    private string? _loadedFolder;
    private int _lastTickFrame = -1;
    private double _lastTickTime;

    private Palette? _palette;
    private readonly List<AccessoryDef> _wornBehind = [];
    private readonly List<AccessoryDef> _wornFront = [];
    private string _equippedReaction = "";
    private readonly List<ReactionDef> _boopPool = [];
    private HashSet<string>? _ownedReactions;
    private string[] _disabledReactions = [];
    private float _reactionLeft;

    private string _footprintKey = "";
    private Vector4 _footprint;

    public bool Ready => _draw is not null && _animator is not null;

    public bool Napping => _animator?.Napping ?? false;

    public PetCatalogue? Catalogue => _catalogue;

    /// <summary>The manifest on screen, for callers measuring against the worn form.</summary>
    public AtlasManifest? Manifest => _assets?.Manifest;

    /// <summary>A form the creature has grown into but is not wearing yet, because a ceremony is
    /// mid-flight and the swap belongs to its flash. While this is set every surface's request to
    /// load something is ignored, which is what stops the phone page and the floating window
    /// pulling the body in two directions.</summary>
    public string? HeldForm { get; private set; }

    /// <summary>Arms the swap without performing it.</summary>
    public void HoldForm(string formFolder) => HeldForm = formFolder;

    /// <summary>Performs the held swap, at the moment the ceremony says so.</summary>
    public void CommitHeldForm(string assetRoot)
    {
        if (HeldForm is not { } folder)
        {
            return;
        }
        HeldForm = null;
        EnsureLoaded(assetRoot, folder);
    }

    /// <summary>Loads (or reloads) the sheet set for a form. The catalogue loads once and stays;
    /// an evolution swaps the body under the same look, mood and particles, which is exactly the
    /// continuity the moment wants.
    /// <para>Every surface calls this with the form the snapshot says, so there is one answer and
    /// no caller decides for itself which body is on screen.</para></summary>
    public void EnsureLoaded(string assetRoot, string formFolder)
    {
        if (_loadedFolder == formFolder || HeldForm is not null)
        {
            return;
        }

        CoreAssets.AssetRootHint = assetRoot;
        var assets = CoreAssets.Load(formFolder);
        if (assets is null)
        {
            return;
        }

        _loadedFolder = formFolder;
        _assets = assets;
        _draw = new CoreDraw(assets);
        _animator = new AnimationController(assets.Manifest);
        _catalogue ??= PetCatalogue.Load();
        _footprintKey = "";
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
            _mouth.SetBase(MouthShapes.For(_animator.CurrentAnimation, Mood));
        }
        _mood.Update(dt);
        _mouth.Update(dt);
        _fx.Update(dt);
        _reactionLeft = MathF.Max(0f, _reactionLeft - dt);

        for (var i = _fxQueue.Count - 1; i >= 0; i--)
        {
            var (delay, fire) = _fxQueue[i];
            delay -= dt;
            if (delay <= 0f)
            {
                _fxQueue.RemoveAt(i);
                fire();
            }
            else
            {
                _fxQueue[i] = (delay, fire);
            }
        }
    }

    public PetPose Pose => _animator?.GetPose() ?? new PetPose { Scale = Vector2.One };

    public MoodLevel Mood => _mood.Current(_animator?.SinceInteraction ?? 0f, Napping);

    /// <summary>The mood as a place on its own scale, 0 asleep to 1 beaming, for the pet page's bar.</summary>
    public float MoodProgress => _mood.Progress01(_animator?.SinceInteraction ?? 0f, Napping);

    /// <summary>Dresses it from a snapshot, and only when the look actually changed: rebuilding the worn
    /// lists and the footprint is not per-frame work. Every surface calls this, the same way they all call
    /// <see cref="EnsureLoaded"/> for the form, because the runtime is shared and the look belongs to the
    /// creature rather than to whichever page is drawing it. It used to be done by the pet page alone,
    /// which is why the one out on the game screen wore the default blue until the app was opened.</summary>
    public void ApplyLook(AetherlingDto? core)
    {
        // The wardrobe dresses it directly while it is open, and the floating creature is drawing all the
        // while: without this the two would fight over the body every frame and the live preview would
        // snap back to the saved look the instant it was changed.
        if (_draftLook)
        {
            return;
        }

        var look = core?.Look;
        var adult = core?.Adult is not null;
        var key = look is null
            ? string.Empty
            : $"{look.Palette}|{(adult ? string.Join(',', look.Accessories) : string.Empty)}|{look.Reaction}"
                + $"|{string.Join(',', look.DisabledReactions ?? [])}";
        if (key == _appliedLook)
        {
            return;
        }
        _appliedLook = key;
        Wear(
            look?.Palette ?? "dawn",
            adult ? look?.Accessories ?? [] : [],
            look?.Reaction ?? string.Empty,
            look?.DisabledReactions ?? []);
    }

    /// <summary>Hands the look back to the snapshot after a wardrobe visit, so whatever the server has is
    /// what shows again.</summary>
    public void ClearDraftLook()
    {
        _draftLook = false;
        _appliedLook = string.Empty;
    }

    private string _appliedLook = string.Empty;
    private bool _draftLook;

    /// <summary>The wardrobe's live preview: a look nobody has saved yet. It takes the body over until
    /// <see cref="ClearDraftLook"/> hands it back, so the snapshot cannot overwrite what is being tried on.</summary>
    public void ApplyDraftLook(
        string paletteRef,
        IReadOnlyList<string> accessoryRefs,
        string reactionRef,
        IReadOnlyList<string> disabledReactions)
    {
        _draftLook = true;
        Wear(paletteRef, accessoryRefs, reactionRef, disabledReactions);
    }

    /// <summary>What the owner has earned, from the store inventory. Null until somebody has actually
    /// asked, which is why a boop falls back to the one legacy equipped flourish until then rather than
    /// to nothing. Feeding it clears the applied look so the pool is rebuilt on the next frame.</summary>
    public void SetOwnedReactions(IEnumerable<string>? refs)
    {
        _ownedReactions = refs is null ? null : new HashSet<string>(refs, StringComparer.OrdinalIgnoreCase);
        _appliedLook = string.Empty;
        RebuildBoopPool();
    }

    /// <summary>What the pet wears. Sent whole, exactly like the server stores it; unknown refs
    /// drop silently so a stale look never breaks the draw. A growing form wears no accessories
    /// (the young silhouettes are not what the art was authored against), so callers pass an
    /// empty list until adulthood.</summary>
    private void Wear(
        string paletteRef,
        IReadOnlyList<string> accessoryRefs,
        string reactionRef,
        IReadOnlyList<string> disabledReactions)
    {
        _palette = _catalogue?.PaletteByRef(paletteRef);
        _equippedReaction = reactionRef;
        _disabledReactions = [.. disabledReactions];
        RebuildBoopPool();
        _wornBehind.Clear();
        _wornFront.Clear();
        if (_catalogue is null)
        {
            return;
        }

        var front = new List<AccessoryDef>();
        var arms = new List<AccessoryDef>();
        foreach (var itemRef in accessoryRefs)
        {
            var def = _catalogue.Accessory(itemRef);
            if (def is null)
            {
                continue;
            }
            if (def.Behind)
            {
                _wornBehind.Add(def);
            }
            else if (def.Slot == AccessoryDef.ArmsSlot)
            {
                arms.Add(def);
            }
            else
            {
                front.Add(def);
            }
        }

        // Arms draw last, in front of everything worn, so a pommel never sinks into a scarf.
        _wornFront.AddRange(front);
        _wornFront.AddRange(arms);
        _footprintKey = "";
    }

    /// <summary>Everything earned that has not been switched off in the wardrobe. Rebuilt rather than
    /// filtered per boop, since a poke should cost nothing.</summary>
    private void RebuildBoopPool()
    {
        _boopPool.Clear();
        if (_ownedReactions is null)
        {
            return;
        }
        foreach (var itemRef in _ownedReactions)
        {
            if (Array.Exists(_disabledReactions, r => string.Equals(r, itemRef, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            if (ReactionDef.Find(itemRef) is { } def)
            {
                _boopPool.Add(def);
            }
        }
    }

    /// <summary>A poke, from wherever it was poked: the squish, a mood lift, and one of its learned
    /// flourishes blooming over it, picked fresh each time so the creature never feels like one trick.
    /// Nothing earned, or everything switched off, leaves the plain squish.</summary>
    public void Boop()
    {
        _animator?.Boop();
        _mood.Lift();
        var played = _ownedReactions is null
            ? PlayEquippedReaction()
            : PlayRandomReaction();
        if (!played)
        {
            _fx.Burst(ParticleKind.Sparkle, new Vector2(128f, 150f), 8, Look.CrystalPale, 60f);
        }
    }

    private bool PlayRandomReaction()
    {
        if (_boopPool.Count == 0)
        {
            return false;
        }
        PlayReaction(_boopPool[Random.Shared.Next(_boopPool.Count)]);
        return true;
    }

    public void Celebrate()
    {
        _mood.Lift();
        _fx.Burst(ParticleKind.Sparkle, new Vector2(128f, 150f), 16, Look.CrystalPale, 90f);
    }

    /// <summary>A stroke of petting: warmth without the boop squish. The caller rate-limits.</summary>
    public void Pet()
    {
        _mood.Lift();
        _fx.Emit(ParticleKind.Heart, AnchorLocal256("head") + new Vector2(0f, -14f),
            new Vector4(1f, 0.55f, 0.65f, 0.9f), 26f);
    }

    /// <summary>Found after time away: at rest, not still glowing from a lift nobody watched.</summary>
    public void PrimeQuiet() => _mood.PrimeQuiet();

    /// <summary>A crystal landing in the mouth: the chew (there is no eat clip, the boop squish
    /// stands in), a shard-and-mote burst in the crystal's colour, the crunch on the face.</summary>
    public void PlayFeedLand(Vector4 accent, bool reduceMotion)
    {
        _animator?.Boop();
        _mood.Lift();
        if (reduceMotion)
        {
            return;
        }

        var mouthAt = AnchorLocal256("face") + new Vector2(0f, 16f);
        _fx.Burst(ParticleKind.Shard, mouthAt, 5, accent with { W = 0.95f }, 12f);
        _fx.Burst(ParticleKind.Mote, mouthAt, 3, accent with { W = 0.7f }, 16f);
        _mouth.Play(MouthShapes.Sequence(0.26f, 0.1f, "chew-open", "mm", "chew-open", "mm", "chew-open"), 1.6f);
    }

    /// <summary>The gentle refusal (full, gated): nothing consumed, a heart, a soft line the
    /// caller shows. Never a scold.</summary>
    public void PlayRefusal(bool reduceMotion)
    {
        if (reduceMotion)
        {
            return;
        }
        _fx.Emit(ParticleKind.Heart, AnchorLocal256("face"), new Vector4(1f, 0.7f, 0.6f, 0.8f), 10f);
    }

    /// <summary>An evolution or the adulting: the old light gathers, the new floods out from the
    /// feet, then the bloom. The caller swaps the form at the flood, which the timing hides.</summary>
    public void PlayEvolutionMoment(Vector4 accent, bool reduceMotion)
    {
        _mood.Lift();
        if (reduceMotion)
        {
            return;
        }

        var body = AnchorLocal256("body");
        var feet = new Vector2(128f, 246f);
        _fx.Burst(ParticleKind.Mote, body, 10, accent with { W = 0.5f }, 84f, behind: true);
        _fx.Emit(ParticleKind.Ring, feet, accent with { W = 0.9f }, 96f);

        Queue(0.24f, () =>
        {
            _fx.Emit(ParticleKind.Ring, new Vector2(128f, 244f), accent with { W = 0.75f }, 118f);
            _fx.Burst(ParticleKind.Shard, AnchorLocal256("body") + new Vector2(0f, 40f), 10,
                accent with { W = 0.95f }, 46f);
        });

        Queue(0.52f, () =>
        {
            _animator?.PlayHopClip();
            _fx.Burst(ParticleKind.Glow, AnchorLocal256("body"), 6, accent with { W = 0.45f }, 52f, behind: true);
            _fx.Burst(ParticleKind.Sparkle, AnchorLocal256("body"), 12, new Vector4(1f, 0.97f, 0.82f, 0.95f), 74f);
        });

        _mouth.Play([new MouthKey(0f, "wow", 0.15f), new MouthKey(1.6f, "laugh", 0.2f)], 3.2f);
    }

    /// <summary>Plays the equipped flourish; false when nothing (or nothing known) is equipped,
    /// so the caller can fall back to the plain sparkle.</summary>
    public bool PlayEquippedReaction()
    {
        var def = ReactionDef.Find(_equippedReaction);
        if (def is null)
        {
            return false;
        }
        PlayReaction(def);
        return true;
    }

    /// <summary>One flourish, from its procedural recipe. Each element has its own particle
    /// family, emitted off the body silhouette or the ground line rather than from a loose box,
    /// so the flourish visibly comes OUT of the creature. Reactions own their moment: a second
    /// play replaces the first rather than stacking.</summary>
    public void PlayReaction(ReactionDef def)
    {
        _reactionLeft = ReactionDef.DurationSeconds;
        var body = AnchorLocal256("body");
        var head = AnchorLocal256("head");
        var feet = new Vector2(128f, 246f);
        switch (def.Procedural)
        {
            case "hearts":
                _fx.Burst(ParticleKind.Heart, head + new Vector2(0f, -18f), 9, new Vector4(1f, 0.55f, 0.65f, 0.95f), 34f);
                Queue(0.14f, () => _fx.Burst(ParticleKind.Heart,
                    AnchorLocal256("head") + new Vector2(0f, -26f), 4, new Vector4(1f, 0.68f, 0.75f, 0.9f), 26f));
                break;

            case "sparkles":
                _fx.Emit(ParticleKind.Ring, feet, new Vector4(1f, 0.9f, 0.55f, 0.85f), 92f);
                _fx.Burst(ParticleKind.Sparkle, body, 14, new Vector4(1f, 0.93f, 0.6f, 0.95f), 66f);
                break;

            case "shards":
                // A veil of Aethercore light comes down over the pet rather than erupting out of
                // it: a glint opens it above the crown, a curtain of splinters drifts down the
                // full height cooling pale to ceremony teal, a slower layer falls behind the body
                // for depth, and glints at the waist then the feet mark the veil passing.
                _fx.Emit(ParticleKind.Glint, head + new Vector2(0f, -34f), new Vector4(0.86f, 1f, 0.98f, 0.95f), 30f);
                _fx.Cascade(ParticleKind.Shard, new Vector2(128f, 44f), 9,
                    new Vector4(0.85f, 1f, 0.98f, 0.95f), 50f, 118f,
                    colorEnd: new Vector4(0.4f, 0.85f, 0.82f, 0.85f));
                _fx.Burst(ParticleKind.Glow, body, 2, new Vector4(0.47f, 0.88f, 0.85f, 0.35f), 30f, behind: true);
                Queue(0.14f, () =>
                {
                    _fx.Cascade(ParticleKind.Shard, new Vector2(128f, 38f), 6,
                        new Vector4(0.66f, 0.95f, 0.93f, 0.8f), 64f, 92f, behind: true,
                        colorEnd: new Vector4(0.36f, 0.8f, 0.78f, 0.7f));
                    _fx.Cascade(ParticleKind.Sparkle, new Vector2(128f, 52f), 6,
                        new Vector4(0.9f, 1f, 0.99f, 0.9f), 44f, 105f);
                });
                Queue(0.34f, () =>
                {
                    _fx.Emit(ParticleKind.Glint, AnchorLocal256("body"), new Vector4(0.8f, 0.99f, 0.97f, 0.8f), 24f);
                    _fx.Cascade(ParticleKind.Shard, new Vector2(128f, 60f), 5,
                        new Vector4(0.78f, 0.99f, 0.97f, 0.9f), 34f, 132f,
                        colorEnd: new Vector4(0.4f, 0.85f, 0.82f, 0.8f));
                });
                Queue(0.62f, () =>
                {
                    _fx.Emit(ParticleKind.Glint, new Vector2(128f, 226f), new Vector4(0.7f, 0.96f, 0.94f, 0.75f), 20f);
                    _fx.Emit(ParticleKind.Ring, new Vector2(128f, 244f), new Vector4(0.47f, 0.88f, 0.85f, 0.6f), 88f);
                });
                break;

            case "fire":
                // Cinderburst: a fire stands up. Heat glow behind the body, a wide base of embers
                // licking off the ground, embers erupting off the silhouette, then a narrowing tip
                // of cinders above the crown, each wave climbing higher and cooling as it goes.
                _fx.Burst(ParticleKind.Glow, body, 3, new Vector4(1f, 0.5f, 0.18f, 0.5f), 24f, behind: true);
                _fx.Burst(ParticleKind.Glow, new Vector2(128f, 232f), 2, new Vector4(1f, 0.42f, 0.14f, 0.45f), 34f, behind: true);
                _fx.Burst(ParticleKind.Ember, new Vector2(128f, 238f), 7,
                    new Vector4(1f, 0.72f, 0.28f, 0.95f), 34f, colorEnd: new Vector4(0.88f, 0.28f, 0.1f, 0.85f));
                _fx.BurstRadial(ParticleKind.Ember, body + new Vector2(0f, 8f), 12,
                    new Vector4(1f, 0.78f, 0.32f, 0.95f), 34f, 45f, colorEnd: new Vector4(0.85f, 0.25f, 0.1f, 0.85f));
                Queue(0.14f, () =>
                {
                    _fx.BurstRadial(ParticleKind.Ember, AnchorLocal256("body"), 8,
                        new Vector4(0.98f, 0.55f, 0.2f, 0.9f), 26f, 38f,
                        colorEnd: new Vector4(0.8f, 0.2f, 0.08f, 0.8f));
                    _fx.Burst(ParticleKind.Ember, new Vector2(128f, 234f), 4,
                        new Vector4(1f, 0.66f, 0.24f, 0.9f), 26f, colorEnd: new Vector4(0.85f, 0.22f, 0.08f, 0.8f));
                });
                Queue(0.28f, () => _fx.Burst(ParticleKind.Ember,
                    AnchorLocal256("body") + new Vector2(0f, -34f), 5, new Vector4(1f, 0.62f, 0.24f, 0.85f), 18f,
                    colorEnd: new Vector4(0.8f, 0.18f, 0.06f, 0.75f)));
                Queue(0.42f, () => _fx.Burst(ParticleKind.Sparkle,
                    AnchorLocal256("body") + new Vector2(0f, -62f), 5, new Vector4(1f, 0.85f, 0.4f, 0.85f), 24f));
                break;

            case "ice":
                // Frostglint: one crisp glint on the body, snow drifting down the silhouette, a
                // frost ripple underfoot, then a settle of icy sparkles.
                _fx.Emit(ParticleKind.Glint, body + new Vector2(0f, -10f), new Vector4(0.9f, 0.98f, 1f, 0.95f), 30f);
                _fx.Burst(ParticleKind.Flake, body + new Vector2(0f, -55f), 9, new Vector4(0.8f, 0.94f, 1f, 0.95f), 48f);
                _fx.Emit(ParticleKind.Ring, feet + new Vector2(0f, -10f), new Vector4(0.72f, 0.9f, 0.97f, 0.85f), 84f);
                Queue(0.18f, () => _fx.Burst(ParticleKind.Sparkle,
                    AnchorLocal256("body"), 7, new Vector4(0.78f, 0.93f, 1f, 0.95f), 44f));
                break;

            case "wind":
                // Galeswirl: swoosh streaks orbiting the body on both depth layers, stacked from
                // the crown down to a pair skimming the floor, so the pet stands inside a column
                // of wind rather than wearing a belt of it.
                _fx.Emit(ParticleKind.Gust, body, new Vector4(0.62f, 0.88f, 0.55f, 0.85f), 46f);
                _fx.Emit(ParticleKind.Gust, body + new Vector2(0f, -14f), new Vector4(0.72f, 0.94f, 0.62f, 0.7f), 62f, behind: true);
                _fx.Emit(ParticleKind.Gust, new Vector2(128f, 214f), new Vector4(0.66f, 0.9f, 0.58f, 0.75f), 52f);
                _fx.BurstRadial(ParticleKind.Mote, body, 8, new Vector4(0.62f, 0.88f, 0.55f, 0.8f), 36f, 60f);
                Queue(0.1f, () =>
                {
                    _fx.Emit(ParticleKind.Gust, AnchorLocal256("body") + new Vector2(0f, 6f),
                        new Vector4(0.66f, 0.9f, 0.58f, 0.8f), 56f);
                    _fx.Emit(ParticleKind.Gust, new Vector2(128f, 234f),
                        new Vector4(0.6f, 0.86f, 0.52f, 0.7f), 44f, behind: true);
                });
                Queue(0.2f, () =>
                {
                    _fx.Emit(ParticleKind.Gust, AnchorLocal256("body") + new Vector2(0f, -38f),
                        new Vector4(0.72f, 0.94f, 0.62f, 0.7f), 40f);
                    _fx.Emit(ParticleKind.Gust, new Vector2(128f, 224f),
                        new Vector4(0.64f, 0.9f, 0.56f, 0.75f), 60f);
                });
                Queue(0.32f, () => _fx.Emit(ParticleKind.Ring,
                    new Vector2(128f, 244f), new Vector4(0.62f, 0.88f, 0.55f, 0.55f), 100f));
                break;

            case "earth":
                // Stonemote: the ground heaves. A hard ripple and dust at the feet, a spray of
                // chips, and three heavier stones that lumber up and come down with their own
                // wider thud (the impact bloom is sized by what landed).
                _fx.Emit(ParticleKind.Ring, feet, new Vector4(0.82f, 0.68f, 0.42f, 0.85f), 66f);
                _fx.Burst(ParticleKind.Glow, feet + new Vector2(0f, -10f), 3,
                    new Vector4(0.78f, 0.66f, 0.48f, 0.4f), 36f, behind: true);
                _fx.Burst(ParticleKind.Pebble, feet + new Vector2(0f, -8f), 9,
                    new Vector4(0.72f, 0.55f, 0.34f, 0.95f), 28f);
                _fx.Burst(ParticleKind.Pebble, feet + new Vector2(0f, -10f), 3,
                    new Vector4(0.58f, 0.44f, 0.28f, 1f), 16f, sizeScale: 2.2f);
                _fx.Burst(ParticleKind.Mote, feet + new Vector2(0f, -14f), 6,
                    new Vector4(0.85f, 0.75f, 0.55f, 0.6f), 46f);
                Queue(0.14f, () =>
                {
                    _fx.Burst(ParticleKind.Pebble, new Vector2(128f, 236f), 4,
                        new Vector4(0.6f, 0.46f, 0.3f, 0.9f), 22f);
                    _fx.Emit(ParticleKind.Ring, new Vector2(128f, 246f),
                        new Vector4(0.8f, 0.66f, 0.4f, 0.5f), 104f);
                });
                Queue(0.3f, () => _fx.Burst(ParticleKind.Mote,
                    new Vector2(128f, 240f), 5, new Vector4(0.86f, 0.77f, 0.58f, 0.45f), 62f));
                break;

            case "lightning":
                // Crackle: the charge runs the whole creature, not just its crown. A white pop at
                // the core, forked strikes stacked head to feet, and sparks thrown off the
                // silhouette that fade white to violet, then an afterglow.
                _fx.Emit(ParticleKind.Glint, body, new Vector4(1f, 1f, 1f, 0.95f), 26f);
                _fx.Burst(ParticleKind.Bolt, body + new Vector2(0f, -34f), 2, new Vector4(0.85f, 0.78f, 1f, 1f), 30f);
                _fx.Burst(ParticleKind.Bolt, body + new Vector2(0f, 30f), 2, new Vector4(0.82f, 0.72f, 1f, 1f), 36f);
                _fx.BurstRadial(ParticleKind.Spark, body, 12, new Vector4(0.97f, 0.94f, 1f, 1f), 40f, 170f,
                    colorEnd: new Vector4(0.68f, 0.55f, 0.95f, 0.85f));
                Queue(0.1f, () =>
                {
                    _fx.Burst(ParticleKind.Bolt, AnchorLocal256("head"), 2, new Vector4(0.9f, 0.82f, 1f, 1f), 32f);
                    _fx.Burst(ParticleKind.Bolt, new Vector2(128f, 212f), 2, new Vector4(0.8f, 0.7f, 1f, 1f), 30f);
                    _fx.BurstRadial(ParticleKind.Spark, AnchorLocal256("body"), 7,
                        new Vector4(0.95f, 0.9f, 1f, 1f), 32f, 140f, colorEnd: new Vector4(0.68f, 0.55f, 0.95f, 0.8f));
                });
                Queue(0.2f, () =>
                {
                    _fx.Burst(ParticleKind.Burst, AnchorLocal256("body"), 6,
                        new Vector4(0.73f, 0.62f, 0.95f, 0.85f), 34f);
                    _fx.Emit(ParticleKind.Glint, new Vector2(128f, 218f), new Vector4(0.9f, 0.86f, 1f, 0.85f), 20f);
                });
                break;

            case "water":
                // Ripple: a droplet fountain off the crown that arcs, rains down and splashes into
                // its own ripples at the feet (the droplets carry the splash themselves), over a
                // first ripple underfoot.
                _fx.Emit(ParticleKind.Ring, feet, new Vector4(0.5f, 0.74f, 0.95f, 0.85f), 80f);
                _fx.Burst(ParticleKind.Droplet, head + new Vector2(0f, -6f), 12,
                    new Vector4(0.5f, 0.74f, 0.95f, 0.95f), 22f);
                Queue(0.16f, () => _fx.Burst(ParticleKind.Droplet,
                    AnchorLocal256("body"), 6, new Vector4(0.62f, 0.82f, 0.98f, 0.9f), 24f));
                break;
        }
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

        foreach (var def in _wornBehind)
        {
            if (_catalogue is not null)
            {
                _draw.DrawAccessory(dl, textures, _catalogue.AccessoryImagePath(def), def,
                    bottomCentre, size, pose.CellIndex, pose.Scale, pose.Offset, pose.FlipX);
            }
        }

        var tints = _palette is { } palette
            ? new CoreTints(palette.BodyColor, palette.AccentColor, palette.EyeColor)
            : Screens.PetTints.Dawn;
        _draw.Draw(dl, textures, bottomCentre, size, pose.CellIndex, tints, pose.Scale, pose.Offset,
            null, pose.FlipX);
        _draw.DrawMouth(dl, bottomCentre, size, pose.CellIndex, pose.Scale, pose.Offset, pose.FlipX, _mouth.Current);

        foreach (var def in _wornFront)
        {
            if (_catalogue is not null)
            {
                _draw.DrawAccessory(dl, textures, _catalogue.AccessoryImagePath(def), def,
                    bottomCentre, size, pose.CellIndex, pose.Scale, pose.Offset, pose.FlipX);
            }
        }

        _fx.Draw(dl, bottomCentre, size, behind: false);
    }

    /// <summary>How far the worn look reaches past the pet's own square, as fractions of the pet
    /// size (left, up, right, down). The floating window folds this into its canvas so a lance
    /// is never clipped, and the wider flank is paid on both sides because the pet flips.</summary>
    public Vector4 AccessoryFootprint()
    {
        var manifest = _assets?.Manifest;
        if (manifest is null || _catalogue is null)
        {
            return Vector4.Zero;
        }

        var key = $"{_loadedFolder}|{_wornBehind.Count}|{_wornFront.Count}|{string.Join(',', WornNames())}";
        if (key == _footprintKey)
        {
            return _footprint;
        }

        const float BoopWiden = 1.12f;
        const float HopLateral = 42f / 256f;
        const float HopRise = 30f / 256f;

        float left = 0f, up = 0f, right = 0f, down = 0f;
        void Measure(AccessoryDef def)
        {
            var scale = manifest.SlotScaleFor(def.Slot) / 256f;
            if (!manifest.Anchors.TryGetValue(def.Anchor, out var cells))
            {
                return;
            }
            for (var i = 0; i < cells.Count; i++)
            {
                if (cells[i].Length < 2)
                {
                    continue;
                }
                var anchor = new Vector2(cells[i][0], cells[i][1]) / manifest.Cell;
                var origin = def.OriginPoint * scale;
                var min = anchor - new Vector2(origin.X, origin.Y);
                var max = min + (new Vector2(def.Width, def.Height) * scale);
                left = MathF.Max(left, -min.X * BoopWiden);
                right = MathF.Max(right, (max.X - 1f) * BoopWiden);
                up = MathF.Max(up, -min.Y);
                down = MathF.Max(down, max.Y - 1f);
            }
        }

        foreach (var def in _wornBehind)
        {
            Measure(def);
        }
        foreach (var def in _wornFront)
        {
            Measure(def);
        }

        // The pet flips, so the wider flank is paid on both sides; the hop adds its own travel.
        var flank = MathF.Max(left, right) + HopLateral;
        _footprint = new Vector4(flank, up + HopRise, flank, MathF.Max(0f, down));
        _footprintKey = key;
        return _footprint;
    }

    private IEnumerable<string> WornNames()
    {
        foreach (var def in _wornBehind)
        {
            yield return def.Name;
        }
        foreach (var def in _wornFront)
        {
            yield return def.Name;
        }
    }

    private void Queue(float delay, Action fire) => _fxQueue.Add((delay, fire));

    /// <summary>An anchor of the worn form in the particle pool's 256 design space.</summary>
    private Vector2 AnchorLocal256(string anchor)
    {
        var manifest = _assets?.Manifest;
        if (manifest is null || _animator is null)
        {
            return new Vector2(128f, 150f);
        }
        return manifest.AnchorForCell(anchor, _animator.GetPose().CellIndex) * (256f / manifest.Cell);
    }
}
