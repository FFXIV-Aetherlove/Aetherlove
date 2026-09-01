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
using AetherLove.Shared.Levemetes;
using AetherLove.Shared.Profile.Enums;
using AetherLove.Shared.Store;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;
using Widgets = AetherLove.Widgets;

namespace AetherOS.Apps.Levemetes;

public partial class LevemetesScreen
{
    private enum Section { Browse, Detail, Tour }

    private Section _section = Section.Browse;

    private readonly ILevemetesHost _host;
    private readonly Action _openMyAds;
    private IOsShell? _shell;
    private IShareService? _share;
    private readonly CancellationTokenSource _cts = new();

    private volatile LevemetesBrowseDto? _browse;
    private volatile bool _browseLoading;
    private volatile bool _browseRefetchQueued;
    private volatile string? _browseError;
    private DateTimeOffset _browseFetchedAtUtc;
    private readonly EntranceAnimation _entrance = new();

    private readonly Dictionary<Guid, ISharedImmediateTexture?> _coverTex = new();

    private bool _showFilters;
    private double _filtersOpenedAt;
    private bool _searchActive;
    private string _searchText = "";
    private bool _focusSearch;
    private readonly bool[] _filterCategories = new bool[KnownCategories.Length];
    private readonly bool[] _filterRegions = new bool[RegionValues.Length];
    private int _filterKind;
    private bool _filterNsfw;
    private float _filterPanelHeight;

    private const float PadX = 16f;

    internal static readonly short[] KnownCategories =
        [.. Enum.GetValues<LevemeteCategory>().Select(c => (short)c)];

    private static string LevemetesCacheDir =>
        Path.Combine(UiHost.PluginInterface.ConfigDirectory.FullName, "LevemetesCache");

    public LevemetesScreen(ILevemetesHost host, Action openMyAds)
    {
        _host = host;
        _openMyAds = openMyAds;
    }

    public void OnShow()
    {
        LoadFilterState();
        EnsureTourSeen();
        _entrance.Arm();
        if (_browse is null || DateTimeOffset.UtcNow - _browseFetchedAtUtc > TimeSpan.FromMinutes(2))
        {
            StartBrowseFetch();
        }
    }

    // Category and kind labels live in the central Chat tables ("chat.leve_*") because the Love and
    // Messenger chat cards render them too, and app packs only merge at app registration.
    internal static string CategoryLabel(short category) =>
        Array.IndexOf(KnownCategories, category) >= 0
            ? Loc.T($"chat.leve_cat_{category}")
            : Loc.T("chat.leve_cat_unknown");

    internal static string KindLabel(short kind) =>
        Loc.T(kind == (short)LevemeteKind.Offering ? "chat.leve_kind_offering" : "chat.leve_kind_looking");

    private static FontAwesomeIcon CategoryIcon(short category) => (LevemeteCategory)category switch
    {
        LevemeteCategory.HouseDecoration => FontAwesomeIcon.Home,
        LevemeteCategory.Gposing => FontAwesomeIcon.Camera,
        LevemeteCategory.Commissions => FontAwesomeIcon.PaintBrush,
        LevemeteCategory.Dj => FontAwesomeIcon.Music,
        LevemeteCategory.VenueStaff => FontAwesomeIcon.Users,
        LevemeteCategory.BardsAndBands => FontAwesomeIcon.Guitar,
        LevemeteCategory.Mercenary => FontAwesomeIcon.Crosshairs,
        LevemeteCategory.CraftingGathering => FontAwesomeIcon.Hammer,
        LevemeteCategory.Adult => FontAwesomeIcon.Heart,
        _ => FontAwesomeIcon.Scroll,
    };

    private void LoadFilterState()
    {
        var state = UiHost.Configuration.Levemetes;
        for (var i = 0; i < KnownCategories.Length; i++)
        {
            _filterCategories[i] = (state.CategoryMask & (1 << KnownCategories[i])) != 0;
        }
        MaskToBools(RegionValues, (Region)state.RegionMask, (v, m) => (m & v) != 0, _filterRegions);
        _filterKind = state.Kind;
        _filterNsfw = state.IncludeNsfw;
    }

    private void SaveFilterState()
    {
        var state = UiHost.Configuration.Levemetes;
        var mask = 0;
        for (var i = 0; i < KnownCategories.Length; i++)
        {
            if (_filterCategories[i])
            {
                mask |= 1 << KnownCategories[i];
            }
        }
        state.CategoryMask = mask;
        state.RegionMask = (short)MaskOr(RegionValues, _filterRegions, (a, b) => a | b);
        state.Kind = (short)_filterKind;
        state.IncludeNsfw = _filterNsfw;
        UiHost.Configuration.Save();
    }

