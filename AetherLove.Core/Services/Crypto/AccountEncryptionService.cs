using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Config;
using AetherLove.Services.Hub;
using AetherLove.Shared.Crypto;
using AetherLove.Shared.Messaging;
using AetherLove.Shared.Profile;
using AetherLove.Shared.Yapper;
using Dalamud.Plugin.Services;

namespace AetherLove.Services.Crypto;

/// <summary>Account-owned identity provisioning, additive migration and lossless recovery.</summary>
public sealed class AccountEncryptionService(Configuration config, KeyStorageService keys, CryptoService crypto,
    AetherHubContext hub, IPluginLog log)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private LocalKeyring? _local;
    private AccountKeyringDto? _remote;
    private LegacyIdentityDto[] _inventory = [];
    private long _generation;

    /// <summary>Digest of the local keyring as last written to disk by this instance, so an unchanged
    /// keyring is not rewritten on every synchronization. Null forces the next write.</summary>
    private byte[]? _persistedHash;
    public EncryptionState State { get; private set; } = EncryptionState.Offline;
    public Guid AccountId => config.CryptoAccountId ?? Guid.Empty;
    public bool CanRecover => _local is not null && _local.Generation == _generation;
    public bool HasKeyring => _remote is not null;
    public bool HasPassphraseWrap => _remote?.Wraps.Any(w => w.Kind == "passphrase") == true;
    public bool BackupSaved => _remote?.BackupSaved == true;
    public int MissingKeys => _local?.Contents.Unresolved.Count ?? 0;

    /// <summary>Raised when a synchronization succeeds after the keyring was not usable (first sync of a
    /// session, an unlock, a recovery). Raised while the gate is held, so a handler must never await a
    /// call back into this service; it hands the work to another task.</summary>
    public event Action? Opened;

    public sealed class LocalKeyring
    {
        public byte[] Root { get; set; } = [];
        public long Generation { get; set; }
        public AccountKeyringContents Contents { get; set; } = new();
        public RootWrapDto[] Wraps { get; set; } = [];
        public byte[] RecoverySecret { get; set; } = [];
    }

    public async Task<bool> SynchronizeAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await SynchronizeCoreAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            State = ex is CryptographicException ? EncryptionState.CorruptBundle : EncryptionState.Offline;
            log.Warning("[Keyring] Synchronization deferred ({Reason}); existing key material retained.", ex.GetType().Name);
            return false;
        }
        finally { _gate.Release(); }
    }

    private async Task<bool> SynchronizeCoreAsync(CancellationToken ct)
    {
        var account = await hub.GetAccountInfoAsync(ct).ConfigureAwait(false);
        BindAccount(account.AccountId);
        _generation = account.EncryptionGeneration;
        if (_local is not null && _local.Generation != _generation)
        {
            keys.Clear();
            State = EncryptionState.RecoveryRequired;
            return false;
        }
        _remote = await hub.GetAccountKeyringAsync(ct).ConfigureAwait(false);
        _inventory = await hub.GetAccountKeyInventoryAsync(ct).ConfigureAwait(false);
        var seed = GatherLocal();
        ReadLegacyArchives(seed);
        RecoverLegacy(seed);
        byte[]? remoteHash = null;
        var remoteWraps = _remote?.Wraps;
        if (_remote is not null)
        {
            var winner = OpenRemote(_remote, seed);
            if (winner is null)
            {
                State = EncryptionState.RecoveryRequired;
                return false;
            }
            remoteHash = HashOf(winner.Contents);
            KeyringEnvelope.Merge(winner.Contents, seed);
            if (_local is not null)
            {
                KeyringEnvelope.Merge(winner.Contents, _local.Contents);
                winner.RecoverySecret = _local.Contents.RootId == winner.Contents.RootId ? _local.RecoverySecret : [];
            }
            _local = winner;
        }
        else if (_local is null)
        {
            var pass = await hub.GetAccountPassphraseAsync(ct).ConfigureAwait(false);
            var canUseKek = keys.Kek is { } kek && pass is not null && crypto.CheckPassphraseVerifier(pass.Verifier, pass.VerifierNonce, kek);
            if (!canUseKek && (_generation != 0 || seed.Identities.Count == 0))
            {
                State = EncryptionState.UnlockRequired;
                return false;
            }
            _local = new LocalKeyring { Root = RandomNumberGenerator.GetBytes(32), Contents = seed, Generation = _generation };
            _local.Contents.RootId = Guid.NewGuid();
            _local.Contents.WriteSecret = RandomNumberGenerator.GetBytes(32);
            if (canUseKek)
            {
                _local.Wraps = [WrapRoot("passphrase", "passphrase", keys.Kek!, pass)];
            }
            else
            {
                var anchor = seed.Identities.First();
                _local.Wraps = [WrapRoot("legacy", anchor.KeyId, KeyringEnvelope.Derive(anchor.PrivateKey, AccountId, "legacy/" + anchor.KeyId))];
            }
        }
        else
        {
            KeyringEnvelope.Merge(_local.Contents, seed);
        }
        if (!_local.Wraps.Any(w => w.Kind == "passphrase") && keys.Kek is { } currentKek)
        {
            var descriptor = await hub.GetAccountPassphraseAsync(ct).ConfigureAwait(false);
            if (descriptor is not null && crypto.CheckPassphraseVerifier(descriptor.Verifier, descriptor.VerifierNonce, currentKek))
            {
                _local.Wraps = _local.Wraps.Append(WrapRoot("passphrase", "passphrase", currentKek, descriptor)).ToArray();
            }
        }
        RecoverLegacy(_local.Contents);
        UpdateMissing();
        PersistLocal();
        var alreadySaved = remoteHash is not null
            && ReferenceEquals(_local.Wraps, remoteWraps)
            && CryptographicOperations.FixedTimeEquals(remoteHash, HashOf(_local.Contents));
        if (!alreadySaved)
        {
            await CommitAsync(null, ct).ConfigureAwait(false);
        }
        ApplyActiveKeys();
        var wasOpen = State is EncryptionState.Ready or EncryptionState.MigrationIncomplete;
        State = MissingKeys == 0 ? EncryptionState.Ready : EncryptionState.MigrationIncomplete;
        if (alreadySaved && wasOpen)
        {
            log.Debug("[Keyring] Revision {Revision} is current; identities={Count}, unresolved={Missing}.",
                _remote!.Revision, _local.Contents.Identities.Count, MissingKeys);
        }
        else
        {
            log.Information("[Keyring] {Action} revision {Revision}; identities={Count}, unresolved={Missing}.",
                alreadySaved ? "Opened" : "Saved", _remote!.Revision, _local.Contents.Identities.Count, MissingKeys);
        }
        if (!wasOpen)
        {
            Opened?.Invoke();
        }
        return true;
    }

    /// <summary>A digest of the keyring's contents, used only to tell whether a synchronization changed
    /// anything the server does not already hold. A mismatch costs one extra save, never a lost key.</summary>
    private static byte[] HashOf(AccountKeyringContents contents)
    {
        var plain = JsonSerializer.SerializeToUtf8Bytes(contents);
        try { return SHA256.HashData(plain); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    private void BindAccount(Guid accountId)
    {
        if (config.CryptoAccountId != accountId)
        {
            if (config.CryptoAccountId is not null)
            {
                PreserveLegacyBeforeAccountSwitch(config.CryptoAccountId.Value);
                keys.Clear();
            }
            config.CryptoAccountId = accountId;
            _local = null;
            _remote = null;
        }
        var localPath = LocalPath(accountId);
        var protectedBytes = File.Exists(localPath) ? File.ReadAllBytes(localPath) : config.ProtectedKeyrings.GetValueOrDefault(accountId);
        if (_local is null && protectedBytes is not null)
        {
            byte[] plain;
            try { plain = ProtectedData.Unprotect(protectedBytes, accountId.ToByteArray(), DataProtectionScope.CurrentUser); }
            catch (CryptographicException)
            {
                log.Warning("[Keyring] Local protected copy is unavailable on this device; retaining it for recovery.");
                return;
            }
            try
            {
                _local = JsonSerializer.Deserialize<LocalKeyring>(plain) ?? throw new CryptographicException("Invalid local keyring.");
                Validate(_local.Contents, accountId, _local.Contents.RootId);
            }
            finally { CryptographicOperations.ZeroMemory(plain); }
        }
    }

    private void PreserveLegacyBeforeAccountSwitch(Guid accountId)
    {
        var plain = JsonSerializer.SerializeToUtf8Bytes(new LocalKeyring { Contents = GatherLocal(), Generation = _generation });
        try
        {
            var path = LocalPath(accountId) + ".legacy-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var encrypted = ProtectedData.Protect(plain, accountId.ToByteArray(), DataProtectionScope.CurrentUser);
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
            stream.Write(encrypted);
            stream.Flush(true);
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    private void ReadLegacyArchives(AccountKeyringContents seed)
    {
        var path = LocalPath(AccountId);
        var directory = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(directory)) { return; }
        foreach (var file in Directory.EnumerateFiles(directory, Path.GetFileName(path) + ".legacy-*"))
        {
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(file), AccountId.ToByteArray(), DataProtectionScope.CurrentUser);
            try
            {
                var saved = JsonSerializer.Deserialize<LocalKeyring>(plain) ?? throw new CryptographicException("Invalid legacy archive.");
                if (saved.Generation == _generation) { KeyringEnvelope.Merge(seed, saved.Contents); }
            }
            finally { CryptographicOperations.ZeroMemory(plain); }
        }
    }

    private AccountKeyringContents GatherLocal()
    {
        var result = new AccountKeyringContents { AccountId = AccountId };
        if (keys.Kek is { } legacyKek) { result.LegacyKeks.Add(legacyKek.ToArray()); }
        void Add(string surface, Guid owner, byte[] pub, byte[] priv)
        {
            if (owner != Guid.Empty && crypto.IsKeyPair(pub, priv)
                && _inventory.Any(x => x.Surface == surface && x.OwnerId == owner && x.Bundle.PublicKey.AsSpan().SequenceEqual(pub)))
            {
                result.Identities.Add(new(surface, owner, pub.ToArray(), priv.ToArray()));
            }
            else if (priv.Length > 0)
            {
                result.UnmatchedLocal.Add(new(surface, owner, pub.ToArray(), priv.ToArray()));
            }
        }
        Add("love", config.Auth.ActiveProfileId ?? Guid.Empty, config.Crypto.PublicKey, config.Crypto.PrivateKey);
        foreach (var stash in keys.EnumerateStashedKeys())
        {
            Add("love", stash.ProfileId, stash.PublicKey, stash.PrivateKey);
        }
        Add("messenger", AccountId, config.AccountCrypto.PublicKey, config.AccountCrypto.PrivateKey);
        return result;
    }

    private void RecoverLegacy(AccountKeyringContents ring)
    {
        var progress = true;
        while (progress)
        {
            progress = false;
            foreach (var item in _inventory)
            {
                var b = item.Bundle;
                if (Find(ring, item.Surface, item.OwnerId, b.PublicKey) is not null) { continue; }
                byte[]? plain = keys.Kek is { } kek ? crypto.UnwrapPrivateKey(b.EncryptedPrivateKey, b.WrapNonce, kek) : null;
                foreach (var legacyKek in ring.LegacyKeks)
                {
                    if (plain is not null) { break; }
                    plain = crypto.UnwrapPrivateKey(b.EncryptedPrivateKey, b.WrapNonce, legacyKek);
                }
                if (plain is null && _local is not null)
                {
                    plain = crypto.UnwrapPrivateKey(b.EncryptedPrivateKey, b.WrapNonce, IdentityWrapKey(item.Surface, item.OwnerId));
                }
                foreach (var anchor in ring.Identities.ToArray())
                {
                    if (plain is not null) { break; }
                    byte[]? wrapKey = item.Surface switch
                    {
                        "messenger" when anchor.Surface == "love" => crypto.DeriveAccountWrapKey(anchor.PrivateKey, b.PublicKey),
                        "yapper" when anchor.Surface == "messenger" => crypto.DeriveYapperWrapKey(anchor.PrivateKey, b.PublicKey),
                        "love" when anchor.Surface == "messenger" => crypto.DeriveProfileAccountWrapKey(anchor.PrivateKey, b.PublicKey),
                        "love" when anchor.Surface == "love" => crypto.DeriveSiblingWrapKey(anchor.PrivateKey, b.PublicKey),
                        _ => null,
                    };
                    if (wrapKey is null) { continue; }
                    plain = crypto.UnwrapPrivateKey(b.EncryptedPrivateKey, b.WrapNonce, wrapKey);
                    if (plain is null && b.ProfileWrappedPrivateKey is { } second && b.ProfileWrapNonce is { } nonce)
                    {
                        plain = crypto.UnwrapPrivateKey(second, nonce, wrapKey);
                    }
                }
                if (plain is not null && crypto.IsKeyPair(b.PublicKey, plain))
                {
                    ring.Identities.Add(new(item.Surface, item.OwnerId, b.PublicKey, plain));
                    progress = true;
                }
            }
        }
    }

    private void UpdateMissing()
    {
        _local!.Contents.Unresolved = _inventory.Where(i => Find(_local.Contents, i.Surface, i.OwnerId, i.Bundle.PublicKey) is null)
            .Select(i => $"{i.Surface}/{i.OwnerId:N}/{Convert.ToHexString(SHA256.HashData(i.Bundle.PublicKey))}")
            .Concat(_local.Contents.UnmatchedLocal.Select(i => $"local/{i.Surface}/{i.OwnerId:N}/{i.KeyId}")).ToList();
    }

    private LocalKeyring? OpenRemote(AccountKeyringDto remote, AccountKeyringContents seed, byte[]? explicitRoot = null)
    {
        byte[]? root = explicitRoot ?? (_local?.Contents.RootId == remote.RootId ? _local.Root : null);
        foreach (var wrap in remote.Wraps)
        {
            if (root is not null) { break; }
            byte[]? secret = wrap.Kind == "passphrase" ? keys.Kek : null;
            if (wrap.Kind == "legacy" && seed.Identities.FirstOrDefault(i => i.KeyId == wrap.KeyId) is { } anchor)
            {
                secret = KeyringEnvelope.Derive(anchor.PrivateKey, AccountId, "legacy/" + anchor.KeyId);
            }
            if (secret is null) { continue; }
            try { root = KeyringEnvelope.Open(secret, wrap.Ciphertext, wrap.Nonce, KeyringEnvelope.Context(AccountId, remote.RootId, wrap.Kind + "/" + wrap.KeyId)); }
            catch (CryptographicException) { }
        }
        if (root is null) { return null; }
        var plain = KeyringEnvelope.Open(KeyringEnvelope.Derive(root, AccountId, "contents"), remote.Ciphertext, remote.Nonce,
            KeyringEnvelope.Context(AccountId, remote.RootId, "contents"));
        try
        {
            var contents = JsonSerializer.Deserialize<AccountKeyringContents>(plain) ?? throw new CryptographicException("Invalid keyring.");
            Validate(contents, AccountId, remote.RootId);
            return new LocalKeyring { Root = root, Contents = contents, Wraps = remote.Wraps, Generation = _generation };
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    private void Validate(AccountKeyringContents contents, Guid accountId, Guid rootId)
    {
        if (contents.Version != 1 || contents.AccountId != accountId || contents.RootId != rootId
            || contents.WriteSecret.Length != 32 || contents.Identities.Any(i => !crypto.IsKeyPair(i.PublicKey, i.PrivateKey))
            || contents.Groups.Any(g => g.Key.Length != 32 || g.Epoch < 1) || contents.LegacyKeks.Any(k => k.Length != 32))
        {
            throw new CryptographicException("Invalid keyring contents.");
        }
    }

    private RootWrapDto WrapRoot(string kind, string keyId, byte[] secret, AccountPassphraseDto? pass = null)
    {
        var sealedRoot = KeyringEnvelope.Seal(secret, _local!.Root, KeyringEnvelope.Context(AccountId, _local.Contents.RootId, kind + "/" + keyId));
        return new(kind, keyId, sealedRoot.Ciphertext, sealedRoot.Nonce, pass);
    }

    private void PersistLocal()
    {
        var plain = JsonSerializer.SerializeToUtf8Bytes(_local);
        try
        {
            var path = LocalPath(AccountId);
            var hash = SHA256.HashData(plain);
            if (_persistedHash is not null && File.Exists(path) && CryptographicOperations.FixedTimeEquals(hash, _persistedHash))
            {
                return;
            }
            var encrypted = ProtectedData.Protect(plain, AccountId.ToByteArray(), DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(encrypted);
                stream.Flush(true);
            }
            var verified = ProtectedData.Unprotect(File.ReadAllBytes(temporary), AccountId.ToByteArray(), DataProtectionScope.CurrentUser);
            try
            {
                if (!CryptographicOperations.FixedTimeEquals(plain, verified)) { throw new IOException("Local keyring verification failed."); }
            }
            finally { CryptographicOperations.ZeroMemory(verified); }
            if (File.Exists(path)) { File.Replace(temporary, path, path + ".bak"); }
            else { File.Move(temporary, path); }
            config.ProtectedKeyrings[AccountId] = encrypted;
            config.Save();
            _persistedHash = hash;
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    private static string LocalPath(Guid accountId)
        => Path.Combine(UiHost.PluginInterface.ConfigFile.FullName + ".keys", accountId.ToString("N") + ".bin");

    public KeyringIdentity? FindIdentity(string surface, byte[] publicKey)
        => _local?.Contents.Identities.FirstOrDefault(i => i.Surface == surface && i.PublicKey.AsSpan().SequenceEqual(publicKey));

    public async Task RetireLocalAfterExplicitResetAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            RetireLocalCore();
        }
        finally { _gate.Release(); }
    }

    private void RetireLocalCore()
    {
        var path = LocalPath(AccountId);
        if (File.Exists(path)) { File.Move(path, path + ".retired-" + Guid.NewGuid().ToString("N")); }
        config.ProtectedKeyrings.Remove(AccountId);
        _local = null;
        _remote = null;
        keys.Clear();
        config.Save();
    }

    private async Task CommitAsync(AccountPassphraseDto? pass, CancellationToken ct, bool backupSaved = false)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var plain = JsonSerializer.SerializeToUtf8Bytes(_local!.Contents);
            (byte[] Ciphertext, byte[] Nonce) sealedRing;
            try { sealedRing = KeyringEnvelope.Seal(KeyringEnvelope.Derive(_local.Root, AccountId, "contents"), plain, KeyringEnvelope.Context(AccountId, _local.Contents.RootId, "contents")); }
            finally { CryptographicOperations.ZeroMemory(plain); }
            var next = new AccountKeyringDto(AccountId, _local.Contents.RootId, 0, Guid.NewGuid(), sealedRing.Ciphertext, sealedRing.Nonce,
                _local.Wraps, backupSaved || _remote?.BackupSaved == true);
            try
            {
                var saved = await hub.SaveAccountKeyringAsync(new(next, _remote?.Revision ?? 0, _local.Contents.WriteSecret, pass, _generation), ct).ConfigureAwait(false);
                var check = await hub.GetAccountKeyringAsync(ct).ConfigureAwait(false) ?? throw new IOException("Keyring read-back failed.");
                var opened = OpenRemote(check, _local.Contents) ?? throw new CryptographicException("Keyring read-back failed.");
                KeyringEnvelope.Merge(opened.Contents, _local.Contents);
                opened.RecoverySecret = _local.RecoverySecret;
                _local = opened;
                _remote = check;
                PersistLocal();
                if (check.Revision != saved.Revision && pass is not null) { throw new IOException("Passphrase revision changed; retry required."); }
                return;
            }
            catch (Microsoft.AspNetCore.SignalR.HubException ex) when (ex.Message.Contains("keyring_conflict") || ex.Message.Contains("keyring_recovery_required"))
            {
                if (pass is not null) { throw; }
                var winner = await hub.GetAccountKeyringAsync(ct).ConfigureAwait(false) ?? throw new IOException("Keyring conflict.");
                var opened = OpenRemote(winner, _local.Contents) ?? throw new CryptographicException("Winning root requires recovery.");
                KeyringEnvelope.Merge(opened.Contents, _local.Contents);
                opened.RecoverySecret = opened.Contents.RootId == _local.Contents.RootId ? _local.RecoverySecret : [];
                _local = opened;
                _remote = winner;
                PersistLocal();
            }
        }
        throw new IOException("Keyring changed repeatedly; retry required.");
    }

    private static KeyringIdentity? Find(AccountKeyringContents ring, string surface, Guid owner, byte[] pub)
        => ring.Identities.Find(i => i.Surface == surface && i.OwnerId == owner && i.PublicKey.AsSpan().SequenceEqual(pub));

    private void ApplyActiveKeys()
    {
        config.Crypto = new CryptoKeys();
        config.AccountCrypto = new CryptoKeys();
        foreach (var item in _inventory.Where(x => x.Active))
        {
            var pair = Find(_local!.Contents, item.Surface, item.OwnerId, item.Bundle.PublicKey);
            if (pair is null) { continue; }
            if (item.Surface == "messenger") { keys.StoreAccountKeys(pair.PublicKey, pair.PrivateKey); }
            if (item.Surface == "love")
            {
                if (config.Auth.ActiveProfileId == item.OwnerId) { keys.Store(pair.PublicKey, pair.PrivateKey); }
                else { keys.StashSiblingKeys(item.OwnerId, pair.PublicKey, pair.PrivateKey); }
            }
        }
    }

    private byte[] IdentityWrapKey(string surface, Guid owner)
        => KeyringEnvelope.Derive(_local!.Root, AccountId, $"identity/{surface}/{owner:N}");

    public async Task<KeyringIdentity?> EnsureIdentityAsync(string surface, Guid owner, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!await SynchronizeCoreAsync(ct).ConfigureAwait(false)) { return null; }
            if (surface == "messenger") { owner = AccountId; }
            if (surface == "yapper")
            {
                var profile = await hub.GetMyYapperProfileAsync(ct).ConfigureAwait(false);
                if (profile is null) { return null; }
                owner = profile.ProfileId;
            }
            var existing = _inventory.SingleOrDefault(i => i.Active && i.Surface == surface && i.OwnerId == owner);
            if (existing is not null) { return Find(_local!.Contents, surface, owner, existing.Bundle.PublicKey); }
            if (owner == Guid.Empty) { return null; }
            var pair = _local!.Contents.Identities.FirstOrDefault(i => i.Surface == surface && i.OwnerId == owner);
            if (pair is null)
            {
                var generated = crypto.GenerateIdentityKeyPair();
                pair = new(surface, owner, generated.PublicKey, generated.PrivateKey);
                _local.Contents.Identities.Add(pair);
            }
            PersistLocal();
            await CommitAsync(null, ct).ConfigureAwait(false);
            var (wrapped, nonce) = crypto.WrapPrivateKey(pair.PrivateKey, IdentityWrapKey(surface, owner));
            var bundle = new KeyBundleDto(pair.PublicKey, wrapped, new byte[16], 0, 0, 0, nonce);
            try
            {
                await hub.PublishKeyringIdentityAsync(new(surface, owner, _local.Contents.RootId,
                    _generation, _local.Contents.WriteSecret, bundle), ct).ConfigureAwait(false);
            }
            catch (Microsoft.AspNetCore.SignalR.HubException ex) when (ex.Message.Contains("key_bundle_exists"))
            {
                await SynchronizeCoreAsync(ct).ConfigureAwait(false);
                var winner = _inventory.Single(i => i.Active && i.Surface == surface && i.OwnerId == owner);
                return Find(_local!.Contents, surface, owner, winner.Bundle.PublicKey);
            }
            _inventory = await hub.GetAccountKeyInventoryAsync(ct).ConfigureAwait(false);
            ApplyActiveKeys();
            return pair;
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> UnlockAsync(string passphrase, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var account = await hub.GetAccountInfoAsync(ct).ConfigureAwait(false);
            BindAccount(account.AccountId);
            _generation = account.EncryptionGeneration;
            var remote = await hub.GetAccountKeyringAsync(ct).ConfigureAwait(false);
            _remote = remote;
            if (remote is null && _local is not null && _local.Generation != _generation)
            {
                var descriptor = await hub.GetAccountPassphraseAsync(ct).ConfigureAwait(false);
                if (descriptor is null) { return false; }
                var resetKek = crypto.DeriveKEK(passphrase, descriptor.KdfSalt, descriptor.KdfMemoryKb, descriptor.KdfIterations, descriptor.KdfParallelism);
                if (!crypto.CheckPassphraseVerifier(descriptor.Verifier, descriptor.VerifierNonce, resetKek)) { return false; }
                RetireLocalCore();
                keys.StoreKek(resetKek, descriptor.KdfSalt, descriptor.KdfMemoryKb, descriptor.KdfIterations, descriptor.KdfParallelism);
                return await SynchronizeCoreAsync(ct).ConfigureAwait(false);
            }
            var wrap = remote?.Wraps.SingleOrDefault(w => w.Kind == "passphrase");
            if (wrap?.Passphrase is not { } p) { return false; }
            var kek = crypto.DeriveKEK(passphrase, p.KdfSalt, p.KdfMemoryKb, p.KdfIterations, p.KdfParallelism);
            if (!crypto.CheckPassphraseVerifier(p.Verifier, p.VerifierNonce, kek)) { return false; }
            var root = KeyringEnvelope.Open(kek, wrap.Ciphertext, wrap.Nonce, KeyringEnvelope.Context(AccountId, remote!.RootId, "passphrase/passphrase"));
            var opened = OpenRemote(remote, new() { AccountId = AccountId }, root)!;
            if (_local is not null && _local.Generation == _generation) { KeyringEnvelope.Merge(opened.Contents, _local.Contents); }
            _local = opened;
            _remote = remote;
            keys.StoreKek(kek, p.KdfSalt, p.KdfMemoryKb, p.KdfIterations, p.KdfParallelism);
            PersistLocal();
            return await SynchronizeCoreAsync(ct).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task ChangePassphraseAsync(string passphrase, CancellationToken ct = default)
    {
        if (passphrase.Length < 12) { throw new ArgumentException("Passphrase must contain at least 12 characters."); }
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!await SynchronizeCoreAsync(ct).ConfigureAwait(false)) { throw new InvalidOperationException("Unlock your keys first."); }
            var salt = RandomNumberGenerator.GetBytes(16);
            var kek = crypto.DeriveKEK(passphrase, salt, 65536, 3, 1);
            var verifier = crypto.CreatePassphraseVerifier(kek);
            var pass = new AccountPassphraseDto(salt, 65536, 3, 1, verifier.Verifier, verifier.Nonce);
            _local!.Wraps = _local.Wraps.Where(w => w.Kind != "passphrase").Append(WrapRoot("passphrase", "passphrase", kek, pass)).ToArray();
            PersistLocal();
            await CommitAsync(pass, ct).ConfigureAwait(false);
            keys.StoreKek(kek, salt, 65536, 3, 1);
        }
        finally { _gate.Release(); }
    }

    public async Task SaveRecoveryFileAsync(string path, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!await SynchronizeCoreAsync(ct).ConfigureAwait(false)) { throw new InvalidOperationException("Unlock your keys first."); }
            if (_local!.RecoverySecret.Length != 32)
            {
                _local.RecoverySecret = RandomNumberGenerator.GetBytes(32);
            }
            var id = Convert.ToHexString(SHA256.HashData(_local.RecoverySecret));
            if (!_local.Wraps.Any(w => w.Kind == "recovery" && w.KeyId == id))
            {
                _local.Wraps = _local.Wraps.Append(WrapRoot("recovery", id, KeyringEnvelope.Derive(_local.RecoverySecret, AccountId, "recovery"))).ToArray();
                PersistLocal();
                await CommitAsync(null, ct).ConfigureAwait(false);
            }
            var file = new RecoveryFile(AccountId, _local.Contents.RootId, _local.RecoverySecret);
            var text = file.Encode();
            await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
                stream.Flush(true);
            }
            var check = RecoveryFile.Decode(await File.ReadAllTextAsync(path, ct).ConfigureAwait(false));
            if (!CryptographicOperations.FixedTimeEquals(check.Secret, file.Secret)) { throw new IOException("Recovery file read-back failed."); }
            await CommitAsync(null, ct, backupSaved: true).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task RestoreRecoveryFileAsync(string path, CancellationToken ct = default)
    {
        if (new FileInfo(path).Length > 8192) { throw new FormatException("Recovery file is too large."); }
        var file = RecoveryFile.Decode(await File.ReadAllTextAsync(path, ct).ConfigureAwait(false));
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var account = await hub.GetAccountInfoAsync(ct).ConfigureAwait(false);
            BindAccount(account.AccountId);
            _generation = account.EncryptionGeneration;
            var remote = await hub.GetAccountKeyringAsync(ct).ConfigureAwait(false) ?? throw new InvalidOperationException("No recovery backup.");
            if (file.AccountId != AccountId || file.RootId != remote.RootId) { throw new CryptographicException("Recovery file belongs to another account or encryption identity."); }
            var id = Convert.ToHexString(SHA256.HashData(file.Secret));
            var wrap = remote.Wraps.Single(w => w.Kind == "recovery" && w.KeyId == id);
            var root = KeyringEnvelope.Open(KeyringEnvelope.Derive(file.Secret, AccountId, "recovery"), wrap.Ciphertext, wrap.Nonce,
                KeyringEnvelope.Context(AccountId, remote.RootId, "recovery/" + id));
            var opened = OpenRemote(remote, new() { AccountId = AccountId }, root)!;
            if (_local is not null && _local.Generation == _generation) { KeyringEnvelope.Merge(opened.Contents, _local.Contents); }
            _local = opened;
            _remote = remote;
            _local.RecoverySecret = file.Secret;
            PersistLocal();
            await SynchronizeCoreAsync(ct).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public byte[]? TryDecryptHistory(string surface, Guid owner, IEnumerable<byte[]> peerKeys, byte[] ciphertext, byte[] nonce)
    {
        if (!CanRecover) { return null; }
        foreach (var identity in _local!.Contents.Identities.ToArray().Where(i => i.Surface == surface && i.OwnerId == owner))
        {
            foreach (var peer in peerKeys.Take(128))
            {
                try
                {
                    var key = crypto.DeriveMessageKey(crypto.DeriveSharedSecret(identity.PrivateKey, peer),
                        CryptoService.DeriveConversationSalt(identity.PublicKey, peer));
                    return crypto.Decrypt(key, nonce, ciphertext);
                }
                catch (CryptographicException) { }
            }
        }
        return null;
    }

    public byte[]? FindGroupKey(Guid groupId, int epoch)
        => _local?.Contents.Groups.Find(g => g.GroupId == groupId && g.Epoch == epoch)?.Key.ToArray();

    public async Task ArchiveGroupKeyAsync(Guid groupId, int epoch, byte[] key, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_local is null) { return; }
            KeyringEnvelope.Merge(_local.Contents, new() { AccountId = AccountId, Groups = [new(groupId, epoch, key.ToArray())] });
            PersistLocal();
            await CommitAsync(null, ct).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }
}
