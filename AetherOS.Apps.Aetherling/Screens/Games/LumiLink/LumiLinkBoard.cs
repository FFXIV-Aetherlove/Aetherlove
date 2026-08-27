using System;
using System.Collections.Generic;
using AetherLove.Shared.Aetherling;

namespace AetherOS.Apps.Aetherling.Screens.Games.LumiLink;

internal enum Special : byte
{
    None,
    BoltRow,
    BoltColumn,
    Burst,
    TBurst,
    Prism,
}

/// <summary>One tile. The id is stable for the piece's whole life so the game can animate it across
/// falls; the kind is which of the six pictures it wears; the special is what it does when cleared.
/// A Prism has no kind of its own (it takes the kind it is swapped with).</summary>
internal sealed class Piece
{
    private static int _nextId;

    public int Id { get; } = ++_nextId;

    public int Kind { get; set; }

    public Special Special { get; set; }
}

/// <summary>Why cells left the board in one resolve step, so the game can pick a particle and a sound.</summary>
internal enum ClearCause : byte
{
    Match,
    Bolt,
    Burst,
    Prism,
    Power,
}

/// <summary>One cell cleared in a step, where it was and why.</summary>
internal readonly record struct ClearedCell(int Col, int Row, int Kind, ClearCause Cause);

/// <summary>A special minted in a step: the piece that now carries it and where it sits.</summary>
/// <summary>Carries the piece, not its cell: gravity has already run by the time the step is animated.</summary>
internal readonly record struct MintedSpecial(Piece Piece, int Col, int Row, Special Special);

/// <summary>A piece that moved down in the fall, with the row it came from (negative for a piece spawned
/// above the board, counting up from -1) and the row it landed on.</summary>
internal readonly record struct Fall(Piece Piece, int Col, int FromRow, int ToRow);

/// <summary>What one resolve step did, in the order the game should show it: clears first, then specials
/// minted in the gaps, then everything falling into place.</summary>
internal sealed class ResolveStep
{
    public List<ClearedCell> Cleared { get; } = [];

    public List<MintedSpecial> Minted { get; } = [];

    public List<Fall> Falls { get; } = [];

    public int Points { get; set; }

    public bool PrismCombo { get; set; }

    public int LargestGroup { get; set; }
}

/// <summary>The match-3 board with no opinion about pictures, timing or animation. Eight wide, ten
/// tall, six kinds. It validates swaps, finds matches and their shapes (line, L, T, five), mints the
/// specials those earn, detonates specials that get cleared, applies gravity with fresh spawns, and
/// reports every step as data. Deterministic for a given RNG, which is what makes a replay or a test
/// possible. Nothing in here draws or sounds.</summary>
internal sealed class LumiLinkBoard
{
    public const int Columns = 8;
    public const int Rows = 9;
    public const int Kinds = 6;

    private readonly Piece?[,] _cells = new Piece?[Columns, Rows];
    private Random _rng = new();

    public Piece? this[int col, int row] => InBounds(col, row) ? _cells[col, row] : null;

    public static bool InBounds(int col, int row) => col >= 0 && col < Columns && row >= 0 && row < Rows;

    /// <summary>A fresh board with no match on it and at least one legal move.</summary>
    public void Reset(Random rng)
    {
        _rng = rng;
        do
        {
            for (var c = 0; c < Columns; c++)
            {
                for (var r = 0; r < Rows; r++)
                {
                    _cells[c, r] = null;
                }
            }
            for (var c = 0; c < Columns; c++)
            {
                for (var r = 0; r < Rows; r++)
                {
                    _cells[c, r] = new Piece { Kind = KindAvoidingMatch(c, r) };
                }
            }
        }
        while (!HasMove());
    }

    /// <summary>Every special standing on the board right now. A level change reads these off the old board
    /// so what the player earned can be laid onto the new one.</summary>
    public List<Special> Specials()
    {
        var carried = new List<Special>();
        for (var c = 0; c < Columns; c++)
        {
            for (var r = 0; r < Rows; r++)
            {
                if (_cells[c, r] is { Special: not Special.None } piece)
                {
                    carried.Add(piece.Special);
                }
            }
        }
        return carried;
    }

