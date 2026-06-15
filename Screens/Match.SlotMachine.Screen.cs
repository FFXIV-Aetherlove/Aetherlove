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

/// <summary>Match effect — Slot Machine: a gilded Vegas cabinet whose three reels
/// spin in a vertical blur of symbols then snap to the left avatar, a heart, and the right avatar;
/// "JACKPOT!" flashes, marquee bulbs chase around the frame, and gold coins rain and bounce.
///</summary>
public sealed class MatchSlotMachineScreen : IMatchEffect
{
    private readonly ScreenRouter _router;

    private float _spin;
    private float _settle;
    private readonly ConfettiBurst _confetti = new();
    private readonly Random _rng = new();

    private const int CoinCount = 22;
    private readonly Coin[] _coins = new Coin[CoinCount];
    private bool _ready;

    private struct Coin
    {
        public float Nx;
        public float StartDelay;
        public float Vy;
        public float Bounce;
        public float Phase;
        public float R;
    }

    public MatchSlotMachineScreen(ScreenRouter router)
    {
        _router = router;
    }

    public void OnShow()
    {
        _spin = 0f;
        _settle = 0f;
        _confetti.Reset();
        for (int i = 0; i < _coins.Length; i++)
        {
            _coins[i] = new Coin
            {
                Nx = 0.10f + _rng.NextSingle() * 0.80f,
                StartDelay = _rng.NextSingle() * 0.6f,
                Vy = 0.9f + _rng.NextSingle() * 0.7f,
                Bounce = 0.55f + _rng.NextSingle() * 0.30f,
                Phase = _rng.NextSingle() * MathF.Tau,
                R = Px(5f) + _rng.NextSingle() * Px(4f),
            };
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
            _spin = 1f;
            _settle = 1f;
        }
        else
        {
            AnimationHelper.ClampedProgress(ref _spin, dt, 0.62f, forward: true);
            if (_spin > 0.78f)
            {
                AnimationHelper.ClampedProgress(ref _settle, dt, 1.5f, forward: true);
            }
        }

        var t = ThemeService.Current;
        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var dl = ImGui.GetWindowDrawList();
        var cx = pos.X + size.X * 0.5f;

        DrawBackdrop(dl, pos, size, t, time, reduce);

        var cabTopY = pos.Y + size.Y * 0.215f;
        var cabBotY = pos.Y + size.Y - Px(106f);
        var cabHalfW = size.X * 0.40f;
        var cabMin = new Vector2(cx - cabHalfW, cabTopY);
        var cabMax = new Vector2(cx + cabHalfW, cabBotY);

        DrawCabinet(dl, cabMin, cabMax, t, time, reduce);

        var avatarR = Px(40f);
        var reelGap = Px(8f);
        var reelTop = cabMin.Y + Px(40f);
        var reelBot = cabMax.Y - Px(40f);
        var reelW = (cabMax.X - cabMin.X - Px(40f) - reelGap * 2f) / 3f;
        var reelH = reelBot - reelTop;
        var reelStartX = cabMin.X + Px(20f);

        for (int r = 0; r < 3; r++)
        {
            var rMin = new Vector2(reelStartX + r * (reelW + reelGap), reelTop);
            var rMax = new Vector2(rMin.X + reelW, reelBot);
            DrawReel(dl, r, rMin, rMax, avatarR, t, time, reduce);
        }

        var leftCx = reelStartX + reelW * 0.5f;
        var rightCx = reelStartX + 2f * (reelW + reelGap) + reelW * 0.5f;
        var nameY = cabMax.Y + Px(12f);
        var nameCol = U32(new Vector4(1f, 0.94f, 0.72f, _settle));
        using (UiFonts.H3?.Push())
        {
            CenterText(dl, leftCx, nameY, MatchContent.OwnName, nameCol);
            CenterText(dl, rightCx, nameY, MatchContent.PeerName, nameCol);
        }

        DrawJackpot(dl, pos, size, cx, t, time, reduce);

        if (_settle > 0.01f && !reduce)
        {
            _confetti.Draw(pos, pos + size);
        }

        DrawActionButtons(_router, pos, size, reduce ? 1f : _settle);
    }

    private static readonly Vector4 GoldHi = new(1.00f, 0.90f, 0.52f, 1f);
    private static readonly Vector4 GoldMid = new(0.86f, 0.66f, 0.22f, 1f);
    private static readonly Vector4 GoldLo = new(0.45f, 0.30f, 0.07f, 1f);
    private static readonly Vector4 CoinEdge = new(0.55f, 0.38f, 0.08f, 1f);

