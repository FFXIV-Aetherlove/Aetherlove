using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AetherLove;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using AetherLove.Widgets;
using AetherOS.Sdk;
using AetherLove.Shared.Wayfinder;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Wayfinder;

/// <summary>The Wayfinder home: today's challenges, the continue/start hero, the supporter upsell when the free
/// cap is spent, and the locally-kept list of found places.</summary>
internal sealed class HomeScreen
{
    private const float PadX = 16f;

    private readonly IWayfinderHost _host;
    private readonly WayfinderHistory _history;
    private readonly Action<WayfinderAssignmentDto> _openChallenge;
    private readonly Action _openTour;
    private readonly Action _openCreate;
    private readonly Action _openParty;
    private readonly EntranceAnimation _entrance = new();

    private volatile WayfinderStateDto? _state;
    private volatile bool _loading;
    private volatile string? _error;
    private volatile bool _starting;
    private int _generation;

    public HomeScreen(IWayfinderHost host, WayfinderHistory history,
        Action<WayfinderAssignmentDto> openChallenge, Action openTour, Action openCreate, Action openParty)
    {
        _host = host;
        _history = history;
        _openChallenge = openChallenge;
        _openTour = openTour;
        _openCreate = openCreate;
        _openParty = openParty;
    }

    public void OnShow()
    {
        _entrance.Arm();
        Refresh();
    }

