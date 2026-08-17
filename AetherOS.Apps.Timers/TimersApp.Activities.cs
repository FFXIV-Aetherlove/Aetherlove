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

/// <summary>The Activities card: Fashion Report, the region-aware Jumbo Cactpot, and Ocean Fishing with
/// the real routes; tapping the ocean row expands the next voyages.</summary>
public sealed partial class TimersApp
{
    private const float OceanRowHeight = 62f;
    private const float VoyageRowHeight = 42f;
    private const int VoyageListCount = 4;

    private static readonly Vector4 DayTint = new(0.99f, 0.83f, 0.35f, 1f);
    private static readonly Vector4 SunsetTint = new(0.98f, 0.57f, 0.35f, 1f);
    private static readonly Vector4 NightTint = new(0.56f, 0.64f, 0.98f, 1f);

    private sealed record VoyageView(string DepartLocal, string RouteName, string CalId, string CalTitle,
        long DepartUnix, IReadOnlyList<OceanRoutes.Stop> Stops);

    private TimerRow _frRow;
    private TimerRow _cactpotRow;
    private string _oceanSub = "";
    private bool _oceanRegOpen;
    private TimerRow _oceanCalRow;
    private bool _oceanExpanded;
    private long _voyageIndex = -1;
    private VoyageView[] _voyageViews = [];

    private void BuildActivityViews(DateTime utcNow)
    {
        var (frOpen, frChange) = EorzeaSchedule.FashionReport(utcNow);
        // While open the row still advertises the NEXT opening to the calendar, which is the change after
        // the current window closes.
        var frNextOpen = frOpen ? EorzeaSchedule.FashionReport(frChange.AddSeconds(1)).NextChangeUtc : frChange;
        _frRow = new TimerRow(FontAwesomeIcon.Tshirt, new Vector4(1f, 1f, 1f, 0.9f),
            Loc.T("os.timers_fr_title"),
            Loc.T(frOpen ? "os.timers_fr_closes_in" : "os.timers_fr_opens_in", FormatCountdown(frChange - utcNow)),
            Loc.T(frOpen ? "os.timers_state_open" : "os.timers_state_closed"),
            frOpen ? UiColors.Success : UiColors.Muted,
            "##calfr", Loc.T("os.timers_fr_title"), ToUnix(frNextOpen));

        var draw = EorzeaSchedule.NextCactpotDraw(_region, utcNow);
        _cactpotRow = new TimerRow(FontAwesomeIcon.Dice, new Vector4(1f, 1f, 1f, 0.9f),
            Loc.T("os.timers_cactpot_title"),
            $"{ToLocal(draw).ToString("ddd HH:mm", _culture)} · {RegionLabel(_region)}",
            FormatCountdown(draw - utcNow), UiColors.Body,
            "##calcact", Loc.T("os.timers_cactpot_title"), ToUnix(draw));

        var (departure, registrationOpens, registrationOpen) = EorzeaSchedule.NextVoyage(utcNow);
        _oceanRegOpen = registrationOpen;
        _oceanSub = registrationOpen
            ? Loc.T("os.timers_ocean_reg_open", FormatCountdown(departure - utcNow))
            : Loc.T("os.timers_ocean_next_reg", FormatCountdown(registrationOpens - utcNow));
        _oceanCalRow = new TimerRow(FontAwesomeIcon.Fish, new Vector4(1f, 1f, 1f, 0.9f),
            Loc.T("os.timers_ocean_title"), "", "", UiColors.Body,
            "##caloce", Loc.T("os.timers_ocean_title"), ToUnix(departure));

        MaybeRefreshVoyages(utcNow);
    }

