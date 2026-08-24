using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using AetherLove.Widgets;
using AetherOS.Sdk;
using AetherLove.Shared;
using AetherLove.Shared.Wayfinder;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;

namespace AetherOS.Apps.Wayfinder;

/// <summary>The party hunt: the host opens a gathering, everyone on the host's world joins, and one shared
/// challenge is hunted together. Coordinates and worlds are captured plugin-side; this screen only ever sees
/// the run snapshot and verdict bands.</summary>
internal sealed class PartyScreen
{
    private const float PadX = 16f;

    private static string CacheDir =>
        Path.Combine(UiHost.PluginInterface.ConfigDirectory.FullName, "WayfinderCache");

    private readonly IWayfinderHost _host;
    private readonly WayfinderHistory _history;
    private readonly Action _backToHome;
    private readonly EntranceAnimation _entrance = new();
    private readonly ConfettiBurst _confetti = new();
    private readonly ConcurrentQueue<Action> _uiActions = new();

    private volatile bool _busy;
    private volatile string? _error;
    private bool _wrongWorldOpen;
    private bool _travelSent;
    private WayfinderPartyRunDto? _seenRun;
    private DateTimeOffset _runSeenAt = DateTimeOffset.UtcNow;
    private ISharedImmediateTexture? _imageTex;
    private Guid _imageChallengeId;
    private WayfinderVerdict? _myVerdict;
    private double _myVerdictAt;
    private bool _myWorldWrong;
    private bool _iFound;
    private double _foundAt;
    private string? _selfiePath;
    private Guid _historyStamped;

    public PartyScreen(IWayfinderHost host, WayfinderHistory history, Action backToHome)
    {
        _host = host;
        _history = history;
        _backToHome = backToHome;
    }

    public void OnShow()
    {
        _entrance.Arm();
        _busy = false;
        _error = null;
        _wrongWorldOpen = false;
        _travelSent = false;
        _myVerdict = null;
        _myWorldWrong = false;
        _iFound = false;
        _selfiePath = null;
        Refresh();
    }

