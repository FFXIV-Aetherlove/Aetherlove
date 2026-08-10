using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove;
using AetherLove.Services;
using AetherLove.Services.Hangouts;
using AetherLove.Services.Localization;
using AetherLove.Shared.Hangouts;
using AetherLove.UI;
using AetherLove.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;
using static AetherLove.UI.ProfileFields;

namespace AetherOS.Apps.Hangouts;

/// <summary>The Hangouts tab: friends' hangouts on top, then everyone else's happening-now and later listings.</summary>
public sealed class HangoutsScreen
{
    private readonly IHangoutsHost _host;
    private readonly HangoutStateService _state;
    private readonly Action<HangoutSummaryDto> _openDetail;
    private readonly Action _openManage;

    private readonly List<HangoutCardDto> _items = new();
    private readonly object _itemsLock = new();
    private readonly Dictionary<Guid, ISharedImmediateTexture?> _avatarTex = new();
    private readonly Dictionary<Guid, (float Width, string Text, string[] Lines)> _wrapCache = new();
    private volatile bool _loading;
    private volatile bool _loadingMore;
    private volatile string? _error;
    private bool _hasMore;
    // Pill states parallel to HangoutCategories.FilterValues / RegionValues; none selected = every hangout.
    private readonly bool[] _filterCategories = new bool[HangoutCategories.FilterValues.Length];
    private readonly bool[] _filterRegions = new bool[RegionValues.Length];
    private bool _filterOpen;
    private float _filterPanelH;
    private readonly HangoutsTour _tour = new();

    private const float PadX = 16f;
    private const int MaxDescriptionLines = 3;

    public HangoutsScreen(IHangoutsHost host, HangoutStateService state,
                          Action<HangoutSummaryDto> openDetail, Action openManage)
    {
        _host = host;
        _state = state;
        _openDetail = openDetail;
        _openManage = openManage;
    }

    public void OnShow()
    {
        _filterOpen = false;
        _tour.EnsureSeen();
        StartFetch(reset: true);
    }

    private int SelectedCategoryMask()
    {
        var mask = 0;
        for (var i = 0; i < _filterCategories.Length; i++)
        {
            if (_filterCategories[i])
            {
                mask |= 1 << (short)HangoutCategories.FilterValues[i];
            }
        }
        return mask;
    }

    private AetherLove.Shared.Profile.Enums.Region SelectedRegionMask() =>
        MaskOr(RegionValues, _filterRegions, (a, b) => a | b);

    private static bool PassesMask(int mask, HangoutSummaryDto h) =>
        mask == 0 || (mask & (1 << (short)h.Category)) != 0;

