using System;
using System.Collections.Generic;
using System.Numerics;
using AetherLove.UI;
using AetherOS.Apps.Aetherling.Engine;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Aetherling.Ui;

/// <summary>The equipment strip: one socket per place something can go, showing what is in it. It is a
/// view, not a rule. The engine deliberately lets a halo sit over a hat (<c>AccessoryDef.Displaces</c>
/// only conflicts arms in the same hand), so a socket can hold several things at once and says so with a
/// count rather than refusing the second one.</summary>
internal static class EquipSlots
{
    internal readonly record struct SlotDef(string Key, string NameKey, FontAwesomeIcon Icon);

    /// <summary>Every place, in the order they read down a body. Shown whether or not the player owns
    /// anything for them: an empty socket is how you learn the socket exists.</summary>
    public static readonly IReadOnlyList<SlotDef> All =
    [
        new("head", "os.aetherling_slot_head", FontAwesomeIcon.HatWizard),
        new("glasses", "os.aetherling_slot_glasses", FontAwesomeIcon.Glasses),
        new("facialhair", "os.aetherling_slot_facialhair", FontAwesomeIcon.Smile),
        new("outfit", "os.aetherling_slot_outfit", FontAwesomeIcon.Tshirt),
        new("nook", "os.aetherling_slot_nook", FontAwesomeIcon.Couch),
        new(AccessoryDef.ArmsSlot, "os.aetherling_slot_arms", FontAwesomeIcon.Khanda),
        new(AccessoryDef.BannerSlot, "os.aetherling_slot_banner", FontAwesomeIcon.Flag),
    ];

    public static float Height => Px(72f);

    /// <summary>Draws the strip and returns the slot key that is selected after any click.
    /// <paramref name="wornIn"/> answers how many things are in a socket, <paramref name="ownsFor"/>
    /// whether the player has anything to put there at all.</summary>
    public static string Draw(
        OsAppContext ctx,
        ImDrawListPtr dl,
        Vector2 tl,
        float width,
        string selected,
        Func<string, int> wornIn,
        Func<string, bool> ownsFor)
    {
        var count = All.Count;
        var pad = Px(10f);
        var gap = Px(6f);
        var side = MathF.Min(Px(44f), (width - (pad * 2f) - (gap * (count - 1))) / count);
        var strip = (side * count) + (gap * (count - 1));
        var start = tl.X + ((width - strip) * 0.5f);

        for (var i = 0; i < count; i++)
        {
            var slot = All[i];
            var socket = new Vector2(start + (i * (side + gap)), tl.Y);
            var owns = ownsFor(slot.Key);
            var worn = wornIn(slot.Key);
            var isSelected = string.Equals(selected, slot.Key, StringComparison.OrdinalIgnoreCase);

            ImGui.SetCursorScreenPos(socket);
            var pressed = ImGui.InvisibleButton($"##slot{slot.Key}", new Vector2(side, side));
            var hovered = ImGui.IsItemHovered();
            if (hovered)
            {
                HandOnHover();
                ImGui.SetTooltip(ctx.Localize(slot.NameKey));
            }
            if (pressed)
            {
                selected = slot.Key;
                isSelected = true;
            }

            var br = socket + new Vector2(side, side);
            var radius = Px(10f);
            dl.AddRectFilled(socket, br,
                Look.U32(Look.Crystal with { W = isSelected ? 0.20f : hovered ? 0.12f : 0.05f }), radius);
            dl.AddRect(socket, br,
                Look.U32(worn > 0 ? Look.Crystal : Look.Whisper, isSelected ? 0.9f : worn > 0 ? 0.5f : 0.18f),
                radius, ImDrawFlags.RoundCornersAll, Px(isSelected ? 1.6f : 1f));

            var centre = socket + new Vector2(side * 0.5f, side * 0.5f);
            var alpha = worn > 0 ? 0.95f : owns ? 0.5f : 0.22f;
            IconDraw.AddCentered(dl, slot.Icon, side * 0.42f, centre, Look.U32(Look.CrystalPale, alpha));

            // How many things are in there, when it is more than the one the icon implies.
            if (worn > 1)
            {
                var badge = new Vector2(br.X - Px(5f), socket.Y + Px(5f));
                dl.AddCircleFilled(badge, Px(7f), Look.U32(Look.Crystal, 0.95f), 14);
                Look.Centred(dl, worn.ToString(ctx.Culture), badge.X,
                    badge.Y - (ImGui.GetTextLineHeight() * 0.35f), Look.U32(Look.Void with { W = 1f }), 0.7f);
            }
            else if (worn == 1)
            {
                dl.AddCircleFilled(new Vector2(br.X - Px(6f), socket.Y + Px(6f)), Px(3f),
                    Look.U32(Look.Crystal, 0.95f), 10);
            }

            var label = ctx.Localize(slot.NameKey);
            var scale = 0.68f;
            while (scale > 0.46f && ImGui.CalcTextSize(label).X * scale > side + gap)
            {
                scale -= 0.04f;
            }
            Look.Centred(dl, label, centre.X, br.Y + Px(4f),
                Look.U32(isSelected ? Look.CrystalPale : Look.Whisper, isSelected ? 0.95f : 0.7f), scale);
        }

        return selected;
    }
}
