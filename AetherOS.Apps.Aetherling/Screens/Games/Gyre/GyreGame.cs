using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using AetherLove.Services.Localization;
using AetherLove.Shared.Aetherling;
using AetherLove.Shared.Arcade;
using AetherLove.UI;
using AetherOS.Apps.Aetherling.Screens.Games.LumiLink;
using AetherOS.Apps.Aetherling.Ui;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Aetherling.Screens.Games.Gyre;

/// <summary>Gyre, the marble-chain shooter. The board (<see cref="GyreBoard"/>) decides what happens;
/// this class decides how it looks, sounds and feels: the groove painted along the spline, marbles that
/// shatter into shards of their own colour, a slam that rings, a rising ladder of chimes per cascade,
/// and the creature at the pivot leaning into every shot with its element powers charging in the strip.</summary>
internal sealed class GyreGame : IPetGame
{
    /// <summary>How long the stage-clear card holds the board still.</summary>
    private const float ClearCardSeconds = 1.8f;

    private const float StripHeight = 164f;
    private const float PowerSeconds = 0.55f;
    private const float ShooterPetUnits = 132f;
    private const float CradleUnits = 92f;

    private sealed class Shot
    {
        public Vector2 Pos;
        public Vector2 Dir;
        public int Kind;
        public Vector2? NeedleTarget;

        /// <summary>How far it has flown, so a shot in the air rolls like everything else does.</summary>
        public float Travelled;
    }

    private sealed class Shard
    {
        public Vector2 Pos;
        public Vector2 Vel;
        public Vector4 Colour;
        public float Age;
    }

    private sealed class FloatText
    {
        public Vector2 At;
        public required string Text;
        public Vector4 Colour;
        public float Age;
    }

    private sealed class Ring
    {
        public Vector2 At;
        public float Age;
        public Vector4 Colour;
    }

    /// <summary>The seam where two ends of the chain just met: a hard white flash that collapses inward,
    /// so the moment of contact reads as contact rather than as the chain quietly getting shorter.</summary>
    private sealed class Snap
    {
        public Vector2 At;
        public bool Matched;
        public float Age;
    }

    private static readonly string[] Elements = GyrePieces.Elements;
    private static readonly Vector4[] KindColours = GyrePieces.KindColours;
    private static readonly AetherlingElement[] ElementOrder = LumiLinkGame.ElementOrder;
    private static readonly Vector4 Gold = new(0.98f, 0.82f, 0.36f, 1f);

    private static readonly (Vector4 Top, Vector4 Bottom)[] ChapterTints =
    [
        (new Vector4(0.55f, 0.72f, 0.92f, 1f), new Vector4(0.72f, 0.84f, 0.95f, 1f)),
        (new Vector4(0.45f, 0.62f, 0.38f, 1f), new Vector4(0.62f, 0.72f, 0.42f, 1f)),
        (new Vector4(0.30f, 0.38f, 0.48f, 1f), new Vector4(0.20f, 0.28f, 0.38f, 1f)),
        (new Vector4(0.16f, 0.10f, 0.10f, 1f), new Vector4(0.28f, 0.13f, 0.08f, 1f)),
    ];

    private readonly GyreBoard _board = new();
    private readonly List<Shot> _shots = [];
    private readonly List<Shard> _shards = [];
    private readonly List<FloatText> _texts = [];
    private readonly List<Ring> _rings = [];
    private readonly List<Snap> _snaps = [];

    private Random _rng = new();
    private bool _loaded;
    private int _heldKind;
    private int _nextKind;
    private int _meterPoints;

    /// <summary>The stage-clear card: seconds left, and what it names. The board loads the next stage the
    /// moment one is cleared, so the card is also a HOLD on the tick; without it a cleared stage poofs
    /// straight into the next one and the player never sees what they finished.</summary>
    private float _clearLeft;
    private int _clearStage;
    private int _clearBonus;
    private int _lastScore;
    private float _meterGlide;
    private float _shake;
    private Vector2 _shakeOffset;
    private float _lumiHop;
    private float _powerLeft;
    private AetherlingElement? _powerElement;
    private float _lifeFlash;
    private float _aimAngle;
    private AetherlingDto? _core;

    public ArcadeGame Id => ArcadeGame.Gyre;

    public bool Over => _loaded && _board.Over;

    public int Score => _board.Score;

    public int Metric1 => _board.Stage;

    public int Metric2 => _board.DeepestCascade;

    /// <summary>Which quarter of the ladder the run is in, 0..3; picks the chapter's music.</summary>
    public int Chapter => Math.Min((Math.Max(1, _board.Stage) - 1) / 5, 3);

    /// <summary>How many times The Core has stepped up its pace, so the loop can climb with it.</summary>
    public int EndlessSteps => _board.EndlessSteps;

    public void SetCreature(AetherlingDto? core) => _core = core;

