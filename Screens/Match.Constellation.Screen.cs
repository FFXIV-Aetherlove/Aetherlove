using System;
using System.Numerics;
using AetherLove.Navigation;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using AetherLove.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using static AetherLove.Screens.MatchFx;

namespace AetherLove.Screens;

/// <summary>Match effect — Constellation Heart: glowing star points connect with thin
/// celestial lines that draw on segment-by-segment to trace a heart around the two avatars, over a
/// twinkling night sky.</summary>
public sealed class MatchConstellationScreen : IMatchEffect
{
    private readonly ScreenRouter _router;

    private float _trace;
    private float _settle;
    private readonly ConfettiBurst _confetti = new();
    private readonly Random _rng = new();
    private readonly (float nx, float ny, float r, float ph)[] _stars = new (float, float, float, float)[90];
    private readonly Vector2[] _heart = new Vector2[48];
    private readonly float[] _twinkle = new float[48];
    private bool _ready;

    public MatchConstellationScreen(ScreenRouter router)
    {
        _router = router;
    }

    public void OnShow()
    {
        _trace = 0f;
        _settle = 0f;
        _confetti.Reset();
        for (int i = 0; i < _stars.Length; i++)
        {
            _stars[i] = (_rng.NextSingle(), _rng.NextSingle(),
                         Px(0.5f) + _rng.NextSingle() * Px(1.7f), _rng.NextSingle() * MathF.Tau);
        }
        for (int i = 0; i < _twinkle.Length; i++)
        {
            _twinkle[i] = _rng.NextSingle() * MathF.Tau;
        }
        _ready = true;
    }

    public void Draw()
    {
        if (!_ready)
        {
            OnShow();
        }

        var reduce = AccessibilityService.ReduceMotion;
        var dt = (float)ImGui.GetIO().DeltaTime;
        var time = (float)ImGui.GetTime();

        if (reduce)
        {
            _trace = 1f;
            _settle = 1f;
        }
        else
        {
            AnimationHelper.ClampedProgress(ref _trace, dt, 0.55f, forward: true);
            if (_trace > 0.55f)
            {
                AnimationHelper.ClampedProgress(ref _settle, dt, 1.4f, forward: true);
            }
        }

        var t = ThemeService.Current;
        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var dl = ImGui.GetWindowDrawList();
        var center = pos + size * 0.5f;
        var cx = center.X;

        // Deep night-sky backdrop with a faint secondary nebula bloom behind the heart.
        dl.AddRectFilledMultiColor(pos, pos + size, 0xFF0B0518, 0xFF0B0518, 0xFF120726, 0xFF120726);
        const int glowSteps = 7;
        for (int i = glowSteps; i >= 1; i--)
        {
            var rr = size.X * 0.16f * i;
            var a = 0.045f * (1f - (float)i / (glowSteps + 1));
            dl.AddCircleFilled(center, rr, U32(Rgba(t.SecondaryEnd, a)), 48);
        }

        // Scattered twinkling stars across the whole sky.
        foreach (var s in _stars)
        {
            var p = pos + new Vector2(s.nx * size.X, s.ny * size.Y);
            var tw = reduce ? 0.65f : 0.4f + 0.45f * MathF.Sin(time * 2.0f + s.ph);
            dl.AddCircleFilled(p, s.r, U32(new Vector4(0.85f, 0.88f, 1f, tw)));
        }

        BuildHeart(center, size);

        // Reveal the heart outline segment by segment as the trace progresses.
        var revealed = reduce ? _heart.Length : (int)MathF.Floor(_trace * _heart.Length);
        var frac = reduce ? 0f : _trace * _heart.Length - revealed;
        for (int i = 0; i < _heart.Length; i++)
        {
            var a = _heart[i];
            var b = _heart[(i + 1) % _heart.Length];
            if (i < revealed)
            {
                DrawGlowSegment(dl, a, b, t, 1f);
            }
            else if (i == revealed && frac > 0.02f)
            {
                DrawGlowSegment(dl, a, Vector2.Lerp(a, b, frac), t, frac);
            }
        }

        // Glowing star node at every connected vertex; the leading node sparkles bright.
        for (int i = 0; i < _heart.Length; i++)
        {
            if (i > revealed)
            {
                continue;
            }
            var lit = reduce ? 1f : 0.55f + 0.45f * MathF.Sin(time * 3.2f + _twinkle[i]);
            var leading = !reduce && i == revealed;
            DrawStarNode(dl, _heart[i], t, lit, leading);
        }

        // Avatars nestled inside the heart.
        var radius = Px(46f);
        var gap = Px(56f);
        var leftPos = new Vector2(cx - gap, center.Y + Px(6f));
        var rightPos = new Vector2(cx + gap, center.Y + Px(6f));

        Avatar(dl, leftPos, radius, MatchContent.OwnAvatar, 0, 0f);
        Avatar(dl, rightPos, radius, MatchContent.PeerAvatar, 0, 0f);
        if (reduce)
        {
            dl.AddCircle(leftPos, radius + Px(3f), t.AccentU32, 64, Px(2f));
            dl.AddCircle(rightPos, radius + Px(3f), t.AccentU32, 64, Px(2f));
        }
        else
        {
            var ringPhase = time * 1.4f;
            GradientRing(dl, leftPos, radius + Px(3f), Px(2.5f), t.SecondaryStart, t.SecondaryEnd, ringPhase);
            GradientRing(dl, rightPos, radius + Px(3f), Px(2.5f), t.SecondaryStart, t.SecondaryEnd, -ringPhase);
        }

        // Title, subtitle, names and confetti bloom in once the heart finishes drawing.
        if (_settle > 0.01f)
        {
            using (UiFonts.H1?.Push())
            {
                var label = Loc.T("deck.match_its_a_match");
                var w = ImGui.CalcTextSize(label).X;
                var x0 = cx - w * 0.5f;
                var vtx = dl.VtxBuffer.Size;
                dl.AddText(new Vector2(x0, pos.Y + size.Y * 0.135f), U32(new Vector4(1f, 1f, 1f, _settle)), label);
                if (!reduce)
                {
                    GradientText(dl, vtx, x0, x0 + w, t.SecondaryStart, t.SecondaryEnd, time * 1.5f);
                }
            }

            using (UiFonts.H3?.Push())
            {
                CenterText(dl, cx, pos.Y + size.Y * 0.215f, Loc.T("deck.match_fx_constellation"),
                    U32(Rgba(t.AccentLight, _settle * 0.9f)));
            }

            var nameCol = U32(new Vector4(0.92f, 0.92f, 0.95f, _settle));
            CenterText(dl, leftPos.X, center.Y + radius + Px(18f), MatchContent.OwnName, nameCol);
            CenterText(dl, rightPos.X, center.Y + radius + Px(18f), MatchContent.PeerName, nameCol);

            if (!reduce)
            {
                _confetti.Draw(pos, pos + size);
            }
        }

        DrawActionButtons(_router, pos, size, reduce ? 1f : _settle);
    }

