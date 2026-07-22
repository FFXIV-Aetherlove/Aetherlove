using System;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherLove.Widgets;

/// <summary>Full-phone celebration after the Patreon link flow completes. The flow finishes off the UI
/// thread, so the scene is queued and drawn by the main window.</summary>
public sealed class SupporterThanksScene
{
    private static readonly Vector4 Gold = new(1.00f, 0.80f, 0.28f, 1f);

    private volatile bool _pending;
    private bool _open;
    private double _openedAt;

    public SupporterThanksScene(Services.Patreon.PatreonLinkFlow flow)
    {
        flow.LinkCompleted += () => _pending = true;
    }

    public void Draw(Vector2 winPos, Vector2 winSize)
    {
        if (_pending)
        {
            _pending = false;
            _open = true;
            _openedAt = ImGui.GetTime();
        }
        if (!_open)
        {
            return;
        }
        if (ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            _open = false;
            return;
        }

        // Child windows render above the parent draw list, so the scene needs its own last-submitted
        // overlay child to sit on top.
        ImGui.SetCursorScreenPos(winPos);
        using var pad = Dalamud.Interface.Utility.Raii.ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        using var overlay = Dalamud.Interface.Utility.Raii.ImRaii.Child("##supThanksOverlay", winSize, false,
            ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoScrollbar
            | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoNav);
        if (!overlay.Success)
        {
            return;
        }

        var theme = ThemeService.Current;
        var tl = winPos + Px(theme.BezelLeft, theme.BezelTop);
        var br = winPos + winSize - Px(theme.BezelRight, theme.BezelBottom);
        var size = br - tl;
        var reduce = AccessibilityService.ReduceMotion;
        var time = reduce ? 0f : (float)(ImGui.GetTime() - _openedAt);
        var appear = reduce ? 1f : Math.Clamp(time / 0.5f, 0f, 1f);
        appear = 1f - (1f - appear) * (1f - appear);

        var dl = ImGui.GetWindowDrawList();
        dl.PushClipRect(tl, br, true);

        var washTop = ImGui.GetColorU32(Darken(Gold, 0.90f) with { W = appear });
        var washBottom = ImGui.GetColorU32(Darken(Gold, 0.55f) with { W = appear });
        dl.AddRectFilledMultiColor(tl, br, washTop, washTop, washBottom, washBottom);

        var starCenter = tl + new Vector2(size.X * 0.5f, size.Y * 0.34f);

        const int RayCount = 12;
        const float Slice = MathF.Tau / RayCount;
        var baseAngle = time * 0.15f;
        var reach = size.Length();
        for (var i = 0; i < RayCount; i++)
        {
            var a0 = baseAngle + i * Slice;
            var a1 = a0 + Slice * 0.42f;
            var rayCol = (i & 1) == 0 ? Gold : UiColors.Patreon;
            dl.AddTriangleFilled(
                starCenter,
                starCenter + new Vector2(MathF.Cos(a0), MathF.Sin(a0)) * reach,
                starCenter + new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * reach,
                ImGui.GetColorU32(rayCol with { W = 0.10f * appear }));
        }

        dl.AddCircleFilled(starCenter, size.X * 0.40f, ImGui.GetColorU32(Gold with { W = 0.12f * appear }), 48);
        var beat = reduce ? 0f : MathF.Sin(time * 2.4f) * 0.04f;
        var pop = reduce ? 1f : EaseOutBack(Math.Clamp(time / 0.7f, 0f, 1f));
        var starPx = Px(72f) * MathF.Max(0f, pop + beat);
        dl.AddCircleFilled(starCenter, starPx * 0.85f, ImGui.GetColorU32(Gold with { W = 0.20f * appear }), 48);
        IconDraw.AddCentered(dl, FontAwesomeIcon.Star, starPx, starCenter,
            ImGui.GetColorU32(Gold with { W = appear }));

        if (!reduce && time < 1.4f)
        {
            var bt = time / 1.4f;
            var eased = EaseOutCubic(bt);
            dl.AddCircle(starCenter, starPx * 0.9f + size.X * 0.55f * eased,
                ImGui.GetColorU32(Gold with { W = 0.35f * (1f - bt) }), 64, Px(2.5f));
            const int BurstCount = 10;
            for (var i = 0; i < BurstCount; i++)
            {
                var ang = i * (MathF.Tau / BurstCount) + 0.35f;
                var dist = starPx + size.X * 0.5f * eased;
                var pos = starCenter + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * dist;
                IconDraw.AddCentered(dl, FontAwesomeIcon.Star, Px(9f) * (1f - bt * 0.5f), pos,
                    ImGui.GetColorU32(Gold with { W = 1f - bt }));
            }
        }

        DrawSparkles(dl, tl, size, time, appear);

        var titleFontPtr = ImGui.GetFont();
        var titleSize = ImGui.GetFontSize();
        using (UiFonts.H1?.Push())
        {
            titleFontPtr = ImGui.GetFont();
            titleSize = ImGui.GetFontSize();
        }
        var title = Loc.T("settings.sup_thanks_title").ToUpperInvariant();
        var titleW = ImGui.CalcTextSize(title).X * (titleSize / ImGui.GetFontSize());
        var flankPx = titleSize * 0.5f;
        var maxTitleW = size.X - Px(24f) * 2f - (Px(8f) + flankPx) * 2f;
        if (titleW > maxTitleW)
        {
            titleSize *= maxTitleW / titleW;
            titleW = maxTitleW;
            flankPx = titleSize * 0.5f;
        }
        var titleY = starCenter.Y + Px(96f);
        var titleX = tl.X + (size.X - titleW) * 0.5f;
        var textA = (uint)(appear * 255f);
        dl.AddText(titleFontPtr, titleSize, new Vector2(titleX + Px(1.5f), titleY + Px(1.5f)),
            (textA * 2 / 3) << 24, title);
        dl.AddText(titleFontPtr, titleSize, new Vector2(titleX, titleY), (textA << 24) | 0x00FFFFFFu, title);
        IconDraw.AddCentered(dl, FontAwesomeIcon.Star, flankPx,
            new Vector2(titleX - Px(8f) - flankPx * 0.5f, titleY + titleSize * 0.5f),
            ImGui.GetColorU32(Gold with { W = appear }));
        IconDraw.AddCentered(dl, FontAwesomeIcon.Star, flankPx,
            new Vector2(titleX + titleW + Px(8f) + flankPx * 0.5f, titleY + titleSize * 0.5f),
            ImGui.GetColorU32(Gold with { W = appear }));

        var subFontPtr = ImGui.GetFont();
        var subSize = ImGui.GetFontSize();
        using (UiFonts.H3?.Push())
        {
            subFontPtr = ImGui.GetFont();
            subSize = ImGui.GetFontSize();
        }
        var sub = Loc.T("settings.sup_thanks_sub");
        var subW = ImGui.CalcTextSize(sub).X * (subSize / ImGui.GetFontSize());
        dl.AddText(subFontPtr, subSize,
            new Vector2(tl.X + (size.X - subW) * 0.5f, titleY + titleSize + Px(10f)),
            (textA << 24) | 0x00FFFFFFu, sub);

        var body = Loc.T("settings.sup_thanks_body");
        var bodyW = size.X * 0.78f;
        var bodySz = ImGui.CalcTextSize(body, false, bodyW);
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
            new Vector2(tl.X + (size.X - MathF.Min(bodySz.X, bodyW)) * 0.5f, titleY + titleSize + subSize + Px(26f)),
            ((textA * 9 / 10) << 24) | 0x00FFFFFFu, body, bodyW);

