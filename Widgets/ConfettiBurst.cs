using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using AetherLove.UI;

namespace AetherLove.Widgets;

/// <summary>Falling-confetti effect. Reset on show, then call Draw each frame with the clip rect.</summary>
public sealed class ConfettiBurst
{
    private sealed class Particle
    {
        public float X, Y, StartX, Vy, Amp, Freq, Phase, Rot, RotSpeed, Life, Size;
        public uint BaseColor;
        public bool IsCircle;
    }

    private const int Count = 48;
    private const float Duration = 5f;
    private const float FadeStart = 4f;

    private readonly List<Particle> _particles = [];
    private readonly Random _rng = new();
    private bool _initialized;
    private float _elapsed;

    public void Reset()
    {
        _particles.Clear();
        _initialized = false;
        _elapsed = 0f;
    }

    public void Draw(Vector2 clipMin, Vector2 clipMax)
    {
        var dt = ImGui.GetIO().DeltaTime;
        _elapsed += dt;

        if (!_initialized)
        {
            for (int i = 0; i < Count; i++)
            {
                var p = new Particle();
                Spawn(p, clipMin, clipMax, atTop: false);
                _particles.Add(p);
            }
            _initialized = true;
        }

        if (_elapsed >= Duration)
        {
            return;
        }

        var globalAlpha = _elapsed >= FadeStart
            ? 1f - (_elapsed - FadeStart) / (Duration - FadeStart)
            : 1f;

        var dl = ImGui.GetWindowDrawList();
        dl.PushClipRect(clipMin, clipMax, true);

        foreach (var p in _particles)
        {
            p.Life += dt;
            p.Y += p.Vy * dt;
            p.X = p.StartX + MathF.Sin(p.Life * p.Freq + p.Phase) * p.Amp;
            p.Rot += p.RotSpeed * dt;

            if (p.Y > clipMax.Y + p.Size * 2f)
            {
                Spawn(p, clipMin, clipMax, atTop: true);
                continue;
            }

            var alpha = MathF.Min(1f, MathF.Max(0f, p.Life / 0.25f)) * globalAlpha;
            var colorA = ((uint)(alpha * 210f) << 24) | (p.BaseColor & 0x00FFFFFFu);
            var center = new Vector2(p.X, p.Y);

            if (p.IsCircle)
            {
                dl.AddCircleFilled(center, p.Size, colorA);
            }
            else
            {
                var hw = p.Size;
                var hh = p.Size * 0.45f;
                var cos = MathF.Cos(p.Rot);
                var sin = MathF.Sin(p.Rot);
                var tl = center + new Vector2(-hw * cos + hh * sin, -hw * sin - hh * cos);
                var tr = center + new Vector2(hw * cos + hh * sin, hw * sin - hh * cos);
                var br = center + new Vector2(hw * cos - hh * sin, hw * sin + hh * cos);
                var bl = center + new Vector2(-hw * cos - hh * sin, -hw * sin + hh * cos);
                dl.AddQuadFilled(tl, tr, br, bl, colorA);
            }
        }

        dl.PopClipRect();
    }

    private void Spawn(Particle p, Vector2 clipMin, Vector2 clipMax, bool atTop)
    {
        var areaW = clipMax.X - clipMin.X;
        var areaH = clipMax.Y - clipMin.Y;

        p.StartX = clipMin.X + _rng.NextSingle() * areaW;
        p.X = p.StartX;
        p.Y = atTop ? clipMin.Y - _rng.NextSingle() * Px(60f)
                    : clipMin.Y + _rng.NextSingle() * areaH;
        p.Vy = 55f + _rng.NextSingle() * 85f;
        p.Amp = Px(8f) + _rng.NextSingle() * Px(28f);
        p.Freq = 1.5f + _rng.NextSingle() * 2.5f;
        p.Phase = _rng.NextSingle() * MathF.Tau;
        p.Rot = _rng.NextSingle() * MathF.Tau;
        p.RotSpeed = (_rng.NextSingle() - 0.5f) * 7f;
        p.Life = 0f;
        p.BaseColor = UiColors.ConfettiPalette[_rng.Next(UiColors.ConfettiPalette.Length)];
        p.IsCircle = _rng.NextSingle() < 0.28f;
        p.Size = p.IsCircle ? Px(2.5f) + _rng.NextSingle() * Px(2.0f)
                            : Px(4.5f) + _rng.NextSingle() * Px(4.5f);
    }
}