    private static void DrawBackdrop(ImDrawListPtr dl, Vector2 pos, Vector2 size, ThemeDefinition t,
        float time, bool reduce)
    {
        var top = U32(new Vector4(0.16f, 0.03f, 0.06f, 1f));
        var bottom = U32(new Vector4(0.05f, 0.01f, 0.03f, 1f));
        dl.AddRectFilledMultiColor(pos, pos + size, top, top, bottom, bottom);

        var center = pos + size * 0.5f;
        const int rays = 14;
        var spin = reduce ? 0f : time * 0.25f;
        var reach = size.X * 0.95f;
        for (int i = 0; i < rays; i++)
        {
            var a0 = i / (float)rays * MathF.Tau + spin;
            var a1 = (i + 0.5f) / rays * MathF.Tau + spin;
            var p0 = center + new Vector2(MathF.Cos(a0), MathF.Sin(a0)) * reach;
            var p1 = center + new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * reach;
            var col = (i & 1) == 0 ? Rgba(t.SecondaryStart, 0.06f) : Rgba(t.AccentDark, 0.05f);
            dl.AddTriangleFilled(center, p0, p1, U32(col));
        }

        const int glowSteps = 5;
        for (int i = glowSteps; i >= 1; i--)
        {
            var rr = size.X * 0.20f * i;
            var a = 0.05f * (1f - (float)i / (glowSteps + 1));
            dl.AddCircleFilled(center, rr, U32(Rgba(t.SecondaryEnd, a)), 48);
        }
    }

    /// <summary>Draws the gilded cabinet: a layered gold bezel around a dark felt face, with a chase of
    /// alternating marquee bulbs around the perimeter.</summary>
    private static void DrawCabinet(ImDrawListPtr dl, Vector2 min, Vector2 max, ThemeDefinition t,
        float time, bool reduce)
    {
        var rounding = Px(18f);
        var outer = new Vector2(Px(14f), Px(14f));
        var bezelMin = min - outer;
        var bezelMax = max + outer;

        dl.AddRectFilled(bezelMin + new Vector2(Px(3f), Px(5f)), bezelMax + new Vector2(Px(3f), Px(7f)),
            U32(new Vector4(0f, 0f, 0f, 0.45f)), rounding + Px(4f));

        dl.AddRectFilledMultiColor(bezelMin, bezelMax,
            U32(GoldHi), U32(GoldMid), U32(GoldLo), U32(GoldMid));
        dl.AddRect(bezelMin, bezelMax, U32(GoldLo), rounding + Px(4f), ImDrawFlags.RoundCornersAll, Px(2f));

        dl.AddRectFilled(min, max, U32(new Vector4(0.07f, 0.02f, 0.05f, 1f)), rounding);
        dl.AddRect(min, max, U32(new Vector4(0.02f, 0.0f, 0.01f, 1f)), rounding, ImDrawFlags.RoundCornersAll, Px(2f));
        dl.AddRectFilledMultiColor(min, new Vector2(max.X, min.Y + Px(26f)),
            U32(new Vector4(0.20f, 0.05f, 0.10f, 0.6f)), U32(new Vector4(0.20f, 0.05f, 0.10f, 0.6f)),
            U32(new Vector4(0.07f, 0.02f, 0.05f, 0f)), U32(new Vector4(0.07f, 0.02f, 0.05f, 0f)));

        DrawBulbs(dl, bezelMin, bezelMax, t, time, reduce);
    }

    private static void DrawBulbs(ImDrawListPtr dl, Vector2 min, Vector2 max, ThemeDefinition t,
        float time, bool reduce)
    {
        var bulbR = Px(4.5f);
        var inset = Px(8f);
        var stepX = Px(26f);
        var stepY = Px(26f);
        var w = max.X - min.X - inset * 2f;
        var h = max.Y - min.Y - inset * 2f;
        var nx = Math.Max(2, (int)(w / stepX));
        var ny = Math.Max(2, (int)(h / stepY));

        int idx = 0;
        void Bulb(Vector2 c)
        {
            var on = reduce ? (idx & 1) == 0 : MathF.Sin(time * 6f - idx * 0.9f) > 0f;
            var col = on
                ? Vector4.Lerp(t.SecondaryStart, t.SecondaryEnd, 0.5f + 0.5f * MathF.Sin(idx * 0.7f))
                : new Vector4(0.30f, 0.24f, 0.10f, 1f);
            if (on && !reduce)
            {
                dl.AddCircleFilled(c, bulbR * 2.1f, U32(Rgba(col, 0.30f)), 12);
            }
            dl.AddCircleFilled(c, bulbR, U32(col), 12);
            dl.AddCircleFilled(c - new Vector2(bulbR * 0.3f, bulbR * 0.3f), bulbR * 0.32f,
                U32(new Vector4(1f, 1f, 1f, on ? 0.8f : 0.25f)), 8);
            idx++;
        }

        var x0 = min.X + inset;
        var y0 = min.Y + inset;
        var dx = w / nx;
        var dy = h / ny;
        for (int i = 0; i < nx; i++)
        {
            Bulb(new Vector2(x0 + dx * i, y0));
        }
        for (int i = 0; i < ny; i++)
        {
            Bulb(new Vector2(max.X - inset, y0 + dy * i));
        }
        for (int i = nx; i > 0; i--)
        {
            Bulb(new Vector2(x0 + dx * i, max.Y - inset));
        }
        for (int i = ny; i > 0; i--)
        {
            Bulb(new Vector2(x0, y0 + dy * i));
        }
    }

