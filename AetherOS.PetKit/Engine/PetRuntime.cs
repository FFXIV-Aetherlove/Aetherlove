using System;
using System.Collections.Generic;
using System.Numerics;
using AetherLove.Shared.Aetherling;
using AetherOS.PetKit.Rendering;
using AetherOS.PetKit.Rendering.LineArt;

using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;

namespace AetherOS.PetKit.Engine;

/// <summary>The one live creature. Its page inside the phone and its floating window outside both draw it,
/// so it cannot belong to either: two owners each advancing it would have it blinking and hopping at double
/// speed on every frame they are both up. Since the growth release it also owns the worn look, the dynamic
/// mouth and the flourish playback, for the same reason: every surface must read the same pet.</summary>
public sealed class PetRuntime
{
    private const string DefaultPaletteName = "Dawn";

    private readonly ParticleFx _fx = new();
    private readonly MoodTracker _mood = new();
    private readonly MouthController _mouth = new();

    /// <summary>The eye track player, the mouth's twin: blends over the clip's own eye rather
    /// than replacing it.</summary>
    private readonly EyeController _eyes = new();
    private readonly List<(float Delay, Action Fire)> _fxQueue = [];

    private CoreAssets? _assets;
    private CoreDraw? _draw;
    private AnimationController? _animator;
    private PetCatalogue? _catalogue;
    private string? _loadedFolder;
    private int _lastTickFrame = -1;
    private double _lastTickTime;

    private Palette? _palette;
    /// <summary>Seconds left of an emote that asked for the held items to be put down.</summary>
    private float _armsStowLeft;

    private readonly List<AccessoryDef> _wornBehind = [];
    private readonly List<AccessoryDef> _wornFront = [];

    /// <summary>The ears/tail animation stacks. One rig per runtime, because a runtime IS a
    /// creature; two creatures sharing one rig would wag in lockstep.</summary>
    private readonly Rendering.PartsRig _parts = new();
    /// <summary>The rig behind a worn strand pair (the Antennae): one per runtime, ticked with the body so
    /// the stalks swing with the hop. Idle when nothing worn declares strands.</summary>
    private readonly Rendering.TentacleFx _wornStrands = new();

    /// <summary>The flown-item sim, live whether or not a kite is worn: with no pin it parks.</summary>
    private readonly Rendering.KiteFx _kite = new();

    /// <summary>The rig behind the shell's OWN strand anatomy (a jelly's tendrils, a crab's
    /// legs), one per runtime like every rig here. Idle when the worn manifest declares none.</summary>
    private readonly Rendering.TentacleFx _shellStrands = new();

    /// <summary>The Reaching: the code-drawn limbs, one rig per runtime. Gated on adulthood
    /// through <see cref="HandsEnabled"/> each tick; off, the render is byte-identical to the
    /// limbless path.</summary>
    private readonly Rendering.HandFx _hands = new();

    /// <summary>The one switch that kills the limbs everywhere, kept beside the rig so turning
    /// the feature off is one line. The per-pet gate (adulthood) composes with it in Tick.</summary>
    public bool HandsEnabled { get; set; } = true;

    /// <summary>An adult body: any form that is not a hatchling rung or the ceremony crystal.
    /// The limbs arrive with adulthood, continuing the growth line rather than pre-empting it.</summary>
    private bool AdultForm => _loadedFolder is not (null
        or CoreAssets.HatchlingFolder or CoreAssets.Hatchling2Folder or CoreAssets.Hatchling3Folder
        or CoreAssets.CeremonyFolder);

    /// <summary>Drives the limbs from a caller's OWN clock rather than an emote's: a race gait is
    /// synced to distance, and winding a seconds-based track over it puts the arms out of step with
    /// the legs at every change of pace. Call after <see cref="Tick"/> and before <see cref="Draw"/>;
    /// the hands take the limb's shape and its length limit but not the follow spring or the drift,
    /// which are seconds-based motions this path exists to avoid.</summary>
    public void DriveHands(HandsDelta delta) => _hands.DriveExternal(delta);

    /// <summary>The drawn body's canvas and spring. One spring per runtime because a runtime IS
    /// a creature, and a shared spring makes two pets breathe together; the spring is the one
    /// thing in the drawn-shell system with memory, so it needs real seconds and a tick stamp.</summary>
    private readonly LineCanvas _lineCanvas = new();
    private readonly LineShell.LineMotion _lineMotion = new();
    private float _lineDt;
    private long _lineTick;
    private int _phaseSeed;

    /// <summary>The solved drawn-body channels, memoized per rendered frame: the pins behind the
    /// creature are placed before the body draws, so the channels must exist before the draw
    /// begins, and every surface asking in one frame must get one answer.</summary>
    private int _lineSolvedFrame = -1;
    private LineShell.Channels _lineBody;
    private LineShell.Channels _lineTrim;
    private int _lineCell;
    private int _lineNext;
    private float _linePhase;

    /// <summary>The face this frame, solved once beside the body: the shell's own pose-table eye
    /// and blush, then whatever mood, drowsiness or a playing emote has made of it.</summary>
    private LineShell.EyeState _lineEye = LineShell.Open;

    private float _lineBlush;
    private string _equippedReaction = "";
    private readonly List<ReactionDef> _boopPool = [];
    private HashSet<string>? _ownedReactions;
    private string[] _disabledReactions = [];
    private float _reactionLeft;
    private readonly GlyphController _glyphs = new();
    private float _glyphCooldown;
    private float _glyphAmbientCooldown;
    private bool _reduceMotion;

