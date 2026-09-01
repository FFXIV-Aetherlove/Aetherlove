using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using AetherLove.Os;
using AetherLove.Shared.Aetherling;
using AetherLove.Shared.Arcade;
using AetherLove.UI;
using AetherLove.Widgets;
using AetherOS.PetKit.Engine;
using AetherOS.Apps.Aetherling.Screens.Games;
using AetherOS.Apps.Aetherling.Ui;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Aetherling.Screens;

/// <summary>Playtime: the little screen behind the pet page's Game door. A soft select hub with one card
/// per game, a gentle 3-2-1, the game itself, and a results moment; the weekly and all-time boards ride
/// the arcade's score system underneath but none of its looks. The companion in these games is a puppet
/// drawn from the shared body: a round of play never lifts its mood or resets its habits, so the creature
/// on the pet page is exactly as the player left it.</summary>
internal sealed class GamesScreen
{
    private const float PadX = 18f;
    private const float CountdownSeconds = 2.4f;

    private enum Phase
    {
        Select,
        Title,
        Countdown,
        Playing,
        Paused,
        Results,
        Leaderboard,
    }

    private readonly IAetherlingHost _host;
    private readonly PetRuntime _runtime;
    private readonly IArcadeScores _scores;
    private readonly IAppStorage _storage;
    private readonly SoftLeaderboardPanel _leaderboard;
    private readonly CloudHopGame _cloudHop = new();
    private readonly CrystalCatchGame _crystalCatch = new();
    private readonly HillRollGame _hillRoll = new();
    private readonly Games.LumiLink.LumiLinkGame _lumiLink = new();
    private readonly Games.LumiLink.LumiLinkGuide _lumiLinkGuide = new();
    private readonly Games.Gyre.GyreGame _gyre = new();
    private readonly Games.Gyre.GyreGuide _gyreGuide = new();
    private readonly Dictionary<ArcadeGame, int> _bests = [];

    /// <summary>Games this shelf has never been opened for. A card wears "New" until its first open, not
    /// until its first score: a run that ends badly is still a game the player has met.</summary>
    private readonly HashSet<ArcadeGame> _unopened = [];

    private Phase _phase = Phase.Select;
    private Phase _boardReturn = Phase.Select;
    private IPetGame? _active;
    private ArcadeGame _boardGame = ArcadeGame.CloudHop;
    private AetherlingDto? _core;
    private float _countdown;
    private float _runSeconds;
    private double _lastFrame;
    private bool _submitted;
    private bool _newBest;
    private int _lastScore;
    private bool _bestsLoaded;
    private const float LumiLinkSpeedPerLevel = 0.02f;

    /// <summary>What one Gyre stage adds to its chapter's loop, pitch and tempo together. Five stages a
    /// chapter, so a chapter ends about a seventh faster than it began: audible as the screws tightening,
    /// short of the chipmunk a whole run's worth of climbing would reach.</summary>
    private const float GyreSpeedPerStage = 0.035f;

    private const float GyreMusicPerEndlessStep = 0.02f;

    private const float GyreEndlessMusicCap = 0.30f;
    private string? _bgmTrack;
    private float _bgmSpeed = 1f;

    /// <summary>On the game list rather than inside one, which is the only phase the app's nav bar may
    /// draw over: a run holds ImGui's active id, and the leaderboard sits over a run paused behind it.</summary>
    public bool AtHub => _phase == Phase.Select;

    /// <summary>The music switch was flipped. The app owns the stored answer, since the ceremony's own mute
    /// button writes the same one.</summary>
    public event Action<bool>? MuteChanged;

    /// <summary>The music level, for the app to persist beside the mute it already stores.</summary>
    public event Action<float>? VolumeChanged;

    /// <summary>A round is in progress (counting down, playing or paused). The app mirrors this to the
    /// host so the phone battery can never die mid-run.</summary>
    public bool RunActive => _phase is Phase.Countdown or Phase.Playing or Phase.Paused;

    public GamesScreen(IAetherlingHost host, PetRuntime runtime, IArcadeScores scores, IAppStorage storage)
    {
        _host = host;
        _runtime = runtime;
        _scores = scores;
        _storage = storage;
        _leaderboard = new SoftLeaderboardPanel(scores);
    }

    private IPetGame[] AllGames => [_cloudHop, _crystalCatch, _hillRoll, _lumiLink, _gyre];

    public void OnShow(AetherlingDto? core)
    {
        _core = core;
        _phase = Phase.Select;
        _active = null;
        _lastFrame = ImGui.GetTime();
        if (core is not null)
        {
            _runtime.EnsureLoaded(_host.AssetRoot, PetState.FormFolder(core));
            _runtime.ApplyLook(core);
        }
        if (!_bestsLoaded)
        {
            _bestsLoaded = true;
            foreach (var game in AllGames)
            {
                _bests[game.Id] = _storage.Get<int?>(BestKey(game.Id)) ?? 0;
                // A shelf that predates the flag has been played for years: only a game whose badge has
                // never been offered counts as new, and a best score proves it was played before.
                var seen = _storage.Get<bool?>(OpenedKey(game.Id)) ?? _bests[game.Id] > 0;
                if (!seen && NewGames.Contains(game.Id))
                {
                    _unopened.Add(game.Id);
                }
            }
        }
    }

