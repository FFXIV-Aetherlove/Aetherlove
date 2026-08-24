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

    public async Task<AetherlingWheelDto> GetAetherlingWheelAsync(CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<AetherlingWheelDto>("GetAetherlingWheelAsync", ct).ConfigureAwait(false);

    public async Task<AetherlingWheelDto> SpinAetherlingWheelAsync(CancellationToken ct = default)
    {
        try
        {
            return await (await ConnAsync(ct))
                .InvokeAsync<AetherlingWheelDto>("SpinAetherlingWheelAsync", ct)
                .ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl)
        {
            throw rl;
        }
    }

    public async Task<AetherlingWheelDto> RevealAetherlingWheelAsync(CancellationToken ct = default)
    {
        try
        {
            return await (await ConnAsync(ct))
                .InvokeAsync<AetherlingWheelDto>("RevealAetherlingWheelAsync", ct)
                .ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl)
        {
            throw rl;
        }
    }

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

    public async Task<AetherlingDto> RenameAetherlingAsync(string name, CancellationToken ct = default)
    {
        try
        {
            return await (await ConnAsync(ct))
                .InvokeAsync<AetherlingDto>("RenameAetherlingAsync", name, ct)
                .ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl)
        {
            throw rl;
        }
    }

    public async Task<AetherlingDto> FeedAetherlingAsync(short element, string? job, CancellationToken ct = default)
    {
        try
        {
            return await (await ConnAsync(ct))
                .InvokeAsync<AetherlingDto>("FeedAetherlingAsync", element, job, ct)
                .ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl)
        {
            throw rl;
        }
    }

    public async Task<AetherlingDto> RevealAetherlingCardAsync(short slot, CancellationToken ct = default)
    {
        try
        {
            return await (await ConnAsync(ct))
                .InvokeAsync<AetherlingDto>("RevealAetherlingCardAsync", slot, ct)
                .ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl)
        {
            throw rl;
        }
    }

    public async Task<AetherlingDto> CompleteAetherlingOnboardingAsync(CancellationToken ct = default)
    {
        try
        {
            return await (await ConnAsync(ct))
                .InvokeAsync<AetherlingDto>("CompleteAetherlingOnboardingAsync", ct)
                .ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl)
        {
            throw rl;
        }
    }

    public async Task<AetherlingDto> SetAetherlingLookAsync(AetherlingLookDto look, CancellationToken ct = default)
    {
        try
        {
            return await (await ConnAsync(ct))
                .InvokeAsync<AetherlingDto>("SetAetherlingLookAsync", look, ct)
                .ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl)
        {
            throw rl;
        }
    }

    /// <summary>Sending half of party pets: whether members of a party this account joins may see its
    /// Aetherling.</summary>
    public async Task<AetherlingDto> SetAetherlingPartySharingAsync(bool shares, CancellationToken ct = default)
    {
        try
        {
            return await (await ConnAsync(ct))
                .InvokeAsync<AetherlingDto>("SetAetherlingPartySharingAsync", shares, ct)
                .ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl)
        {
            throw rl;
        }
    }

    public async Task<AetherlingDto> ReportAetherlingEmoteSightingAsync(string emoteKey, CancellationToken ct = default)
    {
        try
        {
            return await (await ConnAsync(ct))
                .InvokeAsync<AetherlingDto>("ReportAetherlingEmoteSightingAsync", emoteKey, ct)
                .ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl)
        {
            throw rl;
        }
    }
}
