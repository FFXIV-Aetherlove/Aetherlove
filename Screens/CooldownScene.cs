using System;
using System.Collections.Generic;
using System.Numerics;
using AetherLove.Services;
using AetherLove.UI;
using AetherLove.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using static AetherLove.Screens.MatchFx;

namespace AetherLove.Screens;

/// <summary>The night-sky pegasus scene drawn over the deck for its empty states (slot cooldown and an empty
/// candidate pool). The caller supplies the heading, body, live countdown text and any refresh error; call
/// <see cref="Reset"/> when the view re-appears to replay the fade-in.</summary>
public sealed class CooldownScene
{
    private static readonly Vector4 SkyTop = new(0.07f, 0.06f, 0.16f, 1f);
    private static readonly Vector4 SkyMid = new(0.12f, 0.10f, 0.26f, 1f);
    private static readonly Vector4 SkyLow = new(0.05f, 0.04f, 0.11f, 1f);
    private static readonly Vector4 MoonCore = new(0.99f, 0.96f, 0.86f, 1f);
    private static readonly Vector4 MoonGlow = new(0.92f, 0.84f, 0.62f, 1f);
    private static readonly Vector4 Coat = new(0.13f, 0.11f, 0.24f, 1f);
    private static readonly Vector4 CoatDeep = new(0.08f, 0.07f, 0.17f, 1f);
    private static readonly Vector4 WingNear = new(0.20f, 0.18f, 0.36f, 1f);
    private static readonly Vector4 WingFar = new(0.12f, 0.11f, 0.24f, 1f);
    private static readonly Vector4 Silver = new(0.86f, 0.88f, 0.98f, 1f);
    private static readonly Vector4 Violet = new(0.62f, 0.52f, 0.92f, 1f);
    private static readonly Vector4 Gold = new(1.00f, 0.86f, 0.56f, 1f);
    private static readonly Vector4 Cream = new(0.96f, 0.94f, 0.99f, 1f);
    private static readonly Vector4 BodyText = new(0.84f, 0.86f, 0.96f, 1f);
    private static readonly Vector4 Danger = new(0.95f, 0.55f, 0.55f, 1f);

    private const float FlapHz = 1.2f;

    private float _settle;

    public void Reset()
    {
        _settle = 0f;
    }

    public bool Draw(Vector2 pos, Vector2 size, string heading, string body, string? timer, string? error, string? reswipe)
    {
        var reswipeClicked = false;
        var reduce = AccessibilityService.ReduceMotion;
        var dt = (float)ImGui.GetIO().DeltaTime;
        var time = (float)ImGui.GetTime();
        var dl = ImGui.GetWindowDrawList();

        if (reduce)
        {
            _settle = 1f;
        }
        else
        {
            AnimationHelper.ClampedProgress(ref _settle, dt, 1.4f, true);
        }
        var settle = _settle;
        var anim = reduce ? 0f : time;

        var min = pos;
        var max = pos + size;
        var cx = pos.X + size.X * 0.5f;

        dl.PushClipRect(min, max, true);

        DrawSky(dl, min, max);
        DrawStars(dl, pos, size, time, reduce, settle);

        var moonCenter = new Vector2(cx + Px(70f), pos.Y + size.Y * 0.31f);
        DrawMoon(dl, moonCenter, Px(54f), anim, settle);

        DrawClouds(dl, pos, size, time, reduce, settle);

        var flap = reduce ? 0.35f : MathF.Sin(time * FlapHz);
        var bob = reduce ? 0f : -flap * Px(10f);
        var horseCenter = new Vector2(cx - Px(8f), pos.Y + size.Y * 0.47f + bob);

        DrawStardust(dl, horseCenter, time, reduce, settle);
        DrawHorse(dl, horseCenter, flap, time, reduce, settle);

        var showReswipe = !string.IsNullOrEmpty(reswipe);

        DrawHeading(dl, cx, pos.Y + Px(44f), heading, reduce, time, settle);
        DrawBody(dl, cx, max.Y - (showReswipe ? Px(134f) : Px(106f)), size.X - Px(72f), body, settle);

        if (showReswipe)
        {
            reswipeClicked = DrawReswipePill(dl, cx, max.Y - Px(86f), reswipe!, anim, settle);
        }

        if (!string.IsNullOrEmpty(timer))
        {
            DrawTimerPill(dl, cx, max.Y - (showReswipe ? Px(42f) : Px(58f)), timer, anim, settle);
        }

        // The refresh error and the reswipe CTA compete for the same bottom slot; reswipe wins (errors here
        // are transient refresh failures and clear on the next tick).
        if (!string.IsNullOrEmpty(error) && !showReswipe)
        {
            DrawError(dl, cx, max.Y - Px(28f), size.X - Px(72f), error, settle);
        }

        dl.PopClipRect();
        return reswipeClicked;
    }

