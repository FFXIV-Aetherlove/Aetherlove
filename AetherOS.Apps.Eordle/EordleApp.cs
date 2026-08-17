using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AetherLove.Shared.Arcade;
using AetherLove.UI;
using AetherOS.Apps.Eordle.Words;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Eordle;

/// <summary>A word-guess marathon on the handheld LCD: consecutive five-letter words, six guesses each,
/// until one slips through. Mouse-only on the Sudoku model; the on-screen keyboard is the only input,
/// so the game never takes the real keyboard away from chat.</summary>
public sealed class EordleApp : IAetherApp
{
    private const string HighScoresKey = "high_scores";
    private const string HelpSeenKey = "help_seen";
    private const string WordLangKey = "wordLang";
    private const int ScoreSlots = 5;

    private const double FlipStagger = 0.08;
    private const double FlipDuration = 0.24;
    private const double ShakeSeconds = 0.4;
    private const double NotWordFlashSeconds = 1.8;

    /// <summary>How long the quit key stays armed after the first tap.</summary>
    private const double QuitConfirmSeconds = 2.5;
    private const double AutoAdvanceSeconds = 2.5;
    private const double DecorLetterSeconds = 0.4;
    private const double DecorHoldSeconds = 1.4;

    private static readonly Vector4 TileTopColor = new(0.44f, 0.62f, 0.35f, 1f);
    private static readonly Vector4 TileBottomColor = new(0.12f, 0.25f, 0.15f, 1f);

    private static readonly string[] KeyboardLayout = ["QWERTYUIOP", "ASDFGHJKL", "ZXCVBNM"];

    /// <summary>Per-letter ids and glyph strings, built once: the board and keyboard would otherwise
    /// allocate around eighty throwaway strings every frame.</summary>
    private static readonly string[] LetterKeyIds =
        [.. Enumerable.Range(0, 26).Select(i => "##eordleKey" + (char)('A' + i))];

    private static readonly string[] LetterGlyphs =
        [.. Enumerable.Range(0, 26).Select(i => ((char)('A' + i)).ToString())];

    private static string Glyph(char letter) =>
        letter >= 'A' && letter <= 'Z' ? LetterGlyphs[letter - 'A'] : letter.ToString();
    private static readonly string[] DecorWords = ["IFRIT", "SHIVA", "TITAN", "RAMUH", "VIERA"];

    private enum View
    {
        Splash,
        Playing,
        Solved,
        GameOver,
        Scores,
        Leaderboard,
        Help,
    }

    private readonly Func<string> name;
    private readonly IAppStorage storage;
    private readonly AetherLove.Os.IArcadeRewards rewards;
    private readonly AetherLove.Os.IArcadeScores scores;
    private readonly AetherLove.Widgets.ArcadeLeaderboardView leaderboard;
    private readonly EordleGame game = new();

    private View view = View.Splash;
    private bool paused;
    private double lastFrameTime;
    private double splashStartedAt;
    private int[] highScores = [];
    private bool loaded;
    private WordLanguage wordLang = WordLanguage.En;
    private int lastRunScore;
    private bool lastRunWasRecord;
    private double submitAt = -100.0;
    private double shakeAt = -100.0;
    private double quitArmedAt = -100.0;
    private double notWordAt = -100.0;
    private double tooShortAt = -100.0;
    private double solvedShownAt;

    public EordleApp(Func<string> name, IAppCapabilities capabilities, AetherLove.Os.IArcadeRewards rewards,
        AetherLove.Os.IArcadeScores scores)
    {
        this.name = name;
        this.storage = capabilities.Storage("eordle");
        this.rewards = rewards;
        this.scores = scores;
        this.leaderboard = new AetherLove.Widgets.ArcadeLeaderboardView(scores, ArcadeGame.Eordle);
    }

    public string Id => "eordle";

    public string Name => this.name();

    public FontAwesomeIcon Icon => FontAwesomeIcon.Font;

    public Vector4 TileTop => TileTopColor;

    public Vector4 TileBottom => TileBottomColor;

    public int Badge => 0;

    public bool HasSurface => true;

    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Strings =>
        Localization.AppStrings.Packs;

    public void Open()
    {
    }

    public void OnForeground()
    {
        this.lastFrameTime = ImGui.GetTime();
        this.splashStartedAt = ImGui.GetTime();
        if (this.view == View.Solved)
        {
            // The reward card auto-advances on wall clock, which keeps running while nothing draws.
            this.solvedShownAt = ImGui.GetTime();
        }
    }

    public void OnBackground()
    {
        if (this.view == View.Playing)
        {
            this.paused = true;
        }
    }

    public void OnIntent(OsIntent intent)
    {
    }

