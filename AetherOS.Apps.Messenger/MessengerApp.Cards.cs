using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Shared.Hangouts;
using AetherLove.Shared.Levemetes;
using AetherLove.Shared.Messenger;
using AetherLove.Shared.News;
using AetherLove.Shared.Places;
using AetherLove.Shared.Profile.Enums;
using AetherLove.UI;
using AetherLove.Widgets;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;

namespace AetherOS.Apps.Messenger;

/// <summary>Share cards: whole-body [venue=]/[hangout=]/[news=]/[calevent=] messages render as the same
/// tappable cards as the AetherLove match chat, and the messenger is a share-sheet target for all four.
/// Every click keeps the user anchored to the messenger: venue and news deep links carry a return-to-
/// messenger affordance and hangouts open an in-app popup.</summary>
public sealed partial class MessengerApp
{
    private const float VenueCardH = 150f;
    private const float LevemeteCardH = 120f;
    private const float HangoutCardH = 110f;
    private const float NewsCardH = 122f;
    private const float CalendarCardH = 92f;
    private const float MarketCardH = 86f;

    private sealed record VenueCardVisual(VenueCardDto? Card, ISharedImmediateTexture? Tex, bool LogoBackdrop);

    private sealed record LevemeteCardVisual(LevemeteCardDto? Card, ISharedImmediateTexture? Tex);

    // One fetched card per shared id (session cache); null content marks an id that could not be loaded
    // (ended, deleted, or not visible) so the row renders the tombstone.
    private readonly ConcurrentDictionary<Guid, VenueCardVisual> _venueCards = new();
    private readonly ConcurrentDictionary<Guid, HangoutCardDto?> _hangoutCards = new();
    private readonly ConcurrentDictionary<Guid, NewsCardDto?> _newsCards = new();
    private readonly ConcurrentDictionary<Guid, LevemeteCardVisual> _levemeteCards = new();
    private readonly ConcurrentDictionary<Guid, byte> _cardFetches = new();

    private ShareItem? _pendingShare;
    private CalendarEventShare.Payload? _calPrompt;
    private float _calPromptH;

    // Home-world min price per shared item id (session cache); 0 marks a fetch that found no listings.
    private readonly ConcurrentDictionary<uint, long> _marketPrices = new();
    private readonly ConcurrentDictionary<uint, byte> _marketPriceFetches = new();

    public IReadOnlyList<string> AcceptedShareTypes { get; } =
        [ShareTypes.Venue, ShareTypes.Hangout, ShareTypes.News, ShareTypes.CalendarEvent, ShareTypes.Levemete, ShareTypes.MarketItem];

    /// <summary>A share-sheet item landed on the messenger: the chat list enters share mode and the next
    /// tapped chat receives the composed card message.</summary>
    private void BeginShare(ShareItem item)
    {
        _pendingShare = item;
        CloseChat();
        _overlay = Overlay.None;
    }

    private static string? ComposeShareBody(ShareItem item) => item.Type switch
    {
        ShareTypes.Venue when Guid.TryParse(item.RefId, out var venueId) => VenueShare.Compose(venueId),
        ShareTypes.Hangout when Guid.TryParse(item.RefId, out var hangoutId) => HangoutShare.Compose(hangoutId),
        ShareTypes.News when Guid.TryParse(item.RefId, out var newsId) => NewsShare.Compose(newsId),
        ShareTypes.CalendarEvent => CalendarEventShare.TryComposeFromShareItem(item),
        ShareTypes.Levemete when Guid.TryParse(item.RefId, out var adId) => LevemeteShare.Compose(adId),
        ShareTypes.MarketItem => MarketShare.TryComposeFromShareItem(item),
        _ => null,
    };

    private void DrawShareBanner(float width)
    {
        if (_pendingShare is not { } item)
        {
            return;
        }
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var h = Px(38f);
        var cursor = ImGui.GetCursorScreenPos();
        var tl = new Vector2(cursor.X + Px(6f), cursor.Y);
        var br = tl + new Vector2(width - Px(12f), h);

        dl.AddRectFilled(tl, br, ImGui.GetColorU32(t.Accent with { W = 0.16f }), Px(9f));
        dl.AddRect(tl, br, ImGui.GetColorU32(t.Accent with { W = 0.65f }), Px(9f));

        var icon = item.Type switch
        {
            ShareTypes.Venue => FontAwesomeIcon.Store,
            ShareTypes.Hangout => FontAwesomeIcon.Bullhorn,
            ShareTypes.CalendarEvent => FontAwesomeIcon.CalendarAlt,
            ShareTypes.MarketItem => FontAwesomeIcon.Coins,
            _ => FontAwesomeIcon.Newspaper,
        };
        IconCentered(dl, icon, Px(13f), new Vector2(tl.X + Px(17f), (tl.Y + br.Y) * 0.5f),
            ImGui.GetColorU32(t.AccentLight));

        var textX = tl.X + Px(32f);
        var fs = ImGui.GetFontSize();
        dl.PushClipRect(new Vector2(textX, tl.Y), new Vector2(br.X - Px(30f), br.Y), true);
        dl.AddText(ImGui.GetFont(), fs * 0.86f, new Vector2(textX, tl.Y + Px(5f)),
            Col(BodyText), Loc.T("os.msgr_share_pick"));
        dl.AddText(ImGui.GetFont(), fs * 0.78f, new Vector2(textX, tl.Y + Px(5f) + fs * 0.9f),
            Col(MutedText), item.Title.Replace('\n', ' '));
        dl.PopClipRect();

        var xC = new Vector2(br.X - Px(17f), (tl.Y + br.Y) * 0.5f);
        ImGui.SetCursorScreenPos(xC - new Vector2(Px(11f), Px(11f)));
        if (ImGui.InvisibleButton("##msgrShareCancel", new Vector2(Px(22f), Px(22f))))
        {
            _pendingShare = null;
        }
        IconCentered(dl, FontAwesomeIcon.Times, Px(11f), xC,
            ImGui.IsItemHovered() ? Col(DangerRed) : Col(MutedText));

        ImGui.SetCursorScreenPos(new Vector2(cursor.X, br.Y));
        ImGui.Dummy(new Vector2(0f, Px(6f)));
    }

