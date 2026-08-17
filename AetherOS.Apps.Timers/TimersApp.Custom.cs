using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Timers;

/// <summary>The custom-timers card and its in-page add overlay.</summary>
public sealed partial class TimersApp
{
    private const float AddRowHeight = 40f;
    private const float LeadPillHeight = 26f;

    private static readonly Vector4 PanelBg = new(0.11f, 0.10f, 0.13f, 1f);
    private static readonly Vector4 PanelBorder = new(0.32f, 0.30f, 0.38f, 0.65f);
    private static readonly Vector4 GhostFill = new(1f, 1f, 1f, 0.08f);

    private static readonly string[] HourItems = Enumerable.Range(0, 24).Select(h => h.ToString("00")).ToArray();
    private static readonly string[] MinuteItems = Enumerable.Range(0, 12).Select(m => (m * 5).ToString("00")).ToArray();
    private static readonly int[] LeadOptions = [0, 5, 15, 30];

    private readonly record struct CustomRowView(TimerRow Row, Guid Id, string DeleteId);

    private List<CustomTimer>? _customTimers;
    private CustomRowView[] _customRows = [];
    private bool _addOpen;
    private string _addName = "";
    private DateTime _addDate = DateTime.Now.Date;
    private int _addHour = 20;
    private int _addMinute;
    private readonly HashSet<int> _addLeads = new();
    private bool _addFocusPending;

    private void ReloadCustomTimers()
    {
        _customTimers = new List<CustomTimer>(_host.GetCustomTimers());
        BumpData();
    }

    private void BumpData() => System.Threading.Interlocked.Increment(ref _dataStamp);

    private void BuildCustomRows(DateTime utcNow)
    {
        if (_customTimers is not { } timers || timers.Count == 0)
        {
            _customRows = [];
            return;
        }
        var sorted = timers.OrderBy(t => t.DueUtc).ToArray();
        var rows = new CustomRowView[sorted.Length];
        for (var i = 0; i < sorted.Length; i++)
        {
            var timer = sorted[i];
            var elapsed = timer.DueUtc <= utcNow;
            var row = new TimerRow(FontAwesomeIcon.Clock, new Vector4(1f, 1f, 1f, 0.9f), timer.Name,
                ToLocal(timer.DueUtc).ToString("ddd d MMM HH:mm", _culture),
                elapsed ? Loc.T("os.timers_elapsed") : FormatCountdown(timer.DueUtc - utcNow),
                elapsed ? UiColors.Danger : UiColors.Body,
                elapsed ? "" : $"##calct{timer.Id:N}", timer.Name, elapsed ? 0L : ToUnix(timer.DueUtc));
            rows[i] = new CustomRowView(row, timer.Id, $"##delct{timer.Id:N}");
        }
        _customRows = rows;
    }

    private void DrawCustomCard(OsAppContext ctx)
    {
        var dl = ImGui.GetWindowDrawList();
        var winW = ImGui.GetWindowSize().X;
        var rowH = Px(RowHeight);
        var addH = Px(AddRowHeight);
        var lineH = ImGui.GetTextLineHeight();
        var bodyH = (_customRows.Length > 0 ? _customRows.Length * rowH : lineH + Px(10f)) + addH;
        var cardTL = BeginCard(dl, winW, bodyH, Loc.T("os.timers_custom_title"),
            out var cardW, out var cardH, out var y);

        if (_customRows.Length == 0)
        {
            dl.AddText(new Vector2(cardTL.X + Px(14f), y + Px(2f)), ImGui.GetColorU32(UiColors.Hint),
                Loc.T("os.timers_custom_empty"));
            y += lineH + Px(10f);
        }
        else
        {
            for (var i = 0; i < _customRows.Length; i++)
            {
                if (i > 0)
                {
                    DrawHairline(dl, cardTL.X, y, cardW);
                }
                var view = _customRows[i];
                var row = view.Row;
                var rowTL = new Vector2(cardTL.X, y);
                var hovered = ImGui.IsMouseHoveringRect(rowTL, rowTL + new Vector2(cardW, rowH));
                DrawTimerRow(dl, rowTL, cardW, rowH, in row, rightInset: Px(26f));
                if (hovered
                    && RoundIconButton(view.DeleteId, FontAwesomeIcon.Trash,
                        new Vector2(cardTL.X + cardW - Px(23f), y + rowH * 0.5f), Px(11f), Px(9f)))
                {
                    DeleteCustomTimer(view.Id);
                }
                y += rowH;
            }
        }

        DrawHairline(dl, cardTL.X, y, cardW);
        DrawAddRow(dl, new Vector2(cardTL.X, y), cardW, addH);

        EndCard(cardTL, cardW, cardH);
    }

