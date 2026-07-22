using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using AetherOS.Apps.Camera;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace AetherLove.Os;

/// <summary>Camera captures stored in the Photos library's camera album: the framed crop is baked into the
/// stored copy, stamped with capture time and the in-game location. Also migrates the pre-2.0 private camera
/// roll into the album on first use.</summary>
public sealed class CameraLibraryService : ICameraLibrary
{
    private sealed class LegacyRollEntry
    {
        public string File { get; set; } = "";
        public float CropX { get; set; }
        public float CropY { get; set; }
        public float CropW { get; set; }
        public float CropH { get; set; }
        public DateTime TakenAtUtc { get; set; }
    }

    private readonly PhotoLibraryService _library;
    private readonly AppStorageService _storage;
    private bool _migrated;

    public CameraLibraryService(PhotoLibraryService library, AppStorageService storage)
    {
        _library = library;
        _storage = storage;
    }

    public IReadOnlyList<CameraPhoto> Photos
    {
        get
        {
            EnsureMigrated();
            return _library.Photos(_library.EnsureCameraAlbum())
                .Select(p => new CameraPhoto(p.Id, p.Path, p.AddedAtUtc, p.Location))
                .ToArray();
        }
    }

    public CameraPhoto? AddCapture(string sourcePath, Vector4 crop)
    {
        EnsureMigrated();
        var item = Import(sourcePath, crop, DateTime.UtcNow, CurrentLocation());
        return item == null ? null : new CameraPhoto(item.Id, item.Path, item.AddedAtUtc, item.Location);
    }

    public void Delete(string photoId) => _library.DeletePhoto(photoId);

    private AetherOS.Apps.Photos.PhotoItem? Import(string sourcePath, Vector4 crop, DateTime takenAtUtc, string? location)
    {
        try
        {
            var temp = Path.Combine(Path.GetTempPath(), $"aethercam_{Guid.NewGuid():N}.png");
            using (var image = SixLabors.ImageSharp.Image.Load(sourcePath))
            {
                if (crop.Z >= 1f && crop.W >= 1f)
                {
                    var x = (int)Math.Clamp(crop.X, 0f, image.Width - 1f);
                    var y = (int)Math.Clamp(crop.Y, 0f, image.Height - 1f);
                    var w = (int)Math.Clamp(crop.Z, 1f, image.Width - x);
                    var h = (int)Math.Clamp(crop.W, 1f, image.Height - y);
                    if (w < image.Width || h < image.Height)
                    {
                        image.Mutate(c => c.Crop(new Rectangle(x, y, w, h)));
                    }
                }
                image.SaveAsPng(temp);
            }
            var item = _library.ImportCapture(temp, takenAtUtc, location);
            File.Delete(temp);
            return item;
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[CameraLibrary] Failed to store a capture.");
            return null;
        }
    }

    /// <summary>Zone name plus world, e.g. "Mist, Balmung"; null when not in game.</summary>
    private static string? CurrentLocation()
    {
        try
        {
            var territoryId = UiHost.ClientState.TerritoryType;
            if (territoryId == 0)
            {
                return null;
            }
            var zone = UiHost.DataManager.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>()
                .GetRow(territoryId).PlaceName.Value.Name.ExtractText();
            var worldId = UiHost.ObjectTable.LocalPlayer?.CurrentWorld.RowId ?? 0u;
            var world = worldId > 0
                ? UiHost.DataManager.GetExcelSheet<Lumina.Excel.Sheets.World>().GetRow(worldId).Name.ExtractText()
                : string.Empty;
            if (zone.Length == 0)
            {
                return world.Length == 0 ? null : world;
            }
            return world.Length == 0 ? zone : $"{zone}, {world}";
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void EnsureMigrated()
    {
        if (_migrated)
        {
            return;
        }
        _migrated = true;
        try
        {
            var storage = _storage.For("camera");
            var roll = storage.Get<List<LegacyRollEntry>>("roll");
            if (roll is not { Count: > 0 })
            {
                return;
            }
            foreach (var entry in Enumerable.Reverse(roll))
            {
                var path = Path.Combine(storage.Directory, entry.File);
                if (!File.Exists(path))
                {
                    continue;
                }
                Import(path, new Vector4(entry.CropX, entry.CropY, entry.CropW, entry.CropH), entry.TakenAtUtc, null);
                File.Delete(path);
            }
            storage.Set("roll", new List<LegacyRollEntry>());
            Plugin.Log.Information("[CameraLibrary] Migrated {Count} camera-roll shot(s) into the photo library.", roll.Count);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[CameraLibrary] Camera-roll migration failed.");
        }
    }
}
