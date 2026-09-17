using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using AetherLove.Services.Media;
using AetherLove.Shared.Assets;
using AetherLove.Shared.Racing.Cards;
using AetherOS.Apps.Racer.Rendering;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;

namespace AetherOS.Apps.Racer.Screens.Cards;

/// <summary>Where a card's pictures live in the racing-cards pack and how to find them: the three core
/// atlases of 18 cells each in catalogue order, one detail image per card, the small Gold atlas the race
/// notice draws from, and the element crystal for the frame's socket. A texture that has not decoded yet, or
/// a file that is missing, answers null from the cache every frame; nothing here remembers a miss.</summary>
internal static class CardArt
{
    public const int AtlasPageCells = 18;
    public const int AtlasColumns = 6;
    public const int AtlasRows = 3;
    private const string AtlasPrefix = "core-atlas-";
    private const string GoldAtlasFile = "race-gold-atlas.png";
    private const string CrystalFolder = "food";
    private const string ImageExtension = ".png";
    private const float PlaceholderRadiusShare = 0.19f;
    private const float PlaceholderRingScale = 1.55f;
    private const float PlaceholderRuleInset = 12f;
    private const float PlaceholderRuleLift = 10f;
    private const float PlaceholderStroke = 0.6f;
    private static readonly Vector2 CentreFocus = new(0.5f, 0.5f);
    private static readonly Vector4 FullCrop = new(0f, 0f, 1f, 1f);

    private static readonly Dictionary<string, int> IndexById = BuildIndex();
    private static readonly Dictionary<string, int> GoldIndexById = BuildGoldIndex();

    public static bool PackReady(OsAppContext ctx) => ctx.Capabilities.Assets.IsReady(AssetPacks.RacingCards);

    /// <summary>The card's position in catalogue order, or -1 for an id the catalogue does not carry.</summary>
    public static int IndexOf(string cardId) => IndexById.TryGetValue(cardId, out var index) ? index : -1;

    /// <summary>The card's cell in the Gold atlas (the Golds in catalogue order), or -1 for a Silver.</summary>
    public static int GoldIndexOf(string cardId) => GoldIndexById.TryGetValue(cardId, out var index) ? index : -1;

    public static string AtlasPath(int page) => MediaPaths.Downloaded(MediaPaths.RacingCards, AtlasPrefix + page + ImageExtension);

    public static string DetailPath(string cardId) => MediaPaths.Downloaded(MediaPaths.RacingCards, cardId + ImageExtension);

    public static string GoldAtlasPath => MediaPaths.Downloaded(MediaPaths.RacingCards, GoldAtlasFile);

    public static string CrystalPath(string element) => MediaPaths.Downloaded(MediaPaths.RacingCards, CrystalFolder, element + ImageExtension);

    /// <summary>The normalized cell (left, top, width, height) of catalogue index <paramref name="index"/> on
    /// its atlas page.</summary>
    public static Vector4 AtlasCell(int index)
    {
        var cell = index % AtlasPageCells;
        var col = cell % AtlasColumns;
        var row = cell / AtlasColumns;
        return new Vector4(col / (float)AtlasColumns, row / (float)AtlasRows, 1f / AtlasColumns, 1f / AtlasRows);
    }

    public static int AtlasPage(int index) => index / AtlasPageCells;

    /// <summary>The normalized cell of a Gold in the Gold atlas.</summary>
    public static Vector4 GoldAtlasCell(int goldIndex)
    {
        var col = goldIndex % AtlasColumns;
        var row = goldIndex / AtlasColumns;
        return new Vector4(col / (float)AtlasColumns, row / (float)AtlasRows, 1f / AtlasColumns, 1f / AtlasRows);
    }

