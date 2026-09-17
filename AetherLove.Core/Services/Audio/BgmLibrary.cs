using System.IO;
using AetherLove.Services.Media;
using AetherLove.Shared.Assets;

namespace AetherLove.Services.Audio;

/// <summary>Where the phone's music lives: the <c>bgm</c> pack of the downloaded assets, brought in by the
/// asset sync behind the phone. Nothing here plays anything; the Aetherling host asks <see cref="Resolve"/>
/// for a path and gets null for a track that has not arrived, which it treats as silence.</summary>
public sealed class BgmLibrary
{
    public string Root => MediaPaths.Downloaded(MediaPaths.Music);

    /// <summary>The track's path when it is on disk, else null.</summary>
    public string? Resolve(string fileName)
    {
        if (!AssetManifest.IsValidPath(fileName) || fileName.Contains('/'))
        {
            return null;
        }
        var path = Path.Combine(Root, fileName);
        return File.Exists(path) ? path : null;
    }
}
