using System;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove.Shared.Aetherling;
using AetherLove.Shared.Racing;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;

namespace AetherOS.Apps.Racer.Screens;

/// <summary>Lifetime racing counts, the ground they were run on, and the last few races. Every picture draws at its
/// painted aspect ratio (<see cref="StatsArtwork"/>) with its text fitted to the plate painted for it, and every block
/// measures its wrapped text before it reserves its height, so the page holds from the smallest phone size to the
/// largest and in the longest language.</summary>
internal sealed class StatsScreen(IRacerHost host)
{
    private const int LogLimit = 25;
    private const float Inset = 8f;
    private const float Gap = 10f;
    private const float SectionLead = 8f;
    private const float CardPad = 10f;
    private const float ModeRowInset = 20f;
    private const float ItemGap = 6f;
    private const float AchievementIcon = 40f;
    private const float ElementRow = 46f;
    private const float ElementIcon = 36f;
    private const float LogIconMin = 56f;
    private const float LogIconMax = 76f;
    private const string EmptyCell = "-";

    private static readonly Vector4 PartyChip = new(0.34f, 0.13f, 0.60f, 1f);
    private static readonly Vector4 PracticeChip = new(0.05f, 0.35f, 0.19f, 1f);

    private static readonly string[] OrdinalKeys = ["os.racer_ordinal_1", "os.racer_ordinal_2", "os.racer_ordinal_3"];

    private static readonly (short Element, string Key)[] ElementRows =
    [
        ((short)AetherlingElement.None, "os.racer_element_neutral"),
        ((short)AetherlingElement.Fire, "os.racer_element_fire"),
        ((short)AetherlingElement.Lightning, "os.racer_element_lightning"),
        ((short)AetherlingElement.Wind, "os.racer_element_wind"),
        ((short)AetherlingElement.Ice, "os.racer_element_ice"),
        ((short)AetherlingElement.Water, "os.racer_element_water"),
        ((short)AetherlingElement.Earth, "os.racer_element_earth"),
    ];

    private static readonly short[] Grades =
    [
        (short)LumiRaceDifficulty.Easy,
        (short)LumiRaceDifficulty.Normal,
        (short)LumiRaceDifficulty.Hard,
    ];

    private LumiRaceStateDto? _state;
    private LumiRaceStateDto? _pending;
    private string? _error;
    private string? _pendingError;
    private LumiRaceLogEntryDto[]? _log;
    private LumiRaceLogEntryDto[]? _pendingLog;
    private string? _logError;
    private string? _pendingLogError;
    private bool _logAsked;

    public void Draw(OsAppContext ctx)
    {
        if (_pending is { } pending)
        {
            _pending = null;
            _state = pending;
        }
        if (_pendingError is { } pendingError)
        {
            _pendingError = null;
            _error = pendingError;
        }
        if (_pendingLog is { } pendingLog)
        {
            _pendingLog = null;
            _log = pendingLog;
        }
        if (_pendingLogError is { } pendingLogError)
        {
            _pendingLogError = null;
            _logError = pendingLogError;
        }
        if (_state is null && _error is null && _pending is null)
        {
            Refresh();
        }
        if (!_logAsked)
        {
            RefreshLog();
        }

        DrawTitle(ctx);
        if (_state is { } state)
        {
            DrawScoreboard(ctx, state);
            DrawPodium(ctx, state);
            DrawAchievements(ctx, state);
            DrawMode(ctx, StatsArtwork.Party, "os.racer_log_party", "os.racer_stats_party_",
                state.PartyRaces, state.PartyWins, state.PartySeconds, state.PartyThirds);
            DrawMode(ctx, StatsArtwork.Practice, "os.racer_log_practice", "os.racer_stats_practice_",
                state.PracticeRaces, state.PracticeWins, state.PracticeSeconds, state.PracticeThirds);
            DrawElements(ctx, state.ElementCounts);
        }
        else
        {
            DrawMessageCard(ctx, _error is { Length: > 0 } error ? error : ctx.Localize("os.racer_loading"));
        }

        DrawLog(ctx);
        ImGui.Dummy(new Vector2(1f, Px(12)));
    }

