using System;
using System.Numerics;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Aetherling.Ui;

/// <summary>The chrome the pet's two sub-pages share: the way back, the title, and the two row shapes they
/// are both made of.</summary>
internal static class PetPageUi
{
    /// <summary>Back pill and title. Returns the y to carry on from.</summary>
    public static float Header(OsAppContext ctx, ImDrawListPtr dl, Vector2 origin, string back, string title,
        Action onBack)
    {
        var pad = Px(18f);
        if (SharedUiHelpers.DrawFloatingBackPill(new Vector2(origin.X + pad, origin.Y + Px(14f)), back,
                FontAwesomeIcon.Heart))
        {
            onBack();
        }

        var y = origin.Y + Px(56f);
        using (ctx.TitleFont?.Push())
        {
            dl.AddText(new Vector2(origin.X + pad, y), Look.U32(Look.CrystalPale), title);
        }
        return y + Px(38f);
    }

    /// <summary>The one thing worth knowing, in a card that glows. It is the only lit thing on a deliberately
    /// dark page, which is what makes it read as the creature telling you something rather than another row.
    /// Returns its height plus the gap under it.</summary>
    public static float TipCard(OsAppContext ctx, ImDrawListPtr dl, Vector2 origin, Vector2 size, float y,
        string text, double now)
    {
        var pad = Px(18f);
        var innerX = Px(16f);
        var innerY = Px(14f);
        const float TextScale = 0.92f;

        var width = size.X - (pad * 2f);
        var wrap = width - (innerX * 2f);
        var iconRow = Px(26f);
        var height = innerY + iconRow + Look.WrappedHeight(text, wrap, TextScale) + innerY;
        var tl = new Vector2(origin.X + pad, y);
        var br = tl + new Vector2(width, height);
        var radius = Px(16f);
        var breath = ctx.ReduceMotion ? 0.5f : Look.Breathe(now, 4.6f);

        // The bloom is rings of rounded rect stepping outwards, because a rectangular glow cannot be had from
        // the circle helpers and a texture for one would be a whole asset for one card.
        const int Rings = 5;
        for (var i = Rings; i >= 1; i--)
        {
            var spread = Px(2.5f) * i;
            var alpha = (0.055f + (0.03f * breath)) * (1f - ((i - 1) / (float)Rings));
            dl.AddRectFilled(tl - new Vector2(spread, spread), br + new Vector2(spread, spread),
                Look.U32(Look.Crystal, alpha), radius + spread);
        }

        dl.AddRectFilled(tl, br, Look.U32(new Vector4(0.09f, 0.16f, 0.19f, 0.92f)), radius);
        dl.AddRect(tl, br, Look.U32(Look.Crystal, 0.30f + (0.25f * breath)), radius, ImDrawFlags.RoundCornersAll,
            Px(1.3f));

        var centreX = tl.X + (width * 0.5f);
        IconDraw.AddCentered(dl, FontAwesomeIcon.Lightbulb, Px(13f), new Vector2(centreX, tl.Y + innerY + Px(7f)),
            Look.U32(Look.CrystalPale, 0.70f + (0.30f * breath)));
        Look.CentredWrapped(dl, text, centreX, tl.Y + innerY + iconRow, wrap,
            Look.U32(Look.CrystalPale, 0.88f), TextScale);

        return height + Px(16f);
    }

    /// <summary>A read-only fact. Returns its height plus the gap under it.</summary>
    public static float Row(
        ImDrawListPtr dl, Vector2 origin, Vector2 size, float y, FontAwesomeIcon icon, string label, string value)
    {
        var pad = Px(18f);
        var height = Px(46f);
        var tl = new Vector2(origin.X + pad, y);
        var br = new Vector2(origin.X + size.X - pad, y + height);
        dl.AddRectFilled(tl, br, 0x12FFFFFFu, Px(12f));

        IconDraw.AddCentered(dl, icon, Px(13f), new Vector2(tl.X + Px(24f), y + (height * 0.5f)),
            Look.U32(Look.Crystal, 0.8f));
        dl.AddText(new Vector2(tl.X + Px(44f), y + (height * 0.5f) - (ImGui.GetTextLineHeight() * 0.5f)),
            Look.U32(Look.Whisper, 0.9f), label);
        var valueW = ImGui.CalcTextSize(value).X;
        dl.AddText(new Vector2(br.X - Px(14f) - valueW, y + (height * 0.5f) - (ImGui.GetTextLineHeight() * 0.5f)),
            Look.U32(Look.CrystalPale), value);
        return height + Px(8f);
    }

    /// <summary>A settings row. A null <paramref name="on"/> is an action rather than a state, so it draws a
    /// chevron where the switch would be.</summary>
    public static bool Toggle(
        ImDrawListPtr dl, Vector2 origin, Vector2 size, float y, string label, bool? on)
    {
        var pad = Px(18f);
        var height = Px(38f);
        var tl = new Vector2(origin.X + pad, y);
        var width = size.X - (pad * 2f);

        ImGui.SetCursorScreenPos(tl);
        var pressed = ImGui.InvisibleButton($"##aetherlingToggle{label}", new Vector2(width, height));
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            HandOnHover();
        }

        dl.AddRectFilled(tl, tl + new Vector2(width, height),
            Look.U32(Look.Crystal with { W = hovered ? 0.12f : 0.05f }), Px(10f));
        dl.AddText(new Vector2(tl.X + Px(12f), y + (height * 0.5f) - (ImGui.GetTextLineHeight() * 0.5f)),
            Look.U32(Look.CrystalPale, 0.92f), label);

        var right = tl.X + width - Px(12f);
        if (on is not { } state)
        {
            IconDraw.AddCentered(dl, FontAwesomeIcon.ChevronRight, Px(11f),
                new Vector2(right - Px(6f), y + (height * 0.5f)), Look.U32(Look.Whisper, 0.8f));
            return pressed;
        }

        var trackW = Px(34f);
        var trackH = Px(18f);
        var trackTl = new Vector2(right - trackW, y + ((height - trackH) * 0.5f));
        dl.AddRectFilled(trackTl, trackTl + new Vector2(trackW, trackH),
            Look.U32(state ? Look.Crystal with { W = 0.55f } : new Vector4(1f, 1f, 1f, 0.12f)), trackH * 0.5f);
        var knobX = state ? trackTl.X + trackW - (trackH * 0.5f) : trackTl.X + (trackH * 0.5f);
        dl.AddCircleFilled(new Vector2(knobX, trackTl.Y + (trackH * 0.5f)), (trackH * 0.5f) - Px(2f),
            Look.U32(Look.CrystalPale, 0.95f), 16);
        return pressed;
    }
}
