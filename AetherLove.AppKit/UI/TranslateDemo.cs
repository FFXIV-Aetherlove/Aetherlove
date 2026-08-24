using System;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherLove.UI;

/// <summary>A looping mock of the right-click translate gesture, shared by the OS onboarding step and the
/// update offer: a foreign chat bubble, a cursor that right-clicks it, the little menu, and the text
/// swapping to the translation. Pure draw-list theatre on a fixed timeline; nothing is clickable. Under
/// ReduceMotion it shows one static frame (the menu open over the bubble) instead of animating.</summary>
public static class TranslateDemo
{
    private const double Loop = 6.5;

    /// <summary>Draws the demo at the cursor, <paramref name="width"/> wide, and advances the cursor past
    /// it. The card sizes to the taller of the two sample texts so nothing spills past its edges when the
    /// swap happens, whatever length the localized samples come out at.</summary>
    public static void Draw(float width)
    {
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var tl = ImGui.GetCursorScreenPos();
        var still = AccessibilityService.ReduceMotion;
        var time = still ? 2.4 : ImGui.GetTime() % Loop;

        // Timeline: 0-1.4 original bubble; 1.4 right-click; 1.6-3.2 menu; 3.2 click Translate;
        // 3.6-6.0 translated bubble; then fade back around.
        var swapped = time >= 3.6;
        var source = Loc.T("os.translate_demo_source");
        var result = Loc.T("os.translate_demo_result");
        var text = swapped ? result : source;

        var bubblePad = Px(10f);
        var wrapW = width - Px(70f);
        var sourceSz = ImGui.CalcTextSize(source, false, wrapW);
        var resultSz = ImGui.CalcTextSize(result, false, wrapW);
        var maxTextH = MathF.Max(sourceSz.Y, resultSz.Y);
        var height = MathF.Max(Px(92f), Px(16f) + maxTextH + bubblePad * 2.4f + Px(14f));

        dl.AddRectFilled(tl, tl + new Vector2(width, height), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.045f)), Px(12f));

        var textSz = ImGui.CalcTextSize(text, false, wrapW);
        var bubbleTL = tl + new Vector2(Px(14f), Px(16f));
        var bubbleBR = bubbleTL + textSz + new Vector2(bubblePad * 2f, bubblePad * 1.4f);
        dl.AddRectFilled(bubbleTL, bubbleBR, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.10f)), Px(10f));
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), bubbleTL + new Vector2(bubblePad, bubblePad * 0.7f),
            ImGui.GetColorU32(swapped ? t.AccentLight : UiColors.Body), text, wrapW);
        if (swapped && !still)
        {
            var glowT = (float)Math.Clamp((time - 3.6) / 0.4, 0.0, 1.0);
            dl.AddRect(bubbleTL, bubbleBR, ImGui.GetColorU32(t.Accent with { W = 0.7f * (1f - glowT) + 0.2f }),
                Px(10f), ImDrawFlags.None, Px(1.4f));
        }

        // The little context menu, clamped inside the card whatever the bubble came out at.
        var menuVisible = still || time is >= 1.6 and < 3.4;
        var rowH = Px(22f);
        var menuW = MathF.Min(Px(140f),
            Px(34f) + MathF.Max(ImGui.CalcTextSize(Loc.T("os.translate")).X,
                ImGui.CalcTextSize(Loc.T("os.translate_settings")).X) * 0.82f + Px(10f));
        var menuX = MathF.Min(bubbleBR.X - Px(30f), tl.X + width - menuW - Px(8f));
        var menuY = MathF.Min(bubbleTL.Y + Px(18f), tl.Y + height - (rowH * 2f + Px(8f)) - Px(6f));
        if (menuVisible)
        {
            var appear = still ? 1f : (float)Math.Clamp((time - 1.6) / 0.18, 0.0, 1.0);
            var menuTL = new Vector2(menuX, menuY);
            var menuBR = menuTL + new Vector2(menuW, rowH * 2f + Px(8f));
            dl.AddRectFilled(menuTL, menuBR, ImGui.GetColorU32(new Vector4(0.10f, 0.10f, 0.14f, 0.97f * appear)), Px(8f));
            dl.AddRect(menuTL, menuBR, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.16f * appear)), Px(8f));

            var hot = still || time >= 2.4;
            if (hot)
            {
                dl.AddRectFilled(menuTL + new Vector2(Px(3f), Px(4f)),
                    new Vector2(menuBR.X - Px(3f), menuTL.Y + Px(4f) + rowH),
                    ImGui.GetColorU32(t.Accent with { W = 0.30f * appear }), Px(6f));
            }
            var iconPx = Px(11f);
            IconDraw.AddCentered(dl, FontAwesomeIcon.Language, iconPx,
                menuTL + new Vector2(Px(14f), Px(4f) + rowH * 0.5f),
                ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.9f * appear)));
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.82f,
                menuTL + new Vector2(Px(26f), Px(4f) + (rowH - ImGui.GetFontSize() * 0.82f) * 0.5f),
                ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.95f * appear)), Loc.T("os.translate"));
            IconDraw.AddCentered(dl, FontAwesomeIcon.Cog, iconPx,
                menuTL + new Vector2(Px(14f), Px(4f) + rowH * 1.5f),
                ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.45f * appear)));
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.82f,
                menuTL + new Vector2(Px(26f), Px(4f) + rowH + (rowH - ImGui.GetFontSize() * 0.82f) * 0.5f),
                ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.5f * appear)), Loc.T("os.translate_settings"));
        }

        // The cursor: glides onto the bubble, clicks (ripple), moves to the menu row, clicks again.
        if (!still)
        {
            var start = tl + new Vector2(width - Px(30f), height - Px(16f));
            var overBubble = new Vector2(MathF.Min(bubbleBR.X - Px(38f), tl.X + width - Px(40f)),
                bubbleTL.Y + Px(20f));
            var overMenu = new Vector2(menuX + menuW * 0.45f, menuY + Px(15f));
            Vector2 cursor;
            if (time < 1.4)
            {
                cursor = Vector2.Lerp(start, overBubble, Ease((float)(time / 1.4)));
            }
            else if (time < 2.2)
            {
                cursor = overBubble;
            }
            else if (time < 3.0)
            {
                cursor = Vector2.Lerp(overBubble, overMenu, Ease((float)((time - 2.2) / 0.8)));
            }
            else
            {
                cursor = overMenu;
            }

            if (time is >= 1.4 and < 1.9)
            {
                var ripple = (float)((time - 1.4) / 0.5);
                dl.AddCircle(cursor, Px(6f) + ripple * Px(12f),
                    ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.5f * (1f - ripple))), 24, Px(1.5f));
            }
            if (time is >= 3.0 and < 3.5)
            {
                var ripple = (float)((time - 3.0) / 0.5);
                dl.AddCircle(cursor, Px(6f) + ripple * Px(12f),
                    ImGui.GetColorU32(t.Accent with { W = 0.7f * (1f - ripple) }), 24, Px(1.5f));
            }
            IconDraw.Add(dl, FontAwesomeIcon.MousePointer, Px(15f), cursor,
                ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.95f)));
        }

        ImGui.Dummy(new Vector2(width, height));
    }

    private static float Ease(float x) => 1f - MathF.Pow(1f - Math.Clamp(x, 0f, 1f), 3f);
}