    private void DrawSky(ImDrawListPtr dl, Vector2 min, Vector2 max)
    {
        var split = new Vector2(max.X, AnimationHelper.Lerp(min.Y, max.Y, 0.5f));
        dl.AddRectFilledMultiColor(min, split, U32(SkyTop), U32(SkyTop), U32(SkyMid), U32(SkyMid));
        var lowMin = new Vector2(min.X, split.Y);
        dl.AddRectFilledMultiColor(lowMin, max, U32(SkyMid), U32(SkyMid), U32(SkyLow), U32(SkyLow));
    }

    private void DrawStars(ImDrawListPtr dl, Vector2 pos, Vector2 size, float time, bool reduce, float settle)
    {
        Span<float> sx = stackalloc float[] { 0.07f, 0.18f, 0.29f, 0.41f, 0.52f, 0.63f, 0.71f, 0.83f, 0.91f, 0.13f, 0.37f, 0.58f, 0.78f, 0.96f };
        Span<float> sy = stackalloc float[] { 0.12f, 0.30f, 0.08f, 0.22f, 0.40f, 0.15f, 0.34f, 0.20f, 0.46f, 0.50f, 0.55f, 0.66f, 0.60f, 0.72f };
        for (int i = 0; i < sx.Length; i++)
        {
            var x = pos.X + sx[i] * size.X;
            var y = pos.Y + sy[i] * size.Y;
            var twinkle = reduce ? 0.6f : 0.45f + 0.45f * MathF.Sin(time * 1.6f + i * 1.7f);
            var a = twinkle * settle;
            var r = Px(1.1f) + sx[i] * Px(1.4f);
            dl.AddCircleFilled(new Vector2(x, y), r, U32(new Vector4(1f, 0.98f, 0.92f, a)), 8);
            if (i % 4 == 0)
            {
                var spike = r * (reduce ? 2.2f : 2.0f + 0.8f * MathF.Sin(time * 2f + i));
                var sc = U32(new Vector4(1f, 0.98f, 0.92f, a * 0.55f));
                dl.AddLine(new Vector2(x - spike, y), new Vector2(x + spike, y), sc, Px(1f));
                dl.AddLine(new Vector2(x, y - spike), new Vector2(x, y + spike), sc, Px(1f));
            }
        }
    }

    private void DrawMoon(ImDrawListPtr dl, Vector2 center, float radius, float anim, float settle)
    {
        var pulse = 1f + 0.03f * MathF.Sin(anim * 0.8f);
        for (int i = 6; i >= 1; i--)
        {
            var f = i / 6f;
            var a = 0.13f * (1f - f) * settle;
            dl.AddCircleFilled(center, radius * (1f + f * 1.5f) * pulse, U32(Rgba(MoonGlow, a)), 64);
        }
        dl.AddCircleFilled(center, radius * pulse, U32(Rgba(MoonGlow, settle)), 64);
        dl.AddCircleFilled(center, radius * 0.92f * pulse, U32(Rgba(MoonCore, settle)), 64);
        var craterCol = U32(Rgba(MoonGlow, 0.5f * settle));
        dl.AddCircleFilled(center + new Vector2(-radius * 0.30f, -radius * 0.22f), radius * 0.16f, craterCol, 20);
        dl.AddCircleFilled(center + new Vector2(radius * 0.24f, radius * 0.10f), radius * 0.12f, craterCol, 18);
        dl.AddCircleFilled(center + new Vector2(radius * 0.04f, radius * 0.40f), radius * 0.09f, craterCol, 16);
    }

    private void DrawClouds(ImDrawListPtr dl, Vector2 pos, Vector2 size, float time, bool reduce, float settle)
    {
        DrawCloud(dl, pos, size, 0.20f, 0.40f, Px(56f), time * 0.010f, 0.10f, reduce, settle);
        DrawCloud(dl, pos, size, 0.70f, 0.66f, Px(72f), time * 0.014f, 0.08f, reduce, settle);
    }