    public void Refresh()
    {
        var generation = Interlocked.Increment(ref _generation);
        _loading = true;
        _error = null;
        _ = Task.Run(async () =>
        {
            try
            {
                var state = await _host.GetStateAsync().ConfigureAwait(false);
                if (generation == _generation)
                {
                    _state = state;
                    _stateLoadedAt = DateTimeOffset.UtcNow;
                }
            }
            catch (Exception ex)
            {
                if (generation == _generation)
                {
                    _error = HubErrorText.Localize(ex);
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

    private void StartChallenge()
    {
        if (_starting)
        {
            return;
        }
        _starting = true;
        _error = null;
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await _host.StartAsync().ConfigureAwait(false);
                _pendingOpen = result.Assignment;
            }
            catch (Exception ex)
            {
                _error = HubErrorText.Localize(ex);
            }
            finally
            {
                _starting = false;
            }
        });
    }

    private volatile WayfinderAssignmentDto? _pendingOpen;

    public void Draw(OsAppContext ctx)
    {
        if (_pendingOpen is { } open)
        {
            _pendingOpen = null;
            _openChallenge(open);
            return;
        }

        _entrance.BeginFrame();
        DrawHistoryViewer(ctx);
        var t = ThemeService.Current;
        var winW = ImGui.GetWindowSize().X;

        ImGui.Dummy(new Vector2(0f, Px(10f)));
        ImGui.SetCursorPosX(Px(PadX));
        using (ctx.TitleFont?.Push())
        {
            ImGui.TextColored(t.AccentLight, Loc.T("os.app_wayfinder"));
        }
        DrawMenu(ctx);
        ImGui.Dummy(new Vector2(0f, Px(6f)));

        var state = _state;
        if (state is null)
        {
            if (_loading)
            {
                ImGui.Dummy(new Vector2(0f, Px(40f)));
                var center = new Vector2(ImGui.GetWindowPos().X + winW * 0.5f, ImGui.GetCursorScreenPos().Y + Px(20f));
                LoadingSpinner.Draw(center, Px(14f), Px(3f), ImGui.GetColorU32(t.Accent));
                ImGui.Dummy(new Vector2(0f, Px(50f)));
                DrawCenteredHint(Loc.T("os.wf_loading"), winW);
            }
            else
            {
                DrawCenteredHint(_error ?? Loc.T("os.wf_offline"), winW);
                DrawRetry(winW);
            }
            _entrance.EndFrame();
            return;
        }

        DrawStartsCard(state, winW);
        if (ctx.Capabilities.Party.InParty)
        {
            DrawPartyCard(winW);
        }

        if (_error is { } error)
        {
            ImGui.SetCursorPosX(Px(PadX));
            ImGui.PushTextWrapPos(winW - Px(PadX));
            ImGui.TextColored(UiColors.Danger, error);
            ImGui.PopTextWrapPos();
            ImGui.Dummy(new Vector2(0f, Px(4f)));
        }

        if (state.Active is { } active)
        {
            DrawContinueCard(active, winW);
        }
        else if (state.StartsRemainingToday <= 0)
        {
            DrawLimitCard(ctx, state, winW);
        }
        else if (state.ChallengesAvailable <= 0)
        {
            DrawAllDoneCard(state, winW);
        }
        else
        {
            DrawStartCard(winW);
        }

        if (_host.IsScout)
        {
            DrawAddWaypointRow(winW);
        }

        DrawHistory(ctx, winW);
        ImGui.Dummy(new Vector2(0f, Px(14f)));
        _entrance.EndFrame();
    }

    private void DrawMenu(OsAppContext ctx)
    {
        const string popupId = "##wfMenu";
        var menuTL = AppHeader.DrawMenuButton(ImGui.GetWindowSize().X, PadX, popupId);
        var open = AppHeader.BeginMenuPopup(menuTL, popupId);
        if (open)
        {
            var tour = Loc.T("os.wf_menu_tour");
            var supporter = Loc.T("os.wf_menu_supporter");
            var refresh = Loc.T("os.wf_menu_refresh");
            var w = AppHeader.MenuWidth(tour, supporter, refresh);
            var rowH = AppHeader.MenuRowHeight();

            if (AppHeader.MenuRow(FontAwesomeIcon.Compass, tour, w, rowH))
            {
                _openTour();
                ImGui.CloseCurrentPopup();
            }
            if (AppHeader.MenuRow(FontAwesomeIcon.Star, supporter, w, rowH, UiColors.FavoriteStar))
            {
                ctx.Shell?.SendIntent("settings", OsIntents.CreateReturn(OsIntents.OpenSupporter, "wayfinder"));
                ImGui.CloseCurrentPopup();
            }
            if (AppHeader.MenuRow(FontAwesomeIcon.SyncAlt, refresh, w, rowH))
            {
                Refresh();
                ImGui.CloseCurrentPopup();
            }
        }
        AppHeader.EndMenuPopup(open);
    }

    /// <summary>Today's start pips plus the found/available counters.</summary>
    private void DrawStartsCard(WayfinderStateDto state, float winW)
    {
        var t = ThemeService.Current;
        var cardW = winW - Px(PadX) * 2f;
        var lineH = ImGui.GetTextLineHeight();
        var cardH = lineH * 2f + Px(30f);
        ImGui.SetCursorPosX(Px(PadX));
        var tl = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(tl, tl + new Vector2(cardW, cardH), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.05f)), Px(14f));

        dl.AddText(new Vector2(tl.X + Px(14f), tl.Y + Px(10f)), ImGui.GetColorU32(UiColors.Hint),
            Loc.T("os.wf_today"));

        // The row always spans the supporter cap, so a free account sees the slots it is missing.
        var slots = Math.Max(state.DailyCap, state.SupporterDailyCap);
        var used = state.DailyCap - state.StartsRemainingToday;
        var pipR = Px(6f);
        var pipGap = Px(8f);
        var pipsW = slots * pipR * 2f + (slots - 1) * pipGap;
        var pipX = tl.X + cardW - Px(14f) - pipsW + pipR;
        var pipY = tl.Y + Px(10f) + lineH * 0.5f;
        for (var i = 0; i < slots; i++)
        {
            var center = new Vector2(pipX + i * (pipR * 2f + pipGap), pipY);
            if (i >= state.DailyCap)
            {
                dl.AddCircle(center, pipR, ImGui.GetColorU32(UiColors.Hint with { W = 0.28f }), 0, Px(1.4f));
                if (ImGui.IsMouseHoveringRect(center - new Vector2(pipR, pipR), center + new Vector2(pipR, pipR)))
                {
                    ImGui.SetTooltip(Loc.T("os.wf_locked_slot", state.SupporterDailyCap));
                }
            }
            else if (i < used)
            {
                dl.AddCircle(center, pipR, ImGui.GetColorU32(UiColors.Hint with { W = 0.5f }), 0, Px(1.4f));
            }
            else
            {
                dl.AddCircleFilled(center, pipR, ImGui.GetColorU32(t.Accent));
            }
        }

        var subY = tl.Y + Px(14f) + lineH + Px(4f);
        dl.AddText(new Vector2(tl.X + Px(14f), subY), ImGui.GetColorU32(UiColors.Body),
            Loc.T("os.wf_starts_left", state.StartsRemainingToday, state.DailyCap));
        var foundLabel = Loc.T("os.wf_found_total", state.TotalFound);
        var foundSz = ImGui.CalcTextSize(foundLabel);
        dl.AddText(new Vector2(tl.X + cardW - foundSz.X - Px(14f), subY), ImGui.GetColorU32(t.AccentLight), foundLabel);

        ImGui.Dummy(new Vector2(0f, cardH + Px(8f)));
    }

