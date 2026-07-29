using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AetherLove;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Services.Market;
using AetherLove.Services.Realtor;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Realtor;

/// <summary>One district's open plots: size and tenant filters on top, then a card per plot with its
/// price, lottery state and data freshness. Reopening refreshes through the cached service.</summary>
internal sealed class DistrictScreen
{
    private const float PadX = 16f;

    private readonly RealtorDataService _data;
    private readonly RealtorFilters _filters;
    private readonly Action _back;
    private readonly EntranceAnimation _entrance = new();

    private int _worldId;
    private string _worldName = "";
    private int _districtId;
    private string _districtName = "";
    private volatile List<PaissaPlot>? _plots;
    private int _generation;

    public DistrictScreen(RealtorDataService data, RealtorFilters filters, Action back)
    {
        _data = data;
        _filters = filters;
        _back = back;
    }

    public void Open(int worldId, string worldName, PaissaDistrict district)
    {
        _worldId = worldId;
        _worldName = worldName;
        _districtId = district.Id;
        _districtName = district.Name;
        _plots = SortPlots(district.OpenPlots ?? []);
        _entrance.Arm();
    }

    public void OnShow()
    {
        _entrance.Arm();
        Refresh();
    }

    private void Refresh()
    {
        if (_worldId == 0)
        {
            return;
        }
        var generation = Interlocked.Increment(ref _generation);
        var districtId = _districtId;
        var worldId = _worldId;
        _ = Task.Run(async () =>
        {
            try
            {
                var detail = await _data.GetWorldAsync(worldId, CancellationToken.None).ConfigureAwait(false);
                var district = detail?.Districts.Find(d => d.Id == districtId);
                if (district is not null && generation == _generation)
                {
                    _plots = SortPlots(district.OpenPlots ?? []);
                }
            }
            catch (Exception ex)
            {
                UiHost.Log.Debug($"[Realtor] District refresh failed: {ex.Message}");
            }
        });
    }

    private static List<PaissaPlot> SortPlots(List<PaissaPlot> plots) =>
        [.. plots.OrderBy(p => p.WardNumber).ThenBy(p => p.PlotNumber)];

    public void Draw(OsAppContext ctx)
    {
        _entrance.BeginFrame();
        var winW = ImGui.GetWindowSize().X;
        if (RealtorHeader.Draw($"{_districtName} · {_worldName}"))
        {
            _back();
            _entrance.EndFrame();
            return;
        }

        ImGui.SetCursorPosX(Px(PadX));
        RealtorUi.DrawFilterRow("district", _filters);
        ImGui.Dummy(new Vector2(0f, Px(8f)));

        var plots = _plots ?? [];
        if (plots.Count == 0)
        {
            ImGui.Dummy(new Vector2(0f, Px(24f)));
            DrawCenteredHint(Loc.T("os.realtor_empty_district"), winW);
            _entrance.EndFrame();
            return;
        }

        var any = false;
        foreach (var plot in plots)
        {
            if (!_filters.Matches(plot))
            {
                continue;
            }
            DrawPlotRow(plot, winW);
            any = true;
        }
        if (!any)
        {
            ImGui.Dummy(new Vector2(0f, Px(24f)));
            DrawCenteredHint(Loc.T("os.realtor_empty_filtered"), winW);
        }
        ImGui.Dummy(new Vector2(0f, Px(14f)));
        _entrance.EndFrame();
    }

