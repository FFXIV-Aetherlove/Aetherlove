using System;
using System.Numerics;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Yapper.Screens;

/// <summary>A hashtag's public timeline.</summary>
internal sealed class TagFeedScreen
{
    private readonly IYapperHost _host;
    private readonly YapperStore _store;
    private readonly Action _back;

    private string _tag = string.Empty;
    private FeedPane? _pane;

    public TagFeedScreen(IYapperHost host, YapperStore store, Action back)
    {
        _host = host;
        _store = store;
        _back = back;
    }

    public void Open(string tag)
    {
        _tag = tag.TrimStart('#');
        var captured = _tag;
        _pane = new FeedPane(_store, cursor => _host.GetTagFeedAsync(captured, cursor), _ => { });
    }

    public void Draw(OsAppContext ctx, YapCard card)
    {
        var pad = Px(14f);
        ImGui.SetCursorPos(new Vector2(pad, Px(10f)));
        if (ImGui.InvisibleButton("##yapTagBack", new Vector2(Px(28f), Px(24f))))
        {
            _back();
        }
        HandOnHover();
        IconDraw.AddCentered(ImGui.GetWindowDrawList(), FontAwesomeIcon.ArrowLeft, Px(15f),
            ImGui.GetItemRectMin() + ImGui.GetItemRectSize() * 0.5f,
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.8f)));
        ImGui.SameLine();
        ImGui.SetCursorPosY(Px(12f));
        ImGui.TextColored(ctx.Theme.Accent, $"#{_tag}");

        PushScrollbarStyle();
        using (var child = ImRaii.Child("##yapTagList", new Vector2(0f, 0f), false))
        {
            if (child.Success)
            {
                _pane?.DrawCards(ctx, card, "os.yapper_tag_empty");
            }
        }
        PopScrollbarStyle();
    }
}
