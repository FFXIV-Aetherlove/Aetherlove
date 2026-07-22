using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AetherLove;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Shared.Places;
using AetherLove.Shared.Profile.Enums;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;
using Widgets = AetherLove.Widgets;

namespace AetherOS.Apps.Places;

public partial class PlacesScreen
{
    private enum Section { Browse, Detail, AddVenue, Tour }

    private Section _section = Section.Browse;

    private readonly IPlacesHost _host;
    private readonly AetherLove.Os.ISocialBridge _social;
    private readonly Action _openMyVenues;
    private IOsShell? _shell;
    private IShareService? _share;
    private readonly CancellationTokenSource _cts = new();

    private volatile PlacesBrowseDto? _browse;
    private volatile bool _browseLoading;
    private volatile bool _browseRefetchQueued;
    private volatile string? _browseError;
    private DateTimeOffset _browseFetchedAtUtc;
    private readonly EntranceAnimation _entrance = new();

    private readonly Dictionary<Guid, ISharedImmediateTexture?> _logoTex = new();
    private readonly Dictionary<Guid, ISharedImmediateTexture?> _liveBannerTex = new();
    private readonly Dictionary<string, ISharedImmediateTexture?> _clumpTex = new();

    private bool _showFilters;
    private double _filtersOpenedAt;
    private bool _searchActive;
    private string _searchText = "";
    private bool _focusSearch;
    private bool _hideIntroOpen;
    private float _hideIntroPanelH;
    private bool _hiddenListOpen;
    private float _hiddenListPanelH;
    private readonly bool[] _filterTags = new bool[VenueFields.VenueTagValues.Length];
    private readonly bool[] _filterRegions = new bool[RegionValues.Length];
    private bool _filterNsfw;
    private bool _filterAlwaysOpen = true;
    private float _filterPanelHeight;

    private const float PadX = 16f;

    private bool ProfileNsfwDefault => _host.NsfwEnabled;

    private static string PlacesCacheDir =>
        Path.Combine(UiHost.PluginInterface.ConfigDirectory.FullName, "PlacesCache");

    public PlacesScreen(IPlacesHost host, AetherLove.Os.ISocialBridge social, Action openMyVenues)
    {
        _host = host;
        _social = social;
        _openMyVenues = openMyVenues;
    }

    public void OnShow()
    {
        EnsureFiltersInitialized();
        LoadFilterState();
        EnsureTourSeen();
        _entrance.Arm();
        if (_browse is null || DateTimeOffset.UtcNow - _browseFetchedAtUtc > TimeSpan.FromMinutes(2))
        {
            StartBrowseFetch();
        }
    }

    /// <summary>Seeds the filter defaults once the player's region is readable; retries on a later open while zoning.</summary>
    private void EnsureFiltersInitialized()
    {
        var state = UiHost.Configuration.Places;
        if (state.FilterDefaultsSeeded)
        {
            // Backfill for accounts seeded before RegionMaskDefault existed, so the seeded region stops
            // lighting the "filters active" dot.
            if (state.RegionMaskDefault == 0 && state.RegionMask != 0)
            {
                state.RegionMaskDefault = state.RegionMask;
                UiHost.Configuration.Save();
            }
            return;
        }
        var region = VenueLocationDetector.DetectRegion();
        if (region is null)
        {
            return;
        }
        state.TagMask = 0;
        state.RegionMask = (short)DefaultRegionMask(region.Value);
        state.RegionMaskDefault = state.RegionMask;
        state.IncludeNsfw = ProfileNsfwDefault;
        state.FilterDefaultsSeeded = true;
        UiHost.Configuration.Save();
    }

    private static Region DefaultRegionMask(Region current) => current switch
    {
        Region.NorthAmerica => Region.NorthAmerica | Region.Oceania,
        Region.Europe => Region.Europe | Region.Oceania,
        Region.Japan => Region.Japan,
        Region.Oceania => Region.Oceania,
        _ => (Region)0,
    };

    private void LoadFilterState()
    {
        var state = UiHost.Configuration.Places;
        MaskToBools(VenueFields.VenueTagValues, (VenueTag)state.TagMask,
            (v, m) => (m & v) != 0, _filterTags);
        MaskToBools(RegionValues, (Region)state.RegionMask,
            (v, m) => (m & v) != 0, _filterRegions);
        _filterNsfw = state.IncludeNsfw;
        _filterAlwaysOpen = !state.HideAlwaysOpen;
    }

    private void SaveFilterState()
    {
        var state = UiHost.Configuration.Places;
        state.TagMask = (int)MaskOr(VenueFields.VenueTagValues, _filterTags, (a, b) => a | b);
        state.RegionMask = (short)MaskOr(RegionValues, _filterRegions, (a, b) => a | b);
        state.IncludeNsfw = _filterNsfw;
        state.HideAlwaysOpen = !_filterAlwaysOpen;
        UiHost.Configuration.Save();
    }

    private PlacesFilterDto BuildFilter()
    {
        var state = UiHost.Configuration.Places;
        return new PlacesFilterDto((VenueTag)state.TagMask, (Region)state.RegionMask, state.IncludeNsfw,
            state.HideAlwaysOpen);
    }

    private bool FiltersAreActive
    {
        get
        {
            var state = UiHost.Configuration.Places;
            return state.TagMask != 0 || state.RegionMask != state.RegionMaskDefault
                || _filterNsfw != ProfileNsfwDefault || !_filterAlwaysOpen;
        }
    }

    private void ResetFilters()
    {
        Array.Clear(_filterTags);
        Array.Clear(_filterRegions);
        _filterNsfw = ProfileNsfwDefault;
        _filterAlwaysOpen = true;
        SaveFilterState();
        StartBrowseFetch();
    }

