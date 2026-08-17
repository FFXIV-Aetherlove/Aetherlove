using System;
using System.Collections.Generic;
using System.Numerics;
using AetherLove;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using AetherOS.Apps.Timers.Schedule;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Timers;

/// <summary>The per-second view memo, the shared row and card primitives, and the region resolution.
/// Everything the draw loop touches per row is prebuilt here, so 40 retainers and 64 vessels render
/// without per-frame allocations.</summary>
public sealed partial class TimersApp
{
    private const double AddedTickSeconds = 2.0;

    private readonly record struct TimerRow(
        FontAwesomeIcon Icon,
        Vector4 IconTint,
        string Title,
        string Sub,
        string Right,
        Vector4 RightColor,
        string CalId,
        string CalTitle,
        long CalUnix);

    private readonly Dictionary<string, double> _calAdded = new();

    private long _viewsSecond = -1;
    private int _viewsVersion = -1;
    private int _dataStamp;
    private int _viewsDataStamp = -1;
    private double _revealStamp = -1.0;

    private GameRegion _region = GameRegion.Europe;

    private OsWidgetItem[] _widgetItems = [];
    private OsWidgetAction[] _widgetActions = [];

    private void InvalidateViews() => _viewsSecond = -1;

    /// <summary>Rows for the home widgets page; polled every frame, so everything is memoized per second.</summary>
    public IReadOnlyList<OsWidgetItem> WidgetItems
    {
        get
        {
            EnsureViews(DateTime.UtcNow);
            return _widgetItems;
        }
    }

    public IReadOnlyList<OsWidgetAction> WidgetActions
    {
        get
        {
            EnsureViews(DateTime.UtcNow);
            return _widgetActions;
        }
    }

    /// <summary>Rebuilds every prebuilt view once per second, or immediately when the retainer books or
    /// local data change under it.</summary>
    private void EnsureViews(DateTime utcNow)
    {
        var second = utcNow.Ticks / TimeSpan.TicksPerSecond;
        if (second == _viewsSecond && _retainers.Version == _viewsVersion && _dataStamp == _viewsDataStamp)
        {
            return;
        }
        _viewsSecond = second;
        _viewsVersion = _retainers.Version;
        _viewsDataStamp = _dataStamp;

        _config ??= _host.GetReminderConfig();
        if (_customTimers is null)
        {
            ReloadCustomTimers();
            _viewsDataStamp = _dataStamp;
        }
        _region = _host.CurrentRegion;
        MaybeRefreshCommitments();
        PruneAddedTicks();

        BuildHeroView(utcNow);
        BuildResetRows(utcNow);
        BuildActivityViews(utcNow);
        BuildGroupViews(utcNow);
        BuildCustomRows(utcNow);
        BuildComingRows(utcNow);
        BuildWidgetItems(utcNow);
    }

    private void BuildWidgetItems(DateTime utcNow)
    {
        var items = new List<OsWidgetItem>(3);

        var bestReset = _resetRows.Length > 0 ? _resetRows[0] : default;
        for (var i = 1; i < _resetRows.Length; i++)
        {
            if (_resetRows[i].CalUnix < bestReset.CalUnix)
            {
                bestReset = _resetRows[i];
            }
        }
        if (_resetRows.Length > 0)
        {
            items.Add(new OsWidgetItem(bestReset.Title, bestReset.Right));
        }

        if (_readyWorkName is { } ready)
        {
            items.Add(new OsWidgetItem(ready, Loc.T("os.timers_ready")));
        }
        else if (_soonestWorkName is { } working)
        {
            items.Add(new OsWidgetItem(working, FormatCountdown(_soonestWorkUtc - utcNow)));
        }

        string? planName = null;
        var planUtc = DateTime.MaxValue;
        if (_customTimers is { } timers)
        {
            foreach (var timer in timers)
            {
                if (timer.DueUtc > utcNow && timer.DueUtc < planUtc)
                {
                    planUtc = timer.DueUtc;
                    planName = timer.Name;
                }
            }
        }
        foreach (var commitment in _commitments)
        {
            if (commitment.WhenUtc > utcNow && commitment.WhenUtc < planUtc)
            {
                planUtc = commitment.WhenUtc;
                planName = commitment.Name;
            }
        }
        if (planName is not null)
        {
            items.Add(new OsWidgetItem(planName, FormatCountdown(planUtc - utcNow)));
        }

        _widgetItems = items.ToArray();
        _widgetActions = [new OsWidgetAction(FontAwesomeIcon.Bell, Loc.T("os.timers_widget_bell"), OpenRemindersFromWidget)];
    }

