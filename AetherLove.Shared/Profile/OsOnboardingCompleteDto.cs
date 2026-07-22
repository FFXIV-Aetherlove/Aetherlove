using MessagePack;

namespace AetherLove.Shared.Profile;

/// <summary>Marks the account's OS onboarding complete and sets its OS identity. Sent by the AetherOS shell when
/// the human finishes the OS onboarding flow (design/passphrase/name/ToS).</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record OsOnboardingCompleteDto(string OsDisplayName);
