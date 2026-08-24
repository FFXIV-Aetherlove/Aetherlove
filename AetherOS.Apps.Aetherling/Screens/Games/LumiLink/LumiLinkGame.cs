using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using AetherLove.Services.Localization;
using AetherLove.Shared.Aetherling;
using AetherLove.UI;
using AetherLove.Shared.Arcade;
using AetherOS.Apps.Aetherling.Engine;
using AetherOS.Apps.Aetherling.Rendering;
using AetherOS.Apps.Aetherling.Ui;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Aetherling.Screens.Games.LumiLink;

/// <summary>Lumi-Link, the match-3. The board (<see cref="LumiLinkBoard"/>) decides what happens; this
/// class decides how it looks, sounds and feels: swaps that squash, clears that pop into shards of their
/// own colour, falls with weight and a bounce, a rising ladder of chimes per cascade, a shake that grows
/// with the combo, a level bar under the grid that pays time and sways the whole board into the next
/// theme, and the creature on top with its element powers charging below it.
/// <para>The clock is the only budget. Everything visual is gated on ReduceMotion, which collapses the
/// animations to instant settles and keeps the sounds.</para></summary>
internal sealed class LumiLinkGame : IPetGame
{
    private const float StripHeight = 164f;
    private const float HudHeight = 72f;
    private const float SwapSeconds = 0.14f;
    private const float BadSwapSeconds = 0.5f;
    private const float PopSeconds = 0.22f;
    private const float MintSeconds = 0.16f;
    private const float SwaySeconds = 0.9f;
    private const float ShuffleSeconds = 0.6f;
    private const float PowerSeconds = 0.55f;
    private const float HintAfterSeconds = 6f;
    private const float FallAccel = 46f;
    private const float FallMax = 22f;
    private const int Themes = LumiLinkPieces.Themes;

    private enum Phase
    {
        Idle,
        Swapping,
        SwappingBack,
        Popping,
        Minting,
        Falling,
        Shuffling,
        Sway,
        PowerFx,
    }

    private sealed class Vis
    {
        public int Kind;
        public Special Special;
        public int Col;
        public float Y;
        public float TargetRow;
        public float Vy;
        public float Scale = 1f;
        public bool Settled = true;
        public float Land;
    }

    private sealed class Pop
    {
        public int Col;
        public float Row;
        public int Kind;
        public Special Special;
        public float Age;
    }

    private sealed class FloatText
    {
        public Vector2 At;
        public string Text;
        public Vector4 Colour;
        public float Age;
        public float Scale;

        public FloatText(Vector2 at, string text, Vector4 colour, float scale)
        {
            At = at;
            Text = text;
            Colour = colour;
            Scale = scale;
        }
    }

    private sealed class LineFlash
    {
        public bool Horizontal;
        public int Index;
        public float Age;
        public Vector4 Colour;
    }

    private readonly LumiLinkBoard _board = new();
    private readonly Dictionary<int, Vis> _vis = [];
    private readonly List<Pop> _pops = [];
    private readonly List<FloatText> _texts = [];
    private readonly List<LineFlash> _flashes = [];
    private readonly Queue<ResolveStep> _queue = new();
    private readonly ParticleFx _fx = new();

    private static readonly string[] Elements = LumiLinkPieces.Elements;
    internal static readonly AetherlingElement[] ElementOrder =
    [
        AetherlingElement.Fire, AetherlingElement.Water, AetherlingElement.Ice,
        AetherlingElement.Wind, AetherlingElement.Lightning, AetherlingElement.Earth,
    ];
    private static readonly Vector4[] KindColours = LumiLinkPieces.KindColours;
    private static readonly Vector4 Gold = new(0.98f, 0.82f, 0.36f, 1f);

    private Phase _phase = Phase.Idle;
    private float _phaseT;
    private Random _rng = new();
    private int _theme;
    private int _nextTheme;
    private float _timeLeft;
    private float _frozenLeft;
    private int _score;
    private int _level;
    private int _levelPoints;
    private int _levelTarget;
    private bool _levelUpPending;
    private int _meterPoints;
    private int _biggestCascade;
    private int _cascadeIndex;
    private float _idleSeconds;
    private float _shake;
    private Vector2 _shakeOffset;
    private float _lumiHop;
    private float _lumiHopVy;
    private float _meterGlide;
    private float _levelGlide;
    private float _lastTick;
    private float _clock;
    private readonly List<(float At, GameSound Sound)> _cues = [];
    private bool _over;

    private (int C, int R)? _selected;
    private (int C, int R)? _pressCell;
    private Vector2 _pressAt;
    private (int C1, int R1, int C2, int R2)? _swap;
    private bool _badReturned;
    private (int C1, int R1, int C2, int R2)? _hint;
    private AetherlingElement? _powerElement;
    private AetherlingDto? _core;

    public ArcadeGame Id => ArcadeGame.LumiLink;

    public bool Over => _over;

    public int Score => _score;

    public int Metric1 => _level;

    public int Metric2 => _biggestCascade;

    /// <summary>The creature whose elements power the strip. Set before a run starts; null plays with no
    /// powers at all, which is what a hatchling gets.</summary>
    public void SetCreature(AetherlingDto? core) => _core = core;

