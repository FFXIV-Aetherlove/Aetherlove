using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AetherLove.Shared.Arcade;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Racooner;

/// <summary>Racooner on the shared handheld LCD: a Frogger-like where a raccoon hops through Eorzean cart
/// traffic, rides pads across an aether stream, and banks itself in five dens. Scoring is 10 per new row
/// reached, 200 per den plus a timer bonus, and 500 per cleared level.</summary>
public sealed class RacoonerApp : IAetherApp
{
    private enum View { Splash, Playing, GameOver, Scores, Leaderboard }

    private static readonly Vector4 TileTopColor = new(0.55f, 0.60f, 0.66f, 1f);
    private static readonly Vector4 TileBottomColor = new(0.18f, 0.22f, 0.28f, 1f);

    private const string HighScoresKey = "high_scores";
    private const int ScoreSlots = 5;
    private const float LowTimerWarnSeconds = 10f;
    private const int WaterDashesPerLane = 4;

    private readonly Func<string> name;
    private readonly IAppStorage storage;
    private readonly IKeyboardInput keys;
    private readonly AetherLove.Os.IArcadeRewards rewards;
    private readonly AetherLove.Os.IArcadeScores scores;
    private readonly AetherLove.Widgets.ArcadeLeaderboardView leaderboard;
    private readonly RacoonerGame game = new();

    private View view = View.Splash;
    private bool paused;
    private double lastFrameTime;
    private double splashStartedAt;
    private int[] highScores = [];
    private bool scoresLoaded;
    private int lastRunScore;
    private int lastRunLevel;
    private int lastRunBanked;
    private bool lastRunWasRecord;
    private double runSeconds;

    public RacoonerApp(Func<string> name, IAppCapabilities capabilities, AetherLove.Os.IArcadeRewards rewards, AetherLove.Os.IArcadeScores scores)
    {
        this.name = name;
        this.storage = capabilities.Storage("racooner");
        this.keys = capabilities.Keyboard;
        this.rewards = rewards;
        this.scores = scores;
        this.leaderboard = new AetherLove.Widgets.ArcadeLeaderboardView(scores, ArcadeGame.Racooner);
    }

    public string Id => "racooner";

    public string Name => this.name();

