using System;
using AetherLove.Shared.Places;
using AetherLove.Shared.Profile.Enums;

namespace AetherLove.Services;

/// <summary>Snapshot of the player's in-game position; fields the game couldn't provide stay at their defaults.</summary>
public sealed record DetectedVenueLocation(
    string DataCenter,
    string World,
    Region Region,
    HousingDistrict District,
    short Ward,
    short Plot,
    short Room);

/// <summary>Reads the player's live location on demand to prefill the venue editor.</summary>
public static class VenueLocationDetector
{
    public static DetectedVenueLocation Detect()
    {
        var dataCenter = string.Empty;
        var world = string.Empty;
        var region = (Region)0;
        try
        {
            var worldId = UiHost.ObjectTable.LocalPlayer?.CurrentWorld.RowId ?? 0u;
            if (worldId > 0)
            {
                var worldSheet = UiHost.DataManager.GetExcelSheet<Lumina.Excel.Sheets.World>();
                var worldRow = worldSheet.GetRow(worldId);
                world = worldRow.Name.ExtractText();
                var dcSheet = UiHost.DataManager.GetExcelSheet<Lumina.Excel.Sheets.WorldDCGroupType>();
                var dcRow = dcSheet.GetRow(worldRow.DataCenter.RowId);
                dataCenter = dcRow.Name.ExtractText();
                region = RegionFromDcRowId(dcRow.Region.RowId);
            }
        }
        catch (Exception ex)
        {
            UiHost.Log.Debug(ex, "[VenueLocationDetector] World detect failed.");
        }

        var district = DistrictFromTerritory(UiHost.ClientState.TerritoryType);
        short ward = 0;
        short plot = 0;
        short room = 0;
        if (district != HousingDistrict.Unknown)
        {
            try
            {
                unsafe
                {
                    var hm = FFXIVClientStructs.FFXIV.Client.Game.HousingManager.Instance();
                    if (hm != null)
                    {
                        var wardIdx = hm->GetCurrentWard();
                        if (wardIdx >= 0 && wardIdx < 30)
                        {
                            ward = (short)(wardIdx + 1);
                        }
                        var plotIdx = hm->GetCurrentPlot();
                        if (plotIdx >= 0 && plotIdx < 60)
                        {
                            plot = (short)(plotIdx + 1);
                        }
                        var roomNo = hm->GetCurrentRoom();
                        if (roomNo > 0)
                        {
                            room = roomNo;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                UiHost.Log.Debug(ex, "[VenueLocationDetector] Housing detect failed.");
            }
        }

        return new DetectedVenueLocation(dataCenter, world, region, district, ward, plot, room);
    }

    /// <summary>The player's current physical-data-center region, or null when it can't be read.</summary>
    public static Region? DetectRegion()
    {
        try
        {
            var worldId = UiHost.ObjectTable.LocalPlayer?.CurrentWorld.RowId ?? 0u;
            if (worldId == 0)
            {
                return null;
            }
            var worldRow = UiHost.DataManager.GetExcelSheet<Lumina.Excel.Sheets.World>().GetRow(worldId);
            var dcRow = UiHost.DataManager.GetExcelSheet<Lumina.Excel.Sheets.WorldDCGroupType>()
                .GetRow(worldRow.DataCenter.RowId);
            var region = RegionFromDcRowId(dcRow.Region.RowId);
            return region == (Region)0 ? null : region;
        }
        catch (Exception ex)
        {
            UiHost.Log.Debug(ex, "[VenueLocationDetector] Region detect failed.");
            return null;
        }
    }

    /// <summary>Lumina WorldDCGroupType.Region ids: 1=JP, 2=NA, 3=EU, 4=OCE.</summary>
    private static Region RegionFromDcRowId(uint rowId) => rowId switch
    {
        1 => Region.Japan,
        2 => Region.NorthAmerica,
        3 => Region.Europe,
        4 => Region.Oceania,
        _ => (Region)0,
    };

    /// <summary>TerritoryType → residential district (exterior, interiors, chambers, apartment lobby).</summary>
    internal static HousingDistrict DistrictFromTerritory(uint territory) => territory switch
    {
        339 or 282 or 283 or 284 or 384 or 608 => HousingDistrict.Mist,
        340 or 342 or 343 or 344 or 385 or 609 => HousingDistrict.LavenderBeds,
        341 or 345 or 346 or 347 or 386 or 610 => HousingDistrict.Goblet,
        641 or 649 or 650 or 651 or 652 or 655 => HousingDistrict.Shirogane,
        979 or 980 or 981 or 982 or 983 or 999 => HousingDistrict.Empyreum,
        _ => HousingDistrict.Unknown,
    };
}
