using System;
using System.Numerics;
using System.Text;
using System.Text.Json;
using AetherLove.Shared.Places;
using Lumina.Excel.Sheets;

namespace AetherLove.Services;

/// <summary>The shared-location chat payload for the messenger. Only a message whose entire body is
/// "[location=...]" renders as a card. The location rides base64url JSON inside the token, so (like the
/// personal calendar share) it stays E2E between the participants and the server never sees it. IDs are stored
/// rather than names so the card localises the zone/district in the viewer's language and drops a map marker
/// at the exact spot the sharer stood.</summary>
public static class LocationShare
{
    public sealed record LocationCardData(
        uint TerritoryId,
        uint MapId,
        float MapX,
        float MapY,
        string DataCenter,
        string World,
        short District,
        short Ward,
        short Plot);

    private sealed record Json(uint Ter, uint Map, float X, float Y, string Dc, string W, short Di, short Wa, short Pl);

    /// <summary>Reads the player's live position into a shareable card, or null when it can't be read (not
    /// logged in, between areas).</summary>
    public static LocationCardData? Capture()
    {
        try
        {
            var player = UiHost.ObjectTable.LocalPlayer;
            if (player is null)
            {
                return null;
            }
            var loc = VenueLocationDetector.Detect();
            var territoryId = UiHost.ClientState.TerritoryType;

            uint mapId = 0;
            float mapX = 0f;
            float mapY = 0f;
            try
            {
                var terr = UiHost.DataManager.GetExcelSheet<TerritoryType>().GetRow(territoryId);
                mapId = terr.Map.RowId;
                var map = terr.Map.Value;
                var pos = player.Position;
                mapX = ToMapCoordinate(pos.X, map.SizeFactor, map.OffsetX);
                mapY = ToMapCoordinate(pos.Z, map.SizeFactor, map.OffsetY);
            }
            catch (Exception ex)
            {
                UiHost.Log.Debug(ex, "[LocationShare] map coordinate resolve failed.");
            }

            return new LocationCardData(territoryId, mapId, mapX, mapY,
                loc.DataCenter, loc.World, (short)loc.District, loc.Ward, loc.Plot);
        }
        catch (Exception ex)
        {
            UiHost.Log.Debug(ex, "[LocationShare] capture failed.");
            return null;
        }
    }

    public static string Compose(LocationCardData data)
    {
        var json = JsonSerializer.Serialize(new Json(data.TerritoryId, data.MapId, data.MapX, data.MapY,
            data.DataCenter, data.World, data.District, data.Ward, data.Plot));
        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"[location={b64}]";
    }

    public static bool TryParse(string text, out LocationCardData data)
    {
        data = null!;
        var s = text.Trim();
        if (s.Length < 12
            || !s.StartsWith("[location=", StringComparison.OrdinalIgnoreCase)
            || !s.EndsWith("]", StringComparison.Ordinal))
        {
            return false;
        }
        try
        {
            var b64 = s[10..^1].Replace('-', '+').Replace('_', '/');
            b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
            var j = JsonSerializer.Deserialize<Json>(Encoding.UTF8.GetString(Convert.FromBase64String(b64)));
            if (j is null)
            {
                return false;
            }
            data = new LocationCardData(j.Ter, j.Map, j.X, j.Y, j.Dc ?? "", j.W ?? "", j.Di, j.Wa, j.Pl);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>The zone/area name resolved in the VIEWER's game language (empty when unresolved).</summary>
    public static string ZoneName(uint territoryId)
    {
        try
        {
            return UiHost.DataManager.GetExcelSheet<TerritoryType>().GetRow(territoryId)
                .PlaceName.Value.Name.ExtractText();
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    /// <summary>The generic overload, not the boxing one: <see cref="HousingDistrict"/> is backed by
    /// <see cref="short"/>, so a boxed <see cref="int"/> throws rather than reporting undefined.</summary>
    public static HousingDistrict DistrictOf(short district) =>
        Enum.IsDefined((HousingDistrict)district) ? (HousingDistrict)district : HousingDistrict.Unknown;

    /// <summary>FFXIV world-to-map coordinate conversion (the 2-decimal in-game coords).</summary>
    private static float ToMapCoordinate(float world, ushort sizeFactor, short offset)
    {
        var scale = (sizeFactor == 0 ? 100 : sizeFactor) / 100f;
        var scaled = (world + offset) * scale;
        return 41f / scale * ((scaled + 1024f) / 2048f) + 1f;
    }
}