    private void PruneAddedTicks()
    {
        if (_calAdded.Count < 24)
        {
            return;
        }
        var now = ImGui.GetTime();
        var stale = new List<string>();
        foreach (var (key, stamp) in _calAdded)
        {
            if (now - stamp >= AddedTickSeconds)
            {
                stale.Add(key);
            }
        }
        foreach (var key in stale)
        {
            _calAdded.Remove(key);
        }
    }

    private static string RegionLabel(GameRegion region) => region switch
    {
        GameRegion.Japan => Loc.T("os.timers_region_jp"),
        GameRegion.NorthAmerica => Loc.T("os.timers_region_na"),
        GameRegion.Oceania => Loc.T("os.timers_region_oce"),
        _ => Loc.T("os.timers_region_eu"),
    };

    internal static string FormatCountdown(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
        {
            span = TimeSpan.Zero;
        }
        if (span.TotalDays >= 1)
        {
            return $"{(int)span.TotalDays}d {span.Hours}h";
        }
        if (span.TotalHours >= 1)
        {
            return $"{(int)span.TotalHours}h {span.Minutes}m";
        }
        if (span.TotalMinutes >= 1)
        {
            return $"{span.Minutes}m";
        }
        return $"{span.Seconds}s";
    }

    private static DateTime ToLocal(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();

    private static long ToUnix(DateTime utc) =>
        new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds();

    private static float EaseOut(float x) => 1f - (1f - x) * (1f - x) * (1f - x);

    private float Reveal(OsAppContext ctx)
    {
        if (_revealStamp < 0.0)
        {
            _revealStamp = ImGui.GetTime();
        }
        if (ctx.ReduceMotion)
        {
            return 1f;
        }
        const float revealSeconds = 0.7f;
        return EaseOut(Math.Clamp((float)(ImGui.GetTime() - _revealStamp) / revealSeconds, 0f, 1f));
    }

    /// <summary>The card frame every section shares: fill and hint title. Returns the card's top-left;
    /// rows start at <paramref name="rowsY"/>. The caller must close with <see cref="EndCard"/>.</summary>
    private Vector2 BeginCard(ImDrawListPtr dl, float winW, float bodyH, string title, out float cardW,
        out float cardH, out float rowsY)
    {
        var lineH = ImGui.GetTextLineHeight();
        var headerH = Px(12f) + lineH + Px(6f);
        cardW = winW - Px(PadX) * 2f;
        cardH = headerH + bodyH + Px(8f);
        ImGui.SetCursorPosX(Px(PadX));
        var tl = ImGui.GetCursorScreenPos();
        dl.AddRectFilled(tl, tl + new Vector2(cardW, cardH), OsDrawShared.White(0.05f), Px(CardRounding));
        dl.AddText(new Vector2(tl.X + Px(14f), tl.Y + Px(12f)), ImGui.GetColorU32(UiColors.Hint), title);
        rowsY = tl.Y + headerH;
        return tl;
    }

    private static void EndCard(Vector2 cardTL, float cardW, float cardH)
    {
        ImGui.SetCursorScreenPos(cardTL);
        ImGui.Dummy(new Vector2(cardW, cardH + Px(10f)));
    }

    private static void DrawHairline(ImDrawListPtr dl, float x, float y, float w)
    {
        dl.AddLine(new Vector2(x + Px(12f), y), new Vector2(x + w - Px(12f), y), OsDrawShared.White(0.06f), Px(1f));
    }

    private void DrawTimerRow(ImDrawListPtr dl, Vector2 tl, float w, float h, in TimerRow row,
        float rightInset = 0f)
    {
        var t = ThemeService.Current;
        var textX = tl.X + Px(14f);
        if (row.Icon != FontAwesomeIcon.None)
        {
            var chipR = Px(13f);
            var chipC = new Vector2(tl.X + Px(12f) + chipR, tl.Y + h * 0.5f);
            dl.AddCircleFilled(chipC, chipR, ImGui.ColorConvertFloat4ToU32(t.Accent with { W = 0.14f }), 24);
            IconDraw.AddCentered(dl, row.Icon, Px(12f), chipC, ImGui.ColorConvertFloat4ToU32(row.IconTint));
            textX = chipC.X + chipR + Px(10f);
        }

        var rightEdge = tl.X + w - Px(12f) - rightInset;
        if (row.CalId.Length > 0)
        {
            var calC = new Vector2(rightEdge - Px(11f), tl.Y + h * 0.5f);
            DrawCalendarGlyph(dl, calC, in row);
            rightEdge = calC.X - Px(11f) - Px(8f);
        }

        if (row.Right.Length > 0)
        {
            var sz = ImGui.CalcTextSize(row.Right);
            dl.AddText(new Vector2(rightEdge - sz.X, tl.Y + (h - sz.Y) * 0.5f),
                ImGui.ColorConvertFloat4ToU32(row.RightColor), row.Right);
            rightEdge -= sz.X + Px(8f);
        }

        var font = ImGui.GetFont();
        var fontSize = ImGui.GetFontSize();
        dl.PushClipRect(new Vector2(textX, tl.Y), new Vector2(rightEdge, tl.Y + h), true);
        if (row.Sub.Length > 0)
        {
            dl.AddText(new Vector2(textX, tl.Y + Px(7f)), ImGui.GetColorU32(UiColors.Body), row.Title);
            dl.AddText(font, fontSize * 0.88f, new Vector2(textX, tl.Y + Px(25f)),
                ImGui.GetColorU32(UiColors.Hint), row.Sub);
        }
        else
        {
            dl.AddText(new Vector2(textX, tl.Y + (h - fontSize) * 0.5f), ImGui.GetColorU32(UiColors.Body), row.Title);
        }
        dl.PopClipRect();
    }

    private void DrawCalendarGlyph(ImDrawListPtr dl, Vector2 center, in TimerRow row)
    {
        var r = Px(11f);
        if (_calAdded.TryGetValue(row.CalId, out var stamp) && ImGui.GetTime() - stamp < AddedTickSeconds)
        {
            IconDraw.AddCentered(dl, FontAwesomeIcon.Check, Px(11f), center, ImGui.GetColorU32(UiColors.Success));
            return;
        }
        ImGui.SetCursorScreenPos(center - new Vector2(r, r));
        var clicked = ImGui.InvisibleButton(row.CalId, new Vector2(r * 2f, r * 2f));
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            HandOnHover();
            ImGui.SetTooltip(Loc.T("os.timers_cal_add_tt"));
        }
        dl.AddCircleFilled(center, r, OsDrawShared.White(hovered ? 0.13f : 0.06f), 20);
        IconDraw.AddCentered(dl, FontAwesomeIcon.CalendarPlus, Px(10f), center,
            hovered ? ThemeService.Current.AccentU32 : OsDrawShared.White(0.75f));
        if (clicked)
        {
            _shell.DeliverIntent(CalendarAppId,
                OsIntents.CreateCalendarAdd(row.CalTitle, Loc.T("os.timers_cal_note"), row.CalUnix, CalendarLeadMinutes));
            _calAdded[row.CalId] = ImGui.GetTime();
        }
    }

