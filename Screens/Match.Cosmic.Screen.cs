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

/// <summary>Match effect - Cosmic Collision: two avatars hurtle together into a
/// shockwave of light over a twinkling starfield.</summary>
public sealed class MatchCosmicScreen : IMatchEffect
{
    private readonly ScreenRouter _router;

    private float _approach;
    private float _settle;
    private readonly ConfettiBurst _confetti = new();
    private readonly Random _rng = new();
    private readonly (float nx, float ny, float r, float ph)[] _stars = new (float, float, float, float)[80];
    private bool _starsReady;

    public MatchCosmicScreen(ScreenRouter router)
    {
        _router = router;
    }

    public void OnShow()
    {
        _approach = 0f;
        _settle = 0f;
        _confetti.Reset();
        for (int i = 0; i < _stars.Length; i++)
        {
            _stars[i] = (_rng.NextSingle(), _rng.NextSingle(),
                         Px(0.6f) + _rng.NextSingle() * Px(1.6f), _rng.NextSingle() * MathF.Tau);
        }
        _starsReady = true;
    }

    public void Draw()
    {
        if (!_starsReady)
        {
            OnShow();
        }

        var reduce = AccessibilityService.ReduceMotion;
        var dt = (float)ImGui.GetIO().DeltaTime;
        var time = (float)ImGui.GetTime();

        if (reduce)
        {
            _approach = 1f;
            _settle = 1f;
        }
        else
        {
            AnimationHelper.ClampedProgress(ref _approach, dt, 1.25f, forward: true);
            if (_approach > 0.78f)
            {
                AnimationHelper.ClampedProgress(ref _settle, dt, 1.7f, forward: true);
            }
        }

        var t = ThemeService.Current;
        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var dl = ImGui.GetWindowDrawList();
        var center = pos + size * 0.5f;
        var cx = center.X;

        // Deep-space backdrop with a faint secondary nebula glow at the centre.
        dl.AddRectFilled(pos, pos + size, 0xFF080610);
        const int glowSteps = 6;
        for (int i = glowSteps; i >= 1; i--)
        {
            var rr = size.X * 0.18f * i;
            var a = 0.05f * (1f - (float)i / (glowSteps + 1));
            dl.AddCircleFilled(center, rr, U32(Rgba(t.SecondaryEnd, a)), 48);
        }

        // Starfield (twinkling, or steady under reduced motion).
        foreach (var s in _stars)
        {
            var p = pos + new Vector2(s.nx * size.X, s.ny * size.Y);
            var tw = reduce ? 0.7f : 0.45f + 0.45f * MathF.Sin(time * 2.2f + s.ph);
            dl.AddCircleFilled(p, s.r, U32(new Vector4(1f, 1f, 1f, tw)));
        }

        // Avatars sweep in from the edges toward a small resting gap.
        var radius = Px(48f);
        var rest = Px(60f);
        var startOff = size.X * 0.62f;
        var off = AnimationHelper.Lerp(startOff, rest + radius, EaseInCubic(_approach));
        var leftPos = new Vector2(cx - off, center.Y);
        var rightPos = new Vector2(cx + off, center.Y);

        // Shockwave ring + flash at the moment of impact.
        var collide = Smooth01((_approach - 0.7f) / 0.3f);
        if (collide > 0.01f)
        {
            var ringR = collide * size.X * 0.6f;
            var ringA = (1f - collide) * 0.6f;
            dl.AddCircle(center, ringR, U32(Rgba(t.SecondaryStart, ringA)), 64, Px(3f));
            var flashA = (1f - collide) * 0.5f * (reduce ? 0f : 1f);
            if (flashA > 0.001f)
            {
                dl.AddCircleFilled(center, Px(70f) * (0.4f + collide), U32(new Vector4(1f, 1f, 1f, flashA)), 48);
            }
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
            var ringPhase = time * 1.6f;
            GradientRing(dl, leftPos, radius + Px(3f), Px(2.5f), t.SecondaryStart, t.SecondaryEnd, ringPhase);
            GradientRing(dl, rightPos, radius + Px(3f), Px(2.5f), t.SecondaryStart, t.SecondaryEnd, -ringPhase);
        }

        // Title, subtitle, names and confetti fade in as the impact settles.
        if (_settle > 0.01f)
        {
            using (UiFonts.H1?.Push())
            {
                var label = Loc.T("deck.match_its_a_match");
                var w = ImGui.CalcTextSize(label).X;
                var x0 = cx - w * 0.5f;
                var vtx = dl.VtxBuffer.Size;
                dl.AddText(new Vector2(x0, pos.Y + size.Y * 0.19f), U32(new Vector4(1f, 1f, 1f, _settle)), label);
                if (!reduce)
                {
                    GradientText(dl, vtx, x0, x0 + w, t.SecondaryStart, t.SecondaryEnd, time * 1.6f);
                }
            }

            using (UiFonts.H3?.Push())
            {
                CenterText(dl, cx, pos.Y + size.Y * 0.275f, Loc.T("deck.match_fx_cosmic"),
                    U32(Rgba(t.AccentLight, _settle * 0.9f)));
            }

            var nameCol = U32(new Vector4(0.92f, 0.92f, 0.92f, _settle));
            CenterText(dl, leftPos.X, center.Y + radius + Px(12f), MatchContent.OwnName, nameCol);
            CenterText(dl, rightPos.X, center.Y + radius + Px(12f), MatchContent.PeerName, nameCol);

            if (!reduce)
            {
                _confetti.Draw(pos, pos + size);
            }
        }

        DrawActionButtons(_router, pos, size, reduce ? 1f : _settle);
    }

    private static float Smooth01(float x)
    {
        x = Math.Clamp(x, 0f, 1f);
        return x * x * (3f - 2f * x);
    }

    private static float EaseInCubic(float x)
    {
        x = Math.Clamp(x, 0f, 1f);
        return x * x * x;
    }
}