    public static bool TryImage(OsAppContext ctx, string path, out ImTextureID texture, out Vector2 size)
    {
        texture = default;
        size = default;
        try
        {
            if (ctx.Capabilities.Textures.Get(path) is { } handle
                && ctx.Capabilities.Textures.GetSize(path) is { } dimensions && dimensions.X > 0f && dimensions.Y > 0f)
            {
                texture = handle;
                size = dimensions;
                return true;
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
        }

        return false;
    }

    /// <summary>The card's picture cover-fitted into the window: the detail image on an inspect face, or on any
    /// face drawn wider than an atlas cell holds pixels, once it has decoded; else the atlas cell. False when
    /// neither is on hand yet.</summary>
    public static bool Draw(OsAppContext ctx, ImDrawListPtr dl, RaceCard card, Vector2 tl, Vector2 br, bool detail, Vector2 pointer, bool packReady)
    {
        if (!packReady)
        {
            return false;
        }

        var target = br - tl;
        var index = IndexOf(card.Id);
        ImTextureID atlas = default;
        var atlasSize = Vector2.Zero;
        var hasAtlas = index >= 0 && TryImage(ctx, AtlasPath(AtlasPage(index)), out atlas, out atlasSize);
        var cellWidth = hasAtlas ? atlasSize.X / AtlasColumns : 0f;
        if ((detail || target.X > cellWidth) && TryImage(ctx, DetailPath(card.Id), out var full, out var fullSize))
        {
            var (min, max) = CardLayout.CoverUv(fullSize, target, FullCrop, CentreFocus, pointer);
            dl.AddImage(full, tl, br, min, max);
            return true;
        }

        if (!hasAtlas)
        {
            return false;
        }

        var (uv0, uv1) = CardLayout.CoverUv(atlasSize, target, AtlasCell(index), CentreFocus, pointer);
        dl.AddImage(atlas, tl, br, uv0, uv1);
        return true;
    }

    /// <summary>The Gold's small picture from the race notice atlas, or false while it is not on hand.</summary>
    public static bool DrawGoldThumbnail(OsAppContext ctx, ImDrawListPtr dl, string cardId, Vector2 tl, Vector2 br, uint tint, bool packReady)
    {
        var index = GoldIndexOf(cardId);
        if (!packReady || index < 0 || !TryImage(ctx, GoldAtlasPath, out var atlas, out _))
        {
            return false;
        }

        var cell = GoldAtlasCell(index);
        var uv0 = new Vector2(cell.X, cell.Y);
        dl.AddImage(atlas, tl, br, uv0, uv0 + new Vector2(cell.Z, cell.W), tint);
        return true;
    }

    /// <summary>What the art window shows while the pack is still downloading or the picture is still
    /// decoding: an accent-tinted rect, a ring, and the element's vector crystal at a fifth of the window's
    /// shorter side. <paramref name="alpha"/> fades the whole placeholder, for a surface that fades out.</summary>
    public static void DrawPlaceholder(ImDrawListPtr dl, string element, Vector2 tl, Vector2 br, float unit, float alpha = 1f)
    {
        var accent = CardElementStyle.Accent(element);
        dl.AddRectFilled(tl, br, ElementFx.U32(accent with { W = 0.13f * alpha }));
        var center = (tl + br) * 0.5f;
        var radius = MathF.Min(br.X - tl.X, br.Y - tl.Y) * PlaceholderRadiusShare;
        var stroke = MathF.Max(1f, unit * PlaceholderStroke);
        dl.AddCircle(center, radius * PlaceholderRingScale, ElementFx.U32(accent with { W = 0.18f * alpha }), 32, stroke);
        var canvas = new DrawListCanvas(dl, center, unit);
        CardFrameDesign.Crystal(ref canvas, Vector2.Zero, radius / unit, CardElementStyle.CrystalKind(element), accent with { W = 0.55f * alpha });
        dl.AddLine(new Vector2(tl.X + (unit * PlaceholderRuleInset), br.Y - (unit * PlaceholderRuleLift)),
            new Vector2(br.X - (unit * PlaceholderRuleInset), br.Y - (unit * PlaceholderRuleLift)), ElementFx.U32(accent with { W = 0.22f * alpha }), stroke);
    }

    private static Dictionary<string, int> BuildIndex()
    {
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < RaceCardCatalogue.Cards.Count; i++)
        {
            index[RaceCardCatalogue.Cards[i].Id] = i;
        }

        return index;
    }

    private static Dictionary<string, int> BuildGoldIndex()
    {
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        var next = 0;
        foreach (var card in RaceCardCatalogue.Cards)
        {
            if (card.IsGold)
            {
                index[card.Id] = next++;
            }
        }

        return index;
    }
}
