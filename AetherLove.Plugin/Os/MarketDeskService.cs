using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Services.Market;
using AetherOS.Apps.Market;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Network.Structures;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;

namespace AetherLove.Os;

/// <summary>Captures the player's own retainers for the Market app. The roster (names, gil, listing
/// counts, expiry) is read from RetainerManager whenever the summoning bell list opens; a retainer's 20
/// market slots and their prices are read while that retainer is summoned. Snapshots persist per character,
/// sales are inferred by diffing snapshots against the retainer's gil delta, and undercuts are checked
/// against cached Universalis minimums plus prices observed while the player browses any market board.
/// Deliberately hook-free and strictly read-only; every game read is try/caught so a game patch degrades to
/// "scan unavailable" instead of breaking the app.</summary>
public sealed class MarketDeskService : IMarketDesk, IDisposable
{
    private const int MarketSlots = 20;
    private const int SalesCap = 50;

    private sealed class DeskFile
    {
        public List<MarketRetainerSnapshot> Retainers { get; set; } = [];
        public List<MarketInferredSale> Sales { get; set; } = [];
    }

    private readonly AetherOS.Sdk.IAppStorage _storage;
    private readonly MarketDataService _data;
    private readonly MarketItemIndex _index;
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<uint, (long Min, long SeenAt)> _observedMins = new();
    private DeskFile _file = new();
    private ulong _loadedContentId;
    private volatile bool _captureSeen;
    private Dictionary<uint, long> _undercuts = [];
    private int _refreshing;

