using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Clock;

public sealed class ClockApp : IAetherApp
{
    private const float FaceDiameterRatio = 0.65f;
    private const double EorzeaTimeFactor = 1440.0 / 70.0;
    private const double SecondsPerDay = 86400.0;
    private const uint HandColor = 0xFFFFFFFFu;
    private const uint TickColor = 0xCCFFFFFFu;

    private static readonly Vector4 TileTopColor = new(0.30f, 0.38f, 0.92f, 1f);
    private static readonly Vector4 TileBottomColor = new(0.12f, 0.13f, 0.48f, 1f);
    private static readonly Vector4 MutedLabel = new(1f, 1f, 1f, 0.55f);

    private readonly Func<string> name;
    private readonly IClockAlarms alarms;

    private int tab;
    private int newHours;
    private int newMinutes;
    private int newSeconds;
    private string newLabel = string.Empty;
    private int alarmHour = -1;
    private int alarmMinute;
    private string alarmLabel = string.Empty;

    public ClockApp(Func<string> name, IClockAlarms alarms)
    {
        this.name = name;
        this.alarms = alarms;
    }

    public string Id => "clock";

    public string Name => this.name();

    public FontAwesomeIcon Icon => FontAwesomeIcon.Clock;

    public Vector4 TileTop => TileTopColor;

    public Vector4 TileBottom => TileBottomColor;

    public int Badge => this.alarms.RingingCount;

    public bool HasSurface => true;

    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Strings => AppStrings.Packs;

    public void Open()
    {
    }

