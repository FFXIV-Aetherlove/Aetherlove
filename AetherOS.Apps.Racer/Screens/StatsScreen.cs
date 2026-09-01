using System;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove.Shared.Aetherling;
using AetherLove.Shared.Racing;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Racer.Screens;

/// <summary>Lifetime racing counts, the ground they were run on, and the last few races. Counts only:
/// the race never shows a time, a score or a rating.</summary>
internal sealed class StatsScreen(IRacerHost host, Action back, Func<bool> muted, Action toggleMute, Func<float> volume, Action<float> setVolume)
{
    private const int LogLimit = 25;

    /// <summary>The buckets the element table lists, neutral ground first and then the wheel.</summary>
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

        var avail = ImGui.GetContentRegionAvail();
        using var body = ImRaii.Child("##racerStats", avail, false, ImGuiWindowFlags.NoScrollbar);
        if (!body)
        {
            return;
        }

        RacerBackdrop.DrawCard(ctx, host, ImGui.GetWindowPos(), ImGui.GetWindowSize(), dim: 0f);
        RacerChrome.DrawMuteChip(ctx, muted(), toggleMute, volume(), setVolume);

        // The art carries the page's own heading, so no title is drawn; the numbers live in the
        // window the frame leaves between the banner and the podium, in the card's own ink.
        var window = RacerBackdrop.CardWindow(ImGui.GetWindowSize());
        var backHeight = Px(34 + 14);
        ImGui.SetCursorPosY(window.Top);
        using var ink = ImRaii.PushColor(ImGuiCol.Text, RacerChrome.CardBlue);
        using var rule = ImRaii.PushColor(ImGuiCol.Separator, RacerChrome.CardBlue with { W = 0.25f });
        using (var content = ImRaii.Child("##racerStatsBody",
            new Vector2(avail.X, window.Bottom - window.Top - backHeight), false))
        {
            if (content)
            {
                DrawContent(ctx);
            }
        }

