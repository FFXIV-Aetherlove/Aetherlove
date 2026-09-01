using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace AetherOS.PetKit.Rendering;

/// <summary>The particle families the app knows how to draw, implemented procedurally from
/// ImGui primitives, so swapping in real sheet art later changes only DrawOne.</summary>
public enum ParticleKind
{
    /// <summary>Four-point star twinkle.</summary>
    Sparkle,

    /// <summary>A soft round mote (ceremony dust). Slow, small, faint.</summary>
    Mote,

    /// <summary>A soft glow orb rising close to the body.</summary>
    Glow,

    /// <summary>A burst spark with gravity.</summary>
    Burst,

    /// <summary>An expanding circle outline (ripples, sparkle rings).</summary>
    Ring,

    /// <summary>A spinning crystal shard, an Aethercore splinter.</summary>
    Shard,

    /// <summary>A rising heart (petting, warm refusals).</summary>
    Heart,

    /// <summary>A jagged flicker line (the lightning signature). Short-lived, strobing.</summary>
    Bolt,

    /// <summary>A licking flame fleck that climbs, flickers sideways and cools through its
    /// colour ramp (the fire signature; pair with a red ColorEnd so it dies as a cinder).</summary>
    Ember,

    /// <summary>A raindrop on a gravity arc, stretched along its velocity. Landing on the
    /// ground line kills it and blooms a small ripple and mist where it fell.</summary>
    Droplet,

    /// <summary>A wind streak orbiting the origin: the origin is the centre, spread is the
    /// orbit radius, and the streak sweeps around a squashed ellipse hugging the body.</summary>
    Gust,

    /// <summary>A tumbling stone chip with gravity that bounces on the ground line, kicking
    /// dust on its first, heaviest landing.</summary>
    Pebble,

    /// <summary>A fast electric splinter, a bright streak along its velocity that decelerates
    /// hard and dies quickly (thrown off the body by the lightning signature).</summary>
    Spark,

    /// <summary>A six-armed snowflake falling slowly with a sway.</summary>
    Flake,

    /// <summary>One crisp star flash that grows and vanishes in a third of a second, the
    /// glint that sells ice and the white pop that opens a lightning strike.</summary>
    Glint,
}

/// <summary>A tiny procedural particle pool in pet-local space: positions are authored in the 256 design
/// cell (0,0 = top-left, feet at y=256, x centred at 128), so one live pool follows the pet at any
/// display size and across every surface. Hard-capped at <see cref="MaxParticles"/>.</summary>
public sealed class ParticleFx
{
    private const int MaxParticles = 128;

    /// <summary>The ground line in the 256 cell, where droplets splash and pebbles bounce.</summary>
    private const float GroundY = 246f;

    private struct Particle
    {
        public ParticleKind Kind;
        public Vector2 Pos;
        public Vector2 Vel;
        public float Age;
        public float Life;
        public float Size;
        public Vector4 Color;

        /// <summary>Lerped to over the particle's life; defaults to Color.</summary>
        public Vector4 ColorEnd;
        public float Spin;
        public bool Behind;

        /// <summary>Falling curtain: no gravity, a sway, and a twinkle.</summary>
        public bool Drift;
    }

    private readonly List<Particle> _particles = new(MaxParticles);
    private readonly List<(Vector2 Pos, Vector4 Color, float Size)> _impacts = [];
    private readonly Random _rng = new();

    /// <summary>Anything alive? Surfaces can skip the draw pass entirely when quiet.</summary>
    public bool Any => _particles.Count > 0;

    public void Clear() => _particles.Clear();

    /// <summary>Emits <paramref name="count"/> particles around an origin in the 256 cell.
    /// <paramref name="sizeScale"/> multiplies the kind's own size roll, so one emitter can mix
    /// weights (the heavy stones among an earth flourish's chips).</summary>
    public void Burst(
        ParticleKind kind,
        Vector2 origin,
        int count,
        Vector4 color,
        float spread = 60f,
        bool behind = false,
        Vector4? colorEnd = null,
        float sizeScale = 1f)
    {
        for (var i = 0; i < count; i++)
        {
            Spawn(kind, origin, color, spread, behind, colorEnd, sizeScale);
        }
    }

