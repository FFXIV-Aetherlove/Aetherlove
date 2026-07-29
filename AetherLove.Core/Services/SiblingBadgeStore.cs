using System;
using System.Collections.Generic;

namespace AetherLove.Services;

/// <summary>Live badge counts for the account's profiles, fed by the account-group SiblingBadges push and
/// seeded from ListProfiles at bootstrap. The ACTIVE profile's counts live on <see cref="NotificationCenter"/>
/// (kept by its own pushes); consumers add this store's totals for the inactive siblings to get account-wide
/// numbers (the app tile badge, the DTR entries).</summary>
public sealed class SiblingBadgeStore
{
    private readonly object _lock = new();
    private readonly Dictionary<Guid, (int NewMatches, int UnreadChats)> _counts = new();
    private readonly Dictionary<Guid, string> _names = new();

    public event Action? Changed;

    public void Apply(Guid profileId, int newMatches, int unreadChats)
    {
        lock (_lock)
        {
            _counts[profileId] = (newMatches < 0 ? 0 : newMatches, unreadChats < 0 ? 0 : unreadChats);
        }
        Changed?.Invoke();
    }

    /// <summary>Applies a delta from a live account push (a match/message/unmatch for one of the account's
    /// profiles). Counts floor at zero.</summary>
    public void Bump(Guid profileId, int deltaMatches, int deltaUnread)
    {
        lock (_lock)
        {
            var (m, u) = _counts.TryGetValue(profileId, out var cur) ? cur : (0, 0);
            m = Math.Max(0, m + deltaMatches);
            u = Math.Max(0, u + deltaUnread);
            _counts[profileId] = (m, u);
        }
        Changed?.Invoke();
    }

    /// <summary>The account profile's display name (for a per-profile notification title), or null if unknown.</summary>
    public string? NameFor(Guid profileId)
    {
        lock (_lock)
        {
            return _names.TryGetValue(profileId, out var n) ? n : null;
        }
    }

    /// <summary>Full re-seed from a ListProfiles fetch (bootstrap or picker refresh): counts and display names.</summary>
    public void ReplaceAll(IEnumerable<(Guid ProfileId, string Name, int NewMatches, int UnreadChats)> profiles)
    {
        lock (_lock)
        {
            _counts.Clear();
            _names.Clear();
            foreach (var (id, name, m, u) in profiles)
            {
                _counts[id] = (m, u);
                _names[id] = name;
            }
        }
        Changed?.Invoke();
    }

    /// <summary>Summed counts of every profile except the active one.</summary>
    public (int NewMatches, int UnreadChats) TotalsExcluding(Guid activeProfileId)
    {
        lock (_lock)
        {
            var m = 0;
            var u = 0;
            foreach (var pair in _counts)
            {
                if (pair.Key == activeProfileId)
                {
                    continue;
                }
                m += pair.Value.NewMatches;
                u += pair.Value.UnreadChats;
            }
            return (m, u);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _counts.Clear();
        }
        Changed?.Invoke();
    }
}
