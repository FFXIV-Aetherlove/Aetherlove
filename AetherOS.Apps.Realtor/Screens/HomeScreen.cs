using System;
using System.Collections.Generic;
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
using AetherLove.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Realtor;

/// <summary>The Realtor home: the selected world's open-plot count plus one row per residential
/// district. Data comes from the cached PaissaDB service; a tap on a district opens its plot list.</summary>
internal sealed class HomeScreen
{
    private const float PadX = 16f;

    private readonly RealtorDataService _data;
    private readonly RealtorFilters _filters;
    private readonly Action _openWorldPick;
    private readonly Action<int, string, PaissaDistrict> _openDistrict;
    private readonly Action _openTour;
    private readonly EntranceAnimation _entrance = new();

    private string _worldName = "";
    private int _worldId;
    private volatile PaissaWorldDetail? _detail;
    private volatile bool _loading;
    private volatile bool _failed;
    private int _generation;

    public HomeScreen(RealtorDataService data, RealtorFilters filters, Action openWorldPick,
        Action<int, string, PaissaDistrict> openDistrict, Action openTour)
    {
        _data = data;
        _filters = filters;
        _openWorldPick = openWorldPick;
        _openDistrict = openDistrict;
        _openTour = openTour;
    }

    public string WorldName => _worldName;
    public int WorldId => _worldId;

    public void OnShow(string? storedWorld)
    {
        _entrance.Arm();
        if (_worldName.Length == 0)
        {
            _worldName = storedWorld ?? MarketScopes.DetectCurrent()?.World.ApiName ?? "";
        }
        StartFetch();
    }

    public void SetWorld(string name)
    {
        _worldName = name;
        _worldId = 0;
        _detail = null;
        _entrance.Arm();
        StartFetch();
    }

    private void StartFetch()
    {
        if (_worldName.Length == 0)
        {
            return;
        }
        var generation = Interlocked.Increment(ref _generation);
        var worldName = _worldName;
        _loading = true;
        _failed = false;
        _ = Task.Run(async () =>
        {
            try
            {
                var worlds = await _data.GetWorldsAsync(CancellationToken.None).ConfigureAwait(false);
                var world = worlds?.Find(w => string.Equals(w.Name, worldName, StringComparison.OrdinalIgnoreCase));
                if (world is null)
                {
                    if (generation == _generation)
                    {
                        _failed = true;
                    }
                    return;
                }
                var detail = await _data.GetWorldAsync(world.Id, CancellationToken.None).ConfigureAwait(false);
                if (generation == _generation)
                {
                    _worldId = world.Id;
                    _detail = detail;
                    _failed = detail is null;
                }
            }
            catch (Exception ex)
            {
                UiHost.Log.Debug($"[Realtor] Home fetch failed: {ex.Message}");
                if (generation == _generation)
                {
                    _failed = true;
                }
            }
            finally
            {
                if (generation == _generation)
                {
                    _loading = false;
                }
            }
        });
    }

    public void Draw(OsAppContext ctx)
    {
        _entrance.BeginFrame();
        var t = ThemeService.Current;
        var winW = ImGui.GetWindowSize().X;

        ImGui.Dummy(new Vector2(0f, Px(10f)));
        ImGui.SetCursorPosX(Px(PadX));
        using (ctx.TitleFont?.Push())
        {
            ImGui.TextColored(t.AccentLight, Loc.T("os.app_realtor"));
        }
        DrawWorldPill(winW);

        ImGui.Dummy(new Vector2(0f, Px(6f)));

        if (_worldName.Length == 0)
        {
            DrawCenteredHint(Loc.T("os.realtor_unknown_world"), winW);
            _entrance.EndFrame();
            return;
        }

        var detail = _detail;
        if (detail is null)
        {
            if (_loading)
            {
                ImGui.Dummy(new Vector2(0f, Px(40f)));
                var center = new Vector2(ImGui.GetWindowPos().X + winW * 0.5f, ImGui.GetCursorScreenPos().Y + Px(20f));
                LoadingSpinner.Draw(center, Px(14f), Px(3f), ImGui.GetColorU32(t.Accent));
                ImGui.Dummy(new Vector2(0f, Px(50f)));
                DrawCenteredHint(Loc.T("os.realtor_loading"), winW);
            }
            else if (_failed)
            {
                DrawCenteredHint(Loc.T("os.realtor_offline"), winW);
                DrawRetry(winW);
            }
            _entrance.EndFrame();
            return;
        }

        DrawHero(detail, winW);
        ImGui.Dummy(new Vector2(0f, Px(10f)));
        ImGui.SetCursorPosX(Px(PadX));
        RealtorUi.DrawFilterRow("home", _filters);
        ImGui.Dummy(new Vector2(0f, Px(8f)));
        foreach (var district in detail.Districts)
        {
            DrawDistrictRow(district, winW);
        }

        ImGui.Dummy(new Vector2(0f, Px(14f)));
        _entrance.EndFrame();
    }

