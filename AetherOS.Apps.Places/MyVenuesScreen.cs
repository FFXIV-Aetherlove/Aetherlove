using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AetherLove;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Shared.Places;
using AetherLove.UI;
using AetherLove.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Places;

/// <summary>Venue-owner management: the "My venues" list and the venue definition editor
/// (details, tags, location, opening times, banner + logo).</summary>
public partial class MyVenuesScreen
{
    private enum Section { List, Editor, Reviews }

    private Section _section = Section.List;

    private readonly IPlacesHost _host;
    private readonly IAppCapabilities _caps;
    private readonly Action _backToPlaces;
    private readonly CancellationTokenSource _cts = new();

    private volatile MyVenueDto[]? _venues;
    private volatile bool _loading;
    private volatile string? _loadError;
    private volatile string? _actionError;
    private volatile bool _saving;
    private float _savedTimer;

    private readonly Dictionary<Guid, ISharedImmediateTexture?> _logoTex = new();
    private readonly Dictionary<Guid, ISharedImmediateTexture?> _bannerTex = new();
    private readonly EntranceAnimation _entrance = new();

    private const float PadX = 16f;
    private const int MaxVenuesFallback = 3;

    private static string MyVenueCacheDir =>
        Path.Combine(UiHost.PluginInterface.ConfigDirectory.FullName, "MyVenueCache");

    public MyVenuesScreen(IPlacesHost host, IAppCapabilities caps, Action backToPlaces)
    {
        _host = host;
        _caps = caps;
        _backToPlaces = backToPlaces;
    }

    public void OnShow()
    {
        _section = Section.List;
        StartListFetch();
    }

