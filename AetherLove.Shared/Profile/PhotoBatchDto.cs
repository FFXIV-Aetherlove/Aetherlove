using MessagePack;

namespace AetherLove.Shared.Profile;

/// <summary>One photo's data inside a <see cref="PhotoBatchDto"/>.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record PhotoUploadDto(
    string Base64,
    int CropX,
    int CropY,
    int CropWidth,
    int CropHeight,
    bool IsNsfw);

/// <summary>Onboarding step 2: avatar + main portrait + up to 3 extras. Null slot = leave server copy alone.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record PhotoBatchDto(
    PhotoUploadDto? Avatar,
    PhotoUploadDto? Main,
    PhotoUploadDto? Extra1,
    PhotoUploadDto? Extra2,
    PhotoUploadDto? Extra3);