    /// <summary>Lays specials onto plain tiles picked at random, one each. The tile keeps the picture it was
    /// dealt; only what it does when it clears changes. Anything the board has no plain tile left for is
    /// dropped, which cannot happen at the sizes this board runs at.</summary>
    public void Scatter(IReadOnlyList<Special> specials)
    {
        if (specials.Count == 0)
        {
            return;
        }

        var plain = new List<(int Col, int Row)>();
        for (var c = 0; c < Columns; c++)
        {
            for (var r = 0; r < Rows; r++)
            {
                if (_cells[c, r] is { Special: Special.None })
                {
                    plain.Add((c, r));
                }
            }
        }

        foreach (var special in specials)
        {
            if (plain.Count == 0)
            {
                return;
            }
            var pick = _rng.Next(plain.Count);
            var (col, row) = plain[pick];
            plain.RemoveAt(pick);
            _cells[col, row]!.Special = special;
        }
    }

    /// <summary>A kind that makes no line of three with what is already placed to the left and above.</summary>
    private int KindAvoidingMatch(int col, int row)
    {
        var forbidden = 0;
        if (col >= 2 && _cells[col - 1, row] is { } a && _cells[col - 2, row] is { } b && a.Kind == b.Kind)
        {
            forbidden |= 1 << a.Kind;
        }
        if (row >= 2 && _cells[col, row - 1] is { } c && _cells[col, row - 2] is { } d && c.Kind == d.Kind)
        {
            forbidden |= 1 << c.Kind;
        }
        int kind;
        do
        {
            kind = _rng.Next(Kinds);
        }
        while ((forbidden & (1 << kind)) != 0);
        return kind;
    }

    public static bool Adjacent(int c1, int r1, int c2, int r2) =>
        Math.Abs(c1 - c2) + Math.Abs(r1 - r2) == 1;

    /// <summary>Whether swapping these two would do anything: a match, or a special in the pair.</summary>
    public bool SwapIsLegal(int c1, int r1, int c2, int r2)
    {
        if (!InBounds(c1, r1) || !InBounds(c2, r2) || !Adjacent(c1, r1, c2, r2))
        {
            return false;
        }
        var a = _cells[c1, r1];
        var b = _cells[c2, r2];
        if (a is null || b is null)
        {
            return false;
        }
        if (a.Special != Special.None && b.Special != Special.None)
        {
            return true;
        }
        if (a.Special == Special.Prism || b.Special == Special.Prism)
        {
            return true;
        }
        Swap(c1, r1, c2, r2);
        var legal = FindGroups().Count > 0;
        Swap(c1, r1, c2, r2);
        return legal;
    }

    private void Swap(int c1, int r1, int c2, int r2)
    {
        (_cells[c1, r1], _cells[c2, r2]) = (_cells[c2, r2], _cells[c1, r1]);
    }

    /// <summary>Performs a legal swap and resolves everything it sets off, step by step, until the board
    /// is quiet. The first step may be a special-on-special combo or a Prism swap, which clear by rule
    /// rather than by match.</summary>
    public List<ResolveStep> PlaySwap(int c1, int r1, int c2, int r2)
    {
        var steps = new List<ResolveStep>();
        var a = _cells[c1, r1]!;
        var b = _cells[c2, r2]!;
        Swap(c1, r1, c2, r2);

        var first = new ResolveStep();
        var handled = false;
        if (a.Special != Special.None && b.Special != Special.None)
        {
            ComboInto(first, c2, r2, a, c1, r1, b);
            handled = true;
        }
        else if (a.Special == Special.Prism || b.Special == Special.Prism)
        {
            var (prismC, prismR, prism, otherKind) = a.Special == Special.Prism
                ? (c2, r2, a, b.Kind)
                : (c1, r1, b, a.Kind);
            // The Prism's own removal must not fire its fallback (most common kind) on top of the swap.
            prism.Special = Special.None;
            ClearKind(first, otherKind, ClearCause.Prism);
            RemoveAt(first, prismC, prismR, ClearCause.Prism);
            first.Points += GameScoring.LumiLinkPrismPerCell * first.Cleared.Count;
            handled = true;
        }

        if (handled)
        {
            ApplyGravity(first);
            steps.Add(first);
        }
        else
        {
            // The swapped cells are where a minted special wants to land.
            var resolved = ResolveMatches(1, (c2, r2), (c1, r1));
            if (resolved is null)
            {
                Swap(c1, r1, c2, r2);
                return steps;
            }
            steps.Add(resolved);
        }

        var cascade = 2;
        while (ResolveMatches(cascade, null, null) is { } next)
        {
            steps.Add(next);
            cascade++;
        }
        return steps;
    }

