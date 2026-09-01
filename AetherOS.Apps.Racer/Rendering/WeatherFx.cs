namespace AetherOS.Apps.Racer.Rendering;

using System;
using System.Numerics;

using Dalamud.Bindings.ImGui;

/// <summary>The seven skies, as the drawing side sees them. Resolved from the weather's element,
/// because the element is the only handle the race object hands the viewer and the map is a clean
/// bijection with the wheel.</summary>
public enum WeatherSky
{
    /// <summary>No element: the default sky, and the one that must cost nothing.</summary>
    Clear,

    /// <summary>ice: slow drift, and the only weather with a resolvable mark.</summary>
    Snowfall,

    /// <summary>water: steep streaks, and ripples on the road where they land.</summary>
    Rain,

    /// <summary>wind: carries the ground's own element, in bursts. Owns no mark of its own.</summary>
    Gale,

    /// <summary>fire: a warm dark veil that breathes, and thermals nobody can quite see.</summary>
    Haze,

    /// <summary>lightning: almost nothing happening, punctuated.</summary>
    Static,

    /// <summary>earth: the densest sky, and the only one whose marks are darker than its air.</summary>
    Dustveil,
}

/// <summary>
/// The sky over a race: a screen-space, fixed-population weather layer. One instance owned by the
/// race stage.
///
/// <para>A torus, not a fountain: the pool is filled once and never grows. A mark that leaves the
/// stage wraps to the opposite edge, so the population is constant and there is no spawn path to
/// cap. Alpha comes from screen position (a fade band at each edge) rather than from age. Struct
/// marks in a pre-sized array, iterated by index, no allocation after construction.</para>
///
/// <para>Positions are normalised to the stage rect, so a resize is free and nothing rotates with
/// the track-up camera. The single exception is rain's ground ripples, which belong on the road
/// and are handed out as track coordinates for the stage to project (<see cref="TryRipple"/>).</para>
///
/// <para>Every scatter draw comes off <see cref="Next"/>, seeded from the race seed. The race's
/// own RNG is the sim's contract and nothing decorative may touch it, so a replay snows the same
/// way without the sim ever knowing there was weather on it.</para>
/// </summary>
public sealed class WeatherFx
{
    /// <summary>Hard cap on live marks; with a one-call far mark it bounds the layer's floor cost
    /// at 64 drawlist calls.</summary>
    public const int MaxMarks = 64;

    /// <summary>The luminance every cast is authored at: the value the six ground washes were
    /// solved to. A veil at exactly the wash's own brightness changes the stage's hue and leaves
    /// its brightness where the dressing put it, at any alpha.</summary>
    public const float CastLuminance = 0.1450f;

    /// <summary>How far past the stage edge a mark travels before it wraps. Wide enough that a
    /// big thermal never pops at the rim.</summary>
    private const float WrapMargin = 0.08f;

    /// <summary>The fade band at each edge, as a fraction of the stage.</summary>
    private const float EdgeBand = 0.08f;

    /// <summary>What the px figures below are authored against: a phone stage about this tall.
    /// Every size and speed scales by <c>size.Y / ReferenceStage</c>.</summary>
    private const float ReferenceStage = 700f;

    /// <summary>Nothing atmospheric may be the brightest thing on screen. Back layer under the
    /// first number, front layer under the second, by clamp, so a mistuned roll cannot break it.</summary>
    private const float BackAlphaCap = 0.22f;
    private const float FrontAlphaCap = 0.30f;

    /// <summary>One mark. Everything about it is a float or a flag and none of it is a reference,
    /// so the whole layer is one contiguous 64-slot array the draw loop walks by index.</summary>
    private struct Mark
    {
        /// <summary>Normalised to the stage rect: 0..1 across, 0..1 down.</summary>
        public float X;
        public float Y;

        /// <summary>Stage-heights per second, both axes. X is converted by the stage's aspect at
        /// integration time, so a diagonal is a diagonal at any aspect.</summary>
        public float Vx;
        public float Vy;

        /// <summary>The instance's own 0..1 roll: size, speed and alpha all read it, so one mark
        /// is consistently the small faint slow one.</summary>
        public float Roll;

        /// <summary>Sway, flicker or spin phase.</summary>
        public float Phase;

        /// <summary>Authored peak alpha, before the edge fade and before the caps.</summary>
        public float Alpha;

        /// <summary>In the thin layer drawn over the runners.</summary>
        public bool Front;

        /// <summary>A gale arc rather than a carried mark. Arcs cross once and go back to sleep.</summary>
        public bool Arc;

        /// <summary>Its slot exists, it simply is not drawn; a burst has a population without the
        /// pool having a spawn path.</summary>
        public bool Idle;
    }

