using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Config;
using AetherLove.Services.Crypto;
using AetherLove.Services.Hub;
using AetherLove.Shared.Messaging;
using AetherLove.Shared.Profile;
using Dalamud.Plugin.Services;

namespace AetherLove.Services.Auth;

public enum AccountUnlockOutcome
{
    Success,
    WrongPassphrase,
    Unrecoverable,
}

/// <summary>The whole-account passphrase unlock: one passphrase entry opens the active profile's bundle,
/// every sibling bundle, and the messenger account bundle, then converges them all onto one KEK (with
/// sibling wraps) so every later unlock on any device is a single silent derivation. Keys are only ever
/// restored or re-wrapped in place, never generated or discarded.</summary>
public sealed class AccountUnlockService
{
    private readonly record struct Candidate(string Source, byte[] Salt, int Mem, int Iter, int Par, byte[] Kek);

    private readonly AetherHubContext _hub;
    private readonly CryptoService _crypto;
    private readonly KeyStorageService _keys;
    private readonly Configuration _config;
    private readonly IPluginLog _log;

    public AccountUnlockService(AetherHubContext hub, CryptoService crypto, KeyStorageService keys,
        Configuration config, IPluginLog log)
    {
        _hub = hub;
        _crypto = crypto;
        _keys = keys;
        _config = config;
        _log = log;
    }

    public async Task<AccountUnlockOutcome> UnlockAsync(string passphrase, KeyBundleDto activeBundle,
        CancellationToken ct = default)
    {
        var activeId = _config.Auth.ActiveProfileId ?? Guid.Empty;
        var siblings = await _hub.GetSiblingKeyBundlesAsync(ct).ConfigureAwait(false);
        var pass = await _hub.GetAccountPassphraseAsync(ct).ConfigureAwait(false);
        KeyBundleDto? accountBundle = null;
        try
        {
            accountBundle = await _hub.GetAccountKeyBundleAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[AccountUnlock] Account-bundle fetch failed; messenger keys stay untouched this pass.");
        }
        KeyBundleDto[] retired;
        try
        {
            retired = await _hub.GetMyRetiredKeyBundlesAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            retired = [];
            _log.Warning(ex, "[AccountUnlock] Retired-bundle fetch failed; restoration continues without them.");
        }

        var keks = DeriveCandidates(passphrase, activeBundle, pass, siblings, accountBundle, retired);

        Candidate? accountRecord = null;
        bool? passphraseIsCurrent = null;
        if (pass is not null)
        {
            foreach (var c in keks)
            {
                if (c.Source == "account record")
                {
                    accountRecord = c;
                    if (pass.Verifier is { Length: > 0 })
                    {
                        passphraseIsCurrent = _crypto.CheckPassphraseVerifier(pass.Verifier, pass.VerifierNonce, c.Kek);
                    }
                    break;
                }
            }
        }
        _log.Information("[AccountUnlock] {Candidates} candidate sets, {Siblings} sibling bundle(s), {Retired} retired, accountBundle={HasAccount}, verifierMatch={Verifier}.",
            keks.Count, siblings.Length, retired.Length, accountBundle is not null, passphraseIsCurrent?.ToString() ?? "(none)");

        var bundles = new Dictionary<Guid, KeyBundleDto> { [activeId] = activeBundle };
        foreach (var sib in siblings)
        {
            bundles[sib.ProfileId] = sib.Bundle;
        }

        // Opened private keys, seeded from the pairing-checked stash so anchors work from the start. Opening
        // one bundle can anchor another, so the sweep runs to a fixpoint.
        var opened = new Dictionary<Guid, (byte[] Priv, Candidate? ViaKek)>();
        foreach (var (id, b) in bundles)
        {
            if (id != activeId && _keys.GetStashedPrivateKey(id, b.PublicKey) is { } stashPriv)
            {
                opened[id] = (stashPriv, null);
            }
        }
        var progress = true;
        while (progress)
        {
            progress = false;
            foreach (var (id, b) in bundles)
            {
                if (opened.ContainsKey(id))
                {
                    continue;
                }
                if (TryOpen(b, keks, opened) is not { } hit)
                {
                    continue;
                }
                opened[id] = hit;
                progress = true;
            }
        }

        if (!opened.ContainsKey(activeId))
        {
            if (passphraseIsCurrent is false)
            {
                _log.Debug("[AccountUnlock] The typed passphrase fails the account verifier; retired bundles stay untouched.");
            }
            else if (await TryReactivateRetiredAsync(retired, keks, opened, ct).ConfigureAwait(false) is { } back)
            {
                bundles[activeId] = back.Dto;
                opened[activeId] = (back.Priv, back.ViaKek);
            }
        }
        if (!opened.TryGetValue(activeId, out var active))
        {
            if (passphraseIsCurrent == true)
            {
                _log.Warning("[AccountUnlock] The passphrase matches the account verifier but opens no stored bundle; this profile's keys cannot be recovered here.");
                return AccountUnlockOutcome.Unrecoverable;
            }
            _log.Debug("[AccountUnlock] No candidate parameter set or anchor opens any of the active profile's bundles.");
            return AccountUnlockOutcome.WrongPassphrase;
        }

        // The convergence target: the account record's KEK when the passphrase provably matches it, else
        // whichever passphrase-derived candidate opened a bundle.
        var canonical = passphraseIsCurrent == true ? accountRecord : null;
        canonical ??= active.ViaKek;
        if (canonical is null)
        {
            foreach (var (_, o) in opened)
            {
                if (o.ViaKek is { } via)
                {
                    canonical = via;
                    break;
                }
            }
        }

        if (pass is null && canonical is { } record)
        {
            try
            {
                var (verifier, verifierNonce) = _crypto.CreatePassphraseVerifier(record.Kek);
                await _hub.SetAccountPassphraseAsync(
                    new AccountPassphraseDto(record.Salt, record.Mem, record.Iter, record.Par, verifier, verifierNonce),
                    ct).ConfigureAwait(false);
                _log.Debug("[AccountUnlock] Backfilled the account passphrase record from the '{Source}' parameters.", record.Source);
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "[AccountUnlock] Account passphrase backfill failed.");
            }
        }

