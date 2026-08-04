using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Config;
using AetherLove.Services.Crypto;
using AetherLove.Services.Hub;
using AetherLove.Shared.Messaging;
using AetherLove.Shared.Profile;
using Dalamud.Plugin.Services;

namespace AetherLove.Services.Messenger;

/// <summary>Messenger E2EE on the ACCOUNT keypair: pairwise ECDH for direct chats, and a symmetric group key
/// per epoch wrapped per member via the same pairwise derivation. The keypair provisions silently from the
/// stored account KEK or, when the KEK was never captured (migrated 1.x devices), from the unlocked profile
/// key via the bundle's profile wrap; the user is never prompted. A device that already holds the keypair
/// also repairs a bundle no other device can open (see <see cref="TryHealBundleAsync"/>).</summary>
public sealed class MessengerCryptoService
{
    private readonly CryptoService _crypto;
    private readonly KeyStorageService _keys;
    private readonly AetherHubContext _hub;
    private readonly Configuration _config;
    private readonly IPluginLog _log;
    private bool _healChecked;

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
        if (_keys.AccountKeys is { } local)
        {
            await TryHealBundleAsync(local, ct).ConfigureAwait(false);
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
                _log.Warning("[MessengerCrypto] Neither the stored KEK nor the profile key opens the account bundle; messaging stays locked here until a device that holds the keypair repairs the wrap.");
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

    /// <summary>Once per session, repairs a server bundle that only THIS device can open. A bundle minted
    /// before the account had a passphrase record carries a profile wrap in its canonical fields; once that
    /// profile's keypair is replaced, no other device can ever open it, while this one keeps working from its
    /// stored copy and never notices. Rewrapping under the verifier-proven account KEK publishes the SAME
    /// keypair, so no history is lost and peers see nothing change.</summary>
    private async Task TryHealBundleAsync((byte[] PublicKey, byte[] PrivateKey) local, CancellationToken ct)
    {
        if (_healChecked || _keys.Kek is not { } kek)
        {
            return;
        }
        try
        {
            await HealBundleAsync(local, kek, ct).ConfigureAwait(false);
            _healChecked = true;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[MessengerCrypto] The account-bundle repair check failed; the next sync retries.");
        }
    }

    private async Task HealBundleAsync((byte[] PublicKey, byte[] PrivateKey) local, byte[] kek, CancellationToken ct)
    {
        var pass = await _hub.GetAccountPassphraseAsync(ct).ConfigureAwait(false);
        // Only a verifier-proven KEK may be published: rewrapping under a stale one would lock everyone out
        // for good instead of just this account's other devices.
        if (pass is not { Verifier.Length: > 0, VerifierNonce.Length: > 0 }
            || !_crypto.CheckPassphraseVerifier(pass.Verifier, pass.VerifierNonce, kek))
        {
            return;
        }
        if (_keys.KekParams is null)
        {
            _keys.StoreKek(kek, pass.KdfSalt, pass.KdfMemoryKb, pass.KdfIterations, pass.KdfParallelism);
        }
        var bundle = await _hub.GetAccountKeyBundleAsync(ct).ConfigureAwait(false);
        // A public-key mismatch means the local pair belongs to a retired bundle (a passphrase reset on another
        // device), so republishing it would drag the account back onto a keypair it has moved off.
        if (bundle is null || !bundle.PublicKey.AsSpan().SequenceEqual(local.PublicKey))
        {
            return;
        }
        if (_crypto.UnwrapPrivateKey(bundle.EncryptedPrivateKey, bundle.WrapNonce, kek) is not null
            && StampsMatch(bundle, pass))
        {
            return;
        }

        var (wrapped, wrapNonce) = _crypto.WrapPrivateKey(local.PrivateKey, kek);
        byte[]? profileWrapped = null;
        byte[]? profileNonce = null;
        if (_keys.GetPrivateKey() is { } profilePriv)
        {
            (profileWrapped, profileNonce) = _crypto.Encrypt(
                _crypto.DeriveAccountWrapKey(profilePriv, local.PublicKey), local.PrivateKey);
        }
        await _hub.RewrapAccountKeyBundleAsync(new KeyBundleDto(
                local.PublicKey, wrapped, pass.KdfSalt, pass.KdfMemoryKb, pass.KdfIterations, pass.KdfParallelism,
                wrapNonce,
                WrapProfileId: profileWrapped is null ? null : _config.Auth.ActiveProfileId,
                ProfileWrappedPrivateKey: profileWrapped,
                ProfileWrapNonce: profileNonce), ct)
            .ConfigureAwait(false);
        _log.Information("[MessengerCrypto] Repaired the account key bundle's wrap under the account passphrase; the account's other devices can open it now.");
    }

    /// <summary>True when the bundle's recorded Argon2id inputs are the account's current ones, so a device
    /// deriving the KEK from the passphrase record lands on the key the bundle is actually wrapped under.</summary>
    private static bool StampsMatch(KeyBundleDto bundle, AccountPassphraseDto pass)
        => bundle.KdfMemoryKb == pass.KdfMemoryKb
            && bundle.KdfIterations == pass.KdfIterations
            && bundle.KdfParallelism == pass.KdfParallelism
            && bundle.KdfSalt.AsSpan().SequenceEqual(pass.KdfSalt);

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