    private string _footprintKey = "";
    private Vector4 _footprint;

    public bool Ready => _draw is not null && _animator is not null;

    /// <summary>The living layer's tick, invoked with this frame's dt from inside the frame guard, so
    /// whichever surface draws the creature keeps its world-watching alive without its own plumbing.</summary>
    public Action<float>? OnTick { get; set; }

    public bool Napping => _animator?.Napping ?? false;

    /// <summary>The choreography playing right now, or null.</summary>
    public EmoteDef? CurrentEmote => _animator?.CurrentEmote;

    /// <summary>A performance may start: awake, idle-handed, and motion not reduced. The player emoting
    /// nearby is not the player asking the pet for anything, so a no here is a silent no.</summary>
    public bool CanPerformEmote => !_reduceMotion && _animator is { Napping: false, CurrentEmote: null };

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
        if (_phaseSeed != 0)
        {
            _animator.PrimePhase(_phaseSeed);
        }
        _catalogue ??= PetCatalogue.Load();
        _footprintKey = "";
    }

    /// <summary>Starts this creature somewhere else in its own idle, from anything stable: a
    /// pet's name, a race slot. Two pets of the same shell breathing in perfect unison is the
    /// single most artificial thing a roster of them can do; one seeded offset removes the cause.
    /// Survives a form reload, so set it once when the creature spawns.</summary>
    public void SetPhaseSeed(string key)
    {
        var h = 2166136261u;
        foreach (var ch in key)
        {
            h = (h ^ ch) * 16777619u;
        }

        var seed = (int)(h & 0x7FFFFFFF);
        if (seed == 0)
        {
            seed = 1;
        }
        // Callers may repeat themselves every frame; re-priming the same seed would hold the
        // clip hostage on its seeded step.
        if (seed == _phaseSeed)
        {
            return;
        }
        _phaseSeed = seed;
        _animator?.PrimePhase(_phaseSeed);
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

        // Kept for the drawn body's spring, the one thing on the pet surface with memory: it
        // needs real seconds rather than a frame count, and the tick stamp is what lets a
        // creature drawn more than once in a frame move exactly once.
        _lineDt = dt;
        _lineTick++;

        _reduceMotion = reduceMotion;
        if (_animator is not null)
        {
            _animator.ReduceMotion = reduceMotion;

            // Re-read every tick rather than cached, so a shell swap takes effect on the frame
            // it happens.
            _animator.SuppressEyeCellSwap =
                _assets?.Manifest is { } lineManifest && LineArtDispatch.ShellFor(lineManifest.Skin) != 0;
            _animator.Update(dt);
            _mouth.SetBase(MouthShapes.For(_animator.CurrentAnimation, Mood));
        }
        _mood.Update(dt);
        _mouth.Update(dt);
        _eyes.Update(dt);
        _fx.Update(dt);
        _reactionLeft = MathF.Max(0f, _reactionLeft - dt);
        _armsStowLeft = MathF.Max(0f, _armsStowLeft - dt);
        _glyphs.Update(dt, reduceMotion);
        _glyphCooldown = MathF.Max(0f, _glyphCooldown - dt);
        _glyphAmbientCooldown = MathF.Max(0f, _glyphAmbientCooldown - dt);

        // The parts read the pet's CURRENT ANIMATION as a mood word, never a hidden stat: the
        // creature that naps has a sleepy tail, and a boop wags it.
        if (_animator is not null)
        {
            // A playing emote outranks the clip: an emote is the pet saying something, where a
            // clip is only what it happens to be doing.
            var partsMood = _animator.CurrentEmote is { Parts.Length: > 0 } emotingParts
                ? emotingParts.Parts
                : _animator.CurrentAnimation switch
            {
                "nap" => "sleepy",
                "hop" or "boop" => "happy",
                _ => "idle",
            };
            // Decorated before the rigs read it, so anything hanging off the creature is simulated
            // from the pin it is DRAWN on. A drawn body's strand seats used to come off the baked
            // per-cell table: a signal that steps eight times a second, at the sheet-era position,
            // carrying none of the swing or the spring the body actually has. The rig turns seat
            // motion into swing, so it was being kicked by impulses that did not match the drawing,
            // which is what made a hanging cord's weight lurch under a lantern that was gliding.
            var pose = DecoratedPose(_animator.GetPose());
            _parts.Update(dt, reduceMotion, partsMood, _assets?.Manifest, pose.CellIndex, pose.Offset.Y);

            // An emote's own hand track rides through whatever the body is doing: the two halves
            // are one performance. A real body clip with no emote behind it cancels a stray
            // track, so hands can never ride a hop they were not choreographed with.
            if (_animator.CurrentAnimation is not ("idle" or "blink") && _animator.CurrentEmote == null)
            {
                _hands.Cancel();
            }

            // The arm this shell draws, re-read every tick rather than cached: a shell swap must
            // take effect on the frame it happens, and the row carries the follow spring's
            // looseness and the water's amplitude, both about to be integrated. The hands go
            // before the flown item's rig, so a kite yanked by a wave feels the wave.
            _hands.Enabled = HandsEnabled && AdultForm;
            if (_assets?.Manifest is { } handManifest)
            {
                _hands.Style = handManifest.HandStyle;
            }
            _hands.Update(dt, reduceMotion);

            _kite.Update(dt, reduceMotion, KitePin256(pose));
            // The morph on the strand fans: Ripple is throw, Tip is lean, Rate is clock speed.
            // Rate scales the seconds rather than the clock; the fan is one integrated clock,
            // and multiplying it would teleport every crest along the strand.
            var strandMorph = _animator.CurrentMorph;
            var strandAmp = MathF.Max(0f, 1f + strandMorph.Ripple);
            var strandLean = strandMorph.Tip * 0.35f;
            var strandDt = dt * MathF.Max(0f, 1f + strandMorph.Rate);
            _wornStrands.AmpScale = strandAmp;
            _wornStrands.LeanBias = strandLean;
            _shellStrands.AmpScale = strandAmp;
            _shellStrands.LeanBias = strandLean;
            _wornStrands.Update(strandDt, reduceMotion, WornStrandSeat(pose));
            _shellStrands.Update(strandDt, reduceMotion, ShellStrandSeat(pose));
        }

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

        OnTick?.Invoke(dt);
    }

    public PetPose Pose => _animator?.GetPose() ?? new PetPose { Scale = Vector2.One };

    /// <summary>A visiting companion stands in somebody else's spot: its nook furniture stays home.
    /// Set before the look is applied.</summary>
    public bool SuppressNook { get; set; }

    /// <summary>A visiting companion feels whatever its owner's own creature feels: same interaction
    /// clock, same warmth, same nap. Called every frame after <see cref="Tick"/>, so its own timers
    /// never diverge from the creature it is mirroring.</summary>
    public void MimicFrom(PetRuntime source)
    {
        if (_animator is null || source._animator is null)
        {
            return;
        }
        _mood.CopyFrom(source._mood);
        _animator.MimicInteractionClock(source._animator.SinceInteraction);
        _animator.MimicNap(source._animator.Napping);
    }

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
            if (SuppressNook && def.Slot == AccessoryDef.NookSlot)
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
            _fx.Burst(ParticleKind.Sparkle, new Vector2(128f, 150f), 8, CrystalPale, 60f);
        }

        // Not every boop speaks: the squish is the response, and a symbol on every tap would flatten
        // both. A boop that reaches Beaming has genuinely changed something and gets the burst.
        if (Mood == MoodLevel.Beaming)
        {
            ShowGlyph("burst");
        }
        else if (Random.Shared.NextSingle() < 0.34f)
        {
            ShowGlyph("heart");
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
        _fx.Burst(ParticleKind.Sparkle, new Vector2(128f, 150f), 16, CrystalPale, 90f);
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
    public void PlayFeedLand(Vector4 accent, bool reduceMotion, string elementKey = "")
    {
        // The one saying the spec draws as its legal example: crystal then burst, "that was lovely",
        // backwards at something that already happened, which is thanks. Never shown before a feed.
        ShowGlyph("crystal", "burst", elementKey);
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

    /// <summary>Seconds before another glyph may be shown at all. A glyph is an event; two in a row is a
    /// stream, and a stream is a UI.</summary>
    private const float GlyphGapSeconds = 5.5f;

    /// <summary>Seconds before an AMBIENT glyph may be shown, well above the idle-variant scheduler's own
    /// cadence so a glyph never becomes wallpaper.</summary>
    private const float GlyphAmbientGapSeconds = 48f;

    /// <summary>Plays a learned (or practised) choreography with its mouth track. Amplitude below 1 is
    /// the practice attempt: the same curves at reduced excursion with an unsure mouth. Refused while
    /// napping, mid-emote or under reduce-motion; the refusal is silent and the caller never asks.</summary>
    public bool PlayEmote(EmoteDef def, float amplitude = 1f, bool force = false)
    {
        if (_animator is null || (!force && !CanPerformEmote))
        {
            return false;
        }
        _animator.PlayEmote(def, amplitude);
        // The hands' half of the choreography, at the body's own excursion. HandFx declines on
        // its own when there are no hands to move, which is why this line has no condition on it.
        _hands.PlayEmote(def, amplitude);
        if (def.StowArms)
        {
            // Extend rather than restart, so a second stowing emote cannot flash the weapon back
            // for a frame in the middle of the first.
            _armsStowLeft = MathF.Max(_armsStowLeft, def.Seconds);
        }
        if (amplitude < 1f)
        {
            _mouth.Play([new MouthKey(0f, "hmm", 0.3f)], def.Seconds);
        }
        else if (def.Mouth.Length > 0)
        {
            _mouth.Play(def.Mouth, def.Seconds);
        }

        // The eyes, on the same clock as the mouth and under the same rule: a practice attempt
        // keeps its own uncertain face rather than performing the real one badly.
        if (amplitude >= 1f && def.Eyes.Length > 0)
        {
            _eyes.Play(def.Eyes, def.Seconds);
        }

        // Offered rather than played: ShowGlyph keeps its own gates.
        if (amplitude >= 1f && def.Glyph.Length > 0)
        {
            ShowGlyph(def.Glyph);
        }
        // Garnish only on the real thing: a clumsy 60% attempt that sprays tears and sparkles is not a
        // creature failing to copy you, it is a creature succeeding loudly.
        if (amplitude >= 1f && !_reduceMotion)
        {
            PlayEmoteGarnish(def);
        }
        return true;
    }

    /// <summary>The particles and glyphs a few choreographies need to read at all: tears for a sob,
    /// notes for a hum, a bang for the moment it works something out. The body track is the emote; this
    /// is what makes it legible, so it is keyed by emote rather than baked into the curves.
    ///
    /// <para>Only the ones that need it are listed. An emote whose body already says everything gets
    /// nothing here on purpose, because garnish on all fifty would be weather rather than expression.</para></summary>
    private void PlayEmoteGarnish(EmoteDef def)
    {
        var head = AnchorLocal256("head");
        var body = AnchorLocal256("body");

        switch (def.Key)
        {
            case "cry":
                for (var i = 0; i < 3; i++)
                {
                    Queue(0.35f + (0.55f * i), () => _fx.Burst(ParticleKind.Droplet,
                        AnchorLocal256("face") + new Vector2(0f, 8f), 2,
                        new Vector4(0.60f, 0.76f, 0.87f, 0.9f), 42f));
                }
                break;

            case "blowkiss":
                Queue(0.52f, () => _fx.Burst(ParticleKind.Heart,
                    AnchorLocal256("face") + new Vector2(-26f, -6f), 3,
                    new Vector4(0.92f, 0.43f, 0.34f, 0.9f), 30f));
                break;

            case "blush":
                Queue(0.3f, () => _fx.Emit(ParticleKind.Heart,
                    AnchorLocal256("face"), new Vector4(0.92f, 0.43f, 0.34f, 0.6f), 12f));
                break;

            case "furious":
            case "fume":
                Queue(0.35f, () => _fx.Burst(ParticleKind.Gust,
                    AnchorLocal256("head") + new Vector2(-30f, -6f), 2,
                    new Vector4(0.75f, 0.73f, 0.78f, 0.6f), 20f));
                Queue(0.9f, () => _fx.Burst(ParticleKind.Gust,
                    AnchorLocal256("head") + new Vector2(30f, -6f), 2,
                    new Vector4(0.75f, 0.73f, 0.78f, 0.6f), 20f));
                break;

            case "pray":
            case "vpose":
            case "huzzah":
                _fx.Burst(ParticleKind.Sparkle, body, 6, new Vector4(1f, 0.93f, 0.6f, 0.95f), 60f);
                Queue(0.9f, () => _fx.Burst(ParticleKind.Sparkle,
                    AnchorLocal256("head"), 4, new Vector4(1f, 0.93f, 0.6f, 0.9f), 45f));
                break;

            case "hum":
            case "singalong":
                // The library has a real note; the drop's drifting motes were a stand-in for a pool that
                // had none. Motes still ride along, so the notes have air moving around them.
                Queue(0.35f, () => AuditionGlyph("note"));
                for (var i = 0; i < (def.Key == "hum" ? 2 : 4); i++)
                {
                    var side = i % 2 == 0 ? 34f : -34f;
                    Queue(0.5f + (0.45f * i), () => _fx.Emit(ParticleKind.Mote,
                        AnchorLocal256("head") + new Vector2(side, -10f),
                        new Vector4(0.18f, 0.15f, 0.24f, 0.8f), 8f));
                }
                break;

            case "shiver":
                _fx.Cascade(ParticleKind.Flake, new Vector2(128f, 60f), 5,
                    new Vector4(0.49f, 0.75f, 0.88f, 0.85f), 70f, 26f);
                break;

            case "swelter":
                Queue(0.8f, () => _fx.Emit(ParticleKind.Droplet,
                    AnchorLocal256("head") + new Vector2(34f, 0f),
                    new Vector4(0.60f, 0.76f, 0.87f, 0.9f), 6f));
                break;

            case "sneeze":
                Queue(0.95f, () => _fx.Burst(ParticleKind.Gust,
                    AnchorLocal256("face") + new Vector2(-30f, 10f), 3,
                    new Vector4(0.8f, 0.8f, 0.84f, 0.55f), 24f));
                break;

            case "dizzy":
                _fx.BurstRadial(ParticleKind.Sparkle, head + new Vector2(0f, -20f), 5,
                    new Vector4(1f, 0.9f, 0.5f, 0.9f), 40f, 18f);
                break;

            case "fear":
                Queue(0.35f, () => _fx.Burst(ParticleKind.Gust,
                    AnchorLocal256("body"), 2, new Vector4(0.85f, 0.85f, 0.9f, 0.5f), 40f));
                break;

            case "eureka":
                // The "!" the emote is entirely about. The drop parked this waiting for a glyph; the
                // library already had one.
                Queue(0.85f, () => AuditionGlyph("bang"));
                Queue(0.85f, () => _fx.Emit(ParticleKind.Glint,
                    AnchorLocal256("head") + new Vector2(0f, -40f),
                    new Vector4(0.96f, 0.79f, 0.36f, 1f), 6f));
                break;

        }
    }

    /// <summary>
    /// The ONE way a glyph is ever shown; every trigger goes through here so the rules hold in a single
    /// place. <paramref name="then"/> makes it a saying, capped at two, and a saying narrates the present
    /// or the past and never points at the future: crystal then burst is thanks, heart then crystal is a
    /// demand, and there is no version of this app that ships a demand. Returns whether the pet actually
    /// spoke; a declined glyph is a normal, silent outcome.
    /// </summary>
    public bool ShowGlyph(string name, string? then = null, string element = "", bool ambient = false)
    {
        if (_draw is null || _assets is null)
        {
            return false;
        }
        // Anchor-gated exactly like the dynamic mouth: a sheet-set that declares no head pin never
        // speaks, and it has nothing to say either.
        if (!_assets.Manifest.Anchors.ContainsKey("head"))
        {
            return false;
        }
        if (_glyphCooldown > 0f || (ambient && _glyphAmbientCooldown > 0f))
        {
            return false;
        }
        // A Reaction owns its moment: both bloom at the head over the same half-second, and two things
        // arriving at one anchor is noise rather than expression.
        if (_reactionLeft > 0f)
        {
            return false;
        }

        _glyphs.Show(name, then, element);
        _glyphCooldown = GlyphGapSeconds;
        if (ambient)
        {
            _glyphAmbientCooldown = GlyphAmbientGapSeconds;
        }
        return true;
    }

    /// <summary>The wardrobe's audition and the once-ever moments (a first sighting, a eureka): shows a
    /// glyph NOW, clearing the gaps first. A moment that can never come again must not be lost to a rate
    /// limit; everything structural the gate checks still applies.</summary>
    public void AuditionGlyph(string name, string? then = null, string element = "")
    {
        _glyphCooldown = 0f;
        _glyphAmbientCooldown = 0f;
        _reactionLeft = 0f;
        ShowGlyph(name, then, element);
    }

    /// <summary>Draws whatever the creature is currently saying, above its own head anchor. Called per
    /// surface rather than from inside <see cref="Draw"/>, because the floating pet hands in the
    /// FOREGROUND list where a symbol needs no window headroom and takes no clicks.</summary>
    public void DrawGlyph(ImDrawListPtr dl, Vector2 bottomCentre, float size, bool bubbleFrame)
    {
        if (!_glyphs.Playing || _draw is null || _animator is null || _assets is null)
        {
            return;
        }
        var manifest = _assets.Manifest;
        if (!manifest.Anchors.ContainsKey("head"))
        {
            return;
        }

        var pose = DecoratedPose(_animator.GetPose());
        var head = _draw.AnchorScreen("head", bottomCentre, size, pose);
        var shape = _glyphs.Current;

        var ink = manifest.InkFor(_palette?.BodyColor ?? Vector4.One);
        if (ink.W <= 0f)
        {
            ink = MouthDraw.DefaultLine;
        }

        var accent = _palette?.AccentColor ?? new Vector4(0.55f, 0.78f, 0.95f, 1f);
        accent.W = 1f;

        var fill = GlyphFill(shape, _glyphs.CurrentElement);

        // The light wears the pet's colours (it IS the pet's light); the bubble stays a warm off-white,
        // the register the game's own bubbles live in.
        var halo = bubbleFrame
            ? BubbleFill
            : new Vector4(
                MathF.Min(1f, accent.X + 0.16f),
                MathF.Min(1f, accent.Y + 0.16f),
                MathF.Min(1f, accent.Z + 0.16f),
                0.9f);

        GlyphDraw.Draw(
            dl, head, size / 256f,
            bubbleFrame ? GlyphFrame.Bubble : GlyphFrame.Light,
            shape, _glyphs.Alpha, _glyphs.Reveal, _glyphs.Lift, ink, fill, halo);
    }

    /// <summary>The fill a glyph wears: the creature's own accent for a feeling, the element's colour for
    /// an element, a pale neutral for a report, nothing for the two marks. Public because a picker that
    /// shows glyphs has to show them in the colours the creature would actually say them in.</summary>
    public Vector4 GlyphFill(in GlyphShape shape, string element = "")
    {
        var accent = _palette?.AccentColor ?? new Vector4(0.55f, 0.78f, 0.95f, 1f);
        accent.W = 1f;
        return shape.Tint switch
        {
            GlyphTint.Accent => accent,
            GlyphTint.Element => ElementColour(element, accent),
            GlyphTint.Neutral => NeutralFill,
            _ => new Vector4(0f, 0f, 0f, 0f),
        };
    }

    /// <summary>World glyphs stay neutral: the pet is reporting, not feeling.</summary>
    private static readonly Vector4 NeutralFill = new(0.84f, 0.88f, 0.90f, 1f);

    private static readonly Vector4 CrystalPale = new(0.812f, 0.992f, 0.973f, 1f);

    private static readonly Vector4 BubbleFill = new(0.97f, 0.955f, 0.90f, 0.95f);

    /// <summary>An element's own colour, from the game's colour language. Light and dark are carried here
    /// rather than in <see cref="Elements"/> deliberately: the wheel stays six, and they are a third axis
    /// that must never read as a seventh leaning.</summary>
    private static Vector4 ElementColour(string key, Vector4 fallback)
    {
        if (key == "light")
        {
            return new Vector4(0.93f, 0.91f, 0.78f, 1f);
        }
        if (key == "dark")
        {
            return new Vector4(0.54f, 0.42f, 0.85f, 1f);
        }
        foreach (var def in Elements.All)
        {
            if (def.Key == key)
            {
                return def.Accent;
            }
        }
        return fallback;
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

    /// <summary>The hop with a single turn laid over it.</summary>
    public void PlayTurn() => _animator?.PlayTurn();

    /// <summary>Where the worn flown item's pin sits this frame, unflipped 256-space: the
    /// per-cell anchor plus the pose's own lift. Null while nothing flown is worn, which parks
    /// the sim's trail rather than feeding it zeroes.</summary>
    private Vector2? KitePin256(PetPose pose)
    {
        var manifest = _assets?.Manifest;
        if (manifest is null)
        {
            return null;
        }
        foreach (var def in _wornFront)
        {
            if (def.Fx == "kite")
            {
                var pin = (manifest.AnchorFor(def.Anchor, pose) * (256f / manifest.Cell))
                          + pose.Offset;
                // A hand-anchored flown item rides the hand, after the hands update, so it
                // feels this frame's wave rather than last frame's.
                if (_hands is { Enabled: true } && _hands.TryGet(def.Anchor, out var ride, out _))
                {
                    pin += ride;
                }
                return pin;
            }
        }
        return null;
    }

    /// <summary>Solves the drawn body's channels for this frame, memoized so every surface asking
    /// in one frame gets one answer, and returns the drawn-shell id (0 for a sheet body). Solved
    /// before any draw begins because the pins behind the creature are placed before the body,
    /// and they have to be able to ask the shell where they are. The authored pose, then mood on
    /// top of it, then the shell's own material: mood changes what the pet is TRYING to hold, and
    /// the material decides how it gets there.</summary>
    private int SolveLine(PetPose pose)
    {
        if (_assets?.Manifest is not { } manifest || _animator is null)
        {
            return 0;
        }
        var shell = LineArtDispatch.ShellFor(manifest.Skin);
        if (shell == 0)
        {
            return 0;
        }

        var frame = ImGui.GetFrameCount();
        if (frame == _lineSolvedFrame)
        {
            return shell;
        }
        _lineSolvedFrame = frame;

        // Every one of these comes off the POSE, not off the controller: GetPose applies the
        // drowsy eye-cell substitution and pins the blend to it, and asking the controller for
        // the running clip's numbers instead is what a lid flickering half-shut every frame
        // period looks like.
        _lineCell = pose.CellIndex;
        _lineNext = pose.NextCellIndex;
        _linePhase = pose.FramePhase;

        var target = LineShell.WithMood(
            LineArtDispatch.PoseAt(shell, pose.PrevCellIndex, _lineCell, _lineNext, pose.AfterCellIndex, _linePhase),
            Mood);

        // The emote, as posture rather than as a transform on the finished picture: composed
        // after mood and before the material, so an emote wobbles on a jelly and snaps on chitin.
        target = LineShell.WithEmote(target, _animator.CurrentMorph);

        // The beat advances at the pose's OWN rate, so a top that is heavy lidded keeps turning
        // and simply turns slower; which channels the clip actually animates is decided from the
        // pose table rather than from the clip's name, which is what keeps a wingbeat alive
        // through a blink.
        var beat = _lineMotion.Advance(target, _lineDt, _lineTick);
        target = LineArtDispatch.WithAmbient(shell, target, _animator.CurrentFrames.ToArray(), beat,
            out var clipDrives, out var driven);
        _lineMotion.RecordDriven(clipDrives, driven);
        (_lineBody, _lineTrim) = _lineMotion.Step(target, LineArtDispatch.StuffFor(shell), _lineDt, _lineTick);

        // The face, solved out here so nothing in LineArt/ knows what a cell is.
        (_lineEye, _lineBlush) = LineArtDispatch.FaceAt(shell, _lineCell, _lineNext, _linePhase);

        // Drowsiness as a lid, never a swapped cell: the swap would pin the pose spline and
        // stop the creature breathing while it got sleepy.
        if (_animator?.DrowsyEye is { } drowsy && EyeStates.Find(drowsy) is { } drowsyEye)
        {
            _lineEye = drowsyEye;
        }

        // The blink, on the lid: whichever has the eye further shut wins, so a drowsy pet still
        // blinks and a blink cannot open a heavy lid halfway through itself.
        if (_animator?.BlinkLid is > 0f and var blinkLid && blinkLid > _lineEye.Lid)
        {
            _lineEye = _lineEye with { Lid = blinkLid, Straight = false };
        }

        // Then the emote's eye track over the top, owning the eye only as far as it has faded in.
        _lineEye = _eyes.Over(_lineEye);

        // The emote's blush adds to the shell's own rather than replacing it.
        _lineBlush = Math.Clamp(_lineBlush + (_animator?.CurrentMorph.Blush ?? 0f), 0f, 1f);
        return shell;
    }

    /// <summary>Wires the pose to the drawn body, when there is one: pins flow between cells and
    /// ask the shell where they are, so everything worn rides the body as drawn this frame,
    /// spring and all. A sheet body returns the pose untouched.</summary>
    private PetPose DecoratedPose(PetPose pose)
    {
        var shell = SolveLine(pose);
        if (shell == 0 || _assets?.Manifest is not { } manifest)
        {
            return pose;
        }

        pose.SmoothAnchors = true;
        var ch = _lineBody;
        pose.DrawnAnchor = name => manifest.Anchors.ContainsKey(name)
            ? LineArtDispatch.Pin(shell, name, manifest.AnchorForCell(name, manifest.RestCell ?? 0), ch)
            : null;
        return pose;
    }

    /// <summary>Draws the creature as dressed. <paramref name="props"/> false leaves the nook and the
    /// banner at home for a surface too small for furniture, without touching what is worn.</summary>
    public void Draw(ImDrawListPtr dl, ITextureCache textures, Vector2 bottomCentre, float size, PetPose pose,
        bool props = true)
    {
        if (_draw is null)
        {
            return;
        }

        var shell = SolveLine(pose);
        pose = DecoratedPose(pose);

        _fx.Draw(dl, bottomCentre, size, behind: true);

        foreach (var def in _wornBehind)
        {
            if (!props && def.Slot is AccessoryDef.NookSlot or AccessoryDef.BannerSlot)
            {
                continue;
            }
            if (def.IsDrawnPart)
            {
                if (_palette is { } partPalette)
                {
                    _draw.DrawPart(dl, def, bottomCentre, size, pose, partPalette, _parts, _wornStrands);
                }
                continue;
            }
            if (_catalogue is not null)
            {
                _draw.DrawAccessory(dl, textures, _catalogue.AccessoryImagePath(def), def,
                    bottomCentre, size, pose);
            }
        }

        // A wrap's FAR half: the body draws between the two halves, and a ring only reads as going round
        // the creature if the creature is drawn inside it. The near half follows in the front pass at its
        // slot's own place, so the two need no ordering rule of their own.
        if (_catalogue is not null)
        {
            foreach (var def in _wornFront)
            {
                if (def.HasWrapBack)
                {
                    _draw.DrawAccessory(dl, textures, _catalogue.AccessoryBackPath(def), def,
                        bottomCentre, size, pose);
                }
            }
        }

        var tints = _palette is { } palette
            ? new CoreTints(palette.BodyColor, palette.AccentColor, palette.EyeColor)
            : PetTints.Dawn;

        // The shell's own strand anatomy, behind the body: a jellyfish is not a bare bell.
        if (_palette is { } strandPalette)
        {
            _draw.DrawShellStrands(dl, bottomCentre, size, pose, strandPalette, _shellStrands);
        }

        // The limbs, behind the body unless the shell's row says front: the root slides under
        // the silhouette and the baked nubs read as the shoulder joints.
        var frontHands = _hands.Enabled && (_assets?.Manifest.HandStyle.Front ?? false);
        if (_hands.Enabled && !frontHands && _palette is { } limbPalette)
        {
            _draw.DrawLimbs(dl, bottomCentre, size, pose, limbPalette, _hands, front: false);
        }

        // The body itself, from geometry when the shell has one and from the sheets when it does
        // not, at exactly this point in the layer order: after the strands and behind items,
        // before the mouth and the front accessories. A drawn body changes how the creature is
        // MADE and nothing about what it is wearing.
        if (shell != 0)
        {
            var local = pose.FlipX ? pose.Offset with { X = -pose.Offset.X } : pose.Offset;
            var at = bottomCentre + (local * (size / 256f));
            var ink = _assets!.Manifest.InkFor(tints.Body);
            if (ink.W <= 0f)
            {
                ink = MouthDraw.DefaultLine;
            }
            LineArtDispatch.Draw(shell, _lineCanvas, dl, at, size, _lineBody, _lineTrim,
                _lineEye, _lineBlush, tints.Body, tints.Accent, tints.Eye, ink,
                pose.Scale, pose.FlipX);
        }
        else
        {
            _draw.Draw(dl, textures, bottomCentre, size, pose.CellIndex, tints, pose.Scale, pose.Offset,
                null, pose.FlipX);
        }
        _draw.DrawMouth(dl, bottomCentre, size, pose, _mouth.Current, tints.Body);

        // The front-of-body limbs, after the face, with the blend that answers the overlap.
        if (frontHands && _palette is { } frontLimbPalette)
        {
            _draw.DrawLimbs(dl, bottomCentre, size, pose, frontLimbPalette, _hands, front: true);
        }

        foreach (var def in _wornFront)
        {
            if (_armsStowLeft > 0f && def.Slot == AccessoryDef.ArmsSlot)
            {
                continue;
            }
            if (!props && def.Slot is AccessoryDef.NookSlot or AccessoryDef.BannerSlot)
            {
                continue;
            }
            if (_catalogue is null)
            {
                continue;
            }
            if (def.Fx == "kite")
            {
                _draw.DrawFlown(dl, textures, _catalogue.AccessoryImagePath(def), def,
                    bottomCentre, size, pose, _kite);
                continue;
            }
            _draw.DrawAccessory(dl, textures, _catalogue.AccessoryImagePath(def), def,
                bottomCentre, size, pose, hands: _hands);
        }

        _fx.Draw(dl, bottomCentre, size, behind: false);
    }

    /// <summary>Where a worn strand pair is sown this frame, in cell pixels with the hop folded in, so the
    /// rig sees the body's whole motion. Null when nothing worn declares strands, which lets it relax.</summary>
    private Vector2? WornStrandSeat(PetPose pose)
    {
        if (_assets?.Manifest is not { } manifest || !manifest.Anchors.ContainsKey("head"))
        {
            return null;
        }
        var wearsStrands = false;
        foreach (var def in _wornBehind)
        {
            wearsStrands |= def.Strands is not null;
        }
        foreach (var def in _wornFront)
        {
            wearsStrands |= def.Strands is not null;
        }
        if (!wearsStrands)
        {
            return null;
        }
        return manifest.AnchorFor("head", pose) + (pose.Offset * (manifest.Cell / 256f));
    }

    /// <summary>Where the shell's own strand anatomy is sown this frame, or null for a shell
    /// that declares none, which lets the rig relax.</summary>
    private Vector2? ShellStrandSeat(PetPose pose)
    {
        if (_assets?.Manifest is not { Strands: { } def } manifest || !manifest.Anchors.ContainsKey(def.Seat))
        {
            return null;
        }
        return manifest.AnchorFor(def.Seat, pose) + (pose.Offset * (manifest.Cell / 256f));
    }

    /// <summary>The bare creature in any palette at any size: no accessories, no effects, the live idle
    /// cell so a board of them bobs in time. The Lumi-Link piece. Reads the runtime, never writes it.</summary>
    public void DrawBare(ImDrawListPtr dl, ITextureCache textures, Vector2 bottomCentre, float size,
        Palette palette, Vector2 scale, float alpha = 1f)
    {
        if (_draw is null || _animator is null)
        {
            return;
        }
        var tints = new CoreTints(
            palette.BodyColor with { W = alpha },
            palette.AccentColor with { W = alpha },
            palette.EyeColor with { W = alpha });
        var pose = _animator.GetPose();
        pose.Scale = scale;
        pose.Offset = Vector2.Zero;
        pose.FlipX = false;

        var shell = SolveLine(pose);
        if (shell != 0 && _assets is { } assets)
        {
            var ink = assets.Manifest.InkFor(tints.Body);
            if (ink.W <= 0f)
            {
                ink = MouthDraw.DefaultLine;
            }
            LineArtDispatch.Draw(shell, _lineCanvas, dl, bottomCentre, size, _lineBody, _lineTrim,
                _lineEye, _lineBlush, tints.Body, tints.Accent, tints.Eye, ink,
                scale, flip: false);
        }
        else
        {
            _draw.Draw(dl, textures, bottomCentre, size, pose.CellIndex, tints, scale, Vector2.Zero, null, false);
        }
        _draw.DrawMouth(dl, bottomCentre, size, pose, _mouth.Current, tints.Body, alpha);
    }

    /// <summary>Every palette the catalogue knows, for surfaces that paint a creature that is not this one.</summary>
    public IReadOnlyList<Palette> Palettes => _catalogue?.Palettes ?? [];

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

        // A code-drawn part has no quad: it is charged as a disc of its own reach around every
        // cell of its anchor, because a swoosh sweeps the tail through the whole circle and the
        // window must hold wherever it lands.
        void MeasurePart(string anchorName, float reach256)
        {
            if (!manifest.Anchors.TryGetValue(anchorName, out var cells))
            {
                return;
            }
            var frac = reach256 / 256f;
            for (var i = 0; i < cells.Count; i++)
            {
                if (cells[i].Length < 2)
                {
                    continue;
                }
                var anchor = new Vector2(cells[i][0], cells[i][1]) / manifest.Cell;
                left = MathF.Max(left, -(anchor.X - frac));
                right = MathF.Max(right, (anchor.X + frac) - 1f);
                up = MathF.Max(up, -(anchor.Y - frac));
                down = MathF.Max(down, (anchor.Y + frac) - 1f);
            }
        }

        void Measure(AccessoryDef def)
        {
            // The shell's fit resizes a drawn part, so the room it reserves has to shrink with it
            // or a fitted shell keeps paying for the model's authored size.
            var (partFit, partOffset, _) = manifest.FitFor(def.Slot, def.Name);
            if (def.Tail is { } tailDef)
            {
                // Length plus a girth's worth of fur, swept anywhere the stack can point it.
                MeasurePart("tail", ((tailDef.Len * 1.15f) + MathF.Abs(tailDef.NudgePoint.Y)) * partFit);
                return;
            }
            if (def.Ears is { } earDef)
            {
                MeasurePart("earL", earDef.Height * 1.2f * partFit);
                MeasurePart("earR", earDef.Height * 1.2f * partFit);
                return;
            }
            if (def.Strands is { } strandDef)
            {
                // Arc length plus the spread, swept anywhere the wave and the swing can point it.
                MeasurePart("head",
                    (strandDef.Len + strandDef.Spread + (strandDef.Root * strandDef.Bulb)) * partFit);
                return;
            }
            // The same arithmetic DrawAccessory uses, or the reserved rect is not the rect that
            // gets drawn: the shell's fit moves and resizes the sprite, and a wrap is placed by
            // the shell's seat and sized by the ratio between that seat and the one it was drawn
            // for, which is how a scarf cut for a 222 wide waist ends up off the card.
            var scale = manifest.SlotScaleFor(def.Slot) * partFit / 256f;
            if (!manifest.Anchors.TryGetValue(def.Anchor, out var cells))
            {
                return;
            }
            var seat = def.RidesWrapSeat
                ? (def.Anchor == "head" ? manifest.HeadSeat : manifest.WrapSeat)
                : null;
            var reach = Vector2.One;
            if (seat is { } sized && def.WrapRx > 0f && def.Width > 0)
            {
                var ratio = sized.Rx * (256f / manifest.Cell) / def.WrapRx;
                reach = new Vector2(ratio, def.WrapBand ? 1f : ratio);
            }
            var seatRest = seat is null
                ? Vector2.Zero
                : manifest.AnchorForCell(def.Anchor, manifest.RestCell ?? 0);
            for (var i = 0; i < cells.Count; i++)
            {
                if (cells[i].Length < 2)
                {
                    continue;
                }
                var local = new Vector2(cells[i][0], cells[i][1]);
                if (seat is { } waist)
                {
                    local = new Vector2(waist.Cx, waist.Cy + waist.Sink) + (local - seatRest);
                }
                local += partOffset * (manifest.Cell / 256f);
                var anchor = local / manifest.Cell;
                var origin = def.OriginPoint * scale * reach;
                var min = anchor - new Vector2(origin.X, origin.Y);
                var max = min + (new Vector2(def.Width, def.Height) * scale * reach);
                // A flown item's simulated lines sweep past the picture; the manifest says by
                // how much, and empty air the item moves through is still reach.
                if (def.FxReach is { Length: >= 4 } fxReach)
                {
                    min -= new Vector2(fxReach[0], fxReach[1]) * scale;
                    max += new Vector2(fxReach[2], fxReach[3]) * scale;
                }
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

        // The shell's own strand anatomy reaches past the cell exactly as a worn fan does, and
        // the floating window must hold wherever a tendril swings.
        if (manifest.Strands is { } shellStrands)
        {
            MeasurePart(shellStrands.Seat,
                shellStrands.Len + shellStrands.Spread + (shellStrands.Root * shellStrands.Bulb));
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