    /// <summary>The route list only changes when the two-hour voyage window rolls over, so the sheet walk
    /// runs once per window rather than once per second.</summary>
    private void MaybeRefreshVoyages(DateTime utcNow)
    {
        var index = EorzeaSchedule.VoyageIndex(utcNow);
        if (index == _voyageIndex && _voyageViews.Length > 0)
        {
            return;
        }
        _voyageIndex = index;
        try
        {
            var voyages = OceanRoutes.Upcoming(UiHost.DataManager, utcNow, VoyageListCount);
            var views = new VoyageView[voyages.Count];
            for (var i = 0; i < voyages.Count; i++)
            {
                var voyage = voyages[i];
                views[i] = new VoyageView(
                    ToLocal(voyage.DepartureUtc).ToString("HH:mm", _culture),
                    voyage.RouteName,
                    $"##calov{i}",
                    $"{Loc.T("os.timers_ocean_title")} · {voyage.RouteName}",
                    ToUnix(voyage.DepartureUtc),
                    voyage.Stops);
            }
            _voyageViews = views;
        }
        catch (Exception)
        {
            _voyageViews = [];
        }
    }

    private void DrawActivitiesCard(OsAppContext ctx)
    {
        var dl = ImGui.GetWindowDrawList();
        var winW = ImGui.GetWindowSize().X;
        var rowH = Px(RowHeight);
        var oceanH = Px(OceanRowHeight);
        var voyageH = Px(VoyageRowHeight);
        var expandedH = _oceanExpanded ? _voyageViews.Length * voyageH : 0f;
        var bodyH = rowH * 2f + oceanH + expandedH;
        var cardTL = BeginCard(dl, winW, bodyH, Loc.T("os.timers_activities_title"),
            out var cardW, out var cardH, out var y);

        DrawTimerRow(dl, new Vector2(cardTL.X, y), cardW, rowH, in _frRow);
        y += rowH;
        DrawHairline(dl, cardTL.X, y, cardW);
        DrawTimerRow(dl, new Vector2(cardTL.X, y), cardW, rowH, in _cactpotRow);
        y += rowH;
        DrawHairline(dl, cardTL.X, y, cardW);
        DrawOceanRow(dl, new Vector2(cardTL.X, y), cardW, oceanH);
        y += oceanH;

        if (_oceanExpanded)
        {
            for (var i = 0; i < _voyageViews.Length; i++)
            {
                DrawHairline(dl, cardTL.X, y, cardW);
                DrawVoyageRow(dl, new Vector2(cardTL.X, y), cardW, voyageH, _voyageViews[i]);
                y += voyageH;
            }
        }

        EndCard(cardTL, cardW, cardH);
    }

    private void DrawOceanRow(ImDrawListPtr dl, Vector2 tl, float w, float h)
    {
        var t = ThemeService.Current;
        var chipR = Px(13f);
        var chipC = new Vector2(tl.X + Px(12f) + chipR, tl.Y + h * 0.5f);
        var textX = chipC.X + chipR + Px(10f);

        var calC = new Vector2(tl.X + w - Px(23f), tl.Y + h * 0.5f);
        DrawCalendarGlyph(dl, calC, in _oceanCalRow);

        ImGui.SetCursorScreenPos(tl);
        var clicked = ImGui.InvisibleButton("##oceanRow", new Vector2(w, h));
        HandOnHover();
        if (ImGui.IsItemHovered())
        {
            dl.AddRectFilled(tl, tl + new Vector2(w, h), OsDrawShared.White(0.03f));
        }
        if (clicked)
        {
            _oceanExpanded = !_oceanExpanded;
        }

        dl.AddCircleFilled(chipC, chipR, ImGui.ColorConvertFloat4ToU32(t.Accent with { W = 0.14f }), 24);
        IconDraw.AddCentered(dl, FontAwesomeIcon.Fish, Px(12f), chipC, OsDrawShared.White(0.9f));

        var chevC = new Vector2(calC.X - Px(11f) - Px(16f), tl.Y + h * 0.5f);
        IconDraw.AddCentered(dl, _oceanExpanded ? FontAwesomeIcon.ChevronUp : FontAwesomeIcon.ChevronDown,
            Px(10f), chevC, OsDrawShared.White(0.55f));

        var font = ImGui.GetFont();
        var fontSize = ImGui.GetFontSize();
        var clipMaxX = chevC.X - Px(12f);
        dl.PushClipRect(new Vector2(textX, tl.Y), new Vector2(clipMaxX, tl.Y + h), true);
        dl.AddText(new Vector2(textX, tl.Y + Px(6f)), ImGui.GetColorU32(UiColors.Body),
            Loc.T("os.timers_ocean_title"));
        dl.AddText(font, fontSize * 0.88f, new Vector2(textX, tl.Y + Px(24f)),
            ImGui.GetColorU32(_oceanRegOpen ? UiColors.Success : UiColors.Hint), _oceanSub);
        dl.PopClipRect();
        if (_voyageViews.Length > 0)
        {
            DrawStopsLine(dl, new Vector2(textX, tl.Y + Px(41f)), clipMaxX - textX, _voyageViews[0].Stops);
        }
    }

