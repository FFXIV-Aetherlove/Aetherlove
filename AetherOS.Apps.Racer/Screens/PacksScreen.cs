using System;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove.Shared.Racing;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Racer.Screens;
internal sealed class PacksScreen(IRacerHost host, Action changed)
{
    private LumiRaceStateDto? _state;
    private Task<LumiRaceStateDto>? _request;
    private string? _error;
    private PackRipOverlay? _opening;
    public bool IsOpening => _opening is not null;

    public void OnShow()
    {
        _opening = null;
        Refresh();
    }

    private void Refresh()
    {
        _error = null;
        _state = null;
        _request = Task.Run(() => host.GetStateAsync());
    }

    public void Draw(OsAppContext ctx)
    {
        if (_request is { IsCompleted: true } request)
        {
            _request = null;
            try
            {
                _state = request.GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _error = host.DescribeError(ex);
            }
        }

        if (_opening is { } opening)
        {
            opening.Draw(ctx);
            if (opening.Closed)
            {
                _opening = null;
                Refresh();
                changed();
            }

            return;
        }

        if (_error is { } failed)
        {
            RacerChrome.CenteredWrapped(failed);
            if (GrandstandFrame.ActionButton(ctx, host, "##packsRetry", ctx.Localize("os.racer_cup_retry")))
            {
                Refresh();
            }

            return;
        }

        if (_state is not { } state)
        {
            RacerChrome.CenteredText(ctx.Localize("os.racer_loading"));
            return;
        }

        if (state.PendingPacks.Length == 0)
        {
            var at = ImGui.GetCursorScreenPos();
            var size = ImGui.GetContentRegionAvail();
            GrandstandFrame.Panel(ctx, host, "paper", at, size);
            GrandstandFrame.Label(ctx, ctx.Localize("os.racer_packs_empty"), at + new Vector2(Px(20)), size - new Vector2(Px(40)), GrandstandFrame.Ink);
            return;
        }

        var origin = ImGui.GetCursorScreenPos();
        var room = ImGui.GetContentRegionAvail();
        using var grid = ImRaii.Child("##packGallery", room, false, ImGuiWindowFlags.NoBackground);
        if (!grid)
        {
            return;
        }

        var gap = Px(14);
        var columns = room.X >= Px(230) ? 2 : 1;
        var width = (room.X - gap * (columns + 1) - ImGui.GetStyle().ScrollbarSize) / columns;
        var height = width / .74f;
        for (var i = 0; i < state.PendingPacks.Length; i++)
        {
            var at = origin + new Vector2(gap + i % columns * (width + gap), Px(12) + i / columns * (height + Px(48))) - new Vector2(0, ImGui.GetScrollY());
            var artSize = new Vector2(width, height);
            GrandstandFrame.Sparkles(ctx, at - new Vector2(Px(7)), artSize + new Vector2(Px(14)), 18);
            ImGui.SetCursorScreenPos(at);
            var clicked = ImGui.InvisibleButton($"##pack{state.PendingPacks[i].PackId}", artSize);
            HandOnHover();
            var hovered = ImGui.IsItemHovered();
            var lift = hovered && !ctx.ReduceMotion ? Px(4) : 0;
            PackArtwork.Draw(ctx, host, state.PendingPacks[i].PackId, at - new Vector2(0, lift), artSize);
            GrandstandFrame.Label(ctx, string.Format(ctx.Localize("os.racer_pack_number"), i + 1), at + new Vector2(0, height + Px(8)), new Vector2(width, Px(24)), GrandstandFrame.Cream);
            if (clicked)
            {
                _opening = new PackRipOverlay(host, state.PendingPacks[i], () =>
                {
                }, at, artSize, PackArtwork.For(state.PendingPacks[i].PackId));
                break;
            }
        }

        ImGui.SetCursorPos(new Vector2(0, MathF.Ceiling(state.PendingPacks.Length / (float)columns) * (height + Px(48))));
        ImGui.Dummy(Vector2.One);
    }
}
