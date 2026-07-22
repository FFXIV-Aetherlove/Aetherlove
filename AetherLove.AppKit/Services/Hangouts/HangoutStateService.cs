using System;
using System.Collections.Generic;
using System.Linq;
using AetherLove.Shared.Hangouts;

namespace AetherLove.Services.Hangouts;

/// <summary>In-memory hangout state (never persisted to avoid stale markers after restart); account-level keyed by AetherAccount id.</summary>
public sealed class HangoutStateService
{
    private readonly object _lock = new();
    private Guid _myAccountId;
    private HangoutSummaryDto? _mine;
    private readonly Dictionary<Guid, string> _myRsvpers = new();
    private readonly Dictionary<Guid, HangoutSummaryDto> _byOwnerAccount = new();
    private readonly HashSet<Guid> _myRsvpIds = new();

    /// <summary>Scopes the state to the signed-in account; an identity change wipes everything. Profile
    /// switches within the account keep the state (hangouts are account-level).</summary>
    public void SetOwner(Guid accountId)
    {
        lock (_lock)
        {
            if (_myAccountId == accountId)
            {
                return;
            }
            _myAccountId = accountId;
            ClearCore();
        }
    }

    public HangoutSummaryDto? MyHangout
    {
        get
        {
            lock (_lock)
            {
                return _mine;
            }
        }
    }

    /// <summary>Who's coming to the user's own hangout, in RSVP order (OS identities).</summary>
    public IReadOnlyList<(Guid AccountId, string DisplayName)> MyRsvpers
    {
        get
        {
            lock (_lock)
            {
                return _myRsvpers.Select(kv => (kv.Key, kv.Value)).ToArray();
            }
        }
    }

    /// <summary>The live hangout a messenger contact is hosting, if any.</summary>
    public HangoutSummaryDto? ForContact(Guid accountId)
    {
        lock (_lock)
        {
            return _byOwnerAccount.TryGetValue(accountId, out var h) ? h : null;
        }
    }

    public IReadOnlyList<HangoutSummaryDto> ContactHangouts()
    {
        lock (_lock)
        {
            return _byOwnerAccount.Values.ToArray();
        }
    }

    public bool IsRsvped(Guid hangoutId)
    {
        lock (_lock)
        {
            return _myRsvpIds.Contains(hangoutId);
        }
    }

    /// <summary>Full replace from the on-connect sync fetch.</summary>
    public void ApplySync(HangoutSyncDto sync)
    {
        lock (_lock)
        {
            _mine = sync.MyHangout;
            _byOwnerAccount.Clear();
            foreach (var h in sync.ContactHangouts)
            {
                _byOwnerAccount[h.OwnerAccountId] = h;
            }
            _myRsvpIds.Clear();
            _myRsvpIds.UnionWith(sync.MyRsvpHangoutIds);
            _myRsvpers.Clear();
            foreach (var r in sync.MyHangoutRsvpers ?? [])
            {
                _myRsvpers[r.AccountId] = r.DisplayName;
            }
        }
    }

    /// <summary>Local echo after a successful create.</summary>
    public void SetMyHangout(HangoutSummaryDto? hangout)
    {
        lock (_lock)
        {
            _mine = hangout;
            if (hangout is null)
            {
                _myRsvpers.Clear();
            }
        }
    }

    /// <summary>Returns true for a contact's new hangout; the user's own multi-client echo returns false.</summary>
    public bool ApplyStarted(HangoutSummaryDto hangout)
    {
        lock (_lock)
        {
            if (hangout.OwnerAccountId == _myAccountId)
            {
                _mine = hangout;
                return false;
            }
            var isNew = !_byOwnerAccount.TryGetValue(hangout.OwnerAccountId, out var existing)
                || existing.Id != hangout.Id;
            _byOwnerAccount[hangout.OwnerAccountId] = hangout;
            return isNew;
        }
    }

    /// <summary>Returns true when the user had an active RSVP on the ended hangout.</summary>
    public bool ApplyEnded(Guid hangoutId, Guid ownerAccountId)
    {
        lock (_lock)
        {
            if (_byOwnerAccount.TryGetValue(ownerAccountId, out var existing) && existing.Id == hangoutId)
            {
                _byOwnerAccount.Remove(ownerAccountId);
            }
            if (_mine?.Id == hangoutId)
            {
                _mine = null;
                _myRsvpers.Clear();
            }
            return _myRsvpIds.Remove(hangoutId);
        }
    }

    public void ApplyRsvpChanged(HangoutRsvpChangedPushDto push)
    {
        lock (_lock)
        {
            if (_mine?.Id != push.HangoutId)
            {
                return;
            }
            _mine = _mine with { RsvpCount = push.RsvpCount };
            if (push.Going)
            {
                _myRsvpers[push.RsvperAccountId] = push.RsvperDisplayName;
            }
            else
            {
                _myRsvpers.Remove(push.RsvperAccountId);
            }
        }
    }

    /// <summary>Local echo after the user's own "on my way" toggle.</summary>
    public void SetMyRsvp(Guid hangoutId, bool going, int rsvpCount)
    {
        lock (_lock)
        {
            if (going)
            {
                _myRsvpIds.Add(hangoutId);
            }
            else
            {
                _myRsvpIds.Remove(hangoutId);
            }
            var entry = _byOwnerAccount.Values.FirstOrDefault(h => h.Id == hangoutId);
            if (entry is not null)
            {
                _byOwnerAccount[entry.OwnerAccountId] = entry with { RsvpCount = rsvpCount };
            }
        }
    }

    /// <summary>Drops a contact's hangout marker (contact removed or blocked).</summary>
    public void RemoveContact(Guid accountId)
    {
        lock (_lock)
        {
            _byOwnerAccount.Remove(accountId);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            ClearCore();
        }
    }

    private void ClearCore()
    {
        _mine = null;
        _myRsvpers.Clear();
        _byOwnerAccount.Clear();
        _myRsvpIds.Clear();
    }
}
