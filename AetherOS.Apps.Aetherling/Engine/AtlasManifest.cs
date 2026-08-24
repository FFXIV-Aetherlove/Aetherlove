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

    /// <summary>The sheet's stable identity key ("wispv2"), which is what the parts rig watches to
    /// know the creature changed shells under it.</summary>
    [JsonPropertyName("skin")]
    public string Skin { get; set; } = string.Empty;

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

    /// <summary>Per-slot accessory fit multipliers; slots not listed wear items at 1.</summary>
    [JsonPropertyName("slotScales")]
    public Dictionary<string, float> SlotScales { get; set; } = [];

    /// <summary>The sheet's own painted ink colour (hex), shared by the dynamic mouth so its
    /// strokes match the drawn line work.</summary>
    [JsonPropertyName("lineColor")]
    public string LineColor { get; set; } = string.Empty;

    /// <summary>Species multiplier on the dynamic mouth's size; the young forms run smaller.</summary>
    [JsonPropertyName("mouthScale")]
    public float MouthScale { get; set; } = 1f;

    /// <summary>Named lid state to the sheet cell that draws it: open, threeq, half, quarter, shut,
    /// plus the two pre-nap pairs drowsy and heavy. A sheet without the map is a two-state face and
    /// falls back to its blink's own shut cell.</summary>
    [JsonPropertyName("eyeCells")]
    public Dictionary<string, int> EyeCells { get; set; } = [];

    public float SlotScaleFor(string slot) => SlotScales.TryGetValue(slot, out var scale) ? scale : 1f;

    /// <summary>The waist: where an item that goes AROUND the body rides, and how wide it is there, in
    /// cell pixels on the rest cell. A shell that declares none is not gated: wraps keep their plain pin.</summary>
    [JsonPropertyName("wrapSeat")]
    public SeatDef? WrapSeat { get; set; }

    /// <summary>The same for the crown. A head wrap picks this one, chosen by the anchor the item already
    /// names, so nothing has to say which seat it wants twice.</summary>
    [JsonPropertyName("headSeat")]
    public SeatDef? HeadSeat { get; set; }

    /// <summary>The cell a lid state draws on, or null when this sheet cannot say. Anything closed-ish
    /// falls back to the blink's shut cell so an older sheet still shuts its eyes; anything open-ish
    /// answers null, which leaves the running clip's own cell alone.</summary>
    public int? EyeCellFor(string state)
    {
        if (EyeCells.TryGetValue(state, out var cell))
        {
            return cell;
        }
        return state is "shut" or "quarter" or "heavy" or "drowsy" ? ShutEyeCell : null;
    }

    /// <summary>The blink's middle frame, which is the one cell every sheet has with the eyes closed.</summary>
    [JsonIgnore]
    public int? ShutEyeCell =>
        Animations.TryGetValue("blink", out var blink) && blink.Frames.Length >= 2
            ? blink.Frames[blink.Frames.Length / 2]
            : null;

    /// <summary>Declaring a mouth anchor is the surgery marker: the sheet shipped without a baked
    /// mouth and expects the dynamic one. A sheet without it keeps its painted face untouched.</summary>
    [JsonIgnore]
    public bool HasDynamicMouth => Anchors.ContainsKey("mouth");

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

    /// <summary>The frame to read an anchor from for anything that must not animate with the body: idle's
    /// first, which is the pose the creature is in when it is doing nothing. Null when a sheet declares no
    /// idle, and the caller keeps the live frame rather than guessing at cell zero.</summary>
    [JsonIgnore]
    public int? RestCell =>
        Animations.TryGetValue("idle", out var idle) && idle.Frames.Length > 0 ? idle.Frames[0] : null;

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

/// <summary>One ellipse on the rest cell: a waist or a crown an encircling item rides. <c>rx</c> is what
/// the band should MATCH, usually the body's half-width at <c>cy</c>; <c>sink</c> drops the seat a few
/// pixels so the band sits ON the silhouette rather than level with its widest point.</summary>
public sealed class SeatDef
{
    [JsonPropertyName("cx")]
    public float Cx { get; set; }

    [JsonPropertyName("cy")]
    public float Cy { get; set; }

    [JsonPropertyName("rx")]
    public float Rx { get; set; }

    [JsonPropertyName("ry")]
    public float Ry { get; set; }

    [JsonPropertyName("rot")]
    public float Rot { get; set; }

    [JsonPropertyName("sink")]
    public float Sink { get; set; }
}
