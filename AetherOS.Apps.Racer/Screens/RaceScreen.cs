using System;
using System.Collections.Generic;
using System.Numerics;
using AetherLove.Shared.Racing;
using AetherOS.PetKit.Engine;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Racer.Screens;

/// <summary>The stage: counts down to the server's gun, replays the resolved race from its inputs, and
/// hands over to the result scene. The sim is stepped locally (it is deterministic, so every screen in a
/// party shows the same race), but the server's placements are the record and win any disagreement.</summary>
internal sealed class RaceScreen(IRacerHost host, Action back, Func<bool> muted, Action toggleMute, Func<float> volume, Action<float> setVolume)
{
    private enum Phase
    {
        Parade,
        Running,
        Result,
    }

    private LumiRaceStartResultDto? _result;
    private AetherRaceLive.Race? _race;
    private PetRuntime[] _pets = [];
    private float[] _prevS = [];
    private float[] _prevLat = [];
    private float[] _prevGait = [];
    private TimeSpan _serverOffset;
    private Phase _phase;
    private bool _countdownPlayed;
    private int _nextEvent;
    private readonly List<(string Text, float Age)> _lines = [];
    private readonly Rendering.WeatherFx _weather = new();
    private readonly Rendering.RaceDressing _dress = new();
    private PackRipOverlay? _pack;
    private bool _skipped;
    private string? _courseKey;

    // Eye in world bounds, zoom in screen px per bound, heading in radians (track-up).
    private bool _camInit;
    private Vector2 _camPos;
    private float _camZoom;
    private float _camHeading;
    private bool _camTrackUp;
    private bool _finishing;

    /// <summary>The run-in shot's zoom once the winner is home. Monotone: seeded from the shot the
    /// camera arrived on and only ever pulled back, never returned.</summary>
    private float _tapeZoom;

    /// <summary>Where the progress rail's left end sits, in bounds along the course. Only ever
    /// advances, so the window can close on the tape but never re-open.</summary>
    private float _railStart;
    private int _lockIdx;
    private float _dwellT;
    private bool _dwellLeaders;
    private float _paradeT;
    private float _paradeBudget;

    /// <summary>How far into the tick after the drawn one the clock is. The field is drawn a whole
    /// tick behind and interpolated across it, never extrapolated.</summary>
    private float _frac;

    /// <summary>Sized once and reused: the draw path allocates nothing.</summary>
    private Vector2[] _screens = [];
    private int[] _order = [];
    private int[] _nameOrder = [];
    private readonly List<Vector2> _nameTaken = [];

    /// <summary>This frame's camera. Fields rather than arguments because the sky's strike
    /// callbacks are bound once and read them live.</summary>
    private Rendering.StageCam _cam;
    private float _camS;
    private AetherRaceLive.Race? _siteRace;

    private string _section = string.Empty;
    private float _sectionAge;
    private float _podiumAge;
    private float[] _crowdAt = [];
    private int[] _crowdTurn = [];
    private float[] _crowdGap = [];
    private float[] _voice = [];
    private readonly Spark[] _sparks = new Spark[40];
    private bool _banged;
    private bool _stampLanded;
    private CardFlipOverlay? _flip;

    /// <summary>countdown.ogg's three hits, measured off the file: 0.06 s, 1.06 s and 2.06 s, the last
    /// one landing on the gun. The lead is when playback starts; the offset is where the first hit sits
    /// inside it, and forgetting to subtract it puts every lamp a whole beat late.</summary>
    private const float CountdownLead = 2.06f;
    private const float CountdownFirstHit = 0.06f;

    /// <summary>The parade's course card, then its rest and travel per runner.</summary>
    private const float ParadeTitleSeconds = 1.6f;
    private const float ParadeHold = 0.52f;
    private const float ParadeTravel = 0.44f;

    /// <summary>How long before the tape the camera locks on the leader, judged on the tape's own
    /// clock so it means the same at any pace.</summary>
    private const float FinishLockSeconds = 5f;

    /// <summary>Where the finish line sits down the stage once the winner is home.</summary>
    private const float TapeTopFrac = 0.16f;

    /// <summary>The widest the run-in shot will open, in bounds.</summary>
    private const float TapeMaxSpan = 40f;

    /// <summary>Room left behind the last runner so it is never on the stage edge.</summary>
    private const float TapeAir = 6f;

    /// <summary>The tightest the run-in shot goes, so a bunched finish does not fill the stage with
    /// one creature.</summary>
    private const float TapeMinSpan = 18f;

    private const float TapeRate = 2f;

    /// <summary>The narrowest window the rail will close to, in bounds. Closing all the way onto a
    /// bunched field makes every pip swing the width of the stage on a single stride.</summary>
    private const float RailMinSpan = 35f;

    /// <summary>Room kept behind the last runner, so the back marker is never on the rail's end cap.</summary>
    private const float RailAir = 8f;

    private const float RailRate = 1.6f;

    /// <summary>The ribbon's share of the stage at full zoom.</summary>
    private const float TrackWidthFrac = 0.7f;

    /// <summary>What a north-up camera pays to hold a road on the diagonal; track-up pays nothing.</summary>
    private const float FlatDiagonalAllowance = 2.2f;

    /// <summary>Divide guard. Below every authored width, or it would clamp the Duskwind's ford
    /// instead of letting it funnel.</summary>
    private const float MinRoadBounds = 2f;

    /// <summary>The pull-back: the share of the fit axis a group's span gets, and the widest span
    /// the camera will serve before the rail takes over.</summary>
    private const float FitFrac = 0.72f;
    private const float MaxFitBounds = 30f;

    /// <summary>Where the field sits. Track-up has an ahead, so the eye sits low.</summary>
    private const float TrackUpPivotY = 0.62f;
    private const float FlatPivotY = 0.52f;

    /// <summary>A focus change moving the heading this far is cut, not eased: a dwell flip
    /// teleports the focus by a group gap, and easing that reads as the stage spinning.</summary>
    private const float CutHeadingDelta = 0.35f;

    /// <summary>The most the stage may turn in a second, about 92 degrees. A net for what the cut
    /// sites miss, not a look dial: the road itself asks under 30.</summary>
    private const float MaxCamTurn = 1.6f;

    /// <summary>The longest frame the stage integrates, so a hitch cannot spin it.</summary>
    private const float MaxFrameSeconds = 1f / 20f;

    /// <summary>Ribbon furniture spacing, in bounds. Tested against the walk's OWN step: a
    /// hardcoded window catches two consecutive rows and draws every post twice.</summary>
    private const float PostSpacing = 50f;
    private const float ChevronSpacing = 12f;

    /// <summary>How far along the track a strike probes for road. Only the visible stretch can
    /// matter.</summary>
    private const float StrikeProbeSpan = 90f;

    /// <summary>The stage's ground. It must stay the colour the dressing solves its wash alphas
    /// against, or every course reads as this instead of as its element.</summary>
    private static readonly uint SceneInk = Rendering.RaceDressing.NightInk;

    /// <summary>The ripple ring: radius as a share of a bound, its squash onto the ground plane,
    /// and peak alpha. The ring is screen-space, so it never lies down when the track turns.</summary>
    private const float RippleRadiusBounds = 0.3f;
    private const float RippleSquash = 0.45f;
    private const float RippleAlpha = 0.34f;

    /// <summary>The section banner's slide, hold and fade.</summary>
    private const float SectionSlideSeconds = 0.35f;
    private const float SectionHoldSeconds = 2.4f;
    private const float SectionFadeSeconds = 0.6f;

    /// <summary>How long the course's track takes to leave once the race is over. Long enough that
    /// the music is still under the placings as they land and gone by the time anybody reads them.</summary>
    private const float ResultFadeSeconds = 5f;

    /// <summary>The stamp's own ink: it is a rubber stamp, so it stays red whatever the card says.</summary>
    private const uint StampInk = 0xFF1E14DEu;

    /// <summary>When the stamp drops in after the page opens, and how long it falls.</summary>
    private const float StampDelay = 2f;
    private const float StampFall = 0.30f;

    /// <summary>How loud the stamp lands.</summary>
    private const float StampThudLevel = 0.22f;

    /// <summary>The steps, in the flag's order, and the ink a number reads in on each.</summary>
    private static readonly Vector4[] PodiumInk =
    [
        new(0.68f, 0.11f, 0.16f, 0.95f),
        new(0.93f, 0.93f, 0.95f, 0.95f),
        new(0.13f, 0.27f, 0.55f, 0.95f),
    ];

    private static readonly Vector4[] PodiumMarkInk =
    [
        new(1f, 1f, 1f, 0.9f),
        new(0.16f, 0.16f, 0.2f, 0.9f),
        new(1f, 1f, 1f, 0.9f),
    ];

    /// <summary>The podium's cheer: the stagger down the steps, and the winner's repeat.</summary>
    private const float PodiumCheerDelay = 0.45f;
    private const float PodiumCheerLoop = 3.6f;

    /// <summary>What each place does with itself while the standings are up. The winner works through
    /// its whole set; the losers are at a party too, and only the last one sulks.</summary>
    private static readonly string[] WinnerCheers = ["cheer", "vpose", "huzzah", "dance", "psych"];
    private static readonly string[] RunnerUpCheers = ["cheer", "beam", "huzzah", "grin", "wave"];
    private static readonly string[] AlsoRanCheers = ["wave", "laugh", "sway", "chuckle", "smile", "beam"];
    private static readonly string[] LastCheers = ["shrug", "pout", "hmm", "chuckle"];

    /// <summary>Six voices far enough apart to be told apart, dealt to the field by the race's own seed.</summary>
    private static readonly float[] VoiceLadder = [0.76f, 0.88f, 0.99f, 1.11f, 1.24f, 1.40f];

    /// <summary>How often a place speaks up, and how loud. The podium is nearer the front.</summary>
    private static readonly float[] CrowdGapByRank = [PodiumCheerLoop, 4.3f, 4.7f, 5.2f, 5.6f, 6.0f];
    private static readonly float[] CrowdLevelByRank = [0.5f, 0.4f, 0.36f, 0.28f, 0.26f, 0.24f];

