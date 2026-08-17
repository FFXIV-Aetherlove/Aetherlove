using System;
using System.Collections.Generic;
using System.IO;
using AetherOS.Apps.Aetherling.Engine;

namespace AetherOS.Apps.Aetherling.Rendering;

/// <summary>Locates and loads one sheet-set: a manifest and its layer sheets. It deals in paths rather than
/// textures, because textures are resolved through the SDK's texture cache at draw time.</summary>
public sealed class CoreAssets
{
    /// <summary>The crystal's own sheets.</summary>
    public const string CeremonyFolder = "ceremony";

    /// <summary>What comes out of it. Named for as little as the folder above it is.</summary>
    public const string HatchlingFolder = "1";

    /// <summary>The two later young forms and the grown shape, numbered like the first.</summary>
    public const string Hatchling2Folder = "2";
    public const string Hatchling3Folder = "3";
    public const string AdultFolder = "4";

    /// <summary>The wearables and the palette file, beside the sheet folders.</summary>
    public const string AccessoryFolder = "acc";
    public const string PaletteFile = "palettes.json";

    /// <summary>The food art, one PNG per element key.</summary>
    public const string CrystalFolder = "crystals";

    /// <summary>Path to an element's crystal art, or null when the tree is missing. The caller
    /// hands it to the texture cache, which answers null for anything unreadable, so a missing
    /// file costs the icon and nothing else.</summary>
    public static string? CrystalPath(string elementKey) =>
        ResolveRoot() is { } root && elementKey.Length > 0
            ? Path.Combine(root, CrystalFolder, $"{elementKey}.png")
            : null;

    private const string ManifestFile = "manifest.json";
    private const string BesideAssemblyRoot = "Media";
    private const string BesideAssemblyLeaf = "unknown";

    private CoreAssets(string sheetDirectory, AtlasManifest manifest)
    {
        SheetDirectory = sheetDirectory;
        Manifest = manifest;

        var paths = new string[manifest.Layers.Count];
        for (var i = 0; i < paths.Length; i++)
        {
            paths[i] = Path.GetFullPath(Path.Combine(sheetDirectory, manifest.Layers[i].File));
        }

        LayerPaths = paths;
    }

    /// <summary>The folder the sheets and the manifest were read from.</summary>
    public string SheetDirectory { get; }

    public AtlasManifest Manifest { get; }

    /// <summary>Absolute sheet paths, parallel to <see cref="AtlasManifest.Layers"/>.</summary>
    public IReadOnlyList<string> LayerPaths { get; }

    /// <summary>In-game asset root, set by the host at registration: Dalamud byte-loads plugin
    /// assemblies, so <see cref="System.Reflection.Assembly.Location"/> is empty there and the
    /// beside-the-assembly probe below cannot work. Out-of-game hosts leave it null.</summary>
    public static string? AssetRootHint { get; set; }

    /// <summary>Loads from <see cref="AssetRootHint"/> when the host set one, else from the media folder
    /// beside this assembly, falling back to the process base directory. Returns null when the tree is
    /// missing or unreadable, so a bad install costs the crystal and nothing else.</summary>
    public static CoreAssets? Load(string sheetFolder = CeremonyFolder)
    {
        foreach (var root in CandidateRoots())
        {
            try
            {
                var directory = Path.Combine(root, sheetFolder);
                var manifestPath = Path.Combine(directory, ManifestFile);
                if (!File.Exists(manifestPath))
                {
                    continue;
                }

                return new CoreAssets(directory, AtlasManifest.Load(manifestPath));
            }
            catch (Exception)
            {
                // Try the next root.
            }
        }

        return null;
    }

    /// <summary>The first root that holds the asset tree, resolved the same way <see cref="Load"/>
    /// probes, for callers after loose files (the palette JSON, accessory defs) rather than a
    /// sheet set. Null when no root exists.</summary>
    public static string? ResolveRoot()
    {
        foreach (var root in CandidateRoots())
        {
            try
            {
                if (Directory.Exists(root))
                {
                    return root;
                }
            }
            catch (Exception)
            {
                // Try the next root.
            }
        }

        return null;
    }

    private static IEnumerable<string> CandidateRoots()
    {
        if (!string.IsNullOrEmpty(AssetRootHint))
        {
            yield return AssetRootHint;
        }

        var assemblyDir = Path.GetDirectoryName(typeof(CoreAssets).Assembly.Location);
        if (!string.IsNullOrEmpty(assemblyDir))
        {
            yield return Path.Combine(assemblyDir, BesideAssemblyRoot, BesideAssemblyLeaf);
        }

        yield return Path.Combine(AppContext.BaseDirectory, BesideAssemblyRoot, BesideAssemblyLeaf);
    }
}
