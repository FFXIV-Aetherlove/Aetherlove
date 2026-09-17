using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AetherLove.Shared.Aetherling;

namespace AetherOS.Apps.Aetherling.Screens.Games.AetherPop;

internal enum PopSpecial
{
    None,
    Star,
    Rainbow,
    Letter,

    /// <summary>A sleeping hatchling in one of the six colours. Popped with its own colour it wakes and
    /// casts that element's power where it was; dropped, it only falls.</summary>
    Lumi,
}

internal enum PopEventKind
{
    Land,
    Pop,
    Drop,
    Points,
    StarBurst,
    LetterTaken,
    AetherDone,
    Push,
    Lift,
    RoundCleared,
    GameOver,
    Fire,
    Water,
    Bolt,
    Frozen,
    Thawed,
    Dealt,
    LumiWake,
    Frost,
    Plough,
}

/// <summary>One consequence of a shot, for the feel layer to draw and sound. <paramref name="Delay"/> is
/// the board's suggestion for staging: a flame reaches the far end of a row later than the near end.</summary>
internal sealed record PopEvent(
    PopEventKind Kind,
    Vector2 At = default,
    int Colour = -1,
    int Count = 0,
    int Points = 0,
    PopSpecial Special = PopSpecial.None,
    int Letter = -1,
    float Delay = 0f,
    int Row = 0);

internal sealed class PopBubble
{
    /// <summary>0..5, or -1 for a star or rainbow, which have no colour of their own.</summary>
    public int Kind;

    public PopSpecial Special;

    /// <summary>Index into AETHER when <see cref="Special"/> is a letter.</summary>
    public int Letter = -1;

    /// <summary>Shots left before a frozen bubble thaws and falls; zero means it is not frozen.</summary>
    public int FrozenLeft;

    public bool Frozen => FrozenLeft > 0;
}

/// <summary>The headless bubble shooter: a hex grid hanging from a compactor, a shot that sticks, pops
/// and cuts clusters loose, and the rules around it (the line, the push, the rounds, the specials and
/// the element powers). Every consequence comes back as a <see cref="PopEvent"/>; the board never draws
/// and never reads the clock except through <see cref="Update"/>.</summary>
internal sealed class PopBoard
{
    public const string Letters = "AETHER";

    private static readonly int[] EvenDr = [0, 0, -1, -1, 1, 1];
    private static readonly int[] EvenDc = [-1, 1, -1, 0, -1, 0];
    private static readonly int[] OddDr = [0, 0, -1, -1, 1, 1];
    private static readonly int[] OddDc = [-1, 1, 0, 1, 0, 1];

    private readonly List<PopBubble?[]> _rows = [];
    private int _rowBase;
    private Random _rng = new();
    private IReadOnlyList<PopRoundDto> _rounds = [];
    private PopRoundDto? _round;
    private int _shotsSincePush;
    private float _roundSeconds;
    private float _nextRowAt;
    private float _lastRowAt;
    private float _nextBarAt;
    private int _barSteps;
    private readonly List<(int Kind, int Row, int Col, Vector2 At)> _wakes = [];

    public List<PopEvent> Events { get; } = [];

    public int Score { get; private set; }

    public int Round { get; private set; }

    public bool Final => Round >= PopRounds.FinalRound;

    public bool Over { get; private set; }

    public int BiggestDrop { get; private set; }

    /// <summary>How many rows the compactor has pushed the board down.</summary>
    public int CeilingRows { get; private set; }

    public int ShotsPerPush { get; private set; } = 8;

    /// <summary>How many rows the Final round's bar has come down, for the music to climb with. A lift
    /// does not take a step back.</summary>
    public int FinalSteps => _barSteps;

    /// <summary>The compactor's pips: shots until the next push, or in the Final the seconds until the
    /// next row.</summary>
    public int Pips => Final ? (int)GameScoring.PopFinalRowSeconds : ShotsPerPush;

    public int PipsLit => Final
        ? Math.Clamp(Pips - (int)MathF.Ceiling(_nextRowAt - _roundSeconds), 0, Pips)
        : _shotsSincePush;