    public void OnShow()
    {
        _error = null;
        _logError = null;
        Refresh();
        RefreshLog();
    }

    private void Refresh()
    {
        _error = string.Empty;
        _ = Task.Run(async () =>
        {
            try
            {
                _pending = await host.GetStateAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _pendingError = host.DescribeError(ex);
            }
        });
    }

    private void RefreshLog()
    {
        _logAsked = true;
        _ = Task.Run(async () =>
        {
            try
            {
                _pendingLog = await host.GetRaceLogAsync(LogLimit).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _pendingLogError = host.DescribeError(ex);
            }
        });
    }

    private void DrawTitle(OsAppContext ctx)
    {
        var (at, width) = Block();
        var height = DrawArt(ctx, StatsArtwork.Title, at, width, "paper");
        var (plateAt, plateSize) = StatsArtwork.Plate(StatsArtwork.Title, at, width);
        Fit(ctx, ctx.Localize("os.racer_stats"), plateAt, plateSize, GrandstandFrame.Ink, RacerTextSize.Heading, RacerTextSize.Body);
        Advance(height);
    }

    private void DrawScoreboard(OsAppContext ctx, LumiRaceStateDto state)
    {
        var (at, width) = Block();
        var height = DrawArt(ctx, StatsArtwork.Scoreboard, at, width, "dark");
        var (plateAt, plateSize) = StatsArtwork.Plate(StatsArtwork.Scoreboard, at, width);
        float figureHeight;
        float ascent;
        using (RacerFonts.Get(RacerTextSize.Heading)?.Push())
        {
            figureHeight = ImGui.GetTextLineHeight();
            ascent = ImGui.GetFont().Ascent;
        }

        var labelHeight = LineHeight(RacerTextSize.Caption);
        var top = plateAt.Y + MathF.Max(0f, (plateSize.Y - figureHeight - labelHeight) / 2f);
        GrandstandFrame.Figure(state.RacesRun.ToString(), plateAt.X + (plateSize.X / 2f), top + ascent, GrandstandFrame.Gold);
        var labelAt = new Vector2(plateAt.X, top + figureHeight);
        Fit(ctx, ctx.Localize("os.racer_stats_races"), labelAt,
            new Vector2(plateSize.X, plateAt.Y + plateSize.Y - labelAt.Y), GrandstandFrame.Cream,
            RacerTextSize.Caption, RacerTextSize.Small);
        Advance(height);
    }

    private void DrawPodium(OsAppContext ctx, LumiRaceStateDto state)
    {
        (string Art, int Value, string Label)[] medals =
        [
            (StatsArtwork.MedalGold, state.Wins, ctx.Localize("os.racer_stats_wins")),
            (StatsArtwork.MedalSilver, state.Seconds, ctx.Localize("os.racer_stats_seconds")),
            (StatsArtwork.MedalBronze, state.Thirds, ctx.Localize("os.racer_stats_thirds")),
        ];
        var (at, width) = Block();
        var gap = Px(ItemGap);
        var itemWidth = (width - (gap * 2f)) / 3f;
        var medalHeight = StatsArtwork.HeightFor(StatsArtwork.MedalGold, itemWidth);
        var labelTop = medalHeight + Px(4);
        var tallest = 0f;
        for (var i = 0; i < medals.Length; i++)
        {
            var itemAt = at + new Vector2((itemWidth + gap) * i, 0f);
            DrawArt(ctx, medals[i].Art, itemAt, itemWidth, "paper");
            var (faceAt, faceSize) = StatsArtwork.Plate(medals[i].Art, itemAt, itemWidth);
            Fit(ctx, medals[i].Value.ToString(), faceAt, faceSize, Blue(), RacerTextSize.Heading, RacerTextSize.Body);
            var labelHeight = WrappedHeight(medals[i].Label, RacerTextSize.Body, itemWidth);
            GrandstandFrame.WrappedLabel(ctx, medals[i].Label, itemAt + new Vector2(0f, labelTop),
                new Vector2(itemWidth, labelHeight), GrandstandFrame.Ink, RacerTextSize.Body);
            tallest = MathF.Max(tallest, labelHeight);
        }
        Advance(labelTop + tallest);
    }

