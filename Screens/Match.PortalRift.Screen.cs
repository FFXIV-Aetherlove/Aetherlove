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

/// <summary>Match effect — Portal Rift: a dimensional tear cracks open at centre with
/// jagged energy edges and a swirling gradient core; the two avatars emerge and drift to opposite sides.
///</summary>
public sealed class MatchPortalRiftScreen : IMatchEffect
{
    private readonly ScreenRouter _router;

    private float _open;
    private float _emerge;
    private float _settle;

    private readonly ConfettiBurst _confetti = new();
    private readonly Random _rng = new();

    private readonly (float ang, float speed, float size, float dist, float ph)[] _embers =
        new (float, float, float, float, float)[26];
    private readonly (float baseAng, float jitterFreq, float reach)[] _bolts =
        new (float, float, float)[14];
    private bool _ready;

    public MatchPortalRiftScreen(ScreenRouter router)
    {
        _router = router;
    }

    public void OnShow()
    {
        _open = 0f;
        _emerge = 0f;
        _settle = 0f;
        _confetti.Reset();
        for (int i = 0; i < _embers.Length; i++)
        {
            _embers[i] = (
                _rng.NextSingle() * MathF.Tau,
                (0.4f + _rng.NextSingle() * 1.3f) * (_rng.Next(2) == 0 ? 1f : -1f),
                Px(1.2f) + _rng.NextSingle() * Px(2.4f),
                0.78f + _rng.NextSingle() * 0.5f,
                _rng.NextSingle() * MathF.Tau);
        }
        for (int i = 0; i < _bolts.Length; i++)
        {
            _bolts[i] = (
                i / (float)_bolts.Length * MathF.Tau,
                3f + _rng.NextSingle() * 5f,
                0.16f + _rng.NextSingle() * 0.22f);
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
            _open = 1f;
            _emerge = 1f;
            _settle = 1f;
        }
        else
        {
            AnimationHelper.ClampedProgress(ref _open, dt, 1.15f, forward: true);
            if (_open > 0.55f)
            {
                AnimationHelper.ClampedProgress(ref _emerge, dt, 1.1f, forward: true);
            }
            if (_emerge > 0.62f)
            {
                AnimationHelper.ClampedProgress(ref _settle, dt, 1.6f, forward: true);
            }
        }

        var t = ThemeService.Current;
        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var dl = ImGui.GetWindowDrawList();
        var center = pos + size * 0.5f;
        var cx = center.X;
        var cy = center.Y;

        dl.AddRectFilled(pos, pos + size, 0xFF06040C);

        var openE = EaseOutCubic(_open);
        var portalH = AnimationHelper.Lerp(Px(6f), Px(196f), openE);
        var portalW = AnimationHelper.Lerp(Px(2f), Px(92f), openE) * (0.5f + 0.5f * openE);
        var rx = portalW * 0.5f;
        var ry = portalH * 0.5f;

        DrawPortalGlow(dl, center, rx, ry, t, openE);
        DrawSwirl(dl, center, rx, ry, t, time, openE, reduce);
        DrawFilaments(dl, center, rx, ry, t, time, openE, reduce);
        DrawRiftEdge(dl, center, rx, ry, t, time, openE, reduce);
        DrawBolts(dl, center, rx, ry, t, time, openE, reduce);

        var radius = Px(46f);
        var rest = Px(72f) + radius;
        var driftE = EaseOutCubic(_emerge);
        var off = AnimationHelper.Lerp(0f, rest, driftE);
        var leftPos = new Vector2(cx - off, cy);
        var rightPos = new Vector2(cx + off, cy);
        var avatarScale = AnimationHelper.Lerp(0.18f, 1f, driftE);
        var avatarR = radius * avatarScale;

        var emergeAlpha = Math.Clamp(_emerge * 1.4f, 0f, 1f);
        if (emergeAlpha > 0.01f)
        {
            DrawEmergingAvatar(dl, center, leftPos, avatarR, emergeAlpha, t, time, reduce, +1);
            DrawEmergingAvatar(dl, center, rightPos, avatarR, emergeAlpha, t, time, reduce, -1);
        }

        DrawEmbers(dl, center, rx, ry, t, time, openE, reduce);

        if (_settle > 0.01f)
        {
            using (UiFonts.H1?.Push())
            {
                var label = Loc.T("deck.match_its_a_match");
                var w = ImGui.CalcTextSize(label).X;
                var x0 = cx - w * 0.5f;
                var vtx = dl.VtxBuffer.Size;
                dl.AddText(new Vector2(x0, pos.Y + size.Y * 0.115f), U32(new Vector4(1f, 1f, 1f, _settle)), label);
                if (!reduce)
                {
                    GradientText(dl, vtx, x0, x0 + w, t.SecondaryStart, t.SecondaryEnd, time * 1.7f);
                }
            }

            using (UiFonts.H3?.Push())
            {
                CenterText(dl, cx, pos.Y + size.Y * 0.2f, Loc.T("deck.match_fx_portal"),
                    U32(Rgba(t.AccentLight, _settle * 0.9f)));
            }

            var nameCol = U32(new Vector4(0.93f, 0.93f, 0.96f, _settle));
            CenterText(dl, leftPos.X, cy + radius + Px(14f), MatchContent.OwnName, nameCol);
            CenterText(dl, rightPos.X, cy + radius + Px(14f), MatchContent.PeerName, nameCol);

            if (!reduce)
            {
                _confetti.Draw(pos, pos + size);
            }
        }

        DrawActionButtons(_router, pos, size, reduce ? 1f : _settle);
    }

