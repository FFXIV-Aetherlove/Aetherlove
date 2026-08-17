using Dalamud.Bindings.ImGui;

namespace AetherLove.Os;

/// <summary>Now-playing state the notification shade's media tile shows. Implemented plugin-side over the
/// media session host, so the shade can live in AetherOS.Shell without referencing plugin services.</summary>
public interface IOsMediaRemote
{
    /// <summary>False hides the tile entirely: surface toggled off, no session, or backend not ready.</summary>
    bool TileVisible { get; }

    /// <summary>A readable session exists, regardless of which surfaces are switched on.</summary>
    bool HasSession { get; }

    /// <summary>False hides the transport on the minimised phone, which then keeps its logo. Separate from
    /// the shade's own gate so each surface can be switched off on its own.</summary>
    bool MiniVisible { get; }

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
