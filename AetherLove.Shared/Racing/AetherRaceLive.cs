namespace AetherLove.Shared.Racing;

/// <summary>
/// The continuous race engine, ported bit-exact from the Aetherling prototype.
///
/// <para><b>The one law: the sim is pure and seeded.</b> Fixed timestep, one RNG
/// (<see cref="RaceRng"/>, mulberry32, never <see cref="Random"/>), no clocks, no player input
/// during the run. Hand it a seed, a course, a weather and a field and the whole race is already
/// decided: a replay re-derives the result rather than showing it.</para>
///
/// <para>Units: 1 unit = one body length ("a bound"). Speeds ~9-12 bounds/s. Time in seconds,
/// stepped at <see cref="Dials.Dt"/>. Lateral: 0 is the centre line, positive is LEFT of the
/// running direction. Curvature kappa is positive turning LEFT, so the inside of a turn is
/// <c>sign(kappa)</c>.</para>
///
/// <para>The gold/silver card system is unbuilt: <see cref="Runner.Gold"/> and
/// <see cref="Runner.Silver"/> exist as inert fields, nothing sets them, and no card logic runs.
/// The per-runner timing draws a carded runner would consume are still drawn, unconditionally,
/// so the RNG stream's shape never depends on who is carded.</para>
/// </summary>
public static class AetherRaceLive
{
    /// <summary>Seeded RNG: mulberry32 plus Box-Muller with a spare. All chance flows through
    /// one stream. Never substitute <see cref="Random"/> anywhere: the RNG and its draw order
    /// are the contract every replay is re-derived against.
    ///
    /// <para>Log, Sin and Cos go through <see cref="PortableMath"/> and not Math/MathF on
    /// purpose: the platform transcendentals differ by ulps between OSes, and
    /// <see cref="Runner.Fortune"/> integrates the Gaussian across ~1,400 ticks, so a one-ulp
    /// drift accumulates until it flips a branch and two machines watch different races from
    /// the same seed.</para></summary>
    public sealed class RaceRng
    {
        private uint a;
        private double? spare;

        public RaceRng(uint seed) => this.a = seed;

        public double Next()
        {
            unchecked
            {
                this.a += 0x6D2B79F5u;
                var t = (this.a ^ (this.a >> 15)) * (1u | this.a);
                t = (t + ((t ^ (t >> 7)) * (61u | t))) ^ t;
                return (t ^ (t >> 14)) / 4294967296.0;
            }
        }

        public double Gauss()
        {
            if (this.spare is { } s)
            {
                this.spare = null;
                return s;
            }

            double u;
            do
            {
                u = this.Next();
            }
            while (u == 0);

            // Box-Muller on purpose: exactly two Next() draws yield exactly two Gaussians, so
            // the stream's shape never changes. A ziggurat or rejection method would consume a
            // variable number of draws and reshuffle every race.
            var v = this.Next();
            var m = Math.Sqrt(-2 * PortableMath.Log(u));
            this.spare = m * PortableMath.Sin(2 * Math.PI * v);
            return m * PortableMath.Cos(2 * Math.PI * v);
        }

        /// <summary>An integer in [0, n): the Fisher-Yates draw.</summary>
        public int Int(int n) => (int)Math.Floor(this.Next() * n);
    }

    /// <summary>Every tuning value, gathered where a harness can name them. All of it is
    /// dial-set material: move one and a stored seed re-derives a different race, so
    /// <see cref="DialsVersion"/> moves with it.</summary>
    public static class Dials
    {
        /// <summary>Names the physics a race was resolved under, so a replay can run those
        /// physics and not today's. A tuning change must never rewrite history.
        ///
        /// <para><b>p4.1: the ford stopped being a wall.</b> Width was a STEP per segment, so the
        /// Duskwind's ford clamped a railing runner 1.4 bounds sideways in one tick. Width now ramps
        /// over <see cref="WidthTaper"/> and a runner reads a narrowing ahead of itself
        /// (<see cref="NarrowLookBase"/>). Only the Duskwind moves: it owns the only per-segment
        /// width, and the taper is skipped on a uniform course.</para></summary>
        public const string DialsVersion = "racelive-p4.1";

        public const float Dt = 1f / 30f;
        public const float V0 = 10.0f;

        /// <summary>Speed's top-speed share, and the whole of what Speed does to pace.</summary>
        public const float SpeedSpan = 0.025f;
        public const float StatK = 15f;
        public const float Accel = 5.5f;
        public const float Brake = 7.5f;
        public const float Grip = 4.4f;
        public const float NoiseBand = 0.062f;
        public const float NoiseTheta = 0.35f;
        public const float FormBand = 0.022f;
        public const float StumbleRate = 0.016f;
        public const float StumbleMult = 0.74f;
        public const float StumbleSecs = 0.75f;

        /// <summary>How often a burst arrives, and it is nobody's stat: Power owns a burst's
        /// strength, never its chance to occur.</summary>
        public const float BurstRate = 0.045f;

        /// <summary>What a burst is worth: Power's channel. Base for everyone, span for the
        /// powerful.</summary>
        public const float BurstMultBase = 1.10f;
        public const float BurstMultSpan = 0.20f;
        public const float BurstSecs = 3.0f;
        public const float BurstCost = 0.06f;
        public const float BurstCd = 6.0f;

        /// <summary>What a burst costs, discounted by Stamina: reduced cost, never complete
        /// immunity.</summary>
        public const float BurstThrift = 0.4f;

        // SusBase sets the drain level for the whole field at every stat scale; SusSpan is
        // Stamina's edge over the field's own middle (see Runner.RelStamina).
        public const float SusBase = 0.945f;
        public const float SusSpan = 0.045f;

        /// <summary>How much cheaper over-pace is for a runner with more tank than its rivals,
        /// applied to the field-relative edge so the absolute cost never swings with stat
        /// scale.</summary>
        public const float TankSpan = 0.45f;
        public const float DrainK = 3.0f;
        public const float KickCash = 0.09f;
        public const float FocusEcon = 0.25f;

        /// <summary>How much better a sharp runner corners: the one Focus channel a course can
        /// lean on.</summary>
        public const float FocusGrip = 0.30f;

        // The hill, split in two so it is not one stat's private course: GradeSpeed is what a
        // climb takes off the pace (Power shields it), GradeDrain is what it takes out of the
        // tank (Stamina discounts it).
        public const float GradeSpeed = 1.9f;
        public const float GradeDrain = 0.55f;
        public const float FadeBase = 0.84f;

        /// <summary>Heart's first channel: how well a spent runner keeps going, measured
        /// against the field.</summary>
        public const float FadeSpan = 0.10f;

        /// <summary>Where the closing stretch starts, as a fraction of the course.</summary>
        public const float ClutchFrom = 0.875f;

        /// <summary>How close a rival must be, in bounds, for the finish to be a contest worth
        /// finding something for.</summary>
        public const float ClutchReach = 3.2f;

        /// <summary>How long the clutch surge lasts once it fires.</summary>
        public const float ClutchSecs = 2.2f;

        /// <summary>What the surge is worth, against the field's own Heart, like the fade
        /// floor.</summary>
        public const float ClutchSpan = 0.22f;
        public const float Regen = 0.015f;
        public const float BlockDs = 1.45f;
        public const float BlockW = 0.90f;
        public const float JostleW = 0.75f;
        public const float PackDrag = 0.013f;
        public const float DraftGain = 0.021f;
        public const float DraftNear = 0.9f;
        public const float DraftFar = 3.0f;
        public const float DraftW = 0.55f;
        public const float LatSpeed = 2.3f;
        public const float ClearAhead = 1.05f;

        /// <summary>The pace edge, in bounds/s, a blocked runner wants before it judges a pass
        /// worth the wide line.</summary>
        public const float PassEdge = 0.06f;
        public const float GateMin = 0.10f;
        public const float GateRand = 0.30f;
        public const float GateReact = 0.55f;
        public const float LaneMargin = 0.65f;

        /// <summary>The length, in bounds, a change of track width is spread over. Sized off the
        /// move it buys: the ford asks 1.4 bounds of lateral travel, about 6 bounds of road, and
        /// this is twice that so a runner negotiates rather than survives it.</summary>
        public const float WidthTaper = 14f;

        /// <summary>How far down the road a runner reads a NARROWING, base and Focus term. Shorter
        /// than the bend anticipation on purpose: a funnel only needs the right side by the time it
        /// arrives, and reading it earlier single-files the field up a wide straight.</summary>
        public const float NarrowLookBase = 8f;

        /// <inheritdoc cref="NarrowLookBase"/>
        public const float NarrowLookFocus = 12f;
        public const float TerrainBite = 0.35f;
        public const float ZoneBite = 1.0f;

        // There is deliberately no WeatherBite: weather is neutral and never consults who is
        // running (see Race.LocalEdge).
        public const float EdgeSpeed = 0.008f;
        public const float EdgeDrain = 0.05f;
        public const float BoxedSecs = 1.15f;
        public const float GroupGap = 9f;
        public const float FinishLinger = 8f;
    }

