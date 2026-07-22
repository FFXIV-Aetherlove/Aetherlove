using AetherLove.Config;
namespace AetherLove.Services.Crypto;

/// <summary>Holds the unwrapped X25519 keypair, persisted via <c>Configuration.Crypto</c>.</summary>
public sealed class KeyStorageService
{
    private readonly Configuration _config;

    public KeyStorageService(Configuration config)
    {
        _config = config;
    }

    public bool HasLocalKey =>
        _config.Crypto.PrivateKey.Length == CryptoService.X25519KeyLength &&
        _config.Crypto.PublicKey.Length == CryptoService.X25519KeyLength;

    public byte[]? GetPrivateKey()
        => HasLocalKey ? _config.Crypto.PrivateKey : null;

    public byte[]? GetPublicKey()
        => HasLocalKey ? _config.Crypto.PublicKey : null;

    public void Store(byte[] publicKey, byte[] privateKey)
    {
        _config.Crypto = new CryptoKeys
        {
            PublicKey = publicKey,
            PrivateKey = privateKey,
        };
        _config.Save();
    }

    /// <summary>The account passphrase KEK, captured at passphrase entry so sibling-profile bundles can be
    /// wrapped/unwrapped without re-prompting. Null when not captured yet.</summary>
    public byte[]? Kek => _config.AccountKek.Length == CryptoService.AesGcmKeyLength ? _config.AccountKek : null;

    public void StoreKek(byte[] kek)
    {
        _config.AccountKek = kek;
        _config.Save();
    }

    /// <summary>The account-level messenger keypair, unwrapped. Null when not provisioned on this device yet.</summary>
    public (byte[] PublicKey, byte[] PrivateKey)? AccountKeys =>
        _config.AccountCrypto.PrivateKey.Length == CryptoService.X25519KeyLength &&
        _config.AccountCrypto.PublicKey.Length == CryptoService.X25519KeyLength
            ? (_config.AccountCrypto.PublicKey, _config.AccountCrypto.PrivateKey)
            : null;

    public void StoreAccountKeys(byte[] publicKey, byte[] privateKey)
    {
        _config.AccountCrypto = new CryptoKeys
        {
            PublicKey = publicKey,
            PrivateKey = privateKey,
        };
        _config.Save();
    }

    /// <summary>Sign-out wipe: the active keypair, the account KEK + messenger keypair, and every stashed
    /// sibling keypair.</summary>
    public void Clear()
    {
        _config.Crypto = new CryptoKeys();
        _config.AccountKek = [];
        _config.AccountCrypto = new CryptoKeys();
        foreach (var state in _config.ProfileLocal.Values)
        {
            state.Crypto = new CryptoKeys();
        }
        _config.Save();
    }
}