    public void Draw(OsAppContext ctx)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(ctx.Px(10f), ctx.Px(8f)));
        var open = ImGui.BeginChild("##clockScroll", ImGui.GetContentRegionAvail(), false,
            ImGuiWindowFlags.AlwaysUseWindowPadding);
        ImGui.PopStyleVar();
        if (open)
        {
            DrawTabHeader(ctx);
            switch (this.tab)
            {
                case 1:
                    DrawTimers(ctx);
                    break;
                case 2:
                    DrawAlarms(ctx);
                    break;
                default:
                    DrawAnalogClock(ctx);
                    DrawDigitalRows(ctx);
                    break;
            }
        }
        ImGui.EndChild();
    }

    public void OnIntent(OsIntent intent)
    {
        if (intent.Type == OsIntents.OpenClockTimers)
        {
            this.tab = 1;
        }
    }

    private void DrawTabHeader(OsAppContext ctx)
    {
        var w = (ImGui.GetContentRegionAvail().X - ctx.Px(12f)) / 3f;
        TabButton(ctx, ctx.Localize("os.clock_tab_clock"), 0, w);
        ImGui.SameLine(0f, ctx.Px(6f));
        TabButton(ctx, ctx.Localize("os.clock_tab_timers"), 1, w);
        ImGui.SameLine(0f, ctx.Px(6f));
        TabButton(ctx, ctx.Localize("os.clock_tab_alarms"), 2, w);
        ImGui.Dummy(new Vector2(0f, ctx.Px(10f)));
    }

    private void TabButton(OsAppContext ctx, string label, int index, float width)
    {
        Segmented(ctx, label, this.tab == index, width, () => this.tab = index, ctx.Px(32f));
    }

    private static void Segmented(OsAppContext ctx, string label, bool active, float width, Action onClick, float height)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, active ? ctx.Theme.Accent : ctx.Theme.ChipFill);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, active ? ctx.Theme.AccentLight : ctx.Theme.ChipFill);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, ctx.Theme.AccentDark);
        if (ImGui.Button(label, new Vector2(width, height)))
        {
            onClick();
        }
        ImGui.PopStyleColor(3);
    }

    private void DrawRingingBanner(OsAppContext ctx)
    {
        if (this.alarms.RingingCount == 0)
        {
            return;
        }
        ImGui.PushStyleColor(ImGuiCol.Text, ctx.Theme.AccentLight);
        using (ctx.HeadingFont?.Push())
        {
            ImGui.TextUnformatted(ctx.Localize("os.clock_ringing"));
        }
        ImGui.PopStyleColor();
        ImGui.Dummy(new Vector2(0f, ctx.Px(4f)));
        if (PrimaryButton(ctx, ctx.Localize("os.clock_dismiss_all")))
        {
            this.alarms.DismissAllAlarms();
        }
        ImGui.Dummy(new Vector2(0f, ctx.Px(10f)));
        ImGui.Separator();
        ImGui.Dummy(new Vector2(0f, ctx.Px(6f)));
    }

    private void DrawTimers(OsAppContext ctx)
    {
        DrawRingingBanner(ctx);
        DrawNewTimerForm(ctx);
        DrawAlarmSettings(ctx);

        ImGui.Dummy(new Vector2(0f, ctx.Px(10f)));
        ImGui.Separator();
        ImGui.Dummy(new Vector2(0f, ctx.Px(6f)));
        SectionLabel(ctx, ctx.Localize("os.clock_active"));
        var timers = this.alarms.Timers.Where(t => !t.IsAlarm).ToList();
        if (timers.Count == 0)
        {
            SectionLabelMuted(ctx, ctx.Localize("os.clock_no_timers"));
            return;
        }
        foreach (var t in timers)
        {
            DrawEntryRow(ctx, t, string.IsNullOrWhiteSpace(t.Label) ? ctx.Localize("os.clock_untitled") : t.Label);
        }
    }

    private void DrawNewTimerForm(OsAppContext ctx)
    {
        SectionLabel(ctx, ctx.Localize("os.clock_new_timer"));

        var third = (ImGui.GetContentRegionAvail().X - ctx.Px(12f)) / 3f;
        Stepper(ctx, "os.clock_hours", "##h", ref this.newHours, 99, third);
        ImGui.SameLine(0f, ctx.Px(6f));
        Stepper(ctx, "os.clock_minutes", "##m", ref this.newMinutes, 59, third);
        ImGui.SameLine(0f, ctx.Px(6f));
        Stepper(ctx, "os.clock_seconds", "##s", ref this.newSeconds, 59, third);

        ImGui.Dummy(new Vector2(0f, ctx.Px(6f)));
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        ImGui.InputTextWithHint("##label", ctx.Localize("os.clock_label_hint"), ref this.newLabel, 64);

        ImGui.Dummy(new Vector2(0f, ctx.Px(6f)));
        var dur = new TimeSpan(this.newHours, this.newMinutes, this.newSeconds);
        var valid = dur > TimeSpan.Zero;
        if (!valid)
        {
            ImGui.BeginDisabled();
        }
        if (PrimaryButton(ctx, ctx.Localize("os.clock_start")))
        {
            this.alarms.AddTimer(this.newLabel.Trim(), dur);
            this.newHours = 0;
            this.newMinutes = 0;
            this.newSeconds = 0;
            this.newLabel = string.Empty;
        }
        if (!valid)
        {
            ImGui.EndDisabled();
            SectionLabelMuted(ctx, ctx.Localize("os.clock_invalid_duration"));
        }
    }

    private static void Stepper(OsAppContext ctx, string labelKey, string id, ref int value, int max, float width)
    {
        ImGui.BeginGroup();
        ImGui.PushStyleColor(ImGuiCol.Text, MutedLabel);
        ImGui.TextUnformatted(ctx.Localize(labelKey));
        ImGui.PopStyleColor();
        ImGui.SetNextItemWidth(width);
        if (ImGui.InputInt(id, ref value, 0, 0))
        {
            value = Math.Clamp(value, 0, max);
        }
        ImGui.EndGroup();
    }

    private void DrawAlarmSettings(OsAppContext ctx)
    {
        ImGui.Dummy(new Vector2(0f, ctx.Px(10f)));
        ImGui.Separator();
        ImGui.Dummy(new Vector2(0f, ctx.Px(6f)));

        SectionLabelMuted(ctx, ctx.Localize("os.clock_alarm_sound"));
        var sound = this.alarms.AlarmSoundId;
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - ctx.Px(92f));
        if (ImGui.BeginCombo("##alarmSound", SoundLabel(sound)))
        {
            for (var i = 1; i <= 16; i++)
            {
                if (ImGui.Selectable(SoundLabel(i), i == sound))
                {
                    this.alarms.AlarmSoundId = i;
                }
            }
            ImGui.EndCombo();
        }
        ImGui.SameLine(0f, ctx.Px(6f));
        if (ImGui.Button(ctx.Localize("os.clock_preview"), new Vector2(ctx.Px(84f), 0f)))
        {
            this.alarms.PreviewSound(this.alarms.AlarmSoundId);
        }

        ImGui.Dummy(new Vector2(0f, ctx.Px(6f)));
        var notify = this.alarms.NotifyInChat;
        if (ImGui.Checkbox(ctx.Localize("os.clock_notify_chat"), ref notify))
        {
            this.alarms.NotifyInChat = notify;
        }

        ImGui.Dummy(new Vector2(0f, ctx.Px(6f)));
        SectionLabelMuted(ctx, ctx.Localize("os.clock_auto_off"));
        var w = (ImGui.GetContentRegionAvail().X - ctx.Px(18f)) / 4f;
        AutoOffButton(ctx, "os.clock_auto_off_off", 0, w);
        ImGui.SameLine(0f, ctx.Px(6f));
        AutoOffButton(ctx, "os.clock_auto_off_1", 1, w);
        ImGui.SameLine(0f, ctx.Px(6f));
        AutoOffButton(ctx, "os.clock_auto_off_3", 3, w);
        ImGui.SameLine(0f, ctx.Px(6f));
        AutoOffButton(ctx, "os.clock_auto_off_5", 5, w);
    }

    private void AutoOffButton(OsAppContext ctx, string labelKey, int minutes, float width)
    {
        Segmented(ctx, ctx.Localize(labelKey), this.alarms.AutoOffMinutes == minutes, width,
            () => this.alarms.AutoOffMinutes = minutes, ctx.Px(28f));
    }

    private void DrawAlarms(OsAppContext ctx)
    {
        DrawRingingBanner(ctx);

        SectionLabelMuted(ctx, ctx.Localize("os.clock_current_time"));
        using (ctx.TitleFont?.Push())
        {
            ImGui.TextUnformatted(DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture));
        }

        ImGui.Dummy(new Vector2(0f, ctx.Px(10f)));
        ImGui.Separator();
        ImGui.Dummy(new Vector2(0f, ctx.Px(6f)));

        SectionLabel(ctx, ctx.Localize("os.clock_new_alarm"));
        if (this.alarmHour < 0)
        {
            var n = DateTime.Now;
            this.alarmHour = n.Hour;
            this.alarmMinute = n.Minute;
        }
        var half = (ImGui.GetContentRegionAvail().X - ctx.Px(6f)) / 2f;
        Stepper(ctx, "os.clock_hours", "##ah", ref this.alarmHour, 23, half);
        ImGui.SameLine(0f, ctx.Px(6f));
        Stepper(ctx, "os.clock_minutes", "##am", ref this.alarmMinute, 59, half);

        ImGui.Dummy(new Vector2(0f, ctx.Px(4f)));
        var target = string.Create(CultureInfo.InvariantCulture, $"{this.alarmHour:00}:{this.alarmMinute:00}");
        SectionLabelMuted(ctx, string.Format(ctx.Localize("os.clock_rings_at"), target));

        ImGui.Dummy(new Vector2(0f, ctx.Px(6f)));
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        ImGui.InputTextWithHint("##alabel", ctx.Localize("os.clock_label_hint"), ref this.alarmLabel, 64);

        ImGui.Dummy(new Vector2(0f, ctx.Px(6f)));
        if (PrimaryButton(ctx, ctx.Localize("os.clock_set_alarm")))
        {
            this.alarms.AddAlarm(this.alarmHour, this.alarmMinute, this.alarmLabel.Trim());
            this.alarmLabel = string.Empty;
        }

        DrawAlarmSettings(ctx);

        ImGui.Dummy(new Vector2(0f, ctx.Px(10f)));
        ImGui.Separator();
        ImGui.Dummy(new Vector2(0f, ctx.Px(6f)));
        SectionLabel(ctx, ctx.Localize("os.clock_active_alarms"));
        var list = this.alarms.Timers.Where(t => t.IsAlarm).ToList();
        if (list.Count == 0)
        {
            SectionLabelMuted(ctx, ctx.Localize("os.clock_no_alarms"));
            return;
        }
        foreach (var a in list)
        {
            var time = a.DueUtc.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
            var primary = string.IsNullOrWhiteSpace(a.Label) ? time : $"{time}  {a.Label}";
            DrawEntryRow(ctx, a, primary);
        }
    }

    private void DrawEntryRow(OsAppContext ctx, ClockTimerView t, string primary)
    {
        var dl = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var height = ctx.Px(52f);
        var ringing = t.State == ClockTimerState.Ringing;
        var fill = ringing ? ctx.Theme.Accent with { W = 0.32f } : ctx.Theme.ChipFill;
        dl.AddRectFilled(pos, pos + new Vector2(width, height), ImGui.ColorConvertFloat4ToU32(fill), ctx.Px(12f));

        ImGui.SetCursorScreenPos(pos + ctx.Px(14f, 8f));
        ImGui.TextUnformatted(primary);

        var secondary = ringing ? ctx.Localize("os.clock_ringing") : FormatRemaining(t.Remaining);
        ImGui.SetCursorScreenPos(pos + ctx.Px(14f, 28f));
        ImGui.PushStyleColor(ImGuiCol.Text, ringing ? ctx.Theme.AccentLight : MutedLabel);
        ImGui.TextUnformatted(secondary);
        ImGui.PopStyleColor();

        var btnLabel = ringing ? ctx.Localize("os.clock_dismiss") : ctx.Localize("os.clock_cancel");
        var btnW = ctx.Px(80f);
        var btnH = ctx.Px(28f);
        ImGui.SetCursorScreenPos(new Vector2(pos.X + width - btnW - ctx.Px(10f), pos.Y + (height - btnH) * 0.5f));
        if (ImGui.Button($"{btnLabel}##t{t.Id:N}", new Vector2(btnW, btnH)))
        {
            this.alarms.RemoveTimer(t.Id);
        }

        ImGui.SetCursorScreenPos(pos);
        ImGui.Dummy(new Vector2(width, height));
        ImGui.Dummy(new Vector2(0f, ctx.Px(8f)));
    }

    private static string SoundLabel(int id) => string.Create(CultureInfo.InvariantCulture, $"<se.{id}>");

    private static string FormatRemaining(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
        {
            span = TimeSpan.Zero;
        }
        return span.TotalHours >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{span.Minutes:00}:{span.Seconds:00}");
    }

    private static void SectionLabel(OsAppContext ctx, string text)
    {
        using (ctx.HeadingFont?.Push())
        {
            ImGui.TextUnformatted(text);
        }
        ImGui.Dummy(new Vector2(0f, ctx.Px(4f)));
    }

    private static void SectionLabelMuted(OsAppContext ctx, string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, MutedLabel);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
    }

    private static bool PrimaryButton(OsAppContext ctx, string label)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, ctx.Theme.ButtonNormal);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ctx.Theme.ButtonHovered);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, ctx.Theme.ButtonActive);
        var clicked = ImGui.Button(label, new Vector2(ImGui.GetContentRegionAvail().X, ctx.Px(38f)));
        ImGui.PopStyleColor(3);
        return clicked;
    }

    private static void DrawAnalogClock(OsAppContext ctx)
    {
        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var radius = ctx.ContentSize.X * FaceDiameterRatio * 0.5f;
        var center = new Vector2(origin.X + width * 0.5f, origin.Y + radius + ctx.Px(26f));

        dl.AddCircleFilled(center, radius, ImGui.ColorConvertFloat4ToU32(ctx.Theme.ChipFill), 96);
        dl.AddCircle(center, radius, ImGui.ColorConvertFloat4ToU32(ctx.Theme.AccentDark), 96, ctx.Px(3f));

        for (var i = 0; i < 12; i++)
        {
            var angle = i * MathF.PI / 6f - MathF.PI / 2f;
            var dir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            var quarter = i % 3 == 0;
            var inner = radius * (quarter ? 0.82f : 0.88f);
            var outer = radius * 0.95f;
            dl.AddLine(center + dir * inner, center + dir * outer, TickColor, ctx.Px(quarter ? 3f : 1.5f));
        }

        var now = DateTime.Now;
        var seconds = now.Second + (ctx.ReduceMotion ? 0f : now.Millisecond / 1000f);
        var minutes = now.Minute + seconds / 60f;
        var hours = now.Hour % 12 + minutes / 60f;

        DrawHand(dl, center, hours / 12f, radius * 0.50f, HandColor, ctx.Px(4f));
        DrawHand(dl, center, minutes / 60f, radius * 0.72f, HandColor, ctx.Px(3f));
        DrawHand(dl, center, seconds / 60f, radius * 0.80f, ImGui.ColorConvertFloat4ToU32(ctx.Theme.Accent), ctx.Px(1.5f));
        dl.AddCircleFilled(center, ctx.Px(4.5f), ImGui.ColorConvertFloat4ToU32(ctx.Theme.Accent), 24);

        ImGui.Dummy(new Vector2(width, radius * 2f + ctx.Px(52f)));
    }

    private static void DrawHand(ImDrawListPtr dl, Vector2 center, float turns, float length, uint color, float thickness)
    {
        var angle = turns * MathF.Tau - MathF.PI / 2f;
        var dir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        dl.AddLine(center - dir * (length * 0.15f), center + dir * length, color, thickness);
    }

    private static void DrawDigitalRows(OsAppContext ctx)
    {
        var unixSeconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        var eorzeaSeconds = unixSeconds * EorzeaTimeFactor % SecondsPerDay;
        var eorzeaHours = (int)(eorzeaSeconds / 3600.0);
        var eorzeaMinutes = (int)(eorzeaSeconds / 60.0) % 60;

        DrawTimeRow(ctx, ctx.Localize("os.clock_local"), DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
        DrawTimeRow(ctx, ctx.Localize("os.clock_server"), DateTime.UtcNow.ToString("HH:mm", CultureInfo.InvariantCulture));
        DrawTimeRow(ctx, ctx.Localize("os.clock_eorzea"), string.Create(CultureInfo.InvariantCulture, $"{eorzeaHours:00}:{eorzeaMinutes:00}"));
    }

    private static void DrawTimeRow(OsAppContext ctx, string label, string time)
    {
        var dl = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var height = ctx.Px(58f);
        dl.AddRectFilled(pos, pos + new Vector2(width, height), ImGui.ColorConvertFloat4ToU32(ctx.Theme.ChipFill), ctx.Px(14f));

        ImGui.SetCursorScreenPos(pos + ctx.Px(14f, 8f));
        ImGui.PushStyleColor(ImGuiCol.Text, MutedLabel);
        ImGui.TextUnformatted(label);
        ImGui.PopStyleColor();

        using (ctx.TitleFont?.Push())
        {
            var textSize = ImGui.CalcTextSize(time);
            ImGui.SetCursorScreenPos(new Vector2(pos.X + width - textSize.X - ctx.Px(14f), pos.Y + (height - textSize.Y) * 0.5f));
            ImGui.TextUnformatted(time);
        }

        ImGui.SetCursorScreenPos(pos);
        ImGui.Dummy(new Vector2(width, height));
        ImGui.Dummy(new Vector2(0f, ctx.Px(10f)));
    }
}
