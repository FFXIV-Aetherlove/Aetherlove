using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Config;
using AetherLove.Services.Crypto;
using AetherLove.Services.Hub;
using Dalamud.Plugin.Services;

namespace AetherLove.Services.Messenger;

/// <summary>Messenger E2EE on the ACCOUNT keypair: pairwise ECDH for direct chats, and a symmetric group key
/// per epoch wrapped per member via the same pairwise derivation. The keypair provisions silently from the
/// stored account KEK or, when the KEK was never captured (migrated 1.x devices), from the unlocked profile
/// key via the bundle's profile wrap; the user is never prompted.</summary>
public sealed class MessengerCryptoService
{
    private readonly CryptoService _crypto;
    private readonly KeyStorageService _keys;
    private readonly AetherHubContext _hub;
    private readonly Configuration _config;
    private readonly IPluginLog _log;

    public MessengerCryptoService(CryptoService crypto, KeyStorageService keys, AetherHubContext hub,
        Configuration config, IPluginLog log)
    {
        _crypto = crypto;
        _keys = keys;
        _hub = hub;
        _config = config;
        _log = log;
    }

    public bool HasAccountKeys => _keys.AccountKeys is not null;

    public byte[]? AccountPublicKey => _keys.AccountKeys?.PublicKey;

    /// <summary>Ensures the account keypair exists locally: unwraps the server bundle under the stored KEK or
    /// the unlocked profile key's wrap, or generates and publishes one when the server has none. Returns false
    /// only when neither the KEK nor a matching profile key can open anything (the recovery gate owns the
    /// interactive path). Never prompts.</summary>
    public async Task<bool> EnsureProvisionedAsync(CancellationToken ct = default)
    {
        if (_keys.AccountKeys is not null)
        {
            return true;
        }
        var kek = _keys.Kek;
        var profilePriv = _keys.GetPrivateKey();
        if (kek is null && profilePriv is null)
        {
            return false;
        }
        try
        {
            var bundle = await _hub.GetAccountKeyBundleAsync(ct).ConfigureAwait(false);
            if (bundle is not null)
            {
                if (kek is not null
                    && _crypto.UnwrapPrivateKey(bundle.EncryptedPrivateKey, bundle.WrapNonce, kek) is { } viaKek)
                {
                    _keys.StoreAccountKeys(bundle.PublicKey, viaKek);
                    return true;
                }
                if (profilePriv is not null
                    && bundle is { ProfileWrappedPrivateKey: { Length: > 0 } wrapped, ProfileWrapNonce.Length: > 0 })
                {
                    var wrapKey = _crypto.DeriveAccountWrapKey(profilePriv, bundle.PublicKey);
                    if (_crypto.UnwrapPrivateKey(wrapped, bundle.ProfileWrapNonce, wrapKey) is { } viaProfile)
                    {
                        _keys.StoreAccountKeys(bundle.PublicKey, viaProfile);
                        return true;
                    }
                }
                if (bundle is { WrapProfileId: { } wrapper, ProfileWrappedPrivateKey: { Length: > 0 } stashWrapped, ProfileWrapNonce.Length: > 0 }
                    && _keys.GetStashedPrivateKey(wrapper) is { } stashPriv)
                {
                    var stashKey = _crypto.DeriveAccountWrapKey(stashPriv, bundle.PublicKey);
                    if (_crypto.UnwrapPrivateKey(stashWrapped, bundle.ProfileWrapNonce, stashKey) is { } viaStash)
                    {
                        _keys.StoreAccountKeys(bundle.PublicKey, viaStash);
                        return true;
                    }
                }
                _log.Warning("[MessengerCrypto] Neither the stored KEK nor the profile key opens the account bundle; recovery gate will prompt.");
                return false;
            }

            var pass = await _hub.GetAccountPassphraseAsync(ct).ConfigureAwait(false);
            // A stale KEK must not mint a bundle it can't reopen elsewhere; no verifier (migrated account)
            // means the KEK came from a successful profile unlock and is trusted.
            if (kek is not null && pass is not null
                && !_crypto.CheckPassphraseVerifier(pass.Verifier, pass.VerifierNonce, kek))
            {
                kek = null;
            }
            if (kek is null && profilePriv is null)
            {
                return false;
            }

            var (pubKey, privKey) = _crypto.GenerateIdentityKeyPair();
            byte[]? profileWrapped = null;
            byte[]? profileNonce = null;
            if (profilePriv is not null)
            {
                (profileWrapped, profileNonce) = _crypto.Encrypt(_crypto.DeriveAccountWrapKey(profilePriv, pubKey), privKey);
            }
            // Without a KEK the canonical wrap fields carry the profile wrap; a passphrase-holding device
            // fails the KEK unwrap and falls through to the profile wrap it unlocked moments earlier.
            var (mainWrapped, mainNonce) = kek is not null
                ? _crypto.WrapPrivateKey(privKey, kek)
                : (profileWrapped!, profileNonce!);
            var salt = pass?.KdfSalt;
            if (salt is null)
            {
                salt = new byte[CryptoService.KdfSaltLength];
                RandomNumberGenerator.Fill(salt);
            }
            await _hub.UploadAccountKeyBundleAsync(new Shared.Messaging.KeyBundleDto(
                    pubKey, mainWrapped, salt, pass?.KdfMemoryKb ?? 0, pass?.KdfIterations ?? 0,
                    pass?.KdfParallelism ?? 0, mainNonce,
                    WrapProfileId: profileWrapped is null ? null : _config.Auth.ActiveProfileId,
                    ProfileWrappedPrivateKey: profileWrapped,
                    ProfileWrapNonce: profileNonce), ct)
                .ConfigureAwait(false);
            _keys.StoreAccountKeys(pubKey, privKey);
            _log.Information("[MessengerCrypto] Account key bundle provisioned ({Mode}).",
                kek is not null ? "account KEK" : "profile-key wrap");
            return true;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[MessengerCrypto] Account key provisioning failed.");
            return false;
        }
    }

