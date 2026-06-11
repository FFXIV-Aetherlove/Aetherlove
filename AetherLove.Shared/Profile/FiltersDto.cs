using AetherLove.Shared.Profile.Enums;
using MessagePack;

namespace AetherLove.Shared.Profile;

/// <summary>Onboarding step 3: match preferences. Empty bitmask = no preference.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record FiltersDto(
    Race WantedRaceMask,
    Gender WantedGenderMask,
    Region WantedRegionMask,
    Language WantedLanguageMask);
