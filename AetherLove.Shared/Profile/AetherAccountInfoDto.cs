using System;
using MessagePack;

namespace AetherLove.Shared.Profile;

/// <summary>Account-level OS shell identity snapshot, independent of any dating profile; see <see cref="AetherConnectionDto"/> for profile snapshot.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record AetherAccountInfoDto(
    Guid AccountId,
    string OsDisplayName,
    string? CharacterName,
    string? HomeWorld,
    string? DataCenter,
    long PrimaryLodestoneId,
    UserRole Role,
    bool IsSupporter,
    bool OsOnboarded,
    // Signals a fresh device to gate unlock on passphrase before unwrapping keys.
    bool HasPassphrase,
    int ProfileCount,
    // OS avatar image (WebP or JPEG for older clients); trailing default for wire-safety.
    byte[]? OsAvatarWebp = null,
    // Account-wide ban: when set, the shell gates every server-backed app and shows the reason. Distinct from a
    // per-profile AetherLove ban, which surfaces via AetherConnectionDto.Status/BanReason.
    bool AccountDisabled = false,
    string? AccountDisabledReason = null,
    // Account-level staff notices (the OS track), newest first. Disjoint from AetherConnectionDto.Warnings /
    // .ModeratorMessages, which carry only the profile-sourced AetherLove track. Null means "none".
    WarningDto[]? StaffWarnings = null,
    ModeratorMessageDto[]? StaffMessages = null);
