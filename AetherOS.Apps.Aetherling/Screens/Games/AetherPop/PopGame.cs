using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using AetherLove.Services.Localization;
using AetherLove.Shared.Aetherling;
using AetherLove.Shared.Arcade;
using AetherLove.UI;
using AetherOS.Apps.Aetherling.Screens.Games.Gyre;
using AetherOS.Apps.Aetherling.Screens.Games.LumiLink;
using AetherOS.Apps.Aetherling.Ui;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Aetherling.Screens.Games.AetherPop;

/// <summary>Aether Pop, the bubble shooter. The board (<see cref="PopBoard"/>) decides what happens; this
/// class decides how it looks, sounds and feels: the shot banking off the walls, the grid jiggling where it
/// lands, pops that shatter, drops that fall past the line, the compactor grinding down, and the creature
/// in the cradle with its element powers riding the next shot.</summary>
internal sealed class PopGame : IPetGame
{
    private const float ClearCardSeconds = 1.8f;
    private const float StripHeight = 164f;
    private const float ShooterPetUnits = 132f;
    private const float CradleUnits = 96f;
    private const float GameOverHold = 1.4f;
    private const float TrailSeconds = 0.22f;
    private const int AimDots = 14;
    private const int AimBounces = 3;

    private sealed class Shot
    {
        public Vector2 Pos;
        public Vector2 Dir;
        public int Kind;
        public AetherlingElement Element;
        public float Travelled;
        public float Squash;
        public Vector2 SquashAxis;
        public readonly List<(Vector2 Pos, float Age)> Trail = [];
    }

    private sealed class Shard
    {
        public Vector2 Pos;
        public Vector2 Vel;
        public Vector4 Colour;
        public float Age;
        public float Life;
    }

    private sealed class Faller
    {
        public Vector2 Pos;
        public Vector2 Vel;
        public int Kind;
        public PopSpecial Special;
        public int Letter;
        public float Spin;
        public float Age;
    }

    private sealed class FloatText
    {
        public Vector2 At;
        public required string Text;
        public Vector4 Colour;
        public float Age;
        public float Scale = 0.95f;
    }

    private sealed class Ring
    {
        public Vector2 At;
        public float Age;
        public float Life;
        public Vector4 Colour;
        public float Reach;
    }

    /// <summary>A power's picture, played over the board for a moment after it fires.</summary>
    private sealed class PowerFx
    {
        public AetherlingElement Element;
        public Vector2 At;
        public float RowY;
        public float Age;
        public readonly List<Vector2> Bolt = [];
    }

    private static readonly Vector4[] KindColours = PopPieces.KindColours;
    private static readonly string[] Elements = PopPieces.Elements;
    private static readonly AetherlingElement[] ElementOrder = LumiLinkGame.ElementOrder;
    private static readonly Vector4 Gold = Look.Spark;
    private static readonly Vector4 Danger = new(1f, 0.32f, 0.3f, 1f);

    private static readonly (Vector4 Top, Vector4 Bottom)[] ChapterTints =
    [
        (new Vector4(0.36f, 0.58f, 0.86f, 1f), new Vector4(0.62f, 0.8f, 0.94f, 1f)),
        (new Vector4(0.28f, 0.52f, 0.46f, 1f), new Vector4(0.5f, 0.72f, 0.5f, 1f)),
        (new Vector4(0.34f, 0.26f, 0.5f, 1f), new Vector4(0.6f, 0.36f, 0.56f, 1f)),
        (new Vector4(0.12f, 0.08f, 0.16f, 1f), new Vector4(0.34f, 0.12f, 0.16f, 1f)),
    ];

    private readonly PopBoard _board = new();
    private readonly List<Shot> _shots = [];
    private readonly List<Shard> _shards = [];
    private readonly List<Faller> _fallers = [];
    private readonly List<FloatText> _texts = [];
    private readonly List<Ring> _rings = [];
    private readonly List<PowerFx> _powerFx = [];
    private readonly List<(float Delay, Action Fire)> _queue = [];
    private readonly Dictionary<(int Row, int Col), (float Amp, float Age)> _jiggle = [];
    private readonly float[] _letterFlash = new float[PopBoard.Letters.Length];

    private Random _rng = new();
    private bool _loaded;
    private int _heldKind;
    private int _nextKind;
    private int _meterPoints;
    private int _lastScore;
    private float _meterGlide;
    private float _shake;
    private Vector2 _shakeOffset;
    private float _lumiHop;
    private float _lumiSquash;
    private float _aimAngle;
    private float _ceilingGlide;
    private float _ceilingVel;
    private float _pushFlash;
    private float _clearLeft;
    private int _clearRound;
    private int _clearBonus;
    private float _overLeft;
    private bool _overShown;
    private float _aetherFlash;
    private float _liftStreaks;
    private float _redFlash;
    private AetherlingElement _armed;
    private AetherlingDto? _core;

    /// <summary>The field's height in canvas units this frame: the width is fixed at 1000 and the height
    /// is whatever the stage gives, so a taller phone shows more rows.</summary>
    private float _canvasH = PopRounds.ReferenceHeight;

    private bool _glideSet;

    public ArcadeGame Id => ArcadeGame.AetherPop;

    /// <summary>Over once the board says so and the squash has played out: the last moment needs to be seen.</summary>
    public bool Over => _loaded && _board.Over && _overLeft <= 0f;

    public int Score => _board.Score;

    public int Metric1 => _board.Round;

    public int Metric2 => _board.BiggestDrop;

    /// <summary>Which quarter of the ladder the run is in, 0..3; picks the chapter's music.</summary>
    public int Chapter => PopRounds.Chapter(_board.Round);

    public int FinalSteps => _board.FinalSteps;

    public void SetCreature(AetherlingDto? core) => _core = core;

    public void Reset(Random rng)
    {
        _rng = rng;
        _loaded = false;
        _shots.Clear();
        _shards.Clear();
        _fallers.Clear();
        _texts.Clear();
        _rings.Clear();
        _powerFx.Clear();
        _queue.Clear();
        _jiggle.Clear();
        Array.Fill(_letterFlash, 0f);
        _meterPoints = 0;
        _lastScore = 0;
        _meterGlide = 0f;
        _shake = 0f;
        _lumiHop = 0f;
        _lumiSquash = 0f;
        _aimAngle = -MathF.PI / 2f;
        _ceilingGlide = 0f;
        _ceilingVel = 0f;
        _pushFlash = 0f;
        _clearLeft = 0f;
        _overLeft = 0f;
        _overShown = false;
        _aetherFlash = 0f;
        _liftStreaks = 0f;
        _redFlash = 0f;
        _armed = AetherlingElement.None;
        _glideSet = false;
    }

    private bool MeterFull => _meterPoints >= GameScoring.PopPowerMeterPoints;

