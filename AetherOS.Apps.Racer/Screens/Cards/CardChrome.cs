using System;
using System.Numerics;
using AetherLove.Shared.Racing.Cards;
using AetherOS.Apps.Racer.Rendering;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Racer.Screens.Cards;

/// <summary>The small pieces the card screens share: the dark chip button, the empty and shaded slot plates, the name line
/// under a face, the level pips, the ring around a chosen face and the card tooltip colours.</summary>
internal static class CardChrome
{
    public const float ChipHeight = 24f;
    public const uint ChipFill = 0xE6422517;
    public const uint ChipFillHover = 0xFF63391C;
    public const uint ChipEdge = 0x8879CEF7;
    private const float NameLineShare = 1.25f;
    private const float ChipIconWidth = 18f;
    private const float ChipIconSize = 11f;
    private const float ChipIconPad = 2f;
    private const float ChipRounding = 6f;
    private const float PlateRounding = 7f;
    private const float PlateBorder = 1.2f;
    private const float PlateLabelInset = 4f;
    private const float RingPad = 3f;
    private const float RingWidth = 2f;
    private const float PipRadius = 3.5f;
    private const float PipGap = 4f;
    private const float PipBorder = 1f;
    private const uint ChipFillDisabled = 0x80422517;
    private const uint ChipInk = GrandstandFrame.Cream;
    private const uint ChipInkDisabled = 0x80E5F5FF;
    private const uint ShadeFill = 0xB0000000;
    private static readonly Vector4 PlateFill = new(0.08f, 0.10f, 0.14f, 1f);
    private static readonly Vector4 PlateEdge = new(0.34f, 0.40f, 0.49f, 0.8f);
    private static readonly Vector4 PlateEdgeLit = new(0.98f, 0.87f, 0.55f, 1f);
    private static readonly Vector4 PlateInk = new(0.86f, 0.90f, 0.96f, 1f);
    private static readonly Vector4 PipLit = new(0.96f, 0.76f, 0.26f, 1f);
    private static readonly Vector4 PipDark = new(0.30f, 0.27f, 0.22f, 0.55f);

    /// <summary>The Album's cream filter fields and the per-row chooser drawn in their colours.</summary>
    public static readonly Vector4 FieldInk = new(0.28f, 0.13f, 0.05f, 1f);
    public static readonly Vector4 FieldFill = new(0.98f, 0.94f, 0.86f, 1f);
    public static readonly Vector4 FieldFillHover = new(1f, 0.98f, 0.93f, 1f);
    public static readonly Vector4 FieldEdge = new(0.62f, 0.45f, 0.24f, 0.7f);

    /// <summary>The height a name line takes: the card text floor and its leading.</summary>
    public static float NameLineHeight => Px(CardFaceRenderer.MinTextPx) * NameLineShare;

    /// <summary>The height the level pips take.</summary>
    public static float PipHeight => Px(PipRadius * 2f);

    /// <summary>The height a name line takes in <paramref name="size"/> with its leading.</summary>
    public static float NameLineHeightFor(RacerTextSize size)
    {
        using var font = RacerFonts.Get(size)?.Push();
        return ImGui.GetFontSize() * NameLineShare;
    }

    /// <summary>The height the level pips take at <paramref name="scale"/>.</summary>
    public static float PipHeightAt(float scale) => Px(PipRadius * 2f * scale);

    public static string SlotName(OsAppContext ctx, int slot) => slot == RaceCardHandRules.GoldSlot
        ? ctx.Localize("os.racer_hand_slot_gold")
        : string.Format(ctx.Localize("os.racer_hand_slot_silver"), slot);

    /// <summary>A small dark button in the racer's secondary shape, drawn at a screen position.</summary>
    public static bool Chip(OsAppContext ctx, string id, string label, Vector2 at, Vector2 size, bool enabled, FontAwesomeIcon? icon = null,
        RacerTextSize labelFont = RacerTextSize.Caption, float iconScale = 1f)
    {
        ImGui.SetCursorScreenPos(at);
        var pressed = ImGui.InvisibleButton(id, size) && enabled;
        var hovered = ImGui.IsItemHovered() && enabled;
        if (hovered)
        {
            HandOnHover();
        }

        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(at, at + size, !enabled ? ChipFillDisabled : hovered ? ChipFillHover : ChipFill, Px(ChipRounding));
        dl.AddRect(at, at + size, hovered ? GrandstandFrame.Gold : ChipEdge, Px(ChipRounding), ImDrawFlags.None, Px(1f));
        var textAt = at;
        var textSize = size;
        if (icon is { } glyph)
        {
            var iconW = Px(ChipIconWidth * iconScale);
            AetherLove.UI.IconDraw.AddCentered(dl, glyph, Px(ChipIconSize * iconScale), at + new Vector2((iconW * 0.5f) + Px(ChipIconPad), size.Y * 0.5f), enabled ? GrandstandFrame.Gold : ChipInkDisabled);
            textAt = at + new Vector2(iconW, 0f);
            textSize = size - new Vector2(iconW + Px(ChipIconPad), 0f);
        }

        GrandstandFrame.Label(ctx, label, textAt, textSize, enabled ? ChipInk : ChipInkDisabled, labelFont);
        return pressed;
    }

