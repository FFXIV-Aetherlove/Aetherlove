using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AetherOS.Apps.Aetherling.Engine;

namespace AetherOS.Apps.Aetherling.Rendering;

/// <summary>The wearable catalogue: accessory defs, palettes and the map from store ItemRefs to
/// them. The store's ref is the one canonical key everywhere (the look, the grants, the shelf);
/// the defs carry display names, so this is where the two vocabularies meet. A def without a ref
/// mapping never surfaces, which is what keeps a stray asset from becoming an unownable item.</summary>
internal sealed class PetCatalogue
{
    private readonly Dictionary<string, AccessoryDef> _byRef = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _refByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Palette> _paletteByRef = new(StringComparer.OrdinalIgnoreCase);

    private PetCatalogue(string root)
    {
        AccessoryDirectory = Path.Combine(root, CoreAssets.AccessoryFolder);
        foreach (var def in AccessoryDef.LoadAll(AccessoryDirectory))
        {
            var itemRef = RefFor(def);
            if (itemRef.Length == 0 || _byRef.ContainsKey(itemRef))
            {
                continue;
            }
            _byRef[itemRef] = def;
            _refByName[def.Name] = itemRef;
        }

        Palettes = PaletteCollection.Load(Path.Combine(root, CoreAssets.PaletteFile)).Palettes;
        foreach (var palette in Palettes)
        {
            _paletteByRef[Slugify(palette.Name)] = palette;
        }
    }

    public string AccessoryDirectory { get; }

    public IReadOnlyList<Palette> Palettes { get; }

    public IReadOnlyCollection<AccessoryDef> Accessories => _byRef.Values;

    public AccessoryDef? Accessory(string itemRef) =>
        _byRef.TryGetValue(itemRef, out var def) ? def : null;

    public string? RefOf(AccessoryDef def) =>
        _refByName.TryGetValue(def.Name, out var itemRef) ? itemRef : null;

    /// <summary>The palette a store ref names; unknown refs fall back to the free base.</summary>
    public Palette PaletteByRef(string itemRef)
    {
        if (itemRef.Length > 0 && _paletteByRef.TryGetValue(itemRef, out var palette))
        {
            return palette;
        }
        return Palettes.FirstOrDefault(p => p.Name == "Dawn") ?? Palettes[0];
    }

    public string AccessoryImagePath(AccessoryDef def) =>
        Path.GetFullPath(Path.Combine(AccessoryDirectory, def.File));

    /// <summary>A code-drawn part's row thumbnail: <c>acc/thumbs/&lt;ref&gt;.png</c>, rendered by the vault's
    /// store_render tool. Every other item's thumbnail is its own sprite, and a part has none.</summary>
    public string AccessoryThumbPath(AccessoryDef def) =>
        RefOf(def) is { Length: > 0 } itemRef
            ? Path.GetFullPath(Path.Combine(AccessoryDirectory, "thumbs", itemRef + ".png"))
            : string.Empty;

    /// <summary>The far half of a wrap item, on the same terms: empty when there is none.</summary>
    public string AccessoryBackPath(AccessoryDef def) =>
        def.Back.Length == 0 ? string.Empty : Path.GetFullPath(Path.Combine(AccessoryDirectory, def.Back));