    public void Reset(Random rng)
    {
        _rng = rng;
        _board.Reset(rng);
        _vis.Clear();
        _pops.Clear();
        _texts.Clear();
        _flashes.Clear();
        _queue.Clear();
        _fx.Clear();
        for (var c = 0; c < LumiLinkBoard.Columns; c++)
        {
            for (var r = 0; r < LumiLinkBoard.Rows; r++)
            {
                if (_board[c, r] is { } p)
                {
                    _vis[p.Id] = new Vis { Kind = p.Kind, Col = c, Y = r, TargetRow = r };
                }
            }
        }
        _phase = Phase.Idle;
        _phaseT = 0f;
        _theme = rng.Next(Themes);
        _timeLeft = GameScoring.LumiLinkStartSeconds;
        _frozenLeft = 0f;
        _score = 0;
        _level = 1;
        _levelPoints = 0;
        _levelTarget = GameScoring.LumiLinkLevel1Target;
        _levelUpPending = false;
        _meterPoints = 0;
        _biggestCascade = 0;
        _cascadeIndex = 0;
        _idleSeconds = 0f;
        _shake = 0f;
        _lumiHop = 0f;
        _lumiHopVy = 0f;
        _meterGlide = 0f;
        _levelGlide = 0f;
        _lastTick = 0f;
        _clock = 0f;
        _cues.Clear();
        _over = false;
        _selected = null;
        _pressCell = null;
        _swap = null;
        _hint = null;
        _powerElement = null;
    }

    private bool ElementUnlocked(AetherlingElement element) => ElementUnlocked(_core, element);

    internal static bool ElementUnlocked(AetherlingDto? core, AetherlingElement element)
    {
        if (core?.Adult is not { } adult)
        {
            return false;
        }
        if ((AetherlingElement)adult.Element == element)
        {
            return true;
        }
        foreach (var d in adult.Diet)
        {
            if ((AetherlingElement)d.Element == element && d.Count >= adult.DietTurnThreshold)
            {
                return true;
            }
        }
        return false;
    }

    private int FeedsLeft(AetherlingElement element) => FeedsLeft(_core, element);

    internal static int FeedsLeft(AetherlingDto? core, AetherlingElement element)
    {
        if (core?.Adult is not { } adult)
        {
            return 0;
        }
        foreach (var d in adult.Diet)
        {
            if ((AetherlingElement)d.Element == element)
            {
                return Math.Max(0, adult.DietTurnThreshold - d.Count);
            }
        }
        return adult.DietTurnThreshold;
    }

    private bool MeterFull => _meterPoints >= GameScoring.LumiLinkPowerMeterPoints;

    public void UpdateAndDraw(OsAppContext ctx, ImDrawListPtr dl, GameStage stage, float dt)
    {
        var reduce = stage.ReduceMotion;
        var origin = stage.Origin;
        var size = stage.Size;

        var boardW = size.X - 24f;
        var tile = MathF.Floor(MathF.Min(boardW / LumiLinkBoard.Columns,
            (size.Y - StripHeight - HudHeight - 16f) / LumiLinkBoard.Rows));
        var gridW = tile * LumiLinkBoard.Columns;
        var gridH = tile * LumiLinkBoard.Rows;
        var gridTl = new Vector2(origin.X + (size.X - gridW) * 0.5f, origin.Y + StripHeight + 6f);

        Advance(stage, dt, tile);
        _clock += dt;
        for (var i = _cues.Count - 1; i >= 0; i--)
        {
            if (_cues[i].At <= _clock)
            {
                stage.Sound(_cues[i].Sound);
                _cues.RemoveAt(i);
            }
        }
        if (!reduce)
        {
            _shakeOffset = _shake > 0.01f
                ? new Vector2((float)(_rng.NextDouble() * 2 - 1), (float)(_rng.NextDouble() * 2 - 1)) * _shake
                : Vector2.Zero;
        }
        else
        {
            _shakeOffset = Vector2.Zero;
        }
        var gtl = gridTl + _shakeOffset;

        DrawStrip(ctx, dl, stage, origin, size);
        DrawBoardBack(dl, gtl, gridW, gridH, tile);
        if (stage.InputActive && !_over)
        {
            HandleInput(dl, stage, gtl, tile);
        }

        dl.PushClipRect(gtl - new Vector2(2f, 2f), gtl + new Vector2(gridW + 2f, gridH + 2f), true);
        DrawPieces(ctx, dl, stage, gtl, tile);
        DrawPops(ctx, dl, stage, gtl, tile);
        DrawFlashes(dl, gtl, tile, gridW, gridH);
        if (_phase == Phase.Sway)
        {
            DrawSwayCurtain(dl, gtl, gridW, gridH);
        }
        dl.PopClipRect();

        DrawHud(ctx, dl, origin, size, gtl, gridW, gridH);
        DrawTexts(dl);
        var fxBottom = new Vector2(origin.X + (size.X * 0.5f), origin.Y + size.Y);
        _fx.Update(dt);
        _fx.Draw(dl, fxBottom, size.Y, behind: false);
    }

