using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove.Navigation;
using AetherLove.Services;
using AetherLove.Services.Chat;
using AetherLove.Services.Hangouts;
using AetherLove.Services.Hub;
using AetherLove.Services.Localization;
using AetherLove.Shared.Hangouts;
using AetherLove.UI;
using AetherLove.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;

namespace AetherLove.Screens;

/// <summary>The Hangouts tab: matches' hangouts on top, then everyone else's happening-now and later listings.</summary>
public sealed class HangoutsScreen
{
    private readonly ScreenRouter _router;
    private readonly AetherLoveHubClient _hub;
    private readonly HangoutStateService _state;
    private readonly ChatCacheStore _chatCache;
    private readonly Widgets.HangoutOverlay _overlay;
    private readonly Widgets.HangoutSharePicker _sharePicker;
    private readonly ProfileScreen _profileScreen;
    private readonly MyProfileScreen _myProfileScreen;

    private readonly List<HangoutCardDto> _items = new();
    private readonly object _itemsLock = new();
    private readonly Dictionary<Guid, ISharedImmediateTexture?> _avatarTex = new();
    private readonly Dictionary<Guid, (float Width, string Text, string[] Lines)> _wrapCache = new();
    private volatile bool _loading;
    private volatile bool _loadingMore;
    private volatile string? _error;
    private bool _hasMore;
    // Pill states parallel to HangoutFields.CategoryValues; none selected = every hangout.
    private readonly bool[] _filterCategories = new bool[HangoutFields.CategoryValues.Length];
    private bool _filterOpen;
    private float _filterPanelH;
    private bool _startInfoOpen;
    private float _startInfoPanelH;
    private float _introPanelH;

    private const float PadX = 16f;
    private const int MaxDescriptionLines = 3;

    public HangoutsScreen(
        ScreenRouter router,
        AetherLoveHubClient hub,
        HangoutStateService state,
        ChatCacheStore chatCache,
        Widgets.HangoutOverlay overlay,
        Widgets.HangoutSharePicker sharePicker,
        ProfileScreen profileScreen,
        MyProfileScreen myProfileScreen)
    {
        _router = router;
        _hub = hub;
        _state = state;
        _chatCache = chatCache;
        _overlay = overlay;
        _sharePicker = sharePicker;
        _profileScreen = profileScreen;
        _myProfileScreen = myProfileScreen;
    }

    private HangoutSummaryDto? _pendingOpenHangout;

    /// <summary>Deep-link from a chat notification; opens immediately or on the next OnShow.</summary>
    public void RequestOpenHangout(HangoutSummaryDto hangout)
    {
        if (_router.Current == Screen.Hangouts)
        {
            OpenHangoutOverlay(hangout);
            return;
        }
        _pendingOpenHangout = hangout;
    }

    private void OpenHangoutOverlay(HangoutSummaryDto hangout) =>
        _overlay.Open(hangout, onViewProfile: () => OpenOwnerProfile(hangout.OwnerProfileId));

    public void OnShow()
    {
        _filterOpen = false;
        _startInfoOpen = false;
        StartFetch(reset: true);
        if (_pendingOpenHangout is { } pending)
        {
            _pendingOpenHangout = null;
            OpenHangoutOverlay(pending);
        }
    }

