using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Emoji;
using AetherLove.Navigation;
using AetherLove.Services;
using AetherLove.Services.Hub;
using AetherLove.Services.Localization;
using AetherLove.Shared;
using AetherLove.Shared.Profile;
using AetherLove.Shared.Profile.Enums;
using AetherLove.UI;
using AetherLove.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;
using OtterGui.Widgets;

namespace AetherLove.Screens;

/// <summary>"My Profile" screen with View, Edit, and Images tabs.</summary>
public partial class MyProfileScreen
{
    private enum Tab { View, Edit, Images }
    private Tab _activeTab = Tab.View;
    private Tab _prevTab = Tab.Edit;

    private readonly ProfileScreen _profileScreen;

    // Emoji picker for the About-Me field in the Edit tab (mirrors onboarding Step 4).
    private readonly EmojiPickerPopup _bioEmojiPicker = new();

    // Lazy: Lumina labels need Plugin.DataManager to be live.
    private ClippedSelectableCombo<string>? _jobCombo;

    private ClippedSelectableCombo<string> EnsureJobCombo() =>
        _jobCombo ??= new ClippedSelectableCombo<string>(
            "mypjob", "##mypjob", 260f,
            GetJobLabels().ToList(),
            s => s);


    private string _displayName = "";
    private string _bio = "";
    private int _regionIdx;
    private int _raceIdx;
    private int _genderIdx;
    private readonly bool[] _langSelected = new bool[Languages.Length];
    private readonly bool[] _contentInterests = new bool[ContentLabels.Length];
    private readonly bool[] _lookingFor = new bool[LookingForLabels.Length];
    private bool _nsfwOptIn;
    private int _timezoneIdx;

    private int _jobComboIdx;
    private int _expansionIdx;
    private string _spotifyInput = "";
    private string _spotifyTrackId = "";
    private string _spotifyTrackName = "";
    private bool _spotifyFetching;
    private string _favoriteMovie = "";
    private string _favoriteAnime = "";
    private string _favoriteFFCharacter = "";
    private readonly bool[] _weekdayHours = new bool[24];
    private readonly bool[] _weekendHours = new bool[24];
    private readonly bool[] _syncToolsSelected = new bool[SyncToolValues.Length];


    private readonly bool[] _filterRaces = new bool[Races.Length];
    private readonly bool[] _filterGenders = new bool[Genders.Length];
    // Excludes "Prefer not to say" from the filter list.
    private readonly bool[] _filterRegions = new bool[Regions.Length - 1];
    private readonly bool[] _filterLanguages = new bool[Languages.Length];


    private float _savedTimer;

    private readonly AetherLoveHubClient _hubClient;
    private CancellationTokenSource _cts = new();
    private volatile bool _editFormHydrated;
    private volatile bool _editFormLoading;
    private volatile string? _editFormLoadError;
    private volatile bool _savingToServer;

    // Cached editable state; invalidated after a local save or on a (re)connect.
    private OnboardingStateDto? _cachedState;

    /// <summary>Drops the cached editable state so the next Edit-tab entry re-fetches from the server.</summary>
    public void InvalidateEditCache() => _cachedState = null;

    // Server-known photos. `_serverAvatar` is the Order=0 row (avatar). `_serverMain` is Order=1.
    // `_serverExtras[i]` is Order=2+i (extras 1..3). Null = no photo set server-side at that slot.
    private OnboardingPhotoDto? _serverAvatar;
    private OnboardingPhotoDto? _serverMain;
    private readonly OnboardingPhotoDto?[] _serverExtras = new OnboardingPhotoDto?[3];
    private ISharedImmediateTexture? _serverAvatarTex;
    private ISharedImmediateTexture? _serverMainTex;
    private readonly ISharedImmediateTexture?[] _serverExtraTex = new ISharedImmediateTexture?[3];
    private volatile bool _committingImages;
    private float _imagesSavedTimer;


    private readonly RateLimitModal _rateLimitModal;
    private readonly SaveErrorModal _saveErrorModal;
    private readonly PendingImagePick _imgPendingPick;

    public MyProfileScreen(ProfileScreen profileScreen, AetherLoveHubClient hubClient,
                           RateLimitModal rateLimitModal,
                           SaveErrorModal saveErrorModal, ImageRequirementsModal imageReqModal)
    {
        _profileScreen = profileScreen;
        _hubClient = hubClient;
        _rateLimitModal = rateLimitModal;
        _saveErrorModal = saveErrorModal;
        _imgPendingPick = new PendingImagePick(imageReqModal);
    }


