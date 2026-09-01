using System;
using System.Collections.Generic;
using System.Numerics;
using AetherLove.Shared.Arcade;
using AetherOS.PetKit.Engine;
using AetherOS.PetKit.Rendering;
using AetherOS.Apps.Aetherling.Ui;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Aetherling.Screens.Games;

/// <summary>Hill Roll: the companion rides a little cart over rolling pastel hills. One button: hold to
/// roll, let go to coast. Carrying speed over a crest lifts the cart into the air, and a hard landing
/// wobbles it; wobble too much and it tips over in a dizzy, harmless tumble that costs one of three
/// lives, with a blinking Mario-style respawn on the very spot. Crystals sit along the road, some on the
/// crests where only a jump reaches them, and turbo bolts lock the cart at full tilt for a burst.</summary>
internal sealed class HillRollGame : IPetGame
{
    private const float ThrottleAccel = 12f;
    private const float CoastDrag = 4f;
    private const float SlopeFactor = 6f;
    private const float AirGravity = 22f;
    private const float SoftLanding = 13f;
    private const float TumbleImpact = 32f;
    private const float WobbleDecay = 0.6f;
    private const int TotalLives = 3;
    private const float RespawnBlinkSeconds = 1.6f;

    /// <summary>How much a badly angled touchdown hurts, per radian of mismatch per metre per second.
    /// The angle is the dominant term on purpose: meeting the road square is what a landing IS.</summary>
    private const float AngleImpactFactor = 26f;

    /// <summary>Radians per second the nose swings in the air. Fast enough to turn a bad attitude around
    /// inside a normal flight, which is the whole point of having the control at all.</summary>
    private const float AirPitchRate = 2.6f;

    private const float MaxAirPitch = 1.2f;

    /// <summary>Radians of travel over which the swing decelerates into its limit. Without it the nose
    /// turned at full rate right up to the clamp and stopped dead against a wall, which reads as the
    /// control breaking rather than the cart running out of rotation.</summary>
    private const float AirPitchEaseSpan = 0.5f;

    /// <summary>Metres of daylight under the wheels before the nose answers the button at all, and the
    /// stretch over which that authority fades in. A rut or a crest lifts the cart for an instant many
    /// times a minute, and every one of those was tipping the nose; a hop is not a jump.</summary>
    private const float AirControlHeight = 7.5f;
    private const float AirControlBlend = 3f;

    /// <summary>How much harder than gravity the road has to pull away before the wheels let go. Above 1
    /// the cart clings through minor crests instead of skipping off every one of them.</summary>
    private const float GroundStick = 1.35f;

    /// <summary>Metres per second squared the cart picks up while airborne. Small, but it means a jump
    /// never costs momentum and a long flight is quietly rewarded.</summary>
    private const float AirDriftAccel = 1.6f;


    /// <summary>No single landing may fill more than this much of the tip meter, so a tumble always takes
    /// at least two rough landings in a row; one bad bump is a wobble, never a death.</summary>
    private const float MaxWobblePerLanding = 0.6f;
    private const float MetresPerScreen = 46f;
    private const float CartAnchor01 = 0.32f;

    private readonly List<float> _pickups = [];
    private readonly List<float> _turbos = [];

    /// <summary>Boost rings hanging in the sky over a crest: X is the metre mark, Y the world height.
    /// Only a real jump reaches them, and the reward is the fastest the cart ever goes.</summary>
    private readonly List<Vector2> _airBoosts = [];
    private readonly ParticleFx _fx = new();

    private Random _rng = new();
    private float _x;
    private float _v;
    private bool _air;
    private float _airY;
    private float _airVy;
    private float _pitch;
    private float _wobble;
    private float _squash;
    private float _elapsed;
    private float _tumbleT;
    private bool _tumbling;
    private float _pickupCursor;
    private float _turboCursor;
    private float _airBoostCursor;
    private float _turboLeft;
    private float _movingTime;
    private float _blink;
    private float _runCyclePhase;
    private int _lives;
    private int _crystals;

    public ArcadeGame Id => ArcadeGame.HillRoll;

    public bool Over { get; private set; }

    public int Score => (int)(_x * GameScoring.HillRollPointsPerMetre) + (_crystals * GameScoring.HillRollCrystalPoints);

    public int Metric1 => (int)_x;

    public int Metric2 => _crystals;