    /// <summary>Draws one reel: a recessed slot that scrolls a blurred ribbon of symbols while spinning,
    /// then snaps to its final face — the left avatar (reel 0), a heart (reel 1), or the right avatar
    /// (reel 2). A central pay-line band marks the landed symbol.</summary>
    private void DrawReel(ImDrawListPtr dl, int reelIndex, Vector2 min, Vector2 max, float avatarR,
        ThemeDefinition t, float time, bool reduce)
    {
        var rounding = Px(7f);
        var center = (min + max) * 0.5f;
        var midY = center.Y;

        dl.AddRectFilled(min, max, U32(new Vector4(0.96f, 0.95f, 0.91f, 1f)), rounding);
        dl.AddRectFilledMultiColor(min, new Vector2(max.X, min.Y + (max.Y - min.Y) * 0.32f),
            U32(new Vector4(0f, 0f, 0f, 0.35f)), U32(new Vector4(0f, 0f, 0f, 0.35f)),
            U32(new Vector4(0f, 0f, 0f, 0f)), U32(new Vector4(0f, 0f, 0f, 0f)));
        dl.AddRectFilledMultiColor(new Vector2(min.X, max.Y - (max.Y - min.Y) * 0.32f), max,
            U32(new Vector4(0f, 0f, 0f, 0f)), U32(new Vector4(0f, 0f, 0f, 0f)),
            U32(new Vector4(0f, 0f, 0f, 0.35f)), U32(new Vector4(0f, 0f, 0f, 0.35f)));

        dl.PushClipRect(min, max, true);

        var reelStop = Math.Clamp((_spin - reelIndex * 0.16f) / (1f - 0.32f), 0f, 1f);
        var spinning = reelStop < 0.999f && !reduce;

        if (spinning)
        {
            var speed = AnimationHelper.Lerp(1900f, 60f, EaseOutQuint(reelStop));
            var scroll = (time * speed + reelIndex * 53f) % (max.Y - min.Y + Px(60f));
            var blur = AnimationHelper.Lerp(0.85f, 0.0f, reelStop);
            DrawSpinningSymbols(dl, reelIndex, min, max, scroll, blur, t, time);
        }
        else
        {
            DrawReelFace(dl, reelIndex, center, avatarR, t, time, reduce);
        }

        dl.PopClipRect();

        var lineCol = U32(new Vector4(0.86f, 0.16f, 0.22f, 0.85f));
        dl.AddLine(new Vector2(min.X, midY), new Vector2(max.X, midY), lineCol, Px(2f));

        dl.AddRect(min, max, U32(GoldLo), rounding, ImDrawFlags.RoundCornersAll, Px(2.5f));
        dl.AddRect(min + new Vector2(Px(1.5f), Px(1.5f)), max - new Vector2(Px(1.5f), Px(1.5f)),
            U32(new Vector4(1f, 1f, 1f, 0.35f)), rounding, ImDrawFlags.RoundCornersAll, Px(1f));
    }

