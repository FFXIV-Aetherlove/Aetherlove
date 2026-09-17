namespace AetherOS.Apps.Racer.Rendering;

using System;
using System.Collections.Generic;
using System.Numerics;

using AetherLove.Shared.Racing;
using Dalamud.Bindings.ImGui;

/// <summary>
/// The ground either side of the road: a three-band elemental wash out from both rails, terrace
/// rungs where the road tilts, and a scatter of roadside solids. Placed once at the gun and drawn
/// from baked world geometry, so a frame costs a projection and a handful of fills.
///
/// <para>Hue is what tells the seven courses apart and brightness never is. Every wash colour is
/// laid at the alpha that lands it at exactly <see cref="WashLum"/> over <see cref="ElementFx.Night"/>
/// (<see cref="QuietLum"/> for the course with no element), solved rather than authored, so moving
/// an element's tint re-solves the wash instead of silently retuning it.
/// <see cref="WeatherFx.CastLuminance"/> is the same number, which is what lets a full-screen veil
/// change the stage's hue without moving the ground under it.</para>
///
/// <para>Everything that is a pure function of arc length is resolved in <see cref="Generate"/>: the
/// zone colour ease, the grade tilt on the wash alpha, the band offsets, every rung, every solid.
/// The draw path reads baked structs, projects one anchor per station and steps it with
/// <see cref="StageCam.ToScreenDelta"/>, rejects each quad on its own screen bounds, and allocates
/// nothing.</para>
///
/// <para>Solids are drawn UPRIGHT in screen space from a ground anchor, so they do not roll with the
/// track-up camera and are correct under the north-up one too. Nothing here samples the sim, and
/// nothing here may run after the runners: the verge never occludes a Lumi.</para>
///
/// <para><b>What it deliberately does not do:</b> no spatial index (the walk is at most 300 structs
/// behind a squared-distance reject), no thinning of the scatter by rank, no zoom-keyed detail
/// switches (every one of them popped as the camera eased, and two of them never fired at all under
/// the north-up camera), and no coupling to the sky beyond <see cref="PrevailingLean"/>, the gust
/// handed to <see cref="Draw"/> and the transient light read through <see cref="LightAt"/>.</para>
/// </summary>
internal sealed class RaceDressing
{
    /// <summary>The tilt past which a stretch reads as graded. The ribbon's chevrons read this same
    /// constant, so the player learns one threshold and not two.</summary>
    internal const float GradeFloor = 0.012f;

    /// <summary>How far the dressing and the rain ripples both stay off the two tapes, in bounds.
    /// One number, read by both.</summary>
    internal const float TapeClear = 6f;

    /// <summary>The race stage's own ground, packed once. Every wash alpha is solved against it, so
    /// the backdrop the running phase paints and the solve can never be two different colours.</summary>
    public static readonly uint NightInk = ElementFx.U32(ElementFx.Night);

    /// <summary>The luminances the road surface and its kerb lines are re-authored to. Held at what
    /// the old fixed pair measured, because the road reads by its EDGES against the dressed ground
    /// beside it and moving either would change how the stage reads, not just its colour.</summary>
    private const float RoadLum = 0.1920f;
    private const float KerbLum = 0.3477f;

    /// <summary>How much of the neutral surface stays in the road. A road is a surface and not a
    /// light, so it keeps most of its element rather than all of it.</summary>
    private const float RoadMix = 0.10f;

    private static readonly Vector4 RoadNeutral = new(0.55f, 0.55f, 0.58f, 1f);

    /// <summary>The road's own surface for a course. Same luminance on every one of them, so hue is
    /// what says which course this is and brightness never does, exactly as the ground wash works.
    /// It used to be one literal on all seven, which is why every road read red.</summary>
    public static uint RoadInk(string? element) => ElementFx.U32(RoadAt(element, RoadLum));

    /// <summary>The lines down the road's two edges. Brighter than the surface, same rule.</summary>
    public static uint KerbInk(string? element) => ElementFx.U32(RoadAt(element, KerbLum));

    private static Vector4 RoadAt(string? element, float luminance)
    {
        var look = ElementFx.For(element);
        var hue = look.Key.Length == 0
            ? RoadNeutral
            : Vector4.Lerp(look.Tint, RoadNeutral, RoadMix);
        return ElementFx.AtLuminance(hue, luminance) with { W = 1f };
    }

    private const byte Boulder = 0;
    private const byte Stone = 1;
    private const byte Cone = 2;
    private const byte Spire = 3;
    private const byte Shard = 4;
    private const byte Patch = 5;
    private const byte Tuft = 6;
    private const byte Reed = 7;
    private const byte Flower = 8;
    private const byte Fleck = 9;
    private const byte Moss = 10;
    private const byte Tick = 11;
    private const byte Ledge = 12;

    /// <summary>The seven materials, one per theme index.</summary>
    private const byte MatBasalt = 0;
    private const byte MatWindstone = 1;
    private const byte MatWet = 2;
    private const byte MatStorm = 3;
    private const byte MatEarth = 4;
    private const byte MatIce = 5;

    /// <summary>The lateral corridor, in world bounds, measured from the rail outward: a bare verge
    /// the wash starts at, the strip no ink may enter, how far a solid may reach, and how far the
    /// outermost wash band carries.</summary>
    private const float Verge = 0.10f;
    private const float RailClear = 0.45f;
    private const float Reach = 5.6f;
    private const float BandEdge3 = 6.4f;

    private const float BandEdge1 = 1.6f;
    private const float BandEdge2 = 3.6f;
    private const float Band2Alpha = 0.55f;
    private const float Band3Alpha = 0.24f;

    /// <summary>The width a band spread is authored against, and the floor a narrow road holds it
    /// to so the outer band never runs past the measured self-clearance.</summary>
    private const float FitWidth = 6f;
    private const float FitFloor = 0.62f;

    /// <summary>Bounds between wash stations, and how much of the way a station's colour travels
    /// toward the ground under it. A zone that arrives over a dozen bounds reads as ground
    /// changing; a seam across the verge reads as a rendering fault.</summary>
    private const float StationStep = 2f;
    private const float ZoneEase = 0.28f;

    /// <summary>Downhill feels open and uphill feels close, so the wash lifts on a descent and
    /// settles on a climb. Asymmetric on purpose: the lift is the louder half.</summary>
    private const float DescentGain = 6f;
    private const float DescentCap = 0.30f;
    private const float ClimbGain = 3f;
    private const float ClimbCap = 0.12f;

    /// <summary>The luminance every elemental wash is solved to over the stage's own night, so hue
    /// is what tells the courses apart and brightness never is. The quiet course sits a step under.</summary>
    private const float WashLum = 0.2000f;
    private const float QuietLum = 0.1720f;

    /// <summary>How much of the neutral ground stays in a wash. Low, because the courses have to be
    /// told apart at a glance: mixing most of the grey back in left the widest gap between any two
    /// of them at 15/255, which no one can see.</summary>
    private const float GroundMixScale = 0.30f;

    private const float TerraceGap = 22f;
    private const float TerraceTight = 11f;
    private const float SteepSpan = 0.045f;
    private const float SteepFloor = 0.45f;
    private const float SteepGain = 0.55f;
    private const int RungsPerSide = 3;
    private const float RungLatBase = 0.7f;
    private const float RungLatStep = 1.25f;
    private const float RungLength = 2.4f;
    private const float RungPitch = 0.95f;
    private const float RungWeight = 1.4f;

    /// <summary>Placement: the ceiling on instances, the base stride and every modifier on it.</summary>
    private const int Cap = 300;
    private const float StrideBase = 8.5f;
    private const float StrideSpan = 5f;
    private const float OutsideStride = 0.74f;
    private const float StrideOpenBase = 0.60f;
    private const float StrideOpenGain = 0.40f;
    private const float ClimbStride = 12f;
    private const float ThinOpen = 1.05f;
    private const float ThinChance = 0.22f;
    private const float TallChance = 0.6f;
    private const float KindAWeight = 0.45f;
    private const float KindBWeight = 0.80f;
    private const float ScaleMin = 0.62f;
    private const float ScaleSpan = 0.86f;
    private const float TightScale = 1.20f;
    private const float TightFrac = 0.85f;
    private const float OpenRef = 3f;
    private const float OpenMin = 0.55f;
    private const float OpenMax = 1.15f;
    private const float OutsideBias = 0.75f;
    private const float OffBase = 0.35f;
    private const float OffGain = 0.65f;
    private const double BitsScale = 4294967296.0;

    /// <summary>Screen sizes, in design pixels. A solid dissolves out between <see cref="LodPx"/>
    /// and <see cref="LodPx"/> plus <see cref="LodFade"/> rather than being cut at a threshold: the
    /// camera's zoom eases continuously and the smallest placed instance sits within a pixel of the
    /// stage's own pull-back floor, so a hard gate blinks it every time the field strings out. Above
    /// <see cref="DetailPx"/> a solid also draws its trimmings.</summary>
    private const float LodPx = 7f;
    private const float LodFade = 4f;
    private const float DetailPx = 18f;
    private const float PadFrac = 1.6f;

    /// <summary>The material pass's own sizes, in design pixels: under <see cref="FacesPx"/> a solid
    /// keeps a single fill; from <see cref="MarksPx"/> a quarter of the instances carry a mark.</summary>
    private const float FacesPx = 15f;
    private const float MarksPx = 28f;

    /// <summary>The value planes: how far the crown lifts toward the glow per material, the lit
    /// flank's share of that lift, how far the dark flank sinks toward the night, the edge-normal
    /// dot products that sort the three, and the hub's pull toward the apex.</summary>
    private const float FlankShare = 0.43f;
    private const float DarkFlankMix = 0.34f;
    private const float CrownDot = 0.62f;
    private const float FlankDot = 0.06f;
    private const float HubTopPull = 0.16f;