    private readonly Mark[] marks = new Mark[MaxMarks];

    /// <summary>Rain's ground ripples, in track coordinates for the stage to project. Three of
    /// them buys rain that falls on the road rather than in front of the camera.</summary>
    private readonly float[] rippleAhead = new float[3];
    private readonly float[] rippleLat = new float[3];
    private readonly float[] rippleAge = new float[3];
    private readonly float[] rippleLife = new float[3];

    private WeatherSky sky;
    private int count;
    private int arcFirst;
    private uint rng = 1u;
    private float clock;

    private Vector2 origin;
    private Vector2 size = Vector2.One;
    private float aspect = 1f;
    private float unit = 1f;
    private float frontUnit = 8f;

    private Vector4 markColour;
    private Vector4 accentColour;
    private Vector4 cast;
    private float castAlpha;

    // The gale's cargo: the ground's element, re-read every frame.
    private string carriedKey = string.Empty;
    private Vector4 carriedColour;
    private FxMotion carriedMotion;
    private float carriedVy;

    // The gale's burst clock and the prevailing wind, fixed for the race and shared with the
    // dressing's lean so the roadside grass and the moving air bend the same way.
    private float gustWait;
    private float gustT;
    private float lean = 1f;

    // The strike: one reserved slot, because lightning is the one thing here that is not a torus.
    private float strikeWait;
    private float strikeAge;
    private float strikeLife;
    private float strikeX;
    private float strikeY;
    private Vector2 strikeSite;
    private bool strikeSited;
    private float strikeSeed;
    private float flash;

    /// <summary>Is there a sky at all? Clear answers no and every entry point below leaves
    /// immediately: no pool tick, no cast, no draw call. A clear sky must measure identical to no
    /// weather layer at all.</summary>
    public bool Active => this.sky != WeatherSky.Clear;

    public WeatherSky Sky => this.sky;

    /// <summary>Live marks. Dev read-out only.</summary>
    public int LiveMarks
    {
        get
        {
            if (this.sky == WeatherSky.Clear)
            {
                return 0;
            }

            var n = 0;
            for (var i = 0; i < this.count; i++)
            {
                if (!this.marks[i].Idle)
                {
                    n++;
                }
            }

            return n;
        }
    }

    /// <summary>Holds the transients (the strike, the next gust burst). Set by the stage while
    /// the camera is locked on the finish, so no strike lands near the tape.</summary>
    public bool HoldTransients { get; set; }

    /// <summary>Asked by the strike: is a bolt of this radius, centred here, clear of the road?
    /// Screen space, set once by the stage. A callback rather than a rectangle because the road
    /// is a curve under a rotating camera and its bounding box would forbid every site. Null means
    /// no opinion, and the strike falls back to its old top-of-stage band.</summary>
    public Func<Vector2, float, bool>? StrikeSiteClear { get; set; }

    /// <summary>Projects the point a strike chose, every frame it is drawn: a site kept as a screen
    /// fraction slides onto the road it was chosen to avoid. Null keeps the screen-fraction site,
    /// which is what a caller with no camera wants.</summary>
    public Func<Vector2, Vector2>? SiteToScreen { get; set; }

    /// <summary>Turns a screen point back into the space <see cref="SiteToScreen"/> reads.</summary>
    public Func<Vector2, Vector2>? SiteFromScreen { get; set; }