    // The stat curve: log-compressed, the +3 matching StatBlock's base competence, scale-free.
    private static readonly float CfLo = PortableMath.Log(5f);
    private static readonly float CfHi = PortableMath.Log(93f);

    public static float Cfn(float stat) => MathF.Min(1f, MathF.Max(0f,
        (PortableMath.Log(MathF.Max(2f, stat) + 3f) - CfLo) / (CfHi - CfLo)));

    /// <summary>The curve at the roster's middling stat, the zero point of the speed
    /// channels.</summary>
    private static readonly float CfnFive = Cfn(5f);

    /// <summary>A track condition: five scalars and a label, read every tick and applied
    /// identically to all runners. Nothing here takes a runner argument and nothing here ever
    /// will; that is what "weather is neutral" means in code. The values are dial-set material
    /// like everything in <see cref="Dials"/>.</summary>
    public sealed record Weather(string Element, float Grip, float Stumble, float Noise, float Drain, float Burst, string Label)
    {
        /// <summary>Lateral drift outward through a bend: understeer, the thing grip alone
        /// cannot say. Consumes no RNG draw (deterministic in the tick state), so the stream
        /// every replay is re-derived against is untouched. See <see cref="Race.Step"/>'s
        /// lateral integration for the term.</summary>
        public float Slip { get; init; }
    }

    // Each weather owns one channel outright, and no two own the same one in the same
    // direction; fire and lightning split Burst in opposite directions. Drain multiplies the
    // positive branch only (recovery is untouched), and Burst multiplies the burst rate and
    // never closerKick.
    public static readonly Dictionary<string, Weather> Weathers = new()
    {
        ["clear"] = new Weather(string.Empty, 1.00f, 1.00f, 1.00f, 1.00f, 1.00f, "clear"),

        // heavy going: every push costs a quarter more, so the tank empties earlier.
        ["rain"] = new Weather("water", 0.96f, 1.15f, 1.00f, 1.24f, 1.00f, "rain — soft going"),

        // the wind decides: the fortune stream widens to ~±11%.
        ["gale"] = new Weather("wind", 1.00f, 1.10f, 1.55f, 1.06f, 1.00f, "a gale"),

        // the line breaks: the corner will not hold you. Slip is the only non-zero one in the
        // table; 14 was measured at both stat scales (the term is quadratic in speed, so a
        // value tuned only on starters overshoots on a grown field).
        ["snowfall"] = new Weather("ice", 0.82f, 1.30f, 1.00f, 1.06f, 1.00f, "snowfall") { Slip = 14f },

        // fuel goes faster and the lunges thin out; closerKick is unconditional, so a leaning
        // closer with fuel still kicks in the haze.
        ["haze"] = new Weather("fire", 1.00f, 1.00f, 1.00f, 1.16f, 0.75f, "heat haze"),

        // bursts half again as common: the only row above 1.00 on a channel the player
        // experiences as good news.
        ["static"] = new Weather("lightning", 1.00f, 1.00f, 1.10f, 1.00f, 1.50f, "static in the air"),

        // the footing is gone: about one extra trip per runner per race.
        ["dustveil"] = new Weather("earth", 0.96f, 1.70f, 1.15f, 1.00f, 1.00f, "a dust veil"),
    };

    /// <summary>The neutral sky. Every course admits it, and it is what a caller with no usable stored
    /// sky falls back to.</summary>
    public const string ClearWeather = "clear";

    /// <summary>The sky each element wears, in <see cref="RacingElements.WheelOrder"/>'s own
    /// order: fire, lightning, wind, ice, water, earth. Parallel to that array on purpose:
    /// index arithmetic against the wheel IS the admissibility rule, so a weather's neighbours
    /// are its neighbours on the one chart the player already knows.</summary>
    public static readonly string[] SkyOfElement = ["haze", "static", "gale", "snowfall", "rain", "dustveil"];

    /// <summary>Every sky a course may run, "clear" first, in a fixed order: the home sky, then
    /// the clockwise neighbour, then the anticlockwise one. A terrain-less course admits nothing
    /// but "clear".</summary>
    public static string[] AdmittedWeathers(CourseDef course)
    {
        var t = Array.IndexOf(RacingElements.WheelOrder, course.Terrain);
        if (t < 0)
        {
            return ["clear"];
        }

        var n = RacingElements.WheelOrder.Length;
        return ["clear", SkyOfElement[t], SkyOfElement[(t + 1) % n], SkyOfElement[((t + n) - 1) % n]];
    }

    /// <summary>FNV-1a, 32-bit, over the string's UTF-16 code units low byte first, spelled out
    /// by hand and never <see cref="string.GetHashCode"/>: .NET randomises string hashing per
    /// process, so a hash-keyed weather would agree with itself in every same-process test and
    /// then disagree between the server that resolves a race and the client that replays
    /// it.</summary>
    public static uint Fnv1a32(string s)
    {
        unchecked
        {
            var h = 2166136261u;
            foreach (var ch in s)
            {
                h = (h ^ (byte)ch) * 16777619u;
                h = (h ^ (byte)(ch >> 8)) * 16777619u;
            }

            return h;
        }
    }

    /// <summary>The sky at a course on a seed: clear 0.65, the course's own weather 0.15, each
    /// wheel-neighbour's 0.10.
    ///
    /// <para>It draws from its own stream and never from the race's: a draw taken from
    /// <c>race.rng</c> would consume one <c>Next()</c> before runner construction and change
    /// every race in the engine, on every seed, forever. It is caller-side on purpose:
    /// selection is policy and simulation is physics, so nothing inside <see cref="Race"/>
    /// calls it and <see cref="CreateRace"/>'s "clear" default stays untouched. The weight
    /// table is dial-set material: re-tune it and a stored seed re-derives a different sky, so
    /// <see cref="Dials.DialsVersion"/> moves when it moves.</para></summary>
    public static string PickWeather(int seed, CourseDef course)
    {
        var admitted = AdmittedWeathers(course);
        if (admitted.Length == 1)
        {
            return "clear"; // no terrain, no point on the wheel to measure from, and no draw at all
        }

        // 0x57454154 is "WEAT". The course hash keeps the same seed from painting the same sky
        // over the whole roster.
        var w = new RaceRng(unchecked((uint)seed ^ Fnv1a32(course.Name) ^ 0x57454154u));

        // One warm-up draw: a low seed XORed with constants is a low mulberry32 state, and its
        // first output is the weakest.
        w.Next();

        var u = w.Next();
        return u < 0.65 ? admitted[0]
            : u < 0.80 ? admitted[1]
            : u < 0.90 ? admitted[2]
            : admitted[3];
    }

    // A course is authored as segments, and the builder pads the final straight so the total
    // lands exactly on the category distance: fixed lengths make every course of a category
    // comparable.
    public enum RaceCategory
    {
        Sprint,
        Route,
        Journey,
    }

    public static readonly Dictionary<RaceCategory, float> CategoryLengths = new()
    {
        [RaceCategory.Sprint] = 300f,
        [RaceCategory.Route] = 450f,
        [RaceCategory.Journey] = 540f,
    };

    public readonly record struct CatTune(float Speed, float Drain);

    /// <summary>Course length shifts the weight between Speed and Stamina: a sprint amplifies
    /// the speed channel and softens the fuel one, and a journey does the reverse.</summary>
    public static readonly Dictionary<RaceCategory, CatTune> CatTunes = new()
    {
        [RaceCategory.Sprint] = new CatTune(1.40f, 0.40f),
        [RaceCategory.Route] = new CatTune(1.00f, 1.00f),
        [RaceCategory.Journey] = new CatTune(0.45f, 1.25f),
    };

    public enum SegmentKind
    {
        Straight,
        Corner,
    }

    public sealed class Segment
    {
        public SegmentKind Kind;
        public float Len;
        public float Turn;
        public float Radius;
        public float Grade;
        public float? Width;
        public string Element = string.Empty;
        public string Name = string.Empty;
    }

    public sealed class CourseDef
    {
        /// <summary>Stable wire/storage identity for the course. Names are display copy; the
        /// key never changes once shipped.</summary>
        public required string Key;
        public required string Name;
        public required RaceCategory Category;
        public string Terrain = string.Empty;
        public required float Width;
        public required Segment[] Segments;
        public float RunInGrade;
    }

    public readonly record struct TrackSample(float X, float Y, float Heading, float Kappa, float Grade, float Width, string Element, string Section);

    public sealed class Track
    {
        public required string Name;
        public required RaceCategory Category;
        public required string Terrain;
        public required float Length;
        public required float Width;
        public required float Step;
        public required int Count;
        public required float[] Xs;
        public required float[] Ys;
        public required float[] Hs;
        public required float[] Ks;
        public required float[] Gs;
        public required float[] Ws;
        public required string[] Elems;
        public required string[] Names;

