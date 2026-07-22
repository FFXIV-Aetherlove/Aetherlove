using System;
using System.Collections.Generic;
using System.Linq;
using AetherLove.Config;
using AetherLove.Services.Localization;
using AetherLove.UI;

namespace AetherLove.Services;

/// <summary>Client-side chat categories backed by the plugin config. Saves run inside the lock: serialization walks the same collections the mutators edit.</summary>
public sealed class ChatCategoryStore
{
    private readonly Configuration _config;
    private readonly object _lock = new();

    public ChatCategoryStore(Configuration config)
    {
        _config = config;
        MigrateLegacyArchive();
    }

    private void MigrateLegacyArchive()
    {
        lock (_lock)
        {
            if (_config.ArchivedMatches.Count == 0)
            {
                return;
            }
            var archive = new ChatCategoryConfig
            {
                Name = Loc.T("chat.category_archive_default"),
                Color = UiColors.CategoryArchiveColor,
            };
            _config.ChatCategories.Add(archive);
            foreach (var peerId in _config.ArchivedMatches)
            {
                _config.ChatCategoryMembers[peerId] = archive.Id;
            }
            _config.ArchivedMatches = [];
            _config.Save();
        }
    }

    /// <summary>Snapshot of the categories in display order.</summary>
    public List<ChatCategoryConfig> GetCategories()
    {
        lock (_lock)
        {
            return _config.ChatCategories.Select(c => new ChatCategoryConfig
            {
                Id = c.Id,
                Name = c.Name,
                Color = c.Color,
            }).ToList();
        }
    }

    public ChatCategoryConfig? Get(Guid categoryId)
    {
        lock (_lock)
        {
            var c = _config.ChatCategories.FirstOrDefault(c => c.Id == categoryId);
            return c is null ? null : new ChatCategoryConfig { Id = c.Id, Name = c.Name, Color = c.Color };
        }
    }

    /// <summary>Snapshot of the peer→category map; mappings to unknown categories mean top-level.</summary>
    public Dictionary<Guid, Guid> GetMembership()
    {
        lock (_lock)
        {
            return new Dictionary<Guid, Guid>(_config.ChatCategoryMembers);
        }
    }

    /// <summary>The category a chat lives in, or null when it is top-level (including dangling mappings).</summary>
    public Guid? CategoryOf(Guid peerId)
    {
        lock (_lock)
        {
            if (_config.ChatCategoryMembers.TryGetValue(peerId, out var catId)
                && _config.ChatCategories.Any(c => c.Id == catId))
            {
                return catId;
            }
            return null;
        }
    }

    /// <summary>Moves a chat into a category, or back to the top level when <paramref name="categoryId"/> is null or no longer exists.</summary>
    public void SetCategory(Guid peerId, Guid? categoryId)
    {
        lock (_lock)
        {
            bool changed;
            if (categoryId is { } cat && _config.ChatCategories.Any(c => c.Id == cat))
            {
                changed = !_config.ChatCategoryMembers.TryGetValue(peerId, out var prev) || prev != cat;
                _config.ChatCategoryMembers[peerId] = cat;
            }
            else
            {
                changed = _config.ChatCategoryMembers.Remove(peerId);
            }
            if (changed)
            {
                _config.Save();
            }
        }
    }

    public ChatCategoryConfig Create(string name, uint color)
    {
        var cat = new ChatCategoryConfig { Name = name, Color = color };
        lock (_lock)
        {
            _config.ChatCategories.Add(cat);
            _config.Save();
        }
        return new ChatCategoryConfig { Id = cat.Id, Name = cat.Name, Color = cat.Color };
    }

    public void Update(Guid categoryId, string name, uint color)
    {
        lock (_lock)
        {
            var c = _config.ChatCategories.FirstOrDefault(c => c.Id == categoryId);
            if (c is null || (c.Name == name && c.Color == color))
            {
                return;
            }
            c.Name = name;
            c.Color = color;
            _config.Save();
        }
    }

    /// <summary>Deletes a category; its chats fall back to the top level.</summary>
    public void Delete(Guid categoryId)
    {
        lock (_lock)
        {
            var changed = _config.ChatCategories.RemoveAll(c => c.Id == categoryId) > 0;
            foreach (var peerId in _config.ChatCategoryMembers
                         .Where(kv => kv.Value == categoryId).Select(kv => kv.Key).ToList())
            {
                _config.ChatCategoryMembers.Remove(peerId);
                changed = true;
            }
            if (changed)
            {
                _config.Save();
            }
        }
    }

    public void Reorder(Guid categoryId, int newIndex)
    {
        lock (_lock)
        {
            var idx = _config.ChatCategories.FindIndex(c => c.Id == categoryId);
            if (idx < 0)
            {
                return;
            }
            var cat = _config.ChatCategories[idx];
            _config.ChatCategories.RemoveAt(idx);
            newIndex = Math.Clamp(newIndex, 0, _config.ChatCategories.Count);
            _config.ChatCategories.Insert(newIndex, cat);
            if (newIndex != idx)
            {
                _config.Save();
            }
        }
    }

    public void RemovePeer(Guid peerId)
    {
        lock (_lock)
        {
            if (_config.ChatCategoryMembers.Remove(peerId))
            {
                _config.Save();
            }
        }
    }

    /// <summary>Call only with a complete, freshly-synced match list; an empty list is ignored so a cold cache can't wipe valid mappings.</summary>
    public void PruneTo(IReadOnlyCollection<Guid> matchedPeers)
    {
        if (matchedPeers.Count == 0)
        {
            return;
        }
        lock (_lock)
        {
            var known = matchedPeers as ISet<Guid> ?? matchedPeers.ToHashSet();
            var changed = false;
            foreach (var peerId in _config.ChatCategoryMembers.Keys.Where(k => !known.Contains(k)).ToList())
            {
                _config.ChatCategoryMembers.Remove(peerId);
                changed = true;
            }
            if (changed)
            {
                _config.Save();
            }
        }
    }
}
