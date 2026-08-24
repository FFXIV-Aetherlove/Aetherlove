using System;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove.Services.Localization;
using AetherLove.Shared.Yapper;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Yapper;

/// <summary>The workhorse card: header row, text, gallery, embed card, blur states, action row with
/// optimistic counts, overflow menu, tombstone and one-level repost inset. Reads and writes through
/// the store so every surface stays in sync.</summary>
internal sealed class YapCard(
    IYapperHost host,
    YapperStore store,
    YapperMediaCache mediaCache,
    AetherLove.UI.TranslateUi translate,
    Func<Guid?> myProfileId,
    Action<YapDto> openDetail,
    Action<YapDto> onReply,
    Action<YapDto> onQuote,
    Action<YapDto> onEdit,
    Action<YapDto> onDeleted,
    Action<Guid> openProfile,
    Action<YapDto, int> openImage,
    Action<YapDto> onReport,
    Action<YapAuthorDto, bool> onModerate,
    Action<YapEmbedDto> openEmbed,
    Func<Guid?> pinnedYapId,
    Action<Guid?> setPinned)
{
    private const float AvatarR = 18f;

    private readonly System.Collections.Generic.Dictionary<Guid, (string Src, AetherLove.Emoji.ParsedMessage Parsed)> _parsed = [];

    /// <summary>Emoji-aware parse of a yap body, memoized per yap so feeds don't re-parse every frame.</summary>
    private AetherLove.Emoji.ParsedMessage Parse(Guid id, string text)
    {
        if (_parsed.TryGetValue(id, out var entry) && entry.Src == text)
        {
            return entry.Parsed;
        }
        if (_parsed.Count > 600)
        {
            _parsed.Clear();
        }
        var parsed = AetherLove.Emoji.ParsedMessage.Parse(text);
        _parsed[id] = (text, parsed);
        return parsed;
    }

    /// <summary>Draws one card; returns its height contribution implicitly via the cursor. The height is
    /// remembered so a muted or blocked author's cards can roll up from the size they actually had. An inset
    /// card is a quote or reply parent rather than a feed row, so its height is not the row's.</summary>
    public void Draw(OsAppContext ctx, YapDto dto, bool inset = false, bool clickable = true, bool replyContext = true)
    {
        var top = ImGui.GetCursorPosY();
        DrawCard(ctx, dto, inset, clickable, replyContext);
        if (!inset)
        {
            store.NoteHeight(dto.Id, dto.Author?.ProfileId, ImGui.GetCursorPosY() - top);
        }
    }

    private void DrawCard(OsAppContext ctx, YapDto dto, bool inset, bool clickable, bool replyContext)
    {
        var live = store.Get(dto.Id) ?? store.Upsert(dto);
        var winW = ImGui.GetWindowSize().X;
        var pad = Px(14f);
        var startY = ImGui.GetCursorPosY();

        if (live.Deleted)
        {
            DrawTombstone(pad, live);
            DrawSeparator();
            return;
        }
        if (live.Handicapped && !store.IsRevealed(live.Id))
        {
            DrawHandicapped(ctx, live, pad);
            DrawSeparator();
            return;
        }

        ImGui.SetCursorPos(new Vector2(pad, ImGui.GetCursorPosY() + Px(10f)));
        DrawHeader(ctx, live, pad, winW);

        var contentX = pad + Px(AvatarR * 2f) + Px(10f);
        // Reply context in list surfaces (Twitter-style): the parent renders as a quote inset above the
        // reply body. The thread view suppresses it, where the chain is already on screen.
        if (replyContext && live.Kind == YapKind.Reply && live.InReplyTo is { } parent)
        {
            DrawNested(ctx, parent, contentX, winW - contentX - pad);
        }
        if (live.RepostOf is not null && live.Text is null)
        {
            // Plain repost: the target IS the content, framed under a "reposted" line.
            DrawNested(ctx, live.RepostOf, contentX, winW - contentX - pad);
        }
        else
        {
            if (!string.IsNullOrEmpty(live.Text))
            {
                ImGui.SetCursorPosX(contentX);
                // The body renders in its own child, which sits above the whole-card target, so the text
                // itself must carry the open action or tapping it goes nowhere.
                Parse(live.Id, translate.Display(live.Id, live.Text)).DrawWrapped(
                    $"##yapBody{live.Id:N}", winW - pad - contentX,
                    clickable ? () => openDetail(live) : null);
                if (ImGui.BeginPopupContextItem($"##yapTrCtx{live.Id:N}", ImGuiPopupFlags.MouseButtonRight))
                {
                    translate.DrawMenuItems(live.Id, live.Text);
                    ImGui.EndPopup();
                }
            }
            DrawGallery(ctx, live, contentX, winW - contentX - pad);
            if (live.Embed is { } embed)
            {
                DrawEmbed(ctx, embed, contentX, winW - contentX - pad);
            }
            if (live.RepostOf is not null)
            {
                DrawNested(ctx, live.RepostOf, contentX, winW - contentX - pad);
            }
        }

        DrawActions(ctx, live, contentX, winW, pad);

        if (clickable)
        {
            // The whole-card open target is submitted last, so buttons above win their clicks.
            var endY = ImGui.GetCursorPosY();
            ImGui.SetCursorPos(new Vector2(0f, startY));
            if (ImGui.InvisibleButton($"##yapOpen{live.Id:N}", new Vector2(winW, Math.Max(1f, endY - startY))))
            {
                openDetail(live);
            }
            ImGui.SetCursorPosY(endY);
        }
        DrawSeparator();
    }

    private void DrawHeader(OsAppContext ctx, YapDto dto, float pad, float winW)
    {
        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var center = origin + new Vector2(Px(AvatarR), Px(AvatarR));

        // Submitted before the whole-card target so tapping the avatar opens the author instead.
        if (dto.Author is { } who)
        {
            ImGui.SetCursorScreenPos(origin);
            if (ImGui.InvisibleButton($"##yapAvatar{dto.Id:N}", new Vector2(Px(AvatarR * 2f), Px(AvatarR * 2f))))
            {
                openProfile(who.ProfileId);
            }
            HandOnHover();
            ImGui.SetCursorScreenPos(origin);
        }

        var avatarTex = dto.Author?.Avatar is { Length: > 0 } bytes
            ? mediaCache.GetAvatar(dto.Author.ProfileId, bytes)
            : null;
        if (avatarTex?.GetWrapOrDefault() is { } wrap)
        {
            dl.AddImageRounded(wrap.Handle, origin, origin + new Vector2(Px(AvatarR * 2f), Px(AvatarR * 2f)),
                Vector2.Zero, Vector2.One, 0xFFFFFFFFu, Px(AvatarR));
        }
        else
        {
            dl.AddCircleFilled(center, Px(AvatarR), ImGui.GetColorU32(ctx.Theme.Accent with { W = 0.35f }));
            var initial = (dto.Author?.DisplayName?.Length > 0 ? dto.Author.DisplayName[..1] : "?").ToUpperInvariant();
            var sz = ImGui.CalcTextSize(initial);
            dl.AddText(center - sz * 0.5f, 0xFFFFFFFFu, initial);
        }
        AvatarRings.Draw(dl, center, Px(AvatarR), dto.Author?.FrameRef);

        var textX = ImGui.GetCursorPosX() + Px(AvatarR * 2f) + Px(10f);
        ImGui.SetCursorPos(new Vector2(textX, ImGui.GetCursorPosY()));
        ImGui.TextUnformatted(dto.Author?.DisplayName ?? "?");
        if (dto.Author?.IsSupporter == true)
        {
            ImGui.SameLine();
            IconDraw.AddCentered(dl, FontAwesomeIcon.Star, Px(10f),
                ImGui.GetCursorScreenPos() + new Vector2(Px(5f), ImGui.GetTextLineHeight() * 0.5f),
                ImGui.GetColorU32(new Vector4(1f, 0.78f, 0.25f, 1f)));
            ImGui.Dummy(new Vector2(Px(12f), 0f));
        }
        ImGui.SameLine();
        var meta = $"@{dto.Author?.Handle}  ·  {RelativeTime(dto.CreatedAtUtc)}"
            + (dto.EditedAtUtc is not null ? $"  ·  {Loc.T("os.yapper_edited")}" : "");
        ImGui.TextColored(new Vector4(1f, 1f, 1f, 0.45f), meta);

        DrawOverflow(ctx, dto, winW);
    }

    private void DrawOverflow(OsAppContext ctx, YapDto dto, float winW)
    {
        var dl = ImGui.GetWindowDrawList();
        var y = ImGui.GetCursorPosY() - ImGui.GetTextLineHeightWithSpacing();
        ImGui.SetCursorPos(new Vector2(winW - Px(34f), y));
        ImGui.InvisibleButton($"##yapMore{dto.Id:N}", new Vector2(Px(28f), Px(20f)));
        HandOnHover();
        IconDraw.AddCentered(dl, FontAwesomeIcon.EllipsisH, Px(13f),
            ImGui.GetItemRectMin() + ImGui.GetItemRectSize() * 0.5f,
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, ImGui.IsItemHovered() ? 0.85f : 0.4f)));
        if (ImGui.IsItemClicked())
        {
            ImGui.OpenPopup($"##yapMenu{dto.Id:N}");
        }
        if (ImGui.BeginPopup($"##yapMenu{dto.Id:N}"))
        {
            var mine = dto.Author?.ProfileId == myProfileId();
            if (!dto.Deleted && dto.Visibility == YapVisibility.Everyone
                && DrawIconMenuItem(FontAwesomeIcon.QuoteRight, Loc.T("os.yapper_menu_quote")))
            {
                ImGui.CloseCurrentPopup();
                onQuote(dto);
            }
            if (mine)
            {
                if (dto.Kind != YapKind.Reply)
                {
                    var pinned = pinnedYapId() == dto.Id;
                    if (DrawIconMenuItem(pinned ? FontAwesomeIcon.ThumbtackSlash : FontAwesomeIcon.Thumbtack,
                        Loc.T(pinned ? "os.yapper_menu_unpin" : "os.yapper_menu_pin")))
                    {
                        ImGui.CloseCurrentPopup();
                        setPinned(pinned ? null : dto.Id);
                    }
                }
                if (DrawIconMenuItem(FontAwesomeIcon.Edit, Loc.T("os.yapper_menu_edit")))
                {
                    ImGui.CloseCurrentPopup();
                    onEdit(dto);
                }
                if (DrawIconMenuItem(FontAwesomeIcon.Trash, Loc.T("os.yapper_menu_delete")))
                {
                    ImGui.CloseCurrentPopup();
                    var id = dto.Id;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await host.DeleteYapAsync(id).ConfigureAwait(false);
                            store.Remove(id);
                            onDeleted(dto);
                        }
                        catch (Exception)
                        {
                        }
                    });
                }
            }
            else
            {
                if (dto.Author is { } who)
                {
                    if (DrawIconMenuItem(FontAwesomeIcon.VolumeMute, Loc.T("os.yapper_menu_mute")))
                    {
                        ImGui.CloseCurrentPopup();
                        onModerate(who, false);
                    }
                    if (DrawIconMenuItem(FontAwesomeIcon.Ban, Loc.T("os.yapper_menu_block")))
                    {
                        ImGui.CloseCurrentPopup();
                        onModerate(who, true);
                    }
                }
                if (DrawIconMenuItem(FontAwesomeIcon.Flag, Loc.T("os.yapper_menu_report")))
                {
                    ImGui.CloseCurrentPopup();
                    onReport(dto);
                }
            }
            ImGui.EndPopup();
        }
    }

    /// <summary>Twitter-style mosaic: 1 full-width, 2 halves, 3 = tall left + stacked right, 4+ = 2x2
    /// with a "+N" overlay. A content-warned gallery blurs until any tile is clicked; an unblurred tile
    /// click opens the breakout viewer.</summary>
    private void DrawGallery(OsAppContext ctx, YapDto dto, float x, float availW)
    {
        var n = dto.Media.Length;
        if (n == 0)
        {
            return;
        }
        var blur = ShouldBlur(dto);
        ImGui.Dummy(new Vector2(0f, Px(6f)));
        ImGui.SetCursorPosX(x);
        var origin = ImGui.GetCursorScreenPos();
        var gap = Px(3f);
        var half = (availW - gap) * 0.5f;

        float totalH;
        if (n == 1)
        {
            var aspect = dto.Media[0].Width > 0 ? (float)dto.Media[0].Height / dto.Media[0].Width : 0.56f;
            var naturalH = availW * aspect;
            totalH = Math.Clamp(naturalH, Px(80f), Px(360f));
            // A single image is never crop-fitted: when the height cap clips its natural size,
            // contain-fit so the full picture stays visible (no cut-off heads).
            DrawTile(dto, 0, origin, new Vector2(availW, totalH), blur, 0, contain: naturalH > totalH + 0.5f);
        }
        else if (n == 2)
        {
            totalH = Px(170f);
            DrawTile(dto, 0, origin, new Vector2(half, totalH), blur, 0);
            DrawTile(dto, 1, origin + new Vector2(half + gap, 0f), new Vector2(half, totalH), blur, 0);
        }
        else if (n == 3)
        {
            totalH = Px(210f);
            var rowH = (totalH - gap) * 0.5f;
            DrawTile(dto, 0, origin, new Vector2(half, totalH), blur, 0);
            DrawTile(dto, 1, origin + new Vector2(half + gap, 0f), new Vector2(half, rowH), blur, 0);
            DrawTile(dto, 2, origin + new Vector2(half + gap, rowH + gap), new Vector2(half, rowH), blur, 0);
        }
        else
        {
            var rowH = Px(105f);
            totalH = rowH * 2f + gap;
            DrawTile(dto, 0, origin, new Vector2(half, rowH), blur, 0);
            DrawTile(dto, 1, origin + new Vector2(half + gap, 0f), new Vector2(half, rowH), blur, 0);
            DrawTile(dto, 2, origin + new Vector2(0f, rowH + gap), new Vector2(half, rowH), blur, 0);
            DrawTile(dto, 3, origin + new Vector2(half + gap, rowH + gap), new Vector2(half, rowH), blur, n - 4);
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(availW, totalH));
    }

    /// <summary>Draw-list-only media tile for the quote inset: no item is submitted, so the inset's
    /// whole-card open target underneath receives the clicks.</summary>
    private void DrawTileRaw(ImDrawListPtr dl, YapDto dto, int index, Vector2 tl, Vector2 size, bool blur, bool contain)
    {
        var wrap = mediaCache.Get(dto.Media[index].ImageId, $"quote {dto.Id:N} img#{index}")?.Tex?.GetWrapOrDefault();
        if (wrap is null)
        {
            dl.AddRectFilled(tl, tl + size, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.06f)), Px(8f));
            return;
        }
        var (uv0, uv1) = CoverFitUvs(wrap.Width, wrap.Height, size.X, size.Y);
        if (blur)
        {
            DrawBlurredCover(dl, wrap, tl, size, uv0, uv1, rounding: Px(8f));
        }
        else if (contain && wrap.Width > 0 && wrap.Height > 0)
        {
            dl.AddRectFilled(tl, tl + size, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.35f)), Px(8f));
            var scale = MathF.Min(size.X / wrap.Width, size.Y / wrap.Height);
            var drawn = new Vector2(wrap.Width * scale, wrap.Height * scale);
            var inner = tl + (size - drawn) * 0.5f;
            dl.AddImageRounded(wrap.Handle, inner, inner + drawn, Vector2.Zero, Vector2.One, 0xFFFFFFFFu, Px(8f));
        }
        else
        {
            dl.AddImageRounded(wrap.Handle, tl, tl + size, uv0, uv1, 0xFFFFFFFFu, Px(8f));
        }
    }

    private void DrawTile(YapDto dto, int index, Vector2 tl, Vector2 size, bool blur, int overflow, bool contain = false)
    {
        ImGui.SetCursorScreenPos(tl);
        if (ImGui.InvisibleButton($"##yapImg{dto.Id:N}_{index}", size))
        {
            if (blur)
            {
                store.Reveal(dto.Id);
            }
            else
            {
                openImage(dto, index);
            }
        }
        HandOnHover();

        var dl = ImGui.GetWindowDrawList();
        var wrap = mediaCache.Get(dto.Media[index].ImageId, $"yap {dto.Id:N} img#{index}")?.Tex?.GetWrapOrDefault();
        if (wrap is not null)
        {
            var (uv0, uv1) = CoverFitUvs(wrap.Width, wrap.Height, size.X, size.Y);
            if (blur)
            {
                DrawBlurredCover(dl, wrap, tl, size, uv0, uv1, rounding: Px(10f));
            }
            else if (contain && wrap.Width > 0 && wrap.Height > 0)
            {
                dl.AddRectFilled(tl, tl + size, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.35f)), Px(10f));
                var scale = MathF.Min(size.X / wrap.Width, size.Y / wrap.Height);
                var drawn = new Vector2(wrap.Width * scale, wrap.Height * scale);
                var inner = tl + (size - drawn) * 0.5f;
                dl.AddImageRounded(wrap.Handle, inner, inner + drawn, Vector2.Zero, Vector2.One, 0xFFFFFFFFu, Px(10f));
            }
            else
            {
                dl.AddImageRounded(wrap.Handle, tl, tl + size, uv0, uv1, 0xFFFFFFFFu, Px(10f));
            }
        }
        else
        {
            dl.AddRectFilled(tl, tl + size, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.06f)), Px(10f));
        }
        if (overflow > 0)
        {
            dl.AddRectFilled(tl, tl + size, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.55f)), Px(10f));
            var label = $"+{overflow}";
            var sz = ImGui.CalcTextSize(label);
            dl.AddText(tl + (size - sz) * 0.5f, 0xFFFFFFFFu, label);
        }
    }

    private sealed record VenueEmbedVisual(AetherLove.Shared.Places.VenueCardDto? Card,
        Dalamud.Interface.Textures.ISharedImmediateTexture? Tex, bool LogoBackdrop);

    private sealed record LeveEmbedVisual(AetherLove.Shared.Levemetes.LevemeteCardDto? Card,
        Dalamud.Interface.Textures.ISharedImmediateTexture? Tex);

    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, VenueEmbedVisual> _venueEmbeds = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, LeveEmbedVisual> _leveEmbeds = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, byte> _embedFetches = new();

    private void StartVenueEmbedFetch(Guid venueId)
    {
        if (_venueEmbeds.ContainsKey(venueId) || !_embedFetches.TryAdd(venueId, 0))
        {
            return;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                var dto = await host.GetVenueCardAsync(venueId).ConfigureAwait(false);
                Dalamud.Interface.Textures.ISharedImmediateTexture? tex = null;
                var logoBackdrop = false;
                var cacheDir = System.IO.Path.Combine(AetherLove.UiHost.PluginInterface.ConfigDirectory.FullName, "PlacesCache");
                if (dto.BannerWebp is { Length: > 0 })
                {
                    tex = AetherLove.Services.AvatarDiskCache.Store(cacheDir, $"yapvenue_{venueId:N}", dto.BannerWebp);
                }
                else if (dto.Summary.LogoWebp is { Length: > 0 })
                {
                    tex = AetherLove.Services.AvatarDiskCache.Store(cacheDir, $"yapvenuelogo_{venueId:N}", dto.Summary.LogoWebp);
                    logoBackdrop = true;
                }
                _venueEmbeds[venueId] = new VenueEmbedVisual(dto, tex, logoBackdrop);
            }
            catch (Exception)
            {
                _venueEmbeds[venueId] = new VenueEmbedVisual(null, null, false);
            }
            finally
            {
                _embedFetches.TryRemove(venueId, out _);
            }
        });
    }

    private void StartLeveEmbedFetch(Guid adId)
    {
        if (_leveEmbeds.ContainsKey(adId) || !_embedFetches.TryAdd(adId, 0))
        {
            return;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                var dto = await host.GetLevemeteCardAsync(adId).ConfigureAwait(false);
                Dalamud.Interface.Textures.ISharedImmediateTexture? tex = null;
                if (dto?.CoverWebp is { Length: > 0 })
                {
                    var cacheDir = System.IO.Path.Combine(AetherLove.UiHost.PluginInterface.ConfigDirectory.FullName, "LevemetesCache");
                    tex = AetherLove.Services.AvatarDiskCache.Store(cacheDir, $"yapleve_{adId:N}", dto.CoverWebp);
                }
                _leveEmbeds[adId] = new LeveEmbedVisual(dto, tex);
            }
            catch (Exception)
            {
                _leveEmbeds[adId] = new LeveEmbedVisual(null, null);
            }
            finally
            {
                _embedFetches.TryRemove(adId, out _);
            }
        });
    }

    private void DrawEmbed(OsAppContext ctx, YapEmbedDto embed, float x, float availW)
    {
        if (embed.Unavailable)
        {
            DrawEmbedPill(ctx, embed, x, availW, Loc.T("os.yapper_embed_gone"));
            return;
        }
        if (embed.Kind == YapEmbedKind.Venue)
        {
            StartVenueEmbedFetch(embed.Id);
            if (!_venueEmbeds.TryGetValue(embed.Id, out var visual))
            {
                DrawEmbedPill(ctx, embed, x, availW, Loc.T("places.share_loading"));
                return;
            }
            if (visual.Card is not { } card)
            {
                DrawEmbedPill(ctx, embed, x, availW, Loc.T("places.share_unavailable"));
                return;
            }
            DrawVenueEmbedCard(ctx, embed, card, visual, x, availW);
            return;
        }
        StartLeveEmbedFetch(embed.Id);
        if (!_leveEmbeds.TryGetValue(embed.Id, out var leveVisual))
        {
            DrawEmbedPill(ctx, embed, x, availW, Loc.T("chat.leve_card_loading"));
            return;
        }
        if (leveVisual.Card is not { } leveCard)
        {
            DrawEmbedPill(ctx, embed, x, availW, Loc.T("chat.leve_card_unavailable"));
            return;
        }
        DrawLeveEmbedCard(ctx, embed, leveCard, leveVisual, x, availW);
    }

    private void DrawEmbedPill(OsAppContext ctx, YapEmbedDto embed, float x, float availW, string label)
    {
        ImGui.Dummy(new Vector2(0f, Px(6f)));
        ImGui.SetCursorPosX(x);
        var dl = ImGui.GetWindowDrawList();
        var tl = ImGui.GetCursorScreenPos();
        var h = Px(44f);
        dl.AddRectFilled(tl, tl + new Vector2(availW, h), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.05f)), Px(8f));
        dl.AddRect(tl, tl + new Vector2(availW, h), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.12f)), Px(8f));
        var icon = embed.Kind == YapEmbedKind.Venue ? FontAwesomeIcon.MapMarkerAlt : FontAwesomeIcon.Briefcase;
        IconDraw.AddCentered(dl, icon, Px(15f), tl + new Vector2(Px(20f), h * 0.5f),
            ImGui.GetColorU32(embed.Unavailable ? new Vector4(1f, 1f, 1f, 0.35f) : ctx.Theme.Accent));
        dl.AddText(tl + new Vector2(Px(38f), (h - ImGui.GetTextLineHeight()) * 0.5f),
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, embed.Unavailable ? 0.4f : 0.9f)),
            TruncateToWidth(label, availW - Px(48f)));
        ImGui.Dummy(new Vector2(availW, h));
    }

    private void DrawVenueEmbedCard(OsAppContext ctx, YapEmbedDto embed,
        AetherLove.Shared.Places.VenueCardDto card, VenueEmbedVisual visual, float x, float availW)
    {
        ImGui.Dummy(new Vector2(0f, Px(6f)));
        ImGui.SetCursorPosX(x);
        var dl = ImGui.GetWindowDrawList();
        var tl = ImGui.GetCursorScreenPos();
        var h = Px(150f);
        var br = tl + new Vector2(availW, h);
        if (ImGui.InvisibleButton($"##yapEmbedV{embed.Id:N}", new Vector2(availW, h)))
        {
            openEmbed(embed);
        }
        HandOnHover();
        var hovered = ImGui.IsItemHovered();

        var venue = card.Summary;
        var wrap = visual.Tex?.GetWrapOrDefault();
        if (wrap is not null)
        {
            var (uv0, uv1) = CoverFitUvs(wrap.Width, wrap.Height, availW, h);
            dl.AddImageRounded(wrap.Handle, tl, br, uv0, uv1, 0xFFFFFFFFu, Px(12f), ImDrawFlags.RoundCornersAll);
            if (visual.LogoBackdrop)
            {
                dl.AddRectFilled(tl, br, 0x59000000u, Px(12f));
            }
        }
        else
        {
            dl.AddRectFilled(tl, br, ImGui.GetColorU32(ctx.Theme.Accent with { W = 0.14f }), Px(12f));
        }
        dl.AddRectFilledMultiColor(new Vector2(tl.X, br.Y - Px(84f)), br,
            0x00000000u, 0x00000000u, 0xD8000000u, 0xD8000000u);
        dl.AddRect(tl, br, ImGui.GetColorU32(ctx.Theme.Accent with { W = hovered ? 0.90f : 0.55f }), Px(12f),
            ImDrawFlags.None, Px(1.5f));
        VenueFields.DrawStarSummary(dl, tl + new Vector2(Px(12f), Px(11f)),
            venue.AverageRating, venue.ReviewCount, Px(12f));

        var textX = tl.X + Px(14f);
        var textMaxW = availW - Px(28f);
        float nameH;
        using (UiFonts.H3?.Push())
        {
            nameH = ImGui.GetFontSize();
            dl.AddText(ImGui.GetFont(), nameH, new Vector2(textX, br.Y - Px(58f)),
                0xFFFFFFFFu, TruncateToWidth(venue.Name, textMaxW));
        }
        dl.AddText(new Vector2(textX, br.Y - Px(58f) + nameH + Px(3f)), ImGui.GetColorU32(UiColors.Body),
            TruncateToWidth(VenueFields.LocationLine(venue), textMaxW - Px(12f)));
        ImGui.SetCursorScreenPos(new Vector2(tl.X, br.Y));
    }

    private void DrawLeveEmbedCard(OsAppContext ctx, YapEmbedDto embed,
        AetherLove.Shared.Levemetes.LevemeteCardDto card, LeveEmbedVisual visual, float x, float availW)
    {
        ImGui.Dummy(new Vector2(0f, Px(6f)));
        ImGui.SetCursorPosX(x);
        var dl = ImGui.GetWindowDrawList();
        var tl = ImGui.GetCursorScreenPos();
        var h = Px(120f);
        var br = tl + new Vector2(availW, h);
        if (ImGui.InvisibleButton($"##yapEmbedL{embed.Id:N}", new Vector2(availW, h)))
        {
            openEmbed(embed);
        }
        HandOnHover();
        var hovered = ImGui.IsItemHovered();

        var wrap = visual.Tex?.GetWrapOrDefault();
        if (wrap is not null)
        {
            var (uv0, uv1) = CoverFitUvs(wrap.Width, wrap.Height, availW, h);
            dl.AddImageRounded(wrap.Handle, tl, br, uv0, uv1, 0xFFFFFFFFu, Px(12f), ImDrawFlags.RoundCornersAll);
        }
        else
        {
            dl.AddRectFilled(tl, br, ImGui.GetColorU32(ctx.Theme.Accent with { W = 0.14f }), Px(12f));
        }
        dl.AddRectFilledMultiColor(new Vector2(tl.X, br.Y - Px(70f)), br,
            0x00000000u, 0x00000000u, 0xD8000000u, 0xD8000000u);
        dl.AddRect(tl, br, ImGui.GetColorU32(ctx.Theme.Accent with { W = hovered ? 0.90f : 0.55f }), Px(12f),
            ImDrawFlags.None, Px(1.5f));

        var textX = tl.X + Px(14f);
        var textMaxW = availW - Px(28f);
        float nameH;
        using (UiFonts.H3?.Push())
        {
            nameH = ImGui.GetFontSize();
            dl.AddText(ImGui.GetFont(), nameH, new Vector2(textX, br.Y - Px(52f)),
                0xFFFFFFFFu, TruncateToWidth(card.Title, textMaxW));
        }
        var kindLabel = Loc.T(card.Kind == (short)AetherLove.Shared.Levemetes.LevemeteKind.Offering
            ? "chat.leve_kind_offering"
            : "chat.leve_kind_looking");
        var catLabel = Enum.IsDefined((AetherLove.Shared.Levemetes.LevemeteCategory)card.Category)
            ? Loc.T($"chat.leve_cat_{card.Category}")
            : Loc.T("chat.leve_cat_unknown");
        dl.AddText(new Vector2(textX, br.Y - Px(52f) + nameH + Px(3f)), ImGui.GetColorU32(UiColors.Body),
            TruncateToWidth($"{kindLabel} · {catLabel}", textMaxW - Px(12f)));
        ImGui.SetCursorScreenPos(new Vector2(tl.X, br.Y));
    }

    private void DrawNested(OsAppContext ctx, YapDto nested, float x, float availW)
    {
        ImGui.Dummy(new Vector2(0f, Px(6f)));
        ImGui.SetCursorPosX(x);
        var dl = ImGui.GetWindowDrawList();
        var tl = ImGui.GetCursorScreenPos();
        var startY = ImGui.GetCursorPosY();
        var lineH = ImGui.GetTextLineHeight();

        ImGui.SetCursorPos(new Vector2(x + Px(10f), startY + Px(8f)));
        if (nested.Deleted)
        {
            ImGui.PushTextWrapPos(x + availW - Px(10f));
            ImGui.TextColored(new Vector4(1f, 1f, 1f, 0.4f), Loc.T(TombstoneKey(nested)));
            ImGui.PopTextWrapPos();
        }
        else
        {
            // The author and media lines go on the draw list, not as items, so the inset's open target
            // underneath receives their clicks; only the text child needs its own handler.
            dl.AddText(ImGui.GetCursorScreenPos(), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.7f)),
                TruncateToWidth($"{nested.Author?.DisplayName}  @{nested.Author?.Handle}", availW - Px(20f)));
            ImGui.Dummy(new Vector2(0f, lineH + ImGui.GetStyle().ItemSpacing.Y));
            if (!string.IsNullOrEmpty(nested.Text))
            {
                ImGui.SetCursorPosX(x + Px(10f));
                Parse(nested.Id, nested.Text).DrawWrapped($"##yapNested{nested.Id:N}", availW - Px(20f),
                    () => openDetail(nested));
            }
            if (nested.Media.Length > 0)
            {
                ImGui.SetCursorPosX(x + Px(10f));
                ImGui.Dummy(new Vector2(0f, Px(2f)));
                ImGui.SetCursorPosX(x + Px(10f));
                var innerW = availW - Px(20f);
                var mediaTl = ImGui.GetCursorScreenPos();
                var blur = ShouldBlur(nested);
                float mediaH;
                if (nested.Media.Length == 1)
                {
                    var meta = nested.Media[0];
                    var aspect = meta.Width > 0 ? (float)meta.Height / meta.Width : 0.56f;
                    var naturalH = innerW * aspect;
                    mediaH = Math.Clamp(naturalH, Px(60f), Px(220f));
                    DrawTileRaw(dl, nested, 0, mediaTl, new Vector2(innerW, mediaH), blur, naturalH > mediaH + 0.5f);
                }
                else
                {
                    mediaH = Px(84f);
                    var gap = Px(3f);
                    var tileW = (innerW - gap * (nested.Media.Length - 1)) / nested.Media.Length;
                    for (var i = 0; i < nested.Media.Length; i++)
                    {
                        DrawTileRaw(dl, nested, i, mediaTl + new Vector2((tileW + gap) * i, 0f),
                            new Vector2(tileW, mediaH), blur, contain: false);
                    }
                }
                ImGui.Dummy(new Vector2(0f, mediaH + ImGui.GetStyle().ItemSpacing.Y));
            }
        }
        var endY = ImGui.GetCursorPosY() + Px(8f);
        dl.AddRect(tl, new Vector2(tl.X + availW, tl.Y + (endY - startY)),
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.14f)), Px(10f));

        // The quote is its own destination: tapping anywhere on the inset opens the quoted yap.
        if (!nested.Deleted)
        {
            var saved = ImGui.GetCursorPos();
            ImGui.SetCursorScreenPos(tl);
            if (ImGui.InvisibleButton($"##yapNestedOpen{nested.Id:N}", new Vector2(availW, endY - startY)))
            {
                openDetail(nested);
            }
            HandOnHover();
            ImGui.SetCursorPos(saved);
        }
        ImGui.SetCursorPosY(endY);
    }

    private void DrawActions(OsAppContext ctx, YapDto dto, float x, float winW, float pad)
    {
        ImGui.Dummy(new Vector2(0f, Px(6f)));
        var slotW = (winW - x - pad) / 4f;
        var muted = new Vector4(1f, 1f, 1f, 0.45f);

        DrawAction(0, FontAwesomeIcon.Comment, dto.ReplyCount, muted, () => onReply(dto));
        // FollowersOnly yaps are not repostable, so the button goes inert instead of erroring.
        var canRepost = dto.Visibility == YapVisibility.Everyone;
        DrawAction(1, FontAwesomeIcon.Retweet, dto.RepostCount,
            dto.RepostedByMe ? new Vector4(0.30f, 0.85f, 0.45f, 1f) : muted,
            canRepost ? () => ImGui.OpenPopup($"##yapRepostMenu{dto.Id:N}") : null);
        // Liking your own yap is rejected server-side, so the count still shows but the tap does nothing.
        var mine = dto.Author?.ProfileId == myProfileId();
        DrawAction(2, FontAwesomeIcon.Heart, dto.LikeCount,
            dto.LikedByMe ? new Vector4(0.95f, 0.35f, 0.45f, 1f) : muted,
            mine ? null : () => ToggleLike(dto));
        DrawAction(3, FontAwesomeIcon.Bookmark, dto.BookmarkCount,
            dto.BookmarkedByMe ? ctx.Theme.Accent : muted,
            () => ToggleBookmark(dto));

        void DrawAction(int slot, FontAwesomeIcon icon, int count, Vector4 color, Action? onClick)
        {
            var dl = ImGui.GetWindowDrawList();
            ImGui.SetCursorPos(new Vector2(x + slotW * slot, ImGui.GetCursorPosY()));
            var tl = ImGui.GetCursorScreenPos();
            if (ImGui.InvisibleButton($"##yapAct{slot}{dto.Id:N}", new Vector2(slotW * 0.8f, Px(22f)))
                && onClick is not null)
            {
                onClick();
            }
            if (onClick is not null)
            {
                HandOnHover();
            }
            IconDraw.AddCentered(dl, icon, Px(13f), tl + new Vector2(Px(9f), Px(11f)), ImGui.GetColorU32(color));
            if (count > 0)
            {
                dl.AddText(tl + new Vector2(Px(22f), Px(3f)), ImGui.GetColorU32(color), Compact(count));
            }
            if (slot < 3)
            {
                ImGui.SameLine();
            }
        }

        if (ImGui.BeginPopup($"##yapRepostMenu{dto.Id:N}"))
        {
            if (DrawIconMenuItem(FontAwesomeIcon.Retweet,
                Loc.T(dto.RepostedByMe ? "os.yapper_menu_unrepost" : "os.yapper_menu_repost")))
            {
                ImGui.CloseCurrentPopup();
                ToggleRepost(dto);
            }
            if (DrawIconMenuItem(FontAwesomeIcon.QuoteRight, Loc.T("os.yapper_menu_quote")))
            {
                ImGui.CloseCurrentPopup();
                onQuote(dto);
            }
            ImGui.EndPopup();
        }
        ImGui.Dummy(new Vector2(0f, Px(6f)));
    }

    private void ToggleLike(YapDto dto)
    {
        var target = !dto.LikedByMe;
        store.Update(dto.Id, d => d with { LikedByMe = target, LikeCount = Math.Max(0, d.LikeCount + (target ? 1 : -1)) });
        _ = Task.Run(() => host.SetYapLikeAsync(dto.Id, target, default));
    }

    private void ToggleBookmark(YapDto dto)
    {
        var target = !dto.BookmarkedByMe;
        store.Update(dto.Id, d => d with { BookmarkedByMe = target, BookmarkCount = Math.Max(0, d.BookmarkCount + (target ? 1 : -1)) });
        _ = Task.Run(() => host.SetYapBookmarkAsync(dto.Id, target, default));
    }

    private void ToggleRepost(YapDto dto)
    {
        if (dto.RepostedByMe)
        {
            store.Update(dto.Id, d => d with { RepostedByMe = false, RepostCount = Math.Max(0, d.RepostCount - 1) });
            _ = Task.Run(async () =>
            {
                try
                {
                    await host.UndoYapRepostAsync(dto.Id).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    store.Update(dto.Id, d => d with { RepostedByMe = true, RepostCount = d.RepostCount + 1 });
                }
            });
            return;
        }
        store.Update(dto.Id, d => d with { RepostedByMe = true, RepostCount = d.RepostCount + 1 });
        _ = Task.Run(async () =>
        {
            try
            {
                var created = await host.CreateYapAsync(new YapCreateDto(
                    YapKind.Repost, null, null, dto.Id, YapVisibility.Everyone, false, null)).ConfigureAwait(false);
                store.Upsert(created);
            }
            catch (Exception)
            {
                store.Update(dto.Id, d => d with { RepostedByMe = false, RepostCount = Math.Max(0, d.RepostCount - 1) });
            }
        });
    }

    /// <summary>Own posts never blur under the viewer preference; a content warning blurs for everyone
    /// (the author asked for it).</summary>
    private bool ShouldBlur(YapDto dto) =>
        (dto.HasContentWarning
            || (dto.IsNsfw && store.ViewerBlursNsfw && dto.Author?.ProfileId != myProfileId()))
        && !store.IsRevealed(dto.Id);

    private static string TombstoneKey(YapDto dto) => dto switch
    {
        { BlockedAuthor: true } => "os.yapper_tombstone_blocked",
        { RemovedByModeration: true } => "os.yapper_tombstone_removed",
        _ => "os.yapper_tombstone",
    };

    private void DrawTombstone(float pad, YapDto dto)
    {
        ImGui.Dummy(new Vector2(0f, Px(12f)));
        ImGui.SetCursorPosX(pad);
        ImGui.PushTextWrapPos(ImGui.GetWindowSize().X - pad);
        ImGui.TextColored(new Vector4(1f, 1f, 1f, 0.4f), Loc.T(TombstoneKey(dto)));
        ImGui.PopTextWrapPos();
        ImGui.Dummy(new Vector2(0f, Px(12f)));
    }

    private void DrawHandicapped(OsAppContext ctx, YapDto dto, float pad)
    {
        ImGui.Dummy(new Vector2(0f, Px(12f)));
        ImGui.SetCursorPosX(pad);
        ImGui.TextColored(new Vector4(1f, 1f, 1f, 0.5f), Loc.T("os.yapper_nsfw_hidden"));
        ImGui.SetCursorPosX(pad);
        if (ImGui.SmallButton($"{Loc.T("os.yapper_reveal")}##hd{dto.Id:N}"))
        {
            store.Reveal(dto.Id);
        }
        HandOnHover();
        ImGui.Dummy(new Vector2(0f, Px(12f)));
    }

    private static void DrawSeparator()
    {
        var dl = ImGui.GetWindowDrawList();
        var y = ImGui.GetCursorScreenPos().Y;
        dl.AddLine(new Vector2(ImGui.GetWindowPos().X, y),
            new Vector2(ImGui.GetWindowPos().X + ImGui.GetWindowSize().X, y),
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.07f)));
        ImGui.Dummy(new Vector2(0f, Px(1f)));
    }

    internal static string RelativeTime(DateTimeOffset utc)
    {
        var span = DateTimeOffset.UtcNow - utc;
        if (span.TotalMinutes < 1)
        {
            return Loc.T("os.yapper_now");
        }
        if (span.TotalHours < 1)
        {
            return $"{(int)span.TotalMinutes}m";
        }
        if (span.TotalDays < 1)
        {
            return $"{(int)span.TotalHours}h";
        }
        if (span.TotalDays < 7)
        {
            return $"{(int)span.TotalDays}d";
        }
        return utc.ToLocalTime().ToString("d MMM");
    }

    internal static string Compact(int count) => count switch
    {
        >= 1_000_000 => $"{count / 1_000_000f:0.#}M",
        >= 1_000 => $"{count / 1_000f:0.#}K",
        _ => count.ToString(),
    };
}