    /// <summary>Opens a sky. Called once at race start, off the race's own seed. The only method
    /// here that writes the pool's population: after this the array is walked and never grown.</summary>
    /// <param name="element">The weather's element; empty is clear.</param>
    /// <param name="seed">The race seed. A replay snows the same way.</param>
    /// <param name="prevailing">The race's prevailing wind, +1 or -1, shared with the dressing's
    /// lean so the grass at the roadside bends the way the air is blowing.</param>
    public void Begin(string? element, int seed, float prevailing = 1f)
    {
        this.sky = element switch
        {
            "ice" => WeatherSky.Snowfall,
            "water" => WeatherSky.Rain,
            "wind" => WeatherSky.Gale,
            "fire" => WeatherSky.Haze,
            "lightning" => WeatherSky.Static,
            "earth" => WeatherSky.Dustveil,
            _ => WeatherSky.Clear,
        };

        this.rng = unchecked((uint)seed ^ 0x5c4f01u);
        this.clock = 0f;
        this.flash = 0f;
        this.strikeAge = 0f;
        this.strikeLife = 0f;
        this.gustT = 0f;
        this.lean = prevailing >= 0f ? 1f : -1f;
        this.carriedKey = string.Empty;
        this.carriedColour = ElementFx.Neutral.Body;
        this.carriedMotion = FxMotion.None;
        this.carriedVy = 8f / ReferenceStage;
        this.HoldTransients = false;

        var look = ElementFx.For(element);
        this.cast = ElementFx.AtLuminance(look.Tint, CastLuminance);

        if (this.sky == WeatherSky.Clear)
        {
            this.count = 0;
            this.arcFirst = 0;
            return;
        }

        // Density, depth split and cast alpha; every count is under the 64 cap by construction.
        int back, front, arcs;
        switch (this.sky)
        {
            case WeatherSky.Snowfall:
                back = 30; front = 4; arcs = 0; this.castAlpha = 0.08f;
                this.markColour = look.Body;
                break;

            case WeatherSky.Rain:
                back = 28; front = 4; arcs = 0; this.castAlpha = 0.09f;
                this.markColour = look.Body;
                break;

            case WeatherSky.Gale:
                back = 11; front = 1; arcs = 3; this.castAlpha = 0.06f;
                this.markColour = look.Tint;
                break;

            case WeatherSky.Haze:
                back = 12; front = 2; arcs = 0; this.castAlpha = 0.10f;

                // A thermal is air, not a mark with a body, so it takes fire's light rather than
                // the ember's hot gold.
                this.markColour = look.Tint;
                break;

            case WeatherSky.Static:
                back = 8; front = 0; arcs = 0; this.castAlpha = 0.07f;
                this.markColour = look.Body;
                break;

            default:
                back = 34; front = 4; arcs = 0; this.castAlpha = 0.10f;

                // Earth absorbs, so a dust mote is drawn in the umber body, darker than the air
                // it floats in. A dust veil made of bright specks is snow in brown.
                this.markColour = look.Body;
                break;
        }

        this.accentColour = look.Tint;
        this.arcFirst = back + front;
        this.count = Math.Min(MaxMarks, back + front + arcs);
        for (var i = 0; i < this.count; i++)
        {
            var isFront = i >= back && i < back + front;
            this.marks[i] = this.Roll(isFront);
            this.marks[i].X = this.Next();
            this.marks[i].Y = this.Next();
            if (i >= this.arcFirst)
            {
                this.marks[i].Arc = true;
                this.marks[i].Idle = true;
            }
        }

        this.gustWait = 1.5f + (this.Next() * 1.5f);
        this.strikeWait = 1.5f + (this.Next() * 3f);

        for (var i = 0; i < this.rippleAge.Length; i++)
        {
            this.RollRipple(i);
            this.rippleAge[i] = this.Next() * this.rippleLife[i];
        }
    }

    /// <summary>One tick of the whole layer. Nothing here allocates, nothing here draws, and on a
    /// clear sky nothing here runs.</summary>
    /// <param name="dt">Seconds since the last frame, already clamped by the stage.</param>
    /// <param name="stageTL">The stage rect's top-left, in screen px.</param>
    /// <param name="stageSize">The stage rect's size, in screen px.</param>
    /// <param name="lingPx">A drawn runner's height in px. The front layer's size cap is measured
    /// against the creature rather than chosen.</param>
    /// <param name="groundElement">The element of the ground under the camera right now, for the
    /// gale to carry. Ignored by every other sky.</param>
    public void Update(float dt, Vector2 stageTL, Vector2 stageSize, float lingPx, string? groundElement)
    {
        if (this.sky == WeatherSky.Clear)
        {
            return;
        }

        this.origin = stageTL;
        this.size = stageSize;
        this.aspect = stageSize.X > 1f ? stageSize.Y / stageSize.X : 1f;
        this.unit = Math.Clamp(stageSize.Y / ReferenceStage, 0.45f, 2.6f);

        // The front layer's ceiling: an eighth of a runner's drawn height, so it shrinks with the
        // creature rather than staying big while the field gets small.
        this.frontUnit = MathF.Max(3f, lingPx * 0.125f);
        this.clock += dt;

        switch (this.sky)
        {
            case WeatherSky.Gale:
                this.UpdateCarried(groundElement);
                this.UpdateGusts(dt);
                break;

            case WeatherSky.Static:
                this.UpdateStrike(dt);
                break;

            case WeatherSky.Rain:
                this.UpdateRipples(dt);
                break;
        }

        for (var i = 0; i < this.count; i++)
        {
            ref var m = ref this.marks[i];
            if (m.Idle)
            {
                continue;
            }

            var vx = m.Vx;
            switch (this.sky)
            {
                case WeatherSky.Snowfall:
                    // The sway is a lateral velocity, not a displacement, so a flake wanders
                    // instead of vibrating.
                    vx += MathF.Sin((this.clock * 2.2f) + m.Phase) * 0.031f;
                    break;

                case WeatherSky.Haze:
                    vx += MathF.Sin((this.clock * 1.4f) + m.Phase) * 0.014f;
                    break;

                case WeatherSky.Gale when !m.Arc:
                    // Carried marks stay present between bursts and merely accelerate during one.
                    vx += this.lean * this.gustT * (0.34f + (m.Roll * 0.26f));
                    break;
            }

            m.X += vx * dt * this.aspect;
            m.Y += m.Vy * dt;
            this.Wrap(ref m);
        }
    }

