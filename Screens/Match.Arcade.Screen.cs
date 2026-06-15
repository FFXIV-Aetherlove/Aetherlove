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

/// <summary>Match effect — Arcade 8-bit: a CRT cabinet with scanlines, a chunky pixel
/// heart that beats, a blocky "MATCH!" banner and a high-score counter that ticks up.</summary>
public sealed class MatchArcadeScreen : IMatchEffect
{
    private readonly ScreenRouter _router;

    private float _boot;
    private float _settle;
    private float _scoreFill;
    private const int ScoreTarget = 133769;

    private static readonly byte[,] Heart =
    {
        { 0, 1, 1, 0, 0, 1, 1, 0 },
        { 1, 2, 2, 1, 1, 2, 2, 1 },
        { 1, 2, 2, 2, 2, 2, 2, 1 },
        { 1, 2, 2, 2, 2, 2, 2, 1 },
        { 0, 1, 2, 2, 2, 2, 1, 0 },
        { 0, 0, 1, 2, 2, 1, 0, 0 },
        { 0, 0, 0, 1, 1, 0, 0, 0 },
    };

    private static readonly byte[][] Glyphs =
    {
        new byte[] { 0b01110, 0b10001, 0b10001, 0b11111, 0b10001, 0b10001, 0b10001 }, // A
        new byte[] { 0b10001, 0b11011, 0b10101, 0b10101, 0b10001, 0b10001, 0b10001 }, // M
        new byte[] { 0b11111, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100 }, // T
        new byte[] { 0b01110, 0b10001, 0b10000, 0b10000, 0b10000, 0b10001, 0b01110 }, // C
        new byte[] { 0b10001, 0b10001, 0b10001, 0b11111, 0b10001, 0b10001, 0b10001 }, // H
        new byte[] { 0b00100, 0b00100, 0b00100, 0b00100, 0b00100, 0b00000, 0b00100 }, // !
    };

    private const string Word = "MATCH!";

    public MatchArcadeScreen(ScreenRouter router)
    {
        _router = router;
    }

    public void OnShow()
    {
        _boot = 0f;
        _settle = 0f;
        _scoreFill = 0f;
    }

