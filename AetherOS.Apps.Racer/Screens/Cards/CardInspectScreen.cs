using System;
using System.Numerics;
using AetherLove.Shared.Racing.Cards;
using AetherOS.Apps.Racer.Rendering;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Racer.Screens.Cards;

/// <summary>One card up close, read only: the face at reading size, the level line, the story as a flavour quote
/// under the flip button, the rules in the Album's own words, the level table, and the back on request. It changes
/// nothing; the hand is chosen on the race hand screen.</summary>
internal sealed class CardInspectScreen(IRacerHost host, Action back)
{
    private const int TabCard = 0;
    private const int TabArt = 1;
    private const int TabInfo = 2;
    private const float FaceMaxWidth = 300f;
    private const float FaceInset = 24f;
    private const float FlipSeconds = 0.3f;
    private const float OpenSeconds = 0.2f;
    private const float OpenInsetShare = 0.025f;
    private const float FlipInsetShare = 0.06f;
    private const float TabHeight = 26f;
    private const float TabGap = 6f;
    private const float SectionGap = 8f;
    private const float LineGap = 4f;
    private const float ParagraphGap = 2f;
    private const float TabRounding = 6f;
    private const float TabOutline = 2f;
    private const float Inset = 12f;
    private const float ParagraphInset = 24f;

    /// <summary>The paper panel's gold frame and shadow at its foot. The page stops scrolling above it, so no
    /// line is ever drawn over the frame or the grandstand below it.</summary>
    private const float PaperFrameInset = 16f;
    private const float PercentScale = 100f;
    private const float QuoteInset = 36f;
    private const float OrnamentHeight = 12f;
    private const float OrnamentHalfWidth = 84f;
    private const float OrnamentDiamond = 3.5f;
    private const float OrnamentGap = 6f;
    private const float OrnamentStroke = 1f;
    private const float FooterShare = 0.5f;
    private const float QuoteAlpha = 0.9f;
    private const float QuoteSlant = 0.2f;
    private const float TableRounding = 6f;
    private const float CellPadX = 14f;
    private const float CellPadY = 6f;
    private const float ColumnGap = 18f;
    private const float RuleStroke = 1f;
    private const float MarkerInset = 5f;
    private const float MarkerWidth = 3f;
    private const float BoldStroke = 1f;
    private const float HighlightAlpha = 0.9f;
    private const int LevelRows = RaceCardLevels.MaxLevel - RaceCardLevels.MinLevel + 1;
    private const uint RgbMask = 0x00FFFFFFu;
    private const uint TableFill = (GrandstandFrame.Ink & RgbMask) | 0x10000000u;
    private const uint TableEdge = (GrandstandFrame.Ink & RgbMask) | 0x66000000u;
    private const uint TableRule = (GrandstandFrame.Ink & RgbMask) | 0x33000000u;

    private readonly CardFaceRenderer _faces = new();
    private readonly CardParagraphs _paragraphs = new();
    private RaceCard? _card;
    private OwnedCard? _owned;
    private int _tab;
    private bool _showBack;
    private double _flipStarted = -1d;
    private double _opened;

    public void Show(RaceCard card, OwnedCard? owned)
    {
        _card = card;
        _owned = owned;
        _tab = TabCard;
        _showBack = false;
        _flipStarted = -1d;
        _opened = ImGui.GetTime();
    }

