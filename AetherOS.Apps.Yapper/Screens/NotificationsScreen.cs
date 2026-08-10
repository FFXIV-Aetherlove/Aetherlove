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

/// <summary>The notifications inbox: coalesced rows, mark-all-read on entry, tap-through to the yap
/// or the actor's profile.</summary>
internal sealed class NotificationsScreen
{
    private readonly IYapperHost _host;
    private readonly YapperMediaCache _mediaCache;
    private readonly Action<Guid> _openYap;
    private readonly Action<Guid> _openProfile;
    private readonly Action _markedRead;

    private List<YapperNotificationDto> _rows = [];
    private DateTimeOffset? _cursor;
    private volatile bool _loading;
    private volatile bool _loadedOnce;

    /// <summary>Arrival stamps for live pushes so a freshly landed row flashes; written on the push thread.</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, DateTime> _flashAt = new();

    private const float FlashSeconds = 1.8f;

    public NotificationsScreen(IYapperHost host, YapperMediaCache mediaCache,
        Action<Guid> openYap, Action<Guid> openProfile, Action markedRead)
    {
        _host = host;
        _mediaCache = mediaCache;
        _openYap = openYap;
        _openProfile = openProfile;
        _markedRead = markedRead;
    }

    /// <summary>Prepends a live push so the open inbox updates without a refetch.</summary>
    public void ApplyPush(YapperNotificationDto dto)
    {
        var rows = new List<YapperNotificationDto>(_rows);
        rows.RemoveAll(r => r.Id == dto.Id);
        rows.Insert(0, dto);
        _rows = rows;
        _flashAt[dto.Id] = DateTime.UtcNow;
        if (_flashAt.Count > 64)
        {
            _flashAt.Clear();
        }
    }

