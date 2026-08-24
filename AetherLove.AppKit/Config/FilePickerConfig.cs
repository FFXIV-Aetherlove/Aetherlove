using System.Collections.Generic;

namespace AetherLove.Config;

/// <summary>What the file picker remembers between opens and between sessions: one global last
/// folder (by decision, shared across every purpose), the starred folders, and the view
/// preferences. All of it is comfort, none of it is data: a missing or stale path degrades to
/// the user's documents folder and nothing is lost.</summary>
public sealed class FilePickerConfig
{
    /// <summary>The folder the last pick happened in, restored as the start folder next time.</summary>
    public string LastFolder { get; set; } = "";

    /// <summary>Folders the user starred, shown in the sidebar in the order they were added.</summary>
    public List<string> Favorites { get; set; } = [];

    /// <summary>Window size in design pixels, kept across opens; zero means the default.</summary>
    public float WindowW { get; set; }

    public float WindowH { get; set; }

    /// <summary>True for the thumbnail grid, false for the detail list.</summary>
    public bool GridView { get; set; } = true;

    /// <summary>0 name, 1 date, 2 size; negative order flags descending via <see cref="SortDescending"/>.</summary>
    public int SortField { get; set; }

    public bool SortDescending { get; set; }

    public bool ShowHidden { get; set; }
}
