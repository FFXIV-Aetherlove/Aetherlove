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

    public void Clear()
    {
        _config.Crypto = new CryptoKeys();
        _config.Save();
    }
}