    private void DrawAddRow(ImDrawListPtr dl, Vector2 tl, float w, float h)
    {
        var t = ThemeService.Current;
        ImGui.SetCursorScreenPos(tl);
        var clicked = ImGui.InvisibleButton("##customAdd", new Vector2(w, h));
        HandOnHover();
        if (ImGui.IsItemHovered())
        {
            dl.AddRectFilled(tl, tl + new Vector2(w, h), OsDrawShared.White(0.03f));
        }
        var chipR = Px(11f);
        var chipC = new Vector2(tl.X + Px(14f) + chipR, tl.Y + h * 0.5f);
        dl.AddCircleFilled(chipC, chipR, t.AccentU32, 24);
        IconDraw.AddCentered(dl, FontAwesomeIcon.Plus, Px(10f), chipC, OsDrawShared.White(0.95f));
        var label = Loc.T("os.timers_custom_add");
        var labelSz = ImGui.CalcTextSize(label);
        dl.AddText(new Vector2(chipC.X + chipR + Px(10f), tl.Y + (h - labelSz.Y) * 0.5f),
            ImGui.ColorConvertFloat4ToU32(t.AccentLight), label);
        if (clicked)
        {
            OpenAddOverlay();
        }
    }

    private void OpenAddOverlay()
    {
        _addOpen = true;
        _addName = "";
        _addDate = DateTime.Now.Date;
        var soon = DateTime.Now.AddMinutes(30);
        _addHour = soon.Hour;
        _addMinute = soon.Minute / 5 * 5;
        _addLeads.Clear();
        _addLeads.Add(0);
        _addFocusPending = true;
    }

    private void DeleteCustomTimer(Guid id)
    {
        if (_customTimers is not { } timers)
        {
            return;
        }
        timers.RemoveAll(t => t.Id == id);
        _host.SaveCustomTimers(timers);
        BumpData();
    }

    private void DrawAddOverlay(OsAppContext ctx, Vector2 origin, Vector2 avail)
    {
        ImGui.SetCursorScreenPos(origin);
        using var layer = ImRaii.Child("##timersAddOverlay", avail, false,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoBackground);
        if (!layer)
        {
            return;
        }
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(origin, origin + avail, OsDrawShared.Black(0.55f));

        var padIn = Px(16f);
        var lineH = ImGui.GetTextLineHeight();
        var inputH = lineH + Px(14f);
        var pillH = Px(LeadPillHeight);
        var btnH = Px(34f);
        var panelW = MathF.Min(avail.X - Px(40f), Px(300f));
        var innerW = panelW - padIn * 2f;
        var panelH = padIn + lineH + Px(10f) + inputH + Px(10f) + inputH + Px(10f) + inputH + Px(12f)
            + lineH + Px(6f) + pillH + Px(14f) + btnH + padIn;
        var panelTL = origin + (avail - new Vector2(panelW, panelH)) * 0.5f;
        var panelBR = panelTL + new Vector2(panelW, panelH);

        dl.AddRectFilled(panelTL, panelBR, ImGui.ColorConvertFloat4ToU32(PanelBg), Px(16f));
        dl.AddRect(panelTL, panelBR, ImGui.ColorConvertFloat4ToU32(PanelBorder), Px(16f),
            ImDrawFlags.RoundCornersAll, 1f);
        dl.AddText(panelTL + new Vector2(padIn, padIn), ImGui.GetColorU32(UiColors.Hint),
            Loc.T("os.timers_custom_new"));

        PushOverlayInputStyle();
        var y = panelTL.Y + padIn + lineH + Px(10f);
        ImGui.SetCursorScreenPos(new Vector2(panelTL.X + padIn, y));
        ImGui.SetNextItemWidth(innerW);
        if (_addFocusPending)
        {
            ImGui.SetKeyboardFocusHere();
            _addFocusPending = false;
        }
        ImGui.InputTextWithHint("##ctName", Loc.T("os.timers_custom_name_hint"), ref _addName, 64);
        y += inputH + Px(10f);

        ImGui.SetCursorScreenPos(new Vector2(panelTL.X + padIn, y + (inputH - lineH) * 0.5f));
        ImGui.TextColored(UiColors.Hint, Loc.T("os.timers_custom_date"));
        var dateText = _addDate.ToString("ddd d MMM", _culture);
        var dateSz = ImGui.CalcTextSize(dateText);
        var stepR = Px(12f);
        var dateRight = panelBR.X - padIn;
        var rowCy = y + inputH * 0.5f;
        if (RoundIconButton("##ctDateNext", FontAwesomeIcon.ChevronRight,
                new Vector2(dateRight - stepR, rowCy), stepR, Px(9f)))
        {
            _addDate = _addDate.AddDays(1);
        }
        dl.AddText(new Vector2(dateRight - stepR * 2f - Px(10f) - dateSz.X, rowCy - dateSz.Y * 0.5f),
            ImGui.GetColorU32(UiColors.Body), dateText);
        if (RoundIconButton("##ctDatePrev", FontAwesomeIcon.ChevronLeft,
                new Vector2(dateRight - stepR * 3f - Px(20f) - dateSz.X, rowCy), stepR, Px(9f)))
        {
            _addDate = _addDate.AddDays(-1);
        }
        y += inputH + Px(10f);

        ImGui.SetCursorScreenPos(new Vector2(panelTL.X + padIn, y + (inputH - lineH) * 0.5f));
        ImGui.TextColored(UiColors.Hint, Loc.T("os.timers_custom_time"));
        var comboW = Px(58f);
        ImGui.SetCursorScreenPos(new Vector2(panelBR.X - padIn - comboW * 2f - Px(16f), y));
        ImGui.SetNextItemWidth(comboW);
        ImGui.Combo("##ctHour", ref _addHour, HourItems, HourItems.Length);
        HandOnHover();
        ImGui.SameLine(0f, Px(4f));
        ImGui.TextUnformatted(":");
        ImGui.SameLine(0f, Px(4f));
        ImGui.SetNextItemWidth(comboW);
        var minuteIndex = Math.Clamp(_addMinute / 5, 0, MinuteItems.Length - 1);
        if (ImGui.Combo("##ctMinute", ref minuteIndex, MinuteItems, MinuteItems.Length))
        {
            _addMinute = minuteIndex * 5;
        }
        HandOnHover();
        y += inputH + Px(12f);

        dl.AddText(new Vector2(panelTL.X + padIn, y), ImGui.GetColorU32(UiColors.Hint),
            Loc.T("os.timers_custom_lead"));
        y += lineH + Px(6f);
        var pillX = panelTL.X + padIn;
        foreach (var option in LeadOptions)
        {
            var label = option == 0 ? Loc.T("os.timers_lead_at") : Loc.T("os.timers_lead_min", option);
            var selected = _addLeads.Contains(option);
            if (PillButton($"##ctLead{option}", label, selected, new Vector2(pillX, y), pillH, out var pillW))
            {
                if (selected)
                {
                    if (_addLeads.Count > 1)
                    {
                        _addLeads.Remove(option);
                    }
                }
                else
                {
                    _addLeads.Add(option);
                }
            }
            pillX += pillW + Px(8f);
        }
        PopOverlayInputStyle();

        var btnW = (innerW - Px(8f)) * 0.5f;
        var btnY = panelBR.Y - padIn - btnH;
        if (OverlayButton("##ctCancel", Loc.T("common.cancel"), new Vector2(panelTL.X + padIn, btnY),
                new Vector2(btnW, btnH), GhostFill))
        {
            _addOpen = false;
        }
        var canAdd = _addName.Trim().Length > 0;
        var okFill = canAdd ? ThemeService.Current.Accent : ThemeService.Current.Accent with { W = 0.35f };
        if (OverlayButton("##ctOk", Loc.T("common.ok"), new Vector2(panelTL.X + padIn + btnW + Px(8f), btnY),
                new Vector2(btnW, btnH), okFill, canAdd))
        {
            AddCustomTimer();
        }

        // Scrim last: with overlapping items the first-submitted one wins, so the panel controls stay live.
        ImGui.SetCursorScreenPos(origin);
        if (ImGui.InvisibleButton("##ctScrim", avail) && !InPanel(ImGui.GetMousePos(), panelTL, panelBR))
        {
            _addOpen = false;
        }
    }

