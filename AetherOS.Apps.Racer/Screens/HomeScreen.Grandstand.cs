using System;
using System.Numerics;
using AetherLove.Shared.Racing;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;

namespace AetherOS.Apps.Racer.Screens;
internal sealed partial class HomeScreen
{
    /// <summary>Where the stamp card's artwork leaves room for the count: the slot's centre, and the baseline
    /// its printed "/ 5" stands on, as shares of the card.</summary>
    private const float StampCountCentreX = .649f;
    private const float StampCountBaseline = .35f;

    /// <summary>The pack banner's count slot, measured from the per-language anchor: half the slot's width, and
    /// the drop from the printed caption's middle to its baseline, as shares of the banner.</summary>
    private const float PackCountHalfWidth = .0325f;
    private const float PackCountBaselineDrop = .065f;

    private void DrawMenu(OsAppContext ctx, LumiRaceStateDto state)
    {
        var origin = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var gap = Px(6);
        var together = caps.Party.InParty;
        var reason = RaceReason(ctx, state);
        var practice = !together && IsPractice(state);
        var hasPacks = state.PendingPacks.Length > 0;
        var ticketH = MathF.Min((size.X - gap) / 2 * 477 / 619, size.Y * (hasPacks ? .40f : .47f));
        var ticketSize = new Vector2((size.X - gap) / 2, ticketH);
        if (HomeArtwork.Ticket(ctx, host, "##racerRace", HomePanel.Race, origin, ticketSize, reason is null && !_busy, null))
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

        var cupReason = together ? ctx.Localize("os.racer_cup_solo") : CupReason(ctx);
        if (HomeArtwork.Ticket(ctx, host, "##racerCup", HomePanel.Cup, origin + new Vector2(ticketSize.X + gap, 0), ticketSize, cupReason is null && !_busy, together ? ctx.Localize("os.racer_cup_solo_short") : cupReason))
        {
            openCup();
        }

        if (ImGui.IsItemHovered() && !string.IsNullOrEmpty(cupReason))
        {
            Cards.CardChrome.Tooltip(cupReason);
        }

        var stampAt = origin + new Vector2(0, ticketH + gap);
        var stampSize = new Vector2(size.X, MathF.Min(size.X * 347 / 1238, size.Y * .29f));
        if (DrawStampCard(ctx, host, stampAt, stampSize, state.Stamps))
        {
            openStamps();
        }

        if (hasPacks)
        {
            var packAt = stampAt + new Vector2(0, stampSize.Y + gap);
            var packSize = new Vector2(size.X, MathF.Min(size.X * 358 / 1238, size.Y - ticketH - stampSize.Y - gap * 2));
            GrandstandFrame.Sparkles(ctx, packAt - new Vector2(Px(10)), packSize + new Vector2(Px(20)));
            ImGui.SetCursorScreenPos(packAt);
            var clicked = ImGui.InvisibleButton("##racerPacks", packSize);
            HandOnHover();
            HomeArtwork.Draw(ctx, host, HomePanel.Packs, packAt, packSize, ImGui.IsItemHovered());
            var countY = ctx.Culture.TwoLetterISOLanguageName is "de" or "ru" ? .29f : .36f;
            var countX = ctx.Culture.TwoLetterISOLanguageName == "de" ? .412f : .432f;
            GrandstandFrame.Figure(state.PendingPacks.Length.ToString(ctx.Culture), packAt.X + (packSize.X * (countX + PackCountHalfWidth)),
                packAt.Y + (packSize.Y * (countY + PackCountBaselineDrop)), GrandstandFrame.Gold);
            if (clicked)
            {
                openPacks();
            }
        }

    }

    public static bool DrawStampCard(OsAppContext ctx, IRacerHost host, Vector2 at, Vector2 size, int stamps)
    {
        ImGui.SetCursorScreenPos(at);
        var clicked = ImGui.InvisibleButton("##stampCard", size);
        HandOnHover();
        if (stamps >= LumiRaceLimits.StampsPerCard)
        {
            var pulse = ctx.ReduceMotion ? .45f : .35f + .12f * MathF.Sin((float)ImGui.GetTime() * 2);
            RacerChrome.Halo(ImGui.GetWindowDrawList(), at + size / 2, size.X * .6f, new Vector4(1, .77f, .26f, 1), pulse);
            GrandstandFrame.Sparkles(ctx, at - new Vector2(Px(8)), size + new Vector2(Px(16)), 32);
        }
        HomeArtwork.Draw(ctx, host, HomePanel.Stamps, at, size, ImGui.IsItemHovered() || stamps >= LumiRaceLimits.StampsPerCard);
        GrandstandFrame.Figure(Math.Min(stamps, LumiRaceLimits.StampsPerCard).ToString(ctx.Culture), at.X + (size.X * StampCountCentreX),
            at.Y + (size.Y * StampCountBaseline), GrandstandFrame.Ink);
        var dl = ImGui.GetWindowDrawList();
        var radius = MathF.Min(size.X * .063f, size.Y * .24f);
        for (var i = 0; i < LumiRaceLimits.StampsPerCard; i++)
        {
            var center = at + new Vector2(size.X * (.20f + i * .15f), size.Y * .66f);
            var ink = i < stamps ? 0xFF3435B6u : 0x66517EAAu;
            dl.AddCircle(center, radius, ink, 40, Px(1.5f));
            dl.AddCircle(center, radius * .86f, ink, 40, Px(.7f));
            RacerChrome.Stamp(dl, ctx, host.PetAssetRoot, center, radius * .65f, ink);
        }

        return clicked;
    }
}
