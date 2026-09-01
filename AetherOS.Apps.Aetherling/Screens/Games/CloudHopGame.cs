using System;
using System.Collections.Generic;
using System.Numerics;
using AetherLove.Shared.Arcade;
using AetherOS.PetKit.Rendering;
using AetherOS.Apps.Aetherling.Ui;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;

namespace AetherOS.Apps.Aetherling.Screens.Games;

/// <summary>Cloud Hop: the companion bounces from cloud to cloud, ever upward, steered by the cursor.
/// Every bounce gains exactly one row (the arc cannot reach two, which is what keeps the score bound
/// honest), the sky pales from night toward dawn as the climb goes on, and a missed cloud ends the run
/// with a gentle drift down rather than a fall.</summary>
internal sealed class CloudHopGame : IPetGame
{
    private const float ViewHeightU = 340f;
    private const float PetU = 54f;
    private const float KeySteerSpeed = 1.2f;
    private const float StartHalf01 = 0.2f;

    private enum CloudKind
    {
        Puffy,
        Drifty,
        Wisp,
        Super,
        Bramble,
    }

    private struct CloudPad
    {
        public float X01;
        public float BaseX01;
        public float YU;
        public float Half01;
        public CloudKind Kind;
        public float Fade;
        public float DriftPhase;
        public float DriftSpeed;
        public int Row;
    }

    /// <summary>A grumpy little storm puff sweeping back and forth between two rows; brushing it mid-air
    /// ends the run. It sweeps ACROSS the cloud it guards rather than around the middle of the sky, and it
    /// always starts at one end of that sweep, so it arrives at the dangerous spot instead of appearing in
    /// one: a grump sitting on the only cloud of a row the moment it is drawn cannot be dodged, only
    /// out-waited, and the player usually has no room to wait.</summary>
    private struct Grump
    {
        public float YU;
        public float Centre;
        public float Phase;
        public float Speed;
        public float Amp;
    }

    /// <summary>Where a grump is right now. One place, because the collision check and the draw disagreeing
    /// about this is an enemy that kills from somewhere it is not.</summary>
    private static float GrumpX(Grump grump, float elapsed) =>
        grump.Centre + (MathF.Sin((elapsed * grump.Speed) + grump.Phase) * grump.Amp);

    private readonly List<CloudPad> _clouds = [];
    private readonly List<Grump> _grumps = [];
    private readonly ParticleFx _fx = new();

    private Random _rng = new();
    private float _petX;
    private float _petY;
    private float _vy;
    private float _cameraY;
    private float _squash;
    private float _elapsed;
    private float _endT;
    private bool _ending;
    private bool _facingLeft;
    private int _highestRow;
    private int _perfects;
    private int _topRow;
    private int _bonus;
    private bool _superFlight;
    private int _superLaunchRow;
    private float _lastMainX;
    private int _nextGrumpRow;
    private int _rowsSinceForced;

    public ArcadeGame Id => ArcadeGame.CloudHop;

    public bool Over { get; private set; }

    public int Score => (GameScoring.CloudHopRowPoints * _highestRow)
        + (GameScoring.CloudHopPerfectBonus * _perfects) + _bonus;

    public int Metric1 => _highestRow;

    public int Metric2 => _perfects;

    public void Reset(Random rng)
    {
        _rng = rng;
        _clouds.Clear();
        _grumps.Clear();
        _fx.Clear();
        _nextGrumpRow = 40 + _rng.Next(6);
        _rowsSinceForced = 0;
        _petX = 0.5f;
        _petY = 0f;
        _vy = GameScoring.CloudHopBounceVy;
        _cameraY = -ViewHeightU * 0.28f;
        _squash = 0f;
        _elapsed = 0f;
        _endT = 0f;
        _ending = false;
        Over = false;
        _facingLeft = false;
        _highestRow = 0;
        _perfects = 0;
        _topRow = 0;
        _bonus = 0;
        _superFlight = false;
        _superLaunchRow = 0;
        _lastMainX = 0.5f;

        // The launch pad: a generous cloud right underfoot, so the first second teaches the rules for free.
        _clouds.Add(new CloudPad { X01 = 0.5f, BaseX01 = 0.5f, YU = 0f, Half01 = StartHalf01, Kind = CloudKind.Puffy, Fade = 1f, Row = 0 });
    }