    private void StartFetch(bool reset)
    {
        if (reset)
        {
            _loading = true;
        }
        else
        {
            _loadingMore = true;
        }
        _error = null;
        var skip = reset ? 0 : _items.Count;
        var filter = new HangoutDirectoryFilterDto(SelectedCategoryMask(), FriendsOnly: false, SelectedRegionMask());
        _ = Task.Run(async () =>
        {
            try
            {
                var page = await _host.GetHangoutDirectoryAsync(filter, skip).ConfigureAwait(false);
                lock (_itemsLock)
                {
                    if (reset)
                    {
                        _items.Clear();
                    }
                    _items.AddRange(page.Items);
                }
                CacheAvatars(page.Items);
                _hasMore = page.HasMore;
            }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, "[HangoutsScreen] Directory fetch failed.");
                _error = HubErrorText.Localize(ex);
            }
            finally
            {
                _loading = false;
                _loadingMore = false;
            }
        });
    }

    private void CacheAvatars(IEnumerable<HangoutCardDto> cards)
    {
        var cacheDir = Path.Combine(UiHost.PluginInterface.ConfigDirectory.FullName, "HangoutAvatarCache");
        foreach (var card in cards)
        {
            if (card.OwnerAvatarWebp is { Length: > 0 } bytes)
            {
                _avatarTex[card.Summary.OwnerAccountId] =
                    AvatarDiskCache.Store(cacheDir, card.Summary.OwnerAccountId.ToString(), bytes);
            }
        }
    }

    /// <summary>Live and upcoming hangouts of the user's messenger contacts, live sorted first; avatars come
    /// from the disk cache the directory and card fetches fill.</summary>
    private List<HangoutCardDto> BuildFriendCards(int mask, HashSet<Guid> friendOwnerIds)
    {
        var now = DateTimeOffset.UtcNow;
        var cards = new List<HangoutCardDto>();
        foreach (var h in _state.ContactHangouts())
        {
            friendOwnerIds.Add(h.OwnerAccountId);
            if (h.EndUtc <= now || !PassesMask(mask, h))
            {
                continue;
            }
            cards.Add(new HangoutCardDto(h, null));
        }
        return cards
            .OrderBy(c => !HangoutFields.IsLiveNow(c.Summary))
            .ThenBy(c => c.Summary.StartUtc)
            .ToList();
    }

    public void Draw()
    {
        var winPos = ImGui.GetWindowPos();
        var winSize = ImGui.GetWindowSize();
        var winW = ImGui.GetContentRegionAvail().X;

        DrawHeader(winW);

        var now = DateTimeOffset.UtcNow;
        var mask = SelectedCategoryMask();
        var friendOwnerIds = new HashSet<Guid>();
        var friendCards = BuildFriendCards(mask, friendOwnerIds);

        HangoutCardDto[] fetched;
        lock (_itemsLock)
        {
            fetched = _items
                .Where(c => c.Summary.EndUtc > now
                    && !friendOwnerIds.Contains(c.Summary.OwnerAccountId)
                    && PassesMask(mask, c.Summary))
                .ToArray();
        }
        var others = fetched.Where(c => HangoutFields.IsLiveNow(c.Summary)).ToArray();
        var later = fetched.Where(c => !HangoutFields.IsLiveNow(c.Summary)).ToArray();

        PushScrollbarStyle();
        using (var scroll = ImRaii.Child("##hgDirScroll", ImGui.GetContentRegionAvail(), false))
        {
            if (scroll.Success)
            {
                var listW = ImGui.GetContentRegionAvail().X;

                if (_state.MyHangout is { } mine)
                {
                    DrawMyHangoutCard(mine, listW);
                }

                if (_loading)
                {
                    DrawFriendsSection(friendCards, listW);
                    ImGui.Spacing();
                    CenteredMutedText(Loc.T("hangout.dir_loading"));
                }
                else if (_error is { } err)
                {
                    DrawFriendsSection(friendCards, listW);
                    ImGui.Spacing();
                    CenteredMutedText(err);
                }
                else if (friendCards.Count == 0 && others.Length == 0 && later.Length == 0)
                {
                    DrawEmptyState(listW, showCta: _state.MyHangout is null);
                }
                else
                {
                    DrawFriendsSection(friendCards, listW);
                    if (others.Length > 0)
                    {
                        DrawSectionPill(Loc.T("hangout.dir_others"), UiColors.LiveGreen, FontAwesomeIcon.BroadcastTower);
                        DrawCardList(others, listW);
                    }
                    if (later.Length > 0)
                    {
                        DrawSectionPill(Loc.T("hangout.dir_later"), ThemeService.Current.SecondaryEnd, FontAwesomeIcon.Clock);
                        DrawCardList(later, listW);
                    }
                    if (others.Length == 0 && later.Length == 0)
                    {
                        ImGui.Spacing();
                        CenteredMutedText(Loc.T("hangout.dir_public_empty"));
                    }
                    if (_hasMore)
                    {
                        ImGui.Spacing();
                        ImGui.SetCursorPosX(Px(PadX));
                        PushThemeButton(ThemeService.Current);
                        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
                        using (ImRaii.Disabled(_loadingMore))
                        {
                            if (SharedUiHelpers.Button(Loc.T("hangout.dir_load_more"),
                                    new Vector2(ImGui.GetContentRegionAvail().X - Px(PadX), Px(30f))))
                            {
                                StartFetch(reset: false);
                            }
                        }
                        ImGui.PopStyleVar();
                        PopThemeButton();
                    }
                }
                ImGui.Dummy(new Vector2(1f, Px(10f)));
            }
        }
        PopScrollbarStyle();

        if (_filterOpen)
        {
            DrawFilterOverlay(winPos, winSize);
        }
        _tour.Draw(winPos, winSize);
    }

    /// <summary>Slim card for the user's own hangout, linking to the host's hangout dashboard.</summary>
    private void DrawMyHangoutCard(HangoutSummaryDto mine, float listW)
    {
        var live = HangoutFields.IsLiveNow(mine);
        var accent = live ? UiColors.LiveGreen : ThemeService.Current.Accent;
        var pad = Px(PadX);
        var cardH = Px(44f);
        var start = ImGui.GetCursorScreenPos();
        var tl = start + new Vector2(pad, 0f);
        var size = new Vector2(listW - pad * 2f, cardH);
        var br = tl + size;

        ImGui.SetCursorScreenPos(tl);
        var clicked = ImGui.InvisibleButton("##hgMineCard", size);
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            SharedUiHelpers.HandOnHover();
        }
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(tl, br, ImGui.GetColorU32(accent with { W = hovered ? 0.18f : 0.11f }), Px(10f));
        dl.AddRect(tl, br, ImGui.GetColorU32(accent with { W = 0.65f }), Px(10f), ImDrawFlags.None, Px(1.2f));
        IconDraw.AddCentered(dl, HangoutFields.CategoryIcon(mine.Category), Px(18f),
            new Vector2(tl.X + Px(22f), tl.Y + cardH * 0.5f), ImGui.GetColorU32(accent));
        dl.AddText(new Vector2(tl.X + Px(42f), tl.Y + Px(6f)), 0xFFFFFFFFu, Loc.T("hangout.dir_mine"));
        var status = live ? Loc.T("hangout.chip_live") : HangoutFields.TimeLabel(mine);
        dl.AddText(new Vector2(tl.X + Px(42f), tl.Y + Px(23f)),
            live ? ImGui.GetColorU32(UiColors.LiveGreen) : UiColors.TextMuted, status);
        IconDraw.AddCentered(dl, FontAwesomeIcon.ChevronRight, Px(12f),
            new Vector2(br.X - Px(18f), tl.Y + cardH * 0.5f),
            hovered ? 0xFFFFFFFFu : ImGui.GetColorU32(accent with { W = 0.75f }));

        ImGui.SetCursorScreenPos(new Vector2(start.X, br.Y));
        ImGui.Dummy(new Vector2(1f, Px(8f)));

        if (clicked)
        {
            _openManage();
        }
    }

    /// <summary>Matches pill over the cards, or a faint inset placeholder when no matches are hosting.</summary>
    private void DrawFriendsSection(IReadOnlyList<HangoutCardDto> cards, float listW)
    {
        ImGui.Dummy(new Vector2(1f, Px(4f)));
        DrawSectionPill(Loc.T("hangout.dir_friends"), ThemeService.Current.Accent, FontAwesomeIcon.UserFriends);
        if (cards.Count == 0)
        {
            DrawInsetPlaceholder(listW, Loc.T("hangout.dir_friends_empty"));
        }
        else
        {
            DrawCardList(cards, listW);
        }
    }

    private static void DrawInsetPlaceholder(float listW, string text)
    {
        var dl = ImGui.GetWindowDrawList();
        var pad = Px(PadX);
        var h = Px(38f);
        var tl = ImGui.GetCursorScreenPos() + new Vector2(pad, 0f);
        var br = tl + new Vector2(listW - pad * 2f, h);
        dl.AddRectFilled(tl, br, 0x0DFFFFFFu, Px(10f));
        dl.AddRect(tl, br, 0x22FFFFFFu, Px(10f), ImDrawFlags.None, Px(1f));
        var label = Truncate(text, br.X - tl.X - Px(16f));
        var sz = ImGui.CalcTextSize(label);
        dl.AddText(new Vector2(tl.X + (br.X - tl.X - sz.X) * 0.5f, tl.Y + (h - sz.Y) * 0.5f),
            ImGui.GetColorU32(UiColors.Muted), label);
        ImGui.Dummy(new Vector2(1f, h + Px(8f)));
    }

    /// <summary>Nothing to show: a faded bullhorn, the empty line, and (unless the user is already hosting) a
    /// call to action straight into the create form.</summary>
    private void DrawEmptyState(float listW, bool showCta)
    {
        var dl = ImGui.GetWindowDrawList();
        ImGui.Dummy(new Vector2(1f, Px(36f)));
        var center = ImGui.GetCursorScreenPos() + new Vector2(listW * 0.5f, Px(21f));
        IconDraw.AddCentered(dl, FontAwesomeIcon.Bullhorn, Px(42f), center,
            ImGui.GetColorU32(UiColors.Muted with { W = 0.6f }));
        ImGui.Dummy(new Vector2(1f, Px(56f)));
        CenteredMutedText(Loc.T("hangout.dir_empty"));
        if (!showCta)
        {
            return;
        }

        ImGui.Dummy(new Vector2(1f, Px(14f)));
        var label = Loc.T("hangout.dir_empty_cta");
        var btnW = MathF.Min(listW - Px(PadX) * 2f, ImGui.CalcTextSize(label).X + Px(44f));
        ImGui.SetCursorPosX(MathF.Max(Px(PadX), (listW - btnW) * 0.5f));
        PushThemeButton(ThemeService.Current);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(11f));
        var clicked = SharedUiHelpers.Button(label, new Vector2(btnW, Px(36f)));
        ImGui.PopStyleVar();
        PopThemeButton();
        if (clicked)
        {
            _openManage();
        }
    }

    /// <summary>Each row's accent lerps between the theme accent and secondary in a ping-pong across the section.</summary>
    private void DrawCardList(IReadOnlyList<HangoutCardDto> cards, float listW)
    {
        var t = ThemeService.Current;
        const int gradientSteps = 8;
        const int period = 2 * (gradientSteps - 1);
        for (var i = 0; i < cards.Count; i++)
        {
            var phase = i % period;
            var step = phase < gradientSteps ? phase : period - phase;
            var accent = Vector4.Lerp(t.Accent, t.SecondaryEnd, step / (float)(gradientSteps - 1));
            DrawCard(cards[i], listW, accent);
        }
    }

    private void DrawHeader(float winW)
    {
        ImGui.Spacing();
        DrawSubpageHeading(Loc.T("hangout.dir_title"), PadX);

        var menuTL = AppHeader.DrawMenuButton(winW, PadX, "##hgMenuPopup",
            badge: _filterCategories.Any(c => c) || _filterRegions.Any(r => r));
        var menuOpen = AppHeader.BeginMenuPopup(menuTL, "##hgMenuPopup");
        if (menuOpen)
        {
            var w = AppHeader.MenuWidth(
                Loc.T("hangout.menu_filter"),
                Loc.T("hangout.menu_refresh"),
                Loc.T("hangout.menu_start"),
                Loc.T("hangout.menu_tour"));
            var rowH = AppHeader.MenuRowHeight();
            if (AppHeader.MenuRow(FontAwesomeIcon.Filter, Loc.T("hangout.menu_filter"), w, rowH))
            {
                ImGui.CloseCurrentPopup();
                _filterOpen = true;
                _filterPanelH = 0f;
            }
            if (AppHeader.MenuRow(FontAwesomeIcon.SyncAlt, Loc.T("hangout.menu_refresh"), w, rowH))
            {
                ImGui.CloseCurrentPopup();
                StartFetch(reset: true);
            }
            if (AppHeader.MenuRow(FontAwesomeIcon.PlusCircle, Loc.T("hangout.menu_start"), w, rowH))
            {
                ImGui.CloseCurrentPopup();
                _openManage();
            }
            if (AppHeader.MenuRow(FontAwesomeIcon.Route, Loc.T("hangout.menu_tour"), w, rowH))
            {
                ImGui.CloseCurrentPopup();
                _tour.Open();
            }
        }
        AppHeader.EndMenuPopup(menuOpen);

        if (_filterCategories.Any(c => c) || _filterRegions.Any(r => r))
        {
            ImGui.SetCursorPosX(Px(PadX));
            var labels = HangoutCategories.FilterLabels();
            var parts = new List<string>();
            for (var i = 0; i < _filterCategories.Length; i++)
            {
                if (_filterCategories[i])
                {
                    parts.Add(labels[i]);
                }
            }
            for (var i = 0; i < _filterRegions.Length; i++)
            {
                if (_filterRegions[i])
                {
                    parts.Add(Regions[i]);
                }
            }
            ImGui.PushTextWrapPos(winW - Px(PadX));
            ImGui.TextColored(ThemeService.Current.Accent, Loc.T("hangout.filter_active", string.Join(" · ", parts)));
            ImGui.PopTextWrapPos();
        }
        ImGui.Spacing();
    }

    private void DrawCard(HangoutCardDto card, float winW, Vector4 accent)
    {
        var h = card.Summary;
        var live = HangoutFields.IsLiveNow(h);
        var pad = Px(PadX);
        var dl = ImGui.GetWindowDrawList();
        var lineH = ImGui.GetTextLineHeight() + Px(3f);

        var textX0 = Px(60f);
        var descLines = WrappedDescription(h, winW - pad * 2f - textX0 - Px(44f));
        var descTop = Px(52f);
        var cardH = descTop + descLines.Length * lineH + lineH + Px(10f);

        var start = ImGui.GetCursorScreenPos();
        var tl = start + new Vector2(pad, 0f);
        var size = new Vector2(winW - pad * 2f, cardH);
        var br = tl + size;

        ImGui.SetCursorScreenPos(tl);
        var clicked = ImGui.InvisibleButton($"##hgCard_{h.Id}", size);
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            SharedUiHelpers.HandOnHover();
        }

        dl.AddRectFilled(tl, br, ImGui.GetColorU32(accent with { W = hovered ? 0.16f : 0.09f }), Px(10f));
        dl.AddRect(tl, br, ImGui.GetColorU32(accent with { W = 0.60f }), Px(10f), ImDrawFlags.None, Px(1.2f));

        IconDraw.AddCentered(dl, HangoutFields.CategoryIcon(h.Category), Px(30f),
            new Vector2(br.X - Px(28f), tl.Y + cardH * 0.55f), ImGui.GetColorU32(accent with { W = 0.22f }));

        var avatarCenter = new Vector2(tl.X + Px(30f), tl.Y + Px(32f));
        var avatarR = Px(20f);
        _avatarTex.TryGetValue(h.OwnerAccountId, out var tex);
        var wrapTex = tex?.GetWrapOrDefault();
        if (wrapTex != null)
        {
            dl.AddImageRounded(wrapTex.Handle, avatarCenter - new Vector2(avatarR), avatarCenter + new Vector2(avatarR),
                Vector2.Zero, Vector2.One, 0xFFFFFFFF, avatarR, ImDrawFlags.RoundCornersAll);
        }
        else
        {
            dl.AddCircleFilled(avatarCenter, avatarR, UiColors.AvatarFallback);
        }
        dl.AddCircle(avatarCenter, avatarR, ImGui.GetColorU32(accent with { W = 0.9f }), 0, Px(1.5f));
        AvatarRings.Draw(dl, avatarCenter, avatarR, h.OwnerFrameRef);
        if (h.OwnerIsSupporter)
        {
            var badgeCenter = avatarCenter + new Vector2(avatarR * 0.74f, -avatarR * 0.74f);
            var badgeR = Px(7f);
            dl.AddCircleFilled(badgeCenter, badgeR, 0xFF1E1E24u, 24);
            dl.AddCircle(badgeCenter, badgeR, UiColors.FavoriteStar, 24, Px(1.2f));
            IconDraw.AddCentered(dl, FontAwesomeIcon.Star, badgeR * 1.2f,
                badgeCenter + new Vector2(0f, Px(0.5f)), UiColors.FavoriteStar);
        }

        var textX = tl.X + textX0;
        var maxTextW = br.X - textX - Px(44f);

        dl.AddText(new Vector2(textX, tl.Y + Px(10f)), 0xFFFFFFFFu, Truncate(h.OwnerDisplayName, maxTextW - Px(70f)));
        var statusText = live
            ? Loc.T("hangout.chip_live")
            : Loc.T("hangout.starts_in", HangoutFields.StartsInLabel(h));
        var statusCol = live ? ImGui.GetColorU32(UiColors.LiveGreen) : UiColors.TextMuted;
        var statusSz = ImGui.CalcTextSize(statusText);
        var statusPos = new Vector2(br.X - statusSz.X - Px(10f), tl.Y + Px(10f));
        dl.AddText(statusPos, statusCol, statusText);
        if (live)
        {
            var pulse = AccessibilityService.ReduceMotion ? 1f : 0.5f + 0.5f * MathF.Sin((float)ImGui.GetTime() * 4f);
            dl.AddCircleFilled(new Vector2(statusPos.X - Px(8f), statusPos.Y + statusSz.Y * 0.5f), Px(3.5f),
                ImGui.GetColorU32(UiColors.LiveGreen with { W = pulse }));
        }
        else
        {
            IconDraw.AddCentered(dl, FontAwesomeIcon.Clock, Px(10f),
                new Vector2(statusPos.X - Px(9f), statusPos.Y + statusSz.Y * 0.5f), UiColors.TextMuted);
        }

        IconDraw.AddCentered(dl, HangoutCategories.Icon(h.Category), Px(11f),
            new Vector2(textX + Px(6f), tl.Y + Px(37f)), ImGui.GetColorU32(accent));
        var meta = HangoutCategories.Label(h.Category);
        if (h.RsvpCount > 0)
        {
            meta += "  ·  " + Loc.T("hangout.coming_count", HangoutFields.CountLabel(h))
                + (HangoutFields.IsAtCapacity(h) ? " " + Loc.T("hangout.at_capacity") : string.Empty);
        }
        dl.AddText(new Vector2(textX + Px(17f), tl.Y + Px(29f)), ImGui.GetColorU32(accent), Truncate(meta, maxTextW - Px(17f)));

        for (var i = 0; i < descLines.Length; i++)
        {
            dl.AddText(new Vector2(textX, tl.Y + descTop + i * lineH), ImGui.GetColorU32(UiColors.Body), descLines[i]);
        }
        dl.AddText(new Vector2(textX, tl.Y + descTop + descLines.Length * lineH), UiColors.TextFaint,
            Truncate(HangoutFields.FormatAddress(h), maxTextW));

        if (clicked)
        {
            _openDetail(h);
        }

        ImGui.SetCursorScreenPos(new Vector2(start.X, br.Y));
        ImGui.Dummy(new Vector2(1f, Px(8f)));
    }

    /// <summary>Wraps a description to at most <see cref="MaxDescriptionLines"/> lines, memoised per hangout.</summary>
    private string[] WrappedDescription(HangoutSummaryDto h, float maxW)
    {
        if (_wrapCache.TryGetValue(h.Id, out var cached)
            && MathF.Abs(cached.Width - maxW) < 0.5f && cached.Text == h.Description)
        {
            return cached.Lines;
        }

        var words = h.Description.Replace('\n', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var lines = new List<string>();
        var current = "";
        foreach (var word in words)
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (ImGui.CalcTextSize(candidate).X <= maxW)
            {
                current = candidate;
                continue;
            }
            if (current.Length > 0)
            {
                lines.Add(current);
            }
            current = word;
            if (lines.Count == MaxDescriptionLines)
            {
                break;
            }
        }
        if (current.Length > 0 && lines.Count < MaxDescriptionLines)
        {
            lines.Add(current);
        }
        else if (lines.Count >= MaxDescriptionLines)
        {
            lines[MaxDescriptionLines - 1] = Truncate(lines[MaxDescriptionLines - 1] + " " + current, maxW);
        }
        var result = lines.Take(MaxDescriptionLines).ToArray();
        _wrapCache[h.Id] = (maxW, h.Description, result);
        return result;
    }

    private void DrawFilterOverlay(Vector2 winPos, Vector2 winSize)
    {
        var dismissed = DrawPageOverlayPanel("hgDirFilter", winPos, winSize, ref _filterPanelH, Px(280f), w =>
        {
            ModalUi.Header(w, FontAwesomeIcon.Filter, Loc.T("hangout.filter_title"), ThemeService.Current.Accent);

            ImGui.TextColored(UiColors.Subtle, Loc.T("hangout.filter_activities"));
            ImGui.Spacing();
            VenueFields.DrawPillToggleRow("hgcat", HangoutCategories.FilterLabels(), _filterCategories, w);

            ImGui.Spacing();
            ImGui.TextColored(UiColors.Subtle, Loc.T("hangout.filter_regions"));
            ImGui.Spacing();
            VenueFields.DrawPillToggleRow("hgreg", Regions, _filterRegions, w);

            ImGui.Spacing();
            ImGui.PushTextWrapPos(w);
            ImGui.TextColored(UiColors.Hint, Loc.T("hangout.filter_hint"));
            ImGui.PopTextWrapPos();
            ImGui.Spacing();
            ImGui.Spacing();

            if (ModalUi.Button($"{Loc.T("hangout.filter_apply")}##hgFilterApply", w))
            {
                _filterOpen = false;
                StartFetch(reset: true);
            }
        });
        if (dismissed)
        {
            _filterOpen = false;
            StartFetch(reset: true);
        }
    }

    private static void CenteredMutedText(string text)
    {
        var winW = ImGui.GetContentRegionAvail().X;
        var textW = ImGui.CalcTextSize(text).X;
        ImGui.SetCursorPosX(MathF.Max(Px(PadX), (winW - textW) * 0.5f));
        ImGui.TextColored(UiColors.Muted, text);
    }

    private static string Truncate(string text, float maxW)
    {
        text = text.Replace('\n', ' ');
        if (ImGui.CalcTextSize(text).X <= maxW)
        {
            return text;
        }
        while (text.Length > 1 && ImGui.CalcTextSize(text + "…").X > maxW)
        {
            text = text[..^1];
        }
        return text + "…";
    }
}