    public void Draw()
    {
        var reduce = AccessibilityService.ReduceMotion;
        var dt = (float)ImGui.GetIO().DeltaTime;
        var time = (float)ImGui.GetTime();

        if (reduce)
        {
            _boot = 1f;
            _settle = 1f;
            _scoreFill = 1f;
        }
        else
        {
            AnimationHelper.ClampedProgress(ref _boot, dt, 1.6f, forward: true);
            if (_boot > 0.45f)
            {
                AnimationHelper.ClampedProgress(ref _settle, dt, 1.5f, forward: true);
                AnimationHelper.ClampedProgress(ref _scoreFill, dt, 0.7f, forward: true);
            }
        }

        var t = ThemeService.Current;
        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var dl = ImGui.GetWindowDrawList();
        var cx = pos.X + size.X * 0.5f;

        var hot = t.SecondaryStart;
        var cool = t.SecondaryEnd;
        var rim = t.AccentLight;

        // CRT backdrop with a subtle vignette and a centre phosphor glow.
        dl.AddRectFilled(pos, pos + size, 0xFF0A0710);
        dl.AddRectFilledMultiColor(pos, pos + size,
            U32(new Vector4(0f, 0f, 0f, 0.45f)), U32(new Vector4(0f, 0f, 0f, 0.45f)),
            U32(new Vector4(0f, 0f, 0f, 0f)), U32(new Vector4(0f, 0f, 0f, 0f)));
        dl.AddRectFilledMultiColor(pos, pos + size,
            U32(new Vector4(0f, 0f, 0f, 0f)), U32(new Vector4(0f, 0f, 0f, 0f)),
            U32(new Vector4(0f, 0f, 0f, 0.55f)), U32(new Vector4(0f, 0f, 0f, 0.55f)));
        var glowR = size.X * 0.5f;
        for (int i = 5; i >= 1; i--)
        {
            var a = 0.04f * (1f - i / 6f);
            dl.AddCircleFilled(pos + size * new Vector2(0.5f, 0.42f), glowR * i / 5f, U32(Rgba(cool, a)), 40);
        }

        // Scanlines: thin dark bands across the whole panel, dimmed during reduced motion.
        var lineStep = Px(4f);
        var scanA = reduce ? 0.10f : 0.16f;
        for (float y = pos.Y; y < pos.Y + size.Y; y += lineStep)
        {
            dl.AddRectFilled(new Vector2(pos.X, y), new Vector2(pos.X + size.X, y + Px(1.5f)),
                U32(new Vector4(0f, 0f, 0f, scanA)));
        }

        var px = MathF.Max(Px(3f), MathF.Floor(Px(3.4f)));

        // INSERT-COIN banner along the very top.
        using (UiFonts.Body?.Push())
        {
            var top = Loc.T("deck.match_fx_arcade_players");
            var blink = reduce ? 1f : (MathF.Sin(time * 4f) > -0.4f ? 1f : 0.25f);
            CenterText(dl, cx, pos.Y + Px(10f), top, U32(Rgba(rim, _boot * blink)));
        }

        // Big blocky MATCH! banner built from a 5x7 pixel font.
        var glyphCols = 5;
        var glyphRows = 7;
        var glyphGap = 1;
        var totalCells = Word.Length * glyphCols + (Word.Length - 1) * glyphGap;
        var bannerW = totalCells * px;
        var bannerX = cx - bannerW * 0.5f;
        var bannerY = pos.Y + size.Y * 0.13f;
        var bannerReveal = (int)MathF.Round(AnimationHelper.Lerp(0f, Word.Length, Math.Clamp(_boot / 0.85f, 0f, 1f)));

        for (int gi = 0; gi < Word.Length; gi++)
        {
            if (!reduce && gi >= bannerReveal)
            {
                continue;
            }
            var rows = GlyphFor(Word[gi]);
            var gx = bannerX + gi * (glyphCols + glyphGap) * px;
            for (int r = 0; r < glyphRows; r++)
            {
                var bits = rows[r];
                for (int c = 0; c < glyphCols; c++)
                {
                    if ((bits & (1 << (glyphCols - 1 - c))) == 0)
                    {
                        continue;
                    }
                    var pmin = new Vector2(gx + c * px, bannerY + r * px);
                    var pmax = pmin + new Vector2(px - Px(0.5f), px - Px(0.5f));
                    var shade = reduce ? 0.5f : 0.5f + 0.5f * MathF.Sin((c + gi * glyphCols) * 0.5f - time * 3f);
                    dl.AddRectFilled(pmin + new Vector2(px * 0.18f, px * 0.18f), pmax + new Vector2(px * 0.18f, px * 0.18f),
                        U32(new Vector4(0f, 0f, 0f, 0.55f)));
                    dl.AddRectFilled(pmin, pmax, U32(Vector4.Lerp(hot, cool, shade)));
                }
            }
        }

        // Pixel heart that beats between two avatar frames.
        var heartCols = Heart.GetLength(1);
        var heartRows = Heart.GetLength(0);
        var hpx = px * 1.55f;
        var beat = reduce ? 1f : 1f + 0.10f * MathF.Max(0f, MathF.Sin(time * 5.2f));
        var hpxB = hpx * beat;
        var heartW = heartCols * hpxB;
        var heartH = heartRows * hpxB;
        var heartCx = cx;
        var heartCy = pos.Y + size.Y * 0.45f;
        var heartX = heartCx - heartW * 0.5f;
        var heartY = heartCy - heartH * 0.5f;
        var heartAlpha = _settle;

        for (int r = 0; r < heartRows; r++)
        {
            for (int c = 0; c < heartCols; c++)
            {
                var v = Heart[r, c];
                if (v == 0)
                {
                    continue;
                }
                var pmin = new Vector2(heartX + c * hpxB, heartY + r * hpxB);
                var pmax = pmin + new Vector2(hpxB - Px(0.5f), hpxB - Px(0.5f));
                var col = v == 1 ? Rgba(hot, heartAlpha) : Rgba(Vector4.Lerp(hot, rim, 0.45f), heartAlpha);
                dl.AddRectFilled(pmin, pmax, U32(col));
            }
        }

        // Avatars in chunky pixel frames flanking the heart.
        var radius = Px(34f);
        var frameGap = Px(80f);
        var leftPos = new Vector2(cx - frameGap, heartCy);
        var rightPos = new Vector2(cx + frameGap, heartCy);
        DrawPixelFrame(dl, leftPos, radius, px, rim, hot, heartAlpha, reduce, time);
        DrawPixelFrame(dl, rightPos, radius, px, rim, hot, heartAlpha, reduce, time + 0.8f);
        Avatar(dl, leftPos, radius, MatchContent.OwnAvatar, 0, 0f);
        Avatar(dl, rightPos, radius, MatchContent.PeerAvatar, 0, 0f);

        var nameCol = U32(new Vector4(0.95f, 0.95f, 0.98f, _settle));
        using (UiFonts.H3?.Push())
        {
            CenterText(dl, leftPos.X, heartCy + radius + Px(12f), MatchContent.OwnName, nameCol);
            CenterText(dl, rightPos.X, heartCy + radius + Px(12f), MatchContent.PeerName, nameCol);
        }

        // Score that ticks up fast toward the high-score target.
        if (_settle > 0.01f)
        {
            var shown = (int)MathF.Round(AnimationHelper.Lerp(0f, ScoreTarget, EaseOutCubic(_scoreFill)));
            var scoreY = pos.Y + size.Y * 0.61f;
            using (UiFonts.H3?.Push())
            {
                CenterText(dl, cx, scoreY, Loc.T("deck.match_fx_arcade_score"), U32(Rgba(rim, _settle * 0.8f)));
            }
            using (UiFonts.H1?.Push())
            {
                var label = shown.ToString("D6");
                var w = ImGui.CalcTextSize(label).X;
                var x0 = cx - w * 0.5f;
                var sy = scoreY + Px(24f);
                var vtx = dl.VtxBuffer.Size;
                dl.AddText(new Vector2(x0, sy), U32(new Vector4(1f, 1f, 1f, _settle)), label);
                if (!reduce)
                {
                    GradientText(dl, vtx, x0, x0 + w, hot, cool, time * 2.4f);
                }
            }
        }

        DrawActionButtons(_router, pos, size, reduce ? 1f : _settle);
    }

