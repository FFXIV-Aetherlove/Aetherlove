using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AetherLove.Shared.Arcade;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Breaker;

/// <summary>Breaker on the handheld LCD: a paddle, bouncing balls, six brick levels with multi-hit and
/// solid blocks, and five dropped power-ups. Drag the board or hold the pad keys to steer.</summary>
public sealed class BreakerApp : IAetherApp
{
    private enum View { Splash, Playing, GameOver, Scores, Leaderboard }

    private static readonly Vector4 TileTopColor = new(0.86f, 0.60f, 0.24f, 1f);
    private static readonly Vector4 TileBottomColor = new(0.36f, 0.20f, 0.06f, 1f);

    private const string HighScoresKey = "high_scores";
    private const int ScoreSlots = 5;

    private readonly Func<string> name;
    private readonly IAppStorage storage;
    private readonly IKeyboardInput keys;
    private readonly AetherLove.Os.IArcadeRewards rewards;
    private readonly AetherLove.Os.IArcadeScores scores;
    private readonly AetherLove.Widgets.ArcadeLeaderboardView leaderboard;
    private readonly BreakerGame game = new();

    private View view = View.Splash;
    private bool paused;
    private double lastFrameTime;
    private double splashStartedAt;
    private int[] highScores = [];
    private bool scoresLoaded;
    private int lastRunScore;
    private int lastRunLevel;
    private bool lastRunWasRecord;
    private double runSeconds;

    public BreakerApp(Func<string> name, IAppCapabilities capabilities, AetherLove.Os.IArcadeRewards rewards, AetherLove.Os.IArcadeScores scores)
    {
        this.name = name;
        this.storage = capabilities.Storage("breaker");
        this.keys = capabilities.Keyboard;
        this.rewards = rewards;
        this.scores = scores;
        this.leaderboard = new AetherLove.Widgets.ArcadeLeaderboardView(scores, ArcadeGame.Breaker);
    }

    public string Id => "breaker";

    public string Name => this.name();

