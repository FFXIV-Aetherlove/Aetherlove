using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AetherOS.Apps.Aetherling.Engine;

/// <summary>Tint roles a layer can take. Body/Accent/Eye are multiplied by the tint colour at draw time;
/// None is drawn true-colour (the overlay layer).</summary>
public enum TintRole
{
    None,
    Body,
    Accent,
    Eye,
}

public sealed class LayerDef
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("file")]
    public string File { get; set; } = string.Empty;

    [JsonPropertyName("tint")]
    public string Tint { get; set; } = "none";

    /// <summary>Style-owned opacity for this layer (0 to 1), a material property of the sheet
    /// set rather than of the colour it is tinted with.</summary>
    [JsonPropertyName("alpha")]
    public float Alpha { get; set; } = 1f;

    [JsonIgnore]
    public TintRole Role => Tint.ToLowerInvariant() switch
    {
        "body" => TintRole.Body,
        "accent" => TintRole.Accent,
        "eye" => TintRole.Eye,
        _ => TintRole.None,
    };
}

public sealed class AnimationDef
{
    [JsonPropertyName("frames")]
    public int[] Frames { get; set; } = [];

    [JsonPropertyName("fps")]
    public float Fps { get; set; } = 8;

    [JsonPropertyName("loop")]
    public bool Loop { get; set; }
}

/// <summary>One atlas manifest: cell size, layer sheets, animations and per-frame anchor points.
/// Frame indices are cell indices into the atlas grid, row-major, <see cref="Columns"/> cells
/// per row.</summary>
public sealed class AtlasManifest
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Material treatment key, which is what picks the specular parameters.</summary>
    [JsonPropertyName("style")]
    public string Style { get; set; } = "core";

    /// <summary>What the sheet calls itself, shown as the summary line under the newborn.</summary>
    [JsonPropertyName("skinName")]
    public string SkinName { get; set; } = string.Empty;

    [JsonPropertyName("cell")]
    public int Cell { get; set; } = 256;

    [JsonPropertyName("columns")]
    public int Columns { get; set; } = 8;

    /// <summary>Atlas width in pixels (columns times cell).</summary>
    [JsonPropertyName("sheetSize")]
    public int SheetSize { get; set; } = 2048;

    /// <summary>Atlas height in pixels. Absent or 0 means the sheet is square.</summary>
    [JsonPropertyName("sheetHeight")]
    public int SheetHeight { get; set; }

    /// <summary>The height UVs are actually resolved against.</summary>
    [JsonIgnore]
    public int EffectiveSheetHeight => SheetHeight > 0 ? SheetHeight : SheetSize;

    [JsonIgnore]
    public int Rows => Math.Max(1, EffectiveSheetHeight / Math.Max(1, Cell));

    [JsonPropertyName("layers")]
    public List<LayerDef> Layers { get; set; } = [];

    [JsonPropertyName("animations")]
    public Dictionary<string, AnimationDef> Animations { get; set; } = [];

    /// <summary>Anchor name to per-cell [x, y] in cell-local pixels.</summary>
    [JsonPropertyName("anchors")]
    public Dictionary<string, List<int[]>> Anchors { get; set; } = [];

    public static AtlasManifest Load(string path)
    {
        var manifest = JsonSerializer.Deserialize<AtlasManifest>(File.ReadAllText(path));
        if (manifest == null || manifest.Layers.Count == 0 || !manifest.Animations.ContainsKey("idle"))
        {
            throw new InvalidDataException($"Invalid Aetherling atlas manifest: {path}");
        }

        return manifest;
    }

    /// <summary>UV rect (u0, v0, u1, v1) for a cell index. The two axes resolve separately:
    /// sheets are trimmed to the rows their frames use, so they are rarely square.</summary>
    public (float U0, float V0, float U1, float V1) UvForCell(int cellIndex)
    {
        var col = cellIndex % Columns;
        var row = cellIndex / Columns;
        var cellU = (float)Cell / SheetSize;
        var cellV = (float)Cell / EffectiveSheetHeight;
        return (col * cellU, row * cellV, (col + 1) * cellU, (row + 1) * cellV);
    }

    /// <summary>Anchor point for a cell in cell-local pixels, or the cell centre as fallback.</summary>
    public Vector2 AnchorForCell(string anchor, int cellIndex)
    {
        if (Anchors.TryGetValue(anchor, out var list) && cellIndex < list.Count && list[cellIndex].Length >= 2)
        {
            return new Vector2(list[cellIndex][0], list[cellIndex][1]);
        }

        return new Vector2(Cell / 2f, Cell / 2f);
    }
}
