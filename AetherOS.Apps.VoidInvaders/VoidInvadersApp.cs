using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AetherLove.Shared.Arcade;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.VoidInvaders;

/// <summary>Void Invaders on the shared handheld LCD: hold to slide the cannon, tap to fire, watch the
/// shields crumble one cell at a time.</summary>
public sealed class VoidInvadersApp : IAetherApp
{
    private enum View { Splash, Playing, GameOver, Scores, Leaderboard }

    private static readonly Vector4 TileTopColor = new(0.44f, 0.35f, 0.72f, 1f);
    private static readonly Vector4 TileBottomColor = new(0.13f, 0.10f, 0.28f, 1f);

    private const string HighScoresKey = "high_scores";
    private const int ScoreSlots = 5;
    private const int SpriteColumns = 8;
    private const int SpriteRows = 6;

    /// <summary>Three silhouettes, two frames each; the frame flips on every formation step so the whole
    /// rank appears to walk.</summary>
    private static readonly string[][][] Sprites =
    [
        [
            ["...##...", "..####..", ".######.", "##.##.##", "########", "..#..#.."],
            ["...##...", "..####..", ".######.", "##.##.##", "########", ".#.##.#."],
        ],
        [
            ["..#..#..", "#.####.#", "########", "##.##.##", ".######.", "#......#"],
            ["..#..#..", ".######.", "########", "##.##.##", ".######.", "..#..#.."],
        ],
        [
            [".######.", "########", "##.##.##", "########", ".#.##.#.", "#.#..#.#"],
            [".######.", "########", "##.##.##", "########", "..#..#..", ".##..##."],
        ],
    ];

    private readonly Func<string> name;
    private readonly IAppStorage storage;
    private readonly IKeyboardInput keys;
    private readonly AetherLove.Os.IArcadeRewards rewards;
    private readonly AetherLove.Os.IArcadeScores scores;
    private readonly AetherLove.Widgets.ArcadeLeaderboardView leaderboard;
    private readonly VoidInvadersGame game = new();

    private View view = View.Splash;
    private bool paused;
    private double lastFrameTime;
    private double splashStartedAt;
    private int[] highScores = [];
    private bool scoresLoaded;
    private int lastRunScore;
    private int lastRunWave;
    private bool lastRunWasRecord;
    private double runSeconds;

    public VoidInvadersApp(Func<string> name, IAppCapabilities capabilities, AetherLove.Os.IArcadeRewards rewards, AetherLove.Os.IArcadeScores scores)
    {
        this.name = name;
        this.storage = capabilities.Storage("invaders");
        this.keys = capabilities.Keyboard;
        this.rewards = rewards;
        this.scores = scores;
        this.leaderboard = new AetherLove.Widgets.ArcadeLeaderboardView(scores, ArcadeGame.Invaders);
    }

    public string Id => "invaders";

    public string Name => this.name();

