using System;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Store;

/// <summary>A shelf's colorful heading: accent icon chip, shimmering title, gradient underline and a
/// right-aligned "See all". Returns true when See-all is clicked.</summary>
internal static class RailHeader
{
    public static bool Draw(string id, float winW, FontAwesomeIcon icon, string title, Vector4 accent,
        bool reduceMotion, bool seeAll = true)
    {
        const float padX = 16f;
        var dl = ImGui.GetWindowDrawList();
        ImGui.SetCursorPosX(Px(padX));
        var tl = ImGui.GetCursorScreenPos();
        var chipR = Px(11f);
        var chipC = tl + new Vector2(chipR, chipR + Px(2f));

        dl.AddCircleFilled(chipC, chipR, ImGui.GetColorU32(accent with { W = 0.9f }));
        IconDraw.AddCentered(dl, icon, chipR * 1.05f, chipC, 0xFFFFFFFFu);

        var titleX = tl.X + chipR * 2f + Px(8f);
        int vtxStart;
        using (UiFonts.H3?.Push())
        {
            vtxStart = dl.VtxBuffer.Size;
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), new Vector2(titleX, tl.Y), 0xFFFFFFFFu, title);
        }
        var titleW = 0f;
        using (UiFonts.H3?.Push())
        {
            titleW = ImGui.CalcTextSize(title).X;
        }
        if (!reduceMotion)
        {
            GradientSweepVertices(dl, vtxStart, accent, StorePalette.BlueLight,
                (float)(ImGui.GetTime() * 2.0));
        }
        else
        {
            GradientSweepVertices(dl, vtxStart, accent, accent, 0f);
        }

        // A thin underline fading out to the right.
        var lineY = tl.Y + Px(26f);
        dl.AddRectFilledMultiColor(
            new Vector2(titleX, lineY), new Vector2(titleX + titleW + Px(40f), lineY + Px(2f)),
            ImGui.GetColorU32(accent), ImGui.GetColorU32(accent with { W = 0f }),
            ImGui.GetColorU32(accent with { W = 0f }), ImGui.GetColorU32(accent));

        var clicked = false;
        if (seeAll)
        {
            var label = Loc.T("os.store_see_all");
            var labelSz = ImGui.CalcTextSize(label);
            var btnTl = new Vector2(ImGui.GetWindowPos().X + winW - Px(padX) - labelSz.X - Px(8f), tl.Y + Px(2f));
            ImGui.SetCursorScreenPos(btnTl);
            clicked = ImGui.InvisibleButton($"##seeAll{id}", labelSz + new Vector2(Px(8f), Px(4f)));
            var hovered = ImGui.IsItemHovered();
            if (hovered)
            {
                HandOnHover();
            }
            dl.AddText(btnTl + new Vector2(Px(4f), 0f),
                ImGui.GetColorU32(hovered ? StorePalette.BlueLight : UiColors.Hint), label);
        }

        ImGui.SetCursorScreenPos(new Vector2(ImGui.GetWindowPos().X, lineY + Px(8f)));
        return clicked;
    }
}
