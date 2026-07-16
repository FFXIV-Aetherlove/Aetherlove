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

/// <summary>Match effect - Liquid Aurora: aurora bands over a night sky; the avatars are linked by a ribbon of light.</summary>
public sealed class MatchAuroraScreen : IMatchEffect
{
    private readonly ScreenRouter _router;

    private float _reveal;
    private readonly ConfettiBurst _confetti = new();
    private readonly Random _rng = new();
    private readonly (float nx, float ny, float r, float ph, float spd)[] _bokeh = new (float, float, float, float, float)[14];
    private bool _ready;

    private const int BandCount = 5;
    private const int RibbonSegments = 48;

    public MatchAuroraScreen(ScreenRouter router)
    {
        _router = router;
    }

    public void OnShow()
    {
        _reveal = 0f;
        _confetti.Reset();
        for (int i = 0; i < _bokeh.Length; i++)
        {
            _bokeh[i] = (_rng.NextSingle(), _rng.NextSingle(),
                         Px(5f) + _rng.NextSingle() * Px(16f),
                         _rng.NextSingle() * MathF.Tau,
                         0.02f + _rng.NextSingle() * 0.05f);
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
            _reveal = 1f;
        }
        else
        {
            AnimationHelper.ClampedProgress(ref _reveal, dt, 1.1f, forward: true);
        }

        var t = ThemeService.Current;
        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var dl = ImGui.GetWindowDrawList();
        var center = pos + size * 0.5f;
        var cx = center.X;

        dl.AddRectFilled(pos, pos + size, 0xFF06040A);
        dl.AddRectFilledMultiColor(pos, pos + size,
            U32(Rgba(t.SecondaryStart, 0.05f)), U32(Rgba(t.SecondaryEnd, 0.05f)),
            0x00000000, 0x00000000);

        var auroraTime = reduce ? 0f : time;
        DrawAurora(dl, pos, size, t, auroraTime, reduce);
        DrawBokeh(dl, pos, size, t, time, reduce);

        var radius = Px(46f);
        var gap = Px(62f);
        var leftPos = new Vector2(cx - gap - radius, center.Y);
        var rightPos = new Vector2(cx + gap + radius, center.Y);

        DrawRibbon(dl, leftPos, rightPos, t, time, reduce);

        DrawFrostedHalo(dl, leftPos, radius, t, time, reduce);
        DrawFrostedHalo(dl, rightPos, radius, t, time, reduce);

        Avatar(dl, leftPos, radius, MatchContent.OwnAvatar, 0, 0f);
        Avatar(dl, rightPos, radius, MatchContent.PeerAvatar, 0, 0f);
        if (reduce)
        {
            dl.AddCircle(leftPos, radius + Px(3f), t.AccentLightU32, 64, Px(1.6f));
            dl.AddCircle(rightPos, radius + Px(3f), t.AccentLightU32, 64, Px(1.6f));
        }
        else
        {
            var ringPhase = time * 1.1f;
            GradientRing(dl, leftPos, radius + Px(3f), Px(1.8f), t.SecondaryStart, t.SecondaryEnd, ringPhase);
            GradientRing(dl, rightPos, radius + Px(3f), Px(1.8f), t.SecondaryStart, t.SecondaryEnd, -ringPhase);
        }

        var breathe = reduce ? 0.92f : 0.78f + 0.22f * (0.5f + 0.5f * MathF.Sin(time * 1.4f));
        var titleAlpha = _reveal * breathe;
        using (UiFonts.H1?.Push())
        {
            var label = Loc.T("deck.match_its_a_match");
            var w = ImGui.CalcTextSize(label).X;
            var x0 = cx - w * 0.5f;
            dl.AddText(new Vector2(x0, pos.Y + size.Y * 0.20f),
                U32(new Vector4(0.96f, 0.97f, 1f, titleAlpha)), label);
        }

        using (UiFonts.H3?.Push())
        {
            CenterText(dl, cx, pos.Y + size.Y * 0.285f, Loc.T("deck.match_fx_aurora"),
                U32(Rgba(t.AccentLight, _reveal * 0.85f)));
        }

        var nameCol = U32(new Vector4(0.9f, 0.91f, 0.95f, _reveal));
        CenterText(dl, leftPos.X, center.Y + radius + Px(14f), MatchContent.OwnName, nameCol);
        CenterText(dl, rightPos.X, center.Y + radius + Px(14f), MatchContent.PeerName, nameCol);

        if (!reduce && _reveal > 0.4f)
        {
            _confetti.Draw(pos, pos + size);
        }

        DrawActionButtons(_router, pos, size);
    }