        var btnW = size.X * 0.58f;
        ImGui.SetCursorScreenPos(new Vector2(tl.X + (size.X - btnW) * 0.5f, br.Y - Px(64f)));
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.62f, 0.42f, 0.08f, 0.92f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.82f, 0.58f, 0.16f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.48f, 0.32f, 0.05f, 1f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(10f));
        if (ImGui.Button($"{Loc.T("settings.sup_thanks_continue")}##supThanksGo", new Vector2(btnW, Px(36f))))
        {
            _open = false;
        }
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);

        // Scrim submitted last: within one window the first item under the mouse claims the click, so the
        // button must be registered before it or it can never be pressed.
        ImGui.SetCursorScreenPos(tl);
        ImGui.InvisibleButton("##supThanksScrim", size);

        dl.PopClipRect();
    }

    private static void DrawSparkles(ImDrawListPtr dl, Vector2 tl, Vector2 size, float time, float appear)
    {
        const int Count = 18;
        for (var i = 0; i < Count; i++)
        {
            var fx = Frac(i * 0.6180339887f + 0.13f);
            var speed = 0.012f + Frac(i * 0.377f) * 0.02f;
            var fy = Frac(i * 0.7548776662f + 0.41f - time * speed);
            var pos = tl + new Vector2(fx * size.X, fy * size.Y);
            var twinkle = 0.35f + 0.65f * (0.5f + 0.5f * MathF.Sin(time * 1.7f + i * 2.1f));
            var starSize = Px(5f) + Px(4f) * Frac(i * 0.271f);
            var col = (i % 3) switch
            {
                0 => ImGui.GetColorU32(Gold),
                1 => 0xFFFFFFFFu,
                _ => ImGui.GetColorU32(UiColors.Patreon),
            };
            col = (col & 0x00FFFFFFu) | ((uint)(twinkle * appear * 255f) << 24);
            IconDraw.AddCentered(dl, FontAwesomeIcon.Star, starSize, pos, col);
        }
    }

    private static float Frac(float v) => v - MathF.Floor(v);

    private static float EaseOutCubic(float x) => 1f - MathF.Pow(1f - x, 3f);

    private static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        return 1f + c3 * MathF.Pow(x - 1f, 3f) + c1 * MathF.Pow(x - 1f, 2f);
    }

    private static Vector4 Darken(Vector4 c, float amount) =>
        new(c.X * (1f - amount), c.Y * (1f - amount), c.Z * (1f - amount), 1f);
}
