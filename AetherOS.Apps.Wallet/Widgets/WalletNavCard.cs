using System;
using System.Numerics;
using AetherLove.Services;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Wallet;

/// <summary>A gradient card that opens one of the Sparks sub-pages: icon chip, title, a one-line subtitle
/// and a chevron, in the Market hub's tile idiom.</summary>
internal static class WalletNavCard
{
    private const float Rounding = 14f;
    private const float ChipRadius = 15f;

    public static bool Draw(string id, Vector2 size, Vector4 gradTop, Vector4 gradBottom, FontAwesomeIcon icon,
        string title, string subtitle)
    {
        var clicked = ImGui.InvisibleButton(id, size);
        HandOnHover();
        var hovered = ImGui.IsItemHovered();

        var dl = ImGui.GetWindowDrawList();
        var tl = ImGui.GetItemRectMin();
        var br = ImGui.GetItemRectMax();
        var rounding = Px(Rounding);
        OsDrawShared.RoundedGradient(dl, tl, br, rounding, gradTop, gradBottom, hovered ? 1f : 0.94f);

        var watermarkPx = MathF.Min(size.Y * 0.62f, Px(52f));
        var watermarkSz = IconDraw.Measure(icon, watermarkPx);
        IconDraw.Add(dl, icon, watermarkPx, new Vector2(br.X - watermarkSz.X - Px(6f), tl.Y + Px(6f)),
            OsDrawShared.White(0.09f));

        var chipR = Px(ChipRadius);
        var chipC = tl + new Vector2(Px(12f) + chipR, Px(12f) + chipR);
        dl.AddCircleFilled(chipC, chipR * 1.9f, OsDrawShared.White(0.05f));
        dl.AddCircleFilled(chipC, chipR, OsDrawShared.Black(0.22f));
        IconDraw.AddCentered(dl, icon, chipR * 1.1f, chipC, OsDrawShared.White(0.95f));

        if (hovered)
        {
            dl.AddRect(tl, br, OsDrawShared.White(0.30f), rounding, ImDrawFlags.None, Px(1.2f));
        }

        var chevronPx = Px(12f);
        var chevronSz = IconDraw.Measure(FontAwesomeIcon.ChevronRight, chevronPx);
        IconDraw.Add(dl, FontAwesomeIcon.ChevronRight, chevronPx,
            new Vector2(br.X - chevronSz.X - Px(12f), br.Y - chevronSz.Y - Px(11f)), OsDrawShared.White(0.55f));

        var lineH = ImGui.GetTextLineHeight();
        var subW = size.X - Px(24f) - chevronSz.X - Px(8f);
        dl.AddText(new Vector2(tl.X + Px(12f), br.Y - Px(10f) - lineH), OsDrawShared.White(0.74f),
            TruncateToWidth(subtitle, subW));

        using (UiFonts.H3?.Push())
        {
            var titleX = chipC.X + chipR + Px(10f);
            var titleText = TruncateToWidth(title, br.X - Px(12f) - titleX);
            var titleSz = ImGui.CalcTextSize(titleText);
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), new Vector2(titleX, chipC.Y - titleSz.Y * 0.5f),
                OsDrawShared.White(0.98f), titleText);
        }
        return clicked;
    }
}