    public void Reset(Random rng)
    {
        _rng = rng;
        _pickups.Clear();
        _fx.Clear();
        _x = 0f;
        _v = 0f;
        _air = false;
        _airY = 0f;
        _airVy = 0f;
        _pitch = 0f;
        _wobble = 0f;
        _squash = 0f;
        _elapsed = 0f;
        _tumbleT = 0f;
        _tumbling = false;
        Over = false;
        _pickupCursor = 30f;
        _turboCursor = 120f;
        _airBoostCursor = 200f;
        _airBoosts.Clear();
        _turboLeft = 0f;
        _movingTime = 0f;
        _blink = 0f;
        _lives = TotalLives;
        _turbos.Clear();
        _crystals = 0;
    }

    // Terrain height in metres above the base line: two sines whose amplitudes grow with distance, so
    // the road rolls harder the further it goes. StartRamp flattens the first stretch to an honest
    // runway, so the cart begins on stable ground and the first hills arrive once it is rolling. Pure
    // function, so nothing is stored or scrolled. Slope ignores the ramp's own tiny derivative: it is
    // at most a fiftieth of the hill slopes it feeds, and only where the hills are barely born.
    private static float StartRamp(float x) => Math.Clamp((x - 12f) / 70f, 0f, 1f);

    private static float Amp1(float x) => 6f + (6f * Math.Clamp(x / 600f, 0f, 1f));

    private static float Amp2(float x) => 2f + (3f * Math.Clamp(x / 600f, 0f, 1f));

    private static float Height(float x) => StartRamp(x)
        * ((Amp1(x) * MathF.Sin(x * 0.09f)) + (Amp2(x) * MathF.Sin((x * 0.23f) + 1.7f)));

    private static float Slope(float x) => StartRamp(x)
        * ((Amp1(x) * 0.09f * MathF.Cos(x * 0.09f)) + (Amp2(x) * 0.23f * MathF.Cos((x * 0.23f) + 1.7f)));

    /// <summary>The road's second derivative, which is what decides flight: following a crest that curves
    /// away needs downward acceleration of curvature times speed squared, and once that exceeds gravity
    /// the wheels leave the ground. Analytic like the slope, with the ramp's own derivative ignored.</summary>
    private static float Curvature(float x) => StartRamp(x)
        * (-(Amp1(x) * 0.09f * 0.09f * MathF.Sin(x * 0.09f))
            - (Amp2(x) * 0.23f * 0.23f * MathF.Sin((x * 0.23f) + 1.7f)));

    public void UpdateAndDraw(OsAppContext ctx, ImDrawListPtr dl, GameStage stage, float dt)
    {
        if (_tumbling)
        {
            _tumbleT += dt;
            if (_tumbleT >= 0.9f)
            {
                _lives--;
                if (_lives <= 0)
                {
                    Over = true;
                }
                else
                {
                    // The Mario respawn: same spot, dusted off, blinking for a moment while the player
                    // regathers. Speed and wobble start over; the road and the score do not.
                    _tumbling = false;
                    _tumbleT = 0f;
                    _v = 0f;
                    _wobble = 0f;
                    _air = false;
                    _pitch = 0f;
                    _blink = RespawnBlinkSeconds;
                }
            }
        }
        else
        {
            _blink = MathF.Max(0f, _blink - dt);
            Simulate(stage, dt);
        }

        DrawSky(dl, stage);
        DrawHills(dl, stage, back: true);
        DrawHills(dl, stage, back: false);
        DrawPickups(dl, stage);
        DrawCart(ctx, dl, stage, dt);
        DrawFx(dl, stage, dt);
        GameScene.Hud(dl, stage, Score.ToString("N0"), $"{(int)_x} m  ·  {_crystals}", Look.Spark);
        GameScene.Hearts(dl, stage, _lives, TotalLives);
        DrawSkyHint(ctx, dl, stage);
    }

    /// <summary>The one instruction, written in the sky until the cart has genuinely been rolling for a
    /// few seconds, then gone; a player who already knows never reads it twice.</summary>
    private void DrawSkyHint(OsAppContext ctx, ImDrawListPtr dl, GameStage stage)
    {
        var alpha = _movingTime < 3f ? 1f : 1f - ((_movingTime - 3f) / 1.5f);
        if (alpha <= 0f || _tumbling)
        {
            return;
        }
        Look.CentredWrapped(dl, ctx.Localize("os.aetherling_game_hillroll_hint"),
            stage.Origin.X + (stage.Size.X * 0.5f), stage.Origin.Y + (stage.Size.Y * 0.16f),
            stage.Size.X - Px(56f), Look.U32(Look.CrystalPale, alpha), 1.02f);
    }

