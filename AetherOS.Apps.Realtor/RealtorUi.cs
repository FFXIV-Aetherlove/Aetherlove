using System;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Services.Realtor;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Realtor;

/// <summary>Small shared bits for the Realtor screens: district icon/color mapping and duration text.</summary>
internal static class RealtorUi
{
    private const int Mist = 339;
    private const int LavenderBeds = 340;
    private const int Goblet = 341;
    private const int Shirogane = 641;
    private const int Empyreum = 979;

    public static FontAwesomeIcon DistrictIcon(int districtId) => districtId switch
    {
        Mist => FontAwesomeIcon.Water,
        LavenderBeds => FontAwesomeIcon.Seedling,
        Goblet => FontAwesomeIcon.WineGlass,
        Shirogane => FontAwesomeIcon.ToriiGate,
        Empyreum => FontAwesomeIcon.Snowflake,
        _ => FontAwesomeIcon.Home,
    };

    public static Vector4 DistrictColor(int districtId) => districtId switch
    {
        Mist => new Vector4(0.36f, 0.74f, 0.86f, 1f),
        LavenderBeds => new Vector4(0.66f, 0.56f, 0.90f, 1f),
        Goblet => new Vector4(0.90f, 0.66f, 0.32f, 1f),
        Shirogane => new Vector4(0.90f, 0.48f, 0.48f, 1f),
        Empyreum => new Vector4(0.62f, 0.80f, 0.95f, 1f),
        _ => new Vector4(0.70f, 0.70f, 0.70f, 1f),
    };

    public static string FormatAgo(DateTimeOffset at)
    {
        var delta = DateTimeOffset.UtcNow - at;
        if (delta < TimeSpan.Zero)
        {
            delta = TimeSpan.Zero;
        }
        return FormatSpan(delta);
    }

    public static string FormatSpan(TimeSpan span)
    {
        if (span.TotalDays >= 1)
        {
            var hours = span.Hours;
            var text = $"{(int)span.TotalDays}{Loc.T("os.realtor_unit_d")}";
            if (hours > 0)
            {
                text += $" {hours}{Loc.T("os.realtor_unit_h")}";
            }
            return text;
        }
        if (span.TotalHours >= 1)
        {
            var minutes = span.Minutes;
            var text = $"{(int)span.TotalHours}{Loc.T("os.realtor_unit_h")}";
            if (minutes > 0)
            {
                text += $" {minutes}{Loc.T("os.realtor_unit_m")}";
            }
            return text;
        }
        return $"{Math.Max((int)span.TotalMinutes, 1)}{Loc.T("os.realtor_unit_m")}";
    }

    /// <summary>Largest unit only, for tight spots: "4d", "23h", "12m".</summary>
    public static string FormatAgoCoarse(DateTimeOffset at)
    {
        var span = DateTimeOffset.UtcNow - at;
        if (span.TotalDays >= 1)
        {
            return $"{(int)span.TotalDays}{Loc.T("os.realtor_unit_d")}";
        }
        if (span.TotalHours >= 1)
        {
            return $"{(int)span.TotalHours}{Loc.T("os.realtor_unit_h")}";
        }
        return $"{Math.Max((int)span.TotalMinutes, 1)}{Loc.T("os.realtor_unit_m")}";
    }

    /// <summary>The world's lottery phase, read off any lottery plot: the cycle is world-wide, so every plot
    /// carries the same phase and deadline. Null when no open lottery plot reported one.</summary>
    public static (int Phase, DateTimeOffset? Until)? FindLotteryPhase(PaissaWorldDetail detail)
    {
        foreach (var district in detail.Districts)
        {
            if (district.OpenPlots is not { } plots)
            {
                continue;
            }
            foreach (var plot in plots)
            {
                if (plot.LottoPhase is { } phase)
                {
                    return (phase, plot.PhaseUntil);
                }
            }
        }
        return null;
    }

    public static (string Text, Vector4 Color) PhaseLabel(int phase) => phase switch
    {
        PaissaLottoPhase.Accepting => (Loc.T("os.realtor_lotto_accepting_open"), UiColors.LiveGreen),
        PaissaLottoPhase.Results => (Loc.T("os.realtor_lotto_results_open"), new Vector4(0.95f, 0.78f, 0.35f, 1f)),
        _ => (Loc.T("os.realtor_lotto_unavailable"), UiColors.Hint),
    };

    /// <summary>The shared filter pill row (S/M/L plus FC/personal), drawn at the current cursor.
    /// Toggles write straight into <paramref name="filters"/>.</summary>
    public static void DrawFilterRow(string idPrefix, RealtorFilters filters)
    {
        if (DrawFilterPill($"{idPrefix}S", Loc.T("os.realtor_filter_small"), filters.Sizes.Contains(0)))
        {
            filters.ToggleSize(0);
        }
        ImGui.SameLine(0f, Px(6f));
        if (DrawFilterPill($"{idPrefix}M", Loc.T("os.realtor_filter_medium"), filters.Sizes.Contains(1)))
        {
            filters.ToggleSize(1);
        }
        ImGui.SameLine(0f, Px(6f));
        if (DrawFilterPill($"{idPrefix}L", Loc.T("os.realtor_filter_large"), filters.Sizes.Contains(2)))
        {
            filters.ToggleSize(2);
        }
        ImGui.SameLine(0f, Px(12f));
        if (DrawFilterPill($"{idPrefix}Fc", Loc.T("os.realtor_chip_fc"), filters.FreeCompany))
        {
            filters.ToggleFreeCompany();
        }
        ImGui.SameLine(0f, Px(6f));
        if (DrawFilterPill($"{idPrefix}P", Loc.T("os.realtor_chip_personal"), filters.Personal))
        {
            filters.TogglePersonal();
        }
    }

    private static bool DrawFilterPill(string id, string label, bool active)
    {
        var t = ThemeService.Current;
        var sz = ImGui.CalcTextSize(label);
        var pillW = sz.X + Px(18f);
        var pillH = sz.Y + Px(8f);
        var tl = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton($"##realtorFilter{id}", new Vector2(pillW, pillH));
        HandOnHover();
        var hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(tl, tl + new Vector2(pillW, pillH),
            ImGui.GetColorU32(active
                ? t.Accent with { W = 0.55f }
                : new Vector4(1f, 1f, 1f, hovered ? 0.12f : 0.06f)), pillH * 0.5f);
        dl.AddText(tl + new Vector2(Px(9f), Px(4f)),
            ImGui.GetColorU32(active ? new Vector4(1f, 1f, 1f, 1f) : UiColors.Body), label);
        return clicked;
    }
}