    /// <summary>Any cascade still owed after an external clear (a power, a shuffle), resolved to quiet.</summary>
    public List<ResolveStep> Settle()
    {
        var steps = new List<ResolveStep>();
        var cascade = 1;
        while (ResolveMatches(cascade, null, null) is { } next)
        {
            steps.Add(next);
            cascade++;
        }
        return steps;
    }

    private (int C, int R)? _shielded;

    private sealed class Group
    {
        public int Kind;
        public readonly HashSet<(int C, int R)> Cells = [];
        public int LongestRun;
        public bool Horizontal;
        public bool Vertical;
        public (int C, int R)? Intersection;
        public bool IntersectionInterior;
        public readonly List<List<(int C, int R)>> Runs = [];
    }

    /// <summary>Every match on the board as shape-aware groups: runs of three or more, merged where they
    /// share a cell, with the facts a special needs (longest run, both axes present, where they cross).</summary>
    private List<Group> FindGroups()
    {
        var runs = new List<(int Kind, List<(int C, int R)> Cells, bool Horizontal)>();
        for (var r = 0; r < Rows; r++)
        {
            var c = 0;
            while (c < Columns)
            {
                var start = _cells[c, r];
                var end = c + 1;
                while (start is not null && start.Special != Special.Prism && end < Columns
                    && _cells[end, r] is { } n && n.Special != Special.Prism && n.Kind == start.Kind)
                {
                    end++;
                }
                if (start is not null && start.Special != Special.Prism && end - c >= 3)
                {
                    var cells = new List<(int, int)>();
                    for (var x = c; x < end; x++)
                    {
                        cells.Add((x, r));
                    }
                    runs.Add((start.Kind, cells, true));
                }
                c = end;
            }
        }
        for (var c = 0; c < Columns; c++)
        {
            var r = 0;
            while (r < Rows)
            {
                var start = _cells[c, r];
                var end = r + 1;
                while (start is not null && start.Special != Special.Prism && end < Rows
                    && _cells[c, end] is { } n && n.Special != Special.Prism && n.Kind == start.Kind)
                {
                    end++;
                }
                if (start is not null && start.Special != Special.Prism && end - r >= 3)
                {
                    var cells = new List<(int, int)>();
                    for (var y = r; y < end; y++)
                    {
                        cells.Add((c, y));
                    }
                    runs.Add((start.Kind, cells, false));
                }
                r = end;
            }
        }

        var groups = new List<Group>();
        foreach (var run in runs)
        {
            Group? home = null;
            foreach (var g in groups)
            {
                if (g.Kind != run.Kind)
                {
                    continue;
                }
                foreach (var cell in run.Cells)
                {
                    if (g.Cells.Contains(cell))
                    {
                        home = g;
                        // The crossing cell decides L versus T: interior to EITHER run makes a T.
                        g.Intersection = cell;
                        g.IntersectionInterior = Interior(run.Cells, cell);
                        foreach (var other in g.Runs)
                        {
                            g.IntersectionInterior |= Interior(other, cell);
                        }
                        break;
                    }
                }
                if (home is not null)
                {
                    break;
                }
            }
            home ??= new Group { Kind = run.Kind };
            if (!groups.Contains(home))
            {
                groups.Add(home);
            }
            foreach (var cell in run.Cells)
            {
                home.Cells.Add(cell);
            }
            home.Runs.Add(run.Cells);
            home.LongestRun = Math.Max(home.LongestRun, run.Cells.Count);
            if (run.Horizontal)
            {
                home.Horizontal = true;
            }
            else
            {
                home.Vertical = true;
            }
        }
        return groups;
    }

    private static bool Interior(List<(int C, int R)> run, (int C, int R) cell)
    {
        var index = run.IndexOf(cell);
        return index > 0 && index < run.Count - 1;
    }

