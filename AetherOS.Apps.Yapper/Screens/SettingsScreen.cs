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

/// <summary>Yapper settings, in the shared app-settings layout (floating back pill + subpage heading +
/// section headers): content rating switches, per-kind notification toggles, handle rename (30-day
/// cooldown), and the blocked/muted lists with undo actions. Doubles as the IAppSettings body.</summary>
internal sealed class SettingsScreen
{
    private const float PadX = 16f;

    private readonly IYapperHost _host;
    private readonly YapperMediaCache _mediaCache;
    private readonly Func<YapperMyProfileDto?> _me;
    private readonly Action<YapperMyProfileDto> _meUpdated;
    private readonly Action _back;

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
    private DateTime _shownAt = DateTime.MinValue;

    public SettingsScreen(IYapperHost host, YapperMediaCache mediaCache,
        Func<YapperMyProfileDto?> me, Action<YapperMyProfileDto> meUpdated, Action back)
    {
        _host = host;
        _mediaCache = mediaCache;
        _me = me;
        _meUpdated = meUpdated;
        _back = back;
    }

    public void OnShow()
    {
        _renameHandle = _me()?.Handle ?? string.Empty;
        _renameError = null;
        _editName = _me()?.DisplayName ?? string.Empty;
        _editBio = _me()?.Bio ?? string.Empty;
        _profileError = null;
        _shownAt = DateTime.UtcNow;
        RefreshLists();
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
                var updated = await _host.UpdateProfileAsync(name, bio.Length == 0 ? null : bio).ConfigureAwait(false);
                _meUpdated(updated);
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

    private void RefreshLists()
    {
        if (_listsLoading)
        {
            return;
        }
        _listsLoading = true;
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

    /// <summary>The in-app page (profile gear); its back pill returns to the profile.</summary>
    public void Draw(OsAppContext ctx)
    {
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.SetCursorPosX(Px(PadX));
        if (DrawFloatingBackPill(ImGui.GetCursorScreenPos(), Loc.T("settings.back_arrow"), FontAwesomeIcon.User))
        {
            _back();
        }
        ImGui.Spacing();
        DrawBody(ctx);
    }

    /// <summary>The IAppSettings body, shown inside the OS Settings app.</summary>
    public void DrawSettings(OsAppContext ctx, Action? onBack)
    {
        if (onBack != null)
        {
            ImGui.Spacing();
            ImGui.Spacing();
            ImGui.SetCursorPosX(Px(PadX));
            if (DrawFloatingBackPill(ImGui.GetCursorScreenPos(), Loc.T("settings.back_arrow"), FontAwesomeIcon.Cog))
            {
                onBack();
            }
            ImGui.Spacing();
        }
        DrawBody(ctx);
    }

    private void DrawBody(OsAppContext ctx)
    {
        if ((DateTime.UtcNow - _shownAt).TotalSeconds > 30)
        {
            OnShow();
        }
        DrawSubpageHeading(Loc.T("os.yapper_settings_title"), PadX);

        var me = _me();
        if (me is null)
        {
            ImGui.Spacing();
            ImGui.SetCursorPosX(Px(PadX));
            ImGui.TextColored(new Vector4(1f, 1f, 1f, 0.45f), Loc.T("os.yapper_loading"));
            return;
        }
        var winW = ImGui.GetWindowSize().X;

        PushScrollbarStyle();
        using var scroll = ImRaii.Child("##yapSettingsScroll", new Vector2(0f, ImGui.GetContentRegionAvail().Y), false);
        if (!scroll.Success)
        {
            PopScrollbarStyle();
            return;
        }
        ImGui.Spacing();

        DrawSectionHeader(Loc.T("os.yapper_settings_profile"), PadX);
        ImGui.SetCursorPosX(Px(PadX));
        ImGui.SetNextItemWidth(winW - Px(PadX) * 2f);
        ImGui.InputTextWithHint("##yapEditName", Loc.T("os.yapper_edit_name_hint"), ref _editName,
            YapperLimits.DisplayNameMaxLength);
        ImGui.SetCursorPosX(Px(PadX));
        ImGui.InputTextMultiline("##yapEditBio", ref _editBio, YapperLimits.BioRawMaxLength,
            new Vector2(winW - Px(PadX) * 2f, Px(70f)));
        ImGui.SetCursorPosX(Px(PadX));
        var profileDirty = _editName.Trim() != me.DisplayName || _editBio.Trim() != (me.Bio ?? string.Empty);
        ImGui.PushStyleColor(ImGuiCol.Button, ctx.Theme.Accent with { W = 0.85f });
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ctx.Theme.Accent);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, ctx.Theme.Accent with { W = 0.65f });
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(12f));
        using (ImRaii.Disabled(_profileSaving || !profileDirty || _editName.Trim().Length == 0))
        {
            if (Button($"{Loc.T("os.yapper_edit_save")}##yapEditSave", new Vector2(Px(110f), 0f)))
            {
                SaveProfile();
            }
        }
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);
        if (_profileError is { } profileError)
        {
            ImGui.SetCursorPosX(Px(PadX));
            ImGui.PushTextWrapPos(winW - Px(PadX));
            ImGui.TextColored(new Vector4(0.95f, 0.45f, 0.4f, 1f), profileError);
            ImGui.PopTextWrapPos();
        }

        ImGui.Spacing();
        ImGui.Spacing();
        DrawSectionHeader(Loc.T("os.yapper_ob_rating_title"), PadX);
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

        ImGui.Spacing();
        ImGui.Spacing();
        DrawSectionHeader(Loc.T("os.yapper_settings_notifs"), PadX);
        DrawPrefToggle(me, "os.yapper_notif_pref_likes", me.NotifyPrefs.Likes, p => p with { Likes = !p.Likes });
        DrawPrefToggle(me, "os.yapper_notif_pref_replies", me.NotifyPrefs.Replies, p => p with { Replies = !p.Replies });
        DrawPrefToggle(me, "os.yapper_notif_pref_reposts", me.NotifyPrefs.Reposts, p => p with { Reposts = !p.Reposts });
        DrawPrefToggle(me, "os.yapper_notif_pref_mentions", me.NotifyPrefs.Mentions, p => p with { Mentions = !p.Mentions });
        DrawPrefToggle(me, "os.yapper_notif_pref_follows", me.NotifyPrefs.Follows, p => p with { Follows = !p.Follows });
        DrawPrefToggle(me, "os.yapper_notif_pref_newposts", me.NotifyPrefs.NewPosts, p => p with { NewPosts = !p.NewPosts });

        ImGui.Spacing();
        ImGui.Spacing();
        DrawSectionHeader(Loc.T("os.yapper_settings_handle"), PadX);
        ImGui.SetCursorPosX(Px(PadX));
        ImGui.SetNextItemWidth(winW * 0.5f);
        var handle = _renameHandle;
        if (ImGui.InputText("##yapRename", ref handle, YapperLimits.HandleMaxLength))
        {
            _renameHandle = handle;
        }
        ImGui.SameLine();
        var canRename = !_renaming && _renameHandle.Trim() != me.Handle && _renameHandle.Trim().Length >= YapperLimits.HandleMinLength;
        using (ImRaii.Disabled(!canRename))
        {
            if (Button($"{Loc.T("os.yapper_rename_btn")}##yapRenameBtn", Vector2.Zero))
            {
                Rename();
            }
        }
        ImGui.SetCursorPosX(Px(PadX));
        ImGui.PushTextWrapPos(winW - Px(PadX));
        if (_renameError is { } error)
        {
            ImGui.TextColored(new Vector4(0.95f, 0.45f, 0.4f, 1f), error);
        }
        else
        {
            ImGui.TextColored(new Vector4(1f, 1f, 1f, 0.4f), Loc.T("os.yapper_rename_hint"));
        }
        ImGui.PopTextWrapPos();

        DrawUserList(Loc.T("os.yapper_settings_blocked"), _blocked, unblock: true);
        DrawUserList(Loc.T("os.yapper_settings_muted"), _muted, unblock: false);
        ImGui.Dummy(new Vector2(0f, Px(14f)));
        PopScrollbarStyle();
    }

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

    private void ApplyRating(YapperMyProfileDto me, bool isNsfw, bool nsfwEnabled)
    {
        _meUpdated(me with { IsNsfw = isNsfw, NsfwEnabled = nsfwEnabled });
        _ = Task.Run(() => _host.SetRatingAsync(isNsfw, nsfwEnabled, default));
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
                var updated = await _host.RenameHandleAsync(handle).ConfigureAwait(false);
                _meUpdated(updated);
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

    private void DrawUserList(string title, YapperUserRowDto[]? rows, bool unblock)
    {
        ImGui.Spacing();
        ImGui.Spacing();
        DrawSectionHeader(title, PadX);
        if (rows is null or { Length: 0 })
        {
            ImGui.SetCursorPosX(Px(PadX));
            ImGui.TextColored(new Vector4(1f, 1f, 1f, 0.4f), Loc.T("os.yapper_list_empty"));
            return;
        }
        foreach (var row in rows)
        {
            ImGui.SetCursorPosX(Px(PadX));
            ImGui.TextUnformatted($"{row.DisplayName}");
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(1f, 1f, 1f, 0.45f), $"@{row.Handle}");
            ImGui.SameLine(ImGui.GetWindowSize().X - Px(90f));
            var label = Loc.T(unblock ? "os.yapper_unblock_btn" : "os.yapper_menu_unmute");
            if (ImGui.SmallButton($"{label}##yapListUndo{row.ProfileId:N}"))
            {
                var id = row.ProfileId;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (unblock)
                        {
                            await _host.UnblockAsync(id).ConfigureAwait(false);
                        }
                        else
                        {
                            await _host.SetMuteAsync(id, false).ConfigureAwait(false);
                        }
                    }
                    catch (Exception)
                    {
                    }
                    RefreshLists();
                });
            }
            HandOnHover();
        }
    }
}