    private void Refresh()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await _host.RefreshPartyRunAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                UiHost.Log.Debug(ex, "[Wayfinder] Party run refresh failed.");
            }
        });
    }

    public void Draw(OsAppContext ctx)
    {
        while (_uiActions.TryDequeue(out var action))
        {
            action();
        }
        _entrance.BeginFrame();

        var winW = ImGui.GetWindowSize().X;
        var winH = ImGui.GetWindowSize().Y;
        var party = ctx.Capabilities.Party;
        var run = _host.PartyRun;
        if (run is not null && party.PartyId is { } partyId && run.PartyId != partyId)
        {
            run = null;
        }
        TrackRun(run);

        DrawHeader(winW);

        if (!party.InParty)
        {
            DrawCenteredHint(Loc.T("os.wf_party_no_party"), winW);
            _entrance.EndFrame();
            return;
        }

        if (run is null)
        {
            DrawIdle(party, winW);
        }
        else
        {
            switch ((WayfinderRunStatus)run.Status)
            {
                case WayfinderRunStatus.Gathering:
                    DrawGathering(ctx, party, run, winW, winH);
                    break;
                case WayfinderRunStatus.Active:
                    DrawActive(ctx, party, run, winW, winH);
                    break;
                default:
                    DrawResults(party, run, winW, winH);
                    break;
            }
        }

        if (_error is { } error)
        {
            ImGui.Dummy(new Vector2(0f, Px(6f)));
            ImGui.SetCursorPosX(Px(PadX));
            ImGui.PushTextWrapPos(winW - Px(PadX));
            ImGui.TextColored(UiColors.Danger, error);
            ImGui.PopTextWrapPos();
        }

        DrawWrongWorldPopup(ctx, winW, winH);
        _entrance.EndFrame();
    }

    /// <summary>Remembers when the current snapshot arrived so the countdown keeps moving between pushes,
    /// and re-arms the celebration when a fresh hunt replaces a finished one.</summary>
    private void TrackRun(WayfinderPartyRunDto? run)
    {
        if (ReferenceEquals(run, _seenRun))
        {
            return;
        }
        if (run is not null && (_seenRun is null || _seenRun.RunId != run.RunId))
        {
            _myVerdict = null;
            _myWorldWrong = false;
            _iFound = false;
            _travelSent = false;
            _selfiePath = null;
        }
        _seenRun = run;
        _runSeenAt = DateTimeOffset.UtcNow;
        if (run?.ImageBytes is { Length: > 0 } bytes && run.ChallengeId is { } challengeId
            && challengeId != _imageChallengeId)
        {
            _imageTex = AvatarDiskCache.Store(CacheDir, $"run_{challengeId:N}", bytes);
            _imageChallengeId = challengeId;
        }
    }

    private void DrawHeader(float winW)
    {
        ImGui.Dummy(new Vector2(0f, Px(10f)));
        var dl = ImGui.GetWindowDrawList();
        ImGui.SetCursorPosX(Px(PadX));
        var backH = ImGui.GetTextLineHeight() + Px(10f);
        var tl = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##wfPartyBack", new Vector2(backH, backH));
        HandOnHover();
        var hovered = ImGui.IsItemHovered();
        dl.AddCircleFilled(tl + new Vector2(backH * 0.5f, backH * 0.5f), backH * 0.5f,
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, hovered ? 0.14f : 0.07f)));
        var arrowPx = ImGui.GetFontSize() * 0.7f;
        var arrowSz = IconDraw.Measure(FontAwesomeIcon.ArrowLeft, arrowPx);
        IconDraw.Add(dl, FontAwesomeIcon.ArrowLeft, arrowPx,
            tl + new Vector2((backH - arrowSz.X) * 0.5f, (backH - arrowSz.Y) * 0.5f),
            ImGui.GetColorU32(UiColors.Body));
        if (clicked)
        {
            _backToHome();
        }

        ImGui.SameLine();
        using (UiFonts.H3?.Push())
        {
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (backH - ImGui.GetTextLineHeight()) * 0.5f);
            ImGui.TextColored(ThemeService.Current.AccentLight, Loc.T("os.wf_party_title"));
        }
        ImGui.Dummy(new Vector2(0f, Px(8f)));
    }

    /// <summary>No hunt yet: the host can open a gathering, everyone else waits for them.</summary>
    private void DrawIdle(IPartyState party, float winW)
    {
        DrawCenteredHint(Loc.T(party.AmHost ? "os.wf_party_idle_host" : "os.wf_party_idle_member"), winW);
        if (!party.AmHost)
        {
            return;
        }
        ImGui.Dummy(new Vector2(0f, Px(14f)));
        ImGui.SetCursorPosX(Px(PadX));
        if (ModalUi.Button(_busy ? Loc.T("os.wf_party_starting") : Loc.T("os.wf_party_start"), winW - Px(PadX) * 2f)
            && !_busy)
        {
            RunAction(async () => await _host.StartPartyGatherAsync().ConfigureAwait(false));
        }
    }

    private void DrawGathering(OsAppContext ctx, IPartyState party, WayfinderPartyRunDto run, float winW, float winH)
    {
        var me = FindMe(party, run);
        var joined = CountJoined(run);
        var world = _host.WorldName(run.HostWorldId) ?? $"#{run.HostWorldId}";

        ImGui.SetCursorPosX(Px(PadX));
        using (UiFonts.H3?.Push())
        {
            ImGui.TextColored(UiColors.Body, Loc.T("os.wf_party_gather_title"));
        }
        ImGui.SetCursorPosX(Px(PadX));
        ImGui.PushTextWrapPos(winW - Px(PadX));
        ImGui.TextColored(UiColors.Hint, Loc.T("os.wf_party_gather_hint", world));
        ImGui.PopTextWrapPos();
        ImGui.Dummy(new Vector2(0f, Px(10f)));

        DrawRoster(party, run, winW, gathering: true);
        ImGui.Dummy(new Vector2(0f, Px(12f)));

        ImGui.SetCursorPosX(Px(PadX));
        var btnW = winW - Px(PadX) * 2f;
        if (me is not { Joined: true })
        {
            if (ModalUi.Button(_busy ? Loc.T("os.wf_party_joining") : Loc.T("os.wf_party_join"), btnW) && !_busy)
            {
                Join(run);
            }
        }
        else if (!party.AmHost)
        {
            DrawCenteredHint(Loc.T("os.wf_party_joined"), winW);
        }

        if (party.AmHost)
        {
            ImGui.Dummy(new Vector2(0f, Px(8f)));
            ImGui.SetCursorPosX(Px(PadX));
            if (joined >= 2)
            {
                if (ModalUi.Button(_busy ? Loc.T("os.wf_party_beginning") : Loc.T("os.wf_party_begin"), btnW) && !_busy)
                {
                    RunAction(async () => await _host.BeginPartyRunAsync(run.RunId).ConfigureAwait(false));
                }
            }
            else
            {
                DrawCenteredHint(Loc.T("os.wf_party_begin_need"), winW);
            }
            ImGui.Dummy(new Vector2(0f, Px(8f)));
            DrawLinkCentered("##wfPartyCancel", Loc.T("os.wf_party_cancel"), winW, () =>
                RunAction(async () =>
                {
                    await _host.CancelPartyRunAsync(run.RunId).ConfigureAwait(false);
                    _uiActions.Enqueue(_backToHome);
                }));
        }
    }

    private void DrawActive(OsAppContext ctx, IPartyState party, WayfinderPartyRunDto run, float winW, float winH)
    {
        var me = FindMe(party, run);
        var spectating = me?.AssignmentId is null;

        if (_iFound)
        {
            DrawFoundCelebration(ctx, run, winW, winH);
            return;
        }

        DrawPicture(winW);

        // The expansion badge, which the party screen never drew even though the solo challenge page has
        // always had one: it is the only clue to WHERE the picture might be, and a party hunt needs it more
        // than the solo one, since nobody can look it up for you.
        var badge = Px(26f);
        var titleTop = ImGui.GetCursorScreenPos().Y;
        ImGui.SetCursorPosX(Px(PadX));
        using (UiFonts.H3?.Push())
        {
            ImGui.PushTextWrapPos(winW - Px(PadX) - badge - Px(8f));
            ImGui.TextColored(UiColors.Body, run.ChallengeName ?? string.Empty);
            ImGui.PopTextWrapPos();
        }
        ExpansionBadge.Draw(ImGui.GetWindowDrawList(),
            new Vector2(ImGui.GetWindowPos().X + winW - Px(PadX) - badge, titleTop), badge, run.Expansion);

        DrawTimerRow(run, winW);
        DrawRoster(party, run, winW, gathering: false);
        ImGui.Dummy(new Vector2(0f, Px(6f)));

        ImGui.SetCursorPosX(Px(PadX));
        ImGui.PushTextWrapPos(winW - Px(PadX));
        ImGui.TextColored(ThemeService.Current.AccentLight,
            Loc.T("os.wf_party_found_line", run.FoundCount, run.ParticipantCount));
        ImGui.PopTextWrapPos();

        if (_myWorldWrong)
        {
            var world = _host.WorldName(run.HostWorldId) ?? $"#{run.HostWorldId}";
            ImGui.Dummy(new Vector2(0f, Px(4f)));
            ImGui.SetCursorPosX(Px(PadX));
            ImGui.PushTextWrapPos(winW - Px(PadX));
            ImGui.TextColored(UiColors.WarningAccent, Loc.T("os.wf_party_submit_world", world));
            ImGui.PopTextWrapPos();
        }
        DrawVerdictBanner(winW);

        // The host can end a running hunt: without it a party that has given up is stuck staring at the
        // timer, and only the host can free everyone's daily start for the next one.
        if (party.AmHost)
        {
            ImGui.SetCursorPos(new Vector2(0f, winH - Px(88f)));
            DrawLinkCentered("##wfPartyGiveUp", Loc.T("os.wf_party_give_up"), winW, () =>
                RunAction(async () =>
                {
                    await _host.CancelPartyRunAsync(run.RunId).ConfigureAwait(false);
                    _uiActions.Enqueue(_backToHome);
                }));
        }

        if (spectating)
        {
            ImGui.SetCursorPos(new Vector2(0f, winH - Px(48f)));
            DrawCenteredHint(Loc.T("os.wf_party_spectating"), winW);
            return;
        }

        ImGui.SetCursorPos(new Vector2(Px(PadX), winH - Px(58f)));
        if (_busy)
        {
            var center = new Vector2(ImGui.GetWindowPos().X + winW * 0.5f, ImGui.GetCursorScreenPos().Y + Px(18f));
            LoadingSpinner.Draw(center, Px(12f), Px(3f), ImGui.GetColorU32(ThemeService.Current.Accent));
        }
        else if (ModalUi.Button(Loc.T("os.wf_selfie"), winW - Px(PadX) * 2f) && me?.AssignmentId is { } assignmentId)
        {
            TakeSelfie(ctx, run, assignmentId);
        }
    }

    private void DrawResults(IPartyState party, WayfinderPartyRunDto run, float winW, float winH)
    {
        var status = (WayfinderRunStatus)run.Status;
        ImGui.Dummy(new Vector2(0f, Px(8f)));
        ImGui.SetCursorPosX(Px(PadX));
        using (UiFonts.H3?.Push())
        {
            var title = status switch
            {
                WayfinderRunStatus.Completed => Loc.T("os.wf_party_results_title"),
                WayfinderRunStatus.Expired => Loc.T("os.wf_party_expired"),
                _ => Loc.T("os.wf_party_cancelled"),
            };
            ImGui.TextColored(status == WayfinderRunStatus.Completed ? UiColors.LiveGreen : UiColors.WarningAccent,
                title);
        }
        ImGui.Dummy(new Vector2(0f, Px(6f)));
        if (status != WayfinderRunStatus.Cancelled)
        {
            // Wrapped against the PAGE's right margin, not the window edge, and each paragraph set from the
            // left margin: pushing the wrap to winW let long lines run under the bezel, and the hint picked
            // up whatever X the line above had ended on.
            ImGui.SetCursorPosX(Px(PadX));
            ImGui.PushTextWrapPos(winW - Px(PadX));
            ImGui.TextColored(UiColors.Body, run.FoundCount > 0
                ? Loc.T("os.wf_party_results_found", run.FoundCount, run.ParticipantCount)
                : Loc.T("os.wf_party_results_none"));
            ImGui.PopTextWrapPos();
            if (run.FoundCount > 1)
            {
                ImGui.Dummy(new Vector2(0f, Px(3f)));
                ImGui.SetCursorPosX(Px(PadX));
                ImGui.PushTextWrapPos(winW - Px(PadX));
                ImGui.TextColored(UiColors.Hint, Loc.T("os.wf_party_bonus_hint"));
                ImGui.PopTextWrapPos();
            }
        }
        ImGui.Dummy(new Vector2(0f, Px(10f)));
        if (status != WayfinderRunStatus.Cancelled)
        {
            DrawRoster(party, run, winW, gathering: false);
        }

        var btnW = winW - Px(PadX) * 2f;
        if (party.AmHost)
        {
            ImGui.SetCursorPos(new Vector2(Px(PadX), winH - Px(104f)));
            if (ModalUi.Button(_busy ? Loc.T("os.wf_party_starting") : Loc.T("os.wf_party_again"), btnW) && !_busy)
            {
                RunAction(async () =>
                {
                    _host.DismissPartyResults();
                    await _host.StartPartyGatherAsync().ConfigureAwait(false);
                });
            }
        }
        ImGui.SetCursorPos(new Vector2(Px(PadX), winH - Px(58f)));
        if (ModalUi.Button(Loc.T("os.wf_party_done"), btnW))
        {
            _host.DismissPartyResults();
            _backToHome();
        }
    }

    /// <summary>One row per party member: joined state while gathering, verdict warmth while hunting.</summary>
    private void DrawRoster(IPartyState party, WayfinderPartyRunDto run, float winW, bool gathering)
    {
        var dl = ImGui.GetWindowDrawList();
        var rowH = Px(26f);
        var byAccount = new Dictionary<Guid, WayfinderRunMemberDto>(run.Members.Length);
        foreach (var member in run.Members)
        {
            byAccount[member.AccountId] = member;
        }

        foreach (var person in party.Members)
        {
            byAccount.TryGetValue(person.AccountId, out var seat);
            ImGui.SetCursorPosX(Px(PadX));
            var tl = ImGui.GetCursorScreenPos();
            var joined = seat is { Joined: true };
            var alpha = joined ? 0.92f : 0.45f;

            var (icon, color) = RosterBadge(seat, gathering);
            IconDraw.AddCentered(dl, icon, Px(11f), new Vector2(tl.X + Px(9f), tl.Y + rowH * 0.5f),
                ImGui.GetColorU32(color));
            dl.AddText(new Vector2(tl.X + Px(24f), tl.Y + (rowH - ImGui.GetTextLineHeight()) * 0.5f),
                ImGui.GetColorU32(UiColors.Body with { W = alpha }), person.Name);
            if (person.IsHost)
            {
                IconDraw.AddCentered(dl, FontAwesomeIcon.Crown, Px(9f),
                    new Vector2(tl.X + Px(28f) + ImGui.CalcTextSize(person.Name).X + Px(8f), tl.Y + rowH * 0.5f),
                    ImGui.GetColorU32(new Vector4(0.94f, 0.75f, 0.3f, 0.9f)));
            }
            ImGui.Dummy(new Vector2(0f, rowH));
        }
    }

    private static (FontAwesomeIcon Icon, Vector4 Color) RosterBadge(WayfinderRunMemberDto? seat, bool gathering)
    {
        if (seat is not { Joined: true })
        {
            return (FontAwesomeIcon.Circle, UiColors.Hint with { W = 0.35f });
        }
        if (gathering)
        {
            return (FontAwesomeIcon.CheckCircle, UiColors.LiveGreen);
        }
        if (seat.Found)
        {
            return (FontAwesomeIcon.CheckCircle, UiColors.LiveGreen);
        }
        return seat.BestVerdict is { } verdict
            ? ((WayfinderVerdict)verdict) switch
            {
                WayfinderVerdict.VeryClose => (FontAwesomeIcon.Fire, new Vector4(0.98f, 0.45f, 0.30f, 1f)),
                WayfinderVerdict.Close => (FontAwesomeIcon.Sun, new Vector4(0.98f, 0.72f, 0.35f, 1f)),
                WayfinderVerdict.Far => (FontAwesomeIcon.Snowflake, new Vector4(0.45f, 0.70f, 0.95f, 1f)),
                _ => (FontAwesomeIcon.Map, new Vector4(0.62f, 0.68f, 0.95f, 1f)),
            }
            : (FontAwesomeIcon.Compass, UiColors.Hint);
    }

    private void DrawPicture(float winW)
    {
        var cardW = winW - Px(PadX) * 2f;
        var cardH = cardW * 0.55f;
        ImGui.SetCursorPosX(Px(PadX));
        var tl = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        var wrap = _imageTex?.GetWrapOrDefault();
        if (wrap is not null)
        {
            var texW = (float)wrap.Width;
            var texH = (float)wrap.Height;
            var scale = Math.Max(cardW / texW, cardH / texH);
            var visW = cardW / (texW * scale);
            var visH = cardH / (texH * scale);
            var uv0 = new Vector2((1f - visW) * 0.5f, (1f - visH) * 0.5f);
            var uv1 = new Vector2(1f - uv0.X, 1f - uv0.Y);
            dl.AddImageRounded(wrap.Handle, tl, tl + new Vector2(cardW, cardH), uv0, uv1, 0xFFFFFFFFu, Px(16f));
        }
        else
        {
            dl.AddRectFilled(tl, tl + new Vector2(cardW, cardH), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.05f)), Px(16f));
            var iconSz = IconDraw.Measure(FontAwesomeIcon.Image, Px(30f));
            IconDraw.Add(dl, FontAwesomeIcon.Image, Px(30f),
                tl + new Vector2((cardW - iconSz.X) * 0.5f, (cardH - iconSz.Y) * 0.5f),
                ImGui.GetColorU32(UiColors.Hint));
        }
        dl.AddRect(tl, tl + new Vector2(cardW, cardH), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.12f)), Px(16f),
            ImDrawFlags.None, Px(1.2f));
        ImGui.Dummy(new Vector2(0f, cardH + Px(8f)));
    }

    private void DrawTimerRow(WayfinderPartyRunDto run, float winW)
    {
        var remaining = TimeSpan.FromSeconds(Math.Max(0,
            run.RemainingSeconds - (int)(DateTimeOffset.UtcNow - _runSeenAt).TotalSeconds));
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var low = remaining < TimeSpan.FromMinutes(5);
        ImGui.SetCursorPosX(Px(PadX));
        var tl = ImGui.GetCursorScreenPos();
        var clockPx = ImGui.GetFontSize() * 0.85f;
        var clockSz = IconDraw.Measure(FontAwesomeIcon.HourglassHalf, clockPx);
        var timeColor = low ? UiColors.WarningAccent : t.AccentLight;
        IconDraw.Add(dl, FontAwesomeIcon.HourglassHalf, clockPx, tl + new Vector2(0f, Px(1f)),
            ImGui.GetColorU32(timeColor));
        dl.AddText(new Vector2(tl.X + clockSz.X + Px(8f), tl.Y), ImGui.GetColorU32(timeColor),
            HomeScreen.FormatSpan(remaining));
        ImGui.Dummy(new Vector2(0f, ImGui.GetTextLineHeight() + Px(8f)));
    }

    private void DrawVerdictBanner(float winW)
    {
        if (_myVerdict is not { } verdict || verdict == WayfinderVerdict.Found)
        {
            return;
        }
        var (text, color) = verdict switch
        {
            WayfinderVerdict.WrongZone => (Loc.T("os.wf_v_wrongzone"), new Vector4(0.62f, 0.68f, 0.95f, 1f)),
            WayfinderVerdict.Far => (Loc.T("os.wf_v_far"), new Vector4(0.45f, 0.70f, 0.95f, 1f)),
            WayfinderVerdict.Close => (Loc.T("os.wf_v_close"), new Vector4(0.98f, 0.72f, 0.35f, 1f)),
            _ => (Loc.T("os.wf_v_veryclose"), new Vector4(0.98f, 0.45f, 0.30f, 1f)),
        };
        var slide = AccessibilityService.ReduceMotion
            ? 1f
            : (float)Math.Clamp((ImGui.GetTime() - _myVerdictAt) / 0.25, 0.0, 1.0);
        var eased = 1f - (1f - slide) * (1f - slide);
        var cardW = winW - Px(PadX) * 2f;
        var textSz = ImGui.CalcTextSize(text, false, cardW - Px(48f));
        var cardH = textSz.Y + Px(16f);
        ImGui.Dummy(new Vector2(0f, Px(4f)));
        ImGui.SetCursorPosX(Px(PadX));
        var tl = ImGui.GetCursorScreenPos() + new Vector2((1f - eased) * Px(18f), 0f);
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(tl, tl + new Vector2(cardW, cardH), ImGui.GetColorU32(color with { W = 0.14f * eased }), Px(12f));
        dl.AddRect(tl, tl + new Vector2(cardW, cardH), ImGui.GetColorU32(color with { W = 0.55f * eased }), Px(12f),
            ImDrawFlags.None, Px(1.2f));
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), new Vector2(tl.X + Px(12f), tl.Y + Px(8f)),
            ImGui.GetColorU32(UiColors.Body with { W = eased }), text, cardW - Px(48f));
        ImGui.Dummy(new Vector2(0f, cardH + Px(4f)));
    }

    private void DrawFoundCelebration(OsAppContext ctx, WayfinderPartyRunDto run, float winW, float winH)
    {
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var winPos = ImGui.GetWindowPos();
        var still = AccessibilityService.ReduceMotion;
        var age = still ? 99f : (float)Math.Max(0.0, ImGui.GetTime() - _foundAt);
        var green = UiColors.LiveGreen;
        var center = winPos + new Vector2(winW * 0.5f, Px(110f));

        var wash = ImGui.GetColorU32(green with { W = 0.14f });
        dl.AddRectFilledMultiColor(winPos, winPos + new Vector2(winW, winH * 0.5f), wash, wash, 0u, 0u);
        var radius = Px(42f);
        dl.AddCircleFilled(center, radius, ImGui.GetColorU32(green with { W = 0.22f }), 64);
        dl.AddCircle(center, radius, ImGui.GetColorU32(green), 64, Px(2f));
        IconDraw.AddCentered(dl, FontAwesomeIcon.Check, radius * 0.9f, center, ImGui.GetColorU32(green));

        ImGui.Dummy(new Vector2(0f, Px(160f)));
        using (UiFonts.H1?.Push())
        {
            var title = Loc.T("os.wf_found_title");
            var sz = ImGui.CalcTextSize(title);
            ImGui.SetCursorPosX((winW - sz.X) * 0.5f);
            ImGui.TextColored(t.AccentLight, title);
        }
        var body = Loc.T("os.wf_party_found_line", run.FoundCount, run.ParticipantCount);
        var bodySz = ImGui.CalcTextSize(body);
        ImGui.SetCursorPosX((winW - bodySz.X) * 0.5f);
        ImGui.TextColored(UiColors.Body, body);

        if (_selfiePath is { } selfie)
        {
            // The picture itself, the way the solo page shows it; a find without its photo reads as a miss.
            ImGui.Dummy(new Vector2(0f, Px(14f)));
            var thumb = Px(104f);
            var thumbTl = new Vector2(winPos.X + (winW - thumb) * 0.5f, ImGui.GetCursorScreenPos().Y);
            if (ctx.Capabilities.Textures.Get(selfie) is { } tex)
            {
                dl.AddImageRounded(tex, thumbTl, thumbTl + new Vector2(thumb, thumb), Vector2.Zero, Vector2.One,
                    0xFFFFFFFFu, Px(16f));
                dl.AddRect(thumbTl, thumbTl + new Vector2(thumb, thumb),
                    ImGui.GetColorU32(UiColors.LiveGreen with { W = 0.75f }), Px(16f), ImDrawFlags.None, Px(1.6f));
                ImGui.Dummy(new Vector2(0f, thumb + Px(10f)));
            }
            var note = Loc.T("os.wf_found_saved");
            var noteSz = ImGui.CalcTextSize(note);
            ImGui.SetCursorPosX((winW - noteSz.X) * 0.5f);
            ImGui.TextColored(UiColors.Hint, note);
        }

        ImGui.SetCursorPos(new Vector2(Px(PadX), winH - Px(58f)));
        if (_selfiePath is { } shareable)
        {
            var half = (winW - Px(PadX) * 2f - Px(10f)) * 0.5f;
            if (ModalUi.Button($"{Loc.T("os.wf_share")}##wfPartyShare", half))
            {
                HomeScreen.ShareSelfie(ctx, shareable);
            }
            ImGui.SameLine(0f, Px(10f));
            if (ModalUi.Button(Loc.T("os.wf_party_wait_rest"), half))
            {
                _iFound = false;
            }
        }
        else if (ModalUi.Button(Loc.T("os.wf_party_wait_rest"), winW - Px(PadX) * 2f))
        {
            _iFound = false;
        }

        if (!still)
        {
            _confetti.Draw(winPos, winPos + new Vector2(winW, winH));
        }
    }

    /// <summary>The wrong-world explainer, with a one-tap teleport when a travel provider is installed.</summary>
    private void DrawWrongWorldPopup(OsAppContext ctx, float winW, float winH)
    {
        if (!_wrongWorldOpen)
        {
            return;
        }
        var run = _host.PartyRun;
        if (run is null)
        {
            _wrongWorldOpen = false;
            return;
        }
        var world = _host.WorldName(run.HostWorldId) ?? $"#{run.HostWorldId}";
        var t = ThemeService.Current;
        var winPos = ImGui.GetWindowPos();
        var dl = ImGui.GetWindowDrawList();
        var travel = ctx.Capabilities.Travel;
        var offerTravel = travel.IsAvailable && travel.ProviderName is { } provider;

        var panelW = winW - Px(PadX) * 2f;
        var bodyWrap = panelW - Px(32f);
        var body = Loc.T("os.wf_party_wrong_world_body", world);
        var bodySz = ImGui.CalcTextSize(body, false, bodyWrap);
        var panelH = Px(118f) + bodySz.Y + (offerTravel ? Px(46f) : 0f) + (_travelSent ? Px(24f) : 0f);
        var panelTL = new Vector2(winPos.X + Px(PadX), winPos.Y + (winH - panelH) * 0.5f);

        dl.AddRectFilled(winPos, winPos + new Vector2(winW, winH), ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.62f)));
        dl.AddRectFilled(panelTL, panelTL + new Vector2(panelW, panelH),
            ImGui.GetColorU32(new Vector4(0.12f, 0.11f, 0.15f, 0.99f)), Px(18f));
        dl.AddRect(panelTL, panelTL + new Vector2(panelW, panelH), ImGui.GetColorU32(t.Accent with { W = 0.35f }),
            Px(18f), ImDrawFlags.None, Px(1.2f));

        using (UiFonts.H3?.Push())
        {
            var title = Loc.T("os.wf_party_wrong_world_title");
            var titleSz = ImGui.CalcTextSize(title);
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
                new Vector2(panelTL.X + (panelW - titleSz.X) * 0.5f, panelTL.Y + Px(16f)),
                ImGui.GetColorU32(UiColors.WarningAccent), title);
        }
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
            new Vector2(panelTL.X + Px(16f), panelTL.Y + Px(48f)),
            ImGui.GetColorU32(UiColors.Body), body, bodyWrap);

        var y = panelTL.Y + Px(52f) + bodySz.Y;
        if (_travelSent)
        {
            dl.AddText(new Vector2(panelTL.X + Px(16f), y), ImGui.GetColorU32(UiColors.LiveGreen),
                Loc.T("os.wf_party_travel_sent"));
            y += Px(24f);
        }

        if (offerTravel)
        {
            ImGui.SetCursorScreenPos(new Vector2(panelTL.X + Px(16f), y + Px(4f)));
            var travelLabel = Loc.T("os.wf_party_travel", travel.ProviderName!);
            if (ModalUi.Button(travelLabel, panelW - Px(32f)) && !travel.IsBusy)
            {
                _travelSent = travel.GoToWorld(world);
                if (_travelSent)
                {
                    // The trip is under way and takes a while; leaving the explainer up over it just puts a
                    // scrim on the phone for the whole flight.
                    _wrongWorldOpen = false;
                }
            }
            y += Px(46f);
        }

        ImGui.SetCursorScreenPos(new Vector2(panelTL.X + Px(16f), panelTL.Y + panelH - Px(44f)));
        var close = ModalUi.Button(Loc.T("os.wf_party_wrong_world_close"), panelW - Px(32f));

        ImGui.SetCursorScreenPos(winPos);
        var scrim = ImGui.InvisibleButton("##wfWrongWorldScrim", new Vector2(winW, winH));
        var outside = scrim && !ImGui.IsMouseHoveringRect(panelTL, panelTL + new Vector2(panelW, panelH));
        if (close || outside)
        {
            _wrongWorldOpen = false;
        }
    }

    private static WayfinderRunMemberDto? FindMe(IPartyState party, WayfinderPartyRunDto run)
    {
        if (party.OwnAccountId is not { } id)
        {
            return null;
        }
        foreach (var member in run.Members)
        {
            if (member.AccountId == id)
            {
                return member;
            }
        }
        return null;
    }

    private void Join(WayfinderPartyRunDto run)
    {
        _busy = true;
        _error = null;
        _ = Task.Run(async () =>
        {
            try
            {
                await _host.JoinPartyRunAsync(run.RunId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains(HubErrors.WayfinderRunWrongWorld, StringComparison.Ordinal))
                {
                    _uiActions.Enqueue(() =>
                    {
                        _wrongWorldOpen = true;
                        _travelSent = false;
                    });
                }
                else
                {
                    var message = HubErrorText.Localize(ex);
                    _uiActions.Enqueue(() => _error = message);
                }
            }
            finally
            {
                _busy = false;
            }
        });
    }

    private void TakeSelfie(OsAppContext ctx, WayfinderPartyRunDto run, Guid assignmentId)
    {
        ctx.Capabilities.Camera.Capture(new CameraRequest(1f, 128, FreeForm: true),
            shot => _uiActions.Enqueue(() => Submit(run, assignmentId, shot.Path, shot.Crop)));
    }

    private void Submit(WayfinderPartyRunDto run, Guid assignmentId, string shotPath, Vector4 crop)
    {
        _busy = true;
        _error = null;
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await _host.SubmitPartyAttemptAsync(assignmentId).ConfigureAwait(false);
                _uiActions.Enqueue(() => ApplyResult(run, result, shotPath, crop));
            }
            catch (Exception ex)
            {
                var message = HubErrorText.Localize(ex);
                _uiActions.Enqueue(() =>
                {
                    _error = message;
                    DeleteQuietly(shotPath);
                });
            }
            finally
            {
                _busy = false;
            }
        });
    }

    private void ApplyResult(WayfinderPartyRunDto run, WayfinderGroupSubmitResultDto result, string shotPath, Vector4 crop)
    {
        _myVerdict = (WayfinderVerdict)result.Verdict;
        _myVerdictAt = ImGui.GetTime();
        _myWorldWrong = !result.WorldOk && (WayfinderVerdict)result.Verdict == WayfinderVerdict.Found;

        if (result.Found)
        {
            _confetti.Reset();
            _iFound = true;
            _foundAt = ImGui.GetTime();
            _selfiePath = _host.SaveSelfie(shotPath, crop);
            if (run.ChallengeId is { } challengeId && _historyStamped != challengeId)
            {
                _historyStamped = challengeId;
                _history.Add(new WayfinderFoundRecord(
                    challengeId,
                    run.ChallengeName ?? string.Empty,
                    run.Expansion,
                    DateTime.UtcNow,
                    result.AttemptCount,
                    result.SecondsToFind ?? 0,
                    _selfiePath));
            }
        }
        DeleteQuietly(shotPath);
    }

    private void RunAction(Func<Task> action)
    {
        _busy = true;
        _error = null;
        _ = Task.Run(async () =>
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var message = HubErrorText.Localize(ex);
                _uiActions.Enqueue(() => _error = message);
            }
            finally
            {
                _busy = false;
            }
        });
    }

    private static int CountJoined(WayfinderPartyRunDto run)
    {
        var joined = 0;
        foreach (var member in run.Members)
        {
            if (member.Joined)
            {
                joined++;
            }
        }
        return joined;
    }

    private void DrawLinkCentered(string id, string label, float winW, Action onClick)
    {
        var sz = ImGui.CalcTextSize(label);
        ImGui.SetCursorPosX((winW - sz.X) * 0.5f);
        var tl = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(id, sz);
        HandOnHover();
        var hovered = ImGui.IsItemHovered();
        ImGui.GetWindowDrawList().AddText(tl,
            ImGui.GetColorU32(hovered ? UiColors.Body : UiColors.Hint), label);
        if (clicked && !_busy)
        {
            onClick();
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

    private static void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
    }
}
