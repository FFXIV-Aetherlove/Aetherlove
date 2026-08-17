using System.Collections.Generic;
using System.IO;
using System.Numerics;
using AetherLove;
using Dalamud.Interface.Textures;

namespace AetherOS.Apps.Stacker;

/// <summary>Loads and slices a mino skin sprite sheet: one tile per piece, laid out S, Z, J, L, T, O, I
/// (kind order matches the artist's canvas, not StackerGame's internal I/J/L/O/S/T/Z indices), each
/// <see cref="TileSize"/> px square with <see cref="Gap"/> px of transparent padding between tiles.</summary>
internal static class MinoSkins
{
    public const int TileSize = 36;
    public const int Gap = 8;

    /// <summary>Sheet column for each StackerGame piece kind (I, J, L, O, S, T, Z).</summary>
    private static readonly int[] ColumnByKind = [6, 2, 3, 5, 0, 4, 1];

    private static readonly Dictionary<string, ISharedImmediateTexture?> Cache = new();

    /// <summary>The named skin's texture, loaded from <c>Media/stacker/&lt;prefix&gt;_&lt;name&gt;.png</c>
    /// next to the plugin and cached; null until the file is found (or if it never is). The ghost-piece
    /// atlas (<c>skinghost_</c>) uses the same tile layout as the normal one, just a different prefix.</summary>
    public static ISharedImmediateTexture? Get(string name, string prefix = "skin")
    {
        var key = $"{prefix}_{name}";
        if (!Cache.TryGetValue(key, out var tex))
        {
            var dir = Path.GetDirectoryName(UiHost.PluginInterface.AssemblyLocation.FullName) ?? "";
            var path = Path.Combine(dir, "Media", "stacker", $"{key}.png");
            tex = File.Exists(path) ? UiHost.TextureProvider.GetFromFile(path) : null;
            Cache[key] = tex;
        }
        return tex;
    }

    /// <summary>The UV rect (0..1) for a piece kind's tile, given the sheet's pixel size.</summary>
    public static (Vector2 Uv0, Vector2 Uv1) Uv(int kind, Vector2 textureSize)
    {
        var col = ColumnByKind[((kind % ColumnByKind.Length) + ColumnByKind.Length) % ColumnByKind.Length];
        var x0 = col * (TileSize + Gap);
        return (new Vector2(x0 / textureSize.X, 0f), new Vector2((x0 + TileSize) / textureSize.X, TileSize / textureSize.Y));
    }
}
