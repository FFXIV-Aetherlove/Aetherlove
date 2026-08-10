using System;
using System.Numerics;
using AetherOS.Apps.Aetherling.Screens;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;

namespace AetherOS.Apps.Aetherling.Ui;

/// <summary>The five sizes it can stand out on the screen at, as a row of pills. The introduction asks once
/// and the status page keeps asking, so the row lives in neither of them.</summary>
internal static class PetSizePicker
{
    /// <summary>Returns true on the frame the pick changed.</summary>
    public static bool Draw(OsAppContext ctx, ImDrawListPtr dl, Vector2 tl, float width, ref int index)
    {
        var count = FloatingPet.SizeScales.Length;
        var gap = Px(6f);
        var pillW = (width - (gap * (count - 1))) / count;
        var height = Px(36f);
        var changed = false;

        for (var i = 0; i < count; i++)
        {
            var pillTl = new Vector2(tl.X + (i * (pillW + gap)), tl.Y);
            var selected = i == index;

            ImGui.SetCursorScreenPos(pillTl);
            var pressed = ImGui.InvisibleButton($"##aetherlingSize{i}", new Vector2(pillW, height));
            var hovered = ImGui.IsItemHovered();
            if (hovered)
            {
                HandOnHover();
            }

            var fill = selected
                ? Look.Crystal with { W = 0.30f }
                : new Vector4(1f, 1f, 1f, hovered ? 0.12f : 0.05f);
            dl.AddRectFilled(pillTl, pillTl + new Vector2(pillW, height), Look.U32(fill), Px(9f));
            if (selected)
            {
                dl.AddRect(pillTl, pillTl + new Vector2(pillW, height), Look.U32(Look.CrystalPale, 0.7f),
                    Px(9f), ImDrawFlags.RoundCornersAll, Px(1.2f));
            }

            // The label grows with what it stands for, so the row reads as a scale rather than five tabs.
            var labelScale = 0.82f + (i * 0.09f);
            Look.Centred(dl, ctx.Localize($"os.aetherling_size_{i}"), pillTl.X + (pillW * 0.5f),
                pillTl.Y + ((height - (ImGui.GetTextLineHeight() * labelScale)) * 0.5f),
                Look.U32(selected ? Look.CrystalPale : Look.Whisper, selected ? 1f : 0.8f), labelScale);

            if (pressed && !selected)
            {
                index = i;
                changed = true;
            }
        }

        return changed;
    }
}
