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

/// <summary>The caller's own profile hub: header with handle, bio, counts and joined date, the
/// Posts/Replies/Media tabs, and the bookmarks entry.</summary>
internal sealed class ProfileScreen
{
    private static readonly YapperProfileTab[] Tabs =
        [YapperProfileTab.Posts, YapperProfileTab.Replies, YapperProfileTab.Liked, YapperProfileTab.Media];

    private readonly IYapperHost _host;
    private readonly YapperStore _store;
    private readonly YapperMediaCache _mediaCache;
    private readonly Func<YapperMyProfileDto?> _me;
    private readonly Action _refresh;
    private readonly Action _openBookmarks;
    private readonly Action _openSettings;
    private readonly Action<bool> _openFollowList;
    private readonly ImageSourceSheet _imageSheet;
    private readonly Action<Action<string>> _pickFromPhotos;
    private readonly Action<YapDto> _openYap;
    private readonly FeedPane[] _panes;
    private readonly PinnedYapSlot _pinnedSlot;
    private int _tab;

    public ProfileScreen(IYapperHost host, YapperStore store, YapperMediaCache mediaCache,
        Func<YapperMyProfileDto?> me, Action refresh, Action openBookmarks, Action openSettings,
        Action<bool> openFollowList, ImageSourceSheet imageSheet, Action<Action<string>> pickFromPhotos,
        Action<YapDto> openYap)
    {
        _openYap = openYap;
        _host = host;
        _store = store;
        _mediaCache = mediaCache;
        _me = me;
        _refresh = refresh;
        _openBookmarks = openBookmarks;
        _openSettings = openSettings;
        _openFollowList = openFollowList;
        _imageSheet = imageSheet;
        _pickFromPhotos = pickFromPhotos;
        _panes = new FeedPane[Tabs.Length];
        for (var i = 0; i < Tabs.Length; i++)
        {
            var tab = Tabs[i];
            _panes[i] = new FeedPane(store,
                cursor => LoadTabAsync(tab, cursor), _ => { });
        }
        _pinnedSlot = new PinnedYapSlot(host, store);
    }

    public FeedPane PostsPane => _panes[0];

    /// <summary>Re-pulls every loaded tab so posts made since the last visit appear.</summary>
    public void OnShow()
    {
        foreach (var pane in _panes)
        {
            if (pane.LoadedOnce)
            {
                pane.Refresh();
            }
        }
    }

    /// <summary>Routes a fresh own yap into the tabs it belongs to.</summary>
    public void NotifyPosted(YapDto dto)
    {
        var tab = dto.Kind == YapKind.Reply ? YapperProfileTab.Replies : YapperProfileTab.Posts;
        _panes[Array.IndexOf(Tabs, tab)].Prepend(dto.Id);
        if (dto.Media.Length > 0)
        {
            _panes[Array.IndexOf(Tabs, YapperProfileTab.Media)].Prepend(dto.Id);
        }
    }

    private async Task<YapPageDto> LoadTabAsync(YapperProfileTab tab, DateTimeOffset? cursor)
    {
        var me = _me() ?? throw new InvalidOperationException("No profile.");
        return await _host.GetProfileYapsAsync(me.ProfileId, tab, cursor).ConfigureAwait(false);
    }

    public void Draw(OsAppContext ctx, YapCard card)
    {
        var me = _me();
        if (me is null)
        {
            _refresh();
            return;
        }

        var winW = ImGui.GetWindowSize().X;
        var pad = Px(18f);

        PushScrollbarStyle();
        using var scroll = ImRaii.Child("##yapProfileScroll", new Vector2(0f, 0f), false);
        if (!scroll.Success)
        {
            PopScrollbarStyle();
            return;
        }

        DrawBannerHeader(ctx, me, winW, pad);

        ImGui.SetCursorPosX(pad);
        using (ctx.TitleFont?.Push())
        {
            ImGui.TextUnformatted(me.DisplayName);
        }
        ImGui.SetCursorPosX(pad);
        ImGui.TextColored(new Vector4(1f, 1f, 1f, 0.55f), $"@{me.Handle}");

        if (!string.IsNullOrEmpty(me.Bio))
        {
            ImGui.Dummy(new Vector2(0f, Px(6f)));
            ImGui.SetCursorPosX(pad);
            AetherLove.Emoji.ParsedMessage.Parse(me.Bio).DrawWrapped("##yapMyBio", winW - pad * 2f);
        }

        ImGui.Dummy(new Vector2(0f, Px(6f)));
        ImGui.SetCursorPosX(pad);
        ImGui.TextColored(new Vector4(1f, 1f, 1f, 0.45f),
            string.Format(Loc.T("os.yapper_joined"), me.JoinedAtUtc.ToLocalTime().ToString("MMMM yyyy")));
        ImGui.SetCursorPosX(pad);
        YapperUi.DrawStatLink("##yapMyFollowingStat", me.FollowingCount, Loc.T("os.yapper_following"),
            () => _openFollowList(false));
        ImGui.SameLine();
        YapperUi.DrawStatLink("##yapMyFollowersStat", me.FollowerCount, Loc.T("os.yapper_followers"),
            () => _openFollowList(true));

        ImGui.Dummy(new Vector2(0f, Px(10f)));
        DrawTabs(ctx, winW);
        if (Tabs[_tab] == YapperProfileTab.Media)
        {
            _panes[_tab].DrawMediaMosaic(ctx, _mediaCache, "os.yapper_profile_empty", _openYap);
        }
        else
        {
            if (Tabs[_tab] == YapperProfileTab.Posts)
            {
                _pinnedSlot.Draw(ctx, card, me.PinnedYapId);
                _panes[_tab].ExcludeId = me.PinnedYapId;
            }
            _panes[_tab].DrawCards(ctx, card, "os.yapper_profile_empty");
        }
        PopScrollbarStyle();
    }