    public FontAwesomeIcon Icon => FontAwesomeIcon.BorderAll;

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
        this.lastRunLevel = this.game.LevelNumber;
        this.lastRunWasRecord = this.lastRunScore > BestScore;
        this.highScores = this.highScores
            .Append(this.lastRunScore)
            .OrderByDescending(s => s)
            .Take(ScoreSlots)
            .ToArray();
        this.storage.Set(HighScoresKey, this.highScores);
        this.rewards.NoteGameFinished();
        this.scores.SubmitScore(new ArcadeScoreSubmissionDto(
            ArcadeGame.Breaker, this.lastRunScore, (int)(this.runSeconds * 1000.0), this.lastRunLevel));
    }

    private void DrawSplash(OsAppContext ctx, double now, Vector2 winPos, Vector2 winSize)
    {
        var dl = ImGui.GetWindowDrawList();
        var elapsed = now - this.splashStartedAt;
        var litRows = ctx.ReduceMotion
            ? RetroLcd.GlyphHeight
            : (int)Math.Min(RetroLcd.GlyphHeight, elapsed / 0.09);

        var pixel = MathF.Max(2f, winSize.X * 0.84f / RetroLcd.WordColumns("BREAKER"));
        var wordY = winSize.Y * 0.13f;
        RetroLcd.DrawWordCentered(dl, "BREAKER", winPos + new Vector2(0f, wordY), winSize.X, pixel,
            ImGui.GetColorU32(RetroLcd.Pixel), litRows);

        var subtitle = ctx.Localize("os.ark_subtitle");
        var subSize = ImGui.CalcTextSize(subtitle);
        dl.AddText(winPos + new Vector2((winSize.X - subSize.X) * 0.5f,
            wordY + (RetroLcd.GlyphHeight * pixel) + ctx.Px(14f)),
            ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.75f }), subtitle);

        DrawDecorBricks(dl, winPos, winSize, now, ctx.ReduceMotion);

        var buttonW = winSize.X * 0.62f;
        var buttonH = ctx.Px(38f);
        var firstY = winSize.Y * 0.52f;
        if (RetroLcd.Button("##brkPlay", ctx.Localize("os.ark_play"),
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, firstY), new Vector2(buttonW, buttonH),
            ctx.Px(4f), filled: true))
        {
            StartRun();
        }
        if (RetroLcd.Button("##brkScores", ctx.Localize("os.ark_high_scores"),
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, firstY + buttonH + ctx.Px(10f)),
            new Vector2(buttonW, buttonH), ctx.Px(4f), filled: false))
        {
            this.view = View.Scores;
        }
        if (RetroLcd.Button("##brkBoard", ctx.Localize("os.arcade_leaderboard"),
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, firstY + ((buttonH + ctx.Px(10f)) * 2f)),
            new Vector2(buttonW, buttonH), ctx.Px(4f), filled: false))
        {
            this.view = View.Leaderboard;
        }

        if (RetroLcd.Key("##brkExit", Dalamud.Interface.FontAwesomeIcon.ArrowLeft,
            winPos + new Vector2(ctx.Px(12f), ctx.Px(12f)), ctx.Px(30f)))
        {
            ctx.Shell.GoHomeToFolder(IOsShell.ArcadeFolderId);
        }

        if (BestScore > 0)
        {
            var best = string.Format(ctx.Localize("os.ark_best"), BestScore);
            var bestSize = ImGui.CalcTextSize(best);
            dl.AddText(winPos + new Vector2((winSize.X - bestSize.X) * 0.5f, winSize.Y - ctx.Px(30f)),
                ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.7f }), best);
        }
    }

    /// <summary>A couple of brick rows with a ball tracking back and forth beneath them.</summary>
    private static void DrawDecorBricks(ImDrawListPtr dl, Vector2 winPos, Vector2 winSize, double now, bool reduceMotion)
    {
        var brickW = winSize.X * 0.09f;
        var brickH = brickW * 0.4f;
        var startX = winPos.X + ((winSize.X - (brickW * 7f)) * 0.5f);
        var startY = winPos.Y + (winSize.Y * 0.38f);
        for (var row = 0; row < 2; row++)
        {
            for (var col = 0; col < 7; col++)
            {
                var tl = new Vector2(startX + (col * brickW), startY + (row * (brickH + 3f)));
                dl.AddRectFilled(tl, tl + new Vector2(brickW - 3f, brickH - 2f),
                    ImGui.GetColorU32(RetroLcd.Pixel with { W = row == 0 ? 0.9f : 0.6f }));
            }
        }
        var sweep = reduceMotion ? 0.5f : (float)((Math.Sin(now * 1.8) + 1.0) * 0.5);
        var ballX = startX + (sweep * ((brickW * 7f) - brickH));
        var ballY = startY + (brickH * 2f) + winSize.Y * 0.05f;
        dl.AddCircleFilled(new Vector2(ballX + (brickH * 0.5f), ballY), brickH * 0.35f,
            ImGui.GetColorU32(RetroLcd.Pixel));
    }

    private void DrawPlaying(OsAppContext ctx, double delta, Vector2 winPos, Vector2 winSize)
    {
        var hudH = ctx.Px(28f);
        var padH = winSize.Y * 0.16f;
        var padX = ctx.Px(8f);
        var boardMaxW = winSize.X - (padX * 2f);
        var boardMaxH = winSize.Y - hudH - padH - ctx.Px(8f);
        var cell = MathF.Max(2f,
            MathF.Min(boardMaxW / BreakerGame.Columns, boardMaxH / BreakerGame.Rows));
        var boardW = cell * BreakerGame.Columns;
        var boardH = cell * BreakerGame.Rows;
        var boardTL = new Vector2(winPos.X + ((winSize.X - boardW) * 0.5f), winPos.Y + hudH);

        if (RetroLcd.WindowBlurred() || this.keys.GameTextFocused)
        {
            this.paused = true;
        }

        if (!this.paused)
        {
            ReadInput(ctx, delta, boardTL, boardW, boardH, cell);
            this.runSeconds += delta;
            this.game.Tick(delta);
            if (this.game.Dead)
            {
                this.view = View.GameOver;
                FinishRun();
            }
        }

        DrawHud(ctx, winPos, winSize, hudH);
        DrawBoard(ctx, boardTL, boardW, boardH, cell);
        if (this.paused)
        {
            DrawPausedOverlay(ctx, boardTL, boardW, boardH);
        }
        else
        {
            DrawControls(winPos, winSize, padH, ctx);
        }
    }

    private void DrawHud(OsAppContext ctx, Vector2 winPos, Vector2 winSize, float hudH)
    {
        var dl = ImGui.GetWindowDrawList();
        var padX = ctx.Px(12f);
        var textY = winPos.Y + ((hudH - ImGui.GetTextLineHeight()) * 0.5f);
        dl.AddText(new Vector2(winPos.X + padX, textY), ImGui.GetColorU32(RetroLcd.Pixel),
            string.Format(ctx.Localize("os.ark_score"), this.game.Score));

        var middle = string.Format(ctx.Localize("os.ark_level"), this.game.LevelNumber);
        var middleSize = ImGui.CalcTextSize(middle);
        dl.AddText(new Vector2(winPos.X + ((winSize.X - middleSize.X) * 0.5f), textY),
            ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.75f }), middle);

        if (!this.paused && RetroLcd.PauseKey(winPos, winSize, padX, hudH))
        {
            this.paused = true;
        }

        // Lives as little paddles rather than a number, the way the cabinets did it.
        var pip = ctx.Px(10f);
        var reserve = RetroLcd.PauseKeyWidth(hudH) + ctx.Px(8f);
        for (var i = 0; i < Math.Min(this.game.Lives, 6); i++)
        {
            var tl = new Vector2(winPos.X + winSize.X - padX - reserve - ((i + 1) * (pip + ctx.Px(4f))),
                winPos.Y + (hudH * 0.5f) - ctx.Px(2f));
            dl.AddRectFilled(tl, tl + new Vector2(pip, ctx.Px(4f)), ImGui.GetColorU32(RetroLcd.Pixel));
        }
    }

    private void DrawBoard(OsAppContext ctx, Vector2 boardTL, float boardW, float boardH, float cell)
    {
        var dl = ImGui.GetWindowDrawList();
        dl.AddRect(boardTL - new Vector2(2f, 2f), boardTL + new Vector2(boardW + 2f, boardH + 2f),
            ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.55f }), 0f, ImDrawFlags.None, 2f);

        for (var x = 0; x < BreakerGame.Columns; x++)
        {
            for (var y = 0; y < BreakerGame.BrickRows; y++)
            {
                var hits = this.game.Brick(x, y);
                if (hits == 0)
                {
                    continue;
                }
                var tl = boardTL + new Vector2(x * cell, y * cell);
                var br = tl + new Vector2(cell - 2f, cell - 2f);
                if (hits < 0)
                {
                    // Indestructible: hatched so it reads as "don't bother".
                    dl.AddRectFilled(tl, br, ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.35f }));
                    dl.AddLine(tl, br, ImGui.GetColorU32(RetroLcd.Pixel), 1.5f);
                    dl.AddLine(new Vector2(br.X, tl.Y), new Vector2(tl.X, br.Y),
                        ImGui.GetColorU32(RetroLcd.Pixel), 1.5f);
                }
                else
                {
                    // More hits left reads as a more solid block.
                    var alpha = hits switch { 1 => 0.45f, 2 => 0.72f, _ => 1f };
                    dl.AddRectFilled(tl, br, ImGui.GetColorU32(RetroLcd.Pixel with { W = alpha }));
                    dl.AddRect(tl, br, ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.9f }), 0f, ImDrawFlags.None, 1f);
                }
            }
        }

        foreach (var capsule in this.game.Capsules)
        {
            var center = boardTL + new Vector2(capsule.X * cell, capsule.Y * cell);
            var w = cell * 0.42f;
            dl.AddRectFilled(center - new Vector2(w, w * 0.5f), center + new Vector2(w, w * 0.5f),
                ImGui.GetColorU32(RetroLcd.Pixel), w * 0.5f);
            var letter = capsule.Kind switch
            {
                PowerKind.Wide => "W",
                PowerKind.Multi => "M",
                PowerKind.Slow => "S",
                PowerKind.Life => "L",
                _ => "P",
            };
            var size = ImGui.CalcTextSize(letter);
            dl.AddText(center - (size * 0.5f), ImGui.GetColorU32(RetroLcd.Panel), letter);
        }

        var paddleHalf = this.game.PaddleWidth * 0.5f * cell;
        var paddleCenter = boardTL + new Vector2(this.game.PaddleX * cell, this.game.PaddleTop * cell);
        dl.AddRectFilled(paddleCenter - new Vector2(paddleHalf, cell * 0.18f),
            paddleCenter + new Vector2(paddleHalf, cell * 0.18f),
            ImGui.GetColorU32(RetroLcd.Pixel), cell * 0.18f);

        foreach (var ball in this.game.Balls)
        {
            dl.AddCircleFilled(boardTL + new Vector2(ball.X * cell, ball.Y * cell),
                BreakerGame.Radius * cell, ImGui.GetColorU32(RetroLcd.Pixel), 16);
        }

        if (this.game.AwaitingLaunch)
        {
            var hint = ctx.Localize("os.ark_launch_hint");
            var size = ImGui.CalcTextSize(hint);
            dl.AddText(boardTL + new Vector2((boardW - size.X) * 0.5f, boardH * 0.55f),
                ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.8f }), hint);
        }
    }

    /// <summary>Dragging anywhere on the board slides the paddle, which is how this plays best on a
    /// phone; the keyboard and the pad keys glide it at a fixed speed instead.</summary>
    private void ReadInput(OsAppContext ctx, double delta, Vector2 boardTL, float boardW, float boardH, float cell)
    {
        ImGui.SetCursorScreenPos(boardTL);
        ImGui.InvisibleButton("##brkBoard", new Vector2(boardW, boardH));
        if (ImGui.IsItemActive())
        {
            var mouseX = ImGui.GetIO().MousePos.X;
            var target = (mouseX - boardTL.X) / cell;
            this.game.MovePaddle(target - this.game.PaddleX);
        }
        if (ImGui.IsItemDeactivated())
        {
            this.game.Launch();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        var glide = (float)delta * BreakerGame.PaddleGlideSpeed;
        if (this.keys.IsDown(AppKey.Left) || this.keys.IsDown(AppKey.A))
        {
            this.game.MovePaddle(-glide);
        }
        if (this.keys.IsDown(AppKey.Right) || this.keys.IsDown(AppKey.D))
        {
            this.game.MovePaddle(glide);
        }
        if (this.keys.WasPressed(AppKey.Space) || this.keys.WasPressed(AppKey.Up)
            || this.keys.WasPressed(AppKey.W))
        {
            this.game.Launch();
        }
    }

    private void DrawControls(Vector2 winPos, Vector2 winSize, float padH, OsAppContext ctx)
    {
        var key = MathF.Min(padH * 0.72f, winSize.X * 0.17f);
        var centerY = winPos.Y + winSize.Y - (padH * 0.55f);
        var glide = (float)ImGui.GetIO().DeltaTime * BreakerGame.PaddleGlideSpeed;

        if (RetroLcd.KeyLabelHeld("##brkLeft", "A",
            new Vector2(winPos.X + (winSize.X * 0.18f) - (key * 0.5f), centerY - (key * 0.5f)), key))
        {
            this.game.MovePaddle(-glide);
        }
        if (RetroLcd.KeyLabelHeld("##brkRight", "D",
            new Vector2(winPos.X + (winSize.X * 0.82f) - (key * 0.5f), centerY - (key * 0.5f)), key))
        {
            this.game.MovePaddle(glide);
        }
        if (this.game.AwaitingLaunch && RetroLcd.KeyLabel("##brkLaunch", "W",
            new Vector2(winPos.X + (winSize.X * 0.5f) - (key * 0.5f), centerY - (key * 0.5f)), key))
        {
            this.game.Launch();
        }
    }

    private void DrawPausedOverlay(OsAppContext ctx, Vector2 boardTL, float boardW, float boardH)
    {
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(boardTL, boardTL + new Vector2(boardW, boardH),
            ImGui.GetColorU32(RetroLcd.Panel with { W = 0.82f }));
        var label = ctx.Localize("os.ark_paused");
        var labelSize = ImGui.CalcTextSize(label);
        dl.AddText(boardTL + new Vector2((boardW - labelSize.X) * 0.5f, (boardH * 0.5f) - ctx.Px(30f)),
            ImGui.GetColorU32(RetroLcd.Pixel), label);

        var buttonW = boardW * 0.7f;
        var buttonH = ctx.Px(36f);
        if (RetroLcd.Button("##brkResume", ctx.Localize("os.ark_resume"),
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
        var titleKey = this.lastRunWasRecord ? "os.ark_new_record" : "os.ark_game_over";
        using (ctx.TitleFont?.Push())
        {
            var title = ctx.Localize(titleKey);
            var titleSize = ImGui.CalcTextSize(title);
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
                winPos + new Vector2((winSize.X - titleSize.X) * 0.5f, winSize.Y * 0.20f),
                ImGui.GetColorU32(RetroLcd.Pixel), title);
        }

        var lines = new[]
        {
            string.Format(ctx.Localize("os.ark_score"), this.lastRunScore),
            string.Format(ctx.Localize("os.ark_reached"), this.lastRunLevel),
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
        if (RetroLcd.Button("##brkAgain", ctx.Localize("os.ark_play_again"),
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, firstY), new Vector2(buttonW, buttonH),
            ctx.Px(4f), filled: true))
        {
            StartRun();
        }
        if (RetroLcd.Button("##brkMenu", ctx.Localize("os.ark_menu"),
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
            var title = ctx.Localize("os.ark_high_scores");
            var titleSize = ImGui.CalcTextSize(title);
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
                winPos + new Vector2((winSize.X - titleSize.X) * 0.5f, winSize.Y * 0.12f),
                ImGui.GetColorU32(RetroLcd.Pixel), title);
        }

        var padX = ctx.Px(28f);
        var y = winSize.Y * 0.26f;
        if (this.highScores.Length == 0)
        {
            var empty = ctx.Localize("os.ark_no_scores");
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
        if (RetroLcd.Button("##brkBack", ctx.Localize("os.ark_menu"),
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, winSize.Y - buttonH - ctx.Px(24f)),
            new Vector2(buttonW, buttonH), ctx.Px(4f), filled: false))
        {
            this.splashStartedAt = ImGui.GetTime();
            this.view = View.Splash;
        }
    }
}