    public static PetCatalogue? Load()
    {
        var root = CoreAssets.ResolveRoot();
        if (root is null)
        {
            return null;
        }
        try
        {
            return new PetCatalogue(root);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>A palette or worn accessory's store ref is its slugified name; the arms carry
    /// job-coded refs the seed invented, resolved through the table below.</summary>
    private static string RefFor(AccessoryDef def)
    {
        if (def.Slot == AccessoryDef.ArmsSlot)
        {
            return ArmsRefs.TryGetValue(def.Name, out var armRef) ? armRef : "";
        }
        return Slugify(def.Name);
    }

    private static string Slugify(string name)
    {
        var chars = new List<char>(name.Length);
        foreach (var c in name.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                chars.Add(c);
            }
            else if (c is ' ' or '-')
            {
                if (chars.Count > 0 && chars[^1] != '-')
                {
                    chars.Add('-');
                }
            }
        }
        while (chars.Count > 0 && chars[^1] == '-')
        {
            chars.RemoveAt(chars.Count - 1);
        }
        return new string(chars.ToArray());
    }

    /// <summary>Arms def name to store ref, the client twin of the server's seed table. The two
    /// must agree ref for ref or an owned weapon renders as nothing.</summary>
    private static readonly Dictionary<string, string> ArmsRefs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Paladin's Sword"] = "arm-pld-sword",
        ["Paladin's Shield"] = "arm-pld-shield",
        ["Warrior's Axe"] = "arm-war",
        ["Dark Knight's Greatsword"] = "arm-drk",
        ["Gunbreaker's Gunblade"] = "arm-gnb",
        ["White Mage's Staff"] = "arm-whm",
        ["Scholar's Codex"] = "arm-sch",
        ["Astrologian's Star Globe"] = "arm-ast",
        ["Sage's Nouliths"] = "arm-sge",
        ["Monk's Fist Weapon"] = "arm-mnk-fist",
        ["Monk's Off-hand Fist"] = "arm-mnk-offfist",
        ["Dragoon's Lance"] = "arm-drg",
        ["Ninja's Dagger"] = "arm-nin-dagger",
        ["Ninja's Off-hand Dagger"] = "arm-nin-offdagger",
        ["Samurai's Katana"] = "arm-sam",
        ["Reaper's Scythe"] = "arm-rpr",
        ["Viper's Twinblade"] = "arm-vpr-blade",
        ["Viper's Off-hand Twinblade"] = "arm-vpr-offblade",
        ["Bard's Bow"] = "arm-brd",
        ["Machinist's Firearm"] = "arm-mch",
        ["Dancer's Chakram"] = "arm-dnc-chakram",
        ["Dancer's Off-hand Chakram"] = "arm-dnc-offchakram",
        ["Black Mage's Rod"] = "arm-blm",
        ["Summoner's Grimoire"] = "arm-smn",
        ["Red Mage's Rapier"] = "arm-rdm-rapier",
        ["Red Mage's Focus"] = "arm-rdm-focus",
        ["Pictomancer's Brush"] = "arm-pct-brush",
        ["Pictomancer's Palette"] = "arm-pct-palette",
        ["Blue Mage's Cane"] = "arm-blu",
        ["Miner's Pickaxe"] = "arm-min",
        ["Botanist's Hatchet"] = "arm-btn-hatchet",
        ["Botanist's Scythe"] = "arm-btn-scythe",
        ["Botanist's Grass Scythe"] = "arm-btn-scythe",
        ["Fisher's Fishing Rod"] = "arm-fsh",
        ["Carpenter's Saw"] = "arm-crp",
        ["Blacksmith's Cross-pein Hammer"] = "arm-bsm",
        ["Armorer's Raising Hammer"] = "arm-arm",
        ["Goldsmith's Mallet"] = "arm-gsm",
        ["Leatherworker's Round Knife"] = "arm-ltw",
        ["Weaver's Needle"] = "arm-wvr",
        ["Alchemist's Alembic"] = "arm-alc",
        ["Culinarian's Frypan"] = "arm-cul",
        ["Watermelon Wedge"] = "arm-melon",
        ["Evercold Shaved Ice"] = "arm-shavedice",
    };

    /// <summary>Job abbreviation to the arm refs it carries, the client twin of the server's
    /// JobArms (used by arms-follow-job to pick what to equip from what is owned).</summary>
    public static readonly IReadOnlyDictionary<string, string[]> JobArms =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["pld"] = ["arm-pld-sword", "arm-pld-shield"],
            ["war"] = ["arm-war"],
            ["drk"] = ["arm-drk"],
            ["gnb"] = ["arm-gnb"],
            ["whm"] = ["arm-whm"],
            ["sch"] = ["arm-sch"],
            ["ast"] = ["arm-ast"],
            ["sge"] = ["arm-sge"],
            ["mnk"] = ["arm-mnk-fist", "arm-mnk-offfist"],
            ["drg"] = ["arm-drg"],
            ["nin"] = ["arm-nin-dagger", "arm-nin-offdagger"],
            ["sam"] = ["arm-sam"],
            ["rpr"] = ["arm-rpr"],
            ["vpr"] = ["arm-vpr-blade", "arm-vpr-offblade"],
            ["brd"] = ["arm-brd"],
            ["mch"] = ["arm-mch"],
            ["dnc"] = ["arm-dnc-chakram", "arm-dnc-offchakram"],
            ["blm"] = ["arm-blm"],
            ["smn"] = ["arm-smn"],
            ["rdm"] = ["arm-rdm-rapier", "arm-rdm-focus"],
            ["pct"] = ["arm-pct-brush", "arm-pct-palette"],
            ["blu"] = ["arm-blu"],
            ["min"] = ["arm-min"],
            ["btn"] = ["arm-btn-hatchet"],
            ["fsh"] = ["arm-fsh"],
            ["crp"] = ["arm-crp"],
            ["bsm"] = ["arm-bsm"],
            ["arm"] = ["arm-arm"],
            ["gsm"] = ["arm-gsm"],
            ["ltw"] = ["arm-ltw"],
            ["wvr"] = ["arm-wvr"],
            ["alc"] = ["arm-alc"],
            ["cul"] = ["arm-cul"],
        };
}