    public void UpdateAndDraw(OsAppContext ctx, ImDrawListPtr dl, GameStage stage, float dt)
    {
        if (!_loaded)
        {
            _board.Reset(_rng, PopRounds.Load(stage.AssetRoot));
            _heldKind = _board.RollShotKind();
            _nextKind = _board.RollShotKind();
            _loaded = true;
        }

        var origin = stage.Origin;
        var size = stage.Size;
        // The strip sits UNDER the creature, so the board starts at the top and the powers read as
        // Lumi's own (owner, 2026-09-01).
        var fieldTl = origin;
        var fieldSize = new Vector2(size.X, size.Y - StripHeight);
        var s = fieldSize.X / PopRounds.CanvasWidth;
        _canvasH = fieldSize.Y / s;
        _board.LineY = _canvasH - PopRounds.LineFromBottom;
        // The compactor rests just under the HUD band, whatever the phone's UI scale made of the band.
        _board.CeilingRest = ((HudBandHeight() + Px(10f)) / s) + PopRounds.CompactorHeight;
        if (!_glideSet)
        {
            _ceilingGlide = _board.CeilingY;
            _glideSet = true;
        }
        var canvasPx = fieldSize;
        var canvasTl = fieldTl;

        Vector2 ToScreen(Vector2 canvas) => canvasTl + (canvas * s) + _shakeOffset;

        if (_clearLeft > 0f)
        {
            _clearLeft = MathF.Max(0f, _clearLeft - dt);
            AdvanceFx(stage, dt);
        }
        else
        {
            Advance(stage, dt);
        }
        _shakeOffset = _shake > 0.01f && !stage.ReduceMotion
            ? new Vector2((float)((_rng.NextDouble() * 2) - 1), (float)((_rng.NextDouble() * 2) - 1)) * _shake
            : Vector2.Zero;

        DrawField(ctx, dl, stage, fieldTl, fieldSize, canvasTl, canvasPx, s, ToScreen);
        if (stage.InputActive && !_board.Over && _clearLeft <= 0f)
        {
            HandleInput(stage, canvasTl, s, fieldTl);
        }
        DrawBubbles(ctx, dl, stage, s, ToScreen);
        DrawCompactor(dl, stage, s, ToScreen);
        DrawLine(dl, stage, s, ToScreen);
        DrawShooter(ctx, dl, stage, s, ToScreen);
        DrawShotsAndFx(ctx, dl, stage, s, ToScreen);
        DrawStrip(ctx, dl, stage, new Vector2(origin.X, origin.Y + size.Y - StripHeight), new Vector2(size.X, StripHeight));
        DrawHud(dl, stage, fieldTl, fieldSize);
        DrawClearCard(dl, stage, fieldTl, fieldSize);
        DrawGameOver(dl, stage, fieldTl, fieldSize);
    }

    private Vector2 ShooterCanvas() => new(PopRounds.CanvasWidth * 0.5f, _canvasH - PopRounds.ShooterFromBottom);

    private void Advance(GameStage stage, float dt)
    {
        if (!_board.Over)
        {
            _board.Update(dt);
        }

        var speed = GameScoring.PopShotSpeed * dt;
        var minX = PopRounds.SideMargin + PopRounds.BubbleRadius;
        var maxX = PopRounds.CanvasWidth - PopRounds.SideMargin - PopRounds.BubbleRadius;
        for (var i = _shots.Count - 1; i >= 0; i--)
        {
            var shot = _shots[i];
            var steps = Math.Max(1, (int)(speed / 18f));
            var done = false;
            for (var j = 0; j < steps && !done; j++)
            {
                shot.Pos += shot.Dir * (speed / steps);
                shot.Travelled += speed / steps;
                if (shot.Pos.X < minX || shot.Pos.X > maxX)
                {
                    shot.Pos.X = Math.Clamp(shot.Pos.X, minX, maxX);
                    shot.Dir.X = -shot.Dir.X;
                    shot.Squash = 1f;
                    shot.SquashAxis = new Vector2(1f, 0f);
                    stage.Sound(GameSound.Swap);
                    _rings.Add(new Ring { At = shot.Pos, Colour = ShotColour(shot), Life = 0.3f, Reach = 60f });
                }
                if (shot.Element == AetherlingElement.Earth)
                {
                    var points = _board.MetalPass(shot.Pos);
                    if (points > 0)
                    {
                        _shake = MathF.Min(7f, _shake + 1.5f);
                        _rings.Add(new Ring { At = shot.Pos, Colour = KindColours[5], Life = 0.25f, Reach = 70f });
                    }
                    if (_board.AtCeiling(shot.Pos) || shot.Pos.Y < -60f)
                    {
                        _board.MetalDone(shot.Pos);
                        done = true;
                    }
                    continue;
                }
                if (_board.AtCeiling(shot.Pos) || _board.Touches(shot.Pos, out _, out _))
                {
                    _board.Land(shot.Pos, shot.Kind, shot.Element);
                    done = true;
                }
            }
            if (done || shot.Pos.Y < -200f)
            {
                _shots.RemoveAt(i);
            }
            else
            {
                shot.Squash = MathF.Max(0f, shot.Squash - (dt * 7f));
                shot.Trail.Add((shot.Pos, 0f));
                for (var k = shot.Trail.Count - 1; k >= 0; k--)
                {
                    var (p, age) = shot.Trail[k];
                    age += dt;
                    if (age > TrailSeconds)
                    {
                        shot.Trail.RemoveAt(k);
                    }
                    else
                    {
                        shot.Trail[k] = (p, age);
                    }
                }
            }
        }

        DrainEvents(stage);

        var gained = _board.Score - _lastScore;
        if (gained > 0)
        {
            _lastScore = _board.Score;
            _meterPoints = Math.Min(GameScoring.PopPowerMeterPoints, _meterPoints + gained);
        }
        AdvanceFx(stage, dt);
    }

    private void AdvanceFx(GameStage stage, float dt)
    {
        var meterTarget = Math.Clamp(_meterPoints / (float)GameScoring.PopPowerMeterPoints, 0f, 1f);
        _meterGlide += (meterTarget - _meterGlide) * MathF.Min(1f, dt * 8f);
        _shake = MathF.Max(0f, _shake - (dt * 26f));
        _lumiHop = MathF.Min(0f, _lumiHop + (dt * 60f));
        _lumiSquash = MathF.Max(0f, _lumiSquash - (dt * 3f));
        _pushFlash = MathF.Max(0f, _pushFlash - dt);
        _aetherFlash = MathF.Max(0f, _aetherFlash - dt);
        _liftStreaks = MathF.Max(0f, _liftStreaks - dt);
        _redFlash = MathF.Max(0f, _redFlash - dt);
        if (_overLeft > 0f)
        {
            _overLeft = MathF.Max(0f, _overLeft - dt);
        }
        for (var i = 0; i < _letterFlash.Length; i++)
        {
            _letterFlash[i] = MathF.Max(0f, _letterFlash[i] - dt);
        }

        // The compactor glides to where the board says it is, a critically damped spring so a push
        // arrives with weight and a lift floats back up.
        var target = _board.CeilingY;
        if (stage.ReduceMotion)
        {
            _ceilingGlide = target;
        }
        else
        {
            const float Stiff = 14f;
            var step = MathF.Min(dt, 0.05f);
            _ceilingVel += ((target - _ceilingGlide) * Stiff * Stiff * step) - (_ceilingVel * 2f * Stiff * step);
            _ceilingGlide += _ceilingVel * step;
        }

        for (var i = _queue.Count - 1; i >= 0; i--)
        {
            var (delay, fire) = _queue[i];
            delay -= dt;
            if (delay <= 0f)
            {
                _queue.RemoveAt(i);
                fire();
            }
            else
            {
                _queue[i] = (delay, fire);
            }
        }

        var keys = new List<(int, int)>(_jiggle.Keys);
        foreach (var key in keys)
        {
            var (amp, age) = _jiggle[key];
            age += dt;
            if (age > 0.6f)
            {
                _jiggle.Remove(key);
            }
            else
            {
                _jiggle[key] = (amp, age);
            }
        }

        for (var i = _shards.Count - 1; i >= 0; i--)
        {
            var sh = _shards[i];
            sh.Age += dt;
            sh.Vel += new Vector2(0f, 900f * dt);
            sh.Pos += sh.Vel * dt;
            if (sh.Age > sh.Life)
            {
                _shards.RemoveAt(i);
            }
        }
        for (var i = _fallers.Count - 1; i >= 0; i--)
        {
            var f = _fallers[i];
            f.Age += dt;
            f.Vel += new Vector2(0f, 1900f * dt);
            f.Pos += f.Vel * dt;
            f.Spin += f.Vel.Length() * dt;
            if (f.Pos.Y > _canvasH + 80f)
            {
                _fallers.RemoveAt(i);
            }
        }
        for (var i = _texts.Count - 1; i >= 0; i--)
        {
            _texts[i].Age += dt;
            _texts[i].At += new Vector2(0f, -34f * dt);
            if (_texts[i].Age > 1.2f)
            {
                _texts.RemoveAt(i);
            }
        }
        for (var i = _rings.Count - 1; i >= 0; i--)
        {
            _rings[i].Age += dt;
            if (_rings[i].Age > _rings[i].Life)
            {
                _rings.RemoveAt(i);
            }
        }
        for (var i = _powerFx.Count - 1; i >= 0; i--)
        {
            _powerFx[i].Age += dt;
            if (_powerFx[i].Age > 0.9f)
            {
                _powerFx.RemoveAt(i);
            }
        }
    }

