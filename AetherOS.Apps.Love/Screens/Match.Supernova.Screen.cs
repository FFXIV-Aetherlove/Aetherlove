using System;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using AetherLove.Widgets;
using Dalamud.Bindings.ImGui;
using static AetherLove.Screens.MatchFx;

namespace AetherLove.Screens;

/// <summary>Match effect - Supernova Heartbeat: a pulsing heart fires expanding shockwaves over a rotating sunburst while two avatars orbit it.</summary>
public sealed class MatchSupernovaScreen : IMatchEffect
{
    private const int RayCount = 28;
    private const int ShockwaveCount = 3;
    private const float BeatPeriod = 0.9f;

    private readonly LoveRouter _router;

    private float _intro;
    private float _settle;
    private readonly ConfettiBurst _confetti = new();

    public MatchSupernovaScreen(LoveRouter router)
    {
        _router = router;
    }

    public void OnShow()
    {
        _intro = 0f;
        _settle = 0f;
        _confetti.Reset();
    }

    public void Draw()
    {
        var reduce = AccessibilityService.ReduceMotion;
        var dt = (float)ImGui.GetIO().DeltaTime;
        var time = (float)ImGui.GetTime();

        if (reduce)
        {
            _intro = 1f;
            _settle = 1f;
        }
        else
        {
            AnimationHelper.ClampedProgress(ref _intro, dt, 1.6f, forward: true);
            if (_intro > 0.55f)
            {
                AnimationHelper.ClampedProgress(ref _settle, dt, 1.8f, forward: true);
            }
        }

        var t = ThemeService.Current;
        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var dl = ImGui.GetWindowDrawList();
        var center = pos + size * 0.5f;
        var cx = center.X;

        dl.AddRectFilled(pos, pos + size, 0xFF0A0612);

        var maxRayLen = size.X * 0.78f;
        var spin = reduce ? 0f : time * 0.32f;
        var introScale = EaseOutBack(_intro);

        DrawSunburst(dl, center, maxRayLen * introScale, spin, t);

        const int glowSteps = 5;
        for (int i = glowSteps; i >= 1; i--)
        {
            var rr = size.X * 0.16f * i * introScale;
            var a = 0.07f * (1f - (float)i / (glowSteps + 1));
            dl.AddCircleFilled(center, rr, U32(Rgba(t.SecondaryEnd, a)), 48);
        }

        var beatPhase = reduce ? 0f : (time % BeatPeriod) / BeatPeriod;
        var beat = reduce ? 0.35f : HeartbeatWave(beatPhase);
        var heartScale = (0.92f + beat * 0.32f) * introScale;
        var heartR = Px(46f) * heartScale;

        if (!reduce)
        {
            DrawShockwaves(dl, center, time, size.X, t);
        }

        DrawHeart(dl, center, heartR, beat, t, reduce);

        var orbitR = Px(98f) * introScale;
        var avatarR = Px(36f);
        float orbitAng = reduce ? -MathF.PI * 0.5f : time * 0.6f;
        var leftPos = center + new Vector2(MathF.Cos(orbitAng + MathF.PI), MathF.Sin(orbitAng + MathF.PI)) * orbitR;
        var rightPos = center + new Vector2(MathF.Cos(orbitAng), MathF.Sin(orbitAng)) * orbitR;

        DrawAvatar(dl, leftPos, avatarR, time, t, reduce, ccw: false);
        DrawAvatar(dl, rightPos, avatarR, time, t, reduce, ccw: true);

        if (_settle > 0.01f)
        {
            var titleScale = EaseOutBack(_settle);
            using (UiFonts.H1?.Push())
            {
                var label = Loc.T("deck.match_its_a_match");
                var w = ImGui.CalcTextSize(label).X;
                var x0 = cx - w * 0.5f;
                var heartGlyph = ImGui.CalcTextSize("♥ ").X;
                var titleY = pos.Y + size.Y * 0.155f + (1f - titleScale) * Px(14f);

                var glow = reduce ? 0.6f : 0.6f + 0.4f * MathF.Sin(time * 3.0f);
                var glowCol = U32(Rgba(t.SecondaryEnd, _settle * 0.5f * glow));
                dl.AddText(new Vector2(x0 - heartGlyph, titleY), glowCol, "♥");
                dl.AddText(new Vector2(x0 + w + Px(4f), titleY), glowCol, "♥");

                var vtx = dl.VtxBuffer.Size;
                dl.AddText(new Vector2(x0, titleY), U32(new Vector4(1f, 1f, 1f, _settle)), label);
                if (!reduce)
                {
                    GradientText(dl, vtx, x0, x0 + w, t.SecondaryStart, t.SecondaryEnd, time * 2.0f);
                }
            }

            using (UiFonts.H3?.Push())
            {
                CenterText(dl, cx, pos.Y + size.Y * 0.245f, Loc.T("deck.match_fx_supernova"),
                    U32(Rgba(t.AccentLight, _settle * 0.9f)));
            }

            var nameCol = U32(new Vector4(0.92f, 0.92f, 0.92f, _settle));
            CenterText(dl, cx, pos.Y + size.Y * 0.80f, MatchContent.OwnName + "  ♥  " + MatchContent.PeerName, nameCol);

            if (!reduce)
            {
                _confetti.Draw(pos, pos + size);
            }
        }

        DrawActionButtons(_router, pos, size);
    }