    /// <summary>Clears every current match, mints the specials they earn, detonates specials caught in the
    /// clearing, scores the step with the cascade multiplier, then drops and refills. Null when there is
    /// nothing to match.</summary>
    private ResolveStep? ResolveMatches(int cascade, (int C, int R)? preferA, (int C, int R)? preferB)
    {
        var groups = FindGroups();
        if (groups.Count == 0)
        {
            return null;
        }
        var step = new ResolveStep();
        var multiplier = Math.Min(cascade, GameScoring.LumiLinkCascadeCap);

        foreach (var g in groups)
        {
            var special = Special.None;
            if (g.LongestRun >= 5)
            {
                special = Special.Prism;
            }
            else if (g.Horizontal && g.Vertical)
            {
                special = g.IntersectionInterior ? Special.TBurst : Special.Burst;
            }
            else if (g.LongestRun == 4)
            {
                special = g.Horizontal ? Special.BoltColumn : Special.BoltRow;
            }

            // The special lands on the crossing of an L or T, else on the swapped cell, else in the
            // middle of the run: where the eye already is.
            (int C, int R)? home = null;
            if (special != Special.None)
            {
                // Only a plain piece can be the keeper: a special caught in the shape detonates instead.
                bool Plain((int C, int R) cell) => _cells[cell.C, cell.R] is { Special: Special.None };
                if (g.Intersection is { } x && Plain(x))
                {
                    home = x;
                }
                else if (preferA is { } pa && g.Cells.Contains(pa) && Plain(pa))
                {
                    home = pa;
                }
                else if (preferB is { } pb && g.Cells.Contains(pb) && Plain(pb))
                {
                    home = pb;
                }
                else
                {
                    var plain = new List<(int C, int R)>();
                    foreach (var cell in g.Cells)
                    {
                        if (Plain(cell))
                        {
                            plain.Add(cell);
                        }
                    }
                    if (plain.Count > 0)
                    {
                        home = plain[plain.Count / 2];
                    }
                }
            }

            step.Points += multiplier * (g.Cells.Count switch
            {
                3 => GameScoring.LumiLinkMatch3,
                4 => GameScoring.LumiLinkMatch4,
                _ => GameScoring.LumiLinkMatch5 + (g.Cells.Count - 5) * GameScoring.LumiLinkPerExtraCell,
            });
            step.LargestGroup = Math.Max(step.LargestGroup, g.Cells.Count);

            // The keeper is shielded while the shape clears, or a Bolt or Burst caught in the same
            // shape takes the tile that was about to become the new special.
            _shielded = home;
            foreach (var (c, r) in g.Cells)
            {
                if (home is { } h && h.C == c && h.R == r)
                {
                    continue;
                }
                RemoveAt(step, c, r, ClearCause.Match);
            }
            _shielded = null;
            if (home is { } mint && _cells[mint.C, mint.R] is { } keeper)
            {
                keeper.Special = special;
                step.Minted.Add(new MintedSpecial(keeper, mint.C, mint.R, special));
            }
        }

        step.Points += multiplier * GameScoring.LumiLinkSpecialPerCell * CountCause(step, ClearCause.Bolt, ClearCause.Burst);
        ApplyGravity(step);
        return step;
    }

    private static int CountCause(ResolveStep step, ClearCause a, ClearCause b)
    {
        var n = 0;
        foreach (var cell in step.Cleared)
        {
            if (cell.Cause == a || cell.Cause == b)
            {
                n++;
            }
        }
        return n;
    }

    /// <summary>Removes a piece; a special removed this way fires, and what it clears may fire in turn.</summary>
    private void RemoveAt(ResolveStep step, int col, int row, ClearCause cause)
    {
        if (!InBounds(col, row) || _cells[col, row] is not { } piece)
        {
            return;
        }
        if (_shielded is { } s && s.C == col && s.R == row)
        {
            return;
        }
        _cells[col, row] = null;
        step.Cleared.Add(new ClearedCell(col, row, piece.Kind, cause));
        switch (piece.Special)
        {
            case Special.BoltRow:
                ClearRow(step, row, ClearCause.Bolt);
                break;
            case Special.BoltColumn:
                ClearColumn(step, col, ClearCause.Bolt);
                break;
            case Special.Burst:
                ClearSquare(step, col, row, 1, ClearCause.Burst);
                break;
            case Special.TBurst:
                ClearRow(step, row, ClearCause.Burst);
                ClearColumn(step, col, ClearCause.Burst);
                break;
            case Special.Prism:
                // A Prism cleared by something else takes its own colour with it: the one the eye sees.
                ClearKind(step, piece.Kind, ClearCause.Prism);
                break;
        }
    }

