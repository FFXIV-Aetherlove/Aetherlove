using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AetherLove.Shared.Arcade;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.MazeMuncher;

/// <summary>Maze Muncher on the shared handheld LCD: clear the dots, dodge four ghosts, and eat them back
/// for a few seconds after a power pellet.</summary>
public sealed class MazeMuncherApp : IAetherApp
{
    private enum View { Splash, Playing, GameOver, Scores, Leaderboard }

    private static readonly Vector4 TileTopColor = new(0.90f, 0.78f, 0.29f, 1f);
    private static readonly Vector4 TileBottomColor = new(0.42f, 0.28f, 0.06f, 1f);

    private const string HighScoresKey = "high_scores";
    private const int ScoreSlots = 5;

    private readonly Func<string> name;
    private readonly IAppStorage storage;
    private readonly IKeyboardInput keys;
    private readonly AetherLove.Os.IArcadeRewards rewards;
    private readonly AetherLove.Os.IArcadeScores scores;
    private readonly AetherLove.Widgets.ArcadeLeaderboardView leaderboard;
    private readonly MazeMuncherGame game = new();

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

    public MazeMuncherApp(Func<string> name, IAppCapabilities capabilities, AetherLove.Os.IArcadeRewards rewards, AetherLove.Os.IArcadeScores scores)
    {
        this.name = name;
        this.storage = capabilities.Storage("muncher");
        this.keys = capabilities.Keyboard;
        this.rewards = rewards;
        this.scores = scores;
        this.leaderboard = new AetherLove.Widgets.ArcadeLeaderboardView(scores, ArcadeGame.Muncher);
    }

    public string Id => "muncher";

    public string Name => this.name();

