using System;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Services;
using AetherLove.Services.Hub;
using AetherLove.Services.Messenger;
using AetherLove.Shared.Levemetes;
using AetherLove.Shared.Profile;
using AetherOS.Apps.Levemetes;

namespace AetherLove.Os;

/// <summary>The Levemetes app's bridge: hub passthroughs, the server-mediated contact add, and the pending
/// deep-link slot. Sharing goes through the OS share sheet.</summary>
public sealed class LevemetesHostService : ILevemetesHost
{
    private readonly AetherHubContext _hubClient;
    private readonly LevemeteShareContext _shareCtx;
    private readonly MessengerStore _messengerStore;
    private readonly MessengerSyncService _messengerSync;
    private readonly ISocialBridge _social;

    public LevemetesHostService(AetherHubContext hubClient, LevemeteShareContext shareCtx,
        MessengerStore messengerStore, MessengerSyncService messengerSync, ISocialBridge social)
    {
        _hubClient = hubClient;
        _shareCtx = shareCtx;
        _messengerStore = messengerStore;
        _messengerSync = messengerSync;
        _social = social;
    }

    public Task<LevemetesBrowseDto> GetBrowseAsync(LevemetesFilterDto filter, CancellationToken ct = default) =>
        _hubClient.GetLevemetesBrowseAsync(filter, ct);

    public Task<LevemeteDetailDto> GetDetailAsync(Guid adId, CancellationToken ct = default) =>
        _hubClient.GetLevemeteDetailAsync(adId, ct);

    public Task<LevemeteReviewDto[]> GetReviewsAsync(Guid adId, int skip, CancellationToken ct = default) =>
        _hubClient.GetLevemeteReviewsAsync(adId, skip, ct);

    public Task SubmitReviewAsync(Guid adId, short rating, string text, CancellationToken ct = default) =>
        _hubClient.SubmitLevemeteReviewAsync(adId, rating, text, ct);

    public Task DeleteMyReviewAsync(Guid adId, CancellationToken ct = default) =>
        _hubClient.DeleteMyLevemeteReviewAsync(adId, ct);

    public Task<MyLevemeteDto[]> GetMineAsync(CancellationToken ct = default) =>
        _hubClient.GetMyLevemetesAsync(ct);

    public Task<MyLevemeteDto> SaveAdAsync(LevemeteEditDto dto, CancellationToken ct = default) =>
        _hubClient.SaveLevemeteAdAsync(dto, ct);

    public Task DeleteAdAsync(Guid adId, CancellationToken ct = default) =>
        _hubClient.DeleteLevemeteAdAsync(adId, ct);

    public Task<MyLevemeteDto> RenewAdAsync(Guid adId, CancellationToken ct = default) =>
        _hubClient.RenewLevemeteAdAsync(adId, ct);

    public Task<MyLevemeteDto> SetImageAsync(Guid adId, short slot, PhotoUploadDto upload, CancellationToken ct = default) =>
        _hubClient.SetLevemeteImageAsync(adId, slot, upload, ct);

    public Task<MyLevemeteDto> RemoveImageAsync(Guid adId, short slot, CancellationToken ct = default) =>
        _hubClient.RemoveLevemeteImageAsync(adId, slot, ct);

    public async Task AddContactAsync(Guid adId, CancellationToken ct = default)
    {
        await _hubClient.AddLevemeteContactAsync(adId, ct).ConfigureAwait(false);
        try
        {
            await _messengerSync.SyncAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "[LevemetesHost] Messenger resync after contact add failed.");
        }
    }

    public Task ReportAdAsync(Guid adId, string reason, CancellationToken ct = default) =>
        _hubClient.ReportLevemeteAdAsync(adId, reason, ct);

    public (Guid AdId, string? ReturnApp)? TakePendingOpen()
    {
        var pending = _shareCtx.PendingOpenLevemeteId;
        var returnApp = _shareCtx.PendingOpenReturnApp;
        _shareCtx.PendingOpenLevemeteId = null;
        _shareCtx.PendingOpenReturnApp = null;
        return pending is { } adId ? (adId, returnApp) : null;
    }

    public void OpenLoveChat() => _social.OpenChat();

    public bool MessengerAddsEnabled => _messengerStore.Sync?.AllowAdds ?? true;
}
