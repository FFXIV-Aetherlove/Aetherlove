using System;
using System.Collections.Generic;
using AetherLove.Shared.Yapper;

namespace AetherOS.Apps.Yapper;

/// <summary>The app's entity map: one canonical <see cref="YapDto"/> per id so a like on any surface
/// updates every surface. Feed screens keep id lists and resolve through here. Session unblur choices
/// live here too (never persisted).</summary>
internal sealed class YapperStore
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, YapDto> _yaps = new();
    private readonly HashSet<Guid> _revealed = [];

    /// <summary>Mirrors the viewer's always-blur-NSFW setting and identity each frame, so cards and
    /// mosaics can blur without threading extra accessors everywhere.</summary>
    public bool ViewerBlursNsfw { get; set; }

    public Guid? ViewerProfileId { get; set; }

    public YapDto? Get(Guid id)
    {
        lock (_gate)
        {
            return _yaps.GetValueOrDefault(id);
        }
    }

    /// <summary>Stores the yap and its one-level repost target; returns the canonical instance.</summary>
    public YapDto Upsert(YapDto dto)
    {
        lock (_gate)
        {
            if (dto.RepostOf is { } nested)
            {
                _yaps[nested.Id] = nested;
            }
            if (dto.InReplyTo is { } parent)
            {
                _yaps[parent.Id] = parent;
            }
            _yaps[dto.Id] = dto;
            return dto;
        }
    }

    public void Update(Guid id, Func<YapDto, YapDto> mutate)
    {
        lock (_gate)
        {
            if (_yaps.TryGetValue(id, out var dto))
            {
                _yaps[id] = mutate(dto);
            }
        }
    }

    public void Remove(Guid id)
    {
        lock (_gate)
        {
            _yaps.Remove(id);
        }
    }

    public bool IsRevealed(Guid id)
    {
        lock (_gate)
        {
            return _revealed.Contains(id);
        }
    }

    public void Reveal(Guid id)
    {
        lock (_gate)
        {
            _revealed.Add(id);
        }
    }
}
