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
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin;

namespace AetherOS.Apps.Realtor;

/// <summary>The Realtor home: the selected world's open-plot count plus one row per residential
/// district. Data comes from the cached PaissaDB service; a tap on a district opens its plot list.</summary>
internal sealed class HomeScreen
{
    private const float PadX = 16f;

    private static readonly Dictionary<uint, string> _zoneNames = [];

    private readonly RealtorDataService _data;
    private readonly RealtorFilters _filters;
    private readonly LotteryClock _clock;
    private readonly RealtorSettings _settings;
    private readonly IEstateWatch _estates;
    private readonly Action _openSettings = null!;
    private PaissaWorldDetail? _observed;
    private const string MenuPopupId = "##realtorMenuPopup";
    private Vector2 _menuTL;
    private bool _showContribute;
    private readonly Action _openWorldPick;
    private readonly Action<int, string, PaissaDistrict> _openDistrict;
    private readonly Action _openTour;
    private readonly Action _openRealty;
    private readonly EntranceAnimation _entrance = new();

    private string _worldName = "";
    private int _worldId;
    private volatile PaissaWorldDetail? _detail;
    private volatile bool _loading;
    private volatile bool _failed;
    private int _generation;

    public HomeScreen(RealtorDataService data, RealtorFilters filters, LotteryClock clock, RealtorSettings settings,
        IEstateWatch estates, Action openWorldPick,
        Action<int, string, PaissaDistrict> openDistrict, Action openTour, Action openSettings,
        Action openRealty)
    {
        _data = data;
        _filters = filters;
        _clock = clock;
        _settings = settings;
        _estates = estates;
        _openWorldPick = openWorldPick;
        _openDistrict = openDistrict;
        _openTour = openTour;
        _openSettings = openSettings;
        _openRealty = openRealty;
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

    /// <summary>The lottery phase right now. Global, so it holds on every world regardless of how stale that
    /// world's scouting is; null only before the very first sighting this install has ever seen.</summary>
    public int? LotteryPhase => _clock.Current?.Phase;

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

        // The title and world pill stay put; everything below them scrolls.
        RealtorUi.ScrollBody("##realtorHomeBody", () =>
        {
            var bodyW = ImGui.GetWindowSize().X;
            if (_worldName.Length == 0)
            {
                DrawCenteredHint(Loc.T("os.realtor_unknown_world"), bodyW);
                return;
            }

            var detail = _detail;
            if (detail is null)
            {
                if (_loading)
                {
                    ImGui.Dummy(new Vector2(0f, Px(40f)));
                    var center = new Vector2(ImGui.GetWindowPos().X + bodyW * 0.5f, ImGui.GetCursorScreenPos().Y + Px(20f));
                    LoadingSpinner.Draw(center, Px(14f), Px(3f), ImGui.GetColorU32(t.Accent));
                    ImGui.Dummy(new Vector2(0f, Px(50f)));
                    DrawCenteredHint(Loc.T("os.realtor_loading"), bodyW);
                }
                else if (_failed)
                {
                    DrawCenteredHint(Loc.T("os.realtor_offline"), bodyW);
                    DrawRetry(bodyW);
                }
                // Your own house is read from the game, not from PaissaDB, so it is still worth showing
                // while the open-plot data is missing.
                ImGui.Dummy(new Vector2(0f, Px(14f)));
                DrawHomeRow(bodyW);
                return;
            }

            if (!ReferenceEquals(_observed, detail))
            {
                _observed = detail;
                _clock.Observe(detail);
            }

            DrawPhaseCard(bodyW);

            // Nothing is claimable during results, so the headline count goes entirely. Otherwise it counts
            // only plots the current cycle confirms; anything older is reported beside it, not folded in.
            if (_clock.Current?.Phase != PaissaLottoPhase.Results)
            {
                var (verified, stale) = LotteryClock.CountPlots(detail, _clock.Current?.Phase);
                stale = _settings.ShowStale ? stale : 0;
                DrawHero(detail, bodyW, verified, stale);
            }
            ImGui.Dummy(new Vector2(0f, Px(10f)));
            ImGui.SetCursorPosX(Px(PadX));
            RealtorUi.DrawFilterRow("home", _filters);
            ImGui.Dummy(new Vector2(0f, Px(8f)));
            foreach (var district in detail.Districts)
            {
                DrawDistrictRow(district, bodyW);
            }
            DrawHomeRow(bodyW);

            ImGui.Dummy(new Vector2(0f, Px(14f)));
        });

        DrawMenuDropdown();
        DrawContributeOverlay(ctx);
        _entrance.EndFrame();
    }