    public void Draw(OsAppContext ctx)
    {
        if (_card is not { } card)
        {
            back();
            return;
        }

        var owned = _owned;
        var packReady = CardArt.PackReady(ctx);
        ImGui.Dummy(new Vector2(1f, Px(SectionGap)));
        DrawTabs(ctx);
        using (RacerFonts.Get(RacerTextSize.Caption)?.Push())
        {
            RacerChrome.CenteredWrapped(CardStrings.TypeLine(ctx, card));
        }

        ImGui.Dummy(new Vector2(1f, Px(LineGap)));
        var avail = ImGui.GetContentRegionAvail();
        var scrollSize = new Vector2(avail.X, MathF.Max(1f, avail.Y - Px(PaperFrameInset)));
        using var scroll = ImRaii.Child("##inspectScroll", scrollSize, false, ImGuiWindowFlags.NoBackground);
        if (!scroll)
        {
            return;
        }

        if (_tab != TabInfo)
        {
            DrawFace(ctx, card, owned, packReady);
        }

        if (_tab != TabInfo && owned is not null)
        {
            DrawLevelLine(ctx, owned);
        }

        if (_tab != TabInfo)
        {
            if (GrandstandFrame.ActionButton(ctx, host, "##inspectFlip", ctx.Localize("os.racer_inspect_flip"), secondary: true, secondaryIcon: FontAwesomeIcon.Sync))
            {
                _showBack = !_showBack;
                _flipStarted = ImGui.GetTime();
            }

            ImGui.Dummy(new Vector2(1f, Px(SectionGap)));
            if (!_showBack)
            {
                DrawStory(ctx, card);
                ImGui.Dummy(new Vector2(1f, Px(SectionGap)));
            }
        }

        DrawText(ctx, card, owned);
        ImGui.Dummy(new Vector2(1f, Px(SectionGap)));
        if (GrandstandFrame.ActionButton(ctx, host, "##inspectBack", ctx.Localize("os.racer_back"), secondary: true))
        {
            back();
        }

        ImGui.Dummy(new Vector2(1f, Px(SectionGap)));
    }

    private void DrawTabs(OsAppContext ctx)
    {
        string[] labels = [ctx.Localize("os.racer_inspect_tab_card"), ctx.Localize("os.racer_inspect_tab_art"), ctx.Localize("os.racer_inspect_tab_info")];
        var avail = ImGui.GetContentRegionAvail().X;
        var inset = Px(Inset);
        var gap = Px(TabGap);
        var width = (avail - (inset * 2f) - (gap * 2f)) / labels.Length;
        var at = ImGui.GetCursorScreenPos() + new Vector2(inset, 0f);
        var dl = ImGui.GetWindowDrawList();
        for (var tab = 0; tab < labels.Length; tab++)
        {
            var tl = at + new Vector2(tab * (width + gap), 0f);
            var size = new Vector2(width, Px(TabHeight));
            if (CardChrome.Chip(ctx, "##inspectTab" + tab, labels[tab], tl, size, true))
            {
                _tab = tab;
            }

            if (_tab == tab)
            {
                dl.AddRect(tl, tl + size, GrandstandFrame.Gold, Px(TabRounding), ImDrawFlags.None, Px(TabOutline));
            }
        }

        ImGui.SetCursorScreenPos(at + new Vector2(-inset, Px(TabHeight) + Px(TabGap)));
        ImGui.Dummy(new Vector2(1f, 1f));
    }

    private void DrawFace(OsAppContext ctx, RaceCard card, OwnedCard? owned, bool packReady)
    {
        var avail = ImGui.GetContentRegionAvail();
        var width = MathF.Max(1f, avail.X - Px(FaceInset));
        var flip = ctx.ReduceMotion || _flipStarted < 0d ? 1f : Math.Clamp((float)(ImGui.GetTime() - _flipStarted) / FlipSeconds, 0f, 1f);
        var showBack = flip < 0.5f ? !_showBack : _showBack;
        var artOnly = _tab == TabArt && !showBack;
        var displayWidth = artOnly ? width : MathF.Min(width, Px(FaceMaxWidth));
        var size = new Vector2(displayWidth, displayWidth * (artOnly ? 1f : CardFaceLayout.Ratio));
        var at = ImGui.GetCursorScreenPos() + new Vector2((avail.X - displayWidth) * 0.5f, Px(SectionGap));
        var ease = ctx.ReduceMotion ? 1f : Math.Clamp((float)(ImGui.GetTime() - _opened) / OpenSeconds, 0f, 1f);
        var inset = size * (OpenInsetShare * (1f - ease));
        if (flip < 1f)
        {
            inset = size * (FlipInsetShare * MathF.Sin(flip * MathF.PI));
        }

        ImGui.SetCursorScreenPos(at);
        ImGui.InvisibleButton("##inspectFace", size);
        var hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        if (showBack)
        {
            CardFaceRenderer.DrawBack(ctx, dl, at + inset, size - (inset * 2f), hovered);
        }
        else if (artOnly)
        {
            CardFaceRenderer.DrawArtOnly(ctx, dl, card, at, size, packReady);
        }
        else
        {
            _faces.Draw(ctx, dl, "inspect", card, owned?.Level ?? RaceCardLevels.MinLevel, at + inset, size - (inset * 2f), true, true, packReady);
        }

        ImGui.SetCursorScreenPos(new Vector2(at.X - ((avail.X - displayWidth) * 0.5f), at.Y + size.Y + Px(SectionGap)));
        ImGui.Dummy(new Vector2(1f, 1f));
    }