        public TrackSample At(float sQ)
        {
            var j = Math.Clamp((int)MathF.Round(sQ / this.Step), 0, this.Count - 1);
            return new TrackSample(this.Xs[j], this.Ys[j], this.Hs[j], this.Ks[j], this.Gs[j], this.Ws[j], this.Elems[j], this.Names[j]);
        }

        /// <summary>Viewer only: the same sample blended with its neighbouring row so a close
        /// camera follows a smooth curve instead of <see cref="At"/>'s half-bound staircase.
        ///
        /// <para><b>The sim must never call this.</b> <see cref="At"/>'s quantisation is part
        /// of the physics contract every replay is re-derived against: blending it inside the
        /// step would change speeds, lines and results, silently rewriting every race ever
        /// run.</para>
        ///
        /// <para>Heading accumulates continuously as the track is built and is never wrapped,
        /// so it blends linearly without a seam. Element and section name take the nearer row
        /// rather than blending.</para></summary>
        public TrackSample AtLerp(float sQ)
        {
            var f = Math.Clamp(sQ / this.Step, 0f, this.Count - 1);
            var j = Math.Clamp((int)MathF.Floor(f), 0, this.Count - 1);
            var k = Math.Min(j + 1, this.Count - 1);
            var t = f - j;
            return new TrackSample(
                Mix(this.Xs[j], this.Xs[k], t),
                Mix(this.Ys[j], this.Ys[k], t),
                Mix(this.Hs[j], this.Hs[k], t),
                Mix(this.Ks[j], this.Ks[k], t),
                Mix(this.Gs[j], this.Gs[k], t),
                Mix(this.Ws[j], this.Ws[k], t),
                t < 0.5f ? this.Elems[j] : this.Elems[k],
                t < 0.5f ? this.Names[j] : this.Names[k]);
        }

        private static float Mix(float a, float b, float t) => a + ((b - a) * t);
    }

    public static Track BuildTrack(CourseDef def)
    {
        var target = CategoryLengths[def.Category];
        var lens = new float[def.Segments.Length];
        var authored = 0f;
        for (var i = 0; i < def.Segments.Length; i++)
        {
            var seg = def.Segments[i];
            lens[i] = seg.Kind == SegmentKind.Corner ? MathF.Abs(seg.Turn) * MathF.PI / 180f * seg.Radius : seg.Len;
            authored += lens[i];
        }

        var pad = target - authored;
        if (pad < 20f)
        {
            throw new InvalidOperationException($"{def.Name}: authored {authored} of {target}, run-in too short");
        }

        // Sample every half-bound: position, heading, curvature, grade, width, ground element.
        const float step = 0.5f;
        var n = (int)MathF.Ceiling(target / step) + 2;
        var xs = new float[n];
        var ys = new float[n];
        var hs = new float[n];
        var ks = new float[n];
        var gs = new float[n];
        var ws = new float[n];
        var elems = new string[n];
        var names = new string[n];
        for (var i = 0; i < n; i++)
        {
            elems[i] = string.Empty;
            names[i] = string.Empty;
        }

        var effSegs = new List<(SegmentKind Kind, float Len, float Kappa, float Grade, float Width, string Element, string Name)>();
        for (var i = 0; i < def.Segments.Length; i++)
        {
            var seg = def.Segments[i];
            var kappa = seg.Kind == SegmentKind.Corner ? (seg.Turn > 0f ? 1f : -1f) / seg.Radius : 0f;
            effSegs.Add((seg.Kind, lens[i], kappa, seg.Grade, seg.Width ?? def.Width, seg.Element, seg.Name));
        }

        effSegs.Add((SegmentKind.Straight, pad, 0f, def.RunInGrade, def.Width, string.Empty, "the run-in"));

        float x = 0f, y = 0f, h = -MathF.PI / 2f, s = 0f; // heading starts "up the page"
        var idx = 0;
        foreach (var seg in effSegs)
        {
            var end = s + seg.Len;
            while (s < end - 1e-3f && idx < n)
            {
                xs[idx] = x;
                ys[idx] = y;
                hs[idx] = h;
                ks[idx] = seg.Kappa;
                gs[idx] = seg.Grade;
                ws[idx] = seg.Width;
                elems[idx] = seg.Element;
                names[idx] = seg.Name;

                // PortableMath, not MathF: this walk defines the geometry every result is
                // measured against, so it has to be the same geometry on every machine.
                x += PortableMath.Cos(h) * step;
                y += PortableMath.Sin(h) * step;
                h += seg.Kappa * step;
                s += step;
                idx++;
            }
        }

        for (; idx < n; idx++)
        {
            xs[idx] = x;
            ys[idx] = y;
            hs[idx] = h;
            ws[idx] = def.Width;
        }

        TaperWidths(ws, def, step);

        return new Track
        {
            Name = def.Name, Category = def.Category, Terrain = def.Terrain,
            Length = target, Width = def.Width, Step = step, Count = n,
            Xs = xs, Ys = ys, Hs = hs, Ks = ks, Gs = gs, Ws = ws, Elems = elems, Names = names,
        };
    }

    /// <summary>Turns the width profile's segment steps into ramps (Dials p4.1). Width is authored
    /// one value per segment, so the array arrives as a staircase, and the lateral clamp in
    /// <c>Step</c> can only serve a 2.8-bound drop between two rows by teleporting a railing runner
    /// sideways.
    ///
    /// <para>Erosion then mean over the same reach, and the ORDER is the point: the pair lands the
    /// narrow value exactly at the authored boundary, so the ford is never wider than written, only
    /// its approach is. Skipped where every segment runs the course width, so the uniform courses
    /// keep their geometry bit-for-bit.</para></summary>
    private static void TaperWidths(float[] ws, CourseDef def, float step)
    {
        var varies = false;
        foreach (var seg in def.Segments)
        {
            if (seg.Width is { } w && w != def.Width)
            {
                varies = true;
                break;
            }
        }

        if (!varies)
        {
            return;
        }

        var n = ws.Length;
        var reach = (int)MathF.Round(Dials.WidthTaper / 2f / step);
        if (reach < 1)
        {
            return;
        }

        var eroded = new float[n];
        for (var i = 0; i < n; i++)
        {
            var lo = Math.Max(0, i - reach);
            var hi = Math.Min(n - 1, i + reach);
            var m = ws[lo];
            for (var j = lo + 1; j <= hi; j++)
            {
                if (ws[j] < m)
                {
                    m = ws[j];
                }
            }

            eroded[i] = m;
        }

        for (var i = 0; i < n; i++)
        {
            var lo = Math.Max(0, i - reach);
            var hi = Math.Min(n - 1, i + reach);
            var sum = 0f;
            for (var j = lo; j <= hi; j++)
            {
                sum += eroded[j];
            }

            ws[i] = sum / (hi - lo + 1);
        }
    }

    private static Segment Straight(float len, float grade, string name, string? element = null, float? width = null) => new()
    {
        Kind = SegmentKind.Straight, Len = len, Grade = grade, Name = name, Element = element ?? string.Empty, Width = width,
    };

    private static Segment Corner(float turn, float radius, float grade, string name, float? width = null, string? element = null) => new()
    {
        Kind = SegmentKind.Corner, Turn = turn, Radius = radius, Grade = grade, Name = name, Width = width, Element = element ?? string.Empty,
    };

