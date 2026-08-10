using System;
using System.Collections.Generic;
using AetherLove.Shared.Yapper;

namespace AetherOS.Apps.Yapper;

/// <summary>The app's entity map: one canonical <see cref="YapDto"/> per id so a like on any surface
/// updates every surface. Feed screens keep id lists and resolve through here. Session unblur choices
/// live here too (never persisted).</summary>
internal sealed class YapperStore
{
    /// <summary>How long a muted or blocked author's cards take to roll up, in seconds.</summary>
    private const double VanishSeconds = 0.30;

    private readonly object _gate = new();
    private readonly Dictionary<Guid, YapDto> _yaps = new();
    private readonly HashSet<Guid> _revealed = [];
    private readonly Dictionary<Guid, double> _vanishing = new();
    private readonly Dictionary<Guid, float> _heights = new();

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

    /// <summary>Drops everything. Used when the profile these yaps were fetched for stops existing.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _yaps.Clear();
            _revealed.Clear();
            _vanishing.Clear();
            _heights.Clear();
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

    /// <summary>Starts rolling every card by this author off whatever surface is showing them. Muting and
    /// blocking only take effect server-side on the next fetch, so without this the feed you are looking at
    /// keeps their yaps until you refresh, which reads as the action having done nothing.</summary>
    public void BeginVanish(Guid authorProfileId, double now)
    {
        lock (_gate)
        {
            _vanishing.TryAdd(authorProfileId, now);
        }
    }

    /// <summary>How far through the roll-up a yap is (0 to 1), or null when its author is staying.</summary>
    public float? VanishProgress(YapDto dto, double now)
    {
        if (dto.Author?.ProfileId is not { } authorId)
        {
            return null;
        }
        lock (_gate)
        {
            if (!_vanishing.TryGetValue(authorId, out var startedAt))
            {
                return null;
            }
            return (float)Math.Clamp((now - startedAt) / VanishSeconds, 0d, 1d);
        }
    }

    /// <summary>The height a card last drew at, so a vanishing one can roll up from where it actually was.
    /// A card mid-vanish is drawing clipped, so its height then is not worth keeping.</summary>
    public void NoteHeight(Guid yapId, Guid? authorProfileId, float height)
    {
        if (height <= 0f)
        {
            return;
        }
        lock (_gate)
        {
            if (authorProfileId is { } authorId && _vanishing.ContainsKey(authorId))
            {
                return;
            }
            _heights[yapId] = height;
        }
    }

    /// <summary>Null for a card that has never been on screen, which is why those skip the animation.</summary>
    public float? HeightOf(Guid yapId)
    {
        lock (_gate)
        {
            return _heights.TryGetValue(yapId, out var h) ? h : null;
        }
    }
}
