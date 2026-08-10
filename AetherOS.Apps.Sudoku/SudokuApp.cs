using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AetherLove.Shared.Arcade;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Sudoku;

/// <summary>Sudoku on the handheld LCD, as a climbing ladder rather than a difficulty menu: every grid you
/// clear hands you a harder one, until Insane, which it stays on. Three strikes and a clock per grid end
/// the run.
///
/// Unlike the other cabinets this one never polls the keyboard. Doing so takes the keys away from the game
/// for as long as the app reads them, which is a fair trade for a ninety-second Snake run and a bad one for
/// a grid somebody may sit on for ten minutes with chat open. It is driven entirely by the mouse.</summary>
public sealed class SudokuApp : IAetherApp
{
    private const string HighScoresKey = "high_scores";
    private const int ScoreSlots = 5;

    private static readonly Vector4 TileTopColor = new(0.36f, 0.55f, 0.62f, 1f);
    private static readonly Vector4 TileBottomColor = new(0.11f, 0.20f, 0.26f, 1f);

    private enum View
    {
        Splash,
        Playing,
        Cleared,
        GameOver,
        Scores,
        Leaderboard,
    }

    private readonly Func<string> name;
    private readonly IAppStorage storage;
    private readonly AetherLove.Os.IArcadeRewards rewards;
    private readonly AetherLove.Os.IArcadeScores scores;
    private readonly AetherLove.Widgets.ArcadeLeaderboardView leaderboard;
    private readonly SudokuGame game = new();

    private View view = View.Splash;
    private bool paused;
    private double lastFrameTime;
    private double splashStartedAt;
    private int[] highScores = [];
    private bool scoresLoaded;
    private int selected = -1;
    private bool pencil;
    private int lastRunScore;
    private bool lastRunWasRecord;

    public SudokuApp(Func<string> name, IAppCapabilities capabilities, AetherLove.Os.IArcadeRewards rewards,
        AetherLove.Os.IArcadeScores scores)
    {
        this.name = name;
        this.storage = capabilities.Storage("sudoku");
        this.rewards = rewards;
        this.scores = scores;
        this.leaderboard = new AetherLove.Widgets.ArcadeLeaderboardView(scores, ArcadeGame.Sudoku);
    }

    public string Id => "sudoku";

    public string Name => this.name();

    public FontAwesomeIcon Icon => FontAwesomeIcon.Th;

    public Vector4 TileTop => TileTopColor;

    public Vector4 TileBottom => TileBottomColor;

    public int Badge => 0;

    public bool HasSurface => true;

    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Strings =>
        Localization.AppStrings.Packs;

    public void Open()
    {
    }

    public void OnForeground()
    {
        this.lastFrameTime = ImGui.GetTime();
        this.splashStartedAt = ImGui.GetTime();
    }

    /// <summary>The clock is the whole tension, so leaving the phone stops it rather than quietly burning
    /// the grid down while nobody is looking.</summary>
    public void OnBackground()
    {
        if (this.view == View.Playing)
        {
            this.paused = true;
        }
    }

    public void OnIntent(OsIntent intent)
    {
    }

    public void Draw(OsAppContext ctx)
    {
        EnsureScoresLoaded();
        var now = ImGui.GetTime();
        var delta = Math.Clamp(now - this.lastFrameTime, 0.0, 0.5);
        this.lastFrameTime = now;

        var dl = ImGui.GetWindowDrawList();
        var winPos = ImGui.GetWindowPos();
        var winSize = ImGui.GetWindowSize();
        dl.AddRectFilled(winPos, winPos + winSize, ImGui.GetColorU32(RetroLcd.Panel));

        switch (this.view)
        {
            case View.Playing:
                DrawPlaying(ctx, delta, winPos, winSize);
                break;
            case View.Cleared:
                DrawCleared(ctx, winPos, winSize);
                break;
            case View.GameOver:
                DrawGameOver(ctx, winPos, winSize);
                break;
            case View.Scores:
                DrawScores(ctx, winPos, winSize);
                break;
            case View.Leaderboard:
                this.leaderboard.Draw(ctx, winPos, winSize, () =>
                {
                    this.splashStartedAt = ImGui.GetTime();
                    this.view = View.Splash;
                });
                break;
            default:
                DrawSplash(ctx, now, winPos, winSize);
                break;
        }
    }