    public void UpdateAndDraw(OsAppContext ctx, ImDrawListPtr dl, GameStage stage, float dt)
    {
        var pxPerU = stage.Size.Y / ViewHeightU;
        var petPx = PetU * pxPerU;
        var petHalf01 = petPx * 0.5f / stage.Size.X;

        if (!_ending)
        {
            Steer(ctx, stage, petHalf01, dt);
            Simulate(stage, petHalf01, dt);
        }
        else
        {
            _endT += dt;
            _petY -= 12f * dt;
            if (_endT >= 1.1f)
            {
                Over = true;
            }
        }

        DrawSky(ctx, dl, stage);
        DrawClouds(ctx, dl, stage, pxPerU);
        DrawPet(ctx, dl, stage, pxPerU, petPx);
        DrawFx(dl, stage, dt);
        GameScene.Hud(dl, stage, Score.ToString("N0"), $"{_highestRow}", Look.Crystal);
        if (!_ending)
        {
            GameScene.KeyGuide(dl, stage, _elapsed);
        }
    }

    private void Steer(OsAppContext ctx, GameStage stage, float petHalf01, float dt)
    {
        if (!stage.InputActive)
        {
            return;
        }

        // Keyboard only: the cursor dies at the phone's edge, which made mouse steering a trap.
        var keys = ctx.Capabilities.Keyboard;
        var left = keys.IsDown(AppKey.A) || keys.IsDown(AppKey.Left);
        var right = keys.IsDown(AppKey.D) || keys.IsDown(AppKey.Right);
        if (left == right)
        {
            return;
        }
        var step = (right ? 1f : -1f) * KeySteerSpeed * dt;
        _facingLeft = step < 0f;
        _petX = Math.Clamp(_petX + step, petHalf01, 1f - petHalf01);
    }

    private void Simulate(GameStage stage, float petHalf01, float dt)
    {
        _elapsed += dt;
        _squash = MathF.Max(0f, _squash - dt);

        var prevY = _petY;
        _vy -= GameScoring.CloudHopGravity * dt;
        _petY += _vy * dt;

        for (var i = _clouds.Count - 1; i >= 0; i--)
        {
            var cloud = _clouds[i];
            if (cloud.Kind == CloudKind.Drifty)
            {
                var reach = MathF.Min(0.12f + (LateDifficulty(cloud.Row) * 0.08f), cloud.Half01 * 1.1f);
                cloud.X01 = cloud.BaseX01
                    + (MathF.Sin((_elapsed * cloud.DriftSpeed) + cloud.DriftPhase) * reach);
                _clouds[i] = cloud;
            }
            if (cloud.Fade < 1f)
            {
                cloud.Fade = MathF.Max(0f, cloud.Fade - (dt * 1.6f));
                _clouds[i] = cloud;
                if (cloud.Fade <= 0f)
                {
                    _clouds.RemoveAt(i);
                }
                continue;
            }

            if (_vy < 0f && prevY >= cloud.YU && _petY <= cloud.YU
                && MathF.Abs(_petX - cloud.X01) < cloud.Half01 + (petHalf01 * 0.5f))
            {
                if (cloud.Kind == CloudKind.Bramble)
                {
                    Bonk(stage);
                    return;
                }
                Land(stage, i, cloud);
                break;
            }
        }

        foreach (var grump in _grumps)
        {
            var gx = GrumpX(grump, _elapsed);
            if (MathF.Abs(_petX - gx) < petHalf01 + 0.04f
                && MathF.Abs(_petY + (PetU * 0.5f) - grump.YU) < PetU * 0.5f)
            {
                Bonk(stage);
                return;
            }
        }
        _grumps.RemoveAll(g => g.YU < _cameraY - GameScoring.CloudHopRowSpacing);

        var row = (int)MathF.Floor(_petY / GameScoring.CloudHopRowSpacing);
        if (row > _highestRow)
        {
            _highestRow = row;
        }

        // The companion rides low in the frame so most of the view is the sky still to come; what is
        // below is already history.
        var cameraTarget = _petY - (ViewHeightU * 0.3f);
        if (cameraTarget > _cameraY)
        {
            // A super launch outruns the easing, so the chase quickens with the climb and a hard clamp
            // keeps the companion from ever leaving the top of the view.
            var chase = _vy > 500f ? 11f : 6f;
            _cameraY += (cameraTarget - _cameraY) * MathF.Min(1f, dt * chase);
        }
        _cameraY = MathF.Max(_cameraY, _petY - (ViewHeightU * 0.9f));

        if (_superFlight && _vy > 100f && !stage.ReduceMotion && _rng.NextDouble() < 0.45)
        {
            var trail = GameScene.FxPoint(PetScreenPos(stage), FxBottom(stage), stage.Size.Y);
            _fx.Emit(ParticleKind.Sparkle, trail + new Vector2(0f, 6f), Look.Spark with { W = 0.85f }, 14f);
        }

        GenerateRows();
        _clouds.RemoveAll(c => c.YU < _cameraY - GameScoring.CloudHopRowSpacing);

        if (_petY < _cameraY - (PetU * 0.8f))
        {
            BeginEnding(stage);
            if (!stage.ReduceMotion)
            {
                var fxBottom = FxBottom(stage);
                _fx.Cascade(ParticleKind.Flake, GameScene.FxPoint(
                    new Vector2(stage.Origin.X + (stage.Size.X * 0.5f), stage.Origin.Y + (stage.Size.Y * 0.3f)),
                    fxBottom, stage.Size.Y), 14, Look.CrystalPale, 90f, 46f);
            }
        }
    }

