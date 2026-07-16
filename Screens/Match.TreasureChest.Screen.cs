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

/// <summary>Match effect - Treasure Chest: a wooden, iron-banded chest swings its lid
/// open and bursts golden light rays, sparkles and spilling coins to reveal the two avatars as loot.</summary>
public sealed class MatchTreasureChestScreen : IMatchEffect
{
    private readonly ScreenRouter _router;

    private float _open;
    private float _reveal;
    private readonly Random _rng = new();
    private readonly (float ang, float speed, float dist, float size, float spin, float ph)[] _coins
        = new (float, float, float, float, float, float)[14];
    private readonly (float nx, float ny, float r, float ph)[] _sparkles = new (float, float, float, float)[26];
    private bool _ready;

    public MatchTreasureChestScreen(ScreenRouter router)
    {
        _router = router;
    }

    public void OnShow()
    {
        _open = 0f;
        _reveal = 0f;
        for (int i = 0; i < _coins.Length; i++)
        {
            var ang = -MathF.PI * 0.5f + (_rng.NextSingle() - 0.5f) * 2.2f;
            _coins[i] = (ang,
                         0.7f + _rng.NextSingle() * 0.6f,
                         Px(120f) + _rng.NextSingle() * Px(120f),
                         Px(5f) + _rng.NextSingle() * Px(5f),
                         (_rng.NextSingle() - 0.5f) * 10f,
                         _rng.NextSingle());
        }
        for (int i = 0; i < _sparkles.Length; i++)
        {
            _sparkles[i] = (_rng.NextSingle(), _rng.NextSingle(),
                            Px(1.2f) + _rng.NextSingle() * Px(2.6f), _rng.NextSingle() * MathF.Tau);
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
            _open = 1f;
            _reveal = 1f;
        }
        else
        {
            AnimationHelper.ClampedProgress(ref _open, dt, 0.95f, forward: true);
            if (_open > 0.6f)
            {
                AnimationHelper.ClampedProgress(ref _reveal, dt, 1.2f, forward: true);
            }
        }

        var t = ThemeService.Current;
        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var dl = ImGui.GetWindowDrawList();
        var center = pos + size * 0.5f;
        var cx = center.X;

        var gold = new Vector4(1.0f, 0.82f, 0.32f, 1f);
        var goldBright = new Vector4(1.0f, 0.94f, 0.62f, 1f);
        var goldDeep = new Vector4(0.78f, 0.52f, 0.12f, 1f);
        var woodLight = new Vector4(0.46f, 0.30f, 0.16f, 1f);
        var woodDark = new Vector4(0.26f, 0.16f, 0.08f, 1f);
        var ironCol = U32(new Vector4(0.20f, 0.20f, 0.24f, 1f));

        dl.AddRectFilled(pos, pos + size, 0xFF0B0805);
        dl.AddRectFilledMultiColor(pos, pos + size,
            U32(new Vector4(0.10f, 0.06f, 0.02f, 1f)), U32(new Vector4(0.10f, 0.06f, 0.02f, 1f)),
            0xFF060402, 0xFF060402);

        var chestW = Px(180f);
        var chestH = Px(96f);
        var bodyTop = center.Y - Px(30f);
        var bodyBL = new Vector2(cx - chestW * 0.5f, bodyTop + chestH);
        var bodyTR = new Vector2(cx + chestW * 0.5f, bodyTop);

        var lidProg = reduce ? 1f : Smooth01(_open / 0.85f);

        var glow = reduce ? 1f : (0.35f + 0.65f * _reveal);
        const int glowSteps = 7;
        for (int i = glowSteps; i >= 1; i--)
        {
            var rr = size.X * 0.10f * i * (0.8f + 0.6f * lidProg);
            var a = 0.10f * glow * (1f - (float)i / (glowSteps + 1));
            dl.AddCircleFilled(new Vector2(cx, bodyTop - Px(4f)), rr, U32(Rgba(goldBright, a)), 48);
        }

        if (lidProg > 0.05f)
        {
            DrawRays(dl, new Vector2(cx, bodyTop - Px(2f)), size.X * 0.62f,
                reduce ? 0f : time * 0.5f, lidProg, _reveal, gold, goldBright, reduce);
        }

        for (int i = 0; i < _sparkles.Length; i++)
        {
            var s = _sparkles[i];
            var p = pos + new Vector2(s.nx * size.X, s.ny * size.Y);
            var baseTw = reduce ? 0.45f : 0.30f + 0.50f * MathF.Sin(time * 3.0f + s.ph * MathF.Tau);
            var tw = baseTw * (0.35f + 0.65f * _reveal);
            if (tw > 0.02f)
            {
                DrawSparkle(dl, p, s.r * (0.6f + 0.6f * tw), U32(Rgba(goldBright, Math.Clamp(tw, 0f, 1f))));
            }
        }

        if (!reduce)
        {
            foreach (var c in _coins)
            {
                var pr = Math.Clamp((_reveal - c.ph * 0.25f) / 0.75f, 0f, 1f);
                if (pr <= 0.001f)
                {
                    continue;
                }
                var arc = EaseOutCubic(pr);
                var dir = new Vector2(MathF.Cos(c.ang), MathF.Sin(c.ang));
                var origin = new Vector2(cx, bodyTop);
                var travel = c.dist * arc;
                var grav = c.dist * 0.9f * arc * arc;
                var p = origin + dir * travel + new Vector2(0f, grav);
                var fade = 1f - Smooth01((pr - 0.7f) / 0.3f);
                var rot = c.spin * pr * c.speed * 6f + time * c.speed * 2f;
                DrawCoin(dl, p, c.size, rot, fade, gold, goldBright, goldDeep);
            }
        }

        DrawChestBody(dl, bodyBL, bodyTR, woodLight, woodDark, goldDeep, ironCol);

        var avRadius = Px(40f);
        var rise = AnimationHelper.Lerp(Px(34f), Px(64f), reduce ? 1f : EaseOutCubic(_reveal));
        var avY = bodyTop - rise;
        var gap = Px(48f);
        var leftPos = new Vector2(cx - gap, avY);
        var rightPos = new Vector2(cx + gap, avY);

        dl.PushClipRect(pos, new Vector2(pos.X + size.X, bodyTop), true);

        var avAlpha = reduce ? 1f : Smooth01((_reveal - 0.15f) / 0.55f);
        if (avAlpha > 0.01f)
        {
            for (int i = 4; i >= 1; i--)
            {
                var rr = avRadius + Px(10f) * i;
                var a = 0.10f * avAlpha * (1f - i / 5f);
                dl.AddCircleFilled(leftPos, rr, U32(Rgba(goldBright, a)), 40);
                dl.AddCircleFilled(rightPos, rr, U32(Rgba(goldBright, a)), 40);
            }

            Avatar(dl, leftPos, avRadius, MatchContent.OwnAvatar, 0, 0f);
            Avatar(dl, rightPos, avRadius, MatchContent.PeerAvatar, 0, 0f);

            if (reduce)
            {
                dl.AddCircle(leftPos, avRadius + Px(3f), U32(gold), 64, Px(2.5f));
                dl.AddCircle(rightPos, avRadius + Px(3f), U32(gold), 64, Px(2.5f));
            }
            else
            {
                var ph = time * 1.4f;
                GradientRing(dl, leftPos, avRadius + Px(3f), Px(2.5f), gold, goldBright, ph);
                GradientRing(dl, rightPos, avRadius + Px(3f), Px(2.5f), gold, goldBright, -ph);
            }
        }

        dl.PopClipRect();

        DrawChestLid(dl, bodyTR, bodyBL, chestW, lidProg, woodLight, woodDark, goldDeep, ironCol);

        var settle = reduce ? 1f : _reveal;
        if (settle > 0.01f)
        {
            using (UiFonts.H1?.Push())
            {
                var label = Loc.T("deck.match_fx_treasure_title");
                var w = ImGui.CalcTextSize(label).X;
                var x0 = cx - w * 0.5f;
                var vtx = dl.VtxBuffer.Size;
                dl.AddText(new Vector2(x0, pos.Y + size.Y * 0.13f), U32(Rgba(goldBright, settle)), label);
                if (!reduce)
                {
                    GradientText(dl, vtx, x0, x0 + w, gold, goldBright, time * 1.8f);
                }
            }

            using (UiFonts.H3?.Push())
            {
                CenterText(dl, cx, pos.Y + size.Y * 0.215f, Loc.T("deck.match_fx_treasure"),
                    U32(Rgba(t.AccentLight, settle * 0.92f)));
            }

            var nameCol = U32(Rgba(goldBright, settle));
            var nameY = avY + avRadius + Px(12f);
            using (UiFonts.H3?.Push())
            {
                CenterText(dl, leftPos.X, nameY, MatchContent.OwnName, nameCol);
                CenterText(dl, rightPos.X, nameY, MatchContent.PeerName, nameCol);
            }
        }

        DrawActionButtons(_router, pos, size, reduce ? 1f : _reveal);
    }

