using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AetherLove.Shared.Arcade;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.SkySwarm;

/// <summary>Sky Swarm on the shared handheld LCD: a Galaga-style swarm that flies in on curves, breathes in
/// formation and peels off in dive runs, with the tractor-beam capture and dual-fighter rescue.</summary>
public sealed class SkySwarmApp : IAetherApp
{
    private enum View { Splash, Playing, GameOver, Scores, Leaderboard }

    private static readonly Vector4 TileTopColor = new(0.33f, 0.56f, 0.80f, 1f);
    private static readonly Vector4 TileBottomColor = new(0.10f, 0.19f, 0.36f, 1f);

    private const string HighScoresKey = "high_scores";
    private const int ScoreSlots = 5;
    private const int SpriteColumns = 8;
    private const int SpriteRows = 6;
    private const int FighterColumns = 7;
    private const int FighterRows = 5;
    private const float GridCell = 10f;
    private const int GridColumns = 10;
    private const int GridRows = 14;
    private const float StageBannerSeconds = 1.8f;
    private const float RespawnBlinkSeconds = 0.12f;
    private const float BeamFillAlpha = 0.20f;
    private const float BeamEdgeAlpha = 0.45f;
    private const double DecorLoopRate = 0.22;
    private const float DecorRestT = 0.5f;

    /// <summary>Three silhouettes, two wing-flap frames each, all tapering toward the nose because the
    /// swarm flies at you rather than marching.</summary>
    private static readonly string[][][] Sprites =
    [
        [
            ["#......#", "##.##.##", ".######.", ".######.", "..####..", "...##..."],
            ["........", ".#.##.#.", "########", ".######.", "..####..", "...##..."],
        ],
        [
            ["##....##", "###..###", ".######.", "..####..", ".##..##.", "..#..#.."],
            ["........", "##....##", "########", ".######.", ".##..##.", "..#..#.."],
        ],
        [
            ["#.####.#", "########", "##.##.##", "########", ".##..##.", "#......#"],
            ["#.####.#", ".######.", "##.##.##", "########", "..#..#..", ".#....#."],
        ],
    ];

    private static readonly string[] FighterSprite =
    [
        "...#...",
        "...#...",
        "..###..",
        ".#####.",
        "###.###",
    ];

    private readonly Func<string> name;
    private readonly IAppStorage storage;
    private readonly IKeyboardInput keys;
    private readonly AetherLove.Os.IArcadeRewards rewards;
    private readonly AetherLove.Os.IArcadeScores scores;
    private readonly AetherLove.Widgets.ArcadeLeaderboardView leaderboard;
    private readonly SkySwarmGame game = new();

    private View view = View.Splash;
    private bool paused;
    private double lastFrameTime;
    private double splashStartedAt;
    private int[] highScores = [];
    private bool scoresLoaded;
    private int lastRunScore;
    private int lastRunStage;
    private bool lastRunWasRecord;
    private double runSeconds;

    public SkySwarmApp(Func<string> name, IAppCapabilities capabilities, AetherLove.Os.IArcadeRewards rewards, AetherLove.Os.IArcadeScores scores)
    {
        this.name = name;
        this.storage = capabilities.Storage("skyswarm");
        this.keys = capabilities.Keyboard;
        this.rewards = rewards;
        this.scores = scores;
        this.leaderboard = new AetherLove.Widgets.ArcadeLeaderboardView(scores, ArcadeGame.SkySwarm);
    }

    public string Id => "skyswarm";

    public string Name => this.name();