    /// <summary>The scale of the late-game meanness, arriving after the plain difficulty has finished
    /// climbing so the sky never stops getting busier.</summary>
    private static float LateDifficulty(int row) => Math.Clamp((row - 40) / 100f, 0f, 1f);

    /// <summary>Brushed a bramble or a grump: a dazed little bounce into the farewell drift.</summary>
    private void Bonk(GameStage stage)
    {
        BeginEnding(stage);
        if (!stage.ReduceMotion)
        {
            var at = GameScene.FxPoint(PetScreenPos(stage), FxBottom(stage), stage.Size.Y);
            _fx.Burst(ParticleKind.Mote, at, 8, new Vector4(0.55f, 0.55f, 0.62f, 0.8f), 40f);
            _fx.BurstRadial(ParticleKind.Sparkle, at + new Vector2(0f, -30f), 6, Look.Spark, 12f, 60f);
        }
    }

    private void BeginEnding(GameStage stage)
    {
        _ending = true;
        _superFlight = false;
        // The farewell has to happen where the player can see it: the little rescue cloud carries the
        // companion back into the view and drifts there, rather than sinking below the camera.
        _petY = _cameraY + (ViewHeightU * 0.3f);
    }

    private void Land(GameStage stage, int index, CloudPad cloud)
    {
        // A landing settles the super flight it ends: the bonus pays per row the launch actually gained,
        // never flat, which is what keeps the server's per-row score bound true whatever caught it.
        if (_superFlight)
        {
            _superFlight = false;
            _bonus += GameScoring.CloudHopSuperBonusPerRow * Math.Max(0, cloud.Row - _superLaunchRow);
        }

        var super = cloud.Kind == CloudKind.Super;
        _vy = GameScoring.CloudHopBounceVy * (super ? GameScoring.CloudHopSuperBounceFactor : 1f);
        stage.Sound(GameSound.Jump);
        _petY = cloud.YU;
        _squash = super ? 0.16f : 0.1f;
        if (super)
        {
            _superFlight = true;
            _superLaunchRow = cloud.Row;
        }

        var perfect = MathF.Abs(_petX - cloud.X01) < cloud.Half01 * 0.36f;
        if (perfect)
        {
            _perfects++;
        }
        if (cloud.Kind == CloudKind.Wisp)
        {
            cloud.Fade = 0.999f;
            _clouds[index] = cloud;
        }

        if (stage.ReduceMotion)
        {
            return;
        }
        var fxBottom = FxBottom(stage);
        var at = GameScene.FxPoint(CloudScreen(stage, stage.Size.Y / ViewHeightU, cloud), fxBottom, stage.Size.Y);
        if (super)
        {
            _fx.BurstRadial(ParticleKind.Sparkle, at, 14, Look.Spark, 16f, 110f);
            _fx.Emit(ParticleKind.Ring, at, Look.Spark with { W = 0.9f }, 60f);
        }
        else
        {
            _fx.Emit(ParticleKind.Ring, at, Look.CrystalPale with { W = 0.8f }, 40f);
        }
        if (perfect)
        {
            _fx.BurstRadial(ParticleKind.Sparkle, at, 8, Look.Spark, 10f, 60f);
        }
    }

