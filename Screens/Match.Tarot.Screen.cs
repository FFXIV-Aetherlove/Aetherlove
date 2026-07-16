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

/// <summary>Match effect - Tarot Lovers: two tarot cards flip open, revealing the avatars as "The Lovers".</summary>
public sealed class MatchTarotScreen : IMatchEffect
{
    private readonly ScreenRouter _router;

    private float _flip;
    private float _settle;
    private readonly Random _rng = new();
    private readonly (float nx, float ny, float r, float ph, float sp)[] _sparkles = new (float, float, float, float, float)[26];
    private bool _ready;

    private static readonly string[] Zodiac =
    {
        "♈", "♉", "♊", "♋", "♌", "♍",
        "♎", "♏", "♐", "♑", "♒", "♓",
    };

    public MatchTarotScreen(ScreenRouter router)
    {
        _router = router;
    }

    public void OnShow()
    {
        _flip = 0f;
        _settle = 0f;
        for (int i = 0; i < _sparkles.Length; i++)
        {
            _sparkles[i] = (_rng.NextSingle(), _rng.NextSingle(),
                Px(1.0f) + _rng.NextSingle() * Px(2.4f),
                _rng.NextSingle() * MathF.Tau,
                0.5f + _rng.NextSingle() * 1.4f);
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
            _flip = 1f;
            _settle = 1f;
        }
        else
        {
            AnimationHelper.ClampedProgress(ref _flip, dt, 1.1f, forward: true);
            if (_flip > 0.62f)
            {
                AnimationHelper.ClampedProgress(ref _settle, dt, 1.5f, forward: true);
            }
        }

        var t = ThemeService.Current;
        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var dl = ImGui.GetWindowDrawList();
        var center = pos + size * 0.5f;
        var cx = center.X;

        var gold = new Vector4(0.93f, 0.80f, 0.42f, 1f);
        var goldDeep = new Vector4(0.66f, 0.48f, 0.18f, 1f);
        var candle = new Vector4(0.98f, 0.62f, 0.30f, 1f);

        // Candle-warm dusk backdrop with a soft glow rising from the cards.
        dl.AddRectFilledMultiColor(pos, pos + size,
            U32(new Vector4(0.10f, 0.05f, 0.10f, 1f)), U32(new Vector4(0.10f, 0.05f, 0.10f, 1f)),
            U32(new Vector4(0.04f, 0.02f, 0.05f, 1f)), U32(new Vector4(0.04f, 0.02f, 0.05f, 1f)));
        var glowCenter = new Vector2(cx, center.Y - Px(6f));
        var glowPulse = reduce ? 0.85f : 0.78f + 0.18f * MathF.Sin(time * 1.4f);
        const int glowSteps = 7;
        for (int i = glowSteps; i >= 1; i--)
        {
            var rr = size.X * 0.16f * i;
            var a = 0.06f * (1f - (float)i / (glowSteps + 1)) * glowPulse;
            dl.AddCircleFilled(glowCenter, rr, U32(Rgba(candle, a)), 48);
        }

        // Faint zodiac ring of symbols slowly turning behind the cards.
        var ringR = MathF.Min(size.X, size.Y) * 0.40f;
        var ringRot = reduce ? 0f : time * 0.10f;
        dl.AddCircle(center, ringR + Px(11f), U32(Rgba(gold, 0.10f)), 80, Px(1f));
        dl.AddCircle(center, ringR - Px(11f), U32(Rgba(gold, 0.07f)), 80, Px(1f));
        using (UiFonts.Body?.Push())
        {
            for (int i = 0; i < Zodiac.Length; i++)
            {
                var ang = i / (float)Zodiac.Length * MathF.Tau + ringRot - MathF.PI * 0.5f;
                var p = center + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * ringR;
                var twinkle = reduce ? 0.30f : 0.20f + 0.16f * MathF.Sin(time * 1.8f + i);
                var glyph = Zodiac[i];
                var gw = ImGui.CalcTextSize(glyph);
                dl.AddText(p - gw * 0.5f, U32(Rgba(gold, twinkle)), glyph);
            }
        }

        // Two tarot cards flip open by scaling their width 0 -> full around each centre.
        var radius = Px(42f);
        var cardGap = Px(14f);
        var cardW = radius * 2f + Px(22f);
        var cardH = radius * 2f + Px(54f);
        var leftCenter = new Vector2(cx - cardW * 0.5f - cardGap * 0.5f, center.Y);
        var rightCenter = new Vector2(cx + cardW * 0.5f + cardGap * 0.5f, center.Y);

        var openL = reduce ? 1f : EaseOutBack(Smooth01(_flip / 0.62f));
        var openR = reduce ? 1f : EaseOutBack(Smooth01((_flip - 0.10f) / 0.62f));

        DrawTarotCard(dl, leftCenter, cardW, cardH, radius, openL, gold, goldDeep, candle,
            MatchContent.OwnName, MatchContent.OwnAvatar, time, reduce);
        DrawTarotCard(dl, rightCenter, cardW, cardH, radius, openR, gold, goldDeep, candle,
            MatchContent.PeerName, MatchContent.PeerAvatar, time, reduce);

        // Title + subtitle rise as the reading settles.
        if (_settle > 0.01f)
        {
            using (UiFonts.H1?.Push())
            {
                var label = Loc.T("deck.match_fx_tarot_title");
                var w = ImGui.CalcTextSize(label).X;
                var x0 = cx - w * 0.5f;
                var vtx = dl.VtxBuffer.Size;
                dl.AddText(new Vector2(x0, pos.Y + size.Y * 0.135f), U32(Rgba(gold, _settle)), label);
                if (!reduce)
                {
                    GradientText(dl, vtx, x0, x0 + w, gold, candle, time * 1.2f);
                }
            }

            using (UiFonts.H3?.Push())
            {
                CenterText(dl, cx, pos.Y + size.Y * 0.205f, Loc.T("deck.match_its_a_match"),
                    U32(Rgba(new Vector4(1f, 0.94f, 0.86f, 1f), _settle)));
            }

            using (UiFonts.Body?.Push())
            {
                CenterText(dl, cx, pos.Y + size.Y * 0.255f, Loc.T("deck.match_fx_tarot"),
                    U32(Rgba(gold, _settle * 0.78f)));
            }
        }

        // Drifting gold sparkles float upward over everything.
        if (!reduce)
        {
            foreach (var s in _sparkles)
            {
                var rise = (time * s.sp * 0.06f) % 1f;
                var py = pos.Y + ((s.ny - rise + 1f) % 1f) * size.Y;
                var px = pos.X + s.nx * size.X + MathF.Sin(time * 0.9f + s.ph) * Px(8f);
                var twinkle = 0.30f + 0.45f * (0.5f + 0.5f * MathF.Sin(time * 3f + s.ph));
                DrawSparkle(dl, new Vector2(px, py), s.r * (0.6f + 0.4f * twinkle),
                    U32(Rgba(gold, twinkle * 0.8f)));
            }
        }

        DrawActionButtons(_router, pos, size, reduce ? 1f : _settle);
    }