    private void DrawCloud(ImDrawListPtr dl, Vector2 pos, Vector2 size, float fx, float fy, float scale, float drift, float alpha, bool reduce, float settle)
    {
        var driftX = reduce ? 0f : ((drift % 1f) - 0.5f) * Px(40f);
        var c = new Vector2(pos.X + fx * size.X + driftX, pos.Y + fy * size.Y);
        var col = U32(new Vector4(0.55f, 0.56f, 0.74f, alpha * settle));
        FillEllipse(dl, c + new Vector2(-scale * 0.55f, scale * 0.10f), scale * 0.5f, scale * 0.30f, col, 28);
        FillEllipse(dl, c + new Vector2(0f, -scale * 0.06f), scale * 0.62f, scale * 0.40f, col, 30);
        FillEllipse(dl, c + new Vector2(scale * 0.55f, scale * 0.12f), scale * 0.46f, scale * 0.28f, col, 28);
        FillEllipse(dl, c + new Vector2(scale * 0.05f, scale * 0.20f), scale * 0.80f, scale * 0.24f, col, 30);
    }

    private void DrawStardust(ImDrawListPtr dl, Vector2 horseCenter, float time, bool reduce, float settle)
    {
        var tailRoot = horseCenter + new Vector2(-Px(70f), -Px(6f));
        var count = 16;
        for (int i = 0; i < count; i++)
        {
            var f = i / (float)(count - 1);
            var px = tailRoot.X - f * Px(120f);
            var wave = MathF.Sin(f * 6f + (reduce ? 0f : time * 1.5f)) * Px(10f) * f;
            var py = tailRoot.Y + f * Px(34f) + wave;
            var fade = (1f - f) * settle;
            var tw = reduce ? 0.7f : 0.4f + 0.6f * MathF.Abs(MathF.Sin(time * 3f + i * 1.3f));
            var a = fade * tw * 0.9f;
            var col = Vector4.Lerp(Silver, Violet, f);
            var r = Px(2.4f) * (1f - 0.5f * f);
            dl.AddCircleFilled(new Vector2(px, py), r, U32(Rgba(col, a)), 10);
            if (i % 3 == 0)
            {
                var sp = r * 2.4f;
                var sc = U32(Rgba(Cream, a * 0.6f));
                dl.AddLine(new Vector2(px - sp, py), new Vector2(px + sp, py), sc, Px(1f));
                dl.AddLine(new Vector2(px, py - sp), new Vector2(px, py + sp), sc, Px(1f));
            }
        }
    }

    private void DrawHorse(ImDrawListPtr dl, Vector2 center, float flap, float time, bool reduce, float settle)
    {
        var coat = U32(Rgba(Coat, settle));
        var coatDeep = U32(Rgba(CoatDeep, settle));
        var shoulder = new Vector2(center.X + Px(8f), center.Y - Px(6f));

        DrawTail(dl, center, time, reduce, coatDeep);
        DrawWing(dl, shoulder, flap * 0.85f + 0.05f, WingFar, Silver, 0.55f, settle, true);

        DrawBodySilhouette(dl, center, coat, coatDeep, settle);

        var neckBase = new Vector2(center.X + Px(40f), center.Y - Px(16f));
        var poll = new Vector2(center.X + Px(76f), center.Y - Px(54f));
        DrawNeck(dl, neckBase, poll, coat);
        DrawMane(dl, neckBase, poll, time, reduce, coatDeep);
        DrawHead(dl, poll, coat, settle);

        DrawLegs(dl, center, coat, coatDeep, settle);

        DrawWing(dl, shoulder, flap, WingNear, Cream, 1f, settle, false);
    }

    private void DrawBodySilhouette(ImDrawListPtr dl, Vector2 center, uint coat, uint coatDeep, float settle)
    {
        FillEllipse(dl, center, Px(58f), Px(30f), coat, 36);
        FillEllipse(dl, new Vector2(center.X - Px(34f), center.Y + Px(2f)), Px(30f), Px(26f), coatDeep, 30);
        FillEllipse(dl, new Vector2(center.X + Px(34f), center.Y - Px(4f)), Px(28f), Px(24f), coat, 30);
        var rim = U32(Rgba(Silver, 0.30f * settle));
        dl.AddBezierCubic(
            new Vector2(center.X - Px(48f), center.Y - Px(18f)),
            new Vector2(center.X - Px(10f), center.Y - Px(34f)),
            new Vector2(center.X + Px(30f), center.Y - Px(30f)),
            new Vector2(center.X + Px(54f), center.Y - Px(14f)),
            rim, Px(2f), 28);
    }