    private void GenerateRows()
    {
        var neededTopY = _cameraY + ViewHeightU + GameScoring.CloudHopRowSpacing;
        while ((_topRow + 1) * GameScoring.CloudHopRowSpacing < neededTopY)
        {
            _topRow++;
            _rowsSinceForced++;
            var difficulty = Math.Clamp(_topRow / 60f, 0f, 1f);
            var late = LateDifficulty(_topRow);
            var half = (0.17f - (difficulty * 0.095f)) * (0.9f + ((float)_rng.NextDouble() * 0.2f));

            // Every stretch of rows, one forced side-jump: the only safe cloud sits at the edge of what a
            // full bounce can reach, and there is no decoy to bail onto.
            var forced = _topRow > 20 && _rowsSinceForced >= 9 && _rng.NextDouble() < 0.55;
            float x;
            if (forced)
            {
                _rowsSinceForced = 0;
                var side = _lastMainX < 0.5f ? 1f : -1f;
                x = Math.Clamp(_lastMainX + (side * 0.44f), half + 0.02f, 1f - half - 0.02f);
            }
            else
            {
                x = Math.Clamp(
                    _lastMainX + ((((float)_rng.NextDouble() * 2f) - 1f) * 0.45f),
                    half + 0.02f, 1f - half - 0.02f);
            }
            _lastMainX = x;

            var kind = CloudKind.Puffy;
            if (_topRow >= 6 && _rng.NextDouble() < 0.1)
            {
                kind = CloudKind.Super;
            }
            else if (_topRow >= 18 && _rng.NextDouble() < 0.12 + (difficulty * 0.14) + (late * 0.08))
            {
                kind = CloudKind.Wisp;
            }
            else if (_topRow >= 8 && _rng.NextDouble() < 0.18 + (difficulty * 0.18) + (late * 0.1))
            {
                kind = CloudKind.Drifty;
            }
            if (kind == CloudKind.Super)
            {
                // A super is a treat: slightly easier to hit than the row it replaces.
                half = MathF.Max(half, 0.1f);
            }
            _clouds.Add(new CloudPad
            {
                X01 = x,
                BaseX01 = x,
                YU = _topRow * GameScoring.CloudHopRowSpacing,
                Half01 = half,
                Kind = kind,
                Fade = 1f,
                DriftPhase = (float)_rng.NextDouble() * MathF.Tau,
                DriftSpeed = 0.9f + (late * 1.3f) + ((float)_rng.NextDouble() * 0.3f),
                Row = _topRow,
            });

            // The second slot: early on it is a friendly decoy, and from row 30 it turns into a bramble
            // more and more often, so the busy sky starts carrying things you must NOT land on. The safe
            // main cloud above always exists, so a bramble is a trap, never a wall. A forced-jump row
            // keeps its sky empty on purpose.
            if (!forced && _rng.NextDouble() < 0.4 - (difficulty * 0.15) + (late * 0.25))
            {
                var dx = Math.Clamp(x + (x < 0.5f ? 0.34f : -0.34f), half + 0.02f, 1f - half - 0.02f);
                var decoyKind = _topRow >= 30 && _rng.NextDouble() < 0.35 + (late * 0.35)
                    ? CloudKind.Bramble
                    : CloudKind.Puffy;
                _clouds.Add(new CloudPad
                {
                    X01 = dx,
                    BaseX01 = dx,
                    YU = _topRow * GameScoring.CloudHopRowSpacing,
                    Half01 = half * 0.85f,
                    Kind = decoyKind,
                    Fade = 1f,
                    DriftPhase = (float)_rng.NextDouble() * MathF.Tau,
                    Row = _topRow,
                });
            }

            if (_topRow >= _nextGrumpRow)
            {
                _nextGrumpRow = _topRow + 7 + _rng.Next(5) - (int)(late * 3f);
                var amp = 0.24f + (late * 0.16f);

                // Centred on the cloud it guards rather than on the middle of the sky, and started at one
                // END of that sweep. Sweeping around 0.5 with a random phase put a grump directly over the
                // row's only cloud often enough to be common, and there is nothing to do about that: the
                // pet is already climbing and cannot stay put. Starting at an extreme means the threat is
                // always approaching, which is a thing a player can read and beat.
                var centre = Math.Clamp(x, amp + 0.04f, 1f - amp - 0.04f);
                var speed = 0.5f + (late * 0.6f) + ((float)_rng.NextDouble() * 0.3f);

                // The phase is solved against the CURRENT clock, not set to a constant: the sweep reads
                // sin(elapsed * speed + phase), and elapsed is minutes into a run by now, so a fixed phase
                // would drop the grump at an arbitrary point of its arc and put us back where we started.
                // Sent off toward the wider side, which buys the longest approach before it is over the cloud.
                var quarter = centre <= 0.5f ? MathF.PI * 0.5f : MathF.PI * 1.5f;
                _grumps.Add(new Grump
                {
                    YU = (_topRow * GameScoring.CloudHopRowSpacing) + (GameScoring.CloudHopRowSpacing * 0.5f),
                    Centre = centre,
                    Phase = quarter - (_elapsed * speed),
                    Speed = speed,
                    Amp = amp,
                });
            }
        }
    }

