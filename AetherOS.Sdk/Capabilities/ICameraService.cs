using System;
using System.Numerics;

namespace AetherOS.Sdk;

/// <summary>How the selfie camera should frame the shot.</summary>
/// <param name="Aspect">cropHeight / cropWidth (1.0 square, 1.6 for 10:16 portrait). Ignored when free-form.</param>
/// <param name="MinCropWidth">Smallest acceptable crop width in pixels.</param>
/// <param name="FreeForm">No fixed aspect: the frame starts at half the screen width and 80% of its height
/// and resizes on both axes.</param>
public readonly record struct CameraRequest(float Aspect = 1f, int MinCropWidth = 128, bool FreeForm = false);

/// <summary>A captured selfie: the saved image path and the crop rect the user framed.</summary>
public readonly record struct CameraShot(string Path, Vector4 Crop);

/// <summary>The in-game selfie camera. Backed by the shell's screen-capture pipeline, so apps cannot host
/// it themselves.</summary>
public interface ICameraService
{
    /// <summary>Opens the selfie overlay; <paramref name="onCaptured"/> fires once the user saves a shot.</summary>
    void Capture(CameraRequest request, Action<CameraShot> onCaptured);
}
