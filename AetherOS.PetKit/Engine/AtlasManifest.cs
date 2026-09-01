using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AetherOS.PetKit.Engine;

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

    /// <summary>The outline as a VALUE of the body tint instead of a literal hex; 0 (the default)
    /// means use <see cref="LineColor"/>. A drawn shell whose ink sits above the body (the
    /// Smoulder) opts in so its outline is a dark of the player's own colour, the thing a baked
    /// sheet outline always was.</summary>
    [JsonPropertyName("lineValue")]
    public float LineValue { get; set; }

    /// <summary>This style's ink against a body tint: THE one resolver, so the drawn outline, the
    /// dynamic mouth, the limbs and the drawn parts cannot disagree about what colour the creature
    /// is inked in. Returns default when the style declares neither, which every caller already
    /// tests for before falling back to the house slate.</summary>
    public Vector4 InkFor(Vector4 bodyTint)
    {
        if (LineValue > 0f)
        {
            return new Vector4(
                bodyTint.X * LineValue,
                bodyTint.Y * LineValue,
                bodyTint.Z * LineValue,
                bodyTint.W);
        }

        return LineColor.Length > 0 ? Palette.ParseHex(LineColor) : default;
    }

    /// <summary>Per-item and per-slot fit corrections: the lever that lets a shell adjust how the
    /// shared accessory art sits on it without commissioning its own copy. Keys are a slot key or
    /// an accessory's Name; resolve through <see cref="FitFor"/>.</summary>
    [JsonPropertyName("fit")]
    public Dictionary<string, FitDef> Fit { get; set; } = [];

    /// <summary>The shell's own strand anatomy (tendrils, legs, antennae), drawn code-side behind
    /// the body. Not a purchase: a shell that declares strands is anatomically incomplete without
    /// them. Null for a shell that has none.</summary>
    [JsonPropertyName("strands")]
    public StrandDef? Strands { get; set; }

    /// <summary>The arm the hand rig draws. Never null: a manifest that says nothing gets the
    /// shipped defaults, so one tuned limb reaches every shell and a shell overrides only the
    /// fields it disagrees with.</summary>
    [JsonPropertyName("handStyle")]
    public HandStyleDef HandStyle { get; set; } = new();

    /// <summary>This shell's fit correction for one worn item: the slot's entry as the default,
    /// the item's own entry overriding it FIELD BY FIELD (that is why <see cref="FitDef"/>'s
    /// members are nullable). Declared nowhere, the answer is (1, zero, 0). The scale COMPOSES
    /// with <see cref="SlotScaleFor"/>; the offset is 256-space accessory units authored
    /// unflipped; the rotation is degrees about the pin, also unflipped, negated under a flip
    /// exactly as the offset's X is.</summary>
    public (float Scale, Vector2 Offset, float Rot) FitFor(string slot, string name)
    {
        var scale = 1f;
        var offset = Vector2.Zero;
        var rot = 0f;

        void Merge(string key)
        {
            if (!Fit.TryGetValue(key, out var def) || def == null)
            {
                return;
            }

            scale = def.Scale ?? scale;
            offset.X = def.Dx ?? offset.X;
            offset.Y = def.Dy ?? offset.Y;
            rot = def.Rot ?? rot;
        }

        Merge(slot);
        Merge(name);
        return (scale, offset, rot);
    }

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

    /// <summary>A named anchor under a pose: the one every drawing surface should ask, because it
    /// is the only one that knows whether the body it is pinning to is stepping or flowing.
    ///
    /// <para>A drawn body's own answer (<see cref="PetPose.DrawnAnchor"/>) beats every sample of
    /// it. With <see cref="PetPose.SmoothAnchors"/> off this is exactly
    /// <see cref="AnchorForCell"/> and a sheet pet is bit-for-bit unchanged. With it on, the pin
    /// is read between cells along a Catmull-Rom through four keys at the clip's own sub-frame
    /// phase: the baked anchors are samples of the same pose curve the body is drawn from, so
    /// reading between them along that curve undoes the quantisation rather than smoothing a
    /// guess. A straight blend would put the pin on a chord across the curve, which on screen is
    /// a mouth sliding around on a face.</para></summary>
    public Vector2 AnchorFor(string anchor, PetPose pose)
    {
        if (pose.DrawnAnchor is { } drawn && drawn(anchor) is { } live)
        {
            return live;
        }

        var here = AnchorForCell(anchor, pose.CellIndex);
        if (!pose.SmoothAnchors || pose.FramePhase <= 0f)
        {
            return here;
        }

        var next = AnchorForCell(anchor, pose.NextCellIndex);
        var t = Math.Clamp(pose.FramePhase, 0f, 1f);
        var prev = AnchorForCell(anchor, pose.PrevCellIndex);
        var after = AnchorForCell(anchor, pose.AfterCellIndex);
        return new Vector2(
            Catmull(prev.X, here.X, next.X, after.X, t),
            Catmull(prev.Y, here.Y, next.Y, after.Y, t));
    }

    /// <summary>The same Catmull-Rom the drawn shells pose with, repeated here rather than shared
    /// because the manifest must not depend on the drawing layer.</summary>
    private static float Catmull(float p0, float p1, float p2, float p3, float t)
    {
        var t2 = t * t;
        var t3 = t2 * t;
        return 0.5f * ((2f * p1)
            + ((-p0 + p2) * t)
            + (((2f * p0) - (5f * p1) + (4f * p2) - p3) * t2)
            + ((-p0 + (3f * p1) - (3f * p2) + p3) * t3));
    }
}