    private void DrawPlotRow(PaissaPlot plot, float winW)
    {
        var cardW = winW - Px(PadX) * 2f;
        var cardH = Px(72f);
        ImGui.SetCursorPosX(Px(PadX));
        var tl = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(tl, tl + new Vector2(cardW, cardH), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.05f)), Px(12f));

        var (sizeLabel, sizeColor) = plot.Size switch
        {
            0 => (Loc.T("os.realtor_size_s"), UiColors.LiveGreen),
            1 => (Loc.T("os.realtor_size_m"), new Vector4(0.95f, 0.78f, 0.35f, 1f)),
            _ => (Loc.T("os.realtor_size_l"), new Vector4(0.72f, 0.50f, 0.95f, 1f)),
        };
        var chip = Px(26f);
        var chipTl = new Vector2(tl.X + Px(10f), tl.Y + (cardH - chip) * 0.5f);
        dl.AddRectFilled(chipTl, chipTl + new Vector2(chip, chip), ImGui.GetColorU32(sizeColor with { W = 0.20f }), Px(7f));
        var chipSz = ImGui.CalcTextSize(sizeLabel);
        dl.AddText(chipTl + new Vector2((chip - chipSz.X) * 0.5f, (chip - chipSz.Y) * 0.5f),
            ImGui.GetColorU32(sizeColor), sizeLabel);

        var textX = tl.X + Px(10f) + chip + Px(10f);
        var lineH = ImGui.GetTextLineHeight();
        var lineGap = Px(6f);
        var line1Y = tl.Y + (cardH - (lineH * 2f + lineGap)) * 0.5f;
        var line2Y = line1Y + lineH + lineGap;

        var price = $"{MarketFormat.GilFull(plot.Price)} gil";
        var priceSz = ImGui.CalcTextSize(price);
        var priceX = tl.X + cardW - priceSz.X - Px(12f);
        dl.AddText(new Vector2(priceX, line1Y), ImGui.GetColorU32(new Vector4(0.98f, 0.80f, 0.36f, 1f)), price);

        var title = Loc.T("os.realtor_ward_plot", plot.WardNumber + 1, plot.PlotNumber + 1);
        dl.AddText(new Vector2(textX, line1Y), ImGui.GetColorU32(UiColors.Body),
            TruncateToWidth(title, priceX - textX - Px(8f)));

        var tenantParts = new List<string>(2);
        if (plot.AllowsFreeCompany)
        {
            tenantParts.Add(Loc.T("os.realtor_chip_fc"));
        }
        if (plot.AllowsIndividual)
        {
            tenantParts.Add(Loc.T("os.realtor_chip_personal"));
        }
        var tenant = string.Join(" · ", tenantParts);
        var tenantSz = ImGui.CalcTextSize(tenant);
        var tenantX = tl.X + cardW - tenantSz.X - Px(12f);
        if (tenant.Length > 0)
        {
            dl.AddText(new Vector2(tenantX, line2Y), ImGui.GetColorU32(UiColors.Hint), tenant);
        }
        else
        {
            tenantX = tl.X + cardW - Px(12f);
        }

        var (status, statusColor) = StatusLine(plot);
        dl.AddText(new Vector2(textX, line2Y), ImGui.GetColorU32(statusColor),
            TruncateToWidth(status, tenantX - textX - Px(10f)));

        if (ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(tl, tl + new Vector2(cardW, cardH)))
        {
            ImGui.SetTooltip(Loc.T("os.realtor_checked", RealtorUi.FormatAgo(plot.LastUpdatedAt)));
        }

        ImGui.Dummy(new Vector2(0f, cardH + Px(6f)));
    }

    /// <summary>The per-plot status. The phase countdown is deliberately absent: it is identical for every
    /// plot on the world, so the home screen shows it once instead of truncating it on every row.</summary>
    private static (string Text, Vector4 Color) StatusLine(PaissaPlot plot)
    {
        if (!plot.IsLottery)
        {
            return (Loc.T("os.realtor_fcfs"), UiColors.Body);
        }
        switch (plot.LottoPhase)
        {
            case PaissaLottoPhase.Accepting:
            {
                var text = Loc.T("os.realtor_lotto_accepting_open");
                if (plot.LottoEntries is { } entries)
                {
                    text += " · " + Loc.T("os.realtor_entries", entries);
                }
                return (text, UiColors.LiveGreen);
            }
            case PaissaLottoPhase.Results:
                return (Loc.T("os.realtor_lotto_results_open"), new Vector4(0.95f, 0.78f, 0.35f, 1f));
            default:
                return (Loc.T("os.realtor_lotto_unavailable"), UiColors.Hint);
        }
    }

    private static void DrawCenteredHint(string text, float winW)
    {
        var wrapW = winW - Px(PadX) * 2.5f;
        var sz = ImGui.CalcTextSize(text, false, wrapW);
        ImGui.SetCursorPosX((winW - Math.Min(sz.X, wrapW)) * 0.5f);
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + wrapW);
        ImGui.TextColored(UiColors.Hint, text);
        ImGui.PopTextWrapPos();
    }
}