    public bool[] LettersHeld { get; } = new bool[Letters.Length];

    /// <summary>Where the run ends, in canvas units; the game sets it from the field it was given.</summary>
    public float LineY { get; set; } = 1240f;

    /// <summary>The compactor's resting edge, in canvas units; the game sets it under the HUD band.</summary>
    public float CeilingRest { get; set; } = PopRounds.DefaultCeilingRestY;

    public int TopRow => _rowBase;

    public int BottomRow => _rowBase + _rows.Count - 1;

    public PopRoundDto? RoundData => _round;

    public float ColourCapRound => PopRounds.ColourCap(Round);

    public void Reset(Random rng, IReadOnlyList<PopRoundDto> rounds)
    {
        _rng = rng;
        _rounds = rounds;
        Events.Clear();
        Score = 0;
        Over = false;
        BiggestDrop = 0;
        Array.Fill(LettersHeld, false);
        _wakes.Clear();
        LoadRound(1);
    }

    private void LoadRound(int round)
    {
        Round = round;
        _round = _rounds.FirstOrDefault(r => r.Id == round) ?? new PopRoundDto { Id = round, Rows = ["01201201", "1201201"] };
        _rows.Clear();
        _rowBase = 0;
        CeilingRows = 0;
        _shotsSincePush = 0;
        _roundSeconds = 0f;
        ShotsPerPush = Math.Max(3, _round.Shots);
        _nextRowAt = GameScoring.PopFinalFreeSeconds;
        _lastRowAt = 0f;
        _nextBarAt = GameScoring.PopFinalBarFirstSeconds;
        _barSteps = 0;

        var cap = PopRounds.ColourCap(round);
        var nextLetter = 0;
        for (var r = 0; r < _round.Rows.Length; r++)
        {
            var width = PopRounds.WidthOf(r);
            var cells = new PopBubble?[width];
            var text = _round.Rows[r];
            for (var c = 0; c < width && c < text.Length; c++)
            {
                cells[c] = ParseCell(text[c], cap, ref nextLetter);
            }
            _rows.Add(cells);
        }
        if (_rows.Count == 0)
        {
            _rows.Add(new PopBubble?[PopRounds.Columns]);
        }
    }

    private PopBubble? ParseCell(char ch, int cap, ref int nextLetter)
    {
        switch (ch)
        {
            case '.':
            case ' ':
                return null;
            case 'S':
            case 's':
                return new PopBubble { Kind = -1, Special = PopSpecial.Star };
            case 'R':
            case 'r':
                return new PopBubble { Kind = -1, Special = PopSpecial.Rainbow };
            case 'B':
            case 'b':
                return new PopBubble { Kind = _rng.Next(cap), Special = PopSpecial.Lumi };
            case 'L':
            case 'l':
            {
                var letter = nextLetter % Letters.Length;
                nextLetter++;
                return new PopBubble { Kind = _rng.Next(cap), Special = PopSpecial.Letter, Letter = letter };
            }
            default:
                if (ch >= '0' && ch <= '9')
                {
                    return new PopBubble { Kind = (ch - '0') % cap };
                }
                return null;
        }
    }

    public PopBubble? At(int row, int col)
    {
        var i = row - _rowBase;
        if (i < 0 || i >= _rows.Count || col < 0 || col >= _rows[i].Length)
        {
            return null;
        }
        return _rows[i][col];
    }

    private void Set(int row, int col, PopBubble? bubble)
    {
        while (row < _rowBase)
        {
            _rows.Insert(0, new PopBubble?[PopRounds.WidthOf(_rowBase - 1)]);
            _rowBase--;
        }
        while (row - _rowBase >= _rows.Count)
        {
            _rows.Add(new PopBubble?[PopRounds.WidthOf(_rowBase + _rows.Count)]);
        }
        _rows[row - _rowBase][col] = bubble;
    }

    public IEnumerable<(int Row, int Col, PopBubble Bubble)> All()
    {
        for (var i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            for (var c = 0; c < row.Length; c++)
            {
                if (row[c] is { } b)
                {
                    yield return (_rowBase + i, c, b);
                }
            }
        }
    }

