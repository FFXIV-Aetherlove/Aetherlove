using System;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove.Services.Localization;
using AetherLove.Shared.Yapper;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Yapper.Screens;

/// <summary>The private bookmarks list.</summary>
internal sealed class BookmarksScreen
{
    private readonly FeedPane _pane;
    private readonly Action _back;

    public BookmarksScreen(YapperStore store, Func<DateTimeOffset?, Task<YapPageDto>> loader, Action back)
    {
        _pane = new FeedPane(store, loader, _ => { });
        _back = back;
    }

    public void OnShow() => _pane.Refresh();

    public void Draw(OsAppContext ctx, YapCard card)
    {
        var pad = Px(14f);
        ImGui.SetCursorPos(new Vector2(pad, Px(10f)));
        if (ImGui.InvisibleButton("##yapBookmarksBack", new Vector2(Px(28f), Px(24f))))
        {
            _back();
        }
        HandOnHover();
        IconDraw.AddCentered(ImGui.GetWindowDrawList(), FontAwesomeIcon.ArrowLeft, Px(15f),
            ImGui.GetItemRectMin() + ImGui.GetItemRectSize() * 0.5f,
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.8f)));
        ImGui.SameLine();
        ImGui.SetCursorPosY(Px(12f));
        ImGui.TextUnformatted(Loc.T("os.yapper_bookmarks_title"));

        PushScrollbarStyle();
        using (var child = ImRaii.Child("##yapBookmarksList", new Vector2(0f, 0f), false))
        {
            if (child.Success)
            {
                _pane.DrawCards(ctx, card, "os.yapper_bookmarks_empty");
            }
        }
        PopScrollbarStyle();
    }
}