/// <summary>How one accessory, or a whole slot of them, is made to sit on THIS shell: a scale and
/// a nudge in the art's own units. Per-shell fit data, never per-shell art: the moment a shell
/// needs its own picture of a hat, every new shell owes the whole catalogue a redraw. Members are
/// nullable because "absent" and "0" are different answers: a slot can set a lift and one item
/// override only its scale while keeping the lift.</summary>
public sealed class FitDef
{
    /// <summary>Composed with the slot multiplier rather than replacing it.</summary>
    [JsonPropertyName("scale")]
    public float? Scale { get; set; }

    /// <summary>Sideways nudge, 256-space accessory units, authored in the creature's own
    /// left/right so it mirrors when the pet turns around.</summary>
    [JsonPropertyName("dx")]
    public float? Dx { get; set; }

    /// <summary>Vertical nudge, 256-space accessory units; negative lifts.</summary>
    [JsonPropertyName("dy")]
    public float? Dy { get; set; }

    /// <summary>Tilt in degrees about the item's own pin, authored unflipped. Placement of last
    /// resort: turning art far enough that its shading no longer matches its lighting is a
    /// redraw, not a fit.</summary>
    [JsonPropertyName("rot")]
    public float? Rot { get; set; }
}

/// <summary>The arm the hand rig draws: what shape sits between the shoulder pin and the hand,
/// and what the hand itself is. The defaults ARE the shipped arm, so a shell gets the limb by
/// saying nothing at all. All numbers are 256-space, unlike <see cref="StrandDef"/>, because
/// emote hand tracks are authored in those units and a limb measured in cell pixels would reach
/// differently on a 384 sheet than on a 256 one.</summary>
public sealed class HandStyleDef
{
    /// <summary>"flow" (default) is the pseudopod under spring and water; "pseudopod" is the same
    /// limb with both motions off; "capsule" is the straight tapered rod.</summary>
    [JsonPropertyName("model")]
    public string Model { get; set; } = "flow";

    /// <summary>"trio" (default) is the ball with three small digits; "ball" the plain circle;
    /// "mitten" adds a thumb only.</summary>
    [JsonPropertyName("tip")]
    public string Tip { get; set; } = "trio";

    /// <summary>Shoulder radius, a shade wider than the nub so the root slides behind it.</summary>
    [JsonPropertyName("root")]
    public float Root { get; set; } = 10f;

    /// <summary>The wrist end of the taper, slimmer than the hand.</summary>
    [JsonPropertyName("wrist")]
    public float Wrist { get; set; } = 7f;

    /// <summary>Hand radius, the circle the baked nub is.</summary>
    [JsonPropertyName("hand")]
    public float Hand { get; set; } = 9f;

    /// <summary>Maximum arc length. The limb reaches no further however far a track asks, and at
    /// the stop it strains rather than clipping; limited length is what keeps a hand inside the
    /// envelope the window reserved.</summary>
    [JsonPropertyName("len")]
    public float Len { get; set; } = 26f;

    /// <summary>How much of <see cref="Len"/> the limb spends at rest; the rest is slack carried
    /// as a bow.</summary>
    [JsonPropertyName("fill")]
    public float Fill { get; set; } = 0.10f;

    /// <summary>The direction the limb leaves the body, radians from straight down toward
    /// outboard. A capsule pivots to point at the hand; a pseudopod grows out this way and BENDS
    /// to reach.</summary>
    [JsonPropertyName("sag")]
    public float Sag { get; set; } = 0.71f;

    /// <summary>How strongly the root tangent is held before the curve gives in to the target.</summary>
    [JsonPropertyName("bow")]
    public float Bow { get; set; } = 0.40f;

    /// <summary>Faked volume conservation: a contracted limb fattens, a stretched one thins.</summary>
    [JsonPropertyName("swell")]
    public float Swell { get; set; } = 0.23f;

    /// <summary>How loosely the hand chases its track. Zero is the hand nailed to the track.</summary>
    [JsonPropertyName("lag")]
    public float Lag { get; set; } = 0.24f;

    /// <summary>The water: 256-space units of wander, scattered per hand.</summary>
    [JsonPropertyName("drift")]
    public float Drift { get; set; } = 1.2f;

    /// <summary>Turns per second of the drift. Slow: a current, not a second tremor.</summary>
    [JsonPropertyName("driftSpeed")]
    public float DriftSpeed { get; set; } = 0.08f;

    /// <summary>Where the hand rests, outboard 256-space from the shoulder pin: the pose every
    /// emote track is a delta from, and the item root for a hand-anchored accessory.</summary>
    [JsonPropertyName("restX")]
    public float RestX { get; set; } = 12f;

    /// <summary>See <see cref="RestX"/>; positive is down.</summary>
    [JsonPropertyName("restY")]
    public float RestY { get; set; } = 6f;

    /// <summary>The rest pose as a vector, outboard 256-space. The off hand mirrors X.</summary>
    [JsonIgnore]
    public Vector2 Rest => new(RestX, RestY);

    /// <summary>Draw the limbs in FRONT of the body rather than behind it.</summary>
    [JsonPropertyName("front")]
    public bool Front { get; set; } = true;

    /// <summary>True when this row asks for the pseudopod curve rather than the shipped rod.</summary>
    [JsonIgnore]
    public bool IsCurved => !string.Equals(Model, "capsule", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when this row asks for the spring and the water on top of the curve.</summary>
    [JsonIgnore]
    public bool IsFlowing => string.Equals(Model, "flow", StringComparison.OrdinalIgnoreCase);
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
