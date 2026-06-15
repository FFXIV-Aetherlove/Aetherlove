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

/// <summary>Match effect — DNA Helix: a rotating double-helix climbs the centre, its two
/// strands sine-waving 180 degrees out of phase with glowing rungs between paired base points. The two
/// avatars are the prominent larger nodes near the middle, one per strand.</summary>
public sealed class MatchDnaHelixScreen : IMatchEffect
{
    private readonly ScreenRouter _router;

    private float _build;
    private float _settle;
    private readonly ConfettiBurst _confetti = new();

    private const int Samples = 60;
    private const float Frequency = 7.4f;
    private const float SpinSpeed = 0.6f;

    public MatchDnaHelixScreen(ScreenRouter router)
    {
        _router = router;
    }

    public void OnShow()
    {
        _build = 0f;
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
            _build = 1f;
            _settle = 1f;
        }
        else
        {
            AnimationHelper.ClampedProgress(ref _build, dt, 0.85f, forward: true);
            if (_build > 0.55f)
            {
                AnimationHelper.ClampedProgress(ref _settle, dt, 1.4f, forward: true);
            }
        }

        var t = ThemeService.Current;
        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var dl = ImGui.GetWindowDrawList();
        var cx = pos.X + size.X * 0.5f;

        dl.AddRectFilled(pos, pos + size, 0xFF070310);

        var center = pos + size * 0.5f;
        const int glowSteps = 6;
        for (int i = glowSteps; i >= 1; i--)
        {
            var rr = size.X * 0.16f * i;
            var a = 0.05f * (1f - (float)i / (glowSteps + 1));
            dl.AddCircleFilled(center, rr, U32(Rgba(t.SecondaryEnd, a)), 48);
        }

        var spin = reduce ? 0.6f : time * SpinSpeed;
        var amplitude = size.X * 0.24f;
        var topY = pos.Y + size.Y * 0.30f;
        var botY = pos.Y + size.Y - Px(78f);
        var midN = 0.5f;

        DrawHelix(dl, t, cx, topY, botY, amplitude, spin, reduce);
        DrawAvatarNodes(dl, t, cx, topY, botY, midN, amplitude, spin, time, reduce);
        DrawText(dl, t, cx, pos, size, time, reduce);

        if (!reduce && _settle > 0.4f)
        {
            _confetti.Draw(pos, pos + size);
        }