    // The roster. Fixed distances per category; radii 14-45 (14 is a real brake, 40 never
    // binds). Every element has a home course.
    public static readonly CourseDef[] Courses =
    [
        new CourseDef
        {
            Key = "ember-dash",
            Name = "the Ember Dash", Category = RaceCategory.Sprint, Terrain = "fire", Width = 5.5f,
            Segments =
            [
                Straight(70f, 0f, "the break"),
                Corner(-70f, 34f, 0f, "the sweep"),
                Straight(52f, 0.03f, "the cinder rise", element: "fire"),
                Corner(62f, 22f, 0f, "the elbow"),
            ],
            RunInGrade = -0.01f,
        },
        new CourseDef
        {
            Key = "gale-route",
            Name = "the Gale Route", Category = RaceCategory.Route, Terrain = "wind", Width = 6f,
            Segments =
            [
                Straight(58f, 0f, "the break"),
                Corner(45f, 20f, 0f, "the chicane"),
                Corner(-45f, 20f, 0f, "the chicane"),
                Straight(56f, 0.04f, "the shoulder"),
                Corner(-110f, 30f, 0f, "the long bend"),
                Straight(54f, -0.03f, "the downwind", element: "wind"),
                Corner(40f, 26f, 0f, "the last turn"),
            ],
        },
        new CourseDef
        {
            Key = "duskwind-journey",
            Name = "the Duskwind Journey", Category = RaceCategory.Journey, Terrain = "water", Width = 6f,
            Segments =
            [
                Straight(60f, 0f, "the settle"),
                Corner(-60f, 30f, 0f, "the first turn"),
                Straight(66f, 0.06f, "the climb"),
                Corner(70f, 24f, 0.02f, "the crest"),
                Straight(42f, 0f, "the ford", element: "water", width: 3.2f),
                Corner(-50f, 30f, 0f, "the far turn"),
                Straight(56f, -0.05f, "the long fall"),
                Corner(100f, 34f, 0f, "the last light"),
            ],
        },
        new CourseDef
        {
            Key = "quiet-mile",
            Name = "the Quiet Mile", Category = RaceCategory.Route, Terrain = string.Empty, Width = 6.5f,
            Segments =
            [
                Straight(86f, 0f, "the break"),
                Corner(50f, 38f, 0f, "the meadow bend"),
                Straight(72f, 0.02f, "the gentle rise"),
                Corner(-50f, 38f, 0f, "the far bend"),
                Straight(60f, -0.02f, "the easing"),
                Corner(30f, 42f, 0f, "the home turn"),
            ],
        },

        // The technical one: Focus' home. Narrow, five real corners on 20-bound radii that
        // bind at racing speed.
        new CourseDef
        {
            Key = "levin-run",
            Name = "the Levin Run", Category = RaceCategory.Route, Terrain = "lightning", Width = 4.6f,
            Segments =
            [
                Straight(46f, 0f, "the break"),
                Corner(-60f, 20f, 0f, "the first snap"),
                Straight(34f, 0f, "the short chute", element: "lightning"),
                Corner(70f, 18f, 0f, "the zag"),
                Corner(-70f, 18f, 0f, "the zig"),
                Straight(38f, 0.025f, "the rise"),
                Corner(65f, 19f, 0f, "the crack"),
                Straight(30f, 0f, "the false straight", element: "lightning"),
                Corner(-55f, 20f, 0f, "the last snap"),
                Straight(36f, -0.02f, "the dip"),
                Corner(75f, 19f, 0f, "the hairpin"),
                Straight(32f, 0f, "the narrows", element: "lightning"),
                Corner(-65f, 20f, 0f, "the final twist"),
            ],
            RunInGrade = 0.01f,
        },

        // The attritional one: Stamina's home, Heart along for the ride. Two rises, a
        // recovering descent and a last pull to the line.
        new CourseDef
        {
            Key = "stone-ladder",
            Name = "the Stone Ladder", Category = RaceCategory.Journey, Terrain = "earth", Width = 6f,
            Segments =
            [
                Straight(58f, 0f, "the settle"),
                Corner(-50f, 32f, 0.01f, "the first bank"),
                Straight(70f, 0.03f, "the first rise", element: "earth"),
                Corner(60f, 26f, 0.015f, "the shoulder"),
                Straight(64f, 0.035f, "the long ladder"),
                Corner(-55f, 30f, 0f, "the turn at the top"),
                Straight(58f, -0.055f, "the fall"),
                Corner(80f, 28f, 0.01f, "the last bank"),
                Straight(46f, 0.02f, "the last pull", element: "earth"),
            ],
            RunInGrade = 0.005f,
        },

        // The open one: Power's home. Wide, two sweeps that never bind, then half the course
        // is one straight run to the line.
        new CourseDef
        {
            Key = "frostline",
            Name = "the Frostline", Category = RaceCategory.Sprint, Terrain = "ice", Width = 6.8f,
            Segments =
            [
                Straight(50f, 0f, "the break"),
                Corner(-40f, 40f, 0f, "the glass bend"),
                Straight(46f, -0.02f, "the drift", element: "ice"),
                Corner(45f, 36f, 0f, "the far sweep"),
            ],
            RunInGrade = -0.01f,
        },
    ];

    // Pacing styles, derived from the block deterministically. Fractions are of the runner's
    // own top speed; the whole plan is a tendency the fortune stream and the pack argue with.
    public enum RunStyle
    {
        Front,
        Stalker,
        Closer,
    }

    public readonly record struct StylePace(float Break, float Settle, float Move, float Kick);

    public static readonly Dictionary<RunStyle, StylePace> Styles = new()
    {
        [RunStyle.Front] = new StylePace(1.045f, 0.960f, 0.985f, 1.030f),
        [RunStyle.Stalker] = new StylePace(0.975f, 0.945f, 0.995f, 1.045f),
        [RunStyle.Closer] = new StylePace(0.930f, 0.915f, 0.975f, 1.075f),
    };

    /// <summary>The three plan scores a block leans toward, kept in one place so the dominant
    /// style (flavour) and the blended plan (physics) can never disagree. Heart is in none of
    /// them: how bravely a runner finishes is not a plan it makes before the gates.</summary>
    public static (float Front, float Closer, float Stalker) StyleScores(StatBlock stats) => (
        stats.Speed + (0.4f * stats.Focus),
        stats.Power + (0.4f * stats.Stamina),
        stats.Focus + (0.4f * stats.Stamina));

    /// <summary>The dominant lean, for narration and flavour only, never for pace: the physics
    /// runs <see cref="BlendPace"/>'s proportional mix instead, because a three-way branch made
    /// one stat point flip a runner's whole plan.</summary>
    public static RunStyle StyleOf(StatBlock stats)
    {
        var (front, closer, stalker) = StyleScores(stats);
        if (front >= closer && front >= stalker)
        {
            return RunStyle.Front;
        }

        return closer >= stalker ? RunStyle.Closer : RunStyle.Stalker;
    }

    /// <summary>A runner's actual race plan: the three style tables mixed in proportion to how
    /// much its block leans toward each, so a lean is a flavour with no cliff.</summary>
    private static StylePace BlendPace(StatBlock stats)
    {
        var (f, c, s) = StyleScores(stats);
        var sum = MathF.Max(1e-4f, f + c + s);
        float Mix(Func<StylePace, float> pick) =>
            ((pick(Styles[RunStyle.Front]) * f)
                + (pick(Styles[RunStyle.Closer]) * c)
                + (pick(Styles[RunStyle.Stalker]) * s)) / sum;

        return new StylePace(Mix(p => p.Break), Mix(p => p.Settle), Mix(p => p.Move), Mix(p => p.Kick));
    }

    private static float StylePaceOf(StylePace p, RacePhase phase) => phase switch
    {
        RacePhase.Break => p.Break,
        RacePhase.Settle => p.Settle,
        RacePhase.Move => p.Move,
        _ => p.Kick,
    };

    // Phase boundaries as progress fractions; a sprint has no time for a long settle.
    public enum RacePhase
    {
        Break,
        Settle,
        Move,
        Kick,
    }

    public readonly record struct PhaseBounds(float Break, float Settle, float Move);

    public static readonly Dictionary<RaceCategory, PhaseBounds> Phases = new()
    {
        [RaceCategory.Sprint] = new PhaseBounds(0.12f, 0.45f, 0.78f),
        [RaceCategory.Route] = new PhaseBounds(0.09f, 0.58f, 0.84f),
        [RaceCategory.Journey] = new PhaseBounds(0.07f, 0.62f, 0.86f),
    };

    public static RacePhase PhaseAt(RaceCategory category, float progress)
    {
        var p = Phases[category];
        if (progress < p.Break)
        {
            return RacePhase.Break;
        }

        if (progress < p.Settle)
        {
            return RacePhase.Settle;
        }

        return progress < p.Move ? RacePhase.Move : RacePhase.Kick;
    }

    // The race hand: inert placeholders. Nothing sets these.
    public sealed class SilverBuffs
    {
        public float Tank;
        public float Stride;
        public float Craft;
        public float Spark;
        public float Gate;
    }

    /// <summary>One runner's whole live state.</summary>
    public sealed class Runner
    {
        public int Idx;
        public string Name = string.Empty;
        public bool IsPlayer;
        public required StatBlock Stats;
        public float Condition = 1f;

        /// <summary>The dominant lean: flavour and narration only.</summary>
        public RunStyle Style;

        /// <summary>The plan actually run: the three style tables blended by how far the block
        /// leans toward each.</summary>
        public StylePace Pace;

        /// <summary>How hard this block leans each way, normalised to sum to 1. Read by the
        /// behaviours that used to key off the style enum.</summary>
        public float WFront, WCloser, WStalker;

        /// <summary>Stamina and Heart measured against THIS FIELD's mean rather than the stat
        /// curve's absolute floor: the drain level is then set by the dial alone at every stat
        /// scale, and the stat's edge is exactly its lead over its rivals. Fixed when the race
        /// is created, so the race stays pure and replayable.</summary>
        public float RelStamina;
        public float RelHeart;

        /// <summary>The five stats through <see cref="Cfn"/> once at construction, in the fixed
        /// stat order. Constants for the whole run; hoisting them keeps logarithms out of the
        /// physics loop.</summary>
        public float CSpeed, CPower, CStamina, CFocus, CHeart;

        public float S;
        public float Lat;
        public float V;
        public float LatTarget;
        public float LatIdeal;

        public float Stamina = 1f;
        public float Fortune;
        public float Gait;

        /// <summary>Top speed, and Speed is the only stat in it.</summary>
        public float VTop;

        // The race hand: unbuilt, always empty/zero.
        public string Gold = string.Empty;
        public bool GoldFired;
        public float RushT;
        public SilverBuffs Silver = new();