    /// <summary>Emits from the rim of an ellipse hugging the round body, velocities pointing
    /// outward, so a flourish visibly erupts from the pet rather than from a box around it.
    /// Each kind keeps a whisper of its own motion (embers still climb, sparks still scatter).</summary>
    public void BurstRadial(
        ParticleKind kind,
        Vector2 centre,
        int count,
        Vector4 color,
        float radius,
        float speed,
        bool behind = false,
        Vector4? colorEnd = null,
        float sizeScale = 1f)
    {
        for (var i = 0; i < count; i++)
        {
            var a = (float)_rng.NextDouble() * MathF.Tau;
            var dir = new Vector2(MathF.Cos(a), MathF.Sin(a) * 0.8f);
            Spawn(kind, centre + (dir * radius), color, 0f, behind, colorEnd, sizeScale);
            var p = _particles[^1];
            p.Vel = (dir * speed) + (p.Vel * 0.4f);
            _particles[^1] = p;
        }
    }

    /// <summary>Spawns a band across the top of the pet and sends it drifting down over the
    /// creature, a curtain rather than a fountain. Drifting particles ignore gravity, sway as
    /// they fall and twinkle, so the kind's own launch is overridden while its drawing is not.</summary>
    public void Cascade(
        ParticleKind kind,
        Vector2 topCentre,
        int count,
        Vector4 color,
        float halfWidth,
        float fallSpeed,
        bool behind = false,
        Vector4? colorEnd = null,
        float sizeScale = 1f)
    {
        for (var i = 0; i < count; i++)
        {
            // Spread evenly across the band with a jitter, so the curtain never clumps.
            var slot = count <= 1 ? 0.5f : (i + (float)_rng.NextDouble()) / count;
            var at = topCentre + new Vector2(
                ((slot * 2f) - 1f) * halfWidth,
                (float)_rng.NextDouble() * -26f);

            Spawn(kind, at, color, 0f, behind, colorEnd, sizeScale);
            var p = _particles[^1];
            p.Drift = true;
            p.Vel = new Vector2(
                (((float)_rng.NextDouble() * 2f) - 1f) * 10f,
                fallSpeed * (0.75f + ((float)_rng.NextDouble() * 0.5f)));
            p.Life *= 2.2f;
            _particles[^1] = p;
        }
    }

    /// <summary>One ambient particle, for the idle emitters.</summary>
    public void Emit(
        ParticleKind kind,
        Vector2 origin,
        Vector4 color,
        float spread = 40f,
        bool behind = false,
        Vector4? colorEnd = null,
        float sizeScale = 1f)
        => Spawn(kind, origin, color, spread, behind, colorEnd, sizeScale);