    private void DrawNeck(ImDrawListPtr dl, Vector2 baseC, Vector2 poll, uint coat)
    {
        var dir = Vector2.Normalize(poll - baseC);
        var perp = new Vector2(-dir.Y, dir.X);
        var baseHalf = Px(24f);
        var topHalf = Px(14f);
        var a = baseC + perp * baseHalf;
        var b = baseC - perp * baseHalf;
        var c = poll - perp * topHalf;
        var d = poll + perp * topHalf;
        dl.AddQuadFilled(a, b, c, d, coat);
    }

    private void DrawHead(ImDrawListPtr dl, Vector2 poll, uint coat, float settle)
    {
        var jaw = poll + new Vector2(Px(3f), Px(7f));
        FillEllipse(dl, jaw, Px(15f), Px(13f), coat, 20);
        var muzzleDir = Vector2.Normalize(new Vector2(0.72f, 0.70f));
        var muzzle = jaw + muzzleDir * Px(24f);
        var perp = new Vector2(-muzzleDir.Y, muzzleDir.X);
        var a = jaw + perp * Px(10f);
        var b = jaw - perp * Px(10f);
        var c = muzzle - perp * Px(6.5f);
        var d = muzzle + perp * Px(6.5f);
        dl.AddQuadFilled(a, b, c, d, coat);
        FillEllipse(dl, muzzle, Px(8f), Px(6.5f), coat, 16);

        var earDir = Vector2.Normalize(new Vector2(-0.20f, -1f));
        var earPerp = new Vector2(-earDir.Y, earDir.X);
        DrawEar(dl, poll + new Vector2(Px(0f), -Px(2f)), earDir, earPerp, Px(16f), Px(6f), coat);
        DrawEar(dl, poll + new Vector2(Px(11f), Px(0f)), earDir, earPerp, Px(14f), Px(5.5f), coat);

        var eye = jaw + new Vector2(Px(4f), -Px(3f));
        dl.AddCircleFilled(eye, Px(2.4f), U32(Rgba(Silver, 0.9f * settle)), 10);
        var rim = U32(Rgba(Silver, 0.4f * settle));
        dl.AddLine(poll + new Vector2(Px(12f), -Px(2f)), muzzle + new Vector2(Px(2f), -Px(2f)), rim, Px(1.6f));
    }

    private void DrawEar(ImDrawListPtr dl, Vector2 baseC, Vector2 dir, Vector2 perp, float len, float half, uint coat)
    {
        var a = baseC + perp * half;
        var b = baseC - perp * half;
        var tip = baseC + dir * len;
        dl.AddTriangleFilled(a, b, tip, coat);
    }

    private void DrawMane(ImDrawListPtr dl, Vector2 baseC, Vector2 poll, float time, bool reduce, uint coat)
    {
        var steps = 8;
        for (int i = 0; i <= steps; i++)
        {
            var f = i / (float)steps;
            var on = Vector2.Lerp(poll + new Vector2(-Px(8f), -Px(2f)), baseC + new Vector2(-Px(2f), -Px(18f)), f);
            var sway = reduce ? 0f : MathF.Sin(time * 1.4f + f * 3f) * Px(4f);
            var len = Px(20f) * (0.7f + 0.5f * f);
            var tip = on + new Vector2(-Px(12f) - f * Px(8f) + sway, len * 0.6f);
            dl.AddTriangleFilled(on + new Vector2(-Px(3f), 0f), on + new Vector2(Px(3f), 0f), tip, coat);
        }
    }

    private void DrawTail(ImDrawListPtr dl, Vector2 center, float time, bool reduce, uint coat)
    {
        var root = new Vector2(center.X - Px(56f), center.Y - Px(6f));
        var strands = 6;
        for (int i = 0; i < strands; i++)
        {
            var spread = (i - (strands - 1) * 0.5f) * Px(4f);
            var w1 = reduce ? Px(3f) : MathF.Sin(time * 1.3f + i * 0.4f) * Px(5f);
            var w2 = reduce ? Px(6f) : MathF.Sin(time * 1.3f + i * 0.4f - 0.7f) * Px(9f);
            var p1 = root + new Vector2(0f, spread * 0.3f);
            var p2 = root + new Vector2(-Px(26f), Px(16f) + spread * 0.8f + w1);
            var p3 = root + new Vector2(-Px(42f), Px(38f) + spread * 1.2f + w2);
            var p4 = root + new Vector2(-Px(52f), Px(64f) + spread * 1.6f + w2 * 1.2f);
            dl.AddBezierCubic(p1, p2, p3, p4, coat, Px(5f) - i * Px(0.5f), 24);
        }
    }

