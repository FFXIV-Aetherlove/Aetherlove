using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AetherLove.Shared.Arcade;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Stacker;

/// <summary>Stacker on the same handheld LCD as Snake: a 10x20 well, seven-bag pieces, a landing ghost,
/// classic line scoring times the level, and gravity that tightens every ten lines.</summary>
public sealed class StackerApp : IAetherApp
{
    private enum View { Splash, Playing, GameOver, Scores, Leaderboard }

    private static readonly Vector4 TileTopColor = new(0.42f, 0.68f, 0.78f, 1f);
    private static readonly Vector4 TileBottomColor = new(0.13f, 0.26f, 0.33f, 1f);

    private const string HighScoresKey = "high_scores";
    private const int ScoreSlots = 5;

    private readonly Func<string> name;
    private readonly IAppStorage storage;
    private readonly IKeyboardInput keys;
    private readonly AetherLove.Os.IArcadeRewards rewards;
    private readonly AetherLove.Os.IArcadeScores scores;
    private readonly AetherLove.Widgets.ArcadeLeaderboardView leaderboard;
    private readonly StackerGame game = new();

    private View view = View.Splash;
    private bool paused;
    private double lastFrameTime;
    private double splashStartedAt;
    private double repeatAccumulator;
    private double touchDropAccumulator;
    private int[] highScores = [];
    private bool scoresLoaded;
    private int lastRunScore;
    private bool lastRunWasRecord;
    private double runSeconds;

    public StackerApp(Func<string> name, IAppCapabilities capabilities, AetherLove.Os.IArcadeRewards rewards, AetherLove.Os.IArcadeScores scores)
    {
        this.name = name;
        this.storage = capabilities.Storage("stacker");
        this.keys = capabilities.Keyboard;
        this.rewards = rewards;
        this.scores = scores;
        this.leaderboard = new AetherLove.Widgets.ArcadeLeaderboardView(scores, ArcadeGame.Stacker);
    }

    public string Id => "stacker";

    public string Name => this.name();

    public FontAwesomeIcon Icon => FontAwesomeIcon.Th;

    public Vector4 TileTop => TileTopColor;

    public Vector4 TileBottom => TileBottomColor;

    public int Badge => 0;

    public bool HasSurface => true;

    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Strings => Localization.AppStrings.Packs;

    public void Open()
    {
    }

    public void OnForeground()
    {
        this.lastFrameTime = ImGui.GetTime();
        this.splashStartedAt = ImGui.GetTime();
    }

    public void OnBackground()
    {
        if (this.view == View.Playing)
        {
            this.paused = true;
        }
    }