    public void Draw(OsAppContext ctx)
    {
        EnsureLoaded(ctx);
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
            case View.Solved:
                DrawSolved(ctx, now, winPos, winSize);
                break;
            case View.GameOver:
                DrawGameOver(ctx, winPos, winSize);
                break;
            case View.Scores:
                DrawScores(ctx, winPos, winSize);
                break;
            case View.Help:
                DrawHelp(ctx, winPos, winSize);
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

    private void EnsureLoaded(OsAppContext ctx)
    {
        if (this.loaded)
        {
            return;
        }
        this.loaded = true;
        this.highScores = this.storage.Get<int[]>(HighScoresKey) ?? [];
        // The tile language is shape, not colour: filled, outlined, dim. Nobody can read that off a board,
        // so the first visit opens on the legend rather than leaving it behind a button they have no reason
        // to press.
        if (!this.storage.Get<bool>(HelpSeenKey))
        {
            this.view = View.Help;
        }
        var stored = this.storage.Get<string>(WordLangKey);
        var parsed = ParseLang(stored);
        if (parsed is { } lang)
        {
            this.wordLang = lang;
        }
        else
        {
            SetWordLang(SeedLangFromCulture(ctx));
        }
    }

    private static WordLanguage? ParseLang(string? code) => code switch
    {
        "en" => WordLanguage.En,
        "de" => WordLanguage.De,
        "fr" => WordLanguage.Fr,
        _ => null,
    };

    private static WordLanguage SeedLangFromCulture(OsAppContext ctx) =>
        ctx.Culture.TwoLetterISOLanguageName switch
        {
            "de" => WordLanguage.De,
            "fr" => WordLanguage.Fr,
            _ => WordLanguage.En,
        };

    private void SetWordLang(WordLanguage lang)
    {
        this.wordLang = lang;
        this.storage.Set(WordLangKey, lang.ToString().ToLowerInvariant());
    }

    private int BestScore => this.highScores.Length > 0 ? this.highScores[0] : 0;

    private void StartRun()
    {
        this.game.Start(this.wordLang);
        this.paused = false;
        this.submitAt = -100.0;
        this.shakeAt = -100.0;
        this.quitArmedAt = -100.0;
        this.notWordAt = -100.0;
        this.tooShortAt = -100.0;
        this.lastFrameTime = ImGui.GetTime();
        this.view = View.Playing;
    }

    /// <summary>A finished run is the sparks signal; the server re-checks the score against what the
    /// guess ladder makes possible.</summary>
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
            ArcadeGame.Eordle, this.lastRunScore, (int)(this.game.RunSeconds * 1000.0),
            this.game.WordsSolved, this.game.TotalGuesses));
        this.view = View.GameOver;
    }

    /// <summary>How to play, and above all what the three tile shapes mean. The LCD has exactly one colour,
    /// so the usual green/yellow/grey is filled/outlined/dim here and there is no way to guess that from the
    /// board. Opens by itself on the first visit and stays reachable from the splash afterwards.</summary>
    private void DrawHelp(OsAppContext ctx, Vector2 winPos, Vector2 winSize)
    {
        var dl = ImGui.GetWindowDrawList();
        var title = ctx.Localize("os.eordle_help");
        var titleSize = ImGui.CalcTextSize(title);
        var y = winPos.Y + ctx.Px(18f);
        dl.AddText(new Vector2(winPos.X + ((winSize.X - titleSize.X) * 0.5f), y),
            ImGui.GetColorU32(RetroLcd.Pixel), title);
        y += titleSize.Y + ctx.Px(14f);

        // One real tile per state, drawn by the same routine the board uses, so the legend can never drift
        // from what a guess actually looks like.
        var cell = MathF.Min(ctx.Px(34f), winSize.X * 0.13f);
        var left = winPos.X + ctx.Px(16f);
        (EordleTile State, char Letter, string Key)[] legend =
        [
            (EordleTile.Correct, 'A', "os.eordle_help_correct"),
            (EordleTile.Present, 'B', "os.eordle_help_present"),
            (EordleTile.Absent, 'C', "os.eordle_help_absent"),
        ];
        foreach (var (state, letter, key) in legend)
        {
            DrawTile(dl, new Vector2(left, y), cell, letter, state, 1f);
            var line = ctx.Localize(key);
            dl.AddText(new Vector2(left + cell + ctx.Px(12f), y + ((cell - ImGui.GetTextLineHeight()) * 0.5f)),
                ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.9f }), line);
            y += cell + ctx.Px(8f);
        }

        y += ctx.Px(8f);
        foreach (var key in new[] { "os.eordle_help_rule1", "os.eordle_help_rule2", "os.eordle_help_rule3" })
        {
            var line = ctx.Localize(key);
            dl.AddText(new Vector2(left, y), ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.75f }), line);
            y += ImGui.GetTextLineHeight() + ctx.Px(6f);
        }

        var buttonW = winSize.X * 0.62f;
        var buttonH = ctx.Px(38f);
        var buttonY = winPos.Y + winSize.Y - buttonH - ctx.Px(20f);
        if (RetroLcd.Button("##eordleHelpDone", ctx.Localize("os.eordle_help_close"),
            new Vector2(winPos.X + ((winSize.X - buttonW) * 0.5f), buttonY), new Vector2(buttonW, buttonH),
            ctx.Px(4f), filled: true))
        {
            this.storage.Set(HelpSeenKey, true);
            this.splashStartedAt = ImGui.GetTime();
            this.view = View.Splash;
        }
    }

    private void DrawSplash(OsAppContext ctx, double now, Vector2 winPos, Vector2 winSize)
    {
        var dl = ImGui.GetWindowDrawList();
        var elapsed = now - this.splashStartedAt;
        var litRows = ctx.ReduceMotion
            ? RetroLcd.GlyphHeight
            : (int)Math.Min(RetroLcd.GlyphHeight, elapsed / 0.09);

        var pixel = MathF.Max(2f, winSize.X * 0.66f / RetroLcd.WordColumns("EORDLE"));
        var wordH = RetroLcd.GlyphHeight * pixel;
        var wordY = winSize.Y * 0.10f;
        RetroLcd.DrawWordCentered(dl, "EORDLE", winPos + new Vector2(0f, wordY), winSize.X, pixel,
            ImGui.GetColorU32(RetroLcd.Pixel), litRows);

        var subtitle = ctx.Localize("os.eordle_subtitle");
        var subSize = ImGui.CalcTextSize(subtitle);
        dl.AddText(winPos + new Vector2((winSize.X - subSize.X) * 0.5f, wordY + wordH + ctx.Px(12f)),
            ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.75f }), subtitle);

        DrawDecorTiles(ctx, dl, now, winPos, winSize);

        var buttonW = winSize.X * 0.62f;
        var buttonH = ctx.Px(38f);
        var gap = ctx.Px(10f);
        var buttonX = winPos.X + ((winSize.X - buttonW) * 0.5f);
        var y = winPos.Y + (winSize.Y * 0.50f);
        if (RetroLcd.Button("##eordlePlay", ctx.Localize("os.eordle_play"),
            new Vector2(buttonX, y), new Vector2(buttonW, buttonH), ctx.Px(4f), filled: true))
        {
            StartRun();
        }
        y += buttonH + gap;
        y += DrawLangRow(ctx, dl, winPos, winSize, y) + gap;
        if (RetroLcd.Button("##eordleScores", ctx.Localize("os.eordle_high_scores"),
            new Vector2(buttonX, y), new Vector2(buttonW, buttonH), ctx.Px(4f), filled: false))
        {
            this.view = View.Scores;
        }
        y += buttonH + gap;
        if (RetroLcd.Button("##eordleBoard", ctx.Localize("os.arcade_leaderboard"),
            new Vector2(buttonX, y), new Vector2(buttonW, buttonH), ctx.Px(4f), filled: false))
        {
            this.view = View.Leaderboard;
        }

        if (RetroLcd.Key("##eordleExit", FontAwesomeIcon.ArrowLeft,
            winPos + new Vector2(ctx.Px(12f), ctx.Px(12f)), ctx.Px(30f)))
        {
            ctx.Shell.GoHomeToFolder(IOsShell.ArcadeFolderId);
        }

        if (RetroLcd.Key("##eordleHelp", FontAwesomeIcon.Question,
            winPos + new Vector2(winSize.X - ctx.Px(42f), ctx.Px(12f)), ctx.Px(30f)))
        {
            this.view = View.Help;
        }

        if (BestScore > 0)
        {
            var best = string.Format(ctx.Localize("os.eordle_best"), BestScore);
            var bestSize = ImGui.CalcTextSize(best);
            dl.AddText(winPos + new Vector2((winSize.X - bestSize.X) * 0.5f, winSize.Y - ctx.Px(28f)),
                ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.7f }), best);
        }
    }

    /// <summary>The word-list language choice, deliberately independent of the phone language: EN, DE and
    /// FR word banks, free to pick. Returns the row's height so the splash stack can flow around it.</summary>
    private float DrawLangRow(OsAppContext ctx, ImDrawListPtr dl, Vector2 winPos, Vector2 winSize, float y)
    {
        var label = ctx.Localize("os.eordle_word_lang");
        var labelSize = ImGui.CalcTextSize(label);
        var langW = ctx.Px(38f);
        var langH = ctx.Px(26f);
        var gap = ctx.Px(6f);
        var rowW = labelSize.X + ctx.Px(10f) + (langW * 3f) + (gap * 2f);
        var x = winPos.X + ((winSize.X - rowW) * 0.5f);

        dl.AddText(new Vector2(x, y + ((langH - labelSize.Y) * 0.5f)),
            ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.75f }), label);
        x += labelSize.X + ctx.Px(10f);

        foreach (var lang in new[] { WordLanguage.En, WordLanguage.De, WordLanguage.Fr })
        {
            if (RetroLcd.Button($"##eordleLang{lang}", lang.ToString().ToUpperInvariant(),
                new Vector2(x, y), new Vector2(langW, langH), ctx.Px(4f), filled: lang == this.wordLang))
            {
                SetWordLang(lang);
            }
            x += langW + gap;
        }
        return langH;
    }

    /// <summary>Five tiles cycling through primal names one lit letter at a time, the splash's idle life.</summary>
    private void DrawDecorTiles(OsAppContext ctx, ImDrawListPtr dl, double now, Vector2 winPos, Vector2 winSize)
    {
        var tile = ctx.Px(30f);
        var gap = ctx.Px(5f);
        var rowW = (tile * EordleGame.WordLength) + (gap * (EordleGame.WordLength - 1));
        var tl = winPos + new Vector2((winSize.X - rowW) * 0.5f, winSize.Y * 0.33f);

        var word = DecorWords[0];
        var lit = EordleGame.WordLength;
        if (!ctx.ReduceMotion)
        {
            var cycle = (EordleGame.WordLength * DecorLetterSeconds) + DecorHoldSeconds;
            var t = (now - this.splashStartedAt) % (DecorWords.Length * cycle);
            var index = (int)(t / cycle);
            word = DecorWords[index];
            lit = Math.Min(EordleGame.WordLength, (int)((t - (index * cycle)) / DecorLetterSeconds) + 1);
        }

        for (var i = 0; i < EordleGame.WordLength; i++)
        {
            var cellTL = tl + new Vector2(i * (tile + gap), 0f);
            if (i < lit)
            {
                DrawTile(dl, cellTL, tile, word[i], EordleTile.Correct, 1f);
            }
            else
            {
                dl.AddRect(cellTL, cellTL + new Vector2(tile, tile),
                    ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.35f }), 0f, ImDrawFlags.None, 1.5f);
            }
        }
    }

    private void DrawPlaying(OsAppContext ctx, double now, double delta, Vector2 winPos, Vector2 winSize)
    {
        if (RetroLcd.WindowBlurred())
        {
            this.paused = true;
        }

        if (!this.paused)
        {
            this.game.Tick(delta);
        }

        var hudH = ctx.Px(28f);
        DrawHud(ctx, winPos, winSize, hudH);

        var kbH = winSize.Y * 0.28f;
        var boardMaxW = winSize.X - ctx.Px(24f);
        var boardMaxH = winSize.Y - hudH - kbH - ctx.Px(30f);
        var cell = MathF.Floor(MathF.Min(boardMaxW / EordleGame.WordLength, boardMaxH / EordleGame.MaxGuesses));
        var boardSize = new Vector2(cell * EordleGame.WordLength, cell * EordleGame.MaxGuesses);
        var boardTL = new Vector2(winPos.X + ((winSize.X - boardSize.X) * 0.5f), winPos.Y + hudH + ctx.Px(6f));

        DrawBoard(ctx, now, boardTL, cell, boardSize);
        DrawMessage(ctx, now, winPos, winSize, boardTL.Y + boardSize.Y);
        var keyboardBottom = DrawKeyboard(ctx, winPos, winSize, kbH);
        DrawQuit(ctx, now, winPos, winSize, keyboardBottom);

        if (this.paused)
        {
            DrawPausedOverlay(ctx, boardTL, boardSize);
        }

        if (this.game.Outcome != EordleOutcome.Playing)
        {
            var revealDone = ctx.ReduceMotion
                || now >= this.submitAt + ((EordleGame.WordLength - 1) * FlipStagger) + FlipDuration;
            if (revealDone)
            {
                if (this.game.Outcome == EordleOutcome.Solved)
                {
                    this.solvedShownAt = now;
                    this.view = View.Solved;
                }
                else
                {
                    FinishRun();
                }
            }
        }
    }

    private void DrawHud(OsAppContext ctx, Vector2 winPos, Vector2 winSize, float hudH)
    {
        var dl = ImGui.GetWindowDrawList();
        var padX = ctx.Px(10f);
        var textY = winPos.Y + ((hudH - ImGui.GetTextLineHeight()) * 0.5f);
        var reserve = RetroLcd.PauseKeyWidth(hudH) + ctx.Px(8f);

        if (!this.paused && RetroLcd.PauseKey(winPos, winSize, padX, hudH))
        {
            this.paused = true;
        }

        dl.AddText(new Vector2(winPos.X + padX, textY), ImGui.GetColorU32(RetroLcd.Pixel),
            string.Format(ctx.Localize("os.eordle_score"), this.game.Score));

        var clock = TimeSpan.FromSeconds(this.game.WordSeconds).ToString(@"m\:ss");
        var clockSize = ImGui.CalcTextSize(clock);
        dl.AddText(new Vector2(winPos.X + ((winSize.X - clockSize.X) * 0.5f), textY),
            ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.45f }), clock);

        var words = string.Format(ctx.Localize("os.eordle_words"), this.game.WordsSolved);
        var wordsSize = ImGui.CalcTextSize(words);
        dl.AddText(new Vector2(winPos.X + winSize.X - padX - reserve - wordsSize.X, textY),
            ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.7f }), words);
    }

    private void DrawBoard(OsAppContext ctx, double now, Vector2 boardTL, float cell, Vector2 boardSize)
    {
        var dl = ImGui.GetWindowDrawList();
        dl.AddRect(boardTL, boardTL + boardSize, ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.55f }),
            0f, ImDrawFlags.None, 2f);
        RetroLcd.DotGrid(dl, boardTL, EordleGame.WordLength, EordleGame.MaxGuesses, cell);

        var lastRow = this.game.Rows.Count - 1;
        for (var r = 0; r < this.game.Rows.Count; r++)
        {
            var word = this.game.Rows[r];
            var states = this.game.RowStates[r];
            for (var c = 0; c < EordleGame.WordLength; c++)
            {
                var tl = boardTL + new Vector2(c * cell, r * cell);
                if (r == lastRow && !ctx.ReduceMotion)
                {
                    DrawRevealingTile(dl, tl, cell, word[c], states[c], now, c);
                }
                else
                {
                    DrawTile(dl, tl, cell, word[c], states[c], 1f);
                }
            }
        }

        if (this.game.Outcome == EordleOutcome.Playing && this.game.Rows.Count < EordleGame.MaxGuesses)
        {
            var shakeX = 0f;
            var shaking = now - this.shakeAt < ShakeSeconds;
            if (shaking && !ctx.ReduceMotion)
            {
                var t = (float)(now - this.shakeAt);
                shakeX = MathF.Sin(t * 40f) * ctx.Px(3f) * (1f - (t / (float)ShakeSeconds));
            }
            var row = this.game.Rows.Count;
            for (var c = 0; c < this.game.Entry.Length; c++)
            {
                var tl = boardTL + new Vector2((c * cell) + shakeX, row * cell);
                DrawEntryGlyph(dl, tl, cell, this.game.Entry[c]);
            }
        }
    }

    /// <summary>The submit flip: each tile in the freshly judged row stays a plain glyph until its
    /// staggered turn, then squashes open into its judged state, left to right.</summary>
    private void DrawRevealingTile(ImDrawListPtr dl, Vector2 tl, float cell, char letter, EordleTile state,
        double now, int column)
    {
        var t = (now - this.submitAt - (column * FlipStagger)) / FlipDuration;
        if (t >= 1.0)
        {
            DrawTile(dl, tl, cell, letter, state, 1f);
            return;
        }
        if (t < 0.5)
        {
            DrawEntryGlyph(dl, tl, cell, letter);
            return;
        }
        DrawTile(dl, tl, cell, letter, state, (float)((t - 0.5) * 2.0));
    }

    /// <summary>One judged tile in the LCD alpha language. <paramref name="squash"/> scales it open
    /// vertically for the flip; 1 is fully open.</summary>
    private static void DrawTile(ImDrawListPtr dl, Vector2 tl, float cell, char letter, EordleTile state,
        float squash)
    {
        var gap = MathF.Max(1f, cell * 0.08f);
        var half = (cell * 0.5f) - gap;
        var centerY = tl.Y + (cell * 0.5f);
        var inTL = new Vector2(tl.X + gap, centerY - (half * squash));
        var inBR = new Vector2(tl.X + cell - gap, centerY + (half * squash));
        const float GlyphRevealSquash = 0.7f;

        switch (state)
        {
            case EordleTile.Correct:
                dl.AddRectFilled(inTL, inBR, ImGui.GetColorU32(RetroLcd.Pixel));
                if (squash >= GlyphRevealSquash)
                {
                    DrawTileGlyph(dl, tl, cell, letter, ImGui.GetColorU32(RetroLcd.Panel));
                }
                break;
            case EordleTile.Present:
                dl.AddRect(inTL, inBR, ImGui.GetColorU32(RetroLcd.Pixel), 0f, ImDrawFlags.None, 2f);
                if (squash >= GlyphRevealSquash)
                {
                    DrawTileGlyph(dl, tl, cell, letter, ImGui.GetColorU32(RetroLcd.Pixel));
                }
                break;
            default:
                if (squash >= GlyphRevealSquash)
                {
                    DrawTileGlyph(dl, tl, cell, letter, ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.45f }));
                }
                break;
        }
    }

    /// <summary>A not-yet-judged letter: the full glyph with no cell chrome.</summary>
    private static void DrawEntryGlyph(ImDrawListPtr dl, Vector2 tl, float cell, char letter)
    {
        DrawTileGlyph(dl, tl, cell, letter, ImGui.GetColorU32(RetroLcd.Pixel));
    }

    private static void DrawTileGlyph(ImDrawListPtr dl, Vector2 tl, float cell, char letter, uint color)
    {
        var pixel = MathF.Max(1f, cell * 0.52f / RetroLcd.GlyphHeight);
        RetroLcd.DrawWordCentered(dl, Glyph(letter),
            tl + new Vector2(0f, (cell - (RetroLcd.GlyphHeight * pixel)) * 0.5f), cell, pixel, color,
            RetroLcd.GlyphHeight);
    }

    /// <summary>Why the guess was refused, under the board, on every refusal. Drawn on a plate rather than
    /// as bare text: over a board of lit cells a thin line of pixels is easy to miss, and this is the one
    /// piece of the screen that has to be read.</summary>
    private void DrawMessage(OsAppContext ctx, double now, Vector2 winPos, Vector2 winSize, float boardBottom)
    {
        string text;
        if (now - this.notWordAt < NotWordFlashSeconds)
        {
            text = ctx.Localize("os.eordle_not_word");
        }
        else if (now - this.tooShortAt < NotWordFlashSeconds)
        {
            text = ctx.Localize("os.eordle_too_short");
        }
        else if (this.game.Outcome == EordleOutcome.Playing
            && this.game.Rows.Count == 0 && this.game.Entry.Length == 0)
        {
            // A blank board with a keyboard under it says nothing about what it wants. Only on the very
            // first row: once a guess is on the board the game has explained itself.
            var hint = ctx.Localize("os.eordle_hint_type");
            var hintSize = ImGui.CalcTextSize(hint);
            ImGui.GetWindowDrawList().AddText(
                new Vector2(winPos.X + ((winSize.X - hintSize.X) * 0.5f), boardBottom + ctx.Px(10f)),
                ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.65f }), hint);
            return;
        }
        else
        {
            return;
        }
        var size = ImGui.CalcTextSize(text);
        var dl = ImGui.GetWindowDrawList();
        var at = new Vector2(winPos.X + ((winSize.X - size.X) * 0.5f), boardBottom + ctx.Px(8f));
        var pad = new Vector2(ctx.Px(10f), ctx.Px(4f));
        dl.AddRectFilled(at - pad, at + size + pad,
            ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.16f }), ctx.Px(6f));
        dl.AddRect(at - pad, at + size + pad,
            ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.55f }), ctx.Px(6f), ImDrawFlags.None, 1.5f);
        dl.AddText(at, ImGui.GetColorU32(RetroLcd.Pixel), text);
    }

    /// <summary>Ends the run on purpose. Losing was the only way out before, which is a poor answer for
    /// someone who wants to stop while ahead. Two taps: a run with a real score on it is not something to
    /// lose to a stray click, and the second tap is the whole confirmation (a modal here would be heavier
    /// than the thing it guards).</summary>
    private void DrawQuit(OsAppContext ctx, double now, Vector2 winPos, Vector2 winSize, float keyboardBottom)
    {
        if (this.paused)
        {
            return;
        }
        var armed = now - this.quitArmedAt < QuitConfirmSeconds;
        var label = ctx.Localize(armed ? "os.eordle_quit_confirm" : "os.eordle_quit");
        var size = new Vector2(MathF.Max(ctx.Px(74f), ImGui.CalcTextSize(label).X + ctx.Px(20f)), ctx.Px(26f));
        // Under the keyboard and centred: beside the board it sat on the same line as the hint, and both
        // wanted the middle of a narrow screen.
        var y = MathF.Min(keyboardBottom + ctx.Px(10f), winPos.Y + winSize.Y - size.Y - ctx.Px(6f));
        var tl = new Vector2(winPos.X + ((winSize.X - size.X) * 0.5f), y);
        if (RetroLcd.Button("##eordleQuit", label, tl, size, ctx.Px(4f), filled: armed))
        {
            if (armed)
            {
                this.quitArmedAt = -100.0;
                FinishRun();
            }
            else
            {
                this.quitArmedAt = now;
            }
        }
    }

    /// <summary>Draws the on-screen keys and returns the y its bottom row ends at, so anything below can
    /// sit under it rather than guessing at the layout.</summary>
    private float DrawKeyboard(OsAppContext ctx, Vector2 winPos, Vector2 winSize, float kbH)
    {
        if (this.paused)
        {
            return winPos.Y + winSize.Y;
        }
        var gap = ctx.Px(3f);
        var sidePad = ctx.Px(6f);
        var keyW = MathF.Floor((winSize.X - (sidePad * 2f) - (gap * 9f)) / 10f);
        var keyH = MathF.Min(ctx.Px(42f), (kbH - (gap * 2f) - ctx.Px(10f)) / 3f);
        var wideW = MathF.Floor(keyW * 1.5f);
        var top = winPos.Y + winSize.Y - kbH + ctx.Px(4f);

        for (var rowIndex = 0; rowIndex < KeyboardLayout.Length; rowIndex++)
        {
            var row = KeyboardLayout[rowIndex];
            var rowW = (row.Length * keyW) + ((row.Length - 1) * gap);
            if (rowIndex == 2)
            {
                rowW += (wideW + gap) * 2f;
            }
            var x = winPos.X + ((winSize.X - rowW) * 0.5f);
            var y = top + (rowIndex * (keyH + gap));

            foreach (var letter in row)
            {
                if (LetterKey(LetterKeyIds[letter - 'A'], letter, this.game.KeyState(letter),
                    new Vector2(x, y), new Vector2(keyW, keyH)))
                {
                    this.game.TypeLetter(letter);
                }
                x += keyW + gap;
            }

            // Backspace then ENTER, both on the right, where a keyboard puts them. Wordle's own layout
            // splits them across the row and it reads as a mistake on a phone-sized board.
            if (rowIndex == 2)
            {
                if (IconKey("##eordleBack", FontAwesomeIcon.Backspace,
                    new Vector2(x, y), new Vector2(wideW, keyH), this.game.Entry.Length > 0))
                {
                    this.game.Backspace();
                }
                x += wideW + gap;
                if (IconKey("##eordleEnter", FontAwesomeIcon.Check, new Vector2(x, y),
                    new Vector2(wideW, keyH), this.game.Entry.Length == EordleGame.WordLength))
                {
                    HandleSubmit(ctx);
                }
            }
        }

        return top + ((keyH + gap) * KeyboardLayout.Length) - gap;
    }

    private void HandleSubmit(OsAppContext ctx)
    {
        var now = ImGui.GetTime();
        switch (this.game.Submit())
        {
            // The line ALWAYS shows. It used to be the reduce-motion stand-in for the shake, which meant
            // everyone else got a 0.4s wobble and no reason, and a refused guess read as the game quietly
            // eating the word. The shake is the decoration on top of the answer, not the answer.
            case EordleSubmit.NotAWord:
                this.notWordAt = now;
                this.tooShortAt = -100.0;
                this.shakeAt = ctx.ReduceMotion ? -100.0 : now;
                break;
            case EordleSubmit.TooShort:
                this.tooShortAt = now;
                this.notWordAt = -100.0;
                this.shakeAt = ctx.ReduceMotion ? -100.0 : now;
                break;
            case EordleSubmit.Accepted:
            case EordleSubmit.Solved:
            case EordleSubmit.Failed:
                this.submitAt = now;
                break;
        }
    }

    private static (bool Pressed, bool Held) KeyChrome(string id, Vector2 tl, Vector2 size, bool armed = true)
    {
        ImGui.SetCursorScreenPos(tl);
        ImGui.InvisibleButton(id, size);
        var pressed = ImGui.IsItemClicked();
        var held = ImGui.IsItemActive();
        if (ImGui.IsItemHovered() && armed)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }
        return (pressed, held);
    }

    /// <summary>A keyboard key wearing the row's best knowledge of its letter: dimmed once ruled out,
    /// outlined while known present-elsewhere, filled once its place is nailed down.</summary>
    private static bool LetterKey(string id, char letter, EordleKeyState state, Vector2 tl, Vector2 size)
    {
        var (pressed, held) = KeyChrome(id, tl, size);
        var dl = ImGui.GetWindowDrawList();
        var br = tl + size;
        var rounding = MathF.Min(size.X, size.Y) * 0.22f;
        const float DimAlpha = 0.35f;

        uint glyph;
        switch (state)
        {
            case EordleKeyState.Correct:
                dl.AddRectFilled(tl, br, ImGui.GetColorU32(RetroLcd.Pixel with { W = held ? 1f : 0.9f }), rounding);
                glyph = ImGui.GetColorU32(RetroLcd.Panel);
                break;
            case EordleKeyState.Present:
                dl.AddRectFilled(tl, br, ImGui.GetColorU32(RetroLcd.Pixel with { W = held ? 0.85f : 0.14f }), rounding);
                dl.AddRect(tl, br, ImGui.GetColorU32(RetroLcd.Pixel), rounding, ImDrawFlags.None, 2f);
                glyph = ImGui.GetColorU32(held ? RetroLcd.Panel : RetroLcd.Pixel);
                break;
            case EordleKeyState.Absent:
                dl.AddRect(tl, br, ImGui.GetColorU32(RetroLcd.Pixel with { W = DimAlpha }), rounding,
                    ImDrawFlags.None, 1.5f);
                glyph = ImGui.GetColorU32(RetroLcd.Pixel with { W = DimAlpha });
                break;
            default:
                dl.AddRectFilled(tl, br, ImGui.GetColorU32(RetroLcd.Pixel with { W = held ? 0.85f : 0.14f }), rounding);
                dl.AddRect(tl, br, ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.65f }), rounding,
                    ImDrawFlags.None, 2f);
                glyph = ImGui.GetColorU32(held ? RetroLcd.Panel : RetroLcd.Pixel);
                break;
        }

        var pixel = MathF.Max(1f, MathF.Min(size.Y * 0.42f, size.X * 0.8f) / RetroLcd.GlyphHeight);
        RetroLcd.DrawWordCentered(dl, Glyph(letter),
            tl + new Vector2(0f, (size.Y - (RetroLcd.GlyphHeight * pixel)) * 0.5f), size.X, pixel, glyph,
            RetroLcd.GlyphHeight);
        return pressed;
    }

    private static bool IconKey(string id, FontAwesomeIcon icon, Vector2 tl, Vector2 size, bool armed = true)
    {
        var (pressed, held) = KeyChrome(id, tl, size, armed);
        var dl = ImGui.GetWindowDrawList();
        var br = tl + size;
        var rounding = MathF.Min(size.X, size.Y) * 0.22f;
        dl.AddRectFilled(tl, br,
            ImGui.GetColorU32(RetroLcd.Pixel with { W = held ? 0.85f : armed ? 0.14f : 0.05f }), rounding);
        dl.AddRect(tl, br, ImGui.GetColorU32(RetroLcd.Pixel with { W = armed ? 0.65f : 0.25f }), rounding,
            ImDrawFlags.None, 2f);
        RetroLcd.DrawIcon(dl, icon, MathF.Min(size.X, size.Y) * 0.4f, tl + (size * 0.5f),
            ImGui.GetColorU32(held ? RetroLcd.Panel : RetroLcd.Pixel with { W = armed ? 1f : 0.35f }));
        return pressed;
    }

    private void DrawPausedOverlay(OsAppContext ctx, Vector2 boardTL, Vector2 boardSize)
    {
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(boardTL, boardTL + boardSize, ImGui.GetColorU32(RetroLcd.Panel with { W = 0.82f }));
        dl.AddRect(boardTL, boardTL + boardSize,
            ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.5f }), 0f, ImDrawFlags.None, 2f);

        var label = ctx.Localize("os.eordle_paused");
        var labelSize = ImGui.CalcTextSize(label);
        dl.AddText(boardTL + new Vector2((boardSize.X - labelSize.X) * 0.5f, (boardSize.Y * 0.5f) - ctx.Px(34f)),
            ImGui.GetColorU32(RetroLcd.Pixel), label);

        var buttonW = boardSize.X * 0.7f;
        var buttonH = ctx.Px(36f);
        if (RetroLcd.Button("##eordleResume", ctx.Localize("os.eordle_resume"),
            boardTL + new Vector2((boardSize.X - buttonW) * 0.5f, boardSize.Y * 0.5f),
            new Vector2(buttonW, buttonH), ctx.Px(4f), filled: true))
        {
            this.lastFrameTime = ImGui.GetTime();
            this.paused = false;
        }
    }

    /// <summary>Between words: what the last one paid, then straight on. The card advances itself so a
    /// hot streak never needs the button.</summary>
    private void DrawSolved(OsAppContext ctx, double now, Vector2 winPos, Vector2 winSize)
    {
        if (now - this.solvedShownAt >= AutoAdvanceSeconds)
        {
            Advance();
            return;
        }

        var dl = ImGui.GetWindowDrawList();
        var title = ctx.Localize("os.eordle_solved");
        using (ctx.TitleFont?.Push())
        {
            var titleSize = ImGui.CalcTextSize(title);
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
                winPos + new Vector2((winSize.X - titleSize.X) * 0.5f, winSize.Y * 0.16f),
                ImGui.GetColorU32(RetroLcd.Pixel), title);
        }

        var tile = ctx.Px(34f);
        var gap = ctx.Px(5f);
        var rowW = (tile * EordleGame.WordLength) + (gap * (EordleGame.WordLength - 1));
        var tl = winPos + new Vector2((winSize.X - rowW) * 0.5f, winSize.Y * 0.28f);
        for (var i = 0; i < EordleGame.WordLength; i++)
        {
            DrawTile(dl, tl + new Vector2(i * (tile + gap), 0f), tile, this.game.Answer[i],
                EordleTile.Correct, 1f);
        }

        var lines = new[]
        {
            string.Format(ctx.Localize("os.eordle_points"), this.game.LastWordPoints),
            string.Format(ctx.Localize("os.eordle_guesses_used"), this.game.LastWordGuesses),
            string.Format(ctx.Localize("os.eordle_score"), this.game.Score),
        };
        var y = winSize.Y * 0.42f;
        foreach (var line in lines)
        {
            var size = ImGui.CalcTextSize(line);
            dl.AddText(winPos + new Vector2((winSize.X - size.X) * 0.5f, y), ImGui.GetColorU32(RetroLcd.Pixel), line);
            y += ImGui.GetTextLineHeightWithSpacing();
        }

        var buttonW = winSize.X * 0.62f;
        var buttonH = ctx.Px(38f);
        if (RetroLcd.Button("##eordleNext", ctx.Localize("os.eordle_next_word"),
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, winSize.Y * 0.62f),
            new Vector2(buttonW, buttonH), ctx.Px(4f), filled: true))
        {
            Advance();
        }
    }

    private void Advance()
    {
        this.game.NextWord();
        this.submitAt = -100.0;
        this.lastFrameTime = ImGui.GetTime();
        this.view = View.Playing;
    }

    private void DrawGameOver(OsAppContext ctx, Vector2 winPos, Vector2 winSize)
    {
        var dl = ImGui.GetWindowDrawList();
        var title = ctx.Localize(this.lastRunWasRecord ? "os.eordle_new_record" : "os.eordle_game_over");
        using (ctx.TitleFont?.Push())
        {
            var titleSize = ImGui.CalcTextSize(title);
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
                winPos + new Vector2((winSize.X - titleSize.X) * 0.5f, winSize.Y * 0.18f),
                ImGui.GetColorU32(RetroLcd.Pixel), title);
        }

        var lines = new List<string>
        {
            string.Format(ctx.Localize("os.eordle_answer_was"), this.game.Answer),
            string.Format(ctx.Localize("os.eordle_score"), this.lastRunScore),
            string.Format(ctx.Localize("os.eordle_solved_count"), this.game.WordsSolved),
        };
        if (this.game.WordsSolved > 0)
        {
            lines.Add(string.Format(ctx.Localize("os.eordle_fewest"), this.game.BestWordGuesses));
        }
        var y = winSize.Y * 0.32f;
        foreach (var line in lines)
        {
            var size = ImGui.CalcTextSize(line);
            dl.AddText(winPos + new Vector2((winSize.X - size.X) * 0.5f, y), ImGui.GetColorU32(RetroLcd.Pixel), line);
            y += ImGui.GetTextLineHeightWithSpacing();
        }

        var buttonW = winSize.X * 0.62f;
        var buttonH = ctx.Px(38f);
        var firstY = winSize.Y * 0.62f;
        if (RetroLcd.Button("##eordleAgain", ctx.Localize("os.eordle_play_again"),
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, firstY), new Vector2(buttonW, buttonH),
            ctx.Px(4f), filled: true))
        {
            StartRun();
        }
        if (RetroLcd.Button("##eordleMenu", ctx.Localize("os.eordle_menu"),
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
        var title = ctx.Localize("os.eordle_high_scores");
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
            var empty = ctx.Localize("os.eordle_no_scores");
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
        if (RetroLcd.Button("##eordleScoresBack", ctx.Localize("os.eordle_menu"),
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, winSize.Y - buttonH - ctx.Px(24f)),
            new Vector2(buttonW, buttonH), ctx.Px(4f), filled: false))
        {
            this.splashStartedAt = ImGui.GetTime();
            this.view = View.Splash;
        }
    }
}