    private void Spawn(
        ParticleKind kind,
        Vector2 origin,
        Vector4 color,
        float spread,
        bool behind,
        Vector4? colorEnd = null,
        float sizeScale = 1f)
    {
        if (_particles.Count >= MaxParticles)
        {
            _particles.RemoveAt(0);
        }

        var r = _rng;
        float Rand(float lo, float hi) => lo + ((float)r.NextDouble() * (hi - lo));

        var p = new Particle
        {
            Kind = kind,
            Pos = origin + new Vector2(Rand(-spread, spread), Rand(-spread * 0.5f, spread * 0.5f)),
            Color = color,
            ColorEnd = colorEnd ?? color,
            Behind = behind,
            Spin = Rand(-2.2f, 2.2f),
        };

        switch (kind)
        {
            case ParticleKind.Sparkle:
                p.Vel = new Vector2(Rand(-14f, 14f), Rand(-30f, -10f));
                p.Life = Rand(0.7f, 1.2f);
                p.Size = Rand(5f, 9f);
                break;

            case ParticleKind.Mote:
                p.Vel = new Vector2(Rand(-8f, 8f), Rand(-16f, -6f));
                p.Life = Rand(1.8f, 3.0f);
                p.Size = Rand(2.5f, 4.5f);
                break;

            case ParticleKind.Glow:
                p.Vel = new Vector2(Rand(-5f, 5f), Rand(-14f, -7f));
                p.Life = Rand(1.4f, 2.2f);
                p.Size = Rand(6f, 12f);
                break;

            case ParticleKind.Burst:
            {
                var a = Rand(-MathF.PI, MathF.PI);
                var speed = Rand(80f, 200f);
                p.Vel = new Vector2(MathF.Cos(a) * speed, (MathF.Sin(a) * speed * 0.8f) - 40f);
                p.Life = Rand(0.45f, 0.8f);
                p.Size = Rand(3.5f, 7f);
                break;
            }

            case ParticleKind.Ring:
                // Spread doubles as the final radius; rings sit still and grow in DrawOne.
                p.Pos = origin;
                p.Vel = Vector2.Zero;
                p.Life = Rand(0.55f, 0.75f);
                p.Size = MathF.Max(18f, spread);
                break;

            case ParticleKind.Shard:
            {
                var sa = Rand(-MathF.PI, MathF.PI);
                var sp = Rand(50f, 130f);
                p.Vel = new Vector2(MathF.Cos(sa) * sp, (MathF.Sin(sa) * sp * 0.7f) - 55f);
                p.Life = Rand(0.55f, 0.85f);
                p.Size = Rand(5f, 9f);
                p.Spin = Rand(-6f, 6f);
                break;
            }

            case ParticleKind.Heart:
                p.Vel = new Vector2(Rand(-18f, 18f), Rand(-70f, -45f));
                p.Life = Rand(0.8f, 1.3f);
                p.Size = Rand(7f, 11f);
                break;

            case ParticleKind.Bolt:
                p.Pos = origin + new Vector2(Rand(-spread, spread), Rand(-spread, spread));
                p.Vel = Vector2.Zero;
                p.Life = Rand(0.22f, 0.34f);
                p.Size = Rand(34f, 58f);
                break;

            case ParticleKind.Ember:
                // Thrown hard upward and given a long life: with the buoyant drag in Update the
                // plume climbs roughly the height of the pet, fanning out low and narrowing to a
                // tip, the way a flame actually stands.
                p.Vel = new Vector2(Rand(-22f, 22f), Rand(-135f, -85f));
                p.Life = Rand(1.0f, 1.5f);
                p.Size = Rand(3.5f, 6.5f);
                break;

            case ParticleKind.Droplet:
                p.Vel = new Vector2(Rand(-75f, 75f), Rand(-200f, -120f));
                p.Life = Rand(1.2f, 1.6f);
                p.Size = Rand(3f, 5f);
                break;

            case ParticleKind.Gust:
                // X is the arc's start phase, Y its ellipse squash (upright enough that the
                // swirl climbs and dips rather than reading as a flat band).
                p.Pos = origin;
                p.Vel = new Vector2(Rand(-MathF.PI, MathF.PI), Rand(0.5f, 0.78f));
                p.Life = Rand(0.75f, 1.05f);
                p.Size = MathF.Max(24f, spread);
                p.Spin = Rand(2.4f, 3.4f);
                break;

            case ParticleKind.Pebble:
                // Thrown high enough to clear the pet's waist and given the airtime to fall
                // back, bounce and settle inside its own life.
                p.Vel = new Vector2(Rand(-95f, 95f), Rand(-275f, -180f));
                p.Life = Rand(1.15f, 1.6f);
                p.Size = Rand(3f, 5.5f);
                p.Spin = Rand(-7f, 7f);
                break;

            case ParticleKind.Spark:
            {
                var ka = Rand(-MathF.PI, MathF.PI);
                var kv = Rand(150f, 260f);
                p.Vel = new Vector2(MathF.Cos(ka) * kv, MathF.Sin(ka) * kv * 0.9f);
                p.Life = Rand(0.2f, 0.34f);
                p.Size = Rand(6f, 11f);
                break;
            }

            case ParticleKind.Flake:
                // Lives long enough to fall the height of the pet and settle on the ground line;
                // the sway in Update is what keeps the long descent from reading as a straight drop.
                p.Vel = new Vector2(Rand(-6f, 6f), Rand(58f, 86f));
                p.Life = Rand(2.2f, 3.0f);
                p.Size = Rand(4f, 6.5f);
                p.Spin = Rand(-2f, 2f);
                break;

            case ParticleKind.Glint:
                p.Pos = origin;
                p.Vel = Vector2.Zero;
                p.Life = Rand(0.3f, 0.42f);
                p.Size = MathF.Max(16f, spread);
                break;
        }

        if (sizeScale != 1f)
        {
            p.Size *= sizeScale;

            // A heavier stone off the same impulse travels slower and lives longer, so the big
            // rocks lumber where the chips spray. Everything else just scales.
            if (kind is ParticleKind.Pebble)
            {
                p.Vel /= MathF.Sqrt(sizeScale);
                p.Life *= 1.15f;
            }
        }

        _particles.Add(p);
    }