    private void DrawAchievements(OsAppContext ctx, LumiRaceStateDto state)
    {
        var (at, width) = Block();
        var gap = Px(ItemGap);
        var pad = Px(CardPad);
        var itemWidth = (width - gap) / 2f;
        var cards = ctx.Localize("os.racer_stats_cards");
        var starred = ctx.Localize("os.racer_stats_starred");
        var rowHeight = MathF.Max(Px(AchievementIcon), LineHeight(RacerTextSize.Heading));
        var labelHeight = MathF.Max(WrappedHeight(cards, RacerTextSize.Body, itemWidth - (pad * 2f)),
            WrappedHeight(starred, RacerTextSize.Body, itemWidth - (pad * 2f)));
        var size = new Vector2(itemWidth, pad + rowHeight + Px(4) + labelHeight + pad);
        DrawAchievement(ctx, at, size, rowHeight, StatsArtwork.Cards, state.CardsCompleted, cards);
        DrawAchievement(ctx, at + new Vector2(itemWidth + gap, 0f), size, rowHeight, StatsArtwork.Star,
            state.GhostAppearances, starred);
        Advance(size.Y);
    }

    private void DrawAchievement(OsAppContext ctx, Vector2 at, Vector2 size, float rowHeight, string art, int value, string label)
    {
        GrandstandFrame.Panel(ctx, host, "paper", at, size);
        var pad = Px(CardPad);
        var number = value.ToString();
        var numberWidth = TextWidth(number, RacerTextSize.Heading);
        var iconHeight = Px(AchievementIcon);
        var iconWidth = iconHeight * StatsArtwork.Aspect(art);
        var spacing = Px(8);
        var left = at.X + MathF.Max(pad, (size.X - iconWidth - spacing - numberWidth) / 2f);
        StatsArtwork.Draw(ctx, host, art, new Vector2(left, at.Y + pad + ((rowHeight - iconHeight) / 2f)), iconWidth);
        GrandstandFrame.Label(ctx, number, new Vector2(left + iconWidth + spacing, at.Y + pad),
            new Vector2(numberWidth, rowHeight), Blue(), RacerTextSize.Heading);
        GrandstandFrame.WrappedLabel(ctx, label, new Vector2(at.X + pad, at.Y + pad + rowHeight + Px(4)),
            new Vector2(size.X - (pad * 2f), size.Y - (pad * 2f) - rowHeight - Px(4)), GrandstandFrame.Ink, RacerTextSize.Body);
    }

