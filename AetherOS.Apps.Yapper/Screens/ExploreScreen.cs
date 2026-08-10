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

/// <summary>Explore: trending tags and suggested users. The search box arrives with the polish phase;
/// the idle layout it drops into is already here.</summary>
internal sealed class ExploreScreen
{
    private readonly IYapperHost _host;
    private readonly YapperMediaCache _mediaCache;
    private readonly Func<Guid?> _myProfileId;
    private readonly Action<string> _openTag;
    private readonly Action<Guid> _openProfile;

    private volatile YapperTrendingTagDto[]? _trending;
    private volatile YapperUserRowDto[]? _suggested;
    private volatile bool _loading;

    private string _query = string.Empty;
    private string _searchedQuery = string.Empty;
    private DateTime _queryEditedAt;
    private volatile bool _searching;
    private volatile YapperUserRowDto[]? _userResults;
    private volatile YapDto[]? _yapResults;

    public ExploreScreen(IYapperHost host, YapperMediaCache mediaCache, Func<Guid?> myProfileId,
        Action<string> openTag, Action<Guid> openProfile, Action onFollowChanged)
    {
        _host = host;
        _mediaCache = mediaCache;
        _myProfileId = myProfileId;
        _openTag = openTag;
        _openProfile = openProfile;
        _onFollowChanged = onFollowChanged;
    }

    private readonly Action _onFollowChanged;

    public void OnShow()
    {
        Refresh();
        // Drop the cached result set so a revisit re-runs the standing query with fresh follow state.
        _searchedQuery = string.Empty;
    }

