using System;
using System.IO;
using System.Numerics;
using AetherLove.Shared;
using AetherLove.Shared.Profile;
using AetherLove.Shared.Profile.Enums;

namespace AetherLove.Screens;

// Pre-fills the wizard's form fields from a returning user's saved profile.
public partial class OnboardingScreen
{
    private const string ResumePhotoCacheDir = "OnboardingResumeCache";

    private void HydrateFromOnboardingState(OnboardingStateDto state)
    {
        // Race.None signals untouched; preserve auto-detected values instead of overwriting with defaults.
        if (state.Basic.Race != Race.None)
        {
            HydrateBasic(state.Basic);
            HydrateFilters(state.Filters);
        }
        HydratePhotos(state.Photos);
    }

    private void HydrateBasic(BasicProfileDto b)
    {
        _displayName = b.DisplayName ?? string.Empty;
        _bio         = b.Bio ?? string.Empty;

        _raceIdx       = IndexOf(RaceValues, b.Race, fallback: 0);
        _genderIdx     = IndexOf(GenderValues, b.Gender, fallback: 0);
        _regionIdx     = IndexOf(RegionValues, b.Region, fallback: 0);
        _expansionIdx  = IndexOf(ExpansionValues, b.FavoriteExpansion, fallback: 0);
        _jobComboIdx   = IndexOf(JobValues, b.FavoriteJob, fallback: 0);

        MaskToBools(LanguageValues, b.LanguageMask,
            (a, m) => ((short)a & (short)m) != 0, _langSelected);
        MaskToBools(ContentInterestValues, b.ContentInterestMask,
            (a, m) => ((int)a & (int)m) != 0, _contentInterests);
        MaskToBools(LookingForValues, b.LookingForMask,
            (a, m) => ((short)a & (short)m) != 0, _lookingFor);
        MaskToBools(SyncToolValues, b.SyncTool,
            (a, m) => ((short)a & (short)m) != 0, _syncToolsSelected);

        _nsfwOptIn         = b.NsfwEnabled;
        MusicFields[0].Hydrate(b.SpotifyTrackId, b.SpotifyTrackName);
        MusicFields[1].Hydrate(b.SoundCloudUrl, b.SoundCloudName);
        MusicFields[2].Hydrate(b.AppleMusicUrl, b.AppleMusicName);
        MusicFields[3].Hydrate(b.YouTubeMusicUrl, b.YouTubeMusicName);
        _favoriteMovie     = b.FavoriteMovie ?? string.Empty;
        _favoriteAnime     = b.FavoriteAnime ?? string.Empty;
        _favoriteFFCharacter = b.FavoriteFFCharacter ?? string.Empty;

        MaskToHours(b.WeekdayHoursMask, _weekdayHours);
        MaskToHours(b.WeekendHoursMask, _weekendHours);

        if (IsLalafellSelected())
        {
            ClearAdultFlagsForLalafell();
        }
    }

    private void HydrateFilters(FiltersDto f)
    {
        MaskToBools(RaceValues, f.WantedRaceMask,
            (a, m) => ((short)a & (short)m) != 0, _filterRaces);
        MaskToBools(GenderValues, f.WantedGenderMask,
            (a, m) => ((short)a & (short)m) != 0, _filterGenders);
        for (int i = 0; i < _filterRegions.Length; i++)
        {
            _filterRegions[i] = ((short)RegionValues[i] & (short)f.WantedRegionMask) != 0;
        }
        MaskToBools(LanguageValues, f.WantedLanguageMask,
            (a, m) => ((short)a & (short)m) != 0, _filterLanguages);
    }

    private void HydratePhotos(OnboardingPhotoDto[] photos)
    {
        var cacheDir = Path.Combine(UiHost.PluginInterface.ConfigDirectory.FullName, ResumePhotoCacheDir);
        Directory.CreateDirectory(cacheDir);

        foreach (var photo in photos)
        {
            try
            {
                var path = Path.Combine(cacheDir, $"slot_{photo.Order}{AetherLove.Services.ImageFormat.ExtensionFor(photo.WebpBytes)}");
                File.WriteAllBytes(path, photo.WebpBytes);

                if (photo.Order == 0)
                {
                    HydrateAvatarFromPath(path);
                }
                else
                {
                    var slotIdx = photo.Order - 1;
                    if (slotIdx >= 0 && slotIdx < _photos.Length)
                    {
                        HydratePhotoSlotFromPath(_photos[slotIdx], path, photo.Order, photo.IsNsfw);
                    }
                }
            }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, $"[Onboarding] Failed to hydrate photo slot {photo.Order}.");
            }
        }
    }

    private void HydrateAvatarFromPath(string path)
    {
        _avatarPath = path;
        _avatarHandle = UiHost.TextureProvider.GetFromFile(path);
        _avatarCropRect = new Vector4(0f, 0f, PhotoSpec.AvatarSize, PhotoSpec.AvatarSize);
        _avatarConfirmed = true;
        _avatarFromServer = true;
    }

    private void HydratePhotoSlotFromPath(PhotoSlot slot, string path, int order, bool isNsfw)
    {
        slot.Path = path;
        slot.Handle = UiHost.TextureProvider.GetFromFile(path);
        slot.CropRect = new Vector4(0f, 0f, PhotoSpec.PortraitWidth, PhotoSpec.PortraitHeight);
        slot.Confirmed = true;
        slot.FromServer = true;
        slot.Declaration = isNsfw ? PhotoNsfwDecl.Nsfw : PhotoNsfwDecl.Sfw;
    }
}

