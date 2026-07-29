using System;
using System.Collections.Generic;
using System.Linq;
using AetherOS.Sdk;

namespace AetherLove.Services.Market;

public sealed class MarketSelection
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public List<uint> ItemIds { get; set; } = [];
}

/// <summary>The user's market state: watchlist, recently viewed items, and named custom item lists.
/// Persisted per key in the app's storage; every mutation writes through immediately.</summary>
public sealed class MarketUserStore
{
    private const int RecentsCap = 20;
    private const string WatchlistKey = "watchlist";
    private const string RecentsKey = "recents";
    private const string SelectionsKey = "selections";

    private readonly IAppStorage _storage;
    private readonly object _gate = new();
    private List<uint> _watchlist;
    private List<uint> _recents;
    private List<MarketSelection> _selections;

    public MarketUserStore(IAppStorage storage)
    {
        _storage = storage;
        _watchlist = storage.Get<List<uint>>(WatchlistKey) ?? [];
        _recents = storage.Get<List<uint>>(RecentsKey) ?? [];
        _selections = storage.Get<List<MarketSelection>>(SelectionsKey) ?? [];
    }

    public IReadOnlyList<uint> Watchlist
    {
        get
        {
            lock (_gate)
            {
                return [.. _watchlist];
            }
        }
    }

    public bool IsWatched(uint itemId)
    {
        lock (_gate)
        {
            return _watchlist.Contains(itemId);
        }
    }

    public void ToggleWatch(uint itemId)
    {
        lock (_gate)
        {
            if (!_watchlist.Remove(itemId))
            {
                _watchlist.Insert(0, itemId);
            }
            _storage.Set(WatchlistKey, _watchlist);
        }
    }

    public IReadOnlyList<uint> Recents
    {
        get
        {
            lock (_gate)
            {
                return [.. _recents];
            }
        }
    }

    public void PushRecent(uint itemId)
    {
        lock (_gate)
        {
            _recents.Remove(itemId);
            _recents.Insert(0, itemId);
            if (_recents.Count > RecentsCap)
            {
                _recents.RemoveRange(RecentsCap, _recents.Count - RecentsCap);
            }
            _storage.Set(RecentsKey, _recents);
        }
    }

    public IReadOnlyList<MarketSelection> Selections
    {
        get
        {
            lock (_gate)
            {
                return [.. _selections];
            }
        }
    }

    public bool TryGetSelection(Guid id, out MarketSelection selection)
    {
        lock (_gate)
        {
            var found = _selections.FirstOrDefault(s => s.Id == id);
            selection = found!;
            return found is not null;
        }
    }

    public MarketSelection CreateSelection(string name)
    {
        lock (_gate)
        {
            var selection = new MarketSelection { Id = Guid.NewGuid(), Name = name.Trim() };
            _selections.Add(selection);
            _storage.Set(SelectionsKey, _selections);
            return selection;
        }
    }

    public void DeleteSelection(Guid id)
    {
        lock (_gate)
        {
            if (_selections.RemoveAll(s => s.Id == id) > 0)
            {
                _storage.Set(SelectionsKey, _selections);
            }
        }
    }

    /// <summary>Adds when absent, removes when present. True when the item is in the list afterwards.</summary>
    public bool ToggleInSelection(Guid selectionId, uint itemId)
    {
        lock (_gate)
        {
            var selection = _selections.FirstOrDefault(s => s.Id == selectionId);
            if (selection is null)
            {
                return false;
            }
            var added = !selection.ItemIds.Remove(itemId);
            if (added)
            {
                selection.ItemIds.Add(itemId);
            }
            _storage.Set(SelectionsKey, _selections);
            return added;
        }
    }

    public void RemoveFromSelection(Guid selectionId, uint itemId)
    {
        lock (_gate)
        {
            var selection = _selections.FirstOrDefault(s => s.Id == selectionId);
            if (selection is not null && selection.ItemIds.Remove(itemId))
            {
                _storage.Set(SelectionsKey, _selections);
            }
        }
    }
}
