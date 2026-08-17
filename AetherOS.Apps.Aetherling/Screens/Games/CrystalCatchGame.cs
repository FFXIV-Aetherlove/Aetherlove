using System;
using System.Collections.Generic;
using System.Numerics;
using AetherLove.Shared.Arcade;
using AetherOS.Apps.Aetherling.Engine;
using AetherOS.Apps.Aetherling.Rendering;
using AetherOS.Apps.Aetherling.Ui;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;

namespace AetherOS.Apps.Aetherling.Screens.Games;

/// <summary>Crystal Catch: the companion trots along the bottom of a dusk sky catching what is worth
/// catching and dodging what is not. Steered with A and D or the arrow keys (the cursor still works, but
/// keys never hit the phone's edge). The sky fills as the run goes on: bonus crystals worth triple,
/// falling capsules in the Breaker style (F speeds the legs, S slows them, M pulls every crystal in for a
/// few golden seconds), and a growing set of things that cost a heart: grey puffs, bombs, diving spikes
/// and cross little flyers. Every lost heart grants a moment of blinking grace, so nothing can chain.</summary>
internal sealed class CrystalCatchGame : IPetGame
{
    private const int TotalHearts = 3;
    private const float KeySteerSpeed = 1.5f;
    private const float MouthWidthFactor = 0.55f;
    private const float MouthHeightFactor = 0.65f;
    private const float GraceSeconds = 1.1f;
    private const float FastSeconds = 8f;
    private const float SlowSeconds = 6f;
    private const float MagnetSeconds = 5f;

    private enum ItemKind
    {
        Crystal,
        Bonus,
        Puff,
        Bomb,
        Spike,
        PowerFast,
        PowerSlow,
        PowerMagnet,
    }

    private struct Fall
    {
        public float X01;
        public float Y01;
        public float Vy01;
        public float Vx01;
        public float SwayAmp;
        public float SwayFreq;
        public float Sway;
        public ItemKind Kind;
        public int Element;
    }

    /// <summary>A grump crossing the lower sky; touching it costs a heart.</summary>
    private struct Flyer
    {
        public float X01;
        public float Y01;
        public float Dir;
        public float Speed;
    }

    private readonly List<Fall> _items = [];
    private readonly List<Flyer> _flyers = [];
    private readonly ParticleFx _fx = new();

    private Random _rng = new();
    private float _petX;
    private float _petVx;
    private float _elapsed;
    private float _spawnIn;
    private float _catchFlash;
    private float _droop;
    private float _runCycle;
    private float _endT;
    private float _grace;
    private float _fastLeft;
    private float _slowLeft;
    private float _magnetLeft;
    private float _nextFlyerAt;
    private bool _ending;
    private bool _facingLeft;
    private int _hearts;
    private int _caught;
    private int _combo;
    private int _bestCombo;
    private int _score;

    public ArcadeGame Id => ArcadeGame.CrystalCatch;

    public bool Over { get; private set; }

    public int Score => _score;

    public int Metric1 => _caught;

    public int Metric2 => _bestCombo;

    public void Reset(Random rng)
    {
        _rng = rng;
        _items.Clear();
        _flyers.Clear();
        _fx.Clear();
        _petX = 0.5f;
        _petVx = 0f;
        _elapsed = 0f;
        _spawnIn = 0.9f;
        _catchFlash = 0f;
        _droop = 0f;
        _runCycle = 0f;
        _endT = 0f;
        _grace = 0f;
        _fastLeft = 0f;
        _slowLeft = 0f;
        _magnetLeft = 0f;
        _nextFlyerAt = 50f + ((float)_rng.NextDouble() * 10f);
        _ending = false;
        Over = false;
        _facingLeft = false;
        _hearts = TotalHearts;
        _caught = 0;
        _combo = 0;
        _bestCombo = 0;
        _score = 0;
    }

