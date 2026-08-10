using System;
using System.Collections.Generic;
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

/// <summary>The follower / following list for any profile: paged user rows with follow pills,
/// tap-through to the peer profile, and infinite scroll on the keyset cursor.</summary>
internal sealed class FollowListScreen(
    IYapperHost host,
    YapperMediaCache mediaCache,
    Func<Guid?> myProfileId,
    Action back,
    Action<Guid> openProfile,
    Action onFollowChanged)
{
    private Guid _profileId;
    private bool _followers;
    private readonly List<YapperUserRowDto> _rows = [];
    private DateTimeOffset? _cursor;
    private bool _hasMore;
    private volatile bool _loading;
    private bool _loadedOnce;
    private int _generation;

    public void Open(Guid profileId, bool followers)
    {
        _profileId = profileId;
        _followers = followers;
        _rows.Clear();
        _cursor = null;
        _hasMore = true;
        _loadedOnce = false;
        _generation++;
        LoadMore();
    }

    private void LoadMore()
    {
        if (_loading || !_hasMore)
        {
            return;
        }
        _loading = true;
        var generation = _generation;
        var cursor = _cursor;
        _ = Task.Run(async () =>
        {
            try
            {
                var page = _followers
                    ? await host.GetFollowersAsync(_profileId, cursor).ConfigureAwait(false)
                    : await host.GetFollowingAsync(_profileId, cursor).ConfigureAwait(false);
                if (generation != _generation)
                {
                    return;
                }
                _rows.AddRange(page.Rows);
                _cursor = page.NextCursor;
                _hasMore = page.NextCursor is not null;
                _loadedOnce = true;
            }
            catch (Exception)
            {
            }
            finally
            {
                _loading = false;
            }
        });
    }

    public void Draw(OsAppContext ctx)
    {
        var pad = Px(14f);
        ImGui.SetCursorPos(new Vector2(pad, Px(10f)));
        if (ImGui.InvisibleButton("##yapFollowListBack", new Vector2(Px(28f), Px(24f))))
        {
            back();
        }
        HandOnHover();
        IconDraw.AddCentered(ImGui.GetWindowDrawList(), FontAwesomeIcon.ArrowLeft, Px(15f),
            ImGui.GetItemRectMin() + ImGui.GetItemRectSize() * 0.5f,
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.8f)));
        ImGui.SameLine();
        ImGui.SetCursorPosY(Px(12f));
        ImGui.TextUnformatted(Loc.T(_followers ? "os.yapper_followers" : "os.yapper_following"));

        PushScrollbarStyle();
        using (var child = ImRaii.Child("##yapFollowList", new Vector2(0f, 0f), false))
        {
            if (child.Success)
            {
                ImGui.Dummy(new Vector2(0f, Px(4f)));
                if (_rows.Count == 0)
                {
                    ImGui.Dummy(new Vector2(0f, ImGui.GetWindowSize().Y * 0.35f));
                    var empty = Loc.T(_loadedOnce && !_loading ? "os.yapper_followlist_empty" : "os.yapper_loading");
                    ImGui.SetCursorPosX((ImGui.GetWindowSize().X - ImGui.CalcTextSize(empty).X) * 0.5f);
                    ImGui.TextColored(new Vector4(1f, 1f, 1f, 0.45f), empty);
                }
                else
                {
                    for (var i = 0; i < _rows.Count; i++)
                    {
                        DrawRow(ctx, i, pad);
                    }
                    if (_hasMore && ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - Px(200f))
                    {
                        LoadMore();
                    }
                }
            }
        }
        PopScrollbarStyle();
    }

    private void DrawRow(OsAppContext ctx, int index, float pad)
    {
        var row = _rows[index];
        var winW = ImGui.GetWindowSize().X;
        var rowH = Px(52f);
        var tl = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();

        var isMe = row.ProfileId == myProfileId();

        // The follow pill is submitted first so its click wins over the row's open target.
        var pillLabel = Loc.T(row.FollowedByMe ? "os.yapper_unfollow" : "os.yapper_follow");
        var pillW = isMe ? 0f : ImGui.CalcTextSize(pillLabel).X + Px(24f);
        if (!isMe)
        {
            ImGui.SetCursorScreenPos(tl + new Vector2(winW - pillW - pad, (rowH - Px(26f)) * 0.5f));
            var pillTl = ImGui.GetCursorScreenPos();
            if (ImGui.InvisibleButton($"##yapFlFollow{index}", new Vector2(pillW, Px(26f))))
            {
                ToggleFollow(index);
                row = _rows[index];
                pillLabel = Loc.T(row.FollowedByMe ? "os.yapper_unfollow" : "os.yapper_follow");
            }
            HandOnHover();
            dl.AddRectFilled(pillTl, pillTl + new Vector2(pillW, Px(26f)),
                ImGui.GetColorU32(row.FollowedByMe ? new Vector4(1f, 1f, 1f, 0.10f) : ctx.Theme.Accent), Px(13f));
            dl.AddText(pillTl + new Vector2(Px(12f), (Px(26f) - ImGui.GetTextLineHeight()) * 0.5f),
                0xFFFFFFFFu, pillLabel);
        }

        ImGui.SetCursorScreenPos(tl);
        if (ImGui.InvisibleButton($"##yapFlRow{index}", new Vector2(winW - pillW - pad * 2f, rowH)))
        {
            openProfile(row.ProfileId);
        }
        HandOnHover();

        var center = tl + new Vector2(pad + Px(18f), rowH * 0.5f);
        var avatar = row.Avatar is { Length: > 0 } bytes ? mediaCache.GetAvatar(row.ProfileId, bytes) : null;
        if (avatar?.GetWrapOrDefault() is { } wrap)
        {
            dl.AddImageRounded(wrap.Handle, center - new Vector2(Px(18f), Px(18f)), center + new Vector2(Px(18f), Px(18f)),
                Vector2.Zero, Vector2.One, 0xFFFFFFFFu, Px(18f));
        }
        else
        {
            dl.AddCircleFilled(center, Px(18f), ImGui.GetColorU32(ctx.Theme.Accent with { W = 0.35f }));
        }
        AvatarRings.Draw(dl, center, Px(18f), row.FrameRef);
        dl.AddText(tl + new Vector2(pad + Px(46f), Px(9f)), 0xFFFFFFFFu, row.DisplayName);
        var handleColor = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.45f));
        dl.AddText(tl + new Vector2(pad + Px(46f), Px(27f)), handleColor, $"@{row.Handle}");
        if (row.FollowsMe && !isMe)
        {
            var handleW = ImGui.CalcTextSize($"@{row.Handle}").X;
            dl.AddText(tl + new Vector2(pad + Px(46f) + handleW + Px(8f), Px(27f)), handleColor,
                Loc.T("os.yapper_follows_you"));
        }
        ImGui.SetCursorScreenPos(new Vector2(tl.X, tl.Y + rowH));
    }

    private void ToggleFollow(int index)
    {
        var row = _rows[index];
        var target = !row.FollowedByMe;
        _rows[index] = row with { FollowedByMe = target };
        _ = Task.Run(async () =>
        {
            try
            {
                if (target)
                {
                    await host.FollowAsync(row.ProfileId).ConfigureAwait(false);
                }
                else
                {
                    await host.UnfollowAsync(row.ProfileId).ConfigureAwait(false);
                }
                onFollowChanged();
            }
            catch (Exception)
            {
            }
        });
    }
}