    public int Count()
    {
        var n = 0;
        foreach (var _ in All())
        {
            n++;
        }
        return n;
    }

    public Vector2 Centre(int row, int col) => PopRounds.CellCentre(row, col, CeilingRows, CeilingRest);

    /// <summary>The compactor bar's lower edge in canvas units.</summary>
    public float CeilingY => CeilingRest + ((CeilingRows + _rowBase) * PopRounds.RowHeight);

    private IEnumerable<(int Row, int Col)> Neighbours(int row, int col)
    {
        var even = (row & 1) == 0;
        for (var i = 0; i < 6; i++)
        {
            var r = row + (even ? EvenDr[i] : OddDr[i]);
            var c = col + (even ? EvenDc[i] : OddDc[i]);
            if (c >= 0 && c < PopRounds.WidthOf(r))
            {
                yield return (r, c);
            }
        }
    }

    /// <summary>The bubble a flying shot touches first, or null. A shot at the compactor lands whether or
    /// not it touched anything.</summary>
    public bool Touches(Vector2 pos, out int row, out int col)
    {
        row = 0;
        col = 0;
        var best = float.MaxValue;
        var reach = PopRounds.BubbleDiameter * 0.88f;
        foreach (var (r, c, _) in All())
        {
            var d = Vector2.Distance(Centre(r, c), pos);
            if (d < reach && d < best)
            {
                best = d;
                row = r;
                col = c;
            }
        }
        return best < float.MaxValue;
    }

    public bool AtCeiling(Vector2 pos) => pos.Y - PopRounds.BubbleRadius <= CeilingY;

    /// <summary>The empty cell a shot at <paramref name="pos"/> settles into: the cell under the point
    /// when it is free, else the free neighbour of the touched bubble nearest the point.</summary>
    public (int Row, int Col) SnapCell(Vector2 pos, bool touched, int touchedRow, int touchedCol)
    {
        var row = (int)MathF.Round((pos.Y - CeilingRest - PopRounds.BubbleRadius) / PopRounds.RowHeight) - CeilingRows;
        row = Math.Max(row, _rowBase);
        var col = ColFor(row, pos.X);
        if (At(row, col) is null && (!touched || IsNeighbour(row, col, touchedRow, touchedCol)))
        {
            return (row, col);
        }
        if (!touched)
        {
            return (row, col);
        }
        var best = (Row: touchedRow + 1, Col: touchedCol);
        var bestD = float.MaxValue;
        foreach (var (r, c) in Neighbours(touchedRow, touchedCol))
        {
            if (r < _rowBase || At(r, c) is not null)
            {
                continue;
            }
            var d = Vector2.DistanceSquared(Centre(r, c), pos);
            if (d < bestD)
            {
                bestD = d;
                best = (r, c);
            }
        }
        if (bestD < float.MaxValue)
        {
            return best;
        }
        // Boxed in on every side: the nearest free cell anywhere below the compactor.
        for (var r = _rowBase; r <= BottomRow + 1; r++)
        {
            for (var c = 0; c < PopRounds.WidthOf(r); c++)
            {
                if (At(r, c) is not null)
                {
                    continue;
                }
                var d = Vector2.DistanceSquared(Centre(r, c), pos);
                if (d < bestD)
                {
                    bestD = d;
                    best = (r, c);
                }
            }
        }
        return best;
    }

    private bool IsNeighbour(int row, int col, int otherRow, int otherCol)
    {
        foreach (var (r, c) in Neighbours(otherRow, otherCol))
        {
            if (r == row && c == col)
            {
                return true;
            }
        }
        return false;
    }

    private static int ColFor(int row, float x)
    {
        var offset = (row & 1) == 0 ? 0f : PopRounds.BubbleRadius;
        var col = (int)MathF.Round((x - PopRounds.SideMargin - PopRounds.BubbleRadius - offset) / PopRounds.BubbleDiameter);
        return Math.Clamp(col, 0, PopRounds.WidthOf(row) - 1);
    }