    private void Simulate(GameStage stage, float dt)
    {
        _elapsed += dt;
        _squash = MathF.Max(0f, _squash - dt);

        var held = stage.InputActive && ImGui.IsMouseDown(ImGuiMouseButton.Left);
        // The button is a throttle only while there are wheels on the road. In the air it becomes the
        // rotation and nothing else, so the button is never asked to mean two things at once.
        if (!_air)
        {
            _v += (held ? ThrottleAccel : -CoastDrag) * dt;
            // The hill may never out-pull the throttle. There is no reverse gear here, so a slope steep
            // enough to stall the cart strands it on the face forever with nothing the player can press
            // to get off; capping the drag below the throttle guarantees a crawl up the worst of them.
            _v -= MathF.Min(Slope(_x) * SlopeFactor, ThrottleAccel * 0.75f) * dt;
        }
        else
        {
            // NOTHING takes speed away in the sky. A flight even gains a little, so a jump always pays
            // for itself and the air never reads as a punishment for leaving the road.
            _v += AirDriftAccel * dt;
        }

        // A turbo holds a floor under the speed rather than setting it, or an air boost's greater speed
        // would be dragged down to the ground turbo's the very next frame.
        _turboLeft = MathF.Max(0f, _turboLeft - dt);
        if (_turboLeft > 0f)
        {
            _v = MathF.Max(_v, GameScoring.HillRollTurboSpeed);
        }
        else if (_v > GameScoring.HillRollMaxSpeed)
        {
            // Excess bleeds off only once there are wheels on the road; the sky never claws it back.
            if (!_air)
            {
                _v = MathF.Max(GameScoring.HillRollMaxSpeed, _v - (8f * dt));
            }
        }
        else
        {
            _v = Math.Clamp(_v, 0f, GameScoring.HillRollMaxSpeed);
        }
        _v = Math.Clamp(_v, 0f, GameScoring.HillRollAirBoostSpeed);
        if (_v > 2f)
        {
            _movingTime += dt;
        }

        var prevX = _x;
        _x += _v * dt;

        var ground = Height(_x);
        if (_air)
        {
            _airVy -= AirGravity * dt;
            _airY += _airVy * dt;
            if (_airY <= ground)
            {
                // What hurts a landing is the ANGLE the cart meets the road at, not merely how fast it
                // was falling: nose-down onto a downslope is buttery at any speed, while arriving flat
                // or tail-first onto that same slope slams the chassis. So the mismatch between the
                // cart's attitude and the road's own angle is the dominant term, and the vertical
                // closing speed only tops it up.
                var roadPitch = MathF.Atan(-Slope(_x) * 0.5f);
                var mismatch = MathF.Abs(_pitch - roadPitch);
                var closing = MathF.Max(0f, (Slope(_x) * _v) - _airVy);
                var impact = (mismatch * _v * AngleImpactFactor / 26f) + (closing * 0.5f);

                _airY = ground;
                _air = false;
                _squash = 0.1f;
                _pitch = roadPitch;

                // A clean landing keeps its momentum and a slammed one scrubs speed off, which is the
                // feedback that teaches the angle without a word of instruction.
                _v *= 1f - MathF.Min(0.5f, mismatch * 0.6f);

                // A turbo landing forgives half the impact: the boost made the jump, so the boost pads
                // the touchdown; without this every pickup handed out a self-inflicted wobble.
                var accrual = MathF.Min(MaxWobblePerLanding, MathF.Max(0f, (impact - SoftLanding) / TumbleImpact));
                _wobble += _turboLeft > 0f ? accrual * 0.5f : accrual;

                if (!stage.ReduceMotion)
                {
                    if (impact > SoftLanding)
                    {
                        _fx.Emit(ParticleKind.Ring, CartFxPoint(stage), Look.CrystalPale with { W = 0.7f }, 44f);
                        _fx.Burst(ParticleKind.Pebble, CartFxPoint(stage), 5,
                            new Vector4(0.8f, 0.72f, 0.6f, 0.9f), 30f);
                    }
                    else if (mismatch < 0.12f && _v > 12f)
                    {
                        _fx.BurstRadial(ParticleKind.Sparkle, CartFxPoint(stage), 8, Look.Spark, 12f, 70f);
                    }
                }
                if (_wobble >= 1f)
                {
                    BeginTumble(stage);
                }
            }
        }
        else
        {
            // Real Hill Climb flight: the wheels leave the ground when the road curves away faster than
            // gravity can bend the cart's path after it, so speed over a bump IS the jump.
            var curve = Curvature(_x);
            if (curve < 0f && -curve * _v * _v > AirGravity * GroundStick)
            {
                _air = true;
                _airY = ground;
                _airVy = Slope(_x) * _v;
            }
            else
            {
                _airY = ground;
                _wobble = MathF.Max(0f, _wobble - (WobbleDecay * dt));
            }
        }

        // On the ground the nose follows the road: screen Y grows downward and the terrain draws at half
        // vertical scale, so it tracks the NEGATIVE slope and a descent pitches the front down. In the
        // air the player owns it outright, holding to lift the nose and releasing to drop it, which is
        // what makes a landing angle something to fly toward rather than something to suffer.
        var roadPitchNow = MathF.Atan(-Slope(_x) * 0.5f);
        if (_air)
        {
            // Authority arrives with altitude: skimming a bump the cart still hugs the road's angle, and
            // only real air hands the nose over to the player. It also means a descent levels out a
            // little in the last metre or two, which flatters a landing the player already aimed well.
            var authority = Math.Clamp((_airY - Height(_x) - AirControlHeight) / AirControlBlend, 0f, 1f);
            if (authority > 0f)
            {
                // Smoothstep into the limit: full swing while there is rotation left, easing off over the
                // last half radian so the nose settles at its extreme instead of hitting a stop.
                var target = held ? -MaxAirPitch : MaxAirPitch;
                var span = target - _pitch;
                var taper = MathF.Min(1f, MathF.Abs(span) / AirPitchEaseSpan);
                var eased = taper * taper * (3f - (2f * taper));
                _pitch += MathF.Sign(span) * AirPitchRate * authority * eased * dt;
                _pitch = Math.Clamp(_pitch, -MaxAirPitch, MaxAirPitch);
            }
            if (authority < 1f)
            {
                _pitch += (roadPitchNow - _pitch) * MathF.Min(1f, dt * 7f * (1f - authority));
            }
        }
        else
        {
            _pitch += (roadPitchNow - _pitch) * MathF.Min(1f, dt * 7f);
        }

        while (_pickupCursor < _x + (MetresPerScreen * 1.2f))
        {
            _pickups.Add(_pickupCursor);
            _pickupCursor += GameScoring.HillRollMinCrystalSpacing
                + ((float)_rng.NextDouble() * 35f);
        }
        while (_turboCursor < _x + (MetresPerScreen * 1.2f))
        {
            _turbos.Add(_turboCursor);
            _turboCursor += 90f + ((float)_rng.NextDouble() * 60f);
        }
        // A ring sits PAST a crest, not over it: the cart leaves the ground at the peak with almost no
        // upward speed, and its height is bought by the road falling away underneath. So the reachable
        // spot is a little way down the far side, near the crest's own height, where a fast cart is
        // still flying level and a slow one has already dropped past.
        while (_airBoostCursor < _x + (MetresPerScreen * 1.2f))
        {
            var peakX = _airBoostCursor;
            var peakH = Height(peakX);
            for (var probe = 0f; probe < 70f; probe += 2f)
            {
                var h = Height(_airBoostCursor + probe);
                if (h > peakH)
                {
                    peakH = h;
                    peakX = _airBoostCursor + probe;
                }
            }
            _airBoosts.Add(new Vector2(peakX + 16f + ((float)_rng.NextDouble() * 6f), peakH - 3f));
            _airBoostCursor = peakX + 160f + ((float)_rng.NextDouble() * 90f);
        }
        _turbos.RemoveAll(t => t < _x - MetresPerScreen);
        _airBoosts.RemoveAll(b => b.X < _x - MetresPerScreen);

        for (var i = _airBoosts.Count - 1; i >= 0; i--)
        {
            var boost = _airBoosts[i];
            if (MathF.Abs(boost.X - _x) < 3.5f && MathF.Abs(_airY - boost.Y) < 4.5f)
            {
                _airBoosts.RemoveAt(i);
                _v = GameScoring.HillRollAirBoostSpeed;
                _turboLeft = MathF.Max(_turboLeft, 1.6f);
                if (!stage.ReduceMotion)
                {
                    var at = CartFxPoint(stage);
                    _fx.BurstRadial(ParticleKind.Ember, at, 16, Look.Spark, 18f, 150f);
                    _fx.Emit(ParticleKind.Ring, at, Look.Spark with { W = 0.95f }, 80f);
                }
            }
        }

        for (var i = _turbos.Count - 1; i >= 0; i--)
        {
            if (MathF.Abs(_turbos[i] - _x) < 2.5f && MathF.Abs(_airY - Height(_turbos[i])) < 5f)
            {
                _turbos.RemoveAt(i);
                _turboLeft = 2.5f;
                if (!stage.ReduceMotion)
                {
                    _fx.BurstRadial(ParticleKind.Ember, CartFxPoint(stage), 10, Look.Spark, 14f, 90f);
                }
            }
        }
        if (_turboLeft > 0f && !stage.ReduceMotion && _rng.NextDouble() < 0.5)
        {
            _fx.Emit(ParticleKind.Ember, CartFxPoint(stage) + new Vector2(-24f, 0f),
                new Vector4(0.98f, 0.7f, 0.35f, 0.85f), 16f);
        }
        for (var i = _pickups.Count - 1; i >= 0; i--)
        {
            var at = _pickups[i];
            if (at < _x - MetresPerScreen)
            {
                _pickups.RemoveAt(i);
                continue;
            }
            if (MathF.Abs(at - _x) < 2f && MathF.Abs(_airY - Height(at)) < 6f)
            {
                _pickups.RemoveAt(i);
                _crystals++;
                stage.Sound(GameSound.Crystal);
                if (!stage.ReduceMotion)
                {
                    _fx.BurstRadial(ParticleKind.Sparkle, CartFxPoint(stage), 9, Look.Spark, 12f, 70f);
                }
            }
        }

        if (!stage.ReduceMotion && held && !_air && _v > 4f && _rng.NextDouble() < 0.3)
        {
            _fx.Emit(ParticleKind.Mote, CartFxPoint(stage) + new Vector2(-26f, 6f),
                new Vector4(0.85f, 0.76f, 0.62f, 0.5f), 18f);
        }
    }

