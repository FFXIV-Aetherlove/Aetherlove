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

/// <summary>Stacker on the handheld LCD, in two rulesets the player picks on the splash. Classic is the
/// untouched original: flat LCD look, simple kicks, classic line scoring. Modern is the guideline
/// ruleset (SRS kicks, hold, T-spins, back-to-back, combos, skins), contributed by Vavenn and reworked
/// in-house; it scores into its own domain (ArcadeGame.StackerModern) with its own leaderboard, because
/// modern scoring is a different currency.</summary>
public sealed class StackerApp : IAetherApp
{
    private enum View { Splash, Playing, GameOver, Scores, Leaderboard, Controls }

    private enum Mode { Classic, Modern }

    private enum ControlAction { MoveLeft, MoveRight, RotateLeft, RotateRight, Hold, MoveDown, HardDrop }

    private static readonly Vector4 TileTopColor = new(0.42f, 0.68f, 0.78f, 1f);
    private static readonly Vector4 TileBottomColor = new(0.13f, 0.26f, 0.33f, 1f);

    private const string HighScoresKey = "high_scores";
    private const string ModernHighScoresKey = "high_scores_modern";
    private const string InputBindingsKey = "input_bindings";
    private const string ModeKey = "mode";
    private const int ScoreSlots = 5;
    private const int ScoreFeedbackFadePieces = 8;
    /// <summary>How much bigger than the gameplay cell the dimmed background's tiling is drawn.</summary>
    private const float AppBackgroundTileScale = 3f;
    private const float LineClearFlashSeconds = 0.4f;
    private const float LineClearFlashStartAlpha = 0.8f;
    private const float HardDropFlashStartAlpha = 0.6f;


    /// <summary>The one skin that keeps the handheld's green LCD look everywhere else; every other skin
    /// gets a neutral grey board/box background so its own art reads correctly.</summary>
    private static readonly Vector4 NeutralBoardBackground = new(168f / 255f, 168f / 255f, 168f / 255f, 1f);

    private static readonly AppKey[] DefaultBindings =
    [AppKey.A, AppKey.D, AppKey.Up, AppKey.W, AppKey.E, AppKey.S, AppKey.Space];

    private readonly Func<string> name;
    private readonly IAppStorage storage;
    private readonly IKeyboardInput keys;
    private readonly AetherLove.Os.IArcadeRewards rewards;
    private readonly AetherLove.Os.IArcadeScores scores;
    private readonly AetherLove.Widgets.ArcadeLeaderboardView classicLeaderboard;
    private readonly AetherLove.Widgets.ArcadeLeaderboardView modernLeaderboard;
    private readonly StackerArt art;
    private readonly StackerGame classic = new();
    private readonly StackerModernGame modern = new();

    private View view = View.Splash;
    private Mode mode = Mode.Classic;
    private bool paused;
    private double lastFrameTime;
    private double splashStartedAt;
    private double repeatAccumulator;
    private double touchDropAccumulator;
    private int[] highScores = [];
    private int[] modernHighScores = [];
    private bool scoresLoaded;
    private int lastRunScore;
    private int lastRunLines;
    private int lastRunLevel;
    private bool lastRunWasRecord;
    private double runSeconds;
    private AppKey[] bindings = (AppKey[])DefaultBindings.Clone();
    private int rebindingAction = -1;
    private int lastSeenLockCount = -1;
    private int lastSeenLineClearLockCount = -1;
    private double lineClearFlashElapsed = LineClearFlashSeconds;
    private int lastSeenHardDropCount = -1;
    private double hardDropFlashElapsed = StackerModernGame.HardDropLockoutSeconds;
    private string scoreFeedback = string.Empty;
    private int scoreFeedbackAge = ScoreFeedbackFadePieces;
    /// <summary>The one skin. Modern shipped with a picker and four sheets; the owner cut it to the
    /// handheld's own green look (2026-08-18), so the sheets for the rest are gone too.</summary>
    private const string skin = "retro_green";

    public StackerApp(Func<string> name, IAppCapabilities capabilities, AetherLove.Os.IArcadeRewards rewards, AetherLove.Os.IArcadeScores scores)
    {
        this.name = name;
        this.storage = capabilities.Storage("stacker");
        this.keys = capabilities.Keyboard;
        this.art = new StackerArt(capabilities.Textures);
        this.rewards = rewards;
        this.scores = scores;
        this.classicLeaderboard = new AetherLove.Widgets.ArcadeLeaderboardView(scores, ArcadeGame.Stacker);
        this.modernLeaderboard = new AetherLove.Widgets.ArcadeLeaderboardView(scores, ArcadeGame.StackerModern);
    }

    public string Id => "stacker";

    public string Name => this.name();

    public FontAwesomeIcon Icon => FontAwesomeIcon.Th;

    public Vector4 TileTop => TileTopColor;

    public Vector4 TileBottom => TileBottomColor;

    public int Badge => 0;

    public bool HasSurface => true;

    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Strings => Localization.AppStrings.Packs;

    private bool Modern => this.mode == Mode.Modern;

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
        if (this.view != View.Playing || !this.Modern)
        {
            dl.AddRectFilled(winPos, winPos + winSize, ImGui.GetColorU32(RetroLcd.Panel));
        }

        switch (this.view)
        {
            case View.Playing:
                if (this.Modern)
                {
                    DrawPlayingModern(ctx, delta, winPos, winSize);
                }
                else
                {
                    DrawPlayingClassic(ctx, delta, winPos, winSize);
                }
                break;
            case View.GameOver:
                DrawGameOver(ctx, winPos, winSize);
                break;
            case View.Scores:
                DrawScores(ctx, winPos, winSize);
                break;
            case View.Leaderboard:
                (this.Modern ? this.modernLeaderboard : this.classicLeaderboard).Draw(ctx, winPos, winSize, () =>
                {
                    this.splashStartedAt = ImGui.GetTime();
                    this.view = View.Splash;
                });
                break;
            case View.Controls:
                DrawControlsMenu(ctx, winPos, winSize);
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
        this.modernHighScores = this.storage.Get<int[]>(ModernHighScoresKey) ?? [];
        if (this.storage.Get<string>(ModeKey) == "modern")
        {
            this.mode = Mode.Modern;
        }
        var savedBindings = this.storage.Get<AppKey[]>(InputBindingsKey);
        if (savedBindings is { Length: 7 } && savedBindings.All(key => Enum.IsDefined(key)))
        {
            this.bindings = savedBindings;
        }
    }