    private void StartListFetch()
    {
        if (_loading)
        {
            return;
        }
        _loading = true;
        _loadError = null;
        var ct = _cts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                var venues = await _host.GetMyVenuesAsync(ct).ConfigureAwait(false);
                if (ct.IsCancellationRequested)
                {
                    return;
                }
                foreach (var venue in venues)
                {
                    _logoTex[venue.Id] = venue.LogoWebp is { Length: > 0 }
                        ? AvatarDiskCache.Store(MyVenueCacheDir, $"logo_{venue.Id:N}", venue.LogoWebp)
                        : null;
                    _bannerTex[venue.Id] = venue.BannerWebp is { Length: > 0 }
                        ? AvatarDiskCache.Store(MyVenueCacheDir, $"banner_{venue.Id:N}", venue.BannerWebp)
                        : null;
                }
                _venues = venues;
                _entrance.Arm();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, "[MyVenuesScreen] Venue list fetch failed.");
                _loadError = HubErrorText.Localize(ex);
            }
            finally
            {
                _loading = false;
            }
        }, ct);
    }

    public void Draw()
    {
        if (_savedTimer > 0f)
        {
            _savedTimer -= ImGui.GetIO().DeltaTime;
        }

        switch (_section)
        {
            case Section.List:
                DrawList();
                break;
            case Section.Editor:
                DrawEditor();
                break;
            case Section.Reviews:
                DrawReviews();
                break;
        }
    }

    private void DrawList()
    {
        var t = ThemeService.Current;
        var winW = ImGui.GetContentRegionAvail().X;

        ImGui.Spacing();
        ImGui.SetCursorPosX(Px(PadX));
        if (DrawFloatingBackPill(ImGui.GetCursorScreenPos(), Loc.T("places.back"), FontAwesomeIcon.List))
        {
            _backToPlaces();
            return;
        }
        ImGui.Spacing();
        DrawSubpageHeading(Loc.T("places.my_venues"), PadX);

        if (_loading && _venues is null)
        {
            LoadingIndicator.Draw();
            return;
        }
        if (_loadError is not null && _venues is null)
        {
            ImGui.SetCursorPosX(Px(PadX));
            ImGui.PushTextWrapPos(winW - Px(PadX));
            ImGui.TextColored(UiColors.Danger, Loc.T("places.load_failed", _loadError));
            ImGui.PopTextWrapPos();
            return;
        }
        var venues = _venues;
        if (venues is null)
        {
            return;
        }

        PushScrollbarStyle();
        using (var scroll = ImRaii.Child("##myVenuesScroll", ImGui.GetContentRegionAvail(), false))
        {
            if (scroll.Success)
            {
                _entrance.BeginFrame();
                if (_savedTimer > 0f)
                {
                    ImGui.SetCursorPosX(Px(PadX));
                    ImGui.TextColored(UiColors.Success, Loc.T("places.venue_saved"));
                    ImGui.Spacing();
                }
                if (venues.Length == 0)
                {
                    ImGui.Spacing();
                    ImGui.SetCursorPosX(Px(PadX));
                    ImGui.PushTextWrapPos(winW - Px(PadX));
                    ImGui.TextColored(UiColors.Muted, Loc.T("places.my_venues_empty"));
                    ImGui.PopTextWrapPos();
                    ImGui.Spacing();
                }
                foreach (var venue in venues)
                {
                    DrawVenueCard(venue, winW);
                    ImGui.Spacing();
                }

                ImGui.Spacing();
                ImGui.SetCursorPosX(Px(PadX));
                PushThemeButton(t);
                ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
                var atCap = venues.Length >= MaxVenuesFallback;
                if (atCap)
                {
                    ImGui.BeginDisabled();
                }
                if (ImGui.Button(Loc.T("places.new_venue"), new Vector2(winW - Px(PadX) * 2f, Px(34f))))
                {
                    OpenEditor(null);
                }
                if (atCap)
                {
                    ImGui.EndDisabled();
                    ImGui.SetCursorPosX(Px(PadX));
                    ImGui.TextColored(UiColors.Hint, Loc.T("places.venue_cap", MaxVenuesFallback));
                }
                ImGui.PopStyleVar();
                PopThemeButton();
                ImGui.Dummy(new Vector2(1f, Px(10f)));
                _entrance.EndFrame();
            }
        }
        PopScrollbarStyle();
    }

    private void DrawVenueCard(MyVenueDto venue, float winW)
    {
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var pad = Px(PadX);
        var cardW = winW - pad * 2f;
        var cardH = Px(88f);
        var btnW = Px(92f);
        var origin = ImGui.GetCursorScreenPos();
        var tl = new Vector2(origin.X + pad, origin.Y);
        var br = tl + new Vector2(cardW, cardH);
        var mainRight = tl.X + cardW - btnW;

        // Left area edits the venue; the right column opens its reviews. Non-overlapping so no click ambiguity.
        ImGui.SetCursorScreenPos(tl);
        var editClicked = ImGui.InvisibleButton($"##myVenueEdit_{venue.Id:N}", new Vector2(cardW - btnW, cardH));
        var editHovered = ImGui.IsItemHovered();
        ImGui.SetCursorScreenPos(new Vector2(mainRight, tl.Y));
        var reviewsClicked = ImGui.InvisibleButton($"##myVenueReviews_{venue.Id:N}", new Vector2(btnW, cardH));
        var reviewsHovered = ImGui.IsItemHovered();

        dl.AddRectFilled(tl, br,
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, editHovered || reviewsHovered ? 0.07f : 0.045f)), Px(12f));
        dl.AddRect(tl, br, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.07f)), Px(12f), ImDrawFlags.None, Px(1f));

        var logoSize = Px(56f);
        var logoTL = tl + new Vector2(Px(12f), (cardH - logoSize) * 0.5f);
        var wrap = _logoTex.TryGetValue(venue.Id, out var tex) ? tex?.GetWrapOrDefault() : null;
        if (wrap != null)
        {
            dl.AddImageRounded(wrap.Handle, logoTL, logoTL + new Vector2(logoSize, logoSize),
                Vector2.Zero, Vector2.One, 0xFFFFFFFFu, logoSize * 0.24f, ImDrawFlags.RoundCornersAll);
        }
        else
        {
            dl.AddRectFilled(logoTL, logoTL + new Vector2(logoSize, logoSize), UiColors.AvatarFallback, logoSize * 0.24f);
        }

        var textX = logoTL.X + logoSize + Px(12f);
        var textMaxW = mainRight - textX - Px(10f);
        dl.AddText(new Vector2(textX, tl.Y + Px(11f)), 0xFFFFFFFFu, TruncateToWidth(venue.Name, textMaxW));
        dl.AddText(new Vector2(textX, tl.Y + Px(11f) + ImGui.GetTextLineHeight() + Px(3f)),
            ImGui.GetColorU32(UiColors.Subtle),
            TruncateToWidth(VenueFields.LocationLine(venue.World, venue.District, venue.Ward, venue.Plot, venue.Room),
                textMaxW));

        var statsY = tl.Y + Px(11f) + (ImGui.GetTextLineHeight() + Px(3f)) * 2f;
        dl.AddText(new Vector2(textX, statsY), ImGui.GetColorU32(UiColors.Muted),
            Loc.T("places.venue_stats", venue.LikeCount, venue.ReviewCount));
        if (venue.Status != VenueStatus.Active)
        {
            var badge = Loc.T(venue.Status == VenueStatus.PendingModeration
                ? "places.venue_pending"
                : "places.venue_unlisted");
            var badgeSz = ImGui.CalcTextSize(badge);
            dl.AddText(new Vector2(mainRight - badgeSz.X - Px(8f), statsY),
                ImGui.GetColorU32(UiColors.WarningAccent), badge);
        }

        dl.AddLine(new Vector2(mainRight, tl.Y + Px(14f)), new Vector2(mainRight, br.Y - Px(14f)),
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.10f)), Px(1f));
        var col = ImGui.GetColorU32(reviewsHovered ? t.AccentLight : UiColors.Body);
        var iconPx = ImGui.GetFontSize() * 1.1f;
        var iconSz = IconDraw.Measure(FontAwesomeIcon.Star, iconPx);
        var label = Loc.T("places.reviews_title");
        var labelSz = ImGui.CalcTextSize(label);
        var cx = mainRight + btnW * 0.5f;
        var blockY = tl.Y + (cardH - iconSz.Y - Px(4f) - labelSz.Y) * 0.5f;
        IconDraw.Add(dl, FontAwesomeIcon.Star, iconPx, new Vector2(cx - iconSz.X * 0.5f, blockY), col);
        dl.AddText(new Vector2(cx - labelSz.X * 0.5f, blockY + iconSz.Y + Px(4f)), col, label);

        if (reviewsClicked)
        {
            OpenReviews(venue);
        }
        else if (editClicked)
        {
            OpenEditor(venue);
        }
        ImGui.SetCursorScreenPos(new Vector2(origin.X, br.Y));
    }
}