    public MarketDeskService(AetherOS.Sdk.IAppCapabilities caps, MarketDataService data, MarketItemIndex index)
    {
        _storage = caps.Storage("market");
        _data = data;
        _index = index;
        try
        {
            Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "RetainerList", OnRetainerAddon);
            Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "SelectString", OnRetainerAddon);
            Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "RetainerSellList", OnRetainerAddon);
            Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostRefresh, "RetainerSellList", OnRetainerAddon);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[MarketDesk] Addon listener registration failed.");
        }
        try
        {
            Plugin.MarketBoard.OfferingsReceived += OnOfferings;
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[MarketDesk] Market board subscription failed.");
        }
    }

    public void Dispose()
    {
        try
        {
            Plugin.AddonLifecycle.UnregisterListener(OnRetainerAddon);
        }
        catch
        {
            // Listener may already be gone on shutdown.
        }
        try
        {
            Plugin.MarketBoard.OfferingsReceived -= OnOfferings;
        }
        catch
        {
            // Same.
        }
    }

    public bool CaptureReady => _captureSeen || Snapshots.Count > 0;

    public IReadOnlyList<MarketRetainerSnapshot> Snapshots
    {
        get
        {
            lock (_gate)
            {
                EnsureLoadedLocked();
                return [.. _file.Retainers.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)];
            }
        }
    }

    public IReadOnlyList<MarketInferredSale> RecentSales
    {
        get
        {
            lock (_gate)
            {
                EnsureLoadedLocked();
                return [.. _file.Sales];
            }
        }
    }

    public int UndercutCount
    {
        get
        {
            lock (_gate)
            {
                return _undercuts.Count;
            }
        }
    }

    public bool TryGetUndercut(uint itemId, out long marketMin)
    {
        lock (_gate)
        {
            return _undercuts.TryGetValue(itemId, out marketMin);
        }
    }

    public event Action? Changed;


    private static unsafe ulong CurrentContentId()
    {
        try
        {
            var playerState = FFXIVClientStructs.FFXIV.Client.Game.UI.PlayerState.Instance();
            return playerState != null ? playerState->ContentId : 0;
        }
        catch
        {
            return 0;
        }
    }

    private string StorageKey(ulong contentId) => $"retainers:{contentId:X}";

    private void EnsureLoadedLocked()
    {
        var contentId = CurrentContentId();
        if (contentId == 0 || contentId == _loadedContentId)
        {
            return;
        }
        _file = _storage.Get<DeskFile>(StorageKey(contentId)) ?? new DeskFile();
        _loadedContentId = contentId;
        _undercuts = [];
    }

    private void PersistLocked()
    {
        if (_loadedContentId != 0)
        {
            _storage.Set(StorageKey(_loadedContentId), _file);
        }
    }

    private void OnRetainerAddon(AddonEvent type, AddonArgs args)
    {
        try
        {
            CaptureRoster();
            CaptureActiveRetainer();
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[MarketDesk] Capture failed.");
        }
    }

    private unsafe void CaptureRoster()
    {
        if (CurrentContentId() == 0)
        {
            return;
        }
        var manager = RetainerManager.Instance();
        if (manager == null || !manager->IsReady)
        {
            return;
        }

        var changed = false;
        lock (_gate)
        {
            EnsureLoadedLocked();
            for (var i = 0u; i < manager->GetRetainerCount(); i++)
            {
                var retainer = manager->GetRetainerBySortedIndex(i);
                if (retainer == null || retainer->RetainerId == 0 || !retainer->Available)
                {
                    continue;
                }
                var name = retainer->NameString;
                if (name.Length == 0)
                {
                    continue;
                }
                var snapshot = _file.Retainers.FirstOrDefault(r => r.RetainerId == retainer->RetainerId);
                if (snapshot is null)
                {
                    snapshot = new MarketRetainerSnapshot { RetainerId = retainer->RetainerId };
                    _file.Retainers.Add(snapshot);
                }
                snapshot.Name = name;
                snapshot.Gil = retainer->Gil;
                snapshot.MarketItemCount = retainer->MarketItemCount;
                snapshot.MarketExpireUnix = retainer->MarketExpire;
                changed = true;
            }
            if (changed)
            {
                PersistLocked();
            }
        }
        _captureSeen = true;
        if (changed)
        {
            Changed?.Invoke();
        }
    }

    private unsafe void CaptureActiveRetainer()
    {
        if (CurrentContentId() == 0)
        {
            return;
        }
        _index.EnsureBuildStarted();
        var orderModule = ItemOrderModule.Instance();
        if (orderModule == null || orderModule->ActiveRetainerId == 0)
        {
            return;
        }
        var retainerId = orderModule->ActiveRetainerId;
        var inventory = InventoryManager.Instance();
        if (inventory == null)
        {
            return;
        }
        var container = inventory->GetInventoryContainer(InventoryType.RetainerMarket);
        if (container == null || !container->IsLoaded)
        {
            return;
        }

        var listings = new List<MarketRetainerListing>(MarketSlots);
        for (var slot = 0; slot < MarketSlots; slot++)
        {
            var item = container->GetInventorySlot(slot);
            if (item == null || item->ItemId == 0 || item->Quantity <= 0)
            {
                continue;
            }
            var price = (long)inventory->GetRetainerMarketPrice((short)slot);
            if (price <= 0)
            {
                continue;
            }
            _index.TryGet(item->ItemId, out var entry);
            listings.Add(new MarketRetainerListing(item->ItemId,
                entry.Name.Length > 0 ? entry.Name : $"#{item->ItemId}",
                item->Quantity,
                item->Flags.HasFlag(InventoryItem.ItemFlags.HighQuality),
                price));
        }

        long gil = 0;
        var manager = RetainerManager.Instance();
        if (manager != null && manager->IsReady)
        {
            for (var i = 0u; i < manager->GetRetainerCount(); i++)
            {
                var retainer = manager->GetRetainerBySortedIndex(i);
                if (retainer != null && retainer->RetainerId == retainerId)
                {
                    gil = retainer->Gil;
                    break;
                }
            }
        }

        lock (_gate)
        {
            EnsureLoadedLocked();
            var snapshot = _file.Retainers.FirstOrDefault(r => r.RetainerId == retainerId);
            if (snapshot is null)
            {
                snapshot = new MarketRetainerSnapshot { RetainerId = retainerId };
                _file.Retainers.Add(snapshot);
            }
            InferSalesLocked(snapshot, listings, gil);
            snapshot.Listings = listings;
            snapshot.MarketItemCount = listings.Count;
            if (gil > 0)
            {
                snapshot.Gil = gil;
            }
            snapshot.LastScanned = DateTimeOffset.UtcNow;
            PersistLocked();
        }
        _captureSeen = true;
        Changed?.Invoke();
        _ = RefreshUndercutsAsync();
    }

    /// <summary>A slot that emptied since the last scan counts as sold only when the retainer's gil grew
    /// enough to cover it after tax; withdrawn items fail that check and are dropped silently.</summary>
    private void InferSalesLocked(MarketRetainerSnapshot snapshot, List<MarketRetainerListing> fresh, long gilNow)
    {
        if (snapshot.LastScanned is null || gilNow <= 0 || snapshot.Gil <= 0)
        {
            return;
        }
        var gilDelta = gilNow - snapshot.Gil;
        if (gilDelta <= 0)
        {
            return;
        }

        var remaining = new List<MarketRetainerListing>(fresh);
        foreach (var old in snapshot.Listings)
        {
            var match = remaining.FirstOrDefault(l =>
                l.ItemId == old.ItemId && l.UnitPrice == old.UnitPrice && l.Quantity == old.Quantity && l.Hq == old.Hq);
            if (match is not null)
            {
                remaining.Remove(match);
                continue;
            }
            var expectedNet = (long)(old.UnitPrice * (double)old.Quantity * 0.95);
            if (expectedNet <= 0 || gilDelta < (long)(expectedNet * 0.9))
            {
                continue;
            }
            gilDelta -= expectedNet;
            _file.Sales.Insert(0, new MarketInferredSale(old.ItemId, old.ItemName, old.Quantity, old.UnitPrice,
                DateTimeOffset.UtcNow));
        }
        if (_file.Sales.Count > SalesCap)
        {
            _file.Sales.RemoveRange(SalesCap, _file.Sales.Count - SalesCap);
        }
    }

    private void OnOfferings(IMarketBoardCurrentOfferings offerings)
    {
        try
        {
            var now = Environment.TickCount64;
            foreach (var listing in offerings.ItemListings)
            {
                if (listing.ItemId == 0 || listing.PricePerUnit == 0)
                {
                    continue;
                }
                _observedMins.AddOrUpdate(listing.ItemId, (_) => ((long)listing.PricePerUnit, now),
                    (_, prev) => now - prev.SeenAt > 60_000 || (long)listing.PricePerUnit < prev.Min
                        ? ((long)listing.PricePerUnit, now)
                        : prev);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug($"[MarketDesk] Offerings capture failed: {ex.Message}");
        }
    }

    public Task RefreshUndercutsAsync() => RefreshUndercutsCoreAsync();

    /// <summary>Returns the item names that flipped to undercut in this pass, for the alert loop's notice.</summary>
    public async Task<IReadOnlyList<string>> RefreshUndercutsCoreAsync()
    {
        if (Interlocked.Exchange(ref _refreshing, 1) == 1)
        {
            return [];
        }
        try
        {
            List<MarketRetainerListing> own;
            Dictionary<uint, long> previous;
            lock (_gate)
            {
                EnsureLoadedLocked();
                own = _file.Retainers.SelectMany(r => r.Listings).ToList();
                previous = _undercuts;
            }
            if (own.Count == 0)
            {
                return [];
            }
            var scopes = await Plugin.Framework.RunOnFrameworkThread(MarketScopes.DetectCurrent).ConfigureAwait(false);
            if (scopes is null)
            {
                return [];
            }

            var ownMins = own
                .GroupBy(l => l.ItemId)
                .ToDictionary(g => g.Key, g => g.Min(l => l.UnitPrice));
            var agg = await _data.GetAggregatedAsync(scopes.Value.DataCenter, ownMins.Keys.ToArray(), CancellationToken.None)
                .ConfigureAwait(false);

            var undercuts = new Dictionary<uint, long>();
            foreach (var (itemId, ownPrice) in ownMins)
            {
                long marketMin = 0;
                if (agg.TryGetValue(itemId, out var result))
                {
                    var nq = result.Nq.MinListing?.At(MarketScopeKind.DataCenter)?.Price ?? 0;
                    var hq = result.Hq.MinListing?.At(MarketScopeKind.DataCenter)?.Price ?? 0;
                    marketMin = nq > 0 && hq > 0 ? Math.Min(nq, hq) : Math.Max(nq, hq);
                }
                if (_observedMins.TryGetValue(itemId, out var observed) && observed.Min > 0
                    && (marketMin == 0 || observed.Min < marketMin))
                {
                    marketMin = observed.Min;
                }
                if (marketMin > 0 && marketMin < ownPrice)
                {
                    undercuts[itemId] = marketMin;
                }
            }

            List<string> newly = [];
            lock (_gate)
            {
                foreach (var itemId in undercuts.Keys)
                {
                    if (!previous.ContainsKey(itemId))
                    {
                        newly.Add(own.First(l => l.ItemId == itemId).ItemName);
                    }
                }
                _undercuts = undercuts;
            }
            Changed?.Invoke();
            return newly;
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug($"[MarketDesk] Undercut refresh failed: {ex.Message}");
            return [];
        }
        finally
        {
            Interlocked.Exchange(ref _refreshing, 0);
        }
    }
}
