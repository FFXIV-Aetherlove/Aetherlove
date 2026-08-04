using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AetherLove.Shared.Arcade;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.MeteorCommand;

/// <summary>Meteor Command on the shared handheld LCD: tap the sky to detonate an interceptor and shield
/// six settlements from the shower. Waves get faster, the magazine does not get bigger.</summary>
public sealed class MeteorCommandApp : IAetherApp
{
    private enum View { Splash, Playing, GameOver, Scores, Leaderboard }

    private static readonly Vector4 TileTopColor = new(0.83f, 0.47f, 0.29f, 1f);
    private static readonly Vector4 TileBottomColor = new(0.36f, 0.13f, 0.16f, 1f);

    private const string HighScoresKey = "high_scores";
    private const int ScoreSlots = 5;

    private readonly Func<string> name;
    private readonly IAppStorage storage;
    private readonly IKeyboardInput keys;
    private readonly AetherLove.Os.IArcadeRewards rewards;
    private readonly AetherLove.Os.IArcadeScores scores;
    private readonly AetherLove.Widgets.ArcadeLeaderboardView leaderboard;
    private readonly MeteorGame game = new();

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

    public MeteorCommandApp(Func<string> name, IAppCapabilities capabilities, AetherLove.Os.IArcadeRewards rewards, AetherLove.Os.IArcadeScores scores)
    {
        this.name = name;
        this.storage = capabilities.Storage("meteor");
        this.keys = capabilities.Keyboard;
        this.rewards = rewards;
        this.scores = scores;
        this.leaderboard = new AetherLove.Widgets.ArcadeLeaderboardView(scores, ArcadeGame.Meteor);
    }

    public string Id => "meteor";

    public string Name => this.name();

