using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove.Services;
using AetherLove.Services.Auth;
using AetherLove.Services.Crypto;
using AetherLove.Services.Hub;
using AetherLove.Services.Localization;
using AetherLove.Shared.Profile;
using AetherLove.UI;
using AetherLove.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;
using System.Collections.Generic;

namespace AetherLove.Screens;

/// <summary>The multi-profile entry screen: one card per profile of the account (avatar, name, activity badge,
/// supporter lock), plus a create slot. Free accounts get one profile; the second slot is a supporter perk and
/// renders with a starry gate that opens the supporter pitch. Shown on every cold app entry and reachable from
/// Settings.</summary>
public sealed class ProfilePickerScreen
{
    private readonly LoveRouter _router;
    private readonly AetherHubContext _hub;
    private readonly SessionBootstrapper _bootstrap;
    private readonly KeyStorageService _keys;
    private readonly SettingsScreen _settings;

    private readonly object _lock = new();
    private ProfileSummaryDto[] _profiles = [];
    private Guid _activeProfileId;
    private readonly Dictionary<Guid, ISharedImmediateTexture?> _avatarTex = new();
    private volatile bool _loading;
    private volatile bool _busy;
    private volatile string? _error;

    private const float PadX = 16f;
    private const int FreeProfiles = 1;
    private const int SupporterProfiles = 2;

    /// <summary>Set when the picker was opened from Settings as an in-app switch; draws a back pill.</summary>
    public bool OpenedFromSettings { get; set; }

    public ProfilePickerScreen(
        LoveRouter router,
        AetherHubContext hub,
        SessionBootstrapper bootstrap,
        KeyStorageService keys,
        SettingsScreen settings)
    {
        _router = router;
        _hub = hub;
        _bootstrap = bootstrap;
        _keys = keys;
        _settings = settings;
    }

    public void OnShow()
    {
        _error = null;
        Refetch();
    }