    /// <summary>A kind the mouth may offer: one still on the board, or anything under the cap once the
    /// board is nearly bare, so the last few shots are never impossible colours.</summary>
    public int RollShotKind()
    {
        var present = new HashSet<int>();
        foreach (var (_, _, b) in All())
        {
            if (b.Kind >= 0 && !b.Frozen)
            {
                present.Add(b.Kind);
            }
        }
        if (present.Count == 0)
        {
            return _rng.Next(PopRounds.ColourCap(Round));
        }
        var list = present.ToList();
        return list[_rng.Next(list.Count)];
    }

    public void Update(float dt)
    {
        if (Over)
        {
            return;
        }
        _roundSeconds += dt;
        if (!Final || _roundSeconds < GameScoring.PopFinalFreeSeconds)
        {
            return;
        }
        var moved = false;
        var thin = Count() < GameScoring.PopFinalThinBubbles
            && _roundSeconds - _lastRowAt >= GameScoring.PopFinalRefillSeconds;
        if (_roundSeconds >= _nextRowAt || thin)
        {
            Events.Add(new PopEvent(PopEventKind.Push, Count: 1));
            DealRows(1);
            moved = true;
        }
        if (_roundSeconds >= _nextBarAt)
        {
            _barSteps++;
            _nextBarAt += BarGap(_barSteps);
            Push();
            moved = true;
        }
        if (moved)
        {
            CheckLine();
        }
    }

    /// <summary>Seconds from the Final bar's step <paramref name="step"/> to the next: long at first, then
    /// shorter every step down to the floor. Lifts do not rewind it.</summary>
    private static float BarGap(int step) => MathF.Max(GameScoring.PopFinalBarGapFloor,
        GameScoring.PopFinalBarGapStart - (GameScoring.PopFinalBarGapCut * (step - 1)));

    /// <summary>A shot has stopped at <paramref name="pos"/>: it settles, pops, cuts loose, and the run
    /// takes its consequences. <paramref name="element"/> is the armed power riding it, if any; Earth
    /// never lands (see <see cref="MetalPass"/>).</summary>
    public void Land(Vector2 pos, int kind, AetherlingElement element)
    {
        if (Over)
        {
            return;
        }
        var touched = Touches(pos, out var tr, out var tc);
        var (row, col) = SnapCell(pos, touched, tr, tc);
        var bubble = new PopBubble { Kind = kind };
        Set(row, col, bubble);
        var at = Centre(row, col);
        Events.Add(new PopEvent(PopEventKind.Land, at, kind));

        var popped = new List<(int Row, int Col)>();
        var specialsTaken = 0;
        if (element != AetherlingElement.Ice)
        {
            Match(row, col, kind, at, popped, ref specialsTaken);
        }
        switch (element)
        {
            case AetherlingElement.Fire:
                BurnBottomRow(at, popped);
                break;
            case AetherlingElement.Water:
                Flood(row, col, at, popped);
                break;
            case AetherlingElement.Lightning:
                Strike(row, at, popped);
                break;
            case AetherlingElement.Ice:
                Freeze(row, col, at);
                break;
            case AetherlingElement.Wind:
                Lift(GameScoring.PopWindLiftRows);
                break;
        }

        var points = 0;
        if (popped.Count > 0)
        {
            points += RemovePopped(popped, at, ref specialsTaken);
            points += ResolveWakes(ref specialsTaken);
        }
        points += DropLoose(at);
        if (points > 0)
        {
            Award(points, at);
        }

        AfterShot();
    }

    /// <summary>The metal bubble: it never sticks, so the feel layer walks it up the board and calls this
    /// for every bubble it passes through. Pops pay as pops; what they held up falls when it leaves.</summary>
    public int MetalPass(Vector2 pos)
    {
        if (Over)
        {
            return 0;
        }
        var popped = new List<(int Row, int Col)>();
        var reach = PopRounds.BubbleDiameter * 0.8f;
        foreach (var (r, c, _) in All())
        {
            if (Vector2.Distance(Centre(r, c), pos) < reach)
            {
                popped.Add((r, c));
            }
        }
        if (popped.Count == 0)
        {
            return 0;
        }
        var specials = 0;
        var points = RemovePopped(popped, pos, ref specials);
        points += ResolveWakes(ref specials);
        Score += points;
        return points;
    }

