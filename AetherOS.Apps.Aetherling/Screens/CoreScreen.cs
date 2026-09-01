using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Shared.Aetherling;
using AetherOS.PetKit.Engine;
using AetherOS.PetKit.Rendering;
using AetherOS.Apps.Aetherling.Ui;
using AetherLove.UI;
using AetherLove.Widgets;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Aetherling.Screens;

/// <summary>The crystal, and everything that happens around it: the arrival that follows a purchase, the
/// long wait between rungs, and the one button that ends each wait.</summary>
internal sealed class CoreScreen(IAetherlingHost host, Action<float> tempo)
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

    /// <summary>Where the shell pieces go, and which cells they are drawn from.</summary>
    private static readonly float[] ShardAngles = [-1.31f, -0.79f, -0.26f, 0.26f, 0.79f, 1.31f];
    private const int ShardFirstCell = 26;
    private const int ShardCells = 4;

    private readonly ParticleFx _fx = new();
    private readonly ShadingFx _shading = new() { Enabled = true };

    private CoreAssets? _assets;
    private CoreDraw? _draw;
    private CeremonyController? _ceremony;
    private CoreAssets? _hatchlingAssets;
    private CoreDraw? _hatchlingDraw;
    private bool _loadAttempted;
    private bool _birthDone;

    private AetherlingDto? _core;
    private double _lastFrameTime;
    private float _arrival = 1f;
    private bool _musicStarted;
    private bool _busy;
    private string? _error;
    private double _errorUntil;

    /// <summary>Results of the charge round trip, handed over from the hub continuation and consumed on the
    /// draw thread: everything below touches ImGui and the particle pool, which the pool thread must not.</summary>
    private AetherlingDto? _pending;
    private AetherlingDto? _pendingHatch;
    private string? _pendingError;

    /// <summary>Consecutive taps, and when the run lapses. Poking it once should be a flicker and poking it
    /// repeatedly should build to something, so the reward escalates instead of repeating.</summary>
    private int _tapStreak;
    private double _tapExpires;

    /// <summary>How far the server's clock sits from this machine's, sampled whenever the server speaks.
    /// Every countdown runs off this, so moving the system clock moves nothing.</summary>
    private TimeSpan _serverOffset;

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

    /// <summary>Set when the screen is entered straight from a purchase, which is the only time the long
    /// arrival plays.</summary>
    public void BeginArrival()
    {
        _arrival = 0f;
        _musicStarted = false;
    }

    public void OnShow(AetherlingDto? core)
    {
        _lastFrameTime = ImGui.GetTime();
        Apply(core, animate: false);
    }

    /// <summary>Takes a fresh core from the server. A stage the crystal has not reached yet plays the
    /// advance flourish; anything else just lands.</summary>
    public void Apply(AetherlingDto? core, bool animate)
    {
        var known = _core;
        _core = core;
        if (core is null)
        {
            return;
        }
        // Stamped before the ceremony exists, or the first wait after a purchase runs on the local clock.
        _serverOffset = core.ServerNowUtc - DateTimeOffset.UtcNow;
        if (_ceremony is null)
        {
            return;
        }
        var stage = (AetherlingStage)core.CoreStage;
        if (known is not null && known.CoreStage == core.CoreStage
            && known.StageEnteredAtUtc == core.StageEnteredAtUtc)
        {
            return;
        }
        if (animate && stage > _ceremony.Stage)
        {
            _ceremony.AdvanceTo(stage);
            _fx.Burst(ParticleKind.Ring, new Vector2(128f, 150f), 1, Look.CrystalPale, 40f);
            _fx.Burst(ParticleKind.Sparkle, new Vector2(128f, 150f), 14, Look.CrystalPale, 90f);
            _shading.RequestSweep();
        }
        else
        {
            _ceremony.Restore(stage, core.StageEnteredAtUtc.UtcDateTime);
        }
        tempo(SpeedFor(stage));
    }

    /// <summary>The track winds up as the crystal climbs, so the last rung feels hurried.</summary>
    public static float SpeedFor(AetherlingStage stage) => stage switch
    {
        AetherlingStage.Stirring => 1.06f,
        AetherlingStage.Fissured => 1.13f,
        AetherlingStage.Quickening => 1.21f,
        AetherlingStage.Kindling => 1.30f,
        _ => 1.00f,
    };

    /// <summary>The same loop, carried past the hatch: a growing creature keeps the crystal's music and
    /// each form takes it a step higher, picking up above Kindling where the ceremony left it. Derived from
    /// the growth counters the worn form comes from, so the tempo and the body change together.
    ///
    /// <para>The adult has none of it. The music belongs to the becoming, and a grown pet's page is quiet
    /// on purpose.</para></summary>
    public static float? GrowthSpeedFor(AetherlingDto core)
    {
        if (core.HatchedAtUtc is null || core.Adult is not null)
        {
            return null;
        }
        var perStage = Math.Max((short)1, core.Growth?.FeedsPerStage ?? 3);
        var fed = core.Growth?.GrowthFed ?? 0;
        if (fed >= perStage * 2)
        {
            return 1.52f;
        }
        return fed >= perStage ? 1.44f : 1.36f;
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

        Look.Backdrop(dl, ctx.Theme, origin, size);

        if (_ceremony is null || _draw is null || _assets is null)
        {
            Look.Centred(dl, ctx.Localize("os.aetherling_missing"), origin.X + (size.X * 0.5f),
                origin.Y + (size.Y * 0.45f), Look.U32(Look.Whisper));
            // The gate still draws: a player who has paid must be able to keep paying without the art.
            DrawGate(ctx, dl, origin, size, now);
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

        if (!_musicStarted && beat >= (ctx.ReduceMotion ? 0.2f : MusicAt))
        {
            _musicStarted = true;
            tempo(SpeedFor(_ceremony.Stage));
        }

        Look.Motes(dl, origin, size, 30, Look.CrystalPale, 0.45f * coreAlpha, now, ctx.ReduceMotion);
        DrawCore(ctx, dl, origin, size, coreAlpha);

        // The furniture only arrives once the crystal has, so the arrival is never interrupted by a button.
        if (_arrival >= 1f)
        {
            DrawGate(ctx, dl, origin, size, now);
        }

        if (_arrival < 1f)
        {
            var veil = 1f - Math.Clamp(coreAlpha, 0f, 1f);
            dl.AddRectFilled(origin, origin + size, Look.U32(Look.Void with { W = 1f }, Math.Max(sunk, 0f) * veil));
        }

        DrawError(dl, origin, size, now);
    }

    /// <summary>Takes whatever the charge round trip left behind. Called from Draw, because applying a core
    /// spawns particles and reads the ImGui clock.</summary>
    private void DrainPending(double now)
    {
        if (Interlocked.Exchange(ref _pending, null) is { } dto)
        {
            _busy = false;
            Apply(dto, animate: true);
        }
        if (Interlocked.Exchange(ref _pendingHatch, null) is { } hatched)
        {
            _busy = false;
            _core = hatched;
            _serverOffset = hatched.ServerNowUtc - DateTimeOffset.UtcNow;
            _ceremony?.BeginBirth(_reduceMotion);
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
            Poke(ctx);
        }
        if (ImGui.IsItemHovered())
        {
            HandOnHover();
        }
    }

    /// <summary>What was inside, arriving. It lands at exactly the size the crystal filled, so the last thing
    /// the ceremony shows and the first thing the pet screen shows are the same size.</summary>
    private void DrawNewborn(OsAppContext ctx, ImDrawListPtr dl, Vector2 bottom, float target, float progress)
    {
        if (progress < 0f || _hatchlingDraw is null)
        {
            return;
        }
        var scale = CeremonyController.PetPopScale(progress);
        _hatchlingDraw.Draw(dl, ctx.Capabilities.Textures, bottom, target, 0, PetTints.Dawn,
            new Vector2(scale, scale), Vector2.Zero);
    }

    /// <summary>Seconds of stillness that end a run of taps.</summary>
    private const double TapWindow = 1.6;

    /// <summary>The tap payoff, growing with the run: a few motes, then sparkles, then a ring and a flare of
    /// its own light. It never pays sparks or moves the ladder; it is only ever a reaction.</summary>
    private void Poke(OsAppContext ctx)
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
        if (step >= 12 && !ctx.ReduceMotion)
        {
            _fx.Burst(ParticleKind.Shard, origin, 6, Look.CrystalPale, 120f);
        }
    }

    /// <summary>Either the wait or the way out of it, never both.</summary>
    private void DrawGate(OsAppContext ctx, ImDrawListPtr dl, Vector2 origin, Vector2 size, double now)
    {
        if (_core is not { } core || _ceremony is { BirthPlaying: true })
        {
            return;
        }
        // The last rung waits out the same hold as every other one, and then offers the way out of it.
        var lastRung = core.CoreStage >= core.MaxStage;
        var elapsed = (ServerNow - core.StageEnteredAtUtc).TotalMinutes;
        var progress = (float)Math.Clamp(elapsed / Math.Max(1, core.GateMinutes), 0.0, 1.0);
        var target = MathF.Min(size.X * 0.62f, size.Y * 0.42f);
        var centre = origin + new Vector2(size.X * 0.5f, (size.Y * 0.56f) - (target * 0.45f));
        var radius = target * 0.78f;

        DrawFlavour(ctx, dl, origin, size, progress);

        if (progress < 1f)
        {
            DrawArc(dl, centre, radius, progress, now, ctx.ReduceMotion);
            var remaining = Math.Max(0, (int)Math.Ceiling(core.GateMinutes - elapsed));
            var wait = remaining >= 60
                ? string.Format(ctx.Localize("os.aetherling_wait_hm"), remaining / 60, remaining % 60)
                : string.Format(ctx.Localize("os.aetherling_wait_m"), remaining);
            var centreX = origin.X + (size.X * 0.5f);
            var lineY = origin.Y + size.Y - Px(120f);
            var rows = Look.CentredWrapped(dl, ctx.Localize("os.aetherling_stirs_in"), centreX, lineY,
                size.X - Px(48f), Look.U32(Look.Whisper), 0.9f);
            var lineStep = ImGui.GetTextLineHeight() * 0.9f * 1.25f;
            Look.Pill(dl, wait, centreX, lineY + (rows * lineStep) + Px(8f), Look.Crystal,
                ctx.ReduceMotion ? 1f : 0.75f + (0.25f * Look.Breathe(now, 3.8f)));
            return;
        }

        if (lastRung)
        {
            DrawHatch(ctx, dl, origin, size, now);
            return;
        }
        DrawCharge(ctx, dl, origin, size, core, now);
    }

    private DateTimeOffset ServerNow => DateTimeOffset.UtcNow + _serverOffset;

    private void DrawArc(ImDrawListPtr dl, Vector2 centre, float radius, float progress, double now, bool reduceMotion)
    {
        const int Segments = 96;
        var sweep = MathF.Tau * progress;
        var start = -MathF.PI * 0.5f;

        dl.PathClear();
        dl.PathArcTo(centre, radius, start, start + MathF.Tau, Segments);
        dl.PathStroke(Look.U32(Look.Crystal, 0.10f), ImDrawFlags.None, Px(1.5f));

        if (progress <= 0f)
        {
            return;
        }
        var shimmer = reduceMotion ? 1f : 0.75f + (0.25f * Look.Breathe(now, 3.4f));
        dl.PathClear();
        dl.PathArcTo(centre, radius, start, start + sweep, Math.Max(2, (int)(Segments * progress)));
        dl.PathStroke(Look.U32(Look.Crystal, 0.55f * shimmer), ImDrawFlags.None, Px(2.2f));

        var head = new Vector2(
            centre.X + (MathF.Cos(start + sweep) * radius),
            centre.Y + (MathF.Sin(start + sweep) * radius));
        dl.AddCircleFilled(head, Px(2.6f), Look.U32(Look.CrystalPale, 0.9f * shimmer), 12);
    }

    private void DrawFlavour(OsAppContext ctx, ImDrawListPtr dl, Vector2 origin, Vector2 size, float progress)
    {
        if (_core is not { } core)
        {
            return;
        }
        var line = ctx.Localize($"os.aetherling_flavour_{core.CoreStage}");
        Look.CentredWrapped(dl, line, origin.X + (size.X * 0.5f), origin.Y + (size.Y * 0.70f),
            size.X - Px(48f), Look.U32(Look.Whisper, 0.85f), 1f);
        _ = progress;
    }

    /// <summary>The one press that opens it. Free, so no price rides the button; it simply glows and waits.
    /// Pressing it asks the server first, and only the server's yes starts the birth.</summary>
    private void DrawHatch(OsAppContext ctx, ImDrawListPtr dl, Vector2 origin, Vector2 size, double now)
    {
        var label = ctx.Localize("os.aetherling_hatch_cta");
        var height = Px(44f);
        var width = size.X - (Px(30f) * 2f);
        var tl = new Vector2(origin.X + ((size.X - width) * 0.5f), origin.Y + size.Y - height - Px(34f));

        ImGui.SetCursorScreenPos(tl);
        var pressed = ImGui.InvisibleButton("##aetherlingHatch", new Vector2(width, height));
        if (ImGui.IsItemHovered() && !_busy)
        {
            HandOnHover();
        }
        if (pressed && !_busy)
        {
            Hatch();
        }

        var pulse = ctx.ReduceMotion ? 0.5f : Look.Breathe(now, 1.4f);
        var radius = height * 0.5f;
        dl.AddRectFilled(tl, tl + new Vector2(width, height),
            Look.U32(Look.Crystal with { W = 0.18f + (0.16f * pulse) }), radius);
        dl.AddRect(tl, tl + new Vector2(width, height),
            Look.U32(Look.CrystalPale, 0.55f + (0.45f * pulse)), radius, ImDrawFlags.RoundCornersAll, Px(1.8f));

        if (_busy)
        {
            LoadingSpinner.Draw(
                tl + new Vector2(width * 0.5f, height * 0.5f), Px(9f), Px(2.5f), Look.U32(Look.CrystalPale));
            return;
        }

        Look.Centred(dl, label, tl.X + (width * 0.5f),
            tl.Y + ((height - ImGui.GetTextLineHeight()) * 0.5f), Look.U32(Look.CrystalPale));
    }

    private void DrawCharge(
        OsAppContext ctx, ImDrawListPtr dl, Vector2 origin, Vector2 size, AetherlingDto core, double now)
    {
        var label = ctx.Localize("os.aetherling_offer");
        var price = core.NextChargeSparks.ToString("N0");
        var height = Px(44f);
        var width = size.X - (Px(30f) * 2f);
        var tl = new Vector2(origin.X + ((size.X - width) * 0.5f), origin.Y + size.Y - height - Px(34f));

        ImGui.SetCursorScreenPos(tl);
        var pressed = ImGui.InvisibleButton("##aetherlingCharge", new Vector2(width, height));
        var hovered = ImGui.IsItemHovered();
        if (hovered && !_busy)
        {
            HandOnHover();
        }
        if (pressed && !_busy)
        {
            Charge();
        }

        var pulse = ctx.ReduceMotion ? 0.5f : Look.Breathe(now, 1.9f);
        var radius = height * 0.5f;
        dl.AddRectFilled(tl, tl + new Vector2(width, height),
            Look.U32(Look.Crystal with { W = 0.14f + (0.10f * pulse) }), radius);
        dl.AddRect(tl, tl + new Vector2(width, height),
            Look.U32(Look.Crystal, 0.45f + (0.45f * pulse)), radius, ImDrawFlags.RoundCornersAll, Px(1.5f));

        if (_busy)
        {
            LoadingSpinner.Draw(
                tl + new Vector2(width * 0.5f, height * 0.5f), Px(9f), Px(2.5f), Look.U32(Look.CrystalPale));
            return;
        }

        var pillW = ImGui.CalcTextSize(price).X + Px(26f);
        var gap = Px(10f);
        var labelW = ImGui.CalcTextSize(label).X;
        var startX = tl.X + ((width - labelW - gap - pillW) * 0.5f);
        var textY = tl.Y + ((height - ImGui.GetTextLineHeight()) * 0.5f);
        dl.AddText(new Vector2(startX, textY), Look.U32(Look.CrystalPale), label);

        var pillTl = new Vector2(startX + labelW + gap, tl.Y + ((height - Px(22f)) * 0.5f));
        dl.AddRectFilled(pillTl, pillTl + new Vector2(pillW, Px(22f)),
            Look.U32(Look.Spark with { W = 0.20f }), Px(11f));
        IconDraw.AddCentered(dl, FontAwesomeIcon.Bolt, Px(10f),
            pillTl + new Vector2(Px(11f), Px(11f)), Look.U32(Look.Spark));
        dl.AddText(pillTl + new Vector2(Px(20f), (Px(22f) - ImGui.GetTextLineHeight()) * 0.5f),
            Look.U32(Look.Spark), price);
    }

    private void Charge()
    {
        _busy = true;
        _error = null;
        _ = Task.Run(async () =>
        {
            try
            {
                var dto = await host.ChargeAsync().ConfigureAwait(false);
                Interlocked.Exchange(ref _pending, dto);
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref _pendingError, host.DescribeError(ex));
            }
        });
    }

    /// <summary>The moment the shell gives. The loop was the crystal's song, so it dies with it and the game
    /// gets its own music back.</summary>
    private void OnFlashed()
    {
        host.PlayCrack();
        host.StopBgm();
    }

    private void Hatch()
    {
        _busy = true;
        _error = null;
        _ = Task.Run(async () =>
        {
            try
            {
                var dto = await host.HatchAsync().ConfigureAwait(false);
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
        _hatchlingAssets = CoreAssets.Load(CoreAssets.HatchlingFolder);
        if (_hatchlingAssets is not null)
        {
            _hatchlingDraw = new CoreDraw(_hatchlingAssets);
        }
        if (_core is { } core)
        {
            _ceremony.Restore((AetherlingStage)core.CoreStage, core.StageEnteredAtUtc.UtcDateTime);
        }
    }
}