    private void BeginTumble(GameStage stage)
    {
        _tumbling = true;
        _tumbleT = 0f;
        if (!stage.ReduceMotion)
        {
            var at = CartFxPoint(stage);
            _fx.BurstRadial(ParticleKind.Sparkle, at, 12, Look.Spark, 20f, 90f);
            _fx.Burst(ParticleKind.Mote, at, 8, Look.CrystalPale with { W = 0.7f }, 40f);
        }
    }

    private static void DrawSky(ImDrawListPtr dl, GameStage stage)
    {
        GameScene.Sky(dl, stage.Origin, stage.Size,
            new Vector4(0.16f, 0.10f, 0.19f, 1f), new Vector4(0.55f, 0.32f, 0.34f, 1f),
            new Vector4(0.95f, 0.72f, 0.5f, 1f));
        Look.Motes(dl, stage.Origin, stage.Size with { Y = stage.Size.Y * 0.5f }, 14, Look.CrystalPale, 0.35f,
            ImGui.GetTime(), stage.ReduceMotion);
    }

    /// <summary>The hills as vertical gradient strips under the sampled surface; the back layer is the
    /// same road half a screen ahead, dimmer and higher, for cheap depth.</summary>
    private void DrawHills(ImDrawListPtr dl, GameStage stage, bool back)
    {
        var step = MathF.Max(6f, stage.Size.X / 60f);
        var offset = back ? 140f : 0f;
        var lift = back ? stage.Size.Y * 0.12f : 0f;
        var topCol = back ? new Vector4(0.34f, 0.22f, 0.33f, 1f) : new Vector4(0.42f, 0.30f, 0.42f, 1f);
        var bottomCol = back ? new Vector4(0.22f, 0.14f, 0.24f, 1f) : new Vector4(0.16f, 0.11f, 0.20f, 1f);
        var top = Look.U32(topCol);
        var bottomC = Look.U32(bottomCol);
        var rim = Look.U32(Look.CrystalPale, back ? 0.12f : 0.3f);

        var bottomY = stage.Origin.Y + stage.Size.Y;
        float? prevSurface = null;
        var prevScreenX = 0f;
        for (var sx = 0f; sx <= stage.Size.X + step; sx += step)
        {
            var worldX = _x + ((sx / stage.Size.X - CartAnchor01) * MetresPerScreen) + offset;
            var surface = SurfaceY(stage, worldX) - lift;
            var screenX = stage.Origin.X + sx;
            if (prevSurface is { } prev)
            {
                dl.AddRectFilledMultiColor(
                    new Vector2(prevScreenX, MathF.Min(prev, surface)),
                    new Vector2(screenX, bottomY),
                    top, top, bottomC, bottomC);
                dl.AddLine(new Vector2(prevScreenX, prev), new Vector2(screenX, surface), rim, back ? 1f : 1.6f);
            }
            prevSurface = surface;
            prevScreenX = screenX;
        }
    }

