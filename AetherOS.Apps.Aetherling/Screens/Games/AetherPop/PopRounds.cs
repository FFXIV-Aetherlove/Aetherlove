using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace AetherOS.Apps.Aetherling.Screens.Games.AetherPop;

/// <summary>One authored round. Rows are strings read top to bottom, eleven characters on an even row and
/// ten on an odd one: a digit is a kind, a dot is empty, S a star bubble, R a rainbow bubble and L a
/// letter bubble (the letters themselves are dealt in AETHER order as the round loads).</summary>
internal sealed class PopRoundDto
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Shots between two pushes of the compactor.</summary>
    public int Shots { get; set; } = 8;

    /// <summary>Seconds the time bonus is measured against.</summary>
    public float Par { get; set; } = 60f;

    public string[] Rows { get; set; } = [];
}

/// <summary>The twenty authored rounds, loaded once from the shipped rounds.json. Round 20 is the Final
/// round: its grid is only the opening, because the compactor deals a fresh row every push.</summary>
internal static class PopRounds
{
    public const float CanvasWidth = 1000f;

    /// <summary>The backdrops' own height at this width. The playfield is fit by WIDTH and takes
    /// whatever height the stage has, so this is the art's aspect, not the board's.</summary>
    public const float ReferenceHeight = 1540f;

    /// <summary>Eleven across, ten on the offset rows: Puzzle Bobble's eight read too large on the phone,
    /// so the ball is 30 percent smaller and the board three columns wider (owner, 2026-09-01).</summary>
    public const int Columns = 11;

    public const float BubbleDiameter = 86f;

    public const float BubbleRadius = BubbleDiameter * 0.5f;

    public const float SideMargin = (CanvasWidth - (Columns * BubbleDiameter)) * 0.5f;

    /// <summary>Hex rows nest, so a row is shorter than a bubble is tall.</summary>
    public const float RowHeight = BubbleDiameter * 0.866f;

    /// <summary>Where the compactor bar rests before it has pushed at all, until the game measures the HUD
    /// band above it and sets <c>PopBoard.CeilingRest</c> to sit just under that.</summary>
    public const float DefaultCeilingRestY = 230f;

    /// <summary>The compactor bar's own height above its resting edge.</summary>
    public const float CompactorHeight = 46f;

    /// <summary>The line and the shooter are measured from the BOTTOM of the field, because the field's
    /// height is the stage's: a taller phone gets more rows, never a stretched board.</summary>
    public const float LineFromBottom = 300f;

    public const float ShooterFromBottom = 150f;

    public const int FinalRound = 20;

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    private static IReadOnlyList<PopRoundDto>? _rounds;

    public static IReadOnlyList<PopRoundDto> Load(string assetRoot)
    {
        if (_rounds is not null)
        {
            return _rounds;
        }
        try
        {
            var path = Path.Combine(assetRoot, "games", "aetherpop", "rounds.json");
            var parsed = JsonSerializer.Deserialize<List<PopRoundDto>>(File.ReadAllText(path), Options);
            _rounds = parsed is { Count: > 0 } ? parsed : [];
        }
        catch (Exception)
        {
            _rounds = [];
        }
        return _rounds;
    }

    /// <summary>One more colour every five rounds, whatever the file says; the Final round deals all six.</summary>
    public static int ColourCap(int round) => round >= FinalRound ? 6 : Math.Clamp(3 + ((round - 1) / 5), 3, 6);

    /// <summary>Which quarter of the ladder a round is in, 0..3; picks the backdrop and the music.</summary>
    public static int Chapter(int round) => Math.Min((Math.Max(1, round) - 1) / 5, 3);

    public static int WidthOf(int row) => (row & 1) == 0 ? Columns : Columns - 1;

    /// <summary>A cell's centre in canvas units, with the compactor's pushes folded in.</summary>
    public static System.Numerics.Vector2 CellCentre(int row, int col, int ceilingRows, float ceilingRest)
    {
        var x = SideMargin + BubbleRadius + (col * BubbleDiameter) + ((row & 1) == 0 ? 0f : BubbleRadius);
        var y = ceilingRest + BubbleRadius + ((row + ceilingRows) * RowHeight);
        return new System.Numerics.Vector2(x, y);
    }
}