    private void DrawLegs(ImDrawListPtr dl, Vector2 center, uint coat, uint coatDeep, float settle)
    {
        var fHip2 = new Vector2(center.X + Px(30f), center.Y + Px(20f));
        var fKnee2 = fHip2 + new Vector2(Px(4f), Px(20f));
        var fHoof2 = fKnee2 + new Vector2(-Px(2f), Px(20f));
        DrawLegBone(dl, fHip2, fKnee2, fHoof2, coatDeep, Px(7.5f), Px(5.5f));

        var rHip2 = new Vector2(center.X - Px(30f), center.Y + Px(20f));
        var rKnee2 = rHip2 + new Vector2(-Px(4f), Px(20f));
        var rHoof2 = rKnee2 + new Vector2(Px(2f), Px(20f));
        DrawLegBone(dl, rHip2, rKnee2, rHoof2, coatDeep, Px(8f), Px(5.5f));

        var fHip = new Vector2(center.X + Px(38f), center.Y + Px(16f));
        var fKnee = fHip + new Vector2(Px(5f), Px(24f));
        var fHoof = fKnee + new Vector2(-Px(3f), Px(24f));
        DrawLegBone(dl, fHip, fKnee, fHoof, coat, Px(9.5f), Px(6.5f));

        var rHip = new Vector2(center.X - Px(38f), center.Y + Px(14f));
        var rKnee = rHip + new Vector2(-Px(5f), Px(24f));
        var rHoof = rKnee + new Vector2(Px(3f), Px(24f));
        DrawLegBone(dl, rHip, rKnee, rHoof, coat, Px(10f), Px(6.5f));
    }

    private void DrawLegBone(ImDrawListPtr dl, Vector2 hip, Vector2 knee, Vector2 hoof, uint col, float upperW, float lowerW)
    {
        dl.AddLine(hip, knee, col, upperW);
        dl.AddLine(knee, hoof, col, lowerW);
        dl.AddCircleFilled(hoof, lowerW * 0.7f, col, 12);
    }

    private void DrawWing(ImDrawListPtr dl, Vector2 shoulder, float flap, Vector4 baseCol, Vector4 edgeCol, float alphaScale, float settle, bool far)
    {
        var sweep = AnimationHelper.Lerp(-0.55f, 0.45f, 0.5f + 0.5f * flap);
        var feathers = 7;
        var spanLen = Px(96f) * (far ? 0.82f : 1f);
        var baseAng = -2.55f + sweep;
        var spread = 1.05f;
        var origin = shoulder + new Vector2(far ? -Px(6f) : Px(4f), far ? -Px(4f) : Px(0f));

        var tips = new Vector2[feathers];
        for (int i = 0; i < feathers; i++)
        {
            var f = i / (float)(feathers - 1);
            var ang = baseAng + (f - 0.5f) * spread - sweep * 0.4f * f;
            var len = spanLen * (0.62f + 0.55f * MathF.Sin(f * MathF.PI * 0.92f));
            tips[i] = origin + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * len;
        }

        for (int i = 0; i < feathers - 1; i++)
        {
            var f = i / (float)(feathers - 1);
            var shade = Vector4.Lerp(baseCol, baseCol * 1.25f, f);
            var col = U32(Rgba(shade, settle * alphaScale));
            var rootA = origin + (tips[i] - origin) * 0.16f;
            var rootB = origin + (tips[i + 1] - origin) * 0.16f;
            dl.AddTriangleFilled(rootA, tips[i], tips[i + 1], col);
            dl.AddTriangleFilled(rootA, tips[i + 1], rootB, col);
        }

        var membrane = U32(Rgba(baseCol * 0.9f, settle * alphaScale));
        for (int i = 0; i < feathers; i++)
        {
            dl.PathLineTo(tips[feathers - 1 - i]);
        }
        dl.PathLineTo(origin);
        dl.PathFillConvex(membrane);

        var edge = U32(Rgba(edgeCol, (far ? 0.4f : 0.7f) * settle * alphaScale));
        dl.AddLine(origin, tips[0], edge, Px(2f));
        dl.AddLine(tips[0], tips[1], edge, Px(2f));
        for (int i = 0; i < feathers; i++)
        {
            var mid = Vector2.Lerp(origin, tips[i], 0.5f);
            dl.AddLine(mid, tips[i], U32(Rgba(edgeCol, 0.22f * settle * alphaScale)), Px(1.3f));
        }
    }

