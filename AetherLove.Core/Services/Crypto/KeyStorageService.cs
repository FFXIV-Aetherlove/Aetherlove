using System;
using System.Collections.Generic;
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
        _config.AccountKekSalt = [];
        _config.AccountKekMemoryKb = 0;
        _config.AccountKekIterations = 0;
        _config.AccountKekParallelism = 0;
        _config.Save();
    }

    /// <summary>Stores the KEK together with the Argon2id inputs that derived it, so provisioning can stamp
    /// new bundles with parameters that actually reproduce this key.</summary>
    public void StoreKek(byte[] kek, byte[] salt, int memoryKb, int iterations, int parallelism)
    {
        _config.AccountKek = kek;
        _config.AccountKekSalt = salt;
        _config.AccountKekMemoryKb = memoryKb;
        _config.AccountKekIterations = iterations;
        _config.AccountKekParallelism = parallelism;
        _config.Save();
    }

    /// <summary>The stored KEK's recorded derivation inputs, or null when unknown.</summary>
    public (byte[] Salt, int MemoryKb, int Iterations, int Parallelism)? KekParams =>
        _config.AccountKekSalt.Length >= CryptoService.KdfSaltLength
            && _config.AccountKekMemoryKb > 0
            && _config.AccountKekIterations > 0
            && _config.AccountKekParallelism > 0
            ? (_config.AccountKekSalt, _config.AccountKekMemoryKb, _config.AccountKekIterations, _config.AccountKekParallelism)
            : null;

    /// <summary>Every stashed sibling keypair on this device (inactive profiles only).</summary>
    public List<(Guid ProfileId, byte[] PublicKey, byte[] PrivateKey)> EnumerateStashedKeys()
    {
        var list = new List<(Guid, byte[], byte[])>();
        foreach (var (profileId, stash) in _config.ProfileLocal)
        {
            if (profileId != Guid.Empty
                && stash.Crypto.PrivateKey.Length == CryptoService.X25519KeyLength
                && stash.Crypto.PublicKey.Length == CryptoService.X25519KeyLength)
            {
                list.Add((profileId, stash.Crypto.PublicKey, stash.Crypto.PrivateKey));
            }
        }
        return list;
    }

    /// <summary>A stashed INACTIVE profile's private key, or null when that profile has none on this device.
    /// The active profile's keys live in the flat fields, never in the stash.</summary>
    public byte[]? GetStashedPrivateKey(Guid profileId)
        => _config.ProfileLocal.TryGetValue(profileId, out var stash)
            && stash.Crypto.PrivateKey.Length == CryptoService.X25519KeyLength
                ? stash.Crypto.PrivateKey
                : null;

    /// <summary>Pairing-checked variant: the stashed private key only when the stash's public key matches
    /// <paramref name="expectedPublicKey"/>, so a stale stash never anchors a new wrap.</summary>
    public byte[]? GetStashedPrivateKey(Guid profileId, byte[] expectedPublicKey)
        => _config.ProfileLocal.TryGetValue(profileId, out var stash)
            && stash.Crypto.PrivateKey.Length == CryptoService.X25519KeyLength
            && stash.Crypto.PublicKey.AsSpan().SequenceEqual(expectedPublicKey)
                ? stash.Crypto.PrivateKey
                : null;

    /// <summary>Any sibling profile whose private key is unlocked on this device, used to wrap a newly created
    /// profile's key when the account KEK was never captured (every account migrated from 1.x). Null when the
    /// active profile is the only one with keys here.</summary>
    public (Guid ProfileId, byte[] PrivateKey)? FindSiblingKey()
    {
        foreach (var (profileId, stash) in _config.ProfileLocal)
        {
            if (profileId != Guid.Empty && stash.Crypto.PrivateKey.Length == CryptoService.X25519KeyLength)
            {
                return (profileId, stash.Crypto.PrivateKey);
            }
        }
        return null;
    }

    /// <summary>Stashes a sibling profile's unwrapped keypair without disturbing the active profile, so the
    /// unlock chain's intermediate result is reusable on later switches.</summary>
    public void StashSiblingKeys(Guid profileId, byte[] publicKey, byte[] privateKey)
    {
        if (profileId == Guid.Empty || _config.Auth.ActiveProfileId == profileId)
        {
            return;
        }
        if (!_config.ProfileLocal.TryGetValue(profileId, out var stash))
        {
            stash = new ProfileLocalState();
            _config.ProfileLocal[profileId] = stash;
        }
        stash.Crypto = new CryptoKeys
        {
            PublicKey = publicKey,
            PrivateKey = privateKey,
        };
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
        _config.AccountKekSalt = [];
        _config.AccountKekMemoryKb = 0;
        _config.AccountKekIterations = 0;
        _config.AccountKekParallelism = 0;
        _config.AccountCrypto = new CryptoKeys();
        foreach (var state in _config.ProfileLocal.Values)
        {
            state.Crypto = new CryptoKeys();
        }
        _config.Save();
    }
}
