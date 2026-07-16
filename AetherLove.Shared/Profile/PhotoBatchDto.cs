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

/// <summary>Avatar + main portrait + extras. Null slot = leave server copy alone. Extra4/Extra5 are the
/// supporter-only slots (trailing defaults keep the wire shape compatible); the server rejects them for
/// non-supporters.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record PhotoBatchDto(
    PhotoUploadDto? Avatar,
    PhotoUploadDto? Main,
    PhotoUploadDto? Extra1,
    PhotoUploadDto? Extra2,
    PhotoUploadDto? Extra3,
    PhotoUploadDto? Extra4 = null,
    PhotoUploadDto? Extra5 = null);