    private LevemetesFilterDto BuildFilter()
    {
        var state = UiHost.Configuration.Levemetes;
        var categories = KnownCategories.Where(c => (state.CategoryMask & (1 << c)) != 0).ToArray();
        return new LevemetesFilterDto(categories, state.RegionMask, state.Kind, state.IncludeNsfw);
    }

    private bool FiltersAreActive
    {
        get
        {
            var state = UiHost.Configuration.Levemetes;
            return state.CategoryMask != 0 || state.RegionMask != 0 || state.Kind != 0 || state.IncludeNsfw;
        }
    }

    private void ResetFilters()
    {
        Array.Clear(_filterCategories);
        Array.Clear(_filterRegions);
        _filterKind = 0;
        _filterNsfw = false;
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
                var dto = await _host.GetBrowseAsync(filter, ct).ConfigureAwait(false);
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
                UiHost.Log.Warning(ex, "[LevemetesScreen] Browse fetch failed.");
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

    private void CacheBrowseTextures(LevemetesBrowseDto dto)
    {
        foreach (var ad in dto.Ads.Concat(dto.Featured ?? []))
        {
            _coverTex[ad.Id] = ad.CoverWebp is { Length: > 0 }
                ? AvatarDiskCache.Store(LevemetesCacheDir, $"cover_{ad.Id:N}", ad.CoverWebp)
                : null;
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
            ImGui.TextColored(t.AccentLight, Loc.T("os.leve_title"));
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
            DrawCenteredMuted(Loc.T("os.leve_load_failed", _browseError));
            return;
        }
        var browse = _browse;
        if (browse is null)
        {
            return;
        }

        _entrance.BeginFrame();
        PushScrollbarStyle();
        using (var scroll = ImRaii.Child("##leveScroll", ImGui.GetContentRegionAvail(), false))
        {
            if (scroll.Success)
            {
                DrawBrowseContent(browse);
            }
        }
        PopScrollbarStyle();
        _entrance.EndFrame();

        DrawFiltersOverlay();
        DrawMenuDropdown(menuTL);
    }

    private const string MenuPopupId = "##leveMenuPopup";

    private Vector2 DrawMenuButton(float winW)
    {
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var iconFont = UiHost.PluginInterface.UiBuilder.FontIcon;
        var size = Px(30f);
        var winPos = ImGui.GetWindowPos();
        var tl = new Vector2(winPos.X + winW - Px(PadX) - size, ImGui.GetCursorScreenPos().Y - ImGui.GetTextLineHeight() - Px(6f));

        ImGui.SetCursorScreenPos(tl);
        if (ImGui.InvisibleButton("##leveMenuBtn", new Vector2(size, size)))
        {
            ImGui.OpenPopup(MenuPopupId);
        }
        var active = ImGui.IsItemHovered() || ImGui.IsPopupOpen(MenuPopupId);
        if (ImGui.IsItemHovered())
        {
            SharedUiHelpers.HandOnHover();
        }
        dl.AddRectFilled(tl, tl + new Vector2(size, size),
            ImGui.GetColorU32(active ? t.Accent with { W = 0.30f } : new Vector4(1f, 1f, 1f, 0.06f)), Px(8f));

        ImGui.PushFont(iconFont);
        var glyph = FontAwesomeIcon.Bars.ToIconString();
        var glyphSz = ImGui.CalcTextSize(glyph);
        dl.AddText(tl + (new Vector2(size, size) - glyphSz) * 0.5f, ImGui.GetColorU32(t.AccentLight), glyph);
        ImGui.PopFont();

        if (FiltersAreActive)
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
            var myAdsLabel = Loc.T("os.leve_menu_my");
            var w = MathF.Max(Px(150f), ImGui.CalcTextSize(myAdsLabel).X + Px(56f));
            var rowH = ImGui.GetTextLineHeight() + Px(12f);
            if (AppHeader.MenuRow(FontAwesomeIcon.Filter, Loc.T("os.leve_menu_filter"), w, rowH))
            {
                _showFilters = true;
                _filtersOpenedAt = ImGui.GetTime();
                LoadFilterState();
                ImGui.CloseCurrentPopup();
            }
            if (AppHeader.MenuRow(FontAwesomeIcon.Search, Loc.T("os.leve_menu_search"), w, rowH))
            {
                _searchActive = true;
                _focusSearch = true;
                ImGui.CloseCurrentPopup();
            }
            if (AppHeader.MenuRow(FontAwesomeIcon.Scroll, myAdsLabel, w, rowH))
            {
                ImGui.CloseCurrentPopup();
                _openMyAds();
            }
            if (AppHeader.MenuRow(FontAwesomeIcon.Compass, Loc.T("os.leve_menu_tour"), w, rowH))
            {
                OpenTour();
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
        ImGui.InputTextWithHint("##leveSearch", Loc.T("os.leve_search"), ref _searchText, 64);
        ImGui.SameLine(0f, Px(6f));
        if (ImGui.Button("×##leveSearchClose", new Vector2(closeW, ImGui.GetFrameHeight())))
        {
            _searchActive = false;
            _searchText = "";
        }
        ImGui.Spacing();
    }

    private bool MatchesSearch(LevemeteSummaryDto ad)
    {
        var q = _searchText.Trim();
        if (!_searchActive || q.Length == 0)
        {
            return true;
        }
        return ad.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
            || CategoryLabel(ad.Category).Contains(q, StringComparison.OrdinalIgnoreCase)
            || KindLabel(ad.Kind).Contains(q, StringComparison.OrdinalIgnoreCase)
            || (ad.Price?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private void DrawBrowseContent(LevemetesBrowseDto browse)
    {
        var listW = ImGui.GetContentRegionAvail().X;
        var ads = browse.Ads.Where(MatchesSearch).ToList();
        var featured = (browse.Featured ?? []).Where(MatchesSearch).ToList();

        if (browse.Ads.Length == 0 && (browse.Featured?.Length ?? 0) == 0)
        {
            ImGui.Dummy(new Vector2(1f, Px(40f)));
            DrawEmptyState();
            return;
        }
        if (ads.Count == 0 && featured.Count == 0)
        {
            ImGui.Dummy(new Vector2(1f, Px(30f)));
            DrawCenteredMuted(Loc.T("os.leve_no_search_results"));
            return;
        }

        ImGui.Dummy(new Vector2(1f, Px(2f)));
        if (featured.Count > 0)
        {
            DrawSectionPill(Loc.T("os.boost_featured"),
                BoostFx.KeyColor((BoostStyle)featured[0].BoostStyle), FontAwesomeIcon.Bolt);
            foreach (var ad in featured)
            {
                DrawAdCard(ad, listW);
            }
            ImGui.Spacing();
        }
        foreach (var ad in ads)
        {
            DrawAdCard(ad, listW);
        }
        ImGui.Dummy(new Vector2(1f, Px(10f)));
    }

    private void DrawEmptyState()
    {
        var winW = ImGui.GetContentRegionAvail().X;
        var dl = ImGui.GetWindowDrawList();
        var center = ImGui.GetCursorScreenPos() + new Vector2(winW * 0.5f, Px(30f));

        IconDraw.AddCentered(dl, FontAwesomeIcon.Scroll, Px(42f), center,
            ImGui.GetColorU32(UiColors.Muted with { W = 0.6f }));

        ImGui.Dummy(new Vector2(1f, Px(64f)));
        DrawCenteredMuted(Loc.T("os.leve_empty"));

        if (FiltersAreActive)
        {
            ImGui.Spacing();
            ImGui.Spacing();
            var btnW = Px(170f);
            ImGui.SetCursorPosX(MathF.Max(Px(PadX), (winW - btnW) * 0.5f));
            PushThemeButton(ThemeService.Current);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
            if (SharedUiHelpers.Button(Loc.T("os.leve_reset_filters"), new Vector2(btnW, Px(32f))))
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

    internal static string RegionShortList(int regionMask)
    {
        var parts = new List<string>(4);
        var mask = (Region)regionMask;
        if ((mask & Region.NorthAmerica) != 0)
        {
            parts.Add("NA");
        }
        if ((mask & Region.Europe) != 0)
        {
            parts.Add("EU");
        }
        if ((mask & Region.Japan) != 0)
        {
            parts.Add("JPN");
        }
        if ((mask & Region.Oceania) != 0)
        {
            parts.Add("OCE");
        }
        return string.Join(" · ", parts);
    }

    /// <summary>Fallback tile for an ad without a cover: the category icon on a themed panel.</summary>
    internal static void DrawCategoryTile(ImDrawListPtr dl, Vector2 tl, Vector2 size, short category, float rounding)
    {
        var t = ThemeService.Current;
        var br = tl + size;
        dl.AddRectFilled(tl, br, ImGui.GetColorU32(t.Accent with { W = 0.13f }), rounding);
        var center = (tl + br) * 0.5f;
        var chipR = MathF.Min(size.X, size.Y) * 0.32f;
        dl.AddCircleFilled(center, chipR, ImGui.GetColorU32(t.Accent with { W = 0.22f }));
        IconDraw.AddCentered(dl, CategoryIcon(category), chipR, center, ImGui.GetColorU32(t.AccentLight));
        dl.AddRect(tl, br, ImGui.GetColorU32(t.Accent with { W = 0.55f }), rounding, ImDrawFlags.None, Px(1f));
    }

    private void DrawAdCard(LevemeteSummaryDto ad, float listW)
    {
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var pad = Px(PadX);
        var rowW = listW - pad * 2f;
        var rowH = Px(76f);
        var origin = ImGui.GetCursorScreenPos();
        var tl = new Vector2(origin.X + pad, origin.Y);
        var br = tl + new Vector2(rowW, rowH);

        ImGui.SetCursorScreenPos(tl);
        var clicked = ImGui.InvisibleButton($"##leve_{ad.Id:N}", new Vector2(rowW, rowH));
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            SharedUiHelpers.HandOnHover();
        }

        dl.AddRectFilled(tl, br, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, hovered ? 0.07f : 0.045f)), Px(10f));

        var thumbW = Px(80f);
        var thumbH = Px(60f);
        var thumbTL = new Vector2(tl.X + Px(8f), tl.Y + (rowH - thumbH) * 0.5f);
        var wrap = _coverTex.TryGetValue(ad.Id, out var tex) ? tex?.GetWrapOrDefault() : null;
        if (wrap != null)
        {
            var (uv0, uv1) = SharedUiHelpers.CoverFitUvs(wrap.Width, wrap.Height, thumbW, thumbH);
            dl.AddImageRounded(wrap.Handle, thumbTL, thumbTL + new Vector2(thumbW, thumbH),
                uv0, uv1, 0xFFFFFFFFu, Px(8f), ImDrawFlags.RoundCornersAll);
        }
        else
        {
            DrawCategoryTile(dl, thumbTL, new Vector2(thumbW, thumbH), ad.Category, Px(8f));
        }

        var lineH = ImGui.GetTextLineHeight();
        var textX = thumbTL.X + thumbW + Px(10f);
        var textMaxW = br.X - textX - Px(10f);
        var y = tl.Y + Px(8f);

        var kindLabel = KindLabel(ad.Kind);
        var kindCol = ad.Kind == (short)LevemeteKind.Offering ? UiColors.Success : t.AccentLight;
        var kindSz = ImGui.CalcTextSize(kindLabel);
        var iconW = lineH * 0.9f;
        IconDraw.AddCentered(dl, CategoryIcon(ad.Category), lineH * 0.8f,
            new Vector2(textX + iconW * 0.5f, y + lineH * 0.5f), ImGui.GetColorU32(t.AccentLight));
        var kindX = textX + iconW + Px(6f);
        dl.AddText(new Vector2(kindX, y), ImGui.GetColorU32(kindCol), kindLabel);
        dl.AddText(new Vector2(kindX + kindSz.X + Px(6f), y), 0xFFFFFFFFu,
            TruncateToWidth(ad.Title, textMaxW - iconW - kindSz.X - Px(12f)));

        y += lineH + Px(3f);
        var line2 = CategoryLabel(ad.Category);
        var regions = RegionShortList(ad.RegionMask);
        if (regions.Length > 0)
        {
            line2 = $"{line2} · {regions}";
        }
        dl.AddText(new Vector2(textX, y), ImGui.GetColorU32(UiColors.Body), TruncateToWidth(line2, textMaxW));

        y += lineH + Px(3f);
        if (ad.ReviewCount > 0)
        {
            var starEnd = VenueFields.DrawStarSummary(dl, new Vector2(textX, y),
                ad.AverageRating, ad.ReviewCount, Px(9f));
            if (ad.Price is { Length: > 0 } pricedLine)
            {
                dl.AddText(new Vector2(textX + starEnd + Px(8f), y), ImGui.GetColorU32(UiColors.Subtle),
                    TruncateToWidth(pricedLine, textMaxW - starEnd - Px(8f)));
            }
        }
        else if (ad.Price is { Length: > 0 } price)
        {
            dl.AddText(new Vector2(textX, y), ImGui.GetColorU32(UiColors.Subtle),
                TruncateToWidth(price, textMaxW));
        }

        if (BoostRules.IsActive(ad.BoostedUntilUtc, DateTimeOffset.UtcNow))
        {
            BoostFx.Draw(dl, tl, br, Px(10f), (BoostStyle)ad.BoostStyle);
        }

        if (clicked)
        {
            OpenDetail(ad);
        }
        ImGui.SetCursorScreenPos(new Vector2(origin.X, br.Y + Px(6f)));
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
        if (ImGui.InvisibleButton("##leveFilterScrim", windowSize))
        {
            ApplyFilters();
        }

        var w = Px(310f);
        var pad = Px(16f, 16f);
        var h = _filterPanelHeight > 0f ? _filterPanelHeight : Px(400f);
        h = MathF.Min(h, windowSize.Y - Px(30f));
        var panelPos = windowPos + (windowSize - new Vector2(w, h)) * 0.5f;
        panelPos.Y += Px(16f) * (1f - ease);

        ImGui.SetCursorScreenPos(panelPos);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.11f, 0.10f, 0.13f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.32f, 0.30f, 0.38f, 0.65f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, Px(12f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, pad);
        using (var child = ImRaii.Child("##leveFilterPanel", new Vector2(w, h), true,
                   ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.AlwaysUseWindowPadding))
        {
            if (child.Success)
            {
                var innerW = ImGui.GetContentRegionAvail().X;
                ImGui.PushTextWrapPos(innerW);
                Widgets.ModalUi.Header(innerW, FontAwesomeIcon.Filter,
                    Loc.T("os.leve_filters_title"), ThemeService.Current.AccentLight);

                ImGui.TextColored(UiColors.Subtle, Loc.T("os.leve_filter_kind"));
                ImGui.Spacing();
                DrawKindPills(innerW);

                ImGui.Spacing();
                ImGui.TextColored(UiColors.Subtle, Loc.T("os.leve_filter_categories"));
                ImGui.Spacing();
                var catLabels = KnownCategories.Select(CategoryLabel).ToArray();
                VenueFields.DrawPillToggleRow("levecat", catLabels, _filterCategories, innerW,
                    dangerAt: i => KnownCategories[i] == (short)LevemeteCategory.Adult,
                    skipAt: i => KnownCategories[i] == (short)LevemeteCategory.Adult && !_filterNsfw);

                ImGui.Spacing();
                ImGui.TextColored(UiColors.Subtle, Loc.T("os.leve_filter_regions"));
                ImGui.Spacing();
                VenueFields.DrawPillToggleRow("levereg", Regions, _filterRegions, innerW);

                ImGui.Spacing();
                if (DrawToggleSwitch("##leveNsfw", Loc.T("os.leve_filter_nsfw"), _filterNsfw))
                {
                    _filterNsfw = !_filterNsfw;
                    if (!_filterNsfw)
                    {
                        // Turning 18+ off also drops the now-hidden Adult category so it can't strand the results.
                        var adultIdx = Array.IndexOf(KnownCategories, (short)LevemeteCategory.Adult);
                        if (adultIdx >= 0)
                        {
                            _filterCategories[adultIdx] = false;
                        }
                    }
                }
                ImGui.Spacing();
                ImGui.Spacing();

                if (Widgets.ModalUi.Button($"{Loc.T("os.leve_filters_apply")}##leveFilterApply", innerW))
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

    private void DrawKindPills(float innerW)
    {
        var t = ThemeService.Current;
        string[] labels =
        [
            Loc.T("os.leve_kind_both"),
            Loc.T("chat.leve_kind_looking"),
            Loc.T("chat.leve_kind_offering"),
        ];
        short[] values = [0, (short)LevemeteKind.LookingFor, (short)LevemeteKind.Offering];
        for (var i = 0; i < labels.Length; i++)
        {
            if (i > 0)
            {
                ImGui.SameLine(0f, Px(6f));
            }
            var selected = _filterKind == values[i];
            if (selected)
            {
                PushThemeButton(t);
            }
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(1f, 1f, 1f, 0.07f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(1f, 1f, 1f, 0.13f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(1f, 1f, 1f, 0.18f));
            }
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(14f));
            if (SharedUiHelpers.Button($"{labels[i]}##levekind{i}",
                    new Vector2(ImGui.CalcTextSize(labels[i]).X + Px(20f), Px(26f))))
            {
                _filterKind = values[i];
            }
            ImGui.PopStyleVar();
            ImGui.PopStyleColor(3);
        }
    }

    private void ApplyFilters()
    {
        _showFilters = false;
        SaveFilterState();
        StartBrowseFetch();
    }
}
