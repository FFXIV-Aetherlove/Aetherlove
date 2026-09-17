using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using AetherLove.Services.Crypto;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Services.Hub;
using AetherLove.Shared.Messenger;
using Dalamud.Plugin.Services;

namespace AetherLove.Services.Messenger;

/// <summary>Messenger orchestration: the sync fetch on connect, the group key ring (fetch + unwrap my wraps),
/// and the owner-side key duty (generate a fresh epoch key after a rotation, wrap every held epoch for members
/// that miss one, e.g. a fresh join).</summary>
public sealed class MessengerSyncService
{
    private readonly AetherHubContext _hub;
    private readonly MessengerStore _store;
    private readonly MessengerCryptoService _crypto;
    private readonly IPluginLog _log;
    private readonly AccountEncryptionService _encryption;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public MessengerSyncService(AetherHubContext hub, MessengerStore store, MessengerCryptoService crypto, IPluginLog log, AccountEncryptionService encryption)
    {
        _encryption = encryption;
        _hub = hub;
        _store = store;
        _crypto = crypto;
        _log = log;
    }

    /// <summary>Full refresh: provision the account keypair if needed, replace the snapshot, then run the
    /// owner key duty. Safe to call on every connect; failures log and leave the cached state standing.
    /// Single-flight like <see cref="Chat.ChatSyncService"/>: overlapping callers coalesce into one run.</summary>
    public async Task SyncAsync(CancellationToken ct = default)
    {
        if (!_hub.IsConnected)
        {
            return;
        }
        if (!await _gate.WaitAsync(0, ct).ConfigureAwait(false))
        {
            return;
        }
        try
        {
            var sync = await _hub.GetMessengerSyncAsync(ct).ConfigureAwait(false);
            _store.EnsureOwner(sync.MyAccountId);
            _store.ApplySync(sync);
            await RefreshActiveConversationAsync(ct).ConfigureAwait(false);
            await _crypto.EnsureProvisionedAsync(ct).ConfigureAwait(false);
            await MaintainOwnedGroupKeysAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[MessengerSync] sync failed; keeping the cached snapshot.");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Pushes missed during an outage are gone for good, so the conversation the user is looking at
    /// refetches with each sync; closed chats self-heal through OpenChat's own fetch.</summary>
    private async Task RefreshActiveConversationAsync(CancellationToken ct)
    {
        if (_store.ActiveChatId is not { } chatId)
        {
            return;
        }
        MessengerChatKind kind;
        if (_store.Contact(chatId) is not null)
        {
            kind = MessengerChatKind.Direct;
        }
        else if (_store.Group(chatId) is not null)
        {
            kind = MessengerChatKind.Group;
        }
        else
        {
            return;
        }
        try
        {
            var convo = await _hub.GetMessengerConversationAsync(chatId, kind, ct).ConfigureAwait(false);
            _store.SetConversation(chatId, convo.Messages);
            _store.SetPeerKeyHistory(chatId, convo.PeerKeyHistory);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, $"[MessengerSync] refreshing the open conversation {chatId} failed.");
        }
    }

    /// <summary>Fetches and unwraps my key wraps for a group, filling the ring for every epoch it can. Returns
    /// true when the group's CURRENT epoch key is available afterwards.</summary>
    public async Task<bool> EnsureGroupKeysAsync(Guid groupId, CancellationToken ct = default)
    {
        var group = _store.Group(groupId);
        if (group is null || !_crypto.HasAccountKeys)
        {
            return false;
        }
        if (_store.MissingEpochs(groupId, group.KeyEpoch).Length == 0)
        {
            return true;
        }
        try
        {
            var wraps = await _hub.GetMessengerGroupKeysAsync(groupId, ct).ConfigureAwait(false);
            var wrapperKeys = group.Members.ToDictionary(m => m.AccountId, m => m.PublicKey);
            foreach (var wrap in wraps)
            {
                if (_store.GroupKey(groupId, wrap.Epoch) is not null)
                {
                    continue;
                }
                if (_encryption.FindGroupKey(groupId, wrap.Epoch) is { } archived)
                {
                    _store.StoreGroupKey(groupId, wrap.Epoch, archived);
                    continue;
                }
                if ((wrap.WrapperPublicKey ?? wrapperKeys.GetValueOrDefault(wrap.WrapperAccountId)) is not { Length: 32 } wrapperPub)
                {
                    continue;
                }
                if (_crypto.UnwrapGroupKey(wrap.WrappedKey, wrap.Nonce, wrapperPub, wrap.RecipientPublicKey) is { } key)
                {
                    _store.StoreGroupKey(groupId, wrap.Epoch, key);
                    await _encryption.ArchiveGroupKeyAsync(groupId, wrap.Epoch, key, ct).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            _log.Warning(ex, $"[MessengerSync] fetching group keys for {groupId} failed.");
        }
        return _store.GroupKey(groupId, group.KeyEpoch) is not null;
    }

    /// <summary>The owner's key duty across every owned group: after a rotation (epoch bumped, no key held)
    /// generate the new epoch's key, then wrap every held epoch for members missing one and upload.</summary>
    public async Task MaintainOwnedGroupKeysAsync(CancellationToken ct = default)
    {
        if (!_crypto.HasAccountKeys)
        {
            return;
        }
        var me = _store.MyAccountId;
        foreach (var group in _store.Groups.Where(g => g.OwnerAccountId == me))
        {
            try
            {
                await EnsureGroupKeysAsync(group.GroupId, ct).ConfigureAwait(false);
                if (_store.GroupKey(group.GroupId, group.KeyEpoch) is null)
                {
                    var state = await _hub.GetMessengerGroupEpochAsync(group.GroupId, group.KeyEpoch, ct).ConfigureAwait(false);
                    if (state.Established)
                    {
                        continue;
                    }
                    var candidate = MessengerCryptoService.GenerateGroupKey();
                    var initial = group.Members.Where(m => m.PublicKey is { Length: 32 }).Select(m =>
                    {
                        var wrapped = _crypto.WrapGroupKey(candidate, m.PublicKey!)!.Value;
                        return new MessengerGroupKeyWrapDto(m.AccountId, wrapped.WrappedKey, wrapped.Nonce, m.PublicKey);
                    }).ToArray();
                    await _hub.UploadMessengerGroupKeysAsync(new UploadGroupKeysRequest(group.GroupId, group.KeyEpoch,
                        initial, SHA256.HashData(candidate), _crypto.AccountPublicKey), ct).ConfigureAwait(false);
                    _store.StoreGroupKey(group.GroupId, group.KeyEpoch, candidate);
                    await _encryption.ArchiveGroupKeyAsync(group.GroupId, group.KeyEpoch, candidate, ct).ConfigureAwait(false);
                }
                for (var epoch = 1; epoch <= group.KeyEpoch; epoch++)
                {
                    if (_store.GroupKey(group.GroupId, epoch) is not { } key)
                    {
                        continue;
                    }
                    var missing = await _hub.GetMessengerMembersMissingKeysAsync(group.GroupId, epoch, ct)
                        .ConfigureAwait(false);
                    if (missing.Length == 0)
                    {
                        continue;
                    }
                    var members = group.Members.ToDictionary(m => m.AccountId, m => m.PublicKey);
                    var wraps = new List<MessengerGroupKeyWrapDto>();
                    foreach (var target in missing)
                    {
                        if (members.GetValueOrDefault(target) is not { Length: > 0 } pub)
                        {
                            continue;
                        }
                        if (_crypto.WrapGroupKey(key, pub) is { } wrapped)
                        {
                            wraps.Add(new MessengerGroupKeyWrapDto(target, wrapped.WrappedKey, wrapped.Nonce, pub));
                        }
                    }
                    if (wraps.Count > 0)
                    {
                        await _hub.UploadMessengerGroupKeysAsync(
                                new UploadGroupKeysRequest(group.GroupId, epoch, wraps.ToArray(), SHA256.HashData(key), _crypto.AccountPublicKey), ct)
                            .ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _log.Warning(ex, $"[MessengerSync] key maintenance for group {group.GroupId} failed.");
            }
        }
    }
}
