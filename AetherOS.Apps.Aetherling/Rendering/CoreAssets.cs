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
