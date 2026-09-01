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
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Racer.Screens;

/// <summary>Race day: the picture, the day's three courses on cards over it, and the party flow
/// underneath. The card and the numbers each live on their own page now.</summary>
internal sealed class HomeScreen(
    IRacerHost host,
    IAppCapabilities caps,
    Action<LumiRaceStartResultDto> openRace,
    Action openSelection,
    Action openStats,
    Action openStamps,
    Action openWaiting,
    Action openIntro,
    Func<bool> muted,
    Action toggleMute,
    Func<float> volume,
    Action<float> setVolume)
{

    /// <summary>How much of the page the picture keeps: less when the three cards need the room.</summary>
    private const float PictureShare = 0.46f;

    private LumiRaceStateDto? _state;
    private TimeSpan _serverOffset;
    private LumiRaceStateDto? _pendingState;
    private LumiRaceStartResultDto? _pendingStart;
    private string? _pendingError;
    private string? _error;
    private bool _busy;
    private PackRipOverlay? _pack;
    private bool _gateOpen;
    private bool _gateAsked;
    private float _gateHeight;
    private bool _practiceOpen;
    private float _practiceHeight;

    public int PendingPackCount => _state?.PendingPacks.Length ?? 0;

    public void OnShow()
    {
        Refresh();
    }

    public void Draw(OsAppContext ctx)
    {
        Drain();

        var avail = ImGui.GetContentRegionAvail();
        using var body = ImRaii.Child("##racerHome", avail, false, ImGuiWindowFlags.NoScrollbar);
        if (!body)
        {
            return;
        }

        RacerBackdrop.Draw(ctx, host, ImGui.GetWindowPos(), ImGui.GetWindowSize(), dim: 0f);
        DrawButtonWash(ctx);
        RacerChrome.DrawMuteChip(ctx, muted(), toggleMute, volume(), setVolume);

        if (_state is not { } state)
        {
            ImGui.Dummy(new Vector2(1f, avail.Y * 0.45f));
            RacerChrome.CenteredText(ctx.Localize("os.racer_loading"));
            DrawError(ctx);
            return;
        }

        // The picture holds the top; the menu sits on the road in the lower half of it.
        ImGui.Dummy(new Vector2(1f, avail.Y * PictureShare));
        DrawMenu(ctx, state);
        ImGui.Dummy(new Vector2(1f, Px(10)));
        DrawError(ctx);
        DrawGate(ctx, state);
        DrawPracticeNotice(ctx, state);

        if (_pack is { } pack)
        {
            pack.Draw(ctx);
            if (pack.Closed)
            {
                _pack = null;
                Refresh();
            }
        }
    }

    private void Drain()
    {
        if (_pendingState is { } state)
        {
            _pendingState = null;
            _state = state;
            _serverOffset = state.ServerNowUtc - DateTimeOffset.UtcNow;
            if (!_gateAsked && (!state.PetHatched || !state.PetAdult))
            {
                _gateAsked = true;
                _gateOpen = true;
            }
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
    }

    /// <summary>The ground under the buttons, so a label still reads on bright asphalt.</summary>
    private static void DrawButtonWash(OsAppContext ctx)
    {
        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        dl.AddRectFilledMultiColor(
            new Vector2(origin.X, origin.Y + (size.Y * 0.40f)), origin + size,
            0x00000000u, 0x00000000u, 0xB0000000u, 0xB0000000u);
    }

    /// <summary>What the player has to go and do before they can race, said once and dismissed. The
    /// race button dims for it.</summary>
    private void DrawGate(OsAppContext ctx, LumiRaceStateDto state)
    {
        if (!_gateOpen)
        {
            return;
        }

        var dismissed = DrawPageOverlayPanel("racerGate", ImGui.GetWindowPos(), ImGui.GetWindowSize(),
            ref _gateHeight, Px(210f), innerW =>
            {
                using (ctx.TitleFont?.Push())
                {
                    RacerChrome.CenteredText(ctx.Localize("os.racer_gate_title"));
                }
                ImGui.Dummy(new Vector2(1f, Px(8f)));
                OnboardingUi.DrawCenteredParagraph(
                    ctx.Localize(state.PetHatched ? "os.racer_gate_grow" : "os.racer_gate_hatch"),
                    innerW - Px(24f), new Vector4(0.86f, 0.88f, 0.94f, 1f));
                ImGui.Dummy(new Vector2(1f, Px(12f)));
                if (OnboardingUi.DrawPrimaryButton(ctx.Localize("os.racer_gate_ok"), true))
                {
                    _gateOpen = false;
                }
            });
        if (dismissed)
        {
            _gateOpen = false;
        }
    }

    /// <summary>The ways out of this screen: the day's three courses on top, then the card and the
    /// numbers in the flag's own colours.</summary>
    private void DrawMenu(OsAppContext ctx, LumiRaceStateDto state)
    {
        var together = caps.Party.InParty;
        var reason = RaceReason(ctx, state);
        var practice = !together && IsPractice(state);
        var label = together
            ? ctx.Localize("os.racer_race_together")
            : ctx.Localize(practice ? "os.racer_race_practice" : "os.racer_race_now");
        if (RacerChrome.FlagButton(ctx, "##racerRace", label,
            RacerChrome.DutchRed, RacerChrome.WhiteInk, reason, !_busy, chequered: true))
        {
            if (together)
            {
                openWaiting();
            }
            else if (practice && caps.Storage("racer").Get<bool?>(PracticeNoticeKey) != true)
            {
                _practiceOpen = true;
            }
            else
            {
                openSelection();
            }
        }
        ImGui.Dummy(new Vector2(1f, Px(10)));
        if (RacerChrome.FlagButton(ctx, "##racerStamps", ctx.Localize("os.racer_view_stamps"),
            RacerChrome.DutchWhite, RacerChrome.DarkInk, null, !_busy))
        {
            openStamps();
        }
        ImGui.Dummy(new Vector2(1f, Px(10)));
        if (RacerChrome.FlagButton(ctx, "##racerStats", ctx.Localize("os.racer_stats"),
            RacerChrome.CardBlue, RacerChrome.WhiteInk, null, !_busy))
        {
            openStats();
        }
        ImGui.Dummy(new Vector2(1f, Px(8)));
        DrawIntroLink(ctx);
    }

    /// <summary>A quiet line back into the onboarding, for anyone who skipped it or forgot.</summary>
    private void DrawIntroLink(OsAppContext ctx)
    {
        var label = ctx.Localize("os.racer_intro_again");
        var size = ImGui.CalcTextSize(label);
        var avail = ImGui.GetContentRegionAvail().X;
        ImGui.SetCursorPosX((avail - size.X) * 0.5f);
        var at = ImGui.GetCursorScreenPos();
        var pressed = ImGui.InvisibleButton("##racerIntroAgain", new Vector2(size.X, size.Y + Px(4)));
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            HandOnHover();
        }
        var dl = ImGui.GetWindowDrawList();
        dl.AddText(at, ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, hovered ? 1f : 0.85f)), label);
        dl.AddLine(new Vector2(at.X, at.Y + size.Y + Px(1)),
            new Vector2(at.X + size.X, at.Y + size.Y + Px(1)),
            ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, hovered ? 0.8f : 0.4f)), Px(1f));
        if (pressed)
        {
            openIntro();
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

        var reopens = state.StampsPerWeek > 0 && state.StampsThisWeek >= state.StampsPerWeek
            ? state.WeekResetAtUtc ?? NextUtcMidnight()
            : NextUtcMidnight();
        var left = reopens - ServerNow;
        if (left < TimeSpan.Zero)
        {
            left = TimeSpan.Zero;
        }
        var timer = left.TotalHours >= 24
            ? string.Format(ctx.Localize("os.racer_practice_days"), (int)left.TotalDays, left.Hours)
            : string.Format(ctx.Localize("os.racer_practice_hours"), (int)left.TotalHours, left.Minutes);

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

    private DateTimeOffset NextUtcMidnight()
    {
        var now = ServerNow;
        return new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero).AddDays(1);
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
        if (state.NextRaceAtUtc is { } at && at > ServerNow)
        {
            var left = at - ServerNow;
            return string.Format(ctx.Localize("os.racer_next_race"), $"{(int)left.TotalMinutes:0}:{left.Seconds:00}");
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
