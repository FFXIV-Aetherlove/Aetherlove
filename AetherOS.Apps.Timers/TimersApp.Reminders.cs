using System;
using System.Numerics;
using AetherLove.Services.Localization;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Timers;

/// <summary>The Reminders sub-view: per-kind toggles with lead-time pills, the cactpot region override,
/// and the DTR toggle. Every change saves through the host immediately.</summary>
public sealed partial class TimersApp
{
    private static readonly (ReminderKind Kind, string TitleKey, string HintKey)[] ReminderSections =
    [
        (ReminderKind.DailyReset, "os.timers_reset_daily", "os.timers_rem_daily_hint"),
        (ReminderKind.GrandCompanyReset, "os.timers_reset_gc", "os.timers_rem_gc_hint"),
        (ReminderKind.WeeklyReset, "os.timers_reset_weekly", "os.timers_rem_weekly_hint"),
        (ReminderKind.FashionReportOpen, "os.timers_rem_fr", "os.timers_rem_fr_hint"),
        (ReminderKind.CactpotDraw, "os.timers_cactpot_title", "os.timers_rem_cactpot_hint"),
        (ReminderKind.OceanBoarding, "os.timers_rem_ocean", "os.timers_rem_ocean_hint"),
        (ReminderKind.VentureComplete, "os.timers_rem_venture", "os.timers_rem_venture_hint"),
        (ReminderKind.FleetReturn, "os.timers_rem_fleet", "os.timers_rem_fleet_hint"),
    ];

    private void SaveConfig()
    {
        if (_config is { } config)
        {
            _host.SaveReminderConfig(config);
        }
    }

    private void DrawReminders(OsAppContext ctx)
    {
        _config ??= _host.GetReminderConfig();
        var config = _config;

        PushScrollbarStyle();
        using (var body = ImRaii.Child("##timersReminders", new Vector2(0f, 0f), false))
        {
            if (body)
            {
                _entrance.BeginFrame();
                ImGui.Dummy(new Vector2(0f, Px(4f)));
                ImGui.SetCursorPosX(Px(PadX));
                if (DrawFloatingBackPill(ImGui.GetCursorScreenPos(), Loc.T("os.timers_back"),
                        FontAwesomeIcon.HourglassHalf))
                {
                    _view = View.Main;
                    _entrance.Arm();
                }
                ImGui.Dummy(new Vector2(0f, Px(10f)));

                var winW = ImGui.GetWindowSize().X;
                var x = ImGui.GetWindowPos().X + Px(PadX);
                var width = winW - Px(PadX) * 2f;

                ImGui.SetCursorPosX(Px(PadX));
                using (UiFonts.H3?.Push())
                {
                    ImGui.TextColored(UiColors.Body, Loc.T("os.timers_rem_title"));
                }
                ImGui.Dummy(new Vector2(0f, Px(6f)));

                foreach (var section in ReminderSections)
                {
                    DrawReminderSection(ctx, config, section.Kind, section.TitleKey, section.HintKey, x, width);
                }

                ImGui.Dummy(new Vector2(0f, Px(4f)));
                AppSettingsUi.SectionLabel(ctx, x, Loc.T("os.timers_rem_region_title"));
                ImGui.SetCursorPosX(Px(PadX));
                ImGui.PushTextWrapPos(Px(PadX) + width);
                ImGui.TextColored(UiColors.Hint, Loc.T("os.timers_rem_region_hint"));
                ImGui.PopTextWrapPos();
                ImGui.Dummy(new Vector2(0f, Px(6f)));
                DrawRegionPills(config, x, width);
                ImGui.Dummy(new Vector2(0f, Px(12f)));

                var showDtr = config.ShowDtr;
                if (AppSettingsUi.SettingToggle(ctx, "timersDtr", Loc.T("os.timers_rem_dtr"),
                        Loc.T("os.timers_rem_dtr_hint"), x, width, ref showDtr))
                {
                    config.ShowDtr = showDtr;
                    SaveConfig();
                }

                ImGui.Dummy(new Vector2(0f, Px(16f)));
                _entrance.EndFrame();
            }
        }
        PopScrollbarStyle();
    }

    private void DrawReminderSection(OsAppContext ctx, ReminderConfig config, ReminderKind kind,
        string titleKey, string hintKey, float x, float width)
    {
        var entry = config.For(kind);
        var enabled = entry.Enabled;
        if (AppSettingsUi.SettingToggle(ctx, $"timersRem{(int)kind}", Loc.T(titleKey), Loc.T(hintKey),
                x, width, ref enabled))
        {
            entry.Enabled = enabled;
            if (enabled && entry.LeadMinutes.Count == 0)
            {
                entry.LeadMinutes.Add(0);
            }
            SaveConfig();
        }
        if (!entry.Enabled)
        {
            return;
        }

        var pillH = Px(LeadPillHeight);
        var pillX = x + Px(4f);
        var pillY = ImGui.GetCursorScreenPos().Y;
        foreach (var option in LeadOptions)
        {
            var label = option == 0 ? Loc.T("os.timers_lead_at") : Loc.T("os.timers_lead_min", option);
            var selected = entry.LeadMinutes.Contains(option);
            if (PillButton($"##rem{(int)kind}lead{option}", label, selected,
                    new Vector2(pillX, pillY), pillH, out var pillW))
            {
                if (selected)
                {
                    if (entry.LeadMinutes.Count > 1)
                    {
                        entry.LeadMinutes.Remove(option);
                        SaveConfig();
                    }
                }
                else
                {
                    entry.LeadMinutes.Add(option);
                    entry.LeadMinutes.Sort((a, b) => b.CompareTo(a));
                    SaveConfig();
                }
            }
            pillX += pillW + Px(8f);
        }
        ImGui.SetCursorScreenPos(new Vector2(x, pillY + pillH));
        ImGui.Dummy(new Vector2(width, Px(12f)));
    }

    private void DrawRegionPills(ReminderConfig config, float x, float width)
    {
        Span<GameRegion?> options = [null, GameRegion.Japan, GameRegion.NorthAmerica, GameRegion.Europe,
            GameRegion.Oceania];
        var pillH = Px(LeadPillHeight);
        var pillX = x;
        var pillY = ImGui.GetCursorScreenPos().Y;
        var rowBottom = pillY + pillH;
        foreach (var option in options)
        {
            var label = option is { } region ? RegionLabel(region) : Loc.T("os.timers_region_auto");
            var selected = config.CactpotRegionOverride == option;
            var labelW = ImGui.CalcTextSize(label).X + Px(20f);
            if (pillX + labelW > x + width)
            {
                pillX = x;
                pillY = rowBottom + Px(6f);
                rowBottom = pillY + pillH;
            }
            var id = option is { } r ? $"##ctRegion{(int)r}" : "##ctRegionAuto";
            if (PillButton(id, label, selected, new Vector2(pillX, pillY), pillH, out var pillW))
            {
                config.CactpotRegionOverride = option;
                SaveConfig();
                BumpData();
            }
            pillX += pillW + Px(8f);
        }
        ImGui.SetCursorScreenPos(new Vector2(x, rowBottom));
        ImGui.Dummy(new Vector2(width, 0f));
    }
}
