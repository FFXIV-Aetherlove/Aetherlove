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

/// <summary>Match effect — Electric Storm: a crackling lightning arc of jagged
/// segments leaps between two charged avatars over a dark, faintly glowing storm, with stray sparks
/// and the odd full-screen flash.</summary>
public sealed class MatchElectricStormScreen : IMatchEffect
{
    private const int ArcPoints = 16;
    private const int SparkCount = 22;

    private readonly ScreenRouter _router;

    private float _charge;
    private float _settle;
    private readonly Random _rng = new();
    private readonly Vector2[] _arc = new Vector2[ArcPoints];
    private readonly (float along, float side, float phase, float speed)[] _sparks =
        new (float, float, float, float)[SparkCount];
    private bool _ready;

    public MatchElectricStormScreen(ScreenRouter router)
    {
        _router = router;
    }

    public void OnShow()
    {
        _charge = 0f;
        _settle = 0f;
        for (int i = 0; i < _sparks.Length; i++)
        {
            _sparks[i] = (_rng.NextSingle(),
                          -1f + _rng.NextSingle() * 2f,
                          _rng.NextSingle() * MathF.Tau,
                          0.6f + _rng.NextSingle() * 1.8f);
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
            _charge = 1f;
            _settle = 1f;
        }
        else
        {
            AnimationHelper.ClampedProgress(ref _charge, dt, 1.4f, forward: true);
            if (_charge > 0.5f)
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

        DrawStorm(dl, pos, size, center, t, time, reduce);

        var radius = Px(46f);
        var gap = Px(58f);
        var leftPos = new Vector2(cx - gap - radius, center.Y);
        var rightPos = new Vector2(cx + gap + radius, center.Y);

        var flash = reduce ? 0f : FlashAmount(time);
        if (flash > 0.001f)
        {
            dl.AddRectFilled(pos, pos + size, U32(Rgba(t.AccentLight, flash * 0.22f)));
        }

        DrawArc(dl, leftPos, rightPos, radius, t, time, reduce, flash);

        if (!reduce)
        {
            DrawSparks(dl, leftPos, rightPos, t, time);
        }

        Avatar(dl, leftPos, radius, MatchContent.OwnAvatar, 0, 0f);
        Avatar(dl, rightPos, radius, MatchContent.PeerAvatar, 0, 0f);
        if (reduce)
        {
            dl.AddCircle(leftPos, radius + Px(3f), t.AccentU32, 64, Px(2f));
            dl.AddCircle(rightPos, radius + Px(3f), t.AccentU32, 64, Px(2f));
        }
        else
        {
            var glowA = 0.25f + 0.25f * MathF.Sin(time * 7f);
            dl.AddCircle(leftPos, radius + Px(6f), U32(Rgba(t.AccentLight, glowA)), 64, Px(5f));
            dl.AddCircle(rightPos, radius + Px(6f), U32(Rgba(t.AccentLight, glowA)), 64, Px(5f));
            var ringPhase = time * 2.6f;
            GradientRing(dl, leftPos, radius + Px(3f), Px(2.6f), t.SecondaryStart, t.SecondaryEnd, ringPhase);
            GradientRing(dl, rightPos, radius + Px(3f), Px(2.6f), t.SecondaryStart, t.SecondaryEnd, -ringPhase);
        }

        if (_settle > 0.01f)
        {
            using (UiFonts.H1?.Push())
            {
                var label = Loc.T("deck.match_its_a_match");
                var w = ImGui.CalcTextSize(label).X;
                var x0 = cx - w * 0.5f;
                var vtx = dl.VtxBuffer.Size;
                dl.AddText(new Vector2(x0, pos.Y + size.Y * 0.18f), U32(new Vector4(1f, 1f, 1f, _settle)), label);
                if (!reduce)
                {
                    GradientText(dl, vtx, x0, x0 + w, t.SecondaryStart, t.SecondaryEnd, time * 2.4f);
                }
            }

            using (UiFonts.H3?.Push())
            {
                CenterText(dl, cx, pos.Y + size.Y * 0.265f, Loc.T("deck.match_fx_electric"),
                    U32(Rgba(t.AccentLight, _settle * 0.9f)));
            }

            var nameCol = U32(new Vector4(0.92f, 0.92f, 0.92f, _settle));
            CenterText(dl, leftPos.X, center.Y + radius + Px(12f), MatchContent.OwnName, nameCol);
            CenterText(dl, rightPos.X, center.Y + radius + Px(12f), MatchContent.PeerName, nameCol);
        }

        DrawActionButtons(_router, pos, size, reduce ? 1f : _settle);
    }

    private static void DrawStorm(ImDrawListPtr dl, Vector2 pos, Vector2 size, Vector2 center,
        ThemeDefinition t, float time, bool reduce)
    {
        var top = U32(new Vector4(0.05f, 0.05f, 0.09f, 1f));
        var bottom = U32(new Vector4(0.02f, 0.02f, 0.04f, 1f));
        dl.AddRectFilledMultiColor(pos, pos + size, top, top, bottom, bottom);

        const int glowSteps = 6;
        for (int i = glowSteps; i >= 1; i--)
        {
            var rr = size.X * 0.16f * i;
            var pulse = reduce ? 0f : 0.012f * MathF.Sin(time * 1.3f);
            var a = (0.045f + pulse) * (1f - (float)i / (glowSteps + 1));
            dl.AddCircleFilled(center, rr, U32(Rgba(t.SecondaryEnd, a)), 48);
        }
    }

    private void DrawArc(ImDrawListPtr dl, Vector2 left, Vector2 right, float radius,
        ThemeDefinition t, float time, bool reduce, float flash)
    {
        var a = left + new Vector2(radius, 0f);
        var b = right - new Vector2(radius, 0f);
        var span = b - a;
        var perp = new Vector2(-span.Y, span.X);
        var perpLen = MathF.Max(1f, perp.Length());
        perp /= perpLen;

        var amp = reduce ? Px(10f) : Px(20f);
        for (int i = 0; i < ArcPoints; i++)
        {
            var u = i / (float)(ArcPoints - 1);
            var envelope = MathF.Sin(u * MathF.PI);
            float offset;
            if (reduce)
            {
                offset = MathF.Sin(u * MathF.PI) * Px(8f);
            }
            else
            {
                var jitter = MathF.Sin(time * 23f + i * 2.3f) + 0.6f * MathF.Sin(time * 41f + i * 5.1f);
                offset = jitter * amp * envelope;
            }
            _arc[i] = a + span * u + perp * offset;
        }

        var coreA = reduce ? 0.85f : 0.7f + 0.3f * MathF.Sin(time * 11f) + flash * 0.4f;
        coreA = Math.Clamp(coreA, 0f, 1f);
        var glowCol = U32(Rgba(t.SecondaryStart, reduce ? 0.18f : 0.28f));
        var coreCol = U32(new Vector4(0.95f, 0.97f, 1f, coreA));

        for (int i = 1; i < ArcPoints; i++)
        {
            dl.AddLine(_arc[i - 1], _arc[i], glowCol, Px(7f));
        }
        for (int i = 1; i < ArcPoints; i++)
        {
            dl.AddLine(_arc[i - 1], _arc[i], coreCol, Px(2.2f));
        }

        if (!reduce)
        {
            for (int i = 2; i < ArcPoints - 1; i += 3)
            {
                var branchEnd = _arc[i] + perp * (MathF.Sin(time * 31f + i) * Px(14f));
                dl.AddLine(_arc[i], branchEnd, U32(Rgba(t.AccentLight, 0.4f)), Px(1.4f));
            }
        }
    }

    private void DrawSparks(ImDrawListPtr dl, Vector2 left, Vector2 right, ThemeDefinition t, float time)
    {
        var a = left;
        var span = right - left;
        var perp = new Vector2(-span.Y, span.X);
        var perpLen = MathF.Max(1f, perp.Length());
        perp /= perpLen;

        foreach (var s in _sparks)
        {
            var u = (s.along + time * s.speed * 0.35f) % 1f;
            var wob = MathF.Sin(time * 9f + s.phase) * Px(16f) * s.side;
            var p = a + span * u + perp * wob;
            var tw = 0.4f + 0.6f * MathF.Abs(MathF.Sin(time * 12f + s.phase));
            dl.AddCircleFilled(p, Px(1.4f), U32(Rgba(t.AccentLight, tw)));
        }
    }

    private static float FlashAmount(float time)
    {
        var wave = MathF.Sin(time * 0.9f) * MathF.Sin(time * 2.7f);
        var thresh = wave - 0.82f;
        if (thresh <= 0f)
        {
            return 0f;
        }
        return Math.Clamp(thresh / 0.18f, 0f, 1f);
    }
}