    private void DrawSpinningSymbols(ImDrawListPtr dl, int reelIndex, Vector2 min, Vector2 max,
        float scroll, float blur, ThemeDefinition t, float time)
    {
        var slotH = Px(54f);
        var sw = max.X - min.X;
        var symR = MathF.Min(sw, slotH) * 0.34f;
        var span = max.Y - min.Y + slotH * 2f;
        var count = (int)(span / slotH) + 2;

        for (int i = -1; i < count; i++)
        {
            var y = min.Y - slotH + ((i * slotH - scroll) % span + span) % span;
            var c = new Vector2(min.X + sw * 0.5f, y);
            var kind = (((int)MathF.Round(y / slotH) + reelIndex * 3) % 5 + 5) % 5;

            if (blur > 0.04f)
            {
                int trails = 3;
                for (int b = 1; b <= trails; b++)
                {
                    var off = blur * slotH * 0.5f * b / trails;
                    var a = 0.18f * (1f - (float)b / (trails + 1));
                    DrawSymbol(dl, kind, c - new Vector2(0f, off), symR, t, a);
                    DrawSymbol(dl, kind, c + new Vector2(0f, off), symR, t, a);
                }
            }
            DrawSymbol(dl, kind, c, symR, t, 1f);
        }
    }

    private static void DrawSymbol(ImDrawListPtr dl, int kind, Vector2 c, float r, ThemeDefinition t, float alpha)
    {
        switch (kind)
        {
            case 0:
            {
                DrawHeart(dl, c, r * 1.15f, U32(Rgba(new Vector4(0.93f, 0.22f, 0.40f, 1f), alpha)));
                break;
            }
            case 1:
            {
                var d = U32(Rgba(t.SecondaryStart, alpha));
                dl.AddQuadFilled(c + new Vector2(0f, -r), c + new Vector2(r * 0.72f, 0f),
                    c + new Vector2(0f, r), c + new Vector2(-r * 0.72f, 0f), d);
                break;
            }
            case 2:
            {
                DrawStar(dl, c, r, U32(Rgba(GoldHi, alpha)));
                break;
            }
            case 3:
            {
                dl.AddCircleFilled(c, r, U32(Rgba(GoldMid, alpha)), 20);
                dl.AddCircle(c, r, U32(Rgba(CoinEdge, alpha)), 20, Px(1.5f));
                break;
            }
            default:
            {
                dl.AddRectFilled(c - new Vector2(r * 0.8f, r * 0.8f), c + new Vector2(r * 0.8f, r * 0.8f),
                    U32(Rgba(t.SecondaryEnd, alpha)), Px(3f));
                break;
            }
        }
    }

    private void DrawReelFace(ImDrawListPtr dl, int reelIndex, Vector2 center, float avatarR,
        ThemeDefinition t, float time, bool reduce)
    {
        var pop = reduce ? 1f : 0.55f + 0.45f * Smooth01(_settle);
        var landGlow = reduce ? 0.5f : 0.5f + 0.5f * MathF.Sin(time * 4f);

        if (reelIndex == 1)
        {
            var beatR = avatarR * (reduce ? 1f : 0.96f + 0.06f * MathF.Sin(time * 5f));
            dl.AddCircleFilled(center, beatR * 1.25f, U32(Rgba(new Vector4(0.95f, 0.25f, 0.42f, 1f), 0.18f)), 32);
            DrawHeart(dl, center, beatR * pop, U32(new Vector4(0.95f, 0.22f, 0.40f, 1f)));
            DrawHeart(dl, center - new Vector2(beatR * 0.22f, beatR * 0.28f), beatR * 0.34f * pop,
                U32(new Vector4(1f, 0.75f, 0.82f, 0.85f)));
            return;
        }

        var rr = avatarR * pop;
        dl.AddCircleFilled(center, rr + Px(5f), U32(Rgba(t.SecondaryEnd, 0.25f * landGlow)), 40);
        var tex = reelIndex == 0 ? MatchContent.OwnAvatar : MatchContent.PeerAvatar;
        Avatar(dl, center, rr, tex, 0, 0f);
        if (reduce)
        {
            dl.AddCircle(center, rr + Px(2f), U32(GoldHi), 48, Px(2f));
        }
        else
        {
            var ph = time * 1.8f * (reelIndex == 0 ? 1f : -1f);
            GradientRing(dl, center, rr + Px(2f), Px(2.5f), GoldHi, t.SecondaryEnd, ph);
        }
    }

