using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Calendar;

/// <summary>A simple month calendar: the user's venue RSVPs (from the host feed) plus local personal
/// events kept in app storage. Venue rows deep-link into the Places app.</summary>
public sealed class CalendarApp : IAetherApp
{
    private sealed class OwnEvent
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Note { get; set; } = "";
        public DateTime StartUtc { get; set; }
    }

    private sealed record DayEntry(DateTime Local, string Title, string Sub, bool IsVenue, Guid VenueId, string EventId);

    private const string EventsKey = "events";

    private static readonly Vector4 TileTopColor = new(0.96f, 0.45f, 0.40f, 1f);
    private static readonly Vector4 TileBottomColor = new(0.58f, 0.16f, 0.28f, 1f);
    private static readonly Vector4 WhiteText = new(1f, 1f, 1f, 1f);
    private static readonly Vector4 MutedText = new(1f, 1f, 1f, 0.55f);
    private static readonly Vector4 FaintText = new(1f, 1f, 1f, 0.35f);
    private static readonly Vector4 PanelFill = new(1f, 1f, 1f, 0.07f);
    private static readonly Vector4 HoverFill = new(1f, 1f, 1f, 0.12f);
    private static readonly Vector4 CardBorder = new(1f, 1f, 1f, 0.10f);
    private static readonly Vector4 PanelBg = new(0.11f, 0.10f, 0.13f, 1f);
    private static readonly Vector4 PanelBorder = new(0.32f, 0.30f, 0.38f, 0.65f);
    private static readonly Vector4 DangerFill = new(0.82f, 0.22f, 0.28f, 1f);
    private static readonly Vector4 DimColor = new(0f, 0f, 0f, 0.55f);

    private static readonly string[] HourItems = Enumerable.Range(0, 24).Select(h => h.ToString("00")).ToArray();
    private static readonly string[] MinuteItems = Enumerable.Range(0, 12).Select(m => (m * 5).ToString("00")).ToArray();

    private readonly Func<string> name;
    private readonly ICalendarHost host;
    private readonly IAppCapabilities caps;

    private DateTime shownMonth = new(DateTime.Now.Year, DateTime.Now.Month, 1);
    private DateTime selectedDay = DateTime.Now.Date;
    // Phone-language culture, refreshed each Draw; the widget items property has no context of its own.
    private CultureInfo culture = CultureInfo.CurrentCulture;
    private List<OwnEvent>? events;
    private IReadOnlyList<VenueVisit> visits = [];
    private bool addOpen;
    private string addTitle = "";
    private string addNote = "";
    private int addHour = 20;
    private int addMinute = 0;
    private bool focusPending;
    private string? confirmDeleteId;
    private readonly List<OwnEvent> pendingAdds = new();

    public CalendarApp(Func<string> name, ICalendarHost host, IAppCapabilities caps)
    {
        this.name = name;
        this.host = host;
        this.caps = caps;
    }

    public string Id => "calendar";

    public string Name => this.name();

    public FontAwesomeIcon Icon => FontAwesomeIcon.CalendarAlt;

    public Vector4 TileTop => TileTopColor;

    public Vector4 TileBottom => TileBottomColor;

    public int Badge => 0;

    public bool HasSurface => true;

    public IReadOnlyList<string> AcceptedShareTypes { get; } = [ShareTypes.CalendarEvent];

    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Strings => AppStrings.Packs;

    public void Open()
    {
    }

    public void OnForeground()
    {
        this.lastVisitFetchUtc = DateTime.UtcNow;
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            this.visits = await this.host.GetVenueVisitsAsync();
        });
    }

    private DateTime lastVisitFetchUtc;

    /// <summary>The soonest three upcoming entries (own events + venue RSVPs) for the home widgets page.</summary>
    public IReadOnlyList<OsWidgetItem> WidgetItems
    {
        get
        {
            this.events ??= this.caps.Storage(this.Id).Get<List<OwnEvent>>(EventsKey) ?? new List<OwnEvent>();
            this.MaybeRefreshVisits();
            var cutoff = DateTime.UtcNow.AddHours(-1);
            return this.events
                .Select(e => (e.StartUtc, e.Title))
                .Concat(this.visits.Select(v => (v.StartUtc, Title: v.VenueName)))
                .Where(e => e.StartUtc >= cutoff)
                .OrderBy(e => e.StartUtc)
                .Take(3)
                .Select(e => new OsWidgetItem(e.Title,
                    AsLocal(e.StartUtc).ToString("ddd d MMM HH:mm", this.culture)))
                .ToArray();
        }
    }

    /// <summary>Keeps the widget's RSVP data warm without the app being opened, at most every ten minutes.</summary>
    private void MaybeRefreshVisits()
    {
        if ((DateTime.UtcNow - this.lastVisitFetchUtc).TotalMinutes < 10)
        {
            return;
        }
        this.lastVisitFetchUtc = DateTime.UtcNow;
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            this.visits = await this.host.GetVenueVisitsAsync();
        });
    }

    public void OnIntent(OsIntent intent)
    {
        if (intent.Type == ShareIntent.Type && ShareIntent.TryUnwrap(intent, out var shared))
        {
            QueueSharedAdd(shared);
            return;
        }
        if (intent.Type != OsIntents.CalendarAdd
            || !OsIntents.TryGetCalendarAdd(intent, out var title, out var note, out var startUnix))
        {
            return;
        }
        QueueAdd(title, note, DateTimeOffset.FromUnixTimeSeconds(startUnix).UtcDateTime);
    }

    /// <summary>A calendar-event share landing on this app becomes a local event at that date and time;
    /// the Extras JSON carries kind + start, the same shape the chat targets consume.</summary>
    private void QueueSharedAdd(ShareItem item)
    {
        if (item.Type != ShareTypes.CalendarEvent || item.Title.Length == 0)
        {
            return;
        }
        try
        {
            using var doc = JsonDocument.Parse(item.Extras);
            var startUtc = DateTimeOffset.FromUnixTimeSeconds(doc.RootElement.GetProperty("start").GetInt64()).UtcDateTime;
            QueueAdd(item.Title, item.Subtitle, startUtc);
        }
        catch (Exception)
        {
        }
    }

    private void QueueAdd(string title, string note, DateTime startUtc)
    {
        this.pendingAdds.Add(new OwnEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = title,
            Note = note,
            StartUtc = startUtc,
        });
        var local = AsLocal(startUtc);
        this.selectedDay = local.Date;
        this.shownMonth = new DateTime(local.Year, local.Month, 1);
    }

    public void Draw(OsAppContext ctx)
    {
        this.culture = ctx.Culture;
        var storage = ctx.Capabilities.Storage(this.Id);
        this.events ??= storage.Get<List<OwnEvent>>(EventsKey) ?? new List<OwnEvent>();
        if (this.pendingAdds.Count > 0)
        {
            foreach (var add in this.pendingAdds)
            {
                if (!this.events.Any(e => e.Title == add.Title && e.StartUtc == add.StartUtc))
                {
                    this.events.Add(add);
                }
            }
            this.pendingAdds.Clear();
            storage.Set(EventsKey, this.events);
        }

        var overlay = this.addOpen || this.confirmDeleteId != null;
        var flags = overlay ? ImGuiWindowFlags.NoScrollWithMouse : ImGuiWindowFlags.None;
        var contentTL = ImGui.GetCursorScreenPos();
        var contentSize = ImGui.GetContentRegionAvail();
        ImGui.BeginChild("##calendarScroll", contentSize, false, flags);
        if (overlay)
        {
            ImGui.BeginDisabled();
        }

        var winPos = ImGui.GetWindowPos();
        var winW = ImGui.GetWindowSize().X;
        var pad = ctx.Px(14f);
        var width = winW - pad * 2f;
        var x = winPos.X + pad;

        this.DrawHeader(ctx, x, width);
        this.DrawMonthGrid(ctx, x, width);
        this.DrawDayAgenda(ctx, x, width, storage);

        ImGui.Dummy(new Vector2(0f, ctx.Px(14f)));
        if (overlay)
        {
            ImGui.EndDisabled();
        }
        ImGui.EndChild();

        // The overlays live in their OWN child on top of the scroll child: even disabled, the agenda rows'
        // full-width buttons claim hover for their rects, so a same-window dialog drawn after them loses any
        // click landing on a row. A later sibling child wins hover over everything beneath it.
        if (overlay)
        {
            ImGui.SetCursorScreenPos(contentTL);
            ImGui.BeginChild("##calendarOverlay", contentSize, false,
                ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
            if (this.addOpen)
            {
                this.DrawAddOverlay(ctx, storage);
            }
            else if (this.confirmDeleteId != null)
            {
                this.DrawDeleteConfirm(ctx, storage);
            }
            ImGui.EndChild();
        }
    }

    private void DrawHeader(OsAppContext ctx, float x, float width)
    {
        ImGui.Dummy(new Vector2(0f, ctx.Px(10f)));
        var rowTL = ImGui.GetCursorScreenPos();
        var rowH = ctx.Px(32f);
        var dl = ImGui.GetWindowDrawList();

        using (ctx.TitleFont?.Push())
        {
            var title = this.shownMonth.ToString("MMMM yyyy", this.culture);
            dl.AddText(new Vector2(x, rowTL.Y + (rowH - ImGui.GetFontSize()) * 0.5f), U32(WhiteText), title);
        }

        var cy = rowTL.Y + rowH * 0.5f;
        var r = ctx.Px(13f);
        if (RoundIconButton("##calNext", FontAwesomeIcon.ChevronRight, new Vector2(x + width - r, cy), r, ctx.Px(11f)))
        {
            this.shownMonth = this.shownMonth.AddMonths(1);
        }
        if (RoundIconButton("##calPrev", FontAwesomeIcon.ChevronLeft, new Vector2(x + width - r * 3f - ctx.Px(8f), cy), r, ctx.Px(11f)))
        {
            this.shownMonth = this.shownMonth.AddMonths(-1);
        }

        var todayLabel = ctx.Localize("os.cal_today");
        var todaySz = ImGui.CalcTextSize(todayLabel);
        var todayW = todaySz.X + ctx.Px(18f);
        var todayTL = new Vector2(x + width - r * 4f - ctx.Px(20f) - todayW, cy - ctx.Px(12f));
        ImGui.SetCursorScreenPos(todayTL);
        if (ImGui.InvisibleButton("##calToday", new Vector2(todayW, ctx.Px(24f))))
        {
            this.shownMonth = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            this.selectedDay = DateTime.Now.Date;
        }
        var todayFill = ImGui.IsItemHovered() ? HoverFill : PanelFill;
        dl.AddRectFilled(todayTL, todayTL + new Vector2(todayW, ctx.Px(24f)), U32(todayFill), ctx.Px(12f));
        dl.AddText(todayTL + new Vector2(ctx.Px(9f), (ctx.Px(24f) - todaySz.Y) * 0.5f), U32(MutedText), todayLabel);

        ImGui.SetCursorScreenPos(rowTL);
        ImGui.Dummy(new Vector2(width, rowH + ctx.Px(8f)));
    }

    private void DrawMonthGrid(OsAppContext ctx, float x, float width)
    {
        var dl = ImGui.GetWindowDrawList();
        var culture = this.culture;
        var firstDay = (int)culture.DateTimeFormat.FirstDayOfWeek;
        var cellW = width / 7f;
        var cellH = ctx.Px(36f);

        var namesTL = ImGui.GetCursorScreenPos();
        for (var i = 0; i < 7; i++)
        {
            var label = culture.DateTimeFormat.ShortestDayNames[(firstDay + i) % 7];
            var sz = ImGui.CalcTextSize(label);
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.85f,
                new Vector2(x + i * cellW + (cellW - sz.X * 0.85f) * 0.5f, namesTL.Y), U32(FaintText), label);
        }
        ImGui.Dummy(new Vector2(width, ImGui.GetTextLineHeight() + ctx.Px(4f)));

        var marks = this.BuildDayMarks();
        var gridTL = ImGui.GetCursorScreenPos();
        var lead = ((int)this.shownMonth.DayOfWeek - firstDay + 7) % 7;
        var days = DateTime.DaysInMonth(this.shownMonth.Year, this.shownMonth.Month);
        var today = DateTime.Now.Date;
        var rows = (lead + days + 6) / 7;

        for (var day = 1; day <= days; day++)
        {
            var slot = lead + day - 1;
            var center = new Vector2(
                x + (slot % 7) * cellW + cellW * 0.5f,
                gridTL.Y + (slot / 7) * cellH + cellH * 0.5f - ctx.Px(3f));
            var date = new DateTime(this.shownMonth.Year, this.shownMonth.Month, day);

            ImGui.SetCursorScreenPos(center - new Vector2(cellW * 0.5f, cellH * 0.5f));
            if (ImGui.InvisibleButton($"##calDay{day}", new Vector2(cellW, cellH)))
            {
                this.selectedDay = date;
            }
            var hovered = ImGui.IsItemHovered();

            var radius = ctx.Px(13f);
            if (date == this.selectedDay)
            {
                dl.AddCircleFilled(center, radius, U32(ctx.Theme.Accent), 28);
            }
            else if (hovered)
            {
                dl.AddCircleFilled(center, radius, U32(HoverFill), 28);
            }
            if (date == today && date != this.selectedDay)
            {
                dl.AddCircle(center, radius, U32(ctx.Theme.Accent), 28, ctx.Px(1.4f));
            }

            var num = day.ToString(CultureInfo.InvariantCulture);
            var numSz = ImGui.CalcTextSize(num);
            dl.AddText(center - numSz * 0.5f, U32(date == this.selectedDay ? WhiteText : MutedText with { W = 0.85f }), num);

            if (marks.TryGetValue(date, out var mark))
            {
                var dotY = center.Y + radius + ctx.Px(4f);
                var both = mark.Own && mark.Venue;
                var firstX = center.X - (both ? ctx.Px(3.5f) : 0f);
                if (mark.Own)
                {
                    dl.AddCircleFilled(new Vector2(firstX, dotY), ctx.Px(2.2f), U32(ctx.Theme.AccentLight), 12);
                }
                if (mark.Venue)
                {
                    dl.AddCircleFilled(new Vector2(both ? center.X + ctx.Px(3.5f) : center.X, dotY), ctx.Px(2.2f), U32(WhiteText with { W = 0.75f }), 12);
                }
            }
        }

        ImGui.SetCursorScreenPos(gridTL);
        ImGui.Dummy(new Vector2(width, rows * cellH + ctx.Px(8f)));
    }

    private Dictionary<DateTime, (bool Own, bool Venue)> BuildDayMarks()
    {
        var marks = new Dictionary<DateTime, (bool Own, bool Venue)>();
        foreach (var e in this.events!)
        {
            var day = AsLocal(e.StartUtc).Date;
            marks[day] = (true, marks.TryGetValue(day, out var m) && m.Venue);
        }
        foreach (var v in this.visits)
        {
            var day = AsLocal(v.StartUtc).Date;
            marks[day] = (marks.TryGetValue(day, out var m) && m.Own, true);
        }
        return marks;
    }

    private void DrawDayAgenda(OsAppContext ctx, float x, float width, IAppStorage storage)
    {
        var dl = ImGui.GetWindowDrawList();
        var rowTL = ImGui.GetCursorScreenPos();
        var rowH = ctx.Px(30f);

        using (ctx.HeadingFont?.Push())
        {
            var heading = this.selectedDay.ToString("dddd d MMMM", this.culture);
            dl.AddText(new Vector2(x, rowTL.Y + (rowH - ImGui.GetFontSize()) * 0.5f), U32(WhiteText), heading);
        }
        var addR = ctx.Px(13f);
        if (RoundIconButton("##calAdd", FontAwesomeIcon.Plus, new Vector2(x + width - addR, rowTL.Y + rowH * 0.5f), addR, ctx.Px(11f), accentFill: ctx.Theme.Accent))
        {
            this.addOpen = true;
            this.addTitle = "";
            this.addNote = "";
            this.addHour = 20;
            this.addMinute = 0;
            this.focusPending = true;
        }
        ImGui.SetCursorScreenPos(rowTL);
        ImGui.Dummy(new Vector2(width, rowH + ctx.Px(6f)));

        var entries = this.EntriesFor(this.selectedDay);
        if (entries.Count == 0)
        {
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ctx.Px(14f));
            ImGui.PushStyleColor(ImGuiCol.Text, MutedText);
            ImGui.TextUnformatted(ctx.Localize("os.cal_empty"));
            ImGui.PopStyleColor();
            return;
        }

        foreach (var entry in entries)
        {
            this.DrawEntryRow(ctx, entry, x, width);
        }
    }

    private List<DayEntry> EntriesFor(DateTime day)
    {
        var list = new List<DayEntry>();
        foreach (var e in this.events!)
        {
            var local = AsLocal(e.StartUtc);
            if (local.Date == day)
            {
                list.Add(new DayEntry(local, e.Title, e.Note, false, Guid.Empty, e.Id));
            }
        }
        foreach (var v in this.visits)
        {
            var local = AsLocal(v.StartUtc);
            if (local.Date == day)
            {
                list.Add(new DayEntry(local, v.VenueName, "", true, v.VenueId, ""));
            }
        }
        return list.OrderBy(e => e.Local).ToList();
    }

    private void DrawEntryRow(OsAppContext ctx, DayEntry entry, float x, float width)
    {
        var dl = ImGui.GetWindowDrawList();
        var hasSub = entry.Sub.Length > 0;
        var rowH = ctx.Px(hasSub ? 52f : 40f);
        var tl = ImGui.GetCursorScreenPos() with { X = x };
        var br = tl + new Vector2(width, rowH);
        var rowKey = $"{entry.EventId}{entry.VenueId}{entry.Local.Ticks}";

        dl.AddRectFilled(tl, br, U32(PanelFill), ctx.Px(10f));
        dl.AddRect(tl, br, U32(CardBorder), ctx.Px(10f), ImDrawFlags.RoundCornersAll, 1f);

        var accent = entry.IsVenue ? WhiteText with { W = 0.75f } : ctx.Theme.AccentLight;
        dl.AddRectFilled(tl + new Vector2(0f, ctx.Px(8f)), new Vector2(tl.X + ctx.Px(3f), br.Y - ctx.Px(8f)), U32(accent), ctx.Px(1.5f));

        var time = entry.Local.ToString("HH:mm", this.culture);
        var timeW = ImGui.CalcTextSize("00:00").X;
        dl.AddText(new Vector2(tl.X + ctx.Px(12f), tl.Y + ctx.Px(hasSub ? 8f : 11f)), U32(MutedText), time);

        var iconX = tl.X + ctx.Px(12f) + timeW + ctx.Px(10f);
        AddIconCentered(dl, entry.IsVenue ? FontAwesomeIcon.MapMarkerAlt : FontAwesomeIcon.Circle,
            ctx.Px(entry.IsVenue ? 11f : 6f), new Vector2(iconX + ctx.Px(6f), tl.Y + ctx.Px(hasSub ? 15f : 19f)), U32(accent));

        var textX = iconX + ctx.Px(18f);
        var textMaxX = br.X - ctx.Px(entry.IsVenue ? 34f : 60f);
        dl.PushClipRect(new Vector2(textX, tl.Y), new Vector2(textMaxX, br.Y), true);
        dl.AddText(new Vector2(textX, tl.Y + ctx.Px(hasSub ? 8f : 11f)), U32(WhiteText), entry.Title);
        if (hasSub)
        {
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.9f, new Vector2(textX, tl.Y + ctx.Px(29f)), U32(MutedText), entry.Sub);
        }
        dl.PopClipRect();

        // Trailing icon buttons go first: with overlapping items the first-submitted one wins clicks.
        var cy = tl.Y + rowH * 0.5f;
        var shareC = new Vector2(br.X - ctx.Px(entry.IsVenue ? 20f : 46f), cy);
        if (RoundIconButton($"##calShare{rowKey}", FontAwesomeIcon.Share, shareC, ctx.Px(12f), ctx.Px(9f)))
        {
            ShareEntry(ctx, entry);
        }
        if (!entry.IsVenue
            && RoundIconButton($"##calDel{entry.EventId}", FontAwesomeIcon.Trash,
                new Vector2(br.X - ctx.Px(20f), cy), ctx.Px(12f), ctx.Px(9f)))
        {
            this.confirmDeleteId = entry.EventId;
        }

        ImGui.SetCursorScreenPos(tl);
        var clicked = ImGui.InvisibleButton($"##calEntry{rowKey}", new Vector2(width, rowH));
        if (entry.IsVenue)
        {
            if (ImGui.IsItemHovered())
            {
                dl.AddRect(tl, br, U32(ctx.Theme.Accent), ctx.Px(10f), ImDrawFlags.RoundCornersAll, ctx.Px(1.2f));
            }
            if (clicked)
            {
                ctx.Shell.SendIntent("places", OsIntents.Create(OsIntents.OpenVenue, entry.VenueId));
            }
        }

        ImGui.SetCursorScreenPos(tl);
        ImGui.Dummy(new Vector2(width, rowH + ctx.Px(6f)));
    }

    /// <summary>Offers the entry to the share sheet. The Extras JSON carries kind + start; chat targets
    /// turn it into the [calevent=] token via the shared composer.</summary>
    private static void ShareEntry(OsAppContext ctx, DayEntry entry)
    {
        var startUnix = new DateTimeOffset(entry.Local).ToUnixTimeSeconds();
        ctx.Capabilities.Share.Offer(entry.IsVenue
            ? new ShareItem
            {
                Type = ShareTypes.CalendarEvent,
                RefId = entry.VenueId.ToString("D"),
                Title = entry.Title,
                Extras = JsonSerializer.Serialize(new { kind = "venue", start = startUnix }),
                SourceAppId = "calendar",
            }
            : new ShareItem
            {
                Type = ShareTypes.CalendarEvent,
                Title = entry.Title,
                Subtitle = entry.Sub,
                Extras = JsonSerializer.Serialize(new { kind = "personal", start = startUnix }),
                SourceAppId = "calendar",
            });
    }

    private void DrawAddOverlay(OsAppContext ctx, IAppStorage storage)
    {
        var winPos = ImGui.GetWindowPos();
        var winSize = ImGui.GetWindowSize();
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(winPos, winPos + winSize, U32(DimColor));

        var padIn = ctx.Px(16f);
        var lineH = ImGui.GetTextLineHeight();
        var inputH = lineH + ctx.Px(14f);
        var btnH = ctx.Px(34f);
        var panelW = MathF.Min(winSize.X - ctx.Px(40f), ctx.Px(280f));
        var innerW = panelW - padIn * 2f;
        var panelH = padIn + lineH + ctx.Px(8f) + inputH + ctx.Px(10f) + inputH + ctx.Px(10f) + inputH + ctx.Px(14f) + btnH + padIn;
        var panelTL = winPos + (winSize - new Vector2(panelW, panelH)) * 0.5f;
        var panelBR = panelTL + new Vector2(panelW, panelH);

        dl.AddRectFilled(panelTL, panelBR, U32(PanelBg), ctx.Px(16f));
        dl.AddRect(panelTL, panelBR, U32(PanelBorder), ctx.Px(16f), ImDrawFlags.RoundCornersAll, 1f);
        dl.AddText(panelTL + new Vector2(padIn, padIn), U32(MutedText), ctx.Localize("os.cal_add"));

        var y = panelTL.Y + padIn + lineH + ctx.Px(8f);
        ImGui.SetCursorScreenPos(new Vector2(panelTL.X + padIn, y));
        ImGui.SetNextItemWidth(innerW);
        PushInputStyle(ctx);
        if (this.focusPending)
        {
            ImGui.SetKeyboardFocusHere();
            this.focusPending = false;
        }
        ImGui.InputTextWithHint("##calTitle", ctx.Localize("os.cal_event_title"), ref this.addTitle, 64);
        y += inputH + ctx.Px(10f);
        ImGui.SetCursorScreenPos(new Vector2(panelTL.X + padIn, y));
        ImGui.SetNextItemWidth(innerW);
        ImGui.InputTextWithHint("##calNote", ctx.Localize("os.cal_event_note"), ref this.addNote, 96);
        y += inputH + ctx.Px(10f);

        ImGui.SetCursorScreenPos(new Vector2(panelTL.X + padIn, y + (inputH - lineH) * 0.5f));
        ImGui.PushStyleColor(ImGuiCol.Text, MutedText);
        ImGui.TextUnformatted(ctx.Localize("os.cal_time"));
        ImGui.PopStyleColor();
        var comboW = ctx.Px(58f);
        ImGui.SetCursorScreenPos(new Vector2(panelBR.X - padIn - comboW * 2f - ctx.Px(16f), y));
        ImGui.SetNextItemWidth(comboW);
        ImGui.Combo("##calHour", ref this.addHour, HourItems, HourItems.Length);
        ImGui.SameLine(0f, ctx.Px(4f));
        ImGui.TextUnformatted(":");
        ImGui.SameLine(0f, ctx.Px(4f));
        ImGui.SetNextItemWidth(comboW);
        var minuteIndex = Math.Clamp(this.addMinute / 5, 0, MinuteItems.Length - 1);
        if (ImGui.Combo("##calMinute", ref minuteIndex, MinuteItems, MinuteItems.Length))
        {
            this.addMinute = minuteIndex * 5;
        }
        PopInputStyle();

        var btnW = (innerW - ctx.Px(8f)) * 0.5f;
        var btnY = panelBR.Y - padIn - btnH;
        if (PanelButton(ctx, "##calAddCancel", ctx.Localize("common.cancel"), new Vector2(panelTL.X + padIn, btnY), new Vector2(btnW, btnH), PanelFill))
        {
            this.addOpen = false;
        }
        if (PanelButton(ctx, "##calAddOk", ctx.Localize("common.ok"), new Vector2(panelTL.X + padIn + btnW + ctx.Px(8f), btnY), new Vector2(btnW, btnH), ctx.Theme.Accent)
            && this.addTitle.Trim().Length > 0)
        {
            var local = this.selectedDay.Date.AddHours(this.addHour).AddMinutes(this.addMinute);
            this.events!.Add(new OwnEvent
            {
                Id = Guid.NewGuid().ToString("N"),
                Title = this.addTitle.Trim(),
                Note = this.addNote.Trim(),
                StartUtc = DateTime.SpecifyKind(local, DateTimeKind.Local).ToUniversalTime(),
            });
            storage.Set(EventsKey, this.events);
            this.addOpen = false;
        }

        // Scrim last: with overlapping items the first-submitted one wins clicks, so the panel widgets stay clickable.
        ImGui.SetCursorScreenPos(winPos);
        if (ImGui.InvisibleButton("##calAddScrim", winSize) && !InRect(ImGui.GetMousePos(), panelTL, panelBR))
        {
            this.addOpen = false;
        }
    }

    private void DrawDeleteConfirm(OsAppContext ctx, IAppStorage storage)
    {
        var winPos = ImGui.GetWindowPos();
        var winSize = ImGui.GetWindowSize();
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(winPos, winPos + winSize, U32(DimColor));

        var text = ctx.Localize("os.cal_delete_confirm");
        var padIn = ctx.Px(16f);
        var panelW = MathF.Min(winSize.X - ctx.Px(48f), ctx.Px(260f));
        var innerW = panelW - padIn * 2f;
        var textSize = ImGui.CalcTextSize(text, false, innerW);
        var btnH = ctx.Px(34f);
        var panelH = padIn + textSize.Y + ctx.Px(14f) + btnH + padIn;
        var panelTL = winPos + (winSize - new Vector2(panelW, panelH)) * 0.5f;
        var panelBR = panelTL + new Vector2(panelW, panelH);

        dl.AddRectFilled(panelTL, panelBR, U32(PanelBg), ctx.Px(16f));
        dl.AddRect(panelTL, panelBR, U32(PanelBorder), ctx.Px(16f), ImDrawFlags.RoundCornersAll, 1f);
        ImGui.SetCursorScreenPos(panelTL + new Vector2(padIn, padIn));
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + innerW);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();

        var btnW = (innerW - ctx.Px(8f)) * 0.5f;
        var btnY = panelBR.Y - padIn - btnH;
        if (PanelButton(ctx, "##calDelCancel", ctx.Localize("common.cancel"), new Vector2(panelTL.X + padIn, btnY), new Vector2(btnW, btnH), PanelFill))
        {
            this.confirmDeleteId = null;
        }
        if (PanelButton(ctx, "##calDelOk", ctx.Localize("common.ok"), new Vector2(panelTL.X + padIn + btnW + ctx.Px(8f), btnY), new Vector2(btnW, btnH), DangerFill))
        {
            this.events!.RemoveAll(e => e.Id == this.confirmDeleteId);
            storage.Set(EventsKey, this.events);
            this.confirmDeleteId = null;
        }

        // Scrim last: with overlapping items the first-submitted one wins clicks, so the panel widgets stay clickable.
        ImGui.SetCursorScreenPos(winPos);
        if (ImGui.InvisibleButton("##calDelScrim", winSize) && !InRect(ImGui.GetMousePos(), panelTL, panelBR))
        {
            this.confirmDeleteId = null;
        }
    }

    private static DateTime AsLocal(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();

    private static void PushInputStyle(OsAppContext ctx)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, ctx.Px(9f));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(ctx.Px(10f), ctx.Px(7f)));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(1f, 1f, 1f, 0.08f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(1f, 1f, 1f, 0.11f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(1f, 1f, 1f, 0.13f));
        ImGui.PushStyleColor(ImGuiCol.PopupBg, PanelBg);
    }

    private static void PopInputStyle()
    {
        ImGui.PopStyleColor(4);
        ImGui.PopStyleVar(2);
    }

    private static bool RoundIconButton(string id, FontAwesomeIcon icon, Vector2 center, float radius, float iconPx, Vector4? accentFill = null)
    {
        ImGui.SetCursorScreenPos(center - new Vector2(radius, radius));
        var clicked = ImGui.InvisibleButton(id, new Vector2(radius * 2f, radius * 2f));
        var hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        var fill = accentFill is { } accent
            ? (hovered ? accent with { W = 0.85f } : accent)
            : (hovered ? HoverFill : PanelFill);
        dl.AddCircleFilled(center, radius, U32(fill), 28);
        AddIconCentered(dl, icon, iconPx, center, U32(WhiteText));
        return clicked;
    }

    private static bool PanelButton(OsAppContext ctx, string id, string label, Vector2 tl, Vector2 size, Vector4 fill)
    {
        ImGui.SetCursorScreenPos(tl);
        var clicked = ImGui.InvisibleButton(id, size);
        var hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        var col = hovered
            ? new Vector4(fill.X + (1f - fill.X) * 0.12f, fill.Y + (1f - fill.Y) * 0.12f, fill.Z + (1f - fill.Z) * 0.12f, MathF.Min(1f, fill.W + 0.08f))
            : fill;
        dl.AddRectFilled(tl, tl + size, U32(col), ctx.Px(10f));
        var textSize = ImGui.CalcTextSize(label);
        dl.PushClipRect(tl, tl + size, true);
        dl.AddText(tl + (size - textSize) * 0.5f, U32(WhiteText), label);
        dl.PopClipRect();
        return clicked;
    }

    private static void AddIconCentered(ImDrawListPtr dl, FontAwesomeIcon icon, float px, Vector2 center, uint col)
    {
        var glyph = icon.ToIconString();
        ImGui.PushFont(UiBuilder.IconFont);
        var size = ImGui.CalcTextSize(glyph) * (px / ImGui.GetFontSize());
        dl.AddText(ImGui.GetFont(), px, center - size * 0.5f, col, glyph);
        ImGui.PopFont();
    }

    private static bool InRect(Vector2 p, Vector2 tl, Vector2 br)
    {
        return p.X >= tl.X && p.X <= br.X && p.Y >= tl.Y && p.Y <= br.Y;
    }

    private static uint U32(Vector4 c) => ImGui.ColorConvertFloat4ToU32(c);
}