    private void AddCustomTimer()
    {
        var local = _addDate.AddHours(_addHour).AddMinutes(_addMinute);
        var timer = new CustomTimer
        {
            Id = Guid.NewGuid(),
            Name = _addName.Trim(),
            DueUtc = DateTime.SpecifyKind(local, DateTimeKind.Local).ToUniversalTime(),
            LeadMinutes = _addLeads.OrderByDescending(v => v).ToList(),
        };
        _customTimers ??= new List<CustomTimer>();
        _customTimers.Add(timer);
        _host.SaveCustomTimers(_customTimers);
        BumpData();
        _addOpen = false;
    }

    private bool OverlayButton(string id, string label, Vector2 tl, Vector2 size, Vector4 fill,
        bool enabled = true)
    {
        ImGui.SetCursorScreenPos(tl);
        var clicked = ImGui.InvisibleButton(id, size) && enabled;
        if (enabled)
        {
            HandOnHover();
        }
        var hovered = enabled && ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        var col = hovered ? fill with { W = MathF.Min(1f, fill.W + 0.12f) } : fill;
        dl.AddRectFilled(tl, tl + size, ImGui.ColorConvertFloat4ToU32(col), Px(10f));
        var labelSz = ImGui.CalcTextSize(label);
        dl.PushClipRect(tl, tl + size, true);
        dl.AddText(tl + (size - labelSz) * 0.5f, ImGui.GetColorU32(UiColors.Body), label);
        dl.PopClipRect();
        return clicked;
    }

    private static bool InPanel(Vector2 p, Vector2 tl, Vector2 br)
    {
        return p.X >= tl.X && p.X <= br.X && p.Y >= tl.Y && p.Y <= br.Y;
    }

    private static void PushOverlayInputStyle()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(9f));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(Px(10f), Px(7f)));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(1f, 1f, 1f, 0.08f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(1f, 1f, 1f, 0.11f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(1f, 1f, 1f, 0.13f));
        ImGui.PushStyleColor(ImGuiCol.PopupBg, PanelBg);
    }

    private static void PopOverlayInputStyle()
    {
        ImGui.PopStyleColor(4);
        ImGui.PopStyleVar(2);
    }
}