    /// <summary>The metal bubble has left the board: settle what it cut loose and count the shot.</summary>
    public void MetalDone(Vector2 lastAt)
    {
        if (Over)
        {
            return;
        }
        var points = DropLoose(lastAt);
        if (points > 0)
        {
            Award(points, lastAt);
        }
        AfterShot();
    }

    private void AfterShot()
    {
        ThawTick();
        if (!Final)
        {
            _shotsSincePush++;
            if (_shotsSincePush >= ShotsPerPush)
            {
                Push();
            }
        }
        if (CheckLine())
        {
            return;
        }
        if (Count() == 0)
        {
            if (Final)
            {
                DealRows(2);
                return;
            }
            ClearRound();
        }
    }

    /// <summary>The bar comes down a row and takes the board with it.</summary>
    private void Push()
    {
        CeilingRows++;
        _shotsSincePush = 0;
        Events.Add(new PopEvent(PopEventKind.Push, Count: 1));
    }

    private void DealRows(int rows)
    {
        _lastRowAt = _roundSeconds;
        _nextRowAt = _roundSeconds + GameScoring.PopFinalRowSeconds;
        Deal(rows);
    }

    /// <summary>Fresh rows under the bar in the Final round. Each row pushes the board down one, so the bar
    /// stays where it is. A dealt row is nudged under the no-free-match rule: a bubble never lands beside
    /// two of its own colour in the same row.</summary>
    private void Deal(int rows)
    {
        for (var n = 0; n < rows; n++)
        {
            CeilingRows++;
            var row = _rowBase - 1;
            var width = PopRounds.WidthOf(row);
            var cells = new PopBubble?[width];
            for (var c = 0; c < width; c++)
            {
                var roll = _rng.NextDouble();
                if (roll < 0.03)
                {
                    cells[c] = new PopBubble { Kind = -1, Special = PopSpecial.Star };
                    continue;
                }
                if (roll < 0.08)
                {
                    cells[c] = new PopBubble { Kind = -1, Special = PopSpecial.Rainbow };
                    continue;
                }
                if (roll < 0.11)
                {
                    cells[c] = new PopBubble { Kind = _rng.Next(6), Special = PopSpecial.Lumi };
                    continue;
                }
                var kind = _rng.Next(6);
                if (c >= 2 && cells[c - 1]?.Kind == kind && cells[c - 2]?.Kind == kind)
                {
                    kind = (kind + 1 + _rng.Next(5)) % 6;
                }
                var bubble = new PopBubble { Kind = kind };
                if (roll > 0.95 && NextLetter() is { } letter)
                {
                    bubble.Special = PopSpecial.Letter;
                    bubble.Letter = letter;
                }
                cells[c] = bubble;
            }
            _rows.Insert(0, cells);
            _rowBase--;
        }
        Events.Add(new PopEvent(PopEventKind.Dealt, Count: rows));
    }

    private int? NextLetter()
    {
        var missing = new List<int>();
        for (var i = 0; i < Letters.Length; i++)
        {
            if (!LettersHeld[i])
            {
                missing.Add(i);
            }
        }
        foreach (var (_, _, b) in All())
        {
            if (b.Special == PopSpecial.Letter)
            {
                missing.Remove(b.Letter);
            }
        }
        return missing.Count == 0 ? null : missing[_rng.Next(missing.Count)];
    }

    private bool CheckLine()
    {
        foreach (var (r, c, _) in All())
        {
            if (Centre(r, c).Y + PopRounds.BubbleRadius > LineY)
            {
                Over = true;
                Events.Add(new PopEvent(PopEventKind.GameOver, Centre(r, c)));
                return true;
            }
        }
        return false;
    }