    private int[] CurrentHighScores => this.Modern ? this.modernHighScores : this.highScores;

    private int BestScore => this.CurrentHighScores.Length > 0 ? this.CurrentHighScores[0] : 0;

    /// <summary>The playfield/preview-box fill: the retro-green skin keeps the handheld's own tint,
    /// every other skin gets a neutral grey so its own art shows true colour.</summary>
    private Vector4 BoardBackground => skin == "retro_green" ? RetroLcd.Panel : NeutralBoardBackground;

    /// <summary>Chrome (text/icons) colour: the retro-green skin keeps the handheld's green-on-green look,
    /// every other skin goes plain black-and-white instead.</summary>
    private Vector4 ThemeInk => skin == "retro_green" ? RetroLcd.Pixel : new Vector4(1f, 1f, 1f, 1f);

    /// <summary>The fill colour behind solid/filled chrome, paired with <see cref="ThemeInk"/>.</summary>
    private Vector4 ThemePaper => skin == "retro_green" ? RetroLcd.Panel : new Vector4(0f, 0f, 0f, 1f);

    /// <summary>The pause key's own ink: <see cref="ThemeInk"/>'s dark green reads far too close to the
    /// dimmed app background behind it, so on the retro-green skin it borrows the panel's light green
    /// instead (with the icon flipping to the dark ink once the key is held, for contrast against its fill).</summary>
    private Vector4 PauseKeyInk => skin == "retro_green" ? RetroLcd.Panel : this.ThemeInk;

    private Vector4 PauseKeyPaper => skin == "retro_green" ? RetroLcd.Pixel : this.ThemePaper;

    private void StartRun()
    {
        if (this.Modern)
        {
            this.modern.Reset();
            this.lastSeenLockCount = this.modern.LockCount;
            this.lastSeenLineClearLockCount = this.modern.LockCount;
            this.lineClearFlashElapsed = LineClearFlashSeconds;
            this.lastSeenHardDropCount = this.modern.HardDropCount;
            this.hardDropFlashElapsed = StackerModernGame.HardDropLockoutSeconds;
            this.scoreFeedback = string.Empty;
            this.scoreFeedbackAge = ScoreFeedbackFadePieces;
        }
        else
        {
            this.classic.Reset();
        }
        this.runSeconds = 0.0;
        this.lastFrameTime = ImGui.GetTime();
        this.paused = false;
        this.autoPauseGraceUntil = ImGui.GetTime() + AutoPauseGraceSeconds;
        this.view = View.Playing;
    }

    // Blur carries a shared streak from whichever LCD screen last polled it, and the keyboard capture
    // flickers focus for a frame or two when a run begins; without a grace, a fresh run can pause on
    // its very first frame.
    private const double AutoPauseGraceSeconds = 0.4;
    private double autoPauseGraceUntil;

    private bool ShouldAutoPause()
    {
        if (this.paused || ImGui.GetTime() < this.autoPauseGraceUntil)
        {
            return false;
        }
        return RetroLcd.WindowBlurred() || this.keys.GameTextFocused;
    }

    /// <summary>A finished round is the sparks signal; the server decides if it pays. Each mode submits
    /// into its own score domain so the two rulesets never share a ceiling or a board.</summary>
    private void FinishRun()
    {
        if (this.Modern)
        {
            this.lastRunScore = this.modern.Score;
            this.lastRunLines = this.modern.Lines;
            this.lastRunLevel = this.modern.Level;
        }
        else
        {
            this.lastRunScore = this.classic.Score;
            this.lastRunLines = this.classic.Lines;
            this.lastRunLevel = this.classic.Level;
        }
        this.lastRunWasRecord = this.lastRunScore > BestScore;
        var updated = this.CurrentHighScores
            .Append(this.lastRunScore)
            .OrderByDescending(s => s)
            .Take(ScoreSlots)
            .ToArray();
        if (this.Modern)
        {
            this.modernHighScores = updated;
            this.storage.Set(ModernHighScoresKey, updated);
        }
        else
        {
            this.highScores = updated;
            this.storage.Set(HighScoresKey, updated);
        }
        this.rewards.NoteGameFinished();
        this.scores.SubmitScore(new ArcadeScoreSubmissionDto(
            this.Modern ? ArcadeGame.StackerModern : ArcadeGame.Stacker,
            this.lastRunScore, (int)(this.runSeconds * 1000.0), this.lastRunLines));
    }