    /// <summary>Sends the pending share into the tapped chat, then opens it.</summary>
    private void SendPendingShareTo(ChatRow row)
    {
        if (_pendingShare is not { } item || ComposeShareBody(item) is not { } body)
        {
            _pendingShare = null;
            return;
        }
        _pendingShare = null;
        if (!TrySendComposed(row.Id, row.Kind, body))
        {
            _busyError = Loc.T("huberror.generic");
        }
        OpenChat(row.Id, row.Kind);
    }

    /// <summary>Encrypts and sends a composed body to any chat (the share flow's headless send).</summary>
    private bool TrySendComposed(Guid chatId, MessengerChatKind kind, string text)
    {
        (byte[] Ciphertext, byte[] Nonce)? sealedMsg;
        var epoch = 0;
        if (kind == MessengerChatKind.Direct)
        {
            var contact = _store.Contact(chatId);
            sealedMsg = contact?.PeerPublicKey is { Length: > 0 } pub ? _crypto.EncryptDirect(pub, text) : null;
        }
        else
        {
            var group = _store.Group(chatId);
            epoch = group?.KeyEpoch ?? 0;
            var key = group is null ? null : _store.GroupKey(chatId, epoch);
            sealedMsg = key is null ? null : _crypto.EncryptGroup(key, text);
        }
        if (sealedMsg is not { } payload)
        {
            return false;
        }
        var req = new SendMessengerMessageRequest(chatId, kind, payload.Ciphertext, payload.Nonce, epoch, null);
        _ = Task.Run(async () =>
        {
            try
            {
                var resp = await _hub.SendMessengerMessageAsync(req).ConfigureAwait(false);
                var sent = new MessengerMessageDto(resp.MessageId, chatId, kind, _store.MyAccountId,
                    payload.Ciphertext, payload.Nonce, epoch, resp.CreatedAtUtc, resp.CreatedAtUtc, null);
                _uiActions.Enqueue(() => _plain[resp.MessageId] = text);
                _store.ApplyMessage(sent);
                _scrollToBottom = 1f;
            }
            catch (Exception ex)
            {
                _busyError = FriendlyHubError(ex);
                AetherLove.UiHost.Log.Warning(ex, "[MessengerApp] share send failed.");
            }
        });
        return true;
    }

    /// <summary>Failed card fetches retry on the next chat open (the content may just have been unreachable).</summary>
    private void ResetFailedCards()
    {
        foreach (var kv in _venueCards)
        {
            if (kv.Value.Card is null)
            {
                _venueCards.TryRemove(kv.Key, out _);
            }
        }
        foreach (var kv in _hangoutCards)
        {
            if (kv.Value is null)
            {
                _hangoutCards.TryRemove(kv.Key, out _);
            }
        }
        foreach (var kv in _newsCards)
        {
            if (kv.Value is null)
            {
                _newsCards.TryRemove(kv.Key, out _);
            }
        }
        foreach (var kv in _levemeteCards)
        {
            if (kv.Value.Card is null)
            {
                _levemeteCards.TryRemove(kv.Key, out _);
            }
        }
    }

