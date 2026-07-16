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

/// <summary>Match effect - Sky Lanterns: a swarm of glowing paper lanterns drifts
/// upward over a starry night while two larger avatar-lanterns rise together toward the top.</summary>
public sealed class MatchSkyLanternsScreen : IMatchEffect
{
    private readonly ScreenRouter _router;

    private float _rise;
    private float _settle;
    private readonly Random _rng = new();
    private readonly (float nx, float baseY, float scale, float swayAmp, float swayPh, float speed, float hue)[] _lanterns
        = new (float, float, float, float, float, float, float)[16];
    private readonly (float nx, float ny, float r, float ph)[] _stars = new (float, float, float, float)[70];
    private bool _ready;

    public MatchSkyLanternsScreen(ScreenRouter router)
    {
        _router = router;
    }

    public void OnShow()
    {
        _rise = 0f;
        _settle = 0f;
        for (int i = 0; i < _lanterns.Length; i++)
        {
            _lanterns[i] = (
                _rng.NextSingle(),
                _rng.NextSingle(),
                0.45f + _rng.NextSingle() * 0.7f,
                Px(6f) + _rng.NextSingle() * Px(14f),
                _rng.NextSingle() * MathF.Tau,
                0.05f + _rng.NextSingle() * 0.08f,
                _rng.NextSingle());
        }
        for (int i = 0; i < _stars.Length; i++)
        {
            _stars[i] = (_rng.NextSingle(), _rng.NextSingle(),
                         Px(0.5f) + _rng.NextSingle() * Px(1.4f), _rng.NextSingle() * MathF.Tau);
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
            _rise = 1f;
            _settle = 1f;
        }
        else
        {
            AnimationHelper.ClampedProgress(ref _rise, dt, 0.85f, forward: true);
            if (_rise > 0.55f)
            {
                AnimationHelper.ClampedProgress(ref _settle, dt, 1.4f, forward: true);
            }
        }

        var t = ThemeService.Current;
        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var dl = ImGui.GetWindowDrawList();
        var cx = pos.X + size.X * 0.5f;

        DrawNightSky(dl, pos, size, t, time, reduce);

        foreach (var s in _stars)
        {
            var p = pos + new Vector2(s.nx * size.X, s.ny * size.Y);
            var tw = reduce ? 0.65f : 0.4f + 0.45f * MathF.Sin(time * 1.8f + s.ph);
            dl.AddCircleFilled(p, s.r, U32(new Vector4(1f, 0.97f, 0.88f, tw * 0.85f)), 8);
        }

        DrawBackgroundLanterns(dl, pos, size, time, reduce);

        var radius = Px(40f);
        var driftStartY = pos.Y + size.Y * 0.86f;
        var restY = pos.Y + size.Y * 0.46f;
        var ey = reduce ? 1f : EaseOutCubic(_rise);
        var avY = AnimationHelper.Lerp(driftStartY, restY, ey);

        var gap = AnimationHelper.Lerp(Px(96f), Px(58f), ey);
        var sway = reduce ? 0f : MathF.Sin(time * 0.9f) * Px(7f);
        var leftPos = new Vector2(cx - gap + sway * 0.4f, avY);
        var rightPos = new Vector2(cx + gap + sway * 0.4f, avY);

        DrawAvatarLantern(dl, leftPos, radius, t, t.SecondaryStart, time, reduce, 0.3f, MatchContent.OwnAvatar);
        DrawAvatarLantern(dl, rightPos, radius, t, t.SecondaryEnd, time, reduce, 1.7f, MatchContent.PeerAvatar);

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

        if (_settle > 0.01f)
        {
            using (UiFonts.H1?.Push())
            {
                var label = Loc.T("deck.match_its_a_match");
                var w = ImGui.CalcTextSize(label).X;
                var x0 = cx - w * 0.5f;
                var vtx = dl.VtxBuffer.Size;
                dl.AddText(new Vector2(x0, pos.Y + size.Y * 0.105f), U32(new Vector4(1f, 1f, 1f, _settle)), label);
                if (!reduce)
                {
                    GradientText(dl, vtx, x0, x0 + w, t.SecondaryStart, t.SecondaryEnd, time * 1.5f);
                }
            }

            using (UiFonts.H3?.Push())
            {
                CenterText(dl, cx, pos.Y + size.Y * 0.185f, Loc.T("deck.match_fx_lanterns"),
                    U32(Rgba(t.AccentLight, _settle * 0.9f)));
            }

            var nameCol = U32(new Vector4(0.96f, 0.93f, 0.82f, _settle));
            var nameY = avY + radius + Px(20f);
            CenterText(dl, leftPos.X, nameY, MatchContent.OwnName, nameCol);
            CenterText(dl, rightPos.X, nameY, MatchContent.PeerName, nameCol);
        }

        DrawActionButtons(_router, pos, size, reduce ? 1f : _settle);
    }

    private static void DrawNightSky(ImDrawListPtr dl, Vector2 pos, Vector2 size, ThemeDefinition t,
        float time, bool reduce)
    {
        var top = new Vector4(0.03f, 0.04f, 0.11f, 1f);
        var bottom = new Vector4(0.08f, 0.06f, 0.16f, 1f);
        dl.AddRectFilledMultiColor(pos, pos + size, U32(top), U32(top), U32(bottom), U32(bottom));

        var horizon = pos + new Vector2(0f, size.Y * 0.72f);
        var glow = reduce ? 0.16f : 0.16f + 0.04f * MathF.Sin(time * 0.7f);
        const int steps = 7;
        for (int i = 0; i < steps; i++)
        {
            var rr = size.X * (0.5f + 0.16f * i);
            var a = glow * (1f - (float)i / steps);
            dl.AddCircleFilled(new Vector2(pos.X + size.X * 0.5f, horizon.Y + rr * 0.55f), rr,
                U32(Rgba(t.SecondaryEnd, a)), 40);
        }
    }