    private void DrainEvents(GameStage stage)
    {
        foreach (var e in _board.Events)
        {
            switch (e.Kind)
            {
                case PopEventKind.Land:
                    stage.Sound(GameSound.Thud);
                    JiggleAround(e.At);
                    _lumiHop = -10f;
                    break;
                case PopEventKind.Pop:
                {
                    var at = e.At;
                    var colour = e.Colour;
                    var special = e.Special;
                    Later(e.Delay, () =>
                    {
                        SpawnShards(at, colour, special == PopSpecial.None ? 10 : 16, special == PopSpecial.Star);
                        _rings.Add(new Ring { At = at, Colour = colour >= 0 ? KindColours[colour] : Gold, Life = 0.4f, Reach = 90f });
                    });
                    break;
                }
                case PopEventKind.Drop:
                {
                    var at = e.At;
                    var colour = e.Colour;
                    var special = e.Special;
                    var letter = e.Letter;
                    Later(e.Delay, () => _fallers.Add(new Faller
                    {
                        Pos = at,
                        Vel = new Vector2(((float)_rng.NextDouble() - 0.5f) * 260f, -120f - ((float)_rng.NextDouble() * 160f)),
                        Kind = colour,
                        Special = special,
                        Letter = letter,
                    }));
                    break;
                }
                case PopEventKind.Points:
                    if (e.Count > 0)
                    {
                        // A drop: the ladder rung climbs with the size, and the count is the story.
                        stage.Sound((GameSound)((int)GameSound.Ladder0 + Math.Clamp(e.Count - 1, 0, 7)));
                        _texts.Add(new FloatText
                        {
                            At = e.At + new Vector2(0f, 40f),
                            Text = string.Format(Loc.T("os.aetherling_pop_drop"), e.Count, e.Points),
                            Colour = Gold,
                            Scale = e.Count >= 6 ? 1.25f : 1.05f,
                        });
                        _shake = MathF.Min(7f, _shake + 1f + (e.Count * 0.35f));
                        if (e.Count >= 4)
                        {
                            _lumiHop = -18f;
                        }
                    }
                    else
                    {
                        stage.Sound(GameSound.Crystal);
                        _texts.Add(new FloatText { At = e.At, Text = $"+{e.Points}", Colour = Look.CrystalPale });
                    }
                    break;
                case PopEventKind.StarBurst:
                    stage.Sound(GameSound.Chord2);
                    _rings.Add(new Ring { At = e.At, Colour = Gold, Life = 0.7f, Reach = 420f });
                    _shake = 5f;
                    break;
                case PopEventKind.LetterTaken:
                    stage.Sound(GameSound.BigCrystal);
                    if (e.Letter >= 0 && e.Letter < _letterFlash.Length)
                    {
                        _letterFlash[e.Letter] = 1f;
                    }
                    _texts.Add(new FloatText { At = e.At, Text = PopBoard.Letters[Math.Max(0, e.Letter)].ToString(), Colour = Gold, Scale = 1.4f });
                    break;
                case PopEventKind.AetherDone:
                    stage.Sound(GameSound.LevelUp);
                    _aetherFlash = 1.6f;
                    _liftStreaks = 0.8f;
                    _shake = 6f;
                    _texts.Add(new FloatText
                    {
                        At = new Vector2(PopRounds.CanvasWidth * 0.5f, _canvasH * 0.45f),
                        Text = $"+{e.Points:N0}",
                        Colour = Gold,
                        Scale = 1.5f,
                    });
                    break;
                case PopEventKind.Push:
                    stage.Sound(GameSound.Burst);
                    _pushFlash = 0.5f;
                    _shake = MathF.Min(7f, _shake + 3f);
                    JiggleAll();
                    break;
                case PopEventKind.Lift:
                    stage.Sound(GameSound.Bolt);
                    _liftStreaks = 0.7f;
                    break;
                case PopEventKind.RoundCleared:
                    stage.Sound(GameSound.LevelUp);
                    _clearLeft = stage.ReduceMotion ? ClearCardSeconds * 0.5f : ClearCardSeconds;
                    _clearRound = e.Count;
                    _clearBonus = e.Points;
                    _shots.Clear();
                    _ceilingGlide = _board.CeilingY;
                    _ceilingVel = 0f;
                    break;
                case PopEventKind.GameOver:
                    stage.Sound(GameSound.Bad);
                    _shake = 10f;
                    _redFlash = 0.8f;
                    _lumiSquash = 1f;
                    _overLeft = stage.ReduceMotion ? GameOverHold * 0.5f : GameOverHold;
                    _overShown = true;
                    _shots.Clear();
                    break;
                case PopEventKind.Fire:
                    stage.Sound(GameSound.Chord1);
                    _powerFx.Add(new PowerFx { Element = AetherlingElement.Fire, At = e.At, RowY = e.At.Y });
                    _shake = 5f;
                    break;
                case PopEventKind.Water:
                    stage.Sound(GameSound.Chord1);
                    _powerFx.Add(new PowerFx { Element = AetherlingElement.Water, At = e.At });
                    break;
                case PopEventKind.Bolt:
                {
                    stage.Sound(GameSound.Bolt);
                    var fx = new PowerFx { Element = AetherlingElement.Lightning, At = e.At, RowY = e.At.Y };
                    BuildBolt(fx);
                    _powerFx.Add(fx);
                    _shake = 6f;
                    break;
                }
                case PopEventKind.Frozen:
                    stage.Sound(GameSound.Chord0);
                    _rings.Add(new Ring { At = e.At, Colour = new Vector4(0.75f, 0.92f, 1f, 1f), Life = 0.5f, Reach = 200f });
                    break;
                case PopEventKind.Thawed:
                    stage.Sound(GameSound.Crystal);
                    SpawnShards(e.At, -2, 14, false);
                    break;
                case PopEventKind.Dealt:
                    JiggleAll();
                    break;
                case PopEventKind.LumiWake:
                    stage.Sound(GameSound.Chirp);
                    _rings.Add(new Ring { At = e.At, Colour = e.Colour >= 0 ? KindColours[e.Colour] : Gold, Life = 0.6f, Reach = 260f });
                    _texts.Add(new FloatText { At = e.At - new Vector2(0f, 30f), Text = Loc.T("os.aetherling_pop_wake"), Colour = e.Colour >= 0 ? KindColours[e.Colour] : Gold, Scale = 1.15f });
                    _shake = MathF.Min(7f, _shake + 3f);
                    _lumiHop = -16f;
                    break;
                case PopEventKind.Frost:
                    stage.Sound(GameSound.Chord2);
                    _rings.Add(new Ring { At = e.At, Colour = new Vector4(0.75f, 0.92f, 1f, 1f), Life = 0.5f, Reach = 300f });
                    _powerFx.Add(new PowerFx { Element = AetherlingElement.Ice, At = e.At });
                    break;
                case PopEventKind.Plough:
                    stage.Sound(GameSound.Burst);
                    _powerFx.Add(new PowerFx { Element = AetherlingElement.Earth, At = e.At });
                    _shake = 5f;
                    break;
            }
        }
        _board.Events.Clear();
    }

    private void Later(float delay, Action fire)
    {
        if (delay <= 0.001f)
        {
            fire();
            return;
        }
        _queue.Add((delay, fire));
    }

    private void JiggleAround(Vector2 at)
    {
        foreach (var (r, c, _) in _board.All())
        {
            var d = Vector2.Distance(_board.Centre(r, c), at);
            if (d < PopRounds.BubbleDiameter * 2.6f)
            {
                _jiggle[(r, c)] = (MathF.Max(0f, 1f - (d / (PopRounds.BubbleDiameter * 2.6f))) * 9f, 0f);
            }
        }
    }

    private void JiggleAll()
    {
        foreach (var (r, c, _) in _board.All())
        {
            _jiggle[(r, c)] = (5f, 0f);
        }
    }