    private void StartVenueCardFetch(Guid venueId)
    {
        if (_venueCards.ContainsKey(venueId) || !_cardFetches.TryAdd(venueId, 0))
        {
            return;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                var dto = await _hub.GetVenueCardAsync(venueId).ConfigureAwait(false);
                ISharedImmediateTexture? tex = null;
                var logoBackdrop = false;
                var cacheDir = Path.Combine(AetherLove.UiHost.PluginInterface.ConfigDirectory.FullName, "PlacesCache");
                if (dto.BannerWebp is { Length: > 0 })
                {
                    tex = AvatarDiskCache.Store(cacheDir, $"chatvenue_{venueId:N}", dto.BannerWebp);
                }
                else if (dto.Summary.LogoWebp is { Length: > 0 })
                {
                    tex = AvatarDiskCache.Store(cacheDir, $"chatvenuelogo_{venueId:N}", dto.Summary.LogoWebp);
                    logoBackdrop = true;
                }
                _venueCards[venueId] = new VenueCardVisual(dto, tex, logoBackdrop);
            }
            catch (Exception ex)
            {
                AetherLove.UiHost.Log.Warning(ex, $"[MessengerApp] Venue card fetch failed for {venueId}.");
                _venueCards[venueId] = new VenueCardVisual(null, null, false);
            }
            finally
            {
                _cardFetches.TryRemove(venueId, out _);
            }
        });
    }

    private void StartHangoutCardFetch(Guid hangoutId)
    {
        if (_hangoutCards.ContainsKey(hangoutId) || !_cardFetches.TryAdd(hangoutId, 0))
        {
            return;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                _hangoutCards[hangoutId] = await _hub.GetHangoutCardAsync(hangoutId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AetherLove.UiHost.Log.Warning(ex, $"[MessengerApp] Hangout card fetch failed for {hangoutId}.");
                _hangoutCards[hangoutId] = null;
            }
            finally
            {
                _cardFetches.TryRemove(hangoutId, out _);
            }
        });
    }

    private void StartNewsCardFetch(Guid newsId)
    {
        if (_newsCards.ContainsKey(newsId) || !_cardFetches.TryAdd(newsId, 0))
        {
            return;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                _newsCards[newsId] = await _hub.GetNewsCardAsync(newsId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AetherLove.UiHost.Log.Warning(ex, $"[MessengerApp] News card fetch failed for {newsId}.");
                _newsCards[newsId] = null;
            }
            finally
            {
                _cardFetches.TryRemove(newsId, out _);
            }
        });
    }

    /// <summary>Reserved row height when the text is a whole-body share token, else null; must mirror the
    /// card renderers' layout.</summary>
    /// <summary>The location card is a fixed three lines: title, region and datacenter, then world and zone
    /// (with any ward/plot appended).</summary>
    private static float LocationCardHeight(float lineH) => 3 * lineH + 2 * Px(3f) + Px(11f) * 2f;

    /// <summary>The physical-region short code for a datacenter (EU/NA/OCE/JPN), empty when unknown.</summary>
    private static string RegionAbbrev(string dataCenter) => GameWorldData.RegionOfDataCenter(dataCenter) switch
    {
        Region.Europe => "EU",
        Region.NorthAmerica => "NA",
        Region.Oceania => "OCE",
        Region.Japan => "JPN",
        _ => string.Empty,
    };

    private static string JoinParts(params string[] parts)
    {
        var kept = new List<string>();
        foreach (var p in parts)
        {
            if (!string.IsNullOrEmpty(p))
            {
                kept.Add(p);
            }
        }
        return string.Join("  ·  ", kept);
    }

    private void StartLevemeteCardFetch(Guid adId)
    {
        if (_levemeteCards.ContainsKey(adId) || !_cardFetches.TryAdd(adId, 0))
        {
            return;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                var dto = await _hub.GetLevemeteCardAsync(adId).ConfigureAwait(false);
                ISharedImmediateTexture? tex = null;
                if (dto?.CoverWebp is { Length: > 0 })
                {
                    var cacheDir = Path.Combine(AetherLove.UiHost.PluginInterface.ConfigDirectory.FullName, "LevemetesCache");
                    tex = AvatarDiskCache.Store(cacheDir, $"msgrleve_{adId:N}", dto.CoverWebp);
                }
                _levemeteCards[adId] = new LevemeteCardVisual(dto, tex);
            }
            catch (Exception ex)
            {
                AetherLove.UiHost.Log.Warning(ex, $"[MessengerApp] Levemete card fetch failed for {adId}.");
                _levemeteCards[adId] = new LevemeteCardVisual(null, null);
            }
            finally
            {
                _cardFetches.TryRemove(adId, out _);
            }
        });
    }

    private float? CardRowHeight(string text, bool needsDivider, bool isGroupEnd, float lineH)
    {
        float? baseH = null;
        if (VenueShare.TryParse(text, out _))
        {
            baseH = Px(VenueCardH);
        }
        else if (HangoutShare.TryParse(text, out _))
        {
            baseH = Px(HangoutCardH);
        }
        else if (NewsShare.TryParse(text, out _))
        {
            baseH = Px(NewsCardH);
        }
        else if (CalendarEventShare.TryParse(text, out _))
        {
            baseH = Px(CalendarCardH);
        }
        else if (LocationShare.TryParse(text, out _))
        {
            baseH = LocationCardHeight(lineH);
        }
        else if (LevemeteShare.TryParse(text, out _))
        {
            baseH = Px(LevemeteCardH);
        }
        else if (MarketShare.TryParse(text, out _))
        {
            baseH = Px(MarketCardH);
        }
        if (baseH is not { } h)
        {
            return null;
        }
        var rowH = h + (isGroupEnd ? lineH + Px(8f) : Px(2f));
        if (needsDivider)
        {
            rowH += lineH + Px(16f);
        }
        return rowH;
    }

    /// <summary>Renders a whole-body share token as a card instead of a bubble. Returns false for plain text.</summary>
    private bool TryDrawCardMessage(OpenChatInfo open, MessengerMessageDto message, string text, float windowWidth, bool isGroupEnd)
    {
        if (VenueShare.TryParse(text, out var venueId))
        {
            DrawVenueCardMessage(message, venueId, windowWidth, isGroupEnd);
            return true;
        }
        if (HangoutShare.TryParse(text, out var hangoutId))
        {
            DrawHangoutCardMessage(message, hangoutId, windowWidth, isGroupEnd);
            return true;
        }
        if (NewsShare.TryParse(text, out var newsId))
        {
            DrawNewsCardMessage(message, newsId, windowWidth, isGroupEnd);
            return true;
        }
        if (CalendarEventShare.TryParse(text, out var calEvent))
        {
            DrawCalendarEventCard(message, calEvent, windowWidth, isGroupEnd);
            return true;
        }
        if (LocationShare.TryParse(text, out var location))
        {
            DrawLocationCardMessage(message, location, windowWidth, isGroupEnd);
            return true;
        }
        if (LevemeteShare.TryParse(text, out var adId))
        {
            DrawLevemeteCardMessage(message, adId, windowWidth, isGroupEnd);
            return true;
        }
        if (MarketShare.TryParse(text, out var marketItemId))
        {
            DrawMarketCardMessage(message, marketItemId, windowWidth, isGroupEnd);
            return true;
        }
        return false;
    }

    /// <summary>Card position + entrance shared by every card kind; returns the card rect and click/hover.</summary>
    private (Vector2 Tl, Vector2 Br, bool Clicked, bool Hovered, bool Fading, Vector2 Cursor) BeginCard(
        MessengerMessageDto msg, float windowWidth, float cardH, string idPrefix)
    {
        var mine = msg.SenderAccountId == _store.MyAccountId;
        var cursorPos = ImGui.GetCursorScreenPos();
        var cardW = windowWidth * 0.72f;

        var (entryDy, entryAlpha) = MessageEntrance(msg.Id);
        var fading = entryAlpha < 0.999f;
        if (fading)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, entryAlpha * ImGui.GetStyle().Alpha);
        }

        var left = mine ? cursorPos.X + windowWidth - cardW - Px(10) : cursorPos.X + Px(10);
        var tl = new Vector2(left, cursorPos.Y + entryDy);
        var br = tl + new Vector2(cardW, cardH);

        ImGui.SetCursorScreenPos(tl);
        var clicked = ImGui.InvisibleButton($"##{idPrefix}{msg.Id:N}", new Vector2(cardW, cardH));
        var hovered = ImGui.IsItemHovered();
        return (tl, br, clicked, hovered, fading, cursorPos);
    }

    private void EndCard(MessengerMessageDto msg, Vector2 tl, Vector2 br, Vector2 cursorPos, float cardH,
        bool isGroupEnd, bool fading)
    {
        var mine = msg.SenderAccountId == _store.MyAccountId;
        if (isGroupEnd)
        {
            var local = msg.CreatedAtUtc.LocalDateTime;
            var seenSuffix = mine && msg.Kind == MessengerChatKind.Direct && msg.ReadByPeerAtUtc is not null
                ? Loc.T("chat.seen_suffix")
                : string.Empty;
            var timeStr = local.ToString("HH:mm") + seenSuffix;
            var timeSize = ImGui.CalcTextSize(timeStr);
            var timeX = mine ? br.X - timeSize.X : tl.X;
            ImGui.SetCursorScreenPos(new Vector2(timeX, br.Y + Px(2f)));
            ImGui.TextColored(new Vector4(0.75f, 0.75f, 0.75f, 0.40f), timeStr);
            ImGui.SetCursorScreenPos(cursorPos + new Vector2(0, cardH + timeSize.Y + Px(8f)));
        }
        else
        {
            ImGui.SetCursorScreenPos(cursorPos + new Vector2(0, cardH + Px(2f)));
        }
        if (fading)
        {
            ImGui.PopStyleVar();
        }
    }

    private void DrawVenueCardMessage(MessengerMessageDto msg, Guid venueId, float windowWidth, bool isGroupEnd)
    {
        StartVenueCardFetch(venueId);
        _venueCards.TryGetValue(venueId, out var visual);

        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var cardH = Px(VenueCardH);
        var (tl, br, clicked, hovered, fading, cursorPos) = BeginCard(msg, windowWidth, cardH, "msgrVenueCard");
        var cardW = br.X - tl.X;

        if (visual?.Card is { } card)
        {
            var venue = card.Summary;
            var wrap = visual.Tex?.GetWrapOrDefault();
            if (wrap != null)
            {
                var (uv0, uv1) = CoverFitUvs(wrap.Width, wrap.Height, cardW, cardH);
                dl.AddImageRounded(wrap.Handle, tl, br, uv0, uv1, 0xFFFFFFFFu, Px(14f), ImDrawFlags.RoundCornersAll);
                if (visual.LogoBackdrop)
                {
                    dl.AddRectFilled(tl, br, 0x59000000u, Px(14f));
                }
            }
            else
            {
                dl.AddRectFilled(tl, br, ImGui.GetColorU32(t.Accent with { W = 0.14f }), Px(14f));
            }

            dl.AddRectFilledMultiColor(tl, new Vector2(br.X, tl.Y + Px(46f)),
                0x88000000u, 0x88000000u, 0x00000000u, 0x00000000u);
            dl.AddRectFilledMultiColor(new Vector2(tl.X, br.Y - Px(84f)), br,
                0x00000000u, 0x00000000u, 0xD8000000u, 0xD8000000u);
            dl.AddRect(tl, br, ImGui.GetColorU32(t.Accent with { W = hovered ? 0.90f : 0.55f }), Px(14f),
                ImDrawFlags.None, Px(1.5f));

            VenueFields.DrawStarSummary(dl, tl + new Vector2(Px(12f), Px(11f)),
                venue.AverageRating, venue.ReviewCount, Px(12f));

            var textX = tl.X + Px(14f);
            var textMaxW = cardW - Px(28f);
            float nameH;
            using (UiFonts.H3?.Push())
            {
                nameH = ImGui.GetFontSize();
                dl.AddText(ImGui.GetFont(), nameH, new Vector2(textX, br.Y - Px(58f)),
                    0xFFFFFFFFu, TruncateToWidth(venue.Name, textMaxW));
            }
            dl.AddText(new Vector2(textX, br.Y - Px(58f) + nameH + Px(3f)), ImGui.GetColorU32(UiColors.Body),
                TruncateToWidth(VenueFields.LocationLine(venue), textMaxW - Px(12f)));

            if (hovered)
            {
                ImGui.SetTooltip(Loc.T("places.share_view"));
            }
            if (clicked)
            {
                _venueShare.PendingOpenVenueId = venueId;
                _venueShare.PendingOpenReturnApp = Id;
                _shell?.OpenApp("places");
            }
        }
        else
        {
            dl.AddRectFilled(tl, br, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.05f)), Px(14f));
            dl.AddRect(tl, br, ImGui.GetColorU32(t.Accent with { W = 0.35f }), Px(14f), ImDrawFlags.None, Px(1.5f));
            var text = visual is null ? Loc.T("places.share_loading") : Loc.T("places.share_unavailable");
            var textSz = ImGui.CalcTextSize(text);
            dl.AddText(tl + (new Vector2(cardW, cardH) - textSz) * 0.5f, ImGui.GetColorU32(UiColors.Muted), text);
        }

        EndCard(msg, tl, br, cursorPos, cardH, isGroupEnd, fading);
    }

    private void DrawHangoutCardMessage(MessengerMessageDto msg, Guid hangoutId, float windowWidth, bool isGroupEnd)
    {
        StartHangoutCardFetch(hangoutId);
        _hangoutCards.TryGetValue(hangoutId, out var card);

        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var cardH = Px(HangoutCardH);
        var (tl, br, clicked, hovered, fading, cursorPos) = BeginCard(msg, windowWidth, cardH, "msgrHgCard");
        var cardW = br.X - tl.X;

        if (card is { Summary: { } h } && h.EndUtc > DateTimeOffset.UtcNow)
        {
            var live = HangoutFields.IsLiveNow(h);
            var accent = live ? UiColors.LiveGreen : t.Accent;

            dl.AddRectFilled(tl, br, ImGui.GetColorU32(accent with { W = 0.12f }), Px(14f));
            dl.AddRect(tl, br, ImGui.GetColorU32(accent with { W = hovered ? 0.90f : 0.55f }), Px(14f),
                ImDrawFlags.None, Px(1.5f));

            IconDraw.AddCentered(dl, HangoutFields.CategoryIcon(h.Category), Px(20f),
                new Vector2(tl.X + Px(26f), tl.Y + Px(28f)), ImGui.GetColorU32(accent));

            var textX = tl.X + Px(48f);
            var textMaxW = br.X - textX - Px(12f);
            dl.AddText(new Vector2(textX, tl.Y + Px(12f)), 0xFFFFFFFFu, TruncateToWidth(h.OwnerDisplayName, textMaxW));
            var status = (live ? Loc.T("hangout.chip_live") : HangoutFields.TimeLabel(h))
                + "  ·  " + HangoutFields.CategoryLabel(h.Category);
            dl.AddText(new Vector2(textX, tl.Y + Px(31f)), ImGui.GetColorU32(accent), TruncateToWidth(status, textMaxW));

            var bodyX = tl.X + Px(14f);
            var bodyMaxW = cardW - Px(28f);
            dl.AddText(new Vector2(bodyX, tl.Y + Px(56f)), ImGui.GetColorU32(UiColors.Body),
                TruncateToWidth(h.Description, bodyMaxW));
            var footer = HangoutFields.FormatAddress(h) + "  ·  " + Loc.T("hangout.coming_count", HangoutFields.CountLabel(h));
            dl.AddText(new Vector2(bodyX, tl.Y + Px(80f)), UiColors.TextMuted, TruncateToWidth(footer, bodyMaxW));

            if (hovered)
            {
                ImGui.SetTooltip(Loc.T("hangout.share_view"));
            }
            if (clicked)
            {
                OpenHangoutPopup(h);
            }
        }
        else
        {
            dl.AddRectFilled(tl, br, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.05f)), Px(14f));
            dl.AddRect(tl, br, ImGui.GetColorU32(t.Accent with { W = 0.35f }), Px(14f), ImDrawFlags.None, Px(1.5f));
            var text = card is null && !_hangoutCards.ContainsKey(hangoutId)
                ? Loc.T("places.share_loading")
                : Loc.T("hangout.share_unavailable");
            var textSz = ImGui.CalcTextSize(text);
            dl.AddText(tl + (new Vector2(cardW, cardH) - textSz) * 0.5f, ImGui.GetColorU32(UiColors.Muted), text);
        }

        EndCard(msg, tl, br, cursorPos, cardH, isGroupEnd, fading);
    }

    /// <summary>Self-contained location card (no fetch): the sharer's captured spot rides inside the token.
    /// Clicking drops an in-game map marker at the exact coordinates via the system capability.</summary>
    private void DrawLocationCardMessage(MessengerMessageDto msg, LocationShare.LocationCardData loc, float windowWidth, bool isGroupEnd)
    {
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var lineH = ImGui.GetTextLineHeight();
        var cardH = LocationCardHeight(lineH);
        var (tl, br, clicked, hovered, fading, cursorPos) = BeginCard(msg, windowWidth, cardH, "msgrLocCard");

        dl.AddRectFilled(tl, br, ImGui.GetColorU32(t.Accent with { W = 0.12f }), Px(12f));
        dl.AddRect(tl, br, ImGui.GetColorU32(t.Accent with { W = hovered ? 0.90f : 0.55f }), Px(12f),
            ImDrawFlags.None, Px(1.5f));

        IconDraw.AddCentered(dl, FontAwesomeIcon.MapMarkerAlt, Px(20f),
            new Vector2(tl.X + Px(26f), tl.Y + cardH * 0.5f), ImGui.GetColorU32(t.Accent));

        var textX = tl.X + Px(48f);
        var textMaxW = br.X - textX - Px(12f);

        var zone = LocationShare.ZoneName(loc.TerritoryId);
        var regionDc = JoinParts(RegionAbbrev(loc.DataCenter), loc.DataCenter);
        var worldZone = JoinParts(loc.World, zone);
        if (loc.Ward > 0)
        {
            worldZone = JoinParts(worldZone, Loc.T("os.msgr_location_ward", loc.Ward),
                loc.Plot > 0 ? Loc.T("os.msgr_location_plot", loc.Plot) : string.Empty);
        }

        var gap = Px(3f);
        var textTop = tl.Y + (cardH - (3 * lineH + 2 * gap)) * 0.5f;

        dl.AddText(new Vector2(textX, textTop), 0xFFFFFFFFu, Loc.T("os.msgr_location_title"));
        dl.AddText(new Vector2(textX, textTop + lineH + gap), ImGui.GetColorU32(UiColors.Body),
            TruncateToWidth(regionDc, textMaxW));
        dl.AddText(new Vector2(textX, textTop + 2f * (lineH + gap)), UiColors.TextMuted,
            TruncateToWidth(worldZone, textMaxW));

        if (hovered)
        {
            ImGui.BeginTooltip();
            if (regionDc.Length > 0)
            {
                ImGui.TextUnformatted(regionDc);
            }
            if (worldZone.Length > 0)
            {
                ImGui.TextUnformatted(worldZone);
            }
            ImGui.Separator();
            ImGui.TextDisabled(Loc.T("os.msgr_location_open"));
            ImGui.EndTooltip();
        }
        if (clicked)
        {
            _caps.System.OpenMapMarker(loc.TerritoryId, loc.MapId, loc.MapX, loc.MapY, zone);
        }

        EndCard(msg, tl, br, cursorPos, cardH, isGroupEnd, fading);
    }

    private void DrawNewsCardMessage(MessengerMessageDto msg, Guid newsId, float windowWidth, bool isGroupEnd)
    {
        StartNewsCardFetch(newsId);
        _newsCards.TryGetValue(newsId, out var card);

        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var cardH = Px(NewsCardH);
        var (tl, br, clicked, hovered, fading, cursorPos) = BeginCard(msg, windowWidth, cardH, "msgrNewsCard");
        var cardW = br.X - tl.X;

        dl.AddRectFilled(tl, br, ImGui.GetColorU32(t.Accent with { W = 0.13f }), Px(14f));
        dl.AddRect(tl, br, ImGui.GetColorU32(t.Accent with { W = hovered ? 0.90f : 0.55f }), Px(14f),
            ImDrawFlags.None, Px(1.5f));

        var padX = Px(14f);
        var textX = tl.X + padX;
        var textMaxW = cardW - padX * 2f;

        var eyebrow = Loc.T("chat.news_card_label");
        var iconPx = ImGui.GetFontSize() * 0.82f;
        IconDraw.Add(dl, FontAwesomeIcon.Newspaper, iconPx, new Vector2(textX, tl.Y + Px(11f)),
            ImGui.GetColorU32(t.AccentLight));
        dl.AddText(ImGui.GetFont(), iconPx, new Vector2(textX + IconDraw.Measure(FontAwesomeIcon.Newspaper, iconPx).X + Px(7f),
            tl.Y + Px(11f)), ImGui.GetColorU32(t.AccentLight), eyebrow);

        if (card is { } news)
        {
            float titleH;
            using (UiFonts.H3?.Push())
            {
                titleH = ImGui.GetFontSize();
                dl.AddText(ImGui.GetFont(), titleH, new Vector2(textX, tl.Y + Px(36f)),
                    0xFFFFFFFFu, TruncateToWidth(news.Title, textMaxW));
            }
            var previewY = tl.Y + Px(36f) + titleH + Px(6f);
            dl.PushClipRect(new Vector2(textX, previewY), new Vector2(br.X - padX, br.Y - Px(8f)), true);
            dl.AddText(new Vector2(textX, previewY), ImGui.GetColorU32(UiColors.Body),
                TruncateToWidth(news.Preview, textMaxW));
            dl.PopClipRect();

            if (hovered)
            {
                ImGui.SetTooltip(Loc.T("chat.news_card_view"));
            }
            if (clicked)
            {
                _shell?.SendIntent("news", OsIntents.CreateReturn(OsIntents.OpenEntry, newsId, Id));
            }
        }
        else
        {
            var text = !_newsCards.ContainsKey(newsId) ? Loc.T("places.share_loading") : Loc.T("chat.news_card_unavailable");
            var textSz = ImGui.CalcTextSize(text);
            dl.AddText(new Vector2(tl.X + (cardW - textSz.X) * 0.5f, tl.Y + Px(58f)),
                ImGui.GetColorU32(UiColors.Muted), text);
        }

        EndCard(msg, tl, br, cursorPos, cardH, isGroupEnd, fading);
    }

    private void DrawCalendarEventCard(MessengerMessageDto msg, CalendarEventShare.Payload ev, float windowWidth, bool isGroupEnd)
    {
        if (ev.IsVenue)
        {
            StartVenueCardFetch(ev.VenueId);
        }
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var cardH = Px(CalendarCardH);
        var (tl, br, clicked, hovered, fading, cursorPos) = BeginCard(msg, windowWidth, cardH, "msgrCalCard");
        var cardW = br.X - tl.X;

        dl.AddRectFilled(tl, br, ImGui.GetColorU32(t.Accent with { W = 0.14f }), Px(14f));
        dl.AddRect(tl, br, ImGui.GetColorU32(t.Accent with { W = hovered ? 0.90f : 0.55f }), Px(14f),
            ImDrawFlags.None, Px(1.5f));

        var iconR = Px(18f);
        var iconC = new Vector2(tl.X + Px(14f) + iconR, (tl.Y + br.Y) * 0.5f);
        dl.AddCircleFilled(iconC, iconR, ImGui.GetColorU32(t.Accent));
        var glyph = FontAwesomeIcon.CalendarAlt.ToIconString();
        ImGui.PushFont(Dalamud.Interface.UiBuilder.IconFont);
        var glyphSz = ImGui.CalcTextSize(glyph) * (Px(16f) / ImGui.GetFontSize());
        dl.AddText(ImGui.GetFont(), Px(16f), iconC - glyphSz * 0.5f, 0xFFFFFFFFu, glyph);
        ImGui.PopFont();

        var (title, sub, ready) = ResolveCalendarEventText(ev);
        var textX = iconC.X + iconR + Px(12f);
        var lineH = ImGui.GetTextLineHeight();
        var textMaxW = br.X - textX - Px(10f);
        dl.AddText(new Vector2(textX, tl.Y + Px(14f)), 0xFFFFFFFFu, TruncateToWidth(title, textMaxW));
        dl.AddText(new Vector2(textX, tl.Y + Px(14f) + lineH + Px(2f)), ImGui.GetColorU32(t.Accent),
            FormatCalendarEventTime(ev.StartUtc));
        if (sub.Length > 0)
        {
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.82f,
                new Vector2(textX, tl.Y + Px(14f) + (lineH + Px(2f)) * 2f), ImGui.GetColorU32(UiColors.Muted),
                TruncateToWidth(sub.Replace('\n', ' '), textMaxW));
        }

        if (clicked && ready)
        {
            _calPrompt = ev;
            _calPromptH = 0f;
        }
        else if (hovered)
        {
            ImGui.SetTooltip(Loc.T("os.msgr_card_view"));
        }

        EndCard(msg, tl, br, cursorPos, cardH, isGroupEnd, fading);
    }

    /// <summary>Title/subtitle for a calendar-event card: venue kind resolves against the live-fetched venue
    /// card; ready is false while that fetch is still in flight.</summary>
    private (string Title, string Sub, bool Ready) ResolveCalendarEventText(CalendarEventShare.Payload ev)
    {
        if (!ev.IsVenue)
        {
            return (ev.Title, ev.Note, true);
        }
        if (_venueCards.TryGetValue(ev.VenueId, out var visual) && visual.Card is { Summary: { } venue })
        {
            return (venue.Name, VenueFields.LocationLine(venue), true);
        }
        return (Loc.T(_venueCards.ContainsKey(ev.VenueId) ? "places.share_unavailable" : "places.share_loading"), "", false);
    }

    private static string FormatCalendarEventTime(DateTimeOffset startUtc) =>
        startUtc.ToLocalTime().ToString("ddd d MMM' · 'HH:mm", LanguageProvider.CurrentCulture);

    /// <summary>The tapped shared-event chooser: RSVP (future venue events) or add a local calendar copy.
    /// Uses the shared page overlay so it layers above the messages child.</summary>
    private void DrawCalendarEventPrompt()
    {
        if (_calPrompt is not { } ev)
        {
            return;
        }
        if (ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            _calPrompt = null;
            return;
        }

        var t = ThemeService.Current;
        var canRsvp = ev.IsVenue && ev.StartUtc > DateTimeOffset.UtcNow;
        var dismissed = DrawPageOverlayPanel("msgrCalPrompt", ImGui.GetWindowPos(), ImGui.GetWindowSize(),
            ref _calPromptH, Px(180f), innerW =>
        {
            var (title, _, _) = ResolveCalendarEventText(ev);
            ImGui.TextUnformatted(TruncateToWidth(title, innerW));
            ImGui.TextColored(t.Accent, FormatCalendarEventTime(ev.StartUtc));
            ImGui.Dummy(new Vector2(0f, Px(8f)));

            if (canRsvp)
            {
                if (ModalUi.Button($"{Loc.T("chat.calevent_rsvp")}##msgrCalRsvp", innerW))
                {
                    var venueId = ev.VenueId;
                    var start = ev.StartUtc;
                    RunHub(() => _hub.SetVenueRsvpAsync(venueId, start, true));
                    _calPrompt = null;
                }
                ImGui.Spacing();
            }
            if (ModalUi.Button($"{Loc.T("chat.calevent_add")}##msgrCalAdd", innerW))
            {
                var (calTitle, _, _) = ResolveCalendarEventText(ev);
                _shell?.SendIntent("calendar",
                    OsIntents.CreateCalendarAdd(calTitle, ev.IsVenue ? "" : ev.Note, ev.StartUtc.ToUnixTimeSeconds()));
                _calPrompt = null;
            }
            ImGui.Spacing();
            if (ModalUi.Button($"{Loc.T("common.cancel")}##msgrCalCancel", innerW))
            {
                _calPrompt = null;
            }
        });
        if (dismissed)
        {
            _calPrompt = null;
        }
    }

    /// <summary>The live hangout a direct chat's peer is hosting, if any (drives the row marker).</summary>
    private HangoutSummaryDto? HostingFor(ChatRow row)
    {
        if (row.Kind != MessengerChatKind.Direct || row.RemovedByPeer)
        {
            return null;
        }
        var contact = _store.Contact(row.Id);
        if (contact is null)
        {
            return null;
        }
        var hg = _hangoutState.ForContact(contact.PeerAccountId);
        return hg is null || hg.EndUtc <= DateTimeOffset.UtcNow ? null : hg;
    }

    private void DrawLevemeteCardMessage(MessengerMessageDto msg, Guid adId, float windowWidth, bool isGroupEnd)
    {
        StartLevemeteCardFetch(adId);
        _levemeteCards.TryGetValue(adId, out var visual);

        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var cardH = Px(LevemeteCardH);
        var (tl, br, clicked, hovered, fading, cursorPos) = BeginCard(msg, windowWidth, cardH, "msgrLeveCard");
        var cardW = br.X - tl.X;

        if (visual?.Card is { } card)
        {
            var wrap = visual.Tex?.GetWrapOrDefault();
            if (wrap != null)
            {
                var (uv0, uv1) = CoverFitUvs(wrap.Width, wrap.Height, cardW, cardH);
                dl.AddImageRounded(wrap.Handle, tl, br, uv0, uv1, 0xFFFFFFFFu, Px(14f), ImDrawFlags.RoundCornersAll);
            }
            else
            {
                dl.AddRectFilled(tl, br, ImGui.GetColorU32(t.Accent with { W = 0.14f }), Px(14f));
            }

            dl.AddRectFilledMultiColor(new Vector2(tl.X, br.Y - Px(70f)), br,
                0x00000000u, 0x00000000u, 0xD8000000u, 0xD8000000u);
            dl.AddRect(tl, br, ImGui.GetColorU32(t.Accent with { W = hovered ? 0.90f : 0.55f }), Px(14f),
                ImDrawFlags.None, Px(1.5f));

            var textX = tl.X + Px(14f);
            var textMaxW = cardW - Px(28f);
            float nameH;
            using (UiFonts.H3?.Push())
            {
                nameH = ImGui.GetFontSize();
                dl.AddText(ImGui.GetFont(), nameH, new Vector2(textX, br.Y - Px(52f)),
                    0xFFFFFFFFu, TruncateToWidth(card.Title, textMaxW));
            }
            var kindLabel = Loc.T(card.Kind == (short)LevemeteKind.Offering
                ? "chat.leve_kind_offering"
                : "chat.leve_kind_looking");
            var catLabel = Enum.IsDefined((LevemeteCategory)card.Category)
                ? Loc.T($"chat.leve_cat_{card.Category}")
                : Loc.T("chat.leve_cat_unknown");
            dl.AddText(new Vector2(textX, br.Y - Px(52f) + nameH + Px(3f)), ImGui.GetColorU32(UiColors.Body),
                TruncateToWidth($"{kindLabel} · {catLabel}", textMaxW - Px(12f)));

            if (hovered)
            {
                ImGui.SetTooltip(Loc.T("chat.leve_card_view"));
            }
            if (clicked)
            {
                _levemeteShare.PendingOpenLevemeteId = adId;
                _levemeteShare.PendingOpenReturnApp = Id;
                _shell?.OpenApp("levemetes");
            }
        }
        else
        {
            dl.AddRectFilled(tl, br, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.05f)), Px(14f));
            dl.AddRect(tl, br, ImGui.GetColorU32(t.Accent with { W = 0.35f }), Px(14f), ImDrawFlags.None, Px(1.5f));
            var text = visual is null ? Loc.T("chat.leve_card_loading") : Loc.T("chat.leve_card_unavailable");
            var textSz = ImGui.CalcTextSize(text);
            dl.AddText(tl + (new Vector2(cardW, cardH) - textSz) * 0.5f, ImGui.GetColorU32(UiColors.Muted), text);
        }

        EndCard(msg, tl, br, cursorPos, cardH, isGroupEnd, fading);
    }

    private void StartMarketPriceFetch(uint itemId)
    {
        if (_marketPrices.ContainsKey(itemId) || !_marketPriceFetches.TryAdd(itemId, 0))
        {
            return;
        }
        var detected = AetherLove.Services.Market.MarketScopes.DetectCurrent();
        _ = Task.Run(async () =>
        {
            try
            {
                var scopes = detected;
                if (scopes is null)
                {
                    return;
                }
                var agg = await _marketData.GetAggregatedAsync(scopes.Value.DataCenter, [itemId],
                    System.Threading.CancellationToken.None).ConfigureAwait(false);
                long price = 0;
                if (agg.TryGetValue(itemId, out var result))
                {
                    price = result.Nq.MinListing?.At(AetherLove.Services.Market.MarketScopeKind.DataCenter)?.Price
                        ?? result.Hq.MinListing?.At(AetherLove.Services.Market.MarketScopeKind.DataCenter)?.Price
                        ?? 0;
                }
                _marketPrices[itemId] = price;
            }
            catch (Exception ex)
            {
                AetherLove.UiHost.Log.Debug($"[MessengerApp] Market card price fetch failed: {ex.Message}");
            }
            finally
            {
                _marketPriceFetches.TryRemove(itemId, out _);
            }
        });
    }

    private void DrawMarketCardMessage(MessengerMessageDto msg, uint itemId, float windowWidth, bool isGroupEnd)
    {
        StartMarketPriceFetch(itemId);
        _marketIndex.EnsureBuildStarted();
        _marketIndex.TryGet(itemId, out var entry);

        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var cardH = Px(MarketCardH);
        var (tl, br, clicked, hovered, fading, cursorPos) = BeginCard(msg, windowWidth, cardH, "msgrMarketCard");
        var cardW = br.X - tl.X;

        dl.AddRectFilled(tl, br, ImGui.GetColorU32(t.Accent with { W = 0.13f }), Px(14f));
        dl.AddRect(tl, br, ImGui.GetColorU32(t.Accent with { W = hovered ? 0.90f : 0.55f }), Px(14f),
            ImDrawFlags.None, Px(1.5f));

        var padX = Px(12f);
        var eyebrow = Loc.T("chat.market_card_label");
        var iconPx = ImGui.GetFontSize() * 0.82f;
        IconDraw.Add(dl, FontAwesomeIcon.Coins, iconPx, new Vector2(tl.X + padX, tl.Y + Px(10f)),
            ImGui.GetColorU32(t.AccentLight));
        dl.AddText(ImGui.GetFont(), iconPx,
            new Vector2(tl.X + padX + IconDraw.Measure(FontAwesomeIcon.Coins, iconPx).X + Px(7f), tl.Y + Px(10f)),
            ImGui.GetColorU32(t.AccentLight), eyebrow);

        var itemIconSize = Px(36f);
        var itemIconTl = new Vector2(tl.X + padX, tl.Y + Px(32f));
        if (AetherLove.Services.Market.MarketItemIcons.Get(entry.Icon) is { } handle)
        {
            dl.AddImageRounded(handle, itemIconTl, itemIconTl + new Vector2(itemIconSize, itemIconSize),
                Vector2.Zero, Vector2.One, 0xFFFFFFFFu, Px(6f));
        }
        else
        {
            dl.AddRectFilled(itemIconTl, itemIconTl + new Vector2(itemIconSize, itemIconSize),
                ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.08f)), Px(6f));
        }

        var textX = itemIconTl.X + itemIconSize + Px(10f);
        var textMaxW = br.X - padX - textX;
        var name = entry.Name.Length > 0 ? entry.Name : $"#{itemId}";
        dl.AddText(new Vector2(textX, tl.Y + Px(34f)), 0xFFFFFFFFu, TruncateToWidth(name, textMaxW));

        var priceText = _marketPrices.TryGetValue(itemId, out var price) && price > 0
            ? $"{AetherLove.Services.Market.MarketFormat.GilFull(price)} gil"
            : Loc.T("places.share_loading");
        dl.AddText(new Vector2(textX, tl.Y + Px(34f) + ImGui.GetTextLineHeight() + Px(3f)),
            ImGui.GetColorU32(new Vector4(0.98f, 0.80f, 0.36f, 1f)), TruncateToWidth(priceText, textMaxW));

        if (hovered)
        {
            ImGui.SetTooltip(Loc.T("chat.market_card_view"));
        }
        if (clicked)
        {
            _shell?.SendIntent("market", OsIntents.CreateMarketItem(itemId, Id));
        }

        EndCard(msg, tl, br, cursorPos, cardH, isGroupEnd, fading);
    }
}
