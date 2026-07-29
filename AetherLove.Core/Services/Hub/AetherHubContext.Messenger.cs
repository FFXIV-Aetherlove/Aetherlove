using System;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Shared.Messaging;
using AetherLove.Shared.Messenger;
using AetherLove.Shared.Profile;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace AetherLove.Services.Hub;

/// <summary>Messenger hub methods (account-scoped, independent of the active dating profile).</summary>
public sealed partial class AetherHubContext
{
    public async Task<MessengerSyncDto> GetMessengerSyncAsync(CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<MessengerSyncDto>("GetMessengerSyncAsync", ct).ConfigureAwait(false);

    public async Task AddMessengerContactAsync(string code, CancellationToken ct = default)
    {
        try
        {
            await (await ConnAsync(ct)).InvokeAsync("AddMessengerContactAsync", code, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task AddMessengerGroupContactAsync(Guid groupId, Guid accountId, CancellationToken ct = default)
    {
        try
        {
            await (await ConnAsync(ct)).InvokeAsync("AddMessengerGroupContactAsync", groupId, accountId, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task AcceptMessengerRequestAsync(Guid contactId, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("AcceptMessengerRequestAsync", contactId, ct).ConfigureAwait(false);

    public async Task DeclineMessengerRequestAsync(Guid contactId, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("DeclineMessengerRequestAsync", contactId, ct).ConfigureAwait(false);

    public async Task CancelMessengerRequestAsync(Guid contactId, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("CancelMessengerRequestAsync", contactId, ct).ConfigureAwait(false);

    public async Task RemoveMessengerContactAsync(Guid contactId, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("RemoveMessengerContactAsync", contactId, ct).ConfigureAwait(false);

    public async Task SetMessengerAllowAddsAsync(bool allow, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("SetMessengerAllowAddsAsync", allow, ct).ConfigureAwait(false);

    public async Task BlockMessengerUserAsync(Guid accountId, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("BlockMessengerUserAsync", accountId, ct).ConfigureAwait(false);

    public async Task UnblockMessengerUserAsync(Guid accountId, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("UnblockMessengerUserAsync", accountId, ct).ConfigureAwait(false);

    public async Task<MessengerBlockedDto[]> GetMessengerBlockedAsync(CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<MessengerBlockedDto[]>("GetMessengerBlockedAsync", ct).ConfigureAwait(false);

    public async Task ReportMessengerUserAsync(Guid accountId, string reason, CancellationToken ct = default)
    {
        try
        {
            await (await ConnAsync(ct)).InvokeAsync("ReportMessengerUserAsync", accountId, reason, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task<SendMessageResponse> SendMessengerMessageAsync(SendMessengerMessageRequest req, CancellationToken ct = default)
    {
        try
        {
            return await (await ConnAsync(ct)).InvokeAsync<SendMessageResponse>("SendMessengerMessageAsync", req, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task<MessengerConversationDto> GetMessengerConversationAsync(
        Guid chatId, MessengerChatKind kind, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<MessengerConversationDto>("GetMessengerConversationAsync", chatId, kind, ct).ConfigureAwait(false);

    public async Task<MessengerMessageDto> SendMessengerImageAsync(SendMessengerImageRequest req, CancellationToken ct = default)
    {
        try
        {
            return await (await ConnAsync(ct)).InvokeAsync<MessengerMessageDto>("SendMessengerImageAsync", req, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task<byte[]?> GetMessengerImageAsync(Guid imageId, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<byte[]?>("GetMessengerImageAsync", imageId, ct).ConfigureAwait(false);

    public async Task DeleteMessengerImageAsync(Guid imageId, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("DeleteMessengerImageAsync", imageId, ct).ConfigureAwait(false);

    public async Task<MessengerStorageDto> GetMessengerStorageAsync(CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<MessengerStorageDto>("GetMessengerStorageAsync", ct).ConfigureAwait(false);

    public async Task ReportMessengerImageAsync(Guid imageId, string reason, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("ReportMessengerImageAsync", imageId, reason, ct).ConfigureAwait(false);

    public async Task<Guid[]> MarkMessengerReadAsync(Guid chatId, MessengerChatKind kind, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<Guid[]>("MarkMessengerReadAsync", chatId, kind, ct).ConfigureAwait(false);

    public async Task ReactMessengerMessageAsync(Guid messageId, string token, bool add, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("ReactMessengerMessageAsync", messageId, token, add, ct).ConfigureAwait(false);

    public async Task SetMessengerMessagePinAsync(Guid messageId, bool pinned, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("SetMessengerMessagePinAsync", messageId, pinned, ct).ConfigureAwait(false);

    public async Task DeleteMessengerMessageAsync(Guid messageId, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("DeleteMessengerMessageAsync", messageId, ct).ConfigureAwait(false);

    public async Task SetMessengerChatPinAsync(Guid chatId, MessengerChatKind kind, bool pinned, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("SetMessengerChatPinAsync", chatId, kind, pinned, ct).ConfigureAwait(false);

    public async Task<MessengerGroupDto> CreateMessengerGroupAsync(CreateMessengerGroupRequest req, CancellationToken ct = default)
    {
        try
        {
            return await (await ConnAsync(ct)).InvokeAsync<MessengerGroupDto>("CreateMessengerGroupAsync", req, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task AddMessengerGroupMemberAsync(Guid groupId, Guid accountId, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("AddMessengerGroupMemberAsync", groupId, accountId, ct).ConfigureAwait(false);

    public async Task LeaveMessengerGroupAsync(Guid groupId, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("LeaveMessengerGroupAsync", groupId, ct).ConfigureAwait(false);

    public async Task KickMessengerGroupMemberAsync(Guid groupId, Guid accountId, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("KickMessengerGroupMemberAsync", groupId, accountId, ct).ConfigureAwait(false);

    public async Task DisbandMessengerGroupAsync(Guid groupId, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("DisbandMessengerGroupAsync", groupId, ct).ConfigureAwait(false);

    public async Task SetMessengerGroupNameAsync(Guid groupId, string name, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("SetMessengerGroupNameAsync", groupId, name, ct).ConfigureAwait(false);

    public async Task SetMessengerGroupAvatarAsync(Guid groupId, PhotoUploadDto image, CancellationToken ct = default)
    {
        try
        {
            await (await ConnAsync(ct)).InvokeAsync("SetMessengerGroupAvatarAsync", groupId, image, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task UploadMessengerGroupKeysAsync(UploadGroupKeysRequest req, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("UploadMessengerGroupKeysAsync", req, ct).ConfigureAwait(false);

    public async Task<MessengerGroupKeyDto[]> GetMessengerGroupKeysAsync(Guid groupId, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<MessengerGroupKeyDto[]>("GetMessengerGroupKeysAsync", groupId, ct).ConfigureAwait(false);

    public async Task<Guid[]> GetMessengerMembersMissingKeysAsync(Guid groupId, int epoch, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<Guid[]>("GetMessengerMembersMissingKeysAsync", groupId, epoch, ct).ConfigureAwait(false);

    public async Task UploadAccountKeyBundleAsync(KeyBundleDto dto, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("UploadAccountKeyBundleAsync", dto, ct).ConfigureAwait(false);

    public async Task<KeyBundleDto?> GetAccountKeyBundleAsync(CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<KeyBundleDto?>("GetAccountKeyBundleAsync", ct).ConfigureAwait(false);
}
