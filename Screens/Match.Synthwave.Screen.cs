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

/// <summary>Match effect - Synthwave Sunset: a retro outrun scene with a perspective
/// neon grid floor, a sliced gradient sun and two glowing avatars.</summary>
public sealed class MatchSynthwaveScreen : IMatchEffect
{
    private readonly ScreenRouter _router;

    private float _rise;
    private float _settle;
    private float _scroll;
    private readonly ConfettiBurst _confetti = new();

    public MatchSynthwaveScreen(ScreenRouter router)
    {
        _router = router;
    }

    public void OnShow()
    {
        _rise = 0f;
        _settle = 0f;
        _scroll = 0f;
        _confetti.Reset();
    }

    public void Draw()
    {
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
            AnimationHelper.ClampedProgress(ref _rise, dt, 0.9f, forward: true);
            if (_rise > 0.55f)
            {
                AnimationHelper.ClampedProgress(ref _settle, dt, 1.4f, forward: true);
            }
            _scroll += dt * 0.35f;
            if (_scroll > 1f)
            {
                _scroll -= 1f;
            }
        }

        var t = ThemeService.Current;
        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var dl = ImGui.GetWindowDrawList();
        var cx = pos.X + size.X * 0.5f;

        var horizonY = pos.Y + size.Y * 0.55f;
        var pulse = reduce ? 0.85f : 0.7f + 0.3f * (0.5f + 0.5f * MathF.Sin(time * 1.4f));

        DrawSky(dl, pos, size, horizonY, t);
        DrawSun(dl, cx, horizonY, size, t, reduce, time);
        DrawScanlines(dl, pos, size, horizonY);
        DrawGrid(dl, pos, size, horizonY, t, pulse, reduce ? 0f : _scroll);

        var radius = Px(46f);
        var gap = Px(64f);
        var avatarY = AnimationHelper.Lerp(horizonY + Px(40f), horizonY - Px(58f), EaseOutCubic(_rise));
        var leftPos = new Vector2(cx - gap, avatarY);
        var rightPos = new Vector2(cx + gap, avatarY);

        var glowR = radius + Px(10f) + (reduce ? 0f : pulse * Px(6f));
        var glowA = (reduce ? 0.35f : 0.2f + 0.25f * pulse) * _rise;
        dl.AddCircleFilled(leftPos, glowR, U32(Rgba(t.SecondaryEnd, glowA)), 48);
        dl.AddCircleFilled(rightPos, glowR, U32(Rgba(t.SecondaryStart, glowA)), 48);

        Avatar(dl, leftPos, radius, MatchContent.OwnAvatar, 0, 0f);
        Avatar(dl, rightPos, radius, MatchContent.PeerAvatar, 0, 0f);
        if (reduce)
        {
            dl.AddCircle(leftPos, radius + Px(3f), t.AccentLightU32, 64, Px(2.5f));
            dl.AddCircle(rightPos, radius + Px(3f), t.AccentLightU32, 64, Px(2.5f));
        }
        else
        {
            var ringPhase = time * 1.7f;
            GradientRing(dl, leftPos, radius + Px(3f), Px(2.5f), t.SecondaryStart, t.SecondaryEnd, ringPhase);
            GradientRing(dl, rightPos, radius + Px(3f), Px(2.5f), t.SecondaryEnd, t.SecondaryStart, -ringPhase);
        }

        if (_settle > 0.01f)
        {
            DrawTitle(dl, cx, pos.Y + size.Y * 0.17f, t, reduce, time, pulse);

            using (UiFonts.H3?.Push())
            {
                CenterText(dl, cx, pos.Y + size.Y * 0.255f, Loc.T("deck.match_fx_synthwave"),
                    U32(Rgba(t.AccentLight, _settle * 0.9f)));
            }

            var nameCol = U32(new Vector4(0.95f, 0.92f, 0.98f, _settle));
            CenterText(dl, leftPos.X, avatarY + radius + Px(12f), MatchContent.OwnName, nameCol);
            CenterText(dl, rightPos.X, avatarY + radius + Px(12f), MatchContent.PeerName, nameCol);

            if (!reduce)
            {
                _confetti.Draw(pos, pos + size);
            }
        }