    private void EnsureScoresLoaded()
    {
        if (this.scoresLoaded)
        {
            return;
        }
        this.scoresLoaded = true;
        this.highScores = this.storage.Get<int[]>(HighScoresKey) ?? [];
    }

    private int BestScore => this.highScores.Length > 0 ? this.highScores[0] : 0;

    private void StartRun()
    {
        this.game.Start();
        this.selected = -1;
        this.pencil = false;
        this.paused = false;
        this.lastFrameTime = ImGui.GetTime();
        this.view = View.Playing;
    }

    /// <summary>A finished run is the sparks signal; the server decides if it pays, and re-checks the score
    /// against what the ladder makes possible.</summary>
    private void FinishRun()
    {
        this.lastRunScore = this.game.Score;
        this.lastRunWasRecord = this.lastRunScore > BestScore;
        this.highScores = this.highScores
            .Append(this.lastRunScore)
            .OrderByDescending(s => s)
            .Take(ScoreSlots)
            .ToArray();
        this.storage.Set(HighScoresKey, this.highScores);
        this.rewards.NoteGameFinished();
        this.scores.SubmitScore(new ArcadeScoreSubmissionDto(
            ArcadeGame.Sudoku, this.lastRunScore, (int)(this.game.RunSeconds * 1000.0),
            this.game.Solved, (int)this.game.Peak));
        this.view = View.GameOver;
    }