        public float DayForm;

        /// <summary>Heart's clutch: how long the surge has left, and whether it has already
        /// been spent. Once per race, and once only.</summary>
        public float ClutchT;
        public bool ClutchDone;

        public float BurstT;
        public float BurstCd;
        public float StumbleT;
        public float BoxedT;
        public bool BoxedTold;
        public bool Fade;

        public bool SecondWind;
        public float DecideT;
        public int BlockedBy = -1;
        public float Drafting;
        public float PackDrag;
        public float DoorCd;

        public bool Finished;
        public float FinishTime;
        public int Place;
        public int LastPlace;
        public float DistanceRun;

        public float Draw;
        public int Post;
        public float GateDelay;
        public bool QuickSpark;

        // Card timing draws (Last Light / the Weaver): drawn whether or not a card is carried,
        // so the RNG stream's shape never depends on carding. Unused while Gold is always
        // empty.
        public float LastLightAt;
        public bool LastLightEarly;
        public bool LastLightBurst;
        public float WeaverWait;
        public float GhostT;

        public float BlockedT;
    }

    public sealed class RaceEvent
    {
        public float T;
        public required string Kind;
        public int Who;
        public string? Section;
        public string? Perk;
        public int? Place;
    }

    public sealed class RaceGroup
    {
        public List<int> RunnerIdx { get; } = [];
        public float SHead;
        public float STail;
        public bool HasPlayer;
        public bool HasLeader;
    }

    /// <summary>One resolved race in progress: build with <see cref="CreateRace"/>, advance
    /// with <see cref="Step"/> or <see cref="RunToEnd"/>.</summary>
    public sealed class Race
    {
        private Track track = null!;
        private Weather weather = null!;
        private RaceRng rng = null!;
        private Runner[] runners = null!;
        private int[] updateOrder = null!;
        private CourseDef course = null!;

        public Track Track => this.track;
        public Weather Weather => this.weather;
        public IReadOnlyList<Runner> Runners => this.runners;
        public CourseDef Course => this.course;
        public string DialsVersion => Dials.DialsVersion;

        public float Time { get; private set; }
        public int Tick { get; private set; }
        public List<RaceEvent> Events { get; } = [];
        public int FinishedCount { get; private set; }
        public bool Done { get; private set; }
        public float WinnerTime { get; private set; }
        public float Margin { get; private set; }
        public int[] Order { get; private set; } = [];

        public string FirstBendDir = "left";
        public float FirstBendDist;
        public float FirstBendKappa;
        public int FirstAway { get; private set; } = -1;

        private CatTune CatTune => CatTunes[this.course.Category];

        private void Log(string kind, int who, string? section = null, string? perk = null, int? place = null) =>
            this.Events.Add(new RaceEvent { T = this.Time, Kind = kind, Who = who, Section = section, Perk = perk, Place = place });

        private static readonly float[] CandidateOffsets = [0.9f, 1.8f, 2.7f];

        private float LocalEdge(Runner r, float sAt)
        {
            var ground = this.track.At(sAt);
            var edge = Dials.TerrainBite * RacingElements.WheelEdge(r.Stats.Element, this.course.Terrain);
            if (ground.Element.Length > 0)
            {
                edge += Dials.ZoneBite * RacingElements.WheelEdge(r.Stats.Element, ground.Element);
            }

            // Deliberately NO weather branch here: weather is neutral and never consults who
            // is running. Weather.Element stays as identity for admissibility, copy and FX; it
            // is simply never consulted by the sim.
            return edge;
        }

        // One decision: where to run, and, when blocked, WHEN to go. The rail is contested, a
        // pass is earned, overtaking is timed with Focus as the judge, and the front defends.
        private void DecideLine(Runner r)
        {
            var here = this.track.At(r.S);
            var focus = MathF.Min(1f, r.CFocus + r.Silver.Craft);

            // The road AHEAD sets the line: a narrowing read underfoot is a collision, not a
            // decision. Narrowest in the window, not the width at its end.
            var narrowLook = Dials.NarrowLookBase + (Dials.NarrowLookFocus * focus);
            var narrow = here.Width;
            for (var d = 3f; d <= narrowLook; d += 3f)
            {
                var w = this.track.At(r.S + d).Width;
                if (w < narrow)
                {
                    narrow = w;
                }
            }

            var halfW = (narrow / 2f) - Dials.LaneMargin;

            var anticip = 12f + (26f * focus);
            var bendK = here.Kappa;
            var bendDist = 0f;
            if (MathF.Abs(bendK) < 1e-4f)
            {
                for (var d = 3f; d <= anticip; d += 3f)
                {
                    var k = this.track.At(r.S + d).Kappa;
                    if (MathF.Abs(k) > 1e-4f)
                    {
                        bendK = k;
                        bendDist = d;
                        break;
                    }
                }
            }

            var commit = MathF.Min(1f, MathF.Abs(bendK) * 26f) * (1f - (0.6f * bendDist / MathF.Max(anticip, 1f)));

            // Bend-free road ahead: HOLD the line rather than drifting to the middle, so a
            // field of blocked runners fans across the track to pass.
            r.LatIdeal = MathF.Abs(bendK) > 1e-4f
                ? MathF.Sign(bendK) * halfW * commit
                : r.Lat;

            // The door: a leaning front-runner defends by identity, otherwise only the truly
            // sharp. Read off the blend weight, not the style enum, so an even block (0.333)
            // does not defend.
            if (r.WFront > 0.4f || focus > 0.8f)
            {
                Runner? threat = null;
                foreach (var o in this.runners)
                {
                    if (o == r || o.Finished)
                    {
                        continue;
                    }

                    var ds = r.S - o.S;
                    if (ds > 0.3f && ds < 2.6f && MathF.Abs(o.Lat - r.Lat) < 1.6f && o.V > r.V - 0.4f)
                    {
                        if (threat == null || o.S > threat.S)
                        {
                            threat = o;
                        }
                    }
                }

                if (threat != null)
                {
                    var cover = r.LatIdeal + ((threat.Lat - r.LatIdeal) * 0.55f);
                    r.LatIdeal = MathF.Max(-halfW, MathF.Min(halfW, cover));
                    if (this.Time > r.DoorCd && MathF.Abs(threat.Lat - r.Lat) > 0.35f)
                    {
                        r.DoorCd = this.Time + 7f;
                        this.Log("door", r.Idx);
                    }
                }
            }

            var scan = 6f + (8f * focus);
            var boxed = r.BoxedT > Dials.BoxedSecs;
            var phase = PhaseAt(this.course.Category, r.S / this.track.Length);

            var candidates = new List<float> { r.Lat, r.LatIdeal, 0f };
            foreach (var off in CandidateOffsets)
            {
                candidates.Add(r.Lat + off);
                candidates.Add(r.Lat - off);
            }

            var best = r.Lat;
            var bestScore = float.NegativeInfinity;
            foreach (var raw in candidates)
            {
                var c = MathF.Max(-halfW, MathF.Min(halfW, raw));

                // The earned-pass law: a line you would have to steer through a body to reach
                // is not a candidate at all.
                var legal = true;
                if (r.GhostT <= 0f)
                {
                    foreach (var o in this.runners)
                    {
                        if (o == r || o.Finished)
                        {
                            continue;
                        }

                        if (MathF.Abs(o.S - r.S) >= Dials.ClearAhead)
                        {
                            continue;
                        }

                        var lo = MathF.Min(r.Lat, c);
                        var hi = MathF.Max(r.Lat, c);
                        if (o.Lat > lo + 1e-6f && o.Lat < hi - 1e-6f)
                        {
                            legal = false;
                            break;
                        }
                    }
                }

                if (!legal)
                {
                    continue;
                }

                var free = scan;
                foreach (var o in this.runners)
                {
                    if (o == r || o.Finished)
                    {
                        continue;
                    }

                    var ds = o.S - r.S;
                    if (ds <= 0.1f || ds > scan)
                    {
                        continue;
                    }

                    if (MathF.Abs(o.Lat - c) < Dials.BlockW)
                    {
                        free = MathF.Min(free, ds);
                    }
                }

                // A boxed runner stops caring how far the line is from the pretty one, and in
                // the kick room to run is everything.
                var linePenalty = (boxed ? 0.10f : 0.32f) * (phase == RacePhase.Kick ? 0.5f : 1f) * MathF.Abs(c - r.LatIdeal);
                var movePenalty = 0.06f * MathF.Abs(c - r.Lat);

                // The timing term: is my pace edge real (no edge: sit in the tow), and does
                // the pass fit before the bend (passing on the outside into a bend pays the
                // toll for nothing)?
                var timing = 0f;
                if (r.BlockedBy >= 0 && MathF.Abs(c - r.Lat) > 0.5f)
                {
                    var dv = (r.VTop * (phase == RacePhase.Kick ? 1.04f : 0.99f)) - this.runners[r.BlockedBy].V;
                    if (dv < Dials.PassEdge)
                    {
                        timing = -(0.5f + (1.0f * focus)) * (r.GhostT > 0f ? 0f : boxed ? 0.25f : 1f);
                    }
                    else
                    {
                        var passDist = MathF.Min(26f, r.V * (2.4f / dv));
                        var outsideBend = 0f;
                        for (var d = 4f; d <= passDist; d += 4f)
                        {
                            var k = this.track.At(r.S + d).Kappa;
                            if (MathF.Abs(k) > 1e-4f && MathF.Sign(k) != MathF.Sign(c != 0f ? c : 1f))
                            {
                                outsideBend = MathF.Max(outsideBend, MathF.Abs(k) * 26f);
                            }
                        }

                        timing = 0.9f - (outsideBend * (0.4f + (1.1f * focus)) * (r.GhostT > 0f ? 0.15f : boxed ? 0.5f : 1f));
                    }
                }

                var score = free - linePenalty - movePenalty + timing + ((float)this.rng.Next() * 0.18f);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = c;
                }
            }