    /// <summary>The stride's push-off, as a fraction of the drawn body. Moves the DRAWN point:
    /// <c>PetPose.Offset</c> is 256-cell space, scaled by display size again, and seats worn items.</summary>
    private const float BounceFrac = 0.055f;

    public void Begin(LumiRaceStartResultDto result)
    {
        _result = result;
        var dto = result.Race;
        _serverOffset = dto.ServerNowUtc - DateTimeOffset.UtcNow;
        _phase = Phase.Parade;
        _countdownPlayed = false;
        _nextEvent = 0;
        _skipped = false;
        _lines.Clear();
        _pack = null;
        _flip = null;
        _camInit = false;
        _camHeading = 0f;
        _camTrackUp = false;
        _finishing = false;
        _tapeZoom = 0f;
        _railStart = 0f;
        _lockIdx = 0;
        _dwellT = 0f;
        _dwellLeaders = false;
        _frac = 0f;
        _paradeT = 0f;
        _paradeBudget = 0f;
        _section = string.Empty;
        _sectionAge = 0f;
        _podiumAge = 0f;
        Array.Clear(_sparks);
        _banged = false;
        _stampLanded = false;

        var course = FindCourse(dto.CourseKey);
        var field = new RaceRunner[dto.Field.Length];
        foreach (var entry in dto.Field)
        {
            var element = RacingElements.NameOf((AetherLove.Shared.Aetherling.AetherlingElement)entry.Element);
            field[entry.Slot] = new RaceRunner(
                entry.Name,
                new StatBlock(entry.Name, element, entry.Speed, entry.Power, entry.Stamina, entry.Focus, entry.Heart),
                entry.Slot == dto.PlayerSlot);
        }
        _race = AetherRaceLive.CreateRace(dto.Seed, course, field, dto.WeatherKey);
        _dress.Generate(dto.Seed, _race.Track, course);
        _prevS = new float[field.Length];
        _prevLat = new float[field.Length];
        _prevGait = new float[field.Length];
        _screens = new Vector2[field.Length];
        _order = new int[field.Length];
        _nameOrder = new int[field.Length];

        _pets = new PetRuntime[dto.Field.Length];
        foreach (var entry in dto.Field)
        {
            var pet = new PetRuntime();
            pet.SetPhaseSeed($"{entry.Name}#{entry.Slot}");
            pet.EnsureLoaded(host.PetAssetRoot, PetState.FormFolderForStage(entry.Stage, entry.Shell));
            pet.ApplyDraftLook(entry.Palette, entry.Accessories, string.Empty, []);
            _pets[entry.Slot] = pet;
        }

        // A bolt outlives several frames of panning, so its site is pinned to the ground rather
        // than to the screen. Bound once; they read the camera field.
        _weather.SiteToScreen = world => _cam.ToScreen(world);
        _weather.SiteFromScreen = screen => _cam.ToWorld(screen);
        _weather.StrikeSiteClear = (at, clearance) =>
            _siteRace is { } r && RoadClear(r, _camS, in _cam, at, clearance);

        _weather.Begin(_race.Weather.Element, dto.Seed, _dress.PrevailingLean);
        _courseKey = dto.CourseKey;
        if (!muted())
        {
            host.StartCourseBgm(dto.CourseKey);
        }
    }

    /// <summary>Restarts the course track after an unmute mid-race.</summary>
    public void ResumeBgm()
    {
        if (_courseKey is { } key)
        {
            host.StartCourseBgm(key);
        }
    }

    public void OnHidden()
    {
        host.StopBgm();
    }

    private DateTimeOffset ServerNow => DateTimeOffset.UtcNow + _serverOffset;

    private static AetherRaceLive.CourseDef FindCourse(string key)
    {
        foreach (var course in AetherRaceLive.Courses)
        {
            if (course.Key == key)
            {
                return course;
            }
        }
        return AetherRaceLive.Courses[0];
    }