    public void Update(float dt)
    {
        for (var i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            p.Age += dt;
            if (p.Age >= p.Life)
            {
                _particles.RemoveAt(i);
                continue;
            }

            if (p.Drift)
            {
                // A curtain falls at its own pace: no gravity, just a slow sideways wander.
                p.Pos.X += MathF.Sin((p.Age * 1.9f) + p.Spin) * 14f * dt;
            }
            else if (p.Kind is ParticleKind.Burst or ParticleKind.Shard)
            {
                p.Vel.Y += 320f * dt;
            }
            else if (p.Kind is ParticleKind.Droplet)
            {
                p.Vel.Y += 430f * dt;
            }
            else if (p.Kind is ParticleKind.Pebble)
            {
                p.Vel.Y += 460f * dt;
            }
            else if (p.Kind is ParticleKind.Spark)
            {
                p.Vel *= MathF.Max(0f, 1f - (5.5f * dt));
            }
            else if (p.Kind is ParticleKind.Ember)
            {
                // Buoyancy, not gravity: the climb eases off near the top and the sideways fan is
                // killed quickly, so the plume tapers to a tip instead of a dome.
                p.Vel.Y *= MathF.Max(0f, 1f - (0.5f * dt));
                p.Vel.X *= MathF.Max(0f, 1f - (2.6f * dt));
            }

            if (!p.Drift && p.Kind is ParticleKind.Heart or ParticleKind.Mote)
            {
                p.Pos.X += MathF.Sin((p.Age * 3.1f) + p.Spin) * 9f * dt;
            }
            else if (p.Kind is ParticleKind.Flake && p.Vel.Y > 0f)
            {
                p.Pos.X += MathF.Sin((p.Age * 2.2f) + p.Spin) * 22f * dt;
            }
            else if (p.Kind is ParticleKind.Ember)
            {
                p.Pos.X += MathF.Sin((p.Age * 14f) + (p.Spin * 3f)) * 30f * dt;
            }

            p.Pos += p.Vel * dt;

            // Ground contact: droplets die into a splash, pebbles bounce and settle.
            if (p.Kind is ParticleKind.Droplet && p.Pos.Y >= GroundY && p.Vel.Y > 0f)
            {
                _impacts.Add((new Vector2(p.Pos.X, GroundY), p.Color, p.Size));
                _particles.RemoveAt(i);
                continue;
            }

            // A flake that reaches the floor settles there and fades out where it landed, rather
            // than sinking through or popping away mid-air.
            if (p.Kind is ParticleKind.Flake && p.Pos.Y >= GroundY)
            {
                p.Pos.Y = GroundY;
                p.Vel = Vector2.Zero;
            }

            if (p.Kind is ParticleKind.Pebble && p.Pos.Y >= GroundY && p.Vel.Y > 0f)
            {
                if (p.Vel.Y > 140f)
                {
                    _impacts.Add((new Vector2(p.Pos.X, GroundY), p.Color with { W = p.Color.W * 0.7f }, p.Size));
                }

                p.Pos.Y = GroundY;
                p.Vel.Y *= -0.42f;
                p.Vel.X *= 0.72f;
            }

            _particles[i] = p;
        }

        // Impact blooms are spawned after the walk so the list never mutates mid-iteration. The
        // bloom is sized by whatever landed, so a heavy stone throws a wider ring and more dust
        // than a chip without the choreography having to say so.
        foreach (var (pos, col, size) in _impacts)
        {
            var weight = Math.Clamp(size / 4f, 0.6f, 2.6f);
            Spawn(ParticleKind.Ring, pos, col with { W = col.W * 0.5f }, 16f * weight, false);
            Spawn(ParticleKind.Mote, pos + new Vector2(0f, -4f), col with { W = col.W * 0.55f }, 6f * weight, false, null, weight);
            Spawn(ParticleKind.Mote, pos + new Vector2(0f, -2f), col with { W = col.W * 0.4f }, 8f * weight, false, null, weight);
            if (weight > 1.4f)
            {
                Spawn(ParticleKind.Mote, pos + new Vector2(0f, -6f), col with { W = col.W * 0.35f }, 12f * weight, false, null, weight);
            }
        }

        _impacts.Clear();
    }