    /// <summary>The cast: one <c>AddRectFilled</c>, the only part that is genuinely full-screen.
    /// Authored at the wash's own luminance, so it moves the stage's hue and leaves the dressing's
    /// solve exactly where it put it.</summary>
    public void DrawCast(ImDrawListPtr dl, Vector2 stageTL, Vector2 stageBR, float rounding)
    {
        if (this.sky == WeatherSky.Clear)
        {
            return;
        }

        var a = this.castAlpha;
        if (this.sky == WeatherSky.Haze)
        {
            // At plus or minus 0.015 it reads as heat; any more and the stage throbs.
            a += 0.015f * MathF.Sin(this.clock * 0.55f);
        }
        else if (this.sky == WeatherSky.Static && this.flash > 0f)
        {
            // The flash IS the cast, at zero additional calls. Capped at 0.14, which keeps it
            // under the finish tape's white.
            a += 0.07f * this.flash;
        }

        dl.AddRectFilled(stageTL, stageBR, ImGui.ColorConvertFloat4ToU32(this.cast with { W = a }), rounding);
    }

    /// <summary>The bulk of the layer, drawn between the dressing and the field: weather is over
    /// the ground and under the creature.</summary>
    public void DrawBack(ImDrawListPtr dl)
    {
        if (this.sky == WeatherSky.Clear)
        {
            return;
        }

        for (var i = 0; i < this.count; i++)
        {
            ref readonly var m = ref this.marks[i];
            if (!m.Front && !m.Idle)
            {
                this.DrawMark(dl, in m, front: false);
            }
        }

        if (this.sky == WeatherSky.Static && this.strikeLife > 0f)
        {
            this.DrawStrike(dl);
        }
    }

    /// <summary>The thin layer in front, drawn immediately after the runners and before the whole
    /// HUD, so the banner, the progress rail and the narration sit on top of every weather mark
    /// for free.</summary>
    public void DrawFront(ImDrawListPtr dl)
    {
        if (this.sky == WeatherSky.Clear)
        {
            return;
        }

        for (var i = 0; i < this.count; i++)
        {
            ref readonly var m = ref this.marks[i];
            if (m.Front && !m.Idle)
            {
                this.DrawMark(dl, in m, front: true);
            }
        }
    }

    /// <summary>Hands out one live ground ripple in TRACK coordinates for the stage to project.
    /// <paramref name="ahead"/> is a fraction of the visible run of track (negative is behind the
    /// camera's focus), <paramref name="lat"/> a fraction of the road's half-width, and
    /// <paramref name="t"/> the ripple's 0..1 life. Only rain has any.</summary>
    public bool TryRipple(int i, out float ahead, out float lat, out float t)
    {
        ahead = 0f;
        lat = 0f;
        t = 0f;
        if (this.sky != WeatherSky.Rain || i < 0 || i >= this.rippleAge.Length || this.rippleLife[i] <= 0f)
        {
            return false;
        }

        ahead = this.rippleAhead[i];
        lat = this.rippleLat[i];
        t = Math.Clamp(this.rippleAge[i] / this.rippleLife[i], 0f, 1f);
        return true;
    }

    /// <summary>How many ripple slots there are at all, so the stage's loop needs no constant.</summary>
    public int RippleSlots => this.sky == WeatherSky.Rain ? this.rippleAge.Length : 0;

    /// <summary>The ripple's colour, so the stage does not have to know what water looks like.</summary>
    public Vector4 RippleColour => this.markColour;

