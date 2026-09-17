using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Shared.Aetherling;
using AetherOS.PetKit.Engine;
using AetherOS.PetKit.Rendering;
using AetherOS.Apps.Aetherling.Ui;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;

namespace AetherOS.Apps.Aetherling.Screens;

/// <summary>The crystal, and everything that happens around it: the arrival that follows a purchase, the
/// pokes that crack it, and the birth that follows the last one. There is no wait and no ladder: the
/// player breaks it whenever they like, and the server's yes is what starts the birth.</summary>
internal sealed class CoreScreen(IAetherlingHost host, PetRuntime pet)
{
    /// <summary>Beats of the arrival, in seconds from the moment the core is bought.</summary>
    private const float SinkStart = 0.6f;
    private const float SinkEnd = 4.2f;
    private const float MusicAt = 3.6f;
    private const float CoreFadeStart = 4.4f;
    private const float CoreFadeEnd = 7.6f;
    private const float ArrivalEnd = 8.0f;

    /// <summary>The same beats when the player has asked for less motion: the same story, told in a second.</summary>
    private const float QuietArrivalEnd = 1.4f;

    /// <summary>The crystal's loop plays at its authored pace: there is no ladder left for it to climb.</summary>
    public const float Tempo = 1.0f;

    /// <summary>Pokes it takes to break the crystal.</summary>
    private const int BreakPokes = 5;

    /// <summary>Where the shell pieces go, and which cells they are drawn from.</summary>
    private static readonly float[] ShardAngles = [-1.31f, -0.79f, -0.26f, 0.26f, 0.79f, 1.31f];
    private const int ShardFirstCell = 26;
    private const int ShardCells = 4;

    /// <summary>One crack, kept in crystal-local units (a fraction of the displayed size, from the
    /// bottom centre) so it scales with the sprite it is drawn over.</summary>
    private readonly record struct Crack(Vector2[] Points);

    private readonly ParticleFx _fx = new();
    private readonly ShadingFx _shading = new() { Enabled = true };
    private readonly List<Crack> _cracks = [];
    private readonly Random _rng = new();

    private CoreAssets? _assets;
    private CoreDraw? _draw;
    private CeremonyController? _ceremony;
    private bool _loadAttempted;
    private bool _birthDone;

    private AetherlingDto? _core;
    private double _lastFrameTime;
    private float _arrival = 1f;
    private bool _musicCue;
    private bool _flashed;
    private bool _busy;
    private string? _error;
    private double _errorUntil;
    private float _shake;

    /// <summary>The hatch reply, handed over from the hub continuation and consumed on the draw thread:
    /// starting the birth touches the particle pool, which the pool thread must not.</summary>
    private AetherlingDto? _pendingHatch;
    private string? _pendingError;

    /// <summary>Consecutive taps, and when the run lapses. Poking it once should be a flicker and poking it
    /// repeatedly should build to something, so the reward escalates instead of repeating.</summary>
    private int _tapStreak;
    private double _tapExpires;

    /// <summary>Last seen accessibility preference, kept because the hatch reply is drained before the frame
    /// that would otherwise hand it in.</summary>
    private bool _reduceMotion;

    /// <summary>True once the birth has played out, so the app can move on to what came out of it. Reading
    /// it clears it.</summary>
    public bool TryTakeBirthDone()
    {
        if (!_birthDone)
        {
            return false;
        }
        _birthDone = false;
        return true;
    }

    /// <summary>Whether the crystal's music belongs on: the arrival has reached its cue and the shell has
    /// not yet given.</summary>
    public bool MusicWanted => _musicCue && !_flashed;

    /// <summary>The birth is on screen, from the swell to the newborn's held beat.</summary>
    public bool BirthPlaying => _ceremony?.BirthPlaying == true;

    /// <summary>Set when the screen is entered straight from a purchase, which is the only time the long
    /// arrival plays.</summary>
    public void BeginArrival()
    {
        _arrival = 0f;
        _musicCue = false;
        _flashed = false;
        _cracks.Clear();
    }

