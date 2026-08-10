using Dalamud.Bindings.ImGui;

namespace AetherLove.Os;

/// <summary>Now-playing state the notification shade's media tile shows. Implemented plugin-side over the
/// media session host, so the shade can live in AetherOS.Shell without referencing plugin services.</summary>
public interface IOsMediaRemote
{
    /// <summary>False hides the tile entirely: surface toggled off, no session, or backend not ready.</summary>
    bool TileVisible { get; }

    /// <summary>A readable session exists, regardless of whether the shade tile is switched on. Surfaces
    /// outside the shade (the minimised phone) key on this instead, so the shade toggle stays the shade's.</summary>
    bool HasSession { get; }

    string Title { get; }

    string Artist { get; }

    bool IsPlaying { get; }

    bool CanControl { get; }

    /// <summary>Album art for the current session, resolved per frame; null draws the glyph fallback.</summary>
    ImTextureID? Art { get; }

    void TogglePlayPause();

    void Next();

    void Previous();
}