        DrawActionButtons(_router, pos, size, reduce ? 1f : _settle);
    }

    /// <summary>Strand X at normalised height <paramref name="n"/> for the given phase offset; the
    /// 180-degree out-of-phase strand passes <c>MathF.PI</c> as <paramref name="phaseOffset"/>.</summary>
    private static float StrandX(float cx, float n, float amplitude, float spin, float phaseOffset)
    {
        return cx + MathF.Cos(n * Frequency + spin + phaseOffset) * amplitude;
    }

    /// <summary>Front-to-back depth of a strand point in 0..1 (1 = nearest the viewer) from the sine of its
    /// phase, used to fade and shrink the far side so the helix reads as 3D.</summary>
    private static float Depth(float n, float spin, float phaseOffset)
    {
        return 0.5f + 0.5f * MathF.Sin(n * Frequency + spin + phaseOffset);
    }

    private void DrawHelix(ImDrawListPtr dl, ThemeDefinition t, float cx, float topY, float botY,
        float amplitude, float spin, bool reduce)
    {
        var visible = reduce ? 1f : Math.Clamp(_build / 0.85f, 0f, 1f);

        for (int i = 0; i < Samples; i++)
        {
            var n = i / (float)(Samples - 1);
            if (n > visible)
            {
                break;
            }

            var y = AnimationHelper.Lerp(topY, botY, n);
            var xa = StrandX(cx, n, amplitude, spin, 0f);
            var xb = StrandX(cx, n, amplitude, spin, MathF.PI);
            var da = Depth(n, spin, 0f);
            var db = Depth(n, spin, MathF.PI);

            var rungCol = Vector4.Lerp(t.SecondaryStart, t.SecondaryEnd, n);

            if (i % 3 == 0)
            {
                var nearestDepth = MathF.Max(da, db);
                var rungA = 0.18f + 0.32f * nearestDepth;
                dl.AddLine(new Vector2(xa, y), new Vector2(xb, y), U32(Rgba(rungCol, rungA)), Px(1.6f));
            }

            DrawStrandNode(dl, new Vector2(xa, y), rungCol, da);
            DrawStrandNode(dl, new Vector2(xb, y), rungCol, db);
        }
    }

    private static void DrawStrandNode(ImDrawListPtr dl, Vector2 p, Vector4 col, float depth)
    {
        var r = Px(2.4f) + Px(3.4f) * depth;
        var a = 0.30f + 0.65f * depth;
        dl.AddCircleFilled(p, r + Px(2.2f) * depth, U32(Rgba(col, a * 0.30f)), 16);
        dl.AddCircleFilled(p, r, U32(Rgba(col, a)), 16);
        dl.AddCircleFilled(p, r * 0.45f, U32(new Vector4(1f, 1f, 1f, a * 0.85f)), 12);
    }

    private void DrawAvatarNodes(ImDrawListPtr dl, ThemeDefinition t, float cx, float topY, float botY,
        float midN, float amplitude, float spin, float time, bool reduce)
    {
        var appear = reduce ? 1f : Math.Clamp((_build - 0.45f) / 0.4f, 0f, 1f);
        if (appear <= 0.001f)
        {
            return;
        }

        var y = AnimationHelper.Lerp(topY, botY, midN);
        var xOwn = StrandX(cx, midN, amplitude, spin, 0f);
        var xPeer = StrandX(cx, midN, amplitude, spin, MathF.PI);
        var dOwn = Depth(midN, spin, 0f);
        var dPeer = Depth(midN, spin, MathF.PI);

        var ownPos = new Vector2(xOwn, y);
        var peerPos = new Vector2(xPeer, y);

        var baseR = Px(30f);
        var rOwn = (baseR * (0.78f + 0.32f * dOwn)) * appear;
        var rPeer = (baseR * (0.78f + 0.32f * dPeer)) * appear;

        var connCol = U32(Rgba(Vector4.Lerp(t.SecondaryStart, t.SecondaryEnd, 0.5f), 0.55f * appear));
        dl.AddLine(ownPos, peerPos, connCol, Px(2.4f));

        var ringPhase = reduce ? 0f : time * 1.5f;
        DrawAvatarNode(dl, t, ownPos, rOwn, ringPhase, reduce, MatchContent.OwnAvatar);
        DrawAvatarNode(dl, t, peerPos, rPeer, -ringPhase, reduce, MatchContent.PeerAvatar);

        var nameY = y + baseR + Px(18f);
        var nameCol = U32(new Vector4(0.93f, 0.93f, 0.95f, appear));
        var ownLeft = xOwn <= xPeer;
        var ownNameX = ownLeft ? cx - amplitude * 0.62f : cx + amplitude * 0.62f;
        var peerNameX = ownLeft ? cx + amplitude * 0.62f : cx - amplitude * 0.62f;
        CenterText(dl, ownNameX, nameY, MatchContent.OwnName, nameCol);
        CenterText(dl, peerNameX, nameY, MatchContent.PeerName, nameCol);
    }

    private static void DrawAvatarNode(ImDrawListPtr dl, ThemeDefinition t, Vector2 center, float radius,
        float ringPhase, bool reduce, ISharedImmediateTexture? tex)
    {
        if (radius <= 1f)
        {
            return;
        }

        dl.AddCircleFilled(center, radius + Px(6f), U32(Rgba(t.SecondaryEnd, 0.22f)), 48);
        Avatar(dl, center, radius, tex, 0, 0f);
        if (reduce)
        {
            dl.AddCircle(center, radius + Px(2.5f), t.AccentU32, 48, Px(2f));
        }
        else
        {
            GradientRing(dl, center, radius + Px(2.5f), Px(2.4f), t.SecondaryStart, t.SecondaryEnd, ringPhase);
        }
    }

    private void DrawText(ImDrawListPtr dl, ThemeDefinition t, float cx, Vector2 pos, Vector2 size,
        float time, bool reduce)
    {
        if (_settle <= 0.01f)
        {
            return;
        }

        using (UiFonts.H1?.Push())
        {
            var label = Loc.T("deck.match_its_a_match");
            var w = ImGui.CalcTextSize(label).X;
            var x0 = cx - w * 0.5f;
            var vtx = dl.VtxBuffer.Size;
            dl.AddText(new Vector2(x0, pos.Y + size.Y * 0.10f), U32(new Vector4(1f, 1f, 1f, _settle)), label);
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
            CenterText(dl, cx, pos.Y + size.Y * 0.195f, Loc.T("deck.match_fx_dna"),
                U32(Rgba(t.AccentLight, _settle * 0.9f)));
        }
    }
}