using System.Collections.Generic;
using AetherOS.Sdk;

namespace AetherOS.Apps.Wallet;

/// <summary>The starred currencies, shared by the Currencies tab that edits them and the Sparks tab that
/// lists them underneath its cards. Order is the order they were starred.</summary>
internal sealed class WalletFavorites
{
    private const string StorageKey = "favouriteCurrencies";

    private readonly IAppStorage _storage;
    private volatile List<uint> _ids;

    public WalletFavorites(IAppStorage storage)
    {
        _storage = storage;
        _ids = storage.Get<List<uint>>(StorageKey) ?? [];
    }

    public bool Contains(uint itemId) => _ids.Contains(itemId);

    public void Toggle(uint itemId)
    {
        var updated = new List<uint>(_ids);
        if (!updated.Remove(itemId))
        {
            updated.Add(itemId);
        }
        _ids = updated;
        _storage.Set(StorageKey, updated);
    }

    /// <summary>The starred rows in starred order, skipping gil (it owns the hero card) and any id the
    /// current snapshot no longer carries.</summary>
    public List<WalletCurrencyRow> Pick(IReadOnlyList<WalletCurrencyRow> rows)
    {
        var ids = _ids;
        var picked = new List<WalletCurrencyRow>(ids.Count);
        foreach (var itemId in ids)
        {
            foreach (var row in rows)
            {
                if (row.ItemId == itemId && !row.IsPrimary)
                {
                    picked.Add(row);
                    break;
                }
            }
        }
        return picked;
    }
}
