using System;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove.Shared.Aetherling;
using AetherLove.Shared.Racing;
using AetherLove.UI;
using AetherOS.Apps.Racer.Rendering;
using AetherOS.PetKit.Engine;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Racer.Screens;

internal sealed partial class HomeScreen(
    IRacerHost host,
    IAppCapabilities caps,
    Action<LumiRaceStartResultDto> openRace,
    Action openSelection,
    Action openCup,
    Action openPacks,
    Action openStamps,
    Action openWaiting)
{

    private LumiRaceStateDto? _state;
    private TimeSpan _serverOffset;
    private LumiRaceStateDto? _pendingState;
    private LumiCupStateDto? _cupState;
    private LumiCupStateDto? _pendingCup;
    private LumiRaceStartResultDto? _pendingStart;
    private string? _pendingError;
    private string? _error;
    private bool _busy;
    private bool _gateOpen;
    private bool _gateAsked;
    private float _gateHeight;
    private bool _practiceOpen;
    private float _practiceHeight;

    public int PendingPackCount => _state?.PendingPacks.Length ?? 0;

    public void OnShow()
    {
        // The gate says its piece on every visit while the creature cannot race, not once a session: a
        // player who dismissed it and came back an hour later is asking again.
        _gateAsked = false;
        Refresh();
    }

    /// <summary>The gate over a page that is not this one (the introduction), so a player whose creature
    /// cannot race hears it before six pages about racing. Drains the state itself, since this screen's
    /// own frame is not running underneath.</summary>
    public void DrawGateOverlay(OsAppContext ctx)
    {
        DrainState();
        if (_state is { } state)
        {
            DrawGate(ctx, state);
        }
    }

    public void Draw(OsAppContext ctx)
    {
        Drain();

        var avail = ImGui.GetContentRegionAvail();
        using var body = ImRaii.Child("##racerHome", avail, false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoBackground);
        if (!body)
        {
            return;
        }

        if (_error is { } failure)
        {
            GrandstandFrame.Panel(ctx, host, "paper", ImGui.GetWindowPos(), avail);
            using var ink = ImRaii.PushColor(ImGuiCol.Text, GrandstandFrame.Ink);
            ImGui.Dummy(new Vector2(1, Px(24)));
            RacerChrome.CenteredWrapped(failure);
            ImGui.Dummy(new Vector2(1, Px(16)));
            if (GrandstandFrame.ActionButton(ctx, host, "##homeRetry", ctx.Localize("os.racer_cup_retry")))
            {
                _error = null;
                Refresh();
            }
            return;
        }
        if (_state is not { } state)
        {
            ImGui.Dummy(new Vector2(1f, avail.Y * 0.45f));
            RacerChrome.CenteredText(ctx.Localize("os.racer_loading"));
            DrawError(ctx);
            return;
        }

        DrawMenu(ctx, state);
        ImGui.Dummy(new Vector2(1f, Px(10)));
        DrawError(ctx);
    }

    /// <summary>True while the gate is up, for the introduction, which shows the gate but not the notice.</summary>
    public bool GateOpen => _gateOpen;

    /// <summary>True while the gate or the practice notice is up.</summary>
    public bool OverlayOpen => _error is null && _state is not null && (_gateOpen || _practiceOpen);

    /// <summary>The home page's popups. The grandstand calls this after its content, in its own window, so each
    /// popup centres on the whole app and not on the menu below the logo.</summary>
    public void DrawOverlays(OsAppContext ctx)
    {
        if (_error is not null || _state is not { } state)
        {
            return;
        }

        DrawGate(ctx, state);
        DrawPracticeNotice(ctx, state);
    }

    private void Drain()
    {
        DrainState();
        if (_pendingCup is { } cup)
        {
            _pendingCup = null;
            _cupState = cup;
        }
        if (_pendingStart is { } start)
        {
            _pendingStart = null;
            _busy = false;
            openRace(start);
        }
        if (_pendingError is { } error)
        {
            _pendingError = null;
            _busy = false;
            _error = error;
        }
    }

    private void DrainState()
    {
        if (_pendingState is not { } state)
        {
            return;
        }
        _pendingState = null;
        _state = state;
        _error = null;
        _serverOffset = state.ServerNowUtc - DateTimeOffset.UtcNow;
        if (!_gateAsked && (!state.PetHatched || !state.PetAdult))
        {
            _gateAsked = true;
            _gateOpen = true;
        }
    }

    private DateTimeOffset ServerNow => DateTimeOffset.UtcNow + _serverOffset;

    private void Refresh()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                _pendingState = await host.GetStateAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _pendingError = host.DescribeError(ex);
            }
        });
        _ = Task.Run(async () =>
        {
            try
            {
                _pendingCup = await host.GetCupAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                // A server without the cup leaves the cup button live; the cup page reports the error.
            }
        });
    }

    /// <summary>What the player has to go and do before they can race, said on every visit while it
    /// holds. A creature that has not grown up is named, told what grows it, and handed the door to the
    /// food shelf; one with no creature at all is sent to hatch one. The race button dims for both. Drawn
    /// on the race card's paper in its blue ink with the flag's red button, like every other Racer page,
    /// and the title wraps rather than running off the panel.</summary>
    private void DrawGate(OsAppContext ctx, LumiRaceStateDto state)
    {
        if (!_gateOpen)
        {
            return;
        }

        var grow = state.PetHatched;
        var name = string.IsNullOrWhiteSpace(state.PetName) ? ctx.Localize("os.racer_gate_your_pet") : state.PetName;
        var ink = RacerChrome.CardBlue with { W = 1f };
        var width = MathF.Min(ImGui.GetWindowSize().X - Px(48f), Px(380f));
        var dismissed = DrawPageOverlayPanel("racerGate", ImGui.GetWindowPos(), ImGui.GetWindowSize(),
            ref _gateHeight, Px(grow ? 290f : 240f), innerW =>
            {
                var top = ImGui.GetCursorScreenPos();
                IconDraw.AddCentered(ImGui.GetWindowDrawList(), FontAwesomeIcon.Egg, Px(24f),
                    new Vector2(top.X + (innerW * 0.5f), top.Y + Px(14f)),
                    ImGui.ColorConvertFloat4ToU32(RacerChrome.DutchRed with { W = 1f }));
                ImGui.Dummy(new Vector2(1f, Px(34f)));
                OnboardingUi.DrawCenteredParagraph(
                    grow ? string.Format(ctx.Localize("os.racer_gate_title_grow"), name) : ctx.Localize("os.racer_gate_title"),
                    innerW - Px(8f), ink, ctx.TitleFont);
                ImGui.Dummy(new Vector2(1f, Px(6f)));
                OnboardingUi.DrawCenteredParagraph(
                    grow ? string.Format(ctx.Localize("os.racer_gate_grow"), name) : ctx.Localize("os.racer_gate_hatch"),
                    innerW - Px(16f), ink with { W = 0.86f });
                ImGui.Dummy(new Vector2(1f, Px(14f)));
                if (grow)
                {
                    if (RacerChrome.FlagButton(ctx, "##racerGateStore", ctx.Localize("os.racer_gate_store"),
                        RacerChrome.CardBlue, RacerChrome.WhiteInk, fullWidth: true))
                    {
                        _gateOpen = false;
                        ctx.Shell.SendIntent("store", OsIntents.CreatePath(OsIntents.StoreOpen, "consumables"));
                    }
                    ImGui.Dummy(new Vector2(1f, Px(6f)));
                }
                if (RacerChrome.FlagButton(ctx, "##racerGateOk", ctx.Localize("os.racer_gate_ok"),
                    RacerChrome.DutchRed, RacerChrome.WhiteInk, fullWidth: true))
                {
                    _gateOpen = false;
                }
            },
            RacerChrome.Paper with { W = 1f }, RacerChrome.CardBlue with { W = 0.55f }, width);
        if (dismissed)
        {
            _gateOpen = false;
        }
    }

    private const string PracticeNoticeKey = "racer.practiceNoticeSeen";

    /// <summary>Whether the next race runs in practice: either stamp cap reached means no stamp and no
    /// sparks until the caps roll. The race itself is never blocked.</summary>
    internal static bool IsPractice(LumiRaceStateDto state) =>
        (state.StampsPerDay > 0 && state.StampsToday >= state.StampsPerDay)
        || (state.StampsPerWeek > 0 && state.StampsThisWeek >= state.StampsPerWeek);

    /// <summary>Said once when the player first crosses into practice: what changed and when the
    /// tournament opens again. Both ways out continue to the selection page; the quiet one also stops
    /// the notice for good.</summary>
    private void DrawPracticeNotice(OsAppContext ctx, LumiRaceStateDto state)
    {
        if (!_practiceOpen)
        {
            return;
        }

        var timer = Countdown(ctx, PracticeLeft(state, ServerNow));

        var dismissed = DrawPageOverlayPanel("racerPractice", ImGui.GetWindowPos(), ImGui.GetWindowSize(),
            ref _practiceHeight, Px(240f), innerW =>
            {
                using (ctx.TitleFont?.Push())
                {
                    RacerChrome.CenteredText(ctx.Localize("os.racer_practice_title"));
                }
                ImGui.Dummy(new Vector2(1f, Px(8f)));
                OnboardingUi.DrawCenteredParagraph(
                    string.Format(ctx.Localize("os.racer_practice_body"), timer),
                    innerW - Px(24f), new Vector4(0.86f, 0.88f, 0.94f, 1f), UiFonts.Body);
                ImGui.Dummy(new Vector2(1f, Px(12f)));
                // Both ways out are the same button at the same width: one leads on, the other leads on
                // and stops the notice, and neither is the bigger answer.
                if (RacerChrome.FlagButton(ctx, "##racerPracticeOk",
                    ctx.Localize("os.racer_practice_ok"),
                    RacerChrome.DutchRed, RacerChrome.WhiteInk, null, true, false, true))
                {
                    _practiceOpen = false;
                    openSelection();
                }
                ImGui.Dummy(new Vector2(1f, Px(6f)));
                if (RacerChrome.FlagButton(ctx, "##racerPracticeHide",
                    ctx.Localize("os.racer_practice_hide"),
                    RacerChrome.CardFace, RacerChrome.WhiteInk, null, true, false, true))
                {
                    caps.Storage("racer").Set(PracticeNoticeKey, true);
                    _practiceOpen = false;
                    openSelection();
                }
            });
        if (dismissed)
        {
            _practiceOpen = false;
        }
    }

    /// <summary>Days and hours while a day or more is left, hours and minutes after that.</summary>
    /// <summary>How long until the tournament opens again for a player in practice: the sparks week's reset when the
    /// week cap is reached, otherwise the next UTC midnight, when the day cap rolls.</summary>
    internal static TimeSpan PracticeLeft(LumiRaceStateDto state, DateTimeOffset serverNow)
    {
        var midnight = new DateTimeOffset(serverNow.UtcDateTime.Date, TimeSpan.Zero).AddDays(1);
        var reopens = state.StampsPerWeek > 0 && state.StampsThisWeek >= state.StampsPerWeek
            ? state.WeekResetAtUtc ?? midnight
            : midnight;
        var left = reopens - serverNow;
        return left < TimeSpan.Zero ? TimeSpan.Zero : left;
    }

    internal static string Countdown(OsAppContext ctx, TimeSpan left) => left.TotalDays >= 1
        ? string.Format(ctx.Localize("os.racer_practice_days"), (int)left.TotalDays, left.Hours)
        : string.Format(ctx.Localize("os.racer_practice_hours"), (int)left.TotalHours, left.Minutes);

    /// <summary>The wait for next week's cup once this week's is finished, or null while the cup page
    /// has something to show. Blocking the button spares a trip back to a podium already seen.</summary>
    private string? CupReason(OsAppContext ctx)
    {
        if (_cupState is not { Cup.FinishedRaces.Length: LumiCupRules.RaceCount } cup || cup.WeekResetAtUtc <= ServerNow)
        {
            return null;
        }
        return string.Format(ctx.Localize("os.racer_cup_next_in"), Countdown(ctx, cup.WeekResetAtUtc - ServerNow));
    }

    /// <summary>Why the race button cannot be pressed, or null when it can. The reason rides under
    /// the button rather than replacing it, so the menu keeps its shape.</summary>
    private string? RaceReason(OsAppContext ctx, LumiRaceStateDto state)
    {
        if (!state.Enabled)
        {
            return ctx.Localize("os.racer_no_races");
        }
        // The two pet refusals are the popup's to explain, so the button only dims for them.
        if (!state.PetHatched || !state.PetAdult)
        {
            return string.Empty;
        }
        return null;
    }

    /// <summary>Party members learn their reward from the refreshed state after playback; the begin
    /// reply carries the race alone.</summary>
    private void DrawError(OsAppContext ctx)
    {
        if (_error is not { } error)
        {
            return;
        }
        ImGui.Dummy(new Vector2(1f, Px(8)));
        using (ImRaii.PushColor(ImGuiCol.Text, new Vector4(0.92f, 0.45f, 0.45f, 1f)))
        {
            RacerChrome.CenteredWrapped(error);
        }
    }

}