    public FontAwesomeIcon Icon => FontAwesomeIcon.Meteor;

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
            ArcadeGame.Meteor, this.lastRunScore, (int)(this.runSeconds * 1000.0), this.lastRunWave));
    }

    private void DrawSplash(OsAppContext ctx, double now, Vector2 winPos, Vector2 winSize)
    {
        var dl = ImGui.GetWindowDrawList();
        var elapsed = now - this.splashStartedAt;
        var litRows = ctx.ReduceMotion
            ? RetroLcd.GlyphHeight
            : (int)Math.Min(RetroLcd.GlyphHeight, elapsed / 0.09);

        var pixel = MathF.Max(2f, winSize.X * 0.74f / RetroLcd.WordColumns("METEOR"));
        var wordY = winSize.Y * 0.13f;
        RetroLcd.DrawWordCentered(dl, "METEOR", winPos + new Vector2(0f, wordY), winSize.X, pixel,
            ImGui.GetColorU32(RetroLcd.Pixel), litRows);

        var small = MathF.Max(1.5f, pixel * 0.58f);
        RetroLcd.DrawWordCentered(dl, "COMMAND",
            winPos + new Vector2(0f, wordY + (RetroLcd.GlyphHeight * pixel) + ctx.Px(8f)), winSize.X, small,
            ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.8f }), litRows);

        var subtitle = ctx.Localize("os.meteor_subtitle");
        var subSize = ImGui.CalcTextSize(subtitle);
        dl.AddText(winPos + new Vector2((winSize.X - subSize.X) * 0.5f,
            wordY + (RetroLcd.GlyphHeight * (pixel + small)) + ctx.Px(22f)),
            ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.75f }), subtitle);

        DrawDecorSky(dl, ctx, winPos, winSize, now);

        var buttonW = winSize.X * 0.62f;
        var buttonH = ctx.Px(38f);
        var firstY = winSize.Y * 0.53f;
        if (RetroLcd.Button("##metPlay", ctx.Localize("os.meteor_play"),
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, firstY), new Vector2(buttonW, buttonH),
            ctx.Px(4f), filled: true))
        {
            StartRun();
        }
        if (RetroLcd.Button("##metScores", ctx.Localize("os.meteor_high_scores"),
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, firstY + buttonH + ctx.Px(10f)),
            new Vector2(buttonW, buttonH), ctx.Px(4f), filled: false))
        {
            this.view = View.Scores;
        }
        if (RetroLcd.Button("##metBoard", ctx.Localize("os.arcade_leaderboard"),
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, firstY + ((buttonH + ctx.Px(10f)) * 2f)),
            new Vector2(buttonW, buttonH), ctx.Px(4f), filled: false))
        {
            this.view = View.Leaderboard;
        }

        if (RetroLcd.Key("##metExit", Dalamud.Interface.FontAwesomeIcon.ArrowLeft,
            winPos + new Vector2(ctx.Px(12f), ctx.Px(12f)), ctx.Px(30f)))
        {
            ctx.Shell.GoHomeToFolder(IOsShell.ArcadeFolderId);
        }

        if (BestScore > 0)
        {
            var best = string.Format(ctx.Localize("os.meteor_best"), BestScore);
            var bestSize = ImGui.CalcTextSize(best);
            dl.AddText(winPos + new Vector2((winSize.X - bestSize.X) * 0.5f, winSize.Y - ctx.Px(30f)),
                ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.7f }), best);
        }
    }

    /// <summary>A skyline with a couple of streaks drifting over it, under the title.</summary>
    private static void DrawDecorSky(ImDrawListPtr dl, OsAppContext ctx, Vector2 winPos, Vector2 winSize, double now)
    {
        var groundY = winPos.Y + (winSize.Y * 0.52f);
        var color = ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.85f });
        dl.AddRectFilled(new Vector2(winPos.X + ctx.Px(20f), groundY),
            new Vector2(winPos.X + winSize.X - ctx.Px(20f), groundY + ctx.Px(2f)), color);

        var block = ctx.Px(5f);
        for (var i = 0; i < 5; i++)
        {
            var x = winPos.X + (winSize.X * (0.22f + (i * 0.14f)));
            var height = block * (2f + (i % 3));
            dl.AddRectFilled(new Vector2(x, groundY - height), new Vector2(x + block, groundY), color);
        }

        if (ctx.ReduceMotion)
        {
            return;
        }
        for (var i = 0; i < 3; i++)
        {
            var phase = (float)(((now * 0.35) + (i * 0.37)) % 1.0);
            var from = new Vector2(winPos.X + (winSize.X * (0.15f + (i * 0.3f))), winPos.Y + (winSize.Y * 0.30f));
            var to = from + (new Vector2(ctx.Px(26f), ctx.Px(34f)) * phase);
            dl.AddLine(from, to, ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.5f }), 1.5f);
            dl.AddCircleFilled(to, ctx.Px(2f), ImGui.GetColorU32(RetroLcd.Pixel));
        }
    }

    private void DrawPlaying(OsAppContext ctx, float delta, Vector2 winPos, Vector2 winSize)
    {
        var hudH = ctx.Px(44f);
        var boardTop = winPos.Y + hudH;
        var boardMaxW = winSize.X - ctx.Px(12f);
        var boardMaxH = winSize.Y - hudH - ctx.Px(14f);
        var scale = MathF.Min(boardMaxW / MeteorGame.Width, boardMaxH / MeteorGame.Height);
        var boardW = MeteorGame.Width * scale;
        var boardH = MeteorGame.Height * scale;
        var boardTL = new Vector2(winPos.X + ((winSize.X - boardW) * 0.5f), boardTop + ((boardMaxH - boardH) * 0.5f));

        if (RetroLcd.WindowBlurred() || this.keys.GameTextFocused)
        {
            this.paused = true;
        }

        if (!this.paused)
        {
            ReadFireInput(boardTL, scale, boardW, boardH);
            this.runSeconds += delta;
            this.game.Tick(delta);
            if (this.game.Dead)
            {
                this.view = View.GameOver;
                FinishRun();
            }
        }

        DrawHud(ctx, winPos, winSize, hudH);
        DrawField(ctx, boardTL, scale, boardW, boardH);
        if (this.paused)
        {
            DrawPausedOverlay(ctx, boardTL, boardW, boardH);
        }
    }

    /// <summary>The whole play area is the fire button: tap the sky and the interceptor flies there.</summary>
    private void ReadFireInput(Vector2 boardTL, float scale, float boardW, float boardH)
    {
        ImGui.SetCursorScreenPos(boardTL);
        ImGui.InvisibleButton("##metField", new Vector2(boardW, boardH));
        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }
        if (!ImGui.IsItemClicked())
        {
            return;
        }
        var mouse = ImGui.GetIO().MousePos;
        this.game.Fire((mouse - boardTL) / scale);
    }

    private void DrawHud(OsAppContext ctx, Vector2 winPos, Vector2 winSize, float hudH)
    {
        var dl = ImGui.GetWindowDrawList();
        var padX = ctx.Px(12f);
        var line = ImGui.GetTextLineHeight();
        dl.AddText(winPos + new Vector2(padX, ctx.Px(6f)), ImGui.GetColorU32(RetroLcd.Pixel),
            string.Format(ctx.Localize("os.meteor_score"), this.game.Score));
        dl.AddText(winPos + new Vector2(padX, ctx.Px(6f) + line), ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.75f }),
            string.Format(ctx.Localize("os.meteor_wave"), this.game.Wave));

        if (!this.paused && RetroLcd.PauseKey(winPos, winSize, padX, hudH))
        {
            this.paused = true;
        }

        var ammo = string.Format(ctx.Localize("os.meteor_ammo"), this.game.Ammo);
        var ammoSize = ImGui.CalcTextSize(ammo);
        var ammoColor = this.game.Ammo <= 5 ? RetroLcd.Pixel with { W = 0.55f } : RetroLcd.Pixel;
        dl.AddText(winPos + new Vector2(winSize.X - padX - RetroLcd.PauseKeyWidth(hudH) - ctx.Px(8f) - ammoSize.X,
            ctx.Px(6f)), ImGui.GetColorU32(ammoColor), ammo);
    }

    private void DrawField(OsAppContext ctx, Vector2 boardTL, float scale, float boardW, float boardH)
    {
        var dl = ImGui.GetWindowDrawList();
        var ink = ImGui.GetColorU32(RetroLcd.Pixel);
        var faint = ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.45f });
        dl.AddRect(boardTL - new Vector2(2f, 2f), boardTL + new Vector2(boardW + 2f, boardH + 2f),
            ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.5f }), 0f, ImDrawFlags.None, 2f);

        // Meteors are spawned above the sky line so they enter already moving; clipping to the board keeps
        // that head start from being drawn outside the frame.
        dl.PushClipRect(boardTL, boardTL + new Vector2(boardW, boardH), true);

        Vector2 ToScreen(Vector2 unit) => boardTL + (unit * scale);

        var groundY = boardTL.Y + (MeteorGame.GroundY * scale);
        dl.AddRectFilled(new Vector2(boardTL.X, groundY), new Vector2(boardTL.X + boardW, groundY + (2f * scale)), ink);

        for (var i = 0; i < MeteorGame.CityCount; i++)
        {
            var cx = boardTL.X + (MeteorGame.CityCenter(i) * scale);
            var block = 2.4f * scale;
            if (this.game.CityAlive(i))
            {
                for (var b = 0; b < 3; b++)
                {
                    var height = block * (b == 1 ? 2.4f : 1.6f);
                    var x = cx + ((b - 1) * block * 1.15f) - (block * 0.5f);
                    dl.AddRectFilled(new Vector2(x, groundY - height), new Vector2(x + block, groundY), ink);
                }
            }
            else
            {
                dl.AddRectFilled(new Vector2(cx - (block * 1.6f), groundY - (block * 0.4f)),
                    new Vector2(cx + (block * 1.6f), groundY), faint);
            }
        }

        var batteryX = boardTL.X + (MeteorGame.Width * 0.5f * scale);
        var size = 3.2f * scale;
        dl.AddTriangleFilled(new Vector2(batteryX, groundY - (size * 1.6f)),
            new Vector2(batteryX - size, groundY), new Vector2(batteryX + size, groundY), ink);

        foreach (var meteor in this.game.Meteors)
        {
            var head = ToScreen(meteor.Pos);
            dl.AddLine(ToScreen(meteor.Start), head, faint, 1.5f);
            dl.AddCircleFilled(head, MathF.Max(1.5f, 1.1f * scale), ink);
        }

        foreach (var shot in this.game.Shots)
        {
            var head = ToScreen(shot.Pos);
            dl.AddLine(new Vector2(batteryX, groundY - (size * 1.6f)), head,
                ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.6f }), 1.5f);
            dl.AddRectFilled(head - new Vector2(1.5f, 1.5f), head + new Vector2(1.5f, 1.5f), ink);
        }

        foreach (var blast in this.game.Blasts)
        {
            var center = ToScreen(blast.Center);
            dl.AddCircleFilled(center, blast.Radius * scale, ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.85f }));
            dl.AddCircle(center, blast.Radius * scale, ink, 0, 2f);
        }

        if (this.game.Banner is { } bonus)
        {
            var text = string.Format(ctx.Localize("os.meteor_wave_clear"), bonus);
            var textSize = ImGui.CalcTextSize(text);
            dl.AddText(new Vector2(boardTL.X + ((boardW - textSize.X) * 0.5f), boardTL.Y + (boardH * 0.42f)), ink, text);
        }

        dl.PopClipRect();
    }

    private void DrawPausedOverlay(OsAppContext ctx, Vector2 boardTL, float boardW, float boardH)
    {
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(boardTL, boardTL + new Vector2(boardW, boardH),
            ImGui.GetColorU32(RetroLcd.Panel with { W = 0.82f }));
        var label = ctx.Localize("os.meteor_paused");
        var labelSize = ImGui.CalcTextSize(label);
        dl.AddText(boardTL + new Vector2((boardW - labelSize.X) * 0.5f, (boardH * 0.5f) - ctx.Px(30f)),
            ImGui.GetColorU32(RetroLcd.Pixel), label);

        var buttonW = boardW * 0.7f;
        var buttonH = ctx.Px(36f);
        if (RetroLcd.Button("##metResume", ctx.Localize("os.meteor_resume"),
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
        var title = ctx.Localize(this.lastRunWasRecord ? "os.meteor_new_record" : "os.meteor_game_over");
        using (ctx.TitleFont?.Push())
        {
            var titleSize = ImGui.CalcTextSize(title);
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
                winPos + new Vector2((winSize.X - titleSize.X) * 0.5f, winSize.Y * 0.20f),
                ImGui.GetColorU32(RetroLcd.Pixel), title);
        }

        var lines = new[]
        {
            string.Format(ctx.Localize("os.meteor_score"), this.lastRunScore),
            string.Format(ctx.Localize("os.meteor_reached"), this.lastRunWave),
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
        if (RetroLcd.Button("##metAgain", ctx.Localize("os.meteor_play_again"),
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, firstY), new Vector2(buttonW, buttonH),
            ctx.Px(4f), filled: true))
        {
            StartRun();
        }
        if (RetroLcd.Button("##metMenu", ctx.Localize("os.meteor_menu"),
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
            var title = ctx.Localize("os.meteor_high_scores");
            var titleSize = ImGui.CalcTextSize(title);
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
                winPos + new Vector2((winSize.X - titleSize.X) * 0.5f, winSize.Y * 0.12f),
                ImGui.GetColorU32(RetroLcd.Pixel), title);
        }

        var padX = ctx.Px(28f);
        var y = winSize.Y * 0.26f;
        if (this.highScores.Length == 0)
        {
            var empty = ctx.Localize("os.meteor_no_scores");
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
        if (RetroLcd.Button("##metBack", ctx.Localize("os.meteor_menu"),
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, winSize.Y - buttonH - ctx.Px(24f)),
            new Vector2(buttonW, buttonH), ctx.Px(4f), filled: false))
        {
            this.splashStartedAt = ImGui.GetTime();
            this.view = View.Splash;
        }
    }
}
