using System;
using System.Numerics;
using AetherOS.Apps.Racer.Rendering;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;

namespace AetherOS.Apps.Racer.Screens.Cards;

/// <summary>How a card grid's tiles are dressed at one column count: the text sizes of the lines and chips around a
/// face and on a shaded plate, the pip scale and how far the face's own text may grow past its gallery size. Three
/// per row is the look every grid had before the choice existed.</summary>
internal readonly record struct CardTileStyle(RacerTextSize NameText, RacerTextSize ChipText, RacerTextSize PlateText, float PipScale, float ChipHeight, float IconScale, float FaceTextGrowth);

/// <summary>The 1, 2 or 3 cards per row choice the Album and the card picker each offer: its storage, the tile style
/// each count uses and the three small chips that pick it.</summary>
internal static class CardsPerRow
{
    public const int Min = 1;
    public const int Max = 3;
    public const float ChooserHeight = 24f;
    public const float ChooserChipWidth = 34f;
    public const float ChooserGap = 4f;
    private const float ChipRounding = 5f;
    private const float GlyphHeightShare = 0.58f;
    private const float GlyphWidthShare = 0.8f;
    private const float GlyphGap = 2f;
    private const float GlyphRounding = 1.5f;

    private static readonly CardTileStyle Three = new(RacerTextSize.Small, RacerTextSize.Caption, RacerTextSize.Caption, 1f, CardChrome.ChipHeight, 1f, 1f);
    private static readonly CardTileStyle Two = new(RacerTextSize.Caption, RacerTextSize.Caption, RacerTextSize.Body, 1.3f, 28f, 1.2f, 1.3f);
    private static readonly CardTileStyle One = new(RacerTextSize.Body, RacerTextSize.Body, RacerTextSize.Button, 1.7f, 34f, 1.5f, 1.6f);

    public static CardTileStyle StyleFor(int perRow) => perRow switch
    {
        Min => One,
        2 => Two,
        _ => Three,
    };

    /// <summary>The saved count under <paramref name="key"/>, or <paramref name="fallback"/> when nothing valid is stored
    /// or the store cannot be read.</summary>
    public static int Load(IAppStorage storage, string key, int fallback)
    {
        try
        {
            return storage.Get<int?>(key) is { } stored and >= Min and <= Max ? stored : fallback;
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    /// <summary>Saves the count; a store that cannot be written keeps the choice for this session only.</summary>
    public static void Save(IAppStorage storage, string key, int perRow)
    {
        try
        {
            storage.Set(key, (int?)perRow);
        }
        catch (Exception)
        {
        }
    }

    /// <summary>The first tile of the row at <paramref name="scrollY"/>: the card a column change keeps in view.</summary>
    public static int TopIndex(float scrollY, float pitch, int columns) =>
        pitch <= 0f || columns <= 0 ? 0 : Math.Max(0, (int)MathF.Floor(scrollY / pitch)) * columns;

    /// <summary>Three chips in a row inside <paramref name="size"/>, drawing one, two and three card shapes. The chosen
    /// one is filled dark. Returns the count picked this frame, else <paramref name="perRow"/>.</summary>
    public static int DrawChooser(OsAppContext ctx, string id, int perRow, Vector2 at, Vector2 size)
    {
        var gap = Px(ChooserGap);
        var chipWidth = (size.X - (gap * (Max - 1))) / Max;
        var dl = ImGui.GetWindowDrawList();
        var result = perRow;
        for (var count = Min; count <= Max; count++)
        {
            var chipAt = at + new Vector2((count - Min) * (chipWidth + gap), 0f);
            var chipSize = new Vector2(chipWidth, size.Y);
            ImGui.SetCursorScreenPos(chipAt);
            var pressed = ImGui.InvisibleButton(id + count, chipSize);
            var hovered = ImGui.IsItemHovered();
            var chosen = count == perRow;
            if (hovered)
            {
                HandOnHover();
                CardChrome.Tooltip(ctx.Localize("os.racer_cards_per_row_" + count));
            }

            var fill = chosen ? CardChrome.ChipFill : ElementFx.U32(hovered ? CardChrome.FieldFillHover : CardChrome.FieldFill);
            var edge = hovered || chosen ? GrandstandFrame.Gold : ElementFx.U32(CardChrome.FieldEdge);
            dl.AddRectFilled(chipAt, chipAt + chipSize, fill, Px(ChipRounding));
            dl.AddRect(chipAt, chipAt + chipSize, edge, Px(ChipRounding), ImDrawFlags.None, Px(1f));
            DrawGlyph(dl, chipAt, chipSize, count, chosen ? GrandstandFrame.Gold : ElementFx.U32(CardChrome.FieldInk));
            if (pressed)
            {
                result = count;
            }
        }

        return result;
    }

    /// <summary><paramref name="count"/> upright card shapes side by side, centred in the chip. One card stands full
    /// height; more cards shrink until they fit the chip's width.</summary>
    private static void DrawGlyph(ImDrawListPtr dl, Vector2 chipAt, Vector2 chipSize, int count, uint ink)
    {
        var gap = Px(GlyphGap);
        var height = chipSize.Y * GlyphHeightShare;
        var width = height / CardFaceLayout.Ratio;
        var room = chipSize.X * GlyphWidthShare;
        var total = (count * width) + ((count - 1) * gap);
        if (total > room)
        {
            width = (room - ((count - 1) * gap)) / count;
            height = width * CardFaceLayout.Ratio;
            total = room;
        }

        var left = chipAt.X + ((chipSize.X - total) * 0.5f);
        var top = chipAt.Y + ((chipSize.Y - height) * 0.5f);
        for (var i = 0; i < count; i++)
        {
            var min = new Vector2(MathF.Round(left + (i * (width + gap))), MathF.Round(top));
            dl.AddRectFilled(min, min + new Vector2(MathF.Max(1f, MathF.Round(width)), MathF.Round(height)), ink, Px(GlyphRounding));
        }
    }
}
