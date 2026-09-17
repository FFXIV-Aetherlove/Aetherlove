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

/// <summary>Messenger encryption using identities owned by the shared account service.</summary>
public sealed class MessengerCryptoService
{
    private readonly CryptoService _crypto;
    private readonly KeyStorageService _keys;
    private readonly AetherHubContext _hub;
    private readonly Configuration _config;
    private readonly IPluginLog _log;
    private readonly AccountEncryptionService _encryption;

    public MessengerCryptoService(CryptoService crypto, KeyStorageService keys, AetherHubContext hub,
        Configuration config, IPluginLog log, AccountEncryptionService encryption)
    {
        _encryption = encryption;
        _crypto = crypto;
        _keys = keys;
        _hub = hub;
        _config = config;
        _log = log;
    }

    public bool HasAccountKeys => _keys.AccountKeys is not null;

    public byte[]? AccountPublicKey => _keys.AccountKeys?.PublicKey;

    public async Task<bool> EnsureProvisionedAsync(CancellationToken ct = default)
    {
        try
        {
            return await _encryption.EnsureIdentityAsync("messenger", Guid.Empty, ct).ConfigureAwait(false) is not null;
        }
        catch (Exception ex)
        {
            _log.Warning("[MessengerCrypto] Identity unavailable ({Reason}).", ex.GetType().Name);
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
            var plain = _encryption.TryDecryptHistory("messenger", _encryption.AccountId, [peerPublicKey], ciphertext, nonce);
            return plain is null ? null : Encoding.UTF8.GetString(plain);
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
    public byte[]? UnwrapGroupKey(byte[] wrappedKey, byte[] nonce, byte[] wrapperPublicKey, byte[]? recipientPublicKey = null)
    {
        try
        {
            var historical = recipientPublicKey is null ? null : _encryption.FindIdentity("messenger", recipientPublicKey);
            var key = historical is null ? PairwiseKey(wrapperPublicKey)
                : _crypto.DeriveMessageKey(_crypto.DeriveSharedSecret(historical.PrivateKey, wrapperPublicKey),
                    CryptoService.DeriveConversationSalt(historical.PublicKey, wrapperPublicKey));
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
