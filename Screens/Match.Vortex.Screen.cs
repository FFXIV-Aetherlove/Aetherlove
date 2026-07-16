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

/// <summary>Match effect - Vortex Spiral: the avatars ride opposite spiral arms in to meet at the hub.</summary>
public sealed class MatchVortexScreen : IMatchEffect
{
    private readonly ScreenRouter _router;

    private const int ArmCount = 5;
    private const int MotesPerArm = 26;
    private const float SpiralTightness = 2.6f;

    private float _converge;
    private float _reveal;
    private readonly ConfettiBurst _confetti = new();
    private readonly Random _rng = new();
    private readonly (float arm, float t, float size, float jitter)[] _motes =
        new (float, float, float, float)[ArmCount * MotesPerArm];
    private bool _ready;

    public MatchVortexScreen(ScreenRouter router)
    {
        _router = router;
    }

    public void OnShow()
    {
        _converge = 0f;
        _reveal = 0f;
        _confetti.Reset();
        for (int a = 0; a < ArmCount; a++)
        {
            for (int m = 0; m < MotesPerArm; m++)
            {
                var idx = a * MotesPerArm + m;
                var frac = (m + 0.5f) / MotesPerArm;
                _motes[idx] = (a, frac, Px(1.1f) + _rng.NextSingle() * Px(2.2f),
                    (_rng.NextSingle() - 0.5f) * 0.18f);
            }
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
            _converge = 1f;
            _reveal = 1f;
        }
        else
        {
            AnimationHelper.ClampedProgress(ref _converge, dt, 0.62f, forward: true);
            if (_converge > 0.7f)
            {
                AnimationHelper.ClampedProgress(ref _reveal, dt, 1.6f, forward: true);
            }
        }

        var t = ThemeService.Current;
        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var dl = ImGui.GetWindowDrawList();
        var center = pos + size * 0.5f;
        var cx = center.X;

        dl.AddRectFilled(pos, pos + size, 0xFF080510);

        var maxR = MathF.Min(size.X, size.Y) * 0.46f;
        var fieldSpin = reduce ? 0.6f : time * 0.45f;
        var ease = EaseInOutCubic(_converge);

        DrawNebulaGlow(dl, center, maxR, t, reduce, time);
        DrawSpiralArms(dl, center, maxR, fieldSpin, ease, t, reduce, time);
        DrawCore(dl, center, ease, t, reduce, time);

        var radius = Px(44f);
        var ownArm = ArmAngle(0, fieldSpin);
        var peerArm = ArmAngle(ArmCount / 2 + 1, fieldSpin) + MathF.PI;
        var avatarR = AnimationHelper.Lerp(maxR * 0.92f, radius + Px(6f), ease);
        var swirl = (1f - ease) * SpiralTightness;

        var ownPos = center + Polar(ownArm + swirl, avatarR);
        var peerPos = center + Polar(peerArm + swirl, avatarR);

        DrawTrail(dl, center, ownArm, swirl, avatarR, maxR, ease, t, reduce);
        DrawTrail(dl, center, peerArm, swirl, avatarR, maxR, ease, t, reduce);

        Avatar(dl, ownPos, radius, MatchContent.OwnAvatar, 0, 0f);
        Avatar(dl, peerPos, radius, MatchContent.PeerAvatar, 0, 0f);
        if (reduce)
        {
            dl.AddCircle(ownPos, radius + Px(3f), t.AccentU32, 64, Px(2f));
            dl.AddCircle(peerPos, radius + Px(3f), t.AccentU32, 64, Px(2f));
        }
        else
        {
            var ringPhase = time * 1.9f;
            GradientRing(dl, ownPos, radius + Px(3f), Px(2.5f), t.SecondaryStart, t.SecondaryEnd, ringPhase);
            GradientRing(dl, peerPos, radius + Px(3f), Px(2.5f), t.SecondaryStart, t.SecondaryEnd, -ringPhase);
        }

        if (_reveal > 0.01f)
        {
            using (UiFonts.H1?.Push())
            {
                var label = Loc.T("deck.match_its_a_match");
                var w = ImGui.CalcTextSize(label).X;
                var x0 = cx - w * 0.5f;
                var vtx = dl.VtxBuffer.Size;
                dl.AddText(new Vector2(x0, pos.Y + size.Y * 0.16f), U32(new Vector4(1f, 1f, 1f, _reveal)), label);
                if (!reduce)
                {
                    GradientText(dl, vtx, x0, x0 + w, t.SecondaryStart, t.SecondaryEnd, time * 1.7f);
                }
            }

            using (UiFonts.H3?.Push())
            {
                CenterText(dl, cx, pos.Y + size.Y * 0.245f, Loc.T("deck.match_fx_vortex"),
                    U32(Rgba(t.AccentLight, _reveal * 0.9f)));
            }

            var nameCol = U32(new Vector4(0.92f, 0.92f, 0.92f, _reveal));
            CenterText(dl, cx - Px(70f), center.Y + maxR * 0.7f, MatchContent.OwnName, nameCol);
            CenterText(dl, cx + Px(70f), center.Y + maxR * 0.7f, MatchContent.PeerName, nameCol);

            if (!reduce)
            {
                _confetti.Draw(pos, pos + size);
            }
        }

        DrawActionButtons(_router, pos, size, reduce ? 1f : _reveal);
    }

