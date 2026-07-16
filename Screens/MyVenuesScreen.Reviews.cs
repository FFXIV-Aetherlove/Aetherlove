using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove.Services;
using AetherLove.Services.Hub;
using AetherLove.Services.Localization;
using AetherLove.Shared.Places;
using AetherLove.UI;
using AetherLove.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;

namespace AetherLove.Screens;

public partial class MyVenuesScreen
{
    private MyVenueDto? _reviewsVenue;
    private volatile VenueReviewDto[]? _reviews;
    private volatile bool _reviewsLoading;
    private volatile string? _reviewsError;
    private readonly Dictionary<Guid, ISharedImmediateTexture?> _reviewAvatarTex = new();

    private void OpenReviews(MyVenueDto venue)
    {
        _reviewsVenue = venue;
        _reviews = null;
        _reviewsError = null;
        _section = Section.Reviews;
        StartReviewsFetch(venue.Id);
    }

    /// <summary>Pulls every published review for the venue, page by page, caching each author avatar.</summary>
    private void StartReviewsFetch(Guid venueId)
    {
        if (_reviewsLoading)
        {
            return;
        }
        _reviewsLoading = true;
        var ct = _cts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                var all = new List<VenueReviewDto>();
                var skip = 0;
                while (skip < 500)
                {
                    var page = await _hubClient.GetVenueReviewsAsync(venueId, skip, ct).ConfigureAwait(false);
                    if (ct.IsCancellationRequested || venueId != _reviewsVenue?.Id)
                    {
                        return;
                    }
                    if (page.Length == 0)
                    {
                        break;
                    }
                    foreach (var review in page)
                    {
                        if (!_reviewAvatarTex.ContainsKey(review.Id) && review.AuthorAvatarWebp is { Length: > 0 })
                        {
                            _reviewAvatarTex[review.Id] =
                                AvatarDiskCache.Store(MyVenueCacheDir, $"rev_{review.Id:N}", review.AuthorAvatarWebp);
                        }
                    }
                    all.AddRange(page);
                    skip += page.Length;
                }
                _reviews = all.ToArray();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "[MyVenuesScreen] Reviews fetch failed.");
                _reviewsError = HubErrorText.Localize(ex);
            }
            finally
            {
                _reviewsLoading = false;
            }
        }, ct);
    }

    private void DrawReviews()
    {
        var venue = _reviewsVenue;
        if (venue is null)
        {
            _section = Section.List;
            return;
        }
        var winW = ImGui.GetContentRegionAvail().X;
        var pad = Px(PadX);
        var cardW = winW - pad * 2f;

        PushScrollbarStyle();
        var scrollTL = ImGui.GetCursorScreenPos();
        using (var scroll = ImRaii.Child("##myVenueReviewsScroll", ImGui.GetContentRegionAvail(), false))
        {
            if (scroll.Success)
            {
                ImGui.Dummy(new Vector2(1f, Px(4f)));
                DrawReviewsHeader(venue, cardW);
                ImGui.Spacing();
                DrawReviewsSummary(venue, pad);
                ImGui.Spacing();

                var reviews = _reviews;
                if (_reviewsLoading && reviews is null)
                {
                    LoadingIndicator.Draw();
                }
                else if (_reviewsError is not null && reviews is null)
                {
                    ImGui.SetCursorPosX(pad);
                    ImGui.PushTextWrapPos(winW - pad);
                    ImGui.TextColored(UiColors.Danger, Loc.T("places.load_failed", _reviewsError));
                    ImGui.PopTextWrapPos();
                }
                else if (reviews is { Length: 0 })
                {
                    ImGui.SetCursorPosX(pad);
                    ImGui.TextColored(UiColors.Muted, Loc.T("places.no_reviews_owner"));
                }
                else if (reviews is not null)
                {
                    foreach (var review in reviews)
                    {
                        VenueFields.DrawReviewCard(review,
                            _reviewAvatarTex.TryGetValue(review.Id, out var tex) ? tex : null, cardW, PadX);
                        ImGui.Spacing();
                    }
                }
                ImGui.Dummy(new Vector2(1f, Px(12f)));
            }
        }
        PopScrollbarStyle();

        if (DrawFloatingBackPill(scrollTL + Px(10f, 10f), Loc.T("places.back"), FontAwesomeIcon.Store))
        {
            _section = Section.List;
            _entrance.Arm();
        }
    }

    /// <summary>Banner + logo + name header for the reviews page, mirroring the venue detail's.</summary>
    private void DrawReviewsHeader(MyVenueDto venue, float cardW)
    {
        var dl = ImGui.GetWindowDrawList();
        var pad = Px(PadX);
        var h = Px(130f);
        var origin = ImGui.GetCursorScreenPos();
        var tl = new Vector2(origin.X + pad, origin.Y);
        var br = tl + new Vector2(cardW, h);

        var wrap = _bannerTex.TryGetValue(venue.Id, out var tex) ? tex?.GetWrapOrDefault() : null;
        if (wrap != null)
        {
            var visible = h / cardW;
            var uv0 = new Vector2(0f, (1f - visible) * 0.5f);
            var uv1 = new Vector2(1f, (1f + visible) * 0.5f);
            dl.AddImageRounded(wrap.Handle, tl, br, uv0, uv1, 0xFFFFFFFFu, Px(12f), ImDrawFlags.RoundCornersAll);
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
        var logoWrap = _logoTex.TryGetValue(venue.Id, out var lt) ? lt?.GetWrapOrDefault() : null;
        if (logoWrap != null)
        {
            dl.AddImageRounded(logoWrap.Handle, logoTL, logoTL + new Vector2(logoSize, logoSize),
                Vector2.Zero, Vector2.One, 0xFFFFFFFFu, logoSize * 0.24f, ImDrawFlags.RoundCornersAll);
        }
        else
        {
            dl.AddRectFilled(logoTL, logoTL + new Vector2(logoSize, logoSize), UiColors.AvatarFallback, logoSize * 0.24f);
        }
        using (UiFonts.H3?.Push())
        {
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
                new Vector2(logoTL.X + logoSize + Px(10f), br.Y - Px(16f) - ImGui.GetFontSize()),
                0xFFFFFFFFu, TruncateToWidth(venue.Name, cardW - logoSize - Px(40f)));
        }

        ImGui.SetCursorScreenPos(new Vector2(origin.X, br.Y + Px(4f)));
    }

    private void DrawReviewsSummary(MyVenueDto venue, float pad)
    {
        ImGui.SetCursorPosX(pad);
        if (venue.ReviewCount == 0)
        {
            ImGui.TextColored(UiColors.Muted, Loc.T("places.no_rating_yet"));
            return;
        }
        var dl = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var starPx = ImGui.GetFontSize();
        var starSz = IconDraw.Measure(FontAwesomeIcon.Star, starPx);
        var rounded = (int)Math.Round(venue.AverageRating);
        for (var i = 0; i < 5; i++)
        {
            IconDraw.Add(dl, FontAwesomeIcon.Star, starPx,
                new Vector2(pos.X + i * (starSz.X + Px(3f)), pos.Y),
                i < rounded ? VenueFields.GoldStar : VenueFields.DimStar);
        }
        dl.AddText(
            new Vector2(pos.X + 5 * (starSz.X + Px(3f)) + Px(8f), pos.Y + (starSz.Y - ImGui.GetTextLineHeight()) * 0.5f),
            ImGui.GetColorU32(UiColors.Body),
            Loc.T("places.rating_summary", venue.AverageRating.ToString("0.0"), venue.ReviewCount));
        ImGui.Dummy(new Vector2(1f, starSz.Y + Px(2f)));
    }
}
