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
    Action back)
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
        using var body = ImRaii.Child("##racerWait", avail, false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoBackground);
        if (!body)
        {
            return;
        }

        var size = ImGui.GetWindowSize();
        var party = caps.Party;
        // Only a gathering is a lobby. A begun run stays Active until the sweep resolves it a few
        // minutes after the race, and reading that as a lobby showed the finished roster as
        // "2 of 2 joined" with a start button the server was always going to refuse.
        var live = host.PartyRun ?? _state?.PartyRun;
        var running = live is { Status: (short)LumiRacePartyRunStatus.Active };
        var run = live is { Status: (short)LumiRacePartyRunStatus.Gathering } ? live : null;

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
        DrawRoster(ctx, run);
        ImGui.Dummy(new Vector2(1f, Px(14)));
        DrawActions(ctx, run, party, mine, joined, running);

        if (_error is { } error)
        {
            ImGui.Dummy(new Vector2(1f, Px(8)));
            RacerChrome.CenteredMuted(error);
        }

        ImGui.Dummy(new Vector2(1, Px(14)));
        if (GrandstandFrame.ActionButton(ctx, host, "##racerWaitBack", ctx.Localize("os.racer_back"), secondary: true))
        {
            back();
        }
    }

    private void DrawRoster(OsAppContext ctx, LumiRacePartyRunDto? run)
    {
        if (run is null)
        {
            return;
        }
        foreach (var member in run.Members)
        {
            var at = ImGui.GetCursorScreenPos() + new Vector2(Px(12), 0);
            var size = new Vector2(ImGui.GetContentRegionAvail().X - Px(24), Px(50));
            GrandstandFrame.Panel(ctx, host, "paper", at, size);
            var icon = member.Joined ? Dalamud.Interface.FontAwesomeIcon.Check : Dalamud.Interface.FontAwesomeIcon.Clock;
            AetherLove.UI.IconDraw.AddCentered(ImGui.GetWindowDrawList(), icon, Px(16), at + new Vector2(Px(24), size.Y / 2), GrandstandFrame.Ink);
            GrandstandFrame.Label(ctx, member.Name, at + new Vector2(Px(44), 0), size - new Vector2(Px(58), 0), GrandstandFrame.Ink);
            ImGui.Dummy(new Vector2(1, size.Y + Px(8)));
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
                if (GrandstandFrame.ActionButton(ctx, host, "##racerPartyStart", ctx.Localize("os.racer_party_start"), !_busy, wait))
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
            if (GrandstandFrame.ActionButton(ctx, host, "##racerPartyJoin", ctx.Localize("os.racer_party_join"), !_busy))
            {
                Call(async () => await host.JoinPartyRunAsync(run.RunId));
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
        if (GrandstandFrame.ActionButton(ctx, host, "##racerPartyBegin", ctx.Localize("os.racer_party_begin"), !_busy, tooFew))
        {
            _busy = true;
            Call(async () => await host.BeginPartyRunAsync(run.RunId));
        }
        ImGui.Dummy(new Vector2(1f, Px(8)));
        if (GrandstandFrame.ActionButton(ctx, host, "##racerPartyCancel", ctx.Localize("os.racer_party_cancel"), !_busy, secondary: true, secondaryIcon: Dalamud.Interface.FontAwesomeIcon.Times))
        {
            Call(async () => { await host.CancelPartyRunAsync(run.RunId).ConfigureAwait(false); return null; });
        }
    }

    private void Call(Func<Task<LumiRacePartyRunDto?>> call)
    {
        _error = null;
        _busy = true;
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
