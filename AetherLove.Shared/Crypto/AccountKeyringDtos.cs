using MessagePack;
using AetherLove.Shared.Profile;

namespace AetherLove.Shared.Crypto;

[MessagePackObject(keyAsPropertyName: true)]
public sealed record RootWrapDto(string Kind, string KeyId, byte[] Ciphertext, byte[] Nonce,
    AccountPassphraseDto? Passphrase = null);

[MessagePackObject(keyAsPropertyName: true)]
public sealed record AccountKeyringDto(Guid AccountId, Guid RootId, long Revision, Guid OperationId,
    byte[] Ciphertext, byte[] Nonce, RootWrapDto[] Wraps, bool BackupSaved);

[MessagePackObject(keyAsPropertyName: true)]
public sealed record SaveAccountKeyringRequest(AccountKeyringDto Keyring, long ExpectedRevision,
    byte[] WriteSecret, AccountPassphraseDto? NewPassphrase = null, long ExpectedGeneration = 0);

[MessagePackObject(keyAsPropertyName: true)]
public sealed record KeyringIdentityUpload(string Surface, Guid OwnerId, Guid RootId, long Generation,
    byte[] WriteSecret, Messaging.KeyBundleDto Bundle);

[MessagePackObject(keyAsPropertyName: true)]
public sealed record LegacyIdentityDto(string Surface, Guid OwnerId, bool Active, Messaging.KeyBundleDto Bundle);

public enum EncryptionState { Offline, UnlockRequired, Ready, MigrationIncomplete, RecoveryRequired, CorruptBundle }

public sealed record KeyringIdentity(string Surface, Guid OwnerId, byte[] PublicKey, byte[] PrivateKey)
{
    public string KeyId => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(PublicKey));
}

public sealed record KeyringGroupKey(Guid GroupId, int Epoch, byte[] Key);

public sealed class AccountKeyringContents
{
    public int Version { get; set; } = 1;
    public Guid AccountId { get; set; }
    public Guid RootId { get; set; }
    public byte[] WriteSecret { get; set; } = [];
    public List<KeyringIdentity> Identities { get; set; } = [];
    public List<KeyringIdentity> UnmatchedLocal { get; set; } = [];
    public List<KeyringGroupKey> Groups { get; set; } = [];
    public List<string> Unresolved { get; set; } = [];
    public List<byte[]> LegacyKeks { get; set; } = [];
}

public static class KeyEnvelopeLimits
{
    public const int MaxKeyringBytes = 4 * 1024 * 1024;
    public const int MaxKdfCandidates = 8;

    public static bool ValidKdf(byte[]? salt, int memoryKb, int iterations, int parallelism)
        => salt is { Length: >= 16 and <= 64 } && memoryKb is >= 8 and <= 262144
            && iterations is >= 1 and <= 6 && parallelism is >= 1 and <= 4 && memoryKb >= 8 * parallelism;

    public static bool ValidBundle(Messaging.KeyBundleDto b)
        => b is not null && b.PublicKey is { Length: 32 } && b.EncryptedPrivateKey is { Length: 48 }
            && b.WrapNonce is { Length: 12 } && b.KdfSalt is { Length: >= 16 and <= 64 }
            && ((b.KdfMemoryKb == 0 && b.KdfIterations == 0 && b.KdfParallelism == 0)
                || ValidKdf(b.KdfSalt, b.KdfMemoryKb, b.KdfIterations, b.KdfParallelism))
            && ((b.ProfileWrappedPrivateKey is null && b.ProfileWrapNonce is null)
                || (b.ProfileWrappedPrivateKey is { Length: 48 } && b.ProfileWrapNonce is { Length: 12 }));

    public static bool ValidPassphrase(AccountPassphraseDto p)
        => ValidKdf(p.KdfSalt, p.KdfMemoryKb, p.KdfIterations, p.KdfParallelism)
            && p.Verifier is { Length: 48 } && p.VerifierNonce is { Length: 12 };
}