    private void DrawJackpot(ImDrawListPtr dl, Vector2 pos, Vector2 size, float cx, ThemeDefinition t,
        float time, bool reduce)
    {
        if (_settle <= 0.01f && !reduce)
        {
            return;
        }

        var flash = reduce ? 1f : 0.6f + 0.4f * MathF.Sin(time * 8f);
        var alpha = reduce ? 1f : _settle;
        var y = pos.Y + size.Y * 0.085f;

        using (UiFonts.H1?.Push())
        {
            var label = Loc.T("deck.match_fx_slot_title");
            var w = ImGui.CalcTextSize(label).X;
            var x0 = cx - w * 0.5f;

            if (!reduce)
            {
                dl.AddText(new Vector2(x0 + Px(2f), y + Px(2f)), U32(new Vector4(0f, 0f, 0f, 0.5f * alpha)), label);
            }
            var vtx = dl.VtxBuffer.Size;
            dl.AddText(new Vector2(x0, y), U32(Rgba(GoldHi, alpha * flash)), label);
            if (reduce)
            {
                GradientText(dl, vtx, x0, x0 + w, GoldHi, GoldMid, 0f);
            }
            else
            {
                GradientText(dl, vtx, x0, x0 + w, t.SecondaryStart, GoldHi, time * 3f);
            }
        }

        using (UiFonts.H2?.Push())
        {
            CenterText(dl, cx, pos.Y + size.Y * 0.16f, Loc.T("deck.match_fx_slot"),
                U32(Rgba(t.AccentLight, alpha * 0.9f)));
        }
    }

    private void DrawCoins(ImDrawListPtr dl, Vector2 pos, Vector2 size, float time)
    {
        dl.PushClipRect(pos, pos + size, true);
        var t0 = _settle;
        var floorY = pos.Y + size.Y - Px(78f);
        foreach (var coin in _coins)
        {
            var local = t0 - coin.StartDelay;
            if (local <= 0f)
            {
                continue;
            }
            var fall = local * coin.Vy * size.Y * 0.9f;
            var x = pos.X + coin.Nx * size.X;
            var topY = pos.Y + Px(40f);
            var y = topY + fall;

            if (y > floorY)
            {
                var over = y - floorY;
                var damp = MathF.Exp(-over * 0.012f);
                y = floorY - MathF.Abs(MathF.Sin(over * 0.05f)) * Px(40f) * damp * coin.Bounce;
            }

            var wob = MathF.Abs(MathF.Sin(time * 6f + coin.Phase));
            var rx = coin.R * (0.35f + 0.65f * wob);
            DrawCoin(dl, new Vector2(x, y), rx, coin.R);
        }
        dl.PopClipRect();
    }

    private static void DrawCoin(ImDrawListPtr dl, Vector2 c, float rx, float ry)
    {
        var a = new Vector2(c.X, c.Y - ry);
        var b = new Vector2(c.X + rx, c.Y);
        var d = new Vector2(c.X, c.Y + ry);
        var e = new Vector2(c.X - rx, c.Y);
        dl.AddQuadFilled(a, b, d, e, U32(GoldMid));
        var hi = new Vector2(c.X - rx * 0.35f, c.Y);
        dl.AddQuadFilled(a, new Vector2(c.X, c.Y - ry * 0.3f), d, hi, U32(GoldHi));
        dl.AddLine(a, d, U32(CoinEdge), Px(1f));
    }

    private static void DrawHeart(ImDrawListPtr dl, Vector2 c, float r, uint col)
    {
        var lobeR = r * 0.46f;
        dl.AddCircleFilled(c + new Vector2(-lobeR * 0.9f, -r * 0.30f), lobeR, col, 20);
        dl.AddCircleFilled(c + new Vector2(lobeR * 0.9f, -r * 0.30f), lobeR, col, 20);
        dl.AddTriangleFilled(
            c + new Vector2(-r * 0.86f, -r * 0.12f),
            c + new Vector2(r * 0.86f, -r * 0.12f),
            c + new Vector2(0f, r * 0.86f), col);
    }

    private static void DrawStar(ImDrawListPtr dl, Vector2 c, float r, uint col)
    {
        Span<Vector2> outer = stackalloc Vector2[5];
        Span<Vector2> inner = stackalloc Vector2[5];
        for (int i = 0; i < 5; i++)
        {
            var ao = -MathF.PI / 2f + i * MathF.Tau / 5f;
            var ai = ao + MathF.Tau / 10f;
            outer[i] = c + new Vector2(MathF.Cos(ao), MathF.Sin(ao)) * r;
            inner[i] = c + new Vector2(MathF.Cos(ai), MathF.Sin(ai)) * r * 0.45f;
        }
        for (int i = 0; i < 5; i++)
        {
            dl.AddTriangleFilled(c, outer[i], inner[i], col);
            dl.AddTriangleFilled(c, inner[i], outer[(i + 1) % 5], col);
        }
    }

    private static float Smooth01(float x)
    {
        x = Math.Clamp(x, 0f, 1f);
        return x * x * (3f - 2f * x);
    }

    private static float EaseOutQuint(float x)
    {
        x = Math.Clamp(x, 0f, 1f);
        return 1f - MathF.Pow(1f - x, 5f);
    }
}