    private void DrawWorldPill(float winW)
    {
        var t = ThemeService.Current;
        var label = _worldName.Length > 0 ? _worldName : Loc.T("os.realtor_pick_world");
        var iconPx = ImGui.GetFontSize() * 0.72f;
        var textSz = ImGui.CalcTextSize(label);
        var pillW = textSz.X + IconDraw.Measure(FontAwesomeIcon.Globe, iconPx).X + Px(26f);
        var pillH = textSz.Y + Px(10f);
        var dlTop = ImGui.GetWindowDrawList();

        ImGui.SameLine();
        var baseY = ImGui.GetCursorPosY() + Px(2f);
        ImGui.SetCursorPosX(winW - pillW - Px(PadX) - pillH - Px(8f));
        ImGui.SetCursorPosY(baseY);
        var btnTl = ImGui.GetCursorScreenPos();
        var tourClicked = ImGui.InvisibleButton("##realtorTourBtn", new Vector2(pillH, pillH));
        HandOnHover();
        var tourHovered = ImGui.IsItemHovered();
        dlTop.AddCircleFilled(btnTl + new Vector2(pillH * 0.5f, pillH * 0.5f), pillH * 0.5f,
            ImGui.GetColorU32(t.Accent with { W = tourHovered ? 0.32f : 0.16f }));
        var qSz = IconDraw.Measure(FontAwesomeIcon.QuestionCircle, iconPx);
        IconDraw.Add(dlTop, FontAwesomeIcon.QuestionCircle, iconPx,
            btnTl + new Vector2((pillH - qSz.X) * 0.5f, (pillH - qSz.Y) * 0.5f), ImGui.GetColorU32(t.AccentLight));
        if (tourHovered)
        {
            ImGui.SetTooltip(Loc.T("os.realtor_menu_tour"));
        }
        if (tourClicked)
        {
            _openTour();
        }

        ImGui.SameLine();
        ImGui.SetCursorPosX(winW - pillW - Px(PadX));
        ImGui.SetCursorPosY(baseY);
        var tl = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##realtorWorldPill", new Vector2(pillW, pillH));
        HandOnHover();
        var hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(tl, tl + new Vector2(pillW, pillH),
            ImGui.GetColorU32(t.Accent with { W = hovered ? 0.32f : 0.16f }), pillH * 0.5f);
        IconDraw.Add(dl, FontAwesomeIcon.Globe, iconPx,
            new Vector2(tl.X + Px(10f), tl.Y + (pillH - iconPx) * 0.5f), ImGui.GetColorU32(t.AccentLight));
        dl.AddText(new Vector2(tl.X + Px(16f) + IconDraw.Measure(FontAwesomeIcon.Globe, iconPx).X,
            tl.Y + (pillH - textSz.Y) * 0.5f), ImGui.GetColorU32(UiColors.Body), label);
        if (clicked)
        {
            _openWorldPick();
        }
    }

    private void DrawHero(PaissaWorldDetail detail, float winW)
    {
        var t = ThemeService.Current;
        var cardW = winW - Px(PadX) * 2f;
        var cardH = Px(84f);
        ImGui.SetCursorPosX(Px(PadX));
        var tl = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(tl, tl + new Vector2(cardW, cardH), ImGui.GetColorU32(t.Accent with { W = 0.14f }), Px(16f));
        dl.AddRect(tl, tl + new Vector2(cardW, cardH), ImGui.GetColorU32(t.Accent with { W = 0.45f }), Px(16f),
            ImDrawFlags.None, Px(1.2f));

        using (UiFonts.H1?.Push())
        {
            dl.AddText(new Vector2(tl.X + Px(16f), tl.Y + Px(12f)), ImGui.GetColorU32(t.AccentLight),
                detail.NumOpenPlots.ToString());
        }
        float bigH;
        using (UiFonts.H1?.Push())
        {
            bigH = ImGui.GetTextLineHeight();
        }
        dl.AddText(new Vector2(tl.X + Px(16f), tl.Y + Px(12f) + bigH + Px(2f)),
            ImGui.GetColorU32(UiColors.Body), Loc.T("os.realtor_hero_sub", detail.Name));

        if (detail.NumOpenPlots > 0 && detail.OldestPlotTime > 0)
        {
            var stale = Loc.T("os.realtor_stale",
                RealtorUi.FormatAgoCoarse(DateTimeOffset.FromUnixTimeSeconds((long)detail.OldestPlotTime)));
            var sz = ImGui.CalcTextSize(stale);
            dl.AddText(new Vector2(tl.X + cardW - sz.X - Px(14f), tl.Y + Px(12f)),
                ImGui.GetColorU32(UiColors.Hint), stale);
        }
        ImGui.Dummy(new Vector2(0f, cardH));
    }

