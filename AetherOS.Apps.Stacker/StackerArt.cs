using System.Collections.Generic;
using System.IO;
using System.Numerics;
using AetherLove;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;

namespace AetherOS.Apps.Stacker;

/// <summary>The Modern mode's skin assets, all loaded through the texture capability from
/// <c>Media/stacker/</c> beside the plugin. One class for the three sheet roles: mino atlases
/// (<c>skin_</c>/<c>skinghost_</c>, one 36px tile per piece with 8px gaps, artist order S Z J L T O I),
/// two-tile well backgrounds (<c>bg_</c>, light then dark), and single images (<c>gbg_</c> app
/// background, <c>hold_</c>/<c>next_</c> box art). Missing skin files fall back to the role's default
/// sheet; a null result just means the file is absent or still decoding, and callers draw their
/// procedural fallback for that frame.</summary>
internal sealed class StackerArt(ITextureCache textures)
{
    public const int MinoTileSize = 36;
    public const int MinoTileGap = 8;

    /// <summary>Sheet column for each StackerModernGame piece kind (I, J, L, O, S, T, Z).</summary>
    private static readonly int[] ColumnByKind = [6, 2, 3, 5, 0, 4, 1];

    private readonly Dictionary<string, string?> paths = new();

    /// <summary>The texture and pixel size for <c>Media/stacker/&lt;role&gt;_&lt;skin&gt;.png</c>, falling
    /// back to <c>&lt;role&gt;_&lt;fallbackSkin&gt;.png</c>; null while missing or still decoding.</summary>
    public (ImTextureID Handle, Vector2 Size)? Get(string role, string skin, string? fallbackSkin = "default")
    {
        var key = $"{role}_{skin}";
        if (!this.paths.TryGetValue(key, out var path))
        {
            var dir = Path.GetDirectoryName(UiHost.PluginInterface.AssemblyLocation.FullName) ?? "";
            path = Path.Combine(dir, "Media", "stacker", $"{key}.png");
            if (!File.Exists(path) && fallbackSkin is not null)
            {
                path = Path.Combine(dir, "Media", "stacker", $"{role}_{fallbackSkin}.png");
            }
            if (!File.Exists(path))
            {
                path = null;
            }
            this.paths[key] = path;
        }
        if (path is null)
        {
            return null;
        }
        return textures.Get(path) is { } handle && textures.GetSize(path) is { } size
            ? (handle, size)
            : null;
    }

    /// <summary>The UV rect (0..1) for a piece kind's tile in a mino atlas of the given pixel size.
    /// Inset half a texel on every edge: linear filtering samples past the rect, and without the inset the
    /// gap pixels between tiles bleed into the drawn cell as a dirty border.</summary>
    public static (Vector2 Uv0, Vector2 Uv1) MinoUv(int kind, Vector2 textureSize)
    {
        var col = ColumnByKind[((kind % ColumnByKind.Length) + ColumnByKind.Length) % ColumnByKind.Length];
        var x0 = col * (MinoTileSize + MinoTileGap);
        return (new Vector2((x0 + 0.5f) / textureSize.X, 0.5f / textureSize.Y),
            new Vector2((x0 + MinoTileSize - 0.5f) / textureSize.X, (MinoTileSize - 0.5f) / textureSize.Y));
    }

    /// <summary>One of a two-tile background sheet's halves, split along whichever axis is twice the
    /// other, so it works whether the artist laid the tiles side by side or stacked.</summary>
    public static (Vector2 Uv0, Vector2 Uv1) BackgroundUv(Vector2 textureSize, bool light)
    {
        if (textureSize.X >= textureSize.Y)
        {
            var half = textureSize.X * 0.5f;
            var x0 = light ? 0f : half;
            return (new Vector2((x0 + 0.5f) / textureSize.X, 0.5f / textureSize.Y),
                new Vector2((x0 + half - 0.5f) / textureSize.X, (textureSize.Y - 0.5f) / textureSize.Y));
        }
        var halfH = textureSize.Y * 0.5f;
        var y0 = light ? 0f : halfH;
        return (new Vector2(0.5f / textureSize.X, (y0 + 0.5f) / textureSize.Y),
            new Vector2((textureSize.X - 0.5f) / textureSize.X, (y0 + halfH - 0.5f) / textureSize.Y));
    }
}
