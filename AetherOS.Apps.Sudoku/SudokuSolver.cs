using System;

namespace AetherOS.Apps.Sudoku;

/// <summary>The logical techniques a solve needed, in ascending order. Grading a puzzle means solving it
/// with the smallest ladder that works, which is a far better difficulty measure than counting clues: two
/// grids with 26 givens can be a minute apart in real effort.</summary>
public enum SudokuTechnique
{
    NakedSingle = 0,
    HiddenSingle = 1,
    LockedCandidate = 2,
    NakedPair = 3,
    HiddenPair = 4,
    XWing = 5,
    /// <summary>Nothing in the ladder cracked it, so the grid needs bifurcation.</summary>
    Guess = 6,
}

/// <summary>A human-style constraint solver over a 81-cell grid.
///
/// It exists for two jobs the game genuinely needs and a brute-force solver cannot do: grading a generated
/// puzzle by the hardest technique required, and deciding at any moment whether a given cell was actually
/// deducible, which is what the score's integrity term rests on.
///
/// Candidates are 9-bit masks (bit 0 is the digit 1) so the elimination steps are bit twiddling rather than
/// set allocation; the generator calls this thousands of times per puzzle.</summary>
public static class SudokuSolver
{
    public const int Size = 9;
    public const int Cells = 81;
    private const int AllDigits = 0x1FF;

    /// <summary>Cell indices for each of the 27 houses (9 rows, 9 columns, 9 boxes), built once.</summary>
    private static readonly int[][] Houses = BuildHouses();

    /// <summary>The 20 cells that share a house with each cell.</summary>
    private static readonly int[][] Peers = BuildPeers();

    /// <summary>Row, column and box of every cell, precomputed because the solution counter reads them
    /// millions of times per generated grid and integer division is not free at that rate.</summary>
    private static readonly int[] RowIndex = BuildIndex(c => c / Size);
    private static readonly int[] ColIndex = BuildIndex(c => c % Size);
    private static readonly int[] BoxIndex = BuildIndex(c => ((c / Size / 3) * 3) + (c % Size / 3));

    private static int[] BuildIndex(Func<int, int> of)
    {
        var map = new int[Cells];
        for (var cell = 0; cell < Cells; cell++)
        {
            map[cell] = of(cell);
        }
        return map;
    }

    private static int[][] BuildHouses()
    {
        var houses = new int[27][];
        for (var i = 0; i < Size; i++)
        {
            var row = new int[Size];
            var col = new int[Size];
            var box = new int[Size];
            for (var j = 0; j < Size; j++)
            {
                row[j] = (i * Size) + j;
                col[j] = (j * Size) + i;
                var br = ((i / 3) * 3) + (j / 3);
                var bc = ((i % 3) * 3) + (j % 3);
                box[j] = (br * Size) + bc;
            }
            houses[i] = row;
            houses[Size + i] = col;
            houses[(2 * Size) + i] = box;
        }
        return houses;
    }

    private static int[][] BuildPeers()
    {
        var peers = new int[Cells][];
        for (var cell = 0; cell < Cells; cell++)
        {
            var seen = new bool[Cells];
            foreach (var house in Houses)
            {
                if (Array.IndexOf(house, cell) < 0)
                {
                    continue;
                }
                foreach (var other in house)
                {
                    seen[other] = true;
                }
            }
            seen[cell] = false;

            var list = new int[20];
            var n = 0;
            for (var i = 0; i < Cells; i++)
            {
                if (seen[i])
                {
                    list[n++] = i;
                }
            }
            peers[cell] = list;
        }
        return peers;
    }

    public static int[] PeersOf(int cell) => Peers[cell];

    public static int[][] AllHouses() => Houses;

    public static int RowOf(int cell) => cell / Size;

    public static int ColOf(int cell) => cell % Size;

    public static int BoxOf(int cell) => ((cell / Size / 3) * 3) + (cell % Size / 3);

    /// <summary>Candidate masks for every empty cell, given the digits currently placed. Filled cells get a
    /// mask of 0; callers test emptiness against the grid, not against this.</summary>
    public static int[] Candidates(int[] grid)
    {
        var masks = new int[Cells];
        for (var cell = 0; cell < Cells; cell++)
        {
            if (grid[cell] != 0)
            {
                continue;
            }
            var used = 0;
            foreach (var peer in Peers[cell])
            {
                if (grid[peer] != 0)
                {
                    used |= 1 << (grid[peer] - 1);
                }
            }
            masks[cell] = AllDigits & ~used;
        }
        return masks;
    }

