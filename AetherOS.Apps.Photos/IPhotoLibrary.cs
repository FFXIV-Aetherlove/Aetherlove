using System;
using System.Collections.Generic;

namespace AetherOS.Apps.Photos;

public sealed record PhotoAlbum(string Id, string Name, int Count, string? CoverPath);

public sealed record PhotoItem(string Id, string AlbumId, string Name, string Path, DateTime AddedAtUtc,
    string? Location = null);

/// <summary>Host bridge: the on-disk photo library. Everything is local; nothing touches the server. Picking
/// a file, capturing a selfie, and loading textures come from the shared app capabilities; the picked path is
/// handed back to <see cref="AddPhoto"/>.</summary>
public interface IPhotoLibrary
{
    IReadOnlyList<PhotoAlbum> Albums { get; }

    /// <summary>Photos in an album, newest first. Pass null for all photos.</summary>
    IReadOnlyList<PhotoItem> Photos(string? albumId);

    PhotoItem? Photo(string photoId);

    string CreateAlbum(string name);
    void RenameAlbum(string albumId, string name);

    /// <summary>Deletes the album and every photo in it.</summary>
    void DeleteAlbum(string albumId);

    /// <summary>Copies a picked or captured image into the album. A blank <paramref name="displayName"/>
    /// falls back to the source file name.</summary>
    void AddPhoto(string albumId, string sourcePath, string? displayName);

    void RenamePhoto(string photoId, string name);

    /// <summary>Moves a photo into another album; no-op when either side is missing.</summary>
    void MovePhoto(string photoId, string albumId);

    void DeletePhoto(string photoId);

    /// <summary>Absolute path of the album's folder on disk, or null for an unknown album. Albums mirror onto
    /// disk as subfolders named after them.</summary>
    string? AlbumFolder(string albumId);

    /// <summary>Absolute path of the library itself, the folder every album sits in.</summary>
    string LibraryFolder { get; }
}
