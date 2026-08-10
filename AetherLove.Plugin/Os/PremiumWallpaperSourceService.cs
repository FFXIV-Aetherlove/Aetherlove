using System;
using AetherLove.Services.Store;
using Dalamud.Interface.Textures.TextureWraps;

namespace AetherLove.Os;

/// <summary>Plugin-side <see cref="IPremiumWallpaperSource"/>: hands the shell the wallpaper decrypted
/// from a purchased theme's seal.</summary>
public sealed class PremiumWallpaperSourceService(PremiumThemeService themes) : IPremiumWallpaperSource
{
    public IDalamudTextureWrap? GetWallpaper(Guid productId) => themes.BackgroundWrap(productId);
}
