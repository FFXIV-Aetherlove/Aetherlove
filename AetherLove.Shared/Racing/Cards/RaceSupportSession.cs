using System;
using System.Collections.Generic;
using System.Linq;

namespace AetherLove.Shared.Racing.Cards;

/// <summary>A carded race: the hands frozen at construction, one <see cref="RaceSupportController"/> per runner
/// that carries a card, and the step order both sides agree on (observe every runner, apply every runner, then the
/// engine's tick). The server resolves a race through it and the client replays the same race through it; with
/// every hand empty it steps the engine exactly as <see cref="AetherRaceLive.Race.RunToEnd"/> does, because the
/// adapter consumes none of the engine's RNG. Step it to the end with <c>while (!session.IsComplete) session.Step();</c>.</summary>
public sealed class RaceSupportSession
{
    // Mirrors the hard guard on RunToEnd, so a race that never reaches the line still completes the session.
    private const int TickGuard = 30 * 300;
    private const int NotFired = -1;

    private readonly AetherRaceLive.Race race;
    private readonly RaceCardHand[] hands;
    private readonly RaceSupportController?[] controllers;
    private readonly int[] firedTicks;
    private readonly List<RaceSupportEvent> events = [];
    private bool complete;

    private RaceSupportSession(AetherRaceLive.Race race, RaceCardHand[] hands)
    {
        this.race = race;
        this.hands = hands;
        this.controllers = new RaceSupportController?[hands.Length];
        this.firedTicks = new int[hands.Length];
        Array.Fill(this.firedTicks, NotFired);
    }

    /// <summary>Builds the session over a race that has not stepped yet. <paramref name="hands"/> is one hand per
    /// runner in slot order and must match <c>race.Runners.Count</c>; an empty hand gets no controller. The hands
    /// are copied, so editing the list afterwards changes nothing.</summary>
    public static RaceSupportSession Create(AetherRaceLive.Race race, IReadOnlyList<RaceCardHand> hands, string weatherKey, int seed)
    {
        ArgumentNullException.ThrowIfNull(race);
        ArgumentNullException.ThrowIfNull(hands);
        ArgumentNullException.ThrowIfNull(weatherKey);
        if (hands.Count != race.Runners.Count)
        {
            throw new ArgumentException($"Expected one hand per runner ({race.Runners.Count}), got {hands.Count}.", nameof(hands));
        }

        var frozen = new RaceCardHand[hands.Count];
        for (var slot = 0; slot < frozen.Length; slot++)
        {
            frozen[slot] = hands[slot] ?? RaceCardHand.Empty;
        }

        var session = new RaceSupportSession(race, frozen);
        for (var slot = 0; slot < frozen.Length; slot++)
        {
            if (frozen[slot].IsEmpty)
            {
                continue;
            }

            session.controllers[slot] = new RaceSupportController(
                race, slot, frozen[slot], weatherKey, seed, RaceCardVersions.SupportVersion, RaceCardCatalogue.Find, session.events.Add);
        }

        return session;
    }

    public bool IsComplete => this.complete;

    /// <summary>Every runner's lines, in the order they were logged.</summary>
    public IReadOnlyList<RaceSupportEvent> Events => this.events;

    public IReadOnlyList<RaceSupportEvent> EventsFor(int slot)
    {
        return this.events.Where(e => e.Slot == slot).ToArray();
    }

    public bool Fired(int slot) => this.controllers[slot]?.Fired ?? false;

    /// <summary>The tick the slot's Gold fired on, or -1.</summary>
    public int FiredTick(int slot) => this.firedTicks[slot];

    public RaceCardHand HandOf(int slot) => this.hands[slot];

    /// <summary>One tick: every controller observes, every controller applies, then the engine steps. Completes
    /// the session when the race is done.</summary>
    public void Step()
    {
        if (this.complete || this.race.Done || this.race.Tick >= TickGuard)
        {
            this.Complete();
            return;
        }

        foreach (var controller in this.controllers)
        {
            controller?.BeforeStep();
        }

        for (var slot = 0; slot < this.controllers.Length; slot++)
        {
            var controller = this.controllers[slot];
            if (controller is null)
            {
                continue;
            }

            controller.ApplyPending();
            if (controller.Fired && this.firedTicks[slot] < 0)
            {
                this.firedTicks[slot] = this.race.Tick;
            }
        }

        this.race.Step();
        if (this.race.Done)
        {
            this.Complete();
        }
    }

    /// <summary>Restores anything a card still owns and writes every unfired Gold's terminal line. Idempotent.</summary>
    public void Complete()
    {
        if (this.complete)
        {
            return;
        }

        foreach (var controller in this.controllers)
        {
            controller?.Complete();
        }

        this.complete = true;
    }
}