    /// <summary>The line under the face on the Card and Art tabs: the level and the effect bonus it gives. The Info
    /// tab leaves it out, because its level table says both.</summary>
    private static void DrawLevelLine(OsAppContext ctx, OwnedCard owned)
    {
        var level = owned.Level;
        var levelText = level >= RaceCardLevels.MaxLevel ? ctx.Localize("os.racer_card_level_max") : string.Format(ctx.Localize("os.racer_card_level"), level);
        var bonus = BonusPercent(level);
        var line = bonus > 0 ? levelText + CardStrings.Separator + string.Format(ctx.Localize("os.racer_card_effect_bonus"), bonus) : levelText;
        using var font = RacerFonts.Get(RacerTextSize.Caption)?.Push();
        RacerChrome.CenteredWrapped(line);
        ImGui.Dummy(new Vector2(1f, Px(LineGap)));
    }

    private static int BonusPercent(int level) => (int)MathF.Round((RaceCardLevels.Scale(level) - 1f) * PercentScale);

    /// <summary>A level table cell: the card's own bonus where the level's scale adds nothing, otherwise the
    /// added percentage.</summary>
    private static string BonusText(int level, string bonusFormat, string defaultBonus)
    {
        var bonus = BonusPercent(level);
        return bonus > 0 ? string.Format(bonusFormat, bonus) : defaultBonus;
    }

    private void DrawText(OsAppContext ctx, RaceCard card, OwnedCard? owned)
    {
        if (_tab == TabArt || (_showBack && _tab != TabInfo))
        {
            return;
        }

        Paragraph(ctx.Localize(CardStrings.Key(card.Id, CardStrings.Short)));
        Heading(ctx, "os.racer_inspect_when");
        Paragraph(ctx.Localize(CardStrings.Key(card.Id, CardStrings.Condition)));
        Paragraph(ctx.Localize(CardStrings.Key(card.Id, CardStrings.Description)));
        Heading(ctx, "os.racer_inspect_how");
        if (Localization.RaceCardGuards.KeyFor(card.Id) is { } guard)
        {
            Paragraph(ctx.Localize(guard));
        }

        Heading(ctx, "os.racer_inspect_exact");
        Paragraph(ctx.Localize(CardStrings.Key(card.Id, CardStrings.Exact)));
        Heading(ctx, "os.racer_inspect_levels");
        ImGui.Dummy(new Vector2(1f, Px(LineGap)));
        DrawLevelTable(ctx, card, owned);
    }