    private static void DrawRays(ImDrawListPtr dl, Vector2 origin, float length, float phase,
        float lidProg, float reveal, Vector4 a, Vector4 b, bool reduce)
    {
        const int rayCount = 14;
        var intensity = (reduce ? 0.5f : (0.35f + 0.55f * reveal)) * lidProg;
        for (int i = 0; i < rayCount; i++)
        {
            var ang = -MathF.PI + i / (float)rayCount * MathF.Tau * 0.5f - MathF.PI * 0.5f + phase;
            ang = -MathF.PI * 0.5f + (i / (float)(rayCount - 1) - 0.5f) * MathF.PI * 1.6f + phase;
            var dir = new Vector2(MathF.Cos(ang), MathF.Sin(ang));
            var perp = new Vector2(-dir.Y, dir.X);
            var half = Px(7f) * (reduce ? 1f : (0.8f + 0.4f * MathF.Sin(phase * 3f + i)));
            var len = length * (0.7f + 0.3f * ((i % 3) / 2f));
            var tip = origin + dir * len;
            var col = U32(Rgba(b, 0.18f * intensity));
            dl.AddTriangleFilled(origin + perp * half, origin - perp * half, tip, col);
            dl.AddLine(origin, tip, U32(Rgba(a, 0.30f * intensity)), Px(1.4f));
        }
    }

