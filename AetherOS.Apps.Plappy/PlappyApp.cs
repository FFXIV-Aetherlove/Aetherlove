using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AetherLove.Shared.Arcade;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Plappy;

/// <summary>Plappy Birb on the shared handheld LCD: tap anywhere to flap, thread the pillars, and watch the
/// corridor tighten every five of them. Scoring is 10 per pillar plus 5 for going through the middle.</summary>
public sealed class PlappyApp : IAetherApp
{
    private enum View { Splash, Playing, GameOver, Scores, Leaderboard }

    private static readonly Vector4 TileTopColor = new(0.45f, 0.74f, 0.86f, 1f);
    private static readonly Vector4 TileBottomColor = new(0.11f, 0.27f, 0.39f, 1f);

    private const string HighScoresKey = "high_scores";
    private const int ScoreSlots = 5;

    /// <summary>How long the "tier N" banner sits on screen after the world tightens.</summary>
    private const double TierBannerSeconds = 1.6;

    /// <summary>The bird body's half-height in <see cref="DrawBird"/> units. The playing bird is sized
    /// through this so its drawn body matches its collision radius exactly; a bird that looks taller than
    /// it collides reads as a cheat.</summary>
    private const float BirdBodyUnits = 1.7f;

    private readonly Func<string> name;
    private readonly IAppStorage storage;
    private readonly IKeyboardInput keys;
    private readonly AetherLove.Os.IArcadeRewards rewards;
    private readonly AetherLove.Os.IArcadeScores scores;
    private readonly AetherLove.Widgets.ArcadeLeaderboardView leaderboard;
    private readonly PlappyGame game = new();

    private View view = View.Splash;
    private bool paused;
    private double lastFrameTime;
    private double splashStartedAt;
    private int[] highScores = [];
    private bool scoresLoaded;
    private int lastRunScore;
    private int lastRunPillars;
    private int lastRunTier;
    private bool lastRunWasRecord;
    private int shownTier;
    private double tierBannerUntil;

    public PlappyApp(Func<string> name, IAppCapabilities capabilities, AetherLove.Os.IArcadeRewards rewards, AetherLove.Os.IArcadeScores scores)
    {
        this.name = name;
        this.storage = capabilities.Storage("plappy");
        this.keys = capabilities.Keyboard;
        this.rewards = rewards;
        this.scores = scores;
        this.leaderboard = new AetherLove.Widgets.ArcadeLeaderboardView(scores, ArcadeGame.Plappy);
    }

    public string Id => "plappy";

    public string Name => this.name();

    public FontAwesomeIcon Icon => FontAwesomeIcon.Dove;

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

