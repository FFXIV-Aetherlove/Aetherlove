using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Services.Localization;
using AetherLove.Services.Market;
using AetherLove.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace AetherLove.Services;

/// <summary>Background evaluator for market price alerts: every few minutes it batch-fetches the watched
/// items per scope through the shared cached client and fires crossings on all three surfaces (native chat
/// line, OS notification, tile badge). Alerts are one-shot: a fired alert switches itself off until the
/// user re-enables it.</summary>
public sealed class MarketAlertService : IDisposable
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(4);

    private readonly MarketDataService _data;
    private readonly MarketAlertStore _store;
    private readonly NotificationDispatcher _notifier;
    private readonly Os.MarketDeskService _desk;
    private readonly IServiceProvider _services;
    private CancellationTokenSource? _cts;

    public MarketAlertService(MarketDataService data, MarketAlertStore store, NotificationDispatcher notifier,
        Os.MarketDeskService desk, IServiceProvider services)
    {
        _data = data;
        _store = store;
        _notifier = notifier;
        _desk = desk;
        _services = services;
    }

    public void Start()
    {
        if (_cts is not null)
        {
            return;
        }
        _cts = new CancellationTokenSource();
        _ = RunLoopAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    public void Dispose() => Stop();

    private async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TickInterval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            try
            {
                await TickAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "[MarketAlerts] Tick failed.");
            }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        if (!PhonePower.IsOn)
        {
            return;
        }
        var loggedIn = await Plugin.Framework.RunOnFrameworkThread(() => Plugin.ClientState.IsLoggedIn)
            .ConfigureAwait(false);
        if (!loggedIn)
        {
            return;
        }

        // Undercut state refreshes silently: the My sales screen shows the markers, but by explicit
        // request there is no undercut notification.
        await _desk.RefreshUndercutsCoreAsync().ConfigureAwait(false);

        var alerts = _store.Alerts.Where(a => a.Enabled && a.ItemId > 0 && a.ScopeName.Length > 0).ToList();
        if (alerts.Count == 0)
        {
            return;
        }

        var fired = new List<(MarketAlert Alert, long Price)>();
        foreach (var group in alerts.GroupBy(a => (a.ScopeKind, a.ScopeName)))
        {
            ct.ThrowIfCancellationRequested();
            var scope = new MarketScope(group.Key.ScopeKind, group.Key.ScopeName);
            var ids = group.Select(a => a.ItemId).Distinct().ToArray();
            var agg = await _data.GetAggregatedAsync(scope, ids, ct).ConfigureAwait(false);

            _store.Mutate(list =>
            {
                foreach (var snapshot in group)
                {
                    var alert = list.FirstOrDefault(a => a.Id == snapshot.Id);
                    if (alert is null || !agg.TryGetValue(alert.ItemId, out var result))
                    {
                        continue;
                    }
                    var quality = result.Quality(alert.HqOnly);
                    var price = quality.MinListing?.At(alert.ScopeKind)?.Price ?? 0;
                    if (price <= 0)
                    {
                        continue;
                    }
                    alert.LastSeenPrice = price;

                    var threshold = (double)alert.Threshold;
                    if (alert.IsPercent)
                    {
                        var average = quality.AverageSalePrice?.At(alert.ScopeKind)?.Price ?? 0;
                        if (average <= 0)
                        {
                            continue;
                        }
                        threshold = average * alert.Threshold / 100.0;
                    }

                    var crossed = alert.TriggerAbove ? price >= threshold : price <= threshold;
                    if (crossed && alert.Armed)
                    {
                        alert.Armed = false;
                        alert.Enabled = false;
                        alert.LastTriggeredUtc = DateTimeOffset.UtcNow;
                        alert.Acknowledged = false;
                        fired.Add((alert, price));
                    }
                    else if (!crossed && !alert.Armed)
                    {
                        alert.Armed = true;
                    }
                }
            });
        }

        foreach (var (alert, price) in fired)
        {
            Deliver(alert, price);
        }
    }

    private void Deliver(MarketAlert alert, long price)
    {
        var priceText = $"{MarketFormat.GilFull(price)} gil";
        var text = Loc.T(alert.TriggerAbove ? "notif.market_above" : "notif.market_below", alert.ItemName, priceText);
        _notifier.NotifyMarketAlert(alert.ItemId, text);
        try
        {
            var shell = _services.GetRequiredService<AetherOS.Sdk.IOsShell>();
            var itemId = alert.ItemId;
            shell.PostNotification("market", Loc.T("os.app_market"), text,
                onTap: () => _services.GetService<MainPluginWindow>()?.OpenToMarketItem(itemId),
                tag: $"market:alert:{itemId}");
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[MarketAlerts] OS notification failed.");
        }
    }
}