    /// <summary>One mode's four counts under its banner. The banner spans the block at its own aspect ratio and the
    /// paper card starts halfway down it, so the banner reads as a ribbon pinned across the card's top.</summary>
    private void DrawMode(OsAppContext ctx, string art, string titleKey, string prefix, int races, int wins, int seconds, int thirds)
    {
        (string Label, int Value)[] rows =
        [
            (ctx.Localize(prefix + "races"), races),
            (ctx.Localize(prefix + "wins"), wins),
            (ctx.Localize(prefix + "seconds"), seconds),
            (ctx.Localize(prefix + "thirds"), thirds),
        ];
        var (at, width) = Block();
        var pad = Px(ModeRowInset);
        var cardInset = Px(ItemGap);
        var bannerHeight = StatsArtwork.HeightFor(art, width);
        var valueWidth = 0f;
        foreach (var row in rows)
        {
            valueWidth = MathF.Max(valueWidth, TextWidth(row.Value.ToString(), RacerTextSize.Body));
        }

        var rowLeft = at.X + cardInset + pad;
        var labelWidth = width - ((cardInset + pad) * 2f) - valueWidth - Px(12);
        var rowPad = Px(7);
        var rowsTop = at.Y + bannerHeight + Px(2);
        var rowsHeight = 0f;
        foreach (var row in rows)
        {
            rowsHeight += WrappedHeight(row.Label, RacerTextSize.Body, labelWidth) + (rowPad * 2f);
        }

        var cardTop = at.Y + (bannerHeight / 2f);
        var bottom = rowsTop + rowsHeight + Px(CardPad);
        GrandstandFrame.Panel(ctx, host, "paper", new Vector2(at.X + cardInset, cardTop),
            new Vector2(width - (cardInset * 2f), bottom - cardTop));
        DrawArt(ctx, art, at, width, "dark");
        var (plateAt, plateSize) = StatsArtwork.Plate(art, at, width);
        Fit(ctx, ctx.Localize(titleKey), plateAt, plateSize, GrandstandFrame.Cream, RacerTextSize.Button, RacerTextSize.Caption);

        var dl = ImGui.GetWindowDrawList();
        var rule = ImGui.ColorConvertFloat4ToU32(RacerChrome.CardBlue with { W = .15f });
        var y = rowsTop;
        for (var i = 0; i < rows.Length; i++)
        {
            var labelHeight = WrappedHeight(rows[i].Label, RacerTextSize.Body, labelWidth);
            var rowHeight = labelHeight + (rowPad * 2f);
            DrawWrappedLeft(rows[i].Label, new Vector2(rowLeft, y + rowPad), labelWidth, GrandstandFrame.Ink, RacerTextSize.Body);
            var value = rows[i].Value.ToString();
            GrandstandFrame.Label(ctx, value, new Vector2(rowLeft + labelWidth + Px(12) + valueWidth - TextWidth(value, RacerTextSize.Body), y),
                new Vector2(TextWidth(value, RacerTextSize.Body), rowHeight), Blue(), RacerTextSize.Body);
            y += rowHeight;
            if (i < rows.Length - 1)
            {
                dl.AddLine(new Vector2(rowLeft, y), new Vector2(rowLeft + labelWidth + Px(12) + valueWidth, y), rule, Px(1));
            }
        }
        Advance(bottom - at.Y);
    }

    /// <summary>Races by the ground they were run on, a column per grade. A ground never raced is left out, so the
    /// table starts empty and fills in; practice races are not counted here.</summary>
    private void DrawElements(OsAppContext ctx, LumiRaceElementCountDto[]? counts)
    {
        DrawSection(ctx, "os.racer_stats_elements");
        var (at, width) = Block();
        var pad = Px(CardPad);
        var rowHeight = Px(ElementRow);
        var headHeight = LineHeight(RacerTextSize.Caption) + Px(12);
        var gradeWidth = Px(48);
        foreach (var grade in Grades)
        {
            gradeWidth = MathF.Max(gradeWidth, TextWidth(RacerChrome.DifficultyLabel(ctx, grade), RacerTextSize.Caption) + Px(10));
        }

        var visible = 0;
        foreach (var row in ElementRows)
        {
            if (TotalOf(counts, row.Element) > 0)
            {
                visible++;
            }
        }

        var height = headHeight + (rowHeight * visible) + (pad * (visible > 0 ? 1f : .5f));
        GrandstandFrame.Panel(ctx, host, "paper", at, new Vector2(width, height));
        var inner = width - (pad * 2f);
        var nameWidth = inner - rowHeight - (gradeWidth * Grades.Length);
        var gradeStart = at.X + pad + inner - (gradeWidth * Grades.Length);
        for (var i = 0; i < Grades.Length; i++)
        {
            GrandstandFrame.Label(ctx, RacerChrome.DifficultyLabel(ctx, Grades[i]),
                new Vector2(gradeStart + (gradeWidth * i), at.Y + Px(4)),
                new Vector2(gradeWidth, headHeight - Px(4)), Blue(), RacerTextSize.Caption);
        }

        var dl = ImGui.GetWindowDrawList();
        var stripe = ImGui.ColorConvertFloat4ToU32(RacerChrome.CardBlue with { W = .07f });
        var muted = ImGui.ColorConvertFloat4ToU32(RacerChrome.CardBlue with { W = .40f });
        var drawn = 0;
        foreach (var row in ElementRows)
        {
            if (TotalOf(counts, row.Element) == 0)
            {
                continue;
            }

            var rowAt = new Vector2(at.X + pad, at.Y + headHeight + (rowHeight * drawn));
            if (drawn % 2 == 0)
            {
                dl.AddRectFilled(rowAt, rowAt + new Vector2(inner, rowHeight), stripe, Px(6));
            }

            var iconSize = Px(ElementIcon);
            StatsArtwork.DrawInside(ctx, host, StatsArtwork.Element((AetherlingElement)row.Element),
                rowAt + new Vector2((rowHeight - iconSize) / 2f), new Vector2(iconSize));
            Fit(ctx, ctx.Localize(row.Key), rowAt + new Vector2(rowHeight, 0f), new Vector2(nameWidth, rowHeight),
                GrandstandFrame.Ink, RacerTextSize.Body, RacerTextSize.Small, centered: false);
            for (var i = 0; i < Grades.Length; i++)
            {
                var value = CountOf(counts, row.Element, Grades[i]);
                GrandstandFrame.Label(ctx, value == 0 ? EmptyCell : value.ToString(),
                    new Vector2(gradeStart + (gradeWidth * i), rowAt.Y), new Vector2(gradeWidth, rowHeight),
                    value == 0 ? muted : Blue(), RacerTextSize.Body);
            }
            drawn++;
        }
        Advance(height);
    }