    /// <summary>The screen is no longer on show: the loop goes with it, whatever path led away. Called on
    /// every one of them, so a track can never outlive the game it belongs to.</summary>
    public void OnHide()
    {
        if (_bgmTrack is null)
        {
            return;
        }
        _bgmTrack = null;
        _host.StopBgm();
    }

    private static string BestKey(ArcadeGame game) => $"games.best.{game.ToString().ToLowerInvariant()}";

    private static string OpenedKey(ArcadeGame game) => $"games.opened.{game.ToString().ToLowerInvariant()}";

    /// <summary>Which games are new enough to say so. A game leaves this list when it stops being news;
    /// nothing else on the shelf has ever worn the badge, and an empty list is the resting state.</summary>
    private static readonly ArcadeGame[] NewGames = [ArcadeGame.Gyre];

    private static string NameKey(ArcadeGame game) => $"os.aetherling_game_{game.ToString().ToLowerInvariant()}";

    /// <summary>How the game is played, shown on the countdown where it is about to be needed.</summary>
    private static string HintKey(ArcadeGame game) => $"os.aetherling_game_{game.ToString().ToLowerInvariant()}_hint";

    /// <summary>What the game IS, for the shelf and the title screen. A card that recites the keybinds
    /// sells nothing; the controls can wait until the player has chosen and is watching the countdown.</summary>
    private static string BlurbKey(ArcadeGame game) => $"os.aetherling_game_{game.ToString().ToLowerInvariant()}_blurb";

    /// <summary>Each game's own loop, under the plugin's bgm folder.</summary>
    private static string BgmFile(ArcadeGame game) => game switch
    {
        ArcadeGame.CloudHop => "bgm_cloudhop_dreamydandelion.ogg",
        ArcadeGame.CrystalCatch => "bgm_crystall_catch_treasure_trove.ogg",
        ArcadeGame.LumiLink => "bgm_lumi_link.ogg",
        _ => "bgm_hill_roll_firefly_forest.ogg",
    };

    private static FontAwesomeIcon Fallback(ArcadeGame game) => game switch
    {
        ArcadeGame.CloudHop => FontAwesomeIcon.Cloud,
        ArcadeGame.CrystalCatch => FontAwesomeIcon.Gem,
        ArcadeGame.LumiLink => FontAwesomeIcon.Th,
        ArcadeGame.Gyre => FontAwesomeIcon.CircleNotch,
        _ => FontAwesomeIcon.Mountain,
    };

    /// <summary>Gyre swaps its loop per chapter of the ladder; a chapter whose track has not shipped
    /// yet falls back to the first one that exists, so the plumbing precedes the files.</summary>
    /// <summary>Gyre's two loops, alternating by chapter: the sky and the cavern share one, the hills and
    /// the core the other, and each chapter climbs its own pitch across its five stages (see
    /// <see cref="GyreSpeedPerStage"/>). Two tracks rather than four on purpose: a run is twenty stages
    /// long and hearing the same pair return, faster each time, reads as the descent tightening.</summary>
    private string GyreBgm(int chapter)
    {
        string[] tracks = ["bgm_gyre_1.ogg", "bgm_gyre_2.ogg", "bgm_gyre_1.ogg", "bgm_gyre_2.ogg"];
        var bgmDir = Path.Combine(_host.AssetRoot, "..", "bgm");
        var wanted = tracks[Math.Clamp(chapter, 0, 3)];
        if (File.Exists(Path.Combine(bgmDir, wanted)))
        {
            return wanted;
        }
        foreach (var track in tracks)
        {
            if (File.Exists(Path.Combine(bgmDir, track)))
            {
                return track;
            }
        }
        return "bgm_lumi_link.ogg";
    }