    private static bool RoundIconButton(string id, FontAwesomeIcon icon, Vector2 center, float radius,
        float iconPx, Vector4? accentFill = null)
    {
        ImGui.SetCursorScreenPos(center - new Vector2(radius, radius));
        var clicked = ImGui.InvisibleButton(id, new Vector2(radius * 2f, radius * 2f));
        HandOnHover();
        var hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        var fill = accentFill is { } accent
            ? ImGui.ColorConvertFloat4ToU32(hovered ? accent with { W = 0.85f } : accent)
            : OsDrawShared.White(hovered ? 0.13f : 0.07f);
        dl.AddCircleFilled(center, radius, fill, 28);
        IconDraw.AddCentered(dl, icon, iconPx, center, OsDrawShared.White(0.95f));
        return clicked;
    }

    /// <summary>A toggle pill; measures itself so callers can flow and wrap a row of them.</summary>
    private static bool PillButton(string id, string label, bool selected, Vector2 pos, float h, out float w)
    {
        var t = ThemeService.Current;
        var labelSz = ImGui.CalcTextSize(label);
        w = labelSz.X + Px(20f);
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton(id, new Vector2(w, h));
        HandOnHover();
        var hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        var fill = selected
            ? ImGui.ColorConvertFloat4ToU32(t.Accent with { W = hovered ? 0.95f : 0.8f })
            : OsDrawShared.White(hovered ? 0.13f : 0.08f);
        dl.AddRectFilled(pos, pos + new Vector2(w, h), fill, h * 0.5f);
        dl.AddText(pos + new Vector2(Px(10f), (h - labelSz.Y) * 0.5f),
            ImGui.GetColorU32(selected ? UiColors.Body : UiColors.Hint), label);
        return clicked;
    }
}
