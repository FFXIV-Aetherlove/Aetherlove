using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AetherOS.Apps.Photos;

namespace AetherLove.Os;

/// <summary>The Photos app's on-disk library: images copied under the plugin config directory with a
/// JSON manifest for albums and display names. Purely local.</summary>
public sealed class PhotoLibraryService : IPhotoLibrary
{
    private sealed class Manifest
    {
        public List<AlbumEntry> Albums { get; set; } = new();
        public List<PhotoEntry> Photos { get; set; } = new();

        /// <summary>Identity of the camera album, stable across user renames.</summary>
        public string? CameraAlbumId { get; set; }
    }

    private sealed class AlbumEntry
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";

        /// <summary>On-disk subdirectory holding this album's photos, derived from the name.</summary>
        public string Dir { get; set; } = "";
    }

    private sealed class PhotoEntry
    {
        public string Id { get; set; } = "";
        public string AlbumId { get; set; } = "";
        public string Name { get; set; } = "";
        public string FileName { get; set; } = "";
        public DateTime AddedAtUtc { get; set; }
        public string? Location { get; set; }
    }

    private Manifest? _manifest;

    private static readonly string[] AllowedExtensions = [".png", ".jpg", ".jpeg", ".bmp"];

    private readonly string _libraryDir;

    public PhotoLibraryService(AppStorageService storage)
    {
        _libraryDir = storage.For("photos").Directory;
    }

    private string ManifestPath => Path.Combine(_libraryDir, "manifest.json");

    private Manifest Data
    {
        get
        {
            if (_manifest != null)
            {
                return _manifest;
            }
            try
            {
                _manifest = File.Exists(ManifestPath)
                    ? JsonSerializer.Deserialize<Manifest>(File.ReadAllText(ManifestPath)) ?? new Manifest()
                    : new Manifest();
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "[Photos] Failed to read the library manifest.");
                _manifest = new Manifest();
            }
            if (_manifest.Albums.Count == 0)
            {
                _manifest.Albums.Add(new AlbumEntry { Id = Guid.NewGuid().ToString("N"), Name = "Camera" });
                Save();
            }
            EnsureDirLayout(_manifest);
            return _manifest;
        }
    }

    /// <summary>Albums mirror onto disk as subfolders named after them. Pre-folder libraries stored every file
    /// flat in the root, so assign each album a folder once and move its files in.</summary>
    private void EnsureDirLayout(Manifest manifest)
    {
        var changed = false;
        foreach (var album in manifest.Albums)
        {
            if (album.Dir.Length > 0)
            {
                continue;
            }
            album.Dir = SafeDirName(manifest, album.Name);
            changed = true;
            try
            {
                Directory.CreateDirectory(Path.Combine(_libraryDir, album.Dir));
                foreach (var photo in manifest.Photos.Where(p => p.AlbumId == album.Id))
                {
                    var flat = Path.Combine(_libraryDir, photo.FileName);
                    if (File.Exists(flat))
                    {
                        File.Move(flat, Path.Combine(_libraryDir, album.Dir, photo.FileName), overwrite: true);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "[Photos] Failed to move an album onto disk. Album={Album}", album.Name);
            }
        }
        if (changed)
        {
            Save();
        }
    }

    private static readonly HashSet<string> ReservedDirNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>A filesystem-safe folder name for an album, unique among the other albums' folders.</summary>
    private static string SafeDirName(Manifest manifest, string name, string? excludeAlbumId = null)
    {
        var cleaned = new string(name.Trim().Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray()).Trim().TrimEnd('.');
        if (cleaned.Length > 64)
        {
            cleaned = cleaned[..64].TrimEnd('.', ' ');
        }
        if (cleaned.Length == 0)
        {
            cleaned = "Album";
        }
        // Windows device names are reserved even with an extension ("con", "com1.foo").
        if (ReservedDirNames.Contains(cleaned.Split('.')[0].TrimEnd()))
        {
            cleaned = $"{cleaned} (album)";
        }
        var taken = manifest.Albums
            .Where(a => a.Id != excludeAlbumId)
            .Select(a => a.Dir)
            .Where(d => d.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidate = cleaned;
        for (var i = 2; taken.Contains(candidate); i++)
        {
            candidate = $"{cleaned} ({i})";
        }
        return candidate;
    }

    private AlbumEntry? AlbumOf(string albumId) => Data.Albums.FirstOrDefault(a => a.Id == albumId);

    /// <summary>Absolute path of the album's folder on disk (created if missing), or null for an unknown album.</summary>
    public string? AlbumFolder(string albumId)
    {
        var album = AlbumOf(albumId);
        if (album == null)
        {
            return null;
        }
        var dir = Path.Combine(_libraryDir, album.Dir);
        try
        {
            Directory.CreateDirectory(dir);
        }
        catch (IOException)
        {
        }
        return dir;
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(_libraryDir);
            File.WriteAllText(ManifestPath, JsonSerializer.Serialize(_manifest));
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[Photos] Failed to save the library manifest.");
        }
    }

    public IReadOnlyList<PhotoAlbum> Albums =>
        Data.Albums.Select(a =>
        {
            var photos = Data.Photos.Where(p => p.AlbumId == a.Id).OrderByDescending(p => p.AddedAtUtc).ToArray();
            var cover = photos.FirstOrDefault();
            return new PhotoAlbum(a.Id, a.Name, photos.Length, cover == null ? null : PathOf(cover));
        }).ToArray();

    public IReadOnlyList<PhotoItem> Photos(string? albumId) =>
        Data.Photos
            .Where(p => albumId == null || p.AlbumId == albumId)
            .OrderByDescending(p => p.AddedAtUtc)
            .Select(ToItem)
            .ToArray();

    public PhotoItem? Photo(string photoId)
    {
        var entry = Data.Photos.FirstOrDefault(p => p.Id == photoId);
        return entry == null ? null : ToItem(entry);
    }

    public string CreateAlbum(string name)
    {
        var entry = new AlbumEntry { Id = Guid.NewGuid().ToString("N"), Name = name.Trim().Length > 0 ? name.Trim() : "Album" };
        entry.Dir = SafeDirName(Data, entry.Name);
        Data.Albums.Add(entry);
        try
        {
            Directory.CreateDirectory(Path.Combine(_libraryDir, entry.Dir));
        }
        catch (IOException)
        {
        }
        Save();
        return entry.Id;
    }

    public void RenameAlbum(string albumId, string name)
    {
        var album = AlbumOf(albumId);
        if (album == null || name.Trim().Length == 0)
        {
            return;
        }
        album.Name = name.Trim();
        var newDir = SafeDirName(Data, album.Name, excludeAlbumId: albumId);
        if (!string.Equals(newDir, album.Dir, StringComparison.OrdinalIgnoreCase))
        {
            // Keep the old folder when the OS refuses the move (e.g. a file inside is open elsewhere).
            try
            {
                Directory.Move(Path.Combine(_libraryDir, album.Dir), Path.Combine(_libraryDir, newDir));
                album.Dir = newDir;
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "[Photos] Could not rename the album folder on disk.");
            }
        }
        Save();
    }

    public void DeleteAlbum(string albumId)
    {
        var album = AlbumOf(albumId);
        foreach (var photo in Data.Photos.Where(p => p.AlbumId == albumId).ToArray())
        {
            DeletePhotoFile(photo);
            Data.Photos.Remove(photo);
        }
        Data.Albums.RemoveAll(a => a.Id == albumId);
        if (album != null && album.Dir.Length > 0)
        {
            try
            {
                var dir = Path.Combine(_libraryDir, album.Dir);
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
            catch (IOException)
            {
            }
        }
        if (Data.Albums.Count == 0)
        {
            Data.Albums.Add(new AlbumEntry { Id = Guid.NewGuid().ToString("N"), Name = "Camera" });
        }
        Save();
    }

    public void AddPhoto(string albumId, string sourcePath, string? displayName)
    {
        var name = string.IsNullOrWhiteSpace(displayName) ? Path.GetFileNameWithoutExtension(sourcePath) : displayName;
        AddFile(albumId, sourcePath, name, DateTime.UtcNow, location: null);
    }

    /// <summary>The camera album, found by its remembered id (renames keep it) or created on first use.</summary>
    public string EnsureCameraAlbum()
    {
        var id = Data.CameraAlbumId;
        if (id is { Length: > 0 } && Data.Albums.Any(a => a.Id == id))
        {
            return id;
        }
        var camera = Data.Albums.FirstOrDefault(a => a.Name == "Camera") ?? Data.Albums.First();
        Data.CameraAlbumId = camera.Id;
        Save();
        return camera.Id;
    }

    /// <summary>Stores a camera capture in the camera album, stamped with capture time and in-game location.</summary>
    public PhotoItem? ImportCapture(string sourcePath, DateTime takenAtUtc, string? location)
    {
        var entry = AddFile(EnsureCameraAlbum(), sourcePath, Path.GetFileNameWithoutExtension(sourcePath),
            takenAtUtc, location);
        return entry == null ? null : ToItem(entry);
    }

    public void RenamePhoto(string photoId, string name)
    {
        var photo = Data.Photos.FirstOrDefault(p => p.Id == photoId);
        if (photo == null || name.Trim().Length == 0)
        {
            return;
        }
        photo.Name = name.Trim();
        Save();
    }

    public void MovePhoto(string photoId, string albumId)
    {
        var photo = Data.Photos.FirstOrDefault(p => p.Id == photoId);
        var target = AlbumOf(albumId);
        if (photo == null || target == null || photo.AlbumId == albumId)
        {
            return;
        }
        try
        {
            var source = PathOf(photo);
            var destDir = Path.Combine(_libraryDir, target.Dir);
            Directory.CreateDirectory(destDir);
            if (File.Exists(source))
            {
                File.Move(source, Path.Combine(destDir, photo.FileName), overwrite: true);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[Photos] Could not move a photo file between album folders.");
            return;
        }
        photo.AlbumId = albumId;
        Save();
    }

    public void DeletePhoto(string photoId)
    {
        var photo = Data.Photos.FirstOrDefault(p => p.Id == photoId);
        if (photo == null)
        {
            return;
        }
        DeletePhotoFile(photo);
        Data.Photos.Remove(photo);
        Save();
    }

    private PhotoEntry? AddFile(string albumId, string sourcePath, string displayName, DateTime addedAtUtc, string? location)
    {
        try
        {
            var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
            if (!AllowedExtensions.Contains(ext) || !File.Exists(sourcePath))
            {
                return null;
            }
            var id = Guid.NewGuid().ToString("N");
            var destDir = Path.Combine(_libraryDir, AlbumOf(albumId)?.Dir ?? "");
            Directory.CreateDirectory(destDir);
            File.Copy(sourcePath, Path.Combine(destDir, id + ext), overwrite: true);
            var entry = new PhotoEntry
            {
                Id = id,
                AlbumId = albumId,
                Name = displayName,
                FileName = id + ext,
                AddedAtUtc = addedAtUtc,
                Location = location,
            };
            Data.Photos.Add(entry);
            Save();
            return entry;
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[Photos] Failed to import an image.");
            return null;
        }
    }

    private void DeletePhotoFile(PhotoEntry photo)
    {
        try
        {
            var path = Path.Combine(_libraryDir, photo.FileName);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }

    private string PathOf(PhotoEntry photo) =>
        Path.Combine(_libraryDir, _manifest?.Albums.FirstOrDefault(a => a.Id == photo.AlbumId)?.Dir ?? "", photo.FileName);

    private PhotoItem ToItem(PhotoEntry p) => new(p.Id, p.AlbumId, p.Name, PathOf(p), p.AddedAtUtc, p.Location);
}