    private void ClearRound()
    {
        var under = MathF.Max(0f, (_round?.Par ?? 60f) - _roundSeconds);
        var bonus = GameScoring.PopRoundClearBonus + (int)(under * GameScoring.PopTimeBonusPerSecond);
        Score += bonus;
        var cleared = Round;
        Events.Add(new PopEvent(PopEventKind.RoundCleared, Count: cleared, Points: bonus));
        LoadRound(Math.Min(PopRounds.FinalRound, Round + 1));
    }

    private void Award(int points, Vector2 at)
    {
        Score += points;
        Events.Add(new PopEvent(PopEventKind.Points, at, Points: points));
    }

    /// <summary>Three or more of a colour touching, rainbows counting as whichever colour reached them;
    /// a star beside the landing takes every bubble of the shot's colour on the board with it.</summary>
    private void Match(int row, int col, int kind, Vector2 at, List<(int Row, int Col)> popped, ref int specials)
    {
        var starHit = false;
        foreach (var (r, c) in Neighbours(row, col))
        {
            if (At(r, c) is { Special: PopSpecial.Star, Frozen: false })
            {
                starHit = true;
                popped.Add((r, c));
                Events.Add(new PopEvent(PopEventKind.StarBurst, Centre(r, c), kind));
            }
        }
        if (starHit)
        {
            foreach (var (r, c, b) in All())
            {
                if (b.Kind == kind && !b.Frozen && !popped.Contains((r, c)))
                {
                    popped.Add((r, c));
                }
            }
            return;
        }

        var group = new List<(int Row, int Col)>();
        var seen = new HashSet<(int, int)>();
        var stack = new Stack<(int Row, int Col)>();
        stack.Push((row, col));
        seen.Add((row, col));
        while (stack.Count > 0)
        {
            var (r, c) = stack.Pop();
            group.Add((r, c));
            foreach (var (nr, nc) in Neighbours(r, c))
            {
                if (seen.Contains((nr, nc)) || At(nr, nc) is not { } b || b.Frozen)
                {
                    continue;
                }
                if (b.Kind == kind || b.Special == PopSpecial.Rainbow)
                {
                    seen.Add((nr, nc));
                    stack.Push((nr, nc));
                }
            }
        }
        if (group.Count >= 3)
        {
            popped.AddRange(group);
        }
    }

    private void BurnBottomRow(Vector2 at, List<(int Row, int Col)> popped)
    {
        var bottom = int.MinValue;
        foreach (var (r, _, _) in All())
        {
            bottom = Math.Max(bottom, r);
        }
        if (bottom == int.MinValue)
        {
            return;
        }
        var rowY = Centre(bottom, 0).Y;
        Events.Add(new PopEvent(PopEventKind.Fire, new Vector2(at.X, rowY), Row: bottom));
        for (var c = 0; c < PopRounds.WidthOf(bottom); c++)
        {
            if (At(bottom, c) is not null)
            {
                popped.Add((bottom, c));
            }
        }
    }

    private void Flood(int row, int col, Vector2 at, List<(int Row, int Col)> popped)
    {
        Events.Add(new PopEvent(PopEventKind.Water, at));
        var reach = PopRounds.BubbleDiameter * 0.55f;
        foreach (var (r, c, _) in All())
        {
            if (r >= row && MathF.Abs(Centre(r, c).X - at.X) <= reach)
            {
                popped.Add((r, c));
            }
        }
    }

    private void Strike(int row, Vector2 at, List<(int Row, int Col)> popped)
    {
        Events.Add(new PopEvent(PopEventKind.Bolt, at, Row: row));
        for (var c = 0; c < PopRounds.WidthOf(row); c++)
        {
            if (At(row, c) is not null)
            {
                popped.Add((row, c));
            }
        }
    }

    /// <summary>The frozen bubble: the landing and everything touching it freeze solid for three shots,
    /// matching nothing and holding everything, then thaw and fall as a drop.</summary>
    private void Freeze(int row, int col, Vector2 at)
    {
        var count = 0;
        if (At(row, col) is { } self)
        {
            self.FrozenLeft = GameScoring.PopFreezeShots;
            count++;
        }
        foreach (var (r, c) in Neighbours(row, col))
        {
            if (At(r, c) is { } b)
            {
                b.FrozenLeft = GameScoring.PopFreezeShots;
                count++;
            }
        }
        Events.Add(new PopEvent(PopEventKind.Frozen, at, Count: count));
    }