    private void DrawVoyageRow(ImDrawListPtr dl, Vector2 tl, float w, float h, VoyageView voyage)
    {
        var indentX = tl.X + Px(20f);
        var calC = new Vector2(tl.X + w - Px(23f), tl.Y + h * 0.5f);
        var calRow = new TimerRow(FontAwesomeIcon.None, Vector4.Zero, "", "", "", Vector4.Zero,
            voyage.CalId, voyage.CalTitle, voyage.DepartUnix);
        DrawCalendarGlyph(dl, calC, in calRow);

        var font = ImGui.GetFont();
        var fontSize = ImGui.GetFontSize();
        var clipMaxX = calC.X - Px(16f);
        dl.PushClipRect(new Vector2(indentX, tl.Y), new Vector2(clipMaxX, tl.Y + h), true);
        dl.AddText(new Vector2(indentX, tl.Y + Px(5f)), ImGui.GetColorU32(UiColors.Body), voyage.DepartLocal);
        var timeW = ImGui.CalcTextSize(voyage.DepartLocal).X;
        dl.AddText(font, fontSize * 0.88f, new Vector2(indentX + timeW + Px(10f), tl.Y + Px(6f)),
            ImGui.GetColorU32(UiColors.Hint), voyage.RouteName);
        dl.PopClipRect();
        DrawStopsLine(dl, new Vector2(indentX, tl.Y + Px(23f)), clipMaxX - indentX, voyage.Stops);
    }

    private static void DrawStopsLine(ImDrawListPtr dl, Vector2 pos, float maxW,
        IReadOnlyList<OceanRoutes.Stop> stops)
    {
        const float scale = 0.85f;
        var font = ImGui.GetFont();
        var fontSize = ImGui.GetFontSize();
        var small = fontSize * scale;
        dl.PushClipRect(pos, new Vector2(pos.X + maxW, pos.Y + small + Px(4f)), true);
        var x = pos.X;
        for (var i = 0; i < stops.Count; i++)
        {
            if (i > 0)
            {
                dl.AddText(font, small, new Vector2(x, pos.Y), ImGui.GetColorU32(UiColors.Muted), " › ");
                x += ImGui.CalcTextSize(" › ").X * scale;
            }
            var stop = stops[i];
            dl.AddText(font, small, new Vector2(x, pos.Y), ImGui.GetColorU32(UiColors.Hint), stop.SpotName);
            x += ImGui.CalcTextSize(stop.SpotName).X * scale + Px(4f);
            var (icon, tint) = stop.Time switch
            {
                VoyageTime.Day => (FontAwesomeIcon.Sun, DayTint),
                VoyageTime.Sunset => (FontAwesomeIcon.CloudSun, SunsetTint),
                _ => (FontAwesomeIcon.Moon, NightTint),
            };
            IconDraw.Add(dl, icon, Px(9f), new Vector2(x, pos.Y + Px(2f)), ImGui.ColorConvertFloat4ToU32(tint));
            x += Px(9f) + Px(4f);
        }
        dl.PopClipRect();
    }
}