    private void DrawDistrictRow(PaissaDistrict district, float winW)
    {
        var t = ThemeService.Current;
        var cardW = winW - Px(PadX) * 2f;
        var cardH = Px(58f);
        var count = _filters.CountFor(district);
        var enabled = count > 0;
        ImGui.SetCursorPosX(Px(PadX));
        var tl = ImGui.GetCursorScreenPos();
        var clicked = false;
        var hovered = false;
        if (enabled)
        {
            clicked = ImGui.InvisibleButton($"##realtorDistrict{district.Id}", new Vector2(cardW, cardH));
            HandOnHover();
            hovered = ImGui.IsItemHovered();
        }
        else
        {
            ImGui.Dummy(new Vector2(cardW, cardH));
        }
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(tl, tl + new Vector2(cardW, cardH),
            ImGui.GetColorU32(hovered ? t.Accent with { W = 0.18f } : new Vector4(1f, 1f, 1f, enabled ? 0.06f : 0.03f)),
            Px(14f));

        var dim = enabled ? 1f : 0.45f;
        var color = RealtorUi.DistrictColor(district.Id);
        var circle = Px(36f);
        var circleTl = new Vector2(tl.X + Px(11f), tl.Y + (cardH - circle) * 0.5f);
        dl.AddCircleFilled(circleTl + new Vector2(circle * 0.5f, circle * 0.5f), circle * 0.5f,
            ImGui.GetColorU32(color with { W = 0.22f * dim }));
        var iconPx = circle * 0.52f;
        var icon = RealtorUi.DistrictIcon(district.Id);
        var iconSz = IconDraw.Measure(icon, iconPx);
        IconDraw.Add(dl, icon, iconPx,
            circleTl + new Vector2((circle - iconSz.X) * 0.5f, (circle - iconSz.Y) * 0.5f),
            ImGui.GetColorU32(color with { W = dim }));

        var textX = circleTl.X + circle + Px(11f);
        dl.AddText(new Vector2(textX, tl.Y + (cardH - ImGui.GetTextLineHeight()) * 0.5f),
            ImGui.GetColorU32(UiColors.Body with { W = dim }), district.Name);

        var pillLabel = enabled
            ? Loc.T("os.realtor_district_open", count)
            : Loc.T("os.realtor_district_none");
        var pillColor = enabled ? t.AccentLight : UiColors.Hint with { W = 0.6f };
        var pillSz = ImGui.CalcTextSize(pillLabel);
        var pillH = pillSz.Y + Px(8f);
        var pillTl = new Vector2(tl.X + cardW - pillSz.X - Px(28f), tl.Y + (cardH - pillH) * 0.5f);
        dl.AddRectFilled(pillTl, pillTl + new Vector2(pillSz.X + Px(16f), pillH),
            ImGui.GetColorU32(pillColor with { W = 0.14f * dim }), pillH * 0.5f);
        dl.AddText(pillTl + new Vector2(Px(8f), Px(4f)), ImGui.GetColorU32(pillColor), pillLabel);

        ImGui.Dummy(new Vector2(0f, Px(6f)));
        if (clicked)
        {
            _openDistrict(_worldId, _worldName, district);
        }
    }

    private void DrawRetry(float winW)
    {
        ImGui.Dummy(new Vector2(0f, Px(10f)));
        var btnW = Px(150f);
        ImGui.SetCursorPosX((winW - btnW) * 0.5f);
        if (ModalUi.Button(Loc.T("os.realtor_retry"), btnW))
        {
            StartFetch();
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