    private void DrawLog(OsAppContext ctx)
    {
        DrawSection(ctx, "os.racer_stats_log");
        if (_logError is { Length: > 0 } error)
        {
            DrawMessageCard(ctx, error);
            return;
        }
        if (_log is not { } log)
        {
            DrawMessageCard(ctx, ctx.Localize("os.racer_loading"));
            return;
        }
        if (log.Length == 0)
        {
            DrawMessageCard(ctx, ctx.Localize("os.racer_log_empty"));
            return;
        }

        foreach (var entry in log)
        {
            DrawLogRow(ctx, entry);
        }
    }

    /// <summary>One finished race: the medallion of the ground it was run on, as tall as the text beside it, then the
    /// course, the place the player finished in, and chips for the grade and for a party or practice race.</summary>
    private void DrawLogRow(OsAppContext ctx, LumiRaceLogEntryDto entry)
    {
        var (at, width) = Block();
        var pad = Px(CardPad);
        var iconColumn = Px(LogIconMax);
        var textLeft = at.X + pad + iconColumn + Px(12);
        var textWidth = at.X + width - pad - textLeft;
        var course = ctx.Localize($"os.racer_course_{entry.CourseKey}");
        var place = Placement(ctx, entry.Place);
        var date = entry.ResolvedAtUtc.ToLocalTime().ToString("g", ctx.Culture);

        var courseHeight = WrappedHeight(course, RacerTextSize.Body, textWidth) + Px(2);
        var dateHeight = WrappedHeight(date, RacerTextSize.Small, textWidth) + Px(3);
        var placeHeight = (place is null ? LineHeight(RacerTextSize.Caption) : WrappedHeight(place, RacerTextSize.Caption, textWidth)) + Px(4);
        var chipHeight = LineHeight(RacerTextSize.Small) + Px(5);
        var chipGap = Px(5);
        (string Label, Vector4 Fill, uint Ink)[] chips = Chips(ctx, entry);
        var chipLines = 1;
        var lineWidth = 0f;
        foreach (var chip in chips)
        {
            var chipWidth = ChipWidth(chip.Label);
            if (lineWidth > 0f && lineWidth + chipWidth > textWidth)
            {
                chipLines++;
                lineWidth = 0f;
            }
            lineWidth += chipWidth + chipGap;
        }
        var chipsHeight = (chipHeight * chipLines) + (chipGap * (chipLines - 1));

        // A row with no place still reserves the line, so it stands as tall as its neighbours.
        var rowBlock = courseHeight + dateHeight + placeHeight + chipsHeight;
        var blockHeight = place is null ? rowBlock - placeHeight : rowBlock;
        var iconSize = Math.Clamp(rowBlock, Px(LogIconMin), iconColumn);
        var height = MathF.Max(rowBlock, iconSize) + (pad * 2f);

        GrandstandFrame.Panel(ctx, host, "paper", at, new Vector2(width, height));
        StatsArtwork.DrawInside(ctx, host, StatsArtwork.Element((AetherlingElement)entry.Element),
            new Vector2(at.X + pad + ((iconColumn - iconSize) / 2f), at.Y + ((height - iconSize) / 2f)), new Vector2(iconSize));

        var y = at.Y + ((height - blockHeight) / 2f);
        DrawWrappedLeft(course, new Vector2(textLeft, y), textWidth, Blue(), RacerTextSize.Body);
        y += courseHeight;
        DrawWrappedLeft(date, new Vector2(textLeft, y), textWidth,
            ImGui.ColorConvertFloat4ToU32(RacerChrome.CardBlue with { W = .65f }), RacerTextSize.Small);
        y += dateHeight;
        if (place is not null)
        {
            DrawWrappedLeft(place, new Vector2(textLeft, y), textWidth, GrandstandFrame.Ink, RacerTextSize.Caption);
            y += placeHeight;
        }

        var x = textLeft;
        foreach (var chip in chips)
        {
            if (x > textLeft && x + ChipWidth(chip.Label) > textLeft + textWidth)
            {
                x = textLeft;
                y += chipHeight + chipGap;
            }
            x += DrawChip(chip.Label, new Vector2(x, y), chipHeight, chip.Fill, chip.Ink) + chipGap;
        }
        Advance(height, 6f);
    }

