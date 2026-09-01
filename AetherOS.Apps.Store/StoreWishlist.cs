using System;
using System.Collections.Generic;
using System.Linq;
using AetherOS.Sdk;

namespace AetherOS.Apps.Store;

/// <summary>The wishlist: product ids the user wants to keep for later, persisted per install. Purely a
/// client-side list, so it holds no prices, no quantities and no ownership; everything shown beside an
/// entry comes from a live DTO the way the cart's lines do.</summary>
internal sealed class StoreWishlist
{
    private const string ListKey = "wishlist";

    private readonly IAppStorage _storage;
    private readonly List<Guid> _ids = [];
    private bool _loaded;

    public StoreWishlist(IAppStorage storage) => _storage = storage;

    public IReadOnlyList<Guid> Ids
    {
        get
        {
            Load();
            return _ids;
        }
    }

    public int Count
    {
        get
        {
            Load();
            return _ids.Count;
        }
    }

    public bool Contains(Guid productId)
    {
        Load();
        return _ids.Contains(productId);
    }

    public void Add(Guid productId)
    {
        Load();
        if (_ids.Contains(productId))
        {
            return;
        }
        // Newest first, so the last thing saved is the first thing seen.
        _ids.Insert(0, productId);
        Save();
    }

    public void Remove(Guid productId)
    {
        Load();
        if (_ids.Remove(productId))
        {
            Save();
        }
    }

    public void RemoveRange(IEnumerable<Guid> productIds)
    {
        Load();
        var gone = productIds.ToHashSet();
        if (_ids.RemoveAll(gone.Contains) > 0)
        {
            Save();
        }
    }

    /// <summary>Toggles membership and returns true when the product is now on the list.</summary>
    public bool Toggle(Guid productId)
    {
        if (Contains(productId))
        {
            Remove(productId);
            return false;
        }
        Add(productId);
        return true;
    }

    private void Load()
    {
        if (_loaded)
        {
            return;
        }
        _loaded = true;
        foreach (var id in _storage.Get<List<Guid>?>(ListKey) ?? [])
        {
            if (id != Guid.Empty && !_ids.Contains(id))
            {
                _ids.Add(id);
            }
        }
    }

    private void Save() => _storage.Set(ListKey, _ids);
}
