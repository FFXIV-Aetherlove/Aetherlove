using Dalamud.Interface.Textures;

namespace AetherLove.Os;

/// <summary>Account info the notification shade header shows. Implemented plugin-side over the session snapshot
/// and OS avatar cache, so the shade can live in AetherOS.Shell without referencing plugin auth services.</summary>
public interface IOsAccountInfo
{
    string? DisplayName { get; }

    ISharedImmediateTexture? Avatar { get; }
}