    /// <summary>An empty hand slot: a dark plate with a label, lit when <paramref name="highlight"/>.</summary>
    public static void EmptyPlate(OsAppContext ctx, ImDrawListPtr dl, Vector2 at, Vector2 size, string label, bool highlight)
    {
        dl.AddRectFilled(at, at + size, ElementFx.U32(PlateFill), Px(PlateRounding));
        dl.AddRect(at, at + size, ElementFx.U32(highlight ? PlateEdgeLit : PlateEdge), Px(PlateRounding), ImDrawFlags.None, Px(PlateBorder));
        PlateLabel(ctx, at, size, label, ElementFx.U32(PlateInk));
    }

    /// <summary>A face already used elsewhere: darkened, with <paramref name="label"/> over it.</summary>
    public static void ShadedPlate(OsAppContext ctx, ImDrawListPtr dl, Vector2 at, Vector2 size, string label, RacerTextSize labelFont = RacerTextSize.Caption)
    {
        dl.AddRectFilled(at, at + size, ShadeFill, Px(PlateRounding));
        PlateLabel(ctx, at, size, label, ChipInk, labelFont);
    }

    /// <summary>The phone's <see cref="AetherLove.UI.UiTooltip"/> in the plate's dark colours. The racer's paper pages
    /// push a dark ink that a tooltip would otherwise inherit and draw unreadably on its dark fill.</summary>
    public static void Tooltip(params string[] lines)
    {
        using var colours = TooltipColours();
        AetherLove.UI.UiTooltip.Show(lines);
    }

    /// <summary>Pushes the card tooltip's fill and ink for a hand-built tooltip; dispose it after EndTooltip.</summary>
    public static ImRaii.ColorDisposable TooltipColours()
    {
        var colours = ImRaii.PushColor(ImGuiCol.Text, ChipInk);
        colours.Push(ImGuiCol.PopupBg, ElementFx.U32(PlateFill));
        return colours;
    }

    /// <summary>A lit ring around a face.</summary>
    public static void Ring(ImDrawListPtr dl, Vector2 at, Vector2 size)
    {
        var pad = new Vector2(Px(RingPad));
        dl.AddRect(at - pad, at + size + pad, ElementFx.U32(PlateEdgeLit), Px(PlateRounding), ImDrawFlags.None, Px(RingWidth));
    }

    /// <summary>One pip per level a card can reach, the reached ones lit, centred on <paramref name="centreX"/>
    /// with their tops at <paramref name="top"/>, drawn <paramref name="scale"/> times their usual size.</summary>
    public static void LevelPips(ImDrawListPtr dl, float centreX, float top, int level, float scale = 1f)
    {
        var radius = Px(PipRadius * scale);
        var gap = Px(PipGap * scale);
        var count = RaceCardLevels.MaxLevel;
        var width = (count * radius * 2f) + ((count - 1) * gap);
        var x = centreX - (width * 0.5f) + radius;
        for (var i = 0; i < count; i++)
        {
            var centre = new Vector2(x + (i * ((radius * 2f) + gap)), top + radius);
            var lit = i < level;
            dl.AddCircleFilled(centre, radius, ElementFx.U32(lit ? PipLit : PipDark));
            dl.AddCircle(centre, radius, ElementFx.U32(PlateEdge), 0, Px(PipBorder));
        }
    }

    /// <summary>One name centred under a face in a baked racer font (the small one unless <paramref name="size"/> says
    /// otherwise), cut with an ellipsis when the column is too narrow. <paramref name="at"/> is the line's top left.</summary>
    public static void NameLine(ImDrawListPtr dl, string text, Vector2 at, float width, uint ink, RacerTextSize size = RacerTextSize.Small)
    {
        using var font = RacerFonts.Get(size)?.Push();
        var shown = CardText.Ellipsize(text, width, value => ImGui.CalcTextSize(value).X);
        if (shown.Length == 0)
        {
            return;
        }

        var shownWidth = ImGui.CalcTextSize(shown).X;
        dl.AddText(new Vector2(MathF.Round(at.X + ((width - shownWidth) * 0.5f)), MathF.Round(at.Y)), ink, shown);
    }

    private static void PlateLabel(OsAppContext ctx, Vector2 at, Vector2 size, string label, uint ink, RacerTextSize font = RacerTextSize.Caption) =>
        GrandstandFrame.WrappedLabel(ctx, label, at + new Vector2(Px(PlateLabelInset), 0f), size - new Vector2(Px(PlateLabelInset * 2f), 0f), ink, font);
}