    public void Draw(OsAppContext ctx)
    {
        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var now = ImGui.GetTime();
        var dt = Math.Clamp((float)(now - _lastFrame), 0f, 1f / 30f);
        var gap = now - _lastFrame;
        _lastFrame = now;

        _runtime.Tick(ctx.ReduceMotion);

        // The phone was away (home button, another app, combat hide): freeze rather than step, and let
        // the player choose to carry on.
        if (_phase == Phase.Playing && gap > 0.5)
        {
            _phase = Phase.Paused;
        }

        switch (_phase)
        {
            case Phase.Select:
                DrawSelect(ctx, dl, origin, size);
                break;
            case Phase.Title:
                DrawTitle(ctx, dl, origin, size);
                break;
            case Phase.Countdown:
                DrawRun(ctx, dl, origin, size, 0f, inputActive: false);
                DrawCountdown(ctx, dl, origin, size, dt);
                ClaimStage(origin, size);
                break;
            case Phase.Playing:
                // A press on either corner chip is chrome, not play: the games read the mouse over the
                // whole stage, so without this a tap on pause also throttles the cart it is stopping.
                DrawRun(ctx, dl, origin, size, dt, inputActive: !ChromeHovered(origin, size));
                DrawPauseChip(dl, origin);
                ClaimStage(origin, size);
                // Escape always pauses, read raw from ImGui rather than through the capture service: the
                // capture's focused field monopolises the active id, and arming it from the PAUSE screen
                // would deaden the very buttons that resume.
                if (ImGui.IsKeyPressed(ImGuiKey.Escape, false))
                {
                    _phase = Phase.Paused;
                }
                if (_active is { Over: true })
                {
                    Finish();
                    _phase = Phase.Results;
                }
                else
                {
                    _runSeconds += dt;
                }
                break;
            case Phase.Paused:
                // The board stays hidden: a pause must not be a free look at the next move.
                Look.Backdrop(dl, ctx.Theme, origin, size);
                DrawPausedOverlay(ctx, dl, origin, size);
                ClaimStage(origin, size);
                if (ImGui.IsKeyPressed(ImGuiKey.Escape, false))
                {
                    _phase = Phase.Playing;
                }
                break;
            case Phase.Results:
                Look.Backdrop(dl, ctx.Theme, origin, size);
                Look.Halo(dl, origin + new Vector2(size.X * 0.5f, size.Y * 0.3f), size.X * 0.6f, Look.Crystal, 0.1f);
                DrawResults(ctx, dl, origin, size);
                ClaimStage(origin, size);
                break;
            case Phase.Leaderboard:
                DrawLeaderboard(ctx, dl, origin, size);
                break;
        }

        SyncBgm();
        if (_bgmTrack is not null)
        {
            DrawMuteChip(dl, origin, size);
        }
    }

    /// <summary>The loop follows the chosen game: it comes up with the title screen and runs until the
    /// player is back on the shelf. Reconciled from the phase each frame rather than fired on transitions,
    /// so no exit path can leave music behind.</summary>
    /// <summary>How far into its own chapter a Gyre stage is, 0..4. The endless finale sits at the top of
    /// the last one and stays there.</summary>
    private static int StageInChapter(int stage) => Math.Clamp((Math.Max(1, stage) - 1) % 5, 0, 4);

    private void SyncBgm()
    {
        var wanted = _phase != Phase.Select && _active is { } game ? BgmFile(game.Id) : null;
        if (wanted is not null && _active is Games.Gyre.GyreGame gyre)
        {
            wanted = GyreBgm(gyre.Chapter);
        }
        // Lumi-Link's loop climbs two percent per level, pitch and tempo together; Gyre's climbs across
        // the five stages of a chapter and starts over when the next chapter brings the other loop in.
        var speed = 1f;
        if (wanted is not null && _active is Games.LumiLink.LumiLinkGame lumi)
        {
            speed = 1f + (LumiLinkSpeedPerLevel * Math.Max(0, lumi.Metric1 - 1));
        }
        else if (wanted is not null && _active is Games.Gyre.GyreGame gyreSpeed)
        {
            speed = 1f + (GyreSpeedPerStage * StageInChapter(gyreSpeed.Metric1));
            // The Core has no next stage to hand the climb to, so its own steps carry the loop up.
            speed += Math.Min(GyreEndlessMusicCap, GyreMusicPerEndlessStep * gyreSpeed.EndlessSteps);
        }
        if (wanted == _bgmTrack && Math.Abs(speed - _bgmSpeed) < 0.001f)
        {
            return;
        }
        _bgmTrack = wanted;
        _bgmSpeed = speed;
        if (wanted is null)
        {
            _host.StopBgm();
        }
        else
        {
            _host.StartGameBgm(wanted, speed);
        }
    }

    private const string LumiLinkGuideKey = "games.lumilink.guideSeen";
    private const string GyreGuideKey = "games.gyre.guideSeen";

    private void Start(IPetGame game)
    {
        // The match-3 explains itself the first time; the explainer carries the start with it.
        if (game is Games.LumiLink.LumiLinkGame && _storage.Get<bool?>(LumiLinkGuideKey) != true && !_lumiLinkGuide.Active)
        {
            _lumiLinkGuide.Show(() =>
            {
                _storage.Set(LumiLinkGuideKey, true);
                Start(game);
            });
            return;
        }
        if (game is Games.Gyre.GyreGame && _storage.Get<bool?>(GyreGuideKey) != true && !_gyreGuide.Active)
        {
            _gyreGuide.Show(() =>
            {
                _storage.Set(GyreGuideKey, true);
                Start(game);
            });
            return;
        }
        _lumiLink.SetCreature(_core);
        _gyre.SetCreature(_core);
        _active = game;
        game.Reset(new Random());
        _countdown = CountdownSeconds;
        _runSeconds = 0f;
        _submitted = false;
        _newBest = false;
        _phase = Phase.Countdown;
    }