    private static void DrawPortalGlow(ImDrawListPtr dl, Vector2 c, float rx, float ry, ThemeDefinition t, float open)
    {
        const int steps = 7;
        for (int i = steps; i >= 1; i--)
        {
            var f = i / (float)steps;
            var a = 0.16f * (1f - f) * open;
            var col = U32(Rgba(Vector4.Lerp(t.SecondaryEnd, t.SecondaryStart, f), a));
            EllipseFilled(dl, c, rx + Px(46f) * f, ry + Px(58f) * f, col, 44);
        }
    }

    private static void DrawSwirl(ImDrawListPtr dl, Vector2 c, float rx, float ry, ThemeDefinition t,
        float time, float open, bool reduce)
    {
        if (open < 0.02f)
        {
            return;
        }
        EllipseFilled(dl, c, rx, ry, U32(new Vector4(0.02f, 0.0f, 0.05f, 0.95f * open)), 48);

        const int rings = 7;
        var spin = reduce ? 0f : time;
        for (int i = 0; i < rings; i++)
        {
            var f = i / (float)(rings - 1);
            var erx = rx * (0.16f + 0.84f * f);
            var ery = ry * (0.16f + 0.84f * f);
            var tilt = spin * (0.6f + i * 0.35f) * (i % 2 == 0 ? 1f : -1f);
            var col = Vector4.Lerp(t.SecondaryStart, t.SecondaryEnd, f);
            var a = (0.55f - 0.42f * f) * open;
            EllipseRing(dl, c, erx, ery, tilt, U32(Rgba(col, a)), Px(1.6f), 40);
        }

        var coreA = (reduce ? 0.8f : 0.6f + 0.4f * (0.5f + 0.5f * MathF.Sin(time * 3f))) * open;
        EllipseFilled(dl, c, rx * 0.22f, ry * 0.22f, U32(Rgba(t.AccentLight, coreA)), 28);
        EllipseFilled(dl, c, rx * 0.1f, ry * 0.1f, U32(new Vector4(1f, 1f, 1f, coreA)), 20);
    }

    private static void DrawFilaments(ImDrawListPtr dl, Vector2 c, float rx, float ry, ThemeDefinition t,
        float time, float open, bool reduce)
    {
        if (open < 0.05f)
        {
            return;
        }
        const int count = 18;
        var rot = reduce ? 0f : time * 0.9f;
        for (int i = 0; i < count; i++)
        {
            var baseAng = i / (float)count * MathF.Tau;
            var wobble = reduce ? 0f : 0.12f * MathF.Sin(time * 2.4f + i * 1.3f);
            var ang = baseAng + rot + wobble;
            var dir = new Vector2(MathF.Cos(ang), MathF.Sin(ang));
            var inner = c + new Vector2(dir.X * rx * 0.18f, dir.Y * ry * 0.18f);
            var reach = reduce ? 0.85f : 0.7f + 0.3f * MathF.Sin(time * 3f + i);
            var outer = c + new Vector2(dir.X * rx * reach, dir.Y * ry * reach);
            var col = Vector4.Lerp(t.AccentLight, t.SecondaryEnd, (i % 5) / 5f);
            dl.AddLine(inner, outer, U32(Rgba(col, 0.4f * open)), Px(1.3f));
        }
    }

    private static void DrawRiftEdge(ImDrawListPtr dl, Vector2 c, float rx, float ry, ThemeDefinition t,
        float time, float open, bool reduce)
    {
        if (open < 0.03f)
        {
            return;
        }
        const int seg = 72;
        Vector2? prev = null;
        var phase = reduce ? 0f : time * 5f;
        for (int i = 0; i <= seg; i++)
        {
            var f = i / (float)seg;
            var ang = f * MathF.Tau;
            var jag = reduce
                ? 1f + 0.03f * MathF.Sin(ang * 9f)
                : 1f + 0.06f * MathF.Sin(ang * 9f + phase) + 0.035f * MathF.Sin(ang * 21f - phase * 1.7f);
            var p = c + new Vector2(MathF.Cos(ang) * rx * jag, MathF.Sin(ang) * ry * jag);
            if (prev.HasValue)
            {
                var blend = 0.5f + 0.5f * MathF.Sin(ang * 2f - (reduce ? 0f : time * 2f));
                var col = Vector4.Lerp(t.SecondaryStart, t.SecondaryEnd, blend);
                dl.AddLine(prev.Value, p, U32(Rgba(col, 0.95f * open)), Px(2.4f));
            }
            prev = p;
        }
    }

