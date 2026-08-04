using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AetherLove.Shared.Arcade;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Snake;

/// <summary>Snake, the Nokia way: a monochrome LCD panel, a blocky title card, a fixed-step board that
/// speeds up as you eat, and a five-slot high-score table in the app's own storage. Scoring is 10 per
/// pellet plus 1 per second survived.</summary>
public sealed class SnakeApp : IAetherApp
{
    private enum View { Splash, Playing, GameOver, Scores, Leaderboard }

    private static readonly Vector4 TileTopColor = new(0.62f, 0.73f, 0.27f, 1f);
    private static readonly Vector4 TileBottomColor = new(0.24f, 0.31f, 0.10f, 1f);

    private const string HighScoresKey = "high_scores";
    private const int ScoreSlots = 5;

    private readonly Func<string> name;
    private readonly IAppStorage storage;
    private readonly IKeyboardInput keys;
    private readonly AetherLove.Os.IArcadeRewards rewards;
    private readonly AetherLove.Os.IArcadeScores scores;
    private readonly AetherLove.Widgets.ArcadeLeaderboardView leaderboard;
    private readonly SnakeGame game = new();

    private View view = View.Splash;
    private bool paused;
    private double lastFrameTime;
    private double splashStartedAt;
    private int[] highScores = [];
    private bool scoresLoaded;
    private int lastRunScore;
    private bool lastRunWasRecord;
    private double runSeconds;

    public SnakeApp(Func<string> name, IAppCapabilities capabilities, AetherLove.Os.IArcadeRewards rewards, AetherLove.Os.IArcadeScores scores)
    {
        this.name = name;
        this.storage = capabilities.Storage("snake");
        this.keys = capabilities.Keyboard;
        this.rewards = rewards;
        this.scores = scores;
        this.leaderboard = new AetherLove.Widgets.ArcadeLeaderboardView(scores, ArcadeGame.Snake);
    }

    public string Id => "snake";

    public string Name => this.name();

    public FontAwesomeIcon Icon => FontAwesomeIcon.Gamepad;

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

