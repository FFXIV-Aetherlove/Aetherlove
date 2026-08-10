using System;
using System.Collections.Generic;
using System.Linq;
using AetherOS.Sdk;

namespace AetherOS.Apps.Store;

/// <summary>The shopping bag: product ids and quantities, persisted per install so it survives closing
/// the phone. Prices are never persisted; the server reprices at checkout regardless, and every displayed
/// number comes from live DTOs.</summary>
internal sealed class StoreBag
{
    private const string BagKey = "bag";
    private const string SavedAtKey = "bagSavedAtUtc";
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);

    public sealed record Line(Guid ProductId, int Quantity);

    private readonly IAppStorage _storage;
    private readonly List<Line> _lines = [];
    private bool _loaded;

    public StoreBag(IAppStorage storage) => _storage = storage;

    public IReadOnlyList<Line> Lines
    {
        get
        {
            Load();
            return _lines;
        }
    }

    public int Count
    {
        get
        {
            Load();
            return _lines.Sum(l => l.Quantity);
        }
    }

    public int QuantityOf(Guid productId)
    {
        Load();
        return _lines.FirstOrDefault(l => l.ProductId == productId)?.Quantity ?? 0;
    }

    public void Add(Guid productId, int quantity)
    {
        Load();
        var index = _lines.FindIndex(l => l.ProductId == productId);
        if (index >= 0)
        {
            _lines[index] = _lines[index] with { Quantity = _lines[index].Quantity + quantity };
        }
        else
        {
            _lines.Add(new Line(productId, quantity));
        }
        Save();
    }

    public void SetQuantity(Guid productId, int quantity)
    {
        Load();
        var index = _lines.FindIndex(l => l.ProductId == productId);
        if (quantity <= 0)
        {
            if (index >= 0)
            {
                _lines.RemoveAt(index);
            }
        }
        else if (index >= 0)
        {
            _lines[index] = _lines[index] with { Quantity = quantity };
        }
        else
        {
            _lines.Add(new Line(productId, quantity));
        }
        Save();
    }

    public void Remove(Guid productId) => SetQuantity(productId, 0);

    public void RemoveRange(IEnumerable<Guid> productIds)
    {
        Load();
        var gone = productIds.ToHashSet();
        _lines.RemoveAll(l => gone.Contains(l.ProductId));
        Save();
    }

    public void Clear()
    {
        Load();
        _lines.Clear();
        Save();
    }

    private void Load()
    {
        if (_loaded)
        {
            return;
        }
        _loaded = true;
        var savedAt = _storage.Get<DateTimeOffset?>(SavedAtKey);
        if (savedAt is { } stamp && DateTimeOffset.UtcNow - stamp > MaxAge)
        {
            Save();
            return;
        }
        foreach (var line in _storage.Get<List<Line>?>(BagKey) ?? [])
        {
            if (line.Quantity > 0)
            {
                _lines.Add(line);
            }
        }
    }

    private void Save()
    {
        _storage.Set(BagKey, _lines);
        _storage.Set(SavedAtKey, (DateTimeOffset?)DateTimeOffset.UtcNow);
    }
}