    private float SurfaceY(GameStage stage, float worldX) => WorldY(stage, Height(worldX));

    private static float WorldY(GameStage stage, float worldHeight) =>
        stage.Origin.Y + (stage.Size.Y * 0.72f) - (worldHeight * (stage.Size.X / MetresPerScreen) * 0.5f);

    private void DrawPickups(ImDrawListPtr dl, GameStage stage)
    {
        var size = MathF.Max(Px(7f), stage.Size.X * 0.024f);
        foreach (var at in _pickups)
        {
            if (ScreenX(stage, at) is not { } sx)
            {
                continue;
            }
            var sy = SurfaceY(stage, at) - (size * 2.2f);
            GameScene.Crystal(dl, new Vector2(sx, sy), size, Look.Crystal);
        }
        foreach (var at in _turbos)
        {
            if (ScreenX(stage, at) is not { } sx)
            {
                continue;
            }
            var centre = new Vector2(sx, SurfaceY(stage, at) - (size * 2.6f));
            var pulse = stage.ReduceMotion ? 0.5f : Look.Breathe(ImGui.GetTime(), 1.2f, at);
            Look.Halo(dl, centre, size * 3f, Look.Spark, 0.18f + (0.1f * pulse), 3);
            dl.AddCircleFilled(centre, size * 1.5f, Look.U32(Look.Spark, 0.3f), 18);
            dl.AddCircle(centre, size * 1.5f, Look.U32(Look.Spark, 0.9f), 18, MathF.Max(1.2f, size * 0.14f));
            IconDraw.AddCentered(dl, FontAwesomeIcon.Bolt, size * 1.4f, centre, Look.U32(Look.Spark));
        }
        // Only visible to a cart that is actually flying: on the ground they are unreachable scenery, and
        // showing them there reads as a pickup the player keeps failing to collect.
        foreach (var boost in _airBoosts)
        {
            if (!_air || ScreenX(stage, boost.X) is not { } sx)
            {
                continue;
            }
            var centre = new Vector2(sx, WorldY(stage, boost.Y));
            var pulse = stage.ReduceMotion ? 0.5f : Look.Breathe(ImGui.GetTime(), 1f, boost.X);
            var ring = size * (2.1f + (0.25f * pulse));
            Look.Halo(dl, centre, ring * 2f, Look.Spark, 0.22f + (0.12f * pulse), 4);
            dl.AddCircle(centre, ring, Look.U32(Look.Spark, 0.95f), 26, MathF.Max(1.6f, size * 0.22f));
            dl.AddCircle(centre, ring * 0.62f, Look.U32(Look.CrystalPale, 0.55f), 22, MathF.Max(1f, size * 0.1f));
            IconDraw.AddCentered(dl, FontAwesomeIcon.AngleDoubleRight, size * 1.8f, centre,
                Look.U32(Look.CrystalPale));
        }
    }