    /// <summary>Clears every badge surface immediately; the server mark-read rides the refresh.</summary>
    public void OnShow()
    {
        _markedRead();
        Refresh();
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
                var page = await _host.GetNotificationsAsync(null).ConfigureAwait(false);
                _rows = [.. page.Notifications];
                _cursor = page.NextCursor;
                _loadedOnce = true;
                await _host.MarkNotificationsReadAsync().ConfigureAwait(false);
                _markedRead();
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

    private void LoadMore()
    {
        if (_loading || _cursor is null)
        {
            return;
        }
        _loading = true;
        var cursor = _cursor;
        _ = Task.Run(async () =>
        {
            try
            {
                var page = await _host.GetNotificationsAsync(cursor).ConfigureAwait(false);
                var rows = new List<YapperNotificationDto>(_rows);
                foreach (var dto in page.Notifications)
                {
                    if (!rows.Exists(r => r.Id == dto.Id))
                    {
                        rows.Add(dto);
                    }
                }
                _rows = rows;
                _cursor = page.NextCursor;
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
        if (!_loadedOnce && !_loading)
        {
            Refresh();
        }

        PushScrollbarStyle();
        using var scroll = ImRaii.Child("##yapNotifList", new Vector2(0f, 0f), false);
        if (!scroll.Success)
        {
            PopScrollbarStyle();
            return;
        }

        var rows = _rows;
        if (rows.Count == 0)
        {
            ImGui.Dummy(new Vector2(0f, ImGui.GetWindowSize().Y * 0.4f));
            var empty = Loc.T(_loading ? "os.yapper_loading" : "os.yapper_notif_empty");
            ImGui.SetCursorPosX((ImGui.GetWindowSize().X - ImGui.CalcTextSize(empty).X) * 0.5f);
            ImGui.TextColored(new Vector4(1f, 1f, 1f, 0.45f), empty);
        }
        else
        {
            ImGui.Dummy(new Vector2(0f, Px(6f)));
            foreach (var row in rows)
            {
                DrawRow(ctx, row);
            }
            if (_cursor is not null && ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - Px(200f))
            {
                LoadMore();
            }
        }
        PopScrollbarStyle();
    }

    private void DrawRow(OsAppContext ctx, YapperNotificationDto row)
    {
        var pad = Px(14f);
        var winW = ImGui.GetWindowSize().X;
        var rowH = Px(row.Snippet is null ? 52f : 66f);
        var tl = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();

        if (ImGui.InvisibleButton($"##yapNotif{row.Id:N}", new Vector2(winW, rowH)))
        {
            if (row.YapId is { } yapId)
            {
                _openYap(yapId);
            }
            else if (row.Actor is { } actor)
            {
                _openProfile(actor.ProfileId);
            }
        }
        HandOnHover();
        if (ImGui.IsItemHovered())
        {
            dl.AddRectFilled(tl, tl + new Vector2(winW, rowH), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.04f)));
        }
        DrawFlash(ctx, dl, row.Id, tl, new Vector2(winW, rowH));

        var (icon, color) = row.Kind switch
        {
            YapperNotificationKind.Like => (FontAwesomeIcon.Heart, new Vector4(0.95f, 0.35f, 0.45f, 1f)),
            YapperNotificationKind.Reply => (FontAwesomeIcon.Comment, ctx.Theme.Accent),
            YapperNotificationKind.Repost => (FontAwesomeIcon.Retweet, new Vector4(0.30f, 0.85f, 0.45f, 1f)),
            YapperNotificationKind.Mention => (FontAwesomeIcon.At, ctx.Theme.Accent),
            YapperNotificationKind.Follow => (FontAwesomeIcon.UserPlus, ctx.Theme.Accent),
            _ => (FontAwesomeIcon.Bell, new Vector4(1f, 0.78f, 0.25f, 1f)),
        };
        IconDraw.AddCentered(dl, icon, Px(15f), tl + new Vector2(pad + Px(9f), Px(24f)), ImGui.GetColorU32(color));

        var avatarCenter = tl + new Vector2(pad + Px(38f), Px(24f));
        if (row.Actor is { } who)
        {
            var avatar = who.Avatar is { Length: > 0 } bytes ? _mediaCache.GetAvatar(who.ProfileId, bytes) : null;
            if (avatar?.GetWrapOrDefault() is { } wrap)
            {
                dl.AddImageRounded(wrap.Handle, avatarCenter - new Vector2(Px(13f), Px(13f)),
                    avatarCenter + new Vector2(Px(13f), Px(13f)), Vector2.Zero, Vector2.One, 0xFFFFFFFFu, Px(13f));
            }
            else
            {
                dl.AddCircleFilled(avatarCenter, Px(13f), ImGui.GetColorU32(ctx.Theme.Accent with { W = 0.35f }));
            }
            AvatarRings.Draw(dl, avatarCenter, Px(13f), who.FrameRef);
        }

        dl.AddText(tl + new Vector2(pad + Px(58f), Px(8f)),
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, row.Read ? 0.6f : 0.95f)), Headline(row));
        dl.AddText(tl + new Vector2(pad + Px(58f), Px(26f)),
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.4f)), YapCard.RelativeTime(row.UpdatedAtUtc));
        if (row.Snippet is { } snippet)
        {
            // Flatten newlines and clamp to the row: draw-list text honors \n, so a multi-line yap
            // would otherwise bleed into the rows below.
            var flat = snippet.ReplaceLineEndings(" ").Trim();
            dl.AddText(tl + new Vector2(pad + Px(58f), Px(44f)),
                ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.5f)),
                TruncateToWidth(flat, winW - pad * 2f - Px(58f)));
        }
        ImGui.SetCursorScreenPos(new Vector2(tl.X, tl.Y + rowH));
    }

    /// <summary>A fading accent wash over a row that just arrived live, so the update is unmissable.</summary>
    private void DrawFlash(OsAppContext ctx, ImDrawListPtr dl, Guid id, Vector2 tl, Vector2 size)
    {
        if (!_flashAt.TryGetValue(id, out var at))
        {
            return;
        }
        var age = (float)(DateTime.UtcNow - at).TotalSeconds;
        if (age >= FlashSeconds)
        {
            _flashAt.TryRemove(id, out _);
            return;
        }
        var alpha = AetherLove.Services.AccessibilityService.ReduceMotion
            ? 0.18f
            : 0.30f * (1f - age / FlashSeconds);
        dl.AddRectFilled(tl, tl + size, ImGui.GetColorU32(ctx.Theme.Accent with { W = alpha }));
        dl.AddRectFilled(tl, tl + new Vector2(Px(3f), size.Y), ImGui.GetColorU32(ctx.Theme.Accent));
    }

    internal static string Headline(YapperNotificationDto row)
    {
        var name = row.Actor?.DisplayName ?? "?";
        var key = row.Kind switch
        {
            YapperNotificationKind.Like => "os.yapper_notif_like",
            YapperNotificationKind.Reply => "os.yapper_notif_reply",
            YapperNotificationKind.Repost => "os.yapper_notif_repost",
            YapperNotificationKind.Mention => "os.yapper_notif_mention",
            YapperNotificationKind.Follow => "os.yapper_notif_follow",
            _ => "os.yapper_notif_newpost",
        };
        var actorText = row.ActorCount > 1
            ? string.Format(Loc.T("os.yapper_notif_many"), name, row.ActorCount - 1)
            : name;
        return string.Format(Loc.T(key), actorText);
    }
}
