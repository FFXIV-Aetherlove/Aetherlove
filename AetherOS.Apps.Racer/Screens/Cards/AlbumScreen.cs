using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove.Shared.Racing;
using AetherLove.Shared.Racing.Cards;
using AetherOS.Apps.Racer.Rendering;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Racer.Screens.Cards;

/// <summary>The Bonus tab: the player's racing card collection, its filters and the grid, one tile per owned card
/// and a card back for every card not found yet. The hand is chosen on its own screen before a race, so this page
/// knows nothing about it. Hub replies park in fields and are drained at the top of <see cref="Draw"/>.</summary>
internal sealed class AlbumScreen(IRacerHost host, IAppStorage storage, Action<RaceCard, OwnedCard> openInspect)
{
    private const string PerRowKey = "racer.albumCardsPerRow";
    /// <summary>Two cards a row until the player picks another count, which is then remembered.</summary>
    private const int DefaultPerRow = 2;
    private const int NotBuilt = -1;
    private const int NoAnchor = -1;
    private const float Inset = 12f;
    private const float MessageTop = 12f;
    private const float LineGap = 4f;
    private const float SectionGap = 6f;
    private const float FilterGap = 6f;
    private const float FieldRounding = 6f;
    private const float FooterHeight = 40f;
    private const float GridMinHeight = 60f;
    private const float GridGap = 8f;
    private const int SearchLength = 96;
    private const float TooltipWrap = 280f;

    private sealed record Tile(RaceCard Card, OwnedCard? Owned);

    private readonly CardFaceRenderer _faces = new();
    private readonly CardParagraphs _tooltipLines = new();
    private readonly List<Tile> _tiles = [];
    private LumiRaceCardsDto? _cards;
    private LumiRaceCardsDto? _pendingCards;
    private string? _pendingError;
    private string? _error;
    private Dictionary<string, OwnedCard> _owned = new(StringComparer.Ordinal);
    private string _search = string.Empty;
    private int _element;
    private int _band;
    private int _perRow = CardsPerRow.Load(storage, PerRowKey, DefaultPerRow);
    private int _anchorIndex = NoAnchor;
    private float _lastScroll;
    private float _lastPitch;
    private int _lastColumns;
    private string? _filterKey;
    private bool _resetScroll;
    private int _version;
    private int _builtVersion = NotBuilt;

    public void OnShow()
    {
        _error = null;
        _ = Task.Run(async () =>
        {
            try
            {
                _pendingCards = await host.GetCardsAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _pendingError = host.DescribeError(ex);
            }
        });
    }

    public void Draw(OsAppContext ctx)
    {
        Drain();
        var avail = ImGui.GetContentRegionAvail();
        if (_error is { } failed && _cards is null)
        {
            RacerChrome.CenteredWrapped(failed);
            if (GrandstandFrame.ActionButton(ctx, host, "##albumRetry", ctx.Localize("os.racer_cup_retry")))
            {
                OnShow();
            }

            return;
        }

        if (_cards is not { } cards)
        {
            ImGui.Dummy(new Vector2(1f, avail.Y * 0.4f));
            RacerChrome.CenteredText(ctx.Localize("os.racer_loading"));
            return;
        }

        if (!cards.Enabled)
        {
            ImGui.Dummy(new Vector2(1f, Px(MessageTop)));
            RacerChrome.CenteredWrapped(ctx.Localize("os.racer_album_off"));
            return;
        }

        if (_owned.Count == 0)
        {
            DrawEmpty(ctx);
        }

        DrawFilters(ctx);
        var gridHeight = MathF.Max(Px(GridMinHeight), ImGui.GetContentRegionAvail().Y - Px(FooterHeight));
        DrawGrid(ctx, CardArt.PackReady(ctx), gridHeight);
        DrawFooter(ctx);
    }

    private void Drain()
    {
        if (_pendingCards is { } fresh)
        {
            _pendingCards = null;
            _cards = fresh;
            _owned = OwnedCard.Collect(fresh);
            _version++;
        }

        if (_pendingError is { } error)
        {
            _pendingError = null;
            _error = error;
        }
    }

    private static void DrawEmpty(OsAppContext ctx)
    {
        using var font = RacerFonts.Get(RacerTextSize.Caption)?.Push();
        RacerChrome.CenteredWrapped(ctx.Localize("os.racer_album_none"));
        ImGui.Dummy(new Vector2(1f, Px(LineGap)));
        RacerChrome.CenteredWrapped(ctx.Localize("os.racer_album_intro"));
        ImGui.Dummy(new Vector2(1f, Px(SectionGap)));
    }

