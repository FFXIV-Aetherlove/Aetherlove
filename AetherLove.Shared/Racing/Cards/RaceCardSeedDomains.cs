using System;
using System.Globalization;
using System.Text;

namespace AetherLove.Shared.Racing.Cards;

/// <summary>The card resolver's own random stream: FNV-1a 32 over UTF-8 with the prototype's fixed mixer, and a unit
/// float from the hash. It is a separate stream from the engine's
/// <see cref="AetherRaceLive.Fnv1a32"/> (UTF-16) and from <c>RaceRng</c>; it never reads or advances either, so a
/// race with every hand empty steps exactly as a card-free one.</summary>
public static class RaceCardSeedDomains
{
    private const uint FnvOffset = 2166136261u;
    private const uint FnvPrime = 16777619u;
    private const uint MixA = 0x7feb352du;
    private const uint MixB = 0x846ca68bu;
    private const float UnitScale = 1f / 16777216f;

    public const string ThresholdSuffix = "|threshold";
    public const string CadenceSuffix = "|cadence";

    public static uint Hash(string domain)
    {
        var hash = FnvOffset;
        foreach (var b in Encoding.UTF8.GetBytes(domain))
        {
            hash = unchecked((hash ^ b) * FnvPrime);
        }

        hash ^= hash >> 16;
        hash = unchecked(hash * MixA);
        hash ^= hash >> 15;
        hash = unchecked(hash * MixB);
        return hash ^ (hash >> 16);
    }

    /// <summary>The hash's top 24 bits as a float in [0, 1), exact in single precision.</summary>
    public static float Unit(uint value) => (value >> 8) * UnitScale;

    /// <summary>The Gold controller's domain: adapter version, support version, race seed, runner slot, the Gold's
    /// id and its effect version. The runner's name is deliberately not part of it.</summary>
    public static string GoldDomain(string supportVersion, int seed, int slot, string goldId, int effectVersion)
    {
        return string.Join('|', RaceCardVersions.AdapterVersion, supportVersion, Invariant(seed), Invariant(slot), goldId, Invariant(effectVersion));
    }

    private static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);
}