    private static void DrawCoin(ImDrawListPtr dl, Vector2 c, float r, float rot, float alpha,
        Vector4 face, Vector4 bright, Vector4 deep)
    {
        if (alpha <= 0.01f)
        {
            return;
        }
        var squash = MathF.Abs(MathF.Cos(rot));
        var rx = Math.Max(Px(1f), r * squash);
        var top = c - new Vector2(0f, r);
        var bottom = c + new Vector2(0f, r);
        var left = c - new Vector2(rx, 0f);
        var right = c + new Vector2(rx, 0f);
        dl.AddQuadFilled(top, right, bottom, left, U32(Rgba(face, alpha)));
        dl.AddLine(top, bottom, U32(Rgba(bright, alpha * 0.9f)), Px(1f));
        dl.AddQuad(top, right, bottom, left, U32(Rgba(deep, alpha)), Px(1f));
    }

    private static void DrawSparkle(ImDrawListPtr dl, Vector2 c, float r, uint col)
    {
        dl.AddLine(c - new Vector2(r, 0f), c + new Vector2(r, 0f), col, Px(1.2f));
        dl.AddLine(c - new Vector2(0f, r), c + new Vector2(0f, r), col, Px(1.2f));
        dl.AddCircleFilled(c, r * 0.28f, col, 8);
    }