    private void DrawFilters(OsAppContext ctx)
    {
        var avail = ImGui.GetContentRegionAvail().X;
        var inset = Px(Inset);
        var gap = Px(FilterGap);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + inset);
        using var text = ImRaii.PushColor(ImGuiCol.Text, ImGui.ColorConvertFloat4ToU32(CardChrome.FieldInk));
        using var frame = ImRaii.PushColor(ImGuiCol.FrameBg, ImGui.ColorConvertFloat4ToU32(CardChrome.FieldFill));
        using var frameHover = ImRaii.PushColor(ImGuiCol.FrameBgHovered, ImGui.ColorConvertFloat4ToU32(CardChrome.FieldFillHover));
        using var frameActive = ImRaii.PushColor(ImGuiCol.FrameBgActive, ImGui.ColorConvertFloat4ToU32(CardChrome.FieldFillHover));
        using var border = ImRaii.PushColor(ImGuiCol.Border, ImGui.ColorConvertFloat4ToU32(CardChrome.FieldEdge));
        using var rounding = ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, Px(FieldRounding));
        using var borderSize = ImRaii.PushStyle(ImGuiStyleVar.FrameBorderSize, Px(1));
        using var font = RacerFonts.Get(RacerTextSize.Caption)?.Push();
        ImGui.SetNextItemWidth(avail - (inset * 2f));
        ImGui.InputTextWithHint("##albumSearch", ctx.Localize("os.racer_album_search"), ref _search, SearchLength);
        HandOnHover();

        var third = (avail - (inset * 2f) - (gap * 2f)) / 3f;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + inset);
        ImGui.SetNextItemWidth(third);
        var elementLabel = _element == 0 ? ctx.Localize("os.racer_album_filter_elements") : CardStrings.ElementName(ctx, RacingElements.WheelOrder[_element - 1]);
        if (ImGui.BeginCombo("##albumElement", elementLabel))
        {
            using var popupInk = ImRaii.PushColor(ImGuiCol.Text, GrandstandFrame.Cream);
            if (ImGui.Selectable(ctx.Localize("os.racer_album_filter_elements"), _element == 0))
            {
                _element = 0;
            }

            for (var i = 0; i < RacingElements.WheelOrder.Length; i++)
            {
                if (ImGui.Selectable(CardStrings.ElementName(ctx, RacingElements.WheelOrder[i]), _element == i + 1))
                {
                    _element = i + 1;
                }
            }

            ImGui.EndCombo();
        }

        HandOnHover();
        ImGui.SameLine(0f, gap);
        ImGui.SetNextItemWidth(third);
        string[] bands = [ctx.Localize("os.racer_album_filter_cards"), ctx.Localize("os.racer_album_filter_gold"), ctx.Localize("os.racer_album_filter_silver")];
        if (ImGui.BeginCombo("##albumBand", bands[_band]))
        {
            using var popupInk = ImRaii.PushColor(ImGuiCol.Text, GrandstandFrame.Cream);
            for (var i = 0; i < bands.Length; i++)
            {
                if (ImGui.Selectable(bands[i], _band == i))
                {
                    _band = i;
                }
            }

            ImGui.EndCombo();
        }

        HandOnHover();
        ImGui.SameLine(0f, gap);
        var chooserAt = ImGui.GetCursorScreenPos();
        var chooserSize = new Vector2(third, ImGui.GetFrameHeight());
        var perRow = CardsPerRow.DrawChooser(ctx, "##albumPerRow", _perRow, chooserAt, chooserSize);
        ImGui.SetCursorScreenPos(chooserAt);
        ImGui.Dummy(chooserSize);
        if (perRow != _perRow)
        {
            _anchorIndex = CardsPerRow.TopIndex(_lastScroll, _lastPitch, _lastColumns);
            _perRow = perRow;
            CardsPerRow.Save(storage, PerRowKey, perRow);
        }

        ImGui.Dummy(new Vector2(1f, Px(LineGap)));
    }

    private void DrawGrid(OsAppContext ctx, bool packReady, float height)
    {
        var key = _search + "|" + _element + "|" + _band;
        if (key != _filterKey)
        {
            _filterKey = key;
            _resetScroll = true;
            _builtVersion = NotBuilt;
        }

        if (_builtVersion != _version)
        {
            _builtVersion = _version;
            BuildTiles(ctx);
        }

        var inset = Px(Inset);
        var avail = ImGui.GetContentRegionAvail().X;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (inset * 0.5f));
        using var grid = ImRaii.Child("##albumGrid", new Vector2(avail - inset, height), false, ImGuiWindowFlags.NoBackground);
        if (!grid)
        {
            return;
        }

        var scroll = ImGui.GetScrollY();
        if (_resetScroll)
        {
            ImGui.SetScrollY(0f);
            scroll = 0f;
            _resetScroll = false;
            _anchorIndex = NoAnchor;
        }

        if (_tiles.Count == 0)
        {
            ImGui.Dummy(new Vector2(1f, Px(MessageTop)));
            using var font = RacerFonts.Get(RacerTextSize.Caption)?.Push();
            RacerChrome.CenteredWrapped(ctx.Localize("os.racer_album_no_match"));
            return;
        }

        var gap = Px(GridGap);
        var width = MathF.Max(1f, ImGui.GetContentRegionAvail().X - Px(2));
        var viewport = ImGui.GetWindowSize().Y;
        var columns = _perRow;
        var style = CardsPerRow.StyleFor(columns);
        var cellWidth = (width - (gap * (columns - 1))) / columns;
        var cardWidth = MathF.Max(1f, MathF.Min(cellWidth, (viewport - (Px(LineGap) * 2f)) / CardFaceLayout.Ratio));
        var cardHeight = cardWidth * CardFaceLayout.Ratio;
        var pitch = cardHeight + gap;
        if (_anchorIndex != NoAnchor)
        {
            scroll = (Math.Min(_anchorIndex, _tiles.Count - 1) / columns) * pitch;
            ImGui.SetScrollY(scroll);
            _anchorIndex = NoAnchor;
        }

        _lastScroll = scroll;
        _lastPitch = pitch;
        _lastColumns = columns;
        var origin = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        var (first, end) = CardLayout.VisibleRows(_tiles.Count, columns, pitch, scroll, viewport);
        for (var row = first; row < end; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var index = (row * columns) + column;
                if (index >= _tiles.Count)
                {
                    break;
                }

                var tl = origin + new Vector2((column * (cellWidth + gap)) + ((cellWidth - cardWidth) * 0.5f), row * pitch);
                DrawTile(ctx, dl, _tiles[index], tl, new Vector2(cardWidth, cardHeight), packReady, style);
            }
        }

        var rows = (_tiles.Count + columns - 1) / columns;
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, MathF.Max(0f, (rows * pitch) - gap) + Px(LineGap)));
    }

    private void BuildTiles(OsAppContext ctx)
    {
        _tiles.Clear();
        var element = _element == 0 ? null : RacingElements.WheelOrder[_element - 1];
        var needle = _search.Trim();
        foreach (var card in RaceCardCatalogue.Cards)
        {
            if (element is not null && card.Element != element)
            {
                continue;
            }

            if (_band == 1 && !card.IsGold)
            {
                continue;
            }

            if (_band == 2 && card.IsGold)
            {
                continue;
            }

            if (needle.Length > 0 && !CardStrings.NameOf(ctx, card).Contains(needle, StringComparison.OrdinalIgnoreCase)
                && !card.Name.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            _tiles.Add(new Tile(card, _owned.GetValueOrDefault(card.Id)));
        }
    }

    private void DrawTile(OsAppContext ctx, ImDrawListPtr dl, Tile tile, Vector2 tl, Vector2 size, bool packReady, CardTileStyle style)
    {
        ImGui.SetCursorScreenPos(tl);
        if (tile.Owned is not { } owned)
        {
            ImGui.InvisibleButton("##lock" + tile.Card.Id, size);
            var lockedHover = ImGui.IsItemHovered();
            CardFaceRenderer.DrawBack(ctx, dl, tl, size, lockedHover);
            if (lockedHover)
            {
                CardChrome.Tooltip(ctx.Localize("os.racer_album_not_found"), ctx.Localize("os.racer_card_locked"));
            }

            return;
        }

        var id = "##card" + owned.Card.Id;
        var pressed = ImGui.InvisibleButton(id, size);
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            HandOnHover();
            using var colours = CardChrome.TooltipColours();
            using var tooltip = AetherLove.UI.UiTooltip.Begin();
            var left = ImGui.GetCursorPosX();
            var wrap = Px(TooltipWrap);
            ImGui.TextUnformatted(CardStrings.NameOf(ctx, tile.Card));
            _tooltipLines.Draw(CardStrings.TypeLine(ctx, tile.Card), left, wrap);
            _tooltipLines.Draw(ctx.Localize(CardStrings.Key(tile.Card.Id, CardStrings.Condition)), left, wrap);
            _tooltipLines.Draw(ctx.Localize(CardStrings.Key(tile.Card.Id, CardStrings.Description)), left, wrap);
        }

        _faces.Draw(ctx, dl, id, tile.Card, owned.Level, tl, size, false, hovered, packReady, style.FaceTextGrowth);
        if (pressed)
        {
            openInspect(owned.Card, owned);
        }
    }

    private void DrawFooter(OsAppContext ctx)
    {
        var text = string.Format(ctx.Localize("os.racer_album_progress"), _owned.Count, RaceCardCatalogue.CardCount);
        var at = ImGui.GetCursorScreenPos();
        GrandstandFrame.Label(ctx, text, at, new Vector2(ImGui.GetContentRegionAvail().X, Px(FooterHeight)), ImGui.GetColorU32(ImGuiCol.Text), RacerTextSize.Caption);
    }
}
