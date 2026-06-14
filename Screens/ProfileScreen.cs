using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Navigation;
using AetherLove.Services;
using AetherLove.Services.Hub;
using AetherLove.Services.Localization;
using AetherLove.Shared.Moderation;
using AetherLove.Shared.Profile;
using AetherLove.Shared.Profile.Enums;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;

namespace AetherLove.Screens;

/// <summary>Determines the back-button icon on the profile screen.</summary>
public enum ProfileSource { Deck, Self, Chat }

/// <summary>Full-page profile detail view for a single player.</summary>
public class ProfileScreen
{
    private readonly ScreenRouter _router;
    private readonly AetherLoveHubClient _hub;
    private readonly FlairCatalog _flairCatalog;

    private ProfileDetailDto? _profile;
    private string _photoSubtitle = string.Empty;
    private ProfileSource _source = ProfileSource.Deck;

    private Guid _loadingProfileId;
    private volatile bool _loading;
    private volatile string? _loadError;
    private CancellationTokenSource _cts = new();

    // Cached own-profile; invalidated after a local save and on every (re)connect.
    private ProfileDetailDto? _myProfileCache;

    private readonly Dictionary<int, ISharedImmediateTexture?> _photoTextures = new();

    /// <summary>NSFW photos the viewer has explicitly chosen to reveal this session.</summary>
    private readonly HashSet<(Guid ProfileId, int Order)> _revealedNsfw = new();

    private int _photoIndex = 0;
    private int _prevPhotoIndex = -1;
    private float _fadeAlpha = 1f;
    private float _autoTimer = 0f;
    private bool _autoEnabled = true;

    private float _backPillPulseTimer;

    // Report flow state.
    private bool _reportPendingOpen;
    private string _reportReason = string.Empty;
    private bool _reportAgree;
    private volatile bool _reportSubmitting;
    private volatile string? _reportError;
    private float _reportSubmittedTimer;
    /// <summary>Sticky once reported; hides the triangle and gates the modal into success-mode. Cleared by <see cref="ResetState"/>.</summary>
    private bool _reportedThisProfile;

    private const float ReportAutoCloseSeconds = 5f;

    private const float AutoInterval = 4.5f;
    private const float FadeSpeed = 2.5f;

    private static float PhotoHeight => Px(560f);
    private static float PadX => Px(18f);
    private static float ChipPadX => Px(13f);
    private static float ChipPadY => Px(6f);

    private const uint TextPrimary = 0xFFEEEEEE;
    private const uint TextSecondary = UiColors.TextMuted;
    private const uint GraphInactive = 0xFF2D2D2D;

    public ProfileScreen(ScreenRouter router, AetherLoveHubClient hub, FlairCatalog flairCatalog)
    {
        _router = router;
        _hub = hub;
        _flairCatalog = flairCatalog;
    }

