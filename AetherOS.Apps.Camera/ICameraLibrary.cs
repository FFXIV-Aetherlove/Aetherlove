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

    /// <summary>Crops <paramref name="sourcePath"/> to <paramref name="crop"/> (image-space x, y, w, h; zero
    /// means the whole image) and stores it in the camera album. Null on failure.</summary>
    CameraPhoto? AddCapture(string sourcePath, Vector4 crop);

    void Delete(string photoId);
}
