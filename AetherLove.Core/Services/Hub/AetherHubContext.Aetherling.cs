using System.Threading;
using System.Threading.Tasks;
using AetherLove.Shared.Aetherling;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace AetherLove.Services.Hub;

/// <summary>Aetherling passthroughs.</summary>
public sealed partial class AetherHubContext
{
    public async Task<AetherlingDto?> GetAetherlingAsync(CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<AetherlingDto?>("GetAetherlingAsync", ct).ConfigureAwait(false);

    public async Task<AetherlingDto> PurchaseAethercoreAsync(CancellationToken ct = default)
    {
        try
        {
            return await (await ConnAsync(ct))
                .InvokeAsync<AetherlingDto>("PurchaseAethercoreAsync", ct)
                .ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl)
        {
            throw rl;
        }
    }

    public async Task<AetherlingDto> ChargeAethercoreAsync(CancellationToken ct = default)
    {
        try
        {
            return await (await ConnAsync(ct))
                .InvokeAsync<AetherlingDto>("ChargeAethercoreAsync", ct)
                .ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl)
        {
            throw rl;
        }
    }

    public async Task<AetherlingDto> HatchAethercoreAsync(CancellationToken ct = default)
    {
        try
        {
            return await (await ConnAsync(ct))
                .InvokeAsync<AetherlingDto>("HatchAethercoreAsync", ct)
                .ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl)
        {
            throw rl;
        }
    }

    public async Task ResetAetherlingAsync(CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("ResetAetherlingAsync", ct).ConfigureAwait(false);

    public async Task<AetherlingDto> NameAetherlingAsync(string name, CancellationToken ct = default)
    {
        try
        {
            return await (await ConnAsync(ct))
                .InvokeAsync<AetherlingDto>("NameAetherlingAsync", name, ct)
                .ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl)
        {
            throw rl;
        }
    }
}