    /// <summary>The clock, the animation phases and the step queue, all of which run whether or not the
    /// player is touching anything.</summary>
    private void Advance(GameStage stage, float dt, float tile)
    {
        var reduce = stage.ReduceMotion;
        _shake = MathF.Max(0f, _shake - (dt * 26f));
        _lumiHopVy += dt * 900f;
        _lumiHop = MathF.Min(0f, _lumiHop + (_lumiHopVy * dt));
        if (_lumiHop >= 0f)
        {
            _lumiHopVy = 0f;
        }
        for (var i = _texts.Count - 1; i >= 0; i--)
        {
            _texts[i].Age += dt;
            if (_texts[i].Age > 0.9f)
            {
                _texts.RemoveAt(i);
            }
        }
        for (var i = _flashes.Count - 1; i >= 0; i--)
        {
            _flashes[i].Age += dt;
            if (_flashes[i].Age > 0.35f)
            {
                _flashes.RemoveAt(i);
            }
        }

        if (!_over)
        {
            if (_frozenLeft > 0f)
            {
                _frozenLeft -= dt;
            }
            else if (_phase != Phase.Sway)
            {
                _timeLeft -= dt;
            }
            if (_timeLeft <= 5f && _timeLeft > 0f && MathF.Floor(_timeLeft) != MathF.Floor(_lastTick))
            {
                stage.Sound(GameSound.Tick);
            }
            _lastTick = _timeLeft;
            if (_timeLeft <= 0f && _phase == Phase.Idle && _queue.Count == 0)
            {
                _timeLeft = 0f;
                _over = true;
                _selected = null;
            }
        }

        var target = _meterPoints / (float)GameScoring.LumiLinkPowerMeterPoints;
        _meterGlide = reduce ? target : _meterGlide + ((target - _meterGlide) * (1f - MathF.Exp(-dt * 4f)));
        var levelTarget = Math.Clamp(_levelPoints / (float)_levelTarget, 0f, 1f);
        _levelGlide = reduce ? levelTarget : _levelGlide + ((levelTarget - _levelGlide) * (1f - MathF.Exp(-dt * 5f)));

        _phaseT += dt;
        switch (_phase)
        {
            case Phase.Idle:
                _idleSeconds += dt;
                if (_idleSeconds >= HintAfterSeconds && _hint is null)
                {
                    _hint = _board.FindAnyMove();
                }
                break;

            case Phase.Swapping:
                if (reduce || _phaseT >= SwapSeconds)
                {
                    CommitSwap(stage);
                }
                break;

            case Phase.SwappingBack:
                if (!_badReturned && _phaseT >= BadSwapSeconds * 0.55f)
                {
                    _badReturned = true;
                    stage.Sound(GameSound.Bad);
                    _shake = MathF.Max(_shake, 3f);
                }
                if (reduce || _phaseT >= BadSwapSeconds)
                {
                    _swap = null;
                    _phase = Phase.Idle;
                }
                break;

            case Phase.Popping:
                for (var i = 0; i < _pops.Count; i++)
                {
                    _pops[i].Age += dt;
                }
                if (reduce || _phaseT >= PopSeconds)
                {
                    _pops.Clear();
                    BeginMint(stage);
                }
                break;

            case Phase.Minting:
                if (reduce || _phaseT >= MintSeconds)
                {
                    BeginFalls(stage);
                }
                break;

            case Phase.Falling:
                if (StepFalls(dt, reduce, stage))
                {
                    AfterSettle(stage);
                }
                break;

            case Phase.Shuffling:
                if (reduce || _phaseT >= ShuffleSeconds)
                {
                    SyncVisualsToBoard();
                    EnqueueSteps(_board.Settle(), stage);
                    _phase = Phase.Idle;
                    DrainQueue(stage);
                }
                break;

            case Phase.Sway:
                if ((reduce || _phaseT >= SwaySeconds * 0.5f) && _theme != _nextTheme)
                {
                    // A new level is a new board: nothing minted carries over.
                    _theme = _nextTheme;
                    _board.Reset(_rng);
                    _queue.Clear();
                    _pops.Clear();
                    _hint = null;
                    _selected = null;
                    SyncVisualsToBoard();
                }
                if (reduce || _phaseT >= SwaySeconds)
                {
                    _phase = Phase.Idle;
                }
                break;

            case Phase.PowerFx:
                if (reduce || _phaseT >= PowerSeconds)
                {
                    FirePower(stage);
                }
                break;
        }
    }