    private float? ScreenX(GameStage stage, float worldX)
    {
        var sx = stage.Origin.X + ((((worldX - _x) / MetresPerScreen) + CartAnchor01) * stage.Size.X);
        return sx < stage.Origin.X - Px(24f) || sx > stage.Origin.X + stage.Size.X + Px(24f) ? null : sx;
    }

    private void DrawCart(OsAppContext ctx, ImDrawListPtr dl, GameStage stage, float dt)
    {
        var pxPerM = stage.Size.X / MetresPerScreen;
        var cartX = stage.Origin.X + (CartAnchor01 * stage.Size.X);
        var groundY = SurfaceY(stage, _x);
        var rideY = _air ? groundY - ((_airY - Height(_x)) * pxPerM * 0.5f) : groundY;

        // The respawn blink: cart and rider flicker while the player regathers; reduced motion keeps
        // them solid.
        if (_blink > 0f && !stage.ReduceMotion && (int)(_blink * 10f) % 2 == 0)
        {
            return;
        }

        var petPx = MathF.Min(stage.Size.X * 0.17f, stage.Size.Y * 0.2f);
        var cartW = petPx * 1.15f;
        var cartH = petPx * 0.34f;
        var wheelR = petPx * 0.14f;

        var pitch = _tumbling ? _tumbleT * 7f : _pitch;
        var (sin, cos) = MathF.SinCos(pitch);

        Vector2 Rot(Vector2 local) => new(
            cartX + (local.X * cos) - (local.Y * sin),
            rideY - wheelR + (local.X * sin) + (local.Y * cos));

        var bodyTL = new Vector2(-cartW * 0.5f, -cartH);
        dl.PathLineTo(Rot(bodyTL));
        dl.PathLineTo(Rot(new Vector2(cartW * 0.5f, -cartH)));
        dl.PathLineTo(Rot(new Vector2(cartW * 0.42f, 0f)));
        dl.PathLineTo(Rot(new Vector2(-cartW * 0.42f, 0f)));
        dl.PathFillConvex(Look.U32(new Vector4(0.42f, 0.72f, 0.70f, 1f)));
        dl.PathLineTo(Rot(bodyTL));
        dl.PathLineTo(Rot(new Vector2(cartW * 0.5f, -cartH)));
        dl.PathLineTo(Rot(new Vector2(cartW * 0.42f, 0f)));
        dl.PathLineTo(Rot(new Vector2(-cartW * 0.42f, 0f)));
        dl.PathStroke(Look.U32(Look.CrystalPale, 0.5f), ImDrawFlags.Closed, 1.4f);

        var wheelCol = Look.U32(new Vector4(0.20f, 0.22f, 0.30f, 1f));
        var hubCol = Look.U32(Look.CrystalPale, 0.8f);
        foreach (var wx in new[] { -cartW * 0.3f, cartW * 0.3f })
        {
            var wheel = Rot(new Vector2(wx, wheelR * 0.9f));
            dl.AddCircleFilled(wheel, wheelR, wheelCol, 18);
            dl.AddCircleFilled(wheel, wheelR * 0.32f, hubCol, 10);
        }

        var petBottom = Rot(new Vector2(0f, -cartH * 0.9f));
        var pose = new PetPose { Scale = Vector2.One };
        if (_tumbling)
        {
            pose.CellIndex = GameScene.Cell(stage.Manifest, "nap", 0f);
        }
        else if (_air)
        {
            pose.CellIndex = GameScene.Cell(stage.Manifest, "hop", 0.55f);
        }
        else if (_squash > 0f && !stage.ReduceMotion)
        {
            pose.CellIndex = GameScene.Cell(stage.Manifest, "idle", 0f);
            pose.Scale = new Vector2(1.15f, 0.85f);
        }
        else
        {
            _runCyclePhase += dt * MathF.Max(0.4f, _v / GameScoring.HillRollMaxSpeed);
            pose.CellIndex = GameScene.Cell(stage.Manifest, "idle", _runCyclePhase % 1f);
        }
        stage.Runtime.Draw(dl, ctx.Capabilities.Textures, petBottom, petPx, pose, props: false);
    }

    private Vector2 CartFxPoint(GameStage stage)
    {
        var cartX = stage.Origin.X + (CartAnchor01 * stage.Size.X);
        var pxPerM = stage.Size.X / MetresPerScreen;
        var groundY = SurfaceY(stage, _x);
        var rideY = _air ? groundY - ((_airY - Height(_x)) * pxPerM * 0.5f) : groundY;
        return GameScene.FxPoint(new Vector2(cartX, rideY), FxBottom(stage), stage.Size.Y);
    }

    private void DrawFx(ImDrawListPtr dl, GameStage stage, float dt)
    {
        _fx.Update(dt);
        if (!_fx.Any)
        {
            return;
        }
        var fxBottom = FxBottom(stage);
        _fx.Draw(dl, fxBottom, stage.Size.Y, behind: true);
        _fx.Draw(dl, fxBottom, stage.Size.Y, behind: false);
    }

    private static Vector2 FxBottom(GameStage stage) =>
        new(stage.Origin.X + (stage.Size.X * 0.5f), stage.Origin.Y + stage.Size.Y);
}