    private int SelectedCategoryMask()
    {
        var mask = 0;
        for (var i = 0; i < _filterCategories.Length; i++)
        {
            if (_filterCategories[i])
            {
                mask |= 1 << (short)HangoutFields.CategoryValues[i];
            }
        }
        return mask;
    }

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
        var filter = new HangoutDirectoryFilterDto(SelectedCategoryMask(), MatchesOnly: false);
        _ = Task.Run(async () =>
        {
            try
            {
                var page = await _hub.GetHangoutDirectoryAsync(filter, skip).ConfigureAwait(false);
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
                Plugin.Log.Warning(ex, "[HangoutsScreen] Directory fetch failed.");
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
        var cacheDir = Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, "HangoutAvatarCache");
        foreach (var card in cards)
        {
            if (card.OwnerAvatarWebp is { Length: > 0 } bytes)
            {
                _avatarTex[card.Summary.OwnerProfileId] =
                    AvatarDiskCache.Store(cacheDir, card.Summary.OwnerProfileId.ToString(), bytes);
            }
        }
    }

    /// <summary>Live and upcoming hangouts of the user's matches, live sorted first; avatars come from the chat cache.</summary>
    private List<HangoutCardDto> BuildMatchCards(int mask, HashSet<Guid> matchOwnerIds)
    {
        var now = DateTimeOffset.UtcNow;
        var matches = _chatCache.GetMatches();
        var cards = new List<HangoutCardDto>();
        foreach (var h in _state.MatchHangouts())
        {
            matchOwnerIds.Add(h.OwnerProfileId);
            if (h.EndUtc <= now || !PassesMask(mask, h))
            {
                continue;
            }
            var avatar = matches.FirstOrDefault(m => m.PeerProfileId == h.OwnerProfileId)?.PeerAvatarWebp;
            cards.Add(new HangoutCardDto(h, avatar));
        }
        CacheAvatars(cards);
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
        var matchOwnerIds = new HashSet<Guid>();
        var matchCards = BuildMatchCards(mask, matchOwnerIds);

        HangoutCardDto[] fetched;
        lock (_itemsLock)
        {
            fetched = _items
                .Where(c => c.Summary.EndUtc > now
                    && !matchOwnerIds.Contains(c.Summary.OwnerProfileId)
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
                    DrawMatchesSection(matchCards, listW);
                    ImGui.Spacing();
                    CenteredMutedText(Loc.T("hangout.dir_loading"));
                }
                else if (_error is { } err)
                {
                    DrawMatchesSection(matchCards, listW);
                    ImGui.Spacing();
                    CenteredMutedText(err);
                }
                else if (matchCards.Count == 0 && others.Length == 0 && later.Length == 0)
                {
                    DrawEmptyState(listW);
                }
                else
                {
                    DrawMatchesSection(matchCards, listW);
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
                        CenteredMutedText(Loc.T("hangout.dir_empty"));
                    }
                    if (_hasMore)
                    {
                        ImGui.Spacing();
                        ImGui.SetCursorPosX(Px(PadX));
                        PushThemeButton(ThemeService.Current);
                        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
                        using (ImRaii.Disabled(_loadingMore))
                        {
                            if (ImGui.Button(Loc.T("hangout.dir_load_more"),
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

        if (!Plugin.Configuration.Hangouts.SeenDirectoryIntro)
        {
            DrawIntroOverlay(winPos, winSize);
        }
        if (_filterOpen)
        {
            DrawFilterOverlay(winPos, winSize);
        }
        if (_startInfoOpen)
        {
            DrawStartInfoOverlay(winPos, winSize);
        }
        _overlay.Draw(winPos, winSize);
        _sharePicker.Draw(winPos, winSize);
    }

    /// <summary>Slim card for the user's own hangout, linking to the My Profile hangout dashboard.</summary>
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
            _myProfileScreen.RequestHangoutView();
            _router.Navigate(Screen.MyProfile);
        }
    }

    /// <summary>Matches pill over the cards, or a faint inset placeholder when no matches are hosting.</summary>
    private void DrawMatchesSection(IReadOnlyList<HangoutCardDto> cards, float listW)
    {
        ImGui.Dummy(new Vector2(1f, Px(4f)));
        DrawSectionPill(Loc.T("hangout.dir_matches"), ThemeService.Current.Accent, FontAwesomeIcon.Heart);
        if (cards.Count == 0)
        {
            DrawInsetPlaceholder(listW, Loc.T("hangout.dir_matches_empty"));
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

    private static void DrawEmptyState(float listW)
    {
        var dl = ImGui.GetWindowDrawList();
        ImGui.Dummy(new Vector2(1f, Px(36f)));
        var center = ImGui.GetCursorScreenPos() + new Vector2(listW * 0.5f, Px(21f));
        IconDraw.AddCentered(dl, FontAwesomeIcon.Bullhorn, Px(42f), center,
            ImGui.GetColorU32(UiColors.Muted with { W = 0.6f }));
        ImGui.Dummy(new Vector2(1f, Px(56f)));
        CenteredMutedText(Loc.T("hangout.dir_empty"));
        ImGui.Spacing();
        CenteredMutedText(Loc.T("hangout.dir_empty_hint"));
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

        var btn = Px(26f);
        ImGui.SameLine(winW - btn - Px(PadX));
        ImGui.PushFont(Plugin.PluginInterface.UiBuilder.FontIcon);
        ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);
        if (ImGui.Button($"{FontAwesomeIcon.Bars.ToIconString()}##hgMenu", new Vector2(btn, btn)))
        {
            ImGui.OpenPopup("##hgMenuPopup");
        }
        ImGui.PopStyleColor();
        ImGui.PopFont();

        if (ImGui.BeginPopup("##hgMenuPopup"))
        {
            if (ChatScreen.DrawIconMenuItem(FontAwesomeIcon.Filter, Loc.T("hangout.menu_filter")))
            {
                ImGui.CloseCurrentPopup();
                _filterOpen = true;
                _filterPanelH = 0f;
            }
            if (ChatScreen.DrawIconMenuItem(FontAwesomeIcon.SyncAlt, Loc.T("hangout.menu_refresh")))
            {
                ImGui.CloseCurrentPopup();
                StartFetch(reset: true);
            }
            if (ChatScreen.DrawIconMenuItem(FontAwesomeIcon.PlusCircle, Loc.T("hangout.menu_start")))
            {
                ImGui.CloseCurrentPopup();
                _startInfoOpen = true;
                _startInfoPanelH = 0f;
            }
            ImGui.EndPopup();
        }

        if (_filterCategories.Any(c => c))
        {
            ImGui.SetCursorPosX(Px(PadX));
            var labels = HangoutFields.CategoryLabels();
            var parts = new List<string>();
            for (var i = 0; i < _filterCategories.Length; i++)
            {
                if (_filterCategories[i])
                {
                    parts.Add(labels[i]);
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

        dl.AddRectFilled(tl, br, ImGui.GetColorU32(accent with { W = hovered ? 0.16f : 0.09f }), Px(10f));
        dl.AddRect(tl, br, ImGui.GetColorU32(accent with { W = 0.60f }), Px(10f), ImDrawFlags.None, Px(1.2f));

        IconDraw.AddCentered(dl, HangoutFields.CategoryIcon(h.Category), Px(30f),
            new Vector2(br.X - Px(28f), tl.Y + cardH * 0.55f), ImGui.GetColorU32(accent with { W = 0.22f }));

        var avatarCenter = new Vector2(tl.X + Px(30f), tl.Y + Px(32f));
        var avatarR = Px(20f);
        _avatarTex.TryGetValue(h.OwnerProfileId, out var tex);
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

        IconDraw.AddCentered(dl, HangoutFields.CategoryIcon(h.Category), Px(11f),
            new Vector2(textX + Px(6f), tl.Y + Px(37f)), ImGui.GetColorU32(accent));
        var meta = HangoutFields.CategoryLabel(h.Category);
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
            var summary = h;
            _overlay.Open(summary, onViewProfile: () => OpenOwnerProfile(summary.OwnerProfileId));
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

    private void OpenOwnerProfile(Guid ownerProfileId)
    {
        _profileScreen.SetProfile(ownerProfileId, ProfileSource.Hangout);
        _router.Navigate(Screen.Profile);
    }

    private void DrawIntroOverlay(Vector2 winPos, Vector2 winSize)
    {
        DrawPageOverlayPanel("hgDirIntro", winPos, winSize, ref _introPanelH, Px(300f), w =>
        {
            ModalUi.Header(w, FontAwesomeIcon.Bullhorn, Loc.T("hangout.intro_dir_title"), ThemeService.Current.Accent);
            ImGui.PushTextWrapPos(w);
            ImGui.TextColored(UiColors.Body, Loc.T("hangout.intro_dir_body"));
            ImGui.Spacing();
            ImGui.TextColored(UiColors.Hint, Loc.T("hangout.intro_rules"));
            ImGui.PopTextWrapPos();
            ImGui.Spacing();
            if (ModalUi.Button($"{Loc.T("hangout.intro_got_it")}##hgDirGotIt", w))
            {
                Plugin.Configuration.Hangouts.SeenDirectoryIntro = true;
                Plugin.Configuration.Save();
            }
        });
    }

    private void DrawFilterOverlay(Vector2 winPos, Vector2 winSize)
    {
        var dismissed = DrawPageOverlayPanel("hgDirFilter", winPos, winSize, ref _filterPanelH, Px(280f), w =>
        {
            ModalUi.Header(w, FontAwesomeIcon.Filter, Loc.T("hangout.filter_title"), ThemeService.Current.Accent);

            ImGui.TextColored(UiColors.Subtle, Loc.T("hangout.filter_activities"));
            ImGui.Spacing();
            VenueFields.DrawPillToggleRow("hgcat", HangoutFields.CategoryLabels(), _filterCategories, w);

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

    private void DrawStartInfoOverlay(Vector2 winPos, Vector2 winSize)
    {
        var dismissed = DrawPageOverlayPanel("hgStartInfo", winPos, winSize, ref _startInfoPanelH, Px(230f), w =>
        {
            ModalUi.Header(w, FontAwesomeIcon.PlusCircle, Loc.T("hangout.start_info_title"), ThemeService.Current.Accent);
            ImGui.PushTextWrapPos(w);
            ImGui.TextColored(UiColors.Body, Loc.T("hangout.start_info_body"));
            ImGui.PopTextWrapPos();
            ImGui.Spacing();
            ImGui.Spacing();
            var gap = Px(8f);
            var half = (w - gap) * 0.5f;
            if (ModalUi.Button($"{Loc.T("common.close")}##hgStartClose", half))
            {
                _startInfoOpen = false;
            }
            ImGui.SameLine(0f, gap);
            if (ModalUi.Button($"{Loc.T("hangout.start_info_go")}##hgStartGo", half))
            {
                _startInfoOpen = false;
                _myProfileScreen.RequestHangoutView();
                _router.Navigate(Screen.MyProfile);
            }
        });
        if (dismissed)
        {
            _startInfoOpen = false;
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