    public void Reset(Random rng)
    {
        _rng = rng;
        _loaded = false;
        _shots.Clear();
        _shards.Clear();
        _texts.Clear();
        _rings.Clear();
        _snaps.Clear();
        _meterPoints = 0;
        _lastScore = 0;
        _meterGlide = 0f;
        _shake = 0f;
        _lumiHop = 0f;
        _powerLeft = 0f;
        _powerElement = null;
        _lifeFlash = 0f;
        _aimAngle = -MathF.PI / 2f;
    }

    private bool MeterFull => _meterPoints >= GameScoring.GyrePowerMeterPoints;

    public void UpdateAndDraw(OsAppContext ctx, ImDrawListPtr dl, GameStage stage, float dt)
    {
        if (!_loaded)
        {
            _board.Reset(_rng, GyreStages.Load(stage.AssetRoot));
            _heldKind = _board.RollShotKind();
            _nextKind = _board.RollShotKind();
            _loaded = true;
        }

        var origin = stage.Origin;
        var size = stage.Size;
        var fieldTl = new Vector2(origin.X, origin.Y + StripHeight);
        var fieldSize = new Vector2(size.X, size.Y - StripHeight);
        var s = MathF.Min(fieldSize.X / GyreStages.CanvasWidth, fieldSize.Y / GyreStages.CanvasHeight);
        var canvasPx = new Vector2(GyreStages.CanvasWidth, GyreStages.CanvasHeight) * s;
        var canvasTl = fieldTl + ((fieldSize - canvasPx) * 0.5f);

        Vector2 ToScreen(Vector2 canvas) => canvasTl + (canvas * s) + _shakeOffset;

        if (_clearLeft > 0f)
        {
            _clearLeft = MathF.Max(0f, _clearLeft - dt);
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
        DrawChains(ctx, dl, stage, s, ToScreen);
        DrawShooter(ctx, dl, stage, s, ToScreen);
        DrawShotsAndFx(ctx, dl, stage, s, ToScreen);
        DrawStrip(ctx, dl, stage, origin, size);
        DrawHudBits(dl, stage, fieldTl, fieldSize);
        DrawClearCard(dl, stage, fieldTl, fieldSize);
    }

    /// <summary>The card between one stage and the next: what was finished and what it paid. It dims the
    /// field it is drawn over, since the next stage is already sitting behind it, loaded and still.</summary>
    private void DrawClearCard(ImDrawListPtr dl, GameStage stage, Vector2 fieldTl, Vector2 fieldSize)
    {
        if (_clearLeft <= 0f)
        {
            return;
        }

        // Full for most of its life, then out: a card that faded in as well would eat half the time it has.
        var fade = stage.ReduceMotion ? 1f : MathF.Min(1f, _clearLeft / 0.35f);
        var centre = fieldTl + (fieldSize * 0.5f);
        dl.AddRectFilled(fieldTl, fieldTl + fieldSize, Look.U32(new Vector4(0f, 0f, 0f, 0.55f * fade)));

        var cardW = MathF.Min(fieldSize.X - Px(48f), Px(260f));
        var cardH = Px(96f);
        var tl = centre - new Vector2(cardW * 0.5f, cardH * 0.5f);
        dl.AddRectFilled(tl, tl + new Vector2(cardW, cardH),
            Look.U32(new Vector4(0.07f, 0.06f, 0.11f, 0.96f * fade)), Px(16f));
        dl.AddRect(tl, tl + new Vector2(cardW, cardH), Look.U32(Look.Crystal, 0.5f * fade), Px(16f),
            ImDrawFlags.RoundCornersAll, Px(1.2f));

        Look.Centred(dl, string.Format(Loc.T("os.aetherling_gyre_stage_done"), _clearStage),
            centre.X, tl.Y + Px(20f), Look.U32(Look.CrystalPale, fade), 1.15f);
        Look.Centred(dl, string.Format(Loc.T("os.aetherling_gyre_stage_bonus"), _clearBonus),
            centre.X, tl.Y + Px(46f), Look.U32(Gold, fade), 0.95f);
        // Only the last stage is named, and only because that name is localized. The other nineteen carry
        // authored English names in stages.json, which have no business on a translated card.
        if (_board.Endless)
        {
            Look.Centred(dl, string.Format(Loc.T("os.aetherling_gyre_stage_next"),
                    Loc.T("os.aetherling_gyre_the_core")),
                centre.X, tl.Y + Px(70f), Look.U32(Look.Whisper, 0.9f * fade), 0.85f);
        }
    }

    private void Advance(GameStage stage, float dt)
    {
        _board.Update(dt);

        var speed = GameScoring.GyreShotSpeed * dt;
        for (var i = _shots.Count - 1; i >= 0; i--)
        {
            var shot = _shots[i];
            var steps = Math.Max(1, (int)(speed / 24f));
            var hit = false;
            for (var j = 0; j < steps && !hit; j++)
            {
                shot.Pos += shot.Dir * (speed / steps);
                shot.Travelled += speed / steps;
                if (shot.NeedleTarget is { } target)
                {
                    if (Vector2.Distance(shot.Pos, target) < 30f && _board.CollideShot(target) is { } at)
                    {
                        _board.InsertShot(at.Chain, at.Index, shot.Pos, shot.Kind);
                        stage.Sound(GameSound.Swap);
                        hit = true;
                    }
                    continue;
                }
                if (_board.CollideShot(shot.Pos) is { } h)
                {
                    _board.InsertShot(h.Chain, h.Index, shot.Pos, shot.Kind);
                    stage.Sound(GameSound.Swap);
                    hit = true;
                }
            }
            if (hit || shot.Pos.X < -100f || shot.Pos.X > GyreStages.CanvasWidth + 100f
                || shot.Pos.Y < -100f || shot.Pos.Y > GyreStages.CanvasHeight + 100f)
            {
                _shots.RemoveAt(i);
            }
        }

        if (_powerLeft > 0f)
        {
            _powerLeft -= dt;
            if (_powerLeft <= 0f && _powerElement is { } element)
            {
                _board.FireElement(element, _heldKind);
                _powerElement = null;
            }
        }

        DrainEvents(stage);

        var gained = _board.Score - _lastScore;
        if (gained > 0)
        {
            _lastScore = _board.Score;
            _meterPoints = Math.Min(GameScoring.GyrePowerMeterPoints, _meterPoints + gained);
        }
        var meterTarget = Math.Clamp(_meterPoints / (float)GameScoring.GyrePowerMeterPoints, 0f, 1f);
        _meterGlide += (meterTarget - _meterGlide) * MathF.Min(1f, dt * 8f);
        _shake = MathF.Max(0f, _shake - (dt * 26f));
        _lifeFlash = MathF.Max(0f, _lifeFlash - dt);
        _lumiHop = MathF.Min(0f, _lumiHop + (dt * 60f));

        for (var i = _shards.Count - 1; i >= 0; i--)
        {
            var sh = _shards[i];
            sh.Age += dt;
            sh.Vel += new Vector2(0f, 620f * dt);
            sh.Pos += sh.Vel * dt;
            if (sh.Age > 0.8f)
            {
                _shards.RemoveAt(i);
            }
        }
        for (var i = _texts.Count - 1; i >= 0; i--)
        {
            _texts[i].Age += dt;
            _texts[i].At += new Vector2(0f, -34f * dt);
            if (_texts[i].Age > 1.1f)
            {
                _texts.RemoveAt(i);
            }
        }
        for (var i = _rings.Count - 1; i >= 0; i--)
        {
            _rings[i].Age += dt;
            if (_rings[i].Age > 0.5f)
            {
                _rings.RemoveAt(i);
            }
        }
        for (var i = _snaps.Count - 1; i >= 0; i--)
        {
            _snaps[i].Age += dt;
            if (_snaps[i].Age > SnapSeconds)
            {
                _snaps.RemoveAt(i);
            }
        }
    }

    /// <summary>How long a seam flash lives. Short: it is an impact, not a bloom.</summary>
    private const float SnapSeconds = 0.26f;

    private void DrainEvents(GameStage stage)
    {
        foreach (var e in _board.Events)
        {
            switch (e.Kind)
            {
                case GyreEventKind.Pop:
                    SpawnShards(e.At, e.Colour, Math.Min(4 + (e.Count * 2), 18));
                    stage.Sound((GameSound)((int)GameSound.Ladder0 + Math.Clamp(e.Cascade - 1, 0, 7)));
                    if (e.Points > 0)
                    {
                        _texts.Add(new FloatText
                        {
                            At = e.At,
                            Text = e.Cascade > 1
                                ? string.Format(Loc.T("os.aetherling_gyre_combo"), e.Cascade, e.Points)
                                : $"+{e.Points}",
                            Colour = e.Cascade > 1 ? Gold : Look.CrystalPale,
                        });
                    }
                    _shake = MathF.Min(6f, _shake + 1f + (e.Cascade * 0.6f));
                    _lumiHop = -14f;
                    break;
                case GyreEventKind.Slam:
                    _rings.Add(new Ring { At = e.At, Colour = e.Colour >= 0 ? KindColours[e.Colour] : Gold });
                    _snaps.Add(new Snap { At = e.At, Matched = e.Colour >= 0 });
                    if (e.Colour >= 0)
                    {
                        stage.Sound(GameSound.Burst);
                    }
                    _shake = MathF.Min(6f, _shake + (e.Colour >= 0 ? 2.4f : 1.2f));
                    if (e.Points > 0)
                    {
                        _texts.Add(new FloatText { At = e.At, Text = $"+{e.Points}", Colour = Gold });
                    }
                    break;
                case GyreEventKind.Swallow:
                    stage.Sound(GameSound.Bad);
                    _rings.Add(new Ring { At = e.At, Colour = e.Colour >= 0 ? KindColours[e.Colour] : Gold });
                    _shake = MathF.Min(6f, _shake + 1.6f);
                    _lifeFlash = MathF.Max(_lifeFlash, 0.35f);
                    break;
                case GyreEventKind.PowerTaken:
                    stage.Sound(GameSound.BigCrystal);
                    _texts.Add(new FloatText
                    {
                        At = e.At,
                        Text = Loc.T($"os.aetherling_gyre_pu_{PowerupKey(e.Powerup)}"),
                        Colour = Gold,
                    });
                    break;
                case GyreEventKind.LifeLost:
                    stage.Sound(GameSound.Bad);
                    _shake = 9f;
                    _lifeFlash = 0.6f;
                    _shots.Clear();
                    break;
                case GyreEventKind.StageCleared:
                    stage.Sound(GameSound.LevelUp);
                    _clearLeft = stage.ReduceMotion ? ClearCardSeconds * 0.5f : ClearCardSeconds;
                    _clearStage = e.Count;
                    _clearBonus = e.Points;
                    _shots.Clear();
                    break;
                case GyreEventKind.ExtraLife:
                    stage.Sound(GameSound.Chord2);
                    _texts.Add(new FloatText
                    {
                        At = new Vector2(GyreStages.CanvasWidth * 0.5f, GyreStages.CanvasHeight * 0.3f),
                        Text = Loc.T("os.aetherling_gyre_extra_life"),
                        Colour = new Vector4(1f, 0.55f, 0.65f, 1f),
                    });
                    break;
                case GyreEventKind.DudCrumble:
                    SpawnShards(e.At, -1, 6);
                    stage.Sound(GameSound.Crystal);
                    break;
                case GyreEventKind.PowerFired:
                    stage.Sound(GameSound.Chord1);
                    _shake = 7f;
                    break;
            }
        }
        _board.Events.Clear();
    }

    internal static string PowerupKey(GyrePowerup p) => p switch
    {
        GyrePowerup.Aetherlight => "aetherlight",
        GyrePowerup.Driftmoss => "driftmoss",
        GyrePowerup.Recoil => "recoil",
        GyrePowerup.Shatterstone => "shatterstone",
        GyrePowerup.Threadneedle => "threadneedle",
        _ => "sparkfall",
    };

    private void SpawnShards(Vector2 at, int colour, int count)
    {
        var c = colour >= 0 ? KindColours[colour] : new Vector4(0.5f, 0.5f, 0.55f, 1f);
        for (var i = 0; i < count; i++)
        {
            var a = (float)(_rng.NextDouble() * MathF.Tau);
            var v = 120f + ((float)_rng.NextDouble() * 260f);
            _shards.Add(new Shard
            {
                Pos = at,
                Vel = new Vector2(MathF.Cos(a), MathF.Sin(a) * 0.8f) * v,
                Colour = c,
            });
        }
    }

    private void HandleInput(GameStage stage, Vector2 canvasTl, float s, Vector2 fieldTl)
    {
        var mouse = ImGui.GetMousePos();
        if (mouse.Y < fieldTl.Y)
        {
            return;
        }
        var shooter = ShooterCanvas();
        var mouseCanvas = (mouse - canvasTl) / s;
        var dir = mouseCanvas - shooter;
        if (dir.LengthSquared() > 1f)
        {
            _aimAngle = MathF.Atan2(dir.Y, dir.X);
        }

        if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            (_heldKind, _nextKind) = (_nextKind, _heldKind);
            stage.Sound(GameSound.Swap);
        }
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left) && _shots.Count < 2 && _powerLeft <= 0f)
        {
            var aim = new Vector2(MathF.Cos(_aimAngle), MathF.Sin(_aimAngle));
            var shot = new Shot
            {
                Pos = shooter + (aim * (CradleUnits + 20f)),
                Dir = aim,
                Kind = _heldKind,
            };
            if (_board.ConsumeNeedle())
            {
                shot.NeedleTarget = mouseCanvas;
                shot.Dir = Vector2.Normalize(mouseCanvas - shot.Pos);
            }
            _shots.Add(shot);
            stage.Sound(GameSound.Jump);
            _heldKind = _nextKind;
            _nextKind = _board.RollShotKind();
        }
    }

    private Vector2 ShooterCanvas()
    {
        var dto = _board.StageData?.Shooter;
        return dto is null ? new Vector2(500f, 900f) : new Vector2(dto.X, dto.Y);
    }

    private void DrawField(OsAppContext ctx, ImDrawListPtr dl, GameStage stage, Vector2 fieldTl,
        Vector2 fieldSize, Vector2 canvasTl, Vector2 canvasPx, float s, Func<Vector2, Vector2> toScreen)
    {
        var (top, bottom) = ChapterTints[Chapter];
        GameScene.Sky(dl, fieldTl, fieldSize, top, bottom, Look.CrystalPale);

        // A painted board carries its own groove, mouths and fissures; the danger glow is ours either
        // way, because it belongs to the run rather than to the place.
        var bgPath = Path.Combine(stage.AssetRoot, "games", "gyre", $"bg_{_board.Stage:00}.png");
        var painted = ctx.Capabilities.Textures.Get(bgPath);
        if (painted is { } bg)
        {
            dl.AddImage(bg, canvasTl + _shakeOffset, canvasTl + canvasPx + _shakeOffset);
        }
        else
        {
            DrawGrooves(dl, s, toScreen);
        }

        foreach (var chain in _board.Chains)
        {
            var fissure = toScreen(chain.Path.PosAt(chain.Path.Length));
            var r = 62f * s;
            if (painted is null)
            {
                GyrePieces.EllipseFilled(dl, fissure, new Vector2(r, r * 0.62f),
                    Look.U32(new Vector4(0.03f, 0.02f, 0.08f, 0.95f)));
                GyrePieces.Ellipse(dl, fissure, new Vector2(r, r * 0.62f),
                    Look.U32(new Vector4(0.45f, 0.55f, 0.9f, 0.5f)), 1.6f);
                var mouth = toScreen(chain.Path.PosAt(0f));
                dl.AddCircle(mouth, 44f * s, Look.U32(new Vector4(1f, 1f, 1f, 0.28f)), 24, 2f);
            }
            if (chain.FrontFrac > 0.85f)
            {
                var pulse = 0.35f + (stage.ReduceMotion ? 0f : 0.25f * MathF.Sin((float)ImGui.GetTime() * 7f));
                Look.Halo(dl, fissure, r * 2.2f, new Vector4(1f, 0.3f, 0.28f, 1f), pulse, 3);
            }
        }
    }

    /// <summary>The runtime groove: the shipped look until a painted board exists for the stage, and the
    /// guarantee that track and collision can never disagree.</summary>
    private void DrawGrooves(ImDrawListPtr dl, float s, Func<Vector2, Vector2> toScreen)
    {
        foreach (var chain in _board.Chains)
        {
            var path = chain.Path;
            var step = 14f;
            Vector2 prev = toScreen(path.PosAt(0f));
            for (var d = step; d <= path.Length; d += step)
            {
                var p = toScreen(path.PosAt(d));
                var tunnel = path.InTunnel(d);
                if (!tunnel)
                {
                    dl.AddLine(prev, p, Look.U32(new Vector4(0f, 0f, 0f, 0.30f)), 110f * s);
                    dl.AddLine(prev, p, Look.U32(new Vector4(0f, 0f, 0f, 0.22f)), 84f * s);
                }
                prev = p;
            }
        }
    }

    private void DrawChains(OsAppContext ctx, ImDrawListPtr dl, GameStage stage, float s,
        Func<Vector2, Vector2> toScreen)
    {
        var marbleSize = GyreStages.MarbleDiameter * s;
        for (var pass = 0; pass < 2; pass++)
        {
            foreach (var chain in _board.Chains)
            {
                foreach (var m in chain.Marbles)
                {
                    if (chain.Path.InTunnel(m.D))
                    {
                        continue;
                    }
                    var over = chain.Path.InOverpass(m.D);
                    if (over != (pass == 1))
                    {
                        continue;
                    }
                    var at = toScreen(chain.Path.PosAt(m.D));
                    GyrePieces.EllipseFilled(dl, at + new Vector2(0f, marbleSize * 0.18f),
                        new Vector2(marbleSize * 0.4f, marbleSize * 0.16f), Look.U32(new Vector4(0f, 0f, 0f, 0.3f)));
                    GyrePieces.Marble(ctx, dl, stage.AssetRoot, at, marbleSize, m.Kind, m.Dud,
                        1f, m.D * s, m.Power);
                }
            }
        }

        foreach (var chain in _board.Chains)
        {
            var path = chain.Path;
            var step = 14f;
            Vector2? prev = null;
            for (var d = 0f; d <= path.Length; d += step)
            {
                if (path.InTunnel(d))
                {
                    var p = toScreen(path.PosAt(d));
                    if (prev is { } q)
                    {
                        dl.AddLine(q, p, Look.U32(new Vector4(0.12f, 0.1f, 0.16f, 0.96f)), 124f * s);
                    }
                    prev = p;
                }
                else
                {
                    prev = null;
                }
            }
        }

        if (_board.FrozenLeft > 0f)
        {
            var tint = new Vector4(0.5f, 0.75f, 1f, 0.10f + (0.04f * MathF.Sin((float)ImGui.GetTime() * 3f)));
            dl.AddRectFilled(stage.Origin + new Vector2(0f, StripHeight), stage.Origin + stage.Size, Look.U32(tint));
        }
        if (_lifeFlash > 0f)
        {
            dl.AddRectFilled(stage.Origin + new Vector2(0f, StripHeight), stage.Origin + stage.Size,
                Look.U32(new Vector4(1f, 0.2f, 0.2f, 0.35f * _lifeFlash)));
        }
    }

    private void DrawShooter(OsAppContext ctx, ImDrawListPtr dl, GameStage stage, float s,
        Func<Vector2, Vector2> toScreen)
    {
        var shooter = ShooterCanvas();
        var centre = toScreen(shooter);
        var petPx = ShooterPetUnits * s;
        var aim = new Vector2(MathF.Cos(_aimAngle), MathF.Sin(_aimAngle));

        GyrePieces.Ellipse(dl, centre + new Vector2(0f, petPx * 0.42f), new Vector2(petPx * 0.78f, petPx * 0.3f),
            Look.U32(Look.CrystalPale, 0.4f), 2f);
        var notch = centre + new Vector2(0f, petPx * 0.42f)
            + new Vector2(aim.X * petPx * 0.78f, aim.Y * petPx * 0.3f);
        dl.AddCircleFilled(notch, 4f * s + 2f, Look.U32(Look.CrystalPale, 0.85f), 12);

        if (_board.AimLeft > 0f && !_board.Over)
        {
            var from = shooter + (aim * (CradleUnits + 30f));
            var pos = from;
            for (var i = 0; i < 220; i++)
            {
                pos += aim * 12f;
                if (_board.CollideShot(pos) is not null || pos.X < 0f || pos.X > GyreStages.CanvasWidth
                    || pos.Y < 0f || pos.Y > GyreStages.CanvasHeight)
                {
                    break;
                }
                if (i % 3 == 0)
                {
                    dl.AddCircleFilled(toScreen(pos), 2.4f, Look.U32(Gold, 0.55f), 8);
                }
            }
        }

        var behind = aim.Y < 0f;
        var cradle = toScreen(shooter + (aim * CradleUnits));
        void DrawCradle()
        {
            dl.AddCircleFilled(cradle, 26f * s, Look.U32(new Vector4(0.9f, 0.86f, 0.72f, 0.35f)), 22);
            dl.AddCircle(cradle, 26f * s, Look.U32(new Vector4(0.72f, 0.6f, 0.36f, 0.8f)), 22, 2.2f);
            GyrePieces.Marble(ctx, dl, stage.AssetRoot, cradle, GyreStages.MarbleDiameter * s * 0.92f, _heldKind, false);
        }
        if (behind)
        {
            DrawCradle();
        }

        var pose = stage.Runtime.Pose;
        pose.Offset += new Vector2(aim.X * 7f, 0f);
        pose.FlipX = aim.X < 0f;
        if (_lumiHop < -2f)
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

        var next = toScreen(shooter - (aim * (CradleUnits * 0.7f)));
        GyrePieces.Marble(ctx, dl, stage.AssetRoot, next, GyreStages.MarbleDiameter * s * 0.6f, _nextKind, false, 0.85f);
    }

    private void DrawShotsAndFx(OsAppContext ctx, ImDrawListPtr dl, GameStage stage, float s,
        Func<Vector2, Vector2> toScreen)
    {
        foreach (var shot in _shots)
        {
            var at = toScreen(shot.Pos);
            Look.Halo(dl, at, GyreStages.MarbleDiameter * s * 0.7f, KindColours[shot.Kind], 0.3f, 3);
            GyrePieces.Marble(ctx, dl, stage.AssetRoot, at, GyreStages.MarbleDiameter * s * 0.94f, shot.Kind,
                false, 1f, shot.Travelled * s);
        }

        foreach (var sh in _shards)
        {
            var alpha = 1f - (sh.Age / 0.8f);
            dl.AddCircleFilled(toScreen(sh.Pos), MathF.Max(1.5f, 5f * s * alpha), Look.U32(sh.Colour, alpha), 10);
        }
        foreach (var ring in _rings)
        {
            var t = ring.Age / 0.5f;
            dl.AddCircle(toScreen(ring.At), (30f + (110f * t)) * s, Look.U32(ring.Colour, 0.7f * (1f - t)), 32,
                MathF.Max(1.5f, 4f * (1f - t)));
        }
        foreach (var snap in _snaps)
        {
            // Collapsing inward, not blooming outward: the eye should read two things arriving at a point.
            var t = snap.Age / SnapSeconds;
            var at = toScreen(snap.At);
            var ink = snap.Matched ? Gold : new Vector4(0.92f, 0.95f, 1f, 1f);
            var reach = (52f - (40f * t)) * s;
            for (var i = 0; i < 6; i++)
            {
                var a = (MathF.Tau * i / 6f) + (t * 0.6f);
                var dir = new Vector2(MathF.Cos(a), MathF.Sin(a));
                dl.AddLine(at + (dir * reach), at + (dir * (reach + (14f * s * (1f - t)))),
                    Look.U32(ink, 0.85f * (1f - t)), MathF.Max(1.5f, 3.4f * s * (1f - t)));
            }
            Look.Halo(dl, at, (18f + (26f * (1f - t))) * s, ink, 0.5f * (1f - t), 3);
        }
        foreach (var text in _texts)
        {
            var alpha = 1f - Math.Clamp((text.Age - 0.5f) / 0.6f, 0f, 1f);
            Look.Centred(dl, text.Text, toScreen(text.At).X, toScreen(text.At).Y, Look.U32(text.Colour, alpha), 0.95f);
        }
    }

    private void DrawHudBits(ImDrawListPtr dl, GameStage stage, Vector2 fieldTl, Vector2 fieldSize)
    {

        var main = _board.Score.ToString("N0");
        var aside = _board.Endless
            ? Loc.T("os.aetherling_gyre_the_core")
            : string.Format(Loc.T("os.aetherling_gyre_stage"), _board.Stage);
        var pillY = fieldTl.Y + Px(10f);
        var centreX = fieldTl.X + (fieldSize.X * 0.5f);
        var h = Look.Pill(dl, main, centreX, pillY, Look.Crystal, 0.95f, 1.05f);
        Look.Centred(dl, aside, centreX, pillY + h + Px(2f), Look.U32(Look.Whisper), 0.85f);

        // The run's health: a bar that drains a notch per marble the fissure takes, green while it is
        // comfortable and red when it is not. One heart names what the bar is.
        var barW = Px(96f);
        var barH = Px(10f);
        var right = stage.Origin.X + stage.Size.X - GamesScreen.CornerReserve;
        var barTl = new Vector2(right - barW, fieldTl.Y + Px(14f));
        var frac = Math.Clamp(_board.Hp / (float)GameScoring.GyreMaxHp, 0f, 1f);
        var tone = frac > 0.5f
            ? new Vector4(0.45f, 0.85f, 0.45f, 1f)
            : Vector4.Lerp(new Vector4(0.92f, 0.28f, 0.28f, 1f), new Vector4(0.95f, 0.78f, 0.30f, 1f),
                frac * 2f);
        var low = frac <= 0.3f && !stage.ReduceMotion
            ? 0.7f + (0.3f * MathF.Sin((float)ImGui.GetTime() * 6f))
            : 1f;
        dl.AddRectFilled(barTl, barTl + new Vector2(barW, barH), Look.U32(new Vector4(0f, 0f, 0f, 0.45f)),
            barH * 0.5f);
        if (frac > 0f)
        {
            dl.AddRectFilled(barTl, barTl + new Vector2(MathF.Max(barH, barW * frac), barH),
                Look.U32(tone with { W = low }), barH * 0.5f);
        }
        dl.AddRect(barTl, barTl + new Vector2(barW, barH), Look.U32(new Vector4(1f, 1f, 1f, 0.4f)),
            barH * 0.5f, ImDrawFlags.RoundCornersAll, 1.2f);
        IconDraw.AddCentered(dl, FontAwesomeIcon.Heart, Px(12f),
            new Vector2(barTl.X - Px(12f), barTl.Y + (barH * 0.5f)),
            Look.U32(new Vector4(1f, 0.55f, 0.65f, 1f)));

        var pillX = fieldTl.X + Px(12f);
        var bottom = fieldTl.Y + fieldSize.Y - Px(26f);
        void TimerPill(string text, Vector4 colour)
        {
            var sz = ImGui.CalcTextSize(text) * 0.8f;
            dl.AddRectFilled(new Vector2(pillX, bottom), new Vector2(pillX + sz.X + Px(14f), bottom + Px(20f)),
                Look.U32(new Vector4(0f, 0f, 0f, 0.45f)), Px(10f));
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.8f,
                new Vector2(pillX + Px(7f), bottom + Px(3f)), Look.U32(colour), text);
            pillX += sz.X + Px(20f);
        }
        if (_board.FrozenLeft > 0f)
        {
            TimerPill(string.Format(Loc.T("os.aetherling_gyre_frozen"), MathF.Ceiling(_board.FrozenLeft)), KindColours[2]);
        }
        if (_board.SlowLeft > 0f)
        {
            TimerPill(string.Format(Loc.T("os.aetherling_gyre_slow"), MathF.Ceiling(_board.SlowLeft)), KindColours[3]);
        }
        if (_board.DoubleLeft > 0f)
        {
            TimerPill(string.Format(Loc.T("os.aetherling_gyre_double"), MathF.Ceiling(_board.DoubleLeft)), Gold);
        }
        if (_board.ShatterShots > 0)
        {
            TimerPill(string.Format(Loc.T("os.aetherling_gyre_shatter"), _board.ShatterShots), KindColours[5]);
        }
        if (_board.NeedleShots > 0)
        {
            TimerPill(string.Format(Loc.T("os.aetherling_gyre_needle"), _board.NeedleShots), Look.CrystalPale);
        }
    }

    private bool ElementUnlocked(AetherlingElement element) => LumiLinkGame.ElementUnlocked(_core, element);

    private int FeedsLeft(AetherlingElement element) => LumiLinkGame.FeedsLeft(_core, element);

    private void BeginPower(GameStage stage, AetherlingElement element)
    {
        _meterPoints = 0;
        _powerElement = element;
        _powerLeft = PowerSeconds;
        stage.Sound(GameSound.Chord0);
        _lumiHop = -16f;
        _shake = 4f;
    }

    /// <summary>The creature, its elements and the power bar, LumiLink's strip in Gyre's colours: the
    /// powers are literally what it has eaten, whichever game it is playing.</summary>
    private void DrawStrip(OsAppContext ctx, ImDrawListPtr dl, GameStage stage, Vector2 origin, Vector2 size)
    {
        var stripBr = origin + new Vector2(size.X, StripHeight);
        dl.AddRectFilledMultiColor(origin, stripBr,
            Look.U32(new Vector4(0.08f, 0.06f, 0.14f, 0.85f)), Look.U32(new Vector4(0.08f, 0.06f, 0.14f, 0.85f)),
            Look.U32(new Vector4(0.04f, 0.03f, 0.08f, 0f)), Look.U32(new Vector4(0.04f, 0.03f, 0.08f, 0f)));

        var chip = GamesScreen.CornerReserve;
        var discR = 19f;
        var gap = 10f;
        var rowW = (6 * discR * 2f) + (5 * gap);
        var left = origin.X + size.X - 14f - rowW;
        var discY = origin.Y + chip + discR + 14f;
        var now = ImGui.GetTime();
        var meterFull = MeterFull;
        for (var i = 0; i < 6; i++)
        {
            var element = ElementOrder[i];
            var unlocked = ElementUnlocked(element);
            var c = new Vector2(left + discR + (i * ((discR * 2f) + gap)), discY);
            var tappable = unlocked && meterFull && _powerLeft <= 0f && !_board.Over;
            var pulse = tappable && !stage.ReduceMotion ? 0.5f + (0.5f * MathF.Sin((float)(now * 6.0) + i)) : 0f;
            var hovered = ImGui.IsMouseHoveringRect(c - new Vector2(discR), c + new Vector2(discR));
            if (tappable)
            {
                Look.Halo(dl, c, discR * (1.6f + (0.3f * pulse)), KindColours[i], 0.25f + (0.2f * pulse));
            }
            dl.AddCircleFilled(c, discR, Look.U32(new Vector4(1f, 1f, 1f, unlocked ? 0.14f : 0.05f)), 28);
            dl.AddCircle(c, discR, Look.U32(KindColours[i] with { W = unlocked ? 0.6f : 0.15f }), 28, 1.2f);
            var icon = ctx.Capabilities.Textures.Get(Path.Combine(stage.AssetRoot, "crystals", Elements[i] + ".png"));
            if (icon is { } handle)
            {
                var half = discR * 0.78f;
                var tint = unlocked ? Look.U32(new Vector4(1f, 1f, 1f, 1f)) : Look.U32(new Vector4(0.5f, 0.5f, 0.55f, 0.5f));
                dl.AddImage(handle, c - new Vector2(half), c + new Vector2(half), Vector2.Zero, Vector2.One, tint);
            }
            if (!unlocked)
            {
                IconDraw.AddCentered(dl, FontAwesomeIcon.Lock, discR * 0.7f, c + new Vector2(discR * 0.55f, discR * 0.55f),
                    Look.U32(new Vector4(1f, 1f, 1f, 0.75f)));
            }
            if (hovered)
            {
                ImGui.SetTooltip(unlocked
                    ? Loc.T($"os.aetherling_gyre_power_{Elements[i]}")
                    : string.Format(Loc.T("os.aetherling_lumilink_locked"), FeedsLeft(element),
                        Loc.T($"os.aetherling_element_{Elements[i]}")));
                if (tappable)
                {
                    ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                    if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                    {
                        BeginPower(stage, element);
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
        var label = meterFull
            ? Loc.T("os.aetherling_lumilink_power_ready")
            : Loc.T("os.aetherling_lumilink_power_charging");
        var labelSz = ImGui.CalcTextSize(label) * 0.8f;
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.8f,
            new Vector2(barX + barW - labelSz.X, barY + barH + 4f),
            Look.U32(meterFull ? Gold : Look.Whisper), label);
    }
}
