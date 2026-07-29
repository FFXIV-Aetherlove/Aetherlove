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
using AetherLove.Shared.Levemetes;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;
using Widgets = AetherLove.Widgets;

namespace AetherOS.Apps.Levemetes;

public partial class LevemetesScreen
{
    private Guid _detailAdId;
    private volatile LevemeteDetailDto? _detail;
    private volatile bool _detailLoading;
    private volatile string? _detailError;
    private bool _detailFromChat;
    private string? _detailReturnApp;
    private int _photoIndex;
    private readonly HashSet<(Guid AdId, short Order)> _revealedNsfw = new();
    private readonly List<ISharedImmediateTexture?> _photoTexes = new();
    private readonly Dictionary<Guid, ISharedImmediateTexture?> _reviewAvatarTex = new();
    private ISharedImmediateTexture? _posterAvatarTex;
    private List<LevemeteReviewDto> _extraReviews = [];
    private volatile bool _moreReviewsLoading;
    private bool _reviewsExhausted;
    private readonly EntranceAnimation _detailEntrance = new();

    private readonly EmojiPickerPopup _reviewEmojiPicker = new();
    private int _composeRating;
    private string _composeText = "";
    private volatile bool _reviewSubmitting;
    private volatile string? _reviewError;
    private bool _confirmDeleteReview;
    private float _confirmPanelHeight;

    private volatile bool _contactBusy;
    private volatile bool _contactSent;
    private volatile string? _contactError;

    private bool _reportOpen;
    private float _reportPanelH;
    private string _reportText = "";
    private volatile bool _reportBusy;
    private float _reportThanksTimer;

    private void OpenDetail(LevemeteSummaryDto ad)
    {
        _detailFromChat = false;
        _detailReturnApp = null;
        ResetDetailState(ad.Id);
        StartDetailFetch();
    }

    public void OpenDetailFromChat(Guid adId, string? returnApp = null)
    {
        _detailFromChat = true;
        _detailReturnApp = returnApp;
        ResetDetailState(adId);
        StartDetailFetch();
    }

    private void ResetDetailState(Guid adId)
    {
        _detailAdId = adId;
        _detail = null;
        _detailError = null;
        _photoIndex = 0;
        _extraReviews = [];
        _reviewsExhausted = false;
        _confirmDeleteReview = false;
        _reviewError = null;
        _contactSent = false;
        _contactError = null;
        _reportOpen = false;
        _reportText = "";
        _reportThanksTimer = 0f;
        _section = Section.Detail;
    }

