using System;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove.Shared.Racing;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Racer.Screens;

/// <summary>The party's waiting room: race day with the lights down, the roster, and whichever of
/// join, begin or cancel this member is allowed. The host begins it; everyone else waits, and the
/// app takes them all to the stage on the same gun.</summary>
internal sealed class WaitingRoomScreen(
    IRacerHost host,
    IAppCapabilities caps,
    Action back,
    Func<bool> muted,
    Action toggleMute,
    Func<float> volume,
    Action<float> setVolume)
{
    private LumiRaceStateDto? _state;
    private LumiRaceStateDto? _pending;
    private string? _error;
    private bool _busy;

    public void OnShow()
    {
        Refresh();
    }

    public void Draw(OsAppContext ctx)
    {
        if (_pending is { } fresh)
        {
            _pending = null;
            _state = fresh;
            _busy = false;
        }

        var avail = ImGui.GetContentRegionAvail();
        using var body = ImRaii.Child("##racerWait", avail, false, ImGuiWindowFlags.NoScrollbar);
        if (!body)
        {
            return;
        }

        var origin = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        RacerBackdrop.Draw(ctx, host, origin, size, dim: 0.72f);
        RacerChrome.DrawMuteChip(ctx, muted(), toggleMute, volume(), setVolume);

        var party = caps.Party;
        // Only a gathering is a lobby. A begun run stays Active until the sweep resolves it a few
        // minutes after the race, and reading that as a lobby showed the finished roster as
        // "2 of 2 joined" with a start button the server was always going to refuse.
        var live = host.PartyRun ?? _state?.PartyRun;
        var running = live is { Status: (short)LumiRacePartyRunStatus.Active };
        var run = live is { Status: (short)LumiRacePartyRunStatus.Gathering } ? live : null;

        ImGui.Dummy(new Vector2(1f, size.Y * 0.34f));
        using (ctx.TitleFont?.Push())
        {
            RacerChrome.CenteredText(ctx.Localize("os.racer_waiting_title"));
        }

        ImGui.Dummy(new Vector2(1f, Px(8)));
        var joined = 0;
        var mine = false;
        if (run is not null)
        {
            foreach (var member in run.Members)
            {
                if (!member.Joined)
                {
                    continue;
                }
                joined++;
                // The roster lists the WHOLE party with a joined flag, so "am I in it" is true for
                // everyone from the moment the host gathers. Only a joined row is me being in the race,
                // and testing membership alone hid the join button from every guest.
                if (member.AccountId == party.OwnAccountId)
                {
                    mine = true;
                }
            }
        }
        if (run is not null)
        {
            RacerChrome.CenteredText(string.Format(ctx.Localize("os.racer_waiting_body"),
                joined, Math.Max(joined, party.Members.Count)));
        }

        ImGui.Dummy(new Vector2(1f, Px(10)));
        DrawRoster(run);
        ImGui.Dummy(new Vector2(1f, Px(14)));
        DrawActions(ctx, run, party, mine, joined, running);

        if (_error is { } error)
        {
            ImGui.Dummy(new Vector2(1f, Px(8)));
            RacerChrome.CenteredMuted(error);
        }

        ImGui.SetCursorPosY(size.Y - Px(56));
        if (RacerChrome.FlagButton(ctx, "##racerWaitBack", ctx.Localize("os.racer_back"),
            RacerChrome.DutchWhite, RacerChrome.DarkInk))
        {
            back();
        }
    }

    private static void DrawRoster(LumiRacePartyRunDto? run)
    {
        if (run is null)
        {
            return;
        }
        foreach (var member in run.Members)
        {
            var line = member.Joined ? member.Name : member.Name + " ...";
            if (member.Joined)
            {
                RacerChrome.CenteredText(line);
            }
            else
            {
                RacerChrome.CenteredMuted(line);
            }
        }
    }

    private void DrawActions(OsAppContext ctx, LumiRacePartyRunDto? run, IPartyState party, bool mine,
        int joinedCount, bool running)
    {
        if (run is null)
        {
            if (party.AmHost)
            {
                // A just-finished race keeps its run Active until the sweep resolves it, and the server
                // refuses a second gathering while one exists; the button says so instead of erroring.
                var wait = running ? ctx.Localize("os.racer_party_running") : null;
                if (RacerChrome.FlagButton(ctx, "##racerPartyStart", ctx.Localize("os.racer_party_start"),
                    RacerChrome.DutchRed, RacerChrome.WhiteInk, wait, !_busy, chequered: true))
                {
                    Call(() => host.StartPartyGatherAsync()!);
                }
                RacerChrome.CenteredMuted(ctx.Localize("os.racer_party_bonus_hint"));
            }
            else if (running)
            {
                RacerChrome.CenteredMuted(ctx.Localize("os.racer_party_running"));
            }
            return;
        }

        if (!mine)
        {
            if (RacerChrome.FlagButton(ctx, "##racerPartyJoin", ctx.Localize("os.racer_party_join"),
                RacerChrome.CardBlue, RacerChrome.WhiteInk, null, !_busy))
            {
                Call(() => host.JoinPartyRunAsync(run.RunId));
            }
            return;
        }

        if (!party.AmHost)
        {
            return;
        }

        // Counting the roster rather than the racers on it offered a begin the server was always going
        // to refuse; the rule is said up front instead, where the button can honour it.
        var tooFew = joinedCount < 2 ? ctx.Localize("os.racer_party_need_two") : null;
        if (RacerChrome.FlagButton(ctx, "##racerPartyBegin", ctx.Localize("os.racer_party_begin"),
            RacerChrome.DutchRed, RacerChrome.WhiteInk, tooFew, !_busy, chequered: true))
        {
            _busy = true;
            Call(() => host.BeginPartyRunAsync(run.RunId));
        }
        ImGui.Dummy(new Vector2(1f, Px(8)));
        if (RacerChrome.FlagButton(ctx, "##racerPartyCancel", ctx.Localize("os.racer_party_cancel"),
            RacerChrome.DutchWhite, RacerChrome.DarkInk, null, !_busy))
        {
            Call(() => host.CancelPartyRunAsync(run.RunId).ContinueWith(_ => (LumiRacePartyRunDto?)null)!);
        }
    }

    private void Call(Func<Task<LumiRacePartyRunDto?>> call)
    {
        _error = null;
        _ = Task.Run(async () =>
        {
            try
            {
                await call().ConfigureAwait(false);
                _pending = await host.GetStateAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _error = host.DescribeError(ex);
                _busy = false;
            }
        });
    }

    private void Refresh()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                _pending = await host.GetStateAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _error = host.DescribeError(ex);
            }
        });
    }
}