    public FontAwesomeIcon Icon => FontAwesomeIcon.SpaceShuttle;

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
        var delta = (float)Math.Clamp(now - this.lastFrameTime, 0.0, 0.5);
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
        this.lastRunWave = this.game.Wave;
        this.lastRunWasRecord = this.lastRunScore > BestScore;
        this.highScores = this.highScores
            .Append(this.lastRunScore)
            .OrderByDescending(s => s)
            .Take(ScoreSlots)
            .ToArray();
        this.storage.Set(HighScoresKey, this.highScores);
        this.rewards.NoteGameFinished();
        this.scores.SubmitScore(new ArcadeScoreSubmissionDto(
            ArcadeGame.Invaders, this.lastRunScore, (int)(this.runSeconds * 1000.0), this.lastRunWave));
    }

    private static void DrawSprite(ImDrawListPtr dl, int kind, int frame, Vector2 tl, float unit, uint color)
    {
        var rows = Sprites[kind][frame];
        var cell = unit;
        for (var r = 0; r < SpriteRows; r++)
        {
            for (var c = 0; c < SpriteColumns; c++)
            {
                if (rows[r][c] != '#')
                {
                    continue;
                }
                var cellTL = tl + new Vector2(c * cell, r * cell);
                dl.AddRectFilled(cellTL, cellTL + new Vector2(cell, cell), color);
            }
        }
    }

    private void DrawSplash(OsAppContext ctx, double now, Vector2 winPos, Vector2 winSize)
    {
        var dl = ImGui.GetWindowDrawList();
        var elapsed = now - this.splashStartedAt;
        var litRows = ctx.ReduceMotion
            ? RetroLcd.GlyphHeight
            : (int)Math.Min(RetroLcd.GlyphHeight, elapsed / 0.09);

        var pixel = MathF.Max(2f, winSize.X * 0.62f / RetroLcd.WordColumns("VOID"));
        var wordY = winSize.Y * 0.12f;
        RetroLcd.DrawWordCentered(dl, "VOID", winPos + new Vector2(0f, wordY), winSize.X, pixel,
            ImGui.GetColorU32(RetroLcd.Pixel), litRows);

        var small = MathF.Max(1.5f, pixel * 0.5f);
        RetroLcd.DrawWordCentered(dl, "INVADERS",
            winPos + new Vector2(0f, wordY + (RetroLcd.GlyphHeight * pixel) + ctx.Px(8f)), winSize.X, small,
            ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.8f }), litRows);

        var subtitle = ctx.Localize("os.invaders_subtitle");
        var subSize = ImGui.CalcTextSize(subtitle);
        dl.AddText(winPos + new Vector2((winSize.X - subSize.X) * 0.5f,
            wordY + (RetroLcd.GlyphHeight * (pixel + small)) + ctx.Px(22f)),
            ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.75f }), subtitle);

        // A demo rank that shuffles sideways under the title, sized so the bottom row clears the buttons.
        var rankUnits = (2f * 8f) + SpriteRows;
        var unit = MathF.Min(winSize.X * 0.018f, ((winSize.Y * 0.16f) - ctx.Px(12f)) / rankUnits);
        var bob = ctx.ReduceMotion ? 0f : (float)Math.Sin(now * 1.4) * unit * 2f;
        var frame = ctx.ReduceMotion || ((int)(now * 2) % 2 == 0) ? 0 : 1;
        var rowY = winPos.Y + (winSize.Y * 0.40f);
        var rankW = ((2f * 11f) + SpriteColumns) * unit;
        for (var kind = 0; kind < 3; kind++)
        {
            for (var i = 0; i < 3; i++)
            {
                var x = winPos.X + ((winSize.X - rankW) * 0.5f) + (i * unit * 11f) + bob;
                DrawSprite(dl, kind, frame, new Vector2(x, rowY + (kind * unit * 8f)), unit,
                    ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.9f }));
            }
        }

        var buttonW = winSize.X * 0.62f;
        var buttonH = ctx.Px(38f);
        var firstY = winSize.Y * 0.56f;
        if (RetroLcd.Button("##invPlay", ctx.Localize("os.invaders_play"),
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, firstY), new Vector2(buttonW, buttonH),
            ctx.Px(4f), filled: true))
        {
            StartRun();
        }
        if (RetroLcd.Button("##invScores", ctx.Localize("os.invaders_high_scores"),
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, firstY + buttonH + ctx.Px(10f)),
            new Vector2(buttonW, buttonH), ctx.Px(4f), filled: false))
        {
            this.view = View.Scores;
        }
        if (RetroLcd.Button("##invBoard", ctx.Localize("os.arcade_leaderboard"),
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, firstY + ((buttonH + ctx.Px(10f)) * 2f)),
            new Vector2(buttonW, buttonH), ctx.Px(4f), filled: false))
        {
            this.view = View.Leaderboard;
        }

        if (RetroLcd.Key("##invExit", Dalamud.Interface.FontAwesomeIcon.ArrowLeft,
            winPos + new Vector2(ctx.Px(12f), ctx.Px(12f)), ctx.Px(30f)))
        {
            ctx.Shell.GoHomeToFolder(IOsShell.ArcadeFolderId);
        }

        if (BestScore > 0)
        {
            var best = string.Format(ctx.Localize("os.invaders_best"), BestScore);
            var bestSize = ImGui.CalcTextSize(best);
            dl.AddText(winPos + new Vector2((winSize.X - bestSize.X) * 0.5f, winSize.Y - ctx.Px(30f)),
                ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.7f }), best);
        }
    }

    private void DrawPlaying(OsAppContext ctx, float delta, Vector2 winPos, Vector2 winSize)
    {
        var hudH = ctx.Px(44f);
        var padH = winSize.Y * 0.19f;
        var boardMaxW = winSize.X - ctx.Px(12f);
        var boardMaxH = winSize.Y - hudH - padH;
        var scale = MathF.Min(boardMaxW / VoidInvadersGame.Width, boardMaxH / VoidInvadersGame.Height);
        var boardW = VoidInvadersGame.Width * scale;
        var boardH = VoidInvadersGame.Height * scale;
        var boardTL = new Vector2(winPos.X + ((winSize.X - boardW) * 0.5f), winPos.Y + hudH);

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

        DrawHud(ctx, winPos, winSize, hudH, scale);
        DrawField(boardTL, scale, boardW, boardH);
        if (this.paused)
        {
            DrawPausedOverlay(ctx, boardTL, boardW, boardH);
        }
        else
        {
            DrawControls(delta, winPos, winSize, padH);
        }
    }

    private void DrawHud(OsAppContext ctx, Vector2 winPos, Vector2 winSize, float hudH, float scale)
    {
        var dl = ImGui.GetWindowDrawList();
        var padX = ctx.Px(12f);
        var line = ImGui.GetTextLineHeight();
        dl.AddText(winPos + new Vector2(padX, ctx.Px(6f)), ImGui.GetColorU32(RetroLcd.Pixel),
            string.Format(ctx.Localize("os.invaders_score"), this.game.Score));
        dl.AddText(winPos + new Vector2(padX, ctx.Px(6f) + line), ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.75f }),
            string.Format(ctx.Localize("os.invaders_wave"), this.game.Wave));

        if (!this.paused && RetroLcd.PauseKey(winPos, winSize, padX, hudH))
        {
            this.paused = true;
        }

        var pip = MathF.Max(4f, scale * 5f);
        var x = winPos.X + winSize.X - padX - RetroLcd.PauseKeyWidth(hudH) - ctx.Px(8f) - pip;
        for (var i = 0; i < this.game.Lives; i++)
        {
            DrawCannon(dl, new Vector2(x, winPos.Y + ctx.Px(10f)), pip, pip * 0.55f,
                ImGui.GetColorU32(RetroLcd.Pixel));
            x -= pip * 1.4f;
        }
    }

    private static void DrawCannon(ImDrawListPtr dl, Vector2 tl, float width, float height, uint color)
    {
        dl.AddRectFilled(tl + new Vector2(0f, height * 0.45f), tl + new Vector2(width, height), color);
        dl.AddRectFilled(tl + new Vector2(width * 0.4f, 0f), tl + new Vector2(width * 0.6f, height * 0.5f), color);
    }

    private void DrawField(Vector2 boardTL, float scale, float boardW, float boardH)
    {
        var dl = ImGui.GetWindowDrawList();
        var ink = ImGui.GetColorU32(RetroLcd.Pixel);
        dl.AddRect(boardTL - new Vector2(2f, 2f), boardTL + new Vector2(boardW + 2f, boardH + 2f),
            ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.5f }), 0f, ImDrawFlags.None, 2f);

        var frame = this.game.AnimFrame ? 1 : 0;
        var unit = VoidInvadersGame.InvaderWidth / SpriteColumns * scale;
        for (var c = 0; c < VoidInvadersGame.Columns; c++)
        {
            for (var r = 0; r < VoidInvadersGame.Rows; r++)
            {
                if (!this.game.InvaderAlive(c, r))
                {
                    continue;
                }
                DrawSprite(dl, VoidInvadersGame.KindForRow(r), frame,
                    boardTL + (this.game.InvaderPos(c, r) * scale), unit, ink);
            }
        }

        for (var s = 0; s < VoidInvadersGame.ShieldCount; s++)
        {
            for (var c = 0; c < VoidInvadersGame.ShieldColumns; c++)
            {
                for (var r = 0; r < VoidInvadersGame.ShieldRows; r++)
                {
                    if (!this.game.ShieldCellAlive(s, c, r))
                    {
                        continue;
                    }
                    var tl = boardTL + (VoidInvadersGame.ShieldCellPos(s, c, r) * scale);
                    var size = VoidInvadersGame.ShieldCell * scale;
                    dl.AddRectFilled(tl, tl + new Vector2(size - 1f, size - 1f),
                        ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.8f }));
                }
            }
        }

        var playerTL = boardTL + new Vector2(
            (this.game.PlayerX - (VoidInvadersGame.PlayerWidth * 0.5f)) * scale,
            (VoidInvadersGame.PlayerRowY - VoidInvadersGame.PlayerHeight) * scale);
        DrawCannon(dl, playerTL, VoidInvadersGame.PlayerWidth * scale, VoidInvadersGame.PlayerHeight * scale, ink);

        if (this.game.Bullet is { } bullet)
        {
            var pos = boardTL + (bullet * scale);
            dl.AddRectFilled(pos - new Vector2(1f, 3f * scale), pos + new Vector2(1f, 0f), ink);
        }

        foreach (var bomb in this.game.Bombs)
        {
            var pos = boardTL + (bomb.Pos * scale);
            dl.AddRectFilled(pos - new Vector2(1f, 0f), pos + new Vector2(1f, 3f * scale), ink);
        }
    }

    private void ReadKeyboard(float delta)
    {
        if (this.keys.IsDown(AppKey.Left) || this.keys.IsDown(AppKey.A))
        {
            this.game.MoveLeft(delta);
        }
        if (this.keys.IsDown(AppKey.Right) || this.keys.IsDown(AppKey.D))
        {
            this.game.MoveRight(delta);
        }
        if (this.keys.WasPressed(AppKey.Space) || this.keys.WasPressed(AppKey.Up) || this.keys.WasPressed(AppKey.W))
        {
            this.game.Fire();
        }
    }

    private void DrawControls(float delta, Vector2 winPos, Vector2 winSize, float padH)
    {
        var key = MathF.Min(padH * 0.55f, winSize.X * 0.19f);
        var rowY = winPos.Y + winSize.Y - (padH * 0.72f);
        var gap = key * 0.35f;
        var totalW = (key * 3f) + (gap * 2f);
        var x = winPos.X + ((winSize.X - totalW) * 0.5f);

        if (RetroLcd.KeyLabelHeld("##invLeft", "A", new Vector2(x, rowY), key))
        {
            this.game.MoveLeft(delta);
        }
        x += key + gap;
        if (RetroLcd.KeyLabel("##invFire", "W", new Vector2(x, rowY), key))
        {
            this.game.Fire();
        }
        x += key + gap;
        if (RetroLcd.KeyLabelHeld("##invRight", "D", new Vector2(x, rowY), key))
        {
            this.game.MoveRight(delta);
        }
    }

    private void DrawPausedOverlay(OsAppContext ctx, Vector2 boardTL, float boardW, float boardH)
    {
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(boardTL, boardTL + new Vector2(boardW, boardH),
            ImGui.GetColorU32(RetroLcd.Panel with { W = 0.82f }));
        var label = ctx.Localize("os.invaders_paused");
        var labelSize = ImGui.CalcTextSize(label);
        dl.AddText(boardTL + new Vector2((boardW - labelSize.X) * 0.5f, (boardH * 0.5f) - ctx.Px(30f)),
            ImGui.GetColorU32(RetroLcd.Pixel), label);

        var buttonW = boardW * 0.7f;
        var buttonH = ctx.Px(36f);
        if (RetroLcd.Button("##invResume", ctx.Localize("os.invaders_resume"),
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
        var title = ctx.Localize(this.lastRunWasRecord ? "os.invaders_new_record" : "os.invaders_game_over");
        using (ctx.TitleFont?.Push())
        {
            var titleSize = ImGui.CalcTextSize(title);
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
                winPos + new Vector2((winSize.X - titleSize.X) * 0.5f, winSize.Y * 0.20f),
                ImGui.GetColorU32(RetroLcd.Pixel), title);
        }

        var lines = new[]
        {
            string.Format(ctx.Localize("os.invaders_score"), this.lastRunScore),
            string.Format(ctx.Localize("os.invaders_reached"), this.lastRunWave),
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
        if (RetroLcd.Button("##invAgain", ctx.Localize("os.invaders_play_again"),
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, firstY), new Vector2(buttonW, buttonH),
            ctx.Px(4f), filled: true))
        {
            StartRun();
        }
        if (RetroLcd.Button("##invMenu", ctx.Localize("os.invaders_menu"),
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
            var title = ctx.Localize("os.invaders_high_scores");
            var titleSize = ImGui.CalcTextSize(title);
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
                winPos + new Vector2((winSize.X - titleSize.X) * 0.5f, winSize.Y * 0.12f),
                ImGui.GetColorU32(RetroLcd.Pixel), title);
        }

        var padX = ctx.Px(28f);
        var y = winSize.Y * 0.26f;
        if (this.highScores.Length == 0)
        {
            var empty = ctx.Localize("os.invaders_no_scores");
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
        if (RetroLcd.Button("##invBack", ctx.Localize("os.invaders_menu"),
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, winSize.Y - buttonH - ctx.Px(24f)),
            new Vector2(buttonW, buttonH), ctx.Px(4f), filled: false))
        {
            this.splashStartedAt = ImGui.GetTime();
            this.view = View.Splash;
        }
    }
}
