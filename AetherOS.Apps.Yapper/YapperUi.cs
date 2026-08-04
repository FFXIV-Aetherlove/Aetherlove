using System;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove.Services.Localization;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Yapper;

/// <summary>The pinned-post block at the top of a profile's Posts tab: a "Pinned" marker line plus the
/// full card, fetched once through the host when the store doesn't hold the yap yet.</summary>
internal sealed class PinnedYapSlot(IYapperHost host, YapperStore store)
{
    private bool _fetching;

    public void Draw(OsAppContext ctx, YapCard card, Guid? pinnedYapId)
    {
        if (pinnedYapId is not { } id)
        {
            return;
        }
        var dto = store.Get(id);
        if (dto is null)
        {
            if (!_fetching)
            {
                _fetching = true;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        store.Upsert(await host.GetYapAsync(id).ConfigureAwait(false));
                    }
                    catch (Exception)
                    {
                    }
                });
            }
            return;
        }
        if (dto.Deleted)
        {
            return;
        }
        var pad = Px(14f);
        ImGui.Dummy(new Vector2(0f, Px(8f)));
        var tl = ImGui.GetCursorScreenPos();
        IconDraw.AddCentered(ImGui.GetWindowDrawList(), FontAwesomeIcon.Thumbtack, Px(10f),
            tl + new Vector2(pad + Px(5f), ImGui.GetTextLineHeight() * 0.5f),
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.5f)));
        ImGui.SetCursorPosX(pad + Px(18f));
        ImGui.TextColored(new Vector4(1f, 1f, 1f, 0.5f), Loc.T("os.yapper_pinned"));
        card.Draw(ctx, dto);
    }
}

/// <summary>Small UI helpers shared by more than one Yapper screen.</summary>
internal static class YapperUi
{
    /// <summary>A "12 Followers" stat pair rendered as one clickable unit that brightens on hover.</summary>
    public static void DrawStatLink(string id, int count, string label, Action onClick)
    {
        var countText = count.ToString();
        var countW = ImGui.CalcTextSize(countText).X;
        var gap = Px(4f);
        var tl = ImGui.GetCursorScreenPos();
        if (ImGui.InvisibleButton(id, new Vector2(countW + gap + ImGui.CalcTextSize(label).X, ImGui.GetTextLineHeight())))
        {
            onClick();
        }
        HandOnHover();
        var hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        dl.AddText(tl, 0xFFFFFFFFu, countText);
        dl.AddText(tl + new Vector2(countW + gap, 0f),
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, hovered ? 0.75f : 0.45f)), label);
    }
}