    private void ClearRow(ResolveStep step, int row, ClearCause cause)
    {
        for (var c = 0; c < Columns; c++)
        {
            RemoveAt(step, c, row, cause);
        }
    }

    private void ClearColumn(ResolveStep step, int col, ClearCause cause)
    {
        for (var r = 0; r < Rows; r++)
        {
            RemoveAt(step, col, r, cause);
        }
    }

    private void ClearSquare(ResolveStep step, int col, int row, int radius, ClearCause cause)
    {
        for (var c = col - radius; c <= col + radius; c++)
        {
            for (var r = row - radius; r <= row + radius; r++)
            {
                RemoveAt(step, c, r, cause);
            }
        }
    }

    private void ClearKind(ResolveStep step, int kind, ClearCause cause)
    {
        for (var c = 0; c < Columns; c++)
        {
            for (var r = 0; r < Rows; r++)
            {
                if (_cells[c, r] is { } p && p.Kind == kind && p.Special != Special.Prism)
                {
                    RemoveAt(step, c, r, cause);
                }
            }
        }
    }

    /// <summary>Special on special: the genre's combo table. Both pieces are consumed.</summary>
    private void ComboInto(ResolveStep step, int ca, int ra, Piece a, int cb, int rb, Piece b)
    {
        var sa = a.Special;
        var sb = b.Special;
        var bothBolt = IsBolt(sa) && IsBolt(sb);
        var bothBurst = IsBurst(sa) && IsBurst(sb);
        var bothPrism = sa == Special.Prism && sb == Special.Prism;

        // Neutralise before clearing so the pair itself does not fire its single effects on top.
        a.Special = Special.None;
        b.Special = Special.None;
        _cells[ca, ra] = null;
        _cells[cb, rb] = null;
        step.Cleared.Add(new ClearedCell(ca, ra, a.Kind, ClearCause.Burst));
        step.Cleared.Add(new ClearedCell(cb, rb, b.Kind, ClearCause.Burst));

        if (bothPrism)
        {
            for (var c = 0; c < Columns; c++)
            {
                for (var r = 0; r < Rows; r++)
                {
                    RemoveAt(step, c, r, ClearCause.Prism);
                }
            }
            step.Points += GameScoring.LumiLinkPrismPrism;
            step.PrismCombo = true;
            return;
        }
        if (sa == Special.Prism || sb == Special.Prism)
        {
            var other = sa == Special.Prism ? sb : sa;
            var kind = sa == Special.Prism ? b.Kind : a.Kind;
            // Every piece of that kind becomes the other special and fires.
            for (var c = 0; c < Columns; c++)
            {
                for (var r = 0; r < Rows; r++)
                {
                    if (_cells[c, r] is { } p && p.Kind == kind && p.Special == Special.None)
                    {
                        p.Special = IsBolt(other) ? ((c + r) % 2 == 0 ? Special.BoltRow : Special.BoltColumn) : other;
                    }
                }
            }
            ClearKind(step, kind, ClearCause.Prism);
            step.Points += GameScoring.LumiLinkPrismPerCell * step.Cleared.Count;
            return;
        }
        if (bothBolt)
        {
            ClearRow(step, ra, ClearCause.Bolt);
            ClearColumn(step, ca, ClearCause.Bolt);
        }
        else if (bothBurst)
        {
            ClearSquare(step, ca, ra, 2, ClearCause.Burst);
        }
        else
        {
            // Bolt with Burst: three rows and three columns through the pair.
            for (var d = -1; d <= 1; d++)
            {
                ClearRow(step, ra + d, ClearCause.Bolt);
                ClearColumn(step, ca + d, ClearCause.Bolt);
            }
        }
        step.Points += GameScoring.LumiLinkSpecialPerCell * step.Cleared.Count;
    }

    private static bool IsBolt(Special s) => s is Special.BoltRow or Special.BoltColumn;

    private static bool IsBurst(Special s) => s is Special.Burst or Special.TBurst;

