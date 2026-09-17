using System;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove.Shared.Racing;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;

namespace AetherOS.Apps.Racer.Screens;

internal sealed class StampsScreen(IRacerHost host, Action openPacks)
{
    private LumiRaceStateDto? _state;
    private Task<LumiRaceStateDto>? _refresh;
    private Task<LumiRacePackDto>? _claim;
    private CardFlipOverlay? _flip;
    private PackRipOverlay? _pack;
    private string? _error;
    public bool IsOpening => _flip is not null || _pack is not null;

    public void OnShow()
    {
        _error = null;
        _flip = null;
        _pack = null;
        _refresh = host.GetStateAsync();
    }

    public void Draw(OsAppContext ctx)
    {
        try
        {
            if (_refresh is { IsCompleted: true } refresh)
            {
                _refresh = null;
                _state = refresh.GetAwaiter().GetResult();
            }
            if (_claim is { IsCompleted: true } claim)
            {
                _claim = null;
                var claimed = claim.GetAwaiter().GetResult();
                _flip = new CardFlipOverlay(host, claimed, LumiRaceLimits.StampsPerCard, _ => { }, openPacks, compact: true);
                _flip.BeginTurn();
                _refresh = host.GetStateAsync();
            }
        }
        catch (Exception ex)
        {
            _error = host.DescribeError(ex);
        }
        if (_flip is { } flip)
        {
            flip.Draw(ctx);
            if (flip.Done)
            {
                _pack = new PackRipOverlay(host, flip.Pack, () => { }, compact: true);
                _flip = null;
            }
            return;
        }
        if (_pack is { } pack)
        {
            pack.Draw(ctx);
            if (pack.Closed)
            {
                _pack = null;
                openPacks();
            }
            return;
        }
        if (_state is not { } state)
        {
            RacerChrome.CenteredWrapped(_error ?? ctx.Localize("os.racer_loading"));
            if (_error is not null && GrandstandFrame.ActionButton(ctx, host, "##retryCard", ctx.Localize("os.racer_cup_retry")))
            {
                OnShow();
            }

            return;
        }
        var origin = ImGui.GetWindowPos();
        var avail = ImGui.GetWindowSize();
        var full = state.Stamps >= LumiRaceLimits.StampsPerCard;
        var (at, size) = PackArtwork.Stage(origin, avail);
        var dl = ImGui.GetWindowDrawList();
        GrandstandFrame.Panel(ctx, host, "navy", origin, avail);
        if (full)
        {
            GrandstandFrame.Sparkles(ctx, origin, avail, 36);
            RacerChrome.Halo(dl, at + size / 2, size.X * .8f, new Vector4(1, .8f, .3f, 1), .4f);
        }
        RacerCard.Draw(dl, ctx, host, at, size, Math.Min((int)state.Stamps, LumiRaceLimits.StampsPerCard));
        ImGui.SetCursorScreenPos(at);
        var pressed = ImGui.InvisibleButton("##exchangeStampCard", size);
        if (full && _claim is null)
        {
            HandOnHover();
        }

        if (pressed && full && _claim is null)
        {
            _error = null;
            try
            {
                _claim = host.ClaimStampCardAsync(state.CardsCompleted - state.Stamps / LumiRaceLimits.StampsPerCard + 1);
            }
            catch (Exception ex)
            {
                _error = host.DescribeError(ex);
            }
        }
        var label = _error ?? (_claim is not null ? ctx.Localize("os.racer_loading") : full
            ? ctx.Localize("os.racer_card_turn")
            : string.Format(ctx.Localize("os.racer_stamp_progress"), state.Stamps, LumiRaceLimits.StampsPerCard));
        GrandstandFrame.WrappedLabel(ctx, label, at + new Vector2(-Px(10), size.Y + Px(7)),
            new Vector2(size.X + Px(20), Px(42)), GrandstandFrame.Gold, full || _error is not null ? RacerTextSize.Caption : RacerTextSize.Body);
    }
}