    private void DrawAurora(ImDrawListPtr dl, Vector2 pos, Vector2 size, ThemeDefinition t, float time, bool reduce)
    {
        const int cols = 40;
        var step = size.X / cols;
        for (int band = 0; band < BandCount; band++)
        {
            var bandT = band / (float)(BandCount - 1);
            var baseY = pos.Y + size.Y * (0.30f + bandT * 0.42f);
            var thickness = Px(34f) + Px(20f) * (1f - bandT);
            var col = Vector4.Lerp(t.SecondaryStart, t.SecondaryEnd, bandT);
            var alpha = (0.10f + 0.07f * (1f - bandT)) * _reveal;
            var freq = 1.6f + band * 0.45f;
            var phase = time * (0.35f + band * 0.12f) + band * 1.3f;

            var prevTop = new Vector2(pos.X, WaveY(baseY, 0f, freq, phase, size.X, reduce));
            var prevBot = new Vector2(pos.X, prevTop.Y + thickness);
            for (int c = 1; c <= cols; c++)
            {
                var x = pos.X + c * step;
                var yc = WaveY(baseY, c / (float)cols, freq, phase, size.X, reduce);
                var top = new Vector2(x, yc);
                var bot = new Vector2(x, yc + thickness);
                var edgeFade = MathF.Sin(c / (float)cols * MathF.PI);
                var segAlpha = alpha * (0.45f + 0.55f * edgeFade);
                var packed = U32(Rgba(col, segAlpha));
                dl.AddQuadFilled(prevTop, top, bot, prevBot, packed);
                prevTop = top;
                prevBot = bot;
            }
        }
    }

    private static float WaveY(float baseY, float nx, float freq, float phase, float widthPx, bool reduce)
    {
        if (reduce)
        {
            return baseY + MathF.Sin(nx * freq * MathF.Tau) * widthPx * 0.02f;
        }
        var w1 = MathF.Sin(nx * freq * MathF.Tau + phase);
        var w2 = 0.5f * MathF.Sin(nx * freq * 1.9f * MathF.Tau - phase * 0.7f);
        return baseY + (w1 + w2) * widthPx * 0.035f;
    }

    private void DrawBokeh(ImDrawListPtr dl, Vector2 pos, Vector2 size, ThemeDefinition t, float time, bool reduce)
    {
        foreach (var b in _bokeh)
        {
            var driftY = reduce ? b.ny : (b.ny - time * b.spd) % 1f;
            if (driftY < 0f)
            {
                driftY += 1f;
            }
            var swayX = reduce ? 0f : MathF.Sin(time * 0.4f + b.ph) * size.X * 0.02f;
            var p = pos + new Vector2(b.nx * size.X + swayX, driftY * size.Y);
            var twinkle = reduce ? 0.07f : 0.05f + 0.05f * (0.5f + 0.5f * MathF.Sin(time * 1.3f + b.ph));
            var col = Vector4.Lerp(t.AccentLight, t.SecondaryEnd, b.nx);
            dl.AddCircleFilled(p, b.r, U32(Rgba(col, twinkle * _reveal)), 24);
        }
    }

    private void DrawRibbon(ImDrawListPtr dl, Vector2 left, Vector2 right, ThemeDefinition t, float time, bool reduce)
    {
        var bow = Px(28f);
        var prev = left;
        for (int i = 1; i <= RibbonSegments; i++)
        {
            var f = i / (float)RibbonSegments;
            var x = AnimationHelper.Lerp(left.X, right.X, f);
            var arch = MathF.Sin(f * MathF.PI);
            var ripple = reduce ? 0f : MathF.Sin(f * MathF.Tau * 2f - time * 3f) * Px(5f);
            var y = AnimationHelper.Lerp(left.Y, right.Y, f) - arch * bow + ripple * arch;
            var pt = new Vector2(x, y);
            var shimmer = reduce ? 0.55f : 0.5f + 0.5f * MathF.Sin(f * MathF.Tau - time * 2.4f);
            var col = Vector4.Lerp(t.SecondaryStart, t.SecondaryEnd, shimmer);
            var glowAlpha = (0.35f + 0.45f * arch) * _reveal;
            dl.AddLine(prev, pt, U32(Rgba(col, glowAlpha * 0.4f)), Px(6f));
            dl.AddLine(prev, pt, U32(Rgba(col, glowAlpha)), Px(2.2f));
            prev = pt;
        }
    }

    private void DrawFrostedHalo(ImDrawListPtr dl, Vector2 center, float radius, ThemeDefinition t, float time, bool reduce)
    {
        var pulse = reduce ? 1f : 0.9f + 0.1f * MathF.Sin(time * 1.4f);
        const int rings = 4;
        for (int i = rings; i >= 1; i--)
        {
            var rr = (radius + Px(10f)) * (1f + 0.16f * i) * pulse;
            var a = 0.08f * (1f - (float)i / (rings + 1)) * _reveal;
            var col = Vector4.Lerp(t.SecondaryStart, t.SecondaryEnd, 0.5f);
            dl.AddCircleFilled(center, rr, U32(Rgba(col, a)), 48);
        }
        dl.AddCircleFilled(center, radius + Px(5f), U32(new Vector4(1f, 1f, 1f, 0.05f * _reveal)), 48);
    }
}
