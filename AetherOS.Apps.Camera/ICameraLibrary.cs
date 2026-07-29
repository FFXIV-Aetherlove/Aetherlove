using System;
using System.Collections.Generic;
using System.Numerics;

namespace AetherOS.Apps.Camera;

public sealed record CameraPhoto(string Id, string Path, DateTime TakenAtUtc, string? Location);

/// <summary>Host bridge: camera captures live in the Photos app's library (its camera album). The host bakes
/// the framed crop into the stored copy and stamps capture time plus the in-game location.</summary>
public interface ICameraLibrary
{
    /// <summary>Camera-album photos, newest first.</summary>
    IReadOnlyList<CameraPhoto> Photos { get; }

    /// <summary>Whether shots taken with the camera's own shutter are kept in the camera roll.</summary>
    bool AutoImportShutter { get; }

    /// <summary>Whether captures taken for another app are also copied into the camera roll. The capture is
    /// handed back to the requesting app either way.</summary>
    bool AutoImportAppCaptures { get; }

    /// <summary>Crops <paramref name="sourcePath"/> to <paramref name="crop"/> (image-space x, y, w, h; zero
    /// means the whole image) and stores it in the camera album. Null on failure.</summary>
    CameraPhoto? AddCapture(string sourcePath, Vector4 crop);

    /// <summary>Bakes the same crop into a standalone temp file and returns its path, storing nothing. Used when
    /// a capture is handed to another app but must not be kept in the roll: not every requester can apply a crop
    /// rect itself, so the framing has to travel in the pixels. Null on failure.</summary>
    string? BakeCrop(string sourcePath, Vector4 crop);

    void Delete(string photoId);
}