    private void DrawHeading(ImDrawListPtr dl, float cx, float y, string heading, bool reduce, float time, float settle)
    {
        using (UiFonts.H1?.Push())
        {
            int vtx = dl.VtxBuffer.Size;
            CenterText(dl, cx, y, heading, U32(Rgba(Cream, settle)));
            if (!reduce)
            {
                var hw = ImGui.CalcTextSize(heading).X;
                GradientText(dl, vtx, cx - hw * 0.5f, cx + hw * 0.5f, Silver, Violet, time * 1.3f);
            }
        }
    }

    private void DrawBody(ImDrawListPtr dl, float cx, float bottomY, float wrap, string body, float settle)
    {
        using (UiFonts.H3?.Push())
        {
            var lines = WrapLines(body, wrap);
            var lineH = ImGui.GetTextLineHeightWithSpacing();
            var topY = bottomY - lines.Count * lineH;

            var padX = Px(16f);
            var padY = Px(10f);
            var stripMin = new Vector2(cx - wrap * 0.5f - padX, topY - padY);
            var stripMax = new Vector2(cx + wrap * 0.5f + padX, bottomY + padY);
            dl.AddRectFilled(stripMin, stripMax, U32(new Vector4(0.06f, 0.06f, 0.14f, 0.50f * settle)), Px(12f));
            dl.AddRect(stripMin, stripMax, U32(Rgba(Violet, 0.25f * settle)), Px(12f), ImDrawFlags.None, Px(1f));

            var col = U32(Rgba(BodyText, settle));
            for (int i = 0; i < lines.Count; i++)
            {
                CenterText(dl, cx, topY + i * lineH, lines[i], col);
            }
        }
    }

    private void DrawError(ImDrawListPtr dl, float cx, float topY, float wrap, string error, float settle)
    {
        var lines = WrapLines(error, wrap);
        var lineH = ImGui.GetTextLineHeightWithSpacing();
        var col = U32(Rgba(Danger, settle));
        for (int i = 0; i < lines.Count; i++)
        {
            CenterText(dl, cx, topY + i * lineH, lines[i], col);
        }
    }

    private static List<string> WrapLines(string text, float wrap)
    {
        var result = new List<string>();
        var line = string.Empty;
        foreach (var word in text.Split(' '))
        {
            var probe = line.Length == 0 ? word : line + " " + word;
            if (ImGui.CalcTextSize(probe).X > wrap && line.Length > 0)
            {
                result.Add(line);
                line = word;
            }
            else
            {
                line = probe;
            }
        }
        if (line.Length > 0)
        {
            result.Add(line);
        }
        return result;
    }