    public FontAwesomeIcon Icon => FontAwesomeIcon.FighterJet;

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
        this.lastRunStage = this.game.Stage;
        this.lastRunWasRecord = this.lastRunScore > BestScore;
        this.highScores = this.highScores
            .Append(this.lastRunScore)
            .OrderByDescending(s => s)
            .Take(ScoreSlots)
            .ToArray();
        this.storage.Set(HighScoresKey, this.highScores);
        this.rewards.NoteGameFinished();
        this.scores.SubmitScore(new ArcadeScoreSubmissionDto(
            ArcadeGame.SkySwarm, this.lastRunScore, (int)(this.runSeconds * 1000.0),
            this.lastRunStage, this.game.DualAchieved ? 1 : 0));
    }

    private static void DrawSprite(ImDrawListPtr dl, int kind, int frame, Vector2 tl, float unit, uint color)
    {
        var rows = Sprites[kind][frame];
        for (var r = 0; r < SpriteRows; r++)
        {
            for (var c = 0; c < SpriteColumns; c++)
            {
                if (rows[r][c] != '#')
                {
                    continue;
                }
                var cellTL = tl + new Vector2(c * unit, r * unit);
                dl.AddRectFilled(cellTL, cellTL + new Vector2(unit, unit), color);
            }
        }
    }

    private static void DrawFighter(ImDrawListPtr dl, Vector2 center, float width, float height, uint color)
    {
        var cellW = width / FighterColumns;
        var cellH = height / FighterRows;
        var tl = center - new Vector2(width * 0.5f, height * 0.5f);
        for (var r = 0; r < FighterRows; r++)
        {
            for (var c = 0; c < FighterColumns; c++)
            {
                if (FighterSprite[r][c] != '#')
                {
                    continue;
                }
                var cellTL = tl + new Vector2(c * cellW, r * cellH);
                dl.AddRectFilled(cellTL, cellTL + new Vector2(cellW, cellH), color);
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

        var pixel = MathF.Max(2f, winSize.X * 0.5f / RetroLcd.WordColumns("SKY"));
        var wordY = winSize.Y * 0.12f;
        RetroLcd.DrawWordCentered(dl, "SKY", winPos + new Vector2(0f, wordY), winSize.X, pixel,
            ImGui.GetColorU32(RetroLcd.Pixel), litRows);

        var small = MathF.Max(1.5f, pixel * 0.5f);
        RetroLcd.DrawWordCentered(dl, "SWARM",
            winPos + new Vector2(0f, wordY + (RetroLcd.GlyphHeight * pixel) + ctx.Px(8f)), winSize.X, small,
            ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.8f }), litRows);

        var subtitle = ctx.Localize("os.skyswarm_subtitle");
        var subSize = ImGui.CalcTextSize(subtitle);
        dl.AddText(winPos + new Vector2((winSize.X - subSize.X) * 0.5f,
            wordY + (RetroLcd.GlyphHeight * (pixel + small)) + ctx.Px(22f)),
            ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.75f }), subtitle);

        DrawDecorDrone(ctx, dl, winPos, winSize, now);

        var buttonW = winSize.X * 0.62f;
        var buttonH = ctx.Px(38f);
        var firstY = winSize.Y * 0.56f;
        if (RetroLcd.Button("##swarmPlay", ctx.Localize("os.skyswarm_play"),
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, firstY), new Vector2(buttonW, buttonH),
            ctx.Px(4f), filled: true))
        {
            StartRun();
        }
        if (RetroLcd.Button("##swarmScores", ctx.Localize("os.skyswarm_high_scores"),
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, firstY + buttonH + ctx.Px(10f)),
            new Vector2(buttonW, buttonH), ctx.Px(4f), filled: false))
        {
            this.view = View.Scores;
        }
        if (RetroLcd.Button("##swarmBoard", ctx.Localize("os.arcade_leaderboard"),
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, firstY + ((buttonH + ctx.Px(10f)) * 2f)),
            new Vector2(buttonW, buttonH), ctx.Px(4f), filled: false))
        {
            this.view = View.Leaderboard;
        }

        if (RetroLcd.Key("##swarmExit", Dalamud.Interface.FontAwesomeIcon.ArrowLeft,
            winPos + new Vector2(ctx.Px(12f), ctx.Px(12f)), ctx.Px(30f)))
        {
            ctx.Shell.GoHomeToFolder(IOsShell.ArcadeFolderId);
        }

        if (BestScore > 0)
        {
            var best = string.Format(ctx.Localize("os.skyswarm_best"), BestScore);
            var bestSize = ImGui.CalcTextSize(best);
            dl.AddText(winPos + new Vector2((winSize.X - bestSize.X) * 0.5f, winSize.Y - ctx.Px(30f)),
                ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.7f }), best);
        }
    }

    /// <summary>One drone patrols a closed bezier loop under the title while you decide.</summary>
    private static void DrawDecorDrone(OsAppContext ctx, ImDrawListPtr dl, Vector2 winPos, Vector2 winSize, double now)
    {
        var t = ctx.ReduceMotion ? DecorRestT : (float)((now * DecorLoopRate) % 1.0);
        var anchor = winPos + new Vector2(winSize.X * 0.5f, winSize.Y * 0.47f);
        var p1 = anchor + new Vector2(-winSize.X * 0.34f, -ctx.Px(30f));
        var p2 = anchor + new Vector2(winSize.X * 0.34f, -ctx.Px(30f));
        var u = 1f - t;
        var pos = (u * u * u * anchor)
            + (3f * u * u * t * p1)
            + (3f * u * t * t * p2)
            + (t * t * t * anchor);
        var unit = winSize.X * 0.020f;
        var frame = ctx.ReduceMotion || ((int)(now * 4) % 2 == 0) ? 0 : 1;
        DrawSprite(dl, (int)SwarmKind.Drone, frame,
            pos - new Vector2(SpriteColumns * unit * 0.5f, SpriteRows * unit * 0.5f), unit,
            ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.9f }));
    }

    private void DrawPlaying(OsAppContext ctx, float delta, Vector2 winPos, Vector2 winSize)
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

        var hudH = ctx.Px(44f);
        var padH = winSize.Y * 0.19f;
        var boardMaxW = winSize.X - ctx.Px(12f);
        var boardMaxH = winSize.Y - hudH - padH;
        var scale = MathF.Min(boardMaxW / SkySwarmGame.Width, boardMaxH / SkySwarmGame.Height);
        var boardW = SkySwarmGame.Width * scale;
        var boardH = SkySwarmGame.Height * scale;
        var boardTL = new Vector2(winPos.X + ((winSize.X - boardW) * 0.5f), winPos.Y + hudH);

        DrawHud(ctx, winPos, winSize, hudH, scale);
        DrawField(ctx, boardTL, scale, boardW, boardH);
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
            string.Format(ctx.Localize("os.skyswarm_score"), this.game.Score));

        if (!this.paused && RetroLcd.PauseKey(winPos, winSize, padX, hudH))
        {
            this.paused = true;
        }
        var reserve = RetroLcd.PauseKeyWidth(hudH) + ctx.Px(8f);
        var stage = string.Format(ctx.Localize("os.skyswarm_stage"), this.game.Stage);
        var stageSize = ImGui.CalcTextSize(stage);
        dl.AddText(winPos + new Vector2(winSize.X - padX - reserve - stageSize.X, ctx.Px(6f)),
            ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.7f }), stage);

        var pipW = MathF.Max(5f, scale * 5f);
        var pipH = pipW * 0.7f;
        var x = winPos.X + padX + (pipW * 0.5f);
        var pipY = winPos.Y + ctx.Px(6f) + line + (pipH * 0.7f);
        for (var i = 0; i < this.game.Lives; i++)
        {
            DrawFighter(dl, new Vector2(x, pipY), pipW, pipH, ImGui.GetColorU32(RetroLcd.Pixel));
            x += pipW * 1.5f;
        }
    }

    private void DrawField(OsAppContext ctx, Vector2 boardTL, float scale, float boardW, float boardH)
    {
        var dl = ImGui.GetWindowDrawList();
        var ink = ImGui.GetColorU32(RetroLcd.Pixel);
        dl.AddRect(boardTL - new Vector2(2f, 2f), boardTL + new Vector2(boardW + 2f, boardH + 2f),
            ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.55f }), 0f, ImDrawFlags.None, 2f);
        RetroLcd.DotGrid(dl, boardTL, GridColumns, GridRows, GridCell * scale);

        var frame = this.game.AnimFrame ? 1 : 0;
        var unit = SkySwarmGame.ShipWidth / SpriteColumns * scale;
        var shipHalf = new Vector2(SkySwarmGame.ShipWidth * 0.5f, SkySwarmGame.ShipHeight * 0.5f);
        foreach (var ship in this.game.Ships)
        {
            if (ship.State is SwarmState.Waiting or SwarmState.Gone)
            {
                continue;
            }
            var extent = this.game.BeamExtent(ship);
            if (extent > 0f)
            {
                DrawBeam(dl, ship, extent, boardTL, scale);
            }
            DrawSprite(dl, (int)ship.Kind, frame, boardTL + ((ship.Pos - shipHalf) * scale), unit, ink);
            if (ship.HoldsCaptive)
            {
                DrawFighter(dl, boardTL + ((ship.Pos + new Vector2(0f, -SkySwarmGame.ShipHeight)) * scale),
                    SkySwarmGame.PlayerWidth * scale * 0.8f, SkySwarmGame.PlayerHeight * scale * 0.8f,
                    ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.8f }));
            }
        }

        DrawPlayer(ctx, dl, boardTL, scale, ink);

        foreach (var bullet in this.game.Bullets)
        {
            var pos = boardTL + (bullet * scale);
            dl.AddRectFilled(pos - new Vector2(1f, 3f * scale), pos + new Vector2(1f, 0f), ink);
        }
        foreach (var shot in this.game.Shots)
        {
            var pos = boardTL + (shot.Pos * scale);
            var tail = Vector2.Normalize(shot.Vel) * 2.4f * scale;
            dl.AddLine(pos - tail, pos, ink, 2f);
        }

        if (this.game.RescueActive)
        {
            DrawFighter(dl, boardTL + (this.game.RescuePos * scale),
                SkySwarmGame.PlayerWidth * scale, SkySwarmGame.PlayerHeight * scale, ink);
        }
        if (this.game.CaptureActive)
        {
            DrawFighter(dl, boardTL + (this.game.CapturePos * scale),
                SkySwarmGame.PlayerWidth * scale, SkySwarmGame.PlayerHeight * scale, ink);
        }

        DrawBanners(ctx, dl, boardTL, boardW, boardH);
    }

    private void DrawPlayer(OsAppContext ctx, ImDrawListPtr dl, Vector2 boardTL, float scale, uint ink)
    {
        if (this.game.CaptureActive)
        {
            return;
        }
        var color = ink;
        if (this.game.RespawnTimer > 0f)
        {
            if (ctx.ReduceMotion)
            {
                color = ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.5f });
            }
            else if ((int)(this.game.RespawnTimer / RespawnBlinkSeconds) % 2 == 0)
            {
                return;
            }
        }
        var center = boardTL + (new Vector2(this.game.PlayerX,
            SkySwarmGame.PlayerRowY - (SkySwarmGame.PlayerHeight * 0.5f)) * scale);
        var w = SkySwarmGame.PlayerWidth * scale;
        var h = SkySwarmGame.PlayerHeight * scale;
        if (this.game.Dual)
        {
            DrawFighter(dl, center - new Vector2(w * 0.5f, 0f), w, h, color);
            DrawFighter(dl, center + new Vector2(w * 0.5f, 0f), w, h, color);
        }
        else
        {
            DrawFighter(dl, center, w, h, color);
        }
    }

    private static void DrawBeam(ImDrawListPtr dl, SkySwarmGame.Ship ship, float extent, Vector2 boardTL, float scale)
    {
        var top = ship.Pos + new Vector2(0f, SkySwarmGame.ShipHeight * 0.5f);
        var reach = (SkySwarmGame.PlayerRowY - top.Y) * extent;
        var halfW = SkySwarmGame.BeamTopHalfWidth
            + ((SkySwarmGame.BeamBottomHalfWidth - SkySwarmGame.BeamTopHalfWidth) * extent);
        var a = boardTL + ((top + new Vector2(-SkySwarmGame.BeamTopHalfWidth, 0f)) * scale);
        var b = boardTL + ((top + new Vector2(SkySwarmGame.BeamTopHalfWidth, 0f)) * scale);
        var c = boardTL + ((top + new Vector2(halfW, reach)) * scale);
        var d = boardTL + ((top + new Vector2(-halfW, reach)) * scale);
        var fill = ImGui.GetColorU32(RetroLcd.Pixel with { W = BeamFillAlpha });
        dl.AddTriangleFilled(a, b, c, fill);
        dl.AddTriangleFilled(a, c, d, fill);
        var edge = ImGui.GetColorU32(RetroLcd.Pixel with { W = BeamEdgeAlpha });
        dl.AddLine(a, d, edge, 1.5f);
        dl.AddLine(b, c, edge, 1.5f);
    }

    private void DrawBanners(OsAppContext ctx, ImDrawListPtr dl, Vector2 boardTL, float boardW, float boardH)
    {
        if (this.game.StageTime < StageBannerSeconds)
        {
            var text = this.game.IsChallenge
                ? ctx.Localize("os.skyswarm_challenge")
                : string.Format(ctx.Localize("os.skyswarm_stage"), this.game.Stage);
            var size = ImGui.CalcTextSize(text);
            dl.AddText(boardTL + new Vector2((boardW - size.X) * 0.5f, boardH * 0.30f),
                ImGui.GetColorU32(RetroLcd.Pixel), text);
            return;
        }
        if (this.game.ResultTimer <= 0f)
        {
            return;
        }
        var hits = string.Format(ctx.Localize("os.skyswarm_hits"),
            this.game.LastChallengeHits, SkySwarmGame.ChallengeShipCount);
        var hitsSize = ImGui.CalcTextSize(hits);
        var y = boardH * 0.30f;
        dl.AddText(boardTL + new Vector2((boardW - hitsSize.X) * 0.5f, y),
            ImGui.GetColorU32(RetroLcd.Pixel), hits);
        if (this.game.LastChallengeWasPerfect)
        {
            var perfect = string.Format(ctx.Localize("os.skyswarm_perfect"), SkySwarmGame.PerfectBonus);
            var perfectSize = ImGui.CalcTextSize(perfect);
            dl.AddText(boardTL + new Vector2((boardW - perfectSize.X) * 0.5f, y + ImGui.GetTextLineHeightWithSpacing()),
                ImGui.GetColorU32(RetroLcd.Pixel), perfect);
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

        if (RetroLcd.KeyLabelHeld("##swarmLeft", "A", new Vector2(x, rowY), key))
        {
            this.game.MoveLeft(delta);
        }
        x += key + gap;
        if (RetroLcd.KeyLabel("##swarmFire", "W", new Vector2(x, rowY), key))
        {
            this.game.Fire();
        }
        x += key + gap;
        if (RetroLcd.KeyLabelHeld("##swarmRight", "D", new Vector2(x, rowY), key))
        {
            this.game.MoveRight(delta);
        }
    }

    private void DrawPausedOverlay(OsAppContext ctx, Vector2 boardTL, float boardW, float boardH)
    {
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(boardTL, boardTL + new Vector2(boardW, boardH),
            ImGui.GetColorU32(RetroLcd.Panel with { W = 0.82f }));
        var label = ctx.Localize("os.skyswarm_paused");
        var labelSize = ImGui.CalcTextSize(label);
        dl.AddText(boardTL + new Vector2((boardW - labelSize.X) * 0.5f, (boardH * 0.5f) - ctx.Px(30f)),
            ImGui.GetColorU32(RetroLcd.Pixel), label);

        var buttonW = boardW * 0.7f;
        var buttonH = ctx.Px(36f);
        if (RetroLcd.Button("##swarmResume", ctx.Localize("os.skyswarm_resume"),
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
        var title = ctx.Localize(this.lastRunWasRecord ? "os.skyswarm_new_record" : "os.skyswarm_game_over");
        using (ctx.TitleFont?.Push())
        {
            var titleSize = ImGui.CalcTextSize(title);
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
                winPos + new Vector2((winSize.X - titleSize.X) * 0.5f, winSize.Y * 0.20f),
                ImGui.GetColorU32(RetroLcd.Pixel), title);
        }

        var lines = new[]
        {
            string.Format(ctx.Localize("os.skyswarm_score"), this.lastRunScore),
            string.Format(ctx.Localize("os.skyswarm_reached"), this.lastRunStage),
            string.Format(ctx.Localize("os.skyswarm_time"), (int)this.runSeconds),
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
        if (RetroLcd.Button("##swarmAgain", ctx.Localize("os.skyswarm_play_again"),
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, firstY), new Vector2(buttonW, buttonH),
            ctx.Px(4f), filled: true))
        {
            StartRun();
        }
        if (RetroLcd.Button("##swarmMenu", ctx.Localize("os.skyswarm_menu"),
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
            var title = ctx.Localize("os.skyswarm_high_scores");
            var titleSize = ImGui.CalcTextSize(title);
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
                winPos + new Vector2((winSize.X - titleSize.X) * 0.5f, winSize.Y * 0.12f),
                ImGui.GetColorU32(RetroLcd.Pixel), title);
        }

        var padX = ctx.Px(28f);
        var y = winSize.Y * 0.26f;
        if (this.highScores.Length == 0)
        {
            var empty = ctx.Localize("os.skyswarm_no_scores");
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
        if (RetroLcd.Button("##swarmBack", ctx.Localize("os.skyswarm_menu"),
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, winSize.Y - buttonH - ctx.Px(24f)),
            new Vector2(buttonW, buttonH), ctx.Px(4f), filled: false))
        {
            this.splashStartedAt = ImGui.GetTime();
            this.view = View.Splash;
        }
    }
}
