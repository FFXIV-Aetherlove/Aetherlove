using System;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using AetherLove.Widgets;
using Dalamud.Bindings.ImGui;
using static AetherLove.Screens.MatchFx;

namespace AetherLove.Screens;

/// <summary>Match effect - Firework Finale: staggered bursts arc up and bloom into
/// gravity-pulled rings of sparks over a night sky, lighting the two avatars below.</summary>
public sealed class MatchFireworkScreen : IMatchEffect
{
    private readonly LoveRouter _router;

    private const int BurstCount = 6;
    private const int SparksPerBurst = 28;

    private struct Burst
    {
        public float Launch;
        public float Nx;
        public float By;
        public float Speed;
        public bool UseEnd;
        public float Hue;
    }

    private float _reveal;
    private float _elapsed;
    private readonly Burst[] _bursts = new Burst[BurstCount];
    private readonly (float nx, float ny, float r, float ph)[] _stars = new (float, float, float, float)[64];
    private readonly Random _rng = new();
    private bool _ready;

    public MatchFireworkScreen(LoveRouter router)
    {
        _router = router;
    }

    public void OnShow()
    {
        _reveal = 0f;
        _elapsed = 0f;
        for (int i = 0; i < _bursts.Length; i++)
        {
            _bursts[i] = new Burst
            {
                Launch = 0.15f + i * 0.42f + _rng.NextSingle() * 0.18f,
                Nx = 0.18f + _rng.NextSingle() * 0.64f,
                By = 0.20f + _rng.NextSingle() * 0.22f,
                Speed = Px(150f) + _rng.NextSingle() * Px(90f),
                UseEnd = (i & 1) == 0,
                Hue = _rng.NextSingle(),
            };
        }
        for (int i = 0; i < _stars.Length; i++)
        {
            _stars[i] = (_rng.NextSingle(), _rng.NextSingle() * 0.7f,
                         Px(0.5f) + _rng.NextSingle() * Px(1.3f), _rng.NextSingle() * MathF.Tau);
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
            _elapsed += dt;
            AnimationHelper.ClampedProgress(ref _reveal, dt, 0.9f, forward: true);
        }

        var t = ThemeService.Current;
        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var dl = ImGui.GetWindowDrawList();
        var cx = pos.X + size.X * 0.5f;

        var skyTop = U32(new Vector4(0.03f, 0.04f, 0.10f, 1f));
        var skyBottom = U32(new Vector4(0.06f, 0.03f, 0.09f, 1f));
        dl.AddRectFilledMultiColor(pos, pos + size, skyTop, skyTop, skyBottom, skyBottom);

        foreach (var s in _stars)
        {
            var p = pos + new Vector2(s.nx * size.X, s.ny * size.Y);
            var tw = reduce ? 0.6f : 0.4f + 0.4f * MathF.Sin(time * 2.0f + s.ph);
            dl.AddCircleFilled(p, s.r, U32(new Vector4(1f, 1f, 1f, tw)));
        }

        var radius = Px(46f);
        var gap = Px(54f);
        var avatarY = pos.Y + size.Y * 0.66f;
        var leftPos = new Vector2(cx - gap - radius, avatarY);
        var rightPos = new Vector2(cx + gap + radius, avatarY);

        var avatarLight = 0f;

        if (reduce)
        {
            DrawStaticBurst(dl, pos + new Vector2(size.X * 0.30f, size.Y * 0.26f), Px(78f), t.SecondaryStart);
            DrawStaticBurst(dl, pos + new Vector2(size.X * 0.70f, size.Y * 0.32f), Px(70f), t.SecondaryEnd);
            avatarLight = 0.55f;
        }
        else
        {
            for (int i = 0; i < _bursts.Length; i++)
            {
                avatarLight += DrawBurst(dl, in _bursts[i], pos, size, t, _elapsed, avatarY);
            }
            avatarLight = Math.Clamp(avatarLight, 0f, 0.8f);
        }

        if (avatarLight > 0.001f)
        {
            var glow = U32(Rgba(t.SecondaryEnd, avatarLight * 0.5f));
            dl.AddCircleFilled(leftPos, radius + Px(16f), glow, 48);
            dl.AddCircleFilled(rightPos, radius + Px(16f), glow, 48);
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
            var ringPhase = time * 1.5f;
            GradientRing(dl, leftPos, radius + Px(3f), Px(2.5f), t.SecondaryStart, t.SecondaryEnd, ringPhase);
            GradientRing(dl, rightPos, radius + Px(3f), Px(2.5f), t.SecondaryStart, t.SecondaryEnd, -ringPhase);
        }

        var titleAlpha = reduce ? 1f : Smooth01(_reveal);
        if (titleAlpha > 0.01f)
        {
            using (UiFonts.H1?.Push())
            {
                var label = Loc.T("deck.match_its_a_match");
                var w = ImGui.CalcTextSize(label).X;
                var x0 = cx - w * 0.5f;
                var vtx = dl.VtxBuffer.Size;
                dl.AddText(new Vector2(x0, pos.Y + size.Y * 0.13f), U32(new Vector4(1f, 1f, 1f, titleAlpha)), label);
                if (!reduce)
                {
                    GradientText(dl, vtx, x0, x0 + w, t.SecondaryStart, t.SecondaryEnd, time * 1.6f);
                }
            }

            using (UiFonts.H3?.Push())
            {
                CenterText(dl, cx, pos.Y + size.Y * 0.215f, Loc.T("deck.match_fx_firework"),
                    U32(Rgba(t.AccentLight, titleAlpha * 0.9f)));
            }

            var nameCol = U32(new Vector4(0.92f, 0.92f, 0.92f, titleAlpha));
            using (UiFonts.Body?.Push())
            {
                CenterText(dl, leftPos.X, avatarY + radius + Px(10f), MatchContent.OwnName, nameCol);
                CenterText(dl, rightPos.X, avatarY + radius + Px(10f), MatchContent.PeerName, nameCol);
            }
        }

        DrawActionButtons(_router, pos, size, titleAlpha);
    }