    private void HandleInput(ImDrawListPtr dl, GameStage stage, Vector2 gtl, float tile)
    {
        var io = ImGui.GetIO();
        var mouse = io.MousePos;
        var col = (int)MathF.Floor((mouse.X - gtl.X) / tile);
        var row = (int)MathF.Floor((mouse.Y - gtl.Y) / tile);
        var onBoard = LumiLinkBoard.InBounds(col, row)
            && mouse.X >= gtl.X && mouse.Y >= gtl.Y;

        if (_phase != Phase.Idle)
        {
            return;
        }

        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left) && onBoard)
        {
            _idleSeconds = 0f;
            _hint = null;
            _pressCell = (col, row);
            _pressAt = mouse;
            if (_selected is { } sel && LumiLinkBoard.Adjacent(sel.C, sel.R, col, row))
            {
                TrySwap(stage, sel.C, sel.R, col, row);
                _selected = null;
                _pressCell = null;
                return;
            }
            if (_selected is { } same && same.C == col && same.R == row)
            {
                _selected = null;
                return;
            }
            _selected = (col, row);
        }

        // A drag across the cell border is a swap in that direction, the faster way to play.
        if (_pressCell is { } press && ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            var delta = mouse - _pressAt;
            if (MathF.Max(MathF.Abs(delta.X), MathF.Abs(delta.Y)) >= tile * 0.45f)
            {
                var (dc, dr) = MathF.Abs(delta.X) > MathF.Abs(delta.Y)
                    ? (Math.Sign(delta.X), 0)
                    : (0, Math.Sign(delta.Y));
                TrySwap(stage, press.C, press.R, press.C + dc, press.R + dr);
                _selected = null;
                _pressCell = null;
            }
        }
        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            _pressCell = null;
        }
        if (onBoard && _phase == Phase.Idle)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }
    }

    private void TrySwap(GameStage stage, int c1, int r1, int c2, int r2)
    {
        if (!LumiLinkBoard.InBounds(c2, r2))
        {
            return;
        }
        _swap = (c1, r1, c2, r2);
        _phase = _board.SwapIsLegal(c1, r1, c2, r2) ? Phase.Swapping : Phase.SwappingBack;
        _phaseT = 0f;
        _badReturned = false;
        stage.Sound(GameSound.Swap);
    }

    private void CommitSwap(GameStage stage)
    {
        if (_swap is not { } s)
        {
            _phase = Phase.Idle;
            return;
        }
        var a = _board[s.C1, s.R1];
        var b = _board[s.C2, s.R2];
        var steps = _board.PlaySwap(s.C1, s.R1, s.C2, s.R2);
        // The visuals follow the pieces, not the cells.
        if (a is not null && _vis.TryGetValue(a.Id, out var va))
        {
            va.Col = s.C2;
            va.Y = s.R2;
            va.TargetRow = s.R2;
        }
        if (b is not null && _vis.TryGetValue(b.Id, out var vb))
        {
            vb.Col = s.C1;
            vb.Y = s.R1;
            vb.TargetRow = s.R1;
        }
        _swap = null;
        _cascadeIndex = 0;
        EnqueueSteps(steps, stage);
        _phase = Phase.Idle;
        DrainQueue(stage);
    }

    private void EnqueueSteps(List<ResolveStep> steps, GameStage stage)
    {
        foreach (var step in steps)
        {
            _queue.Enqueue(step);
        }
    }

    /// <summary>Takes the next step when the board is quiet; otherwise decides what a quiet board owes:
    /// a level sway, a shuffle, or nothing.</summary>
    private void DrainQueue(GameStage stage)
    {
        if (_queue.Count > 0)
        {
            BeginPop(_queue.Dequeue(), stage);
            return;
        }
        _cascadeIndex = 0;
        if (_levelUpPending)
        {
            _levelUpPending = false;
            BeginSway(stage);
            return;
        }
        if (!_over && !_board.HasMove())
        {
            _phase = Phase.Shuffling;
            _phaseT = 0f;
            _board.Shuffle();
            stage.Sound(GameSound.Bad);
            return;
        }
        if (_timeLeft <= 0f)
        {
            _over = true;
        }
    }

    private ResolveStep? _current;

    private void BeginPop(ResolveStep step, GameStage stage)
    {
        _current = step;
        _cascadeIndex++;
        _biggestCascade = Math.Max(_biggestCascade, _cascadeIndex);
        _phase = Phase.Popping;
        _phaseT = 0f;

        var points = step.Points;
        _score += points;
        _levelPoints += points;
        _meterPoints = Math.Min(GameScoring.LumiLinkPowerMeterPoints, _meterPoints + points);

        var ladder = Math.Min(_cascadeIndex - 1, 7);
        stage.Sound((GameSound)((int)GameSound.Ladder0 + ladder));
        var loudest = ClearCause.Match;
        var sumX = 0f;
        var sumY = 0f;
        foreach (var cell in step.Cleared)
        {
            var piece = FindVisAt(cell.Col, cell.Row);
            _pops.Add(new Pop
            {
                Col = cell.Col,
                Row = cell.Row,
                Kind = cell.Kind,
                Special = piece?.Special ?? Special.None,
            });
            if (piece is not null)
            {
                _vis.Remove(IdOf(piece));
            }
            sumX += cell.Col;
            sumY += cell.Row;
            if (cell.Cause > loudest)
            {
                loudest = cell.Cause;
            }
        }
        if (step.Cleared.Count > 0)
        {
            _popCentre = new Vector2(sumX / step.Cleared.Count, sumY / step.Cleared.Count);
        }
        // A whole row or column gone to a Bolt or Burst gets a streak of light down its line.
        Span<int> rowHits = stackalloc int[LumiLinkBoard.Rows];
        Span<int> colHits = stackalloc int[LumiLinkBoard.Columns];
        foreach (var cell in step.Cleared)
        {
            if (cell.Cause is ClearCause.Bolt or ClearCause.Burst or ClearCause.Power)
            {
                rowHits[cell.Row]++;
                colHits[cell.Col]++;
            }
        }
        for (var r = 0; r < LumiLinkBoard.Rows; r++)
        {
            if (rowHits[r] >= LumiLinkBoard.Columns)
            {
                _flashes.Add(new LineFlash { Horizontal = true, Index = r, Colour = Look.CrystalPale });
            }
        }
        for (var c = 0; c < LumiLinkBoard.Columns; c++)
        {
            if (colHits[c] >= LumiLinkBoard.Rows)
            {
                _flashes.Add(new LineFlash { Horizontal = false, Index = c, Colour = Look.CrystalPale });
            }
        }
        switch (loudest)
        {
            case ClearCause.Bolt:
                stage.Sound(GameSound.Bolt);
                _shake = MathF.Max(_shake, 4f);
                break;
            case ClearCause.Burst:
                stage.Sound(GameSound.Burst);
                _shake = MathF.Max(_shake, 6f);
                break;
            case ClearCause.Prism:
                Chord();
                _shake = MathF.Max(_shake, step.PrismCombo ? 12f : 8f);
                break;
            default:
                _shake = MathF.Max(_shake, 1.2f + (0.6f * _cascadeIndex));
                break;
        }
        if (_cascadeIndex >= 3 || loudest >= ClearCause.Burst)
        {
            _lumiHopVy = -(220f + (40f * Math.Min(_cascadeIndex, 6)));
        }
        if (_cascadeIndex == 4 || step.PrismCombo)
        {
            stage.Sound(GameSound.Chirp);
        }
        _pendingText = (points, _cascadeIndex);

        if (_levelPoints >= _levelTarget)
        {
            _levelUpPending = true;
        }
    }

    private Vector2 _popCentre;
    private (int Points, int Cascade)? _pendingText;

    private void BeginMint(GameStage stage)
    {
        _phase = Phase.Minting;
        _phaseT = 0f;
        if (_current is null)
        {
            return;
        }
        foreach (var m in _current.Minted)
        {
            if (_vis.TryGetValue(m.Piece.Id, out var v))
            {
                v.Special = m.Special;
                v.Scale = 1.35f;
            }
        }
    }

    private void BeginFalls(GameStage stage)
    {
        _phase = Phase.Falling;
        _phaseT = 0f;
        if (_current is null)
        {
            return;
        }
        foreach (var fall in _current.Falls)
        {
            if (!_vis.TryGetValue(fall.Piece.Id, out var v))
            {
                v = new Vis { Kind = fall.Piece.Kind, Special = fall.Piece.Special, Col = fall.Col, Y = fall.FromRow };
                _vis[fall.Piece.Id] = v;
            }
            v.Col = fall.Col;
            v.TargetRow = fall.ToRow;
            v.Settled = false;
            v.Vy = 0f;
        }
    }

    /// <summary>Gravity with a landing bounce; true once nothing is moving.</summary>
    private bool StepFalls(float dt, bool reduce, GameStage stage)
    {
        var allSettled = true;
        foreach (var v in _vis.Values)
        {
            if (v.Settled)
            {
                v.Land = MathF.Max(0f, v.Land - (dt * 6f));
                continue;
            }
            if (reduce)
            {
                v.Y = v.TargetRow;
                v.Settled = true;
                continue;
            }
            v.Vy = MathF.Min(FallMax, v.Vy + (FallAccel * dt));
            v.Y += v.Vy * dt;
            if (v.Y >= v.TargetRow)
            {
                v.Y = v.TargetRow;
                if (v.Vy > 5f)
                {
                    v.Vy = -v.Vy * 0.22f;
                    v.Land = 1f;
                }
                else
                {
                    v.Vy = 0f;
                    v.Settled = true;
                    continue;
                }
            }
            allSettled = false;
        }
        return allSettled;
    }

    private void AfterSettle(GameStage stage)
    {
        foreach (var v in _vis.Values)
        {
            v.Scale = MathF.Max(1f, v.Scale);
        }
        _phase = Phase.Idle;
        DrainQueue(stage);
    }

    private void BeginSway(GameStage stage)
    {
        _level++;
        _levelPoints = 0;
        _levelTarget = (int)MathF.Round(_levelTarget * GameScoring.LumiLinkLevelGrowth);
        _timeLeft = GameScoring.LumiLinkStartSeconds;
        _frozenLeft = 0f;
        _score += GameScoring.LumiLinkLevelUpBonus;
        _nextTheme = (_theme + 1) % Themes;
        _phase = Phase.Sway;
        _phaseT = 0f;
        _lumiHopVy = -320f;
        stage.Sound(GameSound.LevelUp);
        _texts.Add(new FloatText(Vector2.Zero, string.Format(Loc.T("os.aetherling_lumilink_level_up"), _level), Gold, 1.6f));
    }

    private void BeginPower(GameStage stage, AetherlingElement element)
    {
        _meterPoints = 0;
        _powerElement = element;
        _phase = Phase.PowerFx;
        _phaseT = 0f;
        _selected = null;
        Chord();
        _shake = MathF.Max(_shake, 5f);
        _lumiHopVy = -300f;
    }

    /// <summary>Three crystal dings rising a major third and a fifth, a tenth of a second apart.</summary>
    private void Chord()
    {
        _cues.Add((_clock, GameSound.Chord0));
        _cues.Add((_clock + 0.11f, GameSound.Chord1));
        _cues.Add((_clock + 0.22f, GameSound.Chord2));
    }

    private void FirePower(GameStage stage)
    {
        if (_powerElement is not { } element)
        {
            _phase = Phase.Idle;
            return;
        }
        if (element == AetherlingElement.Ice)
        {
            _frozenLeft = GameScoring.LumiLinkIceFreezeSeconds;
            _phase = Phase.Idle;
            DrainQueue(stage);
            return;
        }
        var step = _board.ApplyPower(element);
        _cascadeIndex = 0;
        _queue.Enqueue(step);
        foreach (var next in _board.Settle())
        {
            _queue.Enqueue(next);
        }
        _phase = Phase.Idle;
        DrainQueue(stage);
    }

    /// <summary>The creature, its elements and the power bar: the top of the screen belongs to the
    /// Aetherling, because the powers are literally what it has eaten.</summary>
    private void DrawStrip(OsAppContext ctx, ImDrawListPtr dl, GameStage stage, Vector2 origin, Vector2 size)
    {
        var stripBr = origin + new Vector2(size.X, StripHeight);
        dl.AddRectFilledMultiColor(origin, stripBr,
            Look.U32(new Vector4(0.08f, 0.06f, 0.14f, 0.85f)), Look.U32(new Vector4(0.08f, 0.06f, 0.14f, 0.85f)),
            Look.U32(new Vector4(0.04f, 0.03f, 0.08f, 0f)), Look.U32(new Vector4(0.04f, 0.03f, 0.08f, 0f)));

        // The creature, bottom-left and clear of the pause chip, hopping on the game's say-so.
        var chip = GamesScreen.CornerReserve;
        var petPx = 124f;
        var footY = origin.Y + StripHeight - 6f;
        var petBottom = new Vector2(origin.X + 14f + (petPx * 0.5f), footY + _lumiHop);
        var pose = stage.Runtime.Pose;
        if (_lumiHop < -2f)
        {
            pose.CellIndex = GameScene.Cell(stage.Manifest, "hop", 0.4f);
            pose.Scale = stage.ReduceMotion ? Vector2.One : new Vector2(0.94f, 1.08f);
        }
        var lift = Math.Clamp(-_lumiHop / 18f, 0f, 1f);
        Look.GroundGlow(dl, new Vector2(petBottom.X, footY + 3f), petPx * (0.62f - (0.12f * lift)), petPx * 0.12f,
            Look.Crystal, 0.32f - (0.12f * lift));
        stage.Runtime.Draw(dl, ctx.Capabilities.Textures, petBottom, petPx, pose, props: false);

        // Elements: six discs under the mute chip, unlocked ones lit; a full meter makes them tappable and pulsing.
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
            var tappable = unlocked && meterFull && _phase == Phase.Idle && !_over;
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
                    ? Loc.T($"os.aetherling_lumilink_power_{Elements[i]}")
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

        // The power bar, the mood capsule's shape with a fill: glass, a gradient, a glowing head.
        var barH = 16f;
        var barW = rowW;
        var barX = left;
        var barY = discY + discR + 12f;
        var radius = barH * 0.5f;
        dl.AddRectFilled(new Vector2(barX, barY), new Vector2(barX + barW, barY + barH),
            Look.U32(new Vector4(1f, 1f, 1f, 0.08f)), radius);
        var fillW = MathF.Max(barH, barW * _meterGlide);
        var fillColour = Look.Crystal;
        var fillHot = new Vector4(0.98f, 0.9f, 0.55f, 1f);
        dl.AddRectFilledMultiColor(new Vector2(barX, barY), new Vector2(barX + fillW, barY + barH),
            Look.U32(fillColour), Look.U32(fillHot), Look.U32(fillHot), Look.U32(fillColour));
        dl.AddRectFilled(new Vector2(barX + radius * 0.3f, barY + (barH * 0.16f)),
            new Vector2(barX + fillW - (radius * 0.3f), barY + (barH * 0.44f)),
            Look.U32(new Vector4(1f, 1f, 1f, 0.18f)), barH * 0.16f);
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

        if (_frozenLeft > 0f)
        {
            var frozen = string.Format(Loc.T("os.aetherling_lumilink_frozen"), MathF.Ceiling(_frozenLeft));
            dl.AddText(new Vector2(barX, barY + barH + 4f), Look.U32(KindColours[2]), frozen);
        }
    }

    private void DrawBoardBack(ImDrawListPtr dl, Vector2 gtl, float gridW, float gridH, float tile)
    {
        var br = gtl + new Vector2(gridW, gridH);
        dl.AddRectFilled(gtl - new Vector2(4f), br + new Vector2(4f), Look.U32(new Vector4(0f, 0f, 0f, 0.35f)), 12f);
        for (var c = 0; c < LumiLinkBoard.Columns; c++)
        {
            for (var r = 0; r < LumiLinkBoard.Rows; r++)
            {
                var a = (c + r) % 2 == 0 ? 0.05f : 0.08f;
                dl.AddRectFilled(gtl + new Vector2(c * tile, r * tile), gtl + new Vector2((c + 1) * tile, (r + 1) * tile),
                    Look.U32(new Vector4(1f, 1f, 1f, a)));
            }
        }
        dl.AddRect(gtl - new Vector2(4f), br + new Vector2(4f), Look.U32(new Vector4(1f, 1f, 1f, 0.18f)), 12f, ImDrawFlags.RoundCornersAll, 1.2f);
    }

    private void DrawPieces(OsAppContext ctx, ImDrawListPtr dl, GameStage stage, Vector2 gtl, float tile)
    {
        var now = ImGui.GetTime();
        var swayP = _phase == Phase.Sway ? Math.Clamp(_phaseT / SwaySeconds, 0f, 1f) : -1f;
        var shuffleP = _phase == Phase.Shuffling ? Math.Clamp(_phaseT / ShuffleSeconds, 0f, 1f) : -1f;
        foreach (var v in _vis.Values)
        {
            var centre = gtl + new Vector2((v.Col + 0.5f) * tile, (v.Y + 0.5f) * tile);
            var scale = v.Scale;
            var alpha = 1f;
            var tilt = 0f;

            var badTint = false;
            if (_swap is { } s && _phase is Phase.Swapping or Phase.SwappingBack)
            {
                float eased;
                if (_phase == Phase.Swapping)
                {
                    eased = Look.EaseInOut(stage.ReduceMotion ? 1f : Math.Clamp(_phaseT / SwapSeconds, 0f, 1f));
                }
                else
                {
                    // Out over the first third, hold red for a beat, then back: a refusal you can read.
                    var p = stage.ReduceMotion ? 1f : Math.Clamp(_phaseT / BadSwapSeconds, 0f, 1f);
                    eased = p < 0.3f ? Look.EaseInOut(p / 0.3f)
                        : p < 0.55f ? 1f
                        : 1f - Look.EaseInOut((p - 0.55f) / 0.45f);
                    badTint = p is >= 0.2f and < 0.8f;
                }
                var isA = (int)v.Col == s.C1 && (int)MathF.Round(v.Y) == s.R1;
                var isB = (int)v.Col == s.C2 && (int)MathF.Round(v.Y) == s.R2;
                if (isA)
                {
                    centre += new Vector2((s.C2 - s.C1) * tile, (s.R2 - s.R1) * tile) * eased;
                    scale *= 1f + (0.12f * MathF.Sin(eased * MathF.PI));
                }
                else if (isB)
                {
                    centre += new Vector2((s.C1 - s.C2) * tile, (s.R1 - s.R2) * tile) * eased;
                    scale *= 1f - (0.08f * MathF.Sin(eased * MathF.PI));
                }
                if (!(isA || isB))
                {
                    badTint = false;
                }
                if (badTint && !stage.ReduceMotion)
                {
                    var wob = MathF.Sin((float)(now * 40.0)) * tile * 0.04f;
                    centre.X += wob;
                }
            }
            if (swayP >= 0f && !stage.ReduceMotion)
            {
                // Out to the right on the first half, in from the left on the second, each tile a beat
                // behind its neighbour so the board rolls like a wave.
                var delay = (v.Col + v.Y) * 0.02f;
                var local = Math.Clamp((swayP - delay) / (1f - 0.34f), 0f, 1f);
                if (local < 0.5f)
                {
                    var t = Look.EaseInOut(local * 2f);
                    centre.X += t * (tile * 9f);
                    tilt = t * 0.6f;
                    alpha = 1f - t;
                }
                else
                {
                    var t = Look.EaseOut((local - 0.5f) * 2f);
                    centre.X -= (1f - t) * (tile * 9f);
                    tilt = -(1f - t) * 0.6f;
                    alpha = t;
                }
            }
            if (shuffleP >= 0f && !stage.ReduceMotion)
            {
                var t = MathF.Sin(shuffleP * MathF.PI);
                tilt = t * MathF.Tau * 0.5f;
                scale *= 1f - (0.4f * t);
            }
            if (_selected is { } sel && sel.C == v.Col && (int)MathF.Round(v.Y) == sel.R && _phase == Phase.Idle)
            {
                scale *= 1.12f + (stage.ReduceMotion ? 0f : 0.04f * MathF.Sin((float)(now * 8.0)));
                Look.Halo(dl, centre, tile * 0.7f, Gold, 0.35f);
            }
            if (_hint is { } h && _phase == Phase.Idle && !stage.ReduceMotion
                && ((h.C1 == v.Col && (int)MathF.Round(v.Y) == h.R1) || (h.C2 == v.Col && (int)MathF.Round(v.Y) == h.R2)))
            {
                var pulse = 0.5f + (0.5f * MathF.Sin((float)(now * 5.0)));
                Look.Halo(dl, centre, tile * 0.65f, Look.CrystalPale, 0.15f + (0.25f * pulse));
                scale *= 1f + (0.06f * pulse);
            }
            if (v.Scale > 1f)
            {
                v.Scale = MathF.Max(1f, v.Scale - (ImGui.GetIO().DeltaTime * 2.2f));
            }

            var landSquash = v.Land > 0f && !stage.ReduceMotion ? new Vector2(1f + (0.18f * v.Land), 1f - (0.18f * v.Land)) : Vector2.One;
            if (badTint)
            {
                var redHalf = new Vector2(tile * 0.46f);
                dl.AddRectFilled(centre - redHalf, centre + redHalf, Look.U32(new Vector4(1f, 0.3f, 0.3f, 0.35f)), tile * 0.2f);
                dl.AddRect(centre - redHalf, centre + redHalf, Look.U32(new Vector4(1f, 0.35f, 0.35f, 0.9f)), tile * 0.2f,
                    ImDrawFlags.RoundCornersAll, 2f);
            }
            DrawPiece(ctx, dl, stage, centre, tile * 0.86f * scale, v.Kind, v.Special, alpha, tilt, landSquash, now);
        }
    }

    private void DrawPiece(OsAppContext ctx, ImDrawListPtr dl, GameStage stage, Vector2 centre, float size,
        int kind, Special special, float alpha, float tilt, Vector2 squash, double now)
        => LumiLinkPieces.Draw(ctx, dl, stage.AssetRoot, _theme, centre, size, kind, special,
            alpha, tilt, squash, now, stage.ReduceMotion);

    /// <summary>Cleared pieces shrinking out, each bursting into shards of its own colour the frame it goes.</summary>
    private void DrawPops(OsAppContext ctx, ImDrawListPtr dl, GameStage stage, Vector2 gtl, float tile)
    {
        var fxBottom = new Vector2(stage.Origin.X + (stage.Size.X * 0.5f), stage.Origin.Y + stage.Size.Y);
        foreach (var pop in _pops)
        {
            var p = stage.ReduceMotion ? 1f : Math.Clamp(pop.Age / PopSeconds, 0f, 1f);
            var centre = gtl + new Vector2((pop.Col + 0.5f) * tile, (pop.Row + 0.5f) * tile);
            if (pop.Age <= 0f || (pop.Age < 0.02f && !stage.ReduceMotion))
            {
                var at = GameScene.FxPoint(centre, fxBottom, stage.Size.Y);
                var colour = KindColours[pop.Kind];
                _fx.BurstRadial(ParticleKind.Shard, at, pop.Special == Special.None ? 6 : 12, colour, 4f, 90f,
                    colorEnd: colour with { W = 0f }, sizeScale: 0.55f);
                if (pop.Special != Special.None)
                {
                    _fx.BurstRadial(ParticleKind.Sparkle, at, 10, Look.Spark, 6f, 140f);
                }
            }
            var scale = 1f + (0.25f * MathF.Sin(p * MathF.PI)) - (p * p);
            if (scale <= 0.02f)
            {
                continue;
            }
            DrawPiece(ctx, dl, stage, centre, tile * 0.86f * MathF.Max(0f, scale), pop.Kind, Special.None,
                1f - p, p * 1.4f, Vector2.One, ImGui.GetTime());
        }
        if (_pendingText is { } text && _pops.Count > 0)
        {
            _pendingText = null;
            var at = gtl + new Vector2((_popCentre.X + 0.5f) * tile, (_popCentre.Y + 0.5f) * tile);
            var colour = text.Cascade >= 3 ? Gold : Look.CrystalPale;
            _texts.Add(new FloatText(at, $"+{text.Points:N0}", colour, 1f));
            if (text.Cascade >= 2)
            {
                _texts.Add(new FloatText(at + new Vector2(0f, -tile * 0.8f),
                    string.Format(Loc.T("os.aetherling_lumilink_combo"), text.Cascade), Gold, 1.25f + (0.08f * text.Cascade)));
            }
        }
    }

    private void DrawFlashes(ImDrawListPtr dl, Vector2 gtl, float tile, float gridW, float gridH)
    {
        foreach (var f in _flashes)
        {
            var p = Math.Clamp(f.Age / 0.35f, 0f, 1f);
            var a = (1f - p) * 0.75f;
            if (f.Horizontal)
            {
                var y = gtl.Y + ((f.Index + 0.5f) * tile);
                dl.AddRectFilled(new Vector2(gtl.X, y - (tile * 0.25f)), new Vector2(gtl.X + gridW, y + (tile * 0.25f)),
                    Look.U32(f.Colour with { W = a }), tile * 0.25f);
            }
            else
            {
                var x = gtl.X + ((f.Index + 0.5f) * tile);
                dl.AddRectFilled(new Vector2(x - (tile * 0.25f), gtl.Y), new Vector2(x + (tile * 0.25f), gtl.Y + gridH),
                    Look.U32(f.Colour with { W = a }), tile * 0.25f);
            }
        }
    }

    private void DrawSwayCurtain(ImDrawListPtr dl, Vector2 gtl, float gridW, float gridH)
    {
        var p = Math.Clamp(_phaseT / SwaySeconds, 0f, 1f);
        var a = MathF.Sin(p * MathF.PI) * 0.35f;
        dl.AddRectFilled(gtl, gtl + new Vector2(gridW, gridH), Look.U32(Gold with { W = a }), 8f);
    }

    private void DrawHud(OsAppContext ctx, ImDrawListPtr dl, Vector2 origin, Vector2 size, Vector2 gtl, float gridW, float gridH)
    {
        var top = gtl.Y + gridH + 10f - _shakeOffset.Y;
        var left = gtl.X - _shakeOffset.X;

        var scoreText = _score.ToString("N0", ctx.Culture);
        using (UiFonts.H3?.Push())
        {
            dl.AddText(new Vector2(left, top), Look.U32(Look.Body), scoreText);
        }
        var seconds = MathF.Ceiling(MathF.Max(0f, _timeLeft));
        var timeText = $"{(int)(seconds / 60)}:{(int)(seconds % 60):00}";
        var urgent = _timeLeft <= 10f && _frozenLeft <= 0f;
        var timeColour = _frozenLeft > 0f ? KindColours[2] : urgent ? new Vector4(1f, 0.4f, 0.35f, 1f) : Look.Body;
        using (UiFonts.H3?.Push())
        {
            var sz = ImGui.CalcTextSize(timeText);
            var pulse = urgent && !ImGui.GetIO().KeyCtrl ? 1f + (0.08f * MathF.Sin((float)(ImGui.GetTime() * 9.0))) : 1f;
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * pulse,
                new Vector2(left + gridW - (sz.X * pulse), top), Look.U32(timeColour), timeText);
        }

        // The level bar: what earns time.
        var barY = top + ImGui.GetTextLineHeight() + 12f;
        var barH = 12f;
        var radius = barH * 0.5f;
        dl.AddRectFilled(new Vector2(left, barY), new Vector2(left + gridW, barY + barH),
            Look.U32(new Vector4(1f, 1f, 1f, 0.08f)), radius);
        var fillW = MathF.Max(barH, gridW * _levelGlide);
        dl.AddRectFilledMultiColor(new Vector2(left, barY), new Vector2(left + fillW, barY + barH),
            Look.U32(Look.Crystal), Look.U32(Gold), Look.U32(Gold), Look.U32(Look.Crystal));
        dl.AddRect(new Vector2(left, barY), new Vector2(left + gridW, barY + barH),
            Look.U32(new Vector4(1f, 1f, 1f, 0.22f)), radius, ImDrawFlags.RoundCornersAll, 1.1f);
        var levelLabel = string.Format(Loc.T("os.aetherling_lumilink_level"), _level);
        var labelSz = ImGui.CalcTextSize(levelLabel) * 0.8f;
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.8f,
            new Vector2(left + (gridW - labelSz.X) * 0.5f, barY + barH + 3f), Look.U32(Look.Whisper), levelLabel);
    }

    private void DrawTexts(ImDrawListPtr dl)
    {
        foreach (var t in _texts)
        {
            var p = Math.Clamp(t.Age / 0.9f, 0f, 1f);
            var at = t.At == Vector2.Zero
                ? ImGui.GetWindowPos() + (ImGui.GetWindowSize() * 0.5f)
                : t.At;
            at.Y -= p * 28f;
            var alpha = p < 0.7f ? 1f : 1f - ((p - 0.7f) / 0.3f);
            var size = ImGui.GetFontSize() * t.Scale * (1f + (0.3f * MathF.Sin(MathF.Min(p * 4f, 1f) * MathF.PI * 0.5f)));
            var sz = ImGui.CalcTextSize(t.Text) * (size / ImGui.GetFontSize());
            dl.AddText(ImGui.GetFont(), size, at - (sz * 0.5f) + new Vector2(1f, 1f), Look.U32(new Vector4(0f, 0f, 0f, 0.6f * alpha)), t.Text);
            dl.AddText(ImGui.GetFont(), size, at - (sz * 0.5f), Look.U32(t.Colour with { W = alpha }), t.Text);
        }
    }

    private Vis? FindVisAt(int col, int row)
    {
        foreach (var v in _vis.Values)
        {
            if (v.Col == col && (int)MathF.Round(v.TargetRow) == row)
            {
                return v;
            }
        }
        return null;
    }

    private int IdOf(Vis vis)
    {
        foreach (var (id, v) in _vis)
        {
            if (ReferenceEquals(v, vis))
            {
                return id;
            }
        }
        return -1;
    }

    /// <summary>After a shuffle the pieces kept their ids and powers but changed kinds; the visuals
    /// re-read the board wholesale rather than tracking the reshuffle.</summary>
    private void SyncVisualsToBoard()
    {
        _vis.Clear();
        for (var c = 0; c < LumiLinkBoard.Columns; c++)
        {
            for (var r = 0; r < LumiLinkBoard.Rows; r++)
            {
                if (_board[c, r] is { } p)
                {
                    _vis[p.Id] = new Vis { Kind = p.Kind, Special = p.Special, Col = c, Y = r, TargetRow = r };
                }
            }
        }
    }
}