    private void Refresh()
    {
        if (_loading)
        {
            return;
        }
        _loading = true;
        _ = Task.Run(async () =>
        {
            try
            {
                _trending = await _host.GetTrendingAsync().ConfigureAwait(false);
                _suggested = await _host.GetSuggestedUsersAsync().ConfigureAwait(false);
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

    public void Draw(OsAppContext ctx, YapCard card, YapperStore store)
    {
        if (_trending is null && !_loading)
        {
            Refresh();
        }

        var pad = Px(16f);
        ImGui.Dummy(new Vector2(0f, Px(10f)));
        ImGui.SetCursorPosX(pad);
        ImGui.SetNextItemWidth(ImGui.GetWindowSize().X - pad * 2f);
        var query = _query;
        if (ImGui.InputTextWithHint("##yapSearch", Loc.T("os.yapper_search_hint"), ref query, 100))
        {
            _query = query;
            _queryEditedAt = DateTime.UtcNow;
        }
        MaybeRunSearch();

        PushScrollbarStyle();
        using var scroll = ImRaii.Child("##yapExplore", new Vector2(0f, 0f), false);
        if (!scroll.Success)
        {
            PopScrollbarStyle();
            return;
        }

        if (_query.Trim().Length >= 2)
        {
            DrawSearchResults(ctx, card, store, pad);
            PopScrollbarStyle();
            return;
        }

        ImGui.Dummy(new Vector2(0f, Px(6f)));
        ImGui.SetCursorPosX(pad);
        using (ctx.HeadingFont?.Push())
        {
            ImGui.TextUnformatted(Loc.T("os.yapper_trending_title"));
        }
        ImGui.Dummy(new Vector2(0f, Px(4f)));

        var trending = _trending;
        if (trending is null or { Length: 0 })
        {
            ImGui.SetCursorPosX(pad);
            ImGui.TextColored(new Vector4(1f, 1f, 1f, 0.45f),
                Loc.T(_loading && trending is null ? "os.yapper_loading" : "os.yapper_trending_empty"));
        }
        else
        {
            for (var i = 0; i < trending.Length; i++)
            {
                DrawTrendRow(ctx, i, trending[i], pad);
            }
        }

        ImGui.Dummy(new Vector2(0f, Px(16f)));
        ImGui.SetCursorPosX(pad);
        using (ctx.HeadingFont?.Push())
        {
            ImGui.TextUnformatted(Loc.T("os.yapper_suggested_title"));
        }
        ImGui.Dummy(new Vector2(0f, Px(4f)));

        var suggested = _suggested;
        if (suggested is null or { Length: 0 })
        {
            ImGui.SetCursorPosX(pad);
            ImGui.TextColored(new Vector4(1f, 1f, 1f, 0.45f), Loc.T("os.yapper_suggested_empty"));
        }
        else
        {
            foreach (var row in suggested)
            {
                DrawUserRow(ctx, row, pad);
            }
        }
        ImGui.Dummy(new Vector2(0f, Px(14f)));
        PopScrollbarStyle();
    }

    private void MaybeRunSearch()
    {
        var trimmed = _query.Trim();
        if (trimmed.Length < 2 || _searching || trimmed == _searchedQuery
            || (DateTime.UtcNow - _queryEditedAt).TotalSeconds < 0.5)
        {
            return;
        }
        _searching = true;
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                if (trimmed.StartsWith('#'))
                {
                    _userResults = [];
                    _yapResults = [];
                }
                else
                {
                    _userResults = await _host.SearchUsersAsync(trimmed).ConfigureAwait(false);
                    var page = await _host.SearchYapsAsync(trimmed, null).ConfigureAwait(false);
                    _yapResults = page.Yaps;
                }
                _searchedQuery = trimmed;
            }
            catch (Exception)
            {
            }
            finally
            {
                _searching = false;
            }
        });
    }

    private void DrawSearchResults(OsAppContext ctx, YapCard card, YapperStore store, float pad)
    {
        var trimmed = _query.Trim();
        if (trimmed.StartsWith('#'))
        {
            ImGui.Dummy(new Vector2(0f, Px(10f)));
            ImGui.SetCursorPosX(pad);
            if (ImGui.Button($"{string.Format(Loc.T("os.yapper_open_tag"), trimmed)}##yapOpenTag"))
            {
                _openTag(trimmed);
            }
            HandOnHover();
            return;
        }
        if (_searching && _searchedQuery != trimmed)
        {
            ImGui.Dummy(new Vector2(0f, Px(10f)));
            ImGui.SetCursorPosX(pad);
            ImGui.TextColored(new Vector4(1f, 1f, 1f, 0.45f), Loc.T("os.yapper_loading"));
            return;
        }

        var users = _userResults ?? [];
        if (users.Length > 0)
        {
            ImGui.Dummy(new Vector2(0f, Px(6f)));
            ImGui.SetCursorPosX(pad);
            using (ctx.HeadingFont?.Push())
            {
                ImGui.TextUnformatted(Loc.T("os.yapper_search_people"));
            }
            foreach (var row in users)
            {
                DrawUserRow(ctx, row, pad);
            }
        }
        var yaps = _yapResults ?? [];
        if (yaps.Length > 0)
        {
            ImGui.Dummy(new Vector2(0f, Px(6f)));
            ImGui.SetCursorPosX(pad);
            using (ctx.HeadingFont?.Push())
            {
                ImGui.TextUnformatted(Loc.T("os.yapper_search_yaps"));
            }
            ImGui.Dummy(new Vector2(0f, Px(4f)));
            foreach (var dto in yaps)
            {
                store.Upsert(dto);
                card.Draw(ctx, dto);
            }
        }
        if (users.Length == 0 && yaps.Length == 0 && !_searching)
        {
            ImGui.Dummy(new Vector2(0f, Px(10f)));
            ImGui.SetCursorPosX(pad);
            ImGui.TextColored(new Vector4(1f, 1f, 1f, 0.45f), Loc.T("os.yapper_search_empty"));
        }
    }

    private void DrawTrendRow(OsAppContext ctx, int index, YapperTrendingTagDto trend, float pad)
    {
        var winW = ImGui.GetWindowSize().X;
        var rowH = Px(44f);
        var tl = ImGui.GetCursorScreenPos();
        if (ImGui.InvisibleButton($"##yapTrend{index}", new Vector2(winW, rowH)))
        {
            _openTag(trend.Tag);
        }
        HandOnHover();
        var dl = ImGui.GetWindowDrawList();
        if (ImGui.IsItemHovered())
        {
            dl.AddRectFilled(tl, tl + new Vector2(winW, rowH), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.04f)));
        }
        dl.AddText(tl + new Vector2(pad, Px(6f)), ImGui.GetColorU32(ctx.Theme.Accent), $"#{trend.Tag}");
        dl.AddText(tl + new Vector2(pad, Px(23f)), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.4f)),
            string.Format(Loc.T("os.yapper_trend_count"), trend.YapCount));
    }

    private void DrawUserRow(OsAppContext ctx, YapperUserRowDto row, float pad)
    {
        var winW = ImGui.GetWindowSize().X;
        var rowH = Px(52f);
        var tl = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();

        // You cannot follow yourself, so your own row carries no pill.
        var isMe = row.ProfileId == _myProfileId();

        // The follow pill is submitted first so its click wins over the row's open target.
        var pillLabel = Loc.T(row.FollowedByMe ? "os.yapper_unfollow" : "os.yapper_follow");
        var pillW = isMe ? 0f : ImGui.CalcTextSize(pillLabel).X + Px(24f);
        ImGui.SetCursorScreenPos(tl + new Vector2(winW - pillW - pad, (rowH - Px(26f)) * 0.5f));
        var pillTl = ImGui.GetCursorScreenPos();
        if (!isMe && ImGui.InvisibleButton($"##yapFollow{row.ProfileId:N}", new Vector2(pillW, Px(26f))))
        {
            ToggleFollow(row);
        }
        if (!isMe)
        {
            HandOnHover();
            var followed = row.FollowedByMe;
            dl.AddRectFilled(pillTl, pillTl + new Vector2(pillW, Px(26f)),
                ImGui.GetColorU32(followed ? new Vector4(1f, 1f, 1f, 0.10f) : ctx.Theme.Accent), Px(13f));
            dl.AddText(pillTl + new Vector2(Px(12f), (Px(26f) - ImGui.GetTextLineHeight()) * 0.5f),
                0xFFFFFFFFu, pillLabel);
        }

        ImGui.SetCursorScreenPos(tl);
        if (ImGui.InvisibleButton($"##yapUser{row.ProfileId:N}", new Vector2(winW - pillW - pad * 2f, rowH)))
        {
            _openProfile(row.ProfileId);
        }
        HandOnHover();

        var center = tl + new Vector2(pad + Px(18f), rowH * 0.5f);
        var avatar = row.Avatar is { Length: > 0 } bytes ? _mediaCache.GetAvatar(row.ProfileId, bytes) : null;
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
        dl.AddText(tl + new Vector2(pad + Px(46f), Px(27f)),
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.45f)), $"@{row.Handle}");
        ImGui.SetCursorScreenPos(new Vector2(tl.X, tl.Y + rowH));
    }

    private void ToggleFollow(YapperUserRowDto row)
    {
        var target = !row.FollowedByMe;
        Flip(_suggested);
        Flip(_userResults);
        _ = Task.Run(async () =>
        {
            try
            {
                if (target)
                {
                    await _host.FollowAsync(row.ProfileId).ConfigureAwait(false);
                }
                else
                {
                    await _host.UnfollowAsync(row.ProfileId).ConfigureAwait(false);
                }
                _onFollowChanged();
            }
            catch (Exception)
            {
            }
        });

        void Flip(YapperUserRowDto[]? rows)
        {
            if (rows is null)
            {
                return;
            }
            for (var i = 0; i < rows.Length; i++)
            {
                if (rows[i].ProfileId == row.ProfileId)
                {
                    rows[i] = rows[i] with { FollowedByMe = target };
                }
            }
        }
    }
}