    public void OnShow(AetherlingDto? core)
    {
        _lastFrameTime = ImGui.GetTime();
        _flashed = false;
        _cracks.Clear();
        Apply(core);
    }

    public void Apply(AetherlingDto? core)
    {
        if (core is not null)
        {
            _core = core;
        }
    }

    public void Draw(OsAppContext ctx)
    {
        EnsureLoaded();

        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var now = ImGui.GetTime();
        var dt = Math.Clamp((float)(now - _lastFrameTime), 0f, 0.25f);
        _lastFrameTime = now;

        _reduceMotion = ctx.ReduceMotion;
        DrainPending(now);
        _shake = MathF.Max(0f, _shake - (dt / 0.35f));

        Look.Backdrop(dl, ctx.Theme, origin, size);

        if (_ceremony is null || _draw is null || _assets is null)
        {
            Look.Centred(dl, ctx.Localize("os.aetherling_missing"), origin.X + (size.X * 0.5f),
                origin.Y + (size.Y * 0.45f), Look.U32(Look.Whisper));
            // A paid crystal must still be breakable without the art.
            DrawBreakWithoutArt(ctx, dl, origin, size);
            DrawError(dl, origin, size, now);
            return;
        }

        _ceremony.ReduceMotion = ctx.ReduceMotion;
        _ceremony.Update(dt);
        _shading.Update(dt, ctx.ReduceMotion);
        _fx.Update(dt);

        var arrivalEnd = ctx.ReduceMotion ? QuietArrivalEnd : ArrivalEnd;
        if (_arrival < 1f)
        {
            _arrival = MathF.Min(1f, _arrival + (dt / arrivalEnd));
        }
        var beat = _arrival * arrivalEnd;

        var sunk = ctx.ReduceMotion
            ? Look.EaseInOut(beat / 0.3f)
            : Look.EaseInOut((beat - SinkStart) / (SinkEnd - SinkStart));
        var coreAlpha = ctx.ReduceMotion
            ? Look.EaseOut((beat - 0.5f) / 0.6f)
            : Look.EaseOut((beat - CoreFadeStart) / (CoreFadeEnd - CoreFadeStart));

        if (!_musicCue && beat >= (ctx.ReduceMotion ? 0.2f : MusicAt))
        {
            _musicCue = true;
        }

        Look.Motes(dl, origin, size, 30, Look.CrystalPale, 0.45f * coreAlpha, now, ctx.ReduceMotion);
        DrawCore(ctx, dl, origin, size, coreAlpha);

        // The hint only arrives once the crystal has, so the arrival is never interrupted by a caption.
        if (_arrival >= 1f && !_ceremony.BirthPlaying)
        {
            DrawHint(ctx, dl, origin, size);
        }

        if (_arrival < 1f)
        {
            var veil = 1f - Math.Clamp(coreAlpha, 0f, 1f);
            dl.AddRectFilled(origin, origin + size, Look.U32(Look.Void with { W = 1f }, Math.Max(sunk, 0f) * veil));
        }

        DrawError(dl, origin, size, now);
    }

    /// <summary>Takes whatever the hatch round trip left behind. Called from Draw, because starting the
    /// birth spawns particles and reads the ImGui clock.</summary>
    private void DrainPending(double now)
    {
        if (Interlocked.Exchange(ref _pendingHatch, null) is { } hatched)
        {
            _busy = false;
            _core = hatched;
            if (_ceremony is { } ceremony)
            {
                ceremony.BeginBirth(_reduceMotion);
            }
            else
            {
                _birthDone = true;
            }
        }
        if (Interlocked.Exchange(ref _pendingError, null) is { } message)
        {
            _busy = false;
            _error = message;
            _errorUntil = now + 5.0;
        }
    }

    private void DrawError(ImDrawListPtr dl, Vector2 origin, Vector2 size, double now)
    {
        if (_error is not { Length: > 0 } || now >= _errorUntil)
        {
            return;
        }
        Look.Centred(dl, _error, origin.X + (size.X * 0.5f), origin.Y + size.Y - Px(24f),
            Look.U32(new Vector4(0.95f, 0.5f, 0.5f, 1f)), 0.85f);
    }