    private static (string Label, Vector4 Fill, uint Ink)[] Chips(OsAppContext ctx, LumiRaceLogEntryDto entry)
    {
        var grade = (RacerChrome.DifficultyLabel(ctx, entry.Difficulty), RacerChrome.GradeFlag(entry.Difficulty),
            entry.Difficulty == (short)LumiRaceDifficulty.Easy ? Blue() : GrandstandFrame.Cream);
        var party = (ctx.Localize("os.racer_log_party"), PartyChip, GrandstandFrame.Cream);
        var practice = (ctx.Localize("os.racer_log_practice"), PracticeChip, GrandstandFrame.Cream);
        return (entry.IsParty, entry.IsPractice) switch
        {
            (true, true) => [grade, party, practice],
            (true, false) => [grade, party],
            (false, true) => [grade, practice],
            _ => [grade],
        };
    }

    /// <summary>The finishing place as a sentence, or null when the row has no place to state. The server stores
    /// places counted from zero.</summary>
    private static string? Placement(OsAppContext ctx, short place)
    {
        if (place < 0)
        {
            return null;
        }

        var finish = place + 1;
        var ordinal = finish <= OrdinalKeys.Length
            ? ctx.Localize(OrdinalKeys[finish - 1])
            : string.Format(ctx.Culture, ctx.Localize("os.racer_ordinal_n"), finish);
        return string.Format(ctx.Culture, ctx.Localize("os.racer_log_place"), ordinal);
    }

    private void DrawSection(OsAppContext ctx, string key)
    {
        ImGui.Dummy(new Vector2(1f, Px(SectionLead)));
        var (at, width) = Block();
        var height = DrawArt(ctx, StatsArtwork.Section, at, width, "dark");
        var (plateAt, plateSize) = StatsArtwork.Plate(StatsArtwork.Section, at, width);
        Fit(ctx, ctx.Localize(key), plateAt, plateSize, GrandstandFrame.Cream, RacerTextSize.Body, RacerTextSize.Small);
        Advance(height, 6f);
    }

    private void DrawMessageCard(OsAppContext ctx, string text)
    {
        var (at, width) = Block();
        var pad = Px(CardPad) * 1.5f;
        var textHeight = WrappedHeight(text, RacerTextSize.Body, width - (pad * 2f));
        var size = new Vector2(width, textHeight + (pad * 2f));
        GrandstandFrame.Panel(ctx, host, "paper", at, size);
        GrandstandFrame.WrappedLabel(ctx, text, at + new Vector2(pad), size - new Vector2(pad * 2f), GrandstandFrame.Ink, RacerTextSize.Body);
        Advance(size.Y);
    }

    /// <summary>Draws a piece of artwork <paramref name="width"/> wide, or a Grandstand panel of the same size while it
    /// downloads, and returns the height it took.</summary>
    private float DrawArt(OsAppContext ctx, string art, Vector2 at, float width, string fallbackPanel)
    {
        var height = StatsArtwork.HeightFor(art, width);
        if (!StatsArtwork.Draw(ctx, host, art, at, width))
        {
            GrandstandFrame.Panel(ctx, host, fallbackPanel, at, new Vector2(width, height));
        }
        return height;
    }

