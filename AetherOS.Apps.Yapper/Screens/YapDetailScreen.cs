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

/// <summary>One yap in focus: the full card, its meta line, and the reply thread underneath with a
/// docked reply strip.</summary>
internal sealed class YapDetailScreen
{
    private readonly IYapperHost _host;
    private readonly YapperStore _store;
    private readonly YapCard _card;
    private readonly Action _back;
    private readonly Action<YapDto> _openReply;

    private const int AncestorCap = 16;

    private Guid _yapId;
    private volatile bool _loading;
    private List<Guid> _replyIds = [];
    private DateTimeOffset? _replyCursor;
    private volatile bool _repliesLoading;

    /// <summary>The focal yap's parent chain, root-first, drawn above it for context (the Twitter thread view).</summary>
    private List<Guid> _parentChain = [];

    /// <summary>Scrolls the focal yap into view once its context has loaded, so the tapped post is what
    /// the user sees first even under a tall ancestor stack.</summary>
    private bool _scrollPending;

    // Yaps walked down into from this screen. The app's back stack only holds views, so descending a
    // thread needs its own chain to return one level rather than leaving the thread entirely.
    private readonly List<Guid> _ancestors = [];

    public YapDetailScreen(IYapperHost host, YapperStore store, YapCard card, Action back, Action<YapDto> openReply)
    {
        _host = host;
        _store = store;
        _card = card;
        _back = back;
        _openReply = openReply;
    }

    /// <summary>Focuses a yap. <paramref name="descend"/> means the caller was already on this screen and
    /// walked into a reply, so the yap being left becomes the back target.</summary>
    public void Open(YapDto dto, bool descend = false)
    {
        if (descend && _yapId != Guid.Empty && _yapId != dto.Id)
        {
            _ancestors.Add(_yapId);
            if (_ancestors.Count > AncestorCap)
            {
                _ancestors.RemoveAt(0);
            }
        }
        else if (!descend)
        {
            _ancestors.Clear();
        }
        _yapId = dto.Id;
        _store.Upsert(dto);
        _replyIds = [];
        _replyCursor = null;
        _parentChain = [];
        Refresh();
    }

    /// <summary>Back one level in the thread, or out of the screen once the chain is empty.</summary>
    private void GoBack()
    {
        if (_ancestors.Count == 0)
        {
            _back();
            return;
        }
        var parentId = _ancestors[^1];
        _ancestors.RemoveAt(_ancestors.Count - 1);
        _yapId = parentId;
        _replyIds = [];
        _replyCursor = null;
        _parentChain = [];
        Refresh();
    }

    public void NotifyPosted(YapDto reply)
    {
        if (reply.ParentYapId == _yapId && !_replyIds.Contains(reply.Id))
        {
            _replyIds.Insert(0, reply.Id);
        }
    }

