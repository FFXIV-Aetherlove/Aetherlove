using System;
using System.IO;
using System.Numerics;
using AetherLove.Shared;
using AetherLove.Shared.Profile;

namespace AetherLove.Screens;

// Hydration half of the onboarding screen: takes the profile a returning user already saved on the
// server (their basic info, filters, and photos) and pre-fills the wizard's form fields with it, so
// resuming onboarding shows what they had instead of an empty form.
public partial class OnboardingScreen
{
    private const string ResumePhotoCacheDir = "OnboardingResumeCache";

    private void HydrateFromOnboardingState(OnboardingStateDto state)
    {
        HydrateBasic(state.Basic);
        HydrateFilters(state.Filters);
        HydratePhotos(state.Photos);
    }

    private void HydrateBasic(BasicProfileDto b)
    {
        _displayName = b.DisplayName ?? string.Empty;
        _bio         = b.Bio ?? string.Empty;

        _raceIdx       = IndexOf(RaceValues, b.Race, fallback: 0);
        _genderIdx     = IndexOf(GenderValues, b.Gender, fallback: 0);
        _regionIdx     = IndexOf(RegionValues, b.Region, fallback: RegionValues.Length - 1);
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
        _spotifyTrackId    = b.SpotifyTrackId ?? string.Empty;
        _spotifyTrackName  = b.SpotifyTrackName ?? string.Empty;
        _favoriteMovie     = b.FavoriteMovie ?? string.Empty;
        _favoriteAnime     = b.FavoriteAnime ?? string.Empty;
        _favoriteFFCharacter = b.FavoriteFFCharacter ?? string.Empty;

        MaskToHours(b.WeekdayHoursMask, _weekdayHours);
        MaskToHours(b.WeekendHoursMask, _weekendHours);

        // Scrub adult flags when the hydrated race is Lalafell.
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
        // _filterRegions excludes "Prefer not to say" (last entry).
        for (int i = 0; i < _filterRegions.Length; i++)
        {
            _filterRegions[i] = ((short)RegionValues[i] & (short)f.WantedRegionMask) != 0;
        }
        MaskToBools(LanguageValues, f.WantedLanguageMask,
            (a, m) => ((short)a & (short)m) != 0, _filterLanguages);
    }

    private void HydratePhotos(OnboardingPhotoDto[] photos)
    {
        var cacheDir = Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, ResumePhotoCacheDir);
        Directory.CreateDirectory(cacheDir);

        foreach (var photo in photos)
        {
            try
            {
                var path = Path.Combine(cacheDir, $"slot_{photo.Order}.webp");
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
                Plugin.Log.Warning(ex, $"[Onboarding] Failed to hydrate photo slot {photo.Order}.");
            }
        }
    }

    private void HydrateAvatarFromPath(string path)
    {
        _avatarPath = path;
        _avatarHandle = Plugin.TextureProvider.GetFromFile(path);
        _avatarCropRect = new Vector4(0f, 0f, PhotoSpec.AvatarSize, PhotoSpec.AvatarSize);
        _avatarConfirmed = true;
        _avatarFromServer = true;
    }

    private void HydratePhotoSlotFromPath(PhotoSlot slot, string path, int order, bool isNsfw)
    {
        slot.Path = path;
        slot.Handle = Plugin.TextureProvider.GetFromFile(path);
        slot.CropRect = new Vector4(0f, 0f, PhotoSpec.PortraitWidth, PhotoSpec.PortraitHeight);
        slot.Confirmed = true;
        slot.FromServer = true;
        slot.Declaration = isNsfw ? PhotoNsfwDecl.Nsfw : PhotoNsfwDecl.Sfw;
    }
}

