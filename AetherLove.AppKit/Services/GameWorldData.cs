using System;
using System.Collections.Generic;
using AetherLove.Shared.Profile.Enums;
using Lumina.Excel.Sheets;

namespace AetherLove.Services;

/// <summary>Public FFXIV datacenters (grouped by region) and worlds (grouped by datacenter), read once from
/// the game's Excel sheets to drive the venue location dropdowns.</summary>
public static class GameWorldData
{
    private static readonly Dictionary<Region, List<string>> DcsByRegion = new();
    private static readonly Dictionary<string, List<string>> WorldsByDc = new(StringComparer.Ordinal);
    private static bool _built;

    /// <summary>Datacenter names for a region (sorted), or empty if unknown.</summary>
    public static IReadOnlyList<string> DataCenters(Region region)
    {
        EnsureBuilt();
        return DcsByRegion.TryGetValue(region, out var list) ? list : [];
    }

    /// <summary>World names on a datacenter (sorted), or empty if unknown.</summary>
    public static IReadOnlyList<string> Worlds(string dataCenter)
    {
        EnsureBuilt();
        return WorldsByDc.TryGetValue(dataCenter, out var list) ? list : [];
    }

    /// <summary>The region a datacenter belongs to, or null if unknown.</summary>
    public static Region? RegionOfDataCenter(string dataCenter)
    {
        EnsureBuilt();
        foreach (var (region, dcs) in DcsByRegion)
        {
            if (dcs.Contains(dataCenter))
            {
                return region;
            }
        }
        return null;
    }

    // Lumina WorldDCGroupType.Region: 1=JP, 2=NA, 3=EU, 4=OCE.
    private static Region? MapRegion(uint regionId) => regionId switch
    {
        1 => Region.Japan,
        2 => Region.NorthAmerica,
        3 => Region.Europe,
        4 => Region.Oceania,
        _ => null,
    };

    private static void EnsureBuilt()
    {
        if (_built)
        {
            return;
        }
        _built = true;
        try
        {
            var dcNames = new Dictionary<uint, string>();
            foreach (var dc in UiHost.DataManager.GetExcelSheet<WorldDCGroupType>())
            {
                var name = dc.Name.ExtractText();
                var region = MapRegion(dc.Region.RowId);
                if (string.IsNullOrEmpty(name) || region is null)
                {
                    continue;
                }
                dcNames[dc.RowId] = name;
                if (!DcsByRegion.TryGetValue(region.Value, out var dcList))
                {
                    dcList = [];
                    DcsByRegion[region.Value] = dcList;
                }
                dcList.Add(name);
                WorldsByDc[name] = [];
            }

            foreach (var world in UiHost.DataManager.GetExcelSheet<World>())
            {
                if (!world.IsPublic)
                {
                    continue;
                }
                var name = world.Name.ExtractText();
                if (name.Length > 0 && dcNames.TryGetValue(world.DataCenter.RowId, out var dcName))
                {
                    WorldsByDc[dcName].Add(name);
                }
            }

            foreach (var list in DcsByRegion.Values)
            {
                list.Sort(StringComparer.OrdinalIgnoreCase);
            }
            foreach (var list in WorldsByDc.Values)
            {
                list.Sort(StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex)
        {
            UiHost.Log.Warning(ex, "[GameWorldData] Failed to read world/DC sheets.");
        }
    }
}