    private byte[]? PairwiseKey(byte[] peerPublicKey)
    {
        if (_keys.AccountKeys is not { } mine)
        {
            return null;
        }
        var shared = _crypto.DeriveSharedSecret(mine.PrivateKey, peerPublicKey);
        var salt = CryptoService.DeriveConversationSalt(mine.PublicKey, peerPublicKey);
        return _crypto.DeriveMessageKey(shared, salt);
    }

    public (byte[] Ciphertext, byte[] Nonce)? EncryptDirect(byte[] peerPublicKey, string plaintext)
    {
        var key = PairwiseKey(peerPublicKey);
        return key is null ? null : _crypto.Encrypt(key, Encoding.UTF8.GetBytes(plaintext));
    }

    public string? DecryptDirect(byte[] peerPublicKey, byte[] ciphertext, byte[] nonce)
    {
        try
        {
            var key = PairwiseKey(peerPublicKey);
            return key is null ? null : Encoding.UTF8.GetString(_crypto.Decrypt(key, nonce, ciphertext));
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public static byte[] GenerateGroupKey()
    {
        var key = new byte[CryptoService.AesGcmKeyLength];
        RandomNumberGenerator.Fill(key);
        return key;
    }

    /// <summary>Wraps a group key for one member under the pairwise key between my account keypair and theirs.</summary>
    public (byte[] WrappedKey, byte[] Nonce)? WrapGroupKey(byte[] groupKey, byte[] memberPublicKey)
    {
        var key = PairwiseKey(memberPublicKey);
        return key is null ? null : _crypto.Encrypt(key, groupKey);
    }

    /// <summary>Unwraps my copy of a group key using the WRAPPER's public key (the uploading member's).</summary>
    public byte[]? UnwrapGroupKey(byte[] wrappedKey, byte[] nonce, byte[] wrapperPublicKey)
    {
        try
        {
            var key = PairwiseKey(wrapperPublicKey);
            return key is null ? null : _crypto.Decrypt(key, nonce, wrappedKey);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public (byte[] Ciphertext, byte[] Nonce) EncryptGroup(byte[] groupKey, string plaintext) =>
        _crypto.Encrypt(groupKey, Encoding.UTF8.GetBytes(plaintext));

    public string? DecryptGroup(byte[] groupKey, byte[] ciphertext, byte[] nonce)
    {
        try
        {
            return Encoding.UTF8.GetString(_crypto.Decrypt(groupKey, nonce, ciphertext));
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