    /// <summary>One tarot card; the avatar is revealed as the card's width scales from a seam to fully open.</summary>
    private static void DrawTarotCard(ImDrawListPtr dl, Vector2 cardCenter, float fullW, float fullH,
        float avatarR, float open, Vector4 gold, Vector4 goldDeep, Vector4 candle, string name,
        ISharedImmediateTexture? avatar, float time, bool reduce)
    {
        open = Math.Clamp(open, 0f, 1f);
        var halfW = MathF.Max(Px(1.5f), fullW * 0.5f * open);
        var halfH = fullH * 0.5f;
        var min = new Vector2(cardCenter.X - halfW, cardCenter.Y - halfH);
        var max = new Vector2(cardCenter.X + halfW, cardCenter.Y + halfH);
        var round = Px(10f) * Math.Clamp(open * 1.4f, 0f, 1f);

        // Card face: warm parchment, darker toward the edges.
        dl.AddRectFilledMultiColor(min, max,
            U32(new Vector4(0.18f, 0.10f, 0.14f, 1f)), U32(new Vector4(0.18f, 0.10f, 0.14f, 1f)),
            U32(new Vector4(0.12f, 0.06f, 0.10f, 1f)), U32(new Vector4(0.12f, 0.06f, 0.10f, 1f)));
        dl.AddRectFilled(min, max, U32(Rgba(candle, 0.06f)), round);

        // Double gold border.
        dl.AddRect(min, max, U32(Rgba(gold, 0.95f)), round, ImDrawFlags.RoundCornersAll, Px(2.4f));
        var inset = Px(5f);
        if (halfW > inset + Px(2f))
        {
            var imin = min + new Vector2(inset, inset);
            var imax = max - new Vector2(inset, inset);
            dl.AddRect(imin, imax, U32(Rgba(goldDeep, 0.9f)), MathF.Max(0f, round - inset),
                ImDrawFlags.RoundCornersAll, Px(1.2f));
        }

        // The reveal lives only inside the opening card; clip to the inner frame.
        var clipMin = min + new Vector2(inset, inset);
        var clipMax = max - new Vector2(inset, inset);
        dl.PushClipRect(clipMin, clipMax, true);

        if (open > 0.35f)
        {
            var avatarCenter = new Vector2(cardCenter.X, cardCenter.Y - Px(8f));
            var rimPhase = reduce ? 0f : time * 1.3f;
            if (reduce)
            {
                Avatar(dl, avatarCenter, avatarR, avatar, 0, 0f);
                dl.AddCircle(avatarCenter, avatarR + Px(3f), U32(Rgba(gold, 0.95f)), 64, Px(2f));
            }
            else
            {
                Avatar(dl, avatarCenter, avatarR, avatar, 0, 0f);
                GradientRing(dl, avatarCenter, avatarR + Px(3f), Px(2.4f), gold, candle, rimPhase);
            }

            // A small candle-lit heart sigil under the avatar.
            var heart = new Vector2(cardCenter.X, cardCenter.Y + avatarR + Px(8f));
            DrawHeartGlyph(dl, heart, Px(6f), U32(Rgba(candle, 0.9f)));

            using (UiFonts.Body?.Push())
            {
                var nw = ImGui.CalcTextSize(name);
                if (nw.X < (clipMax.X - clipMin.X))
                {
                    dl.AddText(new Vector2(cardCenter.X - nw.X * 0.5f, cardCenter.Y + avatarR + Px(18f)),
                        U32(Rgba(gold, 0.95f)), name);
                }
            }
        }
        else
        {
            // Card-back arcane mark while it is still mostly closed.
            DrawHeartGlyph(dl, cardCenter, Px(10f), U32(Rgba(gold, 0.45f)));
        }

        dl.PopClipRect();

        // Corner flourishes on top of the frame once the card has real width.
        if (halfW > inset + Px(6f))
        {
            var c = Px(9f);
            DrawCornerFlourish(dl, min + new Vector2(inset, inset), c, c, gold);
            DrawCornerFlourish(dl, new Vector2(max.X - inset, min.Y + inset), -c, c, gold);
            DrawCornerFlourish(dl, new Vector2(min.X + inset, max.Y - inset), c, -c, gold);
            DrawCornerFlourish(dl, max - new Vector2(inset, inset), -c, -c, gold);
        }
    }