    /// <summary>The story as a trading card's flavour line: the words centred between two thin ornaments in the
    /// card's metal, slanted like italic type in the metal's deep ink, wrapped at spaces. The racer fonts have no
    /// italic face, so the slant shears the glyph vertices around each line's middle.</summary>
    private void DrawStory(OsAppContext ctx, RaceCard card)
    {
        var metal = CardFramePalette.For(card.Band);
        var width = ImGui.GetWindowWidth();
        var left = ImGui.GetWindowPos().X;
        var dl = ImGui.GetWindowDrawList();
        Ornament(dl, metal, new Vector2(left + (width * 0.5f), ImGui.GetCursorScreenPos().Y + (Px(OrnamentHeight) * 0.5f)), width, true);
        ImGui.Dummy(new Vector2(1f, Px(OrnamentHeight)));

        using (RacerFonts.Get(RacerTextSize.Body)?.Push())
        {
            var lines = _paragraphs.Lines(ctx.Localize(CardStrings.Key(card.Id, CardStrings.Story)), MathF.Max(1f, width - (Px(QuoteInset) * 2f)));
            var lineHeight = ImGui.GetTextLineHeight();
            var ink = ElementFx.U32(metal.Deep with { W = QuoteAlpha });
            var top = ImGui.GetCursorScreenPos().Y;
            for (var i = 0; i < lines.Length; i++)
            {
                var at = new Vector2(MathF.Round(left + ((width - ImGui.CalcTextSize(lines[i]).X) * 0.5f)), MathF.Round(top + (i * lineHeight)));
                var middle = at.Y + (lineHeight * 0.5f);
                var first = dl.VtxBuffer.Size;
                dl.AddText(at, ink, lines[i]);
                for (var v = first; v < dl.VtxBuffer.Size; v++)
                {
                    var vert = dl.VtxBuffer[v];
                    vert.Pos.X += (middle - vert.Pos.Y) * QuoteSlant;
                    dl.VtxBuffer[v] = vert;
                }
            }

            ImGui.Dummy(new Vector2(1f, lines.Length * lineHeight));
        }

        Ornament(dl, metal, new Vector2(left + (width * 0.5f), ImGui.GetCursorScreenPos().Y + (Px(OrnamentHeight) * 0.5f)), width, false);
        ImGui.Dummy(new Vector2(1f, Px(OrnamentHeight)));
    }

    /// <summary>A hairline that fades out at both ends, centred on <paramref name="centre"/>, with a small
    /// diamond in the gap when <paramref name="diamond"/> is set.</summary>
    private static void Ornament(ImDrawListPtr dl, CardFramePalette metal, Vector2 centre, float pageWidth, bool diamond)
    {
        var half = MathF.Min(Px(diamond ? OrnamentHalfWidth : OrnamentHalfWidth * FooterShare), (pageWidth * 0.5f) - Px(QuoteInset));
        var size = Px(OrnamentDiamond);
        var inner = diamond ? size + Px(OrnamentGap) : 0f;
        var stroke = MathF.Max(1f, Px(OrnamentStroke));
        var solid = ElementFx.U32(metal.Mid);
        var clear = ElementFx.U32(metal.Mid with { W = 0f });
        if (half > inner)
        {
            var top = MathF.Round(centre.Y - (stroke * 0.5f));
            dl.AddRectFilledMultiColor(new Vector2(centre.X - half, top), new Vector2(centre.X - inner, top + stroke), clear, solid, solid, clear);
            dl.AddRectFilledMultiColor(new Vector2(centre.X + inner, top), new Vector2(centre.X + half, top + stroke), solid, clear, clear, solid);
        }

        if (diamond)
        {
            dl.AddQuadFilled(centre - new Vector2(0f, size), centre + new Vector2(size, 0f), centre + new Vector2(0f, size), centre - new Vector2(size, 0f), solid);
        }
    }