    private void DrawMenuDropdown()
    {
        var size = Px(30f);
        ImGui.SetNextWindowPos(new Vector2(_menuTL.X + size, _menuTL.Y + size + Px(4f)),
            ImGuiCond.Always, new Vector2(1f, 0f));
        ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(0.13f, 0.12f, 0.15f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Border, ThemeService.Current.Accent with { W = 0.5f });
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, Px(10f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(Px(4f), Px(4f)));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0f, Px(2f)));
        if (ImGui.BeginPopup(MenuPopupId))
        {
            var contribute = Loc.T("os.realtor_menu_contribute");
            // Widest label wins: sized off one row, a longer translation of any other row clips.
            var w = Px(150f);
            foreach (var label in new[]
            {
                contribute, Loc.T("os.realtor_menu_realty"), Loc.T("os.realtor_menu_settings"),
                Loc.T("os.realtor_menu_tour"),
            })
            {
                w = MathF.Max(w, ImGui.CalcTextSize(label).X + Px(56f));
            }
            var rowH = ImGui.GetTextLineHeight() + Px(12f);
            if (AppHeader.MenuRow(FontAwesomeIcon.HandsHelping, contribute, w, rowH))
            {
                _showContribute = true;
                ImGui.CloseCurrentPopup();
            }
            if (AppHeader.MenuRow(FontAwesomeIcon.Key, Loc.T("os.realtor_menu_realty"), w, rowH))
            {
                ImGui.CloseCurrentPopup();
                _openRealty();
            }
            if (AppHeader.MenuRow(FontAwesomeIcon.Cog, Loc.T("os.realtor_menu_settings"), w, rowH))
            {
                ImGui.CloseCurrentPopup();
                _openSettings();
            }
            if (AppHeader.MenuRow(FontAwesomeIcon.Compass, Loc.T("os.realtor_menu_tour"), w, rowH))
            {
                ImGui.CloseCurrentPopup();
                _openTour();
            }
            ImGui.EndPopup();
        }
        ImGui.PopStyleVar(3);
        ImGui.PopStyleColor(2);
    }

    /// <summary>Explains where the plot data comes from and how to help it stay fresh. In-phone overlay: the
    /// scrim covers only the app's own content rect, never the whole game screen.</summary>
    private void DrawContributeOverlay(OsAppContext ctx)
    {
        if (!_showContribute)
        {
            return;
        }
        var origin = ImGui.GetWindowPos();
        var avail = ImGui.GetWindowSize();

        // The overlay gets its own child, submitted after the scrolling body. ImGui renders every child
        // window above the parent's draw list, so a scrim drawn straight onto the parent would sit UNDER
        // the content it is supposed to cover.
        ImGui.SetCursorScreenPos(origin);
        using var layer = ImRaii.Child("##realtorOverlay", avail, false,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoBackground);
        if (!layer)
        {
            return;
        }

        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(origin, origin + avail, OsDrawShared.Black(0.72f));

        var panelW = avail.X - Px(44f);
        var panelTL = origin + new Vector2((avail.X - panelW) * 0.5f, avail.Y * 0.20f);
        var panelH = avail.Y * 0.52f;
        dl.AddRectFilled(panelTL, panelTL + new Vector2(panelW, panelH),
            ImGui.ColorConvertFloat4ToU32(new Vector4(0.10f, 0.09f, 0.12f, 1f)), Px(16f));
        dl.AddRect(panelTL, panelTL + new Vector2(panelW, panelH),
            ImGui.GetColorU32(ThemeService.Current.Accent with { W = 0.45f }),
            Px(16f), ImDrawFlags.RoundCornersAll, Px(1.2f));

        var innerW = panelW - Px(32f);
        ImGui.SetCursorScreenPos(panelTL + Px(16f, 14f));
        ImGui.BeginGroup();
        ModalUi.Header(innerW, FontAwesomeIcon.HandsHelping, Loc.T("os.realtor_contribute_title"),
            ThemeService.Current.AccentLight);
        ImGui.Dummy(new Vector2(0f, Px(6f)));
        ImGui.SetCursorPosX(ImGui.GetCursorPosX());
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + innerW);
        ImGui.TextColored(UiColors.Body, Loc.T("os.realtor_contribute_body"));
        ImGui.PopTextWrapPos();
        ImGui.Dummy(new Vector2(0f, Px(12f)));
        if (ModalUi.Button(Loc.T("os.realtor_contribute_open"), innerW))
        {
            OpenPluginInstaller();
        }
        if (ModalUi.Button(Loc.T("os.realtor_contribute_close"), innerW))
        {
            _showContribute = false;
        }
        ImGui.EndGroup();

        // Scrim last so the panel's own controls win their clicks, per the first-submitted-wins rule.
        ImGui.SetCursorScreenPos(origin);
        if (ImGui.InvisibleButton("##realtorContributeScrim", avail))
        {
            var m = ImGui.GetMousePos();
            if (m.X < panelTL.X || m.X > panelTL.X + panelW || m.Y < panelTL.Y || m.Y > panelTL.Y + panelH)
            {
                _showContribute = false;
            }
        }
    }

    private static void OpenPluginInstaller()
    {
        try
        {
            UiHost.PluginInterface.OpenPluginInstallerTo(PluginInstallerOpenKind.AllPlugins, "PaissaHouse");
        }
        catch (Exception ex)
        {
            UiHost.Log.Debug($"[Realtor] Could not open the plugin installer: {ex.Message}");
        }
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

        // Hamburger sits outermost, the way every other app's header does it; the world pill moves inboard.
        ImGui.SameLine();
        var baseY = ImGui.GetCursorPosY() + Px(2f);
        ImGui.SetCursorPosX(winW - pillH - Px(PadX));
        ImGui.SetCursorPosY(baseY);
        _menuTL = ImGui.GetCursorScreenPos();
        if (ImGui.InvisibleButton("##realtorMenuBtn", new Vector2(pillH, pillH)))
        {
            ImGui.OpenPopup(MenuPopupId);
        }
        HandOnHover();
        var menuActive = ImGui.IsItemHovered() || ImGui.IsPopupOpen(MenuPopupId);
        dlTop.AddRectFilled(_menuTL, _menuTL + new Vector2(pillH, pillH),
            ImGui.GetColorU32(menuActive ? t.Accent with { W = 0.30f } : new Vector4(1f, 1f, 1f, 0.06f)), Px(8f));
        var barsSz = IconDraw.Measure(FontAwesomeIcon.Bars, iconPx);
        IconDraw.Add(dlTop, FontAwesomeIcon.Bars, iconPx,
            _menuTL + new Vector2((pillH - barsSz.X) * 0.5f, (pillH - barsSz.Y) * 0.5f),
            ImGui.GetColorU32(t.AccentLight));

        ImGui.SameLine();
        ImGui.SetCursorPosX(winW - pillH - Px(PadX) - Px(8f) - pillW);
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

    /// <summary>The world's lottery phase and its live countdown, above the open-plot hero.</summary>
    private void DrawPhaseCard(float winW)
    {
        if (_clock.Current is not { } lottery)
        {
            return;
        }
        var cardW = winW - Px(PadX) * 2f;
        var lineH = ImGui.GetTextLineHeight();
        var rowGap = Px(6f);
        var cardH = lineH * 2f + rowGap + Px(22f);
        ImGui.SetCursorPosX(Px(PadX));
        var tl = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(tl, tl + new Vector2(cardW, cardH), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.05f)), Px(14f));

        var (phaseText, phaseColor) = RealtorUi.PhaseLabel(lottery.Phase);
        var remaining = RealtorUi.FormatSpan(lottery.Until - DateTimeOffset.UtcNow);

        DrawPhaseRow(dl, tl, cardW, tl.Y + Px(11f), Loc.T("os.realtor_phase_label"), phaseText, phaseColor);
        DrawPhaseRow(dl, tl, cardW, tl.Y + Px(11f) + lineH + rowGap, Loc.T("os.realtor_time_label"), remaining,
            UiColors.Body);

        ImGui.Dummy(new Vector2(0f, cardH + Px(8f)));
    }

    private static void DrawPhaseRow(ImDrawListPtr dl, Vector2 tl, float cardW, float y, string label, string value,
        Vector4 valueColor)
    {
        dl.AddText(new Vector2(tl.X + Px(14f), y), ImGui.GetColorU32(UiColors.Hint), label);
        var valueSz = ImGui.CalcTextSize(value);
        dl.AddText(new Vector2(tl.X + cardW - valueSz.X - Px(14f), y), ImGui.GetColorU32(valueColor), value);
    }

    private void DrawHero(PaissaWorldDetail detail, float winW, int openCount, int staleCount)
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

        float bigH;
        float bigW;
        var big = openCount.ToString();
        using (UiFonts.H1?.Push())
        {
            dl.AddText(new Vector2(tl.X + Px(16f), tl.Y + Px(12f)), ImGui.GetColorU32(t.AccentLight), big);
            bigH = ImGui.GetTextLineHeight();
            bigW = ImGui.CalcTextSize(big).X;
        }

        // Plots whose data predates this cycle are counted separately rather than inflating the headline.
        if (staleCount > 0)
        {
            var pill = Loc.T("os.realtor_stale_pill", staleCount);
            var pillSz = ImGui.CalcTextSize(pill);
            var pillH = pillSz.Y + Px(6f);
            var pillTl = new Vector2(tl.X + Px(16f) + bigW + Px(10f),
                tl.Y + Px(12f) + ((bigH - pillH) * 0.5f));
            dl.AddRectFilled(pillTl, pillTl + new Vector2(pillSz.X + Px(14f), pillH),
                ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.10f)), pillH * 0.5f);
            dl.AddText(pillTl + new Vector2(Px(7f), Px(3f)), ImGui.GetColorU32(UiColors.Hint), pill);
        }
        dl.AddText(new Vector2(tl.X + Px(16f), tl.Y + Px(12f) + bigH + Px(2f)),
            ImGui.GetColorU32(UiColors.Body), Loc.T("os.realtor_hero_sub", detail.Name));

        if (openCount > 0 && detail.OldestPlotTime > 0)
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
        // Split the row's own count the same way the headline and the plot list do: confirmed plots are the
        // number, anything predating this cycle is reported separately rather than padding it out.
        var globalPhase = _clock.Current?.Phase;
        var count = 0;
        var staleCount = 0;
        foreach (var plot in district.OpenPlots ?? [])
        {
            if (!_filters.Matches(plot))
            {
                continue;
            }
            if (LotteryClock.IsStale(plot, globalPhase))
            {
                staleCount++;
            }
            else
            {
                count++;
            }
        }
        if (!_settings.ShowStale)
        {
            staleCount = 0;
        }
        // Still worth opening when everything is stale: the list is where you see what is actually known.
        var enabled = count + staleCount > 0;
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

        var pillLabel = count > 0
            ? Loc.T("os.realtor_district_open", count)
            : Loc.T("os.realtor_district_none");
        var pillColor = count > 0 ? t.AccentLight : UiColors.Hint with { W = 0.6f };
        var pillSz = ImGui.CalcTextSize(pillLabel);
        var pillH = pillSz.Y + Px(8f);
        var pillY = tl.Y + (cardH - pillH) * 0.5f;

        // Stale sits outermost, after the confirmed count, so the number you can trust reads first.
        var staleLabel = staleCount > 0 ? Loc.T("os.realtor_stale_pill", staleCount) : string.Empty;
        var staleW = staleCount > 0 ? ImGui.CalcTextSize(staleLabel).X + Px(14f) : 0f;
        var staleTl = new Vector2(tl.X + cardW - staleW - Px(14f), pillY);
        var pillRight = staleCount > 0 ? staleTl.X - Px(6f) : tl.X + cardW - Px(14f);
        var pillTl = new Vector2(pillRight - pillSz.X - Px(16f), pillY);

        var textX = circleTl.X + circle + Px(11f);
        var nameLimit = pillTl.X - textX - Px(8f);
        dl.AddText(new Vector2(textX, tl.Y + (cardH - ImGui.GetTextLineHeight()) * 0.5f),
            ImGui.GetColorU32(UiColors.Body with { W = dim }), TruncateToWidth(district.Name, nameLimit));

        if (staleCount > 0)
        {
            dl.AddRectFilled(staleTl, staleTl + new Vector2(staleW, pillH),
                ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.08f * dim)), pillH * 0.5f);
            dl.AddText(staleTl + new Vector2(Px(7f), Px(4f)),
                ImGui.GetColorU32(UiColors.Hint with { W = dim }), staleLabel);
        }

        dl.AddRectFilled(pillTl, pillTl + new Vector2(pillSz.X + Px(16f), pillH),
            ImGui.GetColorU32(pillColor with { W = 0.14f * dim }), pillH * 0.5f);
        dl.AddText(pillTl + new Vector2(Px(8f), Px(4f)), ImGui.GetColorU32(pillColor), pillLabel);

        ImGui.Dummy(new Vector2(0f, Px(6f)));
        if (clicked)
        {
            _openDistrict(_worldId, _worldName, district);
        }
    }

    /// <summary>The logged-in character's own estate, sat below the districts in the same card. It is read
    /// from the game rather than from PaissaDB, so it ignores the filters above it and survives the data
    /// being unavailable.</summary>
    private void DrawHomeRow(float winW)
    {
        if (_estates.Current is not { TerritoryTypeId: not 0 } home)
        {
            return;
        }

        var t = ThemeService.Current;
        var color = RealtorUi.DistrictColor((int)home.TerritoryTypeId);
        var cardW = winW - (Px(PadX) * 2f);
        var cardH = Px(58f);

        ImGui.SetCursorPosX(Px(PadX));
        var tl = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new Vector2(cardW, cardH));
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(tl, tl + new Vector2(cardW, cardH),
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.06f)), Px(14f));

        var circle = Px(36f);
        var circleTl = new Vector2(tl.X + Px(11f), tl.Y + (cardH - circle) * 0.5f);
        dl.AddCircleFilled(circleTl + new Vector2(circle * 0.5f, circle * 0.5f), circle * 0.5f,
            ImGui.GetColorU32(color with { W = 0.22f }));
        var iconPx = circle * 0.52f;
        var iconSz = IconDraw.Measure(FontAwesomeIcon.Home, iconPx);
        IconDraw.Add(dl, FontAwesomeIcon.Home, iconPx,
            circleTl + new Vector2((circle - iconSz.X) * 0.5f, (circle - iconSz.Y) * 0.5f),
            ImGui.GetColorU32(color));

        // The button is submitted before the label so it keeps its own clicks; the row itself is not a
        // target, since there is nothing else to open.
        var right = tl.X + cardW - Px(14f);
        if (_estates.CanTeleportHome)
        {
            var label = Loc.T("os.realtor_home_teleport");
            var labelSz = ImGui.CalcTextSize(label);
            var btnW = labelSz.X + Px(24f);
            var btnH = labelSz.Y + Px(10f);
            var btnTl = new Vector2(right - btnW, tl.Y + (cardH - btnH) * 0.5f);
            ImGui.SetCursorScreenPos(btnTl);
            var clicked = ImGui.InvisibleButton("##realtorHomeTeleport", new Vector2(btnW, btnH));
            HandOnHover();
            var hovered = ImGui.IsItemHovered();
            dl.AddRectFilled(btnTl, btnTl + new Vector2(btnW, btnH),
                ImGui.GetColorU32(t.Accent with { W = hovered ? 0.75f : 0.55f }), btnH * 0.5f);
            dl.AddText(btnTl + new Vector2(Px(12f), Px(5f)), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 1f)),
                label);
            right = btnTl.X - Px(8f);
            if (clicked)
            {
                _estates.TeleportHome();
            }
        }

        var textX = circleTl.X + circle + Px(11f);
        var name = Loc.T("os.realtor_home_row", ZoneName(home.TerritoryTypeId));
        dl.AddText(new Vector2(textX, tl.Y + (cardH - ImGui.GetTextLineHeight()) * 0.5f),
            ImGui.GetColorU32(UiColors.Body), TruncateToWidth(name, right - textX - Px(8f)));

        ImGui.Dummy(new Vector2(0f, Px(6f)));
    }

    /// <summary>The residential zone's own name in the player's client language, so it matches the district
    /// rows above without depending on PaissaDB having answered.</summary>
    private static string ZoneName(uint territoryTypeId)
    {
        if (_zoneNames.TryGetValue(territoryTypeId, out var cached))
        {
            return cached;
        }
        var name = string.Empty;
        try
        {
            name = UiHost.DataManager.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>()
                .GetRowOrDefault(territoryTypeId)?.PlaceName.ValueNullable?.Name.ExtractText() ?? string.Empty;
        }
        catch (Exception ex)
        {
            UiHost.Log.Debug($"[Realtor] Zone name lookup failed: {ex.Message}");
        }
        _zoneNames[territoryTypeId] = name;
        return name;
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
