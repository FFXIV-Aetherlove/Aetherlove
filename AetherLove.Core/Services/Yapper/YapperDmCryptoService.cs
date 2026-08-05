using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Services.Crypto;
using AetherLove.Services.Hub;
using AetherLove.Shared;
using AetherLove.Shared.Yapper;
using Dalamud.Plugin.Services;

namespace AetherLove.Services.Yapper;

/// <summary>Yapper DM E2EE: a dedicated X25519 keypair per yapper profile (so DM identity can't be
/// correlated with dating profiles or the messenger), wrapped under the account passphrase KEK and
/// provisioned silently, exactly like the messenger's account keypair. The unwrapped pair lives in
/// memory for the session; a bundle the stored KEK can't open (passphrase reset elsewhere) is
/// replaced with a fresh pair, making pre-reset DMs undecryptable placeholders by design.</summary>
public sealed class YapperDmCryptoService
{
    private readonly CryptoService _crypto;
    private readonly KeyStorageService _keys;
    private readonly AetherHubContext _hub;
    private readonly IPluginLog _log;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private (byte[] PublicKey, byte[] PrivateKey)? _pair;

    public YapperDmCryptoService(CryptoService crypto, KeyStorageService keys, AetherHubContext hub, IPluginLog log)
    {
        _crypto = crypto;
        _keys = keys;
        _hub = hub;
        _log = log;
    }

    public bool HasKeys => _pair is not null;

    public byte[]? PublicKey => _pair?.PublicKey;

    /// <summary>Drops the in-memory pair (profile switch / logout); the next ensure re-unwraps.</summary>
    public void Clear() => _pair = null;

    /// <summary>Ensures the yapper keypair exists locally: unwraps the server bundle under the stored
    /// account KEK, generates and publishes one when the server has none, or re-publishes a fresh pair
    /// when the stored KEK can't open the existing bundle. Never prompts. Safe to fire blindly on every
    /// connect: an account without a yapper profile is a quiet no-op, and concurrent callers serialize
    /// so a double generate-and-publish race cannot retire its own fresh bundle.</summary>
    public async Task<bool> EnsureProvisionedAsync(CancellationToken ct = default)
    {
        if (_pair is not null)
        {
            return true;
        }
        var kek = _keys.Kek;
        if (kek is null)
        {
            return false;
        }
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_pair is not null)
            {
                return true;
            }
            var bundle = await _hub.GetYapperDmKeysAsync(ct).ConfigureAwait(false);
            if (bundle is not null
                && _crypto.UnwrapPrivateKey(bundle.EncryptedPrivateKey, bundle.WrapNonce, kek) is { } unwrapped)
            {
                _pair = (bundle.PublicKey, unwrapped);
                return true;
            }

            var (pubKey, privKey) = _crypto.GenerateIdentityKeyPair();
            var (wrapped, nonce) = _crypto.WrapPrivateKey(privKey, kek);
            await _hub.PublishYapperDmKeysAsync(new YapperKeyBundleDto(pubKey, wrapped, nonce), ct)
                .ConfigureAwait(false);
            _pair = (pubKey, privKey);
            _log.Information("[YapperDmCrypto] Key bundle {Mode}.",
                bundle is null ? "provisioned" : "replaced after failed unwrap");
            return true;
        }
        catch (Exception ex)
        {
            if (ex.Message.Contains(HubErrors.YapperNoProfile) || ex.Message.Contains(HubErrors.YapperDisabled))
            {
                _log.Debug("[YapperDmCrypto] No yapper profile (or yapper disabled); skipping provisioning.");
            }
            else
            {
                _log.Warning(ex, "[YapperDmCrypto] Key provisioning failed.");
            }
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    private byte[]? PairwiseKey(byte[] peerPublicKey)
    {
        if (_pair is not { } mine)
        {
            return null;
        }
        var shared = _crypto.DeriveSharedSecret(mine.PrivateKey, peerPublicKey);
        var salt = CryptoService.DeriveConversationSalt(mine.PublicKey, peerPublicKey);
        return _crypto.DeriveMessageKey(shared, salt);
    }

    public (byte[] Ciphertext, byte[] Nonce)? Encrypt(byte[] peerPublicKey, string plaintext)
    {
        var key = PairwiseKey(peerPublicKey);
        return key is null ? null : _crypto.Encrypt(key, Encoding.UTF8.GetBytes(plaintext));
    }

    public string? Decrypt(byte[] peerPublicKey, byte[] ciphertext, byte[] nonce)
    {
        try
        {
            var key = PairwiseKey(peerPublicKey);
            return key is null || ciphertext.Length == 0
                ? null
                : Encoding.UTF8.GetString(_crypto.Decrypt(key, nonce, ciphertext));
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