    private void BuildBolt(PowerFx fx)
    {
        fx.Bolt.Clear();
        var left = PopRounds.SideMargin;
        var right = PopRounds.CanvasWidth - PopRounds.SideMargin;
        var steps = 22;
        for (var i = 0; i <= steps; i++)
        {
            var x = left + ((right - left) * i / steps);
            var y = fx.RowY + (((float)_rng.NextDouble() - 0.5f) * 44f);
            fx.Bolt.Add(new Vector2(x, y));
        }
    }

    private void SpawnShards(Vector2 at, int colour, int count, bool gold)
    {
        var c = colour >= 0 ? KindColours[colour] : colour == -2 ? new Vector4(0.8f, 0.94f, 1f, 1f) : Gold;
        for (var i = 0; i < count; i++)
        {
            var a = (float)(_rng.NextDouble() * MathF.Tau);
            var v = 160f + ((float)_rng.NextDouble() * 340f);
            _shards.Add(new Shard
            {
                Pos = at,
                Vel = new Vector2(MathF.Cos(a), MathF.Sin(a) * 0.9f) * v,
                Colour = gold && (i % 2 == 0) ? Gold : c,
                Life = 0.55f + ((float)_rng.NextDouble() * 0.4f),
            });
        }
    }

    private Vector4 ShotColour(Shot shot) => shot.Element switch
    {
        AetherlingElement.Earth => new Vector4(0.7f, 0.72f, 0.78f, 1f),
        AetherlingElement.None => KindColours[shot.Kind],
        _ => KindColours[Array.IndexOf(ElementOrder, shot.Element)],
    };