    private static void DrawChestBody(ImDrawListPtr dl, Vector2 bl, Vector2 tr,
        Vector4 woodLight, Vector4 woodDark, Vector4 trim, uint iron)
    {
        var tl = new Vector2(bl.X, tr.Y);
        var br = new Vector2(tr.X, bl.Y);
        var round = Px(8f);
        dl.AddRectFilledMultiColor(tl, br,
            U32(woodLight), U32(woodLight), U32(woodDark), U32(woodDark));
        dl.AddRect(tl, br, U32(trim), round, ImDrawFlags.RoundCornersBottom, Px(2.5f));

        var bandW = Px(12f);
        var bandX1 = tl.X + (br.X - tl.X) * 0.30f;
        var bandX2 = tl.X + (br.X - tl.X) * 0.70f;
        dl.AddRectFilled(new Vector2(bandX1 - bandW * 0.5f, tl.Y), new Vector2(bandX1 + bandW * 0.5f, br.Y), iron);
        dl.AddRectFilled(new Vector2(bandX2 - bandW * 0.5f, tl.Y), new Vector2(bandX2 + bandW * 0.5f, br.Y), iron);

        var lockW = Px(22f);
        var lockH = Px(26f);
        var lockC = new Vector2((tl.X + br.X) * 0.5f, tl.Y + (br.Y - tl.Y) * 0.18f);
        dl.AddRectFilled(lockC - new Vector2(lockW * 0.5f, 0f), lockC + new Vector2(lockW * 0.5f, lockH),
            U32(trim), Px(4f));
        dl.AddCircleFilled(lockC + new Vector2(0f, lockH * 0.6f), Px(3.5f), 0xFF1A1208, 12);
    }

    private static void DrawChestLid(ImDrawListPtr dl, Vector2 tr, Vector2 bl, float chestW, float open,
        Vector4 woodLight, Vector4 woodDark, Vector4 trim, uint iron)
    {
        var hingeY = tr.Y;
        var hingeL = new Vector2(bl.X, hingeY);
        var hingeR = new Vector2(tr.X, hingeY);
        var lidH = Px(54f);

        var ang = AnimationHelper.Lerp(0f, MathF.PI * 0.72f, Smooth01(open));
        var cosA = MathF.Cos(ang);
        var sinA = MathF.Sin(ang);

        var upL = hingeL + new Vector2(0f, -lidH * cosA) + new Vector2(0f, 0f);
        var upR = hingeR + new Vector2(0f, -lidH * cosA);
        var lipL = upL + new Vector2(0f, -lidH * 0.18f * sinA);
        var lipR = upR + new Vector2(0f, -lidH * 0.18f * sinA);

        var topL = new Vector2(hingeL.X - Px(2f), upL.Y);
        var topR = new Vector2(hingeR.X + Px(2f), upR.Y);

        dl.AddQuadFilled(hingeL, hingeR, topR, topL, U32(woodDark));
        dl.AddQuadFilled(topL, topR, lipR, lipL,
            U32(Vector4.Lerp(woodLight, woodDark, 0.4f)));

        var midX = (hingeL.X + hingeR.X) * 0.5f;
        var bandTopHinge = new Vector2(midX, hingeY);
        var bandTop = new Vector2(midX, topL.Y);
        dl.AddLine(bandTopHinge, bandTop, iron, Px(12f));

        dl.AddLine(hingeL, hingeR, U32(trim), Px(3f));
        dl.AddCircleFilled(hingeL + new Vector2(Px(10f), 0f), Px(4f), iron, 12);
        dl.AddCircleFilled(hingeR - new Vector2(Px(10f), 0f), Px(4f), iron, 12);

        dl.AddQuad(hingeL, hingeR, topR, topL, U32(trim), Px(1.5f));
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