    private void DrawSplash(OsAppContext ctx, double now, Vector2 winPos, Vector2 winSize)
    {
        var dl = ImGui.GetWindowDrawList();
        var elapsed = now - this.splashStartedAt;
        var litRows = ctx.ReduceMotion
            ? RetroLcd.GlyphHeight
            : (int)Math.Min(RetroLcd.GlyphHeight, elapsed / 0.09);

        var pixel = MathF.Max(2f, winSize.X * 0.76f / RetroLcd.WordColumns("STACKER"));
        var wordY = winSize.Y * 0.10f;
        RetroLcd.DrawWordCentered(dl, "STACKER", winPos + new Vector2(0f, wordY), winSize.X, pixel,
            ImGui.GetColorU32(RetroLcd.Pixel), litRows);

        var subtitle = ctx.Localize("os.stacker_subtitle");
        var subSize = ImGui.CalcTextSize(subtitle);
        dl.AddText(winPos + new Vector2((winSize.X - subSize.X) * 0.5f,
            wordY + (RetroLcd.GlyphHeight * pixel) + ctx.Px(12f)),
            ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.75f }), subtitle);

        // The mode is a real choice with its own leaderboard, so it gets a picker and an explainer
        // instead of hiding in a settings page.
        var modeY = winPos.Y + (winSize.Y * 0.30f);
        var modeGap = ctx.Px(8f);
        var modeW = ((winSize.X - ctx.Px(24f)) - modeGap) * 0.5f;
        var modeH = ctx.Px(30f);
        if (RetroLcd.Button("##stkModeClassic", ctx.Localize("os.stacker_mode_classic"),
            new Vector2(winPos.X + ctx.Px(12f), modeY), new Vector2(modeW, modeH), ctx.Px(4f),
            filled: !this.Modern))
        {
            SetMode(Mode.Classic);
        }
        if (RetroLcd.Button("##stkModeModern", ctx.Localize("os.stacker_mode_modern"),
            new Vector2(winPos.X + ctx.Px(12f) + modeW + modeGap, modeY), new Vector2(modeW, modeH), ctx.Px(4f),
            filled: this.Modern))
        {
            SetMode(Mode.Modern);
        }
        var hint = ctx.Localize(this.Modern ? "os.stacker_mode_modern_hint" : "os.stacker_mode_classic_hint");
        ImGui.SetCursorScreenPos(new Vector2(winPos.X + ctx.Px(16f), modeY + modeH + ctx.Px(8f)));
        ImGui.PushTextWrapPos(winSize.X - ctx.Px(16f));
        ImGui.TextColored(RetroLcd.Pixel with { W = 0.7f }, hint);
        ImGui.PopTextWrapPos();

        var buttonW = winSize.X * 0.62f;
        var buttonH = ctx.Px(36f);
        var firstY = winSize.Y * 0.48f;
        if (RetroLcd.Button("##stackerPlay", ctx.Localize("os.stacker_play"),
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, firstY), new Vector2(buttonW, buttonH),
            ctx.Px(4f), filled: true))
        {
            StartRun();
        }
        if (RetroLcd.Button("##stackerScores", ctx.Localize("os.stacker_high_scores"),
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, firstY + buttonH + ctx.Px(8f)),
            new Vector2(buttonW, buttonH), ctx.Px(4f), filled: false))
        {
            this.view = View.Scores;
        }
        if (RetroLcd.Button("##stackerControls", ctx.Localize("os.stacker_controls"),
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, firstY + ((buttonH + ctx.Px(8f)) * 2f)),
            new Vector2(buttonW, buttonH), ctx.Px(4f), filled: false))
        {
            this.rebindingAction = -1;
            this.view = View.Controls;
        }
        if (RetroLcd.Button("##stackerBoard", ctx.Localize("os.arcade_leaderboard"),
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, firstY + ((buttonH + ctx.Px(8f)) * 3f)),
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
            var bestY = this.Modern ? winSize.Y - ctx.Px(64f) : winSize.Y - ctx.Px(30f);
            dl.AddText(winPos + new Vector2((winSize.X - bestSize.X) * 0.5f, bestY),
                ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.7f }), best);
        }

        if (this.Modern)
        {
        }
    }

    private void SetMode(Mode next)
    {
        if (this.mode == next)
        {
            return;
        }
        this.mode = next;
        this.storage.Set(ModeKey, next == Mode.Modern ? "modern" : "classic");
    }


    // ---- Classic mode: preserved unchanged from before the Modern mode existed. ----

    private void DrawPlayingClassic(OsAppContext ctx, double delta, Vector2 winPos, Vector2 winSize)
    {
        if (ShouldAutoPause())
        {
            this.paused = true;
        }

        if (!this.paused)
        {
            ReadKeyboardClassic(delta);
            this.runSeconds += delta;
            this.classic.Tick(delta);
            if (this.classic.Dead)
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

        DrawHudClassic(ctx, winPos, winSize, hudH, cell);
        DrawWellClassic(boardTL, boardW, boardH, cell);
        if (this.paused)
        {
            DrawPausedOverlay(ctx, boardTL, boardW, boardH);
        }
        else
        {
            DrawTouchPadClassic(delta, winPos, winSize, padH);
        }
    }

    private void DrawHudClassic(OsAppContext ctx, Vector2 winPos, Vector2 winSize, float hudH, float cell)
    {
        var dl = ImGui.GetWindowDrawList();
        var padX = ctx.Px(12f);
        var line = ImGui.GetTextLineHeight();
        dl.AddText(winPos + new Vector2(padX, ctx.Px(6f)), ImGui.GetColorU32(RetroLcd.Pixel),
            string.Format(ctx.Localize("os.stacker_score"), this.classic.Score));
        dl.AddText(winPos + new Vector2(padX, ctx.Px(6f) + line), ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.75f }),
            string.Format(ctx.Localize("os.stacker_level_lines"), this.classic.Level, this.classic.Lines));

        if (!this.paused && RetroLcd.PauseKey(winPos, winSize, padX, hudH))
        {
            this.paused = true;
        }

        // The next piece sits in the top right, drawn small in its own 4x4 box.
        var preview = MathF.Max(3f, cell * 0.5f);
        var previewOrigin = winPos + new Vector2(
            winSize.X - padX - RetroLcd.PauseKeyWidth(hudH) - ctx.Px(8f) - (preview * 4f), ctx.Px(8f));
        foreach (var (x, y) in StackerGame.Cells(this.classic.NextKind, 0, 0, 0))
        {
            RetroLcd.Cell(dl, previewOrigin, x, y, preview, ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.85f }));
        }
    }

    private void DrawWellClassic(Vector2 boardTL, float boardW, float boardH, float cell)
    {
        var dl = ImGui.GetWindowDrawList();
        dl.AddRect(boardTL - new Vector2(2f, 2f), boardTL + new Vector2(boardW + 2f, boardH + 2f),
            ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.55f }), 0f, ImDrawFlags.None, 2f);
        RetroLcd.DotGrid(dl, boardTL, StackerGame.Columns, StackerGame.Rows, cell);

        for (var x = 0; x < StackerGame.Columns; x++)
        {
            for (var y = 0; y < StackerGame.Rows; y++)
            {
                if (this.classic.Filled(x, y))
                {
                    RetroLcd.Cell(dl, boardTL, x, y, cell, ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.88f }));
                }
            }
        }

        // The landing ghost is an outline so it never reads as a settled block.
        foreach (var (x, y) in this.classic.GhostCells())
        {
            if (y < 0)
            {
                continue;
            }
            var tl = boardTL + new Vector2(x * cell, y * cell);
            dl.AddRect(tl + new Vector2(2f, 2f), tl + new Vector2(cell - 2f, cell - 2f),
                ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.35f }), 0f, ImDrawFlags.None, 1.5f);
        }

        foreach (var (x, y) in this.classic.CurrentCells())
        {
            if (y >= 0)
            {
                RetroLcd.Cell(dl, boardTL, x, y, cell, ImGui.GetColorU32(RetroLcd.Pixel));
            }
        }
    }

    /// <summary>Arrows or WASD, with a repeat timer so holding left or right walks the piece across.</summary>
    private void ReadKeyboardClassic(double delta)
    {
        if (this.keys.WasPressed(AppKey.Up) || this.keys.WasPressed(AppKey.W))
        {
            this.classic.Rotate();
        }
        if (this.keys.WasPressed(AppKey.Space))
        {
            this.classic.HardDrop();
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
            this.classic.MoveLeft();
        }
        else if (right)
        {
            this.classic.MoveRight();
        }
        if (down)
        {
            this.classic.SoftDrop();
        }
    }

    private void DrawTouchPadClassic(double delta, Vector2 winPos, Vector2 winSize, float padH)
    {
        var key = MathF.Min(padH * 0.42f, winSize.X * 0.16f);
        var rowY = winPos.Y + winSize.Y - (padH * 0.62f);
        var gap = key * 0.16f;
        var totalW = (key * 5f) + (gap * 4f);
        var x = winPos.X + ((winSize.X - totalW) * 0.5f);

        if (RetroLcd.KeyLabel("##stkLeft", "A", new Vector2(x, rowY), key))
        {
            this.classic.MoveLeft();
        }
        x += key + gap;
        if (RetroLcd.KeyLabel("##stkRotate", "W", new Vector2(x, rowY), key))
        {
            this.classic.Rotate();
        }
        x += key + gap;
        if (RetroLcd.KeyLabel("##stkRight", "D", new Vector2(x, rowY), key))
        {
            this.classic.MoveRight();
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
                this.classic.SoftDrop();
            }
        }
        else
        {
            this.touchDropAccumulator = 0;
        }
        x += key + gap;
        if (RetroLcd.Key("##stkDrop", FontAwesomeIcon.AngleDoubleDown, new Vector2(x, rowY), key))
        {
            this.classic.HardDrop();
        }
    }

    // ---- Modern mode ----

    private void DrawPlayingModern(OsAppContext ctx, double delta, Vector2 winPos, Vector2 winSize)
    {
        if (ShouldAutoPause())
        {
            this.paused = true;
        }

        if (!this.paused)
        {
            ReadKeyboardModern(delta);
            this.runSeconds += delta;
            this.modern.Tick(delta);
            if (this.modern.Dead)
            {
                this.view = View.GameOver;
                FinishRun();
            }
        }

        var padX = ctx.Px(4f);
        var padH = winSize.Y * 0.16f;
        var line = ImGui.GetTextLineHeight();
        var headerH = (line * 2f) + ctx.Px(10f);
        var textTop = winPos.Y + ((headerH - (line * 2f) - ctx.Px(2f)) * 0.5f);
        var previewCell = ctx.Px(11f);
        var pieceCell = ctx.Px(22f);
        var boxPad = ctx.Px(4f);
        var boxLabelH = line + ctx.Px(4f);
        var boxH = boxLabelH + (previewCell * 4f) + (boxPad * 2f);
        var boxesTop = winPos.Y + headerH;
        var feedbackH = line + ctx.Px(4f);
        var feedbackTop = boxesTop + boxH + ctx.Px(4f);
        var boardTop = feedbackTop + feedbackH + ctx.Px(6f);

        var boardMaxW = winSize.X - (padX * 2f);
        var boardMaxH = (winPos.Y + winSize.Y) - boardTop - padH - ctx.Px(4f);
        var cell = MathF.Max(2f, MathF.Floor(MathF.Min(boardMaxW / StackerModernGame.Columns, boardMaxH / StackerModernGame.Rows)));
        var boardW = cell * StackerModernGame.Columns;
        var boardH = cell * StackerModernGame.Rows;
        var boardTL = new Vector2(winPos.X + ((winSize.X - boardW) * 0.5f), boardTop);

        DrawAppBackground(winPos, winSize, cell);
        UpdateScoreFeedback(ctx);
        UpdateLineClearFlash(delta);
        UpdateHardDropFlash(delta);
        DrawHudModern(ctx, winPos, winSize, padX, textTop, line, headerH);
        DrawPreviewBoxes(ctx, winPos, boxesTop, padX, boardMaxW, boxH, boxPad, boxLabelH, pieceCell);
        DrawScoreFeedback(winPos, winSize, feedbackTop, feedbackH);
        DrawWellModern(boardTL, boardW, boardH, cell);
        if (this.paused)
        {
            DrawPausedOverlay(ctx, boardTL, boardW, boardH);
        }
        else
        {
            // Left-click on the well rotates the piece: mouse-first play without reaching for the pad.
            // Hand hit-tested, never a widget: the keyboard capture holds ImGui's active id all run.
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left)
                && ImGui.IsMouseHoveringRect(boardTL, boardTL + new Vector2(boardW, boardH)))
            {
                this.modern.Rotate();
            }
            DrawTouchPadModern(delta, winPos, winSize, padH);
        }
    }

    /// <summary>The Modern on-screen pad: the phone is mouse-first, so every action a key can do has a
    /// clickable key too, including the two rotations and Hold.</summary>
    private void DrawTouchPadModern(double delta, Vector2 winPos, Vector2 winSize, float padH)
    {
        var key = MathF.Min(padH * 0.62f, winSize.X * 0.115f);
        var rowY = winPos.Y + winSize.Y - (padH * 0.5f) - (key * 0.5f);
        var gap = key * 0.14f;
        var totalW = (key * 7f) + (gap * 6f);
        var x = winPos.X + ((winSize.X - totalW) * 0.5f);
        var ink = this.PauseKeyInk;
        var paper = this.PauseKeyPaper;

        if (RetroLcd.Key("##stkmLeft", FontAwesomeIcon.CaretLeft, new Vector2(x, rowY), key, ink, paper))
        {
            this.modern.MoveLeft();
        }
        x += key + gap;
        if (RetroLcd.Key("##stkmRotL", FontAwesomeIcon.UndoAlt, new Vector2(x, rowY), key, ink, paper))
        {
            this.modern.RotateLeft();
        }
        x += key + gap;
        if (RetroLcd.Key("##stkmRotR", FontAwesomeIcon.RedoAlt, new Vector2(x, rowY), key, ink, paper))
        {
            this.modern.Rotate();
        }
        x += key + gap;
        if (RetroLcd.Key("##stkmRight", FontAwesomeIcon.CaretRight, new Vector2(x, rowY), key, ink, paper))
        {
            this.modern.MoveRight();
        }
        x += key + gap;
        if (RetroLcd.KeyLabel("##stkmHold", KeyName(Binding(ControlAction.Hold)), new Vector2(x, rowY), key))
        {
            this.modern.Hold();
        }
        x += key + gap;
        if (RetroLcd.KeyLabelHeld("##stkmDown", "S", new Vector2(x, rowY), key))
        {
            this.touchDropAccumulator += delta;
            while (this.touchDropAccumulator >= 0.06)
            {
                this.touchDropAccumulator -= 0.06;
                this.modern.SoftDrop();
            }
        }
        else
        {
            this.touchDropAccumulator = 0;
        }
        x += key + gap;
        if (RetroLcd.Key("##stkmDrop", FontAwesomeIcon.AngleDoubleDown, new Vector2(x, rowY), key, ink, paper))
        {
            this.modern.HardDrop();
        }
    }

    /// <summary>Notices a fresh lock (even a scoreless one) and either posts a new feedback message or
    /// lets the current one age a step towards fully faded.</summary>
    private void UpdateScoreFeedback(OsAppContext ctx)
    {
        if (this.modern.LockCount == this.lastSeenLockCount)
        {
            return;
        }
        this.lastSeenLockCount = this.modern.LockCount;
        var message = BuildFeedbackMessage(ctx);
        if (message.Length > 0)
        {
            this.scoreFeedback = message;
            this.scoreFeedbackAge = 0;
        }
        else
        {
            this.scoreFeedbackAge++;
        }
    }

    /// <summary>Every phrase is a whole localized string; back-to-back wraps via a format key so the word
    /// order survives translation.</summary>
    private string BuildFeedbackMessage(OsAppContext ctx)
    {
        var cleared = this.modern.LastCleared;
        if (this.modern.LastClearIsTSpin)
        {
            var label = cleared switch
            {
                2 => ctx.Localize("os.stacker_fx_tspin_double"),
                3 => ctx.Localize("os.stacker_fx_tspin_triple"),
                _ => ctx.Localize(this.modern.LastClearIsMini ? "os.stacker_fx_tspin_mini" : "os.stacker_fx_tspin"),
            };
            return this.modern.LastClearIsBackToBack
                ? string.Format(ctx.Localize("os.stacker_fx_b2b"), label)
                : label;
        }
        if (cleared == 4)
        {
            var tetris = ctx.Localize("os.stacker_fx_tetris");
            return this.modern.LastClearIsBackToBack
                ? string.Format(ctx.Localize("os.stacker_fx_b2b"), tetris)
                : tetris;
        }
        if (cleared > 0 && this.modern.LastClearCombo >= 1)
        {
            return string.Format(ctx.Localize("os.stacker_fx_combo"), this.modern.LastClearCombo + 1);
        }
        return string.Empty;
    }

    /// <summary>Notices a fresh lock and, if it cleared lines, restarts the clear-row flash fade-out.</summary>
    private void UpdateLineClearFlash(double delta)
    {
        if (this.modern.LockCount != this.lastSeenLineClearLockCount)
        {
            this.lastSeenLineClearLockCount = this.modern.LockCount;
            this.lineClearFlashElapsed = this.modern.LastCleared > 0 ? 0 : LineClearFlashSeconds;
        }
        else
        {
            this.lineClearFlashElapsed += delta;
        }
    }

    /// <summary>Notices a fresh hard drop and restarts its drop-trail flash fade-out.</summary>
    private void UpdateHardDropFlash(double delta)
    {
        if (this.modern.HardDropCount != this.lastSeenHardDropCount)
        {
            this.lastSeenHardDropCount = this.modern.HardDropCount;
            this.hardDropFlashElapsed = this.modern.HardDropCount > 0 ? 0 : StackerModernGame.HardDropLockoutSeconds;
        }
        else
        {
            this.hardDropFlashElapsed += delta;
        }
    }

    /// <summary>The dimmed app background outside the board/boxes: the skin's dark tile at 3x the playfield's
    /// own cell size, washed with 25% black so the HUD reads clearly on top of it.</summary>
    private void DrawAppBackground(Vector2 winPos, Vector2 winSize, float cell)
    {
        var dl = ImGui.GetWindowDrawList();
        if (this.art.Get("gbg", skin) is { } bg)
        {
            RetroLcd.TiledImage(dl, winPos, winSize, cell * AppBackgroundTileScale, bg.Handle,
                Vector2.Zero, Vector2.One, new Vector4(0.9f, 0.9f, 0.9f, 1f));
        }
        else
        {
            dl.AddRectFilled(winPos, winPos + winSize, ImGui.GetColorU32(RetroLcd.PanelDim));
        }
        dl.AddRectFilled(winPos, winPos + winSize, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.25f)));
    }

    /// <summary>A HUD text field in the retro-green skin's own lit-panel colour. The readable black box the
    /// other skins would want is not here because <c>skin</c> is a const: there is one skin.</summary>
    private static void DrawReadableText(ImDrawListPtr dl, Vector2 pos, string text, float alpha = 1f) =>
        dl.AddText(pos, ImGui.GetColorU32(RetroLcd.Panel with { W = alpha }), text);

    private void DrawScoreFeedback(Vector2 winPos, Vector2 winSize, float feedbackTop, float feedbackH)
    {
        if (this.scoreFeedback.Length == 0 || this.scoreFeedbackAge >= ScoreFeedbackFadePieces)
        {
            return;
        }
        var alpha = 1f - (this.scoreFeedbackAge / (float)ScoreFeedbackFadePieces);
        var dl = ImGui.GetWindowDrawList();
        var size = ImGui.CalcTextSize(this.scoreFeedback);
        DrawReadableText(dl, new Vector2(winPos.X + ((winSize.X - size.X) * 0.5f),
            feedbackTop + ((feedbackH - ImGui.GetTextLineHeight()) * 0.5f)), this.scoreFeedback, alpha);
    }

    private void DrawHudModern(OsAppContext ctx, Vector2 winPos, Vector2 winSize, float padX, float textTop, float line, float headerH)
    {
        var dl = ImGui.GetWindowDrawList();
        var levelText = string.Format(ctx.Localize("os.stacker_level_lines"), this.modern.Level, this.modern.Lines);
        var scoreText = string.Format(ctx.Localize("os.stacker_score"), this.modern.Score);
        DrawReadableText(dl, new Vector2(winPos.X + ((winSize.X - ImGui.CalcTextSize(levelText).X) * 0.5f), textTop), levelText);
        DrawReadableText(dl, new Vector2(winPos.X + ((winSize.X - ImGui.CalcTextSize(scoreText).X) * 0.5f), textTop + line + ctx.Px(2f)), scoreText, 0.85f);

        if (!this.paused && RetroLcd.PauseKey(winPos, winSize, padX, headerH, this.PauseKeyInk, this.PauseKeyPaper))
        {
            this.paused = true;
        }
    }

    /// <summary>The Hold and Next boxes side by side above the board, each labelled and holding one piece.</summary>
    private void DrawPreviewBoxes(OsAppContext ctx, Vector2 winPos, float boxesTop, float padX, float boardMaxW,
        float boxH, float boxPad, float boxLabelH, float pieceCell)
    {
        var dl = ImGui.GetWindowDrawList();
        var gap = ctx.Px(8f);
        var boxW = (boardMaxW - gap) / 2f;
        var holdTL = new Vector2(winPos.X + padX, boxesTop);
        var nextTL = new Vector2(holdTL.X + boxW + gap, boxesTop);

        DrawPreviewBox(dl, ctx, holdTL, boxW, boxH, boxPad, boxLabelH, pieceCell,
            "hold", ctx.Localize("os.stacker_hold_label"), this.modern.HeldKind);
        DrawPreviewBox(dl, ctx, nextTL, boxW, boxH, boxPad, boxLabelH, pieceCell,
            "next", ctx.Localize("os.stacker_next_label"), this.modern.NextKind);
    }

    private void DrawPreviewBox(ImDrawListPtr dl, OsAppContext ctx, Vector2 tl, float boxW, float boxH,
        float boxPad, float boxLabelH, float pieceCell, string role, string label, int kind)
    {
        // The role art is the whole box (background, border and "Hold"/"Next" label baked in); only the
        // piece on top is drawn separately.
        if (this.art.Get(role, skin) is { } box)
        {
            dl.AddImage(box.Handle, tl, tl + new Vector2(boxW, boxH));
        }
        else
        {
            dl.AddRectFilled(tl, tl + new Vector2(boxW, boxH), ImGui.GetColorU32(this.BoardBackground));
            dl.AddRect(tl, tl + new Vector2(boxW, boxH), ImGui.GetColorU32(RetroLcd.PanelEdge), ctx.Px(3f));
            var labelSize = ImGui.CalcTextSize(label);
            dl.AddText(tl + new Vector2((boxW - labelSize.X) * 0.5f, boxPad * 0.4f),
                ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.8f }), label);
        }

        if (kind < 0)
        {
            return;
        }
        // The piece art is drawn larger than the box was sized for, centred on the same point, so it can
        // overflow the border a little rather than shrinking the box or its label to fit.
        var pieceW = pieceCell * 4f;
        var cells = StackerModernGame.Cells(kind, 0, 0, 0).ToList();
        // The piece's own horizontal centre of mass (not the 4x4 grid's midpoint) lands at 25% of the
        // box's width for Next and 75% for Hold (mirrored, so the pair reads as facing each other), so
        // differently-shaped pieces all read as sitting at the same visual spot.
        var cellCenterX = cells.Average(c => c.X + 0.5f) * pieceCell;
        var targetCenterX = tl.X + (boxW * (role == "hold" ? 0.75f : 0.25f));
        var centerY = tl.Y + boxLabelH + ((boxH - boxLabelH) * 0.5f) - (pieceCell * 0.2f);
        // Non-I pieces sit inside a shorter visual box within their 4x4 grid, so nudge them down to match.
        var verticalOffset = pieceCell * (kind == 0 ? 0.5f : 1.0f);
        var origin = new Vector2(targetCenterX - cellCenterX, centerY - (pieceW * 0.5f) + verticalOffset);
        foreach (var (x, y) in cells)
        {
            DrawMino(dl, origin, x, y, pieceCell, kind);
        }
    }

    /// <summary>One board cell drawn from the active skin's texture atlas; skipped for the frame or two
    /// before the texture has finished loading.</summary>
    private void DrawMino(ImDrawListPtr dl, Vector2 origin, int x, int y, float cell, int kind, float alpha = 1f)
    {
        if (this.art.Get("skin", skin, fallbackSkin: null) is not { } tex)
        {
            return;
        }
        var (uv0, uv1) = StackerArt.MinoUv(kind, tex.Size);
        RetroLcd.TexturedCell(dl, origin, x, y, cell, tex.Handle, uv0, uv1, alpha);
    }

    private void DrawWellModern(Vector2 boardTL, float boardW, float boardH, float cell)
    {
        var dl = ImGui.GetWindowDrawList();
        if (this.art.Get("bg", skin, fallbackSkin: "retro") is { } bg)
        {
            var (lightUv0, lightUv1) = StackerArt.BackgroundUv(bg.Size, light: true);
            var (darkUv0, darkUv1) = StackerArt.BackgroundUv(bg.Size, light: false);
            for (var x = 0; x < StackerModernGame.Columns; x++)
            {
                for (var y = 0; y < StackerModernGame.Rows; y++)
                {
                    var (uv0, uv1) = (x + y) % 2 == 0 ? (lightUv0, lightUv1) : (darkUv0, darkUv1);
                    RetroLcd.TexturedCell(dl, boardTL, x, y, cell, bg.Handle, uv0, uv1);
                }
            }
        }
        else
        {
            var fallback = this.BoardBackground;
            var darkFallback = ImGui.GetColorU32(new Vector4(fallback.X * 0.85f, fallback.Y * 0.85f, fallback.Z * 0.85f, 1f));
            var lightFallback = ImGui.GetColorU32(fallback);
            for (var x = 0; x < StackerModernGame.Columns; x++)
            {
                for (var y = 0; y < StackerModernGame.Rows; y++)
                {
                    var checkerTl = boardTL + new Vector2(x * cell, y * cell);
                    dl.AddRectFilled(checkerTl, checkerTl + new Vector2(cell + 0.5f, cell + 0.5f),
                        (x + y) % 2 == 0 ? lightFallback : darkFallback);
                }
            }
        }
        dl.AddRect(boardTL - new Vector2(2f, 2f), boardTL + new Vector2(boardW + 2f, boardH + 2f),
            ImGui.GetColorU32(skin == "retro_green" ? RetroLcd.Panel : new Vector4(1f, 1f, 1f, 1f)), 0f, ImDrawFlags.None, 2f);

        DrawHardDropFlash(dl, boardTL, cell);

        for (var x = 0; x < StackerModernGame.Columns; x++)
        {
            for (var y = 0; y < StackerModernGame.Rows; y++)
            {
                var kind = this.modern.KindAt(x, y);
                if (kind >= 0)
                {
                    DrawMino(dl, boardTL, x, y, cell, kind);
                }
            }
        }

        DrawLineClearFlash(dl, boardTL, boardW, cell);

        // The landing ghost previews where the piece will land, using a dedicated faded/outlined skin.
        var ghost = this.art.Get("skinghost", skin, fallbackSkin: null);
        foreach (var (x, y) in this.modern.GhostCells())
        {
            if (y < 0)
            {
                continue;
            }
            if (ghost is { } ghostTex)
            {
                var (uv0, uv1) = StackerArt.MinoUv(this.modern.PieceKind, ghostTex.Size);
                RetroLcd.TexturedCell(dl, boardTL, x, y, cell, ghostTex.Handle, uv0, uv1);
            }
            // The sheet's ghost tile is barely a shade off the checker, so a firm ink outline rides on
            // top either way; without it the landing preview disappears at a glance.
            var ghostTl = boardTL + new Vector2(x * cell, y * cell);
            dl.AddRect(ghostTl + new Vector2(2f, 2f), ghostTl + new Vector2(cell - 2f, cell - 2f),
                ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.6f }), 0f, ImDrawFlags.None, 2f);
        }

        foreach (var (x, y) in this.modern.CurrentCells())
        {
            if (y >= 0)
            {
                DrawMino(dl, boardTL, x, y, cell, this.modern.PieceKind);
            }
        }
    }

    /// <summary>White trail from where the piece was to where it fell on its last hard drop, drawn under
    /// the settled minos so it reads as a quick flash beneath the piece rather than over it.</summary>
    private void DrawHardDropFlash(ImDrawListPtr dl, Vector2 boardTL, float cell)
    {
        if (this.hardDropFlashElapsed >= StackerModernGame.HardDropLockoutSeconds)
        {
            return;
        }
        var alpha = HardDropFlashStartAlpha * (1f - (float)(this.hardDropFlashElapsed / StackerModernGame.HardDropLockoutSeconds));
        var startCells = StackerModernGame.Cells(this.modern.LastHardDropKind, this.modern.LastHardDropRotation,
            this.modern.LastHardDropX, this.modern.LastHardDropStartY).ToList();
        var endCells = StackerModernGame.Cells(this.modern.LastHardDropKind, this.modern.LastHardDropRotation,
            this.modern.LastHardDropX, this.modern.LastHardDropEndY);
        var minX = startCells.Min(c => c.X);
        var maxX = startCells.Max(c => c.X);
        var minY = Math.Max(0, startCells.Min(c => c.Y));
        var maxY = Math.Min(StackerModernGame.Rows - 1, endCells.Max(c => c.Y));
        var tl = boardTL + new Vector2(minX * cell, minY * cell);
        var br = boardTL + new Vector2((maxX + 1) * cell, (maxY + 1) * cell);
        dl.AddRectFilled(tl, br, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, alpha)));
    }

    /// <summary>White flash over the rows cleared by the last lock, fading out over <see cref="LineClearFlashSeconds"/>.</summary>
    private void DrawLineClearFlash(ImDrawListPtr dl, Vector2 boardTL, float boardW, float cell)
    {
        if (this.lineClearFlashElapsed >= LineClearFlashSeconds || this.modern.LastClearedRows.Count == 0)
        {
            return;
        }
        var alpha = LineClearFlashStartAlpha * (1f - (float)(this.lineClearFlashElapsed / LineClearFlashSeconds));
        var color = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, alpha));
        foreach (var y in this.modern.LastClearedRows)
        {
            var tl = boardTL + new Vector2(0f, y * cell);
            dl.AddRectFilled(tl, tl + new Vector2(boardW, cell), color);
        }
    }

    /// <summary>The rebindable Modern controls, with a repeat timer so holding a move key walks the piece.</summary>
    private void ReadKeyboardModern(double delta)
    {
        if (Pressed(ControlAction.RotateRight))
        {
            this.modern.Rotate();
        }
        if (Pressed(ControlAction.RotateLeft))
        {
            this.modern.RotateLeft();
        }
        if (Pressed(ControlAction.Hold))
        {
            this.modern.Hold();
        }
        if (Pressed(ControlAction.HardDrop))
        {
            this.modern.HardDrop();
        }

        var left = this.keys.IsDown(Binding(ControlAction.MoveLeft));
        var right = this.keys.IsDown(Binding(ControlAction.MoveRight));
        var down = this.keys.IsDown(Binding(ControlAction.MoveDown));
        if (!left && !right && !down)
        {
            this.repeatAccumulator = 0;
            return;
        }
        this.repeatAccumulator += delta;
        var firstPress = Pressed(ControlAction.MoveLeft) || Pressed(ControlAction.MoveRight)
            || Pressed(ControlAction.MoveDown);
        if (!firstPress && this.repeatAccumulator < 0.09)
        {
            return;
        }
        this.repeatAccumulator = 0;
        if (left)
        {
            this.modern.MoveLeft();
        }
        else if (right)
        {
            this.modern.MoveRight();
        }
        if (down)
        {
            this.modern.SoftDrop();
        }
    }

    private AppKey Binding(ControlAction action) => this.bindings[(int)action];

    private bool Pressed(ControlAction action) => this.keys.WasPressed(Binding(action));

    /// <summary>The Modern key-rebind menu. The keyboard is captured ONLY while a row is actively waiting
    /// for its key: capturing on an idle menu would hold the chat box and game hotkeys hostage, and
    /// re-taking focus every frame is exactly what stops ordinary buttons firing.</summary>
    private void DrawControlsMenu(OsAppContext ctx, Vector2 winPos, Vector2 winSize)
    {
        var dl = ImGui.GetWindowDrawList();
        var title = ctx.Localize("os.stacker_controls");
        var titleSize = ImGui.CalcTextSize(title);
        dl.AddText(winPos + new Vector2((winSize.X - titleSize.X) * 0.5f, ctx.Px(18f)),
            ImGui.GetColorU32(RetroLcd.Pixel), title);

        if (this.rebindingAction >= 0)
        {
            this.keys.RequestExclusive();
            if (this.keys.TryGetPressedKey(out var pressed))
            {
                this.bindings[this.rebindingAction] = pressed;
                this.storage.Set(InputBindingsKey, this.bindings);
                this.rebindingAction = -1;
            }
        }

        var y = ctx.Px(62f);
        for (var i = 0; i < this.bindings.Length; i++)
        {
            var action = (ControlAction)i;
            var label = ctx.Localize($"os.stacker_control_{action.ToString().ToLowerInvariant()}");
            dl.AddText(winPos + new Vector2(ctx.Px(18f), y + ctx.Px(8f)),
                ImGui.GetColorU32(RetroLcd.Pixel), label);
            var button = new Vector2(winSize.X - ctx.Px(36f), ctx.Px(30f));
            var buttonLabel = this.rebindingAction == i ? ctx.Localize("os.stacker_press_key") : KeyName(Binding(action));
            if (RetroLcd.Button($"##stkBind{i}", buttonLabel,
                winPos + new Vector2(ctx.Px(18f), y), button, ctx.Px(3f), filled: this.rebindingAction == i))
            {
                this.rebindingAction = this.rebindingAction == i ? -1 : i;
            }
            y += ctx.Px(38f);
        }

        if (RetroLcd.Button("##stkControlsBack", ctx.Localize("os.stacker_menu"),
            winPos + new Vector2((winSize.X - (winSize.X * 0.62f)) * 0.5f, winSize.Y - ctx.Px(54f)),
            new Vector2(winSize.X * 0.62f, ctx.Px(34f)), ctx.Px(4f), filled: false))
        {
            this.rebindingAction = -1;
            this.view = View.Splash;
        }
    }

    private static string KeyName(AppKey key) => key switch
    {
        AppKey.Left => "Left",
        AppKey.Right => "Right",
        AppKey.Up => "Up",
        AppKey.Down => "Down",
        AppKey.Space => "Space",
        _ => key.ToString(),
    };

    private void DrawPausedOverlay(OsAppContext ctx, Vector2 boardTL, float boardW, float boardH)
    {
        var ink = this.Modern ? this.ThemeInk : RetroLcd.Pixel;
        var paper = this.Modern ? this.ThemePaper : RetroLcd.Panel;
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(boardTL, boardTL + new Vector2(boardW, boardH),
            ImGui.GetColorU32(paper with { W = 0.82f }));
        var label = ctx.Localize("os.stacker_paused");
        var labelSize = ImGui.CalcTextSize(label);
        dl.AddText(boardTL + new Vector2((boardW - labelSize.X) * 0.5f, (boardH * 0.5f) - ctx.Px(30f)),
            ImGui.GetColorU32(ink), label);

        var buttonW = boardW * 0.7f;
        var buttonH = ctx.Px(36f);
        if (RetroLcd.Button("##stkResume", ctx.Localize("os.stacker_resume"),
            boardTL + new Vector2((boardW - buttonW) * 0.5f, boardH * 0.5f), new Vector2(buttonW, buttonH),
            ctx.Px(4f), filled: true, ink: ink, paper: paper))
        {
            this.lastFrameTime = ImGui.GetTime();
            this.paused = false;
            this.autoPauseGraceUntil = ImGui.GetTime() + AutoPauseGraceSeconds;
        }
        if (RetroLcd.Button("##stkRestart", ctx.Localize("os.stacker_restart"),
            boardTL + new Vector2((boardW - buttonW) * 0.5f, (boardH * 0.5f) + buttonH + ctx.Px(10f)),
            new Vector2(buttonW, buttonH), ctx.Px(4f), filled: false, ink: ink, paper: paper))
        {
            StartRun();
        }
        if (RetroLcd.Button("##stkMainMenu", ctx.Localize("os.stacker_menu"),
            boardTL + new Vector2((boardW - buttonW) * 0.5f, (boardH * 0.5f) + ((buttonH + ctx.Px(10f)) * 2f)),
            new Vector2(buttonW, buttonH), ctx.Px(4f), filled: false, ink: ink, paper: paper))
        {
            this.splashStartedAt = ImGui.GetTime();
            this.view = View.Splash;
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
            ctx.Localize(this.Modern ? "os.stacker_mode_modern" : "os.stacker_mode_classic"),
            string.Format(ctx.Localize("os.stacker_score"), this.lastRunScore),
            string.Format(ctx.Localize("os.stacker_lines"), this.lastRunLines),
            string.Format(ctx.Localize("os.stacker_level"), this.lastRunLevel),
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

        var modeLabel = ctx.Localize(this.Modern ? "os.stacker_mode_modern" : "os.stacker_mode_classic");
        var modeSize = ImGui.CalcTextSize(modeLabel);
        dl.AddText(winPos + new Vector2((winSize.X - modeSize.X) * 0.5f, winSize.Y * 0.20f),
            ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.7f }), modeLabel);

        var list = this.CurrentHighScores;
        var padX = ctx.Px(28f);
        var y = winSize.Y * 0.28f;
        if (list.Length == 0)
        {
            var empty = ctx.Localize("os.stacker_no_scores");
            var size = ImGui.CalcTextSize(empty);
            dl.AddText(winPos + new Vector2((winSize.X - size.X) * 0.5f, y),
                ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.7f }), empty);
        }
        for (var i = 0; i < list.Length; i++)
        {
            var value = list[i].ToString();
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