    /// <summary>The plants' breeze and the gust's bend on top of it, the wet stone's slow highlight,
    /// and how far transient light pushes a solid's fill and glow.</summary>
    private const float BreezeSwing = 0.10f;
    private const float BreezeRate = 1.4f;
    private const float GustBend = 0.34f;
    private const float WetSwing = 0.10f;
    private const float WetRate = 0.72f;
    private const float LightFillMix = 0.26f;
    private const float LightGlowMix = 0.42f;
    private static readonly Vector4 LightWarm = new(1f, 0.93f, 0.76f, 1f);

    /// <summary>The share of wind flecks that are laid down as low windstones, and the scale they
    /// take so the stone's footprint sits inside the fleck's already-checked one.</summary>
    private const uint WindstoneMask = 3u;
    private const float WindstoneScale = 0.68f;

    /// <summary>Paired section landmarks: the cap, the walk that finds a section's first view, how
    /// far out they stand, the road disc they claim at placement, the verge reach the final
    /// clearance may step them within, the two silhouettes' scales and the zoom under which they
    /// are not drawn.</summary>
    private const int LandmarkCap = 6;
    private const float LandmarkFirstS = 12f;
    private const float LandmarkStep = 10f;
    private const float LandmarkOut = 3.5f;
    private const float LandmarkClaim = 2.2f;
    private const float LandmarkReach = 8f;
    private const float LandmarkTallScale = 1.35f;
    private const float LandmarkSideScale = 0.68f;
    private const float LandmarkSideOffset = 0.60f;
    private const float LandmarkMinZoom = 8f;
    private const uint LandmarkSideSalt = 0x614fu;

    /// <summary>The widest a solid reaches from its own anchor, in bounds: the world cull's pad.</summary>
    private const float DoodadReach = 2.6f;

    private const float SilhouetteFrac = 0.038f;
    private const float SilhouetteMin = 1.15f;
    private const float SilhouetteMax = 2.4f;
    private const float HairWeight = 1f;
    private const float ShadowAlpha = 0.42f;

    /// <summary>Mid moss: the surface every element tint is mixed toward, so the ground keeps its
    /// hue and never glows.</summary>
    private static readonly Vector4 Ground = new(0.35f, 0.42f, 0.32f, 1f);

    /// <summary>Warm up the climb, cool down it: the chevrons' own colour language at the sides'
    /// quieter alpha, so a player learns one warm and not two. Theme-independent by design.</summary>
    private static readonly Vector4 ClimbInk = new(0.95f, 0.65f, 0.4f, 0.34f);
    private static readonly Vector4 FallInk = new(0.5f, 0.75f, 0.95f, 0.34f);

    /// <summary>The bank vocabulary: how far the face fill leans toward the ink, the cap's share of
    /// the glow, the foot shadow's weight on a cut bank and an embankment, the snow cap, the wet
    /// darkening, and the fracture stroke. Where each band sits is a fraction of the face from the
    /// rail outward.</summary>
    private const float BankFillMix = 0.14f;
    private const float BankFillLitGain = 0.12f;
    private const float BankFaceAlpha = 0.92f;
    private const float BankCapAlpha = 0.94f;
    private const float BankCapMix = 0.17f;
    private const float BankCapLitGain = 0.10f;
    private const float BankSnowCapMix = 0.35f;
    private const float BankFootCut = 0.38f;
    private const float BankFootFill = 0.26f;
    private const float BankCapStart = 0.78f;
    private const float BankCapEnd = 0.22f;
    private const float BankFootStart = 0.86f;
    private const float BankFootEnd = 0.14f;
    private const float BankEdgeAlpha = 0.18f;
    private const float BankEdgeLitGain = 0.14f;
    private const float BankEdgeWetAlpha = 0.30f;
    private const float BankSeamAlpha = 0.20f;
    private const float BankSeamPx = 14f;
    private const float BankFracturePx = 26f;
    private static readonly Vector4 BankSnowCap = new(0.72f, 0.82f, 0.88f, 1f);
    private static readonly Vector4 BankWetTint = new(0.80f, 0.86f, 0.92f, 1f);
    private static readonly Vector4 BankFootInk = new(0.025f, 0.028f, 0.041f, 1f);
    private static readonly Vector4 BankFractureInk = new(0.025f, 0.025f, 0.035f, 0.32f);

    /// <summary>The clearance a terrace rung keeps from a bank face, in bounds: its own stroke.</summary>
    private const float RungMargin = 0.12f;
    private const byte LeftSide = 1;
    private const byte RightSide = 2;

    /// <summary>The two fixed rings the shapes reuse every frame, unit-sized and taken once. The
    /// contact ellipse is drawn for six of the thirteen kinds, so its eight sines were the largest
    /// block of transcendentals in the pass.</summary>
    private static readonly Vector2[] Ring8 = BuildRing(8);
    private static readonly Vector2[] Ring7 = BuildRing(7);

    private DressTheme[] themes = [];
    private bool looped;
    private Station[] stations = [];
    private Terrace[] terraces = [];
    private Doodad[] doodads = [];
    private Landmark[] landmarks = [];
    private byte[] rows = [];
    private AetherRaceLive.Track? built;
    private int baseTheme;
    private float maxHalf;

    /// <summary>Which way the roadside grass bends, for the gale to blow with. Every shipped theme
    /// leans positive or not at all; it exists so a future one that leans the other way makes the
    /// air follow the grass rather than fight it.</summary>
    public float PrevailingLean =>
        this.themes.Length > 0 && this.themes[this.baseTheme].Lean < 0f ? -1f : 1f;

    /// <summary>The transient light on a world point, 0..1: a meteor's impact, a strike's sheet
    /// light. Bound once by the stage; null is darkness. Asked per drawn solid, so it must be cheap.</summary>
    public Func<Vector2, float>? LightAt { get; set; }

    /// <summary>Places and bakes the whole course's dressing. Called once, off the race seed and the
    /// course name, on a stream of its own so nothing decorative can perturb the sim.</summary>
    public void Generate(int seed, AetherRaceLive.Track track, AetherRaceLive.CourseDef course)
    {
        this.themes = BuildThemes();
        this.baseTheme = ThemeIndex(course.Terrain);

        var rows = new byte[track.Count];
        for (var j = 0; j < track.Count; j++)
        {
            var zone = track.Elems[j];
            rows[j] = (byte)(zone.Length > 0 ? ThemeIndex(zone) : this.baseTheme);
        }

        var widest = track.Width;
        for (var j = 0; j < track.Count; j++)
        {
            widest = MathF.Max(widest, track.Ws[j]);
        }

        this.maxHalf = widest * 0.5f;
        this.rows = rows;
        this.BakeGround(track, rows);
        this.BakeDoodads(seed, track, course, rows);
        this.BakeLandmarks(seed, track, rows);
        this.built = track;
    }

    /// <summary>The last generation stage, run once after the tapered relief exists: every solid,
    /// every landmark and every terrace rung is resolved against the final bank faces and every road
    /// branch. A solid or landmark keeps its anchor, else moves outward by up to three fixed steps
    /// within its reach, else is dropped; a rung is kept or dropped per side. No RNG is drawn, order is kept, and a retained
    /// instance keeps its scale, phase, bits, kind and theme. Never called per frame.</summary>
    public void ClearScenery(AetherRaceLive.Track track, RaceTerrainRelief.Profile relief, RaceRoadClearance road)
    {
        if (!ReferenceEquals(this.built, track))
        {
            return;
        }

        // Runs on a flat course too: the rendered envelope is wider than the placement one, and
        // the road check with it is part of the pass.
        var placement = new RaceSceneryPlacement(relief);
        Func<Vector2, float, bool> roadClear = road.Clear;
        var kept = 0;
        for (var i = 0; i < this.doodads.Length; i++)
        {
            var d = this.doodads[i];
            var radius = d.Scale * VisualFootprint(d.Kind);
            if (placement.TryPlace(d.World, radius, track, Reach, roadClear, out var at))
            {
                this.doodads[kept++] = d with { World = at };
            }
        }

        if (kept != this.doodads.Length)
        {
            Array.Resize(ref this.doodads, kept);
        }

        kept = 0;
        for (var i = 0; i < this.landmarks.Length; i++)
        {
            var l = this.landmarks[i];
            if (placement.TryPlace(l.World, LandmarkRadius(in this.themes[l.Theme]), track, LandmarkReach, roadClear, out var at))
            {
                this.landmarks[kept++] = l with { World = at };
            }
        }

        if (kept != this.landmarks.Length)
        {
            Array.Resize(ref this.landmarks, kept);
        }

        kept = 0;
        for (var i = 0; i < this.terraces.Length; i++)
        {
            var t = this.terraces[i];
            var sides = (byte)((RungsClear(placement, in t, -1) ? LeftSide : 0) | (RungsClear(placement, in t, 1) ? RightSide : 0));
            if (sides != 0)
            {
                this.terraces[kept++] = t with { Sides = sides };
            }
        }

        if (kept != this.terraces.Length)
        {
            Array.Resize(ref this.terraces, kept);
        }
    }