            r.LatTarget = best;
            if (boxed && MathF.Abs(best - r.Lat) > 0.8f)
            {
                this.Log("breakout", r.Idx);
                r.BoxedT = 0f;
                r.BoxedTold = false;
            }
        }

        /// <summary>Advance the race one fixed tick.</summary>
        public void Step()
        {
            if (this.Done)
            {
                return;
            }

            const float dt = Dials.Dt;
            this.Time += dt;
            this.Tick++;

            // Places first (previous tick's positions), so overtakes are detectable after
            // moving. OrderBy is a stable sort, matching the tie-break behaviour the JS
            // engine's Array.sort (stable since ES2019) relies on here.
            var byProgress = this.runners.OrderBy(r => r, Comparer<Runner>.Create((a, b) =>
            {
                if (a.Finished && b.Finished)
                {
                    return a.FinishTime.CompareTo(b.FinishTime);
                }

                if (a.Finished)
                {
                    return -1;
                }

                return b.Finished ? 1 : b.S.CompareTo(a.S);
            })).ToArray();
            for (var p = 0; p < byProgress.Length; p++)
            {
                byProgress[p].LastPlace = byProgress[p].Place;
                byProgress[p].Place = p;
            }

            foreach (var ui in this.updateOrder)
            {
                var r = this.runners[ui];
                if (r.Finished)
                {
                    r.S += r.V * dt;
                    r.V = MathF.Max(0f, r.V - (2.5f * dt));
                    continue;
                }

                // The gates: held until your own reaction lets you go.
                if (this.Time < r.GateDelay)
                {
                    continue;
                }

                if (this.FirstAway < 0)
                {
                    this.FirstAway = r.Idx;
                    this.Log("firstAway", r.Idx);
                }

                var here = this.track.At(r.S);
                var halfW = (here.Width / 2f) - Dials.LaneMargin;
                var progress = r.S / this.track.Length;
                var phase = PhaseAt(this.course.Category, progress);
                var edge = this.LocalEdge(r, r.S);
                var focus = MathF.Min(1f, r.CFocus + r.Silver.Craft);

                // Fortune: one slow Ornstein-Uhlenbeck stream per runner. The band is the same
                // width for everyone; no stat narrows it.
                var band = Dials.NoiseBand * this.weather.Noise;
                r.Fortune += (-Dials.NoiseTheta * r.Fortune * dt) + (MathF.Sqrt(2f * Dials.NoiseTheta * dt) * (float)this.rng.Gauss());
                r.Fortune = MathF.Max(-2.4f, MathF.Min(2.4f, r.Fortune));

                // The plan: pace fraction by style and phase, element and fortune shading it.
                var frac = StylePaceOf(r.Pace, phase);
                if (this.course.Category == RaceCategory.Sprint)
                {
                    frac += 0.02f;
                }

                // The kick cashes the tank: Stamina fills it, Power is the exchange rate.
                if (phase == RacePhase.Kick)
                {
                    frac += Dials.KickCash * r.Stamina * (0.6f + (0.4f * r.CPower));
                }

                var vGoal = r.VTop * frac * (1f + (r.Fortune * band) + r.DayForm) * (1f + (edge * Dials.EdgeSpeed));

                // Gradient: the hill takes speed and gives it back downhill; Power shields the
                // climb. What the climb costs the tank is Stamina's, further down.
                vGoal *= here.Grade > 0f
                    ? 1f - (here.Grade * Dials.GradeSpeed * (1.25f - (0.6f * r.CPower)))
                    : 1f - (here.Grade * 0.8f);

                // Corners: braking anticipation over a lookahead. Grip falls with weather and
                // with running wide of the apex.
                var grip = Dials.Grip * this.weather.Grip * (here.Grade < 0f ? 0.94f : 1f)
                    * (1f - (Dials.FocusGrip * (0.5f - r.CFocus)));
                var allow = float.PositiveInfinity;
                for (var d = 0f; d <= 24f; d += 3f)
                {
                    var sample = this.track.At(r.S + d);
                    var kap = MathF.Abs(sample.Kappa);
                    if (kap < 1e-4f)
                    {
                        continue;
                    }

                    var radiusHere = (1f / kap) - (MathF.Sign(sample.Kappa) * r.Lat);
                    var vc = MathF.Sqrt(grip * MathF.Max(4f, MathF.Abs(radiusHere)));
                    allow = MathF.Min(allow, MathF.Sqrt((vc * vc) + (2f * Dials.Brake * d)));
                }

                vGoal = MathF.Min(vGoal, allow);

                // Gold cards: none carried, no perk fires.
                if (r.GhostT > 0f)
                {
                    r.GhostT -= dt;
                    vGoal *= 1.03f;
                }

                if (r.RushT > 0f)
                {
                    r.RushT -= dt;
                }

                // Bursts: rare, loud, always upward, stamina-priced. The rate belongs to
                // nobody; Power decides what a burst is worth and Stamina what it costs. A
                // closer entering the kick with fuel left ALWAYS kicks.
                var burstMult = Dials.BurstMultBase + (Dials.BurstMultSpan * r.CPower);
                if (r.BurstT > 0f)
                {
                    r.BurstT -= dt;
                    vGoal *= burstMult;
                }

                r.BurstCd -= dt;
                if ((phase == RacePhase.Move || phase == RacePhase.Kick) && r.BurstCd <= 0f && r.Stamina > 0.15f && !r.Fade)
                {
                    var rate = Dials.BurstRate * this.weather.Burst * (1f + r.Silver.Spark);
                    var closerKick = phase == RacePhase.Kick && r.WCloser > 0.36f && r.Stamina > 0.2f;
                    if (closerKick || (float)this.rng.Next() < rate * dt)
                    {
                        r.BurstT = Dials.BurstSecs;
                        r.BurstCd = Dials.BurstCd;
                        r.Stamina = MathF.Max(0f, r.Stamina - (Dials.BurstCost * (1f - (Dials.BurstThrift * r.CStamina))));
                        this.Log("burst", r.Idx);
                    }
                }

                // Stumble: the discrete bad beat, a named execution error, the only kind of
                // randomness Focus is allowed to touch.
                if (r.StumbleT > 0f)
                {
                    r.StumbleT -= dt;
                    vGoal *= Dials.StumbleMult;
                }
                else
                {
                    var cornerRisk = 1f + MathF.Min(1.6f, MathF.Abs(here.Kappa) * 22f);
                    var rate = Dials.StumbleRate * this.weather.Stumble * cornerRisk * (1.5f - (1.1f * focus));
                    if ((float)this.rng.Next() < rate * dt)
                    {
                        if (r.SecondWind)
                        {
                            r.SecondWind = false;
                            this.Log("secondWind", r.Idx);
                        }
                        else
                        {
                            r.StumbleT = Dials.StumbleSecs;
                            this.Log("stumble", r.Idx, section: here.Section);
                        }
                    }
                }

                // The pack: blocking, jostle, drag, draft. Nobody passes through a body.
                r.BlockedBy = -1;
                r.Drafting = 0f;
                r.PackDrag = 0f;
                var jostle = 0f;
                var neighbours = 0;
                foreach (var o in this.runners)
                {
                    if (o == r)
                    {
                        continue;
                    }

                    var ds = o.S - r.S;
                    var dlat = o.Lat - r.Lat;
                    if (MathF.Abs(ds) < 1.8f && MathF.Abs(dlat) < 1.2f)
                    {
                        neighbours++;
                    }

                    if (MathF.Abs(ds) < 0.8f && MathF.Abs(dlat) < Dials.JostleW && MathF.Abs(dlat) > 1e-6f)
                    {
                        jostle -= MathF.Sign(dlat) * (Dials.JostleW - MathF.Abs(dlat)) * 2.2f;
                    }

                    if (!o.Finished && ds > 0.1f && ds < Dials.BlockDs && MathF.Abs(dlat) < Dials.BlockW
                        && (r.BlockedBy < 0 || o.S < this.runners[r.BlockedBy].S))
                    {
                        r.BlockedBy = o.Idx;
                    }

                    if (ds > Dials.DraftNear && ds < Dials.DraftFar)
                    {
                        // The tow, graded: full in the clean line, half on the quarter.
                        var adl = MathF.Abs(dlat);
                        if (adl < Dials.DraftW)
                        {
                            r.Drafting = 1f;
                        }
                        else if (adl < 0.95f)
                        {
                            r.Drafting = MathF.Max(r.Drafting, 0.5f);
                        }
                    }
                }

                r.PackDrag = Dials.PackDrag * MathF.Min(4f, neighbours);
                if (r.GhostT <= 0f)
                {
                    vGoal *= 1f - r.PackDrag; // the Weaver slips the crush
                }

                if (r.Drafting > 0f)
                {
                    vGoal *= 1f + (Dials.DraftGain * r.Drafting * (0.7f + (0.6f * r.CFocus))); // reading the tow is craft
                }

                var capped = false;
                r.BlockedT = r.BlockedBy >= 0 ? r.BlockedT + dt : 0f;
                if (r.GhostT > 0f)
                {
                    r.BlockedBy = -1; // nothing ahead is a wall, briefly
                }

                if (r.BlockedBy >= 0)
                {
                    var cap = this.runners[r.BlockedBy].V * 0.985f;
                    if (vGoal > cap)
                    {
                        vGoal = MathF.Min(vGoal, MathF.Max(cap, 0f));
                        capped = true;
                    }
                }

                // Stamina: over-sustainable pace drains quadratically, hills tax it, economy
                // and drafting shade it, empty means the fade. Against the field's middle, so
                // the drain level is SusBase at every stat scale.
                var vSus = r.VTop * (Dials.SusBase + (Dials.SusSpan * r.RelStamina));
                var drain = 0f;
                if (r.V > vSus)
                {
                    var over = (r.V - vSus) / r.VTop;
                    drain += Dials.DrainK * over * (over + 0.05f);
                }
                else if (r.V < vSus * 0.97f)
                {
                    drain -= Dials.Regen * (here.Grade < 0f ? 2.2f : 1f);
                }

                // The other half of the hill: what the climb costs, discounted by Stamina.
                if (here.Grade > 0f)
                {
                    drain += here.Grade * Dials.GradeDrain * (1.3f - (0.6f * r.CStamina));
                }

                if (drain > 0f)
                {
                    drain /= (1f + (Dials.TankSpan * r.RelStamina)) * (1f + r.Silver.Tank);
                    drain *= this.CatTune.Drain;
                    drain *= 1f - (Dials.FocusEcon * focus);
                    drain *= this.weather.Drain * (1f - (edge * Dials.EdgeDrain)) * (1f - (0.04f * r.Drafting));
                }

                r.Stamina = MathF.Max(0f, MathF.Min(1f, r.Stamina - (drain * dt)));
                if (!r.Fade && r.Stamina <= 0f)
                {
                    if (r.SecondWind)
                    {
                        r.SecondWind = false;
                        r.Stamina = 0.30f;
                        this.Log("secondWind", r.Idx);
                    }
                    else
                    {
                        r.Fade = true;
                        this.Log("fade", r.Idx);
                    }
                }

                // The fade floor: Heart's first channel. A live burst suspends the fade; the
                // miracle is always upward and never lowers the floor.
                if (r.Fade && (r.BurstT <= 0f || r.LastLightBurst))
                {
                    var floor = Dials.FadeBase + (Dials.FadeSpan * r.RelHeart);
                    vGoal *= r.BurstT > 0f && r.LastLightBurst ? (1f + floor) / 2f : floor;
                }

                // The clutch: Heart's second channel. Three visible gates: the closing
                // stretch, a rival in reach, not yet spent. It consumes NO RNG draw, so the
                // stream every replay is re-derived against is untouched.
                if (r.ClutchT > 0f)
                {
                    r.ClutchT -= dt;
                    vGoal *= 1f + (Dials.ClutchSpan * r.RelHeart);
                }
                else if (!r.ClutchDone && r.RelHeart > 0f && progress >= Dials.ClutchFrom)
                {
                    var contested = false;
                    foreach (var o in this.runners)
                    {
                        if (o.Idx != r.Idx && !o.Finished && MathF.Abs(o.S - r.S) <= Dials.ClutchReach)
                        {
                            contested = true;
                            break;
                        }
                    }

                    // The RelHeart > 0 gate keeps the event honest: below the field's mean the
                    // surge would be NEGATIVE, and a "found one more" event on a runner that
                    // just slowed down teaches viewers to distrust the commentary.
                    if (contested)
                    {
                        r.ClutchDone = true;
                        r.ClutchT = Dials.ClutchSecs;
                        this.Log("clutch", r.Idx, section: here.Section);
                    }
                }

                // Boxed: wanting to run and being held under it. The timer is the tell the
                // line-decision reads; the event is the narration's.
                if (capped && vGoal >= r.V - 0.05f && r.BlockedBy >= 0 && r.V < vSus)
                {
                    r.BoxedT += dt;
                    if (r.BoxedT > Dials.BoxedSecs && !r.BoxedTold)
                    {
                        r.BoxedTold = true;
                        this.Log("boxed", r.Idx, section: here.Section);
                    }
                }
                else
                {
                    r.BoxedT = MathF.Max(0f, r.BoxedT - (dt * 2f));
                    if (r.BoxedT == 0f)
                    {
                        r.BoxedTold = false;
                    }
                }

                // Steering: re-decide on a Focus cadence, then flow toward the chosen line.
                r.DecideT -= dt;
                if (r.DecideT <= 0f)
                {
                    this.DecideLine(r);
                    r.DecideT = (0.95f - (0.55f * focus)) * (r.GhostT > 0f ? 0.5f : 1f);
                }

                var latMax = Dials.LatSpeed * (r.GhostT > 0f ? 1.6f : 1f); // the Weaver threads
                var latWant = MathF.Max(-halfW, MathF.Min(halfW, r.LatTarget)) - r.Lat;
                var latV = MathF.Max(-latMax, MathF.Min(latMax, latWant * 2.2f)) + jostle;
                var newLat = r.Lat + (latV * dt);

                // The slide: a push toward the OUTSIDE of the bend, scaled by curvature and
                // the square of speed. sign(kappa) is the inside of the turn, so subtracting
                // is outward. It fires before the sideways-solidity pass on purpose, and it
                // reads NO RNG and NO runner identity: same push for every body on the tick.
                if (this.weather.Slip > 0f && MathF.Abs(here.Kappa) > 1e-4f)
                {
                    var vv = r.V / Dials.V0;
                    newLat -= MathF.Sign(here.Kappa) * this.weather.Slip * MathF.Abs(here.Kappa) * vv * vv * dt;
                }

                // Bodies are solid sideways too: a runner alongside is a wall, not a
                // suggestion.
                if (r.GhostT <= 0f)
                {
                    foreach (var o in this.runners)
                    {
                        if (o == r || o.Finished)
                        {
                            continue;
                        }

                        if (MathF.Abs(o.S - r.S) >= Dials.ClearAhead)
                        {
                            continue;
                        }

                        var gapNow = r.Lat - o.Lat;
                        var gapNew = newLat - o.Lat;
                        var keep = MathF.Min(MathF.Abs(gapNow), Dials.JostleW);
                        if (MathF.Sign(gapNew) != MathF.Sign(gapNow) || MathF.Abs(gapNew) < keep)
                        {
                            newLat = o.Lat + (MathF.Sign(gapNow != 0f ? gapNow : 1f) * keep);
                        }
                    }
                }

                r.Lat = MathF.Max(-halfW, MathF.Min(halfW, newLat));

                // Integrate: speed chases the goal under accel/brake limits; progress along
                // the centre line pays for the line you run. Power is the jump: Speed decides
                // where a runner ends up, Power how sharply it gets there.
                var dv = vGoal - r.V;
                var accel = Dials.Accel * (0.85f + (0.30f * r.CPower)) * (r.RushT > 0f ? 1.25f : 1f);
                r.V += MathF.Max(-Dials.Brake * dt, MathF.Min(accel * dt, dv));
                r.V = MathF.Max(0f, r.V);
                var lineFactor = MathF.Max(0.55f, MathF.Min(1.6f, 1f - (here.Kappa * r.Lat)));
                r.S += (r.V * dt) / lineFactor;
                r.DistanceRun += r.V * dt;
                r.Gait += (r.V / 1.15f) * dt; // stride phase for the viewer's little limbs

                if (r.S >= this.track.Length)
                {
                    r.Finished = true;
                    var overshoot = (r.S - this.track.Length) / MathF.Max(r.V, 0.01f) * lineFactor;
                    r.FinishTime = this.Time - overshoot;
                    this.FinishedCount++;
                    if (this.FinishedCount == 1)
                    {
                        this.WinnerTime = r.FinishTime;
                        this.Log("wins", r.Idx);
                    }

                    this.Log("finish", r.Idx, place: this.FinishedCount - 1);
                }
            }

            // Overtake events, from place changes among the still-running (rate-limited by the
            // place sort happening pre-move: a swap must survive a full tick to be a story).
            foreach (var r in this.runners)
            {
                if (!r.Finished && r.Place < r.LastPlace && (r.Place == 0 || r.IsPlayer || r.Place < 3))
                {
                    this.Log(r.Place == 0 ? "tookLead" : "overtake", r.Idx, place: r.Place);
                }
            }

            // The race ends when everyone is home, or the lingering clock clamps stragglers.
            if (this.FinishedCount == this.runners.Length
                || (this.FinishedCount > 0 && this.Time > this.WinnerTime + Dials.FinishLinger))
            {
                foreach (var r in this.runners)
                {
                    if (!r.Finished)
                    {
                        r.Finished = true;
                        r.FinishTime = this.Time + ((this.track.Length - r.S) / 8f);
                        this.FinishedCount++;
                    }
                }

                this.Order = this.runners.OrderBy(r => r.FinishTime).Select(r => r.Idx).ToArray();
                this.Margin = this.Order.Length > 1
                    ? this.runners[this.Order[1]].FinishTime - this.runners[this.Order[0]].FinishTime
                    : 0f;
                this.Done = true;
            }
        }

