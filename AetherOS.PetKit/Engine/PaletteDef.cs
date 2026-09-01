using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AetherOS.PetKit.Engine;

/// <summary>How an item is obtained. Free items belong to everyone and never consult ownership;
/// everything else needs an inventory row (a store purchase or a grant).</summary>
public enum ItemTier
{
    Free,
    Seasonal,
    Premium,
}

/// <summary>Parses the tier field shared by palettes and accessories. Unknown means Free: a
/// catalogue typo must never lock a player out of something, and the opposite mistake is the
/// one review catches.</summary>
public static class ItemTiers
{
    public static ItemTier Parse(string? value) => value?.ToLowerInvariant() switch
    {
        "seasonal" => ItemTier.Seasonal,
        "premium" => ItemTier.Premium,
        _ => ItemTier.Free,
    };
}

/// <summary>A colour variant: three colours multiplied onto the greyscale region sheets. Tiny
/// JSON, zero art per palette.</summary>
public sealed class Palette
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "Dawn";

    [JsonPropertyName("tier")]
    public string Tier { get; set; } = "free";

    [JsonIgnore]
    public ItemTier ItemTier => ItemTiers.Parse(Tier);

    [JsonPropertyName("body")]
    public string Body { get; set; } = "#FFFFFF";

    [JsonPropertyName("accent")]
    public string Accent { get; set; } = "#FFFFFF";

    [JsonPropertyName("eye")]
    public string Eye { get; set; } = "#FFFFFF";

    [JsonIgnore]
    public Vector4 BodyColor => ParseHex(Body);

    [JsonIgnore]
    public Vector4 AccentColor => ParseHex(Accent);

    [JsonIgnore]
    public Vector4 EyeColor => ParseHex(Eye);

    /// <summary>#RRGGBB or #RRGGBBAA; a bad value renders white rather than breaking.</summary>
    public static Vector4 ParseHex(string hex)
    {
        var h = hex.TrimStart('#');
        if ((h.Length != 6 && h.Length != 8)
            || !byte.TryParse(h[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)
            || !byte.TryParse(h[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)
            || !byte.TryParse(h[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            return Vector4.One;
        }

        var a = 255;
        if (h.Length == 8 && byte.TryParse(h[6..8], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsedA))
        {
            a = parsedA;
        }

        return new Vector4(r / 255f, g / 255f, b / 255f, a / 255f);
    }
}

public sealed class PaletteCollection
{
    [JsonPropertyName("palettes")]
    public List<Palette> Palettes { get; set; } = [];

    /// <summary>Loads the palette file; a missing or empty one yields a single default so the
    /// pet always has a colour.</summary>
    public static PaletteCollection Load(string path)
    {
        try
        {
            var collection = JsonSerializer.Deserialize<PaletteCollection>(File.ReadAllText(path));
            if (collection is { Palettes.Count: > 0 })
            {
                return collection;
            }
        }
        catch (System.Exception)
        {
            // Fall through to the default.
        }

        return new PaletteCollection { Palettes = [new Palette()] };
    }
}