    private static bool RungsClear(RaceSceneryPlacement placement, in Terrace t, int side)
    {
        for (var r = 0; r < RungsPerSide; r++)
        {
            var from = RungStart(in t, side, r);
            var to = from + (t.F * RungLength);
            if (!placement.Clear(from, RungMargin) || !placement.Clear(Vector2.Lerp(from, to, 0.5f), RungMargin)
                || !placement.Clear(to, RungMargin))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Where a rung begins, in world bounds. The draw path steps the same offsets in screen
    /// space from one projected anchor.</summary>
    private static Vector2 RungStart(in Terrace t, int side, int r) =>
        t.P + (t.N * (side * (t.Half + RungLatBase + (r * RungLatStep)))) + (t.F * (t.Pitch * r * RungPitch));

    /// <summary>Everything on the verge, in one pass: wash, bank faces, terraces, solids. Draws after
    /// the ribbon so nothing softens a rail, and before the field so nothing occludes a runner.</summary>
    /// <param name="camS">The arc length the camera is looking at, for the wash window.</param>
    /// <param name="relief">The race's bank faces, drawn over the wash and under everything standing.</param>
    /// <param name="weather">The sky's element, for the wet and snow bank palettes.</param>
    /// <param name="clock">The drawn sim clock, for the three animated shapes.</param>
    /// <param name="reduceMotion">Freezes that clock and the gust. Every instance keeps its own phase,
    /// so the verge keeps its variety while nothing on it moves; the ice glints stand down too.</param>
    /// <param name="gust">The sky's live gust, signed by the prevailing lean, so the plants bend with
    /// the same air the marks ride.</param>
    public void Draw(ImDrawListPtr dl, AetherRaceLive.Track track, Vector2 origin, Vector2 size,
        float camS, in StageCam cam, RaceTerrainRelief.Profile relief, string weather, float clock, bool reduceMotion,
        float gust)
    {
        // Identity, not a count: every Route course has the same station count, so a length test
        // cannot tell a dressing baked for the wrong track from one baked for this one.
        if (!ReferenceEquals(this.built, track) || cam.Zoom <= 0f)
        {
            return;
        }

        var radius = cam.VisibleRadius(origin, size);
        var live = new Live(reduceMotion ? 0f : clock, reduceMotion ? 0f : gust, !reduceMotion,
            !reduceMotion && weather == "earth");
        this.DrawWash(dl, origin, size, camS, radius, in cam);
        this.DrawBanks(dl, track, origin, size, in cam, relief, weather);
        this.DrawTerraces(dl, origin, size, radius, in cam);
        this.DrawLandmarks(dl, origin, size, in cam, in live);
        this.DrawDoodads(dl, origin, size, radius, in cam, in live);
    }

    /// <summary>What moves this frame: the drawn clock, the signed gust, whether glints may fire, and
    /// whether dust curls at the foot of the earth props. All four are zero or false under reduced
    /// motion, so one struct carries the whole freeze.</summary>
    private readonly record struct Live(float Clock, float Gust, bool Shimmer, bool Dust);

    private readonly record struct DressTheme(
        Vector4 Wash,
        Vector4 Ink,
        Vector4 Fill,
        Vector4 Glow,
        byte KindA,
        byte KindB,
        byte KindC,
        byte Tall,
        float Density,
        float Lean,
        byte Material);

    /// <summary>One wash station: an anchor, the lateral unit, the four band offsets out from it and
    /// the three packed band colours. The eased zone colour and the grade tilt are already in
    /// them, so the draw path never touches a <see cref="Vector4"/>.</summary>
    private readonly record struct Station(
        Vector2 P,
        Vector2 N,
        float O0,
        float O1,
        float O2,
        float O3,
        uint Near,
        uint Mid,
        uint Far);

    /// <summary><see cref="Sides"/> is the mask of sides still drawn after the bank clearance: both
    /// at the bake, either or both dropped where a rung would cross a face.</summary>
    private readonly record struct Terrace(
        Vector2 P,
        Vector2 N,
        Vector2 F,
        float Half,
        float Pitch,
        uint Ink,
        float Radius,
        byte Sides);

    private readonly record struct Doodad(
        Vector2 World,
        float Scale,
        float Phase,
        uint Bits,
        byte Kind,
        byte Theme);

    /// <summary>One paired silhouette at a section's first view: the theme's tall kind with its
    /// third kind beside it. <see cref="Phase"/> is stored, never derived, so a landmark dropped by
    /// the clearance cannot shift the phase of the ones after it.</summary>
    private readonly record struct Landmark(
        Vector2 World,
        byte Theme,
        uint Bits,
        float Phase);

    private static int ThemeIndex(string element) => element switch
    {
        "fire" => 0,
        "wind" => 1,
        "water" => 2,
        "lightning" => 3,
        "earth" => 4,
        "ice" => 5,
        _ => 6,
    };

    private static Vector2[] BuildRing(int n)
    {
        var ring = new Vector2[n];
        for (var i = 0; i < n; i++)
        {
            var a = MathF.Tau * i / n;
            ring[i] = new Vector2(MathF.Cos(a), MathF.Sin(a));
        }

        return ring;
    }

    /// <summary>The seven terrains. Water and ice are the one pair the equal-luminance solve cannot
    /// separate on hue alone, so they are separated on CHROMA instead: water keeps most of its own
    /// blue and ice is mixed most of the way to the moss, which lands it as a pale near-neutral
    /// frost against water's saturated blue. Equalising those two mixes makes the two courses the
    /// same colour.</summary>
    private static DressTheme[] BuildThemes() =>
    [
        Theme("fire", 0.32f, WashLum, 1f, 0f, Boulder, Patch, Cone, Cone),
        Theme("wind", 0.34f, WashLum, 0.95f, 1f, Tuft, Flower, Fleck, Tuft),
        Theme("water", 0.24f, WashLum, 1f, 0.35f, Reed, Patch, Stone, Reed),
        Theme("lightning", 0.34f, WashLum, 1f, 0f, Spire, Tick, Shard, Spire),
        Theme("earth", 0.30f, WashLum, 0.95f, 0f, Boulder, Moss, Ledge, Boulder),
        Theme("ice", 0.52f, WashLum, 1.05f, 0.6f, Patch, Shard, Patch, Shard),
        Theme(string.Empty, 0.55f, QuietLum, 1.5f, 0f, Stone, Tuft, Stone, Stone),
    ];

    private static DressTheme Theme(string element, float groundMix, float washLuminance, float density,
        float lean, byte a, byte b, byte c, byte tall)
    {
        var look = ElementFx.For(element);
        var tint = look.Tint;

        // Earth alone splits: gold is its light, umber is its matter, and a solid is matter.
        var matter = look.Light == FxLight.Absorbs ? look.Body : tint;
        var wash = Vector4.Lerp(tint, Ground, groundMix * GroundMixScale);

        return new DressTheme(
            wash with { W = WashAlpha(wash, washLuminance) },
            tint with { W = 0.62f },
            Vector4.Lerp(matter, ElementFx.Night, 0.78f) with { W = 0.92f },
            Vector4.Lerp(tint, Vector4.One, 0.35f) with { W = 0.85f },
            a,
            b,
            c,
            tall,
            density,
            lean,
            (byte)ThemeIndex(element));
    }

    /// <summary>The alpha a wash must carry to land at a given luminance over the night beneath it.
    /// Solved, never authored: a tint that moves moves its alpha with it.</summary>
    private static float WashAlpha(in Vector4 wash, float target)
    {
        var night = ElementFx.Luminance(ElementFx.Night);
        var lit = ElementFx.Luminance(wash);
        return lit <= night + 0.0001f ? 0f : Math.Clamp((target - night) / (lit - night), 0f, 1f);
    }

    /// <summary>The stretch of road the ground is baked over: one lap on a lap course, the whole race
    /// otherwise.</summary>
    private static float BakedSpan(AetherRaceLive.Track track) => track.LapRows > 0 ? track.LapLength : track.Length;

    private static int StationCount(AetherRaceLive.Track track) => (int)(BakedSpan(track) / StationStep) + 2;

    /// <summary>The widest half-width within a couple of bounds either way. A solid that clears the
    /// road here must still clear it where the road is committing to a narrowing.</summary>
    private static float HalfWidth(AetherRaceLive.Track track, float s)
    {
        var half = track.At(s).Width * 0.5f;
        for (var d = -2.5f; d <= 2.5f; d += 1.25f)
        {
            var at = track.LapRows > 0 ? s + d : Math.Clamp(s + d, 0f, track.Length);
            half = MathF.Max(half, track.At(at).Width * 0.5f);
        }

        return half;
    }

    private void BakeGround(AetherRaceLive.Track track, byte[] rows)
    {
        var count = StationCount(track);
        var built = new Station[count];
        var rungs = new List<Terrace>();
        var wash = this.themes[this.baseTheme].Wash;
        var next = TapeClear;

        for (var i = 0; i < count; i++)
        {
            var s = MathF.Min(i * StationStep, BakedSpan(track));
            var here = track.At(s);
            wash = Vector4.Lerp(wash, this.themes[rows[track.RowAt(s)]].Wash, ZoneEase);

            var half = (here.Width * 0.5f) + Verge;
            var fit = Math.Clamp(here.Width / FitWidth, FitFloor, 1f);
            var tilt = here.Grade < 0f
                ? MathF.Min(DescentCap, -here.Grade * DescentGain)
                : -MathF.Min(ClimbCap, here.Grade * ClimbGain);
            // The verge fades into a deck's shadow rather than ending at a line across it.
            var alpha = wash.W * (1f + tilt) * (1f - track.DeckAt(s).Under);
            var n = new Vector2(-MathF.Sin(here.Heading), MathF.Cos(here.Heading));

            built[i] = new Station(
                new Vector2(here.X, here.Y),
                n,
                half,
                half + (BandEdge1 * fit),
                half + (BandEdge2 * fit),
                half + (BandEdge3 * fit),
                ElementFx.U32(wash with { W = alpha }),
                ElementFx.U32(wash with { W = alpha * Band2Alpha }),
                ElementFx.U32(wash with { W = alpha * Band3Alpha }));

            var g = MathF.Abs(here.Grade);
            if (s < next || g <= GradeFloor)
            {
                continue;
            }

            // Spacing runs from 22 bounds on a barely-tilted stretch to 11 on the steepest the
            // roster has, so it is carried on an accumulator rather than a modulo.
            var steep = Math.Clamp((g - GradeFloor) / SteepSpan, 0f, 1f);
            next = s + TerraceGap - (TerraceTight * steep);

            var climb = here.Grade > 0f;
            var pen = climb ? ClimbInk : FallInk;
            var lat = half + RungLatBase + ((RungsPerSide - 1) * RungLatStep);
            var fwd = ((RungsPerSide - 1) * RungPitch) + RungLength;
            rungs.Add(new Terrace(
                new Vector2(here.X, here.Y),
                n,
                new Vector2(MathF.Cos(here.Heading), MathF.Sin(here.Heading)),
                half,
                climb ? 1f : -1f,
                ElementFx.U32(pen with { W = pen.W * (SteepFloor + (SteepGain * steep)) }),
                MathF.Sqrt((lat * lat) + (fwd * fwd)),
                LeftSide | RightSide));
        }

        this.stations = built;
        this.looped = track.LapRows > 0;
        this.terraces = [.. rungs];
    }

    private void BakeDoodads(int seed, AetherRaceLive.Track track, AetherRaceLive.CourseDef course, byte[] rows)
    {
        var rng = new AetherRaceLive.RaceRng(unchecked((uint)seed ^ AetherRaceLive.Fnv1a32(course.Name)));
        var placed = new Doodad[Cap];
        var n = 0;
        var courseHalf = course.Width * 0.5f;

        for (var side = -1; side <= 1; side += 2)
        {
            var s = TapeClear + ((float)rng.Next() * TapeClear);
            while (s < BakedSpan(track) - TapeClear && n < Cap)
            {
                var here = track.At(s);
                var row = rows[track.RowAt(s)];
                var theme = this.themes[row];
                var half = HalfWidth(track, s);
                var open = Math.Clamp(half / OpenRef, OpenMin, OpenMax);
                var tight = half < courseHalf * TightFrac;
                var outside = here.Kappa != 0f && side != MathF.Sign(here.Kappa);
                var climb = MathF.Max(0f, here.Grade);

                var stride = (StrideBase + ((float)rng.Next() * StrideSpan))
                    * theme.Density
                    * (outside ? OutsideStride : 1f)
                    * (StrideOpenBase + (StrideOpenGain * open))
                    * (1f + (climb * ClimbStride));

                if (open > ThinOpen && rng.Next() < ThinChance)
                {
                    s += stride;
                    continue;
                }

                byte kind;
                if (tight && rng.Next() < TallChance)
                {
                    kind = theme.Tall;
                }
                else
                {
                    var roll = (float)rng.Next();
                    kind = roll < KindAWeight ? theme.KindA : roll < KindBWeight ? theme.KindB : theme.KindC;
                }

                var scale = (ScaleMin + ((float)rng.Next() * ScaleSpan))
                    * (tight ? TightScale : 1f)
                    * KindScale(kind);

                var u = (float)rng.Next();
                u *= u;
                if (outside)
                {
                    u *= OutsideBias;
                }

                // Shrink to fit, never displace: the offset is then drawn across the legal window,
                // so no ink on the road is a property of the placement rather than a clamp.
                var foot = Footprint(kind);
                var radius = MathF.Min(scale * foot, (Reach - Verge - RailClear) * 0.5f);
                scale = radius / foot;

                var lo = half + Verge + RailClear + radius;
                var hi = half + Reach - radius;
                var off = lo + (u * MathF.Max(0f, hi - lo) * MathF.Min(1f, OffBase + (OffGain * open)));
                var left = new Vector2(-MathF.Sin(here.Heading), MathF.Cos(here.Heading));
                var at = new Vector2(here.X, here.Y) + (left * (side * off));

                // Nothing stands on any part of the road: where a course crosses itself, the verge of
                // one piece of road is the middle of another.
                if (track.RoadClaims(at.X, at.Y, radius))
                {
                    s += stride;
                    continue;
                }

                var phase = (float)rng.Next() * MathF.Tau;
                var bits = unchecked((uint)(rng.Next() * BitsScale));

                // A quarter of the wind flecks lie down as low windstones. Decided after the
                // placement check and off bits already drawn, so nothing else on the stream moves.
                if (kind == Fleck && (bits & WindstoneMask) == 0u)
                {
                    kind = Stone;
                    scale *= WindstoneScale;
                }

                placed[n++] = new Doodad(at, scale, phase, bits, kind, row);
                s += stride;
            }
        }

        this.doodads = new Doodad[n];
        Array.Copy(placed, this.doodads, n);
    }

    /// <summary>At most six paired silhouettes, one at the first view of each named section, on
    /// alternating flanks. Placed through the same road claim as the scatter and resolved against
    /// the banks in <see cref="ClearScenery"/>. Off the seed and the arc length, never the RNG stream.</summary>
    private void BakeLandmarks(int seed, AetherRaceLive.Track track, byte[] rows)
    {
        var placed = new Landmark[LandmarkCap];
        var n = 0;
        var section = string.Empty;
        var span = BakedSpan(track);
        for (var s = LandmarkFirstS; s < span - LandmarkFirstS && n < LandmarkCap; s += LandmarkStep)
        {
            var here = track.At(s);
            if (here.Section == section)
            {
                continue;
            }

            section = here.Section;
            var flank = (n & 1) == 0 ? 1f : -1f;
            var left = new Vector2(-MathF.Sin(here.Heading), MathF.Cos(here.Heading));
            var at = new Vector2(here.X, here.Y) + (left * (flank * ((here.Width * 0.5f) + LandmarkOut)));
            if (track.RoadClaims(at.X, at.Y, LandmarkClaim))
            {
                continue;
            }

            placed[n] = new Landmark(at, rows[track.RowAt(s)], unchecked((uint)seed ^ ((uint)s * 2654435761u)), n);
            n++;
        }

        this.landmarks = new Landmark[n];
        Array.Copy(placed, this.landmarks, n);
    }

    /// <summary>A landmark's rendered envelope in bounds: the tall silhouette, or the side one
    /// with its screen offset, whichever reaches further.</summary>
    private static float LandmarkRadius(in DressTheme th) => MathF.Max(
        LandmarkTallScale * VisualFootprint(th.Tall),
        LandmarkSideOffset + (LandmarkSideScale * VisualFootprint(th.KindC)));

    /// <summary>The widest extent a kind can produce at full jitter, in multiples of its own scale,
    /// as a circle about its anchor: shadow, lean and standing height included, so the envelope holds
    /// at every camera rotation while the shape stays upright. The placement solve rests on this
    /// table, and it feeds the RNG stream through the scale clamp, so it is not retuned lightly.</summary>
    private static float Footprint(byte kind) => kind switch
    {
        Patch => 1.34f,
        Ledge => 1.04f,
        Stone => 1.00f,
        Spire => 1.55f,
        Cone => 1.45f,
        Tuft => 1.55f,
        Tick => 0.72f,
        Fleck => 0.70f,
        Moss => 0.68f,
        Shard => 1.45f,
        Reed => 1.55f,
        Flower => 1.45f,
        _ => 1.30f,
    };

    /// <summary>The rendered envelope the final clearance checks: the placement footprint, the
    /// drifting fleck's elevated halo, the moss bed's blades, and a stroke margin at the smallest
    /// visible size. Kept apart from <see cref="Footprint"/> so it never feeds the placement RNG.</summary>
    private static float VisualFootprint(byte kind) => (kind switch
    {
        Fleck => 1.30f,
        Moss => 0.85f,
        _ => Footprint(kind),
    }) + 0.10f;

    /// <summary>How a kind spends its height, so a flat one does not run away with the footprint and
    /// a tall one can afford more.</summary>
    private static float KindScale(byte kind) => kind switch
    {
        Patch => 0.82f,
        Ledge => 0.88f,
        Moss => 0.86f,
        Spire => 1.12f,
        Reed => 1.10f,
        Shard => 1.06f,
        Cone => 1.05f,
        _ => 1f,
    };

    /// <summary>Three filled bands a side, built as quads between offset polylines. Never a fat
    /// round-cap stroke: a soft band drawn as a stroke scallops every corner the road turns.
    ///
    /// <para>Every station in the window is walked. A stride that skipped stations was tried and
    /// removed: the chord it leaves cuts inside a bend, and holding that cut under the wash's own
    /// 0.10-bound verge is a WORLD-space constraint, which makes the legal stride 1 on every course
    /// the roster has. The saving it was reaching for is taken by the per-quad reject instead.</para></summary>
    private void DrawWash(ImDrawListPtr dl, Vector2 origin, Vector2 size, float camS, float radius,
        in StageCam cam)
    {
        // The half-width belongs in the window: a station's outermost band point sits at half plus
        // the outer offset, so a station just outside the eye's reach still has band 3 on stage.
        var window = radius + this.maxHalf + BandEdge3 + StationStep;

        // The window is arc length while the terraces and the solids cull on world distance. That
        // is only conservative while no course folds back on itself inside VisibleRadius; the
        // tightest radius the roster authors is 18 bounds, which does not.
        // A lap course draws every station: the camera's distance runs past one lap and the far side
        // of the circuit can be on stage. Band rejects each quad that is off it.
        var last = this.looped ? this.stations.Length - 1 : Math.Min(this.stations.Length - 1, (int)((camS + window) / StationStep) + 1);
        var first = this.looped ? 0 : Math.Max(0, (int)((camS - window) / StationStep));

        var have = false;
        Vector2 pl0 = default, pl1 = default, pl2 = default, pl3 = default;
        Vector2 pr0 = default, pr1 = default, pr2 = default, pr3 = default;

        for (var i = first; i <= last; i++)
        {
            ref readonly var st = ref this.stations[i];
            var c = cam.ToScreen(st.P);
            var n = cam.ToScreenDelta(st.N);
            var cl0 = c + (n * st.O0);
            var cl1 = c + (n * st.O1);
            var cl2 = c + (n * st.O2);
            var cl3 = c + (n * st.O3);
            var cr0 = c - (n * st.O0);
            var cr1 = c - (n * st.O1);
            var cr2 = c - (n * st.O2);
            var cr3 = c - (n * st.O3);

            if (have)
            {
                Band(dl, pl0, cl0, cl1, pl1, st.Near, origin, size);
                Band(dl, pr0, cr0, cr1, pr1, st.Near, origin, size);
                Band(dl, pl1, cl1, cl2, pl2, st.Mid, origin, size);
                Band(dl, pr1, cr1, cr2, pr2, st.Mid, origin, size);
                Band(dl, pl2, cl2, cl3, pl3, st.Far, origin, size);
                Band(dl, pr2, cr2, cr3, pr3, st.Far, origin, size);
            }

            pl0 = cl0;
            pl1 = cl1;
            pl2 = cl2;
            pl3 = cl3;
            pr0 = cr0;
            pr1 = cr1;
            pr2 = cr2;
            pr3 = cr3;
            have = true;
        }
    }

    /// <summary>One band between two stations, rejected on its own screen bounds. Under the track-up
    /// camera the road runs up the stage, so the two outer bands are usually off it laterally while
    /// the centreline is still well inside: four compares here save most of the pass.</summary>
    private static void Band(ImDrawListPtr dl, Vector2 a, Vector2 b, Vector2 c, Vector2 d, uint col,
        Vector2 origin, Vector2 size)
    {
        var minX = MathF.Min(MathF.Min(a.X, b.X), MathF.Min(c.X, d.X));
        if (minX > origin.X + size.X)
        {
            return;
        }

        var maxX = MathF.Max(MathF.Max(a.X, b.X), MathF.Max(c.X, d.X));
        if (maxX < origin.X)
        {
            return;
        }

        var minY = MathF.Min(MathF.Min(a.Y, b.Y), MathF.Min(c.Y, d.Y));
        if (minY > origin.Y + size.Y)
        {
            return;
        }

        var maxY = MathF.Max(MathF.Max(a.Y, b.Y), MathF.Max(c.Y, d.Y));
        if (maxY < origin.Y)
        {
            return;
        }

        dl.AddQuadFilled(a, b, c, d, col);
    }

    /// <summary>Terrace rungs, stacked outward from both rails and stepped along the running
    /// direction, so the pair reads as ground rising away rather than as parallel ticks. Every
    /// baked set draws at every zoom: at most a few dozen lines, and thinning them by zoom blinked
    /// half the stack in a single frame as the camera eased.</summary>
    private void DrawTerraces(ImDrawListPtr dl, Vector2 origin, Vector2 size, float radius, in StageCam cam)
    {
        var weight = Px(RungWeight);
        for (var i = 0; i < this.terraces.Length; i++)
        {
            ref readonly var t = ref this.terraces[i];
            var far = radius + t.Radius;
            if (Vector2.DistanceSquared(t.P, cam.Eye) > far * far)
            {
                continue;
            }

            var c = cam.ToScreen(t.P);
            var n = cam.ToScreenDelta(t.N);
            var f = cam.ToScreenDelta(t.F);
            for (var side = -1; side <= 1; side += 2)
            {
                if ((t.Sides & (side < 0 ? LeftSide : RightSide)) == 0)
                {
                    continue;
                }

                for (var r = 0; r < RungsPerSide; r++)
                {
                    var lat = t.Half + RungLatBase + (r * RungLatStep);
                    var from = c + (n * (side * lat)) + (f * (t.Pitch * r * RungPitch));
                    var to = from + (f * RungLength);
                    if (!StageCam.OnStage(from, origin, size, weight)
                        && !StageCam.OnStage(to, origin, size, weight))
                    {
                        continue;
                    }

                    dl.AddLine(from, to, t.Ink, weight);
                }
            }
        }
    }

    /// <summary>The bank faces: a broad face, a cap, a narrow foot shadow and a lit edge, with a
    /// sediment seam and a sparse fracture once the projected face is big enough to carry them. A
    /// cut bank (a canyon course) wears its cap on the outer side; an embankment wears it against the
    /// road. Every band is a fraction of the same footprint, so a tapered toe tapers all of them, and
    /// a collapsed end is drawn as a triangle. Omitted below <see cref="RaceRelief.MinZoom"/>.</summary>
    private void DrawBanks(ImDrawListPtr dl, AetherRaceLive.Track track, Vector2 origin, Vector2 size,
        in StageCam cam, RaceTerrainRelief.Profile relief, string weather)
    {
        if (cam.Zoom < Px(RaceRelief.MinZoom) || relief.Panels.Length == 0)
        {
            return;
        }

        var wet = weather == "water";
        var snow = weather == "ice";
        var seamPx = Px(BankSeamPx);
        var fracturePx = Px(BankFracturePx);
        var hair = Px(HairWeight);
        var fracture = ElementFx.U32(BankFractureInk);
        foreach (ref readonly var panel in relief.Panels.AsSpan())
        {
            var a = cam.ToScreen(panel.A);
            var b = cam.ToScreen(panel.B);
            var c = cam.ToScreen(panel.C);
            var d = cam.ToScreen(panel.D);
            if (!RaceRelief.OnStage(a, b, c, d, origin, size))
            {
                continue;
            }

            ref readonly var th = ref this.themes[this.rows[track.RowAt(panel.S)]];
            var across = (c + d) - (a + b);
            var normal = across.LengthSquared() > 0.001f ? Vector2.Normalize(across) : Vector2.UnitY;
            var lit = Math.Clamp(0.5f - (Vector2.Dot(normal, ElementFx.KeyTravel) * 0.5f), 0f, 1f);
            var fill = Vector4.Lerp(th.Fill, th.Ink, BankFillMix + (lit * BankFillLitGain));
            if (wet)
            {
                fill *= BankWetTint;
            }

            var cap = Vector4.Lerp(fill, snow ? BankSnowCap : th.Glow, snow ? BankSnowCapMix : BankCapMix + (lit * BankCapLitGain));
            RaceRelief.Face(dl, a, b, c, d, Ink(fill, BankFaceAlpha));

            var top0 = panel.Cut ? BankCapStart : 0f;
            var top1 = panel.Cut ? 1f : BankCapEnd;
            var ta = Vector2.Lerp(a, d, top0);
            var tb = Vector2.Lerp(b, c, top0);
            var tc = Vector2.Lerp(b, c, top1);
            var td = Vector2.Lerp(a, d, top1);
            RaceRelief.Face(dl, ta, tb, tc, td, Ink(cap, BankCapAlpha));

            var foot0 = panel.Cut ? 0f : BankFootStart;
            var foot1 = panel.Cut ? BankFootEnd : 1f;
            RaceRelief.Face(dl, Vector2.Lerp(a, d, foot0), Vector2.Lerp(b, c, foot0),
                Vector2.Lerp(b, c, foot1), Vector2.Lerp(a, d, foot1),
                Ink(BankFootInk, panel.Cut ? BankFootCut : BankFootFill));
            dl.AddLine(panel.Cut ? ta : td, panel.Cut ? tb : tc,
                Ink(th.Glow, wet ? BankEdgeWetAlpha : BankEdgeAlpha + (lit * BankEdgeLitGain)), hair);

            if (panel.Height * cam.Zoom <= seamPx)
            {
                continue;
            }

            dl.AddLine(Vector2.Lerp(a, d, RaceTerrainRelief.SeamFrac), Vector2.Lerp(b, c, RaceTerrainRelief.SeamFrac),
                Ink(th.Ink, BankSeamAlpha), hair);
            if ((panel.Detail & 7u) == 0u && panel.Height * cam.Zoom > fracturePx)
            {
                dl.AddLine(Vector2.Lerp(a, b, 0.47f),
                    Vector2.Lerp(Vector2.Lerp(a, b, 0.56f), Vector2.Lerp(d, c, 0.56f), 0.65f), fracture, hair);
            }
        }
    }

    private void DrawDoodads(ImDrawListPtr dl, Vector2 origin, Vector2 size, float radius,
        in StageCam cam, in Live live)
    {
        var lod = Px(LodPx);
        var fadeSpan = Px(LodFade);
        var detail = Px(DetailPx);
        var far = radius + DoodadReach;
        var farSq = far * far;
        var eye = cam.Eye;
        var lightAt = this.LightAt;
        Span<Vector2> poly = stackalloc Vector2[12];

        for (var i = 0; i < this.doodads.Length; i++)
        {
            ref readonly var d = ref this.doodads[i];
            var px = d.Scale * cam.Zoom;
            var fade = Math.Clamp((px - lod) / fadeSpan, 0f, 1f);
            if (fade <= 0f)
            {
                continue;
            }

            // World distance before the transform: correct across a hairpin, and it skips the
            // projection on most rejects.
            if (Vector2.DistanceSquared(d.World, eye) > farSq)
            {
                continue;
            }

            var at = cam.ToScreen(d.World);
            if (!StageCam.OnStage(at, origin, size, px * PadFrac))
            {
                continue;
            }

            var light = lightAt?.Invoke(d.World) ?? 0f;
            var theme = Lit(in this.themes[d.Theme], light, live.Clock, d.Phase);
            var detailed = px >= detail;
            DrawShape(dl, at, px, in theme, d.Kind, d.Phase, d.Bits, live.Clock, detailed, fade, live, poly);
            if (detailed)
            {
                DustCurl(dl, at, px, in theme, live.Clock, d.Phase, d.Bits, fade, in live);
            }
        }
    }

    /// <summary>The paired landmarks, each its theme's tall kind with the third kind set beside it in
    /// screen space. Both take the same light and gust as the scatter. Nothing under
    /// <see cref="LandmarkMinZoom"/>: pulled back that far they are two more smudges.</summary>
    private void DrawLandmarks(ImDrawListPtr dl, Vector2 origin, Vector2 size, in StageCam cam, in Live live)
    {
        if (cam.Zoom < Px(LandmarkMinZoom) || this.landmarks.Length == 0)
        {
            return;
        }

        var detail = Px(DetailPx);
        var lightAt = this.LightAt;
        Span<Vector2> poly = stackalloc Vector2[12];
        for (var i = 0; i < this.landmarks.Length; i++)
        {
            ref readonly var l = ref this.landmarks[i];
            ref readonly var th = ref this.themes[l.Theme];
            var at = cam.ToScreen(l.World);
            if (!StageCam.OnStage(at, origin, size, cam.Zoom * LandmarkRadius(in th)))
            {
                continue;
            }

            var light = lightAt?.Invoke(l.World) ?? 0f;
            var theme = Lit(in th, light, live.Clock, l.Phase);
            var side = cam.Zoom * LandmarkSideScale;
            DrawShape(dl, at + new Vector2(cam.Zoom * LandmarkSideOffset, 0f), side, in theme, th.KindC, l.Phase,
                l.Bits ^ LandmarkSideSalt, live.Clock, side >= detail, 1f, live, poly);
            var tall = cam.Zoom * LandmarkTallScale;
            DrawShape(dl, at, tall, in theme, th.Tall, l.Phase, l.Bits, live.Clock, tall >= detail, 1f, live, poly);
        }
    }

    /// <summary>The theme as this instance sees it this frame: transient light warms the fill toward
    /// the glow and the glow toward a warm white, and wet stone carries a slow moving highlight.</summary>
    private static DressTheme Lit(in DressTheme th, float light, float clock, float phase)
    {
        var theme = th;
        if (light > 0.001f)
        {
            theme = theme with
            {
                Fill = Vector4.Lerp(th.Fill, th.Glow with { W = th.Fill.W }, light * LightFillMix),
                Glow = Vector4.Lerp(th.Glow, LightWarm with { W = th.Glow.W }, light * LightGlowMix),
            };
        }

        if (th.Material == MatWet)
        {
            var wet = WetSwing * (0.5f + (0.5f * MathF.Sin((clock * WetRate) + phase)));
            theme = theme with { Glow = Vector4.Lerp(theme.Glow, Vector4.One with { W = theme.Glow.W }, wet) };
        }

        return theme;
    }

    /// <summary>Six independent five-bit slices off one baked word, so a shape's variety costs one
    /// uint at placement and nothing per frame.</summary>
    private static float Jitter(uint bits, int slot) => ((bits >> ((slot % 6) * 5)) & 31u) * (1f / 31f);

    private static uint Ink(in Vector4 c, float alpha) => ElementFx.U32(c with { W = alpha });

    private static float Profile(float d, int kind) => kind switch
    {
        0 => 1f - d,
        1 => MathF.Sqrt(MathF.Max(0f, 1f - (d * d))),
        _ => 1f - (d * d * d),
    };

    /// <summary>A solid's silhouette: a flat ground base with a chain of facets over it. The facet
    /// spacing wobbles by under half a gap so the chain stays monotonic in x and the polygon stays
    /// convex, which a jittered radius would not.</summary>
    private static int CapPoly(Span<Vector2> poly, Vector2 p, float w, float h, int caps, float apex,
        int profL, int profR, float shear, float wobble)
    {
        poly[0] = p + new Vector2(-w * 0.5f, 0f);
        for (var i = 1; i <= caps; i++)
        {
            var u = ((float)i / (caps + 1)) + (MathF.Sin((wobble * 3.1f) + (i * 2.399f)) * (0.34f / (caps + 1)));
            var lit = u < apex;
            var span = MathF.Max(0.08f, lit ? apex : 1f - apex);
            var d = MathF.Min(1f, MathF.Abs(u - apex) / span);
            var y = -h * Profile(d, lit ? profL : profR);
            poly[i] = p + new Vector2(((u - 0.5f) * w) + (shear * -y), y);
        }

        poly[caps + 1] = p + new Vector2(w * 0.5f, 0f);
        return caps + 2;
    }

    /// <summary>A quadratic bend sampled to four points, because a four-point polyline is one
    /// drawlist call where the curve primitive is one call plus its tessellation.</summary>
    private static void Bend(Span<Vector2> poly, Vector2 from, Vector2 ctrl, Vector2 to)
    {
        poly[0] = from;
        poly[1] = (from * (4f / 9f)) + (ctrl * (4f / 9f)) + (to * (1f / 9f));
        poly[2] = (from * (1f / 9f)) + (ctrl * (4f / 9f)) + (to * (4f / 9f));
        poly[3] = to;
    }

    /// <summary>The contact ellipse under a solid, offset the way the house light travels.</summary>
    private static void Shadow(ImDrawListPtr dl, Span<Vector2> poly, Vector2 p, float half, uint col)
    {
        var c = p + (ElementFx.KeyTravel * (half * 0.14f));
        for (var i = 0; i < Ring8.Length; i++)
        {
            poly[i] = c + new Vector2(Ring8[i].X * half * 1.06f, Ring8[i].Y * half * 0.19f);
        }

        dl.AddConvexPolyFilled(ref poly[0], Ring8.Length, col);
    }

    /// <summary>Broad value planes inside a convex silhouette: the top catches the upper-left key, the
    /// lit flank takes a share of it, the far flank and the foot sink toward the night. Every face
    /// vertex is the silhouette's own or the hub inside it, so no face reaches past the placement
    /// bounds. Under <see cref="FacesPx"/> a single fill; the ink stroke closes the silhouette either
    /// way and the buried base is never ruled bright. A material mark lands on a quarter of the
    /// instances above <see cref="MarksPx"/>. At most seven faces, from a seven-vertex boulder.</summary>
    /// <param name="poly">The silhouette, base-left round the top to base-right. Scratch after the stroke.</param>
    private static void DrawSolid(ImDrawListPtr dl, Span<Vector2> poly, int n, in DressTheme th, float px,
        uint bits, float phase, float t, float fade, bool shimmer, bool marks)
    {
        var wt = Math.Clamp(px * SilhouetteFrac, Px(SilhouetteMin), Px(SilhouetteMax));
        var hub = Vector2.Zero;
        var top = 0;
        for (var i = 0; i < n; i++)
        {
            hub += poly[i];
            if (poly[i].Y < poly[top].Y)
            {
                top = i;
            }
        }

        hub /= n;
        hub = Vector2.Lerp(hub, poly[top], HubTopPull);
        if (px >= Px(FacesPx))
        {
            var lift = CrownLift(th.Material);
            var crown = Ink(Vector4.Lerp(th.Fill, th.Glow, lift), th.Fill.W * fade);
            var flank = Ink(Vector4.Lerp(th.Fill, th.Glow, lift * FlankShare), th.Fill.W * fade);
            var dark = Ink(Vector4.Lerp(th.Fill, ElementFx.Night, DarkFlankMix), th.Fill.W * fade);

            // Shared face edges must not fringe, or the facets read as a wire mesh; the stroke below
            // smooths the outer silhouette.
            var flags = dl.Flags;
            dl.Flags &= ~ImDrawListFlags.AntiAliasedFill;
            for (var i = 0; i < n; i++)
            {
                var a = poly[i];
                var b = poly[(i + 1) % n];
                var edge = b - a;
                var length = edge.Length();
                var light = length > 0.001f ? Vector2.Dot(new Vector2(edge.Y, -edge.X) / length, ElementFx.KeyLight) : 0f;
                dl.AddTriangleFilled(a, b, hub, light > CrownDot ? crown : light > FlankDot ? flank : dark);
            }

            dl.Flags = flags;
        }
        else
        {
            dl.AddConvexPolyFilled(ref poly[0], n, Ink(th.Fill, th.Fill.W * fade));
        }

        dl.AddPolyline(ref poly[0], n, Ink(th.Ink, th.Ink.W * fade), ImDrawFlags.Closed, wt);
        if (!marks || px < Px(MarksPx) || (bits & WindstoneMask) != 0u)
        {
            return;
        }

        var hair = Px(HairWeight);
        var left = Vector2.Lerp(poly[0], hub, 0.54f);
        var right = Vector2.Lerp(poly[n - 1], hub, 0.62f);
        var high = Vector2.Lerp(poly[top], hub, 0.56f);
        var low = Vector2.Lerp((poly[0] + poly[n - 1]) * 0.5f, hub, 0.48f);
        var shoulder = Vector2.Lerp(poly[Math.Max(1, top - 1)], hub, 0.42f);
        var cut = Ink(ElementFx.Night, 0.44f * fade);
        switch (th.Material)
        {
            case MatBasalt:
            {
                // One recessed seam with a dim hot interior.
                poly[0] = high;
                poly[1] = hub;
                poly[2] = low;
                dl.AddPolyline(ref poly[0], 3, cut, ImDrawFlags.None, MathF.Max(hair * 1.5f, px * 0.04f));
                var ember = 0.19f + (0.07f * MathF.Sin((t * 1.1f) + phase));
                dl.AddLine(high, hub, Ink(th.Glow, ember * fade), MathF.Max(hair, px * 0.016f));
                break;
            }

            case MatWindstone:
            case MatEarth:
            {
                // Shallow abrasion along the long axis; earth's is a broken sediment seam.
                poly[0] = left;
                poly[1] = Vector2.Lerp(hub, low, 0.3f);
                poly[2] = right;
                dl.AddPolyline(ref poly[0], 3, cut, ImDrawFlags.None, MathF.Max(hair, px * 0.018f));
                dl.AddLine(Vector2.Lerp(left, high, 0.10f), Vector2.Lerp(hub, high, 0.12f),
                    Ink(th.Glow, (th.Material == MatWindstone ? 0.16f : 0.20f) * fade), hair);
                break;
            }

            case MatWet:
            {
                // A short broad reflection, its end lost in the surface.
                dl.AddBezierQuadratic(left, shoulder, high, Ink(th.Glow, 0.38f * fade), MathF.Max(hair, px * 0.034f), 4);
                break;
            }

            case MatStorm:
            case MatIce:
            {
                // A restrained inner facet; ice is cooler and glints now and then.
                dl.AddLine(high, low, Ink(th.Glow, (th.Material == MatIce ? 0.33f : 0.22f) * fade), hair);
                if (th.Material == MatIce && shimmer)
                {
                    var glint = MathF.Max(0f, MathF.Sin((t * 0.7f) + phase) - 0.94f) / 0.06f;
                    if (glint > 0f)
                    {
                        var reach = px * 0.022f;
                        var colour = Ink(th.Glow, glint * 0.30f * fade);
                        dl.AddLine(hub - new Vector2(reach, 0f), hub + new Vector2(reach, 0f), colour, hair);
                        dl.AddLine(hub - new Vector2(0f, reach * 1.6f), hub + new Vector2(0f, reach * 1.6f), colour, hair);
                    }
                }

                break;
            }

            default:
            {
                // Quiet ground keeps a small matte lichen patch.
                dl.AddTriangleFilled(left, Vector2.Lerp(left, high, 0.42f), Vector2.Lerp(left, hub, 0.55f),
                    Ink(Ground, 0.30f * fade));
                break;
            }
        }
    }

    /// <summary>How far a material's crown lifts toward the glow: ice and storm rock read as
    /// translucent, basalt and quiet stone stay matte.</summary>
    private static float CrownLift(byte material) => material switch
    {
        MatBasalt => 0.19f,
        MatWet => 0.24f,
        MatStorm => 0.29f,
        MatIce => 0.34f,
        MatWindstone => 0.23f,
        MatEarth => 0.23f,
        _ => 0.17f,
    };

    /// <summary>A curl of dust lifting off the foot of an earth prop under the dust sky, on a quarter
    /// of the instances. Frozen with the clock.</summary>
    private static void DustCurl(ImDrawListPtr dl, Vector2 p, float px, in DressTheme th, float t, float phase,
        uint bits, float fade, in Live live)
    {
        if (!live.Dust || th.Material != MatEarth || (bits & WindstoneMask) != 0u)
        {
            return;
        }

        var drift = ((t * 0.35f) + phase) % 1f;
        var lift = MathF.Sin(drift * MathF.PI);
        var at = p + new Vector2((drift - 0.5f) * px * 0.55f, -px * 0.04f);
        dl.AddBezierQuadratic(at, at + new Vector2(px * 0.10f, -px * 0.17f), at + new Vector2(px * 0.24f, -px * 0.03f),
            Ink(th.Ink, lift * 0.22f * fade), Px(HairWeight), 5);
    }

    /// <summary>One solid, upright in screen space from a ground anchor. Two stroke weights and
    /// never one: the silhouette tracks the camera and the interior detail stays a hairline.</summary>
    /// <param name="detail">Whether the instance is big enough to earn its trimmings.</param>
    /// <param name="fade">The dissolve at the bottom of the size range, multiplied into every alpha
    /// so a shrinking solid leaves rather than blinks.</param>
    private static void DrawShape(ImDrawListPtr dl, Vector2 p, float px, in DressTheme th, byte kind,
        float phase, uint bits, float t, bool detail, float fade, in Live live, Span<Vector2> poly)
    {
        var lean = th.Lean != 0f ? th.Lean : (phase < MathF.PI ? 1f : -1f);
        if (kind is Tuft or Reed or Flower)
        {
            // One gust for the ground and the air: the plants bend with the marks the sky carries.
            lean = (lean * (1f + (BreezeSwing * MathF.Sin((t * BreezeRate) + phase)))) + (live.Gust * GustBend);
        }

        var wt = Math.Clamp(px * SilhouetteFrac, Px(SilhouetteMin), Px(SilhouetteMax));
        var hair = Px(HairWeight);
        var ink = Ink(th.Ink, th.Ink.W * fade);
        var fill = Ink(th.Fill, th.Fill.W * fade);
        var shade = Ink(ElementFx.Night, ShadowAlpha * fade);

        switch (kind)
        {
            case Boulder:
            {
                var h = px * (0.70f + (0.30f * Jitter(bits, 0)));
                var w = h * (0.95f + (0.50f * Jitter(bits, 1)));
                var caps = 3 + (int)(Jitter(bits, 2) * 2.99f);
                var apex = 0.30f + (0.40f * Jitter(bits, 3));
                Shadow(dl, poly, p, w * 0.5f, shade);

                var n = CapPoly(poly, p, w, h, caps, apex, (int)(Jitter(bits, 4) * 2.99f),
                    (int)(Jitter(bits, 5) * 2.99f), (Jitter(bits, 0) - 0.5f) * 0.22f, phase);
                DrawSolid(dl, poly, n, in th, px, bits, phase, t, fade, live.Shimmer, marks: true);
                break;
            }

            case Stone:
            {
                var h = px * (0.42f + (0.22f * Jitter(bits, 0)));
                var w = h * (1.50f + (0.60f * Jitter(bits, 1)));
                Shadow(dl, poly, p, w * 0.5f, shade);

                var n = CapPoly(poly, p, w, h, 3 + (int)(Jitter(bits, 2) * 1.99f),
                    0.36f + (0.28f * Jitter(bits, 3)), 1, 1, (Jitter(bits, 4) - 0.5f) * 0.16f, phase);
                DrawSolid(dl, poly, n, in th, px, bits, phase, t, fade, live.Shimmer, marks: true);
                break;
            }

            case Cone:
            {
                var h = px * (0.82f + (0.34f * Jitter(bits, 0)));
                var w = px * (0.52f + (0.18f * Jitter(bits, 1)));
                var rim = w * (0.30f + (0.16f * Jitter(bits, 2)));

                // The top leans, the base does not: a leaning cone reads as a broken triangle.
                var tilt = (Jitter(bits, 3) - 0.5f) * w * 0.4f;
                Shadow(dl, poly, p, w, shade);

                var bl = p + new Vector2(-w, 0f);
                var br = p + new Vector2(w, 0f);
                var tl = p + new Vector2(-rim + tilt, -h);
                var tr = p + new Vector2(rim + tilt, -h * (0.92f + (0.12f * Jitter(bits, 4))));
                poly[0] = bl;
                poly[1] = tl;
                poly[2] = tr;
                poly[3] = br;
                DrawSolid(dl, poly, 4, in th, px, bits, phase, t, fade, live.Shimmer, marks: false);

                var throat = Vector2.Lerp(tl, tr, 0.5f) + new Vector2(0f, rim * 0.55f);
                dl.AddTriangleFilled(tl, tr, throat, Ink(ElementFx.Night, 0.90f * fade));
                dl.AddTriangleFilled(Vector2.Lerp(tl, throat, 0.35f), Vector2.Lerp(tr, throat, 0.35f), throat,
                    Ink(th.Glow, (0.28f + (0.14f * MathF.Sin((t * 1.7f) + phase))) * fade));
                if (detail)
                {
                    dl.AddLine(Vector2.Lerp(tl, tr, 0.3f), p + new Vector2(-w * 0.5f, 0f),
                        Ink(th.Glow, 0.30f * fade), wt * 0.8f);
                }

                break;
            }

            case Spire:
            {
                var h = px * (0.92f + (0.26f * Jitter(bits, 0)));
                var w = px * (0.30f + (0.14f * Jitter(bits, 1)));
                Shadow(dl, poly, p, w * 1.4f, shade);

                var n = CapPoly(poly, p, w * 2f, h, 2 + (int)(Jitter(bits, 3) * 1.99f),
                    0.24f + (0.52f * Jitter(bits, 2)), 0, 0, (Jitter(bits, 4) - 0.5f) * 0.45f, phase);
                DrawSolid(dl, poly, n, in th, px, bits, phase, t, fade, live.Shimmer, marks: true);
                if (detail)
                {
                    // A convex polygon cannot be jagged, so the jaggedness is a cluster at its foot.
                    var teeth = 1 + (int)(Jitter(bits, 5) * 1.99f);
                    for (var i = 0; i < teeth; i++)
                    {
                        var tj = Jitter(bits, i);
                        var dir = i == 0 ? -1f : 1f;
                        var bx = dir * w * (0.95f + (0.35f * tj));
                        var hgt = h * (0.28f + (0.24f * tj));
                        var b0 = p + new Vector2(bx - (w * 0.42f), 0f);
                        var b1 = p + new Vector2(bx + (w * 0.42f), 0f);
                        var tip = p + new Vector2(bx + (dir * w * 0.18f), -hgt);
                        dl.AddTriangleFilled(b0, b1, tip, fill);
                        dl.AddTriangleFilled(Vector2.Lerp(b0, b1, 0.48f), b1, tip, Ink(ElementFx.Night, 0.30f * fade));
                        dl.AddLine(dir < 0f ? b0 : b1, tip, ink, hair);
                    }
                }

                break;
            }

            case Shard:
            {
                var shards = 2 + (int)(Jitter(bits, 0) * 1.99f);
                var mast = (int)(Jitter(bits, 5) * (shards - 0.01f));
                Shadow(dl, poly, p, px * 0.40f, shade);

                for (var i = 0; i < shards; i++)
                {
                    var sj = Jitter(bits, i + 1);
                    var hgt = px * (i == mast ? 0.88f + (0.26f * sj) : 0.34f + (0.30f * sj));
                    var bw = px * (0.11f + (0.07f * sj));
                    var bx = px * 0.28f * (i - ((shards - 1) * 0.5f));
                    var b0 = p + new Vector2(bx - bw, 0f);
                    var b1 = p + new Vector2(bx + bw, 0f);
                    var tip = p + new Vector2(bx + (((sj - 0.5f) + (bx / px)) * hgt * 0.22f), -hgt);
                    if (i != mast)
                    {
                        dl.AddTriangleFilled(b0, b1, tip, fill);
                        continue;
                    }

                    poly[0] = b0;
                    poly[1] = tip;
                    poly[2] = b1;
                    DrawSolid(dl, poly, 3, in th, px, bits, phase, t, fade, live.Shimmer, marks: true);
                }

                break;
            }

            case Patch:
            {
                // Irregular in ANGLE and never in radius: a wobbled radius is not convex.
                var n = 7 + (int)(Jitter(bits, 0) * 2.99f);
                var w = px * (0.90f + (0.42f * Jitter(bits, 1)));
                var h = w * (0.30f + (0.20f * Jitter(bits, 2)));
                for (var i = 0; i < n; i++)
                {
                    var a = (MathF.Tau * i / n) + (phase * 0.15f) + (MathF.Sin((phase * 2.7f) + (i * 1.7f)) * (0.9f / n));
                    poly[i] = p + new Vector2(MathF.Cos(a) * w, MathF.Sin(a) * h);
                }

                dl.AddConvexPolyFilled(ref poly[0], n, Ink(th.Ink, 0.26f * fade));
                dl.AddPolyline(ref poly[0], n, Ink(th.Glow, 0.24f * fade), ImDrawFlags.Closed,
                    MathF.Max(hair, wt * 0.75f));
                if (detail)
                {
                    poly[0] = p + new Vector2(-w * 0.58f, h * 0.12f);
                    poly[1] = p + new Vector2(w * 0.08f, -h * 0.24f);
                    poly[2] = p + new Vector2(w * 0.64f, h * 0.06f);
                    dl.AddPolyline(ref poly[0], 3,
                        Ink(th.Glow, (0.24f + (0.22f * MathF.Sin((t * 1.3f) + phase))) * fade),
                        ImDrawFlags.None, hair);

                    for (var i = 0; i < 4; i++)
                    {
                        var a = (MathF.PI * 1.02f) + (MathF.PI * 0.16f * i);
                        poly[i] = p + new Vector2(MathF.Cos(a) * w * 0.97f, MathF.Sin(a) * h * 0.97f);
                    }

                    dl.AddPolyline(ref poly[0], 4, Ink(th.Glow, 0.46f * fade), ImDrawFlags.None, hair);
                }

                break;
            }

            case Tuft:
            {
                var blades = detail ? 4 + (int)(Jitter(bits, 0) * 2.99f) : 2;
                var spread = px * (0.22f + (0.13f * Jitter(bits, 1)));
                var n = CapPoly(poly, p, spread * 2.2f, px * 0.19f, 3, 0.44f, 1, 1, 0f, phase);
                dl.AddConvexPolyFilled(ref poly[0], n, Ink(th.Ink, 0.32f * fade));

                for (var i = 0; i < blades; i++)
                {
                    var u = blades > 1 ? (float)i / (blades - 1) : 0.5f;
                    var bj = Jitter(bits, i + 2);
                    var root = p + new Vector2((u - 0.5f) * 2f * spread, -px * 0.06f);
                    var hgt = px * (0.55f + (0.50f * bj) - (0.25f * MathF.Abs(u - 0.5f)));
                    var bend = lean * px * (0.15f + (0.20f * bj));
                    Bend(poly, root, root + new Vector2(bend * 0.2f, -hgt * 0.62f),
                        root + new Vector2(bend, -hgt));
                    var front = bj > 0.55f;
                    dl.AddPolyline(ref poly[0], 4,
                        front ? ink : Ink(th.Ink, th.Ink.W * 0.52f * fade),
                        ImDrawFlags.None, front ? wt : hair);
                }

                break;
            }

            case Reed:
            {
                var stems = detail ? 3 + (int)(Jitter(bits, 0) * 2.99f) : 3;
                var spread = px * (0.14f + (0.12f * Jitter(bits, 1)));
                if (detail)
                {
                    Shadow(dl, poly, p, spread * 1.25f, shade);
                }

                for (var i = 0; i < stems; i++)
                {
                    var u = stems > 1 ? (float)i / (stems - 1) : 0.5f;
                    var sj = Jitter(bits, i + 2);
                    var root = p + new Vector2((u - 0.5f) * 2f * spread, 0f);
                    var hgt = px * (0.55f + (0.55f * sj));
                    var top = root + new Vector2(((lean * 0.5f) + (u - 0.5f)) * px * 0.20f, -hgt);
                    dl.AddLine(root, top, ink, sj > 0.55f ? wt : hair);
                    if (detail && sj > 0.62f)
                    {
                        var dir = Vector2.Normalize(top - root);
                        var across = new Vector2(-dir.Y, dir.X) * px * 0.055f;
                        var foot = top - (dir * px * 0.20f);
                        dl.AddQuadFilled(foot, foot + across, top, foot - across,
                            Ink(th.Glow, 0.62f * fade));
                    }
                }

                break;
            }

            case Flower:
            {
                var stemH = px * (0.55f + (0.35f * Jitter(bits, 0)));
                var bend = lean * px * 0.16f;
                var head = p + new Vector2(bend, -stemH);
                var r = px * (0.15f + (0.07f * Jitter(bits, 1)));
                Bend(poly, p, p + new Vector2(bend * 0.25f, -stemH * 0.6f), head);
                dl.AddPolyline(ref poly[0], 4, ink, ImDrawFlags.None, wt);

                var petal = Ink(th.Glow, 0.72f * fade);
                if (detail)
                {
                    for (var i = 0; i < 5; i++)
                    {
                        var a = phase + (MathF.Tau * i / 5f);
                        dl.AddCircleFilled(head + new Vector2(MathF.Cos(a) * r, MathF.Sin(a) * r), r * 0.62f, petal, 8);
                    }
                }
                else
                {
                    // Five petals at a few pixels across are a smudge either way.
                    dl.AddCircleFilled(head, r * 1.35f, petal, 8);
                }

                dl.AddCircleFilled(head, r * 0.52f, ink, 8);
                if (detail)
                {
                    dl.AddCircleFilled(p + new Vector2(-lean * px * 0.20f, -stemH * (0.40f + (0.22f * Jitter(bits, 2)))),
                        r * 0.5f, petal, 8);
                }

                break;
            }

            case Fleck:
            {
                var at = p + new Vector2(
                    lean * px * 0.35f * MathF.Sin((t * 0.8f) + phase),
                    -px * (0.62f + (0.22f * MathF.Cos((t * 1.1f) + phase))));
                var r = px * (0.10f + (0.05f * Jitter(bits, 0)));
                dl.AddCircleFilled(at, r * 2f, Ink(th.Glow, 0.16f * fade), 8);
                dl.AddCircleFilled(at, r, Ink(th.Glow, 0.80f * fade), 8);
                if (detail)
                {
                    var fils = 3 + (int)(Jitter(bits, 1) * 1.99f);
                    var fil = Ink(th.Ink, 0.34f * fade);
                    for (var i = 0; i < fils; i++)
                    {
                        var a = phase + (MathF.Tau * i / fils);
                        dl.AddLine(at, at + (new Vector2(MathF.Cos(a), MathF.Sin(a)) * r * 2f), fil, hair);
                    }
                }

                break;
            }

            case Moss:
            {
                var h = px * (0.30f + (0.16f * Jitter(bits, 0)));
                var w = px * (0.95f + (0.40f * Jitter(bits, 1)));
                var n = CapPoly(poly, p, w, h, 3 + (int)(Jitter(bits, 2) * 1.99f),
                    0.35f + (0.30f * Jitter(bits, 3)), 2, 2, 0f, phase);
                dl.AddConvexPolyFilled(ref poly[0], n, Ink(th.Ink, 0.40f * fade));
                if (detail)
                {
                    // Restroked OPEN, so no rule is drawn along the ground line.
                    dl.AddPolyline(ref poly[0], n, Ink(th.Glow, 0.20f * fade), ImDrawFlags.None,
                        MathF.Max(hair, wt * 0.7f));

                    var blade = Ink(th.Ink, 0.55f * fade);
                    for (var i = 0; i < 3; i++)
                    {
                        var mj = Jitter(bits, i + 3);
                        var root = p + new Vector2(((i * 0.5f) - 0.5f) * w * 0.7f, -h * 0.55f);
                        dl.AddLine(root, root + new Vector2(lean * px * 0.06f, -px * (0.12f + (0.12f * mj))),
                            blade, hair);
                    }
                }

                break;
            }

            case Tick:
            {
                var w = px * (0.50f + (0.22f * Jitter(bits, 0)));
                var h = w * (0.30f + (0.14f * Jitter(bits, 1)));
                var cos = MathF.Cos(phase);
                var sin = MathF.Sin(phase);
                for (var i = 0; i < Ring7.Length; i++)
                {
                    var u = Ring7[i];
                    poly[i] = p + new Vector2(((u.X * cos) - (u.Y * sin)) * w, ((u.X * sin) + (u.Y * cos)) * h);
                }

                // Burnt ground is the stage's own night showing through, not the element's ink.
                dl.AddConvexPolyFilled(ref poly[0], Ring7.Length, Ink(ElementFx.Night, 0.45f * fade));

                var fork = p + new Vector2(-w * 0.12f, -h * 0.34f);
                poly[0] = p + new Vector2(-w * 0.82f, h * 0.32f);
                poly[1] = fork;
                poly[2] = p + new Vector2(w * 0.42f, h * 0.14f);
                poly[3] = p + new Vector2(w * 0.88f, -h * 0.40f);
                dl.AddPolyline(ref poly[0], 4, ink, ImDrawFlags.None, wt);
                if (detail)
                {
                    dl.AddLine(fork, fork + new Vector2(w * 0.28f, -h * 1.10f),
                        Ink(th.Glow, 0.40f * fade), hair);
                }

                break;
            }

            default:
            {
                var w = px * (0.60f + (0.26f * Jitter(bits, 0)));
                var h = px * (0.26f + (0.16f * Jitter(bits, 1)));
                var skew = (Jitter(bits, 2) - 0.5f) * w * 0.30f;
                Shadow(dl, poly, p, w * 0.82f, shade);

                var b0 = p + new Vector2(-w, 0f);
                var b1 = p + new Vector2(w, 0f);
                var b2 = p + new Vector2((w * 0.90f) + (skew * 0.3f), -h);
                var b3 = p + new Vector2((-w * 0.86f) + (skew * 0.3f), -h * 0.92f);
                poly[0] = b0;
                poly[1] = b3;
                poly[2] = b2;
                poly[3] = b1;
                DrawSolid(dl, poly, 4, in th, px, bits, phase, t, fade, live.Shimmer, marks: false);

                var uw = w * (0.52f + (0.22f * Jitter(bits, 3)));
                var uy = -h * 0.94f;
                var u0 = p + new Vector2(-uw + skew, uy);
                var u1 = p + new Vector2(uw + skew, uy);
                var u2 = p + new Vector2(uw + skew, uy - (h * 0.85f));
                var u3 = p + new Vector2(-uw + skew, uy - (h * 0.76f));
                poly[0] = u0;
                poly[1] = u3;
                poly[2] = u2;
                poly[3] = u1;
                DrawSolid(dl, poly, 4, in th, px, bits, phase, t, fade, live.Shimmer, marks: false);
                if (detail && (bits & WindstoneMask) == 0u)
                {
                    // The recess where the upper block beds into the slab.
                    dl.AddLine(Vector2.Lerp(u0, u1, 0.16f), Vector2.Lerp(u0, u1, 0.68f),
                        Ink(ElementFx.Night, 0.50f * fade), MathF.Max(hair, wt * 0.85f));
                }

                break;
            }
        }
    }
}