    /// <summary>Re-pulls the profile list (also called when a sibling-badge push lands while the picker shows).</summary>
    public void Refetch()
    {
        _loading = true;
        _ = Task.Run(async () =>
        {
            try
            {
                var list = await _hub.ListProfilesAsync().ConfigureAwait(false);
                var cacheDir = Path.Combine(UiHost.PluginInterface.ConfigDirectory.FullName, "PickerAvatarCache");
                foreach (var p in list.Profiles)
                {
                    if (p.Avatar is { Length: > 0 } bytes)
                    {
                        _avatarTex[p.ProfileId] = AvatarDiskCache.Store(cacheDir, p.ProfileId.ToString(), bytes);
                    }
                }
                lock (_lock)
                {
                    _profiles = list.Profiles;
                    _activeProfileId = list.ActiveProfileId;
                }
            }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, "[ProfilePicker] ListProfilesAsync failed.");
                _error = HubErrorText.Localize(ex);
            }
            finally
            {
                _loading = false;
            }
        });
    }

    private bool IsSupporter => _bootstrap.LastAccount?.IsSupporter == true;

    public void Draw()
    {
        var winPos = ImGui.GetWindowPos();
        var winSize = ImGui.GetWindowSize();

        ImGui.Spacing();
        if (OpenedFromSettings
            && DrawFloatingBackPill(ImGui.GetCursorScreenPos(), Loc.T("common.back"), FontAwesomeIcon.Cog))
        {
            OpenedFromSettings = false;
            _router.Navigate(LoveView.Settings);
        }

        ProfileSummaryDto[] profiles;
        Guid activeId;
        lock (_lock)
        {
            profiles = _profiles;
            activeId = _activeProfileId;
        }

        ImGui.Dummy(new Vector2(1f, Px(26f)));
        DrawHeading();

        PushScrollbarStyle();
        using (var scroll = ImRaii.Child("##pickerScroll", ImGui.GetContentRegionAvail(), false))
        {
            if (scroll.Success)
            {
                var listW = ImGui.GetContentRegionAvail().X;
                if (_busy)
                {
                    ImGui.Dummy(new Vector2(1f, Px(30f)));
                    CenteredMutedText(Loc.T("picker.switching"));
                }
                else if (_loading && profiles.Length == 0)
                {
                    ImGui.Dummy(new Vector2(1f, Px(30f)));
                    CenteredMutedText(Loc.T("common.loading"));
                }
                else
                {
                    ImGui.Dummy(new Vector2(1f, Px(8f)));
                    foreach (var p in profiles)
                    {
                        DrawProfileCard(p, activeId, listW);
                    }
                    DrawCreateSlot(profiles, listW);
                    if (_error is { } err)
                    {
                        ImGui.Spacing();
                        CenteredMutedText(err);
                    }
                }
                ImGui.Dummy(new Vector2(1f, Px(12f)));
            }
        }
        PopScrollbarStyle();

        SupporterInfoPopup.Draw(winPos, winSize, _settings.RequestSupporterView);
    }

    private void DrawHeading()
    {
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var winW = ImGui.GetContentRegionAvail().X;
        var center = ImGui.GetCursorScreenPos() + new Vector2(winW * 0.5f, Px(16f));
        IconDraw.AddCentered(dl, FontAwesomeIcon.Heart, Px(30f), center, ImGui.GetColorU32(t.Accent));
        ImGui.Dummy(new Vector2(1f, Px(38f)));

        var title = Loc.T("picker.title");
        using (UiFonts.H2?.Push())
        {
            var tw = ImGui.CalcTextSize(title).X;
            ImGui.SetCursorPosX(MathF.Max(Px(PadX), (winW - tw) * 0.5f));
            ImGui.TextUnformatted(title);
        }
        CenteredMutedText(Loc.T("picker.subtitle"));
        ImGui.Dummy(new Vector2(1f, Px(14f)));
    }

    private void DrawProfileCard(ProfileSummaryDto p, Guid activeId, float listW)
    {
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var pad = Px(PadX);
        var rowH = Px(64f);
        var start = ImGui.GetCursorScreenPos();
        var tl = start + new Vector2(pad, 0f);
        var br = tl + new Vector2(listW - pad * 2f, rowH);

        ImGui.SetCursorScreenPos(tl);
        var clicked = ImGui.InvisibleButton($"##pick_{p.ProfileId}", br - tl);
        var hovered = ImGui.IsItemHovered();

        dl.AddRectFilled(tl, br, hovered ? 0x1AFFFFFFu : 0x0DFFFFFFu, Px(12f));
        dl.AddRect(tl, br, p.ProfileId == activeId ? ImGui.GetColorU32(t.Accent) : 0x22FFFFFFu, Px(12f),
            ImDrawFlags.None, Px(1f));

        var avatarCenter = new Vector2(tl.X + Px(34f), tl.Y + rowH * 0.5f);
        var avatarR = Px(22f);
        _avatarTex.TryGetValue(p.ProfileId, out var tex);
        var wrap = tex?.GetWrapOrDefault();
        if (wrap != null)
        {
            dl.AddImageRounded(wrap.Handle, avatarCenter - new Vector2(avatarR), avatarCenter + new Vector2(avatarR),
                Vector2.Zero, Vector2.One, 0xFFFFFFFF, avatarR, ImDrawFlags.RoundCornersAll);
        }
        else
        {
            dl.AddCircleFilled(avatarCenter, avatarR, UiColors.AvatarFallback);
            IconDraw.AddCentered(dl, FontAwesomeIcon.User, avatarR * 0.9f, avatarCenter, 0x66FFFFFFu);
        }
        dl.AddCircle(avatarCenter, avatarR, 0x55FFFFFFu, 0, Px(1.2f));

        if (p.Locked)
        {
            dl.AddRectFilled(tl, br, 0x66000000u, Px(12f));
        }

        var textX = tl.X + Px(66f);
        dl.AddText(new Vector2(textX, tl.Y + Px(12f)), 0xFFFFFFFFu, p.DisplayName);
        var banned = p.Status == ProfileLifecycle.Banned;
        var subtitle = p.Locked
            ? Loc.T("picker.locked")
            : banned
                ? Loc.T("picker.banned")
                : p.Status == ProfileLifecycle.Onboarding
                    ? Loc.T("picker.finish_setup")
                    : p.ProfileId == activeId
                        ? Loc.T("picker.current")
                        : " ";
        var subtitleCol = p.Locked
            ? ImGui.GetColorU32(UiColors.Patreon)
            : banned
                ? ImGui.GetColorU32(new Vector4(0.95f, 0.40f, 0.40f, 1f))
                : UiColors.TextMuted;
        dl.AddText(new Vector2(textX, tl.Y + Px(34f)), subtitleCol, subtitle);

        // The supporter perk mark sits at the card's top-right, the same badge as the nav avatar's.
        if (p.Locked)
        {
            DrawSupporterStarBadge(dl, new Vector2(br.X - Px(16f), tl.Y + Px(14f)));
        }

        var badgeTotal = p.NewMatches + p.UnreadChats;
        if (badgeTotal > 0 && !p.Locked)
        {
            var label = badgeTotal > 99 ? "99+" : badgeTotal.ToString();
            var textSz = ImGui.CalcTextSize(label);
            var bubbleR = MathF.Max(Px(11f), textSz.X * 0.5f + Px(6f));
            var bubbleCenter = new Vector2(br.X - Px(24f), tl.Y + rowH * 0.5f);
            dl.AddCircleFilled(bubbleCenter, bubbleR, UiColors.UnreadBadge);
            dl.AddText(bubbleCenter - textSz * 0.5f, 0xFFFFFFFFu, label);
        }

        if (clicked && !_busy)
        {
            OnProfileClicked(p, activeId);
        }

        ImGui.SetCursorScreenPos(new Vector2(start.X, br.Y));
        ImGui.Dummy(new Vector2(1f, Px(10f)));
    }

    private void DrawCreateSlot(ProfileSummaryDto[] profiles, float listW)
    {
        var allowance = IsSupporter ? SupporterProfiles : FreeProfiles;
        if (profiles.Length >= SupporterProfiles)
        {
            return;
        }
        var gated = profiles.Length >= allowance;

        var dl = ImGui.GetWindowDrawList();
        var pad = Px(PadX);
        var rowH = Px(64f);
        var start = ImGui.GetCursorScreenPos();
        var tl = start + new Vector2(pad, 0f);
        var br = tl + new Vector2(listW - pad * 2f, rowH);

        ImGui.SetCursorScreenPos(tl);
        var clicked = ImGui.InvisibleButton("##pickCreate", br - tl);
        var hovered = ImGui.IsItemHovered();

        dl.AddRectFilled(tl, br, hovered ? 0x14FFFFFFu : 0x08FFFFFFu, Px(12f));
        dl.AddRect(tl, br, gated ? ImGui.GetColorU32(UiColors.Patreon with { W = 0.55f }) : 0x22FFFFFFu,
            Px(12f), ImDrawFlags.None, Px(1f));

        var iconCenter = new Vector2(tl.X + Px(34f), tl.Y + rowH * 0.5f);
        dl.AddCircle(iconCenter, Px(22f), 0x44FFFFFFu, 0, Px(1.2f));
        IconDraw.AddCentered(dl, FontAwesomeIcon.Plus, Px(16f), iconCenter, 0x99FFFFFFu);

        var textX = tl.X + Px(66f);
        var title = profiles.Length == 0 ? Loc.T("picker.create") : Loc.T("picker.create_secondary");
        var titleY = gated ? tl.Y + (rowH - ImGui.GetFontSize()) * 0.5f : tl.Y + Px(12f);
        dl.AddText(new Vector2(textX, titleY), 0xFFFFFFFFu, title);
        if (!gated)
        {
            dl.AddText(new Vector2(textX, tl.Y + Px(34f)), UiColors.TextMuted, Loc.T("picker.create_sub"));
        }
        else
        {
            DrawSupporterStarBadge(dl, new Vector2(br.X - Px(16f), tl.Y + Px(14f)));
        }

        if (clicked && !_busy)
        {
            if (gated)
            {
                SupporterInfoPopup.Open("picker.create_supporter_pitch");
            }
            else
            {
                CreateProfile();
            }
        }

        ImGui.SetCursorScreenPos(new Vector2(start.X, br.Y));
        ImGui.Dummy(new Vector2(1f, Px(10f)));
    }

    private void OnProfileClicked(ProfileSummaryDto p, Guid activeId)
    {
        if (p.Locked)
        {
            SupporterInfoPopup.Open("picker.locked_supporter_pitch");
            return;
        }
        if (p.Status == ProfileLifecycle.Banned)
        {
            // A banned profile can't be entered (its deck/hub calls just fail); the red "Banned" label explains why.
            return;
        }
        // Switching to a profile with pending matches/messages drops the user straight into the Matches list
        // (where the pending activity is), not the swipe deck.
        var landOnMatches = p.NewMatches + p.UnreadChats > 0;
        if (p.ProfileId == activeId && _bootstrap.LastConnection is { } conn)
        {
            Enter(conn.Status, landOnMatches);
            return;
        }
        SwitchTo(p.ProfileId, landOnMatches);
    }

    private void Enter(ProfileLifecycle status, bool landOnMatches = false)
    {
        OpenedFromSettings = false;
        var view = status == ProfileLifecycle.Onboarding
            ? LoveView.Onboarding
            : landOnMatches ? LoveView.ChatList : LoveView.Deck;
        _router.Navigate(view);
    }

    private void SwitchTo(Guid profileId, bool landOnMatches)
    {
        _busy = true;
        _error = null;
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await _bootstrap.SwitchProfileAsync(profileId).ConfigureAwait(false);
                if (result is SessionBootstrapResult.SignedInActive or SessionBootstrapResult.SignedInOnboarding)
                {
                    Enter(_bootstrap.LastConnection?.Status ?? ProfileLifecycle.Active, landOnMatches);
                }
                else
                {
                    _error = Loc.T("picker.switch_failed");
                }
            }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, "[ProfilePicker] SwitchProfileAsync failed.");
                _error = HubErrorText.Localize(ex);
            }
            finally
            {
                _busy = false;
            }
        });
    }

    /// <summary>Creates the new profile, switches the session to it, provisions its key bundle under the
    /// account KEK (so the passphrase never re-prompts and the OS-setup gate stays satisfied), then enters the
    /// AetherLove onboarding wizard.</summary>
    private void CreateProfile()
    {
        _busy = true;
        _error = null;
        _ = Task.Run(async () =>
        {
            try
            {
                var created = await _hub.CreateProfileAsync(new CreateProfileRequest(string.Empty)).ConfigureAwait(false);
                var result = await _bootstrap.SwitchProfileAsync(created.ProfileId).ConfigureAwait(false);
                if (result is not (SessionBootstrapResult.SignedInActive or SessionBootstrapResult.SignedInOnboarding))
                {
                    _error = Loc.T("picker.switch_failed");
                    return;
                }
                await TryProvisionKeyBundleAsync().ConfigureAwait(false);
                Enter(ProfileLifecycle.Onboarding);
            }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, "[ProfilePicker] CreateProfileAsync failed.");
                _error = HubErrorText.Localize(ex);
            }
            finally
            {
                _busy = false;
            }
        });
    }

    /// <summary>Publishes the just-created profile's key bundle before its first chat, rather than leaving it to
    /// the recovery gate. The bootstrapper owns the wrapping choice (account KEK, else a sibling profile's key),
    /// so a migrated account that never captured a KEK still gets working encryption with no prompt.</summary>
    private async Task TryProvisionKeyBundleAsync()
    {
        if (_keys.HasLocalKey || _bootstrap.LastConnection?.HasKeyBundle == true)
        {
            return;
        }
        await _bootstrap.EnsureActiveProfileKeysAsync().ConfigureAwait(false);
    }

    /// <summary>The supporter perk mark used across the app (nav avatar, chat header): dark disc, gold ring,
    /// centered star nudged down to compensate for the glyph's empty descent.</summary>
    private static void DrawSupporterStarBadge(ImDrawListPtr dl, Vector2 center)
    {
        var r = Px(9f);
        dl.AddCircleFilled(center, r, 0xFF1E1E24u, 24);
        dl.AddCircle(center, r, UiColors.FavoriteStar, 24, Px(1.2f));
        IconDraw.AddCentered(dl, FontAwesomeIcon.Star, r * 1.2f, center + new Vector2(0f, Px(0.5f)),
            UiColors.FavoriteStar);
    }

    private static void CenteredMutedText(string text)
    {
        var winW = ImGui.GetContentRegionAvail().X;
        var textW = ImGui.CalcTextSize(text).X;
        ImGui.SetCursorPosX(MathF.Max(Px(PadX), (winW - textW) * 0.5f));
        ImGui.TextColored(UiColors.Muted, text);
    }
}