    /// <summary>True when <paramref name="digit"/> in <paramref name="cell"/> breaks no constraint. This is
    /// legality, not correctness: a legal digit can still be the wrong one for the puzzle's solution.</summary>
    public static bool IsLegal(int[] grid, int cell, int digit)
    {
        foreach (var peer in Peers[cell])
        {
            if (grid[peer] == digit)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>Whether this cell was placeable by direct sight when the player filled it: a naked single
    /// (one candidate left) or a hidden single (a candidate with nowhere else to go in some house).
    ///
    /// Singles are the right and only measure for the score's integrity term, at EVERY difficulty. The harder
    /// techniques do not place digits, they remove candidates until a single appears, so honest play at Insane
    /// still commits almost nothing but singles. Filling a cell that still had four candidates means the
    /// answer came from somewhere other than the board, which is exactly what transcribing a solved grid
    /// looks like. It is also cheap enough to run on every keystroke, which the full ladder is not.</summary>
    public static bool WasDeducible(int[] grid, int cell)
    {
        if (grid[cell] != 0)
        {
            return false;
        }

        var masks = Candidates(grid);
        var mask = masks[cell];
        if (mask == 0)
        {
            return false;
        }
        if (BitCount(mask) == 1)
        {
            return true;
        }

        foreach (var house in Houses)
        {
            if (Array.IndexOf(house, cell) < 0)
            {
                continue;
            }
            var elsewhere = 0;
            foreach (var other in house)
            {
                if (other != cell && grid[other] == 0)
                {
                    elsewhere |= masks[other];
                }
            }
            if (BitCount(mask & ~elsewhere) == 1)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Solves as far as the logical ladder allows. Returns the hardest technique used, and whether
    /// the grid came out complete. A puzzle that stalls needs <see cref="SudokuTechnique.Guess"/>.</summary>
    public static (bool Solved, SudokuTechnique Hardest) SolveLogically(int[] grid)
    {
        var work = (int[])grid.Clone();
        // The masks live for the whole solve, not for one step. Rebuilding them per step would discard every
        // elimination the moment it was made, so any grid needing more than singles would find the same
        // elimination forever and never terminate.
        var masks = Candidates(work);
        var hardest = SudokuTechnique.NakedSingle;
        while (Step(work, masks, ref hardest))
        {
        }
        return (IsComplete(work), hardest);
    }

    /// <summary>One pass of the ladder: the cheapest technique that changes something wins, so the recorded
    /// difficulty is the easiest way through rather than the first thing tried.</summary>
    private static bool Step(int[] grid, int[] masks, ref SudokuTechnique hardest)
    {
        if (PlaceSingles(grid, masks, ref hardest))
        {
            return true;
        }
        if (EliminateLockedCandidates(grid, masks, ref hardest))
        {
            return true;
        }
        if (EliminateNakedPairs(grid, masks, ref hardest))
        {
            return true;
        }
        if (EliminateHiddenPairs(grid, masks, ref hardest))
        {
            return true;
        }
        return EliminateXWing(grid, masks, ref hardest);
    }

    private static bool PlaceSingles(int[] grid, int[] masks, ref SudokuTechnique hardest)
    {
        for (var cell = 0; cell < Cells; cell++)
        {
            if (grid[cell] == 0 && BitCount(masks[cell]) == 1)
            {
                Commit(grid, masks, cell, LowestDigit(masks[cell]));
                Raise(ref hardest, SudokuTechnique.NakedSingle);
                return true;
            }
        }

        foreach (var house in Houses)
        {
            for (var digit = 1; digit <= Size; digit++)
            {
                var bit = 1 << (digit - 1);
                var found = -1;
                var count = 0;
                foreach (var cell in house)
                {
                    if (grid[cell] == digit)
                    {
                        count = 0;
                        break;
                    }
                    if (grid[cell] == 0 && (masks[cell] & bit) != 0)
                    {
                        found = cell;
                        count++;
                    }
                }
                if (count == 1)
                {
                    Commit(grid, masks, found, digit);
                    Raise(ref hardest, SudokuTechnique.HiddenSingle);
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>Places a digit and keeps the shared masks true to the grid, which is what lets eliminations
    /// survive from one step to the next.</summary>
    private static void Commit(int[] grid, int[] masks, int cell, int digit)
    {
        grid[cell] = digit;
        masks[cell] = 0;
        var bit = 1 << (digit - 1);
        foreach (var peer in Peers[cell])
        {
            masks[peer] &= ~bit;
        }
    }

    /// <summary>Pointing and claiming: a digit confined to one line within a box (or one box within a line)
    /// can be struck from the rest of that line (or box).</summary>
    private static bool EliminateLockedCandidates(int[] grid, int[] masks, ref SudokuTechnique hardest)
    {
        for (var box = 0; box < Size; box++)
        {
            var cells = Houses[(2 * Size) + box];
            for (var digit = 1; digit <= Size; digit++)
            {
                var bit = 1 << (digit - 1);
                var row = -1;
                var col = -1;
                var any = false;
                foreach (var cell in cells)
                {
                    if (grid[cell] != 0 || (masks[cell] & bit) == 0)
                    {
                        continue;
                    }
                    if (!any)
                    {
                        any = true;
                        row = RowOf(cell);
                        col = ColOf(cell);
                        continue;
                    }
                    if (RowOf(cell) != row)
                    {
                        row = -1;
                    }
                    if (ColOf(cell) != col)
                    {
                        col = -1;
                    }
                }
                if (!any)
                {
                    continue;
                }
                if (row >= 0 && StrikeLine(grid, masks, Houses[row], bit, box))
                {
                    Raise(ref hardest, SudokuTechnique.LockedCandidate);
                    return true;
                }
                if (col >= 0 && StrikeLine(grid, masks, Houses[Size + col], bit, box))
                {
                    Raise(ref hardest, SudokuTechnique.LockedCandidate);
                    return true;
                }
            }
        }
        return false;
    }

    private static bool StrikeLine(int[] grid, int[] masks, int[] line, int bit, int box)
    {
        var changed = false;
        foreach (var cell in line)
        {
            if (grid[cell] == 0 && BoxOf(cell) != box && (masks[cell] & bit) != 0)
            {
                masks[cell] &= ~bit;
                changed = true;
            }
        }
        return changed;
    }

    private static bool EliminateNakedPairs(int[] grid, int[] masks, ref SudokuTechnique hardest)
    {
        foreach (var house in Houses)
        {
            for (var a = 0; a < house.Length; a++)
            {
                var ca = house[a];
                if (grid[ca] != 0 || BitCount(masks[ca]) != 2)
                {
                    continue;
                }
                for (var b = a + 1; b < house.Length; b++)
                {
                    var cb = house[b];
                    if (grid[cb] != 0 || masks[cb] != masks[ca])
                    {
                        continue;
                    }
                    var changed = false;
                    foreach (var cell in house)
                    {
                        if (cell != ca && cell != cb && grid[cell] == 0 && (masks[cell] & masks[ca]) != 0)
                        {
                            masks[cell] &= ~masks[ca];
                            changed = true;
                        }
                    }
                    if (changed)
                    {
                        Raise(ref hardest, SudokuTechnique.NakedPair);
                        return true;
                    }
                }
            }
        }
        return false;
    }

    /// <summary>Two digits that between them can only live in the same two cells of a house own those cells,
    /// so every other candidate there falls away.</summary>
    private static bool EliminateHiddenPairs(int[] grid, int[] masks, ref SudokuTechnique hardest)
    {
        foreach (var house in Houses)
        {
            for (var d1 = 1; d1 <= Size; d1++)
            {
                for (var d2 = d1 + 1; d2 <= Size; d2++)
                {
                    var pair = (1 << (d1 - 1)) | (1 << (d2 - 1));
                    // Both digits must still be unplaced here, or "confined to two cells" is vacuous and the
                    // elimination below would be unsound.
                    var placed = false;
                    foreach (var cell in house)
                    {
                        if (grid[cell] == d1 || grid[cell] == d2)
                        {
                            placed = true;
                            break;
                        }
                    }
                    if (placed)
                    {
                        continue;
                    }

                    var found = 0;
                    foreach (var cell in house)
                    {
                        if (grid[cell] == 0 && (masks[cell] & pair) != 0)
                        {
                            found++;
                        }
                    }
                    if (found != 2)
                    {
                        continue;
                    }

                    var changed = false;
                    foreach (var cell in house)
                    {
                        if (grid[cell] != 0 || (masks[cell] & pair) == 0)
                        {
                            continue;
                        }
                        if ((masks[cell] & ~pair) != 0)
                        {
                            masks[cell] &= pair;
                            changed = true;
                        }
                    }
                    if (changed)
                    {
                        Raise(ref hardest, SudokuTechnique.HiddenPair);
                        return true;
                    }
                }
            }
        }
        return false;
    }

    /// <summary>A digit confined to the same two columns across two rows (or the transpose) forms a rectangle
    /// whose corners must hold it, striking that digit from the rest of those columns.</summary>
    private static bool EliminateXWing(int[] grid, int[] masks, ref SudokuTechnique hardest)
    {
        for (var digit = 1; digit <= Size; digit++)
        {
            var bit = 1 << (digit - 1);
            if (XWingOnLines(grid, masks, bit, rows: true, ref hardest)
                || XWingOnLines(grid, masks, bit, rows: false, ref hardest))
            {
                return true;
            }
        }
        return false;
    }

    private static bool XWingOnLines(int[] grid, int[] masks, int bit, bool rows, ref SudokuTechnique hardest)
    {
        for (var a = 0; a < Size; a++)
        {
            var pa = LinePositions(grid, masks, a, bit, rows);
            if (BitCount(pa) != 2)
            {
                continue;
            }
            for (var b = a + 1; b < Size; b++)
            {
                if (LinePositions(grid, masks, b, bit, rows) != pa)
                {
                    continue;
                }

                var changed = false;
                for (var k = 0; k < Size; k++)
                {
                    if ((pa & (1 << k)) == 0)
                    {
                        continue;
                    }
                    var cross = rows ? Houses[Size + k] : Houses[k];
                    foreach (var cell in cross)
                    {
                        var lineIndex = rows ? RowOf(cell) : ColOf(cell);
                        if (lineIndex == a || lineIndex == b || grid[cell] != 0)
                        {
                            continue;
                        }
                        if ((masks[cell] & bit) != 0)
                        {
                            masks[cell] &= ~bit;
                            changed = true;
                        }
                    }
                }
                if (changed)
                {
                    Raise(ref hardest, SudokuTechnique.XWing);
                    return true;
                }
            }
        }
        return false;
    }

    private static int LinePositions(int[] grid, int[] masks, int index, int bit, bool rows)
    {
        var house = rows ? Houses[index] : Houses[Size + index];
        var positions = 0;
        for (var k = 0; k < Size; k++)
        {
            var cell = house[k];
            if (grid[cell] == 0 && (masks[cell] & bit) != 0)
            {
                positions |= 1 << k;
            }
        }
        return positions;
    }

    /// <summary>Counts solutions, stopping at <paramref name="cap"/>. The generator only ever asks whether
    /// there are two, which is what "this puzzle is still fair" means.
    ///
    /// Digging a grid runs this once per removed clue and a generate runs several digs, so it is the hottest
    /// code in the app by a wide margin. It therefore keeps row, column and box masks incrementally rather
    /// than recomputing candidates at every node, and allocates nothing at all below the initial clone.</summary>
    public static int CountSolutions(int[] grid, int cap = 2)
    {
        Span<int> rows = stackalloc int[Size];
        Span<int> cols = stackalloc int[Size];
        Span<int> boxes = stackalloc int[Size];

        var work = (int[])grid.Clone();
        for (var cell = 0; cell < Cells; cell++)
        {
            if (work[cell] == 0)
            {
                continue;
            }
            var bit = 1 << (work[cell] - 1);
            rows[RowIndex[cell]] |= bit;
            cols[ColIndex[cell]] |= bit;
            boxes[BoxIndex[cell]] |= bit;
        }
        return Count(work, rows, cols, boxes, cap);
    }

    private static int Count(int[] grid, Span<int> rows, Span<int> cols, Span<int> boxes, int cap)
    {
        var best = -1;
        var bestMask = 0;
        var bestCount = Size + 1;

        for (var cell = 0; cell < Cells; cell++)
        {
            if (grid[cell] != 0)
            {
                continue;
            }
            var mask = AllDigits & ~(rows[RowIndex[cell]] | cols[ColIndex[cell]] | boxes[BoxIndex[cell]]);
            var n = BitCount(mask);
            if (n == 0)
            {
                return 0;
            }
            if (n < bestCount)
            {
                bestCount = n;
                bestMask = mask;
                best = cell;
                if (n == 1)
                {
                    break;
                }
            }
        }

        if (best == -1)
        {
            return 1;
        }

        var r = RowIndex[best];
        var c = ColIndex[best];
        var b = BoxIndex[best];
        var found = 0;

        for (var digit = 1; digit <= Size; digit++)
        {
            var bit = 1 << (digit - 1);
            if ((bestMask & bit) == 0)
            {
                continue;
            }

            grid[best] = digit;
            rows[r] |= bit;
            cols[c] |= bit;
            boxes[b] |= bit;

            found += Count(grid, rows, cols, boxes, cap - found);

            grid[best] = 0;
            rows[r] &= ~bit;
            cols[c] &= ~bit;
            boxes[b] &= ~bit;

            if (found >= cap)
            {
                break;
            }
        }
        return found;
    }

    public static bool IsComplete(int[] grid)
    {
        foreach (var value in grid)
        {
            if (value == 0)
            {
                return false;
            }
        }
        return true;
    }

    private static void Raise(ref SudokuTechnique hardest, SudokuTechnique used)
    {
        if (used > hardest)
        {
            hardest = used;
        }
    }

    public static int BitCount(int mask)
    {
        var n = 0;
        while (mask != 0)
        {
            mask &= mask - 1;
            n++;
        }
        return n;
    }

    public static int LowestDigit(int mask)
    {
        for (var digit = 1; digit <= Size; digit++)
        {
            if ((mask & (1 << (digit - 1))) != 0)
            {
                return digit;
            }
        }
        return 0;
    }
}
