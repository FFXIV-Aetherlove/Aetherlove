using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Config;
using AetherLove.Services.Crypto;
using AetherLove.Services.Hub;
using AetherLove.Shared.Messaging;
using AetherLove.Shared.Profile;

namespace AetherLove.Services.Auth;

/// <summary>The client half of the lost-passphrase recovery: derives a KEK from the NEW passphrase, generates
/// fresh keypairs for every profile plus the messenger account keypair, publishes everything in one hub call,
/// and swaps the local key state over. Messages sent before the reset become permanently unreadable for this
/// user; peers keep their history via the server-side key timeline.</summary>
public sealed class PassphraseResetFlow
{
    private const int MemoryKb = 64 * 1024;
    private const int Iterations = 3;
    private const int Parallelism = 1;

    private readonly AetherHubContext _hub;
    private readonly CryptoService _crypto;
    private readonly KeyStorageService _keys;
    private readonly Configuration _config;

    public PassphraseResetFlow(AetherHubContext hub, CryptoService crypto, KeyStorageService keys, Configuration config)
    {
        _hub = hub;
        _crypto = crypto;
        _keys = keys;
        _config = config;
    }

    public async Task RunAsync(string newPassphrase, CancellationToken ct = default)
    {
        var profiles = await _hub.ListProfilesAsync(ct).ConfigureAwait(false);
        UiHost.Log.Debug("[RESET] PassphraseResetFlow: starting reset for {Count} profile(s) plus the messenger account key.", profiles.Profiles.Length);

        var salt = new byte[CryptoService.KdfSaltLength];
        RandomNumberGenerator.Fill(salt);
        var kek = _crypto.DeriveKEK(newPassphrase, salt, MemoryKb, Iterations, Parallelism);
        var (verifier, verifierNonce) = _crypto.CreatePassphraseVerifier(kek);
        var passphraseDto = new AccountPassphraseDto(salt, MemoryKb, Iterations, Parallelism, verifier, verifierNonce);

        var uploads = new List<ProfileBundleUpload>();
        var plain = new Dictionary<Guid, (byte[] Pub, byte[] Priv)>();
        foreach (var profile in profiles.Profiles)
        {
            var (pub, priv) = _crypto.GenerateIdentityKeyPair();
            var (wrapped, nonce) = _crypto.WrapPrivateKey(priv, kek);
            plain[profile.ProfileId] = (pub, priv);
            uploads.Add(new ProfileBundleUpload(profile.ProfileId,
                new KeyBundleDto(pub, wrapped, salt, MemoryKb, Iterations, Parallelism, nonce)));
            UiHost.Log.Debug("[RESET] PassphraseResetFlow: generated a fresh keypair for profile {Profile:N}.", profile.ProfileId);
        }

        var (accountPub, accountPriv) = _crypto.GenerateIdentityKeyPair();
        var (accountWrapped, accountNonce) = _crypto.WrapPrivateKey(accountPriv, kek);
        var accountBundle = new KeyBundleDto(accountPub, accountWrapped, salt, MemoryKb, Iterations, Parallelism, accountNonce);

        UiHost.Log.Debug("[RESET] PassphraseResetFlow: publishing new passphrase + {Count} profile bundles + messenger account bundle to the server.", uploads.Count);
        await _hub.ResetAccountPassphraseAsync(new ResetPassphraseRequest(passphraseDto, uploads.ToArray(), accountBundle), ct)
            .ConfigureAwait(false);
        UiHost.Log.Debug("[RESET] PassphraseResetFlow: server accepted the reset; swapping local key state to the new keys.");

        _keys.StoreKek(kek);
        _keys.StoreAccountKeys(accountPub, accountPriv);
        var activeId = _config.Auth.ActiveProfileId
            ?? (profiles.Profiles.Length == 1 ? profiles.Profiles[0].ProfileId : (Guid?)null);
        if (activeId is { } id && plain.TryGetValue(id, out var active))
        {
            _keys.Store(active.Pub, active.Priv);
        }
        // Stashed sibling keypairs belong to the old passphrase era; they re-unlock from the fresh bundles.
        foreach (var state in _config.ProfileLocal.Values)
        {
            state.Crypto = new CryptoKeys();
        }
        _config.Save();
    }
}
