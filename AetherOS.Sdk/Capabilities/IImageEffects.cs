using System;

namespace AetherOS.Sdk;

/// <summary>The built-in photo filters. Rendering happens host-side; apps only pick one.</summary>
public enum ImageFilter
{
    None,
    Mono,
    Noir,
    Sepia,
    Retro,
    Cool,
    Vivid,
    Fade,
}

/// <summary>Slider adjustments applied on top of a filter: brightness and contrast are multipliers around 1,
/// the hue (degrees) rotates the image's colors, and the tint additionally overlays that hue's color at the
/// given strength (0..1). Neutral values leave the image untouched.</summary>
public sealed record ImageAdjustments(float Brightness = 1f, float Contrast = 1f, float TintHue = 0f, float TintStrength = 0f)
{
    public static readonly ImageAdjustments Neutral = new();

    public bool IsNeutral => Brightness == 1f && Contrast == 1f && TintStrength <= 0f && TintHue % 360f == 0f;
}

/// <summary>Applies photo filters and adjustments to disk images.</summary>
public interface IImageEffects
{
    /// <summary>Writes an edited copy of the image to a host-managed temporary file and hands back its path,
    /// or null when the image could not be processed. A <see cref="ImageFilter.None"/> filter with neutral
    /// adjustments returns the source path unchanged. The callback may fire on a worker thread; store the
    /// result, don't draw from it.</summary>
    void Apply(string sourcePath, ImageFilter filter, ImageAdjustments adjustments, Action<string?> onDone);
}
