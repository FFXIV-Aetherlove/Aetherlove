using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Shared.Store;

namespace AetherOS.Apps.Store;

/// <summary>The app's shared data: storefront, balance, and the browse page list. Async responses are
/// generation-guarded so a stale fetch can never overwrite a newer one, and everything mutates on the
/// draw thread's next read of plain fields (single-writer pattern like the Wallet screens).</summary>
internal sealed class StoreState(IStoreHost host)
{
    private const double StaleSeconds = 60;

    public StoreFrontDto? Front { get; private set; }
    public bool FrontLoading { get; private set; }
    public bool FrontFailed { get; private set; }
    public long? Balance { get; private set; }
    public DateTime LastFrontFetchUtc { get; private set; } = DateTime.MinValue;

    private int _frontGeneration;

    // Browse state, keyed by a filter signature; changing any filter resets the list.
    public List<StoreProductDto> BrowseItems { get; } = [];
    public int BrowseTotal { get; private set; }
    public bool BrowseLoading { get; private set; }
    public bool BrowseEndReached { get; private set; }
    private int _browseGeneration;
    private string _browseSignature = string.Empty;

    public event Action? Changed;

    /// <summary>Product lookup across everything fetched so far (rails + browse), for the bag.</summary>
    public StoreProductDto? Find(Guid productId)
    {
        if (Front is { } front)
        {
            var hit = front.NewItems.Concat(front.MostBought).FirstOrDefault(p => p.Id == productId);
            if (hit is not null)
            {
                return hit;
            }
        }
        return BrowseItems.FirstOrDefault(p => p.Id == productId);
    }

    /// <summary>A live per-product fetch (fresh owned state + exact prices) for the detail view.</summary>
    public Task<StoreProductDto?> FindFreshAsync(Guid productId) => host.GetStoreProductAsync(productId);

    public void RefreshFrontIfStale()
    {
        if ((DateTime.UtcNow - LastFrontFetchUtc).TotalSeconds > StaleSeconds)
        {
            RefreshFront();
        }
    }

    public void MarkFrontStale() => LastFrontFetchUtc = DateTime.MinValue;

    public void RefreshFront()
    {
        var generation = Interlocked.Increment(ref _frontGeneration);
        FrontLoading = true;
        _ = Task.Run(async () =>
        {
            var front = await host.GetStoreFrontAsync().ConfigureAwait(false);
            if (generation != Volatile.Read(ref _frontGeneration))
            {
                return;
            }
            FrontLoading = false;
            if (front is null)
            {
                FrontFailed = Front is null;
            }
            else
            {
                Front = front;
                FrontFailed = false;
                Balance = front.Balance;
                LastFrontFetchUtc = DateTime.UtcNow;
            }
            Changed?.Invoke();
        });
    }

    public void SetBalance(long balance)
    {
        Balance = balance;
        Changed?.Invoke();
    }

    public sealed record BrowseFilter(
        Guid? CategoryId, string? Tag, string? SearchText,
        int? MinPrice, int? MaxPrice, bool OnSaleOnly, StoreSort Sort)
    {
        public string Signature =>
            $"{CategoryId:N}|{Tag}|{SearchText}|{MinPrice}|{MaxPrice}|{OnSaleOnly}|{(short)Sort}";
    }

    private const int BrowsePageSize = 24;

    /// <summary>How many products a shelf card fetches for its face. It only ever draws one; the spares are
    /// there so a shelf whose first product has no art yet still shows something.</summary>
    private const int PreviewSize = 8;

    /// <summary>What a shelf is fetched with when the browse screen is hunting for one named product in it.
    /// A handful of featured products is a lottery the named one usually loses, so it asks for the shelf
    /// whole; the server's own page cap is the ceiling, which is why the preferences are pointed at leaf
    /// shelves rather than at a parent whose subtree can exceed it.</summary>
    private const int PreferredPreviewSize = 60;

    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, List<StoreProductDto>> _previews = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, byte> _previewsInFlight = new();

    /// <summary>A few products from one shelf, for its card's picture. Fetched per shelf rather than taken
    /// from the browse list: that list is paged, so a shelf whose products sit past the first page had
    /// nothing to show and fell back to a bare glyph. One tiny call per shelf, cached for the session.</summary>
    public IReadOnlyList<StoreProductDto> PreviewFor(Guid categoryId, bool wholeShelf = false)
    {
        if (_previews.TryGetValue(categoryId, out var cached))
        {
            return cached;
        }
        if (_previewsInFlight.TryAdd(categoryId, 0))
        {
            var take = wholeShelf ? PreferredPreviewSize : PreviewSize;
            _ = Task.Run(async () =>
            {
                var page = await host.GetStoreProductsAsync(new StoreProductQueryDto(
                    categoryId, null, null, null, null, false, 0, take, StoreSort.Featured))
                    .ConfigureAwait(false);
                _previews[categoryId] = page?.Items.ToList() ?? [];
                Changed?.Invoke();
            });
        }
        return [];
    }

    /// <summary>Starts a fresh browse when the filter changed, else keeps the current list.</summary>
    public void Browse(BrowseFilter filter)
    {
        if (filter.Signature == _browseSignature)
        {
            return;
        }
        _browseSignature = filter.Signature;
        BrowseItems.Clear();
        BrowseTotal = 0;
        BrowseEndReached = false;
        // Abandon anything in flight. Its continuation drops out on the new generation without clearing the
        // loading flag, and leaving that set would make the fetch below refuse to start and spin for good.
        Interlocked.Increment(ref _browseGeneration);
        BrowseLoading = false;
        LoadMore(filter);
    }

    public void LoadMore(BrowseFilter filter)
    {
        if (BrowseLoading || BrowseEndReached || filter.Signature != _browseSignature)
        {
            return;
        }
        BrowseLoading = true;
        var generation = Interlocked.Increment(ref _browseGeneration);
        var skip = BrowseItems.Count;
        _ = Task.Run(async () =>
        {
            var page = await host.GetStoreProductsAsync(new StoreProductQueryDto(
                filter.CategoryId, filter.Tag, filter.SearchText, filter.MinPrice, filter.MaxPrice,
                filter.OnSaleOnly, skip, BrowsePageSize, filter.Sort)).ConfigureAwait(false);
            if (generation != Volatile.Read(ref _browseGeneration) || filter.Signature != _browseSignature)
            {
                return;
            }
            BrowseLoading = false;
            if (page is null)
            {
                BrowseEndReached = BrowseItems.Count > 0;
            }
            else
            {
                BrowseItems.AddRange(page.Items);
                BrowseTotal = page.TotalCount;
                BrowseEndReached = BrowseItems.Count >= page.TotalCount || page.Items.Length == 0;
            }
            Changed?.Invoke();
        });
    }
}