    /// <summary>Draws the pool for one surface. <paramref name="bottomCentre"/> and
    /// <paramref name="displaySize"/> are the same values handed to <see cref="CoreDraw"/>, so
    /// particles land exactly on the pet wherever it is drawn. Call once with behind=true
    /// before the body, once after with behind=false.</summary>
    public void Draw(ImDrawListPtr dl, Vector2 bottomCentre, float displaySize, bool behind)
    {
        if (_particles.Count == 0)
        {
            return;
        }

        var ds = displaySize / 256f;
        foreach (var p in _particles)
        {
            if (p.Behind != behind)
            {
                continue;
            }

            var t = p.Age / p.Life;

            // Ballistic kinds hold their body through flight and vanish fast at the end (a droplet
            // fading mid-air never gets to splash); flakes hold through the fall and melt away over
            // the long tail; everything else keeps the gentle ramp.
            var fade = p.Drift
                ? (t < 0.12f ? t / 0.12f : t > 0.5f ? (1f - t) / 0.5f : 1f)
                : p.Kind switch
                {
                    ParticleKind.Droplet or ParticleKind.Pebble or ParticleKind.Spark =>
                        t < 0.1f ? t / 0.1f : t > 0.8f ? (1f - t) / 0.2f : 1f,
                    ParticleKind.Flake =>
                        t < 0.08f ? t / 0.08f : t > 0.55f ? (1f - t) / 0.45f : 1f,
                    _ => t < 0.15f ? t / 0.15f : 1f - ((t - 0.15f) / 0.85f),
                };

            // The shimmer itself: a drifting particle catches the light on and off as it turns, so
            // a falling curtain glitters instead of sliding down as a solid sheet.
            if (p.Drift)
            {
                fade *= 0.55f + (0.45f * MathF.Sin((p.Age * 9.5f) + (p.Spin * 3f)));
            }

            var baseCol = Vector4.Lerp(p.Color, p.ColorEnd, t);
            var col = baseCol with { W = baseCol.W * Math.Clamp(fade, 0f, 1f) };
            var at = bottomCentre + ((p.Pos - new Vector2(128f, 256f)) * ds);
            DrawOne(dl, p, at, p.Size * ds, col, t);
        }
    }

