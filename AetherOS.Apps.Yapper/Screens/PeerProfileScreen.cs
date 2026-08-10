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

/// <summary>Another user's profile: header with follow/bell/overflow actions, counts, bio, joined
/// date and their posts. NSFW-matrix mismatches render the blurred variant with a reveal step.</summary>
internal sealed class PeerProfileScreen
{
    private readonly IYapperHost _host;
    private readonly YapperStore _store;
    private readonly YapperMediaCache _mediaCache;
    private readonly Action _back;

    private Guid _profileId;
    private volatile YapperProfileViewDto? _view;
    private volatile bool _loading;
    private volatile string? _error;
    private readonly FeedPane?[] _panes = new FeedPane?[3];
    private bool _revealed;

    public PeerProfileScreen(IYapperHost host, YapperStore store, YapperMediaCache mediaCache, Action back,
        Action<Guid> openDm, Action<Guid, string> report, Action<Guid, string, bool, Action> moderate,
        Action<Guid, bool> openFollowList, Action onFollowChanged, Action<YapDto> openYap)
    {
        _openYap = openYap;
        _host = host;
        _store = store;
        _mediaCache = mediaCache;
        _back = back;
        _openDm = openDm;
        _report = report;
        _moderate = moderate;
        _openFollowList = openFollowList;
        _onFollowChanged = onFollowChanged;
    }

    private readonly Action<Guid> _openDm;
    private readonly Action<Guid, string> _report;

    /// <summary>Profile id, handle, whether it is a block, and what to do once it goes through.</summary>
    private readonly Action<Guid, string, bool, Action> _moderate;
    private readonly Action<Guid, bool> _openFollowList;
    private readonly Action _onFollowChanged;
    private readonly Action<YapDto> _openYap;

    private static readonly YapperProfileTab[] Tabs = [YapperProfileTab.Posts, YapperProfileTab.Replies, YapperProfileTab.Media];
    private int _tab;

    private PinnedYapSlot? _pinnedSlot;

    public void Open(Guid profileId)
    {
        _profileId = profileId;
        _view = null;
        _error = null;
        _revealed = false;
        _tab = 0;
        for (var i = 0; i < Tabs.Length; i++)
        {
            var tab = Tabs[i];
            _panes[i] = new FeedPane(_store,
                cursor => _host.GetProfileYapsAsync(profileId, tab, cursor), _ => { });
        }
        _pinnedSlot = new PinnedYapSlot(_host, _store);
        Refresh();
    }

    private void Refresh()
    {
        if (_loading)
        {
            return;
        }
        _loading = true;
        var id = _profileId;
        _ = Task.Run(async () =>
        {
            try
            {
                _view = await _host.GetProfileAsync(id).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _error = AetherLove.Services.HubErrorText.Localize(ex);
            }
            finally
            {
                _loading = false;
            }
        });
    }

