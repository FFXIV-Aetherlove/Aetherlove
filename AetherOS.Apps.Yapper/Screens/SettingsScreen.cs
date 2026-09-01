using System;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove.Services.Localization;
using AetherLove.Shared.Yapper;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Yapper.Screens;

/// <summary>Yapper settings as a hub of categories rather than one long scroll: profile, content rating,
/// notifications, muted and blocked, each its own page, plus deleting the profile. Pages slide in and out so
/// going a level down reads as movement. Doubles as the IAppSettings body.</summary>
internal sealed class SettingsScreen
{
    private const float PadX = 16f;

    /// <summary>How long a page takes to slide in, in seconds.</summary>
    private const double SlideSeconds = 0.20;

    private enum View { Hub, Profile, Rating, Notifications, Muted, Blocked, Deleted }

    private readonly IYapperHost _host;
    private readonly YapperMediaCache _mediaCache;
    private readonly Func<YapperMyProfileDto?> _me;
    private readonly Action<YapperMyProfileDto> _meUpdated;
    private readonly Action _back;
    private readonly Action _onDeleted;
    private readonly Action<string, string, string, bool, Action> _confirm;

    private View _view = View.Hub;
    private double _slideStartedAt;
    private float _slideFrom;

    private string _renameHandle = string.Empty;
    private volatile string? _renameError;
    private volatile bool _renaming;
    private string _editName = string.Empty;
    private string _editBio = string.Empty;
    private volatile string? _profileError;
    private volatile bool _profileSaving;
    private volatile YapperUserRowDto[]? _blocked;
    private volatile YapperUserRowDto[]? _muted;
    private volatile bool _listsLoading;

    /// <summary>Set on the first fetch attempt, success or not: the hub asks for the lists every frame
    /// until they arrive, and an offline account must not turn that into a request per frame.</summary>
    private volatile bool _listsTried;
    private volatile bool _deleting;
    private volatile string? _deleteError;
    /// <summary>The frame this screen last drew on. IAppSettings has no enter hook, so a gap here is
    /// the only signal that the OS Settings app opened this body afresh.</summary>
    private int _lastFrame = -2;
    private readonly AetherLove.Widgets.RingPickerUi _ringPicker = new();
    private bool _ringInit;

    public SettingsScreen(IYapperHost host, YapperMediaCache mediaCache,
        Func<YapperMyProfileDto?> me, Action<YapperMyProfileDto> meUpdated, Action back,
        Action<string, string, string, bool, Action> confirm, Action onDeleted)
    {
        _host = host;
        _mediaCache = mediaCache;
        _me = me;
        _meUpdated = meUpdated;
        _back = back;
        _confirm = confirm;
        _onDeleted = onDeleted;
    }

    public void OnShow()
    {
        _lastFrame = ImGui.GetFrameCount();
        _deleteError = null;
        if (_view != View.Deleted)
        {
            Navigate(View.Hub, forward: false);
        }
    }

    /// <summary>The in-app page (profile gear); its back pill returns to the profile.</summary>
    public void Draw(OsAppContext ctx) => DrawShell(ctx, _back, FontAwesomeIcon.User);

    /// <summary>The IAppSettings body, shown inside the OS Settings app.</summary>
    public void DrawSettings(OsAppContext ctx, Action? onBack) => DrawShell(ctx, onBack, FontAwesomeIcon.Cog);

    private void Navigate(View view, bool forward = true)
    {
        if (view == View.Profile)
        {
            var me = _me();
            _renameHandle = me?.Handle ?? string.Empty;
            _editName = me?.DisplayName ?? string.Empty;
            _editBio = me?.Bio ?? string.Empty;
            _renameError = null;
            _profileError = null;
            _ringInit = false;
        }
        if (view is View.Muted or View.Blocked)
        {
            RefreshLists();
        }
        _view = view;
        _slideFrom = forward ? 1f : -1f;
        _slideStartedAt = ImGui.GetTime();
    }