    public FontAwesomeIcon Icon => FontAwesomeIcon.Paw;

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
        var delta = Math.Clamp(now - this.lastFrameTime, 0.0, 0.5);
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
        this.lastRunBanked = this.game.BankedTotal;
        this.lastRunWasRecord = this.lastRunScore > BestScore;
        this.highScores = this.highScores
            .Append(this.lastRunScore)
            .OrderByDescending(s => s)
            .Take(ScoreSlots)
            .ToArray();
        this.storage.Set(HighScoresKey, this.highScores);
        this.rewards.NoteGameFinished();
        this.scores.SubmitScore(new ArcadeScoreSubmissionDto(
            ArcadeGame.Racooner, this.lastRunScore, (int)(this.runSeconds * 1000.0), this.lastRunLevel));
    }

    private void DrawSplash(OsAppContext ctx, double now, Vector2 winPos, Vector2 winSize)
    {
        var dl = ImGui.GetWindowDrawList();
        var elapsed = now - this.splashStartedAt;
        var litRows = ctx.ReduceMotion
            ? RetroLcd.GlyphHeight
            : (int)Math.Min(RetroLcd.GlyphHeight, elapsed / 0.09);

        var pixel = MathF.Max(2f, winSize.X * 0.78f / RetroLcd.WordColumns("RACOONER"));
        var wordH = RetroLcd.GlyphHeight * pixel;
        var wordY = winSize.Y * 0.15f;
        RetroLcd.DrawWordCentered(dl, "RACOONER", winPos + new Vector2(0f, wordY), winSize.X, pixel,
            ImGui.GetColorU32(RetroLcd.Pixel), litRows);

        var subtitle = ctx.Localize("os.racooner_subtitle");
        var subSize = ImGui.CalcTextSize(subtitle);
        dl.AddText(winPos + new Vector2((winSize.X - subSize.X) * 0.5f, wordY + wordH + ctx.Px(14f)),
            ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.75f }), subtitle);

        DrawDecorRacoon(ctx, dl, winPos, winSize, now);

        var buttonW = winSize.X * 0.62f;
        var buttonH = ctx.Px(38f);
        var firstY = winSize.Y * 0.52f;
        if (RetroLcd.Button("##racPlay", ctx.Localize("os.racooner_play"),
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, firstY), new Vector2(buttonW, buttonH),
            ctx.Px(4f), filled: true))
        {
            StartRun();
        }
        if (RetroLcd.Button("##racScores", ctx.Localize("os.racooner_high_scores"),
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, firstY + buttonH + ctx.Px(10f)),
            new Vector2(buttonW, buttonH), ctx.Px(4f), filled: false))
        {
            this.view = View.Scores;
        }
        if (RetroLcd.Button("##racBoard", ctx.Localize("os.arcade_leaderboard"),
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, firstY + ((buttonH + ctx.Px(10f)) * 2f)),
            new Vector2(buttonW, buttonH), ctx.Px(4f), filled: false))
        {
            this.view = View.Leaderboard;
        }

        if (RetroLcd.Key("##racExit", Dalamud.Interface.FontAwesomeIcon.ArrowLeft,
            winPos + new Vector2(ctx.Px(12f), ctx.Px(12f)), ctx.Px(30f)))
        {
            ctx.Shell.GoHomeToFolder(IOsShell.ArcadeFolderId);
        }

        if (BestScore > 0)
        {
            var best = string.Format(ctx.Localize("os.racooner_best"), BestScore);
            var bestSize = ImGui.CalcTextSize(best);
            dl.AddText(winPos + new Vector2((winSize.X - bestSize.X) * 0.5f, winSize.Y - ctx.Px(30f)),
                ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.7f }), best);
        }
    }

    /// <summary>The raccoon hops in place under the title while you decide.</summary>
    private static void DrawDecorRacoon(OsAppContext ctx, ImDrawListPtr dl, Vector2 winPos, Vector2 winSize, double now)
    {
        var size = winSize.X * 0.13f;
        var hop = ctx.ReduceMotion ? 0f : MathF.Abs(MathF.Sin((float)now * 3f));
        var groundY = winPos.Y + (winSize.Y * 0.40f) + size;
        var shadowW = size * (1f - (hop * 0.3f));
        dl.AddRectFilled(new Vector2(winPos.X + ((winSize.X - shadowW) * 0.5f), groundY + 2f),
            new Vector2(winPos.X + ((winSize.X + shadowW) * 0.5f), groundY + 4f),
            ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.3f }));
        var tl = new Vector2(winPos.X + ((winSize.X - size) * 0.5f),
            winPos.Y + (winSize.Y * 0.40f) - (hop * size * 0.35f));
        DrawRacoon(dl, tl, size, hop > 0.45f, 1f);
    }

    private void DrawPlaying(OsAppContext ctx, double now, double delta, Vector2 winPos, Vector2 winSize)
    {
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

        var hudH = ctx.Px(50f);
        var padH = winSize.Y * 0.26f;
        var boardMaxW = winSize.X - ctx.Px(10f);
        var boardMaxH = winSize.Y - hudH - padH;
        var cell = MathF.Max(3f, MathF.Min(boardMaxW / RacoonerGame.Columns, boardMaxH / RacoonerGame.Rows));
        var boardW = cell * RacoonerGame.Columns;
        var boardH = cell * RacoonerGame.Rows;
        var boardTL = new Vector2(winPos.X + ((winSize.X - boardW) * 0.5f),
            winPos.Y + hudH + ((boardMaxH - boardH) * 0.5f));

        DrawHud(ctx, now, winPos, winSize, hudH);
        DrawBoard(ctx, now, boardTL, cell, boardW, boardH);
        if (this.paused)
        {
            DrawPausedOverlay(ctx, boardTL, boardW, boardH);
        }
        else
        {
            DrawDpad(winPos, winSize, padH);
        }
    }

    private void DrawHud(OsAppContext ctx, double now, Vector2 winPos, Vector2 winSize, float hudH)
    {
        var dl = ImGui.GetWindowDrawList();
        var padX = ctx.Px(12f);
        var reserve = RetroLcd.PauseKeyWidth(hudH) + ctx.Px(8f);
        if (!this.paused && RetroLcd.PauseKey(winPos, winSize, padX, hudH))
        {
            this.paused = true;
        }

        var textY = winPos.Y + ctx.Px(4f);
        dl.AddText(new Vector2(winPos.X + padX, textY), ImGui.GetColorU32(RetroLcd.Pixel),
            string.Format(ctx.Localize("os.racooner_score"), this.game.Score));
        var level = string.Format(ctx.Localize("os.racooner_level"), this.game.Level);
        var levelSize = ImGui.CalcTextSize(level);
        dl.AddText(new Vector2(winPos.X + winSize.X - padX - reserve - levelSize.X, textY),
            ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.7f }), level);

        var pipR = ctx.Px(5f);
        var pipCenterY = textY + ImGui.GetTextLineHeight() + ctx.Px(9f);
        for (var i = 0; i < this.game.Lives; i++)
        {
            DrawLifePip(dl, new Vector2(winPos.X + padX + pipR + (i * pipR * 3f), pipCenterY), pipR);
        }

        // What the run is actually for. Without it the top row is a mystery until somebody guesses.
        var dens = string.Format(ctx.Localize("os.racooner_dens"), FilledDens(), RacoonerGame.BayCount);
        var densSize = ImGui.CalcTextSize(dens);
        dl.AddText(new Vector2(winPos.X + winSize.X - padX - reserve - densSize.X,
                pipCenterY - (densSize.Y * 0.5f)),
            ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.7f }), dens);

        var barH = ctx.Px(5f);
        var barTop = winPos.Y + hudH - barH - ctx.Px(4f);
        var barLeft = winPos.X + padX;
        var barRight = winPos.X + winSize.X - padX - reserve;
        var fillAlpha = 1f;
        if (this.game.TimerRemaining < LowTimerWarnSeconds && !ctx.ReduceMotion)
        {
            fillAlpha = 0.45f + (MathF.Abs(MathF.Sin((float)now * 6f)) * 0.55f);
        }
        dl.AddRect(new Vector2(barLeft, barTop), new Vector2(barRight, barTop + barH),
            ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.55f }));
        dl.AddRectFilled(new Vector2(barLeft, barTop),
            new Vector2(barLeft + ((barRight - barLeft) * this.game.TimerFraction), barTop + barH),
            ImGui.GetColorU32(RetroLcd.Pixel with { W = fillAlpha }));
    }

    private void DrawBoard(OsAppContext ctx, double now, Vector2 boardTL, float cell, float boardW, float boardH)
    {
        var dl = ImGui.GetWindowDrawList();
        dl.AddRect(boardTL - new Vector2(2f, 2f), boardTL + new Vector2(boardW + 2f, boardH + 2f),
            ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.55f }), 0f, ImDrawFlags.None, 2f);
        RetroLcd.DotGrid(dl, boardTL, RacoonerGame.Columns, RacoonerGame.Rows, cell);

        dl.PushClipRect(boardTL, boardTL + new Vector2(boardW, boardH), true);
        DrawTerrain(ctx, now, dl, boardTL, cell, boardW);
        DrawPads(dl, boardTL, cell);
        DrawVehicles(dl, boardTL, cell);
        DrawBoardRacoon(ctx, now, dl, boardTL, cell);
        dl.PopClipRect();
    }

    private void DrawTerrain(OsAppContext ctx, double now, ImDrawListPtr dl, Vector2 boardTL, float cell, float boardW)
    {
        dl.AddRectFilled(boardTL, boardTL + new Vector2(boardW, cell * 2f),
            ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.12f }));
        for (var x = 0; x < RacoonerGame.Columns; x++)
        {
            if (x % 3 == 1)
            {
                DrawTuft(dl, boardTL, x, 0, cell);
            }
            if (x % 4 == 2)
            {
                DrawTuft(dl, boardTL, x, 1, cell);
            }
        }

        DrawSafeStrip(dl, boardTL, RacoonerGame.MedianRow, cell, boardW);
        DrawSafeStrip(dl, boardTL, RacoonerGame.StartRow, cell, boardW);
        DrawBays(ctx, now, dl, boardTL, cell);

        foreach (var lane in this.game.StreamLanes)
        {
            var y = boardTL.Y + ((RacoonerGame.Rows - 1 - lane.Row) * cell);
            var drift = ctx.ReduceMotion ? lane.Row * 0.7f : (float)(now * lane.Speed);
            for (var d = 0; d < WaterDashesPerLane; d++)
            {
                var cx = WrapCells((d * (float)RacoonerGame.Columns / WaterDashesPerLane) + drift);
                var tl = new Vector2(boardTL.X + (cx * cell), y + (cell * 0.45f));
                dl.AddRectFilled(tl, tl + new Vector2(cell * 0.7f, MathF.Max(1f, cell * 0.1f)),
                    ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.18f }));
            }
        }

        for (var r = RacoonerGame.RoadFirstRow; r < RacoonerGame.RoadLastRow; r++)
        {
            var y = boardTL.Y + ((RacoonerGame.Rows - 1 - r) * cell);
            for (var x = 0f; x < boardW; x += cell * 1.3f)
            {
                dl.AddRectFilled(new Vector2(boardTL.X + x, y - 1f),
                    new Vector2(boardTL.X + x + (cell * 0.55f), y + 1f),
                    ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.22f }));
            }
        }
    }

    private int FilledDens()
    {
        var filled = 0;
        foreach (var bay in this.game.Bays)
        {
            if (bay)
            {
                filled++;
            }
        }
        return filled;
    }

    private static void DrawTuft(ImDrawListPtr dl, Vector2 boardTL, int x, int screenRow, float cell)
    {
        var tl = boardTL + new Vector2((x * cell) + (cell * 0.4f), (screenRow * cell) + (cell * 0.55f));
        dl.AddRectFilled(tl, tl + new Vector2(cell * 0.2f, cell * 0.35f),
            ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.35f }));
    }

    private static void DrawSafeStrip(ImDrawListPtr dl, Vector2 boardTL, int row, float cell, float boardW)
    {
        var y = boardTL.Y + ((RacoonerGame.Rows - 1 - row) * cell);
        dl.AddRectFilled(new Vector2(boardTL.X, y), new Vector2(boardTL.X + boardW, y + cell),
            ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.10f }));
    }

    private void DrawBays(OsAppContext ctx, double now, ImDrawListPtr dl, Vector2 boardTL, float cell)
    {
        var bayY = boardTL.Y + ((RacoonerGame.Rows - 1 - RacoonerGame.BankRow) * cell);
        for (var x = 0; x < RacoonerGame.Columns; x++)
        {
            var tl = new Vector2(boardTL.X + (x * cell), bayY);
            var bayIndex = Array.IndexOf(RacoonerGame.BayColumns, x);
            var bumped = this.game.BumpFlash > 0f && x == this.game.BumpColumn;
            if (bayIndex < 0)
            {
                // Wall. Hatched rather than plain, because a solid block was reading as the thing to
                // aim for and the open dens beside it as the blocked ones.
                dl.AddRectFilled(tl + new Vector2(1f, 1f), tl + new Vector2(cell - 1f, cell - 1f),
                    ImGui.GetColorU32(RetroLcd.Pixel with { W = bumped ? 1f : 0.8f }), cell * 0.15f);
                for (var s = 0.15f; s < 1f; s += 0.3f)
                {
                    dl.AddLine(tl + new Vector2(cell * s, cell - 2f), tl + new Vector2(cell * (s + 0.22f), 2f),
                        ImGui.GetColorU32(RetroLcd.Panel with { W = 0.35f }), MathF.Max(1f, cell * 0.06f));
                }
                continue;
            }

            // An open den: a dark mouth with a lit rim and a chevron pointing into it.
            var mouthTl = tl + new Vector2(1.5f, 1.5f);
            var mouthBr = tl + new Vector2(cell - 1.5f, cell - 1.5f);
            dl.AddRectFilled(mouthTl, mouthBr, ImGui.GetColorU32(RetroLcd.Panel with { W = 0.55f }), cell * 0.2f);
            dl.AddRect(mouthTl, mouthBr,
                ImGui.GetColorU32(RetroLcd.Pixel with { W = bumped ? 1f : this.game.Bays[bayIndex] ? 0.55f : 0.85f }),
                cell * 0.2f, ImDrawFlags.RoundCornersAll, MathF.Max(1f, cell * 0.07f));
            if (this.game.Bays[bayIndex])
            {
                DrawRacoon(dl, tl, cell, hopFrame: false, 1f);
            }
            else
            {
                // Lined up: a hop up right now goes in. Saying so on the board turns the timing from a
                // guess into something the player can watch for.
                var aligned = bayIndex == this.game.AlignedBay;
                var pulse = ctx.ReduceMotion ? 1f : 0.65f + (MathF.Abs(MathF.Sin((float)now * 7f)) * 0.35f);
                var mid = tl.X + (cell * 0.5f);
                dl.AddTriangleFilled(
                    new Vector2(mid, bayY + (cell * (aligned ? 0.22f : 0.28f))),
                    new Vector2(mid - (cell * (aligned ? 0.24f : 0.18f)), bayY + (cell * 0.58f)),
                    new Vector2(mid + (cell * (aligned ? 0.24f : 0.18f)), bayY + (cell * 0.58f)),
                    ImGui.GetColorU32(RetroLcd.Pixel with { W = aligned ? pulse : 0.5f }));
                if (aligned)
                {
                    dl.AddRect(tl, tl + new Vector2(cell, cell),
                        ImGui.GetColorU32(RetroLcd.Pixel with { W = pulse }), cell * 0.2f,
                        ImDrawFlags.RoundCornersAll, MathF.Max(1.5f, cell * 0.1f));
                }
            }
            var flashing = (this.game.BankFlash > 0f && bayIndex == this.game.LastBankedBay)
                || (this.game.ClearingLevel && this.game.Bays[bayIndex]);
            if (flashing)
            {
                var flashAlpha = ctx.ReduceMotion ? 0.25f : 0.1f + (MathF.Abs(MathF.Sin((float)now * 8f)) * 0.3f);
                dl.AddRectFilled(tl, tl + new Vector2(cell, cell),
                    ImGui.GetColorU32(RetroLcd.Pixel with { W = flashAlpha }));
            }
            if (bumped)
            {
                dl.AddRect(tl, tl + new Vector2(cell, cell),
                    ImGui.GetColorU32(RetroLcd.Pixel), cell * 0.2f, ImDrawFlags.RoundCornersAll,
                    MathF.Max(1.5f, cell * 0.12f));
            }
        }
    }

    private void DrawPads(ImDrawListPtr dl, Vector2 boardTL, float cell)
    {
        foreach (var lane in this.game.StreamLanes)
        {
            var y = boardTL.Y + ((RacoonerGame.Rows - 1 - lane.Row) * cell);
            foreach (var pad in lane.Entities)
            {
                DrawPad(dl, boardTL.X, y, pad.X, pad.Length, cell);
                if (pad.X + pad.Length > RacoonerGame.Columns)
                {
                    DrawPad(dl, boardTL.X, y, pad.X - RacoonerGame.Columns, pad.Length, cell);
                }
            }
        }
    }

    private static void DrawPad(ImDrawListPtr dl, float boardX, float y, float xCells, int length, float cell)
    {
        var inset = MathF.Max(1f, cell * 0.14f);
        var tl = new Vector2(boardX + (xCells * cell) + 1f, y + inset);
        var br = new Vector2(boardX + ((xCells + length) * cell) - 1f, y + cell - inset);
        dl.AddRectFilled(tl, br, ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.85f }), cell * 0.3f);
        var notchW = MathF.Max(1f, cell * 0.08f);
        for (var n = 1; n < length; n++)
        {
            var nx = boardX + ((xCells + n) * cell) - (notchW * 0.5f);
            dl.AddRectFilled(new Vector2(nx, tl.Y + 2f), new Vector2(nx + notchW, br.Y - 2f),
                ImGui.GetColorU32(RetroLcd.Panel with { W = 0.8f }));
        }
    }

    private void DrawVehicles(ImDrawListPtr dl, Vector2 boardTL, float cell)
    {
        foreach (var lane in this.game.RoadLanes)
        {
            var y = boardTL.Y + ((RacoonerGame.Rows - 1 - lane.Row) * cell);
            var dir = lane.Speed > 0f ? 1f : -1f;
            foreach (var vehicle in lane.Entities)
            {
                DrawVehicle(dl, boardTL.X, y, vehicle.X, vehicle.Length, cell, dir);
                if (vehicle.X + vehicle.Length > RacoonerGame.Columns)
                {
                    DrawVehicle(dl, boardTL.X, y, vehicle.X - RacoonerGame.Columns, vehicle.Length, cell, dir);
                }
            }
        }
    }

    private static void DrawVehicle(ImDrawListPtr dl, float boardX, float y, float xCells, int length, float cell, float dir)
    {
        var ink = ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.95f });
        var panel = ImGui.GetColorU32(RetroLcd.Panel);
        var x0 = boardX + (xCells * cell);
        if (length == 1)
        {
            var cx = x0 + (cell * 0.5f);
            var body = new Vector2(cx - (dir * cell * 0.06f), y + (cell * 0.58f));
            var head = new Vector2(cx + (dir * cell * 0.26f), y + (cell * 0.26f));
            dl.AddLine(body, head, ink, MathF.Max(1.5f, cell * 0.14f));
            dl.AddCircleFilled(body, cell * 0.28f, ink, 16);
            dl.AddCircleFilled(head, cell * 0.14f, ink, 12);
            dl.AddTriangleFilled(
                new Vector2(head.X + (dir * cell * 0.12f), head.Y - (cell * 0.06f)),
                new Vector2(head.X + (dir * cell * 0.12f), head.Y + (cell * 0.06f)),
                new Vector2(head.X + (dir * cell * 0.3f), head.Y),
                ink);
            var legW = MathF.Max(1f, cell * 0.08f);
            dl.AddRectFilled(new Vector2(cx - (cell * 0.16f), y + (cell * 0.76f)),
                new Vector2(cx - (cell * 0.16f) + legW, y + (cell * 0.95f)), ink);
            dl.AddRectFilled(new Vector2(cx + (cell * 0.08f), y + (cell * 0.76f)),
                new Vector2(cx + (cell * 0.08f) + legW, y + (cell * 0.95f)), ink);
            return;
        }

        dl.AddRectFilled(new Vector2(x0 + (cell * 0.08f), y + (cell * 0.3f)),
            new Vector2(x0 + (cell * 1.92f), y + (cell * 0.68f)), ink, cell * 0.12f);
        var canopyX0 = dir > 0f ? x0 + (cell * 0.15f) : x0 + cell;
        dl.AddRectFilled(new Vector2(canopyX0, y + (cell * 0.12f)),
            new Vector2(canopyX0 + (cell * 0.85f), y + (cell * 0.34f)), ink, cell * 0.1f);
        var wheelY = y + (cell * 0.76f);
        dl.AddCircleFilled(new Vector2(x0 + (cell * 0.45f), wheelY), cell * 0.17f, ink, 12);
        dl.AddCircleFilled(new Vector2(x0 + (cell * 1.55f), wheelY), cell * 0.17f, ink, 12);
        dl.AddCircleFilled(new Vector2(x0 + (cell * 0.45f), wheelY), cell * 0.05f, panel, 8);
        dl.AddCircleFilled(new Vector2(x0 + (cell * 1.55f), wheelY), cell * 0.05f, panel, 8);
    }

    private void DrawBoardRacoon(OsAppContext ctx, double now, ImDrawListPtr dl, Vector2 boardTL, float cell)
    {
        var tl = new Vector2(boardTL.X + (this.game.X * cell),
            boardTL.Y + ((RacoonerGame.Rows - 1 - this.game.Row) * cell));

        // Shoved back down off a refused den, so the bounce is on the raccoon and not only on the bank.
        if (this.game.BumpFlash > 0f && !ctx.ReduceMotion)
        {
            tl.Y += cell * 0.3f * MathF.Min(1f, this.game.BumpFlash * 3f);
        }

        var alpha = 1f;
        if (this.game.Dying)
        {
            alpha = ctx.ReduceMotion ? 0.4f : (MathF.Sin((float)now * 12f) > 0f ? 0.9f : 0.15f);
        }
        DrawRacoon(dl, tl, cell, this.game.HopFlash > 0f, alpha);
    }

    /// <summary>The 1-cell raccoon: ears, masked eyes, and a 2-frame hop flip (paws out mid-hop, feet
    /// tucked on the ground).</summary>
    private static void DrawRacoon(ImDrawListPtr dl, Vector2 tl, float size, bool hopFrame, float alpha)
    {
        var ink = ImGui.GetColorU32(RetroLcd.Pixel with { W = alpha });
        var mask = ImGui.GetColorU32(RetroLcd.Panel with { W = MathF.Min(1f, alpha + 0.1f) });
        dl.AddTriangleFilled(tl + new Vector2(size * 0.18f, size * 0.30f),
            tl + new Vector2(size * 0.26f, size * 0.06f),
            tl + new Vector2(size * 0.40f, size * 0.26f), ink);
        dl.AddTriangleFilled(tl + new Vector2(size * 0.60f, size * 0.26f),
            tl + new Vector2(size * 0.74f, size * 0.06f),
            tl + new Vector2(size * 0.82f, size * 0.30f), ink);
        dl.AddRectFilled(tl + new Vector2(size * 0.14f, size * 0.22f),
            tl + new Vector2(size * 0.86f, size * 0.92f), ink, size * 0.26f);
        dl.AddRectFilled(tl + new Vector2(size * 0.18f, size * 0.36f),
            tl + new Vector2(size * 0.82f, size * 0.50f), mask);
        dl.AddCircleFilled(tl + new Vector2(size * 0.34f, size * 0.43f), size * 0.055f, ink, 8);
        dl.AddCircleFilled(tl + new Vector2(size * 0.66f, size * 0.43f), size * 0.055f, ink, 8);
        if (hopFrame)
        {
            dl.AddRectFilled(tl + new Vector2(0f, size * 0.66f),
                tl + new Vector2(size * 0.14f, size * 0.78f), ink, size * 0.05f);
            dl.AddRectFilled(tl + new Vector2(size * 0.86f, size * 0.66f),
                tl + new Vector2(size, size * 0.78f), ink, size * 0.05f);
        }
        else
        {
            dl.AddRectFilled(tl + new Vector2(size * 0.30f, size * 0.84f),
                tl + new Vector2(size * 0.42f, size * 0.92f), mask);
            dl.AddRectFilled(tl + new Vector2(size * 0.58f, size * 0.84f),
                tl + new Vector2(size * 0.70f, size * 0.92f), mask);
        }
    }

    private static void DrawLifePip(ImDrawListPtr dl, Vector2 center, float r)
    {
        var ink = ImGui.GetColorU32(RetroLcd.Pixel);
        dl.AddTriangleFilled(center + new Vector2(-r, -r * 0.1f),
            center + new Vector2(-r * 0.6f, -r * 1.5f),
            center + new Vector2(-r * 0.1f, -r * 0.6f), ink);
        dl.AddTriangleFilled(center + new Vector2(r * 0.1f, -r * 0.6f),
            center + new Vector2(r * 0.6f, -r * 1.5f),
            center + new Vector2(r, -r * 0.1f), ink);
        dl.AddCircleFilled(center, r, ink, 12);
    }

    private void ReadKeyboard()
    {
        if (this.keys.WasPressed(AppKey.Up) || this.keys.WasPressed(AppKey.W))
        {
            this.game.Hop(0, 1);
        }
        else if (this.keys.WasPressed(AppKey.Down) || this.keys.WasPressed(AppKey.S))
        {
            this.game.Hop(0, -1);
        }
        else if (this.keys.WasPressed(AppKey.Left) || this.keys.WasPressed(AppKey.A))
        {
            this.game.Hop(-1, 0);
        }
        else if (this.keys.WasPressed(AppKey.Right) || this.keys.WasPressed(AppKey.D))
        {
            this.game.Hop(1, 0);
        }
    }

    private void DrawDpad(Vector2 winPos, Vector2 winSize, float padH)
    {
        var key = MathF.Min(padH * 0.27f, winSize.X * 0.17f);
        var gap = key * 0.14f;
        var centerX = winPos.X + (winSize.X * 0.5f) - (key * 0.5f);
        var topY = winPos.Y + winSize.Y - padH + (padH * 0.08f);

        if (RetroLcd.KeyLabel("##racUp", "W", new Vector2(centerX, topY), key))
        {
            this.game.Hop(0, 1);
        }
        if (RetroLcd.KeyLabel("##racLeft", "A",
            new Vector2(centerX - key - gap, topY + key + gap), key))
        {
            this.game.Hop(-1, 0);
        }
        if (RetroLcd.KeyLabel("##racRight", "D",
            new Vector2(centerX + key + gap, topY + key + gap), key))
        {
            this.game.Hop(1, 0);
        }
        if (RetroLcd.KeyLabel("##racDown", "S",
            new Vector2(centerX, topY + ((key + gap) * 2f)), key))
        {
            this.game.Hop(0, -1);
        }
    }

    private void DrawPausedOverlay(OsAppContext ctx, Vector2 boardTL, float boardW, float boardH)
    {
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(boardTL, boardTL + new Vector2(boardW, boardH),
            ImGui.GetColorU32(RetroLcd.Panel with { W = 0.82f }));
        var label = ctx.Localize("os.racooner_paused");
        var labelSize = ImGui.CalcTextSize(label);
        dl.AddText(boardTL + new Vector2((boardW - labelSize.X) * 0.5f, (boardH * 0.5f) - ctx.Px(30f)),
            ImGui.GetColorU32(RetroLcd.Pixel), label);

        var buttonW = boardW * 0.7f;
        var buttonH = ctx.Px(36f);
        if (RetroLcd.Button("##racResume", ctx.Localize("os.racooner_resume"),
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
        var title = ctx.Localize(this.lastRunWasRecord ? "os.racooner_new_record" : "os.racooner_game_over");
        using (ctx.TitleFont?.Push())
        {
            var titleSize = ImGui.CalcTextSize(title);
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
                winPos + new Vector2((winSize.X - titleSize.X) * 0.5f, winSize.Y * 0.20f),
                ImGui.GetColorU32(RetroLcd.Pixel), title);
        }

        var lines = new[]
        {
            string.Format(ctx.Localize("os.racooner_score"), this.lastRunScore),
            string.Format(ctx.Localize("os.racooner_reached"), this.lastRunLevel),
            string.Format(ctx.Localize("os.racooner_banked"), this.lastRunBanked),
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
        if (RetroLcd.Button("##racAgain", ctx.Localize("os.racooner_play_again"),
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, firstY), new Vector2(buttonW, buttonH),
            ctx.Px(4f), filled: true))
        {
            StartRun();
        }
        if (RetroLcd.Button("##racMenu", ctx.Localize("os.racooner_menu"),
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
        var title = ctx.Localize("os.racooner_high_scores");
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
            var empty = ctx.Localize("os.racooner_no_scores");
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
        if (RetroLcd.Button("##racBack", ctx.Localize("os.racooner_menu"),
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, winSize.Y - buttonH - ctx.Px(24f)),
            new Vector2(buttonW, buttonH), ctx.Px(4f), filled: false))
        {
            this.splashStartedAt = ImGui.GetTime();
            this.view = View.Splash;
        }
    }

    private static float WrapCells(float v) =>
        ((v % RacoonerGame.Columns) + RacoonerGame.Columns) % RacoonerGame.Columns;
}
