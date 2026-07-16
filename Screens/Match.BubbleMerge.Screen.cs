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

/// <summary>Match effect - Bubble Merge: a drift of iridescent soap bubbles rises
/// while two avatar-bearing bubbles float together and merge into one at centre.</summary>
public sealed class MatchBubbleMergeScreen : IMatchEffect
{
    private readonly ScreenRouter _router;

    private float _merge;
    private float _settle;
    private readonly Random _rng = new();
    private readonly (float nx, float baseY, float r, float ph, float sway, float speed, float tint, float a)[] _bubbles
        = new (float, float, float, float, float, float, float, float)[26];
    private bool _ready;

    public MatchBubbleMergeScreen(ScreenRouter router)
    {
        _router = router;
    }

    public void OnShow()
    {
        _merge = 0f;
        _settle = 0f;
        for (int i = 0; i < _bubbles.Length; i++)
        {
            _bubbles[i] = (
                _rng.NextSingle(),
                _rng.NextSingle(),
                Px(7f) + _rng.NextSingle() * Px(26f),
                _rng.NextSingle() * MathF.Tau,
                Px(10f) + _rng.NextSingle() * Px(22f),
                0.06f + _rng.NextSingle() * 0.10f,
                _rng.NextSingle(),
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
            _merge = 1f;
            _settle = 1f;
        }
        else
        {
            AnimationHelper.ClampedProgress(ref _merge, dt, 0.55f, forward: true);
            if (_merge > 0.74f)
            {
                AnimationHelper.ClampedProgress(ref _settle, dt, 1.4f, forward: true);
            }
        }

        var t = ThemeService.Current;
        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var dl = ImGui.GetWindowDrawList();
        var center = pos + size * 0.5f;
        var cx = center.X;

        var topTint = Rgba(t.SecondaryStart, 0.20f);
        var botTint = Rgba(t.SecondaryEnd, 0.10f);
        dl.AddRectFilledMultiColor(pos, pos + size, U32(Rgba(new Vector4(0.07f, 0.08f, 0.12f, 1f), 1f)),
            U32(Rgba(new Vector4(0.07f, 0.08f, 0.12f, 1f), 1f)),
            U32(new Vector4(0.10f, 0.09f, 0.14f, 1f)), U32(new Vector4(0.10f, 0.09f, 0.14f, 1f)));
        dl.AddRectFilledMultiColor(pos, pos + size, U32(topTint), U32(topTint), U32(botTint), U32(botTint));

        const int glowSteps = 5;
        for (int i = glowSteps; i >= 1; i--)
        {
            var rr = size.X * 0.20f * i;
            var a = 0.06f * (1f - (float)i / (glowSteps + 1));
            dl.AddCircleFilled(center, rr, U32(Rgba(t.SecondaryStart, a)), 48);
        }

        dl.PushClipRect(pos, pos + size, true);
        foreach (var b in _bubbles)
        {
            var rise = reduce ? 0f : (time * b.speed) % 1.2f;
            var ny = b.baseY * 1.1f - rise + 0.1f;
            ny -= MathF.Floor(ny / 1.2f) * 1.2f;
            var sway = reduce ? 0f : MathF.Sin(time * 0.7f + b.ph) * b.sway;
            var p = new Vector2(pos.X + b.nx * size.X + sway, pos.Y + ny * size.Y);
            var tint = Vector4.Lerp(t.SecondaryStart, t.SecondaryEnd, b.tint);
            DrawBubble(dl, p, b.r, tint, b.a, t);
        }
        dl.PopClipRect();

        var radius = Px(46f);
        var bubbleR = radius + Px(16f);
        var spread = size.X * 0.30f;
        var ease = EaseInOutCubic(reduce ? 1f : _merge);
        var off = AnimationHelper.Lerp(spread, 0f, ease);
        var leftPos = new Vector2(cx - off, center.Y);
        var rightPos = new Vector2(cx + off, center.Y);

        var avatarA = reduce ? 1f : Math.Clamp((1f - _merge) / 0.5f, 0f, 1f);
        var mergedA = reduce ? 1f : Smooth01((_merge - 0.6f) / 0.4f);

        var bob = reduce ? 0f : MathF.Sin(time * 1.3f) * Px(4f);
        leftPos.Y += bob;
        rightPos.Y -= bob;

        if (mergedA > 0.01f)
        {
            var bigR = bubbleR * (1f + 0.35f * mergedA);
            DrawBubble(dl, center, bigR, Vector4.Lerp(t.SecondaryStart, t.SecondaryEnd, 0.5f), 0.30f * mergedA, t);
            if (!reduce)
            {
                GradientRing(dl, center, bigR, Px(2.4f), t.SecondaryStart, t.SecondaryEnd, time * 1.4f);
            }
            else
            {
                dl.AddCircle(center, bigR, t.AccentU32, 72, Px(2f));
            }
        }

        if (avatarA > 0.01f)
        {
            DrawBubbleShell(dl, leftPos, bubbleR, t, avatarA);
            DrawBubbleShell(dl, rightPos, bubbleR, t, avatarA);
        }

        var mergeAvR = AnimationHelper.Lerp(radius, radius * 0.82f, ease);
        if (mergedA > 0.5f)
        {
            var inner = Px(13f);
            Avatar(dl, center + new Vector2(-mergeAvR + inner, 0f), mergeAvR, MatchContent.OwnAvatar, 0, 0f);
            Avatar(dl, center + new Vector2(mergeAvR - inner, 0f), mergeAvR, MatchContent.PeerAvatar, 0, 0f);
        }
        else
        {
            Avatar(dl, leftPos, radius, MatchContent.OwnAvatar, 0, 0f);
            Avatar(dl, rightPos, radius, MatchContent.PeerAvatar, 0, 0f);
        }

        if (_settle > 0.01f)
        {
            using (UiFonts.H1?.Push())
            {
                var label = Loc.T("deck.match_its_a_match");
                var w = ImGui.CalcTextSize(label).X;
                var x0 = cx - w * 0.5f;
                var vtx = dl.VtxBuffer.Size;
                dl.AddText(new Vector2(x0, pos.Y + size.Y * 0.17f), U32(new Vector4(1f, 1f, 1f, _settle)), label);
                if (!reduce)
                {
                    GradientText(dl, vtx, x0, x0 + w, t.SecondaryStart, t.SecondaryEnd, time * 1.5f);
                }
            }

            using (UiFonts.H3?.Push())
            {
                CenterText(dl, cx, pos.Y + size.Y * 0.255f, Loc.T("deck.match_fx_bubble"),
                    U32(Rgba(t.AccentLight, _settle * 0.9f)));
            }

            var nameCol = U32(new Vector4(0.93f, 0.93f, 0.95f, _settle));
            var nameY = center.Y + bubbleR * 1.35f + Px(20f);
            CenterText(dl, cx, nameY, MatchContent.OwnName + "  +  " + MatchContent.PeerName, nameCol);
        }

        DrawActionButtons(_router, pos, size, reduce ? 1f : _settle);
    }

    /// <summary>Translucent soap bubble: tinted fill, bright rim, offset white highlight.</summary>
    private static void DrawBubble(ImDrawListPtr dl, Vector2 c, float r, Vector4 tint, float alpha, ThemeDefinition theme)
    {
        dl.AddCircleFilled(c, r, U32(Rgba(tint, alpha * 0.7f)), 40);
        dl.AddCircleFilled(c, r * 0.96f, U32(Rgba(tint, alpha * 0.35f)), 40);
        dl.AddCircle(c, r, U32(Rgba(theme.AccentLight, MathF.Min(1f, alpha * 2.4f))), 40, MathF.Max(1f, r * 0.05f));
        var hi = c + new Vector2(-r * 0.34f, -r * 0.38f);
        dl.AddCircleFilled(hi, r * 0.18f, U32(new Vector4(1f, 1f, 1f, MathF.Min(0.9f, alpha * 2.6f))), 16);
    }

    /// <summary>Bubble shell around an avatar; no inner fill so the avatar reads clearly.</summary>
    private static void DrawBubbleShell(ImDrawListPtr dl, Vector2 c, float r, ThemeDefinition theme, float alpha)
    {
        var tint = Vector4.Lerp(theme.SecondaryStart, theme.SecondaryEnd, 0.4f);
        dl.AddCircleFilled(c, r, U32(Rgba(tint, 0.16f * alpha)), 56);
        dl.AddCircle(c, r, U32(Rgba(theme.AccentLight, 0.85f * alpha)), 56, Px(2.4f));
        var hi = c + new Vector2(-r * 0.40f, -r * 0.42f);
        dl.AddCircleFilled(hi, r * 0.16f, U32(new Vector4(1f, 1f, 1f, 0.65f * alpha)), 18);
    }

    private static float Smooth01(float x)
    {
        x = Math.Clamp(x, 0f, 1f);
        return x * x * (3f - 2f * x);
    }

    private static float EaseInOutCubic(float x)
    {
        x = Math.Clamp(x, 0f, 1f);
        return x < 0.5f ? 4f * x * x * x : 1f - MathF.Pow(-2f * x + 2f, 3f) * 0.5f;
    }
}