    private void DrawBolts(ImDrawListPtr dl, Vector2 c, float rx, float ry, ThemeDefinition t,
        float time, float open, bool reduce)
    {
        if (open < 0.4f)
        {
            return;
        }
        for (int i = 0; i < _bolts.Length; i++)
        {
            var b = _bolts[i];
            var flicker = reduce ? 0.6f : 0.5f + 0.5f * MathF.Sin(time * b.jitterFreq + i * 2.1f);
            if (!reduce && flicker < 0.35f)
            {
                continue;
            }
            var ang = b.baseAng + (reduce ? 0f : time * 0.3f);
            var dir = new Vector2(MathF.Cos(ang), MathF.Sin(ang));
            var perp = new Vector2(-dir.Y, dir.X);
            var start = c + new Vector2(dir.X * rx, dir.Y * ry);
            var p = start;
            const int joints = 4;
            for (int j = 1; j <= joints; j++)
            {
                var jf = j / (float)joints;
                var lateral = reduce
                    ? MathF.Sin(jf * 6f + i) * Px(5f) * (1f - jf)
                    : MathF.Sin(jf * 6f + time * b.jitterFreq + i) * Px(7f) * (1f - jf * 0.5f);
                var next = c + new Vector2(dir.X * rx, dir.Y * ry) * (1f + b.reach * jf) + perp * lateral;
                var a = 0.85f * (1f - jf * 0.6f) * flicker * open;
                dl.AddLine(p, next, U32(Rgba(t.AccentLight, a)), Px(1.8f) * (1f - jf * 0.4f));
                p = next;
            }
        }
    }

    private void DrawEmbers(ImDrawListPtr dl, Vector2 c, float rx, float ry, ThemeDefinition t,
        float time, float open, bool reduce)
    {
        if (open < 0.25f)
        {
            return;
        }
        for (int i = 0; i < _embers.Length; i++)
        {
            var e = _embers[i];
            var ang = e.ang + (reduce ? 0f : time * e.speed * 0.4f);
            var pulse = reduce ? 0.85f : 0.6f + 0.4f * MathF.Sin(time * 2.5f + e.ph);
            var d = e.dist + (reduce ? 0f : 0.08f * MathF.Sin(time * 1.7f + e.ph));
            var p = c + new Vector2(MathF.Cos(ang) * rx * d, MathF.Sin(ang) * ry * d);
            var col = Vector4.Lerp(t.AccentLight, t.SecondaryStart, (i % 4) / 4f);
            dl.AddCircleFilled(p, e.size * (0.6f + 0.4f * pulse), U32(Rgba(col, pulse * open)));
        }
    }

    private static void DrawEmergingAvatar(ImDrawListPtr dl, Vector2 portalCenter, Vector2 avatarPos, float r,
        float alpha, ThemeDefinition t, float time, bool reduce, int sign)
    {
        if (r < Px(2f))
        {
            return;
        }
        var trailCol = U32(Rgba(t.SecondaryEnd, 0.28f * alpha));
        dl.AddLine(portalCenter, avatarPos, trailCol, Px(3f));

        var glowR = r + Px(7f);
        dl.AddCircleFilled(avatarPos, glowR, U32(Rgba(t.SecondaryStart, 0.18f * alpha)), 36);

        Avatar(dl, avatarPos, r, sign > 0 ? MatchContent.OwnAvatar : MatchContent.PeerAvatar, 0, 0f);

        if (reduce)
        {
            dl.AddCircle(avatarPos, r + Px(3f), t.AccentU32, 64, Px(2f));
        }
        else
        {
            var ringPhase = time * 1.8f * sign;
            GradientRing(dl, avatarPos, r + Px(3f), Px(2.4f), t.SecondaryStart, t.SecondaryEnd, ringPhase);
        }
    }

    private static void EllipseFilled(ImDrawListPtr dl, Vector2 c, float rx, float ry, uint col, int seg)
    {
        if (rx <= 0.1f || ry <= 0.1f)
        {
            return;
        }
        var prev = c + new Vector2(rx, 0f);
        for (int i = 1; i <= seg; i++)
        {
            var ang = i / (float)seg * MathF.Tau;
            var p = c + new Vector2(MathF.Cos(ang) * rx, MathF.Sin(ang) * ry);
            dl.AddTriangleFilled(c, prev, p, col);
            prev = p;
        }
    }

    private static void EllipseRing(ImDrawListPtr dl, Vector2 c, float rx, float ry, float tilt, uint col,
        float thickness, int seg)
    {
        if (rx <= 0.1f || ry <= 0.1f)
        {
            return;
        }
        var ct = MathF.Cos(tilt);
        var st = MathF.Sin(tilt);
        Vector2 Pt(float a)
        {
            var ex = MathF.Cos(a) * rx;
            var ey = MathF.Sin(a) * ry;
            return c + new Vector2(ex * ct - ey * st, ex * st + ey * ct);
        }
        var prev = Pt(0f);
        for (int i = 1; i <= seg; i++)
        {
            var p = Pt(i / (float)seg * MathF.Tau);
            dl.AddLine(prev, p, col, thickness);
            prev = p;
        }
    }

    private static float EaseOutCubic(float x)
    {
        x = Math.Clamp(x, 0f, 1f);
        var inv = 1f - x;
        return 1f - inv * inv * inv;
    }
}