    private void DrawShell(OsAppContext ctx, Action? hostBack, FontAwesomeIcon backIcon)
    {
        var frame = ImGui.GetFrameCount();
        var reentered = frame - _lastFrame > 1;
        _lastFrame = frame;
        if (reentered && _view is not (View.Deleted or View.Hub))
        {
            Navigate(View.Hub, forward: false);
        }
        if (_view == View.Deleted)
        {
            DrawDeleted(ctx);
            return;
        }

        var winW = ImGui.GetWindowSize().X;
        var origin = ImGui.GetCursorScreenPos();
        var avail = ImGui.GetContentRegionAvail();

        // The whole page rides in a child so a slide is one offset rather than every row learning about it.
        var t = (float)Math.Clamp((ImGui.GetTime() - _slideStartedAt) / SlideSeconds, 0d, 1d);
        var eased = 1f - MathF.Pow(1f - t, 3f);
        var dx = _slideFrom * winW * (1f - eased);
        ImGui.SetCursorScreenPos(origin + new Vector2(dx, 0f));
        using var page = ImRaii.Child($"##yapSettPage{_view}", avail, false,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoBackground);
        if (!page)
        {
            return;
        }

        switch (_view)
        {
            case View.Hub:
                DrawHub(ctx, winW, hostBack, backIcon);
                break;
            case View.Profile:
                DrawPage(ctx, winW, Loc.T("os.yapper_settings_profile"), () => DrawProfilePage(ctx, winW));
                break;
            case View.Rating:
                DrawPage(ctx, winW, Loc.T("os.yapper_ob_rating_title"), () => DrawRatingPage(winW));
                break;
            case View.Notifications:
                DrawPage(ctx, winW, Loc.T("os.yapper_settings_notifs"), () => DrawNotificationsPage(winW));
                break;
            case View.Muted:
                DrawPage(ctx, winW, Loc.T("os.yapper_settings_muted"), () => DrawUserList(ctx, winW, _muted, unblock: false));
                break;
            case View.Blocked:
                DrawPage(ctx, winW, Loc.T("os.yapper_settings_blocked"), () => DrawUserList(ctx, winW, _blocked, unblock: true));
                break;
        }
    }

    private void DrawHub(OsAppContext ctx, float winW, Action? hostBack, FontAwesomeIcon backIcon)
    {
        var accent = ctx.Theme.Accent;
        ImGui.Spacing();
        ImGui.Spacing();
        if (hostBack is not null)
        {
            ImGui.SetCursorPosX(Px(PadX));
            if (DrawFloatingBackPill(ImGui.GetCursorScreenPos(), Loc.T("settings.back_arrow"), backIcon))
            {
                hostBack();
            }
            ImGui.Spacing();
        }
        DrawSubpageHeading(Loc.T("os.yapper_settings_title"), PadX);

        var me = _me();
        if (me is null)
        {
            ImGui.SetCursorPosX(Px(PadX));
            ImGui.TextColored(new Vector4(1f, 1f, 1f, 0.45f), Loc.T("os.yapper_loading"));
            return;
        }

        PushScrollbarStyle();
        using var scroll = ImRaii.Child("##yapSettHub", new Vector2(0f, ImGui.GetContentRegionAvail().Y), false);
        PopScrollbarStyle();
        if (!scroll)
        {
            return;
        }
        ImGui.Spacing();

        if (!_listsTried)
        {
            RefreshLists();
        }
        DrawIdentityCard(ctx, me, winW);
        ImGui.Spacing();
        ImGui.Spacing();

        DrawMenuCard("yapSett", winW, PadX, new System.Collections.Generic.List<MenuRow>
        {
            new(FontAwesomeIcon.IdBadge, accent, Loc.T("os.yapper_settings_profile"), 0, false,
                () => Navigate(View.Profile)),
            new(FontAwesomeIcon.EyeSlash, accent, Loc.T("os.yapper_ob_rating_title"), 0, false,
                () => Navigate(View.Rating)),
            new(FontAwesomeIcon.Bell, accent, Loc.T("os.yapper_settings_notifs"), 0, false,
                () => Navigate(View.Notifications)),
            new(FontAwesomeIcon.VolumeMute, accent, Loc.T("os.yapper_settings_muted"), _muted?.Length ?? 0, false,
                () => Navigate(View.Muted)),
            new(FontAwesomeIcon.Ban, accent, Loc.T("os.yapper_settings_blocked"), _blocked?.Length ?? 0, false,
                () => Navigate(View.Blocked)),
        });

        ImGui.Spacing();
        ImGui.Spacing();
        DrawSectionHeader(Loc.T("os.yapper_settings_danger"), PadX, DangerColor);
        ImGui.Spacing();
        DrawDeleteButton(winW);
        ImGui.SetCursorPosX(Px(PadX));
        ImGui.PushTextWrapPos(winW - Px(PadX));
        ImGui.TextColored(new Vector4(1f, 1f, 1f, 0.4f), Loc.T("os.yapper_delete_hint"));
        ImGui.PopTextWrapPos();
        ImGui.Dummy(new Vector2(0f, Px(16f)));
    }

