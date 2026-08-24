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

/// <summary>The DM inbox: conversation rows with decrypted previews and unread dots, plus the
/// new-message people search.</summary>
internal sealed class MessagesScreen
{
    private readonly IYapperHost _host;
    private readonly DmStore _dms;
    private readonly YapperMediaCache _mediaCache;
    private readonly Action<Guid> _openChat;

    private volatile bool _loading;
    private bool _composeOpen;
    private string _query = string.Empty;
    private string _searchedQuery = string.Empty;
    private DateTime _queryEditedAt;
    private volatile bool _searching;
    private volatile YapperUserRowDto[]? _results;

    public MessagesScreen(IYapperHost host, DmStore dms, YapperMediaCache mediaCache, Action<Guid> openChat)
    {
        _host = host;
        _dms = dms;
        _mediaCache = mediaCache;
        _openChat = openChat;
    }

    public void OnShow()
    {
        _composeOpen = false;
        _query = string.Empty;
        _searchedQuery = string.Empty;
        _results = null;
        Refresh();
        _ = Task.Run(() => _host.EnsureDmKeysAsync());
    }

    public void Refresh()
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
                _dms.SetConversations(await _host.GetDmConversationsAsync().ConfigureAwait(false));
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
        var winW = ImGui.GetWindowSize().X;
        var pad = Px(16f);