    private void DrawCore(OsAppContext ctx, ImDrawListPtr dl, Vector2 origin, Vector2 size, float alpha)
    {
        if (alpha <= 0f || _ceremony is null || _draw is null)
        {
            return;
        }
        var pose = _ceremony.GetPose();
        var target = BirthStage.DisplaySize(size);
        var displaySize = target / CeremonyController.KindledScale;
        var bottom = BirthStage.BottomCentre(origin, size);
        var birth = _ceremony.BirthPlaying;

        if (_shake > 0f && !ctx.ReduceMotion)
        {
            var amplitude = _shake * _shake * Px(5f);
            bottom += new Vector2(
                (((float)_rng.NextDouble() * 2f) - 1f) * amplitude,
                (((float)_rng.NextDouble() * 2f) - 1f) * amplitude * 0.5f);
        }

        Look.Halo(dl, bottom - new Vector2(0f, target * 0.42f), target * 0.85f,
            Look.Crystal, pose.GlowAlpha * 0.55f * alpha, 6);
        if (pose.HaloAlpha > 0f)
        {
            Look.Halo(dl, bottom - new Vector2(0f, target * 0.42f), target, Look.CrystalPale, pose.HaloAlpha);
        }
        _fx.Draw(dl, bottom, displaySize, behind: true);

        var coreAlpha = alpha * (birth ? pose.CoreAlpha : 1f);
        if (coreAlpha > 0f)
        {
            var tint = new Vector4(1f, 1f, 1f, coreAlpha);
            _draw.Draw(dl, ctx.Capabilities.Textures, bottom, displaySize, pose.CellIndex, tint,
                pose.Scale, pose.Offset, _shading);
            DrawCracks(dl, bottom, displaySize * pose.Scale.Y, coreAlpha);
        }

        if (birth)
        {
            DrawShards(ctx, dl, bottom, displaySize, pose.ShardProgress);
            DrawNewborn(ctx, dl, bottom, target, pose.PetPopProgress);
        }

        _fx.Draw(dl, bottom, displaySize, behind: false);

        if (birth)
        {
            // The flash is drawn over everything, and never above 0.85: one strike per crystal, in a
            // window's worth of screen rather than the whole one.
            if (pose.FlashAlpha > 0f)
            {
                dl.AddRectFilled(origin, origin + size, Look.U32(Look.CrystalPale, pose.FlashAlpha));
            }
            return;
        }

        var half = displaySize * pose.Scale.X * 0.55f;
        ImGui.SetCursorScreenPos(bottom - new Vector2(half, half * 2f));
        if (ImGui.InvisibleButton("##aetherlingCore", new Vector2(half * 2f, half * 2f)))
        {
            _ceremony.Touch();
            Poke(ctx, bottom, displaySize * pose.Scale.Y);
        }
        if (ImGui.IsItemHovered() && !_busy)
        {
            HandOnHover();
        }
    }

    /// <summary>What was inside, arriving: the grown creature, drawn through the one runtime every surface
    /// shares, wearing whatever the hatch reply says (the free base). It lands at exactly the size the
    /// crystal filled, so the last thing the ceremony shows and the first thing the pet page shows are the
    /// same size.</summary>
    private void DrawNewborn(OsAppContext ctx, ImDrawListPtr dl, Vector2 bottom, float target, float progress)
    {
        if (progress < 0f || _core is not { } core)
        {
            return;
        }
        pet.EnsureLoaded(host.AssetRoot, PetState.FormFolder(core));
        pet.ApplyLook(core);
        if (!pet.Ready)
        {
            return;
        }
        pet.Tick(ctx.ReduceMotion);
        var scale = CeremonyController.PetPopScale(progress);
        var pose = pet.Pose;
        pose.Scale *= scale;
        pose.Offset = Vector2.Zero;
        pet.Draw(dl, ctx.Capabilities.Textures, bottom, target, pose);
    }

    /// <summary>Seconds of stillness that end a run of taps.</summary>
    private const double TapWindow = 1.6;

