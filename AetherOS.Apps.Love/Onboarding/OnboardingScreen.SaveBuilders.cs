using System;
using System.Numerics;
using AetherLove.Shared;
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
            Region: ValueAt(RegionValues, _regionIdx, Region.NorthAmerica),
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

    /// <summary>One slot's upload inputs, snapshotted on the UI thread.</summary>
    private readonly record struct PhotoInput(bool Send, string Path, Vector4 Crop, bool IsNsfw, PhotoKind Kind);

    // Unconfirmed slots and slots unchanged from the server copy are sent as null and left untouched server-side.
    private PhotoInput[] SnapshotPhotoInputs()
    {
        var inputs = new PhotoInput[5];
        inputs[0] = _avatarConfirmed && !_avatarFromServer
            ? new PhotoInput(true, _avatarPath, _avatarCropRect, false, PhotoKind.Avatar)
            : default;
        for (var i = 0; i < 4; i++)
        {
            var slot = _photos[i];
            var send = slot.Confirmed && !slot.FromServer;
            // _photos[0] is the main portrait (always SFW); _photos[1..3] are extras with a declaration.
            var isNsfw = i >= 1 && slot.Declaration == PhotoNsfwDecl.Nsfw;
            inputs[i + 1] = send
                ? new PhotoInput(true, slot.Path, slot.CropRect, isNsfw, PhotoKind.Portrait)
                : default;
        }
        return inputs;
    }

    // CPU-bound; call off the UI thread.
    private PhotoBatchDto BuildPhotoBatch(PhotoInput[] inputs)
    {
        static PhotoUploadDto? Make(PhotoInput inp) =>
            inp.Send ? ReadPhotoUpload(inp.Path, inp.Crop, inp.IsNsfw, inp.Kind) : null;

        return new PhotoBatchDto(
            Make(inputs[0]), Make(inputs[1]), Make(inputs[2]), Make(inputs[3]), Make(inputs[4]));
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