        var converged = 0;
        if (canonical is { } target)
        {
            foreach (var (id, b) in bundles)
            {
                if (!opened.TryGetValue(id, out var o))
                {
                    continue;
                }
                var aligned = o.ViaKek is { } via && SameParams(via, target) && StampsMatch(b, target);
                var hasSiblingWrap = b.ProfileWrappedPrivateKey is { Length: > 0 };
                if (aligned && (hasSiblingWrap || opened.Count == 1))
                {
                    continue;
                }
                var dto = ComposeBundle(b.PublicKey, o.Priv, target, id, opened);
                try
                {
                    await _hub.RewrapKeyBundleAsync(id, dto, ct).ConfigureAwait(false);
                    bundles[id] = dto;
                    converged++;
                }
                catch (Exception ex)
                {
                    _log.Warning(ex, "[AccountUnlock] Bundle convergence failed for one profile; keys stay usable locally and the next unlock retries.");
                }
            }
        }

        if (accountBundle is { } acct)
        {
            await UnlockAccountBundleAsync(acct, keks, opened, active.Priv, activeId, canonical, ct).ConfigureAwait(false);
        }

        _keys.Store(bundles[activeId].PublicKey, active.Priv);
        foreach (var (id, o) in opened)
        {
            if (id != activeId)
            {
                _keys.StashSiblingKeys(id, bundles[id].PublicKey, o.Priv);
            }
        }
        if (canonical is { } kek)
        {
            _keys.StoreKek(kek.Kek, kek.Salt, kek.Mem, kek.Iter, kek.Par);
        }
        _log.Information("[AccountUnlock] Unlock complete: {Opened}/{Total} profile bundle(s) unlocked, {Converged} rewrapped, canonical='{Source}'.",
            opened.Count, bundles.Count, converged, canonical?.Source ?? "(none)");
        return AccountUnlockOutcome.Success;
    }

    private List<Candidate> DeriveCandidates(string passphrase, KeyBundleDto activeBundle, AccountPassphraseDto? pass,
        SiblingKeyBundleDto[] siblings, KeyBundleDto? accountBundle, KeyBundleDto[] retired)
    {
        var sets = new List<(string Source, byte[] Salt, int Mem, int Iter, int Par)>();
        void Add(string source, byte[]? salt, int mem, int iter, int par)
        {
            if (salt is null || salt.Length < CryptoService.KdfSaltLength || mem <= 0 || iter <= 0 || par <= 0)
            {
                return;
            }
            foreach (var s in sets)
            {
                if (s.Mem == mem && s.Iter == iter && s.Par == par && s.Salt.AsSpan().SequenceEqual(salt))
                {
                    return;
                }
            }
            sets.Add((source, salt, mem, iter, par));
        }
        if (pass is not null)
        {
            Add("account record", pass.KdfSalt, pass.KdfMemoryKb, pass.KdfIterations, pass.KdfParallelism);
        }
        Add("own stamps", activeBundle.KdfSalt, activeBundle.KdfMemoryKb, activeBundle.KdfIterations, activeBundle.KdfParallelism);
        foreach (var sib in siblings)
        {
            Add("sibling bundle", sib.Bundle.KdfSalt, sib.Bundle.KdfMemoryKb, sib.Bundle.KdfIterations, sib.Bundle.KdfParallelism);
        }
        if (accountBundle is { } acct)
        {
            Add("messenger bundle", acct.KdfSalt, acct.KdfMemoryKb, acct.KdfIterations, acct.KdfParallelism);
        }
        foreach (var old in retired)
        {
            Add("retired bundle", old.KdfSalt, old.KdfMemoryKb, old.KdfIterations, old.KdfParallelism);
        }
        var keks = new List<Candidate>();
        foreach (var s in sets)
        {
            keks.Add(new Candidate(s.Source, s.Salt, s.Mem, s.Iter, s.Par,
                _crypto.DeriveKEK(passphrase, s.Salt, s.Mem, s.Iter, s.Par)));
        }
        return keks;
    }

    private (byte[] Priv, Candidate? ViaKek)? TryOpen(KeyBundleDto b, List<Candidate> keks,
        Dictionary<Guid, (byte[] Priv, Candidate? ViaKek)> opened)
    {
        foreach (var c in keks)
        {
            if (_crypto.UnwrapPrivateKey(b.EncryptedPrivateKey, b.WrapNonce, c.Kek) is { } priv)
            {
                return (priv, c);
            }
        }
        foreach (var (_, anchor) in opened)
        {
            var wrapKey = _crypto.DeriveSiblingWrapKey(anchor.Priv, b.PublicKey);
            var priv = b is { ProfileWrappedPrivateKey.Length: > 0, ProfileWrapNonce.Length: > 0 }
                ? _crypto.UnwrapPrivateKey(b.ProfileWrappedPrivateKey, b.ProfileWrapNonce, wrapKey)
                : null;
            priv ??= _crypto.UnwrapPrivateKey(b.EncryptedPrivateKey, b.WrapNonce, wrapKey);
            if (priv is not null)
            {
                return (priv, null);
            }
        }
        return null;
    }

    /// <summary>Republishes a retired keypair the passphrase still opens, so pre-repair history decrypts
    /// again. Only reached when the verifier does not disprove the passphrase, so a reset-revoked key can
    /// never be resurrected by the old passphrase.</summary>
    private async Task<(KeyBundleDto Dto, byte[] Priv, Candidate ViaKek)?> TryReactivateRetiredAsync(
        KeyBundleDto[] retired, List<Candidate> keks,
        Dictionary<Guid, (byte[] Priv, Candidate? ViaKek)> opened, CancellationToken ct)
    {
        foreach (var old in retired)
        {
            foreach (var c in keks)
            {
                if (_crypto.UnwrapPrivateKey(old.EncryptedPrivateKey, old.WrapNonce, c.Kek) is not { } priv)
                {
                    continue;
                }
                _log.Information("[AccountUnlock] A RETIRED bundle opened via '{Source}'; republishing that original keypair.", c.Source);
                var dto = ComposeBundle(old.PublicKey, priv, c, Guid.Empty, opened);
                await _hub.ReplaceKeyBundleAsync(dto, ct).ConfigureAwait(false);
                return (dto, priv, c);
            }
        }
        return null;
    }

    private async Task UnlockAccountBundleAsync(KeyBundleDto acct, List<Candidate> keks,
        Dictionary<Guid, (byte[] Priv, Candidate? ViaKek)> opened, byte[] activePriv, Guid activeId,
        Candidate? canonical, CancellationToken ct)
    {
        (byte[] Priv, Candidate? ViaKek)? hit = null;
        foreach (var c in keks)
        {
            if (_crypto.UnwrapPrivateKey(acct.EncryptedPrivateKey, acct.WrapNonce, c.Kek) is { } priv)
            {
                hit = (priv, c);
                break;
            }
        }
        if (hit is null)
        {
            foreach (var (_, o) in opened)
            {
                var wrapKey = _crypto.DeriveAccountWrapKey(o.Priv, acct.PublicKey);
                var priv = acct is { ProfileWrappedPrivateKey.Length: > 0, ProfileWrapNonce.Length: > 0 }
                    ? _crypto.UnwrapPrivateKey(acct.ProfileWrappedPrivateKey, acct.ProfileWrapNonce, wrapKey)
                    : null;
                priv ??= _crypto.UnwrapPrivateKey(acct.EncryptedPrivateKey, acct.WrapNonce, wrapKey);
                if (priv is not null)
                {
                    hit = (priv, null);
                    break;
                }
            }
        }
        if (hit is not { } account)
        {
            _log.Warning("[AccountUnlock] The messenger account bundle would not open; the recovery gate handles it.");
            return;
        }
        if (canonical is { } target
            && !(account.ViaKek is { } via && SameParams(via, target) && StampsMatch(acct, target)
                && acct.ProfileWrappedPrivateKey is { Length: > 0 }))
        {
            var (wrapped, wrapNonce) = _crypto.WrapPrivateKey(account.Priv, target.Kek);
            var (profileWrapped, profileWrapNonce) =
                _crypto.Encrypt(_crypto.DeriveAccountWrapKey(activePriv, acct.PublicKey), account.Priv);
            var dto = new KeyBundleDto(acct.PublicKey, wrapped, target.Salt, target.Mem, target.Iter, target.Par,
                wrapNonce, activeId, profileWrapped, profileWrapNonce);
            try
            {
                await _hub.RewrapAccountKeyBundleAsync(dto, ct).ConfigureAwait(false);
                _log.Debug("[AccountUnlock] Messenger account bundle converged onto the '{Source}' parameters.", target.Source);
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "[AccountUnlock] Account-bundle convergence failed; keys stay usable locally and the next unlock retries.");
            }
        }
        _keys.StoreAccountKeys(acct.PublicKey, account.Priv);
    }

    private KeyBundleDto ComposeBundle(byte[] publicKey, byte[] privateKey, Candidate target, Guid ownerId,
        Dictionary<Guid, (byte[] Priv, Candidate? ViaKek)> opened)
    {
        var (wrapped, wrapNonce) = _crypto.WrapPrivateKey(privateKey, target.Kek);
        Guid? wrapProfileId = null;
        byte[]? profileWrapped = null;
        byte[]? profileWrapNonce = null;
        foreach (var (id, o) in opened)
        {
            if (id == ownerId)
            {
                continue;
            }
            (profileWrapped, profileWrapNonce) = _crypto.Encrypt(_crypto.DeriveSiblingWrapKey(o.Priv, publicKey), privateKey);
            wrapProfileId = id;
            break;
        }
        return new KeyBundleDto(publicKey, wrapped, target.Salt, target.Mem, target.Iter, target.Par, wrapNonce,
            wrapProfileId, profileWrapped, profileWrapNonce);
    }

    private static bool SameParams(Candidate a, Candidate b)
        => a.Mem == b.Mem && a.Iter == b.Iter && a.Par == b.Par && a.Salt.AsSpan().SequenceEqual(b.Salt);

    private static bool StampsMatch(KeyBundleDto b, Candidate c)
        => b.KdfMemoryKb == c.Mem && b.KdfIterations == c.Iter && b.KdfParallelism == c.Par
            && b.KdfSalt.AsSpan().SequenceEqual(c.Salt);
}