    /// <summary>The tap payoff, growing with the run: a few motes, then sparkles, then a ring and a flare of
    /// its own light. Every poke past the arrival also cracks the shell a little further, and the last one
    /// breaks it.</summary>
    private void Poke(OsAppContext ctx, Vector2 bottom, float displaySize)
    {
        var now = ImGui.GetTime();
        _tapStreak = now > _tapExpires ? 1 : Math.Min(_tapStreak + 1, 12);
        _tapExpires = now + TapWindow;

        var origin = new Vector2(128f, 140f);
        var step = _tapStreak;
        _fx.Burst(ParticleKind.Sparkle, origin, 3 + (step * 2), Look.CrystalPale, 40f + (step * 12f));

        if (step >= 3)
        {
            _fx.Burst(ParticleKind.Mote, origin, step, Look.Crystal, 70f + (step * 8f), behind: true);
        }
        if (step >= 5)
        {
            _shading.RequestSweep();
            _fx.Burst(ParticleKind.Glow, origin, 1, Look.CrystalPale, 20f, behind: true);
        }
        if (step >= 8)
        {
            _fx.Burst(ParticleKind.Ring, origin, 1, Look.CrystalPale, 30f);
        }

        if (_arrival < 1f || _busy || _core is null)
        {
            return;
        }

        AddCrack(ImGui.GetMousePos(), bottom, displaySize);
        _shake = 1f;
        if (_cracks.Count >= BreakPokes)
        {
            if (!ctx.ReduceMotion)
            {
                _fx.Burst(ParticleKind.Shard, origin, 6, Look.CrystalPale, 120f);
            }
            Hatch();
        }
    }

    /// <summary>A crack from where the crystal was poked: two or three jagged segments, drifting away from
    /// the middle so the shell reads as giving under the finger rather than randomly scribbled.</summary>
    private void AddCrack(Vector2 at, Vector2 bottom, float displaySize)
    {
        var centre = bottom - new Vector2(0f, displaySize * 0.5f);
        var local = (at - centre) / MathF.Max(1f, displaySize);
        local = Vector2.Clamp(local, new Vector2(-0.3f, -0.42f), new Vector2(0.3f, 0.42f));
        var away = local.LengthSquared() < 0.0001f
            ? new Vector2(1f, 0f)
            : Vector2.Normalize(local);
        var segments = 2 + _rng.Next(2);
        var points = new Vector2[segments + 1];
        points[0] = local;
        for (var i = 1; i <= segments; i++)
        {
            var angle = MathF.Atan2(away.Y, away.X) + ((((float)_rng.NextDouble() * 2f) - 1f) * 0.9f);
            var length = 0.06f + ((float)_rng.NextDouble() * 0.08f);
            points[i] = points[i - 1] + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * length;
        }
        _cracks.Add(new Crack(points));
    }

    private void DrawCracks(ImDrawListPtr dl, Vector2 bottom, float displaySize, float alpha)
    {
        if (_cracks.Count == 0)
        {
            return;
        }
        var centre = bottom - new Vector2(0f, displaySize * 0.5f);
        var thickness = Px(1.6f);
        foreach (var crack in _cracks)
        {
            for (var i = 1; i < crack.Points.Length; i++)
            {
                var a = centre + (crack.Points[i - 1] * displaySize);
                var b = centre + (crack.Points[i] * displaySize);
                dl.AddLine(a, b, Look.U32(Look.Void, 0.55f * alpha), thickness * 1.8f);
                dl.AddLine(a, b, Look.U32(Look.CrystalPale, 0.9f * alpha), thickness);
            }
        }
    }

    /// <summary>The one line under the crystal: what to do, then that it is working.</summary>
    private void DrawHint(OsAppContext ctx, ImDrawListPtr dl, Vector2 origin, Vector2 size)
    {
        var key = _cracks.Count == 0 ? "os.aetherling_break_hint" : "os.aetherling_break_more";
        Look.CentredWrapped(dl, ctx.Localize(key), origin.X + (size.X * 0.5f), origin.Y + (size.Y * 0.70f),
            size.X - Px(48f), Look.U32(Look.Whisper, 0.85f), 1f);
    }