    public FontAwesomeIcon Icon => FontAwesomeIcon.Ghost;

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
                DrawPlaying(ctx, now, delta, winPos, winSize);
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
        this.lastRunLevel = this.game.Level;
        this.lastRunWasRecord = this.lastRunScore > BestScore;
        this.highScores = this.highScores
            .Append(this.lastRunScore)
            .OrderByDescending(s => s)
            .Take(ScoreSlots)
            .ToArray();
        this.storage.Set(HighScoresKey, this.highScores);
        this.rewards.NoteGameFinished();
        this.scores.SubmitScore(new ArcadeScoreSubmissionDto(
            ArcadeGame.Muncher, this.lastRunScore, (int)(this.runSeconds * 1000.0), this.lastRunLevel));
    }

    /// <summary>The muncher itself: a disc with a wedge bitten out of it, facing its heading.</summary>
    private static void DrawMuncher(ImDrawListPtr dl, Vector2 center, float radius, Vector2 dir, float mouth, uint color)
    {
        var angle = dir == Vector2.Zero ? 0f : MathF.Atan2(dir.Y, dir.X);
        if (mouth <= 0.01f)
        {
            dl.AddCircleFilled(center, radius, color, 24);
            return;
        }
        dl.PathClear();
        dl.PathLineTo(center);
        dl.PathArcTo(center, radius, angle + mouth, angle + MathF.Tau - mouth, 24);
        dl.PathFillConvex(color);
    }

    private static void DrawGhost(ImDrawListPtr dl, Vector2 center, float radius, GhostState state, uint ink, uint panel)
    {
        var top = center.Y - (radius * 0.15f);
        var left = center.X - radius;
        var right = center.X + radius;
        var bottom = center.Y + radius;

        if (state != GhostState.Eyes)
        {
            if (state == GhostState.Frightened)
            {
                dl.AddCircle(new Vector2(center.X, top), radius, ink, 20, 2f);
                dl.AddRect(new Vector2(left, top), new Vector2(right, bottom), ink, 0f, ImDrawFlags.None, 2f);
            }
            else
            {
                dl.AddCircleFilled(new Vector2(center.X, top), radius, ink, 20);
                dl.AddRectFilled(new Vector2(left, top), new Vector2(right, bottom), ink);
                // Three notches along the hem read as the classic ragged skirt.
                var notch = (right - left) / 3f;
                for (var i = 0; i < 3; i++)
                {
                    dl.AddTriangleFilled(
                        new Vector2(left + (i * notch), bottom),
                        new Vector2(left + ((i + 0.5f) * notch), bottom - (radius * 0.45f)),
                        new Vector2(left + ((i + 1) * notch), bottom),
                        panel);
                }
            }
        }

        var eyeR = radius * 0.26f;
        var eyeY = top - (radius * 0.12f);
        var eyeColor = state == GhostState.Frightened ? ink : panel;
        dl.AddCircleFilled(new Vector2(center.X - (radius * 0.38f), eyeY), eyeR, eyeColor, 10);
        dl.AddCircleFilled(new Vector2(center.X + (radius * 0.38f), eyeY), eyeR, eyeColor, 10);
        if (state == GhostState.Eyes)
        {
            dl.AddCircle(new Vector2(center.X - (radius * 0.38f), eyeY), eyeR, ink, 10, 1.5f);
            dl.AddCircle(new Vector2(center.X + (radius * 0.38f), eyeY), eyeR, ink, 10, 1.5f);
        }
    }

    private void DrawSplash(OsAppContext ctx, double now, Vector2 winPos, Vector2 winSize)
    {
        var dl = ImGui.GetWindowDrawList();
        var elapsed = now - this.splashStartedAt;
        var litRows = ctx.ReduceMotion
            ? RetroLcd.GlyphHeight
            : (int)Math.Min(RetroLcd.GlyphHeight, elapsed / 0.09);

        var pixel = MathF.Max(2f, winSize.X * 0.66f / RetroLcd.WordColumns("MAZE"));
        var wordY = winSize.Y * 0.12f;
        RetroLcd.DrawWordCentered(dl, "MAZE", winPos + new Vector2(0f, wordY), winSize.X, pixel,
            ImGui.GetColorU32(RetroLcd.Pixel), litRows);

        var small = MathF.Max(1.5f, pixel * 0.52f);
        RetroLcd.DrawWordCentered(dl, "MUNCHER",
            winPos + new Vector2(0f, wordY + (RetroLcd.GlyphHeight * pixel) + ctx.Px(8f)), winSize.X, small,
            ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.8f }), litRows);

        var subtitle = ctx.Localize("os.muncher_subtitle");
        var subSize = ImGui.CalcTextSize(subtitle);
        dl.AddText(winPos + new Vector2((winSize.X - subSize.X) * 0.5f,
            wordY + (RetroLcd.GlyphHeight * (pixel + small)) + ctx.Px(22f)),
            ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.75f }), subtitle);

        // A little chase across the middle of the panel.
        var ink = ImGui.GetColorU32(RetroLcd.Pixel);
        var panel = ImGui.GetColorU32(RetroLcd.Panel);
        var radius = winSize.X * 0.035f;
        var rowY = winPos.Y + (winSize.Y * 0.44f);
        var drift = ctx.ReduceMotion ? 0f : (float)((now * 0.5) % 1.0) * winSize.X * 0.2f;
        var mouth = ctx.ReduceMotion ? 0.3f : 0.15f + (MathF.Abs(MathF.Sin((float)now * 7f)) * 0.35f);
        for (var i = 0; i < 4; i++)
        {
            dl.AddCircleFilled(new Vector2(winPos.X + (winSize.X * 0.18f) + (i * radius * 1.6f), rowY),
                radius * 0.22f, ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.6f }), 8);
        }
        DrawMuncher(dl, new Vector2(winPos.X + (winSize.X * 0.50f) + drift, rowY), radius,
            new Vector2(1f, 0f), mouth, ink);
        DrawGhost(dl, new Vector2(winPos.X + (winSize.X * 0.68f) + drift, rowY), radius * 0.9f,
            GhostState.Normal, ink, panel);

        var buttonW = winSize.X * 0.62f;
        var buttonH = ctx.Px(38f);
        var firstY = winSize.Y * 0.54f;
        if (RetroLcd.Button("##munPlay", ctx.Localize("os.muncher_play"),
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, firstY), new Vector2(buttonW, buttonH),
            ctx.Px(4f), filled: true))
        {
            StartRun();
        }
        if (RetroLcd.Button("##munScores", ctx.Localize("os.muncher_high_scores"),
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, firstY + buttonH + ctx.Px(10f)),
            new Vector2(buttonW, buttonH), ctx.Px(4f), filled: false))
        {
            this.view = View.Scores;
        }
        if (RetroLcd.Button("##munBoard", ctx.Localize("os.arcade_leaderboard"),
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, firstY + ((buttonH + ctx.Px(10f)) * 2f)),
            new Vector2(buttonW, buttonH), ctx.Px(4f), filled: false))
        {
            this.view = View.Leaderboard;
        }

        if (RetroLcd.Key("##munExit", Dalamud.Interface.FontAwesomeIcon.ArrowLeft,
            winPos + new Vector2(ctx.Px(12f), ctx.Px(12f)), ctx.Px(30f)))
        {
            ctx.Shell.GoHomeToFolder(IOsShell.ArcadeFolderId);
        }

        if (BestScore > 0)
        {
            var best = string.Format(ctx.Localize("os.muncher_best"), BestScore);
            var bestSize = ImGui.CalcTextSize(best);
            dl.AddText(winPos + new Vector2((winSize.X - bestSize.X) * 0.5f, winSize.Y - ctx.Px(30f)),
                ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.7f }), best);
        }
    }

    private void DrawPlaying(OsAppContext ctx, double now, float delta, Vector2 winPos, Vector2 winSize)
    {
        var hudH = ctx.Px(44f);
        var padH = winSize.Y * 0.26f;
        var boardMaxW = winSize.X - ctx.Px(10f);
        var boardMaxH = winSize.Y - hudH - padH;
        var cell = MathF.Max(3f, MathF.Min(boardMaxW / MazeMuncherGame.Columns, boardMaxH / MazeMuncherGame.Rows));
        var boardW = cell * MazeMuncherGame.Columns;
        var boardH = cell * MazeMuncherGame.Rows;
        var boardTL = new Vector2(winPos.X + ((winSize.X - boardW) * 0.5f),
            winPos.Y + hudH + ((boardMaxH - boardH) * 0.5f));

        if (RetroLcd.WindowBlurred() || this.keys.GameTextFocused)
        {
            this.paused = true;
        }

        if (!this.paused)
        {
            ReadKeyboard();
            this.runSeconds += delta;
            this.game.Tick(delta);
            if (this.game.Dead)
            {
                this.view = View.GameOver;
                FinishRun();
            }
        }

        DrawHud(ctx, winPos, winSize, hudH, now);
        DrawMaze(ctx, now, boardTL, cell, boardW, boardH);
        if (this.paused)
        {
            DrawPausedOverlay(ctx, boardTL, boardW, boardH);
        }
        else
        {
            DrawControls(winPos, winSize, padH);
        }
    }

    private void DrawHud(OsAppContext ctx, Vector2 winPos, Vector2 winSize, float hudH, double now)
    {
        var dl = ImGui.GetWindowDrawList();
        var padX = ctx.Px(12f);
        var line = ImGui.GetTextLineHeight();
        dl.AddText(winPos + new Vector2(padX, ctx.Px(6f)), ImGui.GetColorU32(RetroLcd.Pixel),
            string.Format(ctx.Localize("os.muncher_score"), this.game.Score));
        dl.AddText(winPos + new Vector2(padX, ctx.Px(6f) + line), ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.75f }),
            string.Format(ctx.Localize("os.muncher_level"), this.game.Level));

        if (!this.paused && RetroLcd.PauseKey(winPos, winSize, padX, hudH))
        {
            this.paused = true;
        }

        var pip = ctx.Px(6f);
        var x = winPos.X + winSize.X - padX - RetroLcd.PauseKeyWidth(hudH) - ctx.Px(8f) - pip;
        for (var i = 0; i < this.game.Lives; i++)
        {
            DrawMuncher(dl, new Vector2(x, winPos.Y + ctx.Px(14f)), pip, new Vector2(1f, 0f), 0.3f,
                ImGui.GetColorU32(RetroLcd.Pixel));
            x -= pip * 2.6f;
        }
    }

    private void DrawMaze(OsAppContext ctx, double now, Vector2 boardTL, float cell, float boardW, float boardH)
    {
        var dl = ImGui.GetWindowDrawList();
        var ink = ImGui.GetColorU32(RetroLcd.Pixel);
        var panel = ImGui.GetColorU32(RetroLcd.Panel);
        var wallColor = ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.85f });

        for (var y = 0; y < MazeMuncherGame.Rows; y++)
        {
            for (var x = 0; x < MazeMuncherGame.Columns; x++)
            {
                var tile = this.game.TileAt(x, y);
                var tl = boardTL + new Vector2(x * cell, y * cell);
                switch (tile)
                {
                    case '#':
                        dl.AddRectFilled(tl + new Vector2(1f, 1f), tl + new Vector2(cell - 1f, cell - 1f),
                            wallColor, cell * 0.25f);
                        break;
                    case '-':
                        dl.AddRectFilled(tl + new Vector2(1f, cell * 0.4f),
                            tl + new Vector2(cell - 1f, cell * 0.6f), ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.5f }));
                        break;
                    case '.':
                        dl.AddRectFilled(tl + new Vector2(cell * 0.42f, cell * 0.42f),
                            tl + new Vector2(cell * 0.58f, cell * 0.58f), ink);
                        break;
                    case 'o':
                        var pulse = ctx.ReduceMotion ? 1f : 0.7f + (MathF.Abs(MathF.Sin((float)now * 4f)) * 0.5f);
                        dl.AddCircleFilled(tl + new Vector2(cell * 0.5f, cell * 0.5f), cell * 0.26f * pulse, ink, 12);
                        break;
                }
            }
        }

        foreach (var ghost in this.game.Ghosts)
        {
            var center = boardTL + ((ghost.Pos + new Vector2(0.5f, 0.5f)) * cell);
            DrawGhost(dl, center, cell * 0.42f, ghost.State, ink, panel);
        }

        if (!this.game.Dying)
        {
            var mouth = ctx.ReduceMotion ? 0.3f : MathF.Abs(MathF.Sin((float)now * 9f)) * 0.5f;
            var center = boardTL + ((this.game.PlayerPos + new Vector2(0.5f, 0.5f)) * cell);
            DrawMuncher(dl, center, cell * 0.42f, this.game.PlayerDir, mouth, ink);
        }

        if (this.game.Frozen && !this.game.Dying)
        {
            var label = ctx.Localize("os.muncher_ready");
            var labelSize = ImGui.CalcTextSize(label);
            dl.AddText(boardTL + new Vector2((boardW - labelSize.X) * 0.5f, boardH * 0.56f), ink, label);
        }
    }

    private void ReadKeyboard()
    {
        if (this.keys.WasPressed(AppKey.Up) || this.keys.WasPressed(AppKey.W))
        {
            this.game.Turn(new Vector2(0f, -1f));
        }
        else if (this.keys.WasPressed(AppKey.Down) || this.keys.WasPressed(AppKey.S))
        {
            this.game.Turn(new Vector2(0f, 1f));
        }
        else if (this.keys.WasPressed(AppKey.Left) || this.keys.WasPressed(AppKey.A))
        {
            this.game.Turn(new Vector2(-1f, 0f));
        }
        else if (this.keys.WasPressed(AppKey.Right) || this.keys.WasPressed(AppKey.D))
        {
            this.game.Turn(new Vector2(1f, 0f));
        }
    }

    private void DrawControls(Vector2 winPos, Vector2 winSize, float padH)
    {
        var key = MathF.Min(padH * 0.27f, winSize.X * 0.17f);
        var gap = key * 0.14f;
        var centerX = winPos.X + (winSize.X * 0.5f) - (key * 0.5f);
        var topY = winPos.Y + winSize.Y - padH + (padH * 0.08f);

        if (RetroLcd.KeyLabel("##munUp", "W", new Vector2(centerX, topY), key))
        {
            this.game.Turn(new Vector2(0f, -1f));
        }
        if (RetroLcd.KeyLabel("##munLeft", "A",
            new Vector2(centerX - key - gap, topY + key + gap), key))
        {
            this.game.Turn(new Vector2(-1f, 0f));
        }
        if (RetroLcd.KeyLabel("##munRight", "D",
            new Vector2(centerX + key + gap, topY + key + gap), key))
        {
            this.game.Turn(new Vector2(1f, 0f));
        }
        if (RetroLcd.KeyLabel("##munDown", "S",
            new Vector2(centerX, topY + ((key + gap) * 2f)), key))
        {
            this.game.Turn(new Vector2(0f, 1f));
        }
    }

    private void DrawPausedOverlay(OsAppContext ctx, Vector2 boardTL, float boardW, float boardH)
    {
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(boardTL, boardTL + new Vector2(boardW, boardH),
            ImGui.GetColorU32(RetroLcd.Panel with { W = 0.82f }));
        var label = ctx.Localize("os.muncher_paused");
        var labelSize = ImGui.CalcTextSize(label);
        dl.AddText(boardTL + new Vector2((boardW - labelSize.X) * 0.5f, (boardH * 0.5f) - ctx.Px(30f)),
            ImGui.GetColorU32(RetroLcd.Pixel), label);

        var buttonW = boardW * 0.7f;
        var buttonH = ctx.Px(36f);
        if (RetroLcd.Button("##munResume", ctx.Localize("os.muncher_resume"),
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
        var title = ctx.Localize(this.lastRunWasRecord ? "os.muncher_new_record" : "os.muncher_game_over");
        using (ctx.TitleFont?.Push())
        {
            var titleSize = ImGui.CalcTextSize(title);
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
                winPos + new Vector2((winSize.X - titleSize.X) * 0.5f, winSize.Y * 0.20f),
                ImGui.GetColorU32(RetroLcd.Pixel), title);
        }

        var lines = new[]
        {
            string.Format(ctx.Localize("os.muncher_score"), this.lastRunScore),
            string.Format(ctx.Localize("os.muncher_reached"), this.lastRunLevel),
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
        if (RetroLcd.Button("##munAgain", ctx.Localize("os.muncher_play_again"),
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, firstY), new Vector2(buttonW, buttonH),
            ctx.Px(4f), filled: true))
        {
            StartRun();
        }
        if (RetroLcd.Button("##munMenu", ctx.Localize("os.muncher_menu"),
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
            var title = ctx.Localize("os.muncher_high_scores");
            var titleSize = ImGui.CalcTextSize(title);
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
                winPos + new Vector2((winSize.X - titleSize.X) * 0.5f, winSize.Y * 0.12f),
                ImGui.GetColorU32(RetroLcd.Pixel), title);
        }

        var padX = ctx.Px(28f);
        var y = winSize.Y * 0.26f;
        if (this.highScores.Length == 0)
        {
            var empty = ctx.Localize("os.muncher_no_scores");
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
        if (RetroLcd.Button("##munBack", ctx.Localize("os.muncher_menu"),
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, winSize.Y - buttonH - ctx.Px(24f)),
            new Vector2(buttonW, buttonH), ctx.Px(4f), filled: false))
        {
            this.splashStartedAt = ImGui.GetTime();
            this.view = View.Splash;
        }
    }
}