    private static void DrawOne(ImDrawListPtr dl, in Particle p, Vector2 at, float size, Vector4 col, float t)
    {
        var c = ImGui.ColorConvertFloat4ToU32(col);
        switch (p.Kind)
        {
            case ParticleKind.Sparkle:
            {
                var a = p.Spin * t * 4f;
                var la = size * (1f - (0.3f * t));
                var sa = la * 0.28f;
                Star4(dl, at, la, sa, a, c);
                break;
            }

            case ParticleKind.Mote:
                dl.AddCircleFilled(at, size, c, 10);
                dl.AddCircleFilled(
                    at,
                    size * 0.55f,
                    ImGui.ColorConvertFloat4ToU32(col with { W = col.W * 0.9f, X = 1f, Y = 1f, Z = 1f }),
                    8);
                break;

            case ParticleKind.Glow:
                dl.AddCircleFilled(at, size, ImGui.ColorConvertFloat4ToU32(col with { W = col.W * 0.25f }), 16);
                dl.AddCircleFilled(at, size * 0.6f, ImGui.ColorConvertFloat4ToU32(col with { W = col.W * 0.45f }), 12);
                break;

            case ParticleKind.Burst:
                dl.AddCircleFilled(at, size * (1f - (0.5f * t)), c, 10);
                break;

            case ParticleKind.Ring:
            {
                // Squashed vertically so it reads as a ground ripple, not a hoop.
                var r = size * (0.35f + (0.65f * t));
                var thickness = MathF.Max(1.2f, size * 0.10f * (1f - (0.6f * t)));
                const int Segments = 40;
                for (var s = 0; s <= Segments; s++)
                {
                    var angle = MathF.Tau * s / Segments;
                    dl.PathLineTo(new Vector2(at.X + (MathF.Cos(angle) * r), at.Y + (MathF.Sin(angle) * r * 0.45f)));
                }

                dl.PathStroke(c, ImDrawFlags.Closed, thickness);
                break;
            }

            case ParticleKind.Shard:
            {
                var angle = p.Spin * (0.4f + t);
                var (sin, cos) = MathF.SinCos(angle);
                var lon = size * (1f - (0.25f * t));
                var lat = lon * 0.42f;
                Vector2 R(float x, float y) => new(at.X + (x * cos) - (y * sin), at.Y + (x * sin) + (y * cos));
                dl.AddQuadFilled(R(0, -lon), R(lat, 0), R(0, lon), R(-lat, 0), c);
                var core = ImGui.ColorConvertFloat4ToU32(col with { X = 1f, Y = 1f, Z = 1f, W = col.W * 0.8f });
                dl.AddQuadFilled(R(0, -lon * 0.45f), R(lat * 0.45f, 0), R(0, lon * 0.45f), R(-lat * 0.45f, 0), core);
                break;
            }

            case ParticleKind.Heart:
            {
                // Two circles and a triangle, reads as a heart even at 8 px.
                var r = size * 0.42f;
                var lobeY = at.Y - (r * 0.55f);
                dl.AddCircleFilled(new Vector2(at.X - (r * 0.62f), lobeY), r, c, 12);
                dl.AddCircleFilled(new Vector2(at.X + (r * 0.62f), lobeY), r, c, 12);
                dl.AddTriangleFilled(
                    new Vector2(at.X - (r * 1.55f), lobeY + (r * 0.30f)),
                    new Vector2(at.X + (r * 1.55f), lobeY + (r * 0.30f)),
                    new Vector2(at.X, at.Y + (r * 1.35f)),
                    c);
                break;
            }

            case ParticleKind.Bolt:
            {
                // A jagged 4-segment strike drawn twice (a wide dim glow under a bright core) with
                // a short fork off the midpoint, strobing twice over its short life. The zigzag is
                // seeded from Spin so each bolt keeps its own shape frame to frame.
                if (((int)(t * 8f) % 2) == 1)
                {
                    break;
                }

                var seed = p.Spin;
                var dir = new Vector2(MathF.Sin(seed * 3.7f), 1f);
                dir /= dir.Length();
                var normal = new Vector2(-dir.Y, dir.X);
                var step = size / 4f;
                Span<Vector2> pts = stackalloc Vector2[5];
                pts[0] = at - (dir * size * 0.5f);
                for (var i = 1; i <= 4; i++)
                {
                    var kink = MathF.Sin((seed * 11f) + (i * 2.4f)) * size * 0.22f;
                    pts[i] = at - (dir * size * 0.5f) + (dir * step * i) + (normal * kink * (i == 4 ? 0f : 1f));
                }

                var glow = ImGui.ColorConvertFloat4ToU32(col with { W = col.W * 0.3f });
                for (var i = 0; i < 4; i++)
                {
                    dl.AddLine(pts[i], pts[i + 1], glow, 5f);
                }

                for (var i = 0; i < 4; i++)
                {
                    dl.AddLine(pts[i], pts[i + 1], c, 2f);
                }

                var forkDir = (dir * 0.55f) + (normal * (MathF.Sin(seed * 7f) > 0f ? 0.85f : -0.85f));
                dl.AddLine(pts[2], pts[2] + (forkDir * size * 0.3f), c, 1.5f);
                var tip = ImGui.ColorConvertFloat4ToU32(col with { X = 1f, Y = 1f, Z = 1f, W = col.W * 0.9f });
                dl.AddCircleFilled(pts[4], 2.2f, tip, 6);
                break;
            }

            case ParticleKind.Ember:
            {
                // A pointed flame fleck: round base, licking tip that leans with the flicker, a hot
                // pale core while young (the ColorEnd ramp does the cooling).
                var r = size * (1f - (0.35f * t));
                var flick = MathF.Sin((p.Age * 16f) + p.Spin) * r * 0.3f;
                dl.AddTriangleFilled(
                    new Vector2(at.X - r, at.Y),
                    new Vector2(at.X + r, at.Y),
                    new Vector2(at.X + flick, at.Y - (r * 2.2f)),
                    c);
                dl.AddCircleFilled(new Vector2(at.X, at.Y), r, c, 10);
                if (t < 0.45f)
                {
                    var core = ImGui.ColorConvertFloat4ToU32(
                        col with { X = 1f, Y = 0.95f, Z = 0.75f, W = col.W * (1f - (t / 0.45f)) * 0.9f });
                    dl.AddCircleFilled(at, r * 0.5f, core, 8);
                }

                break;
            }

            case ParticleKind.Droplet:
            {
                // A teardrop stretched along its velocity (round head, tapering tail) with one
                // specular dot, so it reads as thrown water at any size.
                var dir = p.Vel.LengthSquared() > 1f ? Vector2.Normalize(p.Vel) : new Vector2(0f, 1f);
                dl.AddCircleFilled(at, size, c, 10);
                dl.AddCircleFilled(at - (dir * size * 0.7f), size * 0.7f, c, 8);
                dl.AddCircleFilled(at - (dir * size * 1.25f), size * 0.45f, c, 8);
                var glint = ImGui.ColorConvertFloat4ToU32(col with { X = 1f, Y = 1f, Z = 1f, W = col.W * 0.55f });
                dl.AddCircleFilled(at + new Vector2(-size * 0.3f, -size * 0.3f), size * 0.3f, glint, 6);
                break;
            }

            case ParticleKind.Gust:
            {
                // A swoosh arc sweeping around a squashed ellipse centred on the body: segments
                // taper in thickness and alpha towards the tail, so the streak has a head and a
                // wake like brushed wind lines.
                var rx = size;
                var ry = size * (p.Vel.Y > 0f ? p.Vel.Y : 0.42f);
                var phase = p.Vel.X + (p.Spin * p.Age * 2.6f);
                var span = 1.7f * (1f - (0.35f * t));
                const int Segs = 9;
                for (var i = 0; i < Segs; i++)
                {
                    var k = (i + 1f) / Segs;
                    var a0 = phase + (span * (i / (float)Segs));
                    var a1 = phase + (span * ((i + 1) / (float)Segs));
                    var from = new Vector2(at.X + (MathF.Cos(a0) * rx), at.Y + (MathF.Sin(a0) * ry));
                    var to = new Vector2(at.X + (MathF.Cos(a1) * rx), at.Y + (MathF.Sin(a1) * ry));
                    var segCol = ImGui.ColorConvertFloat4ToU32(col with { W = col.W * (0.2f + (0.8f * k)) });
                    dl.AddLine(from, to, segCol, 1f + (2.2f * k * (1f - (0.5f * t))));
                }

                break;
            }

            case ParticleKind.Pebble:
            {
                // A tumbling irregular chip with a darker outline for a little depth.
                var ang = p.Spin * p.Age;
                var (sin, cos) = MathF.SinCos(ang);
                Vector2 R(float x, float y) => new(at.X + (x * cos) - (y * sin), at.Y + (x * sin) + (y * cos));
                var r1 = size;
                var r2 = size * 0.8f;
                var r3 = size * 0.95f;
                var r4 = size * 0.68f;
                dl.AddQuadFilled(R(0, -r1), R(r2, 0), R(0, r3), R(-r4, 0), c);
                var shade = ImGui.ColorConvertFloat4ToU32(
                    col with { X = col.X * 0.65f, Y = col.Y * 0.65f, Z = col.Z * 0.65f });
                dl.AddQuad(R(0, -r1), R(r2, 0), R(0, r3), R(-r4, 0), shade, 1f);
                break;
            }

            case ParticleKind.Spark:
            {
                // A streak along the velocity with a white-hot core, shortening as it dies.
                var dir = p.Vel.LengthSquared() > 1f ? Vector2.Normalize(p.Vel) : new Vector2(0f, -1f);
                var half = dir * size * (1f - (0.4f * t));
                dl.AddLine(at - half, at + half, c, 2f);
                var core = ImGui.ColorConvertFloat4ToU32(col with { X = 1f, Y = 1f, Z = 1f, W = col.W * 0.85f });
                dl.AddLine(at - (half * 0.5f), at + (half * 0.5f), core, 1f);
                break;
            }

            case ParticleKind.Flake:
            {
                // Three crossed lines make six arms; a slow spin keeps them alive.
                var r = size;
                var a0 = p.Spin * 0.5f * p.Age;
                for (var arm = 0; arm < 3; arm++)
                {
                    var ang = a0 + (arm * (MathF.PI / 3f));
                    var (sin, cos) = MathF.SinCos(ang);
                    var d = new Vector2(cos, sin) * r;
                    dl.AddLine(at - d, at + d, c, 1.3f);
                }

                dl.AddCircleFilled(at, r * 0.25f, c, 6);
                break;
            }

            case ParticleKind.Glint:
            {
                // One long-armed star that grows through its short life, with a white centre that
                // shrinks away: the crisp ping of ice and lightning.
                var grow = 0.35f + (0.85f * t);
                Star4(dl, at, size * grow, size * grow * 0.16f, p.Spin * 0.3f, c);
                var core = ImGui.ColorConvertFloat4ToU32(col with { X = 1f, Y = 1f, Z = 1f, W = col.W });
                dl.AddCircleFilled(at, size * 0.12f * (1f - t), core, 8);
                break;
            }
        }
    }

    private static void Star4(ImDrawListPtr dl, Vector2 at, float longArm, float shortArm, float angle, uint c)
    {
        var (sin, cos) = MathF.SinCos(angle);
        Vector2 Rot(float x, float y) => new(at.X + (x * cos) - (y * sin), at.Y + (x * sin) + (y * cos));
        dl.AddQuadFilled(Rot(0, -longArm), Rot(shortArm, 0), Rot(0, longArm), Rot(-shortArm, 0), c);
        dl.AddQuadFilled(Rot(-longArm, 0), Rot(0, shortArm), Rot(longArm, 0), Rot(0, -shortArm), c);
    }
}