    /// <summary>Avatar, name and handle at the top of the hub, so settings open on who you are.</summary>
    private void DrawIdentityCard(OsAppContext ctx, YapperMyProfileDto me, float winW)
    {
        var dl = ImGui.GetWindowDrawList();
        var pad = Px(PadX);
        var h = Px(76f);
        var origin = ImGui.GetCursorScreenPos();
        var tl = new Vector2(origin.X + pad, origin.Y);
        var br = new Vector2(origin.X + winW - pad, origin.Y + h);
        OsDrawShared.RoundedGradient(dl, tl, br, Px(14f),
            ctx.Theme.Accent with { W = 0.22f }, ctx.Theme.Accent with { W = 0.06f });
        dl.AddRect(tl, br, ImGui.GetColorU32(ctx.Theme.Accent with { W = 0.35f }), Px(14f), ImDrawFlags.None, Px(1f));

        var r = Px(24f);
        var centre = new Vector2(tl.X + Px(16f) + r, (tl.Y + br.Y) * 0.5f);
        var wrap = me.Avatar is { Length: > 0 } bytes
            ? _mediaCache.GetAvatar(me.ProfileId, bytes)?.GetWrapOrDefault()
            : null;
        if (wrap is not null)
        {
            dl.AddImageRounded(wrap.Handle, centre - new Vector2(r), centre + new Vector2(r),
                Vector2.Zero, Vector2.One, 0xFFFFFFFFu, r);
        }
        else
        {
            dl.AddCircleFilled(centre, r, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.10f)));
        }
        AvatarRings.Draw(dl, centre, r, me.EquippedFrameRef);

        var textX = centre.X + r + Px(14f);
        var nameSz = ImGui.CalcTextSize(me.DisplayName);
        dl.AddText(new Vector2(textX, centre.Y - nameSz.Y - Px(2f)), 0xFFFFFFFFu, me.DisplayName);
        dl.AddText(new Vector2(textX, centre.Y + Px(2f)),
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.5f)), $"@{me.Handle}");

        ImGui.Dummy(new Vector2(winW, h));
    }

    private void DrawPage(OsAppContext ctx, float winW, string title, Action body)
    {
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.SetCursorPosX(Px(PadX));
        if (DrawFloatingBackPill(ImGui.GetCursorScreenPos(), Loc.T("settings.back_arrow"), FontAwesomeIcon.Cog))
        {
            Navigate(View.Hub, forward: false);
        }
        ImGui.Spacing();
        DrawSubpageHeading(title, PadX);

        PushScrollbarStyle();
        using var scroll = ImRaii.Child($"##yapSettBody{title}", new Vector2(0f, ImGui.GetContentRegionAvail().Y), false);
        PopScrollbarStyle();
        if (!scroll)
        {
            return;
        }
        ImGui.Spacing();
        body();
        ImGui.Dummy(new Vector2(0f, Px(16f)));
    }

    private void DrawProfilePage(OsAppContext ctx, float winW)
    {
        var me = _me();
        if (me is null)
        {
            return;
        }
        DrawSectionHeader(Loc.T("os.yapper_edit_name_hint"), PadX);
        ImGui.SetCursorPosX(Px(PadX));
        ImGui.SetNextItemWidth(winW - Px(PadX) * 2f);
        ImGui.InputTextWithHint("##yapEditName", Loc.T("os.yapper_edit_name_hint"), ref _editName,
            YapperLimits.DisplayNameMaxLength);
        ImGui.Spacing();
        DrawSectionHeader(Loc.T("os.yapper_settings_bio"), PadX);
        ImGui.SetCursorPosX(Px(PadX));
        ImGui.InputTextMultiline("##yapEditBio", ref _editBio, YapperLimits.BioRawMaxLength,
            new Vector2(winW - Px(PadX) * 2f, Px(70f)));

        ImGui.Spacing();
        ImGui.SetCursorPosX(Px(PadX));
        var dirty = _editName.Trim() != me.DisplayName || _editBio.Trim() != (me.Bio ?? string.Empty);
        using (ImRaii.Disabled(_profileSaving || !dirty || _editName.Trim().Length == 0))
        {
            if (DrawPill(Loc.T("os.yapper_edit_save"), "yapEditSave", winW - Px(PadX) * 2f,
                    ctx.Theme.Accent, FontAwesomeIcon.Check))
            {
                SaveProfile();
            }
        }
        if (_profileError is { } profileError)
        {
            DrawError(profileError, winW);
        }

        ImGui.Spacing();
        ImGui.Spacing();
        DrawSectionHeader(Loc.T("os.yapper_settings_handle"), PadX);
        ImGui.SetCursorPosX(Px(PadX));
        ImGui.SetNextItemWidth(winW - Px(PadX) * 2f - Px(96f));
        var handle = _renameHandle;
        if (ImGui.InputText("##yapRename", ref handle, YapperLimits.HandleMaxLength))
        {
            _renameHandle = handle;
        }
        ImGui.SameLine(0f, Px(8f));
        var canRename = !_renaming && _renameHandle.Trim() != me.Handle
            && _renameHandle.Trim().Length >= YapperLimits.HandleMinLength;
        using (ImRaii.Disabled(!canRename))
        {
            if (DrawPill(Loc.T("os.yapper_rename_btn"), "yapRenameBtn", Px(88f), ctx.Theme.Accent, null))
            {
                Rename();
            }
        }
        ImGui.SetCursorPosX(Px(PadX));
        ImGui.PushTextWrapPos(winW - Px(PadX));
        if (_renameError is { } error)
        {
            ImGui.TextColored(DangerColor, error);
        }
        else
        {
            ImGui.TextColored(new Vector4(1f, 1f, 1f, 0.4f), Loc.T("os.yapper_rename_hint"));
        }
        ImGui.PopTextWrapPos();

        ImGui.Spacing();
        ImGui.Spacing();
        DrawSectionHeader(Loc.T("rings.section_title"), PadX);
        EnsureRings(me);
        var ringAvatar = me.Avatar is { Length: > 0 } ringBytes
            ? _mediaCache.GetAvatar(me.ProfileId, ringBytes)
            : null;
        _ringPicker.Draw(ringAvatar, winW, Px(PadX), SaveRing,
            () => ctx.Shell.SendIntent("store", OsIntents.CreatePath(OsIntents.StoreOpen, "avatar-packs")));
    }

    private void DrawRatingPage(float winW)
    {
        var me = _me();
        if (me is null)
        {
            return;
        }
        ImGui.SetCursorPosX(Px(PadX));
        if (DrawToggleSwitch("##yapSetNsfwSelf", Loc.T("os.yapper_rating_my_nsfw"), me.IsNsfw))
        {
            ApplyRating(me, !me.IsNsfw, me.NsfwEnabled);
        }
        ImGui.SetCursorPosX(Px(PadX));
        if (DrawToggleSwitch("##yapSetNsfwView", Loc.T("os.yapper_rating_see_nsfw"), me.NsfwEnabled))
        {
            ApplyRating(me, me.IsNsfw, !me.NsfwEnabled);
        }
        ImGui.SetCursorPosX(Px(PadX));
        if (DrawToggleSwitch("##yapSetBlurNsfw", Loc.T("os.yapper_blur_nsfw"), me.BlurNsfw))
        {
            var blur = !me.BlurNsfw;
            _meUpdated(me with { BlurNsfw = blur });
            _ = Task.Run(() => _host.SetBlurNsfwAsync(blur, default));
        }
        ImGui.SetCursorPosX(Px(PadX));
        if (DrawToggleSwitch("##yapSetAllowDms", Loc.T("os.yapper_settings_allow_dms"), me.AllowDms))
        {
            var allow = !me.AllowDms;
            _meUpdated(me with { AllowDms = allow });
            _ = Task.Run(() => _host.SetAllowDmsAsync(allow, default));
        }
    }

    private void DrawNotificationsPage(float winW)
    {
        var me = _me();
        if (me is null)
        {
            return;
        }
        DrawPrefToggle(me, "os.yapper_notif_pref_likes", me.NotifyPrefs.Likes, p => p with { Likes = !p.Likes });
        DrawPrefToggle(me, "os.yapper_notif_pref_replies", me.NotifyPrefs.Replies, p => p with { Replies = !p.Replies });
        DrawPrefToggle(me, "os.yapper_notif_pref_reposts", me.NotifyPrefs.Reposts, p => p with { Reposts = !p.Reposts });
        DrawPrefToggle(me, "os.yapper_notif_pref_mentions", me.NotifyPrefs.Mentions, p => p with { Mentions = !p.Mentions });
        DrawPrefToggle(me, "os.yapper_notif_pref_follows", me.NotifyPrefs.Follows, p => p with { Follows = !p.Follows });
        DrawPrefToggle(me, "os.yapper_notif_pref_newposts", me.NotifyPrefs.NewPosts, p => p with { NewPosts = !p.NewPosts });
    }

    /// <summary>One muted or blocked person: avatar, name, and a pill that undoes it. Deliberately not a grey
    /// default button; undoing is the only thing on the page and should look like it.</summary>
    private void DrawUserList(OsAppContext ctx, float winW, YapperUserRowDto[]? rows, bool unblock)
    {
        if (_listsLoading && rows is null)
        {
            ImGui.SetCursorPosX(Px(PadX));
            ImGui.TextColored(new Vector4(1f, 1f, 1f, 0.45f), Loc.T("os.yapper_loading"));
            return;
        }
        if (rows is null or { Length: 0 })
        {
            ImGui.Dummy(new Vector2(0f, Px(40f)));
            var empty = Loc.T(unblock ? "os.yapper_blocked_empty" : "os.yapper_muted_empty");
            ImGui.SetCursorPosX(MathF.Max(Px(PadX), (winW - ImGui.CalcTextSize(empty).X) * 0.5f));
            ImGui.TextColored(new Vector4(1f, 1f, 1f, 0.45f), empty);
            return;
        }

        var accent = unblock ? DangerColor : ctx.Theme.Accent;
        var label = Loc.T(unblock ? "os.yapper_unblock_btn" : "os.yapper_menu_unmute");
        var pillW = ImGui.CalcTextSize(label).X + Px(26f);
        foreach (var row in rows)
        {
            var dl = ImGui.GetWindowDrawList();
            var pad = Px(PadX);
            var h = Px(58f);
            var origin = ImGui.GetCursorScreenPos();
            var tl = new Vector2(origin.X + pad, origin.Y);
            var br = new Vector2(origin.X + winW - pad, origin.Y + h);
            dl.AddRectFilled(tl, br, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.045f)), Px(12f));

            var r = Px(18f);
            var centre = new Vector2(tl.X + Px(12f) + r, (tl.Y + br.Y) * 0.5f);
            var wrap = row.Avatar is { Length: > 0 } bytes
                ? _mediaCache.GetAvatar(row.ProfileId, bytes)?.GetWrapOrDefault()
                : null;
            if (wrap is not null)
            {
                dl.AddImageRounded(wrap.Handle, centre - new Vector2(r), centre + new Vector2(r),
                    Vector2.Zero, Vector2.One, 0xFFFFFFFFu, r);
            }
            else
            {
                dl.AddCircleFilled(centre, r, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.10f)));
            }
            AvatarRings.Draw(dl, centre, r, row.FrameRef);

            var textX = centre.X + r + Px(12f);
            var nameSz = ImGui.CalcTextSize(row.DisplayName);
            dl.AddText(new Vector2(textX, centre.Y - nameSz.Y - Px(1f)), 0xFFFFFFFFu,
                TruncateToWidth(row.DisplayName, br.X - textX - pillW - Px(20f)));
            dl.AddText(new Vector2(textX, centre.Y + Px(3f)),
                ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.45f)), $"@{row.Handle}");

            ImGui.SetCursorScreenPos(new Vector2(br.X - pillW - Px(10f), centre.Y - Px(13f)));
            if (DrawPill(label, $"yapUndo{row.ProfileId:N}", pillW, accent,
                    unblock ? FontAwesomeIcon.UserCheck : FontAwesomeIcon.VolumeUp))
            {
                Undo(row.ProfileId, unblock);
            }

            ImGui.SetCursorScreenPos(new Vector2(origin.X, br.Y + Px(8f)));
        }
    }

    /// <summary>What is left after the profile goes: an explanation and one way out.</summary>
    private void DrawDeleted(OsAppContext ctx)
    {
        var winW = ImGui.GetWindowSize().X;
        ImGui.Dummy(new Vector2(0f, ImGui.GetContentRegionAvail().Y * 0.28f));

        var dl = ImGui.GetWindowDrawList();
        var iconCentre = new Vector2(ImGui.GetWindowPos().X + winW * 0.5f, ImGui.GetCursorScreenPos().Y);
        IconDraw.AddCentered(dl, FontAwesomeIcon.CheckCircle, Px(38f), iconCentre,
            ImGui.GetColorU32(ctx.Theme.Accent));
        ImGui.Dummy(new Vector2(0f, Px(46f)));

        using (UiFonts.H2?.Push())
        {
            var title = Loc.T("os.yapper_deleted_title");
            ImGui.SetCursorPosX(MathF.Max(Px(PadX), (winW - ImGui.CalcTextSize(title).X) * 0.5f));
            ImGui.TextUnformatted(title);
        }
        ImGui.Spacing();
        ImGui.SetCursorPosX(Px(PadX) * 2f);
        ImGui.PushTextWrapPos(winW - Px(PadX) * 2f);
        ImGui.TextColored(new Vector4(1f, 1f, 1f, 0.6f), Loc.T("os.yapper_deleted_body"));
        ImGui.PopTextWrapPos();

        ImGui.Dummy(new Vector2(0f, Px(20f)));
        ImGui.SetCursorPosX(Px(PadX) * 2f);
        if (DrawPill(Loc.T("os.yapper_deleted_home"), "yapDeletedHome", winW - Px(PadX) * 4f,
                ctx.Theme.Accent, FontAwesomeIcon.Home))
        {
            _view = View.Hub;
            _onDeleted();
        }
    }

    /// <summary>A filled pill in the given accent, so the actions on these pages read as actions.</summary>
    private static bool DrawPill(string label, string id, float width, Vector4 accent, FontAwesomeIcon? icon)
    {
        var dl = ImGui.GetWindowDrawList();
        var h = Px(26f);
        var tl = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton($"##{id}", new Vector2(width, h));
        var hovered = ImGui.IsItemHovered();
        var clicked = ImGui.IsItemClicked();
        if (hovered)
        {
            HandOnHover();
        }
        var alpha = ImGui.GetStyle().Alpha;
        var fill = accent with { W = (hovered ? 0.95f : 0.80f) * alpha };
        dl.AddRectFilled(tl, tl + new Vector2(width, h), ImGui.GetColorU32(fill), h * 0.5f);

        var text = new Vector4(1f, 1f, 1f, alpha);
        var labelSz = ImGui.CalcTextSize(label);
        var iconW = icon is null ? 0f : Px(13f) + Px(6f);
        var startX = tl.X + (width - labelSz.X - iconW) * 0.5f;
        if (icon is { } glyph)
        {
            IconDraw.AddCentered(dl, glyph, Px(13f),
                new Vector2(startX + Px(6.5f), tl.Y + h * 0.5f), ImGui.GetColorU32(text));
        }
        dl.AddText(new Vector2(startX + iconW, tl.Y + (h - labelSz.Y) * 0.5f), ImGui.GetColorU32(text), label);
        return clicked;
    }

    private void DrawDeleteButton(float winW)
    {
        ImGui.SetCursorPosX(Px(PadX));
        using (ImRaii.Disabled(_deleting))
        {
            if (DrawPill(Loc.T("os.yapper_delete_btn"), "yapDelete", winW - Px(PadX) * 2f,
                    DangerColor, FontAwesomeIcon.TrashAlt))
            {
                _confirm(
                    Loc.T("os.yapper_delete_confirm_title"),
                    Loc.T("os.yapper_delete_confirm_body"),
                    Loc.T("os.yapper_delete_btn"),
                    true,
                    DeleteProfile);
            }
        }
        if (_deleteError is { } deleteError)
        {
            DrawError(deleteError, winW);
        }
    }

    private static void DrawError(string message, float winW)
    {
        ImGui.SetCursorPosX(Px(PadX));
        ImGui.PushTextWrapPos(winW - Px(PadX));
        ImGui.TextColored(DangerColor, message);
        ImGui.PopTextWrapPos();
    }

    private static Vector4 DangerColor => new(0.95f, 0.45f, 0.40f, 1f);

    private void DrawPrefToggle(YapperMyProfileDto me, string key, bool on,
        Func<YapperNotifyPrefsDto, YapperNotifyPrefsDto> mutate)
    {
        ImGui.SetCursorPosX(Px(PadX));
        if (DrawToggleSwitch($"##yapPref{key}", Loc.T(key), on))
        {
            var prefs = mutate(me.NotifyPrefs);
            _meUpdated(me with { NotifyPrefs = prefs });
            _ = Task.Run(() => _host.SetNotifyPrefsAsync(prefs, default));
        }
    }

    private void EnsureRings(YapperMyProfileDto me)
    {
        if (_ringInit)
        {
            return;
        }
        _ringInit = true;
        _ringPicker.Open(me.EquippedFrameRef);
        _ = Task.Run(async () =>
        {
            try
            {
                _ringPicker.SetOwned(await _host.GetOwnedRingsAsync().ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                _ringPicker.FailLoad(AetherLove.Services.HubErrorText.Localize(ex));
            }
        });
    }

    private void SaveRing(string? selected)
    {
        _ringPicker.BeginSave();
        _ = Task.Run(async () =>
        {
            try
            {
                await _host.SetAvatarRingAsync(selected).ConfigureAwait(false);
                if (_me() is { } current)
                {
                    _meUpdated(current with { EquippedFrameRef = selected });
                }
                _ringPicker.NotifySaved();
            }
            catch (Exception ex)
            {
                _ringPicker.NotifyError(AetherLove.Services.HubErrorText.Localize(ex));
            }
        });
    }

    private void ApplyRating(YapperMyProfileDto me, bool isNsfw, bool nsfwEnabled)
    {
        _meUpdated(me with { IsNsfw = isNsfw, NsfwEnabled = nsfwEnabled });
        _ = Task.Run(() => _host.SetRatingAsync(isNsfw, nsfwEnabled, default));
    }

    private void SaveProfile()
    {
        var name = _editName.Trim();
        var bio = _editBio.Trim();
        _profileSaving = true;
        _profileError = null;
        _ = Task.Run(async () =>
        {
            try
            {
                _meUpdated(await _host.UpdateProfileAsync(name, bio.Length == 0 ? null : bio).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                _profileError = AetherLove.Services.HubErrorText.Localize(ex);
            }
            finally
            {
                _profileSaving = false;
            }
        });
    }

    private void Rename()
    {
        _renaming = true;
        _renameError = null;
        var handle = _renameHandle.Trim();
        _ = Task.Run(async () =>
        {
            try
            {
                _meUpdated(await _host.RenameHandleAsync(handle).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                _renameError = AetherLove.Services.HubErrorText.Localize(ex);
            }
            finally
            {
                _renaming = false;
            }
        });
    }

    private void DeleteProfile()
    {
        _deleting = true;
        _deleteError = null;
        _ = Task.Run(async () =>
        {
            try
            {
                await _host.DeleteProfileAsync().ConfigureAwait(false);
                _view = View.Deleted;
            }
            catch (Exception ex)
            {
                _deleteError = AetherLove.Services.HubErrorText.Localize(ex);
            }
            finally
            {
                _deleting = false;
            }
        });
    }

    private void Undo(Guid profileId, bool unblock)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                if (unblock)
                {
                    await _host.UnblockAsync(profileId).ConfigureAwait(false);
                }
                else
                {
                    await _host.SetMuteAsync(profileId, false).ConfigureAwait(false);
                }
            }
            catch (Exception)
            {
            }
            RefreshLists();
        });
    }

    private void RefreshLists()
    {
        if (_listsLoading)
        {
            return;
        }
        _listsLoading = true;
        _listsTried = true;
        _ = Task.Run(async () =>
        {
            try
            {
                _blocked = await _host.GetBlockedAsync().ConfigureAwait(false);
                _muted = await _host.GetMutedAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
            }
            finally
            {
                _listsLoading = false;
            }
        });
    }
}
