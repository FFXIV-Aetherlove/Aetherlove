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

/// <summary>Match effect — Lava Lamp: a warm 70s gradient column where soft
/// translucent globules rise and sink on eased sine motion, while two big avatar-bearing blobs
/// drift together and merge at centre.</summary>
public sealed class MatchLavaLampScreen : IMatchEffect
{
    private readonly ScreenRouter _router;

    private float _rise;
    private float _settle;

    private const int BlobCount = 7;
    private readonly (float nx, float baseY, float amp, float r, float ph, float speed, float tint)[] _blobs
        = new (float, float, float, float, float, float, float)[BlobCount];
    private readonly Random _rng = new();
    private bool _ready;

    public MatchLavaLampScreen(ScreenRouter router)
    {
        _router = router;
    }

    public void OnShow()
    {
        _rise = 0f;
        _settle = 0f;
        for (int i = 0; i < BlobCount; i++)
        {
            _blobs[i] = (
                0.18f + _rng.NextSingle() * 0.64f,
                0.2f + _rng.NextSingle() * 0.6f,
                0.16f + _rng.NextSingle() * 0.22f,
                Px(26f) + _rng.NextSingle() * Px(30f),
                _rng.NextSingle() * MathF.Tau,
                0.18f + _rng.NextSingle() * 0.3f,
                _rng.NextSingle());
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
            _rise = 1f;
            _settle = 1f;
        }
        else
        {
            AnimationHelper.ClampedProgress(ref _rise, dt, 0.5f, forward: true);
            if (_rise > 0.7f)
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

        DrawBackdrop(dl, pos, size, t);

        dl.PushClipRect(pos, pos + size, true);

        var lampTime = reduce ? 1.7f : time;
        foreach (var b in _blobs)
        {
            var wob = MathF.Sin(lampTime * b.speed * MathF.Tau + b.ph);
            var eased = MathF.Sin(wob * (MathF.PI * 0.5f));
            var ny = b.baseY - eased * b.amp;
            var bx = pos.X + b.nx * size.X + MathF.Cos(lampTime * b.speed * 0.6f + b.ph) * Px(10f);
            var by = pos.Y + ny * size.Y;
            var tintCol = Vector4.Lerp(t.SecondaryStart, t.SecondaryEnd, b.tint);
            DrawGlob(dl, new Vector2(bx, by), b.r, tintCol, 0.5f);
        }

        var radius = Px(46f);
        var rest = Px(72f);
        var startOff = size.X * 0.34f;
        var off = AnimationHelper.Lerp(startOff, rest, EaseInOutSine(_rise));
        var bob = reduce ? 0f : MathF.Sin(time * 1.1f) * Px(8f);
        var leftPos = new Vector2(cx - off, center.Y + bob);
        var rightPos = new Vector2(cx + off, center.Y - bob);

        var carrierR = radius + Px(28f);
        var carrierA = AnimationHelper.Lerp(0.38f, 0.62f, _rise);
        DrawGlob(dl, leftPos, carrierR, t.SecondaryStart, carrierA);
        DrawGlob(dl, rightPos, carrierR, t.SecondaryEnd, carrierA);

        var mergeGlow = reduce ? 1f : Smooth01((_rise - 0.7f) / 0.3f);
        if (mergeGlow > 0.01f)
        {
            var bridge = Vector4.Lerp(t.SecondaryStart, t.SecondaryEnd, 0.5f);
            DrawGlob(dl, center, carrierR * (0.8f + 0.5f * mergeGlow), bridge, 0.5f * mergeGlow);
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
            var ringPhase = time * 1.3f;
            GradientRing(dl, leftPos, radius + Px(3f), Px(2.5f), t.SecondaryStart, t.SecondaryEnd, ringPhase);
            GradientRing(dl, rightPos, radius + Px(3f), Px(2.5f), t.SecondaryStart, t.SecondaryEnd, -ringPhase);
        }

        dl.PopClipRect();

        var nameCol = U32(new Vector4(0.96f, 0.92f, 0.86f, _rise));
        CenterText(dl, leftPos.X, leftPos.Y + carrierR + Px(4f), MatchContent.OwnName, nameCol);
        CenterText(dl, rightPos.X, rightPos.Y + carrierR + Px(4f), MatchContent.PeerName, nameCol);

        if (_settle > 0.01f)
        {
            using (UiFonts.H1?.Push())
            {
                var label = Loc.T("deck.match_its_a_match");
                var w = ImGui.CalcTextSize(label).X;
                var x0 = cx - w * 0.5f;
                var vtx = dl.VtxBuffer.Size;
                dl.AddText(new Vector2(x0, pos.Y + size.Y * 0.13f), U32(new Vector4(1f, 0.97f, 0.92f, _settle)), label);
                if (!reduce)
                {
                    GradientText(dl, vtx, x0, x0 + w, t.SecondaryStart, t.SecondaryEnd, time * 1.3f);
                }
            }

            using (UiFonts.H3?.Push())
            {
                CenterText(dl, cx, pos.Y + size.Y * 0.215f, Loc.T("deck.match_fx_lavalamp"),
                    U32(Rgba(t.AccentLight, _settle * 0.92f)));
            }
        }

        DrawActionButtons(_router, pos, size, reduce ? 1f : _settle);
    }

    private static void DrawBackdrop(ImDrawListPtr dl, Vector2 pos, Vector2 size, ThemeDefinition t)
    {
        var top = U32(new Vector4(0.18f, 0.06f, 0.02f, 1f));
        var midV = Vector4.Lerp(t.SecondaryStart, new Vector4(0.22f, 0.05f, 0.03f, 1f), 0.55f);
        var mid = U32(new Vector4(midV.X, midV.Y, midV.Z, 1f));
        var bot = U32(new Vector4(0.05f, 0.01f, 0.06f, 1f));
        var half = pos + new Vector2(size.X, size.Y * 0.5f);
        dl.AddRectFilledMultiColor(pos, half, top, top, mid, mid);
        dl.AddRectFilledMultiColor(new Vector2(pos.X, half.Y), pos + size, mid, mid, bot, bot);
    }

    /// <summary>Stacks soft concentric fills so blobs read as translucent, metaball-style lava.</summary>
    private static void DrawGlob(ImDrawListPtr dl, Vector2 c, float r, Vector4 tint, float coreAlpha)
    {
        const int rings = 5;
        for (int i = rings; i >= 1; i--)
        {
            var f = (float)i / rings;
            var rr = r * f;
            var a = coreAlpha * (1.15f - f) * 0.85f;
            dl.AddCircleFilled(c, rr, U32(Rgba(tint, Math.Clamp(a, 0f, 1f))), 40);
        }
    }

    private static float Smooth01(float x)
    {
        x = Math.Clamp(x, 0f, 1f);
        return x * x * (3f - 2f * x);
    }

    private static float EaseInOutSine(float x)
    {
        x = Math.Clamp(x, 0f, 1f);
        return -(MathF.Cos(MathF.PI * x) - 1f) * 0.5f;
    }
}
