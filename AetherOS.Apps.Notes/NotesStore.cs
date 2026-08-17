using System;
using System.Collections.Generic;
using System.Linq;
using AetherOS.Sdk;

namespace AetherOS.Apps.Notes;

/// <summary>The note library: one JSON value in the app's own storage, written through a short debounce so a
/// burst of keystrokes costs one file write.</summary>
internal sealed class NotesStore
{
    private const string Key = "notes";
    private const double DebounceSeconds = 0.8;

    private readonly IAppStorage _storage;
    private readonly List<Note> _notes = [];
    private DateTime _dirtyAtUtc = DateTime.MinValue;
    private bool _dirty;

    internal NotesStore(IAppStorage storage)
    {
        _storage = storage;
        Reload();
    }

    internal IReadOnlyList<Note> All => _notes;

    internal void Reload()
    {
        if (_dirty)
        {
            Flush();
        }
        _notes.Clear();
        var stored = _storage.Get<List<Note>>(Key);
        if (stored is null)
        {
            return;
        }
        foreach (var note in stored)
        {
            if (note is not null)
            {
                _notes.Add(note);
            }
        }
    }

    internal Note Create()
    {
        var now = DateTime.UtcNow;
        var note = new Note { CreatedUtc = now, UpdatedUtc = now };
        _notes.Add(note);
        MarkDirty();
        return note;
    }

    internal Note Duplicate(Note source)
    {
        var now = DateTime.UtcNow;
        var copy = new Note
        {
            Title = source.Title,
            Body = source.Body,
            ColorIndex = source.ColorIndex,
            CreatedUtc = now,
            UpdatedUtc = now,
        };
        _notes.Add(copy);
        MarkDirty();
        return copy;
    }

    internal void Delete(Note note)
    {
        _notes.RemoveAll(n => n.Id == note.Id);
        MarkDirty();
    }

    /// <summary>Drops a note that was opened, never typed in and then left; an empty shell in the list would
    /// look like a bug rather than a draft.</summary>
    internal void DiscardIfBlank(Note note)
    {
        if (note.Title.Length == 0 && note.Body.Trim().Length == 0)
        {
            Delete(note);
        }
    }

    internal void Touch(Note note)
    {
        note.UpdatedUtc = DateTime.UtcNow;
        MarkDirty();
    }

    internal void MarkDirty()
    {
        _dirty = true;
        _dirtyAtUtc = DateTime.UtcNow;
    }

    /// <summary>Call once per frame; writes only after the debounce window has passed.</summary>
    internal void Tick()
    {
        if (!_dirty)
        {
            return;
        }
        if ((DateTime.UtcNow - _dirtyAtUtc).TotalSeconds < DebounceSeconds)
        {
            return;
        }
        Flush();
    }

    internal void Flush()
    {
        if (!_dirty)
        {
            return;
        }
        _dirty = false;
        _storage.Set(Key, _notes);
    }

    /// <summary>Pinned first, then everything else, each block newest-first, filtered by a case-insensitive
    /// match on the title or the body.</summary>
    internal List<Note> Search(string query)
    {
        IEnumerable<Note> source = _notes;
        var trimmed = query.Trim();
        if (trimmed.Length > 0)
        {
            source = source.Where(n =>
                n.Title.Contains(trimmed, StringComparison.OrdinalIgnoreCase)
                || n.Body.Contains(trimmed, StringComparison.OrdinalIgnoreCase));
        }
        return source
            .OrderByDescending(n => n.Pinned)
            .ThenByDescending(n => n.UpdatedUtc)
            .ToList();
    }
}
