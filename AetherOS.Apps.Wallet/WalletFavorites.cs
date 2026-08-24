using System.Collections.Generic;
using System.Linq;
using AetherOS.Sdk;

namespace AetherOS.Apps.Wallet;

/// <summary>The starred currencies, kept PER CHARACTER: a crafter alt pins scrips, the main pins
/// tomestones, and the overview unions them. Shared by the Currencies tab that edits them and the
/// Sparks tab that lists the logged-in character's underneath its cards. Order is the order they
/// were starred. The pre-alt single list migrates onto the first character that asks, which is the
/// one logged in when the update lands.</summary>
internal sealed class WalletFavorites
{
    private const string LegacyKey = "favouriteCurrencies";
    private const string KeyPrefix = "favouriteCurrencies:";

    private readonly IAppStorage _storage;
    private readonly object _gate = new();
    private readonly Dictionary<ulong, List<uint>> _byCharacter = new();

    public WalletFavorites(IAppStorage storage)
    {
        _storage = storage;
    }

    private static string KeyFor(ulong contentId) => $"{KeyPrefix}{contentId:X}";

    public IReadOnlyList<uint> For(ulong contentId)
    {
        lock (_gate)
        {
            if (_byCharacter.TryGetValue(contentId, out var cached))
            {
                return cached;
            }
            var ids = _storage.Get<List<uint>>(KeyFor(contentId));
            if (ids is null && _storage.Get<List<uint>>(LegacyKey) is { } legacy)
            {
                ids = legacy;
                _storage.Set(KeyFor(contentId), legacy);
                // The storage has no delete; an empty list is the retired shape.
                _storage.Set<List<uint>>(LegacyKey, []);
            }
            ids ??= [];
            _byCharacter[contentId] = ids;
            return ids;
        }
    }

    public bool Contains(ulong contentId, uint itemId) => For(contentId).Contains(itemId);

    public void Toggle(ulong contentId, uint itemId)
    {
        lock (_gate)
        {
            var updated = new List<uint>(For(contentId));
            if (!updated.Remove(itemId))
            {
                updated.Add(itemId);
            }
            _byCharacter[contentId] = updated;
            _storage.Set(KeyFor(contentId), updated);
        }
    }

    public void Forget(ulong contentId)
    {
        lock (_gate)
        {
            _byCharacter.Remove(contentId);
            _storage.Set<List<uint>>(KeyFor(contentId), []);
        }
    }

    /// <summary>The starred rows in starred order, skipping gil (it owns the hero card) and any id the
    /// given snapshot no longer carries.</summary>
    public List<WalletCurrencyRow> Pick(ulong contentId, IReadOnlyList<WalletCurrencyRow> rows)
    {
        var ids = For(contentId);
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

    /// <summary>Every currency starred by ANY of the given characters, first-starred first, so the
    /// overview shows what each alt cares about without anybody curating a second list.</summary>
    public List<uint> PinnedAcross(IEnumerable<ulong> contentIds)
    {
        var union = new List<uint>();
        foreach (var contentId in contentIds)
        {
            foreach (var id in For(contentId))
            {
                if (!union.Contains(id))
                {
                    union.Add(id);
                }
            }
        }
        return union;
    }
}