    /// <summary>Draws one firework over its lifetime and returns avatar lighting amount (0 before launch or after bloom fades).</summary>
    private static float DrawBurst(ImDrawListPtr dl, in Burst b, Vector2 pos, Vector2 size, ThemeDefinition t,
        float elapsed, float avatarY)
    {
        const float period = BurstCount * 0.42f + 0.6f;
        var local = (elapsed - b.Launch) % period;
        if (local < 0f)
        {
            return 0f;
        }

        var apexX = pos.X + b.Nx * size.X;
        var apexY = pos.Y + b.By * size.Y;

        const float riseDur = 0.55f;
        var col = b.UseEnd ? t.SecondaryEnd : t.SecondaryStart;

        if (local < riseDur)
        {
            var rt = local / riseDur;
            var y = AnimationHelper.Lerp(avatarY, apexY, EaseOutCubic(rt));
            var head = new Vector2(apexX, y);
            var tail = new Vector2(apexX, y + Px(18f) * (1f - rt));
            dl.AddLine(tail, head, U32(Rgba(col, 0.8f)), Px(2f));
            dl.AddCircleFilled(head, Px(2.5f), U32(new Vector4(1f, 1f, 1f, 0.9f)), 12);
            return 0f;
        }

        var bt = (local - riseDur) / 1.4f;
        if (bt >= 1f)
        {
            return 0f;
        }

        var center = new Vector2(apexX, apexY);
        var reach = b.Speed * EaseOutCubic(bt);
        var fade = 1f - bt;
        var grav = bt * bt * Px(120f);

        for (int s = 0; s < SparksPerBurst; s++)
        {
            var ang = s / (float)SparksPerBurst * MathF.Tau;
            var dir = new Vector2(MathF.Cos(ang), MathF.Sin(ang));
            var sparkCol = Vector4.Lerp(t.SecondaryStart, t.SecondaryEnd,
                0.5f + 0.5f * MathF.Sin(ang + b.Hue * MathF.Tau));
            var p = center + dir * reach + new Vector2(0f, grav);
            var trail = center + dir * reach * 0.78f + new Vector2(0f, grav * 0.6f);
            dl.AddLine(trail, p, U32(Rgba(sparkCol, fade * 0.5f)), Px(1.6f));
            dl.AddCircleFilled(p, Px(1.8f) * fade + Px(0.6f), U32(Rgba(sparkCol, fade)), 8);
        }

        if (bt < 0.18f)
        {
            var flash = (1f - bt / 0.18f) * 0.7f;
            dl.AddCircleFilled(center, Px(20f), U32(new Vector4(1f, 1f, 1f, flash)), 24);
        }

        var dist = MathF.Abs(apexY - avatarY) / MathF.Max(1f, size.Y);
        return fade * 0.4f * (1f - Math.Clamp(dist, 0f, 1f));
    }

    private static void DrawStaticBurst(ImDrawListPtr dl, Vector2 center, float reach, Vector4 baseCol)
    {
        for (int s = 0; s < SparksPerBurst; s++)
        {
            var ang = s / (float)SparksPerBurst * MathF.Tau;
            var dir = new Vector2(MathF.Cos(ang), MathF.Sin(ang));
            var p = center + dir * reach;
            var trail = center + dir * reach * 0.72f;
            dl.AddLine(trail, p, U32(Rgba(baseCol, 0.45f)), Px(1.6f));
            dl.AddCircleFilled(p, Px(2f), U32(Rgba(baseCol, 0.9f)), 8);
        }
        dl.AddCircleFilled(center, Px(6f), U32(new Vector4(1f, 1f, 1f, 0.85f)), 16);
    }

    private static float Smooth01(float x)
    {
        x = Math.Clamp(x, 0f, 1f);
        return x * x * (3f - 2f * x);
    }

    private static float EaseOutCubic(float x)
    {
        x = Math.Clamp(x, 0f, 1f);
        var inv = 1f - x;
        return 1f - inv * inv * inv;
    }
}