    /// <summary>The X-style header: full-width banner (8:3), avatar straddling its bottom edge, and the
    /// ring action row (settings + bookmarks) to the right. Clicking the banner or avatar opens the
    /// forced-aspect pick-and-crop and uploads the result.</summary>
    private void DrawBannerHeader(OsAppContext ctx, YapperMyProfileDto me, float winW, float pad)
    {
        const float BannerAspect = 3f / 8f;
        var dl = ImGui.GetWindowDrawList();
        var bannerH = winW * BannerAspect;
        var origin = ImGui.GetCursorScreenPos();

        // Banner hit target first so it wins over nothing below; the avatar target follows and overlaps it.
        ImGui.SetCursorScreenPos(origin);
        if (ImGui.InvisibleButton("##yapBannerEdit", new Vector2(winW, bannerH)) && !_uploading)
        {
            PickAndUpload(ctx, banner: true, BannerAspect);
        }
        var bannerHovered = ImGui.IsItemHovered();
        if (bannerHovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        var bannerTex = me.Banner is { Length: > 0 } bannerBytes
            ? _mediaCache.GetInline("my_banner", bannerBytes)?.GetWrapOrDefault()
            : null;
        if (bannerTex is not null)
        {
            var (uv0, uv1) = CoverFitUvs(bannerTex.Width, bannerTex.Height, winW, bannerH);
            dl.AddImage(bannerTex.Handle, origin, origin + new Vector2(winW, bannerH), uv0, uv1);
        }
        else
        {
            dl.AddRectFilledMultiColor(origin, origin + new Vector2(winW, bannerH),
                ImGui.GetColorU32(ctx.Theme.Accent with { W = 0.45f }),
                ImGui.GetColorU32(ctx.Theme.Accent with { W = 0.45f }),
                ImGui.GetColorU32(ctx.Theme.Accent with { W = 0.15f }),
                ImGui.GetColorU32(ctx.Theme.Accent with { W = 0.15f }));
        }
        if (bannerHovered || bannerTex is null)
        {
            IconDraw.AddCentered(dl, FontAwesomeIcon.Camera, Px(16f),
                origin + new Vector2(winW * 0.5f, bannerH * 0.5f),
                ImGui.GetColorU32(new Vector4(1f, 1f, 1f, bannerHovered ? 0.85f : 0.4f)));
        }

        var avatarR = Px(34f);
        var avatarCenter = origin + new Vector2(pad + avatarR, bannerH);
        ImGui.SetCursorScreenPos(avatarCenter - new Vector2(avatarR, avatarR));
        if (ImGui.InvisibleButton("##yapAvatarEdit", new Vector2(avatarR * 2f, avatarR * 2f)) && !_uploading)
        {
            PickAndUpload(ctx, banner: false, aspect: 1f);
        }
        var avatarHovered = ImGui.IsItemHovered();
        if (avatarHovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }
        dl.AddCircleFilled(avatarCenter, avatarR + Px(3f), ImGui.GetColorU32(new Vector4(0.06f, 0.07f, 0.10f, 1f)));
        var avatarTex = me.Avatar is { Length: > 0 } avatarBytes
            ? _mediaCache.GetAvatar(me.ProfileId, avatarBytes)?.GetWrapOrDefault()
            : null;
        if (avatarTex is not null)
        {
            dl.AddImageRounded(avatarTex.Handle, avatarCenter - new Vector2(avatarR, avatarR),
                avatarCenter + new Vector2(avatarR, avatarR), Vector2.Zero, Vector2.One, 0xFFFFFFFFu, avatarR);
        }
        else
        {
            dl.AddCircleFilled(avatarCenter, avatarR, ImGui.GetColorU32(ctx.Theme.Accent with { W = 0.35f }));
        }
        if (avatarHovered)
        {
            IconDraw.AddCentered(dl, FontAwesomeIcon.Camera, Px(14f), avatarCenter,
                ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.9f)));
        }

        // The ring action row right of the avatar, under the banner (X layout).
        var ringY = bannerH + Px(10f);
        DrawRingButton(dl, origin + new Vector2(winW - pad - Px(28f), ringY), FontAwesomeIcon.Bookmark,
            "##yapBookmarksOpen", _openBookmarks);
        DrawRingButton(dl, origin + new Vector2(winW - pad - Px(64f), ringY), FontAwesomeIcon.Cog,
            "##yapSettingsOpen", _openSettings);

