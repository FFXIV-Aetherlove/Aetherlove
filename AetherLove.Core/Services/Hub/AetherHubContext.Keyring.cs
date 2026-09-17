using System.Threading;
using System.Threading.Tasks;
using AetherLove.Shared.Crypto;
using Microsoft.AspNetCore.SignalR.Client;

namespace AetherLove.Services.Hub;

public sealed partial class AetherHubContext
{
    public async Task PublishKeyringIdentityAsync(KeyringIdentityUpload request, CancellationToken ct = default)
        => await (await ConnAsync(ct)).InvokeAsync("PublishKeyringIdentityAsync", request, ct).ConfigureAwait(false);

    public async Task<LegacyIdentityDto[]> GetAccountKeyInventoryAsync(CancellationToken ct = default)
        => await (await ConnAsync(ct)).InvokeAsync<LegacyIdentityDto[]>("GetAccountKeyInventoryAsync", ct).ConfigureAwait(false);

    public async Task<AetherLove.Shared.Messenger.MessengerGroupEpochDto> GetMessengerGroupEpochAsync(System.Guid groupId, int epoch, CancellationToken ct = default)
        => await (await ConnAsync(ct)).InvokeAsync<AetherLove.Shared.Messenger.MessengerGroupEpochDto>("GetMessengerGroupEpochAsync", groupId, epoch, ct).ConfigureAwait(false);

    public async Task<AccountKeyringDto?> GetAccountKeyringAsync(CancellationToken ct = default)
        => await (await ConnAsync(ct)).InvokeAsync<AccountKeyringDto?>("GetAccountKeyringAsync", ct).ConfigureAwait(false);

    public async Task<AccountKeyringDto> SaveAccountKeyringAsync(SaveAccountKeyringRequest request, CancellationToken ct = default)
        => await (await ConnAsync(ct)).InvokeAsync<AccountKeyringDto>("SaveAccountKeyringAsync", request, ct).ConfigureAwait(false);
}
