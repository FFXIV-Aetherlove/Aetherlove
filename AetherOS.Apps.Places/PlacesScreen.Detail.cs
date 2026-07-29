using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove;
using AetherLove.Emoji;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Shared;
using AetherLove.Shared.Places;
using AetherLove.Shared.Profile.Enums;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;
using Widgets = AetherLove.Widgets;

namespace AetherOS.Apps.Places;

public partial class PlacesScreen
{
    private Guid _detailVenueId;
    private string _detailName = "";
    private volatile VenueDetailDto? _detail;
    private volatile bool _detailLoading;
    private volatile string? _detailError;
    private ISharedImmediateTexture? _bannerTex;
    /// <summary>All visible banner slots in order; more than one = the supporter carousel.</summary>
    private readonly List<ISharedImmediateTexture?> _bannerTexes = new();
    private readonly System.Collections.Generic.Dictionary<Guid, ISharedImmediateTexture?> _reviewAvatarTex = new();
    private System.Collections.Generic.List<VenueReviewDto> _extraReviews = [];
    private volatile bool _moreReviewsLoading;
    private bool _reviewsExhausted;

    private readonly EmojiPickerPopup _reviewEmojiPicker = new();
    private int _composeRating;
    private string _composeText = "";
    private volatile bool _reviewSubmitting;
    private volatile string? _reviewError;
    private bool _confirmDeleteReview;
    private float _confirmPanelHeight;
    private volatile bool _engagementBusy;
    private double _likePoppedAt = double.MinValue;
    private bool _tagsExpanded;
    private readonly EntranceAnimation _detailEntrance = new();

    /// <summary>Set by any like/RSVP/review change so returning to the browse list refetches it.</summary>
    private bool _browseStale;

    private void OpenDetail(VenueSummaryDto venue)
    {
        _detailFromChat = false;
        _detailVenueId = venue.Id;
        _detailName = venue.Name;
        _detail = null;
        _detailError = null;
        _extraReviews = [];
        _reviewsExhausted = false;
        _confirmDeleteReview = false;
        _reviewError = null;
        _tagsExpanded = false;
        _section = Section.Detail;
        StartDetailFetch();
    }