    public void UpdateAndDraw(OsAppContext ctx, ImDrawListPtr dl, GameStage stage, float dt)
    {
        var petPx = MathF.Min(stage.Size.X * 0.24f, stage.Size.Y * 0.24f);

        if (!_ending)
        {
            Steer(ctx, stage, petPx, dt);
            Simulate(stage, petPx, dt);
        }
        else
        {
            _endT += dt;
            if (_endT >= 1f)
            {
                Over = true;
            }
        }

        DrawSky(dl, stage);
        DrawItems(dl, stage);
        DrawFlyers(dl, stage, petPx);
        DrawPet(ctx, dl, stage, petPx, dt);
        DrawFx(dl, stage, dt);
        GameScene.Hud(dl, stage, _score.ToString("N0"),
            _combo >= 3 ? $"x{Math.Min(_combo, GameScoring.CrystalCatchComboCap)}" : null, ComboColour());
        GameScene.Hearts(dl, stage, _hearts, TotalHearts);
        DrawPowerChips(dl, stage);
        if (!_ending)
        {
            GameScene.KeyGuide(dl, stage, _elapsed);
        }
    }

    private Vector4 ComboColour() =>
        Vector4.Lerp(Look.Crystal, Look.Spark, Math.Clamp(_combo / (float)GameScoring.CrystalCatchComboCap, 0f, 1f));

    private float SpeedScale() => (1f + (_fastLeft > 0f ? 0.5f : 0f)) * (_slowLeft > 0f ? 0.65f : 1f);

    private void Steer(OsAppContext ctx, GameStage stage, float petPx, float dt)
    {
        var petHalf01 = petPx * 0.5f / stage.Size.X;
        var previous = _petX;
        var scale = SpeedScale();

        // Keyboard only: the cursor dies at the phone's edge, which made mouse steering a trap. Polling
        // takes keyboard focus from the game, so it only happens while a round is genuinely being played.
        if (stage.InputActive)
        {
            var keys = ctx.Capabilities.Keyboard;
            var left = keys.IsDown(AppKey.A) || keys.IsDown(AppKey.Left);
            var right = keys.IsDown(AppKey.D) || keys.IsDown(AppKey.Right);
            if (left != right)
            {
                _petX += (right ? 1f : -1f) * KeySteerSpeed * scale * dt;
            }
        }
        _petX = Math.Clamp(_petX, petHalf01, 1f - petHalf01);
        _petVx = dt > 0f ? (_petX - previous) / dt : 0f;
        if (MathF.Abs(_petVx) > 0.02f)
        {
            _facingLeft = _petVx < 0f;
        }
    }

