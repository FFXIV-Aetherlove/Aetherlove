using System;
using System.Numerics;
using AetherLove.Shared.Racing;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Racer.Screens;

/// <summary>The stamp card on its own page, on race day's own picture: the printed card with the week's
/// stamps on it, and the pack a full card has dealt sitting beside it.</summary>
internal sealed class StampsScreen(IRacerHost host, Action back, Func<bool> muted, Action toggleMute, Func<float> volume, Action<float> setVolume)
{
    private LumiRaceStateDto? _state;
    private LumiRaceStateDto? _pending;
    private string? _error;
    private string? _pendingError;
    private PackRipOverlay? _pack;
    private CardFlipOverlay? _flip;

    public void OnShow()
    {
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                _pending = await host.GetStateAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _pendingError = host.DescribeError(ex);
            }
        });
    }

    public void Draw(OsAppContext ctx)
    {
        if (_pending is { } fresh)
        {
            _pending = null;
            _state = fresh;
        }
        if (_pendingError is { } pendingError)
        {
            _pendingError = null;
            _error = pendingError;
        }

        var avail = ImGui.GetContentRegionAvail();
        var origin = ImGui.GetCursorScreenPos();
        using var body = ImRaii.Child("##racerStamps", avail, false, ImGuiWindowFlags.NoScrollbar);
        if (!body)
        {
            return;
        }

        RacerBackdrop.Draw(ctx, host, origin, avail, 0.42f);
        RacerChrome.DrawMuteChip(ctx, muted(), toggleMute, volume(), setVolume);
        ImGui.Dummy(new Vector2(1f, Px(14)));

        if (_state is not { } state)
        {
            ImGui.Dummy(new Vector2(1f, Px(24)));
            RacerChrome.CenteredText(ctx.Localize("os.racer_loading"));
            DrawBack(ctx);
            return;
        }

        DrawCard(ctx, state, state.PendingPacks.Length > 0);
        ImGui.Dummy(new Vector2(1f, Px(10)));
        DrawPacks(ctx, state);

        if (_error is { } error)
        {
            ImGui.Dummy(new Vector2(1f, Px(8)));
            RacerChrome.CenteredMuted(error);
        }

        DrawBack(ctx);

        if (_flip is { } turning)
        {
            turning.Draw(ctx);
            if (turning.Done)
            {
                _pack = new PackRipOverlay(host, turning.Pack, back);
                _flip = null;
            }
            else if (turning.Dismissed)
            {
                _flip = null;
            }
        }

        if (_pack is { } pack)
        {
            pack.Draw(ctx);
            if (pack.Closed)
            {
                _pack = null;
                OnShow();
            }
        }
    }

    private void DrawCard(OsAppContext ctx, LumiRaceStateDto state, bool hasPack)
    {
        var dl = ImGui.GetWindowDrawList();
        var avail = ImGui.GetContentRegionAvail();

        // What sits below the card: its count line, the pack button when there is one, and the way back.
        var below = Px(34) + (hasPack ? Px(60) : 0f) + Px(58);
        var width = MathF.Min(avail.X - Px(48), (avail.Y - below - Px(24)) * RacerCard.Aspect);
        var size = new Vector2(width, width / RacerCard.Aspect);
        var lead = MathF.Max(0f, (avail.Y - below - size.Y) * 0.42f);
        ImGui.Dummy(new Vector2(1f, lead));
        var topLeft = ImGui.GetCursorScreenPos() + new Vector2((avail.X - width) * 0.5f, 0f);

        dl.AddRectFilled(topLeft + new Vector2(Px(8), size.Y - Px(4)),
            topLeft + new Vector2(size.X - Px(8), size.Y + Px(10)), 0x59000000u, Px(12));
        RacerCard.Draw(dl, ctx, host, topLeft, size, state.Stamps);

        ImGui.Dummy(new Vector2(1f, size.Y + Px(12)));
        RacerChrome.CenteredText(string.Format(ctx.Localize("os.racer_card_progress"),
            state.Stamps, LumiRaceLimits.StampsPerCard));
    }

    private void DrawPacks(OsAppContext ctx, LumiRaceStateDto state)
    {
        if (state.PendingPacks.Length == 0 || _flip is not null || _pack is not null)
        {
            return;
        }

        ImGui.Dummy(new Vector2(1f, Px(6)));
        if (RacerChrome.FlagButton(ctx, "##racerOpenPack", ctx.Localize("os.racer_pack_open"),
            RacerChrome.DutchRed, RacerChrome.WhiteInk))
        {
            _flip = new CardFlipOverlay(host, state.PendingPacks[0], state.Stamps, PlayThud, back);
        }
    }

    private void DrawBack(OsAppContext ctx)
    {
        var avail = ImGui.GetContentRegionAvail();
        if (avail.Y > Px(58))
        {
            ImGui.Dummy(new Vector2(1f, avail.Y - Px(58)));
        }
        if (RacerChrome.FlagButton(ctx, "##racerStampsBack", ctx.Localize("os.racer_back"),
            RacerChrome.DutchBlue, RacerChrome.WhiteInk))
        {
            back();
        }
    }

    private void PlayThud(OsAppContext ctx)
    {
        if (muted())
        {
            return;
        }
        try
        {
            ctx.Capabilities.Audio.Play(System.IO.Path.Combine(host.SoundRoot, "crystal_thud.ogg"), 0.22f);
        }
        catch (Exception)
        {
        }
    }
}