    /// <summary>Prints <paramref name="text"/> on one line at the largest size from <paramref name="largest"/> down to
    /// <paramref name="smallest"/> that fits <paramref name="room"/>, and wraps it at the smallest when none does.</summary>
    private static void Fit(OsAppContext ctx, string text, Vector2 at, Vector2 room, uint ink, RacerTextSize largest,
        RacerTextSize smallest, bool centered = true)
    {
        for (var size = largest; size >= smallest; size--)
        {
            Vector2 measured;
            using (RacerFonts.Get(size)?.Push())
            {
                measured = ImGui.CalcTextSize(text);
            }
            if (measured.X <= room.X && measured.Y <= room.Y)
            {
                GrandstandFrame.Label(ctx, text, at, room, ink, size, centered);
                return;
            }
        }
        GrandstandFrame.WrappedLabel(ctx, text, at, room, ink, smallest);
    }

    private static void DrawWrappedLeft(string text, Vector2 at, float width, uint ink, RacerTextSize size)
    {
        using var font = RacerFonts.Get(size)?.Push();
        ImGui.GetWindowDrawList().AddText(ImGui.GetFont(), ImGui.GetFontSize(),
            new Vector2(MathF.Round(at.X), MathF.Round(at.Y)), ink, text, width);
    }

    private static float DrawChip(string text, Vector2 at, float height, Vector4 fill, uint ink)
    {
        var width = ChipWidth(text);
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(at, at + new Vector2(width, height), ImGui.ColorConvertFloat4ToU32(fill), height / 2f);
        dl.AddRect(at, at + new Vector2(width, height), ImGui.ColorConvertFloat4ToU32(RacerChrome.CardBlue with { W = .30f }),
            height / 2f, ImDrawFlags.None, Px(1));
        using var font = RacerFonts.Get(RacerTextSize.Small)?.Push();
        var measured = ImGui.CalcTextSize(text);
        dl.AddText(new Vector2(MathF.Round(at.X + ((width - measured.X) / 2f)), MathF.Round(at.Y + ((height - measured.Y) / 2f))),
            ink, text);
        return width;
    }

    private static float ChipWidth(string text) => TextWidth(text, RacerTextSize.Small) + Px(14);

    private static float TextWidth(string text, RacerTextSize size)
    {
        using var font = RacerFonts.Get(size)?.Push();
        return ImGui.CalcTextSize(text).X;
    }

    private static float WrappedHeight(string text, RacerTextSize size, float width)
    {
        using var font = RacerFonts.Get(size)?.Push();
        return ImGui.CalcTextSize(text, false, width).Y;
    }

    private static float LineHeight(RacerTextSize size)
    {
        using var font = RacerFonts.Get(size)?.Push();
        return ImGui.GetTextLineHeight();
    }

    private static (Vector2 At, float Width) Block()
    {
        var inset = Px(Inset);
        return (ImGui.GetCursorScreenPos() + new Vector2(inset, 0),
            MathF.Max(Px(80), ImGui.GetContentRegionAvail().X - (inset * 2f)));
    }

    /// <summary>Moves the layout cursor past a block <paramref name="height"/> screen pixels tall and the design-pixel
    /// <paramref name="gap"/> after it.</summary>
    private static void Advance(float height, float gap = Gap)
        => ImGui.Dummy(new Vector2(1, height + Px(gap)));

    private static uint Blue() => ImGui.ColorConvertFloat4ToU32(RacerChrome.CardBlue);

    private static int TotalOf(LumiRaceElementCountDto[]? counts, short element)
    {
        var total = 0;
        foreach (var grade in Grades)
        {
            total += CountOf(counts, element, grade);
        }
        return total;
    }

    private static int CountOf(LumiRaceElementCountDto[]? counts, short element, short difficulty)
    {
        if (counts is null)
        {
            return 0;
        }
        foreach (var entry in counts)
        {
            if (entry.Element == element && entry.Difficulty == difficulty && !entry.IsPractice)
            {
                return entry.Count;
            }
        }
        return 0;
    }
}
