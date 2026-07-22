using System;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using AetherLove.Widgets;
using Dalamud.Bindings.ImGui;
using static AetherLove.Screens.MatchFx;

namespace AetherLove.Screens;

/// <summary>Match effect - Vinyl Spin: a turning record beneath two avatars, ringed by a pulsing equalizer.</summary>
public sealed class MatchVinylScreen : IMatchEffect
{
    private const int BarCount = 40;

    private readonly LoveRouter _router;

    private float _spin;
    private float _drop;
    private float _reveal;
    private readonly ConfettiBurst _confetti = new();

    public MatchVinylScreen(LoveRouter router)
    {
        _router = router;
    }

    public void OnShow()
    {
        _spin = 0f;
        _drop = 0f;
        _reveal = 0f;
        _confetti.Reset();
    }

    public void Draw()
    {
        var reduce = AccessibilityService.ReduceMotion;
        var dt = (float)ImGui.GetIO().DeltaTime;
        var time = (float)ImGui.GetTime();

        if (reduce)
        {
            _spin = 0f;
            _drop = 1f;
            _reveal = 1f;
        }
        else
        {
            _spin += dt * 0.9f;
            AnimationHelper.ClampedProgress(ref _drop, dt, 1.4f, forward: true);
            if (_drop > 0.45f)
            {
                AnimationHelper.ClampedProgress(ref _reveal, dt, 1.3f, forward: true);
            }
        }

        var t = ThemeService.Current;
        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var dl = ImGui.GetWindowDrawList();
        var center = pos + new Vector2(size.X * 0.5f, size.Y * 0.52f);
        var cx = center.X;

        // Warm retro backdrop with a soft secondary glow behind the record.
        dl.AddRectFilledMultiColor(pos, pos + size, 0xFF120A14, 0xFF120A14, 0xFF050308, 0xFF050308);
        const int glowSteps = 5;
        for (int i = glowSteps; i >= 1; i--)
        {
            var rr = size.X * 0.20f * i;
            var a = 0.06f * (1f - (float)i / (glowSteps + 1));
            dl.AddCircleFilled(center, rr, U32(Rgba(t.SecondaryEnd, a)), 48);
        }

        var record = Px(118f);
        var labelR = Px(46f);

        // Equalizer ring of bars pulsing to a fake beat (still at mid-height under reduced motion).
        var barInner = record + Px(10f);
        for (int i = 0; i < BarCount; i++)
        {
            var ang = i / (float)BarCount * MathF.Tau - MathF.PI * 0.5f;
            var dir = new Vector2(MathF.Cos(ang), MathF.Sin(ang));
            float beat;
            if (reduce)
            {
                beat = 0.45f;
            }
            else
            {
                var phase = i * 0.55f;
                beat = 0.5f + 0.5f * MathF.Sin(time * 6.4f + phase);
                beat = beat * beat;
            }
            var len = Px(8f) + beat * Px(26f) * _reveal;
            var inner = center + dir * barInner;
            var outer = center + dir * (barInner + len);
            var blend = 0.5f + 0.5f * MathF.Sin(ang - _spin);
            var col = U32(Rgba(Vector4.Lerp(t.SecondaryStart, t.SecondaryEnd, blend), _reveal));
            dl.AddLine(inner, outer, col, Px(3.4f));
        }

        // The record drops in from above and settles onto the deck.
        var dropOff = AnimationHelper.Lerp(-size.Y * 0.55f, 0f, EaseOutCubic(_drop));
        var recCenter = center + new Vector2(0f, dropOff);

        dl.AddCircleFilled(recCenter, record, 0xFF0B0B0E, 96);
        dl.AddCircleFilled(recCenter, record, 0xFF050506, 96);
        dl.AddCircle(recCenter, record, U32(new Vector4(0.18f, 0.18f, 0.22f, 0.9f)), 96, Px(1.4f));

        const int grooves = 11;
        for (int g = 1; g <= grooves; g++)
        {
            var gr = labelR + (record - labelR) * (g / (float)(grooves + 1));
            dl.AddCircle(recCenter, gr, U32(new Vector4(1f, 1f, 1f, 0.05f)), 80, Px(1f));
        }

        // Gradient label disc in the middle of the record.
        DrawGradientDisc(dl, recCenter, labelR, t.SecondaryStart, t.SecondaryEnd, _spin, reduce);
        dl.AddCircle(recCenter, labelR, U32(new Vector4(1f, 1f, 1f, 0.16f)), 64, Px(1.4f));
        dl.AddCircleFilled(recCenter, Px(4.5f), 0xFF101013, 24);
        dl.AddCircleFilled(recCenter, Px(2.2f), 0xFF000000, 16);

        // Two avatars seated on the label, on opposite sides of the spindle.
        var avR = Px(26f);
        var seat = labelR * 0.46f;
        var leftPos = recCenter + new Vector2(-seat, 0f);
        var rightPos = recCenter + new Vector2(seat, 0f);
        Avatar(dl, leftPos, avR, MatchContent.OwnAvatar, 0, 0f);
        Avatar(dl, rightPos, avR, MatchContent.PeerAvatar, 0, 0f);
        if (reduce)
        {
            dl.AddCircle(leftPos, avR + Px(2f), t.AccentU32, 48, Px(2f));
            dl.AddCircle(rightPos, avR + Px(2f), t.AccentU32, 48, Px(2f));
        }
        else
        {
            var ringPhase = time * 1.8f;
            GradientRing(dl, leftPos, avR + Px(2f), Px(2f), t.SecondaryStart, t.SecondaryEnd, ringPhase);
            GradientRing(dl, rightPos, avR + Px(2f), Px(2f), t.SecondaryStart, t.SecondaryEnd, -ringPhase);
        }

        // Title and names fade in as the record reveals.
        if (_reveal > 0.01f)
        {
            using (UiFonts.H1?.Push())
            {
                var label = Loc.T("deck.match_its_a_match");
                var w = ImGui.CalcTextSize(label).X;
                var x0 = cx - w * 0.5f;
                var y0 = pos.Y + size.Y * 0.085f;
                var vtx = dl.VtxBuffer.Size;
                dl.AddText(new Vector2(x0, y0), U32(new Vector4(1f, 1f, 1f, _reveal)), label);
                if (!reduce)
                {
                    GradientText(dl, vtx, x0, x0 + w, t.SecondaryStart, t.SecondaryEnd, time * 1.6f);
                }
            }

            using (UiFonts.H3?.Push())
            {
                CenterText(dl, cx, pos.Y + size.Y * 0.165f, Loc.T("deck.match_fx_vinyl"),
                    U32(Rgba(t.AccentLight, _reveal * 0.9f)));
            }

            var nameCol = U32(new Vector4(0.95f, 0.95f, 0.97f, _reveal));
            var nameY = recCenter.Y + record + Px(30f);
            using (UiFonts.H2?.Push())
            {
                CenterText(dl, cx - record * 0.62f, nameY, MatchContent.OwnName, nameCol);
                CenterText(dl, cx + record * 0.62f, nameY, MatchContent.PeerName, nameCol);
            }

            if (!reduce)
            {
                _confetti.Draw(pos, pos + size);
            }
        }

        DrawActionButtons(_router, pos, size);
    }

    /// <summary>Fills a disc with a swept two-colour gradient by fanning coloured triangles from the
    /// centre; the sweep rotates with <paramref name="phase"/> unless reduced motion is on.</summary>
    private static void DrawGradientDisc(ImDrawListPtr dl, Vector2 center, float radius,
        Vector4 a, Vector4 b, float phase, bool reduce)
    {
        const int seg = 64;
        var sweep = reduce ? 0f : phase;
        var prevAng = 0f;
        var prev = center + new Vector2(radius, 0f);
        for (int i = 1; i <= seg; i++)
        {
            var ang = i / (float)seg * MathF.Tau;
            var pt = center + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * radius;
            var blend = 0.5f + 0.5f * MathF.Sin((prevAng + ang) * 0.5f - sweep);
            var col = U32(Vector4.Lerp(a, b, blend));
            dl.AddTriangleFilled(center, prev, pt, col);
            prevAng = ang;
            prev = pt;
        }
    }

    private static float EaseOutCubic(float x)
    {
        x = Math.Clamp(x, 0f, 1f);
        var inv = 1f - x;
        return 1f - inv * inv * inv;
    }
}