    public void SetProfile(Guid profileId, ProfileSource source)
    {
        _source = source;

        if (source != ProfileSource.Self
            && _profile is not null
            && _profile.ProfileId == profileId
            && !_loading)
        {
            return;
        }

        ResetState();
        _loadingProfileId = profileId;
        _loading = true;
        _loadError = null;
        var ct = _cts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                var dto = await _hub.GetProfileDetailAsync(profileId, ct).ConfigureAwait(false);
                if (ct.IsCancellationRequested)
                {
                    return;
                }
                ApplyProfile(dto);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (ct.IsCancellationRequested)
                {
                    return;
                }
                _loadError = HubErrorText.Localize(ex);
                _loading = false;
                Plugin.Log.Warning(ex, $"[ProfileScreen] GetProfileDetailAsync({profileId}) failed.");
            }
        }, ct);
    }

    /// <summary>Fetches the signed-in user's profile from the server (or reuses a fresh cache).</summary>
    public void SetMyProfile()
    {
        _source = ProfileSource.Self;
        ResetState();

        if (_myProfileCache is not null)
        {
            ApplyProfile(_myProfileCache);
            return;
        }

        _loadingProfileId = Guid.Empty;
        _loading = true;
        _loadError = null;
        var ct = _cts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                var dto = await _hub.GetMyProfileDetailAsync(ct).ConfigureAwait(false);
                if (ct.IsCancellationRequested)
                {
                    return;
                }
                _myProfileCache = dto;
                ApplyProfile(dto);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (ct.IsCancellationRequested)
                {
                    return;
                }
                _loadError = HubErrorText.Localize(ex);
                _loading = false;
                Plugin.Log.Warning(ex, "[ProfileScreen] GetMyProfileDetailAsync failed.");
            }
        }, ct);
    }

    /// <summary>Drops the cached "My Profile" copy so the next <see cref="SetMyProfile"/> re-fetches.</summary>
    public void InvalidateMyProfileCache() => _myProfileCache = null;

    private void ResetState()
    {
        _cts.Cancel();
        _cts.Dispose();
        _cts = new CancellationTokenSource();

        _profile = null;
        _photoSubtitle = string.Empty;
        _photoIndex = 0;
        _prevPhotoIndex = -1;
        _fadeAlpha = 1f;
        _autoTimer = 0f;
        _autoEnabled = true;
        _backPillPulseTimer = 0f;
        _photoTextures.Clear();
        _revealedNsfw.Clear();

        _reportPendingOpen = false;
        _reportReason = string.Empty;
        _reportAgree = false;
        _reportSubmitting = false;
        _reportError = null;
        _reportSubmittedTimer = 0f;
        _reportedThisProfile = false;
        _loadError = null;
    }

    private void ApplyProfile(ProfileDetailDto dto)
    {
        _profile = dto;
        var parts = new List<string>(2);
        var race = RaceLabel(dto.Race);
        if (!string.IsNullOrEmpty(race))
        {
            parts.Add(race);
        }
        var region = RegionLabel(dto.Region);
        if (!string.IsNullOrEmpty(region))
        {
            parts.Add(region);
        }
        _photoSubtitle = string.Join("  ·  ", parts);
        _loading = false;
        CachePhotoTextures(dto);
    }

    private void CachePhotoTextures(ProfileDetailDto dto)
    {
        var cacheDir = Path.Combine(
            Plugin.PluginInterface.ConfigDirectory.FullName, "ProfileDetailCache");
        try
        {
            Directory.CreateDirectory(cacheDir);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[ProfileScreen] Could not create the photo cache directory; photos will not be cached.");
            return;
        }

        foreach (var photo in dto.Photos)
        {
            try
            {
                var path = Path.Combine(cacheDir,
                    $"{dto.ProfileId}_{photo.Order}{ImageFormat.ExtensionFor(photo.WebpBytes)}");
                File.WriteAllBytes(path, photo.WebpBytes);
                _photoTextures[photo.Order] = Plugin.TextureProvider.GetFromFile(path);
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex,
                    $"[ProfileScreen] Failed to cache photo {photo.Order} for {dto.ProfileId}.");
            }
        }
    }

    public void OnShow()
    {
        _backPillPulseTimer = 0f;
    }

    public void Draw()
    {
        if (_profile is null)
        {
            if (_loadError is null && _loading)
            {
                Widgets.LoadingIndicator.Draw();
            }
            else
            {
                ImGui.SetCursorPosY(ImGui.GetWindowSize().Y * 0.45f);
                var msg = _loadError is not null
                    ? Loc.T("profile.load_failed", _loadError)
                    : Loc.T("profile.none_loaded");
                ImGui.SetCursorPosX((ImGui.GetWindowSize().X - ImGui.CalcTextSize(msg).X) * 0.5f);
                ImGui.PushTextWrapPos(0f);
                ImGui.TextColored(
                    _loadError is not null
                        ? UiColors.Danger
                        : new Vector4(0.5f, 0.5f, 0.5f, 1f),
                    msg);
                ImGui.PopTextWrapPos();
            }

            if (_source != ProfileSource.Self)
            {
                DrawBackPill(ImGui.GetWindowPos() + Px(10f, 10f),
                             ImGui.GetForegroundDrawList());
            }
            return;
        }

        var dt = ImGui.GetIO().DeltaTime;
        _backPillPulseTimer += dt;
        var winSize = ImGui.GetWindowSize();
        var photoCount = CountPhotos();

        UpdateAnimation(dt, photoCount);

        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.102f, 0.102f, 0.102f, 1f));
        PushScrollbarStyle();

        using (var scroll = ImRaii.Child("##profileScroll", Vector2.Zero, false, ImGuiWindowFlags.None))
        {
            if (scroll.Success)
            {
                var scrollViewportTL = ImGui.GetWindowPos();
                var dl = ImGui.GetWindowDrawList();

                // Submit the sticky back-pill's hit-area first so it owns the top-left corner at every scroll
                // offset. Submitted last, a scrolled-in section button underneath consumes the click (ImGui
                // hands overlaps to the earlier item). Restore the cursor so it doesn't shift content down.
                var contentStart = ImGui.GetCursorScreenPos();
                DrawBackPill(scrollViewportTL + Px(10f, 10f), ImGui.GetForegroundDrawList());
                ImGui.SetCursorScreenPos(contentStart);

                DrawImageScroller(photoCount, winSize.X, dl);

                ImGui.Spacing();
                ImGui.Spacing();

                DrawAbout(winSize.X, dl);
                DrawLookingFor(winSize.X, dl);
                DrawInfoSection(winSize.X, dl);
                DrawFavoriteJob(winSize.X, dl);
                DrawFavoriteLocation(winSize.X, dl);
                DrawFavoriteExpansion(winSize.X, dl);
                DrawMusicLinks(winSize.X, dl);
                DrawFavoriteMovie(winSize.X, dl);
                DrawFavoriteAnime(winSize.X, dl);
                DrawFavoriteFFCharacter(winSize.X, dl);
                // Peer play-times are stored in the owner's timezone; re-base into the viewer's so the
                // hours match the viewer's clock. Own-profile view (and the edit form) keep raw data.
                var weekdayMask = _profile.WeekdayHoursMask;
                var weekendMask = _profile.WeekendHoursMask;
                if (_source != ProfileSource.Self)
                {
                    var viewerOffset = _myProfileCache?.TimezoneOffsetMinutes
                        ?? (int)TimeZoneInfo.Local.GetUtcOffset(DateTimeOffset.UtcNow).TotalMinutes;
                    weekdayMask = TimeZoneShift.ShiftHoursMask(weekdayMask, _profile.TimezoneOffsetMinutes, viewerOffset);
                    weekendMask = TimeZoneShift.ShiftHoursMask(weekendMask, _profile.TimezoneOffsetMinutes, viewerOffset);
                }
                DrawAvailabilityGraph(Loc.T("profile.weekday_playtimes"), weekdayMask, winSize.X, dl);
                DrawAvailabilityGraph(Loc.T("profile.weekend_playtimes"), weekendMask, winSize.X, dl);
                DrawSyncTool(winSize.X, dl);

                ImGui.Spacing(); ImGui.Spacing();
                ImGui.Spacing(); ImGui.Spacing();
            }
        }

        PopScrollbarStyle();
        ImGui.PopStyleColor(1);

        if (_source != ProfileSource.Self)
        {
            if (_reportSubmittedTimer > 0f)
            {
                _reportSubmittedTimer -= ImGui.GetIO().DeltaTime;
            }
            if (_reportPendingOpen)
            {
                _reportPendingOpen = false;
                Widgets.ModalHost.Instance?.Open(380f, DrawReportBody);
            }
        }
    }


    private void DrawImageScroller(int photoCount, float winW, ImDrawListPtr dl)
    {
        var photoSz = new Vector2(winW, PhotoHeight);
        var photoTL = ImGui.GetCursorScreenPos();
        var photoLocalY = ImGui.GetCursorPosY();
        var fallbackColor = AvatarFallbackColor(_profile!.ProfileId);

        if (_prevPhotoIndex >= 0)
        {
            var prevOrder = GetPhotoOrderAt(_prevPhotoIndex);
            if (prevOrder >= 0 && ShouldBlur(prevOrder))
            {
                DrawBlurredPhoto(dl, GetPhotoTextureAt(_prevPhotoIndex), photoTL, photoSz,
                    fallbackColor, 1f - _fadeAlpha);
            }
            else
            {
                DrawPhoto(dl, GetPhotoTextureAt(_prevPhotoIndex), photoTL, photoSz,
                    fallbackColor, 1f - _fadeAlpha);
            }
        }

        var currentOrder = GetPhotoOrderAt(_photoIndex);
        var currentBlurred = currentOrder >= 0 && ShouldBlur(currentOrder);
        if (currentBlurred)
        {
            DrawBlurredPhoto(dl, GetPhotoTextureAt(_photoIndex), photoTL, photoSz,
                fallbackColor, _fadeAlpha);
        }
        else
        {
            DrawPhoto(dl, GetPhotoTextureAt(_photoIndex), photoTL, photoSz,
                fallbackColor, _fadeAlpha);
        }

        dl.AddRectFilledMultiColor(
            new Vector2(photoTL.X, photoTL.Y + PhotoHeight * 0.50f),
            photoTL + photoSz,
            0x00000000, 0x00000000,
            0xF2000000, 0xF2000000);

        ImFontPtr nameFont = ImGui.GetFont();
        var nameFontSz = ImGui.GetFontSize();
        using (UiFonts.H1?.Push())
        {
            nameFont = ImGui.GetFont();
            nameFontSz = ImGui.GetFontSize();
        }

        var subText = _photoSubtitle;

        ImFontPtr subFont = ImGui.GetFont();
        var subFontSz = ImGui.GetFontSize();
        var subWidth = 0f;
        using (UiFonts.H3?.Push())
        {
            subFont = ImGui.GetFont();
            subFontSz = ImGui.GetFontSize();
            subWidth = ImGui.CalcTextSize(subText).X;
        }

        var nameY = photoTL.Y + PhotoHeight - nameFontSz - subFontSz - Px(30f);
        dl.AddText(nameFont, nameFontSz,
            new Vector2(photoTL.X + PadX, nameY),
            0xFFFFFFFF, _profile.DisplayName);

        var subY = nameY + nameFontSz + Px(4f);
        dl.AddText(subFont, subFontSz,
            new Vector2(photoTL.X + PadX, subY),
            0xFFCCCCCC, subText);

        var languageNames = LanguagesFromMask(_profile.LanguageMask);
        if (languageNames.Count > 0)
        {
            var FlagW = Px(18f);
            var FlagH = Px(12f);
            var FlagGap = Px(4f);

            var flagX = photoTL.X + PadX + subWidth + Px(8f);
            // Nudge down: text glyphs sit below the line's vertical centre, so centring the flag on the
            // raw font height reads slightly high.
            var flagY = subY + (subFontSz - FlagH) * 0.5f + Px(2f);

            foreach (var lang in languageNames)
            {
                var tex = LanguageFlagService.GetFlag(lang)?.GetWrapOrDefault();
                if (tex is null)
                {
                    continue;
                }
                dl.AddImage(tex.Handle,
                    new Vector2(flagX, flagY),
                    new Vector2(flagX + FlagW, flagY + FlagH));
                flagX += FlagW + FlagGap;
            }
        }

        if (_profile.FlairIds is { Length: > 0 })
        {
            var flairLang = FlairCatalog.ResolveLanguage(Plugin.Configuration.PluginLanguage);
            var flairY = subY + subFontSz + Px(6f);
            var flairX = photoTL.X + PadX;
            foreach (var fid in _profile.FlairIds)
            {
                var f = _flairCatalog.Get(fid);
                if (f is null)
                {
                    continue;
                }
                flairX += DrawFlairPill(dl, new Vector2(flairX, flairY),
                    FlairCatalog.Text(f, flairLang), FlairCatalog.Description(f, flairLang), f.BackgroundColor) + Px(5f);
            }
        }

        if (photoCount > 1)
        {
            var DotSpacing = Px(14f);
            var DotR = Px(3.5f);
            var totalW = (photoCount - 1) * DotSpacing;
            var dotStartX = photoTL.X + winW * 0.5f - totalW * 0.5f;
            var dotY = photoTL.Y + PhotoHeight - Px(14f);

            for (var i = 0; i < photoCount; i++)
            {
                var dot = new Vector2(dotStartX + i * DotSpacing, dotY);
                var r = i == _photoIndex ? DotR + Px(1.5f) : DotR;
                var col = i == _photoIndex ? 0xFFFFFFFFu : 0x88FFFFFFu;
                dl.AddCircleFilled(dot, r, col);
            }
        }

        if (_source != ProfileSource.Self && !_reportedThisProfile)
        {
            DrawReportTriangleButton(photoTL, photoSz, dl);
        }

        ImGui.Dummy(photoSz);

        // NSFW reveal hit target. Submitted before the arrows so they claim edge clicks.
        if (currentBlurred && _fadeAlpha >= 0.95f)
        {
            ImGui.SetCursorScreenPos(photoTL);
            ImGui.InvisibleButton("##nsfwReveal", photoSz);
            if (ImGui.IsItemClicked())
            {
                _revealedNsfw.Add((_profile.ProfileId, currentOrder));
            }
            var hovered = ImGui.IsItemHovered();
            DrawNsfwRevealPill(dl, photoTL, photoSz, hovered);
        }

        if (photoCount > 1)
        {
            var arrowScreenY = photoTL.Y + PhotoHeight * 0.5f - Px(20f);
            var leftBtnScreen = new Vector2(photoTL.X + Px(10f), arrowScreenY);
            var rightBtnScreen = new Vector2(photoTL.X + winW - Px(50f), arrowScreenY);

            ImGui.SetCursorScreenPos(leftBtnScreen);
            if (ImGui.InvisibleButton("##photoPrev", Px(40f, 40f)))
            {
                ManualNavigate(-1, photoCount);
            }
            DrawArrowPill(dl, leftBtnScreen, Px(40f), isLeft: true, hovered: ImGui.IsItemHovered());

            ImGui.SetCursorScreenPos(rightBtnScreen);
            if (ImGui.InvisibleButton("##photoNext", Px(40f, 40f)))
            {
                ManualNavigate(+1, photoCount);
            }
            DrawArrowPill(dl, rightBtnScreen, Px(40f), isLeft: false, hovered: ImGui.IsItemHovered());

            ImGui.SetCursorPosY(photoLocalY + PhotoHeight);
        }

        ImGui.SetCursorPosY(photoLocalY + PhotoHeight);
    }

    /// <summary>Pseudo-blur via 13 offset image draws plus a frosted-glass tint.</summary>
    private static void DrawBlurredPhoto(ImDrawListPtr dl, ISharedImmediateTexture? tex,
        Vector2 tl, Vector2 sz, uint fallbackColor, float alpha)
    {
        if (alpha <= 0f)
        {
            return;
        }

        var wrap = tex?.GetWrapOrDefault();
        if (wrap is null)
        {
            DrawPhoto(dl, tex, tl, sz, fallbackColor, alpha);
            return;
        }

        var imgAspect = (float)wrap.Width / wrap.Height;
        var tgtAspect = sz.X / sz.Y;
        Vector2 uv0, uv1;
        if (imgAspect > tgtAspect)
        {
            var uvW = tgtAspect / imgAspect;
            uv0 = new Vector2((1f - uvW) * 0.5f, 0f);
            uv1 = new Vector2((1f + uvW) * 0.5f, 1f);
        }
        else
        {
            var uvH = imgAspect / tgtAspect;
            uv0 = new Vector2(0f, 0f);
            uv1 = new Vector2(1f, uvH);
        }

        Span<Vector2> offsets = stackalloc Vector2[]
        {
            Px(0f, 0f),
            Px(8f, 0f), Px(-8f, 0f),
            Px(0f, 8f), Px(0f, -8f),
            Px(6f, 6f), Px(-6f, 6f),
            Px(6f, -6f), Px(-6f, -6f),
            Px(16f, 0f), Px(-16f, 0f),
            Px(0f, 16f), Px(0f, -16f),
        };

        var perSampleAlpha = alpha / offsets.Length;
        var sampleA = (uint)Math.Clamp((int)(perSampleAlpha * 255f), 0, 255);
        var sampleCol = (sampleA << 24) | 0x00FFFFFFu;

        foreach (var off in offsets)
        {
            dl.AddImage(wrap.Handle, tl + off, tl + sz + off, uv0, uv1, sampleCol);
        }

        var tintA = (uint)Math.Clamp((int)(alpha * 0.55f * 255f), 0, 255);
        var tintCol = (tintA << 24) | 0x00101010u;
        dl.AddRectFilled(tl, tl + sz, tintCol);
    }

    private void DrawNsfwRevealPill(ImDrawListPtr dl, Vector2 photoTL, Vector2 photoSz, bool hovered)
    {
        var t = ThemeService.Current;
        var center = photoTL + photoSz * 0.5f;

        ImGui.PushFont(Plugin.PluginInterface.UiBuilder.FontIcon);
        var iconStr = FontAwesomeIcon.EyeSlash.ToIconString();
        var iconSz = ImGui.CalcTextSize(iconStr);
        ImGui.PopFont();

        var Label = Loc.T("profile.nsfw_reveal");
        var labelSz = ImGui.CalcTextSize(Label);

        var PadX = Px(18f);
        var PadY = Px(12f);
        var IconGap = Px(10f);

        var pillW = PadX + iconSz.X + IconGap + labelSz.X + PadX;
        var pillH = MathF.Max(iconSz.Y, labelSz.Y) + PadY * 2f;
        var pillTL = new Vector2(center.X - pillW * 0.5f, center.Y - pillH * 0.5f);
        var pillBR = pillTL + new Vector2(pillW, pillH);

        var bg = hovered ? t.AccentWithAlpha(0.92f) : 0xE0202020u;
        dl.AddRectFilled(pillTL, pillBR, bg, pillH * 0.5f);
        dl.AddRect(pillTL, pillBR, 0x88FFFFFFu, pillH * 0.5f, ImDrawFlags.None, 1.5f);

        ImGui.PushFont(Plugin.PluginInterface.UiBuilder.FontIcon);
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
            new Vector2(pillTL.X + PadX, center.Y - iconSz.Y * 0.5f),
            0xFFFFFFFF, iconStr);
        ImGui.PopFont();

        dl.AddText(
            new Vector2(pillTL.X + PadX + iconSz.X + IconGap, center.Y - labelSz.Y * 0.5f),
            0xFFFFFFFF, Label);
    }

    private static void DrawPhoto(ImDrawListPtr dl, ISharedImmediateTexture? tex,
        Vector2 tl, Vector2 sz, uint fallbackColor, float alpha)
    {
        if (alpha <= 0f)
        {
            return;
        }
        var a = (uint)Math.Clamp((int)(alpha * 255f), 0, 255);
        var col = (a << 24) | 0x00FFFFFF;

        var wrap = tex?.GetWrapOrDefault();
        if (wrap != null)
        {
            var imgAspect = (float)wrap.Width / wrap.Height;
            var tgtAspect = sz.X / sz.Y;
            Vector2 uv0, uv1;
            if (imgAspect > tgtAspect)
            {
                var uvW = tgtAspect / imgAspect;
                uv0 = new Vector2((1f - uvW) * 0.5f, 0f);
                uv1 = new Vector2((1f + uvW) * 0.5f, 1f);
            }
            else
            {
                var uvH = imgAspect / tgtAspect;
                uv0 = new Vector2(0f, 0f);
                uv1 = new Vector2(1f, uvH);
            }
            dl.AddImage(wrap.Handle, tl, tl + sz, uv0, uv1, col);
        }
        else
        {
            var faded = (fallbackColor & 0x00FFFFFF) | (a << 24);
            dl.AddRectFilled(tl, tl + sz, faded);
        }
    }

    private static void DrawArrowPill(ImDrawListPtr dl, Vector2 tl, float size,
        bool isLeft, bool hovered)
    {
        var center = tl + new Vector2(size * 0.5f, size * 0.5f);
        var bg = hovered ? ThemeService.Current.AccentWithAlpha(0.80f) : 0xBB000000u;
        dl.AddCircleFilled(center, size * 0.5f, bg);

        ImGui.PushFont(Plugin.PluginInterface.UiBuilder.FontIcon);
        var iconStr = isLeft
            ? FontAwesomeIcon.ChevronLeft.ToIconString()
            : FontAwesomeIcon.ChevronRight.ToIconString();
        var iconSz = ImGui.CalcTextSize(iconStr);
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
            center - iconSz * 0.5f, 0xFFFFFFFF, iconStr);
        ImGui.PopFont();
    }


    private void UpdateAnimation(float dt, int photoCount)
    {
        if (photoCount <= 1)
        {
            return;
        }

        if (_prevPhotoIndex >= 0)
        {
            _fadeAlpha = Math.Clamp(_fadeAlpha + dt * FadeSpeed, 0f, 1f);
            if (_fadeAlpha >= 1f)
            {
                _prevPhotoIndex = -1;
            }
        }

        if (_autoEnabled)
        {
            _autoTimer += dt;
            if (_autoTimer >= AutoInterval)
            {
                _autoTimer = 0f;
                StartTransition((_photoIndex + 1) % photoCount);
            }
        }
    }

    private void StartTransition(int target)
    {
        if (target == _photoIndex)
        {
            return;
        }
        _prevPhotoIndex = _photoIndex;
        _photoIndex = target;
        _fadeAlpha = 0f;
    }

    private void ManualNavigate(int dir, int photoCount)
    {
        _autoEnabled = false;
        _autoTimer = 0f;
        StartTransition((_photoIndex + dir + photoCount) % photoCount);
    }


    private void DrawAbout(float winW, ImDrawListPtr dl)
    {
        if (string.IsNullOrWhiteSpace(_profile!.Bio))
        {
            return;
        }
        ImGui.SetCursorPosX(PadX);
        using (UiFonts.H3?.Push())
        {
            ImGui.TextColored(ThemeService.Current.Accent, Loc.T("profile.about"));
        }
        ImGui.Spacing();
        // DrawWrapped renders in the default text colour; push ImGuiCol.Text for the tint.
        var bioW = winW - PadX * 2f;
        ImGui.SetCursorPosX(PadX);
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.88f, 0.88f, 0.88f, 1f));
        AetherLove.Emoji.ParsedMessage.Parse(_profile.Bio).DrawWrapped("##aboutBio", bioW);
        ImGui.PopStyleColor();
        SpaceDivide(dl, winW);
    }

    private void DrawLookingFor(float winW, ImDrawListPtr dl)
    {
        var chips = LookingForLabelsFromMask(_profile!.LookingForMask);
        if (chips.Length == 0)
        {
            return;
        }
        SectionLabel(Loc.T("profile.looking_for"));
        DrawChips(chips, winW, dl);
        SpaceDivide(dl, winW);
    }

    private void DrawInfoSection(float winW, ImDrawListPtr dl)
    {
        var gender = GenderLabel(_profile!.Gender);
        var langs = LanguagesFromMask(_profile.LanguageMask);
        var tz = TimezoneLabel(_profile.TimezoneOffsetMinutes);
        var hasGender = !string.IsNullOrEmpty(gender);
        var hasLangs = langs.Count > 0;
        var hasTz = !string.IsNullOrEmpty(tz);

        if (!hasGender && !hasLangs && !hasTz)
        {
            return;
        }

        SectionLabel(Loc.T("profile.info"));
        if (hasGender)
        {
            InfoRow(Loc.T("profile.gender"), gender);
        }
        if (hasLangs)
        {
            InfoRow(Loc.T("profile.languages"), string.Join(", ", langs));
        }
        if (hasTz)
        {
            InfoRow(Loc.T("profile.timezone"), tz);
        }
        SpaceDivide(dl, winW);
    }

    private void DrawFavoriteJob(float winW, ImDrawListPtr dl)
    {
        if (_profile!.FavoriteJob == Job.None)
        {
            return;
        }
        SectionLabel(Loc.T("profile.favourite_job"));

        var icon = InferJobRole(_profile.FavoriteJob) switch
        {
            JobRole.Healer => FontAwesomeIcon.Heart,
            JobRole.Tank => FontAwesomeIcon.Shield,
            JobRole.Melee => FontAwesomeIcon.Bolt,
            JobRole.Ranged => FontAwesomeIcon.Crosshairs,
            JobRole.Caster => FontAwesomeIcon.Fire,
            JobRole.Crafter => FontAwesomeIcon.Hammer,
            JobRole.Gatherer => FontAwesomeIcon.Leaf,
            _ => FontAwesomeIcon.Gamepad,
        };

        IconTextRow(icon, JobLabel(_profile.FavoriteJob), ThemeService.Current.AccentU32, dl, winW);
        SpaceDivide(dl, winW);
    }

    private void DrawFavoriteLocation(float winW, ImDrawListPtr dl)
    {
        if (string.IsNullOrEmpty(_profile!.FavoriteLocationName))
        {
            return;
        }
        SectionLabel(Loc.T("profile.favourite_location"));
        IconTextRow(FontAwesomeIcon.MapMarkerAlt, _profile.FavoriteLocationName,
            ThemeService.Current.AccentU32, dl, winW);
        SpaceDivide(dl, winW);
    }

    private void DrawFavoriteExpansion(float winW, ImDrawListPtr dl)
    {
        if (_profile!.FavoriteExpansion == Expansion.None)
        {
            return;
        }
        SectionLabel(Loc.T("profile.favourite_expansion"));
        IconTextRow(FontAwesomeIcon.Star, ExpansionLabel(_profile.FavoriteExpansion),
            0xFFFFD700u, dl, winW);
        SpaceDivide(dl, winW);
    }

    private void DrawMusicLinks(float winW, ImDrawListPtr dl)
    {
        var p = _profile!;
        var hasAny = !string.IsNullOrEmpty(p.SpotifyTrackId)
            || !string.IsNullOrEmpty(p.SoundCloudUrl)
            || !string.IsNullOrEmpty(p.AppleMusicUrl)
            || !string.IsNullOrEmpty(p.YouTubeMusicUrl);
        if (!hasAny)
        {
            return;
        }

        SectionLabel(Loc.T("profile.favourite_song"));

        // Dalamud's FontAwesome has no brand glyphs, so every pill uses the generic music note.
        DrawMusicPill(winW, dl, "sp", p.SpotifyTrackId, p.SpotifyTrackName,
            UiColors.SpotifyGreen, UiColors.SpotifyGreenHover, FontAwesomeIcon.Music, isSpotifyId: true);
        DrawMusicPill(winW, dl, "sc", p.SoundCloudUrl, p.SoundCloudName,
            UiColors.SoundCloudOrange, UiColors.SoundCloudOrangeHover, FontAwesomeIcon.Music, isSpotifyId: false);
        DrawMusicPill(winW, dl, "am", p.AppleMusicUrl, p.AppleMusicName,
            UiColors.AppleMusicPink, UiColors.AppleMusicPinkHover, FontAwesomeIcon.Music, isSpotifyId: false);
        DrawMusicPill(winW, dl, "yt", p.YouTubeMusicUrl, p.YouTubeMusicName,
            UiColors.YouTubeRed, UiColors.YouTubeRedHover, FontAwesomeIcon.Music, isSpotifyId: false);

        SpaceDivide(dl, winW);
    }

    /// <summary>Draws one clickable brand-coloured "favourite song" pill; no-ops when the ref is empty.
    /// Spotify stores a bare track id (rebuilt into a URL); the other providers store a full URL.</summary>
    private void DrawMusicPill(float winW, ImDrawListPtr dl, string idSuffix, string? rawRef, string? name,
        uint baseCol, uint hoverCol, FontAwesomeIcon icon, bool isSpotifyId)
    {
        if (string.IsNullOrEmpty(rawRef))
        {
            return;
        }

        const string spotifyTrackPrefix = "https://open.spotify.com/track/";
        var url = rawRef.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? rawRef
            : (isSpotifyId ? spotifyTrackPrefix + rawRef : rawRef);

        var fullLabel = !string.IsNullOrEmpty(name)
            ? name
            : (rawRef.Length > 24 ? rawRef[..24] + "…" : rawRef);

        ImGui.SetCursorPosX(PadX);
        var pillTL = ImGui.GetCursorScreenPos();

        ImGui.PushFont(Plugin.PluginInterface.UiBuilder.FontIcon);
        var iconStr = icon.ToIconString();
        var iconW = ImGui.CalcTextSize(iconStr).X;
        ImGui.PopFont();

        var gap = Px(8f);
        // Keep the pill inside the card: clip the label to the remaining width with a trailing ellipsis.
        var maxTextW = (winW - PadX * 2f) - (ChipPadX * 2f + iconW + gap);
        var display = TruncateToWidth(fullLabel, maxTextW);

        var textSz = ImGui.CalcTextSize(display);
        var pillW = ChipPadX + iconW + gap + textSz.X + ChipPadX;
        var pillH = textSz.Y + ChipPadY * 2f;
        var pillBR = pillTL + new Vector2(pillW, pillH);

        ImGui.InvisibleButton($"##music_{idSuffix}", new Vector2(pillW, pillH));
        var hovered = ImGui.IsItemHovered();
        var clicked = ImGui.IsItemClicked();

        var pillCol = hovered ? hoverCol : baseCol;
        dl.AddRectFilled(pillTL, pillBR, pillCol, pillH * 0.5f);

        ImGui.PushFont(Plugin.PluginInterface.UiBuilder.FontIcon);
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
            pillTL + new Vector2(ChipPadX, ChipPadY), 0xFFFFFFFF, iconStr);
        ImGui.PopFont();

        dl.AddText(pillTL + new Vector2(ChipPadX + iconW + gap, ChipPadY), 0xFFFFFFFF, display);

        if (hovered)
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(Px(300f));
            ImGui.TextUnformatted(fullLabel);
            ImGui.PopTextWrapPos();
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), Loc.T("music.open_tooltip"));
            ImGui.EndTooltip();
        }

        if (clicked)
        {
            try
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(url)
                    { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "[ProfileScreen] Failed to open music URL.");
            }
        }

        ImGui.Spacing();
    }

    /// <summary>Trims text with a trailing ellipsis so it fits within <paramref name="maxWidth"/> px at the current font.</summary>
    private static string TruncateToWidth(string text, float maxWidth)
    {
        if (string.IsNullOrEmpty(text) || ImGui.CalcTextSize(text).X <= maxWidth)
        {
            return text;
        }
        const string ellipsis = "…";
        var ellipsisW = ImGui.CalcTextSize(ellipsis).X;
        int lo = 0, hi = text.Length;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (ImGui.CalcTextSize(text[..mid]).X + ellipsisW <= maxWidth)
            {
                lo = mid;
            }
            else
            {
                hi = mid - 1;
            }
        }
        return lo <= 0 ? ellipsis : text[..lo].TrimEnd() + ellipsis;
    }

    private void DrawFavoriteMovie(float winW, ImDrawListPtr dl)
    {
        if (string.IsNullOrEmpty(_profile!.FavoriteMovie))
        {
            return;
        }
        SectionLabel(Loc.T("profile.favourite_movie"));
        IconTextRow(FontAwesomeIcon.Film, _profile.FavoriteMovie,
            ThemeService.Current.AccentU32, dl, winW);
        SpaceDivide(dl, winW);
    }

    private void DrawFavoriteAnime(float winW, ImDrawListPtr dl)
    {
        if (string.IsNullOrEmpty(_profile!.FavoriteAnime))
        {
            return;
        }
        SectionLabel(Loc.T("profile.favourite_anime"));
        IconTextRow(FontAwesomeIcon.Tv, _profile.FavoriteAnime,
            ThemeService.Current.AccentU32, dl, winW);
        SpaceDivide(dl, winW);
    }

    private void DrawFavoriteFFCharacter(float winW, ImDrawListPtr dl)
    {
        if (string.IsNullOrEmpty(_profile!.FavoriteFFCharacter))
        {
            return;
        }
        SectionLabel(Loc.T("profile.favourite_ff_character"));
        IconTextRow(FontAwesomeIcon.Star, _profile.FavoriteFFCharacter,
            0xFFFFD700u, dl, winW);
        SpaceDivide(dl, winW);
    }

    private void DrawAvailabilityGraph(string label, int hoursMask, float winW, ImDrawListPtr dl)
    {
        if (hoursMask == 0)
        {
            return;
        }
        SectionLabel(label);

        var GraphH = Px(38f);
        var LabelH = Px(16f);
        var graphW = winW - PadX * 2f;
        var barW = graphW / 24f;

        ImGui.SetCursorPosX(PadX);
        var graphTL = ImGui.GetCursorScreenPos();

        for (var h = 0; h < 24; h++)
        {
            var active = (hoursMask & (1 << h)) != 0;
            var x0 = graphTL.X + h * barW + Px(1.5f);
            var x1 = graphTL.X + (h + 1) * barW - Px(1.5f);
            dl.AddRectFilled(
                new Vector2(x0, graphTL.Y),
                new Vector2(x1, graphTL.Y + GraphH),
                active ? ThemeService.Current.AccentU32 : GraphInactive, Px(3f));
        }

        var labelFontSz = ImGui.GetFontSize() * 0.85f;
        foreach (var h in new[] { 0, 6, 12, 18 })
        {
            var lx = graphTL.X + h * barW;
            dl.AddText(ImGui.GetFont(), labelFontSz,
                new Vector2(lx + Px(1f), graphTL.Y + GraphH + Px(4f)),
                TextSecondary, $"{h:D2}:00");
        }

        ImGui.Dummy(new Vector2(graphW, GraphH + LabelH + Px(4f)));
        SpaceDivide(dl, winW);
    }

    private void DrawSyncTool(float winW, ImDrawListPtr dl)
    {
        var chips = SyncToolLabelsFromMask(_profile!.SyncTool);
        if (chips.Length == 0)
        {
            return;
        }
        SectionLabel(Loc.T("profile.sync_tool"));
        DrawChips(chips, winW, dl);
        SpaceDivide(dl, winW);
    }


    private void DrawBackPill(Vector2 pos, ImDrawListPtr dl)
    {
        if (_source == ProfileSource.Self)
        {
            return;
        }

        var PadX = Px(11f);
        var PadY = Px(7f);
        var IconGap = Px(7f);

        var t = ThemeService.Current;

        // A profile opened from a chat returns to that chat; everywhere else returns to the swipe deck.
        var isChat = _source == ProfileSource.Chat;
        var backTarget = isChat ? Screen.Chat : Screen.Deck;
        var backTooltip = isChat ? Loc.T("profile.back_to_chat") : Loc.T("profile.back_to_swiping");

        ImGui.PushFont(Plugin.PluginInterface.UiBuilder.FontIcon);
        var backStr = FontAwesomeIcon.ArrowLeft.ToIconString();
        var deckStr = (isChat ? FontAwesomeIcon.Comment : FontAwesomeIcon.LayerGroup).ToIconString();
        var backSz = ImGui.CalcTextSize(backStr);
        var deckSz = ImGui.CalcTextSize(deckStr);
        ImGui.PopFont();

        var pillW = PadX + backSz.X + IconGap + deckSz.X + PadX;
        var pillH = MathF.Max(backSz.Y, deckSz.Y) + PadY * 2f;
        var pillBR = pos + new Vector2(pillW, pillH);

        ImGui.SetCursorScreenPos(pos);
        ImGui.InvisibleButton("##backPill", new Vector2(pillW, pillH));
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            ImGui.SetTooltip(backTooltip);
        }
        if (ImGui.IsItemClicked())
        {
            _router.Navigate(backTarget);
        }

        var tmod = _backPillPulseTimer % 4f;
        var pulse = tmod < 0.5f
            ? MathF.Sin(tmod * MathF.PI / 0.5f)
            : 0f;

        var baseBg = hovered ? t.ButtonHovered : t.ButtonNormal;
        var blendBg = Vector4.Lerp(baseBg, t.AccentLight, pulse * 0.55f);
        dl.AddRectFilled(pos, pillBR, ImGui.ColorConvertFloat4ToU32(blendBg), pillH * 0.5f);

        if (pulse > 0.05f)
        {
            var glowAlpha = (uint)(pulse * 80f) << 24;
            dl.AddRect(pos, pillBR, glowAlpha | (ThemeService.Current.AccentU32 & 0x00FFFFFF),
                pillH * 0.5f, ImDrawFlags.None, 2f);
        }

        var baseIconAlpha = hovered ? 1.0f : 0.80f;
        var finalIconAlpha = MathF.Min(1f, baseIconAlpha + pulse * 0.20f);
        var iconCol = ((uint)(finalIconAlpha * 255f) << 24) | 0x00FFFFFF;
        var iconY = pos.Y + PadY;
        ImGui.PushFont(Plugin.PluginInterface.UiBuilder.FontIcon);
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
            new Vector2(pos.X + PadX, iconY),
            iconCol, backStr);
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
            new Vector2(pos.X + PadX + backSz.X + IconGap, iconY),
            iconCol, deckStr);
        ImGui.PopFont();
    }


    /// <summary>Warning triangle in the photo header's top-right corner; opens the report modal.</summary>
    private void DrawReportTriangleButton(Vector2 photoTL, Vector2 photoSz, ImDrawListPtr dl)
    {
        var BtnSize = Px(36f);
        var Margin = Px(14f);

        var saveCursor = ImGui.GetCursorScreenPos();

        var btnTL = new Vector2(
            photoTL.X + photoSz.X - BtnSize - Margin,
            photoTL.Y + Margin);

        ImGui.SetCursorScreenPos(btnTL);
        ImGui.InvisibleButton("##reportTriangle", new Vector2(BtnSize, BtnSize));

        var hovered = ImGui.IsItemHovered();
        var clicked = ImGui.IsItemClicked();

        dl.AddCircleFilled(
            new Vector2(btnTL.X + BtnSize * 0.5f, btnTL.Y + BtnSize * 0.5f),
            BtnSize * 0.5f,
            hovered ? 0x99000000u : 0x66000000u);

        var iconColor = hovered ? 0xFF36C8FCu : 0xFF14A8E2u;
        ImGui.PushFont(Plugin.PluginInterface.UiBuilder.FontIcon);
        var icon = FontAwesomeIcon.ExclamationTriangle.ToIconString();
        var iconSz = ImGui.CalcTextSize(icon);
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
            new Vector2(
                btnTL.X + (BtnSize - iconSz.X) * 0.5f,
                btnTL.Y + (BtnSize - iconSz.Y) * 0.5f),
            iconColor, icon);
        ImGui.PopFont();

        if (hovered)
        {
            ImGui.SetTooltip(Loc.T("profile.report_profile"));
        }
        if (clicked)
        {
            _reportPendingOpen = true;
            _reportError = null;
            _reportSubmittedTimer = 0f;
        }

        ImGui.SetCursorScreenPos(saveCursor);
    }

    private void DrawReportBody(float availW)
    {
        if (_reportedThisProfile)
        {
            DrawReportSuccessView();
        }
        else
        {
            DrawReportFormView(ThemeService.Current);
        }
    }

    private void DrawReportFormView(ThemeDefinition t)
    {
        var availW = ImGui.GetContentRegionAvail().X;

        using (UiFonts.H3?.Push())
        {
            var Title = Loc.T("profile.report_profile");
            var titleSz = ImGui.CalcTextSize(Title);
            ImGui.SetCursorPosX((availW - titleSz.X) * 0.5f);
            ImGui.TextColored(UiColors.Amber, Title);
        }

        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.Separator,
            new Vector4(UiColors.Amber.X, UiColors.Amber.Y, UiColors.Amber.Z, 0.35f));
        ImGui.Separator();
        ImGui.PopStyleColor();
        ImGui.Spacing();

        ImGui.PushTextWrapPos(0f);
        ImGui.TextColored(UiColors.Amber, Loc.T("profile.report_warning"));
        ImGui.PopTextWrapPos();
        ImGui.Spacing();
        ImGui.Spacing();

        ImGui.PushTextWrapPos(0f);
        ImGui.TextColored(new Vector4(0.82f, 0.82f, 0.82f, 1f),
            Loc.T("profile.report_prompt", _profile?.DisplayName ?? Loc.T("profile.this_profile")));
        ImGui.PopTextWrapPos();
        ImGui.Spacing();
        ImGui.SetNextItemWidth(availW);
        ImGui.InputTextMultiline("##profReportReason", ref _reportReason, 500,
            new Vector2(availW, Px(90f)));
        ImGui.Spacing();

        ImGui.Checkbox("##profChkAgree", ref _reportAgree);
        ImGui.SameLine(0f, Px(6f));
        ImGui.PushTextWrapPos(0f);
        ImGui.TextWrapped(Loc.T("profile.report_agree"));
        ImGui.PopTextWrapPos();
        ImGui.Spacing();

        if (_reportError is not null)
        {
            ImGui.PushTextWrapPos(0f);
            ImGui.TextColored(UiColors.Danger, _reportError);
            ImGui.PopTextWrapPos();
            ImGui.Spacing();
        }
        if (_reportSubmitting)
        {
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), Loc.T("profile.submitting"));
            ImGui.Spacing();
        }

        var BtnGap = Px(8f);
        var btnW = (availW - BtnGap) * 0.5f;
        var canSubmit = _reportAgree
                     && !string.IsNullOrWhiteSpace(_reportReason)
                     && !_reportSubmitting;

        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.22f, 0.22f, 0.22f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.38f, 0.38f, 0.38f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.14f, 0.14f, 0.14f, 1f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
        if (ImGui.Button($"{Loc.T("profile.cancel")}##profReportCancel", new Vector2(btnW, Px(32f))) && !_reportSubmitting)
        {
            Widgets.ModalHost.Instance?.Close();
        }
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);
        ImGui.SameLine(0f, BtnGap);

        if (!canSubmit)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.40f);
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3f, 0.3f, 0.3f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.3f, 0.3f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.3f, 0.3f, 0.3f, 1f));
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Button, t.ButtonNormal);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, t.ButtonHovered);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, t.ButtonActive);
        }
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
        if (ImGui.Button($"{Loc.T("profile.submit_report")}##profReportSubmit", new Vector2(btnW, Px(32f))) && canSubmit)
        {
            FireReport();
        }
        ImGui.PopStyleVar(canSubmit ? 1 : 2);
        ImGui.PopStyleColor(3);

        ImGui.Spacing();
    }

    private void DrawReportSuccessView()
    {
        var availW = ImGui.GetContentRegionAvail().X;

        ImGui.PushFont(Plugin.PluginInterface.UiBuilder.FontIconFixedWidth);
        ImGui.SetWindowFontScale(2.2f * UiScale.S);
        var icon = FontAwesomeIcon.CheckCircle.ToIconString();
        var iconSz = ImGui.CalcTextSize(icon);
        ImGui.SetCursorPosX((availW - iconSz.X) * 0.5f);
        ImGui.TextColored(UiColors.Success, icon);
        ImGui.SetWindowFontScale(1.0f);
        ImGui.PopFont();
        ImGui.Spacing();

        using (UiFonts.H3?.Push())
        {
            var Title = Loc.T("profile.report_submitted");
            var titleSz = ImGui.CalcTextSize(Title);
            ImGui.SetCursorPosX((availW - titleSz.X) * 0.5f);
            ImGui.TextColored(UiColors.Success, Title);
        }
        ImGui.Spacing();

        ImGui.PushStyleColor(ImGuiCol.Separator, UiColors.Success with { W = 0.35f });
        ImGui.Separator();
        ImGui.PopStyleColor();
        ImGui.Spacing();

        ImGui.PushTextWrapPos(0f);
        ImGui.TextColored(UiColors.Body,
            Loc.T("profile.report_thanks"));
        ImGui.PopTextWrapPos();
        ImGui.Spacing();
        ImGui.Spacing();

        var secondsLeft = Math.Max(0, (int)Math.Ceiling(_reportSubmittedTimer));
        var countdown = secondsLeft <= 1
            ? Loc.T("profile.closing")
            : Loc.T("profile.closing_in", secondsLeft);
        var countSz = ImGui.CalcTextSize(countdown);
        ImGui.SetCursorPosX((availW - countSz.X) * 0.5f);
        ImGui.TextColored(new Vector4(0.65f, 0.65f, 0.65f, 1f), countdown);
        ImGui.Spacing();
        ImGui.Spacing();

        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.22f, 0.22f, 0.22f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.38f, 0.38f, 0.38f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.14f, 0.14f, 0.14f, 1f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
        if (ImGui.Button($"{Loc.T("profile.close")}##profReportCloseNow", new Vector2(ImGui.GetContentRegionAvail().X, Px(30f))))
        {
            _reportSubmittedTimer = 0f;
            Widgets.ModalHost.Instance?.Close();
        }
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);

        if (_reportSubmittedTimer <= 0f)
        {
            Widgets.ModalHost.Instance?.Close();
        }

        ImGui.Spacing();
    }

    private void FireReport()
    {
        if (_profile is null)
        {
            return;
        }
        var peerId = _profile.ProfileId;
        var reason = _reportReason;

        _reportSubmitting = true;
        _reportError = null;
        var ct = _cts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                var req = new ReportUserRequest(
                    ReportedProfileId: peerId,
                    Reason: reason,
                    IncludeConversation: false,
                    ConversationSnapshot: null);
                await _hub.ReportUserAsync(req, ct).ConfigureAwait(false);
                if (ct.IsCancellationRequested)
                {
                    return;
                }
                _reportSubmitting = false;
                _reportedThisProfile = true;
                _reportSubmittedTimer = ReportAutoCloseSeconds;
                _reportReason = string.Empty;
                _reportAgree = false;
            }
            catch (OperationCanceledException) { _reportSubmitting = false; }
            catch (Exception ex)
            {
                if (ct.IsCancellationRequested) { _reportSubmitting = false; return; }
                _reportError = HubErrorText.Localize(ex);
                _reportSubmitting = false;
                Plugin.Log.Warning(ex, "[ProfileScreen] ReportUserAsync failed.");
            }
        }, ct);
    }

    private static void SectionLabel(string title)
    {
        ImGui.SetCursorPosX(PadX);
        using (UiFonts.H3?.Push())
        {
            ImGui.TextColored(ThemeService.Current.Accent, title);
        }
        ImGui.Spacing();
    }

    private static void InfoRow(string label, string? value)
    {
        ImGui.SetCursorPosX(PadX);
        ImGui.TextColored(UiColors.Muted, label);
        ImGui.SameLine(Px(145f));
        ImGui.TextColored(new Vector4(0.92f, 0.92f, 0.92f, 1f), value ?? "");
    }

    private static void IconTextRow(FontAwesomeIcon icon, string text, uint iconColor,
        ImDrawListPtr dl, float winW)
    {
        ImGui.SetCursorPosX(PadX);
        var pos = ImGui.GetCursorScreenPos();

        ImGui.PushFont(Plugin.PluginInterface.UiBuilder.FontIcon);
        var iconStr = icon.ToIconString();
        var iconSz = ImGui.CalcTextSize(iconStr);
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), pos + Px(0f, 2f), iconColor, iconStr);
        ImGui.PopFont();

        // The icon is on the draw list; draw the text as a real wrapped widget after it so long values wrap
        // within the card and ImGui advances the cursor for the (possibly multi-line) height.
        ImGui.SetCursorPosX(PadX + iconSz.X + Px(10f));
        ImGui.PushTextWrapPos(winW - PadX);
        ImGui.TextColored(ImGui.ColorConvertU32ToFloat4(TextPrimary), text);
        ImGui.PopTextWrapPos();
        ImGui.Spacing();
    }

    private static void SpaceDivide(ImDrawListPtr dl, float winW)
    {
        ImGui.Spacing();
        ImGui.Spacing();
        Divider(dl, winW);
    }

    private static void Divider(ImDrawListPtr dl, float winW)
    {
        ImGui.SetCursorPosX(PadX);
        var p = ImGui.GetCursorScreenPos();
        var endX = p.X + winW - PadX * 2f;
        dl.AddLine(p, new Vector2(endX, p.Y), UiColors.Divider, 1f);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + Px(6f));
        ImGui.Spacing();
    }

    private static void DrawChips(string[] tags, float windowW, ImDrawListPtr dl)
    {
        var curX = PadX;
        var curY = ImGui.GetCursorPosY();
        var chipH = ImGui.CalcTextSize("A").Y + ChipPadY * 2f;
        var maxX = windowW - PadX;

        foreach (var tag in tags)
        {
            var chipW = ImGui.CalcTextSize(tag).X + ChipPadX * 2f;
            if (curX + chipW > maxX && curX > PadX)
            {
                curX = PadX;
                curY += chipH + Px(6f);
            }

            ImGui.SetCursorPos(new Vector2(curX, curY));
            var scrTL = ImGui.GetCursorScreenPos();
            var scrBR = scrTL + new Vector2(chipW, chipH);

            dl.AddRectFilled(scrTL, scrBR, ThemeService.Current.ChipFillU32, chipH * 0.5f);
            dl.AddRect(scrTL, scrBR, ThemeService.Current.ChipBorderU32, chipH * 0.5f, ImDrawFlags.None, 1.5f);
            dl.AddText(scrTL + new Vector2(ChipPadX, ChipPadY), 0xFFFFFFFF, tag);

            ImGui.InvisibleButton($"##chip_{tag}", new Vector2(chipW, chipH));
            curX += chipW + Px(8f);
        }

        ImGui.SetCursorPosY(curY + chipH + Px(4f));
    }

    /// <summary>Slot 0 is the avatar, slots 1..4 are scroller photos; avatar renders solo if alone.</summary>
    private int CountPhotos()
    {
        if (_profile is null)
        {
            return 0;
        }
        var count = 0;
        for (var i = 1; i <= 4; i++)
        {
            if (_photoTextures.ContainsKey(i))
            {
                count++;
            }
        }
        if (count == 0 && _photoTextures.ContainsKey(0))
        {
            count = 1;
        }
        return count;
    }

    private ISharedImmediateTexture? GetPhotoTextureAt(int scrollerIndex)
    {
        var order = GetPhotoOrderAt(scrollerIndex);
        if (order < 0)
        {
            return null;
        }
        _photoTextures.TryGetValue(order, out var tex);
        return tex;
    }

    /// <summary>Resolves the scroller slot index to a photo Order, or -1 if out of range.</summary>
    private int GetPhotoOrderAt(int scrollerIndex)
    {
        var portraitOrders = new List<int>(4);
        for (var i = 1; i <= 4; i++)
        {
            if (_photoTextures.ContainsKey(i))
            {
                portraitOrders.Add(i);
            }
        }
        if (portraitOrders.Count > 0)
        {
            if (scrollerIndex < 0 || scrollerIndex >= portraitOrders.Count)
            {
                return -1;
            }
            return portraitOrders[scrollerIndex];
        }
        return _photoTextures.ContainsKey(0) ? 0 : -1;
    }

    private bool IsPhotoNsfw(int order) =>
        _profile is not null
        && _profile.Photos.Any(p => p.Order == order && p.IsNsfw);

    /// <summary>True when an NSFW extra photo should be blurred until the viewer reveals it.</summary>
    private bool ShouldBlur(int order) =>
        Plugin.Configuration.AlwaysBlurNsfw
        && _source != ProfileSource.Self
        && IsPhotoNsfw(order)
        && !_revealedNsfw.Contains((_profile!.ProfileId, order));

    private static uint AvatarFallbackColor(Guid profileId)
        => UiColors.AvatarFallback;

    private static string[] SyncToolLabelsFromMask(SyncTool mask)
    {
        var result = new List<string>();
        for (var i = 0; i < SyncToolValues.Length; i++)
        {
            if (mask.HasFlag(SyncToolValues[i]))
            {
                result.Add(SyncToolLabels[i]);
            }
        }
        return result.ToArray();
    }

    private static string JobLabel(Job j)
    {
        var idx = Array.IndexOf(JobValues, j);
        var labels = GetJobLabels();
        return idx >= 0 && idx < labels.Count ? labels[idx] : j.ToString();
    }

    private static List<string> LanguagesFromMask(Language mask)
    {
        var result = new List<string>();
        for (var i = 0; i < LanguageValues.Length; i++)
        {
            if (mask.HasFlag(LanguageValues[i]))
            {
                result.Add(Languages[i]);
            }
        }
        return result;
    }

    private static string[] LookingForLabelsFromMask(LookingFor mask)
    {
        var result = new List<string>();
        for (var i = 0; i < LookingForValues.Length; i++)
        {
            if (mask.HasFlag(LookingForValues[i]))
            {
                result.Add(LookingForLabels[i]);
            }
        }
        return result.ToArray();
    }

    private static string TimezoneLabel(int offsetMinutes)
    {
        string utcLabel;
        if (offsetMinutes == 0)
        {
            utcLabel = "UTC";
        }
        else
        {
            var sign = offsetMinutes >= 0 ? "+" : "-";
            var abs = Math.Abs(offsetMinutes);
            var hours = abs / 60;
            var minutes = abs % 60;
            utcLabel = minutes == 0 ? $"UTC{sign}{hours}" : $"UTC{sign}{hours}:{minutes:D2}";
        }

        var localNow = DateTime.UtcNow.AddMinutes(offsetMinutes);
        return Loc.T("profile.timezone_value", utcLabel, localNow.ToString("HH:mm"));
    }

    private static JobRole InferJobRole(Job job) => job switch
    {
        Job.Paladin or Job.Warrior or Job.DarkKnight or Job.Gunbreaker => JobRole.Tank,
        Job.WhiteMage or Job.Scholar or Job.Astrologian or Job.Sage => JobRole.Healer,
        Job.Monk or Job.Dragoon or Job.Ninja or Job.Samurai or Job.Reaper or Job.Viper => JobRole.Melee,
        Job.Bard or Job.Machinist or Job.Dancer => JobRole.Ranged,
        Job.BlackMage or Job.Summoner or Job.RedMage or Job.Pictomancer or Job.BlueMage => JobRole.Caster,
        Job.Carpenter or Job.Blacksmith or Job.Armorer or Job.Goldsmith
            or Job.Leatherworker or Job.Weaver or Job.Alchemist or Job.Culinarian => JobRole.Crafter,
        Job.Miner or Job.Botanist or Job.Fisher => JobRole.Gatherer,
        _ => JobRole.None,
    };
}

public enum JobRole { None, Healer, Tank, Melee, Ranged, Caster, Crafter, Gatherer }
