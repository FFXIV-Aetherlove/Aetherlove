using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AetherOS.PetKit.Engine;

/// <summary>An accessory: a static image that rides a named anchor. The origin is the point in
/// the image pinned to the anchor, so it follows hops and bobs automatically.</summary>
public sealed class AccessoryDef
{
    /// <summary>The weapon slot, the one slot where a single item does not describe the whole
    /// loadout: main hand and off hand are separate entries so either half can be worn alone.</summary>
    public const string ArmsSlot = "arms";

    /// <summary>The back-banner slot. Not sold or shipped this release; the constant stays so the
    /// displacement rule reads whole.</summary>
    public const string BannerSlot = "banner";

    /// <summary>The furniture slot: things the creature sits in or beside rather than wears.</summary>
    public const string NookSlot = "nook";

    /// <summary>The wrap slot: rings and bands that go round the body on the waist seat.</summary>
    public const string WrapSlot = "wrap";

    /// <summary>The ears slot: a code-drawn pair riding the shell's own <c>earL</c>/<c>earR</c>
    /// anchors, behind the head like the horns. Independent of <see cref="TailSlot"/>: either
    /// may be worn alone, both together, or neither.</summary>
    public const string EarsSlot = "ears";

    /// <summary>The tail slot: a code-drawn tail on the shell's own <c>tail</c> anchor, behind
    /// the body so it pokes out from behind rather than being painted over it.</summary>
    public const string TailSlot = "tail";

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("slot")]
    public string Slot { get; set; } = "head";

    [JsonPropertyName("tier")]
    public string Tier { get; set; } = "free";

    [JsonIgnore]
    public ItemTier ItemTier => ItemTiers.Parse(Tier);

    [JsonPropertyName("file")]
    public string File { get; set; } = string.Empty;

    [JsonPropertyName("anchor")]
    public string Anchor { get; set; } = "head";

    /// <summary>Pixel point inside the accessory image pinned onto the anchor.</summary>
    [JsonPropertyName("origin")]
    public int[] Origin { get; set; } = [0, 0];

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    /// <summary>True for nooks and the like, drawn behind the body instead of in front.</summary>
    [JsonPropertyName("behind")]
    public bool Behind { get; set; }

    /// <summary>Whether the piece stays put while the creature moves. Unset means the slot decides, and
    /// furniture decides yes: a cushion is a thing standing on the floor, and one that bobs, squashes and
    /// drifts along with the body it is supposed to be holding reads as glued to the pet rather than as
    /// something the pet is sitting in. Set it explicitly to overrule the slot either way.</summary>
    [JsonPropertyName("still")]
    public bool? Still { get; set; }

    [JsonIgnore]
    public bool StaysStill => Still ?? Slot == NookSlot;

    /// <summary>The live-sim record, when this item has one: "kite" hands the item to the
    /// flown-item rig (<c>Rendering/KiteFx.cs</c>), which simulates the strings the sprite no
    /// longer bakes. Null (every item shipped before this field) draws exactly as it always did.</summary>
    [JsonPropertyName("fx")]
    public string? Fx { get; set; }

    /// <summary>Where the simulated lines moor on the sprite: pixels relative to the ORIGIN (the
    /// pin), y down, unflipped. Written by the generator that draws the sprite, so the two can
    /// never disagree.</summary>
    [JsonPropertyName("fxBridle")]
    public float[]? FxBridle { get; set; }

    /// <summary>Room the sim occupies beyond the image quad, [left, up, right, down] in the
    /// sprite's own pixels. The footprint charges it: the sprite shrank to what is genuinely a
    /// picture, but the lines still sweep the space the baked ones held.</summary>
    [JsonPropertyName("fxReach")]
    public float[]? FxReach { get; set; }

    [JsonIgnore]
    public Vector2 FxBridlePoint => FxBridle is { Length: >= 2 } b ? new Vector2(b[0], b[1]) : Vector2.Zero;

    /// <summary>The tail model, on a <see cref="TailSlot"/> item. Present instead of a sprite:
    /// these parts are drawn in code from the shell's palette, so they cost no texture memory,
    /// follow every colour profile for free, and can be driven by a mood.</summary>
    [JsonPropertyName("tail")]
    public TailPartDef? Tail { get; set; }

    /// <summary>The ear model, on an <see cref="EarsSlot"/> item.</summary>
    [JsonPropertyName("ears")]
    public EarPartDef? Ears { get; set; }

    /// <summary>A strand fan worn on the <see cref="EarsSlot"/> (the Antennae): a pair of code-drawn
    /// stalks sown under the head pin and driven by the strand rig, where an ear model is a shape.</summary>
    [JsonPropertyName("strands")]
    public StrandDef? Strands { get; set; }

    /// <summary>The FAR half of an item that goes AROUND the creature, drawn behind the body while
    /// <see cref="File"/> is drawn in front of it. Empty on everything that does not wrap. Two pictures
    /// because a ring goes round a creature and no single quad can be both in front of a body and behind
    /// it; both halves share one origin, emitted from one crop box, so they cannot drift apart.</summary>
    [JsonPropertyName("back")]
    public string Back { get; set; } = string.Empty;

    /// <summary>The waist this wrap was DRAWN for: the seat half-width it was authored against, 256-space.
    /// The renderer scales the item by the shell's own seat over this, which is what lets one ring fit a
    /// hatchling and the adult without a second PNG. 0 means "do not rescale".</summary>
    [JsonPropertyName("wrapRx")]
    public float WrapRx { get; set; }

    /// <summary>A band of cloth rather than a torus: the seat sets how far it reaches and the cloth keeps
    /// the thickness it was drawn at, so the scale applies in X only.</summary>
    [JsonPropertyName("wrapBand")]
    public bool WrapBand { get; set; }

    /// <summary>True when this item has a far half to draw behind the body. Keyed on the FIELD, never on
    /// the slot: the Rubber Ring stays an outfit piece and gains a back half without moving tab.</summary>
    [JsonIgnore]
    public bool HasWrapBack => Back.Length > 0;

    /// <summary>True when this item is placed by the shell's seat rather than by its own pin.</summary>
    [JsonIgnore]
    public bool RidesWrapSeat => WrapRx > 0f;

    /// <summary>True for the code-drawn parts, which carry a model record where every other
    /// accessory carries a PNG. The one place the catalogue has to know they are different;
    /// everything downstream reads the slot.</summary>
    [JsonIgnore]
    public bool IsDrawnPart => (Slot == TailSlot && Tail != null)
                               || (Slot == EarsSlot && (Ears != null || Strands != null));

    [JsonIgnore]
    public Vector2 OriginPoint => new(
        Origin.Length >= 2 ? Origin[0] : 0, Origin.Length >= 2 ? Origin[1] : 0);

    /// <summary>True when wearing this must take the other off because both pin to the same
    /// place: one arm per hand (keyed on the anchor, the slot's two hands are separate), one
    /// banner ever. Deliberately not a general one-per-slot rule; a halo over a hat is a
    /// combination the wardrobe is right to allow.</summary>
    public bool Displaces(AccessoryDef other) =>
        Slot == other.Slot
        && Name != other.Name
        && Slot switch
        {
            ArmsSlot => Anchor == other.Anchor,
            BannerSlot => true,

            // One pair of ears and one tail, ever, for the banner's reason rather than the
            // weapons': both pin to the same place and would simply draw through each other.
            // The two slots stay independent of one another: this is a rule about wearing two
            // TAILS, never about wearing a tail with ears.
            EarsSlot => true,
            TailSlot => true,

            // One wrap, for the banner's reason: every ring pins to the same waist ellipse, so
            // a second would draw straight through the first.
            WrapSlot => true,

            // One nook: a creature has one place, and two pieces of furniture drawn on one
            // ground line read as a rendering fault rather than a choice.
            NookSlot => true,
            _ => false,
        };

    public static List<AccessoryDef> LoadAll(string directory)
    {
        var result = new List<AccessoryDef>();
        if (!Directory.Exists(directory))
        {
            return result;
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            try
            {
                var def = JsonSerializer.Deserialize<AccessoryDef>(System.IO.File.ReadAllText(file));
                if (def != null && (def.File.Length > 0 || def.IsDrawnPart))
                {
                    result.Add(def);
                }
            }
            catch (System.Exception)
            {
                // A malformed def costs one item, never the wardrobe.
            }
        }

        return result;
    }
}