    private void Simulate(GameStage stage, float petPx, float dt)
    {
        _elapsed += dt;
        _catchFlash = MathF.Max(0f, _catchFlash - dt);
        _droop = MathF.Max(0f, _droop - dt);
        _grace = MathF.Max(0f, _grace - dt);
        _fastLeft = MathF.Max(0f, _fastLeft - dt);
        _slowLeft = MathF.Max(0f, _slowLeft - dt);
        _magnetLeft = MathF.Max(0f, _magnetLeft - dt);

        _spawnIn -= dt;
        if (_spawnIn <= 0f)
        {
            Spawn();
            var interval = MathF.Max(GameScoring.CrystalCatchSpawnFloorSeconds, 0.95f - (_elapsed * 0.0072f));
            _spawnIn += interval;
            // A twin drop consumes a second interval, so the overall rate never beats the floor the
            // server bounds against.
            if (_elapsed > 40f && _rng.NextDouble() < 0.3)
            {
                Spawn();
                _spawnIn += GameScoring.CrystalCatchSpawnFloorSeconds;
            }
        }

        if (_elapsed >= _nextFlyerAt)
        {
            _nextFlyerAt = _elapsed + 12f + ((float)_rng.NextDouble() * 8f);
            var dir = _rng.NextDouble() < 0.5 ? 1f : -1f;
            _flyers.Add(new Flyer
            {
                X01 = dir > 0f ? -0.08f : 1.08f,
                Y01 = 0.68f + ((float)_rng.NextDouble() * 0.1f),
                Dir = dir,
                Speed = 0.22f + (Math.Clamp(_elapsed / 240f, 0f, 1f) * 0.14f),
            });
        }

        var mouth = MouthBox(stage, petPx);
        var body = BodyBox(stage, petPx);
        for (var i = _items.Count - 1; i >= 0; i--)
        {
            var item = _items[i];
            item.Y01 += item.Vy01 * dt;
            item.X01 += item.Vx01 * dt;
            if (item.X01 < 0.05f || item.X01 > 0.95f)
            {
                item.X01 = Math.Clamp(item.X01, 0.05f, 0.95f);
                item.Vx01 = -item.Vx01;
            }
            // The magnet only courts the things worth having; hazards keep their own manners.
            if (_magnetLeft > 0f && item.Kind is ItemKind.Crystal or ItemKind.Bonus)
            {
                item.X01 += (_petX - item.X01) * MathF.Min(1f, dt * 3.2f);
            }
            _items[i] = item;

            var at = ItemScreen(stage, item);
            if (item.Kind == ItemKind.Spike && _grace <= 0f && body.Contains(at))
            {
                _items.RemoveAt(i);
                LoseHeart(stage, at, swallowed: true);
                continue;
            }
            if (mouth.Contains(at))
            {
                _items.RemoveAt(i);
                Catch(stage, item, at);
                continue;
            }
            if (item.Y01 >= 1f)
            {
                _items.RemoveAt(i);
                var floor = at with { Y = stage.Origin.Y + stage.Size.Y - Px(6f) };
                if (item.Kind is ItemKind.Crystal or ItemKind.Bonus)
                {
                    LoseHeart(stage, floor, swallowed: false);
                }
                else if (item.Kind == ItemKind.Bomb && !stage.ReduceMotion)
                {
                    // A grounded bomb pops harmlessly, which is its own small lesson in letting them fall.
                    _fx.BurstRadial(ParticleKind.Spark, GameScene.FxPoint(floor, FxBottom(stage), stage.Size.Y),
                        8, Look.Spark, 10f, 80f);
                }
            }
        }

        for (var i = _flyers.Count - 1; i >= 0; i--)
        {
            var flyer = _flyers[i];
            flyer.X01 += flyer.Dir * flyer.Speed * dt;
            _flyers[i] = flyer;
            if (flyer.X01 < -0.12f || flyer.X01 > 1.12f)
            {
                _flyers.RemoveAt(i);
                continue;
            }
            var at = FlyerScreen(stage, flyer);
            if (_grace <= 0f && body.Contains(at))
            {
                _flyers.RemoveAt(i);
                LoseHeart(stage, at, swallowed: true);
            }
        }
    }

    private void Spawn()
    {
        var ramp = Math.Clamp(_elapsed / 100f, 0f, 1f);
        var item = new Fall
        {
            X01 = SpawnX(),
            Y01 = -0.06f,
            Vy01 = (0.35f + (ramp * 0.5f)) * (0.9f + ((float)_rng.NextDouble() * 0.2f)),
            Kind = ItemKind.Crystal,
            Element = _rng.Next(Elements.All.Count),
            Sway = (float)_rng.NextDouble() * MathF.Tau,
        };

        var hazardShare = 0.15f + MathF.Min(0.15f, _elapsed * 0.0017f);
        var roll = _rng.NextDouble();
        if (_elapsed > 25f && roll < 0.05)
        {
            var power = _rng.NextDouble();
            item.Kind = power < 0.4 ? ItemKind.PowerFast : power < 0.75 ? ItemKind.PowerSlow : ItemKind.PowerMagnet;
        }
        else if (roll < hazardShare)
        {
            var hazard = _rng.NextDouble();
            if (_elapsed > 45f && hazard < 0.15)
            {
                item.Kind = ItemKind.Spike;
                item.Vy01 *= 1.35f;
                item.Vx01 = (_rng.NextDouble() < 0.5 ? -1f : 1f) * 0.12f;
            }
            else if (_elapsed > 30f && hazard < 0.4)
            {
                item.Kind = ItemKind.Bomb;
                item.Vy01 *= 0.9f;
            }
            else
            {
                item.Kind = ItemKind.Puff;
                item.Vy01 *= 0.85f;
            }
        }
        else if (_elapsed > 15f && _rng.NextDouble() < 0.08)
        {
            item.Kind = ItemKind.Bonus;
        }

        // Movement styles arrive with the ramp: diagonals from 18 seconds, swaying curves from 40, so the
        // whole width of the sky ends up in play and standing still stops being a plan.
        if (item.Kind is ItemKind.Crystal or ItemKind.Bonus or ItemKind.Puff or ItemKind.Bomb)
        {
            var style = _rng.NextDouble();
            if (_elapsed > 40f && style < 0.35)
            {
                item.SwayAmp = 0.06f + (ramp * 0.08f);
                item.SwayFreq = 1.8f + ((float)_rng.NextDouble() * 1.4f);
            }
            else if (_elapsed > 18f && style < 0.7)
            {
                var direction = _rng.NextDouble() < 0.5 ? -1f : 1f;
                item.Vx01 = direction * (0.05f + (ramp * 0.1f)) * (0.8f + ((float)_rng.NextDouble() * 0.4f));
            }
        }
        _items.Add(item);
    }

