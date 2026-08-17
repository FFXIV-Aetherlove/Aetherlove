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
    private enum View { Splash, Playing, GameOver, Scores, Leaderboard, Controls }

    private enum ControlAction { MoveLeft, MoveRight, RotateLeft, RotateRight, Hold, MoveDown, HardDrop }

    private static readonly Vector4 TileTopColor = new(0.42f, 0.68f, 0.78f, 1f);
    private static readonly Vector4 TileBottomColor = new(0.13f, 0.26f, 0.33f, 1f);

    private const string HighScoresKey = "high_scores";
    private const string InputBindingsKey = "input_bindings";
    private const string SkinKey = "skin";
    private const int ScoreSlots = 5;
    private const int ScoreFeedbackFadePieces = 8;
    /// <summary>How much bigger than the gameplay cell the dimmed background's tiling is drawn.</summary>
    private const float AppBackgroundTileScale = 3f;
    private const float LineClearFlashSeconds = 0.4f;
    private const float LineClearFlashStartAlpha = 0.8f;
    private const float HardDropFlashStartAlpha = 0.6f;

    /// <summary>Skins are named after their sprite sheet's <c>Media/stacker/skin_&lt;name&gt;.png</c> file.</summary>
    private static readonly string[] SkinNames = ["retro", "retro_green", "classic", "arcade"];

    /// <summary>The one skin that keeps the handheld's green LCD look everywhere else; every other skin
    /// gets a neutral grey board/box background so its own art reads correctly.</summary>
    private static readonly Vector4 NeutralBoardBackground = new(168f / 255f, 168f / 255f, 168f / 255f, 1f);

    private static readonly AppKey[] DefaultBindings =
    [ AppKey.A, AppKey.D, AppKey.Up, AppKey.W, AppKey.E, AppKey.S, AppKey.Space ];

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
    private int[] highScores = [];
    private bool scoresLoaded;
    private int lastRunScore;
    private bool lastRunWasRecord;
    private double runSeconds;
    private AppKey[] bindings = (AppKey[])DefaultBindings.Clone();
    private int rebindingAction = -1;
    private int lastSeenLockCount = -1;
    private int lastSeenLineClearLockCount = -1;
    private double lineClearFlashElapsed = LineClearFlashSeconds;
    private int lastSeenHardDropCount = -1;
    private double hardDropFlashElapsed = StackerGame.HardDropLockoutSeconds;
    private string scoreFeedback = string.Empty;
    private int scoreFeedbackAge = ScoreFeedbackFadePieces;
    private string skin = "retro_green";

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
        if (this.view != View.Playing)
        {
            dl.AddRectFilled(winPos, winPos + winSize, ImGui.GetColorU32(RetroLcd.Panel));
        }

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
        var savedSkin = this.storage.Get<string>(SkinKey);
        if (savedSkin != null && Array.IndexOf(SkinNames, savedSkin) >= 0)
        {
            this.skin = savedSkin;
        }
        var savedBindings = this.storage.Get<AppKey[]>(InputBindingsKey);
        if (savedBindings is { Length: 7 } && savedBindings.All(key => Enum.IsDefined(key)))
        {
            this.bindings = savedBindings;
        }
    }

    private int BestScore => this.highScores.Length > 0 ? this.highScores[0] : 0;

    /// <summary>The playfield/preview-box fill: the retro-green skin keeps the handheld's own tint,
    /// every other skin gets a neutral grey so its own art shows true colour.</summary>
    private Vector4 BoardBackground => this.skin == "retro_green" ? RetroLcd.Panel : NeutralBoardBackground;

    /// <summary>Chrome (text/icons) colour: the retro-green skin keeps the handheld's green-on-green look,
    /// every other skin goes plain black-and-white instead.</summary>
    private Vector4 ThemeInk => this.skin == "retro_green" ? RetroLcd.Pixel : new Vector4(1f, 1f, 1f, 1f);

    /// <summary>The fill colour behind solid/filled chrome, paired with <see cref="ThemeInk"/>.</summary>
    private Vector4 ThemePaper => this.skin == "retro_green" ? RetroLcd.Panel : new Vector4(0f, 0f, 0f, 1f);

    /// <summary>The pause key's own ink: <see cref="ThemeInk"/>'s dark green reads far too close to the
    /// dimmed app background behind it, so on the retro-green skin it borrows the panel's light green
    /// instead (with the icon flipping to the dark ink once the key is held, for contrast against its fill).</summary>
    private Vector4 PauseKeyInk => this.skin == "retro_green" ? RetroLcd.Panel : this.ThemeInk;

    private Vector4 PauseKeyPaper => this.skin == "retro_green" ? RetroLcd.Pixel : this.ThemePaper;

    /// <summary>A row of skin buttons, reused on the splash screen and the pause menu.</summary>
    private void DrawSkinPicker(OsAppContext ctx, float x, float y, float width)
    {
        var gap = ctx.Px(6f);
        var buttonH = ctx.Px(26f);
        var buttonW = (width - (gap * (SkinNames.Length - 1))) / SkinNames.Length;
        var cx = x;
        foreach (var name in SkinNames)
        {
            if (RetroLcd.Button($"##stkSkin{name}", ctx.Localize($"os.stacker_skin_{name}"),
                new Vector2(cx, y), new Vector2(buttonW, buttonH), ctx.Px(3f), filled: this.skin == name,
                ink: this.ThemeInk, paper: this.ThemePaper))
            {
                this.skin = name;
                this.storage.Set(SkinKey, this.skin);
            }
            cx += buttonW + gap;
        }
    }

    private void StartRun()
    {
        this.game.Reset();
        this.runSeconds = 0.0;
        this.lastFrameTime = ImGui.GetTime();
        this.paused = false;
        this.view = View.Playing;
        this.lastSeenLockCount = this.game.LockCount;
        this.lastSeenLineClearLockCount = this.game.LockCount;
        this.lineClearFlashElapsed = LineClearFlashSeconds;
        this.lastSeenHardDropCount = this.game.HardDropCount;
        this.hardDropFlashElapsed = StackerGame.HardDropLockoutSeconds;
        this.scoreFeedback = string.Empty;
        this.scoreFeedbackAge = ScoreFeedbackFadePieces;
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
        if (RetroLcd.Button("##stackerControls", ctx.Localize("os.stacker_controls"),
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, firstY + ((buttonH + ctx.Px(10f)) * 2f)),
            new Vector2(buttonW, buttonH), ctx.Px(4f), filled: false))
        {
            this.rebindingAction = -1;
            this.view = View.Controls;
        }
        if (RetroLcd.Button("##stackerBoard", ctx.Localize("os.arcade_leaderboard"),
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, firstY + ((buttonH + ctx.Px(10f)) * 3f)),
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
            dl.AddText(winPos + new Vector2((winSize.X - bestSize.X) * 0.5f, winSize.Y - ctx.Px(64f)),
                ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.7f }), best);
        }

        DrawSkinPicker(ctx, winPos.X + ctx.Px(8f), winPos.Y + winSize.Y - ctx.Px(34f), winSize.X - ctx.Px(16f));
    }

    /// <summary>A row of tetrominoes tumbling gently under the title.</summary>
    private void DrawDecorPieces(ImDrawListPtr dl, Vector2 winPos, Vector2 winSize, double now, bool reduceMotion)
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
                DrawMino(dl, origin, x, y, cell, kinds[i], 0.9f);
            }
        }
    }

    /// <summary>One board cell drawn from the active skin's texture atlas; skipped for the one frame or two
    /// before the texture has finished loading.</summary>
    private void DrawMino(ImDrawListPtr dl, Vector2 origin, int x, int y, float cell, int kind, float alpha = 1f)
    {
        var tex = MinoSkins.Get(this.skin)?.GetWrapOrDefault();
        if (tex is null)
        {
            return;
        }
        var (uv0, uv1) = MinoSkins.Uv(kind, new Vector2(tex.Width, tex.Height));
        RetroLcd.TexturedCell(dl, origin, x, y, cell, tex.Handle, uv0, uv1, alpha);
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

        // Only a sliver of margin: no on-screen pad means the board can claim almost the whole window.
        var padX = ctx.Px(4f);
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
        var boardMaxH = (winPos.Y + winSize.Y) - boardTop - ctx.Px(4f);
        var cell = MathF.Max(2f, MathF.Floor(MathF.Min(boardMaxW / StackerGame.Columns, boardMaxH / StackerGame.Rows)));
        var boardW = cell * StackerGame.Columns;
        var boardH = cell * StackerGame.Rows;
        var boardTL = new Vector2(winPos.X + ((winSize.X - boardW) * 0.5f), boardTop);

        DrawAppBackground(winPos, winSize, cell);
        UpdateScoreFeedback(ctx);
        UpdateLineClearFlash(delta);
        UpdateHardDropFlash(delta);
        DrawHud(ctx, winPos, winSize, padX, textTop, line, headerH);
        DrawPreviewBoxes(ctx, winPos, boxesTop, padX, boardMaxW, boxH, boxPad, boxLabelH, pieceCell);
        DrawScoreFeedback(winPos, winSize, feedbackTop, feedbackH);
        DrawWell(boardTL, boardW, boardH, cell);
        if (this.paused)
        {
            DrawPausedOverlay(ctx, boardTL, boardW, boardH);
        }
    }

    /// <summary>Notices a fresh lock (even a scoreless one) and either posts a new feedback message or
    /// lets the current one age a step towards fully faded.</summary>
    private void UpdateScoreFeedback(OsAppContext ctx)
    {
        if (this.game.LockCount == this.lastSeenLockCount)
        {
            return;
        }
        this.lastSeenLockCount = this.game.LockCount;
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

    private string BuildFeedbackMessage(OsAppContext ctx)
    {
        var cleared = this.game.LastCleared;
        if (this.game.LastClearIsTSpin)
        {
            var word = cleared switch
            {
                2 => ctx.Localize("os.stacker_fx_double"),
                3 => ctx.Localize("os.stacker_fx_triple"),
                _ => string.Empty,
            };
            var label = word + ctx.Localize("os.stacker_fx_tspin")
                + (this.game.LastClearIsMini ? ctx.Localize("os.stacker_fx_mini") : string.Empty);
            return this.game.LastClearIsBackToBack ? ctx.Localize("os.stacker_fx_b2b_prefix") + label : label;
        }
        if (cleared == 4)
        {
            return ctx.Localize(this.game.LastClearIsBackToBack ? "os.stacker_fx_b2b_tetris" : "os.stacker_fx_tetris");
        }
        if (cleared > 0 && this.game.LastClearCombo >= 1)
        {
            return string.Format(ctx.Localize("os.stacker_fx_combo"), this.game.LastClearCombo + 1);
        }
        return string.Empty;
    }

    /// <summary>Notices a fresh lock and, if it cleared lines, restarts the clear-row flash fade-out.</summary>
    private void UpdateLineClearFlash(double delta)
    {
        if (this.game.LockCount != this.lastSeenLineClearLockCount)
        {
            this.lastSeenLineClearLockCount = this.game.LockCount;
            this.lineClearFlashElapsed = this.game.LastCleared > 0 ? 0 : LineClearFlashSeconds;
        }
        else
        {
            this.lineClearFlashElapsed += delta;
        }
    }

    /// <summary>Notices a fresh hard drop and restarts its drop-trail flash fade-out.</summary>
    private void UpdateHardDropFlash(double delta)
    {
        if (this.game.HardDropCount != this.lastSeenHardDropCount)
        {
            this.lastSeenHardDropCount = this.game.HardDropCount;
            this.hardDropFlashElapsed = this.game.HardDropCount > 0 ? 0 : StackerGame.HardDropLockoutSeconds;
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
        var bgTex = StackerTextures.Get("gbg", this.skin)?.GetWrapOrDefault();
        if (bgTex is null)
        {
            dl.AddRectFilled(winPos, winPos + winSize, ImGui.GetColorU32(RetroLcd.PanelDim));
        }
        else
        {
            RetroLcd.TiledImage(dl, winPos, winSize, cell * AppBackgroundTileScale, bgTex.Handle, Vector2.Zero, Vector2.One, new Vector4(0.9f, 0.9f, 0.9f, 1f));
        }
        dl.AddRectFilled(winPos, winPos + winSize, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.25f)));
    }

    /// <summary>A HUD text field: white on an 80%-opaque black box for readability over the textured
    /// background, except on the retro-green skin which keeps its own lit-panel look.</summary>
    private void DrawReadableText(ImDrawListPtr dl, Vector2 pos, string text, float alpha = 1f)
    {
        if (this.skin == "retro_green")
        {
            dl.AddText(pos, ImGui.GetColorU32(RetroLcd.Panel with { W = alpha }), text);
            return;
        }
        var size = ImGui.CalcTextSize(text);
        var pad = new Vector2(4f, 2f);
        dl.AddRectFilled(pos - pad, pos + size + pad, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.8f * alpha)));
        dl.AddText(pos, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, alpha)), text);
    }

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

    private void DrawHud(OsAppContext ctx, Vector2 winPos, Vector2 winSize, float padX, float textTop, float line, float headerH)
    {
        var dl = ImGui.GetWindowDrawList();
        var levelText = string.Format(ctx.Localize("os.stacker_level_lines"), this.game.Level, this.game.Lines);
        var scoreText = string.Format(ctx.Localize("os.stacker_score"), this.game.Score);
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
            "hold", ctx.Localize("os.stacker_hold_label"), this.game.HeldKind);
        DrawPreviewBox(dl, ctx, nextTL, boxW, boxH, boxPad, boxLabelH, pieceCell,
            "next", ctx.Localize("os.stacker_next_label"), this.game.NextKind);
    }

    private void DrawPreviewBox(ImDrawListPtr dl, OsAppContext ctx, Vector2 tl, float boxW, float boxH,
        float boxPad, float boxLabelH, float pieceCell, string role, string label, int kind)
    {
        // The role art is the whole box (background, border and "Hold"/"Next" label baked in); only the
        // piece on top is drawn separately.
        var boxTex = StackerTextures.Get(role, this.skin)?.GetWrapOrDefault();
        if (boxTex is null)
        {
            dl.AddRectFilled(tl, tl + new Vector2(boxW, boxH), ImGui.GetColorU32(this.BoardBackground));
            dl.AddRect(tl, tl + new Vector2(boxW, boxH), ImGui.GetColorU32(RetroLcd.PanelEdge), ctx.Px(3f));
            var labelSize = ImGui.CalcTextSize(label);
            dl.AddText(tl + new Vector2((boxW - labelSize.X) * 0.5f, boxPad * 0.4f),
                ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.8f }), label);
        }
        else
        {
            dl.AddImage(boxTex.Handle, tl, tl + new Vector2(boxW, boxH));
        }

        if (kind < 0)
        {
            return;
        }
        // The piece art is drawn larger than the box was sized for, centred on the same point, so it can
        // overflow the border a little rather than shrinking the box or its label to fit.
        var pieceW = pieceCell * 4f;
        var cells = StackerGame.Cells(kind, 0, 0, 0).ToList();
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

    private void DrawWell(Vector2 boardTL, float boardW, float boardH, float cell)
    {
        var dl = ImGui.GetWindowDrawList();
        var bgTex = BackgroundSkins.Get(this.skin)?.GetWrapOrDefault();
        if (bgTex is null)
        {
            var fallback = this.BoardBackground;
            var darkFallback = ImGui.GetColorU32(new Vector4(fallback.X * 0.85f, fallback.Y * 0.85f, fallback.Z * 0.85f, 1f));
            var lightFallback = ImGui.GetColorU32(fallback);
            for (var x = 0; x < StackerGame.Columns; x++)
            {
                for (var y = 0; y < StackerGame.Rows; y++)
                {
                    var checkerTl = boardTL + new Vector2(x * cell, y * cell);
                    dl.AddRectFilled(checkerTl, checkerTl + new Vector2(cell + 0.5f, cell + 0.5f),
                        (x + y) % 2 == 0 ? lightFallback : darkFallback);
                }
            }
        }
        else
        {
            var size = new Vector2(bgTex.Width, bgTex.Height);
            var (lightUv0, lightUv1) = BackgroundSkins.LightUv(size);
            var (darkUv0, darkUv1) = BackgroundSkins.DarkUv(size);
            for (var x = 0; x < StackerGame.Columns; x++)
            {
                for (var y = 0; y < StackerGame.Rows; y++)
                {
                    var (uv0, uv1) = (x + y) % 2 == 0 ? (lightUv0, lightUv1) : (darkUv0, darkUv1);
                    RetroLcd.TexturedCell(dl, boardTL, x, y, cell, bgTex.Handle, uv0, uv1);
                }
            }
        }
        dl.AddRect(boardTL - new Vector2(2f, 2f), boardTL + new Vector2(boardW + 2f, boardH + 2f),
            ImGui.GetColorU32(this.skin == "retro_green" ? RetroLcd.Panel : new Vector4(1f, 1f, 1f, 1f)), 0f, ImDrawFlags.None, 2f);

        DrawHardDropFlash(dl, boardTL, cell);

        for (var x = 0; x < StackerGame.Columns; x++)
        {
            for (var y = 0; y < StackerGame.Rows; y++)
            {
                var kind = this.game.KindAt(x, y);
                if (kind >= 0)
                {
                    DrawMino(dl, boardTL, x, y, cell, kind);
                }
            }
        }

        DrawLineClearFlash(dl, boardTL, boardW, cell);

        // The landing ghost previews where the piece will land, using a dedicated faded/outlined skin.
        var ghostTex = MinoSkins.Get(this.skin, "skinghost")?.GetWrapOrDefault();
        foreach (var (x, y) in this.game.GhostCells())
        {
            if (y < 0)
            {
                continue;
            }
            if (ghostTex is null)
            {
                var tl = boardTL + new Vector2(x * cell, y * cell);
                dl.AddRect(tl + new Vector2(2f, 2f), tl + new Vector2(cell - 2f, cell - 2f),
                    ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.35f }), 0f, ImDrawFlags.None, 1.5f);
            }
            else
            {
                var (uv0, uv1) = MinoSkins.Uv(this.game.PieceKind, new Vector2(ghostTex.Width, ghostTex.Height));
                RetroLcd.TexturedCell(dl, boardTL, x, y, cell, ghostTex.Handle, uv0, uv1);
            }
        }

        foreach (var (x, y) in this.game.CurrentCells())
        {
            if (y >= 0)
            {
                DrawMino(dl, boardTL, x, y, cell, this.game.PieceKind);
            }
        }
    }

    /// <summary>White trail from where the piece was to where it fell on its last hard drop, drawn under
    /// the settled minos so it reads as a quick flash beneath the piece rather than over it.</summary>
    private void DrawHardDropFlash(ImDrawListPtr dl, Vector2 boardTL, float cell)
    {
        if (this.hardDropFlashElapsed >= StackerGame.HardDropLockoutSeconds)
        {
            return;
        }
        var alpha = HardDropFlashStartAlpha * (1f - (float)(this.hardDropFlashElapsed / StackerGame.HardDropLockoutSeconds));
        var startCells = StackerGame.Cells(this.game.LastHardDropKind, this.game.LastHardDropRotation,
            this.game.LastHardDropX, this.game.LastHardDropStartY).ToList();
        var endCells = StackerGame.Cells(this.game.LastHardDropKind, this.game.LastHardDropRotation,
            this.game.LastHardDropX, this.game.LastHardDropEndY);
        var minX = startCells.Min(c => c.X);
        var maxX = startCells.Max(c => c.X);
        var minY = Math.Max(0, startCells.Min(c => c.Y));
        var maxY = Math.Min(StackerGame.Rows - 1, endCells.Max(c => c.Y));
        var tl = boardTL + new Vector2(minX * cell, minY * cell);
        var br = boardTL + new Vector2((maxX + 1) * cell, (maxY + 1) * cell);
        dl.AddRectFilled(tl, br, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, alpha)));
    }

    /// <summary>White flash over the rows cleared by the last lock, fading out over <see cref="LineClearFlashSeconds"/>.</summary>
    private void DrawLineClearFlash(ImDrawListPtr dl, Vector2 boardTL, float boardW, float cell)
    {
        if (this.lineClearFlashElapsed >= LineClearFlashSeconds || this.game.LastClearedRows.Count == 0)
        {
            return;
        }
        var alpha = LineClearFlashStartAlpha * (1f - (float)(this.lineClearFlashElapsed / LineClearFlashSeconds));
        var color = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, alpha));
        foreach (var y in this.game.LastClearedRows)
        {
            var tl = boardTL + new Vector2(0f, y * cell);
            dl.AddRectFilled(tl, tl + new Vector2(boardW, cell), color);
        }
    }

    /// <summary>Arrows or WASD, with a repeat timer so holding left or right walks the piece across.</summary>
    private void ReadKeyboard(double delta)
    {
        if (Pressed(ControlAction.RotateRight))
        {
            this.game.Rotate();
        }
        if (Pressed(ControlAction.RotateLeft))
        {
            this.game.RotateLeft();
        }
        if (Pressed(ControlAction.Hold))
        {
            this.game.Hold();
        }
        if (Pressed(ControlAction.HardDrop))
        {
            this.game.HardDrop();
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

    private AppKey Binding(ControlAction action) => this.bindings[(int)action];

    private bool Pressed(ControlAction action) => this.keys.WasPressed(Binding(action));

    private void DrawControlsMenu(OsAppContext ctx, Vector2 winPos, Vector2 winSize)
    {
        this.keys.RequestExclusive();
        var dl = ImGui.GetWindowDrawList();
        var title = ctx.Localize("os.stacker_controls");
        var titleSize = ImGui.CalcTextSize(title);
        dl.AddText(winPos + new Vector2((winSize.X - titleSize.X) * 0.5f, ctx.Px(18f)),
            ImGui.GetColorU32(RetroLcd.Pixel), title);

        if (this.rebindingAction >= 0 && this.keys.TryGetPressedKey(out var pressed))
        {
            this.bindings[this.rebindingAction] = pressed;
            this.storage.Set(InputBindingsKey, this.bindings);
            this.rebindingAction = -1;
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
                this.rebindingAction = i;
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
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(boardTL, boardTL + new Vector2(boardW, boardH),
            ImGui.GetColorU32(this.ThemePaper with { W = 0.82f }));
        var label = ctx.Localize("os.stacker_paused");
        var labelSize = ImGui.CalcTextSize(label);
        dl.AddText(boardTL + new Vector2((boardW - labelSize.X) * 0.5f, (boardH * 0.5f) - ctx.Px(30f)),
            ImGui.GetColorU32(this.ThemeInk), label);

        var buttonW = boardW * 0.7f;
        var buttonH = ctx.Px(36f);
        if (RetroLcd.Button("##stkResume", ctx.Localize("os.stacker_resume"),
            boardTL + new Vector2((boardW - buttonW) * 0.5f, boardH * 0.5f), new Vector2(buttonW, buttonH),
            ctx.Px(4f), filled: true, ink: this.ThemeInk, paper: this.ThemePaper))
        {
            this.lastFrameTime = ImGui.GetTime();
            this.paused = false;
        }
        if (RetroLcd.Button("##stkRestart", ctx.Localize("os.stacker_restart"),
            boardTL + new Vector2((boardW - buttonW) * 0.5f, boardH * 0.5f + buttonH + ctx.Px(10f)), new Vector2(buttonW, buttonH),
            ctx.Px(4f), filled: false, ink: this.ThemeInk, paper: this.ThemePaper))
        {
            StartRun();
        }
        if (RetroLcd.Button("##stkMainMenu", ctx.Localize("os.stacker_menu"),
            boardTL + new Vector2((boardW - buttonW) * 0.5f, boardH * 0.5f + ((buttonH + ctx.Px(10f)) * 2f)),
            new Vector2(buttonW, buttonH), ctx.Px(4f), filled: false, ink: this.ThemeInk, paper: this.ThemePaper))
        {
            this.splashStartedAt = ImGui.GetTime();
            this.view = View.Splash;
        }

        DrawSkinPicker(ctx, boardTL.X + ctx.Px(8f), boardTL.Y + boardH - ctx.Px(34f), boardW - ctx.Px(16f));
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