        ImGui.Dummy(new Vector2(0f, Px(12f)));
        ImGui.SetCursorPosX(pad);
        using (ctx.TitleFont?.Push())
        {
            ImGui.TextUnformatted(Loc.T("os.yapper_messages_title"));
        }
        ImGui.SameLine(winW - pad - Px(28f));
        var dl = ImGui.GetWindowDrawList();
        if (ImGui.InvisibleButton("##yapDmNew", new Vector2(Px(28f), Px(26f))))
        {
            _composeOpen = !_composeOpen;
        }
        HandOnHover();
        IconDraw.AddCentered(dl, _composeOpen ? FontAwesomeIcon.Times : FontAwesomeIcon.PenSquare, Px(16f),
            ImGui.GetItemRectMin() + ImGui.GetItemRectSize() * 0.5f,
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, ImGui.IsItemHovered() ? 0.95f : 0.65f)));

        if (_composeOpen)
        {
            ImGui.Dummy(new Vector2(0f, Px(6f)));
            ImGui.SetCursorPosX(pad);
            ImGui.SetNextItemWidth(winW - pad * 2f);
            var query = _query;
            if (ImGui.InputTextWithHint("##yapDmSearch", Loc.T("os.yapper_dm_search_hint"), ref query, 100))
            {
                _query = query;
                _queryEditedAt = DateTime.UtcNow;
            }
            MaybeSearch();
        }

        ImGui.Dummy(new Vector2(0f, Px(6f)));
        PushScrollbarStyle();
        using var scroll = ImRaii.Child("##yapDmList", new Vector2(0f, 0f), false);
        if (!scroll.Success)
        {
            PopScrollbarStyle();
            return;
        }

        if (_composeOpen && _query.Trim().Length >= 2)
        {
            foreach (var row in _results ?? [])
            {
                DrawUserRow(ctx, row, pad);
            }
            PopScrollbarStyle();
            return;
        }

        var conversations = _dms.Conversations();
        if (conversations.Count == 0)
        {
            ImGui.Dummy(new Vector2(0f, ImGui.GetWindowSize().Y * 0.3f));
            var empty = Loc.T("os.yapper_dm_empty");
            ImGui.SetCursorPosX((winW - ImGui.CalcTextSize(empty).X) * 0.5f);
            ImGui.TextColored(new Vector4(1f, 1f, 1f, 0.45f), empty);
        }
        foreach (var conv in conversations)
        {
            DrawConversationRow(ctx, conv, pad, winW);
        }
        PopScrollbarStyle();
    }

    private void DrawConversationRow(OsAppContext ctx, YapperDmConversationDto conv, float pad, float winW)
    {
        var rowH = Px(60f);
        var tl = ImGui.GetCursorScreenPos();
        if (ImGui.InvisibleButton($"##yapDmRow{conv.Peer.ProfileId:N}", new Vector2(winW, rowH)))
        {
            _openChat(conv.Peer.ProfileId);
        }
        HandOnHover();
        var dl = ImGui.GetWindowDrawList();
        if (ImGui.IsItemHovered())
        {
            dl.AddRectFilled(tl, tl + new Vector2(winW, rowH), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.04f)));
        }

        var center = tl + new Vector2(pad + Px(21f), rowH * 0.5f);
        DrawAvatar(ctx, dl, conv.Peer, center, Px(21f));

        var textX = pad + Px(52f);
        dl.AddText(tl + new Vector2(textX, Px(10f)), 0xFFFFFFFFu, conv.Peer.DisplayName);
        var nameW = ImGui.CalcTextSize(conv.Peer.DisplayName).X;
        dl.AddText(tl + new Vector2(textX + nameW + Px(6f), Px(10f)),
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.45f)),
            $"@{conv.Peer.Handle} · {YapCard.RelativeTime(conv.LastMessageAtUtc)}");

        var preview = Preview(conv);
        var maxPreview = winW - textX - pad - Px(20f);
        while (preview.Length > 4 && ImGui.CalcTextSize(preview).X > maxPreview)
        {
            preview = preview[..^4] + "…";
        }
        dl.AddText(tl + new Vector2(textX, Px(30f)),
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, conv.Unread > 0 ? 0.85f : 0.45f)), preview);

        if (conv.Unread > 0)
        {
            dl.AddCircleFilled(tl + new Vector2(winW - pad - Px(6f), rowH * 0.5f), Px(5f),
                ImGui.GetColorU32(ctx.Theme.Accent));
        }
        dl.AddLine(tl + new Vector2(pad, rowH), tl + new Vector2(winW - pad, rowH),
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.06f)));
    }

    private string Preview(YapperDmConversationDto conv)
    {
        if (conv.LastMessage is not { } last)
        {
            return Loc.T("os.yapper_dm_preview_none");
        }
        if (last.DeletedAtUtc is not null)
        {
            return Loc.T("os.yapper_dm_deleted");
        }
        // A picture with no caption carries no ciphertext, which is not the same as an unreadable message.
        if (last.Image is not null && last.Ciphertext.Length == 0)
        {
            return Loc.T("chat.preview_image");
        }
        // Keys provision asynchronously, so a preview drawn before they land must not claim the message is
        // undecryptable; it decrypts on a later frame once the pair is in.
        var key = _dms.PeerKey(conv.Peer.ProfileId) ?? conv.PeerPublicKey;
        if (!_host.HasDmKeys || key is null)
        {
            return Loc.T("os.yapper_dm_keys_pending");
        }
        var text = _host.DecryptDm(key, last.Ciphertext, last.Nonce);
        return text?.Replace('\n', ' ') ?? Loc.T("os.yapper_dm_undecryptable");
    }

    private void DrawUserRow(OsAppContext ctx, YapperUserRowDto row, float pad)
    {
        var winW = ImGui.GetWindowSize().X;
        var rowH = Px(48f);
        var tl = ImGui.GetCursorScreenPos();
        if (ImGui.InvisibleButton($"##yapDmPick{row.ProfileId:N}", new Vector2(winW, rowH)))
        {
            _openChat(row.ProfileId);
        }
        HandOnHover();
        var dl = ImGui.GetWindowDrawList();
        if (ImGui.IsItemHovered())
        {
            dl.AddRectFilled(tl, tl + new Vector2(winW, rowH), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.04f)));
        }
        var center = tl + new Vector2(pad + Px(17f), rowH * 0.5f);
        DrawAvatar(ctx, dl, new YapAuthorDto(row.ProfileId, row.Handle, row.DisplayName, row.Avatar, row.IsNsfw,
            row.IsSupporter, FrameRef: row.FrameRef), center, Px(17f));
        dl.AddText(tl + new Vector2(pad + Px(44f), Px(6f)), 0xFFFFFFFFu, row.DisplayName);
        dl.AddText(tl + new Vector2(pad + Px(44f), Px(24f)),
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.45f)), $"@{row.Handle}");
    }

    private void DrawAvatar(OsAppContext ctx, ImDrawListPtr dl, YapAuthorDto who, Vector2 center, float r)
    {
        var tex = who.Avatar is { Length: > 0 } bytes ? _mediaCache.GetAvatar(who.ProfileId, bytes) : null;
        if (tex?.GetWrapOrDefault() is { } wrap)
        {
            dl.AddImageRounded(wrap.Handle, center - new Vector2(r, r), center + new Vector2(r, r),
                Vector2.Zero, Vector2.One, 0xFFFFFFFFu, r);
        }
        else
        {
            dl.AddCircleFilled(center, r, ImGui.GetColorU32(ctx.Theme.Accent with { W = 0.35f }));
            var initial = (who.DisplayName.Length > 0 ? who.DisplayName[..1] : "?").ToUpperInvariant();
            var sz = ImGui.CalcTextSize(initial);
            dl.AddText(center - sz * 0.5f, 0xFFFFFFFFu, initial);
        }
        AvatarRings.Draw(dl, center, r, who.FrameRef);
    }

    private void MaybeSearch()
    {
        var trimmed = _query.Trim();
        if (trimmed.Length < 2 || _searching || trimmed == _searchedQuery
            || (DateTime.UtcNow - _queryEditedAt).TotalSeconds < 0.5)
        {
            return;
        }
        _searching = true;
        _ = Task.Run(async () =>
        {
            try
            {
                _results = await _host.SearchUsersAsync(trimmed).ConfigureAwait(false);
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
}