    private void DrawSky(OsAppContext ctx, ImDrawListPtr dl, GameStage stage)
    {
        var dawn = Math.Clamp(_highestRow / 80f, 0f, 1f);
        var top = Vector4.Lerp(new Vector4(0.045f, 0.055f, 0.115f, 1f), new Vector4(0.40f, 0.33f, 0.56f, 1f), dawn);
        var bottom = Vector4.Lerp(new Vector4(0.10f, 0.11f, 0.20f, 1f), new Vector4(0.83f, 0.56f, 0.50f, 1f), dawn);
        GameScene.Sky(dl, stage.Origin, stage.Size, top, bottom, Look.Crystal);
        Look.Motes(dl, stage.Origin, stage.Size, 26, Look.CrystalPale, 0.5f * (1f - (dawn * 0.7f)),
            ImGui.GetTime(), stage.ReduceMotion);
    }

    private void DrawClouds(OsAppContext ctx, ImDrawListPtr dl, GameStage stage, float pxPerU)
    {
        var padArt = ctx.Capabilities.Textures.Get(System.IO.Path.Combine(stage.AssetRoot, "games", "cloudpad.png"));
        var boostArt = ctx.Capabilities.Textures.Get(System.IO.Path.Combine(stage.AssetRoot, "games", "cloudboost.png"));
        foreach (var cloud in _clouds)
        {
            var at = CloudScreen(stage, pxPerU, cloud);
            if (at.Y < stage.Origin.Y - Px(40f) || at.Y > stage.Origin.Y + stage.Size.Y + Px(40f))
            {
                continue;
            }
            var half = cloud.Half01 * stage.Size.X;
            if (cloud.Kind == CloudKind.Super)
            {
                var pulse = stage.ReduceMotion ? 0.5f : Look.Breathe(ImGui.GetTime(), 1.6f, cloud.DriftPhase);
                Look.Halo(dl, at, half * 1.9f, Look.Spark, (0.14f + (0.12f * pulse)) * cloud.Fade, 4);
                GameScene.Cloud(dl, at, half, new Vector4(0.98f, 0.84f, 0.46f, 1f), 0.6f * cloud.Fade, boostArt);
            }
            else if (cloud.Kind == CloudKind.Bramble)
            {
                DrawBramble(dl, at, half, cloud.Fade);
            }
            else
            {
                var alpha = cloud.Kind == CloudKind.Wisp ? 0.3f * cloud.Fade : 0.5f * cloud.Fade;
                GameScene.Cloud(dl, at, half, Look.CrystalPale, alpha, padArt);
            }
        }

        DrawGrumps(dl, stage, pxPerU);
    }