    /// <summary>An interactive "reswipe your last card" pill, shown on the cooldown screen when the player
    /// still has an undoable last swipe and a reswipe left. Styled in the scene's violet palette to stand
    /// apart from the gold countdown. Returns true on click.</summary>
    private bool DrawReswipePill(ImDrawListPtr dl, float cx, float y, string label, float anim, float settle)
    {
        float iconPx = Px(15f);
        ImGui.PushFont(Plugin.PluginInterface.UiBuilder.FontIcon);
        var iconFont = ImGui.GetFont();
        var icon = FontAwesomeIcon.Undo.ToIconString();
        var iconSz = ImGui.CalcTextSize(icon) * (iconPx / ImGui.GetFontSize());
        ImGui.PopFont();

        var textSz = ImGui.CalcTextSize(label);
        var gap = Px(9f);
        var padX = Px(18f);
        var padY = Px(10f);
        var contentW = iconSz.X + gap + textSz.X;
        var pillW = contentW + padX * 2f;
        var pillH = MathF.Max(textSz.Y, iconSz.Y) + padY * 2f;
        var pillMin = new Vector2(cx - pillW * 0.5f, y - pillH * 0.5f);
        var pillMax = pillMin + new Vector2(pillW, pillH);

        ImGui.SetCursorScreenPos(pillMin);
        var clicked = ImGui.InvisibleButton("##cooldownReswipe", new Vector2(pillW, pillH));
        var hovered = ImGui.IsItemHovered();

        var round = pillH * 0.5f;
        var pulse = 0.5f + 0.5f * MathF.Sin(anim * 1.8f);
        dl.AddRectFilled(pillMin - new Vector2(Px(5f), Px(5f)), pillMax + new Vector2(Px(5f), Px(5f)),
            U32(Rgba(Violet, (hovered ? 0.34f : 0.22f) * pulse * settle)), (pillH + Px(10f)) * 0.5f);
        var fill = hovered ? new Vector4(0.27f, 0.21f, 0.45f, 1f) : new Vector4(0.20f, 0.16f, 0.36f, 1f);
        dl.AddRectFilled(pillMin, pillMax, U32(Rgba(fill, settle)), round);
        dl.AddRect(pillMin, pillMax, U32(Rgba(Violet, (hovered ? 0.95f : 0.72f) * settle)), round, ImDrawFlags.None, Px(1.5f));

        var startX = cx - contentW * 0.5f;
        var midY = (pillMin.Y + pillMax.Y) * 0.5f;
        dl.AddText(iconFont, iconPx, new Vector2(startX, midY - iconSz.Y * 0.5f), U32(Rgba(Cream, settle)), icon);
        dl.AddText(new Vector2(startX + iconSz.X + gap, midY - textSz.Y * 0.5f), U32(Rgba(Cream, settle)), label);
        return clicked;
    }

    private void DrawTimerPill(ImDrawListPtr dl, float cx, float y, string timer, float anim, float settle)
    {
        float iconPx = Px(16f);
        ImGui.PushFont(Plugin.PluginInterface.UiBuilder.FontIcon);
        var iconFont = ImGui.GetFont();
        var clock = FontAwesomeIcon.Clock.ToIconString();
        var iconSz = ImGui.CalcTextSize(clock) * (iconPx / ImGui.GetFontSize());
        ImGui.PopFont();

        Vector2 textSz;
        using (UiFonts.H3?.Push())
        {
            textSz = ImGui.CalcTextSize(timer);
        }

        var gap = Px(9f);
        var padX = Px(20f);
        var padY = Px(11f);
        var contentW = iconSz.X + gap + textSz.X;
        var pillW = contentW + padX * 2f;
        var pillH = MathF.Max(textSz.Y, iconSz.Y) + padY * 2f;
        var pillMin = new Vector2(cx - pillW * 0.5f, y - pillH * 0.5f);
        var pillMax = pillMin + new Vector2(pillW, pillH);

        var round = pillH * 0.5f;
        var glow = 0.5f + 0.5f * MathF.Sin(anim * 1.6f);
        dl.AddRectFilled(pillMin - new Vector2(Px(5f), Px(5f)), pillMax + new Vector2(Px(5f), Px(5f)), U32(Rgba(Violet, 0.20f * glow * settle)), (pillH + Px(10f)) * 0.5f);
        dl.AddRectFilled(pillMin, pillMax, U32(Rgba(new Vector4(0.15f, 0.13f, 0.30f, 1f), settle)), round);
        dl.AddRect(pillMin, pillMax, U32(Rgba(Gold, 0.6f * settle)), round, ImDrawFlags.None, Px(1.5f));

        var startX = cx - contentW * 0.5f;
        var midY = (pillMin.Y + pillMax.Y) * 0.5f;
        dl.AddText(iconFont, iconPx, new Vector2(startX, midY - iconSz.Y * 0.5f), U32(Rgba(Gold, settle)), clock);
        using (UiFonts.H3?.Push())
        {
            dl.AddText(new Vector2(startX + iconSz.X + gap, midY - textSz.Y * 0.5f), U32(Rgba(Cream, settle)), timer);
        }
    }

    private void FillEllipse(ImDrawListPtr dl, Vector2 center, float halfW, float halfH, uint col, int segments)
    {
        for (int i = 0; i < segments; i++)
        {
            var ang = i / (float)segments * MathF.Tau;
            dl.PathLineTo(center + new Vector2(MathF.Cos(ang) * halfW, MathF.Sin(ang) * halfH));
        }
        dl.PathFillConvex(col);
    }
}
