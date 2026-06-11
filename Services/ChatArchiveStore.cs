using System;
using System.Collections.Generic;
using System.Linq;
using AetherLove.Config;

namespace AetherLove.Services;

/// <summary>Client-side, per-install set of archived matches keyed by peer profile id. Backed by the plugin
/// config; thread-safe so the incoming-message handler can auto-unarchive off the UI thread.</summary>
public sealed class ChatArchiveStore
{
    private readonly Configuration _config;
    private readonly object _lock = new();
    private readonly HashSet<Guid> _set;

    public ChatArchiveStore(Configuration config)
    {
        _config = config;
        _set = [.. config.ArchivedMatches];
    }

    public bool IsArchived(Guid peerId)
    {
        lock (_lock)
        {
            return _set.Contains(peerId);
        }
    }

    public void SetArchived(Guid peerId, bool archived)
    {
        bool changed;
        lock (_lock)
        {
            changed = archived ? _set.Add(peerId) : _set.Remove(peerId);
            if (changed)
            {
                _config.ArchivedMatches = [.. _set];
            }
        }
        if (changed)
        {
            _config.Save();
        }
    }
}
