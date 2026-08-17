using System.Collections.Generic;
using System.Numerics;
using AetherLove.Shared.Aetherling;

namespace AetherOS.Apps.Aetherling.Engine;

/// <summary>The six elements as the client draws them: wire value, key, accent colour from the
/// game's own element colour language, a step below neon. Keys match the store's crystal refs
/// (crystal-fire) and the server's reaction refs (reaction-fire).</summary>
public static class Elements
{
    public readonly record struct ElementDef(AetherlingElement Value, string Key, Vector4 Accent);

    public static readonly IReadOnlyList<ElementDef> All =
    [
        new(AetherlingElement.Fire, "fire", new Vector4(0.95f, 0.48f, 0.30f, 1f)),
        new(AetherlingElement.Ice, "ice", new Vector4(0.62f, 0.86f, 0.94f, 1f)),
        new(AetherlingElement.Wind, "wind", new Vector4(0.60f, 0.86f, 0.52f, 1f)),
        new(AetherlingElement.Earth, "earth", new Vector4(0.88f, 0.72f, 0.38f, 1f)),
        new(AetherlingElement.Lightning, "lightning", new Vector4(0.73f, 0.62f, 0.95f, 1f)),
        new(AetherlingElement.Water, "water", new Vector4(0.45f, 0.68f, 0.90f, 1f)),
    ];

    public static ElementDef? Find(short value)
    {
        foreach (var element in All)
        {
            if ((short)element.Value == value)
            {
                return element;
            }
        }

        return null;
    }

    public static string CrystalRef(ElementDef element) => $"crystal-{element.Key}";

    /// <summary>Loc key suffix for the element's name; the app pack carries all six.</summary>
    public static string NameKey(ElementDef element) => $"os.aetherling_element_{element.Key}";
}