    private void ThawTick()
    {
        var thawed = new List<(int Row, int Col)>();
        foreach (var (r, c, b) in All())
        {
            if (!b.Frozen)
            {
                continue;
            }
            b.FrozenLeft--;
            if (!b.Frozen)
            {
                thawed.Add((r, c));
            }
        }
        if (thawed.Count == 0)
        {
            return;
        }
        var at = Centre(thawed[0].Row, thawed[0].Col);
        Events.Add(new PopEvent(PopEventKind.Thawed, at, Count: thawed.Count));
        var points = Fall(thawed, at);
        points += DropLoose(at);
        if (points > 0)
        {
            Award(points, at);
        }
    }

    public void Lift(int rows)
    {
        var before = CeilingRows;
        // The Final's dealt rows sit above row 0, so the bar rests at -_rowBase there, not at 0.
        CeilingRows = Math.Max(-_rowBase, CeilingRows - rows);
        if (CeilingRows != before)
        {
            Events.Add(new PopEvent(PopEventKind.Lift, Count: before - CeilingRows));
        }
    }

    private int RemovePopped(List<(int Row, int Col)> popped, Vector2 origin, ref int specials)
    {
        var points = 0;
        var seen = new HashSet<(int, int)>();
        foreach (var (r, c) in popped)
        {
            if (!seen.Add((r, c)) || At(r, c) is not { } b)
            {
                continue;
            }
            var at = Centre(r, c);
            Set(r, c, null);
            points += GameScoring.PopPerBubble;
            if (b.Special is PopSpecial.Star or PopSpecial.Rainbow)
            {
                points += GameScoring.PopSpecialTaken;
                specials++;
            }
            TakeLetter(b, at);
            if (b.Special == PopSpecial.Lumi && b.Kind >= 0)
            {
                _wakes.Add((b.Kind, r, c, at));
            }
            Events.Add(new PopEvent(PopEventKind.Pop, at, b.Kind, Special: b.Special,
                Delay: Vector2.Distance(origin, at) / 2200f));
        }
        return points;
    }

    /// <summary>Every sleeping Lumi popped by the last removal wakes and casts its colour's element where
    /// it was. A cast can pop another sleeper, so this loops until nothing is left to wake.</summary>
    private int ResolveWakes(ref int specials)
    {
        var points = 0;
        while (_wakes.Count > 0)
        {
            var wakes = new List<(int Kind, int Row, int Col, Vector2 At)>(_wakes);
            _wakes.Clear();
            foreach (var (kind, row, col, at) in wakes)
            {
                Events.Add(new PopEvent(PopEventKind.LumiWake, at, kind));
                var popped = new List<(int Row, int Col)>();
                switch (kind)
                {
                    case 0:
                        BurnBottomRow(at, popped);
                        break;
                    case 1:
                        Flood(row, col, at, popped);
                        break;
                    case 2:
                        FrostBurst(row, col, at, popped);
                        break;
                    case 3:
                        Lift(GameScoring.PopWindLiftRows);
                        break;
                    case 4:
                        Strike(row, at, popped);
                        break;
                    default:
                        Plough(row, col, at, popped);
                        break;
                }
                if (popped.Count > 0)
                {
                    points += RemovePopped(popped, at, ref specials);
                }
            }
        }
        return points;
    }

    /// <summary>The ice sleeper's cast: everything within two rings shatters.</summary>
    private void FrostBurst(int row, int col, Vector2 at, List<(int Row, int Col)> popped)
    {
        Events.Add(new PopEvent(PopEventKind.Frost, at));
        var reach = PopRounds.BubbleDiameter * 2.1f;
        foreach (var (r, c, _) in All())
        {
            if (Vector2.Distance(Centre(r, c), at) <= reach)
            {
                popped.Add((r, c));
            }
        }
    }