        /// <summary>Run the whole race to its end, with a hard guard on tick count.</summary>
        public void RunToEnd()
        {
            var guard = 0;
            while (!this.Done && guard++ < 30 * 300)
            {
                this.Step();
            }
        }

        internal static Race Create(int seed, CourseDef course, IReadOnlyList<RaceRunner> field, string weatherKey, float[]? condition)
        {
            var track = BuildTrack(course);
            var weather = Weathers.TryGetValue(weatherKey, out var w) ? w : Weathers["clear"];
            var rng = new RaceRng(unchecked((uint)seed));

            var runners = new Runner[field.Count];
            var halfW0 = (track.Width / 2f) - Dials.LaneMargin;
            for (var idx = 0; idx < field.Count; idx++)
            {
                var f = field[idx];
                var stats = f.Stats;
                var cond = condition != null && idx < condition.Length ? condition[idx] : 1f;
                var style = StyleOf(stats);

                // Per-runner draws, in field order: gait's phase, then (after the non-random
                // fields below) the steering cadence's initial offset. The draw order is the
                // replay contract; do not reorder anything that touches rng.
                var gait = (float)(rng.Next() * Math.PI * 2);

                var (sf, sc, ss) = StyleScores(stats);
                var sSum = MathF.Max(1e-4f, sf + sc + ss);
                var r = new Runner
                {
                    Idx = idx, Name = f.Name, IsPlayer = f.IsPlayer, Stats = stats, Condition = cond, Style = style,
                    Pace = BlendPace(stats),
                    WFront = sf / sSum, WCloser = sc / sSum, WStalker = ss / sSum,
                    S = 0f, Lat = -halfW0 + ((2f * halfW0) * (idx + 0.5f) / field.Count), V = 0f,
                    Stamina = 1f, Fortune = 0f, Gait = gait,
                    Place = idx, LastPlace = idx,
                    CSpeed = Cfn(stats.Speed), CPower = Cfn(stats.Power), CStamina = Cfn(stats.Stamina),
                    CFocus = Cfn(stats.Focus), CHeart = Cfn(stats.Heart),
                };

                // One stat is in top speed, and it is Speed.
                r.VTop = Dials.V0
                    * (1f + (Dials.SpeedSpan * CatTunes[course.Category].Speed * (r.CSpeed - CfnFive)))
                    * cond * (1f + r.Silver.Stride);
                r.DecideT = (float)(rng.Next() * 0.4);
                runners[idx] = r;
            }

            // Stamina and Heart against the field's own middle (see Runner.RelStamina). No RNG
            // is touched here, so the draw order the replay contract depends on is untouched.
            var meanStamina = runners.Average(x => x.CStamina);
            var meanHeart = runners.Average(x => x.CHeart);
            foreach (var r in runners)
            {
                r.RelStamina = r.CStamina - meanStamina;
                r.RelHeart = r.CHeart - meanHeart;
            }

            // The post-position draw, shuffled from the race's own seed: honest race-day luck,
            // deterministic and worth narrating.
            var lats = runners.Select(r => r.Lat).ToArray();
            for (var i = lats.Length - 1; i > 0; i--)
            {
                var j = rng.Int(i + 1);
                (lats[i], lats[j]) = (lats[j], lats[i]);
            }

            for (var i = 0; i < runners.Length; i++)
            {
                runners[i].Lat = lats[i];
                runners[i].Draw = lats[i];
            }

            // The gates: reaction (the quick half of Speed, the alert half of Focus) shaves a
            // randomly drawn delay.
            foreach (var r in runners)
            {
                var rx = (0.6f * r.CSpeed) + (0.4f * r.CFocus);
                var delay = Dials.GateMin + ((float)rng.Next() * Dials.GateRand * (1f - (Dials.GateReact * rx)));
                delay *= 1f - MathF.Min(0.6f, r.Silver.Gate);
                r.GateDelay = MathF.Max(0.04f, delay);
            }

            // The update order, likewise shuffled once: runners step in this order every tick,
            // and whoever steps first wins ties in blocking and jostle.
            var updateOrder = Enumerable.Range(0, runners.Length).ToArray();
            for (var i = updateOrder.Length - 1; i > 0; i--)
            {
                var j = rng.Int(i + 1);
                (updateOrder[i], updateOrder[j]) = (updateOrder[j], updateOrder[i]);
            }

            // The first bend and the posts. Post 1 is the stall nearest the first bend's
            // inside: the shortest run to the turn.
            var firstBendKappa = 0f;
            var firstBendDist = 0f;
            for (var s = 2f; s < track.Length; s += 2f)
            {
                var k = track.At(s).Kappa;
                if (MathF.Abs(k) > 1e-4f)
                {
                    firstBendKappa = k;
                    firstBendDist = s;
                    break;
                }
            }

            var inside = firstBendKappa >= 0f ? 1f : -1f;
            var postOrder = runners.OrderByDescending(r => r.Lat * inside).ToArray();
            for (var i = 0; i < postOrder.Length; i++)
            {
                postOrder[i].Post = i + 1;
            }

            // Card timing draws: one Gauss plus one Next per runner, drawn whether or not the
            // card is carried, so the stream shape never depends on carding.
            foreach (var r in runners)
            {
                var focus = r.CFocus;
                var llErr = MathF.Max(-1.2f, MathF.Min(1.2f, (float)rng.Gauss())) * (0.55f - (0.40f * focus));
                r.LastLightAt = MathF.Max(5f, Dials.BurstSecs * r.VTop * (1f + (0.5f * llErr)));
                r.LastLightEarly = llErr > 0.35f;
                r.WeaverWait = 0.4f + ((1.7f - (1.2f * focus)) * (float)rng.Next());
                r.GhostT = 0f;
            }

            // The day-form roll: exactly one Gaussian per runner, drawn here, in field order.
            // Same band for everyone; no stat skews it.
            foreach (var r in runners)
            {
                var band = Dials.FormBand;
                var form = MathF.Max(-2f, MathF.Min(2f, (float)rng.Gauss()));
                r.DayForm = form * band;
            }

            var race = new Race
            {
                track = track,
                weather = weather,
                rng = rng,
                runners = runners,
                updateOrder = updateOrder,
                course = course,
                FirstBendDir = firstBendKappa >= 0f ? "left" : "right",
                FirstBendDist = firstBendDist,
                FirstBendKappa = firstBendKappa,
            };
            return race;
        }
    }

    /// <summary>Build a race, fully decided by its arguments: seed, course, field, weather key
    /// and per-runner condition. The "clear" default is deliberate; weather selection is
    /// caller-side via <see cref="PickWeather"/>.</summary>
    public static Race CreateRace(int seed, CourseDef course, IReadOnlyList<RaceRunner> field, string weather = "clear", float[]? condition = null) =>
        Race.Create(seed, course, field, weather, condition);

    /// <summary>The field sorted by progress, split where daylight opens: the camera frames one
    /// group and cuts between groups when the field breaks up. Groups come back
    /// front-first.</summary>
    public static List<RaceGroup> GroupRunners(Race race, float? gap = null)
    {
        var g = gap ?? Dials.GroupGap;
        var live = race.Runners.OrderByDescending(r => r.S).ToList();
        var groups = new List<RaceGroup>();
        RaceGroup? current = null;
        foreach (var r in live)
        {
            if (current == null || current.STail - r.S > g)
            {
                current = new RaceGroup { SHead = r.S, STail = r.S, HasLeader = groups.Count == 0 };
                groups.Add(current);
            }

            current.RunnerIdx.Add(r.Idx);
            current.STail = r.S;
            if (r.IsPlayer)
            {
                current.HasPlayer = true;
            }
        }

        return groups;
    }
}
