using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove.Services.Localization;
using AetherLove.Shared.Yapper;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Yapper;

/// <summary>One scrolling feed: an id list resolved through the store, keyset load-more near the
/// bottom, and per-card seen reporting. Every feed surface (home, profile tabs, bookmarks, tags,
/// threads) is an instance of this.</summary>
internal sealed class FeedPane(
    YapperStore store,
    Func<DateTimeOffset?, Task<YapPageDto>> loader,
    Action<Guid> markSeen)
{
    private readonly List<Guid> _ids = [];
    private DateTimeOffset? _cursor;
    private bool _endReached;
    private volatile bool _loading;
    private volatile bool _loadedOnce;
    private volatile string? _error;

    public bool LoadedOnce => _loadedOnce;

    /// <summary>A yap drawn elsewhere on the surface (the pinned post); skipped in the list so it never
    /// shows twice.</summary>
    public Guid? ExcludeId { get; set; }

    public void Refresh()
    {
        if (_loading)
        {
            return;
        }
        _loading = true;
        _error = null;
        _ = Task.Run(async () =>
        {
            try
            {
                var page = await loader(null).ConfigureAwait(false);
                var ids = new List<Guid>(page.Yaps.Length);
                foreach (var yap in page.Yaps)
                {
                    store.Upsert(yap);
                    ids.Add(yap.Id);
                }
                _ids.Clear();
                _ids.AddRange(ids);
                _cursor = page.NextCursor;
                _endReached = page.NextCursor is null;
                _loadedOnce = true;
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

    private void LoadMore()
    {
        if (_loading || _endReached || _cursor is null)
        {
            return;
        }
        _loading = true;
        var cursor = _cursor;
        _ = Task.Run(async () =>
        {
            try
            {
                var page = await loader(cursor).ConfigureAwait(false);
                foreach (var yap in page.Yaps)
                {
                    store.Upsert(yap);
                    if (!_ids.Contains(yap.Id))
                    {
                        _ids.Add(yap.Id);
                    }
                }
                _cursor = page.NextCursor;
                _endReached = page.NextCursor is null;
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

    /// <summary>Prepends a freshly posted yap so it shows without a refetch.</summary>
    public void Prepend(Guid id)
    {
        if (!_ids.Contains(id))
        {
            _ids.Insert(0, id);
        }
    }

    public void RemoveId(Guid id) => _ids.Remove(id);

    /// <summary>Draws the cards into the CURRENT child window; the caller owns scrolling.</summary>
    public void DrawCards(OsAppContext ctx, YapCard card, string emptyKey)
    {
        if (!_loadedOnce)
        {
            Refresh();
        }
        if (_error is { } error && _ids.Count == 0)
        {
            CenteredText(error);
            return;
        }
        if (_ids.Count == 0)
        {
            if (_loading)
            {
                CenteredText(Loc.T("os.yapper_loading"));
            }
            else
            {
                CenteredText(Loc.T(emptyKey));
            }
            return;
        }

        var now = ImGui.GetTime();
        foreach (var id in _ids.ToArray())
        {
            if (id == ExcludeId)
            {
                continue;
            }
            if (store.Get(id) is not { } dto)
            {
                continue;
            }
            if (store.VanishProgress(dto, now) is { } vanish)
            {
                if (!DrawVanishing(ctx, card, dto, vanish))
                {
                    _ids.Remove(id);
                }
                continue;
            }
            card.Draw(ctx, dto);
            markSeen(id);
        }

        // Auto-page once the scroll approaches the bottom.
        if (!_endReached && ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - Px(300f))
        {
            LoadMore();
        }
        if (_loading)
        {
            var label = Loc.T("os.yapper_loading");
            ImGui.SetCursorPosX((ImGui.GetWindowSize().X - ImGui.CalcTextSize(label).X) * 0.5f);
            ImGui.TextColored(new Vector4(1f, 1f, 1f, 0.4f), label);
        }
        ImGui.Dummy(new Vector2(0f, Px(10f)));
    }

    /// <summary>Draws the pane as a three-column mosaic of square image tiles (Twitter media-tab style),
    /// one tile per image, into the CURRENT child window; the caller owns scrolling. Tapping a tile opens
    /// the owning yap.</summary>
    public void DrawMediaMosaic(OsAppContext ctx, YapperMediaCache mediaCache, string emptyKey, Action<YapDto> openYap)
    {
        if (!_loadedOnce)
        {
            Refresh();
        }
        if (_error is { } error && _ids.Count == 0)
        {
            CenteredText(error);
            return;
        }
        if (_ids.Count == 0)
        {
            CenteredText(Loc.T(_loading ? "os.yapper_loading" : emptyKey));
            return;
        }

        var startX = ImGui.GetCursorScreenPos().X;
        var winW = ImGui.GetWindowSize().X;
        var gap = Px(3f);
        const int cols = 3;
        var tile = (winW - gap * (cols - 1)) / cols;
        var col = 0;
        var rowY = ImGui.GetCursorScreenPos().Y;
        var dl = ImGui.GetWindowDrawList();

        foreach (var id in _ids.ToArray())
        {
            if (store.Get(id) is not { } dto || dto.Media.Length == 0)
            {
                continue;
            }
            markSeen(id);
            var obscure = (dto.Handicapped || dto.HasContentWarning
                || (dto.IsNsfw && store.ViewerBlursNsfw && dto.Author?.ProfileId != store.ViewerProfileId))
                && !store.IsRevealed(dto.Id);
            for (var j = 0; j < dto.Media.Length; j++)
            {
                var tl = new Vector2(startX + col * (tile + gap), rowY);
                ImGui.SetCursorScreenPos(tl);
                if (ImGui.InvisibleButton($"##yapMosaic{id:N}_{j}", new Vector2(tile, tile)))
                {
                    openYap(dto);
                }
                HandOnHover();
                var wrap = mediaCache.Get(dto.Media[j].ImageId, $"mosaic {id:N} img#{j}")?.Tex?.GetWrapOrDefault();
                if (wrap is not null && wrap.Width > 0 && wrap.Height > 0)
                {
                    var scale = MathF.Max(tile / wrap.Width, tile / wrap.Height);
                    var visW = tile / (wrap.Width * scale);
                    var visH = tile / (wrap.Height * scale);
                    var uv0 = new Vector2((1f - visW) * 0.5f, (1f - visH) * 0.5f);
                    if (obscure)
                    {
                        DrawBlurredCover(dl, wrap, tl, new Vector2(tile, tile),
                            uv0, uv0 + new Vector2(visW, visH), Px(4f));
                    }
                    else
                    {
                        dl.AddImageRounded(wrap.Handle, tl, tl + new Vector2(tile, tile),
                            uv0, uv0 + new Vector2(visW, visH), 0xFFFFFFFFu, Px(4f));
                    }
                }
                else
                {
                    dl.AddRectFilled(tl, tl + new Vector2(tile, tile),
                        ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.06f)), Px(4f));
                }
                col++;
                if (col == cols)
                {
                    col = 0;
                    rowY += tile + gap;
                }
            }
        }
        if (col > 0)
        {
            rowY += tile + gap;
        }
        ImGui.SetCursorScreenPos(new Vector2(startX, rowY));

        if (!_endReached && ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - Px(300f))
        {
            LoadMore();
        }
        if (_loading)
        {
            var label = Loc.T("os.yapper_loading");
            ImGui.SetCursorPosX((ImGui.GetWindowSize().X - ImGui.CalcTextSize(label).X) * 0.5f);
            ImGui.TextColored(new Vector4(1f, 1f, 1f, 0.4f), label);
        }
        ImGui.Dummy(new Vector2(0f, Px(10f)));
    }

    /// <summary>The roll-up: the card carries on drawing inside a child that shrinks to nothing, so the rows
    /// below slide up to meet it. Clipping does the work rather than a fade, because most of a card is painted
    /// straight onto the draw list where a pushed alpha would not reach it. Returns false once the card is
    /// finished, or immediately when it has no measured height, which means it was never on screen.</summary>
    private bool DrawVanishing(OsAppContext ctx, YapCard card, YapDto dto, float t)
    {
        if (t >= 1f || store.HeightOf(dto.Id) is not { } full)
        {
            return false;
        }
        var eased = t * t;
        ImGui.SetCursorPosX(0f);
        using var clip = ImRaii.Child($"##yapVanish{dto.Id:N}",
            new Vector2(ImGui.GetWindowSize().X, MathF.Max(1f, full * (1f - eased))), false,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse
            | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoInputs);
        if (clip)
        {
            card.Draw(ctx, dto);
        }
        return true;
    }

    private static void CenteredText(string text)
    {
        ImGui.Dummy(new Vector2(0f, ImGui.GetWindowSize().Y * 0.35f));
        ImGui.PushTextWrapPos(ImGui.GetWindowSize().X - Px(30f));
        ImGui.SetCursorPosX(Math.Max(Px(15f), (ImGui.GetWindowSize().X - ImGui.CalcTextSize(text).X) * 0.5f));
        ImGui.TextColored(new Vector4(1f, 1f, 1f, 0.45f), text);
        ImGui.PopTextWrapPos();
    }
}