    /// <summary>The animated "start a challenge" hero.</summary>
    private void DrawStartCard(float winW)
    {
        var t = ThemeService.Current;
        var cardW = winW - Px(PadX) * 2f;
        var cardH = Px(104f);
        ImGui.SetCursorPosX(Px(PadX));
        var tl = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##wfStart", new Vector2(cardW, cardH));
        HandOnHover();
        var hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();

        var pulse = AccessibilityService.ReduceMotion ? 0.45f : 0.35f + 0.18f * (float)Math.Sin(ImGui.GetTime() * 1.8);
        dl.AddRectFilled(tl, tl + new Vector2(cardW, cardH),
            ImGui.GetColorU32(t.Accent with { W = hovered ? 0.26f : 0.16f }), Px(16f));
        dl.AddRect(tl, tl + new Vector2(cardW, cardH), ImGui.GetColorU32(t.Accent with { W = pulse }), Px(16f),
            ImDrawFlags.None, Px(1.4f));

        var circle = Px(52f);
        var circleTl = new Vector2(tl.X + Px(16f), tl.Y + (cardH - circle) * 0.5f);
        dl.AddCircleFilled(circleTl + new Vector2(circle * 0.5f, circle * 0.5f), circle * 0.5f,
            ImGui.GetColorU32(t.Accent with { W = 0.30f }));
        var iconPx = circle * 0.5f;
        var iconSz = IconDraw.Measure(FontAwesomeIcon.Compass, iconPx);
        IconDraw.Add(dl, FontAwesomeIcon.Compass, iconPx,
            circleTl + new Vector2((circle - iconSz.X) * 0.5f, (circle - iconSz.Y) * 0.5f),
            ImGui.GetColorU32(t.AccentLight));

        var textX = circleTl.X + circle + Px(14f);
        float titleH;
        using (UiFonts.H3?.Push())
        {
            titleH = ImGui.GetTextLineHeight();
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), new Vector2(textX, tl.Y + Px(24f)),
                ImGui.GetColorU32(UiColors.Body), _starting ? Loc.T("os.wf_starting") : Loc.T("os.wf_start"));
        }
        dl.AddText(new Vector2(textX, tl.Y + Px(26f) + titleH), ImGui.GetColorU32(UiColors.Hint),
            Loc.T("os.wf_start_sub"));

        ImGui.Dummy(new Vector2(0f, Px(8f)));
        if (clicked && !_starting)
        {
            StartChallenge();
        }
    }

    /// <summary>Continue card for a challenge already in flight, with the live countdown.</summary>
    private void DrawContinueCard(WayfinderAssignmentDto active, float winW)
    {
        var t = ThemeService.Current;
        var cardW = winW - Px(PadX) * 2f;
        var badge = Px(40f);
        var hint = Loc.T("os.wf_active_hint");

        // Measured before the card is submitted: the name wraps to as many lines as it needs and the card
        // grows to fit, so a long name is never cut or pushed under the countdown.
        var remaining = TimeSpan.FromSeconds(Math.Max(0, active.RemainingSeconds
            - (int)(DateTimeOffset.UtcNow - _stateLoadedAt).TotalSeconds));
        var timeLabel = FormatSpan(remaining);
        var timeSz = ImGui.CalcTextSize(timeLabel);
        var textInset = Px(16f) + badge + Px(14f);
        var nameW = cardW - textInset - timeSz.X - Px(26f);
        var hintW = cardW - textInset - Px(16f);
        Vector2 nameSz;
        float nameLineH;
        using (UiFonts.H3?.Push())
        {
            nameSz = ImGui.CalcTextSize(active.ChallengeName, false, nameW);
            nameLineH = ImGui.GetTextLineHeight();
        }
        var hintSz = ImGui.CalcTextSize(hint, false, hintW);
        var cardH = MathF.Max(Px(104f), Px(18f) + nameSz.Y + Px(6f) + hintSz.Y + Px(18f));

        ImGui.SetCursorPosX(Px(PadX));
        var tl = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##wfContinue", new Vector2(cardW, cardH));
        HandOnHover();
        var hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(tl, tl + new Vector2(cardW, cardH),
            ImGui.GetColorU32(t.Accent with { W = hovered ? 0.26f : 0.18f }), Px(16f));
        dl.AddRect(tl, tl + new Vector2(cardW, cardH), ImGui.GetColorU32(t.Accent with { W = 0.5f }), Px(16f),
            ImDrawFlags.None, Px(1.2f));

        var badgeTl = new Vector2(tl.X + Px(16f), tl.Y + Px(18f) + (nameLineH - badge) * 0.5f);
        ExpansionBadge.Draw(dl, badgeTl, badge, active.Expansion);

        dl.AddText(new Vector2(tl.X + cardW - timeSz.X - Px(16f), tl.Y + Px(18f)),
            ImGui.GetColorU32(remaining < TimeSpan.FromMinutes(10) ? UiColors.WarningAccent : t.AccentLight), timeLabel);

        var textX = tl.X + textInset;
        using (UiFonts.H3?.Push())
        {
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), new Vector2(textX, tl.Y + Px(18f)),
                ImGui.GetColorU32(UiColors.Body), active.ChallengeName, nameW);
        }
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
            new Vector2(textX, tl.Y + Px(24f) + nameSz.Y), ImGui.GetColorU32(UiColors.Hint), hint, hintW);

        ImGui.Dummy(new Vector2(0f, Px(8f)));
        if (clicked)
        {
            _openChallenge(active);
        }
    }

    private DateTimeOffset _stateLoadedAt = DateTimeOffset.UtcNow;

    /// <summary>Cap-spent card: friendly for supporters, an upsell for free users.</summary>
    private void DrawLimitCard(OsAppContext ctx, WayfinderStateDto state, float winW)
    {
        var cardW = winW - Px(PadX) * 2f;
        ImGui.SetCursorPosX(Px(PadX));
        var tl = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        var lineH = ImGui.GetTextLineHeight();
        var upsell = !_host.IsSupporter;
        var textW = cardW - Px(28f);
        var body = Loc.T("os.wf_limit_body", state.DailyCap);
        var bodySz = ImGui.CalcTextSize(body, false, textW);
        var untilReset = NextResetUtc() - DateTime.UtcNow;
        var countdown = Loc.T("os.wf_limit_countdown",
            FormatSpan(untilReset < TimeSpan.Zero ? TimeSpan.Zero : untilReset));
        var bodyY = tl.Y + Px(16f) + lineH;
        var countY = bodyY + bodySz.Y + Px(8f);
        var cardH = countY - tl.Y + lineH + Px(14f) + (upsell ? Px(40f) : 0f);
        dl.AddRectFilled(tl, tl + new Vector2(cardW, cardH), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.05f)), Px(16f));

        var moonSz = IconDraw.Measure(FontAwesomeIcon.Moon, Px(16f));
        IconDraw.Add(dl, FontAwesomeIcon.Moon, Px(16f), new Vector2(tl.X + Px(14f), tl.Y + Px(12f)),
            ImGui.GetColorU32(UiColors.Hint));
        dl.AddText(new Vector2(tl.X + Px(14f) + moonSz.X + Px(8f), tl.Y + Px(12f)),
            ImGui.GetColorU32(UiColors.Body), Loc.T("os.wf_limit_title"));
        DrawWrapped(dl, body, new Vector2(tl.X + Px(14f), bodyY), textW, UiColors.Hint);

        var clockSz = IconDraw.Measure(FontAwesomeIcon.HourglassHalf, Px(12f));
        IconDraw.Add(dl, FontAwesomeIcon.HourglassHalf, Px(12f),
            new Vector2(tl.X + Px(14f), countY + (lineH - clockSz.Y) * 0.5f),
            ImGui.GetColorU32(ctx.Theme.AccentLight));
        dl.AddText(new Vector2(tl.X + Px(14f) + clockSz.X + Px(7f), countY),
            ImGui.GetColorU32(ctx.Theme.AccentLight), countdown);

        if (upsell)
        {
            var star = ImGui.ColorConvertU32ToFloat4(UiColors.FavoriteStar);
            var btnY = tl.Y + cardH - Px(40f);
            var starSz = IconDraw.Measure(FontAwesomeIcon.Star, Px(13f));
            var label = Loc.T("os.wf_upsell_btn");
            var labelSz = ImGui.CalcTextSize(label);
            var btnW = starSz.X + Px(8f) + labelSz.X + Px(28f);
            var btnTl = new Vector2(tl.X + (cardW - btnW) * 0.5f, btnY);
            ImGui.SetCursorScreenPos(btnTl);
            var clicked = ImGui.InvisibleButton("##wfUpsell", new Vector2(btnW, Px(30f)));
            HandOnHover();
            var hovered = ImGui.IsItemHovered();
            dl.AddRectFilled(btnTl, btnTl + new Vector2(btnW, Px(30f)),
                ImGui.GetColorU32(star with { W = hovered ? 0.30f : 0.18f }), Px(15f));
            IconDraw.Add(dl, FontAwesomeIcon.Star, Px(13f),
                new Vector2(btnTl.X + Px(14f), btnTl.Y + (Px(30f) - starSz.Y) * 0.5f),
                UiColors.FavoriteStar);
            dl.AddText(new Vector2(btnTl.X + Px(14f) + starSz.X + Px(8f), btnTl.Y + (Px(30f) - labelSz.Y) * 0.5f),
                ImGui.GetColorU32(UiColors.Body), label);
            if (hovered)
            {
                ImGui.SetTooltip(Loc.T("os.wf_upsell", 5));
            }
            if (clicked)
            {
                ctx.Shell.SendIntent("settings", OsIntents.CreateReturn(OsIntents.OpenSupporter, "wayfinder"));
            }
        }

        ImGui.SetCursorScreenPos(new Vector2(tl.X, tl.Y + cardH));
        ImGui.Dummy(new Vector2(0f, Px(8f)));
    }

    private void DrawAllDoneCard(WayfinderStateDto state, float winW)
    {
        var t = ThemeService.Current;
        var cardW = winW - Px(PadX) * 2f;
        ImGui.SetCursorPosX(Px(PadX));
        var tl = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        var text = state.TotalFound > 0 ? Loc.T("os.wf_all_found") : Loc.T("os.wf_none_available");
        var trophySz = IconDraw.Measure(FontAwesomeIcon.Trophy, Px(18f));
        var wrapW = cardW - Px(38f) - trophySz.X;
        var textSz = ImGui.CalcTextSize(text, false, wrapW);
        var cardH = Math.Max(textSz.Y + Px(24f), trophySz.Y + Px(24f));
        dl.AddRectFilled(tl, tl + new Vector2(cardW, cardH), ImGui.GetColorU32(t.Accent with { W = 0.10f }), Px(16f));
        IconDraw.Add(dl, FontAwesomeIcon.Trophy, Px(18f),
            new Vector2(tl.X + Px(14f), tl.Y + (cardH - trophySz.Y) * 0.5f),
            ImGui.GetColorU32(UiColors.FavoriteStar));
        DrawWrapped(dl, text,
            new Vector2(tl.X + Px(14f) + trophySz.X + Px(10f), tl.Y + (cardH - textSz.Y) * 0.5f),
            wrapW, UiColors.Body);
        ImGui.Dummy(new Vector2(0f, cardH + Px(8f)));
    }

    /// <summary>Entry into the party hunt, shown only while in a together party. The sub-line mirrors the
    /// run's state so a live hunt is reachable in one tap from the app's front door.</summary>
    private void DrawPartyCard(float winW)
    {
        var t = ThemeService.Current;
        var cardW = winW - Px(PadX) * 2f;
        var cardH = Px(58f);
        ImGui.SetCursorPosX(Px(PadX));
        var tl = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##wfPartyEntry", new Vector2(cardW, cardH));
        HandOnHover();
        var hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        var green = new Vector4(0.36f, 0.82f, 0.46f, 1f);
        var live = _host.PartyRun is { } run
            && (WayfinderRunStatus)run.Status is WayfinderRunStatus.Gathering or WayfinderRunStatus.Active;
        dl.AddRectFilled(tl, tl + new Vector2(cardW, cardH),
            ImGui.GetColorU32(green with { W = hovered ? 0.20f : live ? 0.14f : 0.08f }), Px(14f));
        dl.AddRect(tl, tl + new Vector2(cardW, cardH), ImGui.GetColorU32(green with { W = 0.45f }), Px(14f),
            ImDrawFlags.None, Px(1.2f));

        var iconPx = Px(18f);
        var iconSz = IconDraw.Measure(FontAwesomeIcon.UserFriends, iconPx);
        IconDraw.Add(dl, FontAwesomeIcon.UserFriends, iconPx,
            new Vector2(tl.X + Px(16f), tl.Y + (cardH - iconSz.Y) * 0.5f), ImGui.GetColorU32(green));
        var textX = tl.X + Px(16f) + iconSz.X + Px(12f);
        dl.AddText(new Vector2(textX, tl.Y + Px(10f)), ImGui.GetColorU32(UiColors.Body),
            Loc.T("os.wf_party_entry_title"));
        var sub = _host.PartyRun is { } current
            ? (WayfinderRunStatus)current.Status switch
            {
                WayfinderRunStatus.Gathering => Loc.T("os.wf_party_state_gathering"),
                WayfinderRunStatus.Active => Loc.T("os.wf_party_state_live"),
                _ => Loc.T("os.wf_party_state_results"),
            }
            : Loc.T("os.wf_party_entry_hint");
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.85f,
            new Vector2(textX, tl.Y + Px(14f) + ImGui.GetTextLineHeight()),
            ImGui.GetColorU32(live ? green : UiColors.Hint), sub);

        var chevronPx = Px(13f);
        var chevronSz = IconDraw.Measure(FontAwesomeIcon.ChevronRight, chevronPx);
        IconDraw.Add(dl, FontAwesomeIcon.ChevronRight, chevronPx,
            new Vector2(tl.X + cardW - chevronSz.X - Px(16f), tl.Y + (cardH - chevronSz.Y) * 0.5f),
            ImGui.GetColorU32(UiColors.Hint));

        ImGui.Dummy(new Vector2(0f, Px(8f)));
        if (clicked)
        {
            _openParty();
        }
    }

    /// <summary>The scout-only entry into waypoint authoring.</summary>
    private void DrawAddWaypointRow(float winW)
    {
        var t = ThemeService.Current;
        var cardW = winW - Px(PadX) * 2f;
        var cardH = Px(52f);
        ImGui.Dummy(new Vector2(0f, Px(4f)));
        ImGui.SetCursorPosX(Px(PadX));
        var tl = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##wfAddWaypoint", new Vector2(cardW, cardH));
        HandOnHover();
        var hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(tl, tl + new Vector2(cardW, cardH),
            ImGui.GetColorU32(t.Accent with { W = hovered ? 0.20f : 0.10f }), Px(14f));
        dl.AddRect(tl, tl + new Vector2(cardW, cardH), ImGui.GetColorU32(t.Accent with { W = 0.40f }), Px(14f),
            ImDrawFlags.None, Px(1.2f));

        var iconPx = Px(18f);
        var iconSz = IconDraw.Measure(FontAwesomeIcon.MapPin, iconPx);
        IconDraw.Add(dl, FontAwesomeIcon.MapPin, iconPx,
            new Vector2(tl.X + Px(16f), tl.Y + (cardH - iconSz.Y) * 0.5f), ImGui.GetColorU32(t.AccentLight));
        dl.AddText(new Vector2(tl.X + Px(16f) + iconSz.X + Px(12f), tl.Y + (cardH - ImGui.GetTextLineHeight()) * 0.5f),
            ImGui.GetColorU32(UiColors.Body), Loc.T("os.wf_create_entry"));

        var chevronPx = Px(13f);
        var chevronSz = IconDraw.Measure(FontAwesomeIcon.ChevronRight, chevronPx);
        IconDraw.Add(dl, FontAwesomeIcon.ChevronRight, chevronPx,
            new Vector2(tl.X + cardW - chevronSz.X - Px(16f), tl.Y + (cardH - chevronSz.Y) * 0.5f),
            ImGui.GetColorU32(UiColors.Hint));

        ImGui.Dummy(new Vector2(0f, Px(4f)));
        if (clicked)
        {
            _openCreate();
        }
    }

    private void DrawHistory(OsAppContext ctx, float winW)
    {
        ImGui.Dummy(new Vector2(0f, Px(10f)));
        ImGui.SetCursorPosX(Px(PadX));
        using (UiFonts.H3?.Push())
        {
            ImGui.TextColored(UiColors.Body, Loc.T("os.wf_history_title"));
        }
        ImGui.Dummy(new Vector2(0f, Px(4f)));

        var records = _history.Records;
        if (records.Count == 0)
        {
            DrawCenteredHint(Loc.T("os.wf_history_empty"), winW);
            return;
        }

        foreach (var record in records)
        {
            DrawHistoryRow(ctx, record, winW);
        }
    }

    private void DrawHistoryRow(OsAppContext ctx, WayfinderFoundRecord record, float winW)
    {
        var cardW = winW - Px(PadX) * 2f;
        var cardH = Px(56f);
        ImGui.SetCursorPosX(Px(PadX));
        var tl = ImGui.GetCursorScreenPos();
        var hasPicture = record.SelfiePath is { Length: > 0 };
        var clicked = false;
        var hovered = false;
        if (hasPicture)
        {
            clicked = ImGui.InvisibleButton($"##wfHistory{record.ChallengeId:N}", new Vector2(cardW, cardH));
            HandOnHover();
            hovered = ImGui.IsItemHovered();
        }
        else
        {
            ImGui.Dummy(new Vector2(cardW, cardH));
        }
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(tl, tl + new Vector2(cardW, cardH),
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, hovered ? 0.10f : 0.05f)), Px(14f));

        var thumb = Px(40f);
        var thumbTl = new Vector2(tl.X + Px(9f), tl.Y + (cardH - thumb) * 0.5f);
        var handle = record.SelfiePath is { } path ? ctx.Capabilities.Textures.Get(path) : null;
        if (handle is { } tex)
        {
            dl.AddImageRounded(tex, thumbTl, thumbTl + new Vector2(thumb, thumb), Vector2.Zero, Vector2.One,
                0xFFFFFFFFu, Px(10f));
        }
        else
        {
            dl.AddRectFilled(thumbTl, thumbTl + new Vector2(thumb, thumb),
                ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.06f)), Px(10f));
            var camPx = thumb * 0.45f;
            var camSz = IconDraw.Measure(FontAwesomeIcon.MapMarkerAlt, camPx);
            IconDraw.Add(dl, FontAwesomeIcon.MapMarkerAlt, camPx,
                thumbTl + new Vector2((thumb - camSz.X) * 0.5f, (thumb - camSz.Y) * 0.5f),
                ImGui.GetColorU32(UiColors.Hint));
        }

        var badgeW = Px(24f);
        var textX = thumbTl.X + thumb + Px(11f);
        // A fixed-height list row, so the name ellipsizes clear of the badge and the full text is on hover.
        var nameW = tl.X + cardW - badgeW - Px(22f) - textX;
        var fitted = TruncateToWidth(record.Name, nameW);
        dl.AddText(new Vector2(textX, tl.Y + Px(10f)), ImGui.GetColorU32(UiColors.Body), fitted);
        if (hovered && !string.Equals(fitted, record.Name, StringComparison.Ordinal))
        {
            ImGui.SetTooltip(record.Name);
        }
        dl.AddText(new Vector2(textX, tl.Y + Px(12f) + ImGui.GetTextLineHeight()), ImGui.GetColorU32(UiColors.Hint),
            Loc.T("os.wf_history_line", record.Attempts, FormatSpan(TimeSpan.FromSeconds(record.Seconds))));

        var badge = badgeW;
        ExpansionBadge.Draw(dl, new Vector2(tl.X + cardW - badge - Px(12f), tl.Y + (cardH - badge) * 0.5f),
            badge, record.Expansion);

        ImGui.Dummy(new Vector2(0f, Px(6f)));
        if (clicked && record.SelfiePath is { } open)
        {
            _viewerPath = open;
        }
    }

    /// <summary>A found place's own picture, in the same free-floating window the challenge shot uses.</summary>
    /// <summary>Offers a find's selfie to the share sheet as a picture, so it can go into a chat or a yap.</summary>
    internal static void ShareSelfie(OsAppContext ctx, string path)
    {
        ctx.Capabilities.Share.Offer(new ShareItem
        {
            Type = ShareTypes.Photo,
            LocalPath = path,
            Title = Loc.T("os.wf_share_title"),
            SourceAppId = "wayfinder",
        });
    }

    private void DrawHistoryViewer(OsAppContext ctx)
    {
        if (_viewerPath is not { } path || ctx.Capabilities.Textures.Get(path) is not { } tex)
        {
            return;
        }

        ImGui.SetNextWindowSize(new Vector2(Px(560f), Px(420f)), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(Px(220f), Px(160f)),
            new Vector2(float.MaxValue, float.MaxValue));

        var open = true;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(Px(6f), Px(6f)));
        if (ImGui.Begin($"{Loc.T("os.wf_image_viewer")}##wfHistoryViewer", ref open,
                ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            var avail = ImGui.GetContentRegionAvail();
            var shareH = ImGui.GetFrameHeight() + Px(6f);
            if (avail.X > 0f && avail.Y > shareH)
            {
                ImGui.Image(tex, new Vector2(avail.X, avail.Y - shareH));
            }
            if (ModalUi.Button($"{Loc.T("os.wf_share")}##wfViewerShare", avail.X))
            {
                ShareSelfie(ctx, path);
            }
            if (ImGui.IsWindowFocused() && ImGui.IsKeyPressed(ImGuiKey.Escape))
            {
                open = false;
            }
        }
        ImGui.End();
        ImGui.PopStyleVar();

        if (!open)
        {
            _viewerPath = null;
        }
    }

    private string? _viewerPath;

    private void DrawRetry(float winW)
    {
        ImGui.Dummy(new Vector2(0f, Px(10f)));
        var btnW = Px(150f);
        ImGui.SetCursorPosX((winW - btnW) * 0.5f);
        if (ModalUi.Button(Loc.T("os.wf_retry"), btnW))
        {
            Refresh();
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

    private static void DrawWrapped(ImDrawListPtr dl, string text, Vector2 tl, float wrapW, Vector4 color)
    {
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), tl, ImGui.GetColorU32(color), text, wrapW);
    }

    /// <summary>The server counts the daily cap from UTC midnight, so that is when new challenges land.</summary>
    private static DateTime NextResetUtc() => DateTime.UtcNow.Date.AddDays(1);

    internal static string FormatSpan(TimeSpan span)
    {
        if (span.TotalHours >= 1)
        {
            return $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}";
        }
        return $"{span.Minutes}:{span.Seconds:00}";
    }
}