    /// <summary>Rolls one mark's constants. Sizes and speeds are authored in px and px/s against a
    /// <see cref="ReferenceStage"/>-tall stage and stored as stage-heights per second, so the
    /// weather is the same weather on any glass.</summary>
    private Mark Roll(bool front)
    {
        var m = default(Mark);
        m.Roll = this.Next();
        m.Phase = this.Next() * MathF.Tau;
        m.Front = front;
        const float px = 1f / ReferenceStage;

        switch (this.sky)
        {
            case WeatherSky.Snowfall:
                m.Vy = (40f + (m.Roll * 30f)) * px;
                m.Alpha = front ? 0.22f + (m.Roll * 0.08f) : 0.10f + (m.Roll * 0.08f);
                break;

            case WeatherSky.Rain:
                // Steep and near-vertical with a small constant lean; swaying rain is snow.
                m.Vy = front ? (900f + (m.Roll * 200f)) * px : (520f + (m.Roll * 240f)) * px;
                m.Vx = m.Vy * this.lean * (0.105f + (m.Roll * 0.071f));
                m.Alpha = front ? 0.22f : 0.12f + (m.Roll * 0.08f);
                break;

            case WeatherSky.Gale:
                m.Vx = this.lean * (70f + (m.Roll * 70f)) * px;
                m.Vy = this.carriedVy;
                m.Alpha = front ? 0.20f : 0.12f + (m.Roll * 0.08f);
                break;

            case WeatherSky.Haze:
                m.Vy = -(18f + (m.Roll * 16f)) * px;
                m.Alpha = 0.06f + (m.Roll * 0.06f);
                break;

            case WeatherSky.Static:
                m.Alpha = 0.14f + (m.Roll * 0.08f);
                break;

            default:
                // Earth's settle half only; the tumble is expressed as a size flicker at draw
                // time, which reads as a chip turning edge-on.
                m.Vy = (20f + (m.Roll * 20f)) * px;
                m.Vx = this.lean * (30f + (m.Roll * 30f)) * px;
                m.Alpha = 0.10f + (m.Roll * 0.10f);
                break;
        }

        return m;
    }

    /// <summary>The recycle: a mark that leaves the stage arrives on the opposite edge of the
    /// axis it left by, with a fresh cross-axis position and a fresh roll. The population never
    /// changes, so neither does the number of calls.</summary>
    private void Wrap(ref Mark m)
    {
        var outLeft = m.X < -WrapMargin;
        var outRight = m.X > 1f + WrapMargin;
        var outTop = m.Y < -WrapMargin;
        var outBottom = m.Y > 1f + WrapMargin;
        if (!outLeft && !outRight && !outTop && !outBottom)
        {
            return;
        }

        if (m.Arc)
        {
            // An arc that has crossed goes back to sleep rather than looping: a gale is bursts
            // with quiet between them.
            m.Idle = true;
            return;
        }

        var front = m.Front;
        m = this.Roll(front);
        if (outBottom)
        {
            m.Y = -WrapMargin;
            m.X = this.Next();
        }
        else if (outTop)
        {
            m.Y = 1f + WrapMargin;
            m.X = this.Next();
        }
        else if (outRight)
        {
            m.X = -WrapMargin;
            m.Y = this.Next();
        }
        else
        {
            m.X = 1f + WrapMargin;
            m.Y = this.Next();
        }
    }

    private void UpdateCarried(string? groundElement)
    {
        var key = groundElement ?? string.Empty;
        if (string.Equals(key, this.carriedKey, StringComparison.Ordinal))
        {
            return;
        }

        this.carriedKey = key;
        var look = ElementFx.For(key);
        this.carriedColour = look.Body;
        this.carriedMotion = look.Motion;

        // The carried mark keeps its own element's vertical behaviour: flakes still fall, embers
        // still climb, dust still hangs.
        this.carriedVy = look.Motion switch
        {
            FxMotion.Drift => 45f,
            FxMotion.Fall => 260f,
            FxMotion.Rise => -30f,
            FxMotion.Tumble => 30f,
            _ => 8f,
        } / ReferenceStage;

        for (var i = 0; i < this.arcFirst; i++)
        {
            this.marks[i].Vy = this.carriedVy;
        }
    }

    private void UpdateGusts(float dt)
    {
        // The burst envelope: it arrives at once and lets go over about a second and a quarter.
        this.gustT = MathF.Max(0f, this.gustT - (dt * 0.8f));

        this.gustWait -= dt;
        if (this.gustWait > 0f || this.HoldTransients)
        {
            return;
        }

        this.gustWait = 1.5f + (this.Next() * 1.5f);
        this.gustT = 1f;

        // 2 to 4 arcs, always behind: a stroke across a runner's face is what the front cap
        // exists to prevent.
        var wanted = 2 + (int)(this.Next() * 2.99f);
        for (var i = this.arcFirst; i < this.count && wanted > 0; i++)
        {
            ref var m = ref this.marks[i];
            if (!m.Idle)
            {
                continue;
            }

            m = this.Roll(front: false);
            m.Arc = true;
            m.Idle = false;
            m.Vx = this.lean * (390f + (m.Roll * 210f)) / ReferenceStage;
            m.Vy = (m.Roll - 0.5f) * 0.05f;
            m.X = this.lean > 0f ? -WrapMargin : 1f + WrapMargin;
            m.Y = 0.10f + (this.Next() * 0.80f);
            m.Alpha = 0.16f + (m.Roll * 0.09f);
            wanted--;
        }
    }