    private void Finish()
    {
        if (_active is not { } game || _submitted)
        {
            return;
        }
        _submitted = true;
        _lastScore = game.Score;
        var best = _bests.GetValueOrDefault(game.Id);
        _newBest = _lastScore > best;
        if (_newBest)
        {
            _bests[game.Id] = _lastScore;
            _storage.Set(BestKey(game.Id), (int?)_lastScore);
        }
        if (_lastScore > 0)
        {
            _scores.SubmitScore(new ArcadeScoreSubmissionDto(
                game.Id, _lastScore, (int)(_runSeconds * 1000f), game.Metric1, game.Metric2));
            _host.NoteGameFinished();
            _leaderboard.Invalidate(game.Id);
        }
    }

    private void DrawSelect(OsAppContext ctx, ImDrawListPtr dl, Vector2 origin, Vector2 size)
    {
        Look.Backdrop(dl, ctx.Theme, origin, size);
        Look.Motes(dl, origin, size, 22, Look.CrystalPale, 0.4f, ImGui.GetTime(), ctx.ReduceMotion);

        var pad = Px(PadX);
        var name = _core?.PetName ?? AetherlingLimits.DefaultName;
        var y = PetPageUi.Header(ctx, dl, origin, ctx.Localize("os.aetherling_games_title"));

        // Under the title and on its left edge: centred, it landed beside the heading rather than below it.
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.92f, new Vector2(origin.X + pad, y),
            Look.U32(Look.Whisper), ctx.Localize("os.aetherling_games_pick"));
        y += (ImGui.GetTextLineHeight() * 0.92f) + Px(12f);

        var cardW = size.X - (pad * 2f);
        var gap = Px(8f);
        foreach (var game in AllGames)
        {
            y += DrawGameCard(ctx, dl, game, new Vector2(origin.X + pad, y), cardW) + gap;
        }