    private static void DrawSunburst(ImDrawListPtr dl, Vector2 center, float length, float spin, ThemeDefinition t)
    {
        if (length <= 1f)
        {
            return;
        }

        var halfWidth = MathF.Tau / RayCount * 0.32f;
        for (int i = 0; i < RayCount; i++)
        {
            var ang = i / (float)RayCount * MathF.Tau + spin;
            var blend = 0.5f + 0.5f * MathF.Sin(i / (float)RayCount * MathF.Tau);
            var col = Vector4.Lerp(t.SecondaryStart, t.SecondaryEnd, blend);
            var tip = center + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * length;
            var baseA = center + new Vector2(MathF.Cos(ang - halfWidth), MathF.Sin(ang - halfWidth)) * Px(20f);
            var baseB = center + new Vector2(MathF.Cos(ang + halfWidth), MathF.Sin(ang + halfWidth)) * Px(20f);
            dl.AddTriangleFilled(baseA, baseB, tip, U32(Rgba(col, 0.13f)));
        }
    }

    private static void DrawShockwaves(ImDrawListPtr dl, Vector2 center, float time, float spanX, ThemeDefinition t)
    {
        var maxR = spanX * 0.58f;
        for (int i = 0; i < ShockwaveCount; i++)
        {
            var phase = ((time / BeatPeriod) + i / (float)ShockwaveCount) % 1f;
            var r = phase * maxR;
            var alpha = (1f - phase) * 0.45f;
            if (alpha <= 0.01f || r <= 1f)
            {
                continue;
            }
            var col = Vector4.Lerp(t.SecondaryStart, t.SecondaryEnd, phase);
            dl.AddCircle(center, r, U32(Rgba(col, alpha)), 72, Px(2.5f));
        }
    }

    private static void DrawHeart(ImDrawListPtr dl, Vector2 center, float r, float beat, ThemeDefinition t, bool reduce)
    {
        var fill = U32(Vector4.Lerp(t.SecondaryStart, t.SecondaryEnd, 0.5f + 0.35f * beat));
        var lobeR = r * 0.5f;
        var lobeOffX = r * 0.42f;
        var lobeOffY = -r * 0.22f;
        var leftLobe = center + new Vector2(-lobeOffX, lobeOffY);
        var rightLobe = center + new Vector2(lobeOffX, lobeOffY);

        dl.AddCircleFilled(leftLobe, lobeR, fill, 32);
        dl.AddCircleFilled(rightLobe, lobeR, fill, 32);

        var apex = center + new Vector2(0f, r * 0.78f);
        var leftWing = center + new Vector2(-r * 0.78f, lobeOffY + r * 0.04f);
        var rightWing = center + new Vector2(r * 0.78f, lobeOffY + r * 0.04f);
        dl.AddTriangleFilled(leftWing, rightWing, apex, fill);

        var sheenCol = U32(Rgba(new Vector4(1f, 1f, 1f, 1f), reduce ? 0.18f : 0.18f + 0.22f * MathF.Max(0f, beat)));
        dl.AddCircleFilled(leftLobe + new Vector2(-lobeR * 0.25f, -lobeR * 0.3f), lobeR * 0.35f, sheenCol, 16);
    }

    private static void DrawAvatar(ImDrawListPtr dl, Vector2 p, float radius, float time, ThemeDefinition t, bool reduce, bool ccw)
    {
        Avatar(dl, p, radius, ccw ? MatchContent.PeerAvatar : MatchContent.OwnAvatar, 0, 0f);
        if (reduce)
        {
            dl.AddCircle(p, radius + Px(3f), t.AccentU32, 48, Px(2f));
        }
        else
        {
            var phase = time * 1.8f * (ccw ? -1f : 1f);
            GradientRing(dl, p, radius + Px(3f), Px(2.5f), t.SecondaryStart, t.SecondaryEnd, phase, 72);
        }
    }

    private static float HeartbeatWave(float phase)
    {
        if (phase < 0.10f)
        {
            return phase / 0.10f;
        }
        if (phase < 0.22f)
        {
            return 1f - (phase - 0.10f) / 0.12f * 0.55f;
        }
        if (phase < 0.32f)
        {
            return 0.45f + (phase - 0.22f) / 0.10f * 0.4f;
        }
        if (phase < 0.55f)
        {
            return 0.85f * (1f - (phase - 0.32f) / 0.23f);
        }
        return 0f;
    }

    private static float EaseOutBack(float x)
    {
        x = Math.Clamp(x, 0f, 1f);
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        var p = x - 1f;
        return 1f + c3 * p * p * p + c1 * p * p;
    }
}