    /// <summary>Leaving the app freezes the run instead of letting the bird drop while nobody is looking.</summary>
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
                DrawPlaying(ctx, delta, now, winPos, winSize);
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
        this.lastFrameTime = ImGui.GetTime();
        this.paused = false;
        this.shownTier = 0;
        this.tierBannerUntil = 0.0;
        this.view = View.Playing;
    }

    /// <summary>A finished round is the sparks signal; the server decides if it pays.</summary>
    private void FinishRun()
    {
        this.lastRunScore = this.game.Score;
        this.lastRunPillars = this.game.PillarsCleared;
        this.lastRunTier = this.game.Tier;
        this.lastRunWasRecord = this.lastRunScore > BestScore;
        this.highScores = this.highScores
            .Append(this.lastRunScore)
            .OrderByDescending(s => s)
            .Take(ScoreSlots)
            .ToArray();
        this.storage.Set(HighScoresKey, this.highScores);
        this.rewards.NoteGameFinished();
        this.scores.SubmitScore(new ArcadeScoreSubmissionDto(
            ArcadeGame.Plappy, this.lastRunScore, (int)(this.game.ElapsedSeconds * 1000f),
            this.lastRunPillars, this.lastRunTier));
    }

    private void DrawSplash(OsAppContext ctx, double now, Vector2 winPos, Vector2 winSize)
    {
        var dl = ImGui.GetWindowDrawList();
        var elapsed = now - this.splashStartedAt;
        // The word types itself on row by row, like an LCD waking up.
        var litRows = ctx.ReduceMotion
            ? RetroLcd.GlyphHeight
            : (int)Math.Min(RetroLcd.GlyphHeight, elapsed / 0.09);

        var pixel = MathF.Max(2f, winSize.X * 0.72f / RetroLcd.WordColumns("PLAPPY"));
        var wordH = RetroLcd.GlyphHeight * pixel;
        var wordY = winSize.Y * 0.13f;
        RetroLcd.DrawWordCentered(dl, "PLAPPY", winPos + new Vector2(0f, wordY), winSize.X, pixel,
            ImGui.GetColorU32(RetroLcd.Pixel), litRows);

        var subtitle = ctx.Localize("os.plappy_subtitle");
        var subSize = ImGui.CalcTextSize(subtitle);
        dl.AddText(winPos + new Vector2((winSize.X - subSize.X) * 0.5f, wordY + wordH + ctx.Px(12f)),
            ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.75f }), subtitle);

        DrawDecorFlight(ctx, dl, winPos, winSize, now);

        var buttonW = winSize.X * 0.62f;
        var buttonH = ctx.Px(38f);
        var firstY = winSize.Y * 0.54f;
        if (RetroLcd.Button("##plpPlay", ctx.Localize("os.plappy_play"),
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, firstY), new Vector2(buttonW, buttonH),
            ctx.Px(4f), filled: true))
        {
            StartRun();
        }
        if (RetroLcd.Button("##plpScores", ctx.Localize("os.plappy_high_scores"),
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, firstY + buttonH + ctx.Px(10f)),
            new Vector2(buttonW, buttonH), ctx.Px(4f), filled: false))
        {
            this.view = View.Scores;
        }
        if (RetroLcd.Button("##plpBoard", ctx.Localize("os.arcade_leaderboard"),
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, firstY + ((buttonH + ctx.Px(10f)) * 2f)),
            new Vector2(buttonW, buttonH), ctx.Px(4f), filled: false))
        {
            this.view = View.Leaderboard;
        }

        if (RetroLcd.Key("##plpExit", FontAwesomeIcon.ArrowLeft,
            winPos + new Vector2(ctx.Px(12f), ctx.Px(12f)), ctx.Px(30f)))
        {
            ctx.Shell.GoHomeToFolder(IOsShell.ArcadeFolderId);
        }

        if (BestScore > 0)
        {
            var best = string.Format(ctx.Localize("os.plappy_best"), BestScore);
            var bestSize = ImGui.CalcTextSize(best);
            dl.AddText(winPos + new Vector2((winSize.X - bestSize.X) * 0.5f, winSize.Y - ctx.Px(30f)),
                ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.7f }), best);
        }
    }

    /// <summary>A birb bobs through a pair of pillars under the title while you decide.</summary>
    private static void DrawDecorFlight(OsAppContext ctx, ImDrawListPtr dl, Vector2 winPos, Vector2 winSize, double now)
    {
        var unit = ctx.Px(2.6f);
        var bandY = winPos.Y + (winSize.Y * 0.42f);
        var ink = ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.5f });
        var gapHalf = unit * 5f;
        for (var i = 0; i < 2; i++)
        {
            var x = winPos.X + (winSize.X * (0.24f + (i * 0.42f)));
            var center = bandY + (i == 0 ? -unit * 2f : unit * 2f);
            dl.AddRectFilled(new Vector2(x, bandY - (unit * 9f)), new Vector2(x + (unit * 4f), center - gapHalf), ink);
            dl.AddRectFilled(new Vector2(x, center + gapHalf), new Vector2(x + (unit * 4f), bandY + (unit * 9f)), ink);
        }

        var bob = ctx.ReduceMotion ? 0f : MathF.Sin((float)now * 3.4f) * unit * 1.6f;
        var tilt = ctx.ReduceMotion ? 0f : MathF.Cos((float)now * 3.4f) * -0.8f;
        DrawBird(dl, new Vector2(winPos.X + (winSize.X * 0.5f), bandY + bob), unit, tilt);
    }

    private void DrawPlaying(OsAppContext ctx, float delta, double now, Vector2 winPos, Vector2 winSize)
    {
        var hudH = ctx.Px(44f);
        var boardMaxW = winSize.X - ctx.Px(12f);
        var boardMaxH = winSize.Y - hudH - ctx.Px(14f);
        var scale = MathF.Min(boardMaxW / PlappyGame.Width, boardMaxH / PlappyGame.Height);
        var boardW = PlappyGame.Width * scale;
        var boardH = PlappyGame.Height * scale;
        var boardTL = new Vector2(winPos.X + ((winSize.X - boardW) * 0.5f),
            winPos.Y + hudH + ((boardMaxH - boardH) * 0.5f));

        if (RetroLcd.WindowBlurred() || this.keys.GameTextFocused)
        {
            this.paused = true;
        }

        DrawHud(ctx, winPos, winSize, hudH);

        if (!this.paused)
        {
            ReadFlapInput(boardTL, boardW, boardH);
            this.game.Tick(delta);
            if (this.game.Tier > this.shownTier)
            {
                this.shownTier = this.game.Tier;
                this.tierBannerUntil = now + TierBannerSeconds;
            }
            if (this.game.Dead)
            {
                this.view = View.GameOver;
                FinishRun();
            }
        }

        DrawField(ctx, boardTL, scale, boardW, boardH);
        if (this.paused)
        {
            DrawPausedOverlay(ctx, boardTL, boardW, boardH);
        }
        else if (this.game.Waiting)
        {
            DrawReadyPrompt(ctx, now, boardTL, boardW, boardH);
        }
        else if (now < this.tierBannerUntil)
        {
            DrawTierBanner(ctx, boardTL, boardW, boardH);
        }
    }

    /// <summary>The whole play area is the flap button, and the keyboard mirrors it. Keys are only polled
    /// during a live run, because polling takes the keyboard away from the game.</summary>
    private void ReadFlapInput(Vector2 boardTL, float boardW, float boardH)
    {
        ImGui.SetCursorScreenPos(boardTL);
        ImGui.InvisibleButton("##plpField", new Vector2(boardW, boardH));
        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }
        if (ImGui.IsItemClicked()
            || this.keys.WasPressed(AppKey.Space)
            || this.keys.WasPressed(AppKey.Up)
            || this.keys.WasPressed(AppKey.W))
        {
            this.game.Flap();
        }
    }

    private void DrawHud(OsAppContext ctx, Vector2 winPos, Vector2 winSize, float hudH)
    {
        var dl = ImGui.GetWindowDrawList();
        var padX = ctx.Px(12f);
        var line = ImGui.GetTextLineHeight();
        dl.AddText(winPos + new Vector2(padX, ctx.Px(6f)), ImGui.GetColorU32(RetroLcd.Pixel),
            string.Format(ctx.Localize("os.plappy_score"), this.game.Score));
        dl.AddText(winPos + new Vector2(padX, ctx.Px(6f) + line), ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.75f }),
            string.Format(ctx.Localize("os.plappy_tier"), this.game.Tier + 1));

        if (!this.paused && RetroLcd.PauseKey(winPos, winSize, padX, hudH))
        {
            this.paused = true;
        }

        var best = string.Format(ctx.Localize("os.plappy_best"), Math.Max(BestScore, this.game.Score));
        var bestSize = ImGui.CalcTextSize(best);
        dl.AddText(winPos + new Vector2(winSize.X - padX - RetroLcd.PauseKeyWidth(hudH) - ctx.Px(8f) - bestSize.X,
            ctx.Px(6f)), ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.7f }), best);
    }

    private void DrawField(OsAppContext ctx, Vector2 boardTL, float scale, float boardW, float boardH)
    {
        var dl = ImGui.GetWindowDrawList();
        var ink = ImGui.GetColorU32(RetroLcd.Pixel);
        var faint = ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.42f });
        dl.AddRect(boardTL - new Vector2(2f, 2f), boardTL + new Vector2(boardW + 2f, boardH + 2f),
            ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.5f }), 0f, ImDrawFlags.None, 2f);
        dl.PushClipRect(boardTL, boardTL + new Vector2(boardW, boardH), true);

        var groundY = boardTL.Y + (PlappyGame.GroundY * scale);
        dl.AddLine(new Vector2(boardTL.X, groundY), new Vector2(boardTL.X + boardW, groundY), ink, 2f);
        for (var x = 0f; x < boardW; x += ctx.Px(7f))
        {
            dl.AddLine(new Vector2(boardTL.X + x, groundY + ctx.Px(3f)),
                new Vector2(boardTL.X + x + ctx.Px(3f), boardTL.Y + boardH), faint, 1.5f);
        }

        foreach (var pillar in this.game.Pillars)
        {
            var left = boardTL.X + (pillar.X * scale);
            var right = left + (PlappyGame.PillarWidth * scale);
            var top = boardTL.Y + (pillar.GapTop * scale);
            var bottom = boardTL.Y + (pillar.GapBottom * scale);
            var lip = ctx.Px(3f);
            var body = ImGui.GetColorU32(RetroLcd.Pixel with { W = pillar.Cleared ? 0.72f : 1f });
            dl.AddRectFilled(new Vector2(left + lip, boardTL.Y), new Vector2(right - lip, top - lip), body);
            dl.AddRectFilled(new Vector2(left, top - lip), new Vector2(right, top), body);
            dl.AddRectFilled(new Vector2(left, bottom), new Vector2(right, bottom + lip), body);
            dl.AddRectFilled(new Vector2(left + lip, bottom + lip), new Vector2(right - lip, groundY), body);
        }

        var tilt = Math.Clamp(this.game.BirdVelocity / 70f, -1f, 1f);
        DrawBird(dl, boardTL + new Vector2(PlappyGame.BirdX * scale, this.game.BirdY * scale),
            PlappyGame.BirdRadius * scale / BirdBodyUnits, tilt);

        if (this.game.ThreadFlash > 0f)
        {
            var label = $"+{PlappyGame.ThreadBonus}";
            var size = ImGui.CalcTextSize(label);
            var rise = (0.6f - this.game.ThreadFlash) * ctx.Px(22f);
            dl.AddText(boardTL + new Vector2((PlappyGame.BirdX * scale) - (size.X * 0.5f),
                (this.game.BirdY * scale) - ctx.Px(20f) - rise),
                ImGui.GetColorU32(RetroLcd.Pixel with { W = Math.Clamp(this.game.ThreadFlash / 0.6f, 0f, 1f) }), label);
        }

        dl.PopClipRect();
    }

    /// <summary>The birb, as a handheld LCD would have it: fixed segments, and a nose that dips or lifts
    /// with the climb instead of a real rotation.</summary>
    private static void DrawBird(ImDrawListPtr dl, Vector2 center, float unit, float tilt)
    {
        var ink = ImGui.GetColorU32(RetroLcd.Pixel);
        var lit = ImGui.GetColorU32(RetroLcd.Panel);
        var nose = tilt * unit * 1.3f;

        dl.AddRectFilled(center + new Vector2(-unit * 2.4f, -unit * BirdBodyUnits),
            center + new Vector2(unit * 1.6f, unit * BirdBodyUnits), ink, unit * 0.8f);
        dl.AddTriangleFilled(
            center + new Vector2(unit * 1.4f, -unit * 0.5f + nose),
            center + new Vector2(unit * 3.4f, unit * 0.2f + nose),
            center + new Vector2(unit * 1.4f, unit * 0.9f + nose), ink);
        dl.AddTriangleFilled(
            center + new Vector2(-unit * 2.2f, -unit * 0.9f - nose),
            center + new Vector2(-unit * 3.9f, -unit * 1.6f - nose),
            center + new Vector2(-unit * 2.2f, unit * 0.6f - nose), ink);

        var wingY = tilt < -0.2f ? -unit * 1.6f : tilt > 0.35f ? unit * 0.5f : -unit * 0.5f;
        dl.AddRectFilled(center + new Vector2(-unit * 1.7f, wingY),
            center + new Vector2(unit * 0.4f, wingY + (unit * 1.2f)), lit, unit * 0.4f);
        dl.AddCircleFilled(center + new Vector2(unit * 0.7f, -unit * 0.7f), MathF.Max(1f, unit * 0.35f), lit);
    }

    private void DrawReadyPrompt(OsAppContext ctx, double now, Vector2 boardTL, float boardW, float boardH)
    {
        var dl = ImGui.GetWindowDrawList();
        var label = ctx.Localize("os.plappy_tap");
        var size = ImGui.CalcTextSize(label);
        var bob = ctx.ReduceMotion ? 0f : MathF.Sin((float)now * 3f) * ctx.Px(3f);
        dl.AddText(boardTL + new Vector2((boardW - size.X) * 0.5f, (boardH * 0.62f) + bob),
            ImGui.GetColorU32(RetroLcd.Pixel), label);
    }

    private void DrawTierBanner(OsAppContext ctx, Vector2 boardTL, float boardW, float boardH)
    {
        var dl = ImGui.GetWindowDrawList();
        var label = string.Format(ctx.Localize("os.plappy_tier_up"), this.shownTier + 1);
        var size = ImGui.CalcTextSize(label);
        var pad = ctx.Px(8f);
        var tl = boardTL + new Vector2((boardW - size.X) * 0.5f - pad, (boardH * 0.12f) - pad);
        dl.AddRectFilled(tl, tl + size + new Vector2(pad * 2f, pad * 2f),
            ImGui.GetColorU32(RetroLcd.Pixel), ctx.Px(4f));
        dl.AddText(tl + new Vector2(pad, pad), ImGui.GetColorU32(RetroLcd.Panel), label);
    }

    private void DrawPausedOverlay(OsAppContext ctx, Vector2 boardTL, float boardW, float boardH)
    {
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(boardTL, boardTL + new Vector2(boardW, boardH),
            ImGui.GetColorU32(RetroLcd.Panel with { W = 0.82f }));
        var label = ctx.Localize("os.plappy_paused");
        var labelSize = ImGui.CalcTextSize(label);
        dl.AddText(boardTL + new Vector2((boardW - labelSize.X) * 0.5f, (boardH * 0.5f) - ctx.Px(30f)),
            ImGui.GetColorU32(RetroLcd.Pixel), label);

        var buttonW = boardW * 0.7f;
        var buttonH = ctx.Px(36f);
        if (RetroLcd.Button("##plpResume", ctx.Localize("os.plappy_resume"),
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
        var title = ctx.Localize(this.lastRunWasRecord ? "os.plappy_new_record" : "os.plappy_game_over");
        using (ctx.TitleFont?.Push())
        {
            var titleSize = ImGui.CalcTextSize(title);
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
                winPos + new Vector2((winSize.X - titleSize.X) * 0.5f, winSize.Y * 0.20f),
                ImGui.GetColorU32(RetroLcd.Pixel), title);
        }

        var lines = new[]
        {
            string.Format(ctx.Localize("os.plappy_score"), this.lastRunScore),
            string.Format(ctx.Localize("os.plappy_pillars"), this.lastRunPillars),
            string.Format(ctx.Localize("os.plappy_tier"), this.lastRunTier + 1),
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
        if (RetroLcd.Button("##plpAgain", ctx.Localize("os.plappy_play_again"),
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, firstY), new Vector2(buttonW, buttonH),
            ctx.Px(4f), filled: true))
        {
            StartRun();
        }
        if (RetroLcd.Button("##plpMenu", ctx.Localize("os.plappy_menu"),
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
        var title = ctx.Localize("os.plappy_high_scores");
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
            var empty = ctx.Localize("os.plappy_no_scores");
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
        if (RetroLcd.Button("##plpBack", ctx.Localize("os.plappy_menu"),
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, winSize.Y - buttonH - ctx.Px(24f)),
            new Vector2(buttonW, buttonH), ctx.Px(4f), filled: false))
        {
            this.splashStartedAt = ImGui.GetTime();
            this.view = View.Splash;
        }
    }
}
