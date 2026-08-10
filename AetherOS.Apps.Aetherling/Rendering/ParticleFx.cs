using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace AetherOS.Apps.Aetherling.Rendering;

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
}

/// <summary>A tiny procedural particle pool in core-local space: positions are authored in the 256 design
/// cell (0,0 = top-left, base at y=256, x centred at 128), so one live pool follows the crystal at any
/// display size and across every surface. Hard-capped at <see cref="MaxParticles"/>.</summary>
public sealed class ParticleFx
{
    private const int MaxParticles = 96;

    private struct Particle
    {
        public ParticleKind Kind;
        public Vector2 Pos;
        public Vector2 Vel;
        public float Age;
        public float Life;
        public float Size;
        public Vector4 Color;
        public float Spin;
        public bool Behind;
    }

    private readonly List<Particle> _particles = new(MaxParticles);
    private readonly Random _rng = new();

    /// <summary>Anything alive? Surfaces can skip the draw pass entirely when quiet.</summary>
    public bool Any => _particles.Count > 0;

    public void Clear() => _particles.Clear();

    /// <summary>Emits <paramref name="count"/> particles around an origin in the 256 cell.</summary>
    public void Burst(
        ParticleKind kind, Vector2 origin, int count, Vector4 color, float spread = 60f, bool behind = false)
    {
        for (var i = 0; i < count; i++)
        {
            Spawn(kind, origin, color, spread, behind);
        }
    }

    /// <summary>One ambient particle, for the idle emitters.</summary>
    public void Emit(ParticleKind kind, Vector2 origin, Vector4 color, float spread = 40f, bool behind = false)
        => Spawn(kind, origin, color, spread, behind);

    private void Spawn(ParticleKind kind, Vector2 origin, Vector4 color, float spread, bool behind)
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

            if (p.Kind is ParticleKind.Burst or ParticleKind.Shard)
            {
                p.Vel.Y += 320f * dt;
            }

            if (p.Kind is ParticleKind.Mote)
            {
                p.Pos.X += MathF.Sin((p.Age * 3.1f) + p.Spin) * 9f * dt;
            }

            p.Pos += p.Vel * dt;
            _particles[i] = p;
        }
    }

    /// <summary>Draws the pool for one surface. <paramref name="bottomCentre"/> and
    /// <paramref name="displaySize"/> are the same values handed to <see cref="CoreDraw"/>, so
    /// particles land exactly on the crystal wherever it is drawn. Call once with behind=true
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
            var fade = t < 0.15f ? t / 0.15f : 1f - ((t - 0.15f) / 0.85f);
            var col = p.Color with { W = p.Color.W * Math.Clamp(fade, 0f, 1f) };
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