    private void UpdateStrike(float dt)
    {
        this.flash = MathF.Max(0f, this.flash - (dt * 3.4f));

        if (this.strikeLife > 0f)
        {
            this.strikeAge += dt;
            if (this.strikeAge >= this.strikeLife)
            {
                this.strikeLife = 0f;
            }

            return;
        }

        this.strikeWait -= dt;
        if (this.strikeWait > 0f || this.HoldTransients)
        {
            return;
        }

        this.strikeWait = 2.5f + (this.Next() * 3.5f);
        this.strikeSeed = this.Next() * 10f;

        // The flash fires whatever happens below: sheet lightning, the whole stage lit for an
        // instant, folded into the cast DrawCast was already drawing.
        this.flash = 1f;

        // The bolt is only ever drawn where there is no road under it: a small fork is a local
        // event, and a bolt landing on a runner reads as an attack the player cannot defend.
        // A road holding 70% of the stage width refuses every site under a tighter test.
        var span = StrikeSpan(this.strikeSeed) * this.unit;
        var clearance = span * 0.25f;
        for (var attempt = 0; attempt < StrikeSiteTries; attempt++)
        {
            var x = 0.02f + (this.Next() * 0.96f);
            var y = 0.04f + (this.Next() * 0.68f);
            var at = this.origin + new Vector2(x * this.size.X, y * this.size.Y);
            if (this.StrikeSiteClear != null && !this.StrikeSiteClear(at, clearance))
            {
                continue;
            }

            this.strikeX = x;
            this.strikeY = y;
            this.strikeSited = this.SiteFromScreen is not null;
            this.strikeSite = this.strikeSited ? this.SiteFromScreen!(at) : Vector2.Zero;
            this.strikeAge = 0f;
            this.strikeLife = 0.22f + (this.Next() * 0.12f);
            return;
        }

        // Nowhere off the road to put it: this strike is sheet lightning and nothing else, which
        // is a real sky and a common one.
        this.strikeLife = 0f;
    }

    /// <summary>How many places a strike will look for road-free sky before giving up and being
    /// sheet lightning only.</summary>
    private const int StrikeSiteTries = 8;

    /// <summary>The bolt's length for a given strike, in stage units. Shared with the site test
    /// so the clearance matches the room the bolt actually needs.</summary>
    private static float StrikeSpan(float seed) => 34f + (seed * 2.4f);

    private void UpdateRipples(float dt)
    {
        for (var i = 0; i < this.rippleAge.Length; i++)
        {
            this.rippleAge[i] += dt;
            if (this.rippleAge[i] >= this.rippleLife[i])
            {
                this.RollRipple(i);
            }
        }
    }

    private void RollRipple(int i)
    {
        this.rippleAge[i] = 0f;
        this.rippleLife[i] = 0.45f + (this.Next() * 0.3f);
        this.rippleAhead[i] = -0.30f + (this.Next() * 0.95f);
        this.rippleLat[i] = -0.84f + (this.Next() * 1.68f);
    }