    private void StartDetailFetch()
    {
        if (_detailLoading)
        {
            return;
        }
        _detailLoading = true;
        var adId = _detailAdId;
        var ct = _cts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                var dto = await _host.GetDetailAsync(adId, ct).ConfigureAwait(false);
                if (ct.IsCancellationRequested || adId != _detailAdId)
                {
                    return;
                }
                CacheDetailTextures(adId, dto);
                _composeRating = dto.MyReview?.Rating ?? 0;
                _composeText = dto.MyReview?.Text ?? "";
                _detail = dto;
                _detailEntrance.Arm();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, "[LevemetesScreen] Detail fetch failed.");
                _detailError = HubErrorText.Localize(ex);
            }
            finally
            {
                _detailLoading = false;
            }
        }, ct);
    }

    private void CacheDetailTextures(Guid adId, LevemeteDetailDto dto)
    {
        _photoTexes.Clear();
        foreach (var photo in dto.Photos.OrderBy(p => p.Order))
        {
            _photoTexes.Add(photo.WebpBytes is { Length: > 0 }
                ? AvatarDiskCache.Store(LevemetesCacheDir, $"ad_{adId:N}_{photo.Order}", photo.WebpBytes)
                : null);
        }
        _posterAvatarTex = dto.PosterAvatarWebp is { Length: > 0 }
            ? AvatarDiskCache.Store(LevemetesCacheDir, $"poster_{adId:N}", dto.PosterAvatarWebp)
            : null;
        CacheReviewAvatars(dto.Reviews);
        if (dto.MyReview is not null)
        {
            CacheReviewAvatars([dto.MyReview]);
        }
    }

    private void CacheReviewAvatars(LevemeteReviewDto[] reviews)
    {
        foreach (var review in reviews)
        {
            if (!_reviewAvatarTex.ContainsKey(review.Id) && review.AuthorAvatarWebp is { Length: > 0 })
            {
                _reviewAvatarTex[review.Id] = AvatarDiskCache.Store(
                    LevemetesCacheDir, $"rev_{review.Id:N}", review.AuthorAvatarWebp);
            }
        }
    }

    private void DrawDetail()
    {
        var detail = _detail;
        PushScrollbarStyle();
        var scrollViewportTL = ImGui.GetCursorScreenPos();
        using (var scroll = ImRaii.Child("##leveDetailScroll", ImGui.GetContentRegionAvail(), false))
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
                    DrawCenteredMuted(Loc.T("os.leve_load_failed", _detailError));
                }
                else if (detail is not null)
                {
                    DrawDetailContent(detail);
                }
            }
        }
        PopScrollbarStyle();

        if (DrawFloatingBackPill(scrollViewportTL + Px(10f, 10f),
                Loc.T(_detailFromChat ? "os.leve_back_to_chat" : "os.leve_back"),
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
                    _host.OpenLoveChat();
                }
            }
            else
            {
                _section = Section.Browse;
                _entrance.Arm();
            }
        }
        DrawDeleteReviewConfirm();
        DrawReportOverlay();
    }

    private void DrawDetailContent(LevemeteDetailDto detail)
    {
        var t = ThemeService.Current;
        var winW = ImGui.GetContentRegionAvail().X;
        var pad = Px(PadX);
        var cardW = winW - pad * 2f;
        _detailEntrance.BeginFrame();

        DrawPhotoCarousel(detail, cardW);
        ImGui.Dummy(new Vector2(1f, Px(8f)));

        ImGui.SetCursorPosX(pad);
        var kindLabel = KindLabel(detail.Kind);
        var kindCol = detail.Kind == (short)LevemeteKind.Offering ? UiColors.Success : t.AccentLight;
        ImGui.TextColored(kindCol, kindLabel);
        ImGui.SameLine(0f, Px(8f));
        ImGui.TextColored(UiColors.Muted, CategoryLabel(detail.Category));

        var titleRowTop = ImGui.GetCursorScreenPos();
        ImGui.SetCursorPosX(pad);
        using (UiFonts.H3?.Push())
        {
            ImGui.PushTextWrapPos(pad + cardW - Px(76f));
            ImGui.TextUnformatted(detail.Title);
            ImGui.PopTextWrapPos();
        }
        DrawSharePill(winW, titleRowTop, detail);

        var regions = RegionShortList(detail.RegionMask);
        if (regions.Length > 0)
        {
            ImGui.SetCursorPosX(pad);
            ImGui.TextColored(UiColors.Body, regions);
        }

        if (detail.PosterName is { Length: > 0 } posterName)
        {
            ImGui.Dummy(new Vector2(1f, Px(4f)));
            ImGui.SetCursorPosX(pad);
            var posterDl = ImGui.GetWindowDrawList();
            var avatarR = Px(11f);
            var rowTL = ImGui.GetCursorScreenPos();
            var avatarC = rowTL + new Vector2(avatarR, avatarR);
            if (_posterAvatarTex?.GetWrapOrDefault() is { } posterWrap)
            {
                posterDl.AddImageRounded(posterWrap.Handle, rowTL, rowTL + new Vector2(avatarR * 2f),
                    Vector2.Zero, Vector2.One, 0xFFFFFFFFu, avatarR);
            }
            else
            {
                posterDl.AddCircleFilled(avatarC, avatarR, ImGui.GetColorU32(UiColors.AvatarFallback));
                IconDraw.AddCentered(posterDl, FontAwesomeIcon.User, avatarR, avatarC,
                    ImGui.GetColorU32(UiColors.Muted));
            }
            posterDl.AddText(new Vector2(rowTL.X + avatarR * 2f + Px(6f), avatarC.Y - ImGui.GetTextLineHeight() * 0.5f),
                ImGui.GetColorU32(UiColors.Body), posterName);
            ImGui.Dummy(new Vector2(1f, avatarR * 2f));
        }

        if (detail.ReviewsEnabled && detail.ReviewCount > 0)
        {
            ImGui.SetCursorPosX(pad);
            var dl = ImGui.GetWindowDrawList();
            var starEnd = VenueFields.DrawStarSummary(dl, ImGui.GetCursorScreenPos(),
                detail.AverageRating, detail.ReviewCount, Px(11f));
            ImGui.Dummy(new Vector2(starEnd, ImGui.GetTextLineHeight() + Px(2f)));
        }

        if (detail.Price is { Length: > 0 } price)
        {
            ImGui.Dummy(new Vector2(1f, Px(6f)));
            DrawPriceCard(price, cardW);
        }

        if (detail.Description.Length > 0)
        {
            ImGui.Dummy(new Vector2(1f, Px(10f)));
            DrawH2Header(Loc.T("os.leve_description"));
            ImGui.SetCursorPosX(pad);
            var parsed = ParsedMessage.Parse(detail.Description);
            parsed.DrawWrapped("##leveDesc", cardW);
        }

        if (detail.Discord is { Length: > 0 } discord)
        {
            ImGui.Dummy(new Vector2(1f, Px(10f)));
            ImGui.SetCursorPosX(pad);
            DrawDiscordButton($"{Loc.T("os.leve_discord_button")}##leveDiscord",
                new Vector2(cardW, Px(30f)), discord);
        }

        DrawAvailability(detail, cardW);

        ImGui.Dummy(new Vector2(1f, Px(12f)));
        DrawContactButton(detail, cardW);

        if (detail.ReviewsEnabled)
        {
            ImGui.Dummy(new Vector2(1f, Px(12f)));
            DrawH2Header(Loc.T("os.leve_reviews"));
            if (!detail.IsMine)
            {
                DrawReviewCompose(detail, cardW);
            }

            foreach (var review in detail.Reviews.Concat(_extraReviews))
            {
                DrawReviewCard(review, cardW);
                ImGui.Spacing();
            }
            if (detail.Reviews.Length + _extraReviews.Count == 0 && detail.MyReview is null)
            {
                DrawCenteredMuted(Loc.T("os.leve_no_reviews"));
            }
            DrawLoadMoreReviews(detail, cardW);
        }

        ImGui.Dummy(new Vector2(1f, Px(10f)));
        DrawReportLink(detail);
        ImGui.Dummy(new Vector2(1f, Px(12f)));
        _detailEntrance.EndFrame();
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

    private void DrawPhotoCarousel(LevemeteDetailDto detail, float cardW)
    {
        var dl = ImGui.GetWindowDrawList();
        var pad = Px(PadX);
        var photos = detail.Photos;
        var photoH = cardW * (PhotoSpec.LevemeteHeight / (float)PhotoSpec.LevemeteWidth);
        var origin = ImGui.GetCursorScreenPos();
        var tl = new Vector2(origin.X + pad, origin.Y);
        var br = tl + new Vector2(cardW, photoH);

        if (photos.Length == 0)
        {
            DrawCategoryTile(dl, tl, new Vector2(cardW, photoH), detail.Category, Px(12f));
            ImGui.SetCursorScreenPos(new Vector2(origin.X, br.Y + Px(2f)));
            return;
        }

        if (_photoIndex >= photos.Length)
        {
            _photoIndex = 0;
        }
        var photo = photos[_photoIndex];
        var tex = _photoIndex < _photoTexes.Count ? _photoTexes[_photoIndex] : null;
        var blurred = photo.IsNsfw && !_revealedNsfw.Contains((detail.Id, photo.Order));

        var wrap = tex?.GetWrapOrDefault();
        if (wrap != null)
        {
            if (blurred)
            {
                DrawBlurredCover(dl, wrap, tl, new Vector2(cardW, photoH));
            }
            else
            {
                var (uv0, uv1) = SharedUiHelpers.CoverFitUvs(wrap.Width, wrap.Height, cardW, photoH);
                dl.AddImageRounded(wrap.Handle, tl, br, uv0, uv1, 0xFFFFFFFFu, Px(12f), ImDrawFlags.RoundCornersAll);
            }
        }
        else
        {
            dl.AddRectFilled(tl, br, ImGui.GetColorU32(ThemeService.Current.Accent with { W = 0.12f }), Px(12f));
        }
        dl.AddRect(tl, br, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.10f)), Px(12f), ImDrawFlags.None, Px(1f));

        // Arrows are submitted first so their hover wins over the full-photo reveal button below.
        if (photos.Length > 1)
        {
            DrawCarouselArrow(tl, photoH, left: true, photos.Length);
            DrawCarouselArrow(new Vector2(br.X - Px(34f), tl.Y), photoH, left: false, photos.Length);

            var dotGap = Px(10f);
            var dotsW = (photos.Length - 1) * dotGap;
            var dotY = br.Y - Px(12f);
            for (var i = 0; i < photos.Length; i++)
            {
                var center = new Vector2(tl.X + cardW * 0.5f - dotsW * 0.5f + i * dotGap, dotY);
                dl.AddCircleFilled(center, Px(i == _photoIndex ? 3.5f : 2.5f),
                    ImGui.GetColorU32(new Vector4(1f, 1f, 1f, i == _photoIndex ? 0.95f : 0.45f)));
            }
        }

        if (blurred)
        {
            ImGui.SetCursorScreenPos(tl);
            if (ImGui.InvisibleButton("##leveNsfwReveal", new Vector2(cardW, photoH)))
            {
                _revealedNsfw.Add((detail.Id, photo.Order));
            }
            var hovered = ImGui.IsItemHovered();
            if (hovered)
            {
                SharedUiHelpers.HandOnHover();
            }
            DrawNsfwRevealPill(dl, tl, new Vector2(cardW, photoH), hovered);
        }

        ImGui.SetCursorScreenPos(new Vector2(origin.X, br.Y + Px(2f)));
    }

    private void DrawCarouselArrow(Vector2 tl, float photoH, bool left, int count)
    {
        var dl = ImGui.GetWindowDrawList();
        var size = new Vector2(Px(34f), photoH);
        ImGui.SetCursorScreenPos(tl);
        if (ImGui.InvisibleButton(left ? "##levePrev" : "##leveNext", size))
        {
            _photoIndex = ((_photoIndex + (left ? -1 : 1)) % count + count) % count;
        }
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            SharedUiHelpers.HandOnHover();
        }
        var icon = left ? FontAwesomeIcon.ChevronLeft : FontAwesomeIcon.ChevronRight;
        IconDraw.AddCentered(dl, icon, Px(16f), tl + size * 0.5f,
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, hovered ? 0.95f : 0.55f)));
    }

    /// <summary>Pseudo-blur via 13 offset cover-fit draws plus a frosted tint.</summary>
    private static void DrawBlurredCover(ImDrawListPtr dl, Dalamud.Interface.Textures.TextureWraps.IDalamudTextureWrap wrap,
        Vector2 tl, Vector2 sz)
    {
        var (uv0, uv1) = SharedUiHelpers.CoverFitUvs(wrap.Width, wrap.Height, sz.X, sz.Y);
        Span<Vector2> offsets =
        [
            Px(0f, 0f),
            Px(8f, 0f), Px(-8f, 0f),
            Px(0f, 8f), Px(0f, -8f),
            Px(6f, 6f), Px(-6f, 6f),
            Px(6f, -6f), Px(-6f, -6f),
            Px(16f, 0f), Px(-16f, 0f),
            Px(0f, 16f), Px(0f, -16f),
        ];
        var sampleA = (uint)Math.Clamp((int)(255f / offsets.Length), 0, 255);
        var sampleCol = (sampleA << 24) | 0x00FFFFFFu;
        dl.PushClipRect(tl, tl + sz, true);
        foreach (var off in offsets)
        {
            dl.AddImage(wrap.Handle, tl + off, tl + sz + off, uv0, uv1, sampleCol);
        }
        dl.PopClipRect();
        dl.AddRectFilled(tl, tl + sz, 0x8C101010u, Px(12f));
    }

    private static void DrawNsfwRevealPill(ImDrawListPtr dl, Vector2 photoTL, Vector2 photoSz, bool hovered)
    {
        var t = ThemeService.Current;
        var center = photoTL + photoSz * 0.5f;

        ImGui.PushFont(UiHost.PluginInterface.UiBuilder.FontIcon);
        var iconStr = FontAwesomeIcon.EyeSlash.ToIconString();
        var iconSz = ImGui.CalcTextSize(iconStr);
        ImGui.PopFont();

        var label = Loc.T("os.leve_nsfw_reveal");
        var labelSz = ImGui.CalcTextSize(label);
        var padX = Px(18f);
        var padY = Px(12f);
        var iconGap = Px(10f);

        var pillW = padX + iconSz.X + iconGap + labelSz.X + padX;
        var pillH = MathF.Max(iconSz.Y, labelSz.Y) + padY * 2f;
        var pillTL = new Vector2(center.X - pillW * 0.5f, center.Y - pillH * 0.5f);
        var pillBR = pillTL + new Vector2(pillW, pillH);

        var bg = hovered ? ImGui.GetColorU32(t.Accent with { W = 0.92f }) : 0xE0202020u;
        dl.AddRectFilled(pillTL, pillBR, bg, pillH * 0.5f);
        dl.AddRect(pillTL, pillBR, 0x88FFFFFFu, pillH * 0.5f, ImDrawFlags.None, 1.5f);

        ImGui.PushFont(UiHost.PluginInterface.UiBuilder.FontIcon);
        dl.AddText(new Vector2(pillTL.X + padX, center.Y - iconSz.Y * 0.5f), 0xFFFFFFFFu, iconStr);
        ImGui.PopFont();
        dl.AddText(new Vector2(pillTL.X + padX + iconSz.X + iconGap, center.Y - labelSz.Y * 0.5f),
            0xFFFFFFFFu, label);
    }

    private void DrawSharePill(float winW, Vector2 rowTop, LevemeteDetailDto detail)
    {
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var label = Loc.T("os.leve_share");
        var labelSz = ImGui.CalcTextSize(label);
        var iconPx = ImGui.GetFontSize() * 0.85f;
        var iconSz = IconDraw.Measure(FontAwesomeIcon.Share, iconPx);
        var padX = Px(11f);
        var gap = Px(6f);
        var pillH = labelSz.Y + Px(9f);
        var pillW = padX * 2f + iconSz.X + gap + labelSz.X;
        var tl = new Vector2(rowTop.X + winW - Px(PadX) - pillW, rowTop.Y);

        ImGui.SetCursorScreenPos(tl);
        var clicked = ImGui.InvisibleButton("##leveShareBtn", new Vector2(pillW, pillH));
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            SharedUiHelpers.HandOnHover();
        }

        dl.AddRectFilled(tl, tl + new Vector2(pillW, pillH),
            ImGui.GetColorU32(t.Accent with { W = hovered ? 0.45f : 0.22f }), pillH * 0.5f);
        dl.AddRect(tl, tl + new Vector2(pillW, pillH),
            ImGui.GetColorU32(t.Accent with { W = hovered ? 0.95f : 0.60f }), pillH * 0.5f, ImDrawFlags.None, Px(1f));
        IconDraw.Add(dl, FontAwesomeIcon.Share, iconPx,
            new Vector2(tl.X + padX, tl.Y + (pillH - iconSz.Y) * 0.5f), ImGui.GetColorU32(t.AccentLight));
        dl.AddText(new Vector2(tl.X + padX + iconSz.X + gap, tl.Y + (pillH - labelSz.Y) * 0.5f),
            0xFFFFFFFFu, label);

        if (clicked)
        {
            _share?.Offer(new ShareItem
            {
                Type = ShareTypes.Levemete,
                RefId = detail.Id.ToString("D"),
                Title = detail.Title,
                Subtitle = $"{KindLabel(detail.Kind)} · {CategoryLabel(detail.Category)}",
                SourceAppId = "levemetes",
            }, title: detail.Title);
        }
    }

    private void DrawPriceCard(string price, float cardW)
    {
        var dl = ImGui.GetWindowDrawList();
        var pad = Px(PadX);
        var lineH = ImGui.GetTextLineHeight();
        var cardH = Px(12f) + lineH * 2f + Px(3f) + Px(12f);
        var origin = ImGui.GetCursorScreenPos();
        var tl = new Vector2(origin.X + pad, origin.Y);
        var br = tl + new Vector2(cardW, cardH);
        dl.AddRectFilled(tl, br, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.045f)), Px(10f));

        var iconPx = Px(20f);
        var iconSz = IconDraw.Measure(FontAwesomeIcon.Coins, iconPx);
        IconDraw.Add(dl, FontAwesomeIcon.Coins, iconPx,
            new Vector2(tl.X + Px(12f), tl.Y + (cardH - iconSz.Y) * 0.5f),
            ImGui.GetColorU32(ThemeService.Current.AccentLight));

        var textX = tl.X + Px(12f) + iconSz.X + Px(12f);
        var textMaxW = br.X - textX - Px(12f);
        dl.AddText(new Vector2(textX, tl.Y + Px(12f)), ImGui.GetColorU32(UiColors.Muted),
            Loc.T("os.leve_price"));
        dl.AddText(new Vector2(textX, tl.Y + Px(12f) + lineH + Px(3f)), ImGui.GetColorU32(UiColors.Body),
            TruncateToWidth(price, textMaxW));

        ImGui.SetCursorScreenPos(new Vector2(origin.X, br.Y));
    }

    private void DrawAvailability(LevemeteDetailDto detail, float cardW)
    {
        if (detail.WeekdayHoursMask == 0 && detail.WeekendHoursMask == 0)
        {
            return;
        }
        var weekday = detail.WeekdayHoursMask;
        var weekend = detail.WeekendHoursMask;
        if (!detail.IsMine)
        {
            var viewerOffset = (int)TimeZoneInfo.Local.GetUtcOffset(DateTimeOffset.UtcNow).TotalMinutes;
            weekday = TimeZoneShift.ShiftHoursMask(weekday, detail.TimezoneOffsetMinutes, viewerOffset);
            weekend = TimeZoneShift.ShiftHoursMask(weekend, detail.TimezoneOffsetMinutes, viewerOffset);
        }
        ImGui.Dummy(new Vector2(1f, Px(10f)));
        DrawAvailabilityGraph(Loc.T("os.leve_avail_weekday"), weekday, cardW);
        DrawAvailabilityGraph(Loc.T("os.leve_avail_weekend"), weekend, cardW);
    }

    private static void DrawAvailabilityGraph(string label, int hoursMask, float cardW)
    {
        if (hoursMask == 0)
        {
            return;
        }
        var dl = ImGui.GetWindowDrawList();
        var pad = Px(PadX);
        ImGui.SetCursorPosX(pad);
        ImGui.TextColored(UiColors.Subtle, label);
        ImGui.Spacing();

        var graphH = Px(34f);
        var labelH = Px(16f);
        var barW = cardW / 24f;
        ImGui.SetCursorPosX(pad);
        var graphTL = ImGui.GetCursorScreenPos();

        for (var h = 0; h < 24; h++)
        {
            var active = (hoursMask & (1 << h)) != 0;
            var x0 = graphTL.X + h * barW + Px(1.5f);
            var x1 = graphTL.X + (h + 1) * barW - Px(1.5f);
            dl.AddRectFilled(new Vector2(x0, graphTL.Y), new Vector2(x1, graphTL.Y + graphH),
                active ? ThemeService.Current.AccentU32 : ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.07f)), Px(3f));
        }

        var labelFontSz = ImGui.GetFontSize() * 0.85f;
        foreach (var h in new[] { 0, 6, 12, 18 })
        {
            var lx = graphTL.X + h * barW;
            dl.AddText(ImGui.GetFont(), labelFontSz, new Vector2(lx + Px(1f), graphTL.Y + graphH + Px(4f)),
                ImGui.GetColorU32(UiColors.Muted), $"{h:D2}:00");
        }

        ImGui.Dummy(new Vector2(cardW, graphH + labelH + Px(6f)));
    }

    private void DrawContactButton(LevemeteDetailDto detail, float cardW)
    {
        if (detail.IsMine)
        {
            DrawCenteredMuted(Loc.T("os.leve_your_ad"));
            return;
        }
        var pad = Px(PadX);
        if (_contactSent)
        {
            ImGui.SetCursorPosX(pad);
            ImGui.PushTextWrapPos(pad + cardW);
            ImGui.TextColored(UiColors.Success, Loc.T("os.leve_contact_sent"));
            ImGui.PopTextWrapPos();
            return;
        }
        if (!detail.PosterAcceptsContact)
        {
            ImGui.SetCursorPosX(pad);
            ImGui.PushTextWrapPos(pad + cardW);
            ImGui.TextColored(UiColors.Muted, Loc.T("os.leve_contact_disabled"));
            ImGui.PopTextWrapPos();
            return;
        }

        ImGui.SetCursorPosX(pad);
        PushThemeButton(ThemeService.Current);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(10f));
        if (_contactBusy)
        {
            ImGui.BeginDisabled();
        }
        if (SharedUiHelpers.Button($"{Loc.T("os.leve_contact")}##leveContact", new Vector2(cardW, Px(36f))))
        {
            StartContact(detail);
        }
        if (_contactBusy)
        {
            ImGui.EndDisabled();
        }
        ImGui.PopStyleVar();
        PopThemeButton();

        if (_contactError is not null)
        {
            ImGui.SetCursorPosX(pad);
            ImGui.PushTextWrapPos(pad + cardW);
            ImGui.TextColored(UiColors.Danger, _contactError);
            ImGui.PopTextWrapPos();
        }
    }

    private void StartContact(LevemeteDetailDto detail)
    {
        _contactBusy = true;
        _contactError = null;
        var adId = detail.Id;
        var ct = _cts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await _host.AddContactAsync(adId, ct).ConfigureAwait(false);
                if (adId == _detailAdId)
                {
                    _contactSent = true;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, "[LevemetesScreen] Contact add failed.");
                _contactError = HubErrorText.Localize(ex);
            }
            finally
            {
                _contactBusy = false;
            }
        }, ct);
    }

    private void DrawReviewCompose(LevemeteDetailDto detail, float cardW)
    {
        var t = ThemeService.Current;
        var pad = Px(PadX);

        ImGui.SetCursorPosX(pad);
        var reviewHeading = detail.MyReview is not null
            ? Loc.T("os.leve_review_yours")
            : detail.PosterName is { Length: > 0 } reviewedName
                ? Loc.T(detail.Kind == (short)LevemeteKind.Offering
                    ? "os.leve_review_for_provider"
                    : "os.leve_review_for_buyer", reviewedName)
                : Loc.T("os.leve_review_write");
        ImGui.TextColored(UiColors.Subtle, reviewHeading);
        ImGui.SameLine();
        {
            var iconH = ImGui.GetTextLineHeight();
            var grinTex = UiHost.EmojiService.GetEmoji("grinning")?.GetWrapOrDefault();
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Px(2f, 2f));
            var clicked = grinTex != null
                ? ImGui.ImageButton(grinTex.Handle, new Vector2(iconH - Px(4f)))
                : ImGui.SmallButton(":)##leveReviewEmoji");
            ImGui.PopStyleVar();
            _reviewEmojiPicker.Draw();
            if (clicked)
            {
                _reviewEmojiPicker.Open(InsertReviewEmoji);
            }
        }

        if (detail.MyReview is null)
        {
            ImGui.SetCursorPosX(pad);
            ImGui.PushTextWrapPos(pad + cardW);
            ImGui.TextColored(UiColors.Hint, Loc.T("os.leve_review_warning"));
            ImGui.PopTextWrapPos();
        }

        ImGui.SetCursorPosX(pad);
        VenueFields.DrawStarRow("leveCompose", ref _composeRating, interactive: true, Px(20f));

        ImGui.SetCursorPosX(pad);
        var textBefore = _composeText;
        ImGui.SetNextItemWidth(cardW);
        InputTextMultilineWithPaste("##leveReviewText", ref _composeText, EmojiText.MaxBioRawLength,
            new Vector2(cardW, Px(56f)));
        if (EmojiText.EffectiveLength(_composeText) > LevemetesLimits.ReviewMaxLength)
        {
            _composeText = textBefore;
        }

        if (detail.MyReview is { PendingModeration: true })
        {
            ImGui.SetCursorPosX(pad);
            ImGui.PushTextWrapPos(pad + cardW);
            ImGui.TextColored(UiColors.ReviewOrange, Loc.T("os.leve_review_pending"));
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
        var canSubmit = _composeRating is >= LevemetesLimits.MinRating and <= LevemetesLimits.MaxRating
            && !_reviewSubmitting;
        PushThemeButton(t);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
        if (!canSubmit)
        {
            ImGui.BeginDisabled();
        }
        var submitLabel = detail.MyReview is null ? Loc.T("os.leve_review_send") : Loc.T("os.leve_review_update");
        if (SharedUiHelpers.Button($"{submitLabel}##leveReviewSubmit", new Vector2(Px(150f), btnH)))
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
            if (SharedUiHelpers.Button($"{Loc.T("os.leve_review_delete")}##leveReviewDelete", new Vector2(Px(100f), btnH)))
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
        if (EmojiText.EffectiveLength(_composeText + add) <= LevemetesLimits.ReviewMaxLength)
        {
            _composeText += add;
        }
    }

    private void SubmitReview(LevemeteDetailDto detail)
    {
        _reviewSubmitting = true;
        _reviewError = null;
        var adId = detail.Id;
        var rating = (short)_composeRating;
        var text = _composeText;
        var ct = _cts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await _host.SubmitReviewAsync(adId, rating, text, ct).ConfigureAwait(false);
                if (adId == _detailAdId)
                {
                    StartDetailFetch();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, "[LevemetesScreen] Review submit failed.");
                _reviewError = HubErrorText.Localize(ex);
            }
            finally
            {
                _reviewSubmitting = false;
            }
        }, ct);
    }

    private void DrawReviewCard(LevemeteReviewDto review, float cardW) =>
        VenueFields.DrawReviewCard(review.Id, review.Rating, review.Text, review.CreatedAtUtc, review.Mine,
            _reviewAvatarTex.TryGetValue(review.Id, out var tex) ? tex : null, cardW, PadX);

    private void DrawLoadMoreReviews(LevemeteDetailDto detail, float cardW)
    {
        var loaded = detail.Reviews.Length + _extraReviews.Count;
        if (_reviewsExhausted
            || detail.Reviews.Length < LevemetesLimits.ReviewsPageSize
            || detail.ReviewCount <= loaded)
        {
            return;
        }
        ImGui.SetCursorPosX(Px(PadX));
        PushThemeButton(ThemeService.Current);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
        if (SharedUiHelpers.Button(_moreReviewsLoading ? "…" : Loc.T("os.leve_reviews_more"), new Vector2(cardW, Px(30f)))
            && !_moreReviewsLoading)
        {
            LoadMoreReviews(detail, loaded);
        }
        ImGui.PopStyleVar();
        PopThemeButton();
    }

    private void LoadMoreReviews(LevemeteDetailDto detail, int skip)
    {
        _moreReviewsLoading = true;
        var adId = detail.Id;
        var ct = _cts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                var page = await _host.GetReviewsAsync(adId, skip, ct).ConfigureAwait(false);
                if (adId != _detailAdId)
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
                UiHost.Log.Warning(ex, "[LevemetesScreen] Review page fetch failed.");
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
        if (ImGui.InvisibleButton("##leveDelReviewScrim", windowSize))
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
        using (var child = ImRaii.Child("##leveDelReviewPanel", new Vector2(w, h), true,
                   ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.AlwaysUseWindowPadding))
        {
            if (child.Success)
            {
                var innerW = ImGui.GetContentRegionAvail().X;
                ImGui.PushTextWrapPos(innerW);
                Widgets.ModalUi.Header(innerW, FontAwesomeIcon.Trash,
                    Loc.T("os.leve_review_delete_title"), UiColors.Danger);
                ImGui.TextColored(UiColors.Body, Loc.T("os.leve_review_delete_body"));
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
                if (SharedUiHelpers.Button(Loc.T("os.leve_review_delete"), new Vector2(btnW, Px(30f))))
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
        var adId = _detailAdId;
        var ct = _cts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await _host.DeleteMyReviewAsync(adId, ct).ConfigureAwait(false);
                _composeRating = 0;
                _composeText = "";
                if (adId == _detailAdId)
                {
                    StartDetailFetch();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, "[LevemetesScreen] Review delete failed.");
            }
        }, ct);
    }

    private void DrawReportLink(LevemeteDetailDto detail)
    {
        if (detail.IsMine)
        {
            return;
        }
        if (_reportThanksTimer > 0f)
        {
            _reportThanksTimer -= ImGui.GetIO().DeltaTime;
            DrawCenteredMuted(Loc.T("os.leve_report_thanks"));
            return;
        }
        var label = Loc.T("os.leve_report");
        var labelSz = ImGui.CalcTextSize(label);
        var winW = ImGui.GetContentRegionAvail().X;
        ImGui.SetCursorPosX(MathF.Max(Px(PadX), (winW - labelSz.X) * 0.5f));
        ImGui.TextColored(UiColors.Muted, label);
        if (ImGui.IsItemHovered())
        {
            SharedUiHelpers.HandOnHover();
        }
        if (ImGui.IsItemClicked())
        {
            _reportOpen = true;
            _reportPanelH = 0f;
            _reportText = "";
        }
    }

    private void DrawReportOverlay()
    {
        if (!_reportOpen)
        {
            return;
        }
        var dismissed = DrawPageOverlayPanel("leveReport", ImGui.GetWindowPos(), ImGui.GetWindowSize(),
            ref _reportPanelH, Px(300f), w =>
        {
            Widgets.ModalUi.Header(w, FontAwesomeIcon.Flag, Loc.T("os.leve_report_title"), UiColors.Danger);
            ImGui.PushTextWrapPos(w);
            ImGui.TextColored(UiColors.Body, Loc.T("os.leve_report_body"));
            ImGui.PopTextWrapPos();
            ImGui.Spacing();
            ImGui.SetNextItemWidth(w);
            InputTextMultilineWithPaste("##leveReportText", ref _reportText, 500, new Vector2(w, Px(64f)));
            ImGui.Spacing();
            var canSend = _reportText.Trim().Length > 0 && !_reportBusy;
            if (!canSend)
            {
                ImGui.BeginDisabled();
            }
            if (Widgets.ModalUi.Button($"{Loc.T("os.leve_report_send")}##leveReportSend", w))
            {
                SendReport();
            }
            if (!canSend)
            {
                ImGui.EndDisabled();
            }
        });
        if (dismissed)
        {
            _reportOpen = false;
        }
    }

    private void SendReport()
    {
        _reportBusy = true;
        var adId = _detailAdId;
        var reason = _reportText.Trim();
        var ct = _cts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await _host.ReportAdAsync(adId, reason, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, "[LevemetesScreen] Report failed.");
            }
            finally
            {
                _reportBusy = false;
                _reportOpen = false;
                _reportThanksTimer = 4f;
            }
        }, ct);
    }
}