    /// <summary>A spawn column clear of everything still near the top, so a crystal never rides down glued
    /// to a bomb it is impossible to take without.</summary>
    private float SpawnX()
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var x = 0.08f + ((float)_rng.NextDouble() * 0.84f);
            var clear = true;
            foreach (var other in _items)
            {
                if (other.Y01 < 0.22f && MathF.Abs(other.X01 - x) < 0.14f)
                {
                    clear = false;
                    break;
                }
            }
            if (clear)
            {
                return x;
            }
        }
        return 0.08f + ((float)_rng.NextDouble() * 0.84f);
    }

    private void Catch(GameStage stage, Fall item, Vector2 at)
    {
        var fxAt = GameScene.FxPoint(at, FxBottom(stage), stage.Size.Y);
        switch (item.Kind)
        {
            case ItemKind.Crystal:
            case ItemKind.Bonus:
                var bonus = item.Kind == ItemKind.Bonus;
                var points = bonus ? GameScoring.CrystalCatchBonusPoints : GameScoring.CrystalCatchPoints;
                _score += points + Math.Min(GameScoring.CrystalCatchComboCap, _combo);
                _combo++;
                _bestCombo = Math.Max(_bestCombo, _combo);
                _caught++;
                _catchFlash = 0.15f;
                stage.Sound(bonus ? GameSound.BigCrystal : GameSound.Crystal);
                if (!stage.ReduceMotion)
                {
                    var accent = Elements.All[item.Element].Accent;
                    _fx.Burst(ParticleKind.Shard, fxAt, bonus ? 10 : 6, accent, 30f);
                    _fx.Burst(ParticleKind.Mote, fxAt, 4, Look.CrystalPale with { W = 0.7f }, 40f);
                    if (bonus)
                    {
                        _fx.BurstRadial(ParticleKind.Sparkle, fxAt, 12, Look.Spark, 16f, 90f);
                    }
                    else if (_combo > 0 && _combo % 5 == 0)
                    {
                        _fx.BurstRadial(ParticleKind.Sparkle, fxAt, 10, Look.Spark, 14f, 70f);
                    }
                }
                break;

            case ItemKind.PowerFast:
                _fastLeft = FastSeconds;
                EmitPower(stage, fxAt, Look.Crystal);
                break;
            case ItemKind.PowerSlow:
                _slowLeft = SlowSeconds;
                EmitPower(stage, fxAt, new Vector4(0.55f, 0.55f, 0.62f, 1f));
                break;
            case ItemKind.PowerMagnet:
                _magnetLeft = MagnetSeconds;
                EmitPower(stage, fxAt, Look.Spark);
                break;

            case ItemKind.Puff:
            case ItemKind.Bomb:
            case ItemKind.Spike:
                if (_grace <= 0f)
                {
                    stage.Sound(GameSound.Thud);
                    if (item.Kind == ItemKind.Bomb && !stage.ReduceMotion)
                    {
                        _fx.BurstRadial(ParticleKind.Spark, fxAt, 14, Look.Spark, 12f, 120f);
                        _fx.Burst(ParticleKind.Mote, fxAt, 6, new Vector4(0.3f, 0.3f, 0.34f, 0.9f), 40f);
                    }
                    LoseHeart(stage, at, swallowed: true);
                }
                break;
        }
    }

    private void EmitPower(GameStage stage, Vector2 fxAt, Vector4 colour)
    {
        if (!stage.ReduceMotion)
        {
            _fx.Emit(ParticleKind.Ring, fxAt, colour with { W = 0.9f }, 50f);
            _fx.Burst(ParticleKind.Sparkle, fxAt, 6, colour, 24f);
        }
    }

    private void LoseHeart(GameStage stage, Vector2 at, bool swallowed)
    {
        _hearts--;
        _combo = 0;
        _droop = 0.4f;
        _grace = GraceSeconds;

        if (!stage.ReduceMotion)
        {
            var fxAt = GameScene.FxPoint(at, FxBottom(stage), stage.Size.Y);
            if (swallowed)
            {
                _fx.Burst(ParticleKind.Mote, fxAt, 8, new Vector4(0.55f, 0.55f, 0.62f, 0.8f), 40f);
            }
            else
            {
                _fx.Emit(ParticleKind.Ring, fxAt, Look.Whisper with { W = 0.7f }, 46f);
            }
        }
        if (_hearts <= 0)
        {
            _ending = true;
        }
    }

    private static void DrawSky(ImDrawListPtr dl, GameStage stage)
    {
        GameScene.Sky(dl, stage.Origin, stage.Size,
            new Vector4(0.10f, 0.07f, 0.18f, 1f), new Vector4(0.23f, 0.14f, 0.28f, 1f),
            new Vector4(0.73f, 0.62f, 0.95f, 1f));
        Look.Motes(dl, stage.Origin, stage.Size, 20, Look.CrystalPale, 0.4f, ImGui.GetTime(), stage.ReduceMotion);
    }

    private void DrawItems(ImDrawListPtr dl, GameStage stage)
    {
        var size = MathF.Max(Px(9f), stage.Size.X * 0.032f);
        foreach (var item in _items)
        {
            var at = ItemScreen(stage, item);
            switch (item.Kind)
            {
                case ItemKind.Crystal:
                    GameScene.Crystal(dl, at, size, Elements.All[item.Element].Accent);
                    break;
                case ItemKind.Bonus:
                    Look.Halo(dl, at, size * 3.4f, Look.Spark, 0.2f, 3);
                    GameScene.Crystal(dl, at, size * 1.6f, Look.Spark);
                    break;
                case ItemKind.Puff:
                    GameScene.Puff(dl, at, size * 1.15f);
                    break;
                case ItemKind.Bomb:
                    DrawBomb(dl, at, size);
                    break;
                case ItemKind.Spike:
                    DrawSpike(dl, at, size);
                    break;
                default:
                    DrawCapsule(dl, at, size, item.Kind);
                    break;
            }
        }
    }

    private void DrawBomb(ImDrawListPtr dl, Vector2 at, float size)
    {
        var r = size * 1.05f;
        dl.AddCircleFilled(at, r, Look.U32(new Vector4(0.16f, 0.16f, 0.2f, 1f)), 20);
        dl.AddCircle(at, r, Look.U32(new Vector4(0.5f, 0.5f, 0.58f, 0.8f)), 20, MathF.Max(1f, size * 0.12f));
        dl.AddCircleFilled(at + new Vector2(-r * 0.3f, -r * 0.3f), r * 0.24f, Look.U32(new Vector4(1f, 1f, 1f, 0.18f)), 10);
        var fuseTop = at + new Vector2(r * 0.3f, -r * 1.2f);
        dl.AddBezierCubic(at + new Vector2(0f, -r), at + new Vector2(0.1f * r, -r * 1.15f),
            fuseTop + new Vector2(-r * 0.2f, r * 0.1f), fuseTop, Look.U32(new Vector4(0.6f, 0.5f, 0.4f, 1f)),
            MathF.Max(1f, size * 0.12f));
        var twinkle = 0.6f + (0.4f * Look.Breathe(ImGui.GetTime(), 0.5f, at.X));
        dl.AddCircleFilled(fuseTop, r * 0.16f * twinkle, Look.U32(Look.Spark, twinkle), 8);
    }

    private static void DrawSpike(ImDrawListPtr dl, Vector2 at, float size)
    {
        var outer = size * 1.35f;
        var inner = size * 0.6f;
        const int Points = 8;
        for (var i = 0; i < Points * 2; i++)
        {
            var angle = MathF.PI * i / Points;
            var radius = i % 2 == 0 ? outer : inner;
            dl.PathLineTo(at + new Vector2(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius));
        }
        dl.PathFillConvex(Look.U32(new Vector4(0.42f, 0.46f, 0.60f, 0.95f)));
        dl.AddCircleFilled(at, inner * 0.7f, Look.U32(new Vector4(0.28f, 0.30f, 0.42f, 1f)), 14);
    }

    /// <summary>The Breaker capsule, translated into this app's softness: a rounded pill wearing one
    /// letter, colour saying friend or trouble before the letter is even read.</summary>
    private static void DrawCapsule(ImDrawListPtr dl, Vector2 at, float size, ItemKind kind)
    {
        var (letter, colour) = kind switch
        {
            ItemKind.PowerFast => ("F", Look.Crystal),
            ItemKind.PowerSlow => ("S", new Vector4(0.62f, 0.58f, 0.68f, 1f)),
            _ => ("M", Look.Spark),
        };
        var half = new Vector2(size * 1.5f, size * 0.95f);
        Look.Halo(dl, at, size * 2.6f, colour, 0.16f, 3);
        dl.AddRectFilled(at - half, at + half, Look.U32(colour, 0.3f), half.Y);
        dl.AddRect(at - half, at + half, Look.U32(colour, 0.85f), half.Y, ImDrawFlags.None, 1.4f);
        Look.Centred(dl, letter, at.X, at.Y - (ImGui.GetTextLineHeight() * 0.5f), Look.U32(Look.CrystalPale));
    }

    private void DrawFlyers(ImDrawListPtr dl, GameStage stage, float petPx)
    {
        var size = petPx * 0.3f;
        var ink = Look.U32(new Vector4(0.18f, 0.18f, 0.24f, 1f));
        foreach (var flyer in _flyers)
        {
            var at = FlyerScreen(stage, flyer);
            GameScene.Puff(dl, at, size);
            dl.AddLine(at + new Vector2(-size * 0.4f, -size * 0.34f), at + new Vector2(-size * 0.1f, -size * 0.2f),
                ink, MathF.Max(1.2f, size * 0.08f));
            dl.AddLine(at + new Vector2(size * 0.4f, -size * 0.34f), at + new Vector2(size * 0.1f, -size * 0.2f),
                ink, MathF.Max(1.2f, size * 0.08f));
        }
    }

    private Vector2 ItemScreen(GameStage stage, Fall item)
    {
        var sway = item.SwayAmp > 0f
            ? MathF.Sin((_elapsed * item.SwayFreq) + item.Sway) * item.SwayAmp
            : 0f;
        return new Vector2(
            stage.Origin.X + (Math.Clamp(item.X01 + sway, 0.03f, 0.97f) * stage.Size.X),
            stage.Origin.Y + (item.Y01 * stage.Size.Y));
    }

    private static Vector2 FlyerScreen(GameStage stage, Flyer flyer) => new(
        stage.Origin.X + (flyer.X01 * stage.Size.X),
        stage.Origin.Y + (flyer.Y01 * stage.Size.Y));

    private RectF MouthBox(GameStage stage, float petPx)
    {
        var centreX = stage.Origin.X + (_petX * stage.Size.X);
        var bottom = PetBottomY(stage);
        var halfW = petPx * MouthWidthFactor * 0.5f;
        var top = bottom - (petPx * MouthHeightFactor) - (petPx * 0.2f);
        return new RectF(centreX - halfW, top, centreX + halfW, bottom - (petPx * 0.1f));
    }

    private RectF BodyBox(GameStage stage, float petPx)
    {
        var centreX = stage.Origin.X + (_petX * stage.Size.X);
        var bottom = PetBottomY(stage);
        var halfW = petPx * 0.4f;
        return new RectF(centreX - halfW, bottom - (petPx * 0.95f), centreX + halfW, bottom);
    }

    private readonly record struct RectF(float Left, float Top, float Right, float Bottom)
    {
        public bool Contains(Vector2 p) => p.X >= Left && p.X <= Right && p.Y >= Top && p.Y <= Bottom;
    }

    private static float PetBottomY(GameStage stage) => stage.Origin.Y + stage.Size.Y - Px(10f);

    private void DrawPet(OsAppContext ctx, ImDrawListPtr dl, GameStage stage, float petPx, float dt)
    {
        var bottom = new Vector2(stage.Origin.X + (_petX * stage.Size.X), PetBottomY(stage));

        var glowColour = _magnetLeft > 0f ? Look.Spark : new Vector4(0.73f, 0.62f, 0.95f, 1f);
        Look.GroundGlow(dl, bottom + new Vector2(0f, Px(4f)), petPx * 0.7f, petPx * 0.16f, glowColour,
            _magnetLeft > 0f ? 0.55f : 0.4f);
        Look.GroundRipples(dl, bottom + new Vector2(0f, Px(4f)), petPx * 0.8f, petPx * 0.18f,
            Look.CrystalPale, stage.ReduceMotion ? 0f : 0.16f, ImGui.GetTime());

        // The classic grace blink: the companion flickers while it cannot be hurt. Reduced motion keeps
        // it solid, the grace itself is unchanged.
        if (_grace > 0f && !stage.ReduceMotion && (int)(_grace * 10f) % 2 == 0)
        {
            return;
        }

        var speed01 = Math.Clamp(MathF.Abs(_petVx) / (KeySteerSpeed * 1.5f), 0f, 1f);
        _runCycle += dt * (8f + (6f * speed01)) / 8f;

        var pose = new PetPose { Scale = Vector2.One, FlipX = _facingLeft };
        if (_ending || _droop > 0f)
        {
            pose.CellIndex = GameScene.Cell(stage.Manifest, "nap", 0f);
        }
        else if (_catchFlash > 0f)
        {
            pose.CellIndex = GameScene.Cell(stage.Manifest, "boop", 0.35f);
            pose.Scale = stage.ReduceMotion ? Vector2.One : new Vector2(1.12f, 0.9f);
        }
        else
        {
            pose.CellIndex = GameScene.Cell(stage.Manifest, "idle", _runCycle % 1f);
        }
        stage.Runtime.Draw(dl, ctx.Capabilities.Textures, bottom, petPx, pose);
    }

    /// <summary>Small chips under the hearts naming what is in effect and how long it has left.</summary>
    private void DrawPowerChips(ImDrawListPtr dl, GameStage stage)
    {
        var y = stage.Origin.Y + Px(36f);
        var right = stage.Origin.X + stage.Size.X - Px(14f);
        DrawPowerChip(dl, ref right, y, "M", Look.Spark, _magnetLeft, MagnetSeconds);
        DrawPowerChip(dl, ref right, y, "S", new Vector4(0.62f, 0.58f, 0.68f, 1f), _slowLeft, SlowSeconds);
        DrawPowerChip(dl, ref right, y, "F", Look.Crystal, _fastLeft, FastSeconds);
    }

    private static void DrawPowerChip(ImDrawListPtr dl, ref float right, float y, string letter, Vector4 colour,
        float left, float total)
    {
        if (left <= 0f)
        {
            return;
        }
        var side = Px(18f);
        var tl = new Vector2(right - side, y);
        var alpha = 0.35f + (0.65f * Math.Clamp(left / total, 0f, 1f));
        dl.AddRectFilled(tl, tl + new Vector2(side, side), Look.U32(colour, 0.25f * alpha), side * 0.4f);
        dl.AddRect(tl, tl + new Vector2(side, side), Look.U32(colour, 0.8f * alpha), side * 0.4f,
            ImDrawFlags.None, 1.2f);
        Look.Centred(dl, letter, tl.X + (side * 0.5f), tl.Y + Px(2f), Look.U32(Look.CrystalPale, alpha), 0.85f);
        right -= side + Px(6f);
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
