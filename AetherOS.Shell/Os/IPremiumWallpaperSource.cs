using System;
using Dalamud.Interface.Textures.TextureWraps;

namespace AetherLove.Os;

/// <summary>The shell's way to reach a purchased theme's wallpaper. Implemented plugin-side over the seal
/// store, so the shell never learns anything about the store or its crypto.</summary>
public interface IPremiumWallpaperSource
{
    /// <summary>The wallpaper for a purchased theme; null while the seal is opening or when there is none.</summary>
    IDalamudTextureWrap? GetWallpaper(Guid productId);
}
