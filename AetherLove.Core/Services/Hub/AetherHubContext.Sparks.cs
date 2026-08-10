using System.Threading;
using System.Threading.Tasks;
using AetherLove.Shared.Sparks;
using Microsoft.AspNetCore.SignalR.Client;

namespace AetherLove.Services.Hub;

/// <summary>Sparks activity passthroughs.</summary>
public sealed partial class AetherHubContext
{
    public async Task<int> ReportSparkActivityAsync(short action, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<int>("ReportSparkActivityAsync", action, ct).ConfigureAwait(false);

    public async Task<SparkStatusDto> GetSparkStatusAsync(CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<SparkStatusDto>("GetSparkStatusAsync", ct).ConfigureAwait(false);

    public async Task<SparkWalletDto> GetSparkWalletAsync(CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<SparkWalletDto>("GetSparkWalletAsync", ct).ConfigureAwait(false);

    public async Task<SparkLedgerPageDto> GetSparkLedgerAsync(long? beforeSequence, int take, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<SparkLedgerPageDto>("GetSparkLedgerAsync", beforeSequence, take, ct).ConfigureAwait(false);
}