        ImGui.SetCursorScreenPos(origin + new Vector2(0f, bannerH + avatarR + Px(10f)));
    }

    private static void DrawRingButton(ImDrawListPtr dl, Vector2 tl, FontAwesomeIcon icon, string id, Action onClick)
    {
        ImGui.SetCursorScreenPos(tl);
        if (ImGui.InvisibleButton(id, new Vector2(Px(28f), Px(28f))))
        {
            onClick();
        }
        HandOnHover();
        var center = tl + new Vector2(Px(14f), Px(14f));
        dl.AddCircle(center, Px(14f), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.35f)), 32, Px(1.2f));
        IconDraw.AddCentered(dl, icon, Px(13f), center,
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, ImGui.IsItemHovered() ? 1f : 0.75f)));
    }

    private bool _uploading;
    private IImageEffects? _effects;

    private void PickAndUpload(OsAppContext ctx, bool banner, float aspect)
    {
        var caps = ctx.Capabilities;
        _effects = caps.Effects;
        var request = new AetherOS.Sdk.ImageCropRequest(
            Loc.T("os.yapper_pick_image"), "Images{.png,.jpg,.jpeg,.webp}",
            Loc.T(banner ? "os.yapper_crop_banner" : "os.yapper_crop_avatar"),
            aspect,
            banner ? 600 : 100,
            banner ? 225 : 100);
        _imageSheet.Open(
            onSelfie: () => caps.Camera.Capture(
                new AetherOS.Sdk.CameraRequest(aspect, banner ? 600 : 100),
                shot => Upload(banner, shot.Path, shot.Crop)),
            onPhotos: () => _pickFromPhotos(path => caps.Images.CropFile(path, request,
                cropped => Upload(banner, cropped.Path, cropped.Crop))),
            onFile: () => caps.Images.PickAndCrop(request,
                cropped => Upload(banner, cropped.Path, cropped.Crop)));
    }

    /// <summary>Oversized picks are downscaled host-side before upload (the server re-encodes the banner
    /// to 1500x500 and the avatar to 100x100 anyway), so a raw screenshot can never trip the server's
    /// input-size cap.</summary>
    private void Upload(bool banner, string path, Vector4 crop)
    {
        const int UploadMaxWidth = 1920;
        const int UploadMaxHeight = 1080;
        _uploading = true;
        if (_effects is not { } effects)
        {
            UploadPrepared(banner, path, crop);
            return;
        }
        effects.PrepareUpload(path, UploadMaxWidth, UploadMaxHeight, (prepared, scale) =>
        {
            if (prepared is null)
            {
                _uploading = false;
                return;
            }
            UploadPrepared(banner, prepared, crop * scale);
        });
    }

    private void UploadPrepared(bool banner, string path, Vector4 crop)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var bytes = System.IO.File.ReadAllBytes(path);
                var upload = new AetherLove.Shared.Profile.PhotoUploadDto(
                    Convert.ToBase64String(bytes),
                    (int)crop.X, (int)crop.Y, (int)crop.Z, (int)crop.W,
                    false);
                if (banner)
                {
                    await _host.SetBannerAsync(upload).ConfigureAwait(false);
                }
                else
                {
                    await _host.SetAvatarAsync(upload).ConfigureAwait(false);
                }
                _refresh();
            }
            catch (Exception)
            {
            }
            finally
            {
                _uploading = false;
            }
        });
    }

    private void DrawTabs(OsAppContext ctx, float winW)
    {
        var dl = ImGui.GetWindowDrawList();
        var tabH = Px(34f);
        var slotW = winW / Tabs.Length;
        var baseY = ImGui.GetCursorScreenPos().Y;
        for (var i = 0; i < Tabs.Length; i++)
        {
            var label = Loc.T(Tabs[i] switch
            {
                YapperProfileTab.Replies => "os.yapper_tab_replies",
                YapperProfileTab.Liked => "os.yapper_tab_liked",
                YapperProfileTab.Media => "os.yapper_tab_media",
                _ => "os.yapper_tab_posts",
            });
            var tl = new Vector2(ImGui.GetWindowPos().X + slotW * i, baseY);
            ImGui.SetCursorScreenPos(tl);
            if (ImGui.InvisibleButton($"##yapProfTab{i}", new Vector2(slotW, tabH)))
            {
                _tab = i;
            }
            HandOnHover();
            var active = _tab == i;
            var size = ImGui.CalcTextSize(label);
            dl.AddText(tl + new Vector2((slotW - size.X) * 0.5f, (tabH - size.Y) * 0.5f),
                ImGui.GetColorU32(new Vector4(1f, 1f, 1f, active ? 0.95f : 0.45f)), label);
            if (active)
            {
                dl.AddRectFilled(
                    tl + new Vector2(slotW * 0.5f - Px(20f), tabH - Px(2f)),
                    tl + new Vector2(slotW * 0.5f + Px(20f), tabH),
                    ImGui.GetColorU32(ctx.Theme.Accent), Px(1f));
            }
        }
        ImGui.SetCursorScreenPos(new Vector2(ImGui.GetWindowPos().X, baseY + tabH));
    }
}
