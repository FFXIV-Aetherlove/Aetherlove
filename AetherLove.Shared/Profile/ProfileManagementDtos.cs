using System;
using MessagePack;

namespace AetherLove.Shared.Profile;

/// <summary>One of an account's AetherLove profiles, for the profile switcher.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record ProfileSummaryDto(
    Guid ProfileId,
    string DisplayName,
    ProfileLifecycle Status,
    // 100x100 avatar for the switcher tile; null when the profile has no avatar yet (onboarding).
    byte[]? Avatar,
    // Past the account's allowance (supporter lapsed): rendered with the supporter gate, not selectable.
    bool Locked = false,
    // Picker badge counts, computed per profile so inactive siblings show their pending activity.
    int NewMatches = 0,
    int UnreadChats = 0);

/// <summary>The account's profiles plus which one the caller's token is currently acting as.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record ProfileListDto(
    ProfileSummaryDto[] Profiles,
    Guid ActiveProfileId);

/// <summary>Create a new AetherLove profile under the caller's account. The profile starts in Onboarding and is
/// filled in by the AetherLove first-run; the display name is seeded from the OS identity but editable there.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record CreateProfileRequest(string DisplayName);

/// <summary>The account passphrase KEK parameters: Argon2id KDF inputs plus a verifier (a known constant
/// AES-GCM-wrapped under the KEK) so a new device can validate the passphrase before unwrapping any profile
/// key. One per account; every profile's key bundle is wrapped by this single KEK.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record AccountPassphraseDto(
    byte[] KdfSalt,
    int KdfMemoryKb,
    int KdfIterations,
    int KdfParallelism,
    byte[] Verifier,
    byte[] VerifierNonce);

/// <summary>A fresh key bundle for one of the account's profiles, part of a passphrase reset.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record ProfileBundleUpload(Guid ProfileId, Messaging.KeyBundleDto Bundle);

/// <summary>The lost-passphrase recovery: a NEW passphrase (params + verifier) plus freshly generated
/// keypairs for every live profile and, when the account uses the Messenger, the account keypair. The old
/// private keys are unrecoverable by design; the old PUBLIC keys stay server-side as history so peers keep
/// reading pre-reset messages. The resetter cannot.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record ResetPassphraseRequest(
    AccountPassphraseDto Passphrase,
    ProfileBundleUpload[] ProfileBundles,
    Messaging.KeyBundleDto? AccountBundle);
