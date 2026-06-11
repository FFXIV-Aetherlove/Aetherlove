using MessagePack;

namespace AetherLove.Shared.Profile;

/// <summary>One previously-saved photo returned by <c>GetOnboardingStateAsync</c>.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record OnboardingPhotoDto(
    int Order,
    bool IsNsfw,
    byte[] WebpBytes);

/// <summary>Snapshot of the signed-in user's in-progress onboarding state.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record OnboardingStateDto(
    BasicProfileDto Basic,
    FiltersDto Filters,
    OnboardingPhotoDto[] Photos);