    public void Draw(OsAppContext ctx, YapCard card)
    {
        var pad = Px(16f);
        var winW = ImGui.GetWindowSize().X;

        ImGui.SetCursorPos(new Vector2(pad, Px(10f)));
        if (ImGui.InvisibleButton("##yapPeerBack", new Vector2(Px(28f), Px(24f))))
        {
            _back();
        }
        HandOnHover();
        IconDraw.AddCentered(ImGui.GetWindowDrawList(), FontAwesomeIcon.ArrowLeft, Px(15f),
            ImGui.GetItemRectMin() + ImGui.GetItemRectSize() * 0.5f,
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.8f)));

        var view = _view;
        if (view is null)
        {
            ImGui.Dummy(new Vector2(0f, ImGui.GetWindowSize().Y * 0.35f));
            var text = _error ?? Loc.T("os.yapper_loading");
            ImGui.PushTextWrapPos(winW - pad);
            ImGui.SetCursorPosX(Math.Max(pad, (winW - ImGui.CalcTextSize(text).X) * 0.5f));
            ImGui.TextColored(new Vector4(1f, 1f, 1f, 0.5f), text);
            ImGui.PopTextWrapPos();
            return;
        }

        ImGui.SameLine();
        ImGui.SetCursorPosY(Px(12f));
        ImGui.TextUnformatted(view.DisplayName);

        if (view.Handicapped && !_revealed)
        {
            ImGui.Dummy(new Vector2(0f, ImGui.GetWindowSize().Y * 0.3f));
            var notice = Loc.T("os.yapper_profile_nsfw");
            ImGui.PushTextWrapPos(winW - pad);
            ImGui.SetCursorPosX(Math.Max(pad, (winW - ImGui.CalcTextSize(notice).X) * 0.5f));
            ImGui.TextColored(new Vector4(1f, 1f, 1f, 0.55f), notice);
            ImGui.PopTextWrapPos();
            return;
        }

        PushScrollbarStyle();
        using var scroll = ImRaii.Child("##yapPeerScroll", new Vector2(0f, 0f), false);
        if (!scroll.Success)
        {
            PopScrollbarStyle();
            return;
        }

        // The X-style header: full-width banner (8:3) with the avatar straddling its bottom edge.
        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var bannerH = winW * (3f / 8f);
        var bannerTex = view.Banner is { Length: > 0 } bannerBytes
            ? _mediaCache.GetInline($"peer_banner_{view.ProfileId:N}", bannerBytes)?.GetWrapOrDefault()
            : null;
        if (bannerTex is not null)
        {
            var (uv0, uv1) = CoverFitUvs(bannerTex.Width, bannerTex.Height, winW, bannerH);
            dl.AddImage(bannerTex.Handle, origin, origin + new Vector2(winW, bannerH), uv0, uv1);
        }
        else
        {
            dl.AddRectFilledMultiColor(origin, origin + new Vector2(winW, bannerH),
                ImGui.GetColorU32(ctx.Theme.Accent with { W = 0.45f }),
                ImGui.GetColorU32(ctx.Theme.Accent with { W = 0.45f }),
                ImGui.GetColorU32(ctx.Theme.Accent with { W = 0.15f }),
                ImGui.GetColorU32(ctx.Theme.Accent with { W = 0.15f }));
        }

        var avatarR = Px(34f);
        var avatarCenter = origin + new Vector2(pad + avatarR, bannerH);
        dl.AddCircleFilled(avatarCenter, avatarR + Px(3f), ImGui.GetColorU32(new Vector4(0.06f, 0.07f, 0.10f, 1f)));
        var avatar = view.Avatar is { Length: > 0 } bytes ? _mediaCache.GetAvatar(view.ProfileId, bytes) : null;
        if (avatar?.GetWrapOrDefault() is { } wrap)
        {
            dl.AddImageRounded(wrap.Handle, avatarCenter - new Vector2(avatarR, avatarR),
                avatarCenter + new Vector2(avatarR, avatarR), Vector2.Zero, Vector2.One, 0xFFFFFFFFu, avatarR);
        }
        else
        {
            dl.AddCircleFilled(avatarCenter, avatarR, ImGui.GetColorU32(ctx.Theme.Accent with { W = 0.35f }));
        }
        AvatarRings.Draw(dl, avatarCenter, avatarR, view.FrameRef);
        ImGui.SetCursorScreenPos(origin + new Vector2(0f, bannerH));

        // The follow pill, right of the avatar per the reference layout.
        var pillLabel = Loc.T(view.FollowedByMe ? "os.yapper_unfollow" : "os.yapper_follow");
        var pillW = ImGui.CalcTextSize(pillLabel).X + Px(28f);
        ImGui.SetCursorPos(new Vector2(winW - pillW - pad, ImGui.GetCursorPosY() + Px(12f)));
        var pillTl = ImGui.GetCursorScreenPos();
        if (ImGui.InvisibleButton("##yapPeerFollow", new Vector2(pillW, Px(28f))))
        {
            ToggleFollow(view);
        }
        HandOnHover();
        dl.AddRectFilled(pillTl, pillTl + new Vector2(pillW, Px(28f)),
            ImGui.GetColorU32(view.FollowedByMe ? new Vector4(1f, 1f, 1f, 0.10f) : ctx.Theme.Accent), Px(14f));
        dl.AddText(pillTl + new Vector2(Px(14f), (Px(28f) - ImGui.GetTextLineHeight()) * 0.5f), 0xFFFFFFFFu, pillLabel);

        // The message (DM) button, envelope in a ring left of the follow pill, per the X layout.
        ImGui.SetCursorScreenPos(pillTl - new Vector2(Px(36f), 0f));
        var dmTl = ImGui.GetCursorScreenPos();
        if (ImGui.InvisibleButton("##yapPeerDm", new Vector2(Px(28f), Px(28f))))
        {
            _openDm(view.ProfileId);
        }
        HandOnHover();
        dl.AddCircle(dmTl + new Vector2(Px(14f), Px(14f)), Px(14f),
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.35f)), 32, Px(1.2f));
        IconDraw.AddCentered(dl, FontAwesomeIcon.Envelope, Px(12f), dmTl + new Vector2(Px(14f), Px(14f)),
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, ImGui.IsItemHovered() ? 1f : 0.75f)));

        DrawOverflow(view, winW, pad, pillW);

        ImGui.Dummy(new Vector2(0f, Px(46f)));
        ImGui.SetCursorPosX(pad);
        using (ctx.TitleFont?.Push())
        {
            ImGui.TextUnformatted(view.DisplayName);
        }
        ImGui.SetCursorPosX(pad);
        var meta = $"@{view.Handle}";
        if (view.FollowsMe)
        {
            meta += $"  ·  {Loc.T("os.yapper_follows_you")}";
        }
        ImGui.TextColored(new Vector4(1f, 1f, 1f, 0.55f), meta);

        if (!string.IsNullOrEmpty(view.Bio))
        {
            ImGui.Dummy(new Vector2(0f, Px(6f)));
            ImGui.SetCursorPosX(pad);
            AetherLove.Emoji.ParsedMessage.Parse(view.Bio).DrawWrapped("##yapPeerBio", winW - pad * 2f);
        }

        ImGui.Dummy(new Vector2(0f, Px(6f)));
        ImGui.SetCursorPosX(pad);
        ImGui.TextColored(new Vector4(1f, 1f, 1f, 0.45f),
            string.Format(Loc.T("os.yapper_joined"), view.JoinedAtUtc.ToLocalTime().ToString("MMMM yyyy")));
        ImGui.SetCursorPosX(pad);
        YapperUi.DrawStatLink("##yapPeerFollowingStat", view.FollowingCount, Loc.T("os.yapper_following"),
            () => _openFollowList(view.ProfileId, false));
        ImGui.SameLine();
        YapperUi.DrawStatLink("##yapPeerFollowersStat", view.FollowerCount, Loc.T("os.yapper_followers"),
            () => _openFollowList(view.ProfileId, true));

        ImGui.Dummy(new Vector2(0f, Px(10f)));
        DrawTabs(ctx, winW);
        if (Tabs[_tab] == YapperProfileTab.Media)
        {
            _panes[_tab]?.DrawMediaMosaic(ctx, _mediaCache, "os.yapper_peer_empty", _openYap);
        }
        else
        {
            if (Tabs[_tab] == YapperProfileTab.Posts && _panes[_tab] is { } postsPane)
            {
                _pinnedSlot?.Draw(ctx, card, view.PinnedYapId);
                postsPane.ExcludeId = view.PinnedYapId;
            }
            _panes[_tab]?.DrawCards(ctx, card, "os.yapper_peer_empty");
        }
        PopScrollbarStyle();
    }

    private void DrawOverflow(YapperProfileViewDto view, float winW, float pad, float pillW)
    {
        var dl = ImGui.GetWindowDrawList();
        ImGui.SetCursorPos(new Vector2(winW - pillW - pad - Px(38f), ImGui.GetCursorPosY() - Px(28f)));
        ImGui.InvisibleButton("##yapPeerMore", new Vector2(Px(28f), Px(28f)));
        HandOnHover();
        IconDraw.AddCentered(dl, FontAwesomeIcon.EllipsisH, Px(14f),
            ImGui.GetItemRectMin() + ImGui.GetItemRectSize() * 0.5f,
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.6f)));
        if (ImGui.IsItemClicked())
        {
            ImGui.OpenPopup("##yapPeerMenu");
        }
        if (ImGui.BeginPopup("##yapPeerMenu"))
        {
            if (DrawIconMenuItem(FontAwesomeIcon.VolumeMute,
                Loc.T(view.MutedByMe ? "os.yapper_menu_unmute" : "os.yapper_menu_mute")))
            {
                ImGui.CloseCurrentPopup();
                if (view.MutedByMe)
                {
                    // Unmuting gives nothing away and undoes itself, so it asks nothing.
                    _view = view with { MutedByMe = false };
                    _ = Task.Run(() => _host.SetMuteAsync(view.ProfileId, false, default));
                }
                else
                {
                    _moderate(view.ProfileId, view.Handle, false, () => _view = view with { MutedByMe = true });
                }
            }
            if (DrawIconMenuItem(FontAwesomeIcon.Ban, Loc.T("os.yapper_menu_block")))
            {
                ImGui.CloseCurrentPopup();
                _moderate(view.ProfileId, view.Handle, true, _back);
            }
            if (DrawIconMenuItem(FontAwesomeIcon.Flag, Loc.T("os.yapper_menu_report")))
            {
                ImGui.CloseCurrentPopup();
                _report(view.ProfileId, view.Handle);
            }
            ImGui.EndPopup();
        }
    }

    private void DrawTabs(OsAppContext ctx, float winW)
    {
        var dl = ImGui.GetWindowDrawList();
        var tabH = Px(34f);
        var slotW = winW / Tabs.Length;
        var baseY = ImGui.GetCursorScreenPos().Y;
        for (var i = 0; i < Tabs.Length; i++)
        {
            var label = Loc.T(Tabs[i] switch
            {
                YapperProfileTab.Replies => "os.yapper_tab_replies",
                YapperProfileTab.Media => "os.yapper_tab_media",
                _ => "os.yapper_tab_posts",
            });
            var tl = new Vector2(ImGui.GetWindowPos().X + slotW * i, baseY);
            ImGui.SetCursorScreenPos(tl);
            if (ImGui.InvisibleButton($"##yapPeerTab{i}", new Vector2(slotW, tabH)))
            {
                _tab = i;
            }
            HandOnHover();
            var active = _tab == i;
            var size = ImGui.CalcTextSize(label);
            dl.AddText(tl + new Vector2((slotW - size.X) * 0.5f, (tabH - size.Y) * 0.5f),
                ImGui.GetColorU32(new Vector4(1f, 1f, 1f, active ? 0.95f : 0.45f)), label);
            if (active)
            {
                dl.AddRectFilled(
                    tl + new Vector2(slotW * 0.5f - Px(20f), tabH - Px(2f)),
                    tl + new Vector2(slotW * 0.5f + Px(20f), tabH),
                    ImGui.GetColorU32(ctx.Theme.Accent), Px(1f));
            }
        }
        ImGui.SetCursorScreenPos(new Vector2(ImGui.GetWindowPos().X, baseY + tabH));
    }

    private void ToggleFollow(YapperProfileViewDto view)
    {
        var target = !view.FollowedByMe;
        _view = view with
        {
            FollowedByMe = target,
            FollowerCount = Math.Max(0, view.FollowerCount + (target ? 1 : -1)),
        };
        _ = Task.Run(async () =>
        {
            try
            {
                if (target)
                {
                    await _host.FollowAsync(view.ProfileId).ConfigureAwait(false);
                }
                else
                {
                    await _host.UnfollowAsync(view.ProfileId).ConfigureAwait(false);
                }
                _onFollowChanged();
            }
            catch (Exception)
            {
                _view = view;
            }
        });
    }
}