    /// <summary>A bramble: a dusk-plum cloud wearing a row of thorns, unmistakably not a place to land.</summary>
    private static void DrawBramble(ImDrawListPtr dl, Vector2 at, float half, float fade)
    {
        var plum = new Vector4(0.44f, 0.32f, 0.52f, 1f);
        GameScene.Cloud(dl, at, half, plum, 0.55f * fade);
        var thorn = Look.U32(new Vector4(0.30f, 0.20f, 0.40f, 1f), fade);
        var baseY = at.Y - (half * 0.5f);
        var height = half * 0.42f;
        for (var i = -2; i <= 2; i++)
        {
            var cx = at.X + (i * half * 0.34f);
            dl.PathLineTo(new Vector2(cx - (half * 0.12f), baseY));
            dl.PathLineTo(new Vector2(cx, baseY - height));
            dl.PathLineTo(new Vector2(cx + (half * 0.12f), baseY));
            dl.PathFillConvex(thorn);
        }
    }

    private void DrawGrumps(ImDrawListPtr dl, GameStage stage, float pxPerU)
    {
        var size = PetU * pxPerU * 0.34f;
        foreach (var grump in _grumps)
        {
            var gx = GrumpX(grump, _elapsed);
            var at = new Vector2(
                stage.Origin.X + (gx * stage.Size.X),
                stage.Origin.Y + stage.Size.Y - ((grump.YU - _cameraY) * pxPerU));
            if (at.Y < stage.Origin.Y - Px(30f) || at.Y > stage.Origin.Y + stage.Size.Y + Px(30f))
            {
                continue;
            }
            GameScene.Puff(dl, at, size);
            // Angled brows over the puff's dot eyes are the whole difference between sleepy and cross.
            var ink = Look.U32(new Vector4(0.18f, 0.18f, 0.24f, 1f));
            dl.AddLine(at + new Vector2(-size * 0.4f, -size * 0.34f), at + new Vector2(-size * 0.1f, -size * 0.2f),
                ink, MathF.Max(1.2f, size * 0.08f));
            dl.AddLine(at + new Vector2(size * 0.4f, -size * 0.34f), at + new Vector2(size * 0.1f, -size * 0.2f),
                ink, MathF.Max(1.2f, size * 0.08f));
        }
    }

    private Vector2 CloudScreen(GameStage stage, float pxPerU, CloudPad cloud) => new(
        stage.Origin.X + (cloud.X01 * stage.Size.X),
        stage.Origin.Y + stage.Size.Y - ((cloud.YU - _cameraY) * pxPerU));

    private Vector2 PetScreenPos(GameStage stage) => new(
        stage.Origin.X + (_petX * stage.Size.X),
        stage.Origin.Y + stage.Size.Y - ((_petY - _cameraY) * (stage.Size.Y / ViewHeightU)));

    private void DrawPet(OsAppContext ctx, ImDrawListPtr dl, GameStage stage, float pxPerU, float petPx)
    {
        var bottom = new Vector2(
            stage.Origin.X + (_petX * stage.Size.X),
            stage.Origin.Y + stage.Size.Y - ((_petY - _cameraY) * pxPerU));

        if (_ending)
        {
            GameScene.Cloud(dl, bottom + new Vector2(0f, petPx * 0.08f), petPx * 0.7f, Look.CrystalPale, 0.3f);
        }

        var pose = new Engine.PetPose { Scale = Vector2.One, FlipX = _facingLeft };
        if (_ending)
        {
            pose.CellIndex = GameScene.Cell(stage.Manifest, "nap", 0f);
        }
        else if (_squash > 0f && !stage.ReduceMotion)
        {
            pose.CellIndex = GameScene.Cell(stage.Manifest, "hop", 0f);
            pose.Scale = new Vector2(1.18f, 0.8f);
        }
        else if (_vy > 60f)
        {
            pose.CellIndex = GameScene.Cell(stage.Manifest, "hop", 0.3f);
            pose.Scale = stage.ReduceMotion ? Vector2.One : new Vector2(0.92f, 1.1f);
        }
        else if (_vy > -60f)
        {
            pose.CellIndex = GameScene.Cell(stage.Manifest, "hop", 0.55f);
        }
        else
        {
            pose.CellIndex = GameScene.Cell(stage.Manifest, "hop", 0.75f);
        }
        stage.Runtime.Draw(dl, ctx.Capabilities.Textures, bottom, petPx, pose, props: false);
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