    public void OnShow()
    {
        _activeTab = Tab.View;
        _prevTab = Tab.Edit;

        _editFormHydrated = false;
        _editFormLoadError = null;

        _imgAvatarPath = "";
        _imgAvatarHandle = null;
        _imgAvatarConfirmed = false;
        foreach (var slot in _imgPhotoSlots)
        {
            slot.Clear();
        }
        _imgActiveSlot = -1;
    }

    public void OnHide()
    {
        _cts.Cancel();
        _cts.Dispose();
        _cts = new CancellationTokenSource();
    }

    private bool IsLalafell() =>
        _raceIdx >= 0
        && _raceIdx < RaceValues.Length
        && RaceValues[_raceIdx] == Race.Lalafell;

    private void ClearAdultFlagsForLalafell()
    {
        for (var i = 0; i < LookingForValues.Length && i < _lookingFor.Length; i++)
        {
            if (LookingForValues[i] == LookingFor.Erp)
            {
                _lookingFor[i] = false;
            }
        }
        _nsfwOptIn = false;
    }

    /// <summary>Fetches profile + filters and hydrates the edit-tab fields. Fire-and-forget.</summary>
    private void LoadFromServer()
    {
        if (_editFormLoading)
        {
            return;
        }

        if (_cachedState is not null)
        {
            HydrateFromState(_cachedState);
            _editFormHydrated = true;
            return;
        }

        _editFormLoading = true;
        _editFormLoadError = null;
        var ct = _cts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                var state = await _hubClient.GetOnboardingStateAsync(ct).ConfigureAwait(false);
                if (ct.IsCancellationRequested)
                {
                    return;
                }
                _cachedState = state;
                HydrateFromState(state);
                _editFormHydrated = true;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (ct.IsCancellationRequested)
                {
                    return;
                }
                _editFormLoadError = HubErrorText.Localize(ex);
                Plugin.Log.Warning(ex, "[MyProfileScreen] GetOnboardingStateAsync failed.");
            }
            finally
            {
                _editFormLoading = false;
            }
        }, ct);
    }

    private void HydrateFromState(OnboardingStateDto state)
    {
        var b = state.Basic;
        var f = state.Filters;

        _displayName = b.DisplayName ?? string.Empty;
        _bio = b.Bio ?? string.Empty;

        _raceIdx = IndexOf(RaceValues, b.Race, fallback: 0);
        _genderIdx = IndexOf(GenderValues, b.Gender, fallback: 0);
        _regionIdx = IndexOf(RegionValues, b.Region, fallback: RegionValues.Length - 1);
        _expansionIdx = IndexOf(ExpansionValues, b.FavoriteExpansion, fallback: 0);
        _jobComboIdx = IndexOf(JobValues, b.FavoriteJob, fallback: 0);

        MaskToBools(LanguageValues, b.LanguageMask,
            (a, m) => ((short)a & (short)m) != 0, _langSelected);
        MaskToBools(ContentInterestValues, b.ContentInterestMask,
            (a, m) => ((int)a & (int)m) != 0, _contentInterests);
        MaskToBools(LookingForValues, b.LookingForMask,
            (a, m) => ((short)a & (short)m) != 0, _lookingFor);
        MaskToBools(SyncToolValues, b.SyncTool,
            (a, m) => ((short)a & (short)m) != 0, _syncToolsSelected);

        _nsfwOptIn = b.NsfwEnabled;
        _spotifyInput = b.SpotifyTrackId ?? string.Empty;
        _spotifyTrackId = b.SpotifyTrackId ?? string.Empty;
        _spotifyTrackName = b.SpotifyTrackName ?? string.Empty;
        _favoriteMovie = b.FavoriteMovie ?? string.Empty;
        _favoriteAnime = b.FavoriteAnime ?? string.Empty;
        _favoriteFFCharacter = b.FavoriteFFCharacter ?? string.Empty;

        var tzIdx = Array.FindIndex(AllTimezones, tz => tz.Id == b.Timezone);
        if (tzIdx < 0)
        {
            tzIdx = Array.FindIndex(AllTimezones, tz => tz.Id == TimeZoneInfo.Local.Id);
        }
        _timezoneIdx = Math.Max(0, tzIdx);

        MaskToHours(b.WeekdayHoursMask, _weekdayHours);
        MaskToHours(b.WeekendHoursMask, _weekendHours);

        MaskToBools(RaceValues, f.WantedRaceMask,
            (a, m) => ((short)a & (short)m) != 0, _filterRaces);
        MaskToBools(GenderValues, f.WantedGenderMask,
            (a, m) => ((short)a & (short)m) != 0, _filterGenders);
        // _filterRegions excludes "Prefer not to say" (last entry).
        for (int i = 0; i < _filterRegions.Length; i++)
        {
            _filterRegions[i] = ((short)RegionValues[i] & (short)f.WantedRegionMask) != 0;
        }
        MaskToBools(LanguageValues, f.WantedLanguageMask,
            (a, m) => ((short)a & (short)m) != 0, _filterLanguages);

        if (IsLalafell())
        {
            ClearAdultFlagsForLalafell();
        }

        HydrateServerPhotos(state.Photos);
    }

    private const string MyProfilePhotoCacheDir = "MyProfilePhotoCache";

    private void HydrateServerPhotos(OnboardingPhotoDto[] photos)
    {
        _serverAvatar = null;
        _serverMain = null;
        for (int i = 0; i < _serverExtras.Length; i++)
        {
            _serverExtras[i] = null;
        }
        _serverAvatarTex = null;
        _serverMainTex = null;
        for (int i = 0; i < _serverExtraTex.Length; i++)
        {
            _serverExtraTex[i] = null;
        }

        var cacheDir = Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, MyProfilePhotoCacheDir);
        try { Directory.CreateDirectory(cacheDir); }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[MyProfileScreen] Failed to create photo cache dir.");
            return;
        }

        foreach (var photo in photos)
        {
            try
            {
                var path = Path.Combine(cacheDir, $"slot_{photo.Order}.webp");
                File.WriteAllBytes(path, photo.WebpBytes);
                var tex = Plugin.TextureProvider.GetFromFile(path);

                switch (photo.Order)
                {
                    case 0:
                        _serverAvatar = photo;
                        _serverAvatarTex = tex;
                        break;
                    case 1:
                        _serverMain = photo;
                        _serverMainTex = tex;
                        break;
                    case 2:
                    case 3:
                    case 4:
                        var idx = photo.Order - 2;
                        _serverExtras[idx] = photo;
                        _serverExtraTex[idx] = tex;
                        break;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, $"[MyProfileScreen] Failed to hydrate photo slot {photo.Order}.");
            }
        }
    }

    /// <summary>Pushes the edited form back to the server (basic + filters).</summary>
    private void SaveToServer()
    {
        if (_savingToServer)
        {
            return;
        }
        _savingToServer = true;
        var ct = _cts.Token;

        var basic = BuildBasicProfileDto();
        var filters = BuildFiltersDto();

        _ = Task.Run(async () =>
        {
            try
            {
                await _hubClient.SaveBasicProfileAsync(basic, ct).ConfigureAwait(false);
                await _hubClient.SaveFiltersAsync(filters, ct).ConfigureAwait(false);
                if (ct.IsCancellationRequested)
                {
                    return;
                }
                _savedTimer = 2.5f;
                // Server now has newer data than either cache — force both to re-fetch.
                _cachedState = null;
                _profileScreen.InvalidateMyProfileCache();
            }
            catch (OperationCanceledException) { }
            catch (RateLimitException rl)
            {
                _rateLimitModal.Show(rl);
            }
            catch (Exception ex)
            {
                if (ct.IsCancellationRequested)
                {
                    return;
                }
                _saveErrorModal.Show(HubErrorText.Localize(ex));
                Plugin.Log.Warning(ex, "[MyProfileScreen] Save to server failed.");
            }
            finally
            {
                _savingToServer = false;
            }
        }, ct);
    }

    private BasicProfileDto BuildBasicProfileDto() => new(
        DisplayName: _displayName,
        Bio: _bio,
        Race: RaceValues[Math.Clamp(_raceIdx, 0, RaceValues.Length - 1)],
        Gender: GenderValues[Math.Clamp(_genderIdx, 0, GenderValues.Length - 1)],
        Region: RegionValues[Math.Clamp(_regionIdx, 0, RegionValues.Length - 1)],
        LanguageMask: MaskOr(LanguageValues, _langSelected, (a, b) => (Language)((short)a | (short)b)),
        ContentInterestMask: MaskOr(ContentInterestValues, _contentInterests, (a, b) => (ContentInterest)((int)a | (int)b)),
        LookingForMask: MaskOr(LookingForValues, _lookingFor, (a, b) => (LookingFor)((short)a | (short)b)),
        NsfwEnabled: _nsfwOptIn,
        Timezone: _timezoneIdx >= 0 && _timezoneIdx < AllTimezones.Length
            ? AllTimezones[_timezoneIdx].Id
            : string.Empty,
        FavoriteJob: _jobComboIdx >= 0 && _jobComboIdx < JobValues.Length
            ? JobValues[_jobComboIdx]
            : Job.None,
        FavoriteExpansion: _expansionIdx >= 0 && _expansionIdx < ExpansionValues.Length
            ? ExpansionValues[_expansionIdx]
            : Expansion.None,
        SpotifyTrackId: _spotifyTrackId,
        SpotifyTrackName: _spotifyTrackName,
        FavoriteMovie: _favoriteMovie,
        FavoriteAnime: _favoriteAnime,
        FavoriteFFCharacter: _favoriteFFCharacter,
        WeekdayHoursMask: HoursToMask(_weekdayHours),
        WeekendHoursMask: HoursToMask(_weekendHours),
        SyncTool: MaskOr(SyncToolValues, _syncToolsSelected,
            (a, b) => (SyncTool)((short)a | (short)b)));

    private FiltersDto BuildFiltersDto() => new(
        WantedRaceMask: MaskOr(RaceValues, _filterRaces, (a, b) => (Race)((short)a | (short)b)),
        WantedGenderMask: MaskOr(GenderValues, _filterGenders, (a, b) => (Gender)((short)a | (short)b)),
        WantedRegionMask: MaskOr(RegionValues, _filterRegions, (a, b) => (Region)((short)a | (short)b)),
        WantedLanguageMask: MaskOr(LanguageValues, _filterLanguages, (a, b) => (Language)((short)a | (short)b)));

    /// <summary>Parses a pasted Spotify URL/id and kicks off the title fetch (mirrors onboarding).</summary>
    private void ProcessSpotifyInput()
    {
        if (!SpotifyTrack.TryParseId(_spotifyInput, out var trackId))
        {
            _spotifyTrackId = ""; _spotifyTrackName = "";
            return;
        }
        _spotifyInput = trackId; // collapse a pasted URL down to the bare id in the box

        if (trackId == _spotifyTrackId)
        {
            return;
        }
        _spotifyTrackId = trackId;
        _spotifyTrackName = "";
        _spotifyFetching = true;
        _ = FetchSpotifyTitleAsync(trackId);
    }

    private async Task FetchSpotifyTitleAsync(string trackId)
    {
        try
        {
            _spotifyTrackName = await SpotifyTrack.FetchTrackLabelAsync(trackId).ConfigureAwait(false) ?? string.Empty;
        }
        catch
        {
            _spotifyTrackName = Loc.T("onboarding.opt_spotify_fetch_failed");
        }
        finally
        {
            _spotifyFetching = false;
        }
    }


    public void Draw()
    {
        // File-dialog and crop popup must run every frame before child windows.
        _imgFileDialog.Draw();
        _imgPendingPick.Poll();
        _imgCropPopup.Draw(ImGui.GetWindowPos(), ImGui.GetWindowSize());

        if (_savedTimer > 0f)
        {
            _savedTimer -= ImGui.GetIO().DeltaTime;
        }

        DrawTabStrip();

        switch (_activeTab)
        {
            case Tab.View:
                DrawViewTab();
                break;
            case Tab.Edit:
                DrawEditTab();
                break;
            case Tab.Images:
                DrawImagesTab();
                break;
        }
    }


    private static string[] TabLabels => new[]
    {
        Loc.T("profile.tab_view"), Loc.T("profile.tab_edit"), Loc.T("profile.tab_images"),
    };

    private void DrawTabStrip()
    {
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var availW = ImGui.GetContentRegionAvail().X;
        var TabH = Px(36f);
        var tabW = availW / 3f;

        for (int i = 0; i < 3; i++)
        {
            var isActive = _activeTab == (Tab)i;
            var label = TabLabels[i];
            var x = origin.X + i * tabW;

            if (isActive)
            {
                dl.AddRectFilled(
                    new Vector2(x, origin.Y),
                    new Vector2(x + tabW, origin.Y + TabH),
                    t.AccentWithAlpha(0.18f));
                dl.AddRectFilled(
                    new Vector2(x + Px(8f), origin.Y + TabH - Px(3f)),
                    new Vector2(x + tabW - Px(8f), origin.Y + TabH),
                    t.AccentU32, Px(2f));
            }

            var labelSz = ImGui.CalcTextSize(label);
            dl.AddText(
                new Vector2(x + (tabW - labelSz.X) * 0.5f, origin.Y + (TabH - labelSz.Y) * 0.5f),
                isActive ? 0xFFFFFFFF : 0xFFAAAAAA, label);

            ImGui.SetCursorScreenPos(new Vector2(x, origin.Y));
            ImGui.InvisibleButton($"##mypTab{i}", new Vector2(tabW, TabH));
            if (ImGui.IsItemClicked())
            {
                _activeTab = (Tab)i;
            }
        }

        dl.AddLine(
            new Vector2(origin.X, origin.Y + TabH),
            new Vector2(origin.X + availW, origin.Y + TabH),
            UiColors.Divider, 1f);

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + TabH + Px(6f)));
    }


    private void DrawViewTab()
    {
        if (_prevTab != Tab.View)
        {
            _profileScreen.SetMyProfile();
        }
        _prevTab = Tab.View;

        _profileScreen.Draw();
    }


    private void DrawEditTab()
    {
        if (_prevTab != Tab.Edit && !_editFormHydrated && !_editFormLoading)
        {
            LoadFromServer();
        }
        _prevTab = Tab.Edit;

        var t = ThemeService.Current;
        var w = ImGui.GetContentRegionAvail().X;
        var availH = ImGui.GetContentRegionAvail().Y;
        var SaveBarH = Px(48f);

        if (_editFormLoading && !_editFormHydrated)
        {
            Widgets.LoadingIndicator.Draw();
            return;
        }
        if (_editFormLoadError is not null && !_editFormHydrated)
        {
            ImGui.Spacing();
            ImGui.PushTextWrapPos(0f);
            ImGui.TextColored(new Vector4(0.95f, 0.45f, 0.45f, 1f),
                Loc.T("profile.load_profile_failed", _editFormLoadError));
            ImGui.PopTextWrapPos();
            ImGui.Spacing();
            if (ImGui.Button(Loc.T("profile.retry"), Px(120f, 28f)))
            {
                _editFormLoadError = null;
                LoadFromServer();
            }
            return;
        }

        using (var scroll = ImRaii.Child("##myProfEdit", new Vector2(0f, availH - SaveBarH), false))
        {
            if (scroll.Success)
            {
                DrawEditForm(t, w);
            }
        }

        ImGui.Separator();

        var savingNow = _savingToServer;
        var btnLabel = savingNow ? Loc.T("profile.saving")
                     : _savedTimer > 0f ? Loc.T("profile.saved")
                                            : Loc.T("profile.save_changes");
        var btnColor = _savedTimer > 0f ? new Vector4(0.22f, 0.60f, 0.28f, 1f) : t.ButtonNormal;
        var btnHover = _savedTimer > 0f ? new Vector4(0.22f, 0.60f, 0.28f, 1f) : t.ButtonHovered;

        ImGui.PushStyleColor(ImGuiCol.Button, btnColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, btnHover);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, t.ButtonActive);
        if (savingNow)
        {
            ImGui.BeginDisabled();
        }
        if (ImGui.Button(btnLabel, new Vector2(w - Px(6f), Px(32f))))
        {
            SaveToServer();
        }
        if (savingNow)
        {
            ImGui.EndDisabled();
        }
        ImGui.PopStyleColor(3);
    }

    /// <summary>Appends a picked emoji to the bio, but only while it keeps the user-visible length in bounds.</summary>
    private void InsertBioEmoji(string name)
    {
        var add = $":{name}: ";
        if (AetherLove.Shared.EmojiText.EffectiveLength(_bio + add) <= AetherLove.Shared.EmojiText.MaxBioLength)
        {
            _bio += add;
        }
    }

    private void DrawEditForm(ThemeDefinition t, float w)
    {
        var muted = new Vector4(0.55f, 0.55f, 0.55f, 0.75f);
        ImGui.Spacing();

        DrawSectionHeading(Loc.T("profile.heading_identity"), t);

        DrawFieldLabel(Loc.T("profile.display_name"), t);
        ImGui.SetNextItemWidth(Px(260f));
        ImGui.InputText("##edName", ref _displayName, 32);
        if (_displayName.Contains(' '))
        {
            _displayName = _displayName.Replace(" ", "");
        }
        ImGui.TextColored(muted, Loc.T("profile.display_name_hint"));
        ImGui.Spacing();

        DrawFieldLabel(Loc.T("profile.about_me"), t);
        ImGui.SameLine();
        {
            var iconH = ImGui.GetTextLineHeight();
            var grinTex = Plugin.EmojiService.GetEmoji("grinning")?.GetWrapOrDefault();
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Px(2f, 2f));
            var clicked = grinTex != null
                ? ImGui.ImageButton(grinTex.Handle, new Vector2(iconH - Px(4f)))
                : ImGui.SmallButton(":)##edBioEmoji");
            ImGui.PopStyleVar();
            _bioEmojiPicker.Draw();
            if (clicked)
            {
                _bioEmojiPicker.Open(InsertBioEmoji);
            }
        }
        ImGui.SetNextItemWidth(w - Px(8f));
        var bioBefore = _bio;
        ImGui.InputTextMultiline("##edBio", ref _bio, AetherLove.Shared.EmojiText.MaxBioRawLength,
            new Vector2(w - Px(8f), Px(68f)));
        // Lock the field at the user-visible limit: undo an edit that pushed it over.
        if (AetherLove.Shared.EmojiText.EffectiveLength(_bio) > AetherLove.Shared.EmojiText.MaxBioLength)
        {
            _bio = bioBefore;
        }

        var parsedBio = ParsedMessage.Parse(_bio);
        var effectiveLen = AetherLove.Shared.EmojiText.EffectiveLength(_bio);
        ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X);
        ImGui.TextColored(
            effectiveLen > AetherLove.Shared.EmojiText.MaxBioLength ? new Vector4(0.9f, 0.35f, 0.35f, 1f) : muted,
            Loc.T("profile.char_count", effectiveLen));
        ImGui.PopTextWrapPos();

        ImGui.Spacing();
        ImGui.TextColored(muted, Loc.T("profile.preview"));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.07f, 0.07f, 0.07f, 0.60f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, Px(4f));
        var previewW = w - Px(8f);
        // Height grows with content; subtract the child border before measuring.
        var previewH = _bio.Length > 0
            ? Math.Max(Px(44f), parsedBio.MeasureHeight(previewW - Px(4f)))
            : Px(44f);
        using (var prev = ImRaii.Child("##edBioPreview", new Vector2(previewW, previewH), true))
        {
            if (prev.Success)
            {
                if (_bio.Length > 0)
                {
                    ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X);
                    parsedBio.Draw();
                    ImGui.PopTextWrapPos();
                }
                else
                {
                    ImGui.TextColored(new Vector4(0.38f, 0.38f, 0.38f, 1f), Loc.T("profile.bio_placeholder"));
                }
            }
        }
        ImGui.PopStyleVar();
        ImGui.PopStyleColor();
        ImGui.Spacing();

        DrawSectionHeading(Loc.T("profile.heading_character"), t);

        DrawFieldLabel(Loc.T("profile.race"), t);
        ImGui.SameLine(Px(130f));
        DrawFieldLabel(Loc.T("profile.gender"), t);
        ImGui.SetNextItemWidth(Px(120f));
        var prevRaceIdx = _raceIdx;
        ImGui.Combo("##edRace", ref _raceIdx, Races, Races.Length);
        if (_raceIdx != prevRaceIdx && IsLalafell())
        {
            ClearAdultFlagsForLalafell();
        }
        ImGui.SameLine(Px(130f));
        ImGui.SetNextItemWidth(Px(180f));
        ImGui.Combo("##edGender", ref _genderIdx, Genders, Genders.Length);
        ImGui.Spacing();

        DrawSectionHeading(Loc.T("profile.heading_location"), t);

        DrawFieldLabel(Loc.T("profile.region"), t);
        ImGui.SetNextItemWidth(Px(230f));
        ImGui.Combo("##edRegion", ref _regionIdx, Regions, Regions.Length);
        ImGui.Spacing();

        DrawSectionHeading(Loc.T("profile.heading_languages"), t);

        ImGui.TextColored(muted, Loc.T("profile.languages_hint"));
        ImGui.Spacing();
        var LangCol = Px(160f);
        for (int i = 0; i < Languages.Length; i++)
        {
            if (i % 2 == 1)
            {
                ImGui.SameLine(LangCol);
            }
            ImGui.Checkbox($"{Languages[i]}##edLang{i}", ref _langSelected[i]);
        }
        ImGui.Spacing();

        DrawSectionHeading(Loc.T("profile.heading_content"), t);

        ImGui.TextColored(muted, Loc.T("profile.content_hint"));
        ImGui.Spacing();
        var ContentCol = Px(185f);
        for (int i = 0; i < ContentLabels.Length; i++)
        {
            if (i % 2 == 1)
            {
                ImGui.SameLine(ContentCol);
            }
            ImGui.Checkbox($"{ContentLabels[i]}##edCi{i}", ref _contentInterests[i]);
        }
        ImGui.Spacing();

        DrawSectionHeading(Loc.T("profile.heading_looking_for"), t);

        ImGui.TextColored(muted, Loc.T("profile.looking_for_hint"));
        ImGui.Spacing();
        var lalafell = IsLalafell();
        for (int i = 0; i < LookingForLabels.Length; i++)
        {
            if (lalafell && LookingForValues[i] == LookingFor.Erp)
            {
                continue;
            }
            ImGui.Checkbox($"{LookingForLabels[i]}##edLf{i}", ref _lookingFor[i]);
        }
        ImGui.Spacing();

        DrawSectionHeading(Loc.T("profile.heading_nsfw"), t);

        if (lalafell)
        {
            ImGui.PushTextWrapPos(0f);
            ImGui.TextColored(new Vector4(0.65f, 0.65f, 0.65f, 1f),
                Loc.T("profile.nsfw_lalafell"));
            ImGui.PopTextWrapPos();
        }
        else
        {
            ImGui.TextWrapped(Loc.T("profile.nsfw_explainer"));
            ImGui.Spacing();
            if (_nsfwOptIn)
            {
                ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.55f, 0.10f, 0.10f, 0.90f));
                ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.70f, 0.18f, 0.18f, 1.00f));
                ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0.40f, 0.06f, 0.06f, 1.00f));
                ImGui.Checkbox(Loc.T("profile.nsfw_optin"), ref _nsfwOptIn);
                ImGui.PopStyleColor(3);
            }
            else
            {
                ImGui.Checkbox(Loc.T("profile.nsfw_optin"), ref _nsfwOptIn);
            }
        }
        ImGui.Spacing();

        DrawSectionHeading(Loc.T("profile.heading_optional"), t);

        ImGui.Text(Loc.T("profile.favourite_job"));
        ImGui.SameLine(); HelpTooltip(Loc.T("profile.favourite_job_tooltip"));
        if (EnsureJobCombo().Draw(_jobComboIdx, out var newJob))
        {
            _jobComboIdx = newJob;
        }
        ImGui.Spacing();

        ImGui.Text(Loc.T("profile.favourite_expansion"));
        ImGui.SetNextItemWidth(Px(240f));
        ImGui.Combo("##edExp", ref _expansionIdx, Expansions, Expansions.Length);
        ImGui.Spacing();

        ImGui.Text(Loc.T("profile.favourite_spotify"));
        ImGui.SameLine(); HelpTooltip(Loc.T("profile.spotify_tooltip"));
        ImGui.TextColored(muted, SpotifyTrack.DisplayPrefix);
        ImGui.SameLine(0f, Px(0f));
        ImGui.SetNextItemWidth(Px(160f));
        if (ImGui.InputText("##edSpotify", ref _spotifyInput, 256))
        {
            ProcessSpotifyInput();
        }
        if (_spotifyFetching)
        {
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), Loc.T("onboarding.opt_spotify_fetching"));
        }
        else if (_spotifyTrackName.Length > 0)
        {
            ImGui.PushTextWrapPos(0f);
            ImGui.TextColored(new Vector4(0.35f, 0.85f, 0.45f, 1f), $"  {_spotifyTrackName}");
            ImGui.PopTextWrapPos();
        }
        else if (_spotifyTrackId.Length > 0)
        {
            ImGui.TextColored(muted, Loc.T("profile.track_id", _spotifyTrackId));
        }
        ImGui.Spacing();

        ImGui.Text(Loc.T("profile.favourite_movie"));
        ImGui.SetNextItemWidth(Math.Min(w - Px(8f), Px(280f)));
        ImGui.InputText("##edMovie", ref _favoriteMovie, 128);
        ImGui.Spacing();

        ImGui.Text(Loc.T("profile.favourite_anime"));
        ImGui.SetNextItemWidth(Math.Min(w - Px(8f), Px(280f)));
        ImGui.InputText("##edAnime", ref _favoriteAnime, 128);
        ImGui.Spacing();

        ImGui.Text(Loc.T("profile.favourite_ff_character_full"));
        ImGui.SetNextItemWidth(Math.Min(w - Px(8f), Px(280f)));
        ImGui.InputText("##edFFChar", ref _favoriteFFCharacter, 128);
        ImGui.Spacing();

        DrawSectionHeading(Loc.T("profile.heading_playtime"), t);

        ImGui.TextColored(muted, Loc.T("profile.weekday_playtimes_edit"));
        ImGui.Spacing();
        DrawOnlineHoursEditor(w - Px(8f), _weekdayHours, "mpewd");
        ImGui.Spacing();
        ImGui.Spacing();

        ImGui.TextColored(muted, Loc.T("profile.weekend_playtimes_edit"));
        ImGui.Spacing();
        DrawOnlineHoursEditor(w - Px(8f), _weekendHours, "mpewe");
        ImGui.Spacing();

        DrawSectionHeading(Loc.T("profile.heading_timezone"), t);

        ImGui.SetNextItemWidth(w - Px(8f));
        ImGui.Combo("##edTz", ref _timezoneIdx,
            TimezoneNames, TimezoneNames.Length);
        ImGui.Spacing();

        DrawSectionHeading(Loc.T("profile.heading_sync_tool"), t);

        ImGui.TextColored(muted, Loc.T("profile.sync_tool_hint"));
        ImGui.Spacing();
        var syncCol = Px(170f);
        for (int i = 0; i < SyncToolLabels.Length; i++)
        {
            if (i % 2 == 1)
            {
                ImGui.SameLine(syncCol);
            }
            ImGui.Checkbox($"{SyncToolLabels[i]}##edSync{i}", ref _syncToolsSelected[i]);
        }
        ImGui.Spacing();

        DrawSectionHeading(Loc.T("profile.heading_match_prefs"), t);

        ImGui.TextWrapped(Loc.T("profile.match_prefs_body"));
        ImGui.Spacing();

        DrawFieldLabel(Loc.T("profile.race"), t);
        ImGui.SameLine(0, Px(10f));
        if (ImGui.SmallButton($"{Loc.T("profile.all")}##allRace"))
        {
            for (int i = 0; i < _filterRaces.Length; i++)
            {
                _filterRaces[i] = true;
            }
        }
        ImGui.SameLine(0, Px(4f));
        if (ImGui.SmallButton($"{Loc.T("profile.none")}##noneRace"))
        {
            for (int i = 0; i < _filterRaces.Length; i++)
            {
                _filterRaces[i] = false;
            }
        }

        var RaceCol = Px(155f);
        for (int i = 0; i < Races.Length; i++)
        {
            if (i % 2 == 1)
            {
                ImGui.SameLine(RaceCol);
            }
            ImGui.Checkbox($"{Races[i]}##fr{i}", ref _filterRaces[i]);
        }
        if (!_filterRaces.Any(x => x))
        {
            ImGui.TextColored(muted, Loc.T("profile.filter_any_race"));
        }
        ImGui.Spacing();
        ImGui.Spacing();

        DrawFieldLabel(Loc.T("profile.gender"), t);
        ImGui.SameLine(0, Px(10f));
        if (ImGui.SmallButton($"{Loc.T("profile.all")}##allGender"))
        {
            for (int i = 0; i < _filterGenders.Length; i++)
            {
                _filterGenders[i] = true;
            }
        }
        ImGui.SameLine(0, Px(4f));
        if (ImGui.SmallButton($"{Loc.T("profile.none")}##noneGender"))
        {
            for (int i = 0; i < _filterGenders.Length; i++)
            {
                _filterGenders[i] = false;
            }
        }

        var GenderCol = Px(155f);
        for (int i = 0; i < Genders.Length; i++)
        {
            if (i % 2 == 1)
            {
                ImGui.SameLine(GenderCol);
            }
            ImGui.Checkbox($"{Genders[i]}##fg{i}", ref _filterGenders[i]);
        }
        if (!_filterGenders.Any(x => x))
        {
            ImGui.TextColored(muted, Loc.T("profile.filter_any_gender"));
        }
        ImGui.Spacing();
        ImGui.Spacing();

        DrawFieldLabel(Loc.T("profile.region"), t);
        ImGui.SameLine(0, Px(10f));
        if (ImGui.SmallButton($"{Loc.T("profile.all")}##allRegion"))
        {
            for (int i = 0; i < _filterRegions.Length; i++)
            {
                _filterRegions[i] = true;
            }
        }
        ImGui.SameLine(0, Px(4f));
        if (ImGui.SmallButton($"{Loc.T("profile.none")}##noneRegion"))
        {
            for (int i = 0; i < _filterRegions.Length; i++)
            {
                _filterRegions[i] = false;
            }
        }

        var RegionCol = Px(155f);
        for (int i = 0; i < _filterRegions.Length; i++)
        {
            if (i % 2 == 1)
            {
                ImGui.SameLine(RegionCol);
            }
            ImGui.Checkbox($"{Regions[i]}##fR{i}", ref _filterRegions[i]);
        }
        if (!_filterRegions.Any(x => x))
        {
            ImGui.TextColored(muted, Loc.T("profile.filter_any_region"));
        }
        ImGui.Spacing();
        ImGui.Spacing();

        DrawFieldLabel(Loc.T("profile.spoken_language"), t);
        ImGui.SameLine(); HelpTooltip(Loc.T("profile.spoken_language_tooltip"));
        ImGui.SameLine(0, Px(10f));
        if (ImGui.SmallButton($"{Loc.T("profile.clear")}##clrLang"))
        {
            for (int i = 0; i < _filterLanguages.Length; i++)
            {
                _filterLanguages[i] = false;
            }
        }
        ImGui.Spacing();
        for (int i = 0; i < Languages.Length; i++)
        {
            if (i % 2 == 1)
            {
                ImGui.SameLine(LangCol);
            }
            ImGui.Checkbox($"{Languages[i]}##fL{i}", ref _filterLanguages[i]);
        }
        if (!_filterLanguages.Any(x => x))
        {
            ImGui.TextColored(muted, Loc.T("profile.filter_any_language"));
        }
        ImGui.Spacing();
    }


}