    private void HandleInput(GameStage stage, Vector2 canvasTl, float s, Vector2 fieldTl)
    {
        var mouse = ImGui.GetMousePos();
        if (mouse.Y > fieldTl.Y + (stage.Size.Y - StripHeight))
        {
            return;
        }
        var shooter = ShooterCanvas();
        var mouseCanvas = (mouse - canvasTl) / s;
        var dir = mouseCanvas - shooter;
        if (dir.LengthSquared() > 1f)
        {
            // Only upward, and never flat: a shot along the floor is a shot into the wall forever.
            var a = MathF.Atan2(dir.Y, dir.X);
            _aimAngle = Math.Clamp(a, -MathF.PI + 0.2f, -0.2f);
        }

        if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            (_heldKind, _nextKind) = (_nextKind, _heldKind);
            stage.Sound(GameSound.Swap);
        }
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left) && _shots.Count == 0 && _overLeft <= 0f)
        {
            var aim = new Vector2(MathF.Cos(_aimAngle), MathF.Sin(_aimAngle));
            var shot = new Shot
            {
                Pos = shooter + (aim * (CradleUnits + 10f)),
                Dir = aim,
                Kind = _heldKind,
                Element = _armed,
            };
            _shots.Add(shot);
            stage.Sound(GameSound.Jump);
            _lumiHop = -12f;
            if (_armed != AetherlingElement.None)
            {
                stage.Sound(GameSound.Chord0);
                _shake = 3f;
            }
            _armed = AetherlingElement.None;
            _heldKind = _nextKind;
            _nextKind = _board.RollShotKind();
        }
    }

    private void DrawField(OsAppContext ctx, ImDrawListPtr dl, GameStage stage, Vector2 fieldTl,
        Vector2 fieldSize, Vector2 canvasTl, Vector2 canvasPx, float s, Func<Vector2, Vector2> toScreen)
    {
        var chapter = Chapter;
        var (top, bottom) = ChapterTints[chapter];
        GameScene.Sky(dl, fieldTl, fieldSize, top, bottom, Look.CrystalPale);

        var file = _board.Final ? "bg_final.png" : $"bg_{chapter + 1}.png";
        var bgPath = Path.Combine(stage.AssetRoot, "games", "aetherpop", file);
        if (ctx.Capabilities.Textures.Get(bgPath) is { } bg)
        {
            // Cover, never stretch: the art is 1000x1540 and the field is whatever the stage is, so the
            // picture is cropped to the field's aspect about its centre.
            var artAspect = PopRounds.ReferenceHeight / PopRounds.CanvasWidth;
            var fieldAspect = _canvasH / PopRounds.CanvasWidth;
            var uv0 = Vector2.Zero;
            var uv1 = Vector2.One;
            if (fieldAspect < artAspect)
            {
                var keep = fieldAspect / artAspect;
                uv0.Y = (1f - keep) * 0.5f;
                uv1.Y = 1f - uv0.Y;
            }
            else
            {
                var keep = artAspect / fieldAspect;
                uv0.X = (1f - keep) * 0.5f;
                uv1.X = 1f - uv0.X;
            }
            dl.AddImage(bg, canvasTl + _shakeOffset, canvasTl + canvasPx + _shakeOffset, uv0, uv1);
        }
        else
        {
            Look.Motes(dl, canvasTl, canvasPx, 18, Look.CrystalPale, 0.35f, ImGui.GetTime(), stage.ReduceMotion);
        }

        // The playfield's own walls, so a bank shot has something to read off.
        var left = toScreen(new Vector2(PopRounds.SideMargin, 0f));
        var right = toScreen(new Vector2(PopRounds.CanvasWidth - PopRounds.SideMargin, 0f));
        var floor = toScreen(new Vector2(0f, _board.LineY)).Y;
        dl.AddRectFilled(new Vector2(left.X, canvasTl.Y + _shakeOffset.Y), new Vector2(right.X, floor),
            Look.U32(new Vector4(0f, 0f, 0f, 0.16f)));
        dl.AddLine(new Vector2(left.X, canvasTl.Y + _shakeOffset.Y), new Vector2(left.X, floor),
            Look.U32(Look.CrystalPale, 0.35f), MathF.Max(1.5f, 4f * s));
        dl.AddLine(new Vector2(right.X, canvasTl.Y + _shakeOffset.Y), new Vector2(right.X, floor),
            Look.U32(Look.CrystalPale, 0.35f), MathF.Max(1.5f, 4f * s));

        if (_liftStreaks > 0f && !stage.ReduceMotion)
        {
            // Wind: streaks racing up the field for the moment the board lifts.
            var t = 1f - (_liftStreaks / 0.8f);
            for (var i = 0; i < 12; i++)
            {
                var x = PopRounds.SideMargin + 40f + (i * ((PopRounds.CanvasWidth - 80f - (PopRounds.SideMargin * 2f)) / 11f));
                var y = _board.LineY - (t * 1400f) - ((i % 3) * 120f);
                var a = toScreen(new Vector2(x, y));
                var b = toScreen(new Vector2(x, y + 160f));
                dl.AddLine(a, b, Look.U32(Look.CrystalPale, 0.45f * (1f - t)), MathF.Max(1f, 3f * s));
            }
        }
    }

    private void DrawBubbles(OsAppContext ctx, ImDrawListPtr dl, GameStage stage, float s, Func<Vector2, Vector2> toScreen)
    {
        var now = ImGui.GetTime();
        var size = PopRounds.BubbleDiameter * s;
        var glideOffset = (_ceilingGlide - _board.CeilingY);
        foreach (var (r, c, b) in _board.All())
        {
            var at = _board.Centre(r, c) + new Vector2(0f, glideOffset);
            if (_jiggle.TryGetValue((r, c), out var jig) && !stage.ReduceMotion)
            {
                var decay = 1f - (jig.Age / 0.6f);
                at += new Vector2(MathF.Sin(jig.Age * 38f) * jig.Amp * decay, MathF.Cos(jig.Age * 31f) * jig.Amp * 0.5f * decay);
            }
            var screen = toScreen(at);
            GyrePieces.EllipseFilled(dl, screen + new Vector2(0f, size * 0.16f),
                new Vector2(size * 0.4f, size * 0.15f), Look.U32(new Vector4(0f, 0f, 0f, 0.22f)));
            PopPieces.Bubble(ctx, dl, stage.AssetRoot, screen, size, b.Kind, b.Special, b.Letter, b.Frozen, now);
        }
    }

    /// <summary>The compactor: a heavy bar across the top with the shot pips on it, glowing red as the
    /// next push nears and flashing when it lands.</summary>
    private void DrawCompactor(ImDrawListPtr dl, GameStage stage, float s, Func<Vector2, Vector2> toScreen)
    {
        var left = toScreen(new Vector2(PopRounds.SideMargin, _ceilingGlide));
        var right = toScreen(new Vector2(PopRounds.CanvasWidth - PopRounds.SideMargin, _ceilingGlide));
        var barH = 46f * s;
        var top = new Vector2(left.X, left.Y - barH);
        var bottom = new Vector2(right.X, right.Y);
        var fill = new Vector4(0.16f, 0.15f, 0.2f, 0.97f);
        dl.AddRectFilled(top, bottom, Look.U32(fill), 6f * s);
        dl.AddRectFilledMultiColor(top, new Vector2(bottom.X, top.Y + (barH * 0.45f)),
            Look.U32(new Vector4(1f, 1f, 1f, 0.16f)), Look.U32(new Vector4(1f, 1f, 1f, 0.16f)),
            Look.U32(new Vector4(1f, 1f, 1f, 0f)), Look.U32(new Vector4(1f, 1f, 1f, 0f)));
        var teeth = 12;
        for (var i = 0; i < teeth; i++)
        {
            var x0 = left.X + ((right.X - left.X) * i / teeth);
            var x1 = left.X + ((right.X - left.X) * (i + 1) / teeth);
            var mid = (x0 + x1) * 0.5f;
            dl.AddTriangleFilled(new Vector2(x0 + (2f * s), bottom.Y), new Vector2(x1 - (2f * s), bottom.Y),
                new Vector2(mid, bottom.Y + (10f * s)), Look.U32(new Vector4(0.1f, 0.09f, 0.13f, 0.95f)));
        }
        dl.AddRect(top, bottom, Look.U32(new Vector4(1f, 1f, 1f, 0.28f)), 6f * s, ImDrawFlags.RoundCornersAll, 1.2f);

        // The pips: one per shot until the push (per second until the next row in the Final), the last one red and breathing.
        var pips = _board.Pips;
        var taken = _board.PipsLit;
        var pipR = MathF.Min(7f * s, (right.X - left.X) / (pips * 3.2f));
        var gap = pipR * 3f;
        var startX = (left.X + right.X) * 0.5f - (gap * (pips - 1) * 0.5f);
        var pipY = top.Y + (barH * 0.5f);
        var warn = taken >= pips - 1;
        var pulse = warn && !stage.ReduceMotion ? 0.6f + (0.4f * MathF.Sin((float)ImGui.GetTime() * 9f)) : 1f;
        for (var i = 0; i < pips; i++)
        {
            var c = new Vector2(startX + (i * gap), pipY);
            var lit = i < taken;
            var last = i == pips - 1;
            var colour = last ? Danger : Gold;
            dl.AddCircleFilled(c, pipR, Look.U32(lit ? colour with { W = pulse } : new Vector4(1f, 1f, 1f, 0.12f)), 14);
            if (lit && !stage.ReduceMotion)
            {
                Look.Halo(dl, c, pipR * 2.2f, colour, 0.25f * pulse, 3);
            }
        }
        if (warn)
        {
            dl.AddRectFilled(top, bottom, Look.U32(Danger, 0.12f * pulse), 6f * s);
        }
        if (_pushFlash > 0f)
        {
            dl.AddRectFilled(top, bottom, Look.U32(new Vector4(1f, 1f, 1f, 0.5f * (_pushFlash / 0.5f))), 6f * s);
        }
    }

    /// <summary>The line the run ends on: dashed, dim while the board is far, red and beating when it is
    /// one row away.</summary>
    private void DrawLine(ImDrawListPtr dl, GameStage stage, float s, Func<Vector2, Vector2> toScreen)
    {
        var danger = _board.Danger();
        var left = toScreen(new Vector2(PopRounds.SideMargin, _board.LineY));
        var right = toScreen(new Vector2(PopRounds.CanvasWidth - PopRounds.SideMargin, _board.LineY));
        var beat = danger > 0.5f && !stage.ReduceMotion ? 0.5f + (0.5f * MathF.Sin((float)ImGui.GetTime() * 8f)) : 0.3f;
        var colour = Vector4.Lerp(Look.CrystalPale with { W = 0.45f }, Danger with { W = 0.55f + (0.45f * beat) }, danger);
        var dash = 18f * s;
        for (var x = left.X; x < right.X; x += dash * 2f)
        {
            dl.AddLine(new Vector2(x, left.Y), new Vector2(MathF.Min(x + dash, right.X), left.Y), Look.U32(colour), MathF.Max(1.5f, 3f * s));
        }
        if (danger > 0.3f)
        {
            dl.AddRectFilledMultiColor(new Vector2(left.X, left.Y - (80f * s)), new Vector2(right.X, left.Y),
                Look.U32(Danger, 0f), Look.U32(Danger, 0f), Look.U32(Danger, 0.22f * danger * beat), Look.U32(Danger, 0.22f * danger * beat));
        }
    }

    private void DrawShooter(OsAppContext ctx, ImDrawListPtr dl, GameStage stage, float s, Func<Vector2, Vector2> toScreen)
    {
        var shooter = ShooterCanvas();
        var centre = toScreen(shooter);
        var petPx = ShooterPetUnits * s;
        var aim = new Vector2(MathF.Cos(_aimAngle), MathF.Sin(_aimAngle));
        var now = ImGui.GetTime();

        GyrePieces.Ellipse(dl, centre + new Vector2(0f, petPx * 0.42f), new Vector2(petPx * 0.78f, petPx * 0.3f),
            Look.U32(Look.CrystalPale, 0.4f), 2f);

        // The aim: a short dotted line, and the whole banked path only while a power is armed.
        if (!_board.Over && _clearLeft <= 0f)
        {
            var armed = _armed != AetherlingElement.None;
            var colour = armed ? KindColours[Array.IndexOf(ElementOrder, _armed)] : KindColours[_heldKind];
            var pos = shooter + (aim * (CradleUnits + 20f));
            var dir = aim;
            var minX = PopRounds.SideMargin + PopRounds.BubbleRadius;
            var maxX = PopRounds.CanvasWidth - PopRounds.SideMargin - PopRounds.BubbleRadius;
            var bounces = 0;
            var dots = armed ? 400 : AimDots;
            for (var i = 0; i < dots; i++)
            {
                pos += dir * 22f;
                if (pos.X < minX || pos.X > maxX)
                {
                    pos.X = Math.Clamp(pos.X, minX, maxX);
                    dir.X = -dir.X;
                    bounces++;
                    if (!armed || bounces > AimBounces)
                    {
                        break;
                    }
                }
                if (_board.AtCeiling(pos) || _board.Touches(pos, out _, out _))
                {
                    if (armed)
                    {
                        dl.AddCircle(toScreen(pos), PopRounds.BubbleRadius * s * 0.9f, Look.U32(colour, 0.6f), 24, 2f);
                    }
                    break;
                }
                if (i % 2 == 0)
                {
                    var fade = armed ? 0.7f : 0.7f * (1f - (i / (float)AimDots));
                    dl.AddCircleFilled(toScreen(pos), MathF.Max(1.5f, 3.2f * s), Look.U32(colour, fade), 8);
                }
            }
        }

        var behind = aim.Y < 0f;
        var cradle = toScreen(shooter + (aim * CradleUnits));
        void DrawCradle()
        {
            dl.AddCircleFilled(cradle, 28f * s, Look.U32(new Vector4(0.9f, 0.86f, 0.72f, 0.35f)), 22);
            dl.AddCircle(cradle, 28f * s, Look.U32(new Vector4(0.72f, 0.6f, 0.36f, 0.8f)), 22, 2.2f);
            if (_shots.Count == 0)
            {
                if (_armed != AetherlingElement.None)
                {
                    var i = Array.IndexOf(ElementOrder, _armed);
                    Look.Halo(dl, cradle, 60f * s, KindColours[i], 0.35f + (0.15f * MathF.Sin((float)now * 6f)), 4);
                }
                if (_armed == AetherlingElement.Earth)
                {
                    PopPieces.Metal(dl, cradle, PopRounds.BubbleDiameter * s * 0.92f, 0f);
                }
                else
                {
                    GyrePieces.Marble(ctx, dl, stage.AssetRoot, cradle, PopRounds.BubbleDiameter * s * 0.92f, _heldKind, false);
                    if (_armed == AetherlingElement.Ice)
                    {
                        PopPieces.Ice(dl, cradle, PopRounds.BubbleDiameter * s * 0.92f, now, 1f);
                    }
                }
            }
        }
        if (behind)
        {
            DrawCradle();
        }

        var pose = stage.Runtime.Pose;
        pose.Offset += new Vector2(aim.X * 7f, 0f);
        pose.FlipX = aim.X < 0f;
        if (_lumiSquash > 0f)
        {
            pose.Scale = stage.ReduceMotion ? Vector2.One : new Vector2(1f + (0.35f * _lumiSquash), 1f - (0.55f * _lumiSquash));
        }
        else if (_lumiHop < -2f)
        {
            pose.CellIndex = GameScene.Cell(stage.Manifest, "hop", 0.4f);
            pose.Scale = stage.ReduceMotion ? Vector2.One : new Vector2(0.94f, 1.08f);
        }
        var feet = centre + new Vector2(0f, petPx * 0.46f + _lumiHop * s);
        Look.GroundGlow(dl, new Vector2(centre.X, centre.Y + (petPx * 0.48f)), petPx * 0.6f, petPx * 0.12f,
            Look.Crystal, 0.3f);
        stage.Runtime.Draw(dl, ctx.Capabilities.Textures, feet, petPx, pose, props: false);

        if (!behind)
        {
            DrawCradle();
        }

        var next = toScreen(shooter + new Vector2(CradleUnits * 1.4f, CradleUnits * 0.2f));
        GyrePieces.Marble(ctx, dl, stage.AssetRoot, next, PopRounds.BubbleDiameter * s * 0.6f, _nextKind, false, 0.85f);
    }

    private void DrawShotsAndFx(OsAppContext ctx, ImDrawListPtr dl, GameStage stage, float s, Func<Vector2, Vector2> toScreen)
    {
        var now = ImGui.GetTime();
        var size = PopRounds.BubbleDiameter * s;

        foreach (var f in _fallers)
        {
            var below = Math.Clamp((f.Pos.Y - _board.LineY) / 260f, 0f, 1f);
            PopPieces.Bubble(ctx, dl, stage.AssetRoot, toScreen(f.Pos), size, f.Kind, f.Special, f.Letter, false, now,
                1f - below, f.Spin * s);
        }

        foreach (var shot in _shots)
        {
            var colour = ShotColour(shot);
            foreach (var (p, age) in shot.Trail)
            {
                var a = 1f - (age / TrailSeconds);
                dl.AddCircleFilled(toScreen(p), size * 0.42f * a, Look.U32(colour, 0.18f * a), 16);
            }
            var at = toScreen(shot.Pos);
            Look.Halo(dl, at, size * 0.7f, colour, shot.Element == AetherlingElement.None ? 0.3f : 0.55f, 3);
            if (shot.Element == AetherlingElement.Earth)
            {
                PopPieces.Metal(dl, at, size, shot.Travelled * s);
            }
            else
            {
                var drawSize = size * 0.96f;
                GyrePieces.Marble(ctx, dl, stage.AssetRoot, at, drawSize, shot.Kind, false, 1f, shot.Travelled * s);
                if (shot.Element == AetherlingElement.Ice)
                {
                    PopPieces.Ice(dl, at, drawSize, now, 1f);
                }
            }
            if (shot.Squash > 0f && !stage.ReduceMotion)
            {
                dl.AddCircle(at, size * (0.5f + (0.4f * (1f - shot.Squash))), Look.U32(colour, 0.5f * shot.Squash), 20, 2f);
            }
        }

        foreach (var fx in _powerFx)
        {
            DrawPowerFx(dl, stage, fx, s, toScreen);
        }

        foreach (var sh in _shards)
        {
            var alpha = 1f - (sh.Age / sh.Life);
            dl.AddCircleFilled(toScreen(sh.Pos), MathF.Max(1.5f, 6f * s * alpha), Look.U32(sh.Colour, alpha), 8);
        }
        foreach (var ring in _rings)
        {
            var t = ring.Age / ring.Life;
            dl.AddCircle(toScreen(ring.At), (20f + (ring.Reach * t)) * s, Look.U32(ring.Colour, 0.75f * (1f - t)), 32,
                MathF.Max(1.5f, 4f * (1f - t)));
        }
        foreach (var text in _texts)
        {
            var alpha = 1f - Math.Clamp((text.Age - 0.55f) / 0.65f, 0f, 1f);
            var at = toScreen(text.At);
            Look.Centred(dl, text.Text, at.X, at.Y, Look.U32(text.Colour, alpha), text.Scale);
        }

        if (_redFlash > 0f)
        {
            dl.AddRectFilled(stage.Origin, stage.Origin + stage.Size - new Vector2(0f, StripHeight),
                Look.U32(Danger, 0.35f * (_redFlash / 0.8f)));
        }
        if (_aetherFlash > 0f)
        {
            var t = _aetherFlash / 1.6f;
            var centre = toScreen(new Vector2(PopRounds.CanvasWidth * 0.5f, _canvasH * 0.36f));
            Look.Halo(dl, centre, 260f * s * (1.4f - t), Gold, 0.35f * t, 5);
            Look.GlowText(dl, PopBoard.Letters, centre.X, centre.Y - (20f * s), Look.U32(Gold, t), 2.2f, Gold, 0.8f * t);
        }
    }

    /// <summary>What a power looks like as it happens: flames dropping and running the bottom row, a flood
    /// pouring down a column, a bolt ripping across a row.</summary>
    private void DrawPowerFx(ImDrawListPtr dl, GameStage stage, PowerFx fx, float s, Func<Vector2, Vector2> toScreen)
    {
        var t = fx.Age / 0.9f;
        var left = PopRounds.SideMargin;
        var right = PopRounds.CanvasWidth - PopRounds.SideMargin;
        switch (fx.Element)
        {
            case AetherlingElement.Fire:
            {
                var fire = KindColours[0];
                var reach = MathF.Min(1f, t * 1.6f);
                var half = (right - left) * 0.5f * reach;
                var y = fx.RowY;
                for (var i = 0; i < 24; i++)
                {
                    var x = fx.At.X - half + (2f * half * i / 23f);
                    var flick = MathF.Sin((float)ImGui.GetTime() * 18f + i) * 18f;
                    var h = (60f + flick) * (1f - t);
                    var baseP = toScreen(new Vector2(x, y + 30f));
                    var tip = toScreen(new Vector2(x + (flick * 0.3f), y - h));
                    dl.AddTriangleFilled(baseP - new Vector2(16f * s, 0f), baseP + new Vector2(16f * s, 0f), tip, Look.U32(fire, 0.75f * (1f - t)));
                    dl.AddTriangleFilled(baseP - new Vector2(8f * s, 0f), baseP + new Vector2(8f * s, 0f),
                        toScreen(new Vector2(x, y - (h * 0.55f))), Look.U32(Gold, 0.8f * (1f - t)));
                }
                var drop = Math.Clamp(t * 2.5f, 0f, 1f);
                var head = toScreen(Vector2.Lerp(fx.At, new Vector2(fx.At.X, y), drop));
                Look.Halo(dl, head, 70f * s, fire, 0.5f * (1f - t), 4);
                break;
            }
            case AetherlingElement.Water:
            {
                var water = KindColours[1];
                var front = MathF.Min(1f, t * 1.8f);
                var bottomY = fx.At.Y + ((_board.LineY - fx.At.Y) * front);
                var a = toScreen(new Vector2(fx.At.X - (PopRounds.BubbleRadius * 0.9f), fx.At.Y));
                var b = toScreen(new Vector2(fx.At.X + (PopRounds.BubbleRadius * 0.9f), bottomY));
                dl.AddRectFilledMultiColor(a, b, Look.U32(water, 0.15f * (1f - t)), Look.U32(water, 0.15f * (1f - t)),
                    Look.U32(water, 0.55f * (1f - t)), Look.U32(water, 0.55f * (1f - t)));
                for (var i = 0; i < 10; i++)
                {
                    var dy = bottomY - (i * 55f) - (((float)ImGui.GetTime() * 400f) % 55f);
                    if (dy < fx.At.Y)
                    {
                        continue;
                    }
                    var dx = MathF.Sin(i * 1.7f + (float)ImGui.GetTime() * 9f) * 30f;
                    dl.AddCircleFilled(toScreen(new Vector2(fx.At.X + dx, dy)), 9f * s, Look.U32(Look.CrystalPale, 0.7f * (1f - t)), 10);
                }
                Look.Halo(dl, toScreen(new Vector2(fx.At.X, bottomY)), 90f * s, water, 0.4f * (1f - t), 4);
                break;
            }
            case AetherlingElement.Ice:
            {
                var frost = new Vector4(0.75f, 0.92f, 1f, 1f);
                var reach = PopRounds.BubbleDiameter * 2.1f * MathF.Min(1f, t * 2f);
                for (var i = 0; i < 8; i++)
                {
                    var a = (MathF.Tau * i / 8f) + (t * 0.8f);
                    var dir = new Vector2(MathF.Cos(a), MathF.Sin(a));
                    dl.AddLine(toScreen(fx.At + (dir * reach * 0.2f)), toScreen(fx.At + (dir * reach)),
                        Look.U32(frost, 0.8f * (1f - t)), MathF.Max(1.5f, 5f * s * (1f - t)));
                }
                Look.Halo(dl, toScreen(fx.At), reach * s, frost, 0.35f * (1f - t), 4);
                break;
            }
            case AetherlingElement.Earth:
            {
                var steel = new Vector4(0.78f, 0.8f, 0.86f, 1f);
                var top = MathF.Min(1f, t * 1.8f);
                var head = fx.At - new Vector2(0f, fx.At.Y * top);
                var a = toScreen(new Vector2(fx.At.X - (PopRounds.BubbleRadius * 0.7f), head.Y));
                var b = toScreen(new Vector2(fx.At.X + (PopRounds.BubbleRadius * 0.7f), fx.At.Y));
                dl.AddRectFilledMultiColor(a, b, Look.U32(steel, 0.5f * (1f - t)), Look.U32(steel, 0.5f * (1f - t)),
                    Look.U32(steel, 0.05f * (1f - t)), Look.U32(steel, 0.05f * (1f - t)));
                PopPieces.Metal(dl, toScreen(head), PopRounds.BubbleDiameter * s * 0.9f, t * 600f, 1f - t);
                break;
            }
            case AetherlingElement.Lightning:
            {
                var bolt = KindColours[4];
                var alpha = t < 0.5f ? 1f : 1f - ((t - 0.5f) * 2f);
                var flicker = 0.7f + (0.3f * MathF.Sin((float)ImGui.GetTime() * 60f));
                for (var i = 0; i + 1 < fx.Bolt.Count; i++)
                {
                    var p = toScreen(fx.Bolt[i]);
                    var q = toScreen(fx.Bolt[i + 1]);
                    dl.AddLine(p, q, Look.U32(bolt, 0.4f * alpha), 14f * s);
                    dl.AddLine(p, q, Look.U32(new Vector4(1f, 1f, 1f, 1f), 0.9f * alpha * flicker), 4f * s);
                }
                dl.AddRectFilled(toScreen(new Vector2(left, fx.RowY - 70f)), toScreen(new Vector2(right, fx.RowY + 70f)),
                    Look.U32(bolt, 0.12f * alpha));
                break;
            }
        }
    }

    /// <summary>The band above the compactor: the score, the round, and the AETHER row big enough to
    /// read at a glance, gold as the letters come in. The corner chips own the top corners, so the
    /// band is centred between them.</summary>
    private void DrawHud(ImDrawListPtr dl, GameStage stage, Vector2 fieldTl, Vector2 fieldSize)
    {
        var main = _board.Score.ToString("N0");
        var aside = _board.Final
            ? Loc.T("os.aetherling_pop_final")
            : string.Format(Loc.T("os.aetherling_pop_round"), _board.Round);
        var pillY = fieldTl.Y + Px(10f);
        var centreX = fieldTl.X + (fieldSize.X * 0.5f);

        // A dark plate under the whole band: the backdrops are bright skies and the crystal text drowned.
        var lineH = ImGui.GetTextLineHeight();
        var plateH = HudBandHeight();
        var plateW = Px(230f);
        var plateTl = new Vector2(centreX - (plateW * 0.5f), fieldTl.Y + Px(4f));
        dl.AddRectFilled(plateTl, plateTl + new Vector2(plateW, plateH), Look.U32(new Vector4(0.03f, 0.03f, 0.06f, 0.62f)), Px(14f));
        dl.AddRect(plateTl, plateTl + new Vector2(plateW, plateH), Look.U32(new Vector4(1f, 1f, 1f, 0.12f)), Px(14f), ImDrawFlags.RoundCornersAll, 1f);

        var h = Look.Pill(dl, main, centreX, pillY, Look.CrystalPale, 1f, 1.05f);
        var y = pillY + h + Px(3f);
        Look.Centred(dl, aside, centreX, y, Look.U32(Look.Body, 0.95f), 0.9f);
        y += (lineH * 0.9f) + Px(6f);

        var letterW = Px(24f);
        var scaleBase = 1.25f;
        var startX = centreX - (letterW * PopBoard.Letters.Length * 0.5f);
        for (var i = 0; i < PopBoard.Letters.Length; i++)
        {
            var x = startX + (i * letterW);
            var held = _board.LettersHeld[i];
            var flash = _letterFlash[i];
            var colour = held ? Gold : new Vector4(1f, 1f, 1f, 0.28f);
            var scale = scaleBase + (0.5f * flash);
            var c = new Vector2(x + (letterW * 0.5f), y + (ImGui.GetTextLineHeight() * scaleBase * 0.5f));
            if (held && !stage.ReduceMotion)
            {
                Look.Halo(dl, c, Px(13f), Gold, 0.18f, 3);
            }
            if (flash > 0f)
            {
                Look.Halo(dl, c, Px(16f) * (1f + flash), Gold, 0.4f * flash, 3);
            }
            var text = PopBoard.Letters[i].ToString();
            var textW = ImGui.CalcTextSize(text).X * scale;
            var textH = ImGui.GetTextLineHeight() * scale;
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * scale, c - new Vector2(textW * 0.5f, textH * 0.5f),
                Look.U32(colour), text);
        }

        // Armed power as a pill along the bottom edge of the field.
        var pillX = fieldTl.X + Px(12f);
        var bottom = fieldTl.Y + fieldSize.Y - Px(26f);
        void Pill(string text, Vector4 colour)
        {
            var sz = ImGui.CalcTextSize(text) * 0.8f;
            dl.AddRectFilled(new Vector2(pillX, bottom), new Vector2(pillX + sz.X + Px(14f), bottom + Px(20f)),
                Look.U32(new Vector4(0f, 0f, 0f, 0.45f)), Px(10f));
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.8f,
                new Vector2(pillX + Px(7f), bottom + Px(3f)), Look.U32(colour), text);
            pillX += sz.X + Px(20f);
        }
        if (_armed != AetherlingElement.None)
        {
            var i = Array.IndexOf(ElementOrder, _armed);
            Pill(string.Format(Loc.T("os.aetherling_pop_armed"), Loc.T($"os.aetherling_element_{Elements[i]}")), KindColours[i]);
        }
    }

    /// <summary>The HUD plate's height in screen pixels: the score pill, the round line and the AETHER
    /// row with their gaps. The compactor is placed under it, so it is measured rather than guessed.</summary>
    private static float HudBandHeight()
    {
        var lineH = ImGui.GetTextLineHeight();
        return Px(8f) + (lineH * 1.05f * 1.56f) + Px(3f) + (lineH * 0.9f) + Px(6f) + (lineH * 1.25f) + Px(10f);
    }

    private void DrawClearCard(ImDrawListPtr dl, GameStage stage, Vector2 fieldTl, Vector2 fieldSize)
    {
        if (_clearLeft <= 0f)
        {
            return;
        }
        var fade = stage.ReduceMotion ? 1f : MathF.Min(1f, _clearLeft / 0.35f);
        var centre = fieldTl + (fieldSize * 0.5f);
        dl.AddRectFilled(fieldTl, fieldTl + fieldSize, Look.U32(new Vector4(0f, 0f, 0f, 0.55f * fade)));

        var cardW = MathF.Min(fieldSize.X - Px(48f), Px(260f));
        var cardH = Px(96f);
        var tl = centre - new Vector2(cardW * 0.5f, cardH * 0.5f);
        dl.AddRectFilled(tl, tl + new Vector2(cardW, cardH), Look.U32(new Vector4(0.07f, 0.06f, 0.11f, 0.96f * fade)), Px(16f));
        dl.AddRect(tl, tl + new Vector2(cardW, cardH), Look.U32(Look.Crystal, 0.5f * fade), Px(16f), ImDrawFlags.RoundCornersAll, Px(1.2f));

        Look.Centred(dl, string.Format(Loc.T("os.aetherling_pop_round_done"), _clearRound), centre.X, tl.Y + Px(20f),
            Look.U32(Look.CrystalPale, fade), 1.15f);
        Look.Centred(dl, string.Format(Loc.T("os.aetherling_pop_round_bonus"), _clearBonus), centre.X, tl.Y + Px(46f),
            Look.U32(Gold, fade), 0.95f);
        if (_board.Final)
        {
            Look.Centred(dl, string.Format(Loc.T("os.aetherling_pop_round_next"), Loc.T("os.aetherling_pop_final")),
                centre.X, tl.Y + Px(70f), Look.U32(Look.Whisper, 0.9f * fade), 0.85f);
        }
    }

    private void DrawGameOver(ImDrawListPtr dl, GameStage stage, Vector2 fieldTl, Vector2 fieldSize)
    {
        if (!_overShown || !_board.Over)
        {
            return;
        }
        var centre = fieldTl + new Vector2(fieldSize.X * 0.5f, fieldSize.Y * 0.42f);
        var t = 1f - Math.Clamp(_overLeft / GameOverHold, 0f, 1f);
        Look.GlowText(dl, Loc.T("os.aetherling_pop_squashed"), centre.X, centre.Y, Look.U32(Danger, MathF.Min(1f, t * 3f)),
            1.5f + (0.3f * t), Danger, 0.7f);
    }

    private bool ElementUnlocked(AetherlingElement element) => LumiLinkGame.ElementUnlocked(_core, element);

    private int FeedsLeft(AetherlingElement element) => LumiLinkGame.FeedsLeft(_core, element);

    private void Arm(GameStage stage, AetherlingElement element)
    {
        _meterPoints = 0;
        _armed = element;
        stage.Sound(GameSound.Chord0);
        _lumiHop = -16f;
    }

    /// <summary>The creature, its elements and the power bar, the same strip Lumi-Link and Gyre wear. A
    /// tapped disc arms the NEXT shot rather than firing at once, so every power is aimed.</summary>
    private void DrawStrip(OsAppContext ctx, ImDrawListPtr dl, GameStage stage, Vector2 origin, Vector2 size)
    {
        var stripBr = origin + new Vector2(size.X, StripHeight);
        dl.AddRectFilledMultiColor(origin, stripBr,
            Look.U32(new Vector4(0.04f, 0.03f, 0.08f, 0f)), Look.U32(new Vector4(0.04f, 0.03f, 0.08f, 0f)),
            Look.U32(new Vector4(0.08f, 0.06f, 0.14f, 0.9f)), Look.U32(new Vector4(0.08f, 0.06f, 0.14f, 0.9f)));

        var discR = 19f;
        var gap = 10f;
        var rowW = (6 * discR * 2f) + (5 * gap);
        var left = origin.X + ((size.X - rowW) * 0.5f);
        var discY = origin.Y + discR + 22f;
        var now = ImGui.GetTime();
        var meterFull = MeterFull;
        for (var i = 0; i < 6; i++)
        {
            var element = ElementOrder[i];
            var unlocked = ElementUnlocked(element);
            var c = new Vector2(left + discR + (i * ((discR * 2f) + gap)), discY);
            var armed = _armed == element;
            var tappable = unlocked && meterFull && _armed == AetherlingElement.None && !_board.Over;
            var pulse = (tappable || armed) && !stage.ReduceMotion ? 0.5f + (0.5f * MathF.Sin((float)(now * 6.0) + i)) : 0f;
            var hovered = ImGui.IsMouseHoveringRect(c - new Vector2(discR), c + new Vector2(discR));
            if (tappable || armed)
            {
                Look.Halo(dl, c, discR * (1.6f + (0.3f * pulse)), KindColours[i], 0.25f + (0.2f * pulse));
            }
            dl.AddCircleFilled(c, discR, Look.U32(new Vector4(1f, 1f, 1f, unlocked ? 0.14f : 0.05f)), 28);
            dl.AddCircle(c, discR, Look.U32(KindColours[i] with { W = unlocked ? 0.6f : 0.15f }), 28, armed ? 2.4f : 1.2f);
            PopPieces.ElementIcon(ctx, dl, stage.AssetRoot, c, discR * 1.56f, i, unlocked ? 1f : 0.35f);
            if (!unlocked)
            {
                IconDraw.AddCentered(dl, FontAwesomeIcon.Lock, discR * 0.7f, c + new Vector2(discR * 0.55f, discR * 0.55f),
                    Look.U32(new Vector4(1f, 1f, 1f, 0.75f)));
            }
            if (hovered)
            {
                ImGui.SetTooltip(unlocked
                    ? Loc.T($"os.aetherling_pop_power_{Elements[i]}")
                    : string.Format(Loc.T("os.aetherling_lumilink_locked"), FeedsLeft(element),
                        Loc.T($"os.aetherling_element_{Elements[i]}")));
                if (tappable)
                {
                    ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                    if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                    {
                        Arm(stage, element);
                    }
                }
            }
        }

        var barH = 16f;
        var barW = rowW;
        var barX = left;
        var barY = discY + discR + 12f;
        var radius = barH * 0.5f;
        dl.AddRectFilled(new Vector2(barX, barY), new Vector2(barX + barW, barY + barH),
            Look.U32(new Vector4(1f, 1f, 1f, 0.08f)), radius);
        var fillW = MathF.Max(barH, barW * _meterGlide);
        var fillHot = new Vector4(0.98f, 0.9f, 0.55f, 1f);
        dl.AddRectFilledMultiColor(new Vector2(barX, barY), new Vector2(barX + fillW, barY + barH),
            Look.U32(Look.Crystal), Look.U32(fillHot), Look.U32(fillHot), Look.U32(Look.Crystal));
        dl.AddRect(new Vector2(barX, barY), new Vector2(barX + barW, barY + barH),
            Look.U32(new Vector4(1f, 1f, 1f, 0.22f)), radius, ImDrawFlags.RoundCornersAll, 1.1f);
        if (_meterGlide > 0.02f && !stage.ReduceMotion)
        {
            var head = new Vector2(barX + fillW - radius, barY + radius);
            Look.Halo(dl, head, radius * (meterFull ? 2.6f : 1.8f), fillHot, meterFull ? 0.45f : 0.25f);
        }
        var label = _armed != AetherlingElement.None
            ? Loc.T("os.aetherling_pop_power_armed")
            : meterFull
                ? Loc.T("os.aetherling_lumilink_power_ready")
                : Loc.T("os.aetherling_lumilink_power_charging");
        var labelSz = ImGui.CalcTextSize(label) * 0.8f;
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.8f,
            new Vector2(barX + barW - labelSz.X, barY + barH + 4f),
            Look.U32(meterFull || _armed != AetherlingElement.None ? Gold : Look.Whisper), label);
    }
}
