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

/// <summary>Yapper encryption using profile identities owned by the shared account service.</summary>
public sealed class YapperDmCryptoService
{
    private readonly CryptoService _crypto;
    private readonly KeyStorageService _keys;
    private readonly AetherHubContext _hub;
    private readonly IPluginLog _log;

    private readonly AccountEncryptionService _encryption;
    private (byte[] PublicKey, byte[] PrivateKey)? _pair;
    private int _generation;
    private int _provisioning;

    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.Zero,
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(120),
    ];

    /// <summary>Ready: the pair is loaded. Unavailable: nothing to do right now, retrying changes nothing.
    /// Failed: the attempt threw, so a retry can succeed.</summary>
    private enum Outcome
    {
        Ready,
        Unavailable,
        Failed,
    }

    public YapperDmCryptoService(CryptoService crypto, KeyStorageService keys, AetherHubContext hub, IPluginLog log, AccountEncryptionService encryption)
    {
        _encryption = encryption;
        keys.Cleared += Clear;
        encryption.Opened += ProvisionInBackground;
        _crypto = crypto;
        _keys = keys;
        _hub = hub;
        _log = log;
    }

    public bool HasKeys => _pair is not null;

    public byte[]? PublicKey => _pair?.PublicKey;

    /// <summary>Drops the in-memory pair (profile switch / logout); the next ensure re-unwraps.</summary>
    public void Clear()
    {
        Interlocked.Increment(ref _generation);
        _pair = null;
    }

    /// <summary>One attempt. A failed attempt hands over to <see cref="ProvisionInBackground"/>, so a caller
    /// that only asks once still ends up with keys.</summary>
    public async Task<bool> EnsureProvisionedAsync(CancellationToken ct = default)
    {
        var outcome = await TryProvisionAsync(ct).ConfigureAwait(false);
        if (outcome == Outcome.Failed)
        {
            ProvisionInBackground(afterFailure: true);
        }
        return outcome == Outcome.Ready;
    }

    public void ProvisionInBackground() => ProvisionInBackground(afterFailure: false);

    /// <summary>Provisions off the caller's path and retries a failed attempt after 5, 30 and 120 seconds.
    /// Only one loop runs at a time. It stops on success, when there is nothing to provision (no Yapper
    /// profile, or a locked keyring, which starts a fresh loop itself once it opens), and when the keys
    /// are cleared. <paramref name="afterFailure"/> skips the immediate first attempt, which just failed.</summary>
    private void ProvisionInBackground(bool afterFailure)
    {
        if (Interlocked.CompareExchange(ref _provisioning, 1, 0) != 0)
        {
            return;
        }
        var ownGeneration = _generation;
        _ = Task.Run(async () =>
        {
            try
            {
                foreach (var delay in RetryDelays)
                {
                    if (delay == TimeSpan.Zero && afterFailure)
                    {
                        continue;
                    }
                    if (delay > TimeSpan.Zero)
                    {
                        await Task.Delay(delay).ConfigureAwait(false);
                    }
                    if (ownGeneration != _generation
                        || await TryProvisionAsync(CancellationToken.None).ConfigureAwait(false) != Outcome.Failed)
                    {
                        return;
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref _provisioning, 0);
            }
        });
    }

    private async Task<Outcome> TryProvisionAsync(CancellationToken ct)
    {
        var generation = _keys.Generation;
        var ownGeneration = _generation;
        try
        {
            var pair = await _encryption.EnsureIdentityAsync("yapper", Guid.Empty, ct).ConfigureAwait(false);
            if (generation != _keys.Generation || ownGeneration != _generation) { return Outcome.Unavailable; }
            _pair = pair is null ? null : (pair.PublicKey, pair.PrivateKey);
            return _pair is null ? Outcome.Unavailable : Outcome.Ready;
        }
        catch (Exception ex)
        {
            // The loaded pair stays: a dropped call says nothing about whether it is still the right one.
            _log.Warning("[YapperDmCrypto] Identity unavailable ({Reason}).", ex.GetType().Name);
            return Outcome.Failed;
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