    /// <summary>One row per level with the effect bonus that level gives, read from <see cref="RaceCardLevels"/>.
    /// The row of the level the player owns the card at is filled in the card's metal and printed heavier; for a card
    /// the player does not own no row is.</summary>
    private void DrawLevelTable(OsAppContext ctx, RaceCard card, OwnedCard? owned)
    {
        using var font = RacerFonts.Get(RacerTextSize.Body)?.Push();
        var left = Px(ParagraphInset);
        var tableWidth = MathF.Max(1f, ImGui.GetWindowWidth() - (left * 2f));
        var padX = Px(CellPadX);
        var padY = Px(CellPadY);
        var lineHeight = ImGui.GetTextLineHeight();
        var labelFormat = ctx.Localize("os.racer_card_level");
        var bonusFormat = ctx.Localize("os.racer_inspect_level_bonus");
        var defaultBonus = ctx.Localize("os.racer_inspect_level_default");
        Span<float> heights = stackalloc float[LevelRows];
        var labelWidth = 0f;
        for (var i = 0; i < LevelRows; i++)
        {
            labelWidth = MathF.Max(labelWidth, ImGui.CalcTextSize(string.Format(labelFormat, RaceCardLevels.MinLevel + i)).X);
        }

        var columnX = padX + labelWidth + (Px(ColumnGap) * 0.5f);
        var valueLeft = padX + labelWidth + Px(ColumnGap);
        var valueWidth = MathF.Max(1f, tableWidth - valueLeft - padX);
        var total = 0f;
        for (var i = 0; i < LevelRows; i++)
        {
            var lines = _paragraphs.Lines(BonusText(RaceCardLevels.MinLevel + i, bonusFormat, defaultBonus), valueWidth);
            heights[i] = (MathF.Max(1, lines.Length) * lineHeight) + (padY * 2f);
            total += heights[i];
        }

        ImGui.SetCursorPosX(left);
        var tl = ImGui.GetCursorScreenPos();
        var br = tl + new Vector2(tableWidth, total);
        var rounding = Px(TableRounding);
        var rule = MathF.Max(1f, Px(RuleStroke));
        var bold = MathF.Max(1f, MathF.Round(Px(BoldStroke)));
        var metal = CardFramePalette.For(card.Band);
        var mineLevel = owned?.Level ?? 0;
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(tl, br, TableFill, rounding);
        var y = tl.Y;
        for (var i = 0; i < LevelRows; i++)
        {
            var level = RaceCardLevels.MinLevel + i;
            var rowTl = new Vector2(tl.X, y);
            var rowBr = new Vector2(br.X, y + heights[i]);
            var mine = level == mineLevel;
            if (mine)
            {
                var corners = i == 0 ? ImDrawFlags.RoundCornersTop : i == LevelRows - 1 ? ImDrawFlags.RoundCornersBottom : ImDrawFlags.RoundCornersNone;
                dl.AddRectFilled(rowTl, rowBr, ElementFx.U32(metal.Plaque with { W = HighlightAlpha }), rounding, corners);
                var markerTl = rowTl + new Vector2(Px(MarkerInset), Px(MarkerInset));
                dl.AddRectFilled(markerTl, new Vector2(markerTl.X + Px(MarkerWidth), rowBr.Y - Px(MarkerInset)), ElementFx.U32(metal.Deep), Px(MarkerWidth) * 0.5f);
            }

            if (i > 0)
            {
                dl.AddLine(rowTl, new Vector2(br.X, y), TableRule, rule);
            }

            var ink = mine ? ElementFx.U32(metal.Lettering) : GrandstandFrame.Ink;
            var textY = MathF.Round(y + padY);
            TableText(dl, new Vector2(MathF.Round(tl.X + padX), textY), ink, string.Format(labelFormat, level), mine, bold);
            var lines = _paragraphs.Lines(BonusText(level, bonusFormat, defaultBonus), valueWidth);
            for (var line = 0; line < lines.Length; line++)
            {
                TableText(dl, new Vector2(MathF.Round(tl.X + valueLeft), textY + (line * lineHeight)), ink, lines[line], mine, bold);
            }

            y += heights[i];
        }

        dl.AddLine(new Vector2(tl.X + columnX, tl.Y), new Vector2(tl.X + columnX, br.Y), TableRule, rule);
        dl.AddRect(tl, br, TableEdge, rounding, ImDrawFlags.None, rule);
        ImGui.Dummy(new Vector2(tableWidth, total));
    }

    private static void TableText(ImDrawListPtr dl, Vector2 at, uint ink, string text, bool heavy, float stroke)
    {
        dl.AddText(at, ink, text);
        if (heavy)
        {
            dl.AddText(at + new Vector2(stroke, 0f), ink, text);
        }
    }

    private static void Heading(OsAppContext ctx, string key)
    {
        ImGui.Dummy(new Vector2(1f, Px(SectionGap)));
        using var font = RacerFonts.Get(RacerTextSize.Button)?.Push();
        RacerChrome.CenteredWrapped(ctx.Localize(key));
    }

    /// <summary>A rules paragraph at the page's text inset. It wraps only at spaces, so a number is never
    /// split across two lines.</summary>
    private void Paragraph(string text)
    {
        using var font = RacerFonts.Get(RacerTextSize.Body)?.Push();
        var inset = Px(ParagraphInset);
        _paragraphs.Draw(text, inset, ImGui.GetWindowWidth() - (inset * 2f));
        ImGui.Dummy(new Vector2(1f, Px(ParagraphGap)));
    }
}