    private void StartDetailFetch()
    {
        if (_detailLoading)
        {
            return;
        }
        _detailLoading = true;
        var venueId = _detailVenueId;
        var ct = _cts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                var dto = await _host.GetVenueDetailAsync(venueId, ct).ConfigureAwait(false);
                if (ct.IsCancellationRequested || venueId != _detailVenueId)
                {
                    return;
                }
                CacheDetailTextures(dto);
                _composeRating = dto.MyReview?.Rating ?? 0;
                _composeText = dto.MyReview?.Text ?? "";
                _detail = dto;
                _detailEntrance.Arm();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, "[PlacesScreen] Venue detail fetch failed.");
                _detailError = HubErrorText.Localize(ex);
            }
            finally
            {
                _detailLoading = false;
            }
        }, ct);
    }

    private void CacheDetailTextures(VenueDetailDto dto)
    {
        _bannerTexes.Clear();
        foreach (var b in dto.Banners ?? [])
        {
            if (b.Webp is { Length: > 0 })
            {
                _bannerTexes.Add(AvatarDiskCache.Store(PlacesCacheDir, $"banner_{dto.Summary.Id:N}_{b.Slot}", b.Webp));
            }
        }
        _bannerTex = _bannerTexes.Count > 0
            ? _bannerTexes[0]
            : dto.BannerWebp is { Length: > 0 }
                ? AvatarDiskCache.Store(PlacesCacheDir, $"banner_{dto.Summary.Id:N}", dto.BannerWebp)
                : null;
        _logoTex[dto.Summary.Id] = dto.Summary.LogoWebp is { Length: > 0 }
            ? AvatarDiskCache.Store(PlacesCacheDir, $"logo_{dto.Summary.Id:N}", dto.Summary.LogoWebp)
            : null;
        foreach (var occ in dto.Occurrences)
        {
            CacheClumpTextures(occ);
        }
        CacheReviewAvatars(dto.Reviews);
        if (dto.MyReview is not null)
        {
            CacheReviewAvatars([dto.MyReview]);
        }
    }

    private void CacheReviewAvatars(VenueReviewDto[] reviews)
    {
        foreach (var review in reviews)
        {
            if (!_reviewAvatarTex.ContainsKey(review.Id) && review.AuthorAvatarWebp is { Length: > 0 })
            {
                _reviewAvatarTex[review.Id] = AvatarDiskCache.Store(
                    PlacesCacheDir, $"rev_{review.Id:N}", review.AuthorAvatarWebp);
            }
        }
    }

    private void DrawDetail()
    {
        var detail = _detail;
        PushScrollbarStyle();
        var scrollViewportTL = ImGui.GetCursorScreenPos();
        using (var scroll = ImRaii.Child("##venueDetailScroll", ImGui.GetContentRegionAvail(), false))
        {
            if (scroll.Success)
            {
                ImGui.Dummy(new Vector2(1f, Px(4f)));
                if (_detailLoading && detail is null)
                {
                    Widgets.LoadingIndicator.Draw();
                }
                else if (_detailError is not null && detail is null)
                {
                    DrawCenteredMuted(Loc.T("places.load_failed", _detailError));
                }
                else if (detail is not null)
                {
                    DrawDetailContent(detail);
                }
            }
        }
        PopScrollbarStyle();

        if (DrawFloatingBackPill(scrollViewportTL + Px(10f, 10f),
                Loc.T(_detailFromChat ? "places.back_to_chat" : "places.back"),
                _detailFromChat ? FontAwesomeIcon.Comment : FontAwesomeIcon.List))
        {
            if (_detailFromChat)
            {
                _detailFromChat = false;
                _section = Section.Browse;
                if (_detailReturnApp is { } returnApp)
                {
                    _detailReturnApp = null;
                    _shell?.OpenApp(returnApp);
                }
                else
                {
                    _social.OpenChat();
                }
            }
            else
            {
                _section = Section.Browse;
                _entrance.Arm();
                if (_browseStale)
                {
                    _browseStale = false;
                    StartBrowseFetch();
                }
            }
        }
        DrawDeleteReviewConfirm();
    }

    private void DrawDetailContent(VenueDetailDto detail)
    {
        var t = ThemeService.Current;
        var winW = ImGui.GetContentRegionAvail().X;
        var pad = Px(PadX);
        var cardW = winW - pad * 2f;
        var venue = detail.Summary;
        _detailEntrance.BeginFrame();

        DrawBannerHeader(detail, cardW);
        ImGui.Dummy(new Vector2(1f, Px(4f)));
        DrawGradientDivider(cardW);
        ImGui.Dummy(new Vector2(1f, Px(4f)));

        var ratingRowTop = ImGui.GetCursorScreenPos();
        DrawReviewSummary(venue, pad);
        DrawSharePill(winW, ratingRowTop);
        ImGui.Dummy(new Vector2(1f, Px(4f)));
        DrawGradientDivider(cardW);
        ImGui.Dummy(new Vector2(1f, Px(6f)));

        ImGui.SetCursorPosX(pad);
        if (VenueFields.DrawTagPills(venue.Tags, cardW, _tagsExpanded))
        {
            _tagsExpanded = true;
        }
        ImGui.Dummy(new Vector2(1f, Px(4f)));
        DrawGradientDivider(cardW);
        ImGui.Dummy(new Vector2(1f, Px(8f)));

        DrawLocationCard(venue, cardW);

        if (detail.Description.Length > 0)
        {
            ImGui.Dummy(new Vector2(1f, Px(12f)));
            DrawH2Header(Loc.T("places.venue_description"));
            ImGui.SetCursorPosX(pad);
            var parsed = ParsedMessage.Parse(detail.Description);
            parsed.DrawWrapped("##venueDesc", cardW);
        }

        if (detail.Discord.Length > 0)
        {
            ImGui.Dummy(new Vector2(1f, Px(8f)));
            ImGui.SetCursorPosX(pad);
            DrawDiscordButton($"{Loc.T("places.venue_discord_button")}##venueDiscord",
                new Vector2(cardW, Px(30f)), detail.Discord);
        }

        ImGui.Spacing();
        DrawH2Header(Loc.T("places.schedule"));
        if (detail.Occurrences.Length == 0)
        {
            DrawCenteredMuted(Loc.T("places.nothing_upcoming"));
        }
        else
        {
            foreach (var occ in detail.Occurrences)
            {
                DrawScheduleRow(detail, occ, cardW);
            }
        }

        ImGui.Spacing();
        DrawH2Header(Loc.T("places.reviews_title"));
        DrawReviewCompose(detail, cardW);

        foreach (var review in detail.Reviews.Concat(_extraReviews))
        {
            DrawReviewCard(review, cardW);
            ImGui.Spacing();
        }
        if (detail.Reviews.Length + _extraReviews.Count == 0 && detail.MyReview is null)
        {
            DrawCenteredMuted(Loc.T("places.no_reviews"));
        }

        DrawLoadMoreReviews(detail, cardW);
        ImGui.Dummy(new Vector2(1f, Px(12f)));
        _detailEntrance.EndFrame();
    }

    private static string RegionLabel(Region region)
    {
        var idx = IndexOf(RegionValues, region, -1);
        return idx >= 0 ? Regions[idx] : "";
    }

    private static void DrawH2Header(string title)
    {
        ImGui.SetCursorPosX(Px(PadX));
        using (UiFonts.H2?.Push())
        {
            ImGui.TextColored(ThemeService.Current.AccentLight, title);
        }
        ImGui.Spacing();
    }

    private static void DrawGradientDivider(float cardW)
    {
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var pad = Px(PadX);
        var h = Px(2.5f);
        var origin = ImGui.GetCursorScreenPos();
        var left = origin.X + pad;
        var phase = AccessibilityService.ReduceMotion ? 0f : (float)(ImGui.GetTime() * 0.12f);
        const int strips = 44;
        for (var i = 0; i < strips; i++)
        {
            var f0 = i / (float)strips;
            var f1 = (i + 1) / (float)strips;
            var wave = (f0 + phase) % 1f;
            var blend = wave < 0.5f ? wave * 2f : (1f - wave) * 2f;
            var col = ImGui.GetColorU32(Vector4.Lerp(t.Accent, t.SecondaryEnd, blend) with { W = 0.9f });
            dl.AddRectFilled(new Vector2(left + f0 * cardW, origin.Y), new Vector2(left + f1 * cardW, origin.Y + h), col);
        }
        ImGui.Dummy(new Vector2(1f, h));
    }

    private static void DrawPulsingGradientBorder(ImDrawListPtr dl, Vector2 tl, Vector2 br, float rounding)
    {
        var t = ThemeService.Current;
        var reduce = AccessibilityService.ReduceMotion;
        var drift = reduce ? 0.5f : 0.5f + 0.5f * MathF.Sin((float)ImGui.GetTime() * 1.6f);
        var pulse = reduce ? 0.8f : 0.55f + 0.35f * MathF.Sin((float)ImGui.GetTime() * 3f);
        var col = Vector4.Lerp(t.Accent, t.SecondaryEnd, drift) with { W = pulse };
        dl.AddRect(tl, br, ImGui.GetColorU32(col), rounding, ImDrawFlags.None, Px(2f));
    }

    private void DrawLocationCard(VenueSummaryDto venue, float cardW)
    {
        var dl = ImGui.GetWindowDrawList();
        var pad = Px(PadX);
        var line1 = VenueFields.LocationLine(venue.World, venue.District, venue.Ward, venue.Plot, venue.Room);
        var line2 = $"{venue.DataCenter} · {RegionLabel(venue.Region)}";
        var lineH = ImGui.GetTextLineHeight();
        var cardH = Px(12f) + lineH + Px(3f) + lineH + Px(12f);

        var origin = ImGui.GetCursorScreenPos();
        var tl = new Vector2(origin.X + pad, origin.Y);
        var br = tl + new Vector2(cardW, cardH);
        dl.AddRectFilled(tl, br, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.045f)), Px(10f));

        var iconPx = Px(20f);
        var iconSz = IconDraw.Measure(FontAwesomeIcon.MapMarkerAlt, iconPx);
        IconDraw.Add(dl, FontAwesomeIcon.MapMarkerAlt, iconPx,
            new Vector2(tl.X + Px(12f), tl.Y + (cardH - iconSz.Y) * 0.5f),
            ImGui.GetColorU32(ThemeService.Current.AccentLight));

        var textX = tl.X + Px(12f) + iconSz.X + Px(12f);
        var textMaxW = br.X - textX - Px(12f);
        dl.AddText(new Vector2(textX, tl.Y + Px(12f)), ImGui.GetColorU32(UiColors.Body),
            TruncateToWidth(line1, textMaxW));
        dl.AddText(new Vector2(textX, tl.Y + Px(12f) + lineH + Px(3f)), ImGui.GetColorU32(UiColors.Muted),
            TruncateToWidth(line2, textMaxW));

        ImGui.SetCursorScreenPos(new Vector2(origin.X, br.Y));
    }

    private void DrawBannerHeader(VenueDetailDto detail, float cardW)
    {
        var venue = detail.Summary;
        var dl = ImGui.GetWindowDrawList();
        var pad = Px(PadX);
        var h = Px(130f);
        var origin = ImGui.GetCursorScreenPos();
        var tl = new Vector2(origin.X + pad, origin.Y);
        var br = tl + new Vector2(cardW, h);

        var bannerHandle = _bannerTex;
        var fade = 0f;
        ISharedImmediateTexture? nextHandle = null;
        if (_bannerTexes.Count > 1)
        {
            const double Cycle = 6.0;
            const double FadeSpan = 0.8;
            var tNow = ImGui.GetTime();
            var idx = (int)(tNow / Cycle) % _bannerTexes.Count;
            bannerHandle = _bannerTexes[idx] ?? bannerHandle;
            var phase = tNow % Cycle;
            if (!AccessibilityService.ReduceMotion && phase > Cycle - FadeSpan)
            {
                fade = (float)((phase - (Cycle - FadeSpan)) / FadeSpan);
                nextHandle = _bannerTexes[(idx + 1) % _bannerTexes.Count];
            }
        }

        var wrap = bannerHandle?.GetWrapOrDefault();
        if (wrap != null)
        {
            var (uv0, uv1) = SharedUiHelpers.CoverFitUvs(wrap.Width, wrap.Height, cardW, h);
            dl.AddImageRounded(wrap.Handle, tl, br, uv0, uv1, 0xFFFFFFFFu, Px(12f), ImDrawFlags.RoundCornersAll);
            if (nextHandle?.GetWrapOrDefault() is { } nextWrap && fade > 0f)
            {
                var tint = ((uint)(fade * 255f) << 24) | 0x00FFFFFFu;
                var (nuv0, nuv1) = SharedUiHelpers.CoverFitUvs(nextWrap.Width, nextWrap.Height, cardW, h);
                dl.AddImageRounded(nextWrap.Handle, tl, br, nuv0, nuv1, tint, Px(12f), ImDrawFlags.RoundCornersAll);
            }
        }
        else
        {
            dl.AddRectFilled(tl, br, ImGui.GetColorU32(ThemeService.Current.Accent with { W = 0.14f }), Px(12f));
        }
        dl.AddRectFilledMultiColor(new Vector2(tl.X, br.Y - Px(56f)), br,
            0x00000000u, 0x00000000u, 0xC0000000u, 0xC0000000u);
        dl.AddRect(tl, br, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.10f)), Px(12f), ImDrawFlags.None, Px(1f));

        var logoSize = Px(40f);
        var logoTL = new Vector2(tl.X + Px(12f), br.Y - logoSize - Px(10f));
        DrawLogo(dl, venue.Id, logoTL, logoSize);
        using (UiFonts.H3?.Push())
        {
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
                new Vector2(logoTL.X + logoSize + Px(10f), br.Y - Px(16f) - ImGui.GetFontSize()),
                0xFFFFFFFFu, TruncateToWidth(venue.Name, cardW - logoSize - Px(90f)));
        }

        DrawLikeButton(detail, new Vector2(br.X - Px(12f), tl.Y + Px(12f)));

        ImGui.SetCursorScreenPos(new Vector2(origin.X, br.Y + Px(4f)));
    }

    private void DrawLikeButton(VenueDetailDto detail, Vector2 topRight)
    {
        var dl = ImGui.GetWindowDrawList();
        var venue = detail.Summary;
        var label = venue.LikeCount.ToString();
        var heart = FontAwesomeIcon.Heart.ToIconString();

        ImGui.PushFont(UiHost.PluginInterface.UiBuilder.FontIcon);
        var heartSz = ImGui.CalcTextSize(heart);
        ImGui.PopFont();
        var labelSz = ImGui.CalcTextSize(label);
        var padX = Px(9f);
        var h = MathF.Max(heartSz.Y, labelSz.Y) + Px(10f);
        var w = heartSz.X + Px(6f) + labelSz.X + padX * 2f;
        var tl = new Vector2(topRight.X - w, topRight.Y);

        ImGui.SetCursorScreenPos(tl);
        var clicked = ImGui.InvisibleButton("##venueLike", new Vector2(w, h));
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            SharedUiHelpers.HandOnHover();
        }

        dl.AddRectFilled(tl, tl + new Vector2(w, h),
            ImGui.GetColorU32(new Vector4(0.09f, 0.09f, 0.11f, hovered ? 0.95f : 0.82f)), h * 0.5f);
        var heartCol = venue.LikedByMe ? ThemeService.Current.SecondaryEnd : UiColors.Body;

        var scale = 1f;
        if (!AccessibilityService.ReduceMotion)
        {
            var p = (float)(ImGui.GetTime() - _likePoppedAt) / 0.35f;
            if (p is >= 0f and < 1f)
            {
                scale = 1f + 0.4f * MathF.Sin(p * MathF.PI) * (1f - p);
            }
        }
        var heartPx = ImGui.GetFontSize() * scale;
        var heartCenter = tl + new Vector2(padX + heartSz.X * 0.5f, h * 0.5f);
        ImGui.PushFont(UiHost.PluginInterface.UiBuilder.FontIcon);
        dl.AddText(ImGui.GetFont(), heartPx, heartCenter - heartSz * scale * 0.5f,
            ImGui.GetColorU32(heartCol), heart);
        ImGui.PopFont();
        dl.AddText(tl + new Vector2(padX + heartSz.X + Px(6f), (h - labelSz.Y) * 0.5f), 0xFFFFFFFFu, label);

        if (clicked && !_engagementBusy)
        {
            _likePoppedAt = ImGui.GetTime();
            ToggleLike(detail);
        }
    }

    private void ToggleLike(VenueDetailDto detail)
    {
        _engagementBusy = true;
        _browseStale = true;
        var venueId = detail.Summary.Id;
        var liked = !detail.Summary.LikedByMe;
        var ct = _cts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                var count = await _host.SetVenueLikeAsync(venueId, liked, ct).ConfigureAwait(false);
                if (_detail is { } current && current.Summary.Id == venueId)
                {
                    _detail = current with { Summary = current.Summary with { LikedByMe = liked, LikeCount = count } };
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, "[PlacesScreen] Like toggle failed.");
            }
            finally
            {
                _engagementBusy = false;
            }
        }, ct);
    }

    private void DrawScheduleRow(VenueDetailDto detail, VenueOccurrenceDto occ, float cardW)
    {
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var pad = Px(PadX);
        var rowH = Px(58f);
        var origin = ImGui.GetCursorScreenPos();
        var tl = new Vector2(origin.X + pad, origin.Y);
        var br = tl + new Vector2(cardW, rowH);
        var now = DateTimeOffset.UtcNow;
        var isLive = occ.StartUtc <= now && now < occ.EndUtc;
        var lineH = ImGui.GetTextLineHeight();

        dl.AddRectFilled(tl, br, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.045f)), Px(10f));
        if (isLive)
        {
            DrawPulsingGradientBorder(dl, tl, br, Px(10f));
        }

        var localStart = occ.StartUtc.ToLocalTime();
        var localEnd = occ.EndUtc.ToLocalTime();
        var dayLabel = isLive ? Loc.T("places.open_now") : DayHeader(DateOnly.FromDateTime(localStart.Date));
        var blockY = tl.Y + (rowH - (lineH * 2f + Px(4f))) * 0.5f;
        var labelX = tl.X + Px(14f);
        if (isLive)
        {
            var dotR = Px(4f);
            var pulse = AccessibilityService.ReduceMotion ? 1f : 0.5f + 0.5f * MathF.Sin((float)ImGui.GetTime() * 4f);
            dl.AddCircleFilled(new Vector2(labelX + dotR, blockY + lineH * 0.5f), dotR,
                ImGui.GetColorU32(new Vector4(1f, 0.28f, 0.30f, pulse)));
            labelX += dotR * 2f + Px(7f);
        }
        dl.AddText(new Vector2(labelX, blockY),
            ImGui.GetColorU32(isLive ? t.AccentLight : new Vector4(1f, 1f, 1f, 1f)), dayLabel);
        dl.AddText(new Vector2(tl.X + Px(14f), blockY + lineH + Px(4f)),
            ImGui.GetColorU32(UiColors.Subtle), $"{localStart:HH:mm} – {localEnd:HH:mm}");

        var rightEdge = br.X - Px(14f);
        if (!isLive)
        {
            var btnW = DrawRsvpButton(detail, occ, new Vector2(rightEdge, tl.Y + (rowH - Px(30f)) * 0.5f));
            rightEdge -= btnW + Px(12f);
        }
        var shareW = DrawOccurrenceShareButton(detail, occ, new Vector2(rightEdge, tl.Y + (rowH - Px(30f)) * 0.5f));
        rightEdge -= shareW + Px(12f);
        DrawRsvpClump(dl, occ, new Vector2(rightEdge, tl.Y + rowH * 0.5f + Px(10f)));

        ImGui.SetCursorScreenPos(new Vector2(origin.X, br.Y + Px(6f)));
    }

    private float DrawRsvpButton(VenueDetailDto detail, VenueOccurrenceDto occ, Vector2 topRight)
    {
        var t = ThemeService.Current;
        var label = occ.RsvpedByMe ? Loc.T("places.rsvp_going") : Loc.T("places.rsvp");
        var labelSz = ImGui.CalcTextSize(label);
        var w = labelSz.X + Px(18f);
        var h = Px(30f);
        var tl = new Vector2(topRight.X - w, topRight.Y);

        ImGui.SetCursorScreenPos(tl);
        if (occ.RsvpedByMe)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, UiColors.Success with { W = 0.28f });
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UiColors.Success with { W = 0.42f });
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, UiColors.Success with { W = 0.55f });
        }
        else
        {
            PushThemeButton(t);
        }
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, h * 0.5f);
        var clicked = SharedUiHelpers.Button($"{label}##rsvp{occ.StartUtc.UtcTicks}", new Vector2(w, h));
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);

        if (clicked && !_engagementBusy)
        {
            ToggleRsvp(detail, occ);
        }
        return w;
    }

    private void ToggleRsvp(VenueDetailDto detail, VenueOccurrenceDto occ)
    {
        _engagementBusy = true;
        _browseStale = true;
        var venueId = detail.Summary.Id;
        var going = !occ.RsvpedByMe;
        var start = occ.StartUtc;
        var ct = _cts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                var count = await _host.SetVenueRsvpAsync(venueId, start, going, ct).ConfigureAwait(false);
                if (_detail is { } current && current.Summary.Id == venueId)
                {
                    var updated = current.Occurrences
                        .Select(o => o.StartUtc.UtcTicks == start.UtcTicks
                            ? o with { RsvpedByMe = going, RsvpCount = count }
                            : o)
                        .ToArray();
                    _detail = current with { Occurrences = updated };
                }
                // Re-fetch so an un-RSVP drops the caller's own avatar from the clump.
                var fresh = await _host.GetVenueDetailAsync(venueId, ct).ConfigureAwait(false);
                if (!ct.IsCancellationRequested && venueId == _detailVenueId)
                {
                    CacheDetailTextures(fresh);
                    _detail = fresh;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, "[PlacesScreen] RSVP toggle failed.");
            }
            finally
            {
                _engagementBusy = false;
            }
        }, ct);
    }

    private void DrawReviewSummary(VenueSummaryDto venue, float pad)
    {
        var avg = venue.AverageRating;
        var count = venue.ReviewCount;
        ImGui.SetCursorPosX(pad);
        if (count == 0)
        {
            ImGui.TextColored(UiColors.Muted, Loc.T("places.no_rating_yet"));
            return;
        }
        var dl = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var rounded = (int)Math.Round(avg);
        var star = FontAwesomeIcon.Star.ToIconString();
        ImGui.PushFont(UiHost.PluginInterface.UiBuilder.FontIcon);
        var starSz = ImGui.CalcTextSize(star);
        for (var i = 0; i < 5; i++)
        {
            dl.AddText(pos + new Vector2(i * (starSz.X + Px(3f)), 0f),
                i < rounded ? VenueFields.GoldStar : VenueFields.DimStar, star);
        }
        ImGui.PopFont();
        dl.AddText(pos + new Vector2(5 * (starSz.X + Px(3f)) + Px(6f), 0f),
            ImGui.GetColorU32(UiColors.Body),
            Loc.T("places.rating_summary", avg.ToString("0.0"), count));
        ImGui.Dummy(new Vector2(1f, starSz.Y + Px(2f)));
    }

    private void DrawReviewCompose(VenueDetailDto detail, float cardW)
    {
        var t = ThemeService.Current;
        var pad = Px(PadX);

        ImGui.SetCursorPosX(pad);
        ImGui.TextColored(UiColors.Subtle,
            detail.MyReview is null ? Loc.T("places.write_review") : Loc.T("places.your_review"));
        ImGui.SameLine();
        {
            var iconH = ImGui.GetTextLineHeight();
            var grinTex = UiHost.EmojiService.GetEmoji("grinning")?.GetWrapOrDefault();
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Px(2f, 2f));
            var clicked = grinTex != null
                ? ImGui.ImageButton(grinTex.Handle, new Vector2(iconH - Px(4f)))
                : ImGui.SmallButton(":)##reviewEmoji");
            ImGui.PopStyleVar();
            _reviewEmojiPicker.Draw();
            if (clicked)
            {
                _reviewEmojiPicker.Open(InsertReviewEmoji);
            }
        }

        ImGui.SetCursorPosX(pad);
        VenueFields.DrawStarRow("compose", ref _composeRating, interactive: true, Px(20f));

        ImGui.SetCursorPosX(pad);
        var textBefore = _composeText;
        ImGui.SetNextItemWidth(cardW);
        InputTextMultilineWithPaste("##reviewText", ref _composeText, EmojiText.MaxBioRawLength,
            new Vector2(cardW, Px(56f)));
        if (EmojiText.EffectiveLength(_composeText) > PlacesLimits.ReviewMaxLength)
        {
            _composeText = textBefore;
        }

        if (detail.MyReview is { PendingModeration: true })
        {
            ImGui.SetCursorPosX(pad);
            ImGui.PushTextWrapPos(pad + cardW);
            ImGui.TextColored(UiColors.ReviewOrange, Loc.T("places.review_pending"));
            ImGui.PopTextWrapPos();
        }
        if (_reviewError is not null)
        {
            ImGui.SetCursorPosX(pad);
            ImGui.PushTextWrapPos(pad + cardW);
            ImGui.TextColored(UiColors.Danger, _reviewError);
            ImGui.PopTextWrapPos();
        }

        ImGui.SetCursorPosX(pad);
        var btnH = Px(30f);
        var canSubmit = _composeRating is >= 1 and <= 5 && !_reviewSubmitting;
        PushThemeButton(t);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
        if (!canSubmit)
        {
            ImGui.BeginDisabled();
        }
        var submitLabel = detail.MyReview is null ? Loc.T("places.review_submit") : Loc.T("places.review_update");
        if (SharedUiHelpers.Button($"{submitLabel}##reviewSubmit", new Vector2(Px(150f), btnH)))
        {
            SubmitReview(detail);
        }
        if (!canSubmit)
        {
            ImGui.EndDisabled();
        }
        ImGui.PopStyleVar();
        PopThemeButton();

        if (detail.MyReview is not null)
        {
            ImGui.SameLine(0f, Px(8f));
            PushDangerButton();
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
            if (SharedUiHelpers.Button($"{Loc.T("places.review_delete")}##reviewDelete", new Vector2(Px(100f), btnH)))
            {
                _confirmDeleteReview = true;
            }
            ImGui.PopStyleVar();
            ImGui.PopStyleColor(3);
        }
        ImGui.Spacing();
        ImGui.Spacing();
    }

    private void InsertReviewEmoji(string name)
    {
        var add = $":{name}: ";
        if (EmojiText.EffectiveLength(_composeText + add) <= PlacesLimits.ReviewMaxLength)
        {
            _composeText += add;
        }
    }

    private void SubmitReview(VenueDetailDto detail)
    {
        _reviewSubmitting = true;
        _reviewError = null;
        _browseStale = true;
        var venueId = detail.Summary.Id;
        var rating = (short)_composeRating;
        var text = _composeText;
        var ct = _cts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await _host.SubmitVenueReviewAsync(venueId, rating, text, ct).ConfigureAwait(false);
                if (venueId == _detailVenueId)
                {
                    StartDetailFetch();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, "[PlacesScreen] Review submit failed.");
                _reviewError = HubErrorText.Localize(ex);
            }
            finally
            {
                _reviewSubmitting = false;
            }
        }, ct);
    }

    private void DrawReviewCard(VenueReviewDto review, float cardW) =>
        VenueFields.DrawReviewCard(review,
            _reviewAvatarTex.TryGetValue(review.Id, out var tex) ? tex : null, cardW, PadX);

    private void DrawLoadMoreReviews(VenueDetailDto detail, float cardW)
    {
        var loaded = detail.Reviews.Length + _extraReviews.Count;
        if (_reviewsExhausted || detail.Reviews.Length < 20 || detail.Summary.ReviewCount <= loaded)
        {
            return;
        }
        ImGui.SetCursorPosX(Px(PadX));
        PushThemeButton(ThemeService.Current);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
        if (SharedUiHelpers.Button(_moreReviewsLoading ? "…" : Loc.T("places.reviews_more"), new Vector2(cardW, Px(30f)))
            && !_moreReviewsLoading)
        {
            LoadMoreReviews(detail, loaded);
        }
        ImGui.PopStyleVar();
        PopThemeButton();
    }

    private void LoadMoreReviews(VenueDetailDto detail, int skip)
    {
        _moreReviewsLoading = true;
        var venueId = detail.Summary.Id;
        var ct = _cts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                var page = await _host.GetVenueReviewsAsync(venueId, skip, ct).ConfigureAwait(false);
                if (venueId != _detailVenueId)
                {
                    return;
                }
                CacheReviewAvatars(page);
                if (page.Length == 0)
                {
                    _reviewsExhausted = true;
                }
                else
                {
                    var known = detail.Reviews.Concat(_extraReviews).Select(r => r.Id).ToHashSet();
                    _extraReviews.AddRange(page.Where(r => !known.Contains(r.Id)));
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, "[PlacesScreen] Review page fetch failed.");
            }
            finally
            {
                _moreReviewsLoading = false;
            }
        }, ct);
    }

    private void DrawDeleteReviewConfirm()
    {
        if (!_confirmDeleteReview)
        {
            return;
        }
        var windowPos = ImGui.GetWindowPos();
        var windowSize = ImGui.GetWindowSize();
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(windowPos, windowPos + windowSize,
            ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.55f)));

        ImGui.SetCursorScreenPos(windowPos);
        if (ImGui.InvisibleButton("##delReviewScrim", windowSize))
        {
            _confirmDeleteReview = false;
        }

        var w = Px(272f);
        var pad = Px(16f, 16f);
        var h = _confirmPanelHeight > 0f ? _confirmPanelHeight : Px(170f);
        var panelPos = windowPos + (windowSize - new Vector2(w, h)) * 0.5f;

        ImGui.SetCursorScreenPos(panelPos);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.11f, 0.10f, 0.13f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.32f, 0.30f, 0.38f, 0.65f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, Px(12f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, pad);
        using (var child = ImRaii.Child("##delReviewPanel", new Vector2(w, h), true,
                   ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.AlwaysUseWindowPadding))
        {
            if (child.Success)
            {
                var innerW = ImGui.GetContentRegionAvail().X;
                ImGui.PushTextWrapPos(innerW);
                Widgets.ModalUi.Header(innerW, FontAwesomeIcon.Trash,
                    Loc.T("places.review_delete_title"), UiColors.Danger);
                ImGui.TextColored(UiColors.Body, Loc.T("places.review_delete_body"));
                ImGui.Spacing();
                ImGui.Spacing();
                var btnW = (innerW - Px(8f)) * 0.5f;
                PushThemeButton(ThemeService.Current);
                ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
                if (SharedUiHelpers.Button(Loc.T("common.cancel"), new Vector2(btnW, Px(30f))))
                {
                    _confirmDeleteReview = false;
                }
                ImGui.PopStyleVar();
                PopThemeButton();
                ImGui.SameLine(0f, Px(8f));
                PushDangerButton();
                ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
                if (SharedUiHelpers.Button(Loc.T("places.review_delete"), new Vector2(btnW, Px(30f))))
                {
                    _confirmDeleteReview = false;
                    DeleteMyReview();
                }
                ImGui.PopStyleVar();
                ImGui.PopStyleColor(3);
                ImGui.PopTextWrapPos();
                _confirmPanelHeight = ImGui.GetCursorPosY() + pad.Y;
            }
        }
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(2);
    }

    private void DeleteMyReview()
    {
        _browseStale = true;
        var venueId = _detailVenueId;
        var ct = _cts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await _host.DeleteMyVenueReviewAsync(venueId, ct).ConfigureAwait(false);
                _composeRating = 0;
                _composeText = "";
                if (venueId == _detailVenueId)
                {
                    StartDetailFetch();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, "[PlacesScreen] Review delete failed.");
            }
        }, ct);
    }
}