    private void DrawSplash(OsAppContext ctx, double now, Vector2 winPos, Vector2 winSize)
    {
        var dl = ImGui.GetWindowDrawList();
        var elapsed = now - this.splashStartedAt;
        var litRows = ctx.ReduceMotion
            ? RetroLcd.GlyphHeight
            : (int)Math.Min(RetroLcd.GlyphHeight, elapsed / 0.09);

        var pixel = MathF.Max(2f, winSize.X * 0.66f / RetroLcd.WordColumns("SUDOKU"));
        var wordH = RetroLcd.GlyphHeight * pixel;
        var wordY = winSize.Y * 0.14f;
        RetroLcd.DrawWordCentered(dl, "SUDOKU", winPos + new Vector2(0f, wordY), winSize.X, pixel,
            ImGui.GetColorU32(RetroLcd.Pixel), litRows);

        var subtitle = ctx.Localize("os.sudoku_subtitle");
        var subSize = ImGui.CalcTextSize(subtitle);
        dl.AddText(winPos + new Vector2((winSize.X - subSize.X) * 0.5f, wordY + wordH + ctx.Px(12f)),
            ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.75f }), subtitle);

        DrawLadderPreview(ctx, dl, winPos, winSize);

        var buttonW = winSize.X * 0.62f;
        var buttonH = ctx.Px(38f);
        var firstY = winSize.Y * 0.58f;
        if (RetroLcd.Button("##sudokuPlay", ctx.Localize("os.sudoku_play"),
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, firstY), new Vector2(buttonW, buttonH),
            ctx.Px(4f), filled: true))
        {
            StartRun();
        }
        if (RetroLcd.Button("##sudokuScores", ctx.Localize("os.sudoku_high_scores"),
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, firstY + buttonH + ctx.Px(10f)),
            new Vector2(buttonW, buttonH), ctx.Px(4f), filled: false))
        {
            this.view = View.Scores;
        }
        if (RetroLcd.Button("##sudokuBoard", ctx.Localize("os.arcade_leaderboard"),
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, firstY + ((buttonH + ctx.Px(10f)) * 2f)),
            new Vector2(buttonW, buttonH), ctx.Px(4f), filled: false))
        {
            this.view = View.Leaderboard;
        }

        if (RetroLcd.Key("##sudokuExit", FontAwesomeIcon.ArrowLeft,
            winPos + new Vector2(ctx.Px(12f), ctx.Px(12f)), ctx.Px(30f)))
        {
            ctx.Shell.GoHomeToFolder(IOsShell.ArcadeFolderId);
        }

        if (BestScore > 0)
        {
            var best = string.Format(ctx.Localize("os.sudoku_best"), BestScore);
            var bestSize = ImGui.CalcTextSize(best);
            dl.AddText(winPos + new Vector2((winSize.X - bestSize.X) * 0.5f, winSize.Y - ctx.Px(28f)),
                ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.7f }), best);
        }
    }

    /// <summary>The four rungs, so it is obvious up front that this climbs rather than asks.</summary>
    private static void DrawLadderPreview(OsAppContext ctx, ImDrawListPtr dl, Vector2 winPos, Vector2 winSize)
    {
        var y = winPos.Y + (winSize.Y * 0.40f);
        var step = winSize.X / 5f;
        for (var i = 0; i < 4; i++)
        {
            var label = ctx.Localize(DifficultyKey((SudokuDifficulty)i));
            var size = ImGui.CalcTextSize(label);
            var alpha = 0.35f + (0.2f * i);
            dl.AddText(new Vector2(winPos.X + (step * (i + 0.5f)) + ((step - size.X) * 0.5f) - (step * 0.5f), y),
                ImGui.GetColorU32(RetroLcd.Pixel with { W = alpha }), label);
        }
    }

    private static string DifficultyKey(SudokuDifficulty difficulty) => difficulty switch
    {
        SudokuDifficulty.Easy => "os.sudoku_easy",
        SudokuDifficulty.Medium => "os.sudoku_medium",
        SudokuDifficulty.Difficult => "os.sudoku_difficult",
        _ => "os.sudoku_insane",
    };

    private void DrawPlaying(OsAppContext ctx, double delta, Vector2 winPos, Vector2 winSize)
    {
        if (RetroLcd.WindowBlurred())
        {
            this.paused = true;
        }

        if (!this.paused)
        {
            this.game.Tick(delta);
        }
        else
        {
            this.game.Poll();
        }

        var hudH = ctx.Px(28f);
        DrawHud(ctx, winPos, winSize, hudH);

        if (!this.game.Ready)
        {
            DrawGenerating(ctx, winPos, winSize);
            return;
        }

        var padH = winSize.Y * 0.30f;
        var boardMax = MathF.Min(winSize.X - ctx.Px(20f), winSize.Y - hudH - padH - ctx.Px(14f));
        var cell = MathF.Floor(boardMax / SudokuSolver.Size);
        var boardSize = cell * SudokuSolver.Size;
        var boardTL = new Vector2(winPos.X + ((winSize.X - boardSize) * 0.5f), winPos.Y + hudH + ctx.Px(6f));

        DrawBoard(ctx, boardTL, cell);
        DrawPad(ctx, winPos, winSize, padH);

        if (this.paused)
        {
            DrawPausedOverlay(ctx, boardTL, boardSize);
        }

        switch (this.game.Outcome)
        {
            case SudokuOutcome.Solved:
                this.view = View.Cleared;
                break;
            case SudokuOutcome.OutOfTime:
            case SudokuOutcome.OutOfStrikes:
                FinishRun();
                break;
        }
    }

    private void DrawHud(OsAppContext ctx, Vector2 winPos, Vector2 winSize, float hudH)
    {
        var dl = ImGui.GetWindowDrawList();
        var padX = ctx.Px(10f);
        var textY = winPos.Y + ((hudH - ImGui.GetTextLineHeight()) * 0.5f);
        var reserve = RetroLcd.PauseKeyWidth(hudH) + ctx.Px(8f);

        if (!this.paused && RetroLcd.PauseKey(winPos, winSize, padX, hudH))
        {
            this.paused = true;
        }

        dl.AddText(new Vector2(winPos.X + padX, textY), ImGui.GetColorU32(RetroLcd.Pixel),
            string.Format(ctx.Localize("os.sudoku_score"), this.game.Score));

        var rung = ctx.Localize(DifficultyKey(this.game.Difficulty));
        var clock = TimeSpan.FromSeconds(this.game.SecondsLeft).ToString(@"m\:ss");
        var mid = $"{rung}  {clock}";
        var midSize = ImGui.CalcTextSize(mid);
        // The last minute turns the readout down so it is obvious without a separate warning.
        var urgent = this.game.SecondsLeft <= 60.0;
        dl.AddText(new Vector2(winPos.X + winSize.X - padX - reserve - midSize.X, textY),
            ImGui.GetColorU32(RetroLcd.Pixel with { W = urgent ? 1f : 0.72f }), mid);

        DrawStrikes(ctx, dl, winPos, hudH);
    }

    private void DrawStrikes(OsAppContext ctx, ImDrawListPtr dl, Vector2 winPos, float hudH)
    {
        var size = ctx.Px(7f);
        var gap = ctx.Px(5f);
        var x = winPos.X + ctx.Px(10f) + ImGui.CalcTextSize("Score 000000").X + ctx.Px(10f);
        var y = winPos.Y + ((hudH - size) * 0.5f);
        for (var i = 0; i < SudokuGame.MaxStrikes; i++)
        {
            var spent = i < this.game.Strikes;
            var tl = new Vector2(x + (i * (size + gap)), y);
            if (spent)
            {
                dl.AddRectFilled(tl, tl + new Vector2(size, size), ImGui.GetColorU32(RetroLcd.Pixel));
            }
            else
            {
                dl.AddRect(tl, tl + new Vector2(size, size),
                    ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.5f }), 0f, ImDrawFlags.None, 1.5f);
            }
        }
    }

    private static void DrawGenerating(OsAppContext ctx, Vector2 winPos, Vector2 winSize)
    {
        var dl = ImGui.GetWindowDrawList();
        var label = ctx.Localize("os.sudoku_generating");
        var size = ImGui.CalcTextSize(label);
        dl.AddText(winPos + new Vector2((winSize.X - size.X) * 0.5f, winSize.Y * 0.45f),
            ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.7f }), label);
    }

    private void DrawBoard(OsAppContext ctx, Vector2 boardTL, float cell)
    {
        var dl = ImGui.GetWindowDrawList();
        var boardSize = cell * SudokuSolver.Size;
        var ink = ImGui.GetColorU32(RetroLcd.Pixel);

        dl.AddRectFilled(boardTL, boardTL + new Vector2(boardSize, boardSize),
            ImGui.GetColorU32(RetroLcd.Panel));

        var peers = this.selected >= 0 ? SudokuSolver.PeersOf(this.selected) : null;
        var selectedDigit = this.selected >= 0 ? this.game[this.selected] : 0;

        for (var index = 0; index < SudokuSolver.Cells; index++)
        {
            var cx = SudokuSolver.ColOf(index);
            var cy = SudokuSolver.RowOf(index);
            var tl = boardTL + new Vector2(cx * cell, cy * cell);

            if (index == this.selected)
            {
                dl.AddRectFilled(tl, tl + new Vector2(cell, cell), ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.30f }));
            }
            else if (peers != null && Array.IndexOf(peers, index) >= 0)
            {
                dl.AddRectFilled(tl, tl + new Vector2(cell, cell), ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.10f }));
            }
            else if (selectedDigit != 0 && this.game[index] == selectedDigit)
            {
                dl.AddRectFilled(tl, tl + new Vector2(cell, cell), ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.18f }));
            }

            var value = this.game[index];
            if (value != 0)
            {
                // Givens sit at full strength, the player's own digits a shade lighter, so the grid always
                // shows what was handed to you and what you worked out.
                var alpha = this.game.IsGiven(index) ? 1f : 0.72f;
                var glyph = value.ToString();
                var size = ImGui.CalcTextSize(glyph);
                dl.AddText(tl + ((new Vector2(cell, cell) - size) * 0.5f),
                    ImGui.GetColorU32(RetroLcd.Pixel with { W = alpha }), glyph);
            }
            else if (this.game.MarksAt(index) != 0)
            {
                DrawMarks(dl, tl, cell, this.game.MarksAt(index));
            }
        }

        DrawGridLines(dl, boardTL, cell, boardSize, ink);
        HandleBoardClicks(boardTL, cell, boardSize);
    }

    private static void DrawMarks(ImDrawListPtr dl, Vector2 tl, float cell, int mask)
    {
        var third = cell / 3f;
        for (var digit = 1; digit <= 9; digit++)
        {
            if ((mask & (1 << (digit - 1))) == 0)
            {
                continue;
            }
            var mx = (digit - 1) % 3;
            var my = (digit - 1) / 3;
            var glyph = digit.ToString();
            var size = ImGui.CalcTextSize(glyph) * 0.75f;
            var centre = tl + new Vector2((mx + 0.5f) * third, (my + 0.5f) * third);
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.75f, centre - (size * 0.5f),
                ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.45f }), glyph);
        }
    }

    private static void DrawGridLines(ImDrawListPtr dl, Vector2 boardTL, float cell, float boardSize, uint ink)
    {
        for (var i = 0; i <= SudokuSolver.Size; i++)
        {
            var heavy = i % 3 == 0;
            var thickness = heavy ? 2.5f : 1f;
            var colour = heavy ? ink : ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.35f });
            var offset = i * cell;
            dl.AddLine(boardTL + new Vector2(offset, 0f), boardTL + new Vector2(offset, boardSize), colour, thickness);
            dl.AddLine(boardTL + new Vector2(0f, offset), boardTL + new Vector2(boardSize, offset), colour, thickness);
        }
    }

    private void HandleBoardClicks(Vector2 boardTL, float cell, float boardSize)
    {
        if (this.paused)
        {
            return;
        }

        ImGui.SetCursorScreenPos(boardTL);
        ImGui.InvisibleButton("##sudokuBoard", new Vector2(boardSize, boardSize));
        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }
        if (!ImGui.IsItemClicked())
        {
            return;
        }

        var local = ImGui.GetIO().MousePos - boardTL;
        var cx = Math.Clamp((int)(local.X / cell), 0, SudokuSolver.Size - 1);
        var cy = Math.Clamp((int)(local.Y / cell), 0, SudokuSolver.Size - 1);
        var index = (cy * SudokuSolver.Size) + cx;
        this.selected = this.game.IsGiven(index) && this.selected == index ? -1 : index;
    }

    private void DrawPad(OsAppContext ctx, Vector2 winPos, Vector2 winSize, float padH)
    {
        var key = MathF.Min(padH * 0.30f, (winSize.X - ctx.Px(24f)) / 5.4f);
        var gap = key * 0.14f;
        var rowW = (key * 5f) + (gap * 4f);
        var baseX = winPos.X + ((winSize.X - rowW) * 0.5f);
        var baseY = winPos.Y + winSize.Y - padH + (padH * 0.06f);

        for (var digit = 1; digit <= 9; digit++)
        {
            var row = (digit - 1) / 5;
            var col = (digit - 1) % 5;
            var tl = new Vector2(baseX + (col * (key + gap)), baseY + (row * (key + gap)));
            var exhausted = this.game.RemainingOf(digit) <= 0;
            if (DigitKey(ctx, digit, tl, key, exhausted) && !exhausted)
            {
                Apply(digit);
            }
        }

        // Pencil and erase share the tenth slot's row, to the right of the 6-9 run.
        var toolsX = baseX + (4 * (key + gap));
        var toolsY = baseY + key + gap;
        if (RetroLcd.KeyLabel("##sudokuPencil", this.pencil ? "N" : "n", new Vector2(toolsX, toolsY), key))
        {
            this.pencil = !this.pencil;
        }

        var eraseY = toolsY + key + gap;
        if (RetroLcd.Key("##sudokuErase", FontAwesomeIcon.Eraser,
            new Vector2(baseX + (rowW * 0.5f) - (key * 0.5f), eraseY), key) && this.selected >= 0)
        {
            this.game.Clear(this.selected);
        }
    }

    /// <summary>A digit key that dims once all nine of that digit are placed, so the pad doubles as a tally.</summary>
    private static bool DigitKey(OsAppContext ctx, int digit, Vector2 tl, float size, bool exhausted)
    {
        if (!exhausted)
        {
            return RetroLcd.KeyLabel($"##sudokuDigit{digit}", digit.ToString(), tl, size);
        }

        var dl = ImGui.GetWindowDrawList();
        dl.AddRect(tl, tl + new Vector2(size, size), ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.25f }),
            size * 0.22f, ImDrawFlags.None, 1.5f);
        var pixel = MathF.Max(1f, size * 0.42f / RetroLcd.GlyphHeight);
        RetroLcd.DrawWordCentered(dl, digit.ToString(),
            tl + new Vector2(0f, (size - (RetroLcd.GlyphHeight * pixel)) * 0.5f), size, pixel,
            ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.25f }), RetroLcd.GlyphHeight);
        return false;
    }

    private void Apply(int digit)
    {
        if (this.selected < 0 || this.paused)
        {
            return;
        }
        if (this.pencil)
        {
            this.game.ToggleMark(this.selected, digit);
            return;
        }
        this.game.Place(this.selected, digit);
    }

    private void DrawPausedOverlay(OsAppContext ctx, Vector2 boardTL, float boardSize)
    {
        var dl = ImGui.GetWindowDrawList();
        // The grid is covered rather than dimmed: a paused board left legible is a free window in which to
        // ask something else for the answer.
        dl.AddRectFilled(boardTL, boardTL + new Vector2(boardSize, boardSize),
            ImGui.GetColorU32(RetroLcd.Panel));
        dl.AddRect(boardTL, boardTL + new Vector2(boardSize, boardSize),
            ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.5f }), 0f, ImDrawFlags.None, 2f);

        var label = ctx.Localize("os.sudoku_paused");
        var labelSize = ImGui.CalcTextSize(label);
        dl.AddText(boardTL + new Vector2((boardSize - labelSize.X) * 0.5f, (boardSize * 0.5f) - ctx.Px(34f)),
            ImGui.GetColorU32(RetroLcd.Pixel), label);

        var buttonW = boardSize * 0.7f;
        var buttonH = ctx.Px(36f);
        if (RetroLcd.Button("##sudokuResume", ctx.Localize("os.sudoku_resume"),
            boardTL + new Vector2((boardSize - buttonW) * 0.5f, boardSize * 0.5f),
            new Vector2(buttonW, buttonH), ctx.Px(4f), filled: true))
        {
            this.lastFrameTime = ImGui.GetTime();
            this.paused = false;
        }
    }

    /// <summary>Between grids: what the last one paid, and what is coming next.</summary>
    private void DrawCleared(OsAppContext ctx, Vector2 winPos, Vector2 winSize)
    {
        var dl = ImGui.GetWindowDrawList();
        this.game.Poll();

        var title = ctx.Localize("os.sudoku_cleared");
        using (ctx.TitleFont?.Push())
        {
            var titleSize = ImGui.CalcTextSize(title);
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
                winPos + new Vector2((winSize.X - titleSize.X) * 0.5f, winSize.Y * 0.20f),
                ImGui.GetColorU32(RetroLcd.Pixel), title);
        }

        var lines = new[]
        {
            string.Format(ctx.Localize("os.sudoku_score"), this.game.Score),
            string.Format(ctx.Localize("os.sudoku_solved_count"), this.game.Solved),
            string.Format(ctx.Localize("os.sudoku_next"), ctx.Localize(DifficultyKey(this.game.Difficulty))),
        };
        var y = winSize.Y * 0.36f;
        foreach (var line in lines)
        {
            var size = ImGui.CalcTextSize(line);
            dl.AddText(winPos + new Vector2((winSize.X - size.X) * 0.5f, y), ImGui.GetColorU32(RetroLcd.Pixel), line);
            y += ImGui.GetTextLineHeightWithSpacing();
        }

        var buttonW = winSize.X * 0.62f;
        var buttonH = ctx.Px(38f);
        var ready = this.game.Ready;
        var label = ctx.Localize(ready ? "os.sudoku_next_button" : "os.sudoku_generating");
        if (RetroLcd.Button("##sudokuNext", label,
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, winSize.Y * 0.62f),
            new Vector2(buttonW, buttonH), ctx.Px(4f), filled: true) && ready)
        {
            this.selected = -1;
            this.game.Continue();
            this.lastFrameTime = ImGui.GetTime();
            this.view = View.Playing;
        }
    }

    private void DrawGameOver(OsAppContext ctx, Vector2 winPos, Vector2 winSize)
    {
        var dl = ImGui.GetWindowDrawList();
        var title = ctx.Localize(this.lastRunWasRecord ? "os.sudoku_new_record" : "os.sudoku_game_over");
        using (ctx.TitleFont?.Push())
        {
            var titleSize = ImGui.CalcTextSize(title);
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
                winPos + new Vector2((winSize.X - titleSize.X) * 0.5f, winSize.Y * 0.18f),
                ImGui.GetColorU32(RetroLcd.Pixel), title);
        }

        var reason = this.game.Outcome == SudokuOutcome.OutOfTime
            ? "os.sudoku_out_of_time"
            : "os.sudoku_out_of_strikes";
        var lines = new[]
        {
            ctx.Localize(reason),
            string.Format(ctx.Localize("os.sudoku_score"), this.lastRunScore),
            string.Format(ctx.Localize("os.sudoku_solved_count"), this.game.Solved),
            string.Format(ctx.Localize("os.sudoku_reached"), ctx.Localize(DifficultyKey(this.game.Peak))),
        };
        var y = winSize.Y * 0.32f;
        foreach (var line in lines)
        {
            var size = ImGui.CalcTextSize(line);
            dl.AddText(winPos + new Vector2((winSize.X - size.X) * 0.5f, y), ImGui.GetColorU32(RetroLcd.Pixel), line);
            y += ImGui.GetTextLineHeightWithSpacing();
        }

        var buttonW = winSize.X * 0.62f;
        var buttonH = ctx.Px(38f);
        var firstY = winSize.Y * 0.62f;
        if (RetroLcd.Button("##sudokuAgain", ctx.Localize("os.sudoku_play_again"),
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, firstY), new Vector2(buttonW, buttonH),
            ctx.Px(4f), filled: true))
        {
            StartRun();
        }
        if (RetroLcd.Button("##sudokuMenu", ctx.Localize("os.sudoku_menu"),
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, firstY + buttonH + ctx.Px(10f)),
            new Vector2(buttonW, buttonH), ctx.Px(4f), filled: false))
        {
            this.splashStartedAt = ImGui.GetTime();
            this.view = View.Splash;
        }
    }

    private void DrawScores(OsAppContext ctx, Vector2 winPos, Vector2 winSize)
    {
        var dl = ImGui.GetWindowDrawList();
        var title = ctx.Localize("os.sudoku_high_scores");
        using (ctx.TitleFont?.Push())
        {
            var titleSize = ImGui.CalcTextSize(title);
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
                winPos + new Vector2((winSize.X - titleSize.X) * 0.5f, winSize.Y * 0.12f),
                ImGui.GetColorU32(RetroLcd.Pixel), title);
        }

        var padX = ctx.Px(28f);
        var y = winSize.Y * 0.26f;
        if (this.highScores.Length == 0)
        {
            var empty = ctx.Localize("os.sudoku_no_scores");
            var size = ImGui.CalcTextSize(empty);
            dl.AddText(winPos + new Vector2((winSize.X - size.X) * 0.5f, y),
                ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.7f }), empty);
        }
        for (var i = 0; i < this.highScores.Length; i++)
        {
            var value = this.highScores[i].ToString();
            var valueSize = ImGui.CalcTextSize(value);
            var rowColor = RetroLcd.Pixel with { W = i == 0 ? 1f : 0.8f };
            dl.AddText(winPos + new Vector2(padX, y), ImGui.GetColorU32(rowColor), $"{i + 1}.");
            dl.AddText(winPos + new Vector2(winSize.X - padX - valueSize.X, y), ImGui.GetColorU32(rowColor), value);
            y += ImGui.GetTextLineHeightWithSpacing() * 1.35f;
        }

        var buttonW = winSize.X * 0.62f;
        var buttonH = ctx.Px(38f);
        if (RetroLcd.Button("##sudokuScoresBack", ctx.Localize("os.sudoku_menu"),
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, winSize.Y - buttonH - ctx.Px(24f)),
            new Vector2(buttonW, buttonH), ctx.Px(4f), filled: false))
        {
            this.splashStartedAt = ImGui.GetTime();
            this.view = View.Splash;
        }
    }
}
