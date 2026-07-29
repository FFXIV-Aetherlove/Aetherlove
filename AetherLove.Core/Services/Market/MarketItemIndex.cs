using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AetherOS.Sdk;

namespace AetherLove.Services.Market;

/// <summary>The searchable index of every marketable item: id, name, icon, and rarity, built once in the
/// background from the Lumina Item sheet filtered against Universalis' marketable-id list. The id list is
/// disk-cached for a day (it only changes on patch days) and a stale copy is used when offline; with no
/// copy at all the index falls back to the sheet's own search-category flag.</summary>
public sealed class MarketItemIndex
{
    public readonly record struct Entry(uint Id, string Name, string NameLower, ushort Icon, byte Rarity);

    private static readonly TimeSpan MarketableTtl = TimeSpan.FromHours(24);

    private readonly UniversalisClient _client;
    private readonly IAppStorage _storage;
    private readonly object _buildGate = new();
    private volatile Entry[]? _entries;
    private Dictionary<uint, int> _byId = [];
    private uint[] _sortedIds = [];
    private volatile bool _building;

    public MarketItemIndex(UniversalisClient client, IAppStorage storage)
    {
        _client = client;
        _storage = storage;
    }

    public bool Ready => _entries is not null;

    public void EnsureBuildStarted()
    {
        lock (_buildGate)
        {
            if (_building || _entries is not null)
            {
                return;
            }
            _building = true;
        }
        _ = Task.Run(BuildAsync);
    }

    private sealed class MarketableFile
    {
        public DateTimeOffset FetchedUtc { get; set; }
        public int[] Ids { get; set; } = [];
    }

    private async Task BuildAsync()
    {
        try
        {
            var marketable = await LoadMarketableIdsAsync().ConfigureAwait(false);
            var set = marketable is null ? null : new HashSet<uint>(Array.ConvertAll(marketable, i => (uint)i));

            var sheet = UiHost.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>();
            var entries = new List<Entry>(set?.Count ?? 30000);
            foreach (var row in sheet)
            {
                if (set is not null ? !set.Contains(row.RowId) : row.ItemSearchCategory.RowId == 0)
                {
                    continue;
                }
                var name = row.Name.ExtractText();
                if (name.Length == 0)
                {
                    continue;
                }
                entries.Add(new Entry(row.RowId, name, name.ToLowerInvariant(), row.Icon, row.Rarity));
            }

            var array = entries.ToArray();
            var byId = new Dictionary<uint, int>(array.Length);
            var ids = new uint[array.Length];
            for (var i = 0; i < array.Length; i++)
            {
                byId[array[i].Id] = i;
                ids[i] = array[i].Id;
            }
            Array.Sort(ids);
            _byId = byId;
            _sortedIds = ids;
            _entries = array;
            UiHost.Log.Debug($"[MarketItemIndex] Built with {array.Length} marketable items.");
        }
        catch (Exception ex)
        {
            UiHost.Log.Warning(ex, "[MarketItemIndex] Build failed.");
        }
        finally
        {
            _building = false;
        }
    }

    private async Task<int[]?> LoadMarketableIdsAsync()
    {
        var path = Path.Combine(_storage.Directory, "marketable.json");
        MarketableFile? cached = null;
        try
        {
            if (File.Exists(path))
            {
                cached = JsonSerializer.Deserialize<MarketableFile>(await File.ReadAllTextAsync(path).ConfigureAwait(false));
            }
        }
        catch (Exception ex)
        {
            UiHost.Log.Debug($"[MarketItemIndex] Marketable cache unreadable: {ex.Message}");
        }

        if (cached is { Ids.Length: > 0 } && DateTimeOffset.UtcNow - cached.FetchedUtc < MarketableTtl)
        {
            return cached.Ids;
        }

        var fresh = await _client.GetMarketableAsync(CancellationToken.None).ConfigureAwait(false);
        if (fresh is { Length: > 0 })
        {
            try
            {
                var file = new MarketableFile { FetchedUtc = DateTimeOffset.UtcNow, Ids = fresh };
                await File.WriteAllTextAsync(path, JsonSerializer.Serialize(file)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                UiHost.Log.Debug($"[MarketItemIndex] Marketable cache write failed: {ex.Message}");
            }
            return fresh;
        }
        return cached?.Ids is { Length: > 0 } ? cached.Ids : null;
    }

    /// <summary>Prefix matches ranked before contains matches, capped at <paramref name="max"/>.</summary>
    public IReadOnlyList<Entry> Search(string query, int max = 50)
    {
        var entries = _entries;
        var q = query.Trim().ToLowerInvariant();
        if (entries is null || q.Length == 0)
        {
            return [];
        }

        var results = new List<Entry>(max);
        foreach (var entry in entries)
        {
            if (entry.NameLower.StartsWith(q, StringComparison.Ordinal))
            {
                results.Add(entry);
                if (results.Count >= max)
                {
                    return results;
                }
            }
        }
        foreach (var entry in entries)
        {
            if (!entry.NameLower.StartsWith(q, StringComparison.Ordinal) &&
                entry.NameLower.Contains(q, StringComparison.Ordinal))
            {
                results.Add(entry);
                if (results.Count >= max)
                {
                    break;
                }
            }
        }
        return results;
    }

    public bool TryGet(uint id, out Entry entry)
    {
        var entries = _entries;
        if (entries is not null && _byId.TryGetValue(id, out var index))
        {
            entry = entries[index];
            return true;
        }
        entry = new Entry(id, string.Empty, string.Empty, 0, 1);
        return false;
    }

    /// <summary>The highest marketable item ids, newest patch additions first.</summary>
    public IReadOnlyList<uint> HighestIds(int count)
    {
        var ids = _sortedIds;
        var result = new List<uint>(count);
        for (var i = ids.Length - 1; i >= 0 && result.Count < count; i--)
        {
            result.Add(ids[i]);
        }
        return result;
    }
}
