using System;
using System.Collections.Generic;
using AetherOS.Sdk;

namespace AetherOS.Apps.Wayfinder;

/// <summary>One locally-kept found record; the selfie never leaves this machine.</summary>
public sealed record WayfinderFoundRecord(
    Guid ChallengeId,
    string Name,
    short Expansion,
    DateTime FoundAtUtc,
    int Attempts,
    int Seconds,
    string? SelfiePath);

/// <summary>The app-storage-backed list of places this device's user has found.</summary>
internal sealed class WayfinderHistory
{
    private const string Key = "history";

    private readonly IAppStorage _storage;
    private List<WayfinderFoundRecord>? _records;

    public WayfinderHistory(IAppStorage storage)
    {
        _storage = storage;
    }

    public IReadOnlyList<WayfinderFoundRecord> Records
    {
        get
        {
            _records ??= _storage.Get<List<WayfinderFoundRecord>>(Key) ?? [];
            return _records;
        }
    }

    public void Add(WayfinderFoundRecord record)
    {
        _records ??= _storage.Get<List<WayfinderFoundRecord>>(Key) ?? [];
        _records.RemoveAll(r => r.ChallengeId == record.ChallengeId);
        _records.Insert(0, record);
        _storage.Set(Key, _records);
    }
}