    private void DrawPixelFrame(ImDrawListPtr dl, Vector2 center, float radius, float px, Vector4 rim,
        Vector4 hot, float alpha, bool reduce, float phase)
    {
        var half = radius + px * 1.4f;
        var tl = center - new Vector2(half, half);
        var br = center + new Vector2(half, half);
        var step = px;
        var time = (float)ImGui.GetTime();
        var marchA = reduce ? 0.85f : 0.55f + 0.45f * MathF.Sin(time * 4f + phase);

        for (float x = tl.X; x < br.X; x += step * 2f)
        {
            DrawCell(dl, new Vector2(x, tl.Y - step), px, U32(Rgba(rim, alpha * marchA)));
            DrawCell(dl, new Vector2(x + step, br.Y), px, U32(Rgba(hot, alpha * marchA)));
        }
        for (float y = tl.Y; y < br.Y; y += step * 2f)
        {
            DrawCell(dl, new Vector2(tl.X - step, y), px, U32(Rgba(hot, alpha * marchA)));
            DrawCell(dl, new Vector2(br.X, y + step), px, U32(Rgba(rim, alpha * marchA)));
        }
    }

    private static void DrawCell(ImDrawListPtr dl, Vector2 min, float px, uint col)
    {
        dl.AddRectFilled(min, min + new Vector2(px - Px(0.5f), px - Px(0.5f)), col);
    }

    private static byte[] GlyphFor(char ch)
    {
        switch (ch)
        {
            case 'A':
                return Glyphs[0];
            case 'M':
                return Glyphs[1];
            case 'T':
                return Glyphs[2];
            case 'C':
                return Glyphs[3];
            case 'H':
                return Glyphs[4];
            default:
                return Glyphs[5];
        }
    }

    private static float EaseOutCubic(float x)
    {
        x = Math.Clamp(x, 0f, 1f);
        var inv = 1f - x;
        return 1f - inv * inv * inv;
    }
}