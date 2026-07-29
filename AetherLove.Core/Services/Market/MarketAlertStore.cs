using System;
using System.Collections.Generic;
using System.Linq;
using AetherOS.Sdk;

namespace AetherLove.Services.Market;

public sealed class MarketAlert
{
    public Guid Id { get; set; }
    public uint ItemId { get; set; }
    public string ItemName { get; set; } = "";
    public MarketScopeKind ScopeKind { get; set; }
    public string ScopeName { get; set; } = "";
    public bool HqOnly { get; set; }
    public long Threshold { get; set; }
    public bool IsPercent { get; set; }
    public bool TriggerAbove { get; set; }
    public bool Enabled { get; set; } = true;
    public bool Armed { get; set; } = true;
    public DateTimeOffset? LastTriggeredUtc { get; set; }
    public bool Acknowledged { get; set; } = true;
    public long LastSeenPrice { get; set; }
}

/// <summary>Persisted price alerts. An alert is one-shot: firing switches it off, and flipping its toggle
/// back on re-arms it for another round.</summary>
public sealed class MarketAlertStore
{
    private const string AlertsKey = "alerts";

    private readonly IAppStorage _storage;
    private readonly object _gate = new();
    private List<MarketAlert> _alerts;

    public MarketAlertStore(IAppStorage storage)
    {
        _storage = storage;
        _alerts = storage.Get<List<MarketAlert>>(AlertsKey) ?? [];
    }

    public IReadOnlyList<MarketAlert> Alerts
    {
        get
        {
            lock (_gate)
            {
                return [.. _alerts];
            }
        }
    }

    public MarketAlert? ForItem(uint itemId)
    {
        lock (_gate)
        {
            return _alerts.FirstOrDefault(a => a.ItemId == itemId);
        }
    }

    public void Upsert(MarketAlert alert)
    {
        lock (_gate)
        {
            _alerts.RemoveAll(a => a.Id == alert.Id);
            _alerts.Add(alert);
            _storage.Set(AlertsKey, _alerts);
        }
    }

    public void Remove(Guid id)
    {
        lock (_gate)
        {
            if (_alerts.RemoveAll(a => a.Id == id) > 0)
            {
                _storage.Set(AlertsKey, _alerts);
            }
        }
    }

    public void SetEnabled(Guid id, bool enabled)
    {
        lock (_gate)
        {
            var alert = _alerts.FirstOrDefault(a => a.Id == id);
            if (alert is null)
            {
                return;
            }
            alert.Enabled = enabled;
            if (enabled)
            {
                alert.Armed = true;
            }
            _storage.Set(AlertsKey, _alerts);
        }
    }

    public int UnacknowledgedCount()
    {
        lock (_gate)
        {
            return _alerts.Count(a => !a.Acknowledged);
        }
    }

    public void AcknowledgeItem(uint itemId)
    {
        lock (_gate)
        {
            var changed = false;
            foreach (var alert in _alerts)
            {
                if (alert.ItemId == itemId && !alert.Acknowledged)
                {
                    alert.Acknowledged = true;
                    changed = true;
                }
            }
            if (changed)
            {
                _storage.Set(AlertsKey, _alerts);
            }
        }
    }

    /// <summary>Runs the poll loop's state mutations under the store lock, then persists once.</summary>
    public void Mutate(Action<List<MarketAlert>> mutator)
    {
        lock (_gate)
        {
            mutator(_alerts);
            _storage.Set(AlertsKey, _alerts);
        }
    }
}