    /// <summary>Leaving the app freezes the board instead of letting the snake drive into a wall while
    /// nobody is looking.</summary>
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
            ArcadeGame.Snake, this.lastRunScore, (int)(this.runSeconds * 1000.0), this.game.Pellets));
    }

    private void DrawSplash(OsAppContext ctx, double now, Vector2 winPos, Vector2 winSize)
    {
        var dl = ImGui.GetWindowDrawList();
        var elapsed = now - this.splashStartedAt;
        // The word types itself on row by row, like an LCD waking up.
        var litRows = ctx.ReduceMotion
            ? RetroLcd.GlyphHeight
            : (int)Math.Min(RetroLcd.GlyphHeight, elapsed / 0.09);

        var pixel = MathF.Max(2f, winSize.X * 0.7f / RetroLcd.WordColumns("SNAKE"));
        var wordH = RetroLcd.GlyphHeight * pixel;
        var wordY = winSize.Y * 0.16f;
        RetroLcd.DrawWordCentered(dl, "SNAKE", winPos + new Vector2(0f, wordY), winSize.X, pixel,
            ImGui.GetColorU32(RetroLcd.Pixel), litRows);

        var subtitle = ctx.Localize("os.snake_subtitle");
        var subSize = ImGui.CalcTextSize(subtitle);
        dl.AddText(winPos + new Vector2((winSize.X - subSize.X) * 0.5f, wordY + wordH + ctx.Px(14f)),
            ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.75f }), subtitle);

        DrawDecorSnake(dl, winPos, winSize, now, ctx.ReduceMotion);

        var buttonW = winSize.X * 0.62f;
        var buttonH = ctx.Px(38f);
        var firstY = winSize.Y * 0.52f;
        if (RetroLcd.Button("##snakePlay", ctx.Localize("os.snake_play"),
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, firstY), new Vector2(buttonW, buttonH),
            ctx.Px(4f), filled: true))
        {
            StartRun();
        }
        if (RetroLcd.Button("##snakeScores", ctx.Localize("os.snake_high_scores"),
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, firstY + buttonH + ctx.Px(10f)),
            new Vector2(buttonW, buttonH), ctx.Px(4f), filled: false))
        {
            this.view = View.Scores;
        }
        if (RetroLcd.Button("##snakeBoard", ctx.Localize("os.arcade_leaderboard"),
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, firstY + ((buttonH + ctx.Px(10f)) * 2f)),
            new Vector2(buttonW, buttonH), ctx.Px(4f), filled: false))
        {
            this.view = View.Leaderboard;
        }

        if (RetroLcd.Key("##snakeExit", Dalamud.Interface.FontAwesomeIcon.ArrowLeft,
            winPos + new Vector2(ctx.Px(12f), ctx.Px(12f)), ctx.Px(30f)))
        {
            ctx.Shell.GoHomeToFolder(IOsShell.ArcadeFolderId);
        }

        if (BestScore > 0)
        {
            var best = string.Format(ctx.Localize("os.snake_best"), BestScore);
            var bestSize = ImGui.CalcTextSize(best);
            dl.AddText(winPos + new Vector2((winSize.X - bestSize.X) * 0.5f, winSize.Y - ctx.Px(30f)),
                ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.7f }), best);
        }
    }

    /// <summary>A little snake wriggles under the title while you decide.</summary>
    private static void DrawDecorSnake(ImDrawListPtr dl, Vector2 winPos, Vector2 winSize, double now, bool reduceMotion)
    {
        var pixel = winSize.X * 0.030f;
        var baseX = winPos.X + ((winSize.X - (pixel * 9f)) * 0.5f);
        var baseY = winPos.Y + (winSize.Y * 0.44f);
        var phase = reduceMotion ? 0.0 : now * 2.2;
        for (var i = 0; i < 9; i++)
        {
            var wave = (float)Math.Sin(phase - (i * 0.55)) * pixel * 0.5f;
            var tl = new Vector2(baseX + (i * pixel), baseY + wave);
            var alpha = i == 8 ? 1f : 0.85f;
            dl.AddRectFilled(tl, tl + new Vector2(pixel - 2f, pixel - 2f),
                ImGui.GetColorU32(RetroLcd.Pixel with { W = alpha }));
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
            ReadSteering();
            this.runSeconds += delta;
            this.game.Tick(delta);
            if (this.game.Dead)
            {
                this.view = View.GameOver;
                FinishRun();
            }
        }

        var padX = ctx.Px(10f);
        var hudH = ctx.Px(26f);
        var padH = winSize.Y * 0.26f;
        var boardMaxW = winSize.X - (padX * 2f);
        var boardMaxH = winSize.Y - hudH - padH - ctx.Px(8f);
        var cell = MathF.Max(2f, MathF.Floor(MathF.Min(boardMaxW / SnakeGame.Columns, boardMaxH / SnakeGame.Rows)));
        var boardW = cell * SnakeGame.Columns;
        var boardH = cell * SnakeGame.Rows;
        var boardTL = new Vector2(winPos.X + ((winSize.X - boardW) * 0.5f), winPos.Y + hudH);

        DrawHud(ctx, winPos, winSize, hudH);
        DrawBoard(boardTL, boardW, boardH, cell);
        if (this.paused)
        {
            DrawPausedOverlay(ctx, boardTL, boardW, boardH);
        }
        else
        {
            DrawDpad(winPos, winSize, padH);
        }
    }

    private void DrawHud(OsAppContext ctx, Vector2 winPos, Vector2 winSize, float hudH)
    {
        var dl = ImGui.GetWindowDrawList();
        var score = string.Format(ctx.Localize("os.snake_score"), this.game.Score);
        var best = string.Format(ctx.Localize("os.snake_best"), Math.Max(BestScore, this.game.Score));
        var padX = ctx.Px(12f);
        var reserve = RetroLcd.PauseKeyWidth(hudH) + ctx.Px(8f);
        if (!this.paused && RetroLcd.PauseKey(winPos, winSize, padX, hudH))
        {
            this.paused = true;
        }
        var textY = winPos.Y + ((hudH - ImGui.GetTextLineHeight()) * 0.5f);
        dl.AddText(new Vector2(winPos.X + padX, textY), ImGui.GetColorU32(RetroLcd.Pixel), score);
        var bestSize = ImGui.CalcTextSize(best);
        dl.AddText(new Vector2(winPos.X + winSize.X - padX - reserve - bestSize.X, textY),
            ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.7f }), best);
    }

    private void DrawBoard(Vector2 boardTL, float boardW, float boardH, float cell)
    {
        var dl = ImGui.GetWindowDrawList();
        dl.AddRect(boardTL - new Vector2(2f, 2f), boardTL + new Vector2(boardW + 2f, boardH + 2f),
            ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.55f }), 0f, ImDrawFlags.None, 2f);
        RetroLcd.DotGrid(dl, boardTL, SnakeGame.Columns, SnakeGame.Rows, cell);

        var food = this.game.Food;
        var foodTL = boardTL + new Vector2(food.X * cell, food.Y * cell);
        var inset = MathF.Max(1f, cell * 0.22f);
        dl.AddRectFilled(foodTL + new Vector2(inset, inset), foodTL + new Vector2(cell - inset, cell - inset),
            ImGui.GetColorU32(RetroLcd.Pixel));

        var body = this.game.Body;
        for (var i = 0; i < body.Count; i++)
        {
            var color = i == 0 ? RetroLcd.Pixel : RetroLcd.Pixel with { W = 0.88f };
            RetroLcd.Cell(dl, boardTL, body[i].X, body[i].Y, cell, ImGui.GetColorU32(color));
        }
    }

    /// <summary>Arrow keys and WASD. The capability consumes each press, so steering never also walks your
    /// character around Eorzea.</summary>
    private void ReadSteering()
    {
        if (this.keys.WasPressed(AppKey.Up) || this.keys.WasPressed(AppKey.W))
        {
            this.game.Steer(SnakeDirection.Up);
        }
        else if (this.keys.WasPressed(AppKey.Down) || this.keys.WasPressed(AppKey.S))
        {
            this.game.Steer(SnakeDirection.Down);
        }
        else if (this.keys.WasPressed(AppKey.Left) || this.keys.WasPressed(AppKey.A))
        {
            this.game.Steer(SnakeDirection.Left);
        }
        else if (this.keys.WasPressed(AppKey.Right) || this.keys.WasPressed(AppKey.D))
        {
            this.game.Steer(SnakeDirection.Right);
        }
    }

    private void DrawDpad(Vector2 winPos, Vector2 winSize, float padH)
    {
        var key = MathF.Min(padH * 0.27f, winSize.X * 0.17f);
        var centerX = winPos.X + (winSize.X * 0.5f);
        var centerY = winPos.Y + winSize.Y - (padH * 0.5f);
        var step = key * 1.12f;

        if (RetroLcd.KeyLabel("##snakeUp", "W",
            new Vector2(centerX - (key * 0.5f), centerY - step), key))
        {
            this.game.Steer(SnakeDirection.Up);
        }
        if (RetroLcd.KeyLabel("##snakeDown", "S",
            new Vector2(centerX - (key * 0.5f), centerY), key))
        {
            this.game.Steer(SnakeDirection.Down);
        }
        if (RetroLcd.KeyLabel("##snakeLeft", "A",
            new Vector2(centerX - (key * 0.5f) - step, centerY - (step * 0.5f)), key))
        {
            this.game.Steer(SnakeDirection.Left);
        }
        if (RetroLcd.KeyLabel("##snakeRight", "D",
            new Vector2(centerX - (key * 0.5f) + step, centerY - (step * 0.5f)), key))
        {
            this.game.Steer(SnakeDirection.Right);
        }
    }

    private void DrawPausedOverlay(OsAppContext ctx, Vector2 boardTL, float boardW, float boardH)
    {
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(boardTL, boardTL + new Vector2(boardW, boardH),
            ImGui.GetColorU32(RetroLcd.Panel with { W = 0.82f }));
        var label = ctx.Localize("os.snake_paused");
        var labelSize = ImGui.CalcTextSize(label);
        dl.AddText(boardTL + new Vector2((boardW - labelSize.X) * 0.5f, (boardH * 0.5f) - ctx.Px(30f)),
            ImGui.GetColorU32(RetroLcd.Pixel), label);

        var buttonW = boardW * 0.7f;
        var buttonH = ctx.Px(36f);
        if (RetroLcd.Button("##snakeResume", ctx.Localize("os.snake_resume"),
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
        var title = ctx.Localize(this.lastRunWasRecord ? "os.snake_new_record" : "os.snake_game_over");
        using (ctx.TitleFont?.Push())
        {
            var titleSize = ImGui.CalcTextSize(title);
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
                winPos + new Vector2((winSize.X - titleSize.X) * 0.5f, winSize.Y * 0.20f),
                ImGui.GetColorU32(RetroLcd.Pixel), title);
        }

        var lines = new[]
        {
            string.Format(ctx.Localize("os.snake_score"), this.lastRunScore),
            string.Format(ctx.Localize("os.snake_pellets"), this.game.Pellets),
            string.Format(ctx.Localize("os.snake_time"), (int)this.game.ElapsedSeconds),
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
        if (RetroLcd.Button("##snakeAgain", ctx.Localize("os.snake_play_again"),
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, firstY), new Vector2(buttonW, buttonH),
            ctx.Px(4f), filled: true))
        {
            StartRun();
        }
        if (RetroLcd.Button("##snakeMenu", ctx.Localize("os.snake_menu"),
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
        var title = ctx.Localize("os.snake_high_scores");
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
            var empty = ctx.Localize("os.snake_no_scores");
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
        if (RetroLcd.Button("##snakeBack", ctx.Localize("os.snake_menu"),
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, winSize.Y - buttonH - ctx.Px(24f)),
            new Vector2(buttonW, buttonH), ctx.Px(4f), filled: false))
        {
            this.splashStartedAt = ImGui.GetTime();
            this.view = View.Splash;
        }
    }
}
