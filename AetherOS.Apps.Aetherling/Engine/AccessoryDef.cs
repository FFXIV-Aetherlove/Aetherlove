using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AetherOS.Apps.Aetherling.Engine;

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
                if (def is { File.Length: > 0 })
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