    /// <summary>Fills <see cref="_heart"/> with vertices of a heart curve sized to the canvas, centred
    /// on <paramref name="center"/> and starting at the bottom tip so the trace reveals symmetrically.</summary>
    private void BuildHeart(Vector2 center, Vector2 size)
    {
        var scale = MathF.Min(size.X, size.Y) * 0.0125f;
        for (int i = 0; i < _heart.Length; i++)
        {
            var ang = MathF.PI + i / (float)_heart.Length * MathF.Tau;
            var sin = MathF.Sin(ang);
            var x = 16f * sin * sin * sin;
            var y = 13f * MathF.Cos(ang) - 5f * MathF.Cos(2f * ang)
                    - 2f * MathF.Cos(3f * ang) - MathF.Cos(4f * ang);
            _heart[i] = center + new Vector2(x * scale, -y * scale);
        }
    }

    private static void DrawGlowSegment(ImDrawListPtr dl, Vector2 a, Vector2 b, ThemeDefinition t, float alpha)
    {
        dl.AddLine(a, b, U32(Rgba(t.SecondaryEnd, 0.18f * alpha)), Px(5f));
        dl.AddLine(a, b, U32(Rgba(t.SecondaryStart, 0.4f * alpha)), Px(2.5f));
        dl.AddLine(a, b, U32(new Vector4(1f, 1f, 1f, 0.85f * alpha)), Px(1.2f));
    }

    private static void DrawStarNode(ImDrawListPtr dl, Vector2 p, ThemeDefinition t, float lit, bool leading)
    {
        dl.AddCircleFilled(p, Px(5f) * lit, U32(Rgba(t.SecondaryStart, 0.22f * lit)), 16);
        dl.AddCircleFilled(p, Px(2.2f), U32(new Vector4(1f, 1f, 1f, 0.5f + 0.5f * lit)), 12);
        if (leading)
        {
            var rr = Px(8f);
            dl.AddLine(p - new Vector2(rr, 0f), p + new Vector2(rr, 0f), U32(Rgba(t.AccentLight, 0.85f)), Px(1.2f));
            dl.AddLine(p - new Vector2(0f, rr), p + new Vector2(0f, rr), U32(Rgba(t.AccentLight, 0.85f)), Px(1.2f));
            dl.AddCircleFilled(p, Px(3f), 0xFFFFFFFF, 12);
        }
    }
}