    /// <summary>Every mark here is authored at low LOD: a snowflake at 2 px does not need three
    /// crossed lines, and a raindrop at 600 px/s is a line, not four discs.</summary>
    private void DrawMark(ImDrawListPtr dl, in Mark m, bool front)
    {
        var alpha = m.Alpha * this.Fade(in m);
        if (alpha <= 0.004f)
        {
            return;
        }

        alpha = MathF.Min(alpha, front ? FrontAlphaCap : BackAlphaCap);
        var at = this.origin + new Vector2(m.X * this.size.X, m.Y * this.size.Y);
        var col = this.markColour with { W = alpha };
        var u = this.unit;

        switch (this.sky)
        {
            case WeatherSky.Snowfall when front:
            {
                // At 4 to 9 px a flake resolves: three crossed lines with a dominant first axis,
                // because three identical hairlines is the arcade tell.
                var r = MathF.Min(this.frontUnit, (3f + (m.Roll * 6f)) * u);
                var a0 = (this.clock * 0.55f) + m.Phase;
                var thin = ImGui.ColorConvertFloat4ToU32(col with { W = alpha * 0.7f });
                var core = ImGui.ColorConvertFloat4ToU32(col);
                for (var arm = 0; arm < 3; arm++)
                {
                    var (sin, cos) = MathF.SinCos(a0 + (arm * (MathF.PI / 3f)));
                    var d = new Vector2(cos, sin) * r;
                    dl.AddLine(at - d, at + d, arm == 0 ? core : thin, arm == 0 ? 1.6f : 1f);
                }

                dl.AddCircleFilled(at, r * 0.28f, core, 6);
                break;
            }

            case WeatherSky.Snowfall:
            {
                // At 2 px three crossed lines are one grey smudge; one bead is the honest read.
                dl.AddCircleFilled(at, (1.5f + m.Roll) * u, ImGui.ColorConvertFloat4ToU32(col), 6);
                break;
            }

            case WeatherSky.Rain:
            {
                // A raindrop crossing a phone stage at 600 px/s IS a streak.
                var v = new Vector2(m.Vx, m.Vy);
                var dir = v.LengthSquared() > 1e-6f ? Vector2.Normalize(v) : new Vector2(0f, 1f);
                var span = (front ? 22f : 14f) * (0.75f + (m.Roll * 0.5f)) * u;
                dl.AddLine(at, at - (dir * span), ImGui.ColorConvertFloat4ToU32(col), front ? 2f : 1f);
                break;
            }

            case WeatherSky.Gale when m.Arc:
            {
                // Three tapering strokes keep the head-and-wake read.
                var span = (60f + (m.Roll * 40f)) * u;
                var rise = (14f + (m.Roll * 10f)) * u;
                var dirX = this.lean;
                for (var s = 0; s < 3; s++)
                {
                    var k = (s + 1f) / 3f;
                    var x0 = at.X - (dirX * span * (1f - (s / 3f)));
                    var x1 = at.X - (dirX * span * (1f - ((s + 1) / 3f)));
                    var y0 = at.Y + (MathF.Sin((s / 3f) * MathF.PI) * rise);
                    var y1 = at.Y + (MathF.Sin(((s + 1) / 3f) * MathF.PI) * rise);
                    dl.AddLine(new Vector2(x0, y0), new Vector2(x1, y1),
                        ImGui.ColorConvertFloat4ToU32(this.accentColour with { W = alpha * (0.25f + (0.75f * k)) }),
                        0.9f + (1.8f * k));
                }

                break;
            }

            case WeatherSky.Gale:
            {
                // What the gale carries, at one call: the ground's own element, read off the table.
                var carried = this.carriedColour with { W = alpha };
                var r = (1.6f + (m.Roll * 1.6f)) * u;
                if (this.carriedMotion is FxMotion.Rise or FxMotion.Strike)
                {
                    // Fire and lightning are flecks, not beads: a short streak along the blow.
                    dl.AddLine(at, at - new Vector2(this.lean * r * 5f, 0f),
                        ImGui.ColorConvertFloat4ToU32(carried), 1.2f);
                }
                else
                {
                    dl.AddCircleFilled(at, front ? MathF.Min(r, this.frontUnit) : r,
                        ImGui.ColorConvertFloat4ToU32(carried), 6);
                }

                break;
            }

            case WeatherSky.Haze:
            {
                // Barely there by design; a visible thermal is a bubble.
                dl.AddCircleFilled(at, (10f + (m.Roll * 10f)) * u * (front ? 0.7f : 1f),
                    ImGui.ColorConvertFloat4ToU32(col), 12);
                break;
            }

            case WeatherSky.Static:
            {
                // The air being charged: 1 px grains, nearly stationary, twitching by a pixel.
                var twitch = new Vector2(
                    MathF.Sin((this.clock * 9f) + m.Phase) * 1.6f * u,
                    MathF.Cos((this.clock * 7.4f) + m.Phase) * 1.6f * u);
                dl.AddCircleFilled(at + twitch, (0.9f + (m.Roll * 0.6f)) * u, ImGui.ColorConvertFloat4ToU32(col), 4);
                break;
            }

            default:
            {
                // Dust: larger than snow's beads and much dimmer, drawn in the umber body, darker
                // than the cast it floats in. A shade if anything, never a highlight.
                var flick = 1f + (0.3f * MathF.Sin((this.clock * 3.1f) + m.Phase));
                var r = (3f + (m.Roll * 4f)) * u * flick;
                dl.AddCircleFilled(at, front ? MathF.Min(r, this.frontUnit) : r,
                    ImGui.ColorConvertFloat4ToU32(col), 8);
                break;
            }
        }
    }