        ImGui.Dummy(new Vector2(1f, Px(8)));
        using var buttonInk = ImRaii.PushColor(ImGuiCol.Text, new Vector4(1f, 1f, 1f, 1f));
        if (RacerChrome.FlagButton(ctx, "##racerStatsBack", ctx.Localize("os.racer_back"),
            RacerChrome.CardBlue, RacerChrome.WhiteInk))
        {
            _state = null;
            _error = null;
            _log = null;
            _logError = null;
            _logAsked = false;
            back();
        }
    }

    private void DrawContent(OsAppContext ctx)
    {
        if (_state is { } state)
        {
            Row(ctx.Localize("os.racer_stats_races"), state.RacesRun);
            Row(ctx.Localize("os.racer_stats_wins"), state.Wins);
            Row(ctx.Localize("os.racer_stats_seconds"), state.Seconds);
            Row(ctx.Localize("os.racer_stats_thirds"), state.Thirds);
            Row(ctx.Localize("os.racer_stats_cards"), state.CardsCompleted);
            Row(ctx.Localize("os.racer_stats_starred"), state.GhostAppearances);
            Row(ctx.Localize("os.racer_stats_party_races"), state.PartyRaces);
            Row(ctx.Localize("os.racer_stats_party_wins"), state.PartyWins);
            Row(ctx.Localize("os.racer_stats_party_seconds"), state.PartySeconds);
            Row(ctx.Localize("os.racer_stats_party_thirds"), state.PartyThirds);
            Row(ctx.Localize("os.racer_stats_practice_races"), state.PracticeRaces);
            Row(ctx.Localize("os.racer_stats_practice_wins"), state.PracticeWins);
            Row(ctx.Localize("os.racer_stats_practice_seconds"), state.PracticeSeconds);
            Row(ctx.Localize("os.racer_stats_practice_thirds"), state.PracticeThirds);
            DrawElements(ctx, state.ElementCounts);
        }
        else if (_error is { Length: > 0 } error)
        {
            Centered(error);
        }
        else
        {
            Centered(ctx.Localize("os.racer_loading"));
        }

        DrawLog(ctx);
        ImGui.Dummy(new Vector2(1f, Px(10)));
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

    /// <summary>Fetches the history. The continuation parks its result and never reads or writes what
    /// the page draws with; the draw thread picks it up at the top of the next frame.</summary>
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

    /// <summary>Races by the ground they were run on, three grades wide. A bucket the player has never
    /// raced is left out, so the table starts empty and fills in as they race.</summary>
    private void DrawElements(OsAppContext ctx, LumiRaceElementCountDto[]? counts)
    {
        Section(ctx.Localize("os.racer_stats_elements"));

        ImGui.SetCursorPosX(Px(28));
        ImGui.Dummy(new Vector2(1f, ImGui.GetTextLineHeight()));
        using (ImRaii.PushColor(ImGuiCol.Text, MutedInk()))
        {
            for (var i = 0; i < Grades.Length; i++)
            {
                ColumnText(RacerChrome.DifficultyLabel(ctx, Grades[i]), i);
            }
        }
        ImGui.Dummy(new Vector2(1f, Px(4)));

        Span<int> values = stackalloc int[Grades.Length];
        foreach (var row in ElementRows)
        {
            var total = 0;
            for (var i = 0; i < Grades.Length; i++)
            {
                values[i] = CountOf(counts, row.Element, Grades[i]);
                total += values[i];
            }
            if (total == 0)
            {
                continue;
            }

            ImGui.SetCursorPosX(Px(28));
            ImGui.TextUnformatted(ctx.Localize(row.Key));
            for (var i = 0; i < Grades.Length; i++)
            {
                ColumnText(values[i].ToString(), i);
            }
            ImGui.Dummy(new Vector2(1f, Px(4)));
        }
    }

    /// <summary>The last races the player finished, newest first.</summary>
    private void DrawLog(OsAppContext ctx)
    {
        Section(ctx.Localize("os.racer_stats_log"));

        if (_logError is { Length: > 0 } error)
        {
            Centered(error);
            return;
        }
        if (_log is not { } log)
        {
            Centered(ctx.Localize("os.racer_loading"));
            return;
        }
        if (log.Length == 0)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, MutedInk()))
        {
            Centered(ctx.Localize("os.racer_log_empty"));
        }
            return;
        }

        foreach (var entry in log)
        {
            var when = entry.ResolvedAtUtc.ToLocalTime().ToString("g", ctx.Culture);
            ImGui.SetCursorPosX(Px(28));
            ImGui.TextUnformatted(ctx.Localize($"os.racer_course_{entry.CourseKey}"));
            ImGui.SameLine(RightEdge() - ImGui.CalcTextSize(when).X);
            using (ImRaii.PushColor(ImGuiCol.Text, MutedInk()))
            {
                ImGui.TextUnformatted(when);
            }
            ImGui.SetCursorPosX(Px(28));
            using (ImRaii.PushColor(ImGuiCol.Text, MutedInk()))
            {
                ImGui.TextUnformatted(Detail(ctx, entry));
            }
            ImGui.Dummy(new Vector2(1f, Px(6)));
        }
    }

    private static string Detail(OsAppContext ctx, LumiRaceLogEntryDto entry)
    {
        var detail = $"{RacerChrome.DifficultyLabel(ctx, entry.Difficulty)}   #{entry.Place}";
        if (entry.IsParty)
        {
            detail = $"{detail}   {ctx.Localize("os.racer_log_party")}";
        }
        if (entry.IsPractice)
        {
            detail = $"{detail}   {ctx.Localize("os.racer_log_practice")}";
        }
        return detail;
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

    private static void Section(string title)
    {
        ImGui.Dummy(new Vector2(1f, Px(14)));
        ImGui.Separator();
        ImGui.Dummy(new Vector2(1f, Px(6)));
        ImGui.SetCursorPosX(Px(28));
        ImGui.TextUnformatted(title);
        ImGui.Dummy(new Vector2(1f, Px(6)));
    }

    private static void Row(string label, int value)
    {
        ImGui.SetCursorPosX(Px(28));
        ImGui.TextUnformatted(label);
        var text = value.ToString();
        ImGui.SameLine(RightEdge() - ImGui.CalcTextSize(text).X);
        ImGui.TextUnformatted(text);
        ImGui.Dummy(new Vector2(1f, Px(4)));
    }

    /// <summary>Puts a cell in one of the three grade columns, right-aligned against its own edge.</summary>
    private static void ColumnText(string text, int column)
    {
        var edge = RightEdge() - ((Grades.Length - 1 - column) * Px(58));
        ImGui.SameLine(edge - ImGui.CalcTextSize(text).X);
        ImGui.TextUnformatted(text);
    }

    /// <summary>The right edge every value lines up against, clear of the scrollbar.</summary>
    private static float RightEdge() => ImGui.GetWindowWidth() - ImGui.GetStyle().ScrollbarSize - Px(20);

    /// <summary>The card ink faded for supporting text, in place of the ambient disabled grey that
    /// vanishes on the card's paper.</summary>
    private static uint MutedInk() =>
        ImGui.ColorConvertFloat4ToU32(RacerChrome.CardBlue with { W = RacerChrome.CardBlue.W * 0.62f });

    private static void Centered(string text)
    {
        var size = ImGui.CalcTextSize(text);
        ImGui.SetCursorPosX(MathF.Max(0f, (ImGui.GetWindowWidth() - size.X) * 0.5f));
        ImGui.TextUnformatted(text);
    }
}
