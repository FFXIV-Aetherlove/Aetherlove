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
    /// <summary>One socket. <paramref name="Paint"/> draws the glyph itself instead of the icon font,
    /// taking the socket's centre and its full side; FontAwesome Free has neither an ear nor a tail, and
    /// its nearest offers are a hearing-aid symbol and a quill.</summary>
    internal readonly record struct SlotDef(string Key, string NameKey, FontAwesomeIcon Icon,
        Action<ImDrawListPtr, Vector2, float, uint>? Paint = null);

    /// <summary>Every place, in the order they read down a body. Shown whether or not the player owns
    /// anything for them: an empty socket is how you learn the socket exists.</summary>
    public static readonly IReadOnlyList<SlotDef> All =
    [
        new("head", "os.aetherling_slot_head", FontAwesomeIcon.HatWizard),
        new("glasses", "os.aetherling_slot_glasses", FontAwesomeIcon.Glasses),
        new("facialhair", "os.aetherling_slot_facialhair", FontAwesomeIcon.Smile),
        new(AccessoryDef.EarsSlot, "os.aetherling_slot_ears", FontAwesomeIcon.Cat, PaintEars),
        new("outfit", "os.aetherling_slot_outfit", FontAwesomeIcon.Tshirt),
        new(AccessoryDef.TailSlot, "os.aetherling_slot_tail", FontAwesomeIcon.Feather, PaintTail),
        new("nook", "os.aetherling_slot_nook", FontAwesomeIcon.Couch),
        new(AccessoryDef.ArmsSlot, "os.aetherling_slot_arms", FontAwesomeIcon.Khanda),
        new(AccessoryDef.BannerSlot, "os.aetherling_slot_banner", FontAwesomeIcon.Flag),
    ];

    /// <summary>The ears: two triangles leaning outboard. Every animal on the shelf wears a triangle on its
    /// head, antennae included, and no one of them may stand for the rest (the socket held a cat).</summary>
    internal static void PaintEars(ImDrawListPtr dl, Vector2 centre, float side, uint colour)
    {
        Ear(dl, centre + new Vector2(-0.155f * side, 0.19f * side), 0.15f * side, 0.40f * side, -14f, colour);
        Ear(dl, centre + new Vector2(0.155f * side, 0.19f * side), 0.15f * side, 0.40f * side, 14f, colour);
    }

    private static void Ear(ImDrawListPtr dl, Vector2 baseCentre, float half, float height, float lean, uint colour)
    {
        var a = lean * (MathF.PI / 180f);
        var sin = MathF.Sin(a);
        var cos = MathF.Cos(a);

        Vector2 Place(float x, float y) =>
            baseCentre + new Vector2((x * cos) - (y * sin), (x * sin) + (y * cos));

        dl.AddTriangleFilled(Place(-half, 0f), Place(0f, -height), Place(half, 0f), colour);
    }

    /// <summary>The tail: a tapered curl, the same capsule chain the creature's own tail is drawn with, kept
    /// well inside the socket so it reads as a glyph rather than as a stroke across the tile.</summary>
    internal static void PaintTail(ImDrawListPtr dl, Vector2 centre, float side, uint colour)
    {
        const float scale = 0.72f;
        Span<Vector2> spine = stackalloc Vector2[25];
        var n = 0;
        Curve(spine, ref n, new Vector2(-0.24f, 0.30f), new Vector2(-0.02f, 0.30f), new Vector2(0.10f, 0.02f), 12);
        Curve(spine, ref n, new Vector2(0.10f, 0.02f), new Vector2(0.18f, -0.24f), new Vector2(-0.02f, -0.30f), 12,
            skipFirst: true);

        var root = 0.105f * scale * side;
        var tip = 0.022f * scale * side;
        for (var i = 0; i < n - 1; i++)
        {
            var u0 = i / (float)(n - 1);
            var u1 = (i + 1) / (float)(n - 1);
            var a = centre + (spine[i] * scale * side);
            var b = centre + (spine[i + 1] * scale * side);
            Capsule(dl, a, root + ((tip - root) * u0), b, root + ((tip - root) * u1), colour);
        }
    }

    private static void Curve(Span<Vector2> into, ref int n, Vector2 p0, Vector2 p1, Vector2 p2, int steps,
        bool skipFirst = false)
    {
        for (var i = skipFirst ? 1 : 0; i <= steps && n < into.Length; i++)
        {
            var t = i / (float)steps;
            var a = (1 - t) * (1 - t);
            var b = 2 * (1 - t) * t;
            var c = t * t;
            into[n++] = (p0 * a) + (p1 * b) + (p2 * c);
        }
    }

    private static void Capsule(ImDrawListPtr dl, Vector2 a, float ra, Vector2 b, float rb, uint colour)
    {
        dl.AddCircleFilled(a, ra, colour, 16);
        dl.AddCircleFilled(b, rb, colour, 16);
        var d = b - a;
        var len = d.Length();
        if (len < 0.5f)
        {
            return;
        }
        var normal = new Vector2(-d.Y, d.X) / len;
        dl.AddQuadFilled(a + (normal * ra), b + (normal * rb), b - (normal * rb), a - (normal * ra), colour);
    }

    /// <summary>What the strip needs to place its sockets at a given width. Sockets keep a fixed side and
    /// wrap into rows instead of shrinking; every row left-aligns to the centred first row, so a short last
    /// row leaves its gap on the right.</summary>
    private static (float Side, float Gap, float RowGap, int PerRow, int Rows, float OffsetX) Layout(float width)
    {
        var count = All.Count;
        var pad = Px(10f);
        var gap = Px(6f);
        var rowGap = Px(6f);
        var side = Px(44f);
        var usable = width - (pad * 2f);
        var perRowMax = Math.Max(1, (int)MathF.Floor((usable + gap) / (side + gap)));
        var rows = (count + perRowMax - 1) / perRowMax;
        var perRow = (count + rows - 1) / rows;
        var rowW = (perRow * side) + ((perRow - 1) * gap);
        var offsetX = (width - rowW) * 0.5f;
        return (side, gap, rowGap, perRow, rows, offsetX);
    }

    public static float HeightFor(float width)
    {
        var (side, _, rowGap, _, rows, _) = Layout(width);
        return (rows * side) + ((rows - 1) * rowGap);
    }

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
        var (side, gap, rowGap, perRow, _, offsetX) = Layout(width);
        var startX = tl.X + offsetX;
        for (var i = 0; i < count; i++)
        {
            var slot = All[i];
            var socket = new Vector2(
                startX + ((i % perRow) * (side + gap)),
                tl.Y + ((i / perRow) * (side + rowGap)));
            var owns = ownsFor(slot.Key);
            var worn = wornIn(slot.Key);
            var isSelected = string.Equals(selected, slot.Key, StringComparison.OrdinalIgnoreCase);

            ImGui.SetCursorScreenPos(socket);
            var pressed = ImGui.InvisibleButton($"##slot{slot.Key}", new Vector2(side, side));
            var hovered = ImGui.IsItemHovered();
            if (hovered)
            {
                HandOnHover();
                var tip = ctx.Localize(slot.NameKey);
                if (worn > 0)
                {
                    tip += $"\n{ctx.Localize("os.aetherling_wardrobe_worn")} {worn.ToString(ctx.Culture)}";
                }
                ImGui.SetTooltip(tip);
            }
            if (pressed)
            {
                selected = slot.Key;
                isSelected = true;
            }

            var br = socket + new Vector2(side, side);
            var radius = Px(10f);
            dl.AddRectFilled(socket, br,
                Look.U32(Look.Crystal with { W = isSelected ? 0.28f : hovered ? 0.12f : 0.05f }), radius);
            dl.AddRect(socket, br,
                Look.U32(worn > 0 ? Look.Crystal : Look.Whisper, isSelected ? 0.9f : worn > 0 ? 0.5f : 0.18f),
                radius, ImDrawFlags.RoundCornersAll, Px(isSelected ? 1.6f : 1f));

            var centre = socket + new Vector2(side * 0.5f, side * 0.5f);
            var alpha = isSelected ? 1f : worn > 0 ? 0.95f : owns ? 0.5f : 0.22f;
            var ink = Look.U32(Look.CrystalPale, alpha);
            if (slot.Paint is { } paint)
            {
                paint(dl, centre, side, ink);
            }
            else
            {
                IconDraw.AddCentered(dl, slot.Icon, side * 0.42f, centre, ink);
            }

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
        }

        return selected;
    }
}
