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

/// <summary>Match effect — Kaleidoscope Bloom: a radially-symmetric mandala of
/// triangular shards blooms outward and slowly rotates, revealing the two avatars at its hub.
///</summary>
public sealed class MatchKaleidoscopeScreen : IMatchEffect
{
    private const int ShardCount = 20;

    private readonly ScreenRouter _router;

    private float _bloom;
    private float _reveal;
    private readonly ConfettiBurst _confetti = new();

    public MatchKaleidoscopeScreen(ScreenRouter router)
    {
        _router = router;
    }

    public void OnShow()
    {
        _bloom = 0f;
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
            _bloom = 1f;
            _reveal = 1f;
        }
        else
        {
            AnimationHelper.ClampedProgress(ref _bloom, dt, 0.95f, forward: true);
            if (_bloom > 0.55f)
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

        dl.AddRectFilled(pos, pos + size, 0xFF0A0712);

        var maxR = MathF.Min(size.X, size.Y) * 0.46f;
        var bloomEased = EaseOutBack(_bloom);
        var spin = reduce ? 0f : time * 0.35f;

        const int glowSteps = 5;
        for (int i = glowSteps; i >= 1; i--)
        {
            var rr = maxR * 1.05f * i / glowSteps * bloomEased;
            var a = 0.06f * (1f - (float)i / (glowSteps + 1));
            dl.AddCircleFilled(center, rr, U32(Rgba(t.SecondaryStart, a)), 48);
        }

        var shardR = maxR * Math.Clamp(bloomEased, 0f, 1f);
        var innerR = shardR * 0.18f;
        var step = MathF.Tau / ShardCount;
        for (int i = 0; i < ShardCount; i++)
        {
            var ang = i * step + spin;
            var half = step * 0.46f;
            var blend = (float)i / (ShardCount - 1);
            var shade = 0.55f + 0.45f * MathF.Cos(ang * 2f - spin);
            var col = Vector4.Lerp(t.SecondaryStart, t.SecondaryEnd, blend);
            col = new Vector4(col.X * shade, col.Y * shade, col.Z * shade, 0.92f);
            var tip = center + Dir(ang) * shardR;
            var baseL = center + Dir(ang - half) * innerR;
            var baseR = center + Dir(ang + half) * innerR;
            dl.AddTriangleFilled(baseL, tip, baseR, U32(col));

            var midL = center + Dir(ang - half * 0.5f) * (shardR * 0.5f);
            var midR = center + Dir(ang + half * 0.5f) * (shardR * 0.5f);
            var sheen = U32(Rgba(t.AccentLight, 0.18f * bloomEased));
            dl.AddTriangleFilled(center, midL, midR, sheen);
        }

        var ringPhase = reduce ? 0f : time * 1.2f;
        for (int r = 0; r < 3; r++)
        {
            var rr = shardR * (0.4f + 0.28f * r);
            if (rr <= 1f)
            {
                continue;
            }
            var thick = Px(2.5f) - r * Px(0.5f);
            var dir = (r % 2 == 0) ? 1f : -1f;
            if (reduce)
            {
                GradientRing(dl, center, rr, MathF.Max(Px(1f), thick), t.SecondaryStart, t.SecondaryEnd, 0f);
            }
            else
            {
                GradientRing(dl, center, rr, MathF.Max(Px(1f), thick), t.SecondaryStart, t.SecondaryEnd, ringPhase * dir);
            }
        }

        if (_reveal > 0.01f)
        {
            var radius = Px(40f) * Smooth01(_reveal);
            var gap = radius * 0.78f;
            var leftPos = new Vector2(cx - gap, center.Y);
            var rightPos = new Vector2(cx + gap, center.Y);

            dl.AddCircleFilled(center, radius + Px(10f), 0xCC0A0712, 48);

            Avatar(dl, leftPos, radius, MatchContent.OwnAvatar, 0, 0f);
            Avatar(dl, rightPos, radius, MatchContent.PeerAvatar, 0, 0f);
            if (reduce)
            {
                dl.AddCircle(leftPos, radius + Px(2.5f), t.AccentU32, 64, Px(2f));
                dl.AddCircle(rightPos, radius + Px(2.5f), t.AccentU32, 64, Px(2f));
            }
            else
            {
                var avatarPhase = time * 1.6f;
                GradientRing(dl, leftPos, radius + Px(2.5f), Px(2.5f), t.SecondaryStart, t.SecondaryEnd, avatarPhase);
                GradientRing(dl, rightPos, radius + Px(2.5f), Px(2.5f), t.SecondaryStart, t.SecondaryEnd, -avatarPhase);
            }

            using (UiFonts.H1?.Push())
            {
                var label = Loc.T("deck.match_its_a_match");
                var w = ImGui.CalcTextSize(label).X;
                var x0 = cx - w * 0.5f;
                var vtx = dl.VtxBuffer.Size;
                dl.AddText(new Vector2(x0, pos.Y + size.Y * 0.13f), U32(new Vector4(1f, 1f, 1f, _reveal)), label);
                if (!reduce)
                {
                    GradientText(dl, vtx, x0, x0 + w, t.SecondaryStart, t.SecondaryEnd, time * 1.6f);
                }
                else
                {
                    GradientText(dl, vtx, x0, x0 + w, t.SecondaryStart, t.SecondaryEnd, 0f);
                }
            }

            using (UiFonts.H3?.Push())
            {
                CenterText(dl, cx, pos.Y + size.Y * 0.215f, Loc.T("deck.match_fx_kaleido"),
                    U32(Rgba(t.AccentLight, _reveal * 0.9f)));
            }

            var nameCol = U32(new Vector4(0.92f, 0.92f, 0.92f, _reveal));
            CenterText(dl, cx, pos.Y + size.Y * 0.83f, MatchContent.OwnName + "  &  " + MatchContent.PeerName, nameCol);

            if (!reduce)
            {
                _confetti.Draw(pos, pos + size);
            }
        }

        DrawActionButtons(_router, pos, size);
    }

    private static Vector2 Dir(float angle)
    {
        return new Vector2(MathF.Cos(angle), MathF.Sin(angle));
    }

    private static float Smooth01(float x)
    {
        x = Math.Clamp(x, 0f, 1f);
        return x * x * (3f - 2f * x);
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