        // The mascot itself, idling under its games; drawn with the runtime's own pose because the hub
        // is not a game and here it is genuinely the creature.
        var footY = origin.Y + size.Y - PetNavBar.Reserved;
        var room = footY - y - Px(16f);
        if (room > Px(56f) && _runtime.Ready)
        {
            var petPx = MathF.Min(room, Px(84f));
            var bottom = new Vector2(origin.X + (size.X * 0.5f), footY - Px(2f));
            Look.GroundGlow(dl, bottom + new Vector2(0f, Px(4f)), petPx * 0.7f, petPx * 0.15f, Look.Crystal, 0.3f);
            _runtime.Draw(dl, ctx.Capabilities.Textures, bottom, petPx, _runtime.Pose);
        }
    }

    /// <summary>One game's card, sized to its own text rather than to a guess: the how-to lines are a
    /// sentence in six languages and a fixed height had them running over the best line. Returns the
    /// height it drew, so the list can stack whatever each card needed.</summary>
    private float DrawGameCard(OsAppContext ctx, ImDrawListPtr dl, IPetGame game, Vector2 tl, float width)
    {
        // Five games fit the shelf without scrolling only because the card is measured rather than
        // guessed: the padding, the icon and the gaps between the three lines are all as small as they
        // can be while the blurb still reads as a paragraph.
        const float HintScale = 0.82f;
        var padIn = Px(9f);
        var iconSide = Px(64f);
        var trophySide = Px(24f);

        var textX = tl.X + padIn + iconSide + Px(11f);
        var textLimit = tl.X + width - textX - padIn;
        var hint = ctx.Localize(BlurbKey(game.Id));
        var best = _bests.GetValueOrDefault(game.Id);
        var bestText = best > 0
            ? string.Format(ctx.Localize("os.aetherling_game_best"), best.ToString("N0"))
            : string.Empty;

        var nameH = ImGui.GetTextLineHeight();
        var hintH = Look.WrappedHeight(hint, textLimit, HintScale);
        var bestH = bestText.Length > 0 ? ImGui.GetTextLineHeight() * HintScale : 0f;
        var textBlock = nameH + Px(4f) + hintH + (bestH > 0f ? Px(4f) + bestH : 0f);
        var height = MathF.Max(iconSide + (padIn * 2f), textBlock + (padIn * 2f));

        // The trophy is submitted before the card's own button, so its little corner wins the click.
        var trophyTL = tl + new Vector2(width - trophySide - Px(10f), height - trophySide - Px(8f));
        ImGui.SetCursorScreenPos(trophyTL);
        var trophyClicked = ImGui.InvisibleButton($"##gameTrophy{game.Id}", new Vector2(trophySide, trophySide));
        HandOnHover();
        var trophyHovered = ImGui.IsItemHovered();

        ImGui.SetCursorScreenPos(tl);
        var clicked = ImGui.InvisibleButton($"##gameCard{game.Id}", new Vector2(width, height));
        HandOnHover();
        var hovered = ImGui.IsItemHovered();

        var radius = Px(16f);
        dl.AddRectFilled(tl, tl + new Vector2(width, height),
            Look.U32(new Vector4(1f, 1f, 1f, hovered ? 0.10f : 0.055f)), radius);
        dl.AddRect(tl, tl + new Vector2(width, height),
            Look.U32(hovered ? Look.Crystal with { W = 0.5f } : new Vector4(1f, 1f, 1f, 0.12f)),
            radius, ImDrawFlags.RoundCornersAll, Px(1.2f));

        DrawGameIcon(ctx, dl, game.Id, tl + new Vector2(padIn, (height - iconSide) * 0.5f), iconSide);

        var textY = tl.Y + ((height - textBlock) * 0.5f);
        var name = ctx.Localize(NameKey(game.Id));
        var badge = _unopened.Contains(game.Id) ? ctx.Localize("os.news_new") : string.Empty;
        var badgeW = badge.Length > 0
            ? (ImGui.CalcTextSize(badge).X * HintScale) + Px(14f)
            : 0f;
        dl.AddText(new Vector2(textX, textY), Look.U32(Look.CrystalPale),
            TruncateToWidth(name, textLimit - badgeW));
        if (badge.Length > 0)
        {
            var badgeTl = new Vector2(
                textX + MathF.Min(ImGui.CalcTextSize(name).X, textLimit - badgeW) + Px(8f),
                textY + Px(2f));
            var badgeH = (ImGui.GetTextLineHeight() * HintScale) + Px(3f);
            dl.AddRectFilled(badgeTl, badgeTl + new Vector2(badgeW - Px(8f), badgeH),
                Look.U32(Look.Spark, 0.9f), badgeH * 0.5f);
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * HintScale,
                badgeTl + new Vector2(Px(6f), Px(1f)), Look.U32(Look.Void), badge);
        }
        textY += nameH + Px(4f);
        Look.LeftWrapped(dl, hint, textX, textY, textLimit, Look.U32(Look.Whisper), HintScale);
        if (bestText.Length > 0)
        {
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * HintScale,
                new Vector2(textX, textY + hintH + Px(4f)), Look.U32(Look.Spark, 0.9f), bestText);
        }

        IconDraw.AddCentered(dl, FontAwesomeIcon.Trophy, Px(13f),
            trophyTL + new Vector2(trophySide * 0.5f, trophySide * 0.5f),
            Look.U32(trophyHovered ? Look.Spark : Look.Whisper));

        if (trophyClicked)
        {
            _boardGame = game.Id;
            _boardReturn = Phase.Select;
            _phase = Phase.Leaderboard;
        }
        else if (clicked)
        {
            if (_unopened.Remove(game.Id))
            {
                _storage.Set(OpenedKey(game.Id), (bool?)true);
            }
            _active = game;
            _phase = Phase.Title;
        }
        return height;
    }

    /// <summary>The game's own title screen, the way a cabinet would open: the name up in lights over its
    /// art, the personal best, the how-to, and the three doors.</summary>
    private void DrawTitle(OsAppContext ctx, ImDrawListPtr dl, Vector2 origin, Vector2 size)
    {
        if (_active is not { } game)
        {
            _phase = Phase.Select;
            return;
        }
        Look.Backdrop(dl, ctx.Theme, origin, size);
        Look.Motes(dl, origin, size, 26, Look.CrystalPale, 0.45f, ImGui.GetTime(), ctx.ReduceMotion);

        var centreX = origin.X + (size.X * 0.5f);
        var iconSide = Px(116f);
        var y = origin.Y + (size.Y * 0.08f);
        Look.Halo(dl, new Vector2(centreX, y + (iconSide * 0.5f)), iconSide * 1.3f, Look.Crystal, 0.25f, 4);
        DrawGameIcon(ctx, dl, game.Id, new Vector2(centreX - (iconSide * 0.5f), y), iconSide);
        y += iconSide + Px(20f);

        Look.GlowText(dl, ctx.Localize(NameKey(game.Id)), centreX, y, Look.U32(Look.CrystalPale), 1.5f,
            Look.Crystal, 0.7f);
        y += Px(46f);

        if (_bests.GetValueOrDefault(game.Id) is > 0 and var best)
        {
            y += Look.Pill(dl, string.Format(ctx.Localize("os.aetherling_game_best"), best.ToString("N0")),
                centreX, y, Look.Spark, 0.9f) + Px(12f);
        }

        var wrap = size.X - Px(64f);
        var blurb = ctx.Localize(BlurbKey(game.Id));
        Look.CentredWrapped(dl, blurb, centreX, y, wrap, Look.U32(Look.Body), 0.95f);
        y += Look.WrappedHeight(blurb, wrap, 0.95f) + Px(24f);

        var buttonsTop = MathF.Max(y, origin.Y + (size.Y * 0.58f));
        if (DrawSoftButton(ctx, dl, "##titleStart", ctx.Localize("os.aetherling_game_start"), centreX,
            buttonsTop, true))
        {
            Start(game);
        }
        if (DrawSoftButton(ctx, dl, "##titleBoards", ctx.Localize("os.arcade_leaderboard"), centreX,
            buttonsTop + Px(52f), false))
        {
            _boardGame = game.Id;
            _boardReturn = Phase.Title;
            _phase = Phase.Leaderboard;
        }
        var backTop = buttonsTop + Px(104f);
        if (game is Games.LumiLink.LumiLinkGame)
        {
            if (DrawSoftButton(ctx, dl, "##titleHelp", ctx.Localize("os.aetherling_lumilink_help"), centreX,
                backTop, false))
            {
                _lumiLinkGuide.Show(null);
            }
            backTop += Px(52f);
        }
        if (game is Games.Gyre.GyreGame)
        {
            if (DrawSoftButton(ctx, dl, "##titleHelp", ctx.Localize("os.aetherling_gyre_help"), centreX,
                backTop, false))
            {
                _gyreGuide.Show(null);
            }
            backTop += Px(52f);
        }
        if (DrawSoftButton(ctx, dl, "##titleBack", ctx.Localize("os.aetherling_game_tohub"), centreX,
            backTop, false))
        {
            _active = null;
            _phase = Phase.Select;
        }
        _lumiLinkGuide.Draw(ctx, origin, size, _host.AssetRoot, _core);
        _gyreGuide.Draw(ctx, origin, size, _host.AssetRoot, _core, _runtime);
    }

    private void DrawGameIcon(OsAppContext ctx, ImDrawListPtr dl, ArcadeGame game, Vector2 tl, float side)
    {
        var radius = side * 0.22f;
        var path = Path.Combine(_host.AssetRoot, "games", $"{game.ToString().ToLowerInvariant()}.png");
        if (ctx.Capabilities.Textures.Get(path) is { } texture)
        {
            // The art carries its own frame and glow, so it gets the tile to itself: a plate behind it
            // only shows up as a mismatched square at the corners.
            dl.AddImageRounded(texture, tl, tl + new Vector2(side, side), Vector2.Zero, Vector2.One,
                Look.U32(new Vector4(1f, 1f, 1f, 1f)), radius);
            return;
        }
        dl.AddRectFilled(tl, tl + new Vector2(side, side), Look.U32(Look.Crystal, 0.12f), radius);
        IconDraw.AddCentered(dl, Fallback(game), side * 0.44f, tl + new Vector2(side * 0.5f, side * 0.5f),
            Look.U32(Look.Crystal));
    }

    private void DrawRun(OsAppContext ctx, ImDrawListPtr dl, Vector2 origin, Vector2 size, float dt, bool inputActive)
    {
        if (_active is not { } game || _runtime.Manifest is not { } manifest)
        {
            _phase = Phase.Select;
            return;
        }

        // The mute chip is the screen's, so the screen is what swallows a sound; the games only ever say
        // what happened.
        var audio = ctx.Capabilities.Audio;
        var stage = new GameStage(origin, size, _runtime, manifest, _host.AssetRoot, ctx.ReduceMotion,
            inputActive,
            sound =>
            {
                if (!_host.BgmMuted)
                {
                    GameSounds.Play(audio, _host.SoundRoot, sound);
                }
            });
        game.UpdateAndDraw(ctx, dl, stage, dt);
    }

    /// <summary>The stage's catch-all, submitted LAST in the frame so every real button before it wins its
    /// clicks. It draws nothing; it exists because a held click on bare phone body drags the phone window,
    /// which is the last thing a Hill Roll throttle should do.</summary>
    private static void ClaimStage(Vector2 origin, Vector2 size)
    {
        ImGui.SetCursorScreenPos(origin);
        ImGui.InvisibleButton("##gameStage", size);
    }

    private static float ChipSide => Px(32f);

    /// <summary>How much of the top-right corner the mute chip claims, for a game's own HUD to stay clear
    /// of. Derived from the chip rather than typed twice: the hearts sat under it until this existed.</summary>
    internal static float CornerReserve => ChipSide + Px(22f);

    private static Vector2 PauseChipTL(Vector2 origin) => origin + new Vector2(Px(12f), Px(10f));

    private static Vector2 MuteChipTL(Vector2 origin, Vector2 size) =>
        origin + new Vector2(size.X - ChipSide - Px(12f), Px(10f));

    private static bool OverChip(Vector2 tl)
    {
        var mouse = ImGui.GetIO().MousePos;
        var side = ChipSide;
        return mouse.X >= tl.X && mouse.X <= tl.X + side && mouse.Y >= tl.Y && mouse.Y <= tl.Y + side;
    }

    private static bool ChromeHovered(Vector2 origin, Vector2 size) =>
        OverChip(PauseChipTL(origin)) || OverChip(MuteChipTL(origin, size));

    /// <summary>Hit-tested by hand rather than as an ImGui item: while a game polls the keyboard, the
    /// capture field holds the active id and ImGui refuses to hover ANY other item, so a real button here
    /// is structurally dead for exactly as long as it is needed. Raw mouse reads do not care.</summary>
    private void DrawPauseChip(ImDrawListPtr dl, Vector2 origin)
    {
        var tl = PauseChipTL(origin);
        var hovered = OverChip(tl);
        if (hovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                _phase = Phase.Paused;
            }
        }
        DrawChip(dl, tl, FontAwesomeIcon.Pause, hovered);
    }

    /// <summary>The music switch, top right of every screen that has music: it comes up with the title
    /// screen and stays put through the countdown and the run, so it is always in the same place. Hand
    /// hit-tested for the same reason the pause chip is.</summary>
    private void DrawMuteChip(ImDrawListPtr dl, Vector2 origin, Vector2 size)
    {
        var tl = MuteChipTL(origin, size);
        var hovered = OverChip(tl);
        if (hovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                var muted = !_host.BgmMuted;
                _host.BgmMuted = muted;
                MuteChanged?.Invoke(muted);
                if (!muted && _bgmTrack is { } track)
                {
                    _host.StartGameBgm(track);
                }
            }
        }
        DrawChip(dl, tl, _host.BgmMuted ? FontAwesomeIcon.VolumeMute : FontAwesomeIcon.VolumeDown, hovered);

        // The same loop the app's own chip carries, so the bar under either one moves the other.
        var barMuted = _host.BgmMuted;
        var volume = _host.BgmVolume;
        if (VolumeBar.Draw("aetherlingGamesBgm", dl, tl, new Vector2(ChipSide, ChipSide), ref barMuted, ref volume,
            Look.U32(Look.CrystalPale, 0.9f), Look.U32(new Vector4(0f, 0f, 0f, 0.45f)),
            Look.U32(Look.CrystalPale, 0.95f), UiScale.S))
        {
            _host.BgmVolume = volume;
            VolumeChanged?.Invoke(volume);
            if (barMuted != _host.BgmMuted)
            {
                _host.BgmMuted = barMuted;
                MuteChanged?.Invoke(barMuted);
                if (!barMuted && _bgmTrack is { } resumed)
                {
                    _host.StartGameBgm(resumed);
                }
            }
        }
    }

    private static void DrawChip(ImDrawListPtr dl, Vector2 tl, FontAwesomeIcon icon, bool hovered)
    {
        var centre = tl + new Vector2(ChipSide * 0.5f, ChipSide * 0.5f);
        dl.AddCircleFilled(centre, ChipSide * 0.5f, Look.U32(new Vector4(0f, 0f, 0f, hovered ? 0.5f : 0.32f)), 24);
        IconDraw.AddCentered(dl, icon, Px(11f), centre, Look.U32(Look.CrystalPale, 0.9f));
    }

    private void DrawCountdown(OsAppContext ctx, ImDrawListPtr dl, Vector2 origin, Vector2 size, float dt)
    {
        _countdown -= dt;
        if (_countdown <= 0f)
        {
            _phase = Phase.Playing;
            return;
        }
        var step = (int)MathF.Ceiling(_countdown / (CountdownSeconds / 3f));
        var text = _countdown <= 0.4f ? ctx.Localize("os.aetherling_game_go") : step.ToString();
        var within = 1f - ((_countdown % (CountdownSeconds / 3f)) / (CountdownSeconds / 3f));
        var scale = ctx.ReduceMotion ? 2.2f : 2.6f - (0.5f * Look.EaseOut(within));
        var centreX = origin.X + (size.X * 0.5f);
        var y = origin.Y + (size.Y * 0.26f);

        // The block sits on its own dim panel: over a bright cloud the bare text was unreadable, and the
        // how-to is the one thing a first run must be able to read.
        var wrap = size.X - Px(72f);
        var hint = _active is { } game ? ctx.Localize(HintKey(game.Id)) : string.Empty;
        var hintH = hint.Length > 0 ? Look.WrappedHeight(hint, wrap, 0.95f) : 0f;
        var panelTL = new Vector2(origin.X + Px(22f), y - Px(16f));
        var panelBR = new Vector2(origin.X + size.X - Px(22f), y + Px(118f) + hintH);
        dl.AddRectFilled(panelTL, panelBR, Look.U32(new Vector4(0.01f, 0.015f, 0.03f, 0.66f)), Px(16f));
        dl.AddRect(panelTL, panelBR, Look.U32(new Vector4(1f, 1f, 1f, 0.1f)), Px(16f),
            ImDrawFlags.RoundCornersAll, Px(1f));

        Look.GlowText(dl, text, centreX, y, Look.U32(Look.CrystalPale), scale, Look.Crystal, 0.7f);
        Look.Centred(dl, ctx.Localize("os.aetherling_game_ready"), centreX, y + Px(78f), Look.U32(Look.Body));
        if (hint.Length > 0)
        {
            Look.CentredWrapped(dl, hint, centreX, y + Px(104f), wrap, Look.U32(Look.CrystalPale), 0.95f);
        }
    }

    private void DrawPausedOverlay(OsAppContext ctx, ImDrawListPtr dl, Vector2 origin, Vector2 size)
    {
        dl.AddRectFilled(origin, origin + size, Look.U32(new Vector4(0f, 0f, 0f, 0.55f)));
        var centreX = origin.X + (size.X * 0.5f);
        var y = origin.Y + (size.Y * 0.3f);
        Look.GlowText(dl, ctx.Localize("os.aetherling_game_paused"), centreX, y, Look.U32(Look.CrystalPale),
            1.4f, Look.Crystal, 0.5f);

        y += Px(70f);
        if (DrawSoftButton(ctx, dl, "##gameResume", ctx.Localize("os.aetherling_game_resume"), centreX, y, true))
        {
            _phase = Phase.Playing;
        }
        y += Px(52f);
        if (DrawSoftButton(ctx, dl, "##gameQuit", ctx.Localize("os.aetherling_game_quit"), centreX, y, false))
        {
            Finish();
            _phase = Phase.Results;
        }
    }

    private void DrawResults(OsAppContext ctx, ImDrawListPtr dl, Vector2 origin, Vector2 size)
    {
        var centreX = origin.X + (size.X * 0.5f);
        var y = origin.Y + (size.Y * 0.2f);

        Look.GlowText(dl, ctx.Localize("os.aetherling_game_done"), centreX, y, Look.U32(Look.CrystalPale),
            1.5f, Look.Crystal, 0.6f);
        y += Px(58f);
        Look.GlowText(dl, _lastScore.ToString("N0"), centreX, y, Look.U32(Look.Body), 2.1f,
            _newBest ? Look.Spark : Look.Crystal, _newBest ? 0.8f : 0.4f);
        y += Px(66f);
        if (_newBest)
        {
            Look.Pill(dl, ctx.Localize("os.aetherling_game_newbest"), centreX, y, Look.Spark, 1f);
            y += Px(46f);
        }
        else if (_active is { } game && _bests.GetValueOrDefault(game.Id) is > 0 and var best)
        {
            Look.Centred(dl, string.Format(ctx.Localize("os.aetherling_game_best"), best.ToString("N0")),
                centreX, y, Look.U32(Look.Whisper));
            y += Px(40f);
        }

        if (DrawSoftButton(ctx, dl, "##gameAgain", ctx.Localize("os.aetherling_game_again"), centreX, y, true))
        {
            Start(_active!);
        }
        y += Px(52f);
        if (DrawSoftButton(ctx, dl, "##gameBoards", ctx.Localize("os.arcade_leaderboard"), centreX, y, false))
        {
            _boardGame = _active!.Id;
            _boardReturn = Phase.Results;
            _phase = Phase.Leaderboard;
        }
        y += Px(52f);
        if (DrawSoftButton(ctx, dl, "##gameToHub", ctx.Localize("os.aetherling_game_tohub"), centreX, y, false))
        {
            _phase = Phase.Select;
            _active = null;
        }
    }

    private void DrawLeaderboard(OsAppContext ctx, ImDrawListPtr dl, Vector2 origin, Vector2 size)
    {
        Look.Backdrop(dl, ctx.Theme, origin, size);
        var name = _core?.PetName ?? AetherlingLimits.DefaultName;
        var y = PetPageUi.HeaderWithBack(ctx, dl, origin, name, ctx.Localize(NameKey(_boardGame)), () =>
        {
            _phase = _active is not null && _boardReturn is Phase.Title or Phase.Results
                ? _boardReturn
                : Phase.Select;
        });
        var pad = Px(PadX);
        _leaderboard.Draw(ctx, dl, new Vector2(origin.X + pad, y),
            new Vector2(size.X - (pad * 2f), origin.Y + size.Y - y - Px(12f)), _boardGame);
    }

    private static bool DrawSoftButton(OsAppContext ctx, ImDrawListPtr dl, string id, string label,
        float centreX, float y, bool primary)
    {
        var textW = ImGui.CalcTextSize(label).X;
        var w = MathF.Max(Px(150f), textW + Px(44f));
        var h = ImGui.GetTextLineHeight() + Px(18f);
        var tl = new Vector2(centreX - (w * 0.5f), y);
        ImGui.SetCursorScreenPos(tl);
        var clicked = ImGui.InvisibleButton(id, new Vector2(w, h));
        HandOnHover();
        var hovered = ImGui.IsItemHovered();
        var radius = h * 0.5f;
        dl.AddRectFilled(tl, tl + new Vector2(w, h), Look.U32(primary
            ? Look.Crystal with { W = hovered ? 0.4f : 0.28f }
            : new Vector4(1f, 1f, 1f, hovered ? 0.14f : 0.08f)), radius);
        dl.AddRect(tl, tl + new Vector2(w, h),
            Look.U32(primary ? Look.Crystal : Look.Whisper, hovered ? 0.8f : 0.45f), radius,
            ImDrawFlags.None, 1.2f);
        Look.Centred(dl, label, centreX, tl.Y + Px(9f), Look.U32(primary ? Look.CrystalPale : Look.Body));
        return clicked;
    }
}
