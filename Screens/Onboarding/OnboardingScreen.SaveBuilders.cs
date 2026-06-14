using System;
using System.IO;
using System.Numerics;
using AetherLove.Shared.Profile;
using AetherLove.Shared.Profile.Enums;

namespace AetherLove.Screens;

public partial class OnboardingScreen
{
    private BasicProfileDto BuildBasicProfile()
    {
        return new BasicProfileDto(
            DisplayName: _displayName,
            Bio: _bio,
            Race: ValueAt(RaceValues, _raceIdx, Race.Hyur),
            Gender: ValueAt(GenderValues, _genderIdx, Gender.Male),
            Region: ValueAt(RegionValues, _regionIdx, Region.PreferNotToSay),
            LanguageMask: MaskOr(LanguageValues, _langSelected, (a, b) => (Language)((short)a | (short)b)),
            ContentInterestMask: MaskOr(ContentInterestValues, _contentInterests, (a, b) => (ContentInterest)((int)a | (int)b)),
            LookingForMask: MaskOr(LookingForValues, _lookingFor, (a, b) => (LookingFor)((short)a | (short)b)),
            NsfwEnabled: _nsfwOptIn,
            Timezone: _timezoneIdx < AllTimezones.Length
                ? AllTimezones[_timezoneIdx].Id
                : TimeZoneInfo.Local.Id,
            FavoriteJob: ValueAt(JobValues, _jobComboIdx, Job.None),
            FavoriteExpansion: ValueAt(ExpansionValues, _expansionIdx, Expansion.None),
            SpotifyTrackId: MusicFields[0].Input,
            SpotifyTrackName: MusicFields[0].ResolvedName,
            SoundCloudUrl: MusicFields[1].Input,
            SoundCloudName: MusicFields[1].ResolvedName,
            AppleMusicUrl: MusicFields[2].Input,
            AppleMusicName: MusicFields[2].ResolvedName,
            YouTubeMusicUrl: MusicFields[3].Input,
            YouTubeMusicName: MusicFields[3].ResolvedName,
            FavoriteMovie: _favoriteMovie,
            FavoriteAnime: _favoriteAnime,
            FavoriteFFCharacter: _favoriteFFCharacter,
            WeekdayHoursMask: HoursToMask(_weekdayHours),
            WeekendHoursMask: HoursToMask(_weekendHours),
            SyncTool: MaskOr(SyncToolValues, _syncToolsSelected, (a, b) => (SyncTool)((short)a | (short)b)));
    }

    // Gathers the five photo slots (avatar, main, three extras) into one upload batch. A slot only
    // contributes bytes when the user picked a new local image for it: slots that are unconfirmed, or
    // that are unchanged from what the server already has, are sent as null and left untouched server-side.
    private PhotoBatchDto BuildPhotoBatch()
    {
        PhotoUploadDto? avatar = (_avatarConfirmed, _avatarFromServer) switch
        {
            (true, true)  => null,
            (true, false) => ReadPhotoUpload(_avatarPath, _avatarCropRect, isNsfw: false),
            _             => null,
        };
        PhotoUploadDto? main = (_photos[0].Confirmed, _photos[0].FromServer) switch
        {
            (true, true)  => null,
            (true, false) => ReadPhotoUpload(_photos[0].Path, _photos[0].CropRect, isNsfw: false),
            _             => null,
        };
        var extra1 = BuildExtraSlot(_photos[1]);
        var extra2 = BuildExtraSlot(_photos[2]);
        var extra3 = BuildExtraSlot(_photos[3]);

        return new PhotoBatchDto(avatar, main, extra1, extra2, extra3);
    }

    private PhotoUploadDto? BuildExtraSlot(PhotoSlot slot)
    {
        if (!slot.Confirmed)
        {
            return null;
        }
        if (slot.FromServer)
        {
            return null;
        }
        return ReadPhotoUpload(slot.Path, slot.CropRect, slot.Declaration == PhotoNsfwDecl.Nsfw);
    }

    private FiltersDto BuildFilters()
    {
        return new FiltersDto(
            WantedRaceMask: MaskOr(RaceValues, _filterRaces, (a, b) => (Race)((short)a | (short)b)),
            WantedGenderMask: MaskOr(GenderValues, _filterGenders, (a, b) => (Gender)((short)a | (short)b)),
            WantedRegionMask: MaskOr(RegionValues, _filterRegions, (a, b) => (Region)((short)a | (short)b)),
            WantedLanguageMask: MaskOr(LanguageValues, _filterLanguages, (a, b) => (Language)((short)a | (short)b)));
    }

}
