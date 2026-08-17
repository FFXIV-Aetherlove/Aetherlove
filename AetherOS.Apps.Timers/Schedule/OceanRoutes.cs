using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace AetherOS.Apps.Timers.Schedule;

public enum VoyageTime
{
    Day = 0,
    Sunset = 1,
    Night = 2,
}

/// <summary>Resolves upcoming Ocean Fishing voyages from the game's own IKD sheets. Any sheet failure
/// yields an empty list rather than throwing.</summary>
public static class OceanRoutes
{
    public sealed record Stop(string SpotName, VoyageTime Time);

    public sealed record Voyage(DateTime DepartureUtc, string RouteName, IReadOnlyList<Stop> Stops);

    private const int VoyageCacheCap = 32;
    private const int VoyageIntervalHours = 2;

    private static readonly object _cacheLock = new();
    private static readonly Dictionary<long, Voyage> _voyageCache = new();
    private static List<uint>? _tableRouteIds;

    public static IReadOnlyList<Voyage> Upcoming(IDataManager data, DateTime utcNow, int count)
    {
        var result = new List<Voyage>();
        if (count <= 0)
        {
            return result;
        }
        try
        {
            lock (_cacheLock)
            {
                _tableRouteIds ??= LoadTable(data);
                if (_tableRouteIds.Count == 0)
                {
                    return result;
                }
                var (firstDeparture, _, _) = EorzeaSchedule.NextVoyage(utcNow);
                for (var i = 0; i < count; i++)
                {
                    var departure = firstDeparture.AddHours(i * VoyageIntervalHours);
                    var voyageIndex = EorzeaSchedule.VoyageIndex(departure);
                    if (!_voyageCache.TryGetValue(voyageIndex, out var voyage))
                    {
                        voyage = ResolveVoyage(data, _tableRouteIds, departure, voyageIndex);
                        if (voyage == null)
                        {
                            continue;
                        }
                        if (_voyageCache.Count >= VoyageCacheCap)
                        {
                            _voyageCache.Clear();
                        }
                        _voyageCache[voyageIndex] = voyage;
                    }
                    result.Add(voyage);
                }
            }
            return result;
        }
        catch
        {
            return new List<Voyage>();
        }
    }

    private static List<uint> LoadTable(IDataManager data)
    {
        var list = new List<uint>();
        foreach (var row in data.GetExcelSheet<IKDRouteTable>())
        {
            if (row.Route.RowId != 0 && row.Route.IsValid)
            {
                list.Add(row.Route.RowId);
            }
        }
        return list;
    }

    private static Voyage? ResolveVoyage(IDataManager data, List<uint> routeIds, DateTime departureUtc, long voyageIndex)
    {
        var tableIndex = EorzeaSchedule.RouteTableIndex(voyageIndex, routeIds.Count);
        if (!data.GetExcelSheet<IKDRoute>().TryGetRow(routeIds[tableIndex], out var route))
        {
            return null;
        }
        var stops = new List<Stop>();
        for (var i = 0; i < route.Spot.Count; i++)
        {
            var spotRef = route.Spot[i];
            if (spotRef.RowId == 0)
            {
                continue;
            }
            var spot = spotRef.ValueNullable;
            if (spot == null)
            {
                continue;
            }
            var name = spot.Value.PlaceName.ValueNullable?.Name.ExtractText() ?? string.Empty;
            var time = VoyageTime.Day;
            if (i < route.Time.Count)
            {
                time = MapTime(route.Time[i].ValueNullable?.TimeOfDay ?? 0);
            }
            stops.Add(new Stop(name, time));
        }
        if (stops.Count == 0)
        {
            return null;
        }
        return new Voyage(departureUtc, stops[^1].SpotName, stops);
    }

    // IKDTimeDefine rows 1..3 carry day/sunset/night; row 0 is the sheet's blank row.
    private static VoyageTime MapTime(byte timeOfDay)
    {
        return timeOfDay switch
        {
            2 => VoyageTime.Sunset,
            3 => VoyageTime.Night,
            _ => VoyageTime.Day,
        };
    }
}