    private void Refresh()
    {
        if (_loading)
        {
            return;
        }
        _loading = true;
        _scrollPending = true;
        var id = _yapId;
        _ = Task.Run(async () =>
        {
            try
            {
                var dto = await _host.GetYapAsync(id).ConfigureAwait(false);
                _store.Upsert(dto);
                await LoadParentChainAsync(id, dto.ParentYapId).ConfigureAwait(false);
                await LoadRepliesAsync(reset: true).ConfigureAwait(false);
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

    /// <summary>Walks the reply chain upward so the focal yap renders under its full context; a parent
    /// that fails to load simply truncates the chain there.</summary>
    private async Task LoadParentChainAsync(Guid focalId, Guid? parentId)
    {
        var chain = new List<Guid>();
        while (parentId is { } pid && chain.Count < AncestorCap)
        {
            YapDto parent;
            try
            {
                parent = await _host.GetYapAsync(pid).ConfigureAwait(false);
            }
            catch (Exception)
            {
                break;
            }
            _store.Upsert(parent);
            chain.Insert(0, parent.Id);
            parentId = parent.ParentYapId;
        }
        if (_yapId == focalId)
        {
            _parentChain = chain;
        }
    }

    /// <summary>Replies load through the host once the feed phase supplies the endpoint; a missing
    /// implementation simply leaves the thread empty.</summary>
    private async Task LoadRepliesAsync(bool reset)
    {
        if (_repliesLoading)
        {
            return;
        }
        _repliesLoading = true;
        try
        {
            var page = await _host.GetYapRepliesAsync(_yapId, reset ? null : _replyCursor).ConfigureAwait(false);
            var ids = new List<Guid>(reset ? [] : _replyIds);
            foreach (var reply in page.Yaps)
            {
                _store.Upsert(reply);
                if (!ids.Contains(reply.Id))
                {
                    ids.Add(reply.Id);
                }
            }
            _replyIds = ids;
            _replyCursor = page.NextCursor;
        }
        catch (Exception)
        {
        }
        finally
        {
            _repliesLoading = false;
        }
    }

    public void Draw(OsAppContext ctx)
    {
        var winW = ImGui.GetWindowSize().X;
        var pad = Px(14f);

        ImGui.SetCursorPos(new Vector2(pad, Px(10f)));
        if (ImGui.InvisibleButton("##yapDetailBack", new Vector2(Px(28f), Px(24f))))
        {
            GoBack();
        }
        HandOnHover();
        IconDraw.AddCentered(ImGui.GetWindowDrawList(), FontAwesomeIcon.ArrowLeft, Px(15f),
            ImGui.GetItemRectMin() + ImGui.GetItemRectSize() * 0.5f,
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.8f)));
        ImGui.SameLine();
        ImGui.SetCursorPosY(Px(12f));
        ImGui.TextUnformatted(Loc.T("os.yapper_detail_title"));

        var stripH = Px(52f);
        var contentH = ImGui.GetWindowSize().Y - ImGui.GetCursorPosY() - stripH;
        PushScrollbarStyle();
        using (var content = ImRaii.Child("##yapDetailScroll", new Vector2(0f, contentH), false))
        {
            if (content.Success)
            {
                var dto = _store.Get(_yapId);
                if (dto is null)
                {
                    ImGui.Dummy(new Vector2(0f, Px(30f)));
                    var loading = Loc.T("os.yapper_loading");
                    ImGui.SetCursorPosX((winW - ImGui.CalcTextSize(loading).X) * 0.5f);
                    ImGui.TextColored(new Vector4(1f, 1f, 1f, 0.5f), loading);
                }
                else
                {
                    // The chain already provides context, so cards here suppress the inline parent inset.
                    foreach (var parentId in _parentChain)
                    {
                        if (_store.Get(parentId) is { } parent)
                        {
                            _card.Draw(ctx, parent, replyContext: false);
                        }
                    }
                    if (_scrollPending && !_loading)
                    {
                        if (_parentChain.Count > 0)
                        {
                            ImGui.SetScrollHereY(0f);
                        }
                        else
                        {
                            ImGui.SetScrollY(0f);
                        }
                        _scrollPending = false;
                    }
                    _card.Draw(ctx, dto, clickable: false, replyContext: false);
                    ImGui.SetCursorPosX(pad);
                    ImGui.TextColored(new Vector4(1f, 1f, 1f, 0.4f),
                        $"{dto.CreatedAtUtc.ToLocalTime():t} · {dto.CreatedAtUtc.ToLocalTime():d} · "
                        + string.Format(Loc.T("os.yapper_views"), YapCard.Compact(dto.ViewCount)));
                    ImGui.Dummy(new Vector2(0f, Px(6f)));

                    foreach (var replyId in _replyIds)
                    {
                        if (_store.Get(replyId) is { } reply)
                        {
                            _card.Draw(ctx, reply, replyContext: false);
                        }
                    }
                    if (_replyCursor is not null && !_repliesLoading)
                    {
                        ImGui.SetCursorPosX(pad);
                        if (ImGui.SmallButton(Loc.T("os.yapper_load_more")))
                        {
                            _ = Task.Run(() => LoadRepliesAsync(reset: false));
                        }
                        HandOnHover();
                    }
                }
            }
        }
        PopScrollbarStyle();

        // The docked reply strip.
        var dl = ImGui.GetWindowDrawList();
        var winPos = ImGui.GetWindowPos();
        var winSize = ImGui.GetWindowSize();
        var stripTop = winPos + new Vector2(0f, winSize.Y - stripH);
        dl.AddRectFilled(stripTop, winPos + winSize, ImGui.GetColorU32(new Vector4(0.06f, 0.07f, 0.10f, 1f)));
        ImGui.SetCursorScreenPos(stripTop + new Vector2(pad, Px(9f)));
        var label = Loc.T("os.yapper_reply_hint");
        var stripW = winSize.X - pad * 2f;
        var tl = ImGui.GetCursorScreenPos();
        dl.AddRectFilled(tl, tl + new Vector2(stripW, Px(34f)), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.07f)), Px(17f));
        dl.AddText(tl + new Vector2(Px(14f), (Px(34f) - ImGui.GetTextLineHeight()) * 0.5f),
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.45f)), label);
        if (ImGui.InvisibleButton("##yapReplyStrip", new Vector2(stripW, Px(34f))))
        {
            if (_store.Get(_yapId) is { } dto && !dto.Deleted)
            {
                _openReply(dto);
            }
        }
        HandOnHover();
    }
}