    /// <summary>The strike: one glint opening it and one bolt under it. No travel, a strobe,
    /// gone, and never while the camera is locked on the tape.</summary>
    private void DrawStrike(ImDrawListPtr dl)
    {
        var t = Math.Clamp(this.strikeAge / this.strikeLife, 0f, 1f);
        var at = this.strikeSited && this.SiteToScreen is not null
            ? this.SiteToScreen(this.strikeSite)
            : this.origin + new Vector2(this.strikeX * this.size.X, this.strikeY * this.size.Y);
        var span = StrikeSpan(this.strikeSeed) * this.unit;
        var col = this.markColour with { W = 0.30f };

        if (t < 0.4f)
        {
            // The white pop that opens a strike.
            var k = 1f - (t / 0.4f);
            var grow = 0.35f + (0.85f * (t / 0.4f));
            Star4(dl, at, span * 0.42f * grow, span * 0.07f * grow, this.strikeSeed,
                ImGui.ColorConvertFloat4ToU32(col with { W = col.W * k }));
            dl.AddCircleFilled(at, span * 0.05f * (1f - t),
                ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, col.W * k)), 8);
        }

        // Strobes twice over its short life: lightning is the only element whose mark must be
        // capable of being invisible on a given frame.
        if (((int)(t * 8f) % 2) == 1)
        {
            return;
        }

        var dir = new Vector2(MathF.Sin(this.strikeSeed * 3.7f) * 0.35f, 1f);
        dir /= dir.Length();
        var normal = new Vector2(-dir.Y, dir.X);
        var step = span / 4f;
        Span<Vector2> pts = stackalloc Vector2[5];
        pts[0] = at - (dir * span * 0.5f);
        for (var i = 1; i <= 4; i++)
        {
            var kink = MathF.Sin((this.strikeSeed * 11f) + (i * 2.4f)) * span * 0.22f;
            pts[i] = at - (dir * span * 0.5f) + (dir * step * i) + (normal * kink * (i == 4 ? 0f : 1f));
        }

        var glow = ImGui.ColorConvertFloat4ToU32(col with { W = col.W * 0.30f });
        var core = ImGui.ColorConvertFloat4ToU32(col);
        for (var i = 0; i < 4; i++)
        {
            dl.AddLine(pts[i], pts[i + 1], glow, 5f);
        }

        for (var i = 0; i < 4; i++)
        {
            dl.AddLine(pts[i], pts[i + 1], core, 2f);
        }

        var forkDir = (dir * 0.55f) + (normal * (MathF.Sin(this.strikeSeed * 7f) > 0f ? 0.85f : -0.85f));
        dl.AddLine(pts[2], pts[2] + (forkDir * span * 0.3f), core, 1.5f);

        // The white tip: lightning is the only element that emits white rather than its own hue,
        // and it is what makes a violet bolt read as a discharge rather than as a purple stick.
        dl.AddCircleFilled(pts[4], 2.2f * this.unit,
            ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, col.W)), 6);
    }

    private static void Star4(ImDrawListPtr dl, Vector2 at, float longArm, float shortArm, float angle, uint c)
    {
        var (sin, cos) = MathF.SinCos(angle);
        Vector2 Rot(float x, float y) => new(at.X + (x * cos) - (y * sin), at.Y + (x * sin) + (y * cos));
        dl.AddQuadFilled(Rot(0, -longArm), Rot(shortArm, 0), Rot(0, longArm), Rot(-shortArm, 0), c);
        dl.AddQuadFilled(Rot(-longArm, 0), Rot(0, shortArm), Rot(longArm, 0), Rot(0, -shortArm), c);
    }

    /// <summary>Alpha as a function of screen position rather than of age. A mark eases in over
    /// the band it entered through and out over the one it is leaving by, so nothing ever appears
    /// or vanishes mid-stage.</summary>
    private float Fade(in Mark m)
    {
        var fy = MathF.Min(
            Math.Clamp((m.Y + WrapMargin) / EdgeBand, 0f, 1f),
            Math.Clamp((1f + WrapMargin - m.Y) / EdgeBand, 0f, 1f));
        var fx = MathF.Min(
            Math.Clamp((m.X + WrapMargin) / EdgeBand, 0f, 1f),
            Math.Clamp((1f + WrapMargin - m.X) / EdgeBand, 0f, 1f));
        return MathF.Min(fx, fy);
    }

    /// <summary>mulberry32, kept local so the layer never reaches for the race's own RNG. One
    /// uint of state and no allocation.</summary>
    private float Next()
    {
        unchecked
        {
            this.rng += 0x6D2B79F5u;
            var t = this.rng;
            t = (uint)((t ^ (t >> 15)) * (t | 1u));
            t ^= t + (uint)((t ^ (t >> 7)) * (t | 61u));
            return ((t ^ (t >> 14)) & 0xFFFFFFu) / 16777216f;
        }
    }
}