    private void StartBrowseFetch()
    {
        if (_browseLoading)
        {
            _browseRefetchQueued = true;
            return;
        }
        _browseLoading = true;
        _browseError = null;
        var ct = _cts.Token;
        var filter = BuildFilter();
        _ = Task.Run(async () =>
        {
            try
            {
                var dto = await _host.GetPlacesBrowseAsync(filter, ct).ConfigureAwait(false);
                if (ct.IsCancellationRequested)
                {
                    return;
                }
                CacheBrowseTextures(dto);
                _browse = dto;
                _browseFetchedAtUtc = DateTimeOffset.UtcNow;
                _entrance.Arm();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, "[PlacesScreen] Browse fetch failed.");
                _browseError = HubErrorText.Localize(ex);
            }
            finally
            {
                _browseLoading = false;
                if (_browseRefetchQueued && !ct.IsCancellationRequested)
                {
                    _browseRefetchQueued = false;
                    StartBrowseFetch();
                }
            }
        }, ct);
    }

    private void CacheBrowseTextures(PlacesBrowseDto dto)
    {
        foreach (var venue in dto.Venues)
        {
            _logoTex[venue.Id] = venue.LogoWebp is { Length: > 0 }
                ? AvatarDiskCache.Store(PlacesCacheDir, $"logo_{venue.Id:N}", venue.LogoWebp)
                : null;
        }
        foreach (var occ in dto.HappeningNow)
        {
            CacheClumpTextures(occ);
            _liveBannerTex[occ.VenueId] = occ.BannerWebp is { Length: > 0 }
                ? AvatarDiskCache.Store(PlacesCacheDir, $"banner_{occ.VenueId:N}", occ.BannerWebp)
                : null;
        }
    }

    private void CacheClumpTextures(VenueOccurrenceDto occ)
    {
        if (occ.RsvpAvatars is null)
        {
            return;
        }
        for (var i = 0; i < occ.RsvpAvatars.Length; i++)
        {
            var key = $"{occ.VenueId:N}_{occ.StartUtc.UtcTicks}_{i}";
            if (!_clumpTex.ContainsKey(key))
            {
                _clumpTex[key] = AvatarDiskCache.Store(PlacesCacheDir, key, occ.RsvpAvatars[i]);
            }
        }
    }

    public void Draw(IOsShell shell, IShareService? share)
    {
        _shell = shell;
        _share = share;
        switch (_section)
        {
            case Section.Browse:
                DrawBrowse();
                break;
            case Section.Detail:
                DrawDetail();
                break;
            case Section.AddVenue:
                DrawAddVenue();
                break;
            case Section.Tour:
                DrawTour();
                break;
        }
    }

    private void DrawBrowse()
    {
        var t = ThemeService.Current;
        var winW = ImGui.GetContentRegionAvail().X;

        ImGui.Spacing();
        ImGui.SetCursorPosX(Px(PadX));
        using (UiFonts.H3?.Push())
        {
            ImGui.TextColored(t.AccentLight, Loc.T("places.title"));
        }

        var menuTL = DrawMenuButton(winW);
        ImGui.Spacing();
        if (_searchActive)
        {
            DrawSearchBar(winW);
        }

        if (_browseLoading && _browse is null)
        {
            Widgets.LoadingIndicator.Draw();
            return;
        }
        if (_browseError is not null && _browse is null)
        {
            DrawCenteredMuted(Loc.T("places.load_failed", _browseError));
            return;
        }
        var browse = _browse;
        if (browse is null)
        {
            return;
        }

        _entrance.BeginFrame();

        PushScrollbarStyle();
        using (var scroll = ImRaii.Child("##placesScroll", ImGui.GetContentRegionAvail(), false))
        {
            if (scroll.Success)
            {
                DrawBrowseContent(browse);
            }
        }
        PopScrollbarStyle();
        _entrance.EndFrame();

        DrawFiltersOverlay();
        DrawHideIntroOverlay();
        DrawHiddenVenuesOverlay();
        DrawMenuDropdown(menuTL);
    }

    private const string MenuPopupId = "##placesMenuPopup";

    /// <summary>Returns the button's top-left screen position so the popup can anchor under it.</summary>
    private Vector2 DrawMenuButton(float winW)
    {
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var iconFont = UiHost.PluginInterface.UiBuilder.FontIcon;
        var size = Px(30f);
        var winPos = ImGui.GetWindowPos();
        var tl = new Vector2(winPos.X + winW - Px(PadX) - size, ImGui.GetCursorScreenPos().Y - ImGui.GetTextLineHeight() - Px(6f));

        ImGui.SetCursorScreenPos(tl);
        if (ImGui.InvisibleButton("##placesMenuBtn", new Vector2(size, size)))
        {
            ImGui.OpenPopup(MenuPopupId);
        }
        var active = ImGui.IsItemHovered() || ImGui.IsPopupOpen(MenuPopupId);
        dl.AddRectFilled(tl, tl + new Vector2(size, size),
            ImGui.GetColorU32(active ? t.Accent with { W = 0.30f } : new Vector4(1f, 1f, 1f, 0.06f)), Px(8f));

        ImGui.PushFont(iconFont);
        var glyph = FontAwesomeIcon.Bars.ToIconString();
        var glyphSz = ImGui.CalcTextSize(glyph);
        dl.AddText(tl + (new Vector2(size, size) - glyphSz) * 0.5f, ImGui.GetColorU32(t.AccentLight), glyph);
        ImGui.PopFont();

        var state = UiHost.Configuration.Places;
        if (state.TagMask != 0 || state.RegionMask != state.RegionMaskDefault)
        {
            dl.AddCircleFilled(tl + new Vector2(size - Px(4f), Px(4f)), Px(3.5f), t.AccentU32);
        }
        return tl;
    }

    private void DrawMenuDropdown(Vector2 menuTL)
    {
        var size = Px(30f);
        ImGui.SetNextWindowPos(new Vector2(menuTL.X + size, menuTL.Y + size + Px(4f)),
            ImGuiCond.Always, new Vector2(1f, 0f));
        ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(0.13f, 0.12f, 0.15f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Border, ThemeService.Current.Accent with { W = 0.5f });
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, Px(10f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(Px(4f), Px(4f)));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0f, Px(2f)));
        if (ImGui.BeginPopup(MenuPopupId))
        {
            var manageVenueLabel = Loc.T("places.menu_manage_venue");
            var tourLabel = Loc.T("places.menu_tour");
            var w = MathF.Max(Px(150f), MathF.Max(ImGui.CalcTextSize(manageVenueLabel).X,
                ImGui.CalcTextSize(tourLabel).X) + Px(56f));
            var rowH = ImGui.GetTextLineHeight() + Px(12f);
            if (AppHeader.MenuRow(FontAwesomeIcon.Filter, Loc.T("places.menu_filter"), w, rowH))
            {
                _showFilters = true;
                _filtersOpenedAt = ImGui.GetTime();
                ImGui.CloseCurrentPopup();
            }
            if (AppHeader.MenuRow(FontAwesomeIcon.Search, Loc.T("places.menu_search"), w, rowH))
            {
                _searchActive = true;
                _focusSearch = true;
                ImGui.CloseCurrentPopup();
            }
            if (AppHeader.MenuRow(FontAwesomeIcon.Store, manageVenueLabel, w, rowH))
            {
                ImGui.CloseCurrentPopup();
                if (_host.IsVenueOwner)
                {
                    _openMyVenues();
                }
                else
                {
                    _section = Section.AddVenue;
                }
            }
            if (AppHeader.MenuRow(FontAwesomeIcon.Compass, tourLabel, w, rowH))
            {
                OpenTour();
                ImGui.CloseCurrentPopup();
            }
            var hiddenCount = UiHost.Configuration.Places.HiddenVenues.Count;
            if (hiddenCount > 0
                && AppHeader.MenuRow(FontAwesomeIcon.EyeSlash, Loc.T("places.menu_hidden", hiddenCount), w, rowH))
            {
                _hiddenListOpen = true;
                _hiddenListPanelH = 0f;
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
        ImGui.PopStyleVar(3);
        ImGui.PopStyleColor(2);
    }

    private void DrawSearchBar(float winW)
    {
        ImGui.SetCursorPosX(Px(PadX));
        var closeW = Px(30f);
        var inputW = winW - Px(PadX) * 2f - closeW - Px(6f);
        if (_focusSearch)
        {
            ImGui.SetKeyboardFocusHere();
            _focusSearch = false;
        }
        ImGui.SetNextItemWidth(inputW);
        ImGui.InputTextWithHint("##placesSearch", Loc.T("places.search_hint"), ref _searchText, 64);
        ImGui.SameLine(0f, Px(6f));
        if (ImGui.Button("×##placesSearchClose", new Vector2(closeW, ImGui.GetFrameHeight())))
        {
            _searchActive = false;
            _searchText = "";
        }
        ImGui.Spacing();
    }

    private bool MatchesSearch(VenueSummaryDto v)
    {
        var q = _searchText.Trim();
        if (!_searchActive || q.Length == 0)
        {
            return true;
        }
        return v.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
            || v.World.Contains(q, StringComparison.OrdinalIgnoreCase)
            || v.DataCenter.Contains(q, StringComparison.OrdinalIgnoreCase)
            || RegionLabel(v.Region).Contains(q, StringComparison.OrdinalIgnoreCase)
            || VenueFields.DistrictLabel(v.District).Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private void DrawBrowseContent(PlacesBrowseDto browse)
    {
        var venuesById = browse.Venues.ToDictionary(v => v.Id);
        var listW = ImGui.GetContentRegionAvail().X;

        if (browse.Venues.Length == 0)
        {
            ImGui.Dummy(new Vector2(1f, Px(40f)));
            DrawEmptyState();
            return;
        }

        var hidden = UiHost.Configuration.Places.HiddenVenues;
        var live = browse.HappeningNow
            .Where(o => !hidden.ContainsKey(o.VenueId)
                && venuesById.TryGetValue(o.VenueId, out var v) && MatchesSearch(v))
            .ToList();
        var upcoming = browse.Upcoming
            .Where(o => !hidden.ContainsKey(o.VenueId)
                && venuesById.TryGetValue(o.VenueId, out var v) && MatchesSearch(v))
            .OrderBy(o => o.StartUtc)
            .ToList();

        if (live.Count == 0 && upcoming.Count == 0)
        {
            var searching = _searchActive && _searchText.Trim().Length > 0;
            ImGui.Dummy(new Vector2(1f, Px(30f)));
            DrawCenteredMuted(Loc.T(searching ? "places.no_search_results" : "places.nothing_upcoming"));
            ImGui.Dummy(new Vector2(1f, Px(10f)));
            return;
        }

        if (live.Count > 0)
        {
            DrawLiveSectionHeader();
            foreach (var occ in live)
            {
                DrawLiveCard(venuesById[occ.VenueId], occ, listW);
                ImGui.Dummy(new Vector2(1f, Px(8f)));
            }
            ImGui.Spacing();
        }

        var rsvpd = upcoming.Where(o => o.RsvpedByMe).ToList();
        if (rsvpd.Count > 0)
        {
            ImGui.Spacing();
            DrawSectionPill(Loc.T("places.section_rsvpd"), UiColors.Success, FontAwesomeIcon.CalendarCheck);
            foreach (var occ in rsvpd)
            {
                DrawRsvpdRow(venuesById[occ.VenueId], occ, listW);
            }
        }

        ImGui.Spacing();
        DrawSectionPill(Loc.T("places.this_week"), ThemeService.Current.Accent, FontAwesomeIcon.CalendarAlt);
        var rest = upcoming.Where(o => !o.RsvpedByMe).ToList();
        if (rest.Count == 0)
        {
            DrawCenteredMuted(Loc.T("places.nothing_upcoming"));
            ImGui.Spacing();
        }
        else
        {
            DateOnly? lastDay = null;
            foreach (var occ in rest)
            {
                var localDay = DateOnly.FromDateTime(occ.StartUtc.ToLocalTime().Date);
                if (localDay != lastDay)
                {
                    lastDay = localDay;
                    ImGui.Spacing();
                    ImGui.SetCursorPosX(Px(PadX));
                    ImGui.TextColored(UiColors.Subtle, DayHeader(localDay));
                    ImGui.Spacing();
                }
                DrawUpcomingRow(venuesById[occ.VenueId], occ, listW);
            }
        }

        ImGui.Dummy(new Vector2(1f, Px(10f)));
    }

    private static void DrawLiveSectionHeader()
    {
        var dl = ImGui.GetWindowDrawList();
        var live = Loc.T("places.live");
        var title = Loc.T("places.happening_now");
        var liveSz = ImGui.CalcTextSize(live);
        var titleSz = ImGui.CalcTextSize(title);
        var dotR = Px(4f);
        var innerPad = Px(11f);
        var gap = Px(7f);
        var h = MathF.Max(liveSz.Y, titleSz.Y) + Px(9f);
        var w = innerPad + dotR * 2f + gap + liveSz.X + gap + titleSz.X + innerPad;

        ImGui.SetCursorPosX(Px(PadX));
        var tl = ImGui.GetCursorScreenPos();
        var br = tl + new Vector2(w, h);
        dl.AddRectFilled(tl, br, ImGui.GetColorU32(new Vector4(0.85f, 0.20f, 0.24f, 0.16f)), h * 0.5f);
        dl.AddRect(tl, br, ImGui.GetColorU32(new Vector4(1f, 0.32f, 0.34f, 0.55f)), h * 0.5f, ImDrawFlags.None, Px(1f));

        var pulse = AccessibilityService.ReduceMotion ? 1f : 0.5f + 0.5f * MathF.Sin((float)ImGui.GetTime() * 4f);
        var dotC = new Vector2(tl.X + innerPad + dotR, tl.Y + h * 0.5f);
        dl.AddCircleFilled(dotC, dotR, ImGui.GetColorU32(new Vector4(1f, 0.28f, 0.30f, pulse)));

        var textY = tl.Y + (h - liveSz.Y) * 0.5f;
        var liveX = dotC.X + dotR + gap;
        dl.AddText(new Vector2(liveX, textY), ImGui.GetColorU32(new Vector4(1f, 0.45f, 0.47f, 1f)), live);
        dl.AddText(new Vector2(liveX + liveSz.X + gap, textY), 0xFFFFFFFFu, title);

        ImGui.Dummy(new Vector2(w, h));
        ImGui.Spacing();
    }

    private void DrawEmptyState()
    {
        var winW = ImGui.GetContentRegionAvail().X;
        var dl = ImGui.GetWindowDrawList();
        var center = ImGui.GetCursorScreenPos() + new Vector2(winW * 0.5f, Px(30f));

        IconDraw.AddCentered(dl, FontAwesomeIcon.Compass, Px(42f), center,
            ImGui.GetColorU32(UiColors.Muted with { W = 0.6f }));

        ImGui.Dummy(new Vector2(1f, Px(64f)));
        DrawCenteredMuted(Loc.T("places.empty"));
        ImGui.Spacing();
        DrawCenteredMuted(Loc.T("places.empty_hint"));

        if (FiltersAreActive)
        {
            ImGui.Spacing();
            ImGui.Spacing();
            var btnW = Px(170f);
            ImGui.SetCursorPosX(MathF.Max(Px(PadX), (winW - btnW) * 0.5f));
            PushThemeButton(ThemeService.Current);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
            if (ImGui.Button(Loc.T("places.reset_filters"), new Vector2(btnW, Px(32f))))
            {
                ResetFilters();
            }
            ImGui.PopStyleVar();
            PopThemeButton();
        }
    }

    private static void DrawCenteredMuted(string text)
    {
        var winW = ImGui.GetContentRegionAvail().X;
        var wrapped = ImGui.CalcTextSize(text, false, winW - Px(PadX) * 2f);
        ImGui.SetCursorPosX(MathF.Max(Px(PadX), (winW - wrapped.X) * 0.5f));
        ImGui.PushTextWrapPos(winW - Px(PadX));
        ImGui.TextColored(UiColors.Muted, text);
        ImGui.PopTextWrapPos();
    }

    private string DayHeader(DateOnly localDay)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (localDay == today)
        {
            return Loc.T("places.today");
        }
        if (localDay == today.AddDays(1))
        {
            return Loc.T("places.tomorrow");
        }
        var dayIdx = ((int)localDay.DayOfWeek + 6) % 7;
        return $"{VenueFields.DayAbbreviations[dayIdx]} {localDay.Day:D2}.{localDay.Month:D2}";
    }

    private void DrawLiveCard(VenueSummaryDto venue, VenueOccurrenceDto occ, float listW)
    {
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var pad = Px(PadX);
        var cardW = listW - pad * 2f;
        var cardH = Px(150f);
        var origin = ImGui.GetCursorScreenPos();
        var tl = new Vector2(origin.X + pad, origin.Y);
        var br = tl + new Vector2(cardW, cardH);

        ImGui.SetCursorScreenPos(tl);
        var clicked = ImGui.InvisibleButton($"##live_{venue.Id:N}_{occ.StartUtc.UtcTicks}", new Vector2(cardW, cardH));
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            SharedUiHelpers.HandOnHover();
        }
        DrawVenueContextMenu(venue, $"##livectx_{venue.Id:N}_{occ.StartUtc.UtcTicks}");

        var wrap = _liveBannerTex.TryGetValue(venue.Id, out var tex) ? tex?.GetWrapOrDefault() : null;
        var isLogoBackdrop = false;
        if (wrap is null && _logoTex.TryGetValue(venue.Id, out var logoTex))
        {
            wrap = logoTex?.GetWrapOrDefault();
            isLogoBackdrop = wrap != null;
        }
        if (wrap != null)
        {
            var (uv0, uv1) = SharedUiHelpers.CoverFitUvs(wrap.Width, wrap.Height, cardW, cardH);
            dl.AddImageRounded(wrap.Handle, tl, br, uv0, uv1, 0xFFFFFFFFu, Px(14f), ImDrawFlags.RoundCornersAll);
            if (isLogoBackdrop)
            {
                dl.AddRectFilled(tl, br, 0x59000000u, Px(14f));
            }
        }
        else
        {
            dl.AddRectFilled(tl, br, ImGui.GetColorU32(t.Accent with { W = 0.14f }), Px(14f));
        }

        dl.AddRectFilledMultiColor(tl, new Vector2(br.X, tl.Y + Px(46f)),
            0x88000000u, 0x88000000u, 0x00000000u, 0x00000000u);
        dl.AddRectFilledMultiColor(new Vector2(tl.X, br.Y - Px(84f)), br,
            0x00000000u, 0x00000000u, 0xD8000000u, 0xD8000000u);
        dl.AddRect(tl, br, ImGui.GetColorU32(t.Accent with { W = hovered ? 0.90f : 0.55f }), Px(14f),
            ImDrawFlags.None, Px(1.5f));

        VenueFields.DrawStarSummary(dl, tl + new Vector2(Px(12f), Px(11f)),
            venue.AverageRating, venue.ReviewCount, Px(12f));

        var now = DateTimeOffset.UtcNow;
        var remaining = occ.EndUtc - now;
        if (remaining < TimeSpan.Zero)
        {
            remaining = TimeSpan.Zero;
        }
        DrawEndsInChip(dl, new Vector2(br.X - Px(12f), tl.Y + Px(10f)),
            Loc.T("places.ends_in", FormatRemaining(remaining)));

        var textX = tl.X + Px(14f);
        var textMaxW = cardW - Px(28f);
        float nameH;
        using (UiFonts.H3?.Push())
        {
            nameH = ImGui.GetFontSize();
            dl.AddText(ImGui.GetFont(), nameH, new Vector2(textX, br.Y - Px(58f)),
                0xFFFFFFFFu, TruncateToWidth(venue.Name, textMaxW));
        }
        var line2Y = br.Y - Px(58f) + nameH + Px(3f);
        dl.AddText(new Vector2(textX, line2Y), ImGui.GetColorU32(UiColors.Body),
            TruncateToWidth(RegionDcWorldLine(venue), textMaxW - Px(12f)));

        DrawLiveProgressBar(dl, occ, now, tl, br, t);

        if (clicked)
        {
            OpenDetail(venue);
        }
        ImGui.SetCursorScreenPos(new Vector2(origin.X, br.Y));
    }

    private static string RegionDcWorldLine(VenueSummaryDto venue) => VenueFields.LocationLine(venue);

    private static string LocationSummary(string region, string dataCenter, string world)
    {
        var parts = new List<string>(3);
        if (region.Length > 0)
        {
            parts.Add(region);
        }
        if (dataCenter.Length > 0)
        {
            parts.Add(dataCenter);
        }
        if (world.Length > 0)
        {
            parts.Add(world);
        }
        return string.Join(" · ", parts);
    }

    private static string RegionShort(Region region) => region switch
    {
        Region.NorthAmerica => "NA",
        Region.Europe => "EU",
        Region.Oceania => "OCE",
        Region.Japan => "JPN",
        _ => "",
    };

    private static string FormatRemaining(TimeSpan remaining)
    {
        var hours = (int)remaining.TotalHours;
        return hours > 0
            ? Loc.T("places.duration_hm", hours, remaining.Minutes)
            : Loc.T("places.duration_m", remaining.Minutes);
    }

    private static void DrawEndsInChip(ImDrawListPtr dl, Vector2 topRight, string label)
    {
        var textSz = ImGui.CalcTextSize(label);
        var padX = Px(8f);
        var h = textSz.Y + Px(6f);
        var w = textSz.X + padX * 2f;
        var tl = new Vector2(topRight.X - w, topRight.Y);
        dl.AddRectFilled(tl, tl + new Vector2(w, h), 0xC8181818u, h * 0.5f);
        dl.AddText(tl + new Vector2(padX, Px(3f)), 0xFFFFFFFFu, label);
    }

    private static void DrawLiveProgressBar(ImDrawListPtr dl, VenueOccurrenceDto occ, DateTimeOffset now,
        Vector2 cardTL, Vector2 cardBR, ThemeDefinition t)
    {
        var total = (occ.EndUtc - occ.StartUtc).TotalMinutes;
        if (total <= 0)
        {
            return;
        }
        var fraction = (float)Math.Clamp((now - occ.StartUtc).TotalMinutes / total, 0.0, 1.0);
        var barTL = new Vector2(cardTL.X + Px(12f), cardBR.Y - Px(9f));
        var barBR = new Vector2(cardBR.X - Px(12f), cardBR.Y - Px(6f));
        dl.AddRectFilled(barTL, barBR, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.08f)), Px(2f));
        var fillEnd = barTL.X + (barBR.X - barTL.X) * fraction;
        if (fillEnd > barTL.X + Px(2f))
        {
            dl.AddRectFilledMultiColor(barTL, new Vector2(fillEnd, barBR.Y),
                ImGui.GetColorU32(t.SecondaryStart), ImGui.GetColorU32(t.SecondaryEnd),
                ImGui.GetColorU32(t.SecondaryEnd), ImGui.GetColorU32(t.SecondaryStart));
        }
    }

    private void DrawRsvpClump(ImDrawListPtr dl, VenueOccurrenceDto occ, Vector2 bottomRight)
    {
        var r = Px(11f);
        var step = r * 1.3f;

        var count = occ.RsvpCount;
        var shown = Math.Min(3, (occ.RsvpAvatars ?? []).Length);

        var x = bottomRight.X - r;
        for (var i = shown - 1; i >= 0; i--)
        {
            var center = new Vector2(x - i * step, bottomRight.Y - r);
            var wrap = _clumpTex.TryGetValue($"{occ.VenueId:N}_{occ.StartUtc.UtcTicks}_{i}", out var tex)
                ? tex?.GetWrapOrDefault()
                : null;
            if (wrap != null)
            {
                dl.AddImageRounded(wrap.Handle, center - new Vector2(r, r), center + new Vector2(r, r),
                    Vector2.Zero, Vector2.One, 0xFFFFFFFFu, r, ImDrawFlags.RoundCornersAll);
            }
            else
            {
                dl.AddCircleFilled(center, r, UiColors.AvatarFallback);
            }
            dl.AddCircle(center, r, UiColors.AvatarRing, 32, Px(1f));
        }

        if (count > 0)
        {
            var label = Loc.T("places.going", count);
            var labelSz = ImGui.CalcTextSize(label);
            var labelX = x - MathF.Max(0, shown - 1) * step - r - Px(8f) - labelSz.X;
            dl.AddText(new Vector2(labelX, bottomRight.Y - r - labelSz.Y * 0.5f),
                ImGui.GetColorU32(UiColors.Subtle), label);
        }
    }

    private void DrawRsvpdRow(VenueSummaryDto venue, VenueOccurrenceDto occ, float listW)
    {
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var pad = Px(PadX);
        var rowW = listW - pad * 2f;
        var rowH = Px(62f);
        var origin = ImGui.GetCursorScreenPos();
        var tl = new Vector2(origin.X + pad, origin.Y);
        var br = tl + new Vector2(rowW, rowH);

        ImGui.SetCursorScreenPos(tl);
        var clicked = ImGui.InvisibleButton($"##rsvpd_{venue.Id:N}_{occ.StartUtc.UtcTicks}", new Vector2(rowW, rowH));
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            SharedUiHelpers.HandOnHover();
        }
        DrawVenueContextMenu(venue, $"##rsvpdctx_{venue.Id:N}_{occ.StartUtc.UtcTicks}");

        dl.AddRectFilled(tl, br, ImGui.GetColorU32(t.Accent with { W = hovered ? 0.15f : 0.09f }), Px(12f));
        dl.AddRect(tl, br, ImGui.GetColorU32(t.Accent with { W = 0.45f }), Px(12f), ImDrawFlags.None, Px(1.2f));

        var logoSize = Px(44f);
        var logoTL = new Vector2(tl.X + Px(10f), tl.Y + (rowH - logoSize) * 0.5f);
        DrawLogo(dl, venue.Id, logoTL, logoSize);

        var nameX = logoTL.X + logoSize + Px(12f);
        var nameMaxW = br.X - nameX - Px(110f);
        var line2Y = tl.Y + Px(11f) + ImGui.GetTextLineHeight() + Px(4f);
        dl.AddText(new Vector2(nameX, tl.Y + Px(11f)), 0xFFFFFFFFu, TruncateToWidth(venue.Name, nameMaxW));

        var localStart = occ.StartUtc.ToLocalTime();
        var localEnd = occ.EndUtc.ToLocalTime();
        var whenLabel = $"{DayHeader(DateOnly.FromDateTime(localStart.Date))} · {localStart:HH:mm}–{localEnd:HH:mm}";
        dl.AddText(new Vector2(nameX, line2Y), ImGui.GetColorU32(t.AccentLight),
            TruncateToWidth(whenLabel, nameMaxW));

        var goingLabel = Loc.T("places.rsvp_going");
        var goingSz = ImGui.CalcTextSize(goingLabel);
        dl.AddText(new Vector2(br.X - goingSz.X - Px(12f), tl.Y + Px(11f)),
            ImGui.GetColorU32(UiColors.Success), goingLabel);
        VenueFields.DrawStarSummary(dl, new Vector2(br.X - Px(12f), line2Y),
            venue.AverageRating, venue.ReviewCount, Px(9f), alignRight: true);

        if (clicked)
        {
            OpenDetail(venue);
        }
        ImGui.SetCursorScreenPos(new Vector2(origin.X, br.Y + Px(6f)));
    }

    private void DrawUpcomingRow(VenueSummaryDto venue, VenueOccurrenceDto occ, float listW)
    {
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var pad = Px(PadX);
        var rowW = listW - pad * 2f;
        var rowH = Px(68f);
        var origin = ImGui.GetCursorScreenPos();
        var tl = new Vector2(origin.X + pad, origin.Y);
        var br = tl + new Vector2(rowW, rowH);

        ImGui.SetCursorScreenPos(tl);
        var clicked = ImGui.InvisibleButton($"##up_{venue.Id:N}_{occ.StartUtc.UtcTicks}", new Vector2(rowW, rowH));
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            SharedUiHelpers.HandOnHover();
        }
        DrawVenueContextMenu(venue, $"##upctx_{venue.Id:N}_{occ.StartUtc.UtcTicks}");
        dl.AddRectFilled(tl, br, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, hovered ? 0.07f : 0.045f)), Px(10f));

        var logoSize = Px(52f);
        var logoTL = new Vector2(tl.X + Px(8f), tl.Y + (rowH - logoSize) * 0.5f);
        DrawLogo(dl, venue.Id, logoTL, logoSize);

        var lineH = ImGui.GetTextLineHeight();
        var nameX = logoTL.X + logoSize + Px(10f);
        var nameMaxW = br.X - nameX - Px(80f);
        var y = tl.Y + Px(8f);
        dl.AddText(new Vector2(nameX, y), 0xFFFFFFFFu, TruncateToWidth(venue.Name, nameMaxW));

        y += lineH + Px(3f);
        dl.AddText(new Vector2(nameX, y), ImGui.GetColorU32(UiColors.Body),
            TruncateToWidth(LocationSummary(RegionShort(venue.Region), venue.DataCenter, venue.World), nameMaxW));

        y += lineH + Px(3f);
        var localStart = occ.StartUtc.ToLocalTime();
        var localEnd = occ.EndUtc.ToLocalTime();
        dl.AddText(new Vector2(nameX, y), ImGui.GetColorU32(t.AccentLight),
            $"{localStart:HH:mm}–{localEnd:HH:mm}");

        VenueFields.DrawStarSummary(dl, new Vector2(br.X - Px(10f), tl.Y + Px(8f)),
            venue.AverageRating, venue.ReviewCount, Px(9f), alignRight: true);
        if (occ.RsvpCount > 0)
        {
            var label = Loc.T("places.going", occ.RsvpCount);
            var labelSz = ImGui.CalcTextSize(label);
            dl.AddText(new Vector2(br.X - labelSz.X - Px(10f), y),
                ImGui.GetColorU32(UiColors.Subtle), label);
        }

        if (clicked)
        {
            OpenDetail(venue);
        }
        ImGui.SetCursorScreenPos(new Vector2(origin.X, br.Y + Px(4f)));
    }

    private void DrawLogo(ImDrawListPtr dl, Guid venueId, Vector2 tl, float size)
    {
        var wrap = _logoTex.TryGetValue(venueId, out var tex) ? tex?.GetWrapOrDefault() : null;
        if (wrap != null)
        {
            dl.AddImageRounded(wrap.Handle, tl, tl + new Vector2(size, size),
                Vector2.Zero, Vector2.One, 0xFFFFFFFFu, size * 0.24f, ImDrawFlags.RoundCornersAll);
        }
        else
        {
            dl.AddRectFilled(tl, tl + new Vector2(size, size), UiColors.AvatarFallback, size * 0.24f);
            IconDraw.AddCentered(dl, FontAwesomeIcon.Store, size * 0.45f,
                tl + new Vector2(size, size) * 0.5f, ImGui.GetColorU32(UiColors.Muted));
        }
    }

    private void DrawFiltersOverlay()
    {
        if (!_showFilters)
        {
            return;
        }
        var windowPos = ImGui.GetWindowPos();
        var windowSize = ImGui.GetWindowSize();
        var dl = ImGui.GetWindowDrawList();

        var appear = AccessibilityService.ReduceMotion
            ? 1f
            : Math.Clamp((float)(ImGui.GetTime() - _filtersOpenedAt) / 0.16f, 0f, 1f);
        var ease = 1f - MathF.Pow(1f - appear, 3f);

        dl.AddRectFilled(windowPos, windowPos + windowSize,
            ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.55f * ease)));

        ImGui.SetCursorScreenPos(windowPos);
        if (ImGui.InvisibleButton("##placesFilterScrim", windowSize))
        {
            ApplyFilters();
        }

        var w = Px(310f);
        var pad = Px(16f, 16f);
        var h = _filterPanelHeight > 0f ? _filterPanelHeight : Px(380f);
        h = MathF.Min(h, windowSize.Y - Px(30f));
        var panelPos = windowPos + (windowSize - new Vector2(w, h)) * 0.5f;
        panelPos.Y += Px(16f) * (1f - ease);

        ImGui.SetCursorScreenPos(panelPos);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.11f, 0.10f, 0.13f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.32f, 0.30f, 0.38f, 0.65f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, Px(12f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, pad);
        using (var child = ImRaii.Child("##placesFilterPanel", new Vector2(w, h), true,
                   ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.AlwaysUseWindowPadding))
        {
            if (child.Success)
            {
                var innerW = ImGui.GetContentRegionAvail().X;
                ImGui.PushTextWrapPos(innerW);
                Widgets.ModalUi.Header(innerW, FontAwesomeIcon.Filter,
                    Loc.T("places.filters_title"), ThemeService.Current.AccentLight);

                ImGui.TextColored(UiColors.Subtle, Loc.T("places.filters_tags"));
                ImGui.Spacing();
                VenueFields.DrawPillToggleRow("ptag", VenueFields.VenueTagLabels, _filterTags, innerW,
                    dangerAt: i => VenueFields.VenueTagValues[i] == VenueTag.Nsfw,
                    skipAt: i => VenueFields.VenueTagValues[i] == VenueTag.AlwaysOpen
                        || (VenueFields.VenueTagValues[i] == VenueTag.Nsfw && !_filterNsfw));

                ImGui.Spacing();
                ImGui.TextColored(UiColors.Subtle, Loc.T("places.filters_regions"));
                ImGui.Spacing();
                VenueFields.DrawPillToggleRow("preg", Regions, _filterRegions, innerW);

                ImGui.Spacing();
                if (DrawToggleSwitch("##placesNsfw", Loc.T("places.filters_nsfw"), _filterNsfw))
                {
                    _filterNsfw = !_filterNsfw;
                    if (!_filterNsfw)
                    {
                        // Turning 18+ off also drops the now-hidden 18+ tag filter so it can't strand the results.
                        var nsfwIdx = Array.IndexOf(VenueFields.VenueTagValues, VenueTag.Nsfw);
                        if (nsfwIdx >= 0)
                        {
                            _filterTags[nsfwIdx] = false;
                        }
                    }
                }
                ImGui.Spacing();
                if (DrawToggleSwitch("##places247", Loc.T("places.filters_247"), _filterAlwaysOpen))
                {
                    _filterAlwaysOpen = !_filterAlwaysOpen;
                }
                ImGui.Spacing();
                ImGui.TextColored(UiColors.Hint, Loc.T("places.filters_hint"));
                ImGui.Spacing();
                ImGui.Spacing();

                if (Widgets.ModalUi.Button($"{Loc.T("places.filters_apply")}##placesFilterApply", innerW))
                {
                    ApplyFilters();
                }
                ImGui.PopTextWrapPos();
                _filterPanelHeight = ImGui.GetCursorPosY() + pad.Y;
            }
        }
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(2);
    }

    private void ApplyFilters()
    {
        _showFilters = false;
        SaveFilterState();
        StartBrowseFetch();
    }

    private void DrawVenueContextMenu(VenueSummaryDto venue, string ctxId)
    {
        if (!ImGui.BeginPopupContextItem(ctxId, ImGuiPopupFlags.MouseButtonRight))
        {
            return;
        }
        ImGui.TextDisabled(venue.Name);
        ImGui.Separator();
        if (DrawIconMenuItem(FontAwesomeIcon.EyeSlash, Loc.T("places.hide_venue")))
        {
            ImGui.CloseCurrentPopup();
            HideVenue(venue);
        }
        ImGui.EndPopup();
    }

    /// <summary>The first hide attempt only shows the explainer (nothing is hidden); later ones hide immediately.</summary>
    private void HideVenue(VenueSummaryDto venue)
    {
        var places = UiHost.Configuration.Places;
        if (!places.SeenHideVenueIntro)
        {
            places.SeenHideVenueIntro = true;
            UiHost.Configuration.Save();
            _hideIntroOpen = true;
            _hideIntroPanelH = 0f;
            return;
        }
        places.HiddenVenues[venue.Id] = venue.Name;
        UiHost.Configuration.Save();
    }

    private void DrawHideIntroOverlay()
    {
        if (!_hideIntroOpen)
        {
            return;
        }
        var dismissed = DrawPageOverlayPanel("hideVenueIntro", ImGui.GetWindowPos(), ImGui.GetWindowSize(),
            ref _hideIntroPanelH, Px(300f), w =>
        {
            Widgets.ModalUi.Header(w, FontAwesomeIcon.EyeSlash, Loc.T("places.hide_intro_title"),
                ThemeService.Current.Accent);
            ImGui.PushTextWrapPos(w);
            ImGui.TextColored(UiColors.Body, Loc.T("places.hide_intro_body"));
            ImGui.Spacing();
            ImGui.TextColored(UiColors.Hint, Loc.T("places.hide_intro_note"));
            ImGui.PopTextWrapPos();
            ImGui.Spacing();
            if (Widgets.ModalUi.Button($"{Loc.T("common.got_it")}##hideIntroOk", w))
            {
                _hideIntroOpen = false;
            }
        });
        if (dismissed)
        {
            _hideIntroOpen = false;
        }
    }

    private void DrawHiddenVenuesOverlay()
    {
        if (!_hiddenListOpen)
        {
            return;
        }
        var dismissed = DrawPageOverlayPanel("hiddenVenues", ImGui.GetWindowPos(), ImGui.GetWindowSize(),
            ref _hiddenListPanelH, Px(300f), w =>
        {
            Widgets.ModalUi.Header(w, FontAwesomeIcon.EyeSlash, Loc.T("places.hidden_title"),
                ThemeService.Current.Accent);
            var places = UiHost.Configuration.Places;
            var btnLabel = Loc.T("places.unhide");
            var btnW = ImGui.CalcTextSize(btnLabel).X + Px(20f);
            var left = ImGui.GetCursorPosX();
            Guid? unhide = null;
            PushThemeButton(ThemeService.Current);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
            foreach (var (id, name) in places.HiddenVenues.OrderBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase))
            {
                ImGui.AlignTextToFramePadding();
                ImGui.TextColored(UiColors.Body, TruncateToWidth(name, w - btnW - Px(12f)));
                ImGui.SameLine();
                ImGui.SetCursorPosX(left + w - btnW);
                if (ImGui.Button($"{btnLabel}##unhide_{id:N}", new Vector2(btnW, 0f)))
                {
                    unhide = id;
                }
            }
            ImGui.PopStyleVar();
            PopThemeButton();
            if (unhide is { } target)
            {
                places.HiddenVenues.Remove(target);
                UiHost.Configuration.Save();
                _hiddenListPanelH = 0f;
                if (places.HiddenVenues.Count == 0)
                {
                    _hiddenListOpen = false;
                }
            }
            ImGui.Spacing();
            if (Widgets.ModalUi.Button($"{Loc.T("common.close")}##hiddenClose", w))
            {
                _hiddenListOpen = false;
            }
        });
        if (dismissed)
        {
            _hiddenListOpen = false;
        }
    }
}