    public void Draw(OsAppContext ctx)
    {
        EnsureScoresLoaded();
        var now = ImGui.GetTime();
        var delta = Math.Max(0.0, now - this.lastFrameTime);
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

    public void OnIntent(OsIntent intent)
    {
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
        this.game.Reset();
        this.runSeconds = 0.0;
        this.lastFrameTime = ImGui.GetTime();
        this.paused = false;
        this.view = View.Playing;
    }

    /// <summary>A finished round is the sparks signal; the server decides if it pays.</summary>
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
            ArcadeGame.Stacker, this.lastRunScore, (int)(this.runSeconds * 1000.0), this.game.Lines));
    }

    private void DrawSplash(OsAppContext ctx, double now, Vector2 winPos, Vector2 winSize)
    {
        var dl = ImGui.GetWindowDrawList();
        var elapsed = now - this.splashStartedAt;
        var litRows = ctx.ReduceMotion
            ? RetroLcd.GlyphHeight
            : (int)Math.Min(RetroLcd.GlyphHeight, elapsed / 0.09);

        var pixel = MathF.Max(2f, winSize.X * 0.76f / RetroLcd.WordColumns("STACKER"));
        var wordY = winSize.Y * 0.14f;
        RetroLcd.DrawWordCentered(dl, "STACKER", winPos + new Vector2(0f, wordY), winSize.X, pixel,
            ImGui.GetColorU32(RetroLcd.Pixel), litRows);

        var subtitle = ctx.Localize("os.stacker_subtitle");
        var subSize = ImGui.CalcTextSize(subtitle);
        dl.AddText(winPos + new Vector2((winSize.X - subSize.X) * 0.5f,
            wordY + (RetroLcd.GlyphHeight * pixel) + ctx.Px(14f)),
            ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.75f }), subtitle);

        DrawDecorPieces(dl, winPos, winSize, now, ctx.ReduceMotion);

        var buttonW = winSize.X * 0.62f;
        var buttonH = ctx.Px(38f);
        var firstY = winSize.Y * 0.52f;
        if (RetroLcd.Button("##stackerPlay", ctx.Localize("os.stacker_play"),
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, firstY), new Vector2(buttonW, buttonH),
            ctx.Px(4f), filled: true))
        {
            StartRun();
        }
        if (RetroLcd.Button("##stackerScores", ctx.Localize("os.stacker_high_scores"),
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, firstY + buttonH + ctx.Px(10f)),
            new Vector2(buttonW, buttonH), ctx.Px(4f), filled: false))
        {
            this.view = View.Scores;
        }
        if (RetroLcd.Button("##stackerBoard", ctx.Localize("os.arcade_leaderboard"),
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, firstY + ((buttonH + ctx.Px(10f)) * 2f)),
            new Vector2(buttonW, buttonH), ctx.Px(4f), filled: false))
        {
            this.view = View.Leaderboard;
        }

        if (RetroLcd.Key("##stackerExit", Dalamud.Interface.FontAwesomeIcon.ArrowLeft,
            winPos + new Vector2(ctx.Px(12f), ctx.Px(12f)), ctx.Px(30f)))
        {
            ctx.Shell.GoHomeToFolder(IOsShell.ArcadeFolderId);
        }

        if (BestScore > 0)
        {
            var best = string.Format(ctx.Localize("os.stacker_best"), BestScore);
            var bestSize = ImGui.CalcTextSize(best);
            dl.AddText(winPos + new Vector2((winSize.X - bestSize.X) * 0.5f, winSize.Y - ctx.Px(30f)),
                ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.7f }), best);
        }
    }

    /// <summary>A row of tetrominoes tumbling gently under the title.</summary>
    private static void DrawDecorPieces(ImDrawListPtr dl, Vector2 winPos, Vector2 winSize, double now, bool reduceMotion)
    {
        var cell = winSize.X * 0.026f;
        var baseY = winPos.Y + (winSize.Y * 0.40f);
        var kinds = new[] { 5, 0, 3, 4 };
        var spacing = winSize.X / (kinds.Length + 1f);
        for (var i = 0; i < kinds.Length; i++)
        {
            var rotation = reduceMotion ? 0 : (int)((now * 0.8) + i) % 4;
            var bob = reduceMotion ? 0f : (float)Math.Sin((now * 1.6) - (i * 0.7)) * cell * 0.5f;
            var origin = new Vector2(winPos.X + (spacing * (i + 1)) - (cell * 2f), baseY + bob);
            foreach (var (x, y) in StackerGame.Cells(kinds[i], rotation, 0, 0))
            {
                RetroLcd.Cell(dl, origin, x, y, cell, ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.9f }));
            }
        }
    }

    private void DrawPlaying(OsAppContext ctx, double delta, Vector2 winPos, Vector2 winSize)
    {
        if (RetroLcd.WindowBlurred() || this.keys.GameTextFocused)
        {
            this.paused = true;
        }

        if (!this.paused)
        {
            ReadKeyboard(delta);
            this.runSeconds += delta;
            this.game.Tick(delta);
            if (this.game.Dead)
            {
                this.view = View.GameOver;
                FinishRun();
            }
        }

        var hudH = ctx.Px(46f);
        var padH = winSize.Y * 0.24f;
        var padX = ctx.Px(10f);
        var boardMaxW = winSize.X - (padX * 2f);
        var boardMaxH = winSize.Y - hudH - padH - ctx.Px(8f);
        var cell = MathF.Max(2f, MathF.Floor(MathF.Min(boardMaxW / StackerGame.Columns, boardMaxH / StackerGame.Rows)));
        var boardW = cell * StackerGame.Columns;
        var boardH = cell * StackerGame.Rows;
        var boardTL = new Vector2(winPos.X + ((winSize.X - boardW) * 0.5f), winPos.Y + hudH);

        DrawHud(ctx, winPos, winSize, hudH, cell);
        DrawWell(boardTL, boardW, boardH, cell);
        if (this.paused)
        {
            DrawPausedOverlay(ctx, boardTL, boardW, boardH);
        }
        else
        {
            DrawControls(delta, winPos, winSize, padH);
        }
    }

    private void DrawHud(OsAppContext ctx, Vector2 winPos, Vector2 winSize, float hudH, float cell)
    {
        var dl = ImGui.GetWindowDrawList();
        var padX = ctx.Px(12f);
        var line = ImGui.GetTextLineHeight();
        dl.AddText(winPos + new Vector2(padX, ctx.Px(6f)), ImGui.GetColorU32(RetroLcd.Pixel),
            string.Format(ctx.Localize("os.stacker_score"), this.game.Score));
        dl.AddText(winPos + new Vector2(padX, ctx.Px(6f) + line), ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.75f }),
            string.Format(ctx.Localize("os.stacker_level_lines"), this.game.Level, this.game.Lines));

        if (!this.paused && RetroLcd.PauseKey(winPos, winSize, padX, hudH))
        {
            this.paused = true;
        }

        // The next piece sits in the top right, drawn small in its own 4x4 box.
        var preview = MathF.Max(3f, cell * 0.5f);
        var previewOrigin = winPos + new Vector2(
            winSize.X - padX - RetroLcd.PauseKeyWidth(hudH) - ctx.Px(8f) - (preview * 4f), ctx.Px(8f));
        foreach (var (x, y) in StackerGame.Cells(this.game.NextKind, 0, 0, 0))
        {
            RetroLcd.Cell(dl, previewOrigin, x, y, preview, ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.85f }));
        }
    }

    private void DrawWell(Vector2 boardTL, float boardW, float boardH, float cell)
    {
        var dl = ImGui.GetWindowDrawList();
        dl.AddRect(boardTL - new Vector2(2f, 2f), boardTL + new Vector2(boardW + 2f, boardH + 2f),
            ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.55f }), 0f, ImDrawFlags.None, 2f);
        RetroLcd.DotGrid(dl, boardTL, StackerGame.Columns, StackerGame.Rows, cell);

        for (var x = 0; x < StackerGame.Columns; x++)
        {
            for (var y = 0; y < StackerGame.Rows; y++)
            {
                if (this.game.Filled(x, y))
                {
                    RetroLcd.Cell(dl, boardTL, x, y, cell, ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.88f }));
                }
            }
        }

        // The landing ghost is an outline so it never reads as a settled block.
        foreach (var (x, y) in this.game.GhostCells())
        {
            if (y < 0)
            {
                continue;
            }
            var tl = boardTL + new Vector2(x * cell, y * cell);
            dl.AddRect(tl + new Vector2(2f, 2f), tl + new Vector2(cell - 2f, cell - 2f),
                ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.35f }), 0f, ImDrawFlags.None, 1.5f);
        }

        foreach (var (x, y) in this.game.CurrentCells())
        {
            if (y >= 0)
            {
                RetroLcd.Cell(dl, boardTL, x, y, cell, ImGui.GetColorU32(RetroLcd.Pixel));
            }
        }
    }

    /// <summary>Arrows or WASD, with a repeat timer so holding left or right walks the piece across.</summary>
    private void ReadKeyboard(double delta)
    {
        if (this.keys.WasPressed(AppKey.Up) || this.keys.WasPressed(AppKey.W))
        {
            this.game.Rotate();
        }
        if (this.keys.WasPressed(AppKey.Space))
        {
            this.game.HardDrop();
        }

        var left = this.keys.IsDown(AppKey.Left) || this.keys.IsDown(AppKey.A);
        var right = this.keys.IsDown(AppKey.Right) || this.keys.IsDown(AppKey.D);
        var down = this.keys.IsDown(AppKey.Down) || this.keys.IsDown(AppKey.S);
        if (!left && !right && !down)
        {
            this.repeatAccumulator = 0;
            return;
        }
        this.repeatAccumulator += delta;
        var firstPress = this.keys.WasPressed(AppKey.Left) || this.keys.WasPressed(AppKey.A)
            || this.keys.WasPressed(AppKey.Right) || this.keys.WasPressed(AppKey.D)
            || this.keys.WasPressed(AppKey.Down) || this.keys.WasPressed(AppKey.S);
        if (!firstPress && this.repeatAccumulator < 0.09)
        {
            return;
        }
        this.repeatAccumulator = 0;
        if (left)
        {
            this.game.MoveLeft();
        }
        else if (right)
        {
            this.game.MoveRight();
        }
        if (down)
        {
            this.game.SoftDrop();
        }
    }

    private void DrawControls(double delta, Vector2 winPos, Vector2 winSize, float padH)
    {
        var key = MathF.Min(padH * 0.42f, winSize.X * 0.16f);
        var rowY = winPos.Y + winSize.Y - (padH * 0.62f);
        var gap = key * 0.16f;
        var totalW = (key * 5f) + (gap * 4f);
        var x = winPos.X + ((winSize.X - totalW) * 0.5f);

        if (RetroLcd.KeyLabel("##stkLeft", "A", new Vector2(x, rowY), key))
        {
            this.game.MoveLeft();
        }
        x += key + gap;
        if (RetroLcd.KeyLabel("##stkRotate", "W", new Vector2(x, rowY), key))
        {
            this.game.Rotate();
        }
        x += key + gap;
        if (RetroLcd.KeyLabel("##stkRight", "D", new Vector2(x, rowY), key))
        {
            this.game.MoveRight();
        }
        x += key + gap;
        // Held soft drop needs its own repeat gate, or a finger on the key would drain the piece to the
        // floor in a single frame-rate-dependent instant.
        if (RetroLcd.KeyLabelHeld("##stkDown", "S", new Vector2(x, rowY), key))
        {
            this.touchDropAccumulator += delta;
            while (this.touchDropAccumulator >= 0.06)
            {
                this.touchDropAccumulator -= 0.06;
                this.game.SoftDrop();
            }
        }
        else
        {
            this.touchDropAccumulator = 0;
        }
        x += key + gap;
        if (RetroLcd.Key("##stkDrop", FontAwesomeIcon.AngleDoubleDown, new Vector2(x, rowY), key))
        {
            this.game.HardDrop();
        }
    }

    private void DrawPausedOverlay(OsAppContext ctx, Vector2 boardTL, float boardW, float boardH)
    {
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(boardTL, boardTL + new Vector2(boardW, boardH),
            ImGui.GetColorU32(RetroLcd.Panel with { W = 0.82f }));
        var label = ctx.Localize("os.stacker_paused");
        var labelSize = ImGui.CalcTextSize(label);
        dl.AddText(boardTL + new Vector2((boardW - labelSize.X) * 0.5f, (boardH * 0.5f) - ctx.Px(30f)),
            ImGui.GetColorU32(RetroLcd.Pixel), label);

        var buttonW = boardW * 0.7f;
        var buttonH = ctx.Px(36f);
        if (RetroLcd.Button("##stkResume", ctx.Localize("os.stacker_resume"),
            boardTL + new Vector2((boardW - buttonW) * 0.5f, boardH * 0.5f), new Vector2(buttonW, buttonH),
            ctx.Px(4f), filled: true))
        {
            this.lastFrameTime = ImGui.GetTime();
            this.paused = false;
        }
    }

    private void DrawGameOver(OsAppContext ctx, Vector2 winPos, Vector2 winSize)
    {
        var dl = ImGui.GetWindowDrawList();
        var title = ctx.Localize(this.lastRunWasRecord ? "os.stacker_new_record" : "os.stacker_game_over");
        using (ctx.TitleFont?.Push())
        {
            var titleSize = ImGui.CalcTextSize(title);
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
                winPos + new Vector2((winSize.X - titleSize.X) * 0.5f, winSize.Y * 0.20f),
                ImGui.GetColorU32(RetroLcd.Pixel), title);
        }

        var lines = new[]
        {
            string.Format(ctx.Localize("os.stacker_score"), this.lastRunScore),
            string.Format(ctx.Localize("os.stacker_lines"), this.game.Lines),
            string.Format(ctx.Localize("os.stacker_level"), this.game.Level),
        };
        var y = winSize.Y * 0.34f;
        foreach (var line in lines)
        {
            var size = ImGui.CalcTextSize(line);
            dl.AddText(winPos + new Vector2((winSize.X - size.X) * 0.5f, y), ImGui.GetColorU32(RetroLcd.Pixel), line);
            y += ImGui.GetTextLineHeightWithSpacing();
        }

        var buttonW = winSize.X * 0.62f;
        var buttonH = ctx.Px(38f);
        var firstY = winSize.Y * 0.60f;
        if (RetroLcd.Button("##stkAgain", ctx.Localize("os.stacker_play_again"),
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, firstY), new Vector2(buttonW, buttonH),
            ctx.Px(4f), filled: true))
        {
            StartRun();
        }
        if (RetroLcd.Button("##stkMenu", ctx.Localize("os.stacker_menu"),
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
        using (ctx.TitleFont?.Push())
        {
            var title = ctx.Localize("os.stacker_high_scores");
            var titleSize = ImGui.CalcTextSize(title);
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
                winPos + new Vector2((winSize.X - titleSize.X) * 0.5f, winSize.Y * 0.12f),
                ImGui.GetColorU32(RetroLcd.Pixel), title);
        }

        var padX = ctx.Px(28f);
        var y = winSize.Y * 0.26f;
        if (this.highScores.Length == 0)
        {
            var empty = ctx.Localize("os.stacker_no_scores");
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
            var dotY = y + (ImGui.GetTextLineHeight() * 0.72f);
            for (var x = padX + ctx.Px(22f); x < winSize.X - padX - valueSize.X - ctx.Px(6f); x += ctx.Px(6f))
            {
                dl.AddRectFilled(winPos + new Vector2(x, dotY), winPos + new Vector2(x + 1.5f, dotY + 1.5f),
                    ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.45f }));
            }
            y += ImGui.GetTextLineHeightWithSpacing() * 1.35f;
        }

        var buttonW = winSize.X * 0.62f;
        var buttonH = ctx.Px(38f);
        if (RetroLcd.Button("##stkBack", ctx.Localize("os.stacker_menu"),
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, winSize.Y - buttonH - ctx.Px(24f)),
            new Vector2(buttonW, buttonH), ctx.Px(4f), filled: false))
        {
            this.splashStartedAt = ImGui.GetTime();
            this.view = View.Splash;
        }
    }
}