    private void DrawNebulaGlow(ImDrawListPtr dl, Vector2 center, float maxR, ThemeDefinition t, bool reduce, float time)
    {
        const int steps = 7;
        var breathe = reduce ? 1f : 0.85f + 0.15f * MathF.Sin(time * 0.9f);
        for (int i = steps; i >= 1; i--)
        {
            var rr = maxR * 1.05f * i / steps;
            var a = 0.06f * (1f - (float)i / (steps + 1)) * breathe;
            var col = Vector4.Lerp(t.SecondaryEnd, t.SecondaryStart, (float)i / steps);
            dl.AddCircleFilled(center, rr, U32(Rgba(col, a)), 56);
        }
    }

    private void DrawSpiralArms(ImDrawListPtr dl, Vector2 center, float maxR, float spin, float ease,
        ThemeDefinition t, bool reduce, float time)
    {
        var pullIn = AnimationHelper.Lerp(1f, 0.42f, ease);
        foreach (var mote in _motes)
        {
            var flow = reduce ? 0f : (time * 0.22f) % 1f;
            var local = mote.t - flow;
            if (local < 0f)
            {
                local += 1f;
            }
            var rNorm = local * pullIn;
            var r = rNorm * maxR;
            var arm = ArmAngle((int)mote.arm, spin) + mote.jitter;
            var ang = arm + (1f - rNorm) * SpiralTightness;
            var p = center + Polar(ang, r);

            var depth = 1f - rNorm;
            var blend = 0.5f + 0.5f * MathF.Sin(ang * 1.5f - spin * 2f);
            var col = Vector4.Lerp(t.SecondaryStart, t.SecondaryEnd, blend);
            var alpha = (0.18f + 0.62f * depth) * (0.55f + 0.45f * ease);
            var sz = mote.size * (0.6f + 0.7f * depth);
            dl.AddCircleFilled(p, sz, U32(Rgba(col, alpha)), 10);
        }
    }

    private void DrawTrail(ImDrawListPtr dl, Vector2 center, float armAngle, float swirl, float avatarR,
        float maxR, float ease, ThemeDefinition t, bool reduce)
    {
        var alphaBase = reduce ? 0.25f : 0.42f * (1f - ease * 0.4f);
        if (alphaBase <= 0.001f)
        {
            return;
        }
        const int segs = 14;
        var prev = center + Polar(armAngle + swirl, avatarR);
        for (int i = 1; i <= segs; i++)
        {
            var f = (float)i / segs;
            var r = AnimationHelper.Lerp(avatarR, maxR * 0.95f, f);
            var extraSwirl = swirl + f * SpiralTightness * 0.9f;
            var p = center + Polar(armAngle + extraSwirl, r);
            var a = alphaBase * (1f - f);
            var blend = 0.5f + 0.5f * MathF.Sin(f * MathF.Tau);
            var col = Vector4.Lerp(t.SecondaryStart, t.SecondaryEnd, blend);
            dl.AddLine(prev, p, U32(Rgba(col, a)), Px(2f) * (1f - f) + Px(0.6f));
            prev = p;
        }
    }

    private void DrawCore(ImDrawListPtr dl, Vector2 center, float ease, ThemeDefinition t, bool reduce, float time)
    {
        var pulse = reduce ? 1f : 0.88f + 0.12f * MathF.Sin(time * 3.4f);
        var coreR = Px(26f) * (0.4f + 0.6f * ease) * pulse;
        const int layers = 5;
        for (int i = layers; i >= 1; i--)
        {
            var rr = coreR * i / layers * 1.8f;
            var f = (float)i / layers;
            var col = Vector4.Lerp(t.SecondaryEnd, t.SecondaryStart, f);
            var a = ease * (0.55f * (1f - f) + 0.12f);
            dl.AddCircleFilled(center, rr, U32(Rgba(col, a)), 40);
        }
        dl.AddCircleFilled(center, coreR * 0.5f, U32(new Vector4(1f, 1f, 1f, ease * 0.9f)), 32);
    }

    private static float ArmAngle(int arm, float spin)
    {
        return arm / (float)ArmCount * MathF.Tau + spin;
    }

    private static Vector2 Polar(float angle, float radius)
    {
        return new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
    }

    private static float EaseInOutCubic(float x)
    {
        x = Math.Clamp(x, 0f, 1f);
        return x < 0.5f ? 4f * x * x * x : 1f - MathF.Pow(-2f * x + 2f, 3f) / 2f;
    }
}