    public void Draw(OsAppContext ctx)
    {
        if (_result is not { } result || _race is not { } race)
        {
            back();
            return;
        }

        var avail = ImGui.GetContentRegionAvail();
        using var body = ImRaii.Child("##racerStage", avail, false,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        if (!body)
        {
            return;
        }

        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var size = ImGui.GetContentRegionAvail();
        dl.AddRectFilled(origin, origin + size, SceneInk);

        var untilStart = (float)(result.Race.StartAtUtc - ServerNow).TotalSeconds;

        switch (_phase)
        {
            case Phase.Parade:
                DrawParade(ctx, dl, origin, size, result, race, untilStart);
                RacerChrome.DrawMuteChip(ctx, muted(), toggleMute, volume(), setVolume);
                if (untilStart <= 0f)
                {
                    _phase = Phase.Running;
                }
                break;
            case Phase.Running:
                if (!_banged)
                {
                    _banged = true;
                    StartBang(origin, size, Rendering.ElementFx.For(race.Course.Terrain).Tint);
                }
                StepToClock(race, -untilStart);
                DrawStage(ctx, dl, origin, size, result, race);
                DrawSkipChip(ctx, dl, origin, size);
                RacerChrome.DrawMuteChip(ctx, muted(), toggleMute, volume(), setVolume);
                if (race.Done && (-untilStart) > race.WinnerTime + 3.5f)
                {
                    EnterResult();
                }
                break;
            default:
                DrawResult(ctx, dl, origin, size, result, race);
                RacerChrome.DrawMuteChip(ctx, muted(), toggleMute, volume(), setVolume);
                break;
        }
    }

    private void StepToClock(AetherRaceLive.Race race, float raceSeconds)
    {
        var target = _skipped
            ? int.MaxValue
            : (int)(raceSeconds / AetherRaceLive.Dials.Dt);
        var guard = 0;
        while (!race.Done && race.Tick < target && guard < 2400)
        {
            for (var i = 0; i < race.Runners.Count; i++)
            {
                _prevS[i] = race.Runners[i].S;
                _prevLat[i] = race.Runners[i].Lat;
                _prevGait[i] = race.Runners[i].Gait;
            }
            race.Step();
            guard++;
        }

        // The drawn field sits one whole tick behind the clock and interpolates across it.
        _frac = _skipped ? 1f : Math.Clamp((raceSeconds / AetherRaceLive.Dials.Dt) - race.Tick, 0f, 1f);

        if (_skipped && race.Done)
        {
            EnterResult();
        }
    }

    private void EnterResult()
    {
        _phase = Phase.Result;
        _weather.HoldTransients = true;
        _podiumAge = 0f;
        host.FadeOutBgm(ResultFadeSeconds);
        if (_result is { } result)
        {
            SeatCrowd(result.Race);
        }
    }

    private void DrawParade(OsAppContext ctx, ImDrawListPtr dl, Vector2 origin, Vector2 size,
        LumiRaceStartResultDto result, AetherRaceLive.Race race, float untilStart)
    {
        var courseName = ctx.Localize($"os.racer_course_{result.Race.CourseKey}");
        var terrain = race.Course.Terrain.Length > 0
            ? ctx.Localize($"os.racer_elem_{race.Course.Terrain}")
            : ctx.Localize("os.racer_elem_none");
        var weather = ctx.Localize($"os.racer_wx_{result.Race.WeatherKey}");
        var category = ctx.Localize($"os.racer_cat_{race.Course.Category.ToString().ToLowerInvariant()}");

        // Paced to whatever countdown is left, so a late joiner still sees the whole field.
        if (_paradeBudget <= 0f)
        {
            _paradeBudget = MathF.Max(1f, untilStart);
        }
        var wanted = ParadeTitleSeconds + (_pets.Length * ParadeHold) + ((_pets.Length - 1) * ParadeTravel);
        var rate = wanted > _paradeBudget ? wanted / _paradeBudget : 1f;
        _paradeT += ImGui.GetIO().DeltaTime * rate;
        var walked = _paradeT - ParadeTitleSeconds;

        if (walked < 0f)
        {
            var mid = origin + new Vector2(size.X * 0.5f, size.Y * 0.42f);
            using (ctx.TitleFont?.Push())
            {
                CenteredAt(dl, origin, size.X, mid.Y - origin.Y, courseName, 0xFFFFFFFF);
            }
            CenteredAt(dl, origin, size.X, mid.Y - origin.Y + Px(34),
                $"{category} · {terrain} · {weather}", 0xFFB4AACC);
        }
        else
        {
            using (ctx.TitleFont?.Push())
            {
                CenteredAt(dl, origin, size.X, Px(30), courseName, 0xFFFFFFFF);
            }
            CenteredAt(dl, origin, size.X, Px(64), $"{category} · {terrain} · {weather}", 0xFFB4AACC);
            DrawLineup(ctx, dl, origin, size, result.Race, walked);
        }

        if (!_countdownPlayed && untilStart <= CountdownLead)
        {
            _countdownPlayed = true;
            PlaySfx(ctx, "countdown.ogg", 1f);
        }

        DrawStartLights(ctx, dl, origin, size, untilStart);
    }

    /// <summary>The gantry over the line. Its three lamps come up on countdown.ogg's own three hits,
    /// so the sound is the clock and the lights only show it.</summary>
    private void DrawStartLights(OsAppContext ctx, ImDrawListPtr dl, Vector2 origin, Vector2 size, float untilStart)
    {
        var centreX = origin.X + (size.X * 0.5f);
        var y = origin.Y + size.Y - Px(96);
        var pitch = Px(40);
        var lampR = Px(13);
        var barHalf = (pitch * 1.5f) + Px(14);
        var barTop = y - Px(24);
        var barBottom = y + Px(24);

        dl.AddRectFilled(new Vector2(centreX - barHalf, barTop), new Vector2(centreX + barHalf, barBottom),
            0xE01B1628u, Px(12));
        dl.AddRect(new Vector2(centreX - barHalf, barTop), new Vector2(centreX + barHalf, barBottom),
            0x5AFFFFFFu, Px(12), ImDrawFlags.RoundCornersAll, Px(1.4f));
        dl.AddRectFilled(new Vector2(centreX - barHalf + Px(10), barTop + Px(3)),
            new Vector2(centreX + barHalf - Px(10), barTop + Px(7)), 0x24FFFFFFu, Px(3));

        var live = untilStart <= 0f;
        for (var i = 0; i < 3; i++)
        {
            var at = new Vector2(centreX + ((i - 1) * pitch), y);
            var since = CountdownLead - CountdownFirstHit - i - untilStart;
            var lit = since >= 0f;
            var pop = lit && !ctx.ReduceMotion ? 1f + (0.28f * MathF.Exp(-since * 9f)) : 1f;
            var face = live
                ? new Vector4(0.30f, 0.85f, 0.39f, 1f)
                : new Vector4(0.85f, 0.20f, 0.22f, 1f);

            dl.AddCircleFilled(at, lampR + Px(4), 0xFF120E1Cu, 28);
            if (lit)
            {
                RacerChrome.Halo(dl, at, lampR * 3.1f * pop, face, 0.55f);
                dl.AddCircleFilled(at, lampR * pop, ImGui.ColorConvertFloat4ToU32(face), 28);
                dl.AddCircleFilled(at - new Vector2(0f, lampR * 0.34f), lampR * 0.34f * pop, 0x8CFFFFFFu, 20);
                if (since < 0.4f && !ctx.ReduceMotion)
                {
                    var ring = since / 0.4f;
                    dl.AddCircle(at, lampR * (1f + (ring * 2.6f)),
                        ImGui.ColorConvertFloat4ToU32(face with { W = 0.7f * (1f - ring) }), 28, Px(2f));
                }
            }
            else
            {
                dl.AddCircleFilled(at, lampR, 0xFF2A2340u, 28);
                dl.AddCircleFilled(at - new Vector2(0f, lampR * 0.34f), lampR * 0.30f, 0x1AFFFFFFu, 20);
            }
            dl.AddCircle(at, lampR + Px(4), 0x73FFFFFFu, 28, Px(1.4f));
        }

        if (live && !ctx.ReduceMotion)
        {
            var flash = MathF.Exp(untilStart * 6f);
            if (flash > 0.02f)
            {
                RacerChrome.Halo(dl, new Vector2(centreX, y), barHalf * 2.2f * flash,
                    new Vector4(0.42f, 1f, 0.52f, 1f), 0.5f * flash, 4);
            }
        }
    }

    /// <summary>Where the parade camera is along the line, in runner slots. Smoothstepped on the
    /// travel leg so it settles on each runner.</summary>
    private static float ParadeSlot(float t, int n)
    {
        if (t <= 0f)
        {
            return 0f;
        }
        var cycle = ParadeHold + ParadeTravel;
        var i = (int)(t / cycle);
        if (i >= n - 1)
        {
            return n - 1;
        }
        var local = t - (i * cycle);
        if (local <= ParadeHold)
        {
            return i;
        }
        var p = Math.Clamp((local - ParadeHold) / ParadeTravel, 0f, 1f);
        return i + (p * p * (3f - (2f * p)));
    }

    /// <summary>The line-up as a camera track down the field, one runner at a time nearly filling
    /// the stage, walked from the outside post inward.</summary>
    private void DrawLineup(OsAppContext ctx, ImDrawListPtr dl, Vector2 origin, Vector2 size,
        LumiRaceDto dto, float walked)
    {
        var n = _pets.Length;
        if (n == 0 || _race is not { } race)
        {
            return;
        }

        // Outside post first, tracking inward: the order a real course walks its field.
        var order = new int[n];
        for (var i = 0; i < n; i++)
        {
            order[i] = i;
        }
        Array.Sort(order, (a, b) => race.Runners[b].Post.CompareTo(race.Runners[a].Post));
        var slotAt = ParadeSlot(walked, n);
        var petSize = MathF.Min(size.X * 0.6f, size.Y * 0.44f);
        var spacing = size.X * 0.94f;
        var centreX = origin.X + (size.X * 0.5f);
        var feetY = origin.Y + (size.Y * 0.64f);

        for (var i = 0; i < n; i++)
        {
            var offset = (i - slotAt) * spacing;
            if (MathF.Abs(offset) > size.X * 1.2f)
            {
                continue;
            }

            var slot = order[i];
            var pet = _pets[slot];
            pet.Tick(ctx.ReduceMotion);

            // The one being looked at stands full size and lit.
            var focus = 1f - Math.Clamp(MathF.Abs(i - slotAt), 0f, 1f);
            var scale = 0.84f + (0.16f * focus);
            var dim = 0.45f + (0.55f * focus);
            var bottom = new Vector2(centreX + offset, feetY);
            var drawn = petSize * scale;

            dl.AddCircleFilled(bottom, drawn * 0.3f,
                ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.35f * dim)), 24);
            DrawGroundGlow(dl, bottom, drawn, dto, slot);
            pet.Draw(dl, ctx.Capabilities.Textures, bottom, drawn, pet.Pose, props: false);

            var name = dto.Field[slot].Name;
            var nameSize = ImGui.CalcTextSize(name);
            var ink = slot == dto.PlayerSlot
                ? new Vector4(1f, 1f, 1f, dim)
                : new Vector4(0.61f, 0.57f, 0.72f, dim);
            dl.AddText(new Vector2(bottom.X - (nameSize.X * 0.5f), bottom.Y + Px(10)),
                ImGui.ColorConvertFloat4ToU32(ink), name);
        }
    }


    /// <summary>The standings crowd. Every runner keeps its own clock and its own voice, dealt from
    /// the race seed. The choreography refuses itself mid-emote, so a repeat is only an ask on a timer.</summary>
    private void Crowd(OsAppContext ctx, PetRuntime pet, int slot, int rank, int field)
    {
        if (slot >= _crowdAt.Length || _podiumAge < _crowdAt[slot])
        {
            return;
        }

        var pool = rank switch
        {
            0 => WinnerCheers,
            1 or 2 => RunnerUpCheers,
            _ when rank >= field - 1 => LastCheers,
            _ => AlsoRanCheers,
        };
        var turn = _crowdTurn[slot];
        if (!ctx.ReduceMotion && EmoteChoreographies.Find(pool[(turn + slot) % pool.Length]) is { } def)
        {
            pet.PlayEmote(def);
        }

        var band = Math.Clamp(rank, 0, CrowdLevelByRank.Length - 1);
        PlaySfx(ctx, $"aetherling_chirp_{((turn + slot) % 7) + 1:00}.ogg", CrowdLevelByRank[band], _voice[slot]);

        _crowdTurn[slot] = turn + 1;
        _crowdAt[slot] = _podiumAge + _crowdGap[slot];
    }

    /// <summary>Deals the crowd its voices and its first moments. Everyone starts on a beat of their own,
    /// winner first, so the page opens with a cheer rather than with all six at once.</summary>
    private void SeatCrowd(LumiRaceDto dto)
    {
        var field = dto.Field.Length;
        _crowdAt = new float[field];
        _crowdTurn = new int[field];
        _crowdGap = new float[field];
        _voice = new float[field];

        var rng = new Random(dto.Seed);
        var ladder = new List<float>(VoiceLadder);
        for (var i = 0; i < field; i++)
        {
            if (ladder.Count == 0)
            {
                ladder.AddRange(VoiceLadder);
            }
            var pick = rng.Next(ladder.Count);
            _voice[i] = ladder[pick];
            ladder.RemoveAt(pick);
        }

        for (var rank = 0; rank < dto.Placements.Length; rank++)
        {
            var slot = dto.Placements[rank];
            if (slot < 0 || slot >= field)
            {
                continue;
            }
            var band = Math.Clamp(rank, 0, CrowdGapByRank.Length - 1);
            _crowdGap[slot] = CrowdGapByRank[band] * (0.86f + ((float)rng.NextDouble() * 0.34f));
            _crowdAt[slot] = (PodiumCheerDelay * rank) + ((float)rng.NextDouble() * 0.5f);
        }
    }

    /// <summary>The zone, announced once as the camera's focus crosses into it: slide, hold,
    /// fade.</summary>
    private void DrawSectionBanner(OsAppContext ctx, ImDrawListPtr dl, Vector2 origin, Vector2 size,
        AetherRaceLive.Race race, float camS, float dt)
    {
        var section = race.Track.At(Math.Clamp(camS, 0f, race.Track.Length)).Section;
        if (section.Length > 0 && section != _section)
        {
            _section = section;
            _sectionAge = 0f;
        }

        if (_section.Length == 0)
        {
            return;
        }

        _sectionAge += dt;
        var total = SectionSlideSeconds + SectionHoldSeconds + SectionFadeSeconds;
        if (_sectionAge >= total)
        {
            return;
        }

        var slide = MathF.Min(1f, _sectionAge / SectionSlideSeconds);
        var alpha = _sectionAge <= SectionSlideSeconds + SectionHoldSeconds
            ? slide
            : 1f - ((_sectionAge - SectionSlideSeconds - SectionHoldSeconds) / SectionFadeSeconds);

        // Only the LEADING article comes off. Replacing every "the " turned "the turn at the top"
        // into a key nobody defined, and a missed key is drawn as its own name across the track.
        var name = _section.StartsWith("the ", StringComparison.Ordinal) ? _section[4..] : _section;
        var text = ctx.Localize("os.racer_sec_" + name.Replace('-', '_').Replace(' ', '_'));
        var eased = 1f - MathF.Pow(1f - slide, 3f);
        var y = origin.Y + Px(96) - (Px(14) * (1f - eased));
        RaceLabel(dl, new Vector2(origin.X + (size.X * 0.5f), y), text,
            new Vector4(1f, 1f, 1f, 1f), Math.Clamp(alpha, 0f, 1f), plate: 0.45f);
    }

    /// <summary>Is a circle of this screen radius clear of the road? Measured in world bounds: the
    /// road is a curve under a turning camera and a box around it forbids every site.</summary>
    private static bool RoadClear(AetherRaceLive.Race race, float camS, in Rendering.StageCam cam,
        Vector2 at, float clearance)
    {
        if (cam.Zoom <= 0f)
        {
            return true;
        }

        var world = cam.ToWorld(at);
        var reach = (clearance / cam.Zoom) + (race.Track.Width * 0.5f);
        var step = race.Track.Step * 8f;
        var from = MathF.Max(0f, camS - StrikeProbeSpan);
        var to = MathF.Min(race.Track.Length, camS + StrikeProbeSpan);
        for (var s = from; s <= to; s += step)
        {
            var p = race.Track.AtLerp(s);
            if (Vector2.DistanceSquared(world, new Vector2(p.X, p.Y)) < reach * reach)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>Centred text on a soft plate, with a lit top hairline: the stage's one text
    /// primitive. <paramref name="plate"/> is an alpha, never a size; zero draws the text alone.</summary>
    private static void RaceLabel(ImDrawListPtr dl, Vector2 centre, string text, Vector4 colour,
        float alpha = 1f, float plate = 0.42f)
    {
        var size = ImGui.CalcTextSize(text);
        var at = centre - (size * 0.5f);
        if (plate > 0f)
        {
            var tl = at - new Vector2(Px(10f), Px(5f));
            var br = at + size + new Vector2(Px(10f), Px(5f));
            var round = Px(9f);
            dl.AddRectFilled(tl, br, ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, plate * alpha)), round);
            dl.AddLine(new Vector2(tl.X + round, tl.Y + 0.5f), new Vector2(br.X - round, tl.Y + 0.5f),
                ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.16f * plate * alpha)), 1f);
        }

        dl.AddText(at, ImGui.ColorConvertFloat4ToU32(colour with { W = colour.W * alpha }), text);
    }

    /// <summary>Every runner's name on a plate under its feet, anchored to the GROUND so it does
    /// not ride the stride, and upright at every heading because ImGui text cannot rotate. Plates
    /// that would overlap are dropped, frontmost first, and the player sorts ahead of everyone so a
    /// ghost can never take its name.</summary>
    private void DrawRunnerNames(ImDrawListPtr dl, Vector2 origin, Vector2 size, AetherRaceLive.Race race,
        LumiRaceDto dto, float petSize)
    {
        for (var i = 0; i < _nameOrder.Length; i++)
        {
            _nameOrder[i] = i;
        }
        Array.Sort(_nameOrder, (a, b) =>
        {
            var playerA = a == dto.PlayerSlot;
            var playerB = b == dto.PlayerSlot;
            if (playerA != playerB)
            {
                return playerA ? -1 : 1;
            }
            return race.Runners[a].Place.CompareTo(race.Runners[b].Place);
        });

        _nameTaken.Clear();
        var cull = petSize * 2f;
        foreach (var idx in _nameOrder)
        {
            var feet = _screens[idx];
            if (feet.X < origin.X - cull || feet.X > origin.X + size.X + cull
                || feet.Y < origin.Y - cull || feet.Y > origin.Y + size.Y + cull)
            {
                continue;
            }

            // The PLATES are tested, not their anchors: two runners a whole body apart still put a
            // pair of long names through each other, which is what "SephShmoople" was.
            var name = dto.Field[idx].Name;
            var at = new Vector2(feet.X, feet.Y + (petSize * 0.42f));
            var half = (ImGui.CalcTextSize(name) * 0.5f) + new Vector2(Px(10f), Px(5f));
            var crowded = false;
            for (var i = 0; i + 1 < _nameTaken.Count; i += 2)
            {
                var mid = _nameTaken[i];
                var extent = _nameTaken[i + 1];
                if (MathF.Abs(mid.X - at.X) < extent.X + half.X
                    && MathF.Abs(mid.Y - at.Y) < extent.Y + half.Y)
                {
                    crowded = true;
                    break;
                }
            }
            if (crowded)
            {
                continue;
            }

            _nameTaken.Add(at);
            _nameTaken.Add(half);
            var isPlayer = idx == dto.PlayerSlot;
            var accent = isPlayer ? StageAccent(dto.Field[idx].Element) : new Vector4(1f, 1f, 1f, 0.8f);
            RaceLabel(dl, at, name, accent, isPlayer ? 1f : 0.75f, plate: 0.34f);
        }
    }

    /// <summary>A stumble made visible, driven off the engine's own recovery clock. Not
    /// <c>PetRuntime.Boop</c>, which lifts mood and blooms a flourish: this is a setback.</summary>
    private static void DrawStumble(ImDrawListPtr dl, Vector2 screen, float petSize,
        ref Vector2 feet, ref PetPose pose, float stumbleT)
    {
        var left = Math.Clamp(stumbleT / AetherRaceLive.Dials.StumbleSecs, 0f, 1f);

        // Hardest at the trip, easing out, so the recovery is what reads.
        var lurch = left * left;
        feet += new Vector2(petSize * 0.06f * lurch, petSize * 0.05f * lurch);
        pose.Scale *= new Vector2(1f + (0.10f * lurch), 1f - (0.14f * lurch));

        var puff = ImGui.ColorConvertFloat4ToU32(new Vector4(0.62f, 0.58f, 0.54f, 0.45f * lurch));
        for (var i = 0; i < 3; i++)
        {
            var spread = petSize * (0.18f + (0.16f * i)) * (1f - left);
            var centre = screen + new Vector2(petSize * (0.10f - (0.16f * i)) - spread, petSize * 0.04f);
            dl.AddCircleFilled(centre, petSize * 0.09f * (0.6f + (0.4f * lurch)), puff, 12);
        }
    }

    /// <summary>An element's colour in the stage's own palette. PetKit's accents are a different
    /// set, and mixing the two put two fires on one stage.</summary>
    private static Vector4 StageAccent(short element)
    {
        var name = RacingElements.NameOf((AetherLove.Shared.Aetherling.AetherlingElement)element);
        return Rendering.ElementFx.For(name).Tint;
    }

    private void DrawGroundGlow(ImDrawListPtr dl, Vector2 bottom, float petSize, LumiRaceDto dto, int slot)
    {
        var entry = dto.Field[slot];
        var isPlayer = slot == dto.PlayerSlot;
        if (!isPlayer && !entry.IsPartyMember)
        {
            return;
        }
        var accent = StageAccent(entry.Element);
        var centre = bottom + new Vector2(0f, Px(2));

        if (isPlayer)
        {
            StrokeEllipse(dl, centre, new Vector2(petSize * 0.372f, petSize * 0.12f),
                ImGui.ColorConvertFloat4ToU32(accent), Px(1.5f));
            return;
        }

        FillEllipse(dl, centre, new Vector2(petSize * 0.55f, petSize * 0.16f),
            ImGui.ColorConvertFloat4ToU32(accent with { W = 0.35f }));
    }

    private static void FillEllipse(ImDrawListPtr dl, Vector2 centre, Vector2 radius, uint color)
    {
        Span<Vector2> pts = stackalloc Vector2[20];
        for (var i = 0; i < pts.Length; i++)
        {
            var a = MathF.PI * 2f * i / pts.Length;
            pts[i] = centre + new Vector2(MathF.Cos(a) * radius.X, MathF.Sin(a) * radius.Y);
        }
        dl.AddConvexPolyFilled(ref pts[0], pts.Length, color);
    }

    private static void StrokeEllipse(ImDrawListPtr dl, Vector2 centre, Vector2 radius, uint color, float thickness)
    {
        Span<Vector2> pts = stackalloc Vector2[20];
        for (var i = 0; i < pts.Length; i++)
        {
            var a = MathF.PI * 2f * i / pts.Length;
            pts[i] = centre + new Vector2(MathF.Cos(a) * radius.X, MathF.Sin(a) * radius.Y);
        }
        dl.AddPolyline(ref pts[0], pts.Length, color, ImDrawFlags.Closed, thickness);
    }

    private void DrawStage(OsAppContext ctx, ImDrawListPtr dl, Vector2 origin, Vector2 size,
        LumiRaceStartResultDto result, AetherRaceLive.Race race)
    {
        var dto = result.Race;
        var dt = MathF.Min(ImGui.GetIO().DeltaTime, MaxFrameSeconds);
        var trackUp = !ctx.ReduceMotion;

        // The race's own ground. Every ground wash alpha is arithmetic against this exact colour,
        // so the running phase paints it and the parade and podium keep the app's plum.
        dl.AddRectFilled(origin, origin + size, Rendering.RaceDressing.NightInk);

        var camS = UpdateCamera(race, dto, size, dt, trackUp);
        var pivot = origin + new Vector2(size.X * 0.5f, size.Y * (trackUp ? TrackUpPivotY : FlatPivotY));
        var cam = Rendering.StageCam.From(_camPos, _camHeading, _camZoom, pivot);
        var petSize = _camZoom * 1.35f;
        _cam = cam;
        _camS = camS;
        _siteRace = race;

        DrawRibbon(dl, race, camS, origin, size, in cam);
        _dress.Draw(dl, race.Track, origin, size, camS, in cam, race.Time, ctx.ReduceMotion);
        DrawPosts(dl, race.Track, camS, origin, size, in cam);

        _weather.Update(dt, origin, size, petSize, TerrainAt(race, camS));
        _weather.DrawCast(dl, origin, origin + size, 0f);
        _weather.DrawBack(dl);
        DrawRainRipples(dl, race, camS, size, in cam);

        foreach (var idx in RunnersBackToFront(race, in cam))
        {
            var runner = race.Runners[idx];
            var screen = _screens[idx];
            var pet = _pets[idx];
            pet.Tick(ctx.ReduceMotion);
            DrawGroundGlow(dl, screen, petSize, dto, idx);

            var pose = pet.Pose;
            var feet = screen;
            if (!runner.Finished)
            {
                // Gait is ALREADY a phase in radians; scaling it by tau runs the stride 6x too fast.
                var amp = Math.Clamp(runner.V / 9f, 0f, 1f) * (runner.StumbleT > 0f ? 0.4f : 1f);
                var beat = MathF.Sin(Lerp(_prevGait[idx], runner.Gait, _frac));
                feet.Y -= MathF.Abs(beat) * amp * BounceFrac * petSize;
                pose.Scale *= new Vector2(1f + (beat * amp * 0.05f), 1f - (beat * amp * 0.05f));
                pet.DriveHands(GaitHands(beat, amp));

                if (runner.StumbleT > 0f)
                {
                    DrawStumble(dl, screen, petSize, ref feet, ref pose, runner.StumbleT);
                }
            }

            // Track-up runs the field away from the camera: no facing to express, and the
            // animator's idle-hop flip would mirror it mid-race.
            pose.FlipX = false;
            pet.Draw(dl, ctx.Capabilities.Textures, feet, petSize, pose, props: false);
        }

        DrawRunnerNames(dl, origin, size, race, dto, petSize);
        DrawSectionBanner(ctx, dl, origin, size, race, camS, dt);

        _weather.DrawFront(dl);
        DrawSparks(dl, dt);
        DrawRail(dl, origin, size, race, dto, dt);
        DrainNarration(ctx, race);
        DrawNarration(dl, origin, size);
    }

    /// <summary>Where the eye sits, how close it is and which way is up. Zoom is measured against the
    /// TRACK, not the pack, so the runners keep their size. Off the lead the focus alternates between
    /// the player's group and the leaders'; near the tape it locks on the leader. Reduce-motion holds
    /// it north-up.</summary>
    /// <returns>The arc length the camera is looking at, for the ribbon walk and the weather.</returns>
    private float UpdateCamera(AetherRaceLive.Race race, LumiRaceDto dto, Vector2 size, float dt, bool trackUp)
    {
        Vector2 WorldOf(int idx)
        {
            var runner = race.Runners[idx];
            var sample = race.Track.AtLerp(Lerp(_prevS[idx], runner.S, _frac));
            return WorldAt(in sample, Lerp(_prevLat[idx], runner.Lat, _frac));
        }

        float DrawnS(int idx) => Lerp(_prevS[idx], race.Runners[idx].S, _frac);

        // The leader the running camera rides is the leading UNFINISHED runner, so without this
        // early-out every crossing re-targets it and the view cuts down the field one finisher at a time.
        if (AnyFinished(race))
        {
            return UpdateTapeCamera(race, size, dt, trackUp, DrawnS);
        }

        var groups = AetherRaceLive.GroupRunners(race);
        var playerGroup = groups.Find(g => g.HasPlayer) ?? groups[0];
        var leadGroup = groups[0];

        var leader = -1;
        var leaderS = float.MinValue;
        var leaderV = 0f;
        foreach (var r in race.Runners)
        {
            if (!r.Finished && r.S > leaderS)
            {
                leader = r.Idx;
                leaderS = r.S;
                leaderV = r.V;
            }
        }

        if (leader >= 0)
        {
            _lockIdx = leader;
        }

        // Latches: the speed estimate would otherwise re-cross the threshold and cut twice.
        var cut = false;
        if (!_finishing && leader >= 0
            && (race.Track.Length - leaderS) / MathF.Max(leaderV, 4f) < FinishLockSeconds)
        {
            _finishing = true;
            cut = true;
        }

        _weather.HoldTransients = _finishing;

        Vector2 target;
        float focusS;
        float spanS;
        if (_finishing)
        {
            target = WorldOf(_lockIdx);
            focusS = DrawnS(_lockIdx);
            spanS = 0f;
        }
        else
        {
            AetherRaceLive.RaceGroup focus;
            if (ReferenceEquals(playerGroup, leadGroup) || groups.Count == 1)
            {
                focus = playerGroup;
                _dwellT = 0f;
                _dwellLeaders = false;
            }
            else
            {
                _dwellT += dt;
                var period = _dwellLeaders ? 3f : 6f;
                if (_dwellT >= period)
                {
                    _dwellLeaders = !_dwellLeaders;
                    _dwellT = 0f;
                    cut = true;
                }

                focus = _dwellLeaders ? leadGroup : playerGroup;
            }

            target = Vector2.Zero;
            focusS = 0f;
            foreach (var idx in focus.RunnerIdx)
            {
                target += WorldOf(idx);
                focusS += DrawnS(idx);
            }

            target /= MathF.Max(1, focus.RunnerIdx.Count);
            focusS /= MathF.Max(1, focus.RunnerIdx.Count);
            spanS = focus.SHead - focus.STail;
        }

        // A finished runner's arc length grows past the tape; unclamped it walks the ribbon off.
        focusS = Math.Clamp(focusS, 0f, race.Track.Length);
        var here = race.Track.AtLerp(focusS);

        // Track-up spends the width on the ribbon, so only the height can run out.
        var roadBounds = MathF.Max(MinRoadBounds, here.Width * (trackUp ? 1f : FlatDiagonalAllowance));
        var fitAxis = trackUp ? size.Y : MathF.Min(size.X, size.Y);
        var zoomTrack = size.X * TrackWidthFrac / roadBounds;
        var zoomFloor = fitAxis * FitFrac / MaxFitBounds;

        float targetZoom;
        if (_finishing)
        {
            // Wide enough to hold the leader-to-player gap, so the player is never off the stage.
            var gap = MathF.Abs(Math.Clamp(DrawnS(_lockIdx), 0f, race.Track.Length)
                - Math.Clamp(DrawnS(dto.PlayerSlot), 0f, race.Track.Length));
            targetZoom = Math.Clamp(fitAxis * FitFrac / MathF.Max(6f, gap + 5f), zoomFloor, zoomTrack);
        }
        else
        {
            var zoomFit = fitAxis * FitFrac / MathF.Max(6f, spanS + 5f);
            targetZoom = MathF.Max(MathF.Min(zoomTrack, zoomFit), zoomFloor);
        }

        // The engine builds every course from -PI/2.
        var targetHeading = trackUp ? (-MathF.PI / 2f) - here.Heading : 0f;
        if (trackUp != _camTrackUp)
        {
            _camTrackUp = trackUp;
            cut = true;
        }

        // Heading is never wrapped by the engine: fold the DELTA only, or the stage spins a full
        // turn at the wrap.
        var delta = targetHeading - _camHeading;
        while (delta > MathF.PI)
        {
            delta -= MathF.Tau;
        }

        while (delta < -MathF.PI)
        {
            delta += MathF.Tau;
        }

        if (!_camInit || (cut && MathF.Abs(delta) >= CutHeadingDelta))
        {
            _camPos = target;
            _camZoom = targetZoom;
            _camHeading += delta;
            _camInit = true;
            return focusS;
        }

        var ease = 1f - MathF.Exp(-(_finishing ? 6f : 3.2f) * dt);
        _camPos = Vector2.Lerp(_camPos, target, ease);
        _camZoom += (targetZoom - _camZoom) * ease;
        _camHeading += Math.Clamp(delta * ease, -MaxCamTurn * dt, MaxCamTurn * dt);
        return focusS;
    }

    /// <summary>The ground under the camera: a zone's element, else the course's. Most rows declare
    /// none, so returning null here leaves the sky neutral for most of a race.</summary>
    private static string? TerrainAt(AetherRaceLive.Race race, float s)
    {
        var sample = race.Track.At(s);
        if (sample.Element.Length > 0)
        {
            return sample.Element;
        }
        return race.Course.Terrain.Length > 0 ? race.Course.Terrain : null;
    }

    /// <summary>The still shot once the winner is home: heading locked to the finish line, position
    /// derived from the pin, zoom monotone so a closing tail cannot breathe the view in and out.</summary>
    private float UpdateTapeCamera(AetherRaceLive.Race race, Vector2 size, float dt, bool trackUp,
        Func<int, float> drawnS)
    {
        var line = race.Track.AtLerp(race.Track.Length);
        _finishing = true;
        _weather.HoldTransients = true;

        // How far back the shot still has to reach: the last runner not yet home.
        var tail = race.Track.Length;
        foreach (var runner in race.Runners)
        {
            if (runner.Finished)
            {
                continue;
            }

            tail = MathF.Min(tail, drawnS(runner.Idx));
        }

        var need = MathF.Max(TapeMinSpan, race.Track.Length - tail + TapeAir);
        var usable = size.Y * (1f - TapeTopFrac - 0.07f);
        var want = MathF.Max(usable / TapeMaxSpan, usable / MathF.Max(need, 1f));
        _tapeZoom = _tapeZoom <= 0f ? MathF.Min(want, _camZoom) : MathF.Min(_tapeZoom, want);

        var ease = 1f - MathF.Exp(-TapeRate * dt);
        _camZoom += (_tapeZoom - _camZoom) * ease;

        // Along the finish HEADING, not back down the centre line: the camera's heading is locked to
        // it, so the tape lands on its fraction however the road behind it curves.
        var fwd =new Vector2(MathF.Cos(line.Heading), MathF.Sin(line.Heading));
        var back = ((0.5f - TapeTopFrac) * size.Y) / MathF.Max(1f, _camZoom);
        _camPos = Vector2.Lerp(_camPos, new Vector2(line.X, line.Y) - (fwd * back), ease);

        var targetHeading = trackUp ? (-MathF.PI / 2f) - line.Heading : 0f;
        var delta = targetHeading - _camHeading;
        while (delta > MathF.PI)
        {
            delta -= MathF.Tau;
        }

        while (delta < -MathF.PI)
        {
            delta += MathF.Tau;
        }

        _camHeading += delta * ease;
        _camInit = true;
        return race.Track.Length;
    }

    private static bool AnyFinished(AetherRaceLive.Race race)
    {
        foreach (var runner in race.Runners)
        {
            if (runner.Finished)
            {
                return true;
            }
        }

        return false;
    }

    private static Vector2 WorldAt(in AetherRaceLive.TrackSample p, float lat)
    {
        var normal = new Vector2(-MathF.Sin(p.Heading), MathF.Cos(p.Heading));
        return new Vector2(p.X, p.Y) + (normal * lat);
    }

    private void DrawRibbon(ImDrawListPtr dl, AetherRaceLive.Race race, float camS, Vector2 origin,
        Vector2 size, in Rendering.StageCam cam)
    {
        var track = race.Track;

        // The circumradius is the furthest corner from the eye at any heading; the margin covers the
        // road curving back toward it. The dressing windows on the same number plus its own band.
        var span =Math.Clamp(cam.VisibleRadius(origin, size) + 12f, 20f, 140f);
        var step = track.Step * 2f;

        // Snapped to a fixed world grid. Starting the walk at the camera slides every sample under
        // it, which shimmers the ribbon and fires the furniture at a different place each frame.
        var from = MathF.Max(0f, MathF.Floor((camS - span) / step) * step);
        var to = MathF.Min(track.Length, camS + span);

        var road = Rendering.RaceDressing.RoadInk(race.Course.Terrain);
        var kerb = Rendering.RaceDressing.KerbInk(race.Course.Terrain);
        var prevL = Vector2.Zero;
        var prevR = Vector2.Zero;
        var started = false;
        for (var s = from; s <= to; s += step)
        {
            var p = track.AtLerp(s);
            // The width the runners actually steer to, so a ford funnels where the sim funnels it.
            var half = p.Width * 0.5f;
            var l = cam.ToScreen(WorldAt(in p, -half));
            var r = cam.ToScreen(WorldAt(in p, half));
            if (started)
            {
                dl.AddQuadFilled(prevL, prevR, r, l, road);
                dl.AddLine(prevL, l, kerb, Px(1.5f));
                dl.AddLine(prevR, r, kerb, Px(1.5f));
            }

            DrawChevrons(dl, in p, s, step, in cam);
            prevL = l;
            prevR = r;
            started = true;
        }

        if (from <= 0f)
        {
            DrawTape(dl, track, 0f, in cam);
        }

        if (to >= track.Length)
        {
            DrawTape(dl, track, track.Length, in cam);
        }
    }

    /// <summary>Grade chevrons, on the road surface, walked with the ribbon so they sit under the
    /// ground wash the way the road does. Footprints in world bounds, strokes in screen pixels, so
    /// furniture keeps its weight while its size tracks the camera.</summary>
    private static void DrawChevrons(ImDrawListPtr dl, in AetherRaceLive.TrackSample p, float s,
        float walkStep, in Rendering.StageCam cam)
    {
        if (MathF.Abs(p.Grade) > Rendering.RaceDressing.GradeFloor && s % ChevronSpacing < walkStep)
        {
            var colour = ImGui.ColorConvertFloat4ToU32(p.Grade > 0f
                ? new Vector4(0.95f, 0.65f, 0.4f, 0.55f)
                : new Vector4(0.5f, 0.75f, 0.95f, 0.55f));
            var forward = new Vector2(MathF.Cos(p.Heading), MathF.Sin(p.Heading)) * (p.Grade > 0f ? 1f : -1f);
            var tip = cam.ToScreen(new Vector2(p.X, p.Y) + (forward * 1.4f));
            ChevronArm(dl, tip, cam.ToScreen(WorldAt(in p, 0.6f)), colour);
            ChevronArm(dl, tip, cam.ToScreen(WorldAt(in p, -0.6f)), colour);
            dl.AddCircleFilled(tip, 1.4f, colour, 8);
        }
    }

    /// <summary>Distance posts, walked AFTER the ground rather than with the ribbon. A post stands
    /// on the verge, inside the wash's first band and in the same corridor the dressing's solids
    /// use, so walking it with the road put a wash over it and a rock in front of it.</summary>
    private static void DrawPosts(ImDrawListPtr dl, AetherRaceLive.Track track, float camS,
        Vector2 origin, Vector2 size, in Rendering.StageCam cam)
    {
        var span = Math.Clamp(cam.VisibleRadius(origin, size) + 12f, 20f, 140f);
        var step = track.Step * 2f;
        var from = MathF.Max(0f, MathF.Floor((camS - span) / step) * step);
        var to = MathF.Min(track.Length, camS + span);
        var stem = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.28f));
        var cap = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.46f));

        // Tested against the walk's OWN step, exactly as the ribbon's walk does: a hardcoded window
        // catches two consecutive rows and draws every post twice.
        for (var s = from; s <= to; s += step)
        {
            if (s <= 1f || s % PostSpacing >= step)
            {
                continue;
            }

            var p = track.AtLerp(s);
            var half = p.Width * 0.5f;

            // Outside of the bend: the inside is where the runners are.
            var side = p.Kappa > 0f ? -1f : 1f;
            var foot = cam.ToScreen(WorldAt(in p, side * (half + 0.55f)));
            var top = cam.ToScreen(WorldAt(in p, side * (half + 1.35f)));
            dl.AddLine(foot, top, stem, Px(2.2f));
            dl.AddCircleFilled(top, Px(2.6f), cap, 10);
        }
    }

    /// <summary>One arm of a grade chevron: a wedge, widest at the nose.</summary>
    private static void ChevronArm(ImDrawListPtr dl, Vector2 tip, Vector2 tail, uint colour)
    {
        var run = tail - tip;
        if (run.LengthSquared() < 0.01f)
        {
            return;
        }
        var n = Vector2.Normalize(new Vector2(-run.Y, run.X)) * 1.5f;
        dl.AddTriangleFilled(tip + n, tip - n, tail, colour);
    }

    /// <summary>One tape across the road, from world endpoints so it lies square at any heading.
    /// Dashed into lenses, falling back to a solid bar once a dash would be a few pixels.</summary>
    private void DrawTape(ImDrawListPtr dl, AetherRaceLive.Track track, float s, in Rendering.StageCam cam)
    {
        var p = track.AtLerp(s);
        var half = p.Width * 0.5f;
        var from = cam.ToScreen(WorldAt(in p, half));
        var to = cam.ToScreen(WorldAt(in p, -half));
        var ink = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.6f));

        // One shape at every zoom. Swapping to a solid bar below a pixel threshold flickered: the
        // camera eases across the threshold and the tape changed shape frame to frame.
        var run = to - from;
        if (run.LengthSquared() < 1f)
        {
            return;
        }

        const int dashes = 10;
        var n = Vector2.Normalize(new Vector2(-run.Y, run.X)) * MathF.Max(1.5f, Px(1.6f));
        for (var d = 0; d < dashes; d += 2)
        {
            var a = Vector2.Lerp(from, to, d / (float)dashes);
            var b = Vector2.Lerp(from, to, (d + 0.6f) / dashes);
            var mid = Vector2.Lerp(a, b, 0.5f);
            dl.AddQuadFilled(a, mid + n, b, mid - n, ink);
        }
    }


    /// <summary>Rain's ground ripples: the one weather mark that belongs on the road rather than in
    /// front of the lens. The anchor goes through the camera, the squashed ring does not, so a
    /// splash lies in the surface without lying down when the track turns.</summary>
    private void DrawRainRipples(ImDrawListPtr dl, AetherRaceLive.Race race, float camS, Vector2 size,
        in Rendering.StageCam cam)
    {
        var slots = _weather.RippleSlots;
        if (slots == 0)
        {
            return;
        }

        var run = size.Y * TrackUpPivotY / MathF.Max(4f, cam.Zoom);
        var colour = _weather.RippleColour;
        var track = race.Track;
        for (var i = 0; i < slots; i++)
        {
            if (!_weather.TryRipple(i, out var ahead, out var lat, out var t))
            {
                continue;
            }

            // Skipped rather than clamped: a rank of splashes across the tape is worse than rain
            // having none for a second.
            var s = camS + (ahead * run);
            if (s < Rendering.RaceDressing.TapeClear || s > track.Length - Rendering.RaceDressing.TapeClear)
            {
                continue;
            }

            var p = track.AtLerp(s);
            var at = cam.ToScreen(WorldAt(in p, lat * p.Width * 0.5f));
            var radius = cam.Zoom * RippleRadiusBounds * (0.35f + (0.65f * t));
            var ink = ImGui.ColorConvertFloat4ToU32(colour with { W = RippleAlpha * (1f - t) });
            StrokeEllipse(dl, at, new Vector2(radius, radius * RippleSquash), ink, MathF.Max(1f, radius * 0.1f));
        }
    }

    /// <summary>Projects the field once and returns it back to front. Sorted on screen Y, which is
    /// the depth axis in both camera modes and cannot disagree with what is drawn.</summary>
    private int[] RunnersBackToFront(AetherRaceLive.Race race, in Rendering.StageCam cam)
    {
        for (var i = 0; i < _order.Length; i++)
        {
            var runner = race.Runners[i];
            var sample = race.Track.AtLerp(Lerp(_prevS[i], runner.S, _frac));
            _screens[i] = cam.ToScreen(WorldAt(in sample, Lerp(_prevLat[i], runner.Lat, _frac)));
            _order[i] = i;
        }

        for (var i = 1; i < _order.Length; i++)
        {
            var v = _order[i];
            var y = _screens[v].Y;
            var j = i - 1;
            while (j >= 0 && _screens[_order[j]].Y > y)
            {
                _order[j + 1] = _order[j];
                j--;
            }

            _order[j + 1] = v;
        }

        return _order;
    }

    /// <summary>The field on a strip at the foot, one pip per runner. The left end closes on the
    /// tape as the race runs and never re-opens.</summary>
    private void DrawRail(ImDrawListPtr dl, Vector2 origin, Vector2 size, AetherRaceLive.Race race,
        LumiRaceDto dto, float dt)
    {
        var tail = race.Track.Length;
        for (var i = 0; i < race.Runners.Count; i++)
        {
            tail = MathF.Min(tail, Lerp(_prevS[i], race.Runners[i].S, _frac));
        }

        var span = MathF.Max(RailMinSpan, race.Track.Length - tail + RailAir);
        var target = MathF.Max(0f, race.Track.Length - span);
        _railStart += (target - _railStart) * (1f - MathF.Exp(-RailRate * dt));
        var window = MathF.Max(1f, race.Track.Length - _railStart);

        var y = origin.Y + size.Y - Px(24);
        var left = origin.X + Px(24);
        var right = origin.X + size.X - Px(24);
        dl.AddLine(new Vector2(left, y), new Vector2(right, y), 0xFF3C3450, Px(2f));

        // The player last, and every pip on a dark disc: a bunched finish is exactly when the rail
        // matters and exactly when flat pips of similar colour merge into one smear.
        for (var pass = 0; pass < 2; pass++)
        {
            for (var i = 0; i < race.Runners.Count; i++)
            {
                if (i == dto.PlayerSlot != (pass == 1))
                {
                    continue;
                }

                var progress = Math.Clamp(
                    (Lerp(_prevS[i], race.Runners[i].S, _frac) - _railStart) / window, 0f, 1f);
                var at = new Vector2(left + ((right - left) * progress), y);
                dl.AddCircleFilled(at, Px(6), SceneInk, 16);
                dl.AddCircleFilled(at, Px(4), ImGui.ColorConvertFloat4ToU32(StageAccent(dto.Field[i].Element)), 16);
                if (i == dto.PlayerSlot)
                {
                    dl.AddCircle(at, Px(6), 0xFFFFFFFF, 16, Px(1.5f));
                }
            }
        }
    }

    private void DrainNarration(OsAppContext ctx, AetherRaceLive.Race race)
    {
        // Only two lines fit, so the player's event is taken first.
        var first = _nextEvent;
        while (_nextEvent < race.Events.Count && race.Events[_nextEvent].T <= race.Time)
        {
            _nextEvent++;
        }

        for (var pass = 0; pass < 2; pass++)
        {
            for (var i = first; i < _nextEvent; i++)
            {
                var ev = race.Events[i];
                if (ev.Who < 0 || ev.Who >= race.Runners.Count)
                {
                    continue;
                }
                if (race.Runners[ev.Who].IsPlayer != (pass == 0))
                {
                    continue;
                }

                var key = $"os.racer_ev_{ev.Kind.ToLowerInvariant()}";
                var line = ctx.Localize(key);
                if (line != key)
                {
                    _lines.Add((string.Format(line, race.Runners[ev.Who].Name), 0f));
                }
            }
        }
        for (var i = _lines.Count - 1; i >= 0; i--)
        {
            var line = _lines[i];
            line.Age += ImGui.GetIO().DeltaTime;
            if (line.Age > 4f || _lines.Count - i > 2)
            {
                _lines.RemoveAt(i);
            }
            else
            {
                _lines[i] = line;
            }
        }
    }

    private void DrawNarration(ImDrawListPtr dl, Vector2 origin, Vector2 size)
    {
        var y = origin.Y + size.Y - Px(58);
        for (var i = _lines.Count - 1; i >= 0; i--)
        {
            var (text, age) = _lines[i];
            var alpha = (byte)(255 * Math.Clamp(1.2f - (age / 4f), 0f, 1f));
            var textSize = ImGui.CalcTextSize(text);
            dl.AddText(new Vector2(origin.X + ((size.X - textSize.X) * 0.5f), y), (uint)(alpha << 24) | 0x00FFFFFF, text);
            y -= Px(18);
        }
    }

    private void DrawSkipChip(OsAppContext ctx, ImDrawListPtr dl, Vector2 origin, Vector2 size)
    {
        var label = ctx.Localize("os.racer_skip");
        var textSize = ImGui.CalcTextSize(label);
        var pad = Px(8);
        var a = new Vector2(origin.X + size.X - textSize.X - (pad * 3) - Px(40), origin.Y + Px(10));
        var b = a + textSize + new Vector2(pad * 2, pad);
        var hovered = ImGui.IsMouseHoveringRect(a, b);
        dl.AddRectFilled(a, b, hovered ? 0xC84A3E68u : 0x96382E52u, Px(10));
        dl.AddText(a + new Vector2(pad, pad * 0.5f), 0xFFE6E0F5, label);
        if (hovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                _skipped = true;
            }
        }
    }

    private void DrawResult(OsAppContext ctx, ImDrawListPtr dl, Vector2 origin, Vector2 size,
        LumiRaceStartResultDto result, AetherRaceLive.Race race)
    {
        var dto = result.Race;
        var place = PlaceOf(dto);
        var won = place is >= 1 and <= 3;
        _podiumAge += ImGui.GetIO().DeltaTime;

        using (ctx.TitleFont?.Push())
        {
            CenteredAt(dl, origin, size.X, Px(16), ctx.Localize("os.racer_results_title"), 0xFFFFFFFF);
        }

        DrawPodium(ctx, dl, origin, size, dto);
        DrawAlsoRan(ctx, dl, origin, size, dto);

        if (won && !ctx.ReduceMotion)
        {
            DrawCelebration(dl, origin, size);
        }

        var name = dto.PlayerSlot < dto.Field.Length ? dto.Field[dto.PlayerSlot].Name : string.Empty;
        var headline = string.Format(ctx.Localize($"os.racer_finish_{Math.Clamp((int)place, 1, 6)}"), name);
        var y = size.Y * 0.63f;
        using (ctx.TitleFont?.Push())
        {
            // A long name must not run off the stage, so the line shrinks to fit rather than clipping.
            var room = size.X - Px(36);
            var wide = ImGui.CalcTextSize(headline).X;
            var scale = wide > room ? room / wide : 1f;
            CenteredScaled(dl, origin, size.X, y, headline, 0xFFFFFFFF, scale);
            y += ImGui.GetTextLineHeight() * scale;
        }

        y += Px(22);
        DrawStampAward(ctx, dl, origin, size, result.Reward, ref y);

        ImGui.SetCursorScreenPos(new Vector2(origin.X + Px(24), origin.Y + size.Y - Px(52)));
        var width = size.X - Px(48);
        if (result.Reward.Pack is { } pack && _pack is null && _flip is null)
        {
            if (DrawResultButton(dl, "##racerPack", ctx.Localize("os.racer_pack_open"), width))
            {
                _flip = new CardFlipOverlay(host, pack, LumiRaceLimits.StampsPerCard, PlayStampThud, Close);
            }
        }
        else if (_pack is null && _flip is null
            && DrawResultButton(dl, "##racerDone", ctx.Localize("os.racer_continue"), width))
        {
            Close();
        }

        if (_flip is { } turning)
        {
            turning.Draw(ctx);
            if (turning.Dismissed)
            {
                _flip = null;
            }
            else if (turning.Done)
            {
                _pack = new PackRipOverlay(host, turning.Pack, Close);
                _flip = null;
            }
        }

        if (_pack is { } open)
        {
            open.Draw(ctx);
            if (open.Closed)
            {
                _pack = null;
                Close();
            }
        }
    }

    /// <summary>The stamp, arriving: it drops in, lands with the thud the catch game uses, and the
    /// card rocks under it. A full card keeps going, turning the fifth shard into a prize waiting to
    /// be claimed.</summary>
    private void DrawStampAward(OsAppContext ctx, ImDrawListPtr dl, Vector2 origin, Vector2 size,
        LumiRaceRewardDto reward, ref float y)
    {
        if (!reward.StampAwarded)
        {
            return;
        }

        var centre = new Vector2(origin.X + (size.X * 0.5f), origin.Y + y + Px(24));
        var t = ctx.ReduceMotion ? 1f : Math.Clamp((_podiumAge - StampDelay) / StampFall, 0f, 1f);
        if (t <= 0f)
        {
            y += Px(58);
            return;
        }

        if (!_stampLanded && t >= 1f)
        {
            _stampLanded = true;
            PlaySfx(ctx, "crystal_thud.ogg", StampThudLevel);
        }

        // Falls fast and overshoots a little on landing, which is what makes it read as a smack
        // rather than as a fade-in.
        var drop = (1f - t) * (1f - t) * Px(70f);
        var squash = t >= 1f
            ? 1f + (0.16f * MathF.Exp(-(_podiumAge - StampDelay - StampFall) * 9f)
                * MathF.Sin((_podiumAge - StampDelay - StampFall) * 26f))
            : 1f;
        var r = Px(26f) * (0.75f + (0.25f * t));
        var at = centre - new Vector2(0f, drop);

        var full = reward.CardCompleted;
        var tint = full ? new Vector4(1f, 0.78f, 0.30f, 1f) : new Vector4(0.62f, 0.86f, 0.94f, 1f);
        if (t >= 1f)
        {
            var since = _podiumAge - StampDelay - StampFall;
            var ring = Math.Clamp(since / 0.45f, 0f, 1f);
            if (ring < 1f)
            {
                dl.AddCircle(at, r * (1f + (ring * 2.2f)),
                    ImGui.ColorConvertFloat4ToU32(tint with { W = 0.75f * (1f - ring) }), 28, Px(2f));
            }
            if (full)
            {
                DrawClaimStars(dl, at, r, tint, since, ctx.ReduceMotion);
            }
        }

        RacerChrome.Halo(dl, at, r * (full ? 2.4f : 1.7f), tint, full ? 0.5f : 0.3f);
        RacerChrome.Stamp(dl, ctx, host.PetAssetRoot, at, r, StampInk, new Vector2(1f / squash, squash));

        // A full card's stamp is the way in to the prize, so it takes a press of its own.
        if (full && _pack is null && _flip is null && reward.Pack is { } ready
            && ImGui.IsMouseHoveringRect(at - new Vector2(r, r), at + new Vector2(r, r)))
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                _flip = new CardFlipOverlay(host, ready, LumiRaceLimits.StampsPerCard, PlayStampThud, Close);
            }
        }

        y += Px(58);
        CenteredAt(dl, origin, size.X, y,
            ctx.Localize(full ? "os.racer_card_claim" : "os.racer_stamp_earned"),
            full ? 0xFF4AC2F0u : 0xFFB4AACCu);
        y += Px(20);
    }

    /// <summary>A full card's fifth shard: a turning star field, so the card reads as a prize rather
    /// than as one more tick.</summary>
    private static void DrawClaimStars(ImDrawListPtr dl, Vector2 at, float r, Vector4 tint, float since,
        bool reduceMotion)
    {
        const int points = 9;
        var spin = reduceMotion ? 0f : since * 0.9f;
        for (var i = 0; i < points; i++)
        {
            var a = (MathF.Tau * i / points) + spin;
            var swell = reduceMotion ? 1f : 1f + (0.14f * MathF.Sin((since * 3.4f) + (i * 0.7f)));
            var out0 = r * 1.6f * swell;
            var p = at + (new Vector2(MathF.Cos(a), MathF.Sin(a) * 0.85f) * out0);
            var twinkle = reduceMotion ? 0.8f : 0.55f + (0.45f * MathF.Sin((since * 5f) + (i * 1.3f)));
            Star(dl, p, Px(5.5f) * twinkle, ImGui.ColorConvertFloat4ToU32(tint with { W = twinkle }));
        }
    }

    private static void Star(ImDrawListPtr dl, Vector2 c, float r, uint ink)
    {
        dl.AddQuadFilled(c + new Vector2(0f, -r), c + new Vector2(r * 0.32f, 0f),
            c + new Vector2(0f, r), c + new Vector2(-r * 0.32f, 0f), ink);
        dl.AddQuadFilled(c + new Vector2(-r, 0f), c + new Vector2(0f, -r * 0.32f),
            c + new Vector2(r, 0f), c + new Vector2(0f, r * 0.32f), ink);
    }

    /// <summary>The way on, in the flag's red so it reads as the one thing to press.</summary>
    private static bool DrawResultButton(ImDrawListPtr dl, string id, string label, float width)
    {
        var tl = ImGui.GetCursorScreenPos();
        var height = Px(38);
        var pressed = ImGui.InvisibleButton(id, new Vector2(width, height));
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            HandOnHover();
        }

        var br = tl + new Vector2(width, height);
        var fill = new Vector4(0.68f, 0.11f, 0.16f, hovered ? 1f : 0.92f);
        dl.AddRectFilled(tl + new Vector2(0f, Px(3)), br + new Vector2(0f, Px(3)), 0x66000000u, height * 0.3f);
        dl.AddRectFilled(tl, br, ImGui.ColorConvertFloat4ToU32(fill), height * 0.3f);
        dl.AddRect(tl, br, ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.55f)),
            height * 0.3f, ImDrawFlags.RoundCornersAll, Px(1.6f));
        var text = ImGui.CalcTextSize(label);
        dl.AddText(tl + ((new Vector2(width, height) - text) * 0.5f), 0xFFFFFFFF, label);
        return pressed;
    }

    private void PlayStampThud(OsAppContext ctx) => PlaySfx(ctx, "crystal_thud.ogg", StampThudLevel);

    private void PlaySfx(OsAppContext ctx, string file, float level, float pitch = 1f)
    {
        if (muted())
        {
            return;
        }
        try
        {
            ctx.Capabilities.Audio.Play(System.IO.Path.Combine(host.SoundRoot, file), level, pitch);
        }
        catch (Exception)
        {
        }
    }

    private static void CenteredScaled(ImDrawListPtr dl, Vector2 origin, float width, float y, string text,
        uint ink, float scale)
    {
        var size = ImGui.CalcTextSize(text) * scale;
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * scale,
            new Vector2(origin.X + ((width - size.X) * 0.5f), origin.Y + y), ink, text);
    }

    /// <summary>The top three, on steps in the flag's colours so first, second and third read at a
    /// glance rather than by height alone.</summary>
    private void DrawPodium(OsAppContext ctx, ImDrawListPtr dl, Vector2 origin, Vector2 size, LumiRaceDto dto)
    {
        Span<float> columns = [0.5f, 0.20f, 0.80f];
        Span<float> steps = [58f, 38f, 26f];
        var floor = origin.Y + (size.Y * 0.40f);

        for (var pass = 0; pass < 2; pass++)
        {
            for (var rank = 0; rank < 3 && rank < dto.Placements.Length; rank++)
            {
                if (rank == 0 != (pass == 1))
                {
                    continue;
                }

                var slot = dto.Placements[rank];
                var x = origin.X + (size.X * columns[rank]);
                var top = floor - Px(steps[rank]);
                var half = Px(rank == 0 ? 48 : 42);
                var face = PodiumInk[rank];

                dl.AddRectFilled(new Vector2(x - half, top), new Vector2(x + half, floor + Px(8)),
                    ImGui.ColorConvertFloat4ToU32(face), Px(6));
                dl.AddRectFilled(new Vector2(x - half, top), new Vector2(x + half, top + Px(5)),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.35f)), Px(4));
                dl.AddRect(new Vector2(x - half, top), new Vector2(x + half, floor + Px(8)),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.5f)),
                    Px(6), ImDrawFlags.RoundCornersAll, Px(1.4f));

                var mark = (rank + 1).ToString();
                var markSize = ImGui.CalcTextSize(mark);
                dl.AddText(new Vector2(x - (markSize.X * 0.5f), top + ((floor + Px(8) - top - markSize.Y) * 0.5f)),
                    ImGui.ColorConvertFloat4ToU32(PodiumMarkInk[rank]), mark);

                var pet = _pets[slot];
                pet.Tick(ctx.ReduceMotion);
                Crowd(ctx, pet, slot, rank, dto.Field.Length);
                pet.Draw(dl, ctx.Capabilities.Textures, new Vector2(x, top), Px(rank == 0 ? 108 : 88),
                    pet.Pose, props: false);

                var accent = slot == dto.PlayerSlot
                    ? StageAccent(dto.Field[slot].Element)
                    : new Vector4(1f, 1f, 1f, 0.85f);
                RaceLabel(dl, new Vector2(x, floor + Px(22)), dto.Field[slot].Name, accent, 1f, plate: 0.4f);
            }
        }
    }

    /// <summary>Fourth to sixth, smaller and in a row under the steps. They ran too, and one of them
    /// is often the player.</summary>
    private void DrawAlsoRan(OsAppContext ctx, ImDrawListPtr dl, Vector2 origin, Vector2 size, LumiRaceDto dto)
    {
        var rest = dto.Placements.Length - 3;
        if (rest <= 0)
        {
            return;
        }

        var y = origin.Y + (size.Y * 0.54f);
        var gap = size.X / (rest + 1);
        for (var i = 0; i < rest; i++)
        {
            var slot = dto.Placements[i + 3];
            var x = origin.X + (gap * (i + 1));
            var pet = _pets[slot];
            pet.Tick(ctx.ReduceMotion);
            Crowd(ctx, pet, slot, i + 3, dto.Field.Length);
            pet.Draw(dl, ctx.Capabilities.Textures, new Vector2(x, y), Px(62), pet.Pose, props: false);

            var mine = slot == dto.PlayerSlot;
            var accent = mine ? StageAccent(dto.Field[slot].Element) : new Vector4(1f, 1f, 1f, 0.7f);
            RaceLabel(dl, new Vector2(x, y + Px(14)), dto.Field[slot].Name, accent, mine ? 1f : 0.8f, plate: 0.34f);
        }
    }


    /// <summary>A fixed spark pool: thrown at the gun and left to die.</summary>
    private struct Spark
    {
        public Vector2 Pos;
        public Vector2 Vel;
        public float Age;
        public float Life;
        public Vector4 Colour;
    }

    /// <summary>The gun's bang, so the race starts on an event rather than on the parade
    /// stopping.</summary>
    private void StartBang(Vector2 origin, Vector2 size, Vector4 colour)
    {
        var rng = new Random(_result?.Race.Seed ?? 0);
        for (var i = 0; i < _sparks.Length; i++)
        {
            var at = origin + new Vector2(size.X * (0.18f + (0.16f * (i % 5))), size.Y * 0.52f);
            var a = (float)(rng.NextDouble() * Math.Tau);
            var v = 120f + ((float)rng.NextDouble() * 260f);
            _sparks[i] = new Spark
            {
                Pos = at,
                Vel = new Vector2(MathF.Cos(a), MathF.Sin(a)) * v,
                Age = 0f,
                Life = 0.5f + ((float)rng.NextDouble() * 0.5f),
                Colour = colour,
            };
        }
    }

    private void DrawSparks(ImDrawListPtr dl, float dt)
    {
        for (var i = 0; i < _sparks.Length; i++)
        {
            ref var s = ref _sparks[i];
            if (s.Age >= s.Life)
            {
                continue;
            }
            s.Age += dt;
            s.Vel += new Vector2(0f, 220f * dt);
            s.Pos += s.Vel * dt;
            var k = 1f - (s.Age / s.Life);
            dl.AddCircleFilled(s.Pos, MathF.Max(1f, Px(3) * k),
                ImGui.ColorConvertFloat4ToU32(s.Colour with { W = k }), 8);
        }
    }

    private void DrawCelebration(ImDrawListPtr dl, Vector2 origin, Vector2 size)
    {
        var t = (float)ImGui.GetTime();
        for (var i = 0; i < 26; i++)
        {
            var seed = (i * 2654435769u) & 0xFFFF;
            var phase = ((t * (0.35f + ((seed & 15) * 0.03f))) + (seed / 65535f)) % 1f;
            var x = origin.X + (size.X * (((seed >> 4) & 255) / 255f));
            var y = origin.Y + (size.Y * phase);
            var color = (i % 3) switch
            {
                0 => 0xFF4AC2F0u,
                1 => 0xFF7CD9A0u,
                _ => 0xFFD9A84Au,
            };
            dl.AddCircleFilled(new Vector2(x, y), Px(2.5f), color);
        }
    }

    private short PlaceOf(LumiRaceDto dto)
    {
        for (short i = 0; i < dto.Placements.Length; i++)
        {
            if (dto.Placements[i] == dto.PlayerSlot)
            {
                return (short)(i + 1);
            }
        }
        return (short)dto.Field.Length;
    }

    private void Close()
    {
        host.StopBgm();
        _result = null;
        _race = null;
        _pets = [];
        back();
    }

    /// <summary>The arms run while the body keeps its ordinary idle: a pair out of phase, each hand
    /// rising on its own half of the stride, the whole thing scaled by how fast the runner is actually
    /// going so a walk barely lifts and a stumble half-drops. Driven off the runner's own blended gait
    /// phase rather than a clock, which is what keeps the swing in step with the legs through a change
    /// of pace.</summary>
    private static HandsDelta GaitHands(float beat, float amp) => new()
    {
        Right = new Vector2(7f, -10f * MathF.Max(0f, beat)) * amp,
        Left = new Vector2(7f, -10f * MathF.Max(0f, -beat)) * amp,
        RightTilt = 0.05f * beat * amp,
        LeftTilt = -0.05f * beat * amp,
    };

    private static float Lerp(float a, float b, float t) => a + ((b - a) * t);

    private static void CenteredAt(ImDrawListPtr dl, Vector2 origin, float width, float y, string text, uint color)
    {
        var size = ImGui.CalcTextSize(text);
        dl.AddText(new Vector2(origin.X + ((width - size.X) * 0.5f), origin.Y + y), color, text);
    }
}
