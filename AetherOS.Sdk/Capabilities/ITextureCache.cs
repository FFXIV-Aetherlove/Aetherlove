using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace AetherOS.Sdk;

/// <summary>Loads and caches disk-image textures, returning the ImGui handle ready for <c>ImGui.Image</c>.</summary>
public interface ITextureCache
{
    /// <summary>The ImGui texture handle for <paramref name="path"/>, or null while it is still decoding or
    /// the file is missing.</summary>
    ImTextureID? Get(string path);

    /// <summary>The pixel dimensions of the decoded image, or null while it is still decoding or missing.
    /// Use it to turn a pixel crop rectangle into UV coordinates for a cropped preview.</summary>
    Vector2? GetSize(string path);

    /// <summary>A downscaled copy of the image fitting <paramref name="maxSide"/> pixels on its longer edge,
    /// for grids and tiles that would otherwise keep a full-size screenshot on the GPU per cell. Null while
    /// the thumbnail is still being made; falls back to the full image when one cannot be made.</summary>
    ImTextureID? GetThumbnail(string path, int maxSide);
}