    private void DrawBackgroundLanterns(ImDrawListPtr dl, Vector2 pos, Vector2 size, float time, bool reduce)
    {
        foreach (var l in _lanterns)
        {
            var progress = reduce ? l.baseY : (l.baseY + time * l.speed) % 1.0f;
            var y = pos.Y + size.Y * (1.08f - progress * 1.16f);
            var sway = reduce ? 0f : MathF.Sin(time * 0.8f + l.swayPh) * l.swayAmp;
            var x = pos.X + l.nx * size.X + sway;
            var sz = Px(13f) * l.scale;

            var fade = 1f;
            var fadeEdge = size.Y * 0.12f;
            var topY = pos.Y + fadeEdge;
            if (y < topY)
            {
                fade = Math.Clamp((y - pos.Y) / fadeEdge, 0f, 1f);
            }
            if (fade <= 0.01f)
            {
                continue;
            }

            var warm = Vector4.Lerp(
                new Vector4(1f, 0.62f, 0.28f, 1f),
                new Vector4(1f, 0.82f, 0.42f, 1f),
                l.hue);
            DrawLantern(dl, new Vector2(x, y), sz, warm, fade * 0.85f, time + l.swayPh, reduce);
        }
    }

    private void DrawAvatarLantern(ImDrawListPtr dl, Vector2 center, float radius, ThemeDefinition t,
        Vector4 glowTint, float time, bool reduce, float flamePh, ISharedImmediateTexture? tex)
    {
        var halo = reduce ? 1f : 0.85f + 0.15f * MathF.Sin(time * 2.4f + flamePh);
        const int rings = 6;
        for (int i = rings; i >= 1; i--)
        {
            var rr = radius * (1.25f + 0.42f * i);
            var a = 0.13f * (1f - (float)i / (rings + 1)) * halo;
            dl.AddCircleFilled(center, rr, U32(Rgba(glowTint, a)), 40);
        }

        var canopyTop = center - new Vector2(0f, radius + Px(20f));
        var canopyL = center - new Vector2(radius + Px(8f), Px(4f));
        var canopyR = center + new Vector2(radius + Px(8f), -Px(4f));
        var warm = Vector4.Lerp(new Vector4(1f, 0.66f, 0.30f, 0.55f), Rgba(glowTint, 0.55f), 0.5f);
        dl.AddTriangleFilled(canopyTop, canopyL, canopyR, U32(warm));

        var flameY = center.Y + radius + Px(26f);
        DrawFlame(dl, new Vector2(center.X, flameY), Px(5f), time, flamePh, reduce);

        Avatar(dl, center, radius, tex, 0, 0f);
    }

    private static void DrawLantern(ImDrawListPtr dl, Vector2 center, float sz, Vector4 warm, float alpha,
        float time, bool reduce)
    {
        const int rings = 4;
        for (int i = rings; i >= 1; i--)
        {
            var rr = sz * (1.4f + 0.7f * i);
            var a = 0.10f * (1f - (float)i / (rings + 1)) * alpha;
            dl.AddCircleFilled(center, rr, U32(Rgba(warm, a)), 24);
        }

        var bodyTop = center - new Vector2(0f, sz);
        var bodyBot = center + new Vector2(0f, sz * 0.9f);
        var bw = sz * 0.78f;
        dl.AddQuadFilled(
            new Vector2(center.X - bw, center.Y - sz * 0.55f),
            new Vector2(center.X + bw, center.Y - sz * 0.55f),
            new Vector2(center.X + bw * 0.7f, bodyBot.Y),
            new Vector2(center.X - bw * 0.7f, bodyBot.Y),
            U32(Rgba(warm, alpha)));
        dl.AddTriangleFilled(
            new Vector2(center.X - bw, center.Y - sz * 0.55f),
            new Vector2(center.X + bw, center.Y - sz * 0.55f),
            bodyTop,
            U32(Rgba(warm, alpha * 0.92f)));

        var flameA = reduce ? 1f : 0.7f + 0.3f * MathF.Sin(time * 6f);
        dl.AddCircleFilled(center + new Vector2(0f, sz * 0.05f), sz * 0.26f,
            U32(new Vector4(1f, 0.96f, 0.7f, alpha * flameA)), 10);
    }

    private static void DrawFlame(ImDrawListPtr dl, Vector2 baseCenter, float r, float time, float ph, bool reduce)
    {
        var flick = reduce ? 1f : 0.82f + 0.18f * MathF.Sin(time * 8f + ph);
        dl.AddCircleFilled(baseCenter, r * 1.7f, U32(new Vector4(1f, 0.55f, 0.2f, 0.35f * flick)), 16);
        dl.AddCircleFilled(baseCenter, r, U32(new Vector4(1f, 0.85f, 0.45f, 0.9f * flick)), 12);
        dl.AddCircleFilled(baseCenter - new Vector2(0f, r * 0.25f), r * 0.45f,
            U32(new Vector4(1f, 0.98f, 0.85f, flick)), 8);
    }

    private static float EaseOutCubic(float x)
    {
        x = Math.Clamp(x, 0f, 1f);
        var inv = 1f - x;
        return 1f - inv * inv * inv;
    }
}