    /// <summary>The earth sleeper's cast: a metal plough straight up its column to the compactor.</summary>
    private void Plough(int row, int col, Vector2 at, List<(int Row, int Col)> popped)
    {
        Events.Add(new PopEvent(PopEventKind.Plough, at));
        var reach = PopRounds.BubbleDiameter * 0.55f;
        foreach (var (r, c, _) in All())
        {
            if (r <= row && MathF.Abs(Centre(r, c).X - at.X) <= reach)
            {
                popped.Add((r, c));
            }
        }
    }

    private void TakeLetter(PopBubble b, Vector2 at)
    {
        if (b.Special != PopSpecial.Letter || b.Letter < 0 || b.Letter >= Letters.Length)
        {
            return;
        }
        LettersHeld[b.Letter] = true;
        Events.Add(new PopEvent(PopEventKind.LetterTaken, at, b.Kind, Letter: b.Letter));
        if (LettersHeld.All(x => x))
        {
            Array.Fill(LettersHeld, false);
            Score += GameScoring.PopAetherBonus;
            Events.Add(new PopEvent(PopEventKind.AetherDone, at, Points: GameScoring.PopAetherBonus));
            Lift(GameScoring.PopAetherLiftRows);
        }
    }

    /// <summary>Everything without a path up to the compactor falls. Frozen bubbles hold, so an iced
    /// cluster keeps hanging until it thaws.</summary>
    private int DropLoose(Vector2 near)
    {
        var held = new HashSet<(int, int)>();
        var stack = new Stack<(int Row, int Col)>();
        for (var c = 0; c < PopRounds.WidthOf(_rowBase); c++)
        {
            if (At(_rowBase, c) is not null)
            {
                held.Add((_rowBase, c));
                stack.Push((_rowBase, c));
            }
        }
        while (stack.Count > 0)
        {
            var (r, c) = stack.Pop();
            foreach (var (nr, nc) in Neighbours(r, c))
            {
                if (At(nr, nc) is not null && held.Add((nr, nc)))
                {
                    stack.Push((nr, nc));
                }
            }
        }
        var loose = new List<(int Row, int Col)>();
        foreach (var (r, c, _) in All())
        {
            if (!held.Contains((r, c)))
            {
                loose.Add((r, c));
            }
        }
        return loose.Count == 0 ? 0 : Fall(loose, near);
    }

    private int Fall(List<(int Row, int Col)> cells, Vector2 near)
    {
        var n = 0;
        var specials = 0;
        foreach (var (r, c) in cells)
        {
            if (At(r, c) is not { } b)
            {
                continue;
            }
            var at = Centre(r, c);
            Set(r, c, null);
            n++;
            if (b.Special is PopSpecial.Star or PopSpecial.Rainbow)
            {
                specials++;
            }
            TakeLetter(b, at);
            Events.Add(new PopEvent(PopEventKind.Drop, at, b.Kind, Special: b.Special, Delay: Vector2.Distance(near, at) / 3000f));
        }
        if (n == 0)
        {
            return 0;
        }
        BiggestDrop = Math.Max(BiggestDrop, n);
        var points = DropPoints(n) + (specials * GameScoring.PopSpecialTaken);
        Events.Add(new PopEvent(PopEventKind.Points, near, Count: n, Points: points));
        return points;
    }

    /// <summary>Puzzle Bobble's own curve: 20 for one, doubling per bubble, held at the cap.</summary>
    public static int DropPoints(int n)
    {
        if (n <= 0)
        {
            return 0;
        }
        var points = (long)GameScoring.PopDropBase << Math.Min(n - 1, 20);
        return (int)Math.Min(points, GameScoring.PopDropCap);
    }

    /// <summary>How close the lowest bubble is to the line, 0 far and 1 touching; drives the warning glow.</summary>
    public float Danger()
    {
        var lowest = float.MinValue;
        foreach (var (r, c, _) in All())
        {
            lowest = MathF.Max(lowest, Centre(r, c).Y + PopRounds.BubbleRadius);
        }
        if (lowest == float.MinValue)
        {
            return 0f;
        }
        var gap = LineY - lowest;
        return Math.Clamp(1f - (gap / (PopRounds.RowHeight * 2.5f)), 0f, 1f);
    }
}
