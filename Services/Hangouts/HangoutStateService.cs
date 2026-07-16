using System;
using System.Collections.Generic;
using System.Linq;
using AetherLove.Shared.Hangouts;

namespace AetherLove.Services.Hangouts;

/// <summary>In-memory hangout state; deliberately never persisted, so a stale frame can't survive a restart.</summary>
public sealed class HangoutStateService
{
    private readonly object _lock = new();
    private Guid _myProfileId;
    private HangoutSummaryDto? _mine;
    private readonly Dictionary<Guid, string> _myRsvpers = new();
    private readonly Dictionary<Guid, HangoutSummaryDto> _byMatchOwner = new();
    private readonly HashSet<Guid> _myRsvpIds = new();

    /// <summary>Scopes the state to the signed-in profile; an identity change wipes everything.</summary>
    public void SetOwner(Guid profileId)
    {
        lock (_lock)
        {
            if (_myProfileId == profileId)
            {
                return;
            }
            _myProfileId = profileId;
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

    /// <summary>Who's coming to the user's own hangout, in RSVP order.</summary>
    public IReadOnlyList<(Guid ProfileId, string DisplayName)> MyRsvpers
    {
        get
        {
            lock (_lock)
            {
                return _myRsvpers.Select(kv => (kv.Key, kv.Value)).ToArray();
            }
        }
    }

    public HangoutSummaryDto? ForMatchPeer(Guid peerProfileId)
    {
        lock (_lock)
        {
            return _byMatchOwner.TryGetValue(peerProfileId, out var h) ? h : null;
        }
    }

    public IReadOnlyList<HangoutSummaryDto> MatchHangouts()
    {
        lock (_lock)
        {
            return _byMatchOwner.Values.ToArray();
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
            _byMatchOwner.Clear();
            foreach (var h in sync.MatchHangouts)
            {
                _byMatchOwner[h.OwnerProfileId] = h;
            }
            _myRsvpIds.Clear();
            _myRsvpIds.UnionWith(sync.MyRsvpHangoutIds);
            _myRsvpers.Clear();
            foreach (var r in sync.MyHangoutRsvpers ?? [])
            {
                _myRsvpers[r.ProfileId] = r.DisplayName;
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

    /// <summary>Returns true for a match peer's new hangout; the user's own multi-client echo returns false.</summary>
    public bool ApplyStarted(HangoutSummaryDto hangout)
    {
        lock (_lock)
        {
            if (hangout.OwnerProfileId == _myProfileId)
            {
                _mine = hangout;
                return false;
            }
            var isNew = !_byMatchOwner.TryGetValue(hangout.OwnerProfileId, out var existing)
                || existing.Id != hangout.Id;
            _byMatchOwner[hangout.OwnerProfileId] = hangout;
            return isNew;
        }
    }

    /// <summary>Returns true when the user had an active RSVP on the ended hangout.</summary>
    public bool ApplyEnded(Guid hangoutId, Guid ownerProfileId)
    {
        lock (_lock)
        {
            if (_byMatchOwner.TryGetValue(ownerProfileId, out var existing) && existing.Id == hangoutId)
            {
                _byMatchOwner.Remove(ownerProfileId);
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
                _myRsvpers[push.RsvperProfileId] = push.RsvperDisplayName;
            }
            else
            {
                _myRsvpers.Remove(push.RsvperProfileId);
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
            var entry = _byMatchOwner.Values.FirstOrDefault(h => h.Id == hangoutId);
            if (entry is not null)
            {
                _byMatchOwner[entry.OwnerProfileId] = entry with { RsvpCount = rsvpCount };
            }
        }
    }

    public void RemoveMatchPeer(Guid peerProfileId)
    {
        lock (_lock)
        {
            _byMatchOwner.Remove(peerProfileId);
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
        _byMatchOwner.Clear();
        _myRsvpIds.Clear();
    }
}
