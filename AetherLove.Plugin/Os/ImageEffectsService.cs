using System;
using System.IO;
using System.Threading.Tasks;
using AetherOS.Sdk;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace AetherLove.Os;

/// <summary>Applies the SDK photo filters with ImageSharp, writing each result as a PNG into a private temp
/// folder. Filters preserve image dimensions, so recorded crop rects stay valid on the filtered copy.</summary>
public sealed class ImageEffectsService : IImageEffects
{
    private readonly string _dir;

    public ImageEffectsService()
    {
        _dir = Path.Combine(Path.GetTempPath(), "aetheros_edits");
        try
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, true);
            }
        }
        catch (Exception)
        {
        }
    }

    public void Apply(string sourcePath, ImageFilter filter, ImageAdjustments adjustments, Action<string?> onDone)
    {
        if (filter == ImageFilter.None && adjustments.IsNeutral)
        {
            onDone(sourcePath);
            return;
        }
        Task.Run(() =>
        {
            try
            {
                using var img = Image.Load(sourcePath);
                img.Mutate(x =>
                {
                    switch (filter)
                    {
                        case ImageFilter.Mono:
                            x.Grayscale();
                            break;
                        case ImageFilter.Noir:
                            x.Grayscale();
                            x.Contrast(1.35f);
                            x.Brightness(0.95f);
                            break;
                        case ImageFilter.Sepia:
                            x.Sepia();
                            break;
                        case ImageFilter.Retro:
                            x.Kodachrome();
                            break;
                        case ImageFilter.Cool:
                            x.Lomograph();
                            break;
                        case ImageFilter.Vivid:
                            x.Saturate(1.35f);
                            x.Contrast(1.08f);
                            break;
                        case ImageFilter.Fade:
                            x.Saturate(0.72f);
                            x.Contrast(0.92f);
                            x.Brightness(1.06f);
                            break;
                    }
                    var brightness = Math.Clamp(adjustments.Brightness, 0.25f, 2f);
                    if (Math.Abs(brightness - 1f) > 0.001f)
                    {
                        x.Brightness(brightness);
                    }
                    var contrast = Math.Clamp(adjustments.Contrast, 0.25f, 2f);
                    if (Math.Abs(contrast - 1f) > 0.001f)
                    {
                        x.Contrast(contrast);
                    }
                    var hue = ((adjustments.TintHue % 360f) + 360f) % 360f;
                    if (hue > 0.5f)
                    {
                        x.Hue(hue);
                    }
                });
                var strength = Math.Clamp(adjustments.TintStrength, 0f, 1f);
                if (strength > 0f)
                {
                    using var tint = new Image<Rgba32>(img.Width, img.Height, HueColor(adjustments.TintHue));
                    img.Mutate(x => x.DrawImage(tint, strength));
                }
                Directory.CreateDirectory(_dir);
                var path = Path.Combine(_dir, $"{Guid.NewGuid():N}.png");
                img.Save(path, new PngEncoder());
                onDone(path);
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "[ImageEffects] Failed to apply a filter. Filter={Filter}", filter);
                onDone(null);
            }
        });
    }

    private static Rgba32 HueColor(float hueDeg)
    {
        var h = ((hueDeg % 360f) + 360f) % 360f / 60f;
        var t = 1f - MathF.Abs(h % 2f - 1f);
        var (r, g, b) = (int)h switch
        {
            0 => (1f, t, 0f),
            1 => (t, 1f, 0f),
            2 => (0f, 1f, t),
            3 => (0f, t, 1f),
            4 => (t, 0f, 1f),
            _ => (1f, 0f, t),
        };
        return new Rgba32(r, g, b, 1f);
    }
}