    private static void DrawCornerFlourish(ImDrawListPtr dl, Vector2 corner, float dx, float dy, Vector4 gold)
    {
        var col = U32(Rgba(gold, 0.85f));
        dl.AddLine(corner, corner + new Vector2(dx, 0f), col, Px(1.4f));
        dl.AddLine(corner, corner + new Vector2(0f, dy), col, Px(1.4f));
        dl.AddCircleFilled(corner + new Vector2(dx, dy) * 0.55f, Px(1.4f), col, 8);
    }

    private static void DrawHeartGlyph(ImDrawListPtr dl, Vector2 c, float s, uint col)
    {
        var lobe = s * 0.5f;
        dl.AddCircleFilled(new Vector2(c.X - lobe, c.Y - lobe * 0.5f), lobe, col, 14);
        dl.AddCircleFilled(new Vector2(c.X + lobe, c.Y - lobe * 0.5f), lobe, col, 14);
        dl.AddTriangleFilled(
            new Vector2(c.X - s, c.Y - lobe * 0.25f),
            new Vector2(c.X + s, c.Y - lobe * 0.25f),
            new Vector2(c.X, c.Y + s), col);
    }

    private static void DrawSparkle(ImDrawListPtr dl, Vector2 c, float r, uint col)
    {
        dl.AddLine(new Vector2(c.X - r, c.Y), new Vector2(c.X + r, c.Y), col, Px(1.2f));
        dl.AddLine(new Vector2(c.X, c.Y - r), new Vector2(c.X, c.Y + r), col, Px(1.2f));
        var d = r * 0.5f;
        dl.AddLine(new Vector2(c.X - d, c.Y - d), new Vector2(c.X + d, c.Y + d), col, Px(0.8f));
        dl.AddLine(new Vector2(c.X - d, c.Y + d), new Vector2(c.X + d, c.Y - d), col, Px(0.8f));
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