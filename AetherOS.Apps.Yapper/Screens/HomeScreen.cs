using System;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove.Services.Localization;
using AetherLove.Shared.Yapper;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Yapper.Screens;

/// <summary>The Home feed: Following and For You sub-tabs, each its own pane with independent scroll
/// and cursor state.</summary>
internal sealed class HomeScreen
{
    private readonly FeedPane _following;
    private FeedPane? _forYou;
    private readonly Func<FeedPane?> _forYouFactory;
    private readonly Action<OsAppContext> _drawNotifications;
    private readonly Func<int> _unreadNotifications;
    private readonly Action _onNotificationsOpened;
    private int _tab;

    public HomeScreen(YapperStore store, Func<DateTimeOffset?, Task<YapPageDto>> followingLoader,
        Func<FeedPane?> forYouFactory, Action<Guid> markSeen,
        Action<OsAppContext> drawNotifications, Func<int> unreadNotifications, Action onNotificationsOpened)
    {
        _following = new FeedPane(store, followingLoader, markSeen);
        _forYouFactory = forYouFactory;
        _drawNotifications = drawNotifications;
        _unreadNotifications = unreadNotifications;
        _onNotificationsOpened = onNotificationsOpened;
    }

    public FeedPane Following => _following;

    public bool NotificationsTabActive => _tab == 2;

    public void OpenNotificationsTab()
    {
        _tab = 2;
        _onNotificationsOpened();
    }

    public void OnShow()
    {
        if (_following.LoadedOnce)
        {
            _following.Refresh();
        }
        // For You has no cursor: every visit deals a fresh ranked hand.
        if (_forYou is not null)
        {
            _forYou = _forYouFactory();
        }
    }

    public void Draw(OsAppContext ctx, YapCard card)
    {
        var winW = ImGui.GetWindowSize().X;
        var tabH = Px(40f);
        var dl = ImGui.GetWindowDrawList();
        var winPos = ImGui.GetWindowPos();

        var tabW = winW / 3f;
        for (var i = 0; i < 3; i++)
        {
            var label = Loc.T(i switch
            {
                0 => "os.yapper_tab_following",
                1 => "os.yapper_tab_foryou",
                _ => "os.yapper_tab_notifications",
            });
            var tl = winPos + new Vector2(tabW * i, 0f);
            ImGui.SetCursorScreenPos(tl);
            if (ImGui.InvisibleButton($"##yapHomeTab{i}", new Vector2(tabW, tabH)))
            {
                if (i == 1 && _tab != 1 && _forYou is not null)
                {
                    _forYou = _forYouFactory();
                }
                if (i == 2 && _tab != 2)
                {
                    _onNotificationsOpened();
                }
                _tab = i;
            }
            HandOnHover();
            var active = _tab == i;
            var color = active ? new Vector4(1f, 1f, 1f, 0.95f) : new Vector4(1f, 1f, 1f, 0.45f);
            var size = ImGui.CalcTextSize(label);
            var textPos = tl + new Vector2((tabW - size.X) * 0.5f, (tabH - size.Y) * 0.5f);
            dl.AddText(textPos, ImGui.GetColorU32(color), label);
            if (i == 2 && _unreadNotifications() is > 0 and var unread)
            {
                var badge = unread > 99 ? "99+" : unread.ToString();
                var bsz = ImGui.CalcTextSize(badge);
                var bc = textPos + new Vector2(size.X + Px(9f), size.Y * 0.5f);
                dl.AddCircleFilled(bc, MathF.Max(Px(7f), bsz.X * 0.5f + Px(3f)),
                    ImGui.GetColorU32(new Vector4(0.90f, 0.22f, 0.30f, 1f)));
                dl.AddText(bc - bsz * 0.5f, 0xFFFFFFFFu, badge);
            }
            if (active)
            {
                var underY = tl.Y + tabH - Px(2f);
                dl.AddRectFilled(
                    new Vector2(tl.X + tabW * 0.5f - Px(24f), underY),
                    new Vector2(tl.X + tabW * 0.5f + Px(24f), underY + Px(2f)),
                    ImGui.GetColorU32(ctx.Theme.Accent), Px(1f));
            }
        }
        ImGui.SetCursorPos(new Vector2(0f, tabH));

        if (_tab == 2)
        {
            _drawNotifications(ctx);
            return;
        }

        PushScrollbarStyle();
        using (var child = ImRaii.Child($"##yapHomeFeed{_tab}", new Vector2(0f, 0f), false))
        {
            if (child.Success)
            {
                if (_tab == 0)
                {
                    _following.DrawCards(ctx, card, "os.yapper_home_empty");
                }
                else
                {
                    _forYou ??= _forYouFactory();
                    if (_forYou is null)
                    {
                        ImGui.Dummy(new Vector2(0f, ImGui.GetWindowSize().Y * 0.35f));
                        var empty = Loc.T("os.yapper_foryou_empty");
                        ImGui.SetCursorPosX((winW - ImGui.CalcTextSize(empty).X) * 0.5f);
                        ImGui.TextColored(new Vector4(1f, 1f, 1f, 0.45f), empty);
                    }
                    else
                    {
                        _forYou.DrawCards(ctx, card, "os.yapper_foryou_empty");
                    }
                }
            }
        }
        PopScrollbarStyle();
    }
}