    /// <summary>The element powers, each a plain clear; the cascade they set off comes from
    /// <see cref="Settle"/>. Returns the clears so the game can animate them before settling.</summary>
    public ResolveStep ApplyPower(AetherlingElement element)
    {
        var step = new ResolveStep();
        switch (element)
        {
            case AetherlingElement.Fire:
            {
                var c0 = _rng.Next(Columns - 3);
                var r0 = _rng.Next(Rows - 3);
                for (var c = c0; c < c0 + 4; c++)
                {
                    for (var r = r0; r < r0 + 4; r++)
                    {
                        RemoveAt(step, c, r, ClearCause.Power);
                    }
                }
                break;
            }
            case AetherlingElement.Water:
                ClearRow(step, 0, ClearCause.Power);
                ClearRow(step, 1, ClearCause.Power);
                break;
            case AetherlingElement.Earth:
                ClearRow(step, Rows - 1, ClearCause.Power);
                ClearRow(step, Rows - 2, ClearCause.Power);
                break;
            case AetherlingElement.Lightning:
            {
                // A bolt from the sky: down one column from the top, then out along the row it strikes.
                var col = _rng.Next(Columns);
                var row = 2 + _rng.Next(Rows - 3);
                for (var r = 0; r < row; r++)
                {
                    RemoveAt(step, col, r, ClearCause.Power);
                }
                ClearRow(step, row, ClearCause.Power);
                break;
            }
            case AetherlingElement.Wind:
            {
                var first = _rng.Next(Rows);
                var second = (first + 1 + _rng.Next(Rows - 1)) % Rows;
                ClearRow(step, first, ClearCause.Power);
                ClearRow(step, second, ClearCause.Power);
                break;
            }
        }
        step.Points += GameScoring.LumiLinkPowerPerCell * step.Cleared.Count;
        ApplyGravity(step);
        return step;
    }

    /// <summary>Drops every piece as far as it goes and spawns new ones above the gaps, recording each
    /// piece's journey so the game can animate the fall.</summary>
    private void ApplyGravity(ResolveStep step)
    {
        for (var c = 0; c < Columns; c++)
        {
            var write = Rows - 1;
            for (var r = Rows - 1; r >= 0; r--)
            {
                if (_cells[c, r] is { } p)
                {
                    if (write != r)
                    {
                        _cells[c, write] = p;
                        _cells[c, r] = null;
                        step.Falls.Add(new Fall(p, c, r, write));
                    }
                    write--;
                }
            }
            var spawnFrom = -1;
            for (var r = write; r >= 0; r--)
            {
                var p = new Piece { Kind = _rng.Next(Kinds) };
                _cells[c, r] = p;
                step.Falls.Add(new Fall(p, c, spawnFrom, r));
                spawnFrom--;
            }
        }
    }

    public bool HasMove() => FindAnyMove() is not null;

    /// <summary>One legal swap, for the idle hint. Null means the board needs a shuffle.</summary>
    public (int C1, int R1, int C2, int R2)? FindAnyMove()
    {
        for (var c = 0; c < Columns; c++)
        {
            for (var r = 0; r < Rows; r++)
            {
                if (c + 1 < Columns && SwapIsLegal(c, r, c + 1, r))
                {
                    return (c, r, c + 1, r);
                }
                if (r + 1 < Rows && SwapIsLegal(c, r, c, r + 1))
                {
                    return (c, r, c, r + 1);
                }
            }
        }
        return null;
    }

    /// <summary>Re-deals every kind (specials keep their powers) until a move exists with no free match.</summary>
    public void Shuffle()
    {
        var guard = 0;
        do
        {
            var kinds = new List<int>();
            for (var c = 0; c < Columns; c++)
            {
                for (var r = 0; r < Rows; r++)
                {
                    if (_cells[c, r] is { } p)
                    {
                        kinds.Add(p.Kind);
                    }
                }
            }
            for (var i = kinds.Count - 1; i > 0; i--)
            {
                var j = _rng.Next(i + 1);
                (kinds[i], kinds[j]) = (kinds[j], kinds[i]);
            }
            var k = 0;
            for (var c = 0; c < Columns; c++)
            {
                for (var r = 0; r < Rows; r++)
                {
                    if (_cells[c, r] is { } p)
                    {
                        p.Kind = kinds[k++];
                    }
                }
            }
            guard++;
        }
        while ((FindGroups().Count > 0 || !HasMove()) && guard < 200);
    }
}