    /// <summary>The way in when the ceremony art failed to load: a plain button, since there is no crystal
    /// to poke.</summary>
    private void DrawBreakWithoutArt(OsAppContext ctx, ImDrawListPtr dl, Vector2 origin, Vector2 size)
    {
        var label = ctx.Localize("os.aetherling_break_hint");
        var height = Px(44f);
        var width = size.X - (Px(30f) * 2f);
        var tl = new Vector2(origin.X + ((size.X - width) * 0.5f), origin.Y + size.Y - height - Px(34f));

        ImGui.SetCursorScreenPos(tl);
        var pressed = ImGui.InvisibleButton("##aetherlingBreak", new Vector2(width, height));
        if (ImGui.IsItemHovered() && !_busy)
        {
            HandOnHover();
        }
        if (pressed && !_busy)
        {
            Hatch();
        }
        var radius = height * 0.5f;
        dl.AddRectFilled(tl, tl + new Vector2(width, height), Look.U32(Look.Crystal with { W = 0.2f }), radius);
        dl.AddRect(tl, tl + new Vector2(width, height), Look.U32(Look.CrystalPale, 0.7f), radius,
            ImDrawFlags.RoundCornersAll, Px(1.8f));
        Look.Centred(dl, label, tl.X + (width * 0.5f),
            tl.Y + ((height - ImGui.GetTextLineHeight()) * 0.5f), Look.U32(Look.CrystalPale));
    }

    /// <summary>The moment the shell gives. The loop was the crystal's song, so it dies with it and the game
    /// gets its own music back.</summary>
    private void OnFlashed()
    {
        _flashed = true;
        _cracks.Clear();
        host.PlayCrack();
        host.StopBgm();
    }

    private void Hatch()
    {
        _busy = true;
        _error = null;
        var job = host.CurrentJobAbbreviation;
        _ = Task.Run(async () =>
        {
            try
            {
                var dto = await host.HatchAsync(job).ConfigureAwait(false);
                Interlocked.Exchange(ref _pendingHatch, dto);
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref _pendingError, host.DescribeError(ex));
            }
        });
    }

    /// <summary>The six pieces of shell, thrown on fixed arcs rather than random ones so the birth looks the
    /// same every time it is told about.</summary>
    private void DrawShards(OsAppContext ctx, ImDrawListPtr dl, Vector2 bottom, float displaySize, float progress)
    {
        if (_assets is null || progress < 0f)
        {
            return;
        }
        var textures = ctx.Capabilities.Textures;
        var handle = textures.Get(_assets.LayerPaths[0]);
        if (handle is not { } tex)
        {
            return;
        }

        var manifest = _assets.Manifest;
        var reach = displaySize * 0.9f;
        var side = displaySize * 0.3f;
        for (var i = 0; i < ShardAngles.Length; i++)
        {
            var (u0, v0, u1, v1) = manifest.UvForCell(ShardFirstCell + (i % ShardCells));
            var angle = ShardAngles[i];
            var travel = progress * reach;
            // A thrown piece, not a fired one: it rises, then gravity takes it.
            var centre = bottom + new Vector2(
                MathF.Sin(angle) * travel,
                (-displaySize * 0.55f) - (MathF.Cos(angle) * travel * 0.6f) + (progress * progress * reach * 0.7f));
            var half = side * (1f - (progress * 0.35f)) * 0.5f;
            var alpha = 1f - progress;
            dl.AddImage(tex, centre - new Vector2(half, half), centre + new Vector2(half, half),
                new Vector2(u0, v0), new Vector2(u1, v1), Look.U32(Look.CrystalPale, alpha));
        }
    }

    private void EnsureLoaded()
    {
        if (_loadAttempted)
        {
            return;
        }
        _loadAttempted = true;
        CoreAssets.AssetRootHint = host.AssetRoot;
        _assets = CoreAssets.Load();
        if (_assets is null)
        {
            return;
        }
        _draw = new CoreDraw(_assets);
        _ceremony = new CeremonyController(_assets.Manifest);
        _ceremony.Flashed += OnFlashed;
        _ceremony.BirthFinished += () => _birthDone = true;
    }
}