        DrawActionButtons(_router, pos, size);
    }

    private static void DrawSky(ImDrawListPtr dl, Vector2 pos, Vector2 size, float horizonY, ThemeDefinition t)
    {
        var skyTop = new Vector4(0.05f, 0.03f, 0.13f, 1f);
        var skyHorizon = new Vector4(t.SecondaryEnd.X, t.SecondaryEnd.Y, t.SecondaryEnd.Z, 1f);
        var top = pos;
        var horizon = new Vector2(pos.X + size.X, horizonY);
        dl.AddRectFilledMultiColor(top, horizon, U32(skyTop), U32(skyTop), U32(skyHorizon), U32(skyHorizon));

        var floorTop = new Vector4(0.08f, 0.02f, 0.16f, 1f);
        var floorBottom = new Vector4(0.02f, 0.01f, 0.06f, 1f);
        var fTop = new Vector2(pos.X, horizonY);
        var fBottom = pos + size;
        dl.AddRectFilledMultiColor(fTop, fBottom, U32(floorTop), U32(floorTop), U32(floorBottom), U32(floorBottom));
    }

    private static void DrawSun(ImDrawListPtr dl, float cx, float horizonY, Vector2 size, ThemeDefinition t,
        bool reduce, float time)
    {
        var sunR = size.X * 0.26f;
        var sunCenter = new Vector2(cx, horizonY - sunR * 0.35f);

        var bob = reduce ? 0f : MathF.Sin(time * 0.8f) * Px(3f);
        sunCenter.Y += bob;

        const int rows = 40;
        for (int i = 0; i < rows; i++)
        {
            var yy = sunCenter.Y - sunR + (i + 0.5f) / rows * (sunR * 2f);
            if (yy > horizonY)
            {
                break;
            }
            var dy = yy - sunCenter.Y;
            var half = MathF.Sqrt(MathF.Max(0f, sunR * sunR - dy * dy));
            if (half <= 0f)
            {
                continue;
            }
            var blend = (yy - (sunCenter.Y - sunR)) / (sunR * 2f);
            var col = Vector4.Lerp(t.SecondaryStart, t.SecondaryEnd, Math.Clamp(blend, 0f, 1f));

            var lowerHalf = dy > 0f;
            if (lowerHalf)
            {
                var bandPhase = (yy - sunCenter.Y) / sunR;
                var band = (int)(bandPhase * 6f);
                if ((band & 1) == 0)
                {
                    continue;
                }
                var shrink = 1f - bandPhase * 0.15f;
                half *= shrink;
            }

            var a = new Vector2(sunCenter.X - half, yy);
            var b = new Vector2(sunCenter.X + half, yy);
            dl.AddLine(a, b, U32(col), Px(4.5f));
        }

        dl.AddCircle(sunCenter, sunR, U32(Rgba(t.SecondaryStart, 0.5f)), 80, Px(2f));
    }

    private static void DrawScanlines(ImDrawListPtr dl, Vector2 pos, Vector2 size, float horizonY)
    {
        var step = Px(4f);
        for (float y = pos.Y; y < horizonY; y += step)
        {
            dl.AddLine(new Vector2(pos.X, y), new Vector2(pos.X + size.X, y), 0x14000000, 1f);
        }
    }

    private static void DrawGrid(ImDrawListPtr dl, Vector2 pos, Vector2 size, float horizonY, ThemeDefinition t,
        float pulse, float scroll)
    {
        dl.PushClipRect(new Vector2(pos.X, horizonY), pos + size, true);

        var bottomY = pos.Y + size.Y;
        var floorH = bottomY - horizonY;
        var vanish = new Vector2(pos.X + size.X * 0.5f, horizonY);
        var lineCol = U32(Rgba(t.AccentLight, 0.45f * pulse));
        var glowCol = U32(Rgba(t.SecondaryEnd, 0.25f * pulse));

        const int verticals = 12;
        for (int i = -verticals; i <= verticals; i++)
        {
            var fx = i / (float)verticals;
            var bottomX = pos.X + size.X * 0.5f + fx * size.X * 1.4f;
            dl.AddLine(vanish, new Vector2(bottomX, bottomY), lineCol, Px(1.4f));
        }

        const int horizontals = 16;
        for (int i = 0; i < horizontals; i++)
        {
            var f = (i + scroll) / horizontals;
            f = Math.Clamp(f, 0f, 1f);
            var depth = f * f;
            var y = horizonY + depth * floorH;
            var fade = depth;
            var col = U32(Rgba(t.AccentLight, 0.5f * fade * pulse));
            dl.AddLine(new Vector2(pos.X, y), new Vector2(pos.X + size.X, y), col, Px(1.4f));
            if (i % 2 == 0)
            {
                dl.AddLine(new Vector2(pos.X, y + Px(1.5f)), new Vector2(pos.X + size.X, y + Px(1.5f)), glowCol, Px(2.5f));
            }
        }

        dl.PopClipRect();
    }

    private static void DrawTitle(ImDrawListPtr dl, float cx, float y, ThemeDefinition t, bool reduce, float time,
        float pulse)
    {
        using (UiFonts.H1?.Push())
        {
            var label = Loc.T("deck.match_its_a_match");
            var w = ImGui.CalcTextSize(label).X;
            var x0 = cx - w * 0.5f;

            var glowOff = Px(3f);
            dl.AddText(new Vector2(x0 + glowOff, y + glowOff),
                U32(Rgba(t.SecondaryEnd, 0.4f * pulse)), label);
            dl.AddText(new Vector2(x0 - glowOff, y - glowOff),
                U32(Rgba(t.SecondaryStart, 0.4f * pulse)), label);

            var vtx = dl.VtxBuffer.Size;
            dl.AddText(new Vector2(x0, y), U32(new Vector4(1f, 0.96f, 1f, 1f)), label);
            if (reduce)
            {
                var mid = Vector4.Lerp(t.SecondaryStart, t.SecondaryEnd, 0.5f);
                GradientText(dl, vtx, x0, x0 + w, mid, mid, 0f);
            }
            else
            {
                GradientText(dl, vtx, x0, x0 + w, t.SecondaryStart, t.SecondaryEnd, time * 2.2f);
            }
        }
    }

    private static float EaseOutCubic(float x)
    {
        x = Math.Clamp(x, 0f, 1f);
        var inv = 1f - x;
        return 1f - inv * inv * inv;
    }
}