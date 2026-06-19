using System;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace AetherLove.Shared;

/// <summary>Output target for a processed photo.</summary>
public enum PhotoKind
{
    /// <summary>1:1, output 100x100.</summary>
    Avatar,

    /// <summary>10:16, output 350x560.</summary>
    Portrait,
}

/// <summary>Crop rectangle in original-image pixel coordinates.</summary>
public readonly record struct CropRect(int X, int Y, int Width, int Height);

/// <summary>Thrown when an image can't be decoded/cropped/encoded. The message is a <see cref="HubErrors"/>
/// payload so it localizes the same on the client (pre-upload) and the server.</summary>
public sealed class PhotoProcessingException : Exception
{
    public PhotoProcessingException(string message) : base(message) { }
    public PhotoProcessingException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>Shared image pipeline (decode → crop → resize → strip metadata → encode), run on the client
/// before upload and reused by the server's legacy path. ImageSharp is pure-managed, so it works on Wine.</summary>
public static class PhotoTransform
{
    public const int MaxDecodedDimension = 8192;
    public const int MinCropSide = 32;

    /// <summary>A tiny lossy WebP (the same encoder the server serves photos with) the client decodes at
    /// startup to test whether its local image decoder can actually handle WebP — so machines whose decoder
    /// can't (Wine, or Windows without the WebP codec) get served JPEG instead of gray blocks.</summary>
    public static byte[] CreateProbeWebp()
    {
        using var image = new Image<Rgba32>(8, 8, new Rgba32(120, 90, 200, 255));
        using var ms = new MemoryStream();
        image.Save(ms, new WebpEncoder { FileFormat = WebpFileFormatType.Lossy, Quality = 80 });
        return ms.ToArray();
    }

    public static (int Width, int Height) TargetDimensions(PhotoKind kind) => kind switch
    {
        PhotoKind.Avatar => (PhotoSpec.AvatarSize, PhotoSpec.AvatarSize),
        PhotoKind.Portrait => (PhotoSpec.PortraitWidth, PhotoSpec.PortraitHeight),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>True when the crop rect covers the whole image — the signal a client used to mark an
    /// already-cropped, target-sized upload (vs. an original that still needs server cropping).</summary>
    public static bool IsFullImage(CropRect crop, int width, int height) =>
        crop.X <= 0 && crop.Y <= 0 && crop.Width >= width && crop.Height >= height;

    /// <summary>Full client pipeline: decode, crop, resize to the kind's target, strip metadata, encode PNG
    /// (lossless — the server does the single lossy WebP re-encode).</summary>
    public static byte[] ProcessToPng(byte[] originalBytes, CropRect crop, PhotoKind kind)
    {
        using var image = DecodeGuarded(originalBytes);
        CropAndResize(image, crop, kind);
        StripMetadata(image);
        using var output = new MemoryStream();
        image.Save(output, new PngEncoder());
        return output.ToArray();
    }

    /// <summary>Decodes any supported image to full-size PNG bytes (no crop/resize). Used to preview a
    /// source the client's own texture loader can't decode — notably WebP, which WIC lacks on Wine.</summary>
    public static byte[] DecodeToPng(byte[] originalBytes)
    {
        using var image = DecodeGuarded(originalBytes);
        using var output = new MemoryStream();
        image.Save(output, new PngEncoder());
        return output.ToArray();
    }

    /// <summary>Decode with a dimension guard (checked via a cheap identify pass before the full decode, so a
    /// decompression bomb can't be materialised) and frame cap. Caller owns disposal.</summary>
    public static Image DecodeGuarded(byte[] bytes)
    {
        if (bytes is null || bytes.Length == 0)
        {
            throw new PhotoProcessingException(HubErrors.Format(HubErrors.ImgPayloadInvalid));
        }

        try
        {
            ImageInfo info;
            using (var probe = new MemoryStream(bytes, writable: false))
            {
                info = Image.Identify(probe);
            }
            if (info.Width > MaxDecodedDimension || info.Height > MaxDecodedDimension)
            {
                throw new PhotoProcessingException(HubErrors.Format(HubErrors.ImgDimensionsTooLarge,
                    info.Width, info.Height, MaxDecodedDimension));
            }

            using var stream = new MemoryStream(bytes, writable: false);
            return Image.Load(new DecoderOptions { MaxFrames = 1 }, stream);
        }
        catch (PhotoProcessingException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new PhotoProcessingException(HubErrors.Format(HubErrors.ImgDecodeFailed), ex);
        }
    }

    /// <summary>Crop to the (bounds-clipped) rect, then stretch-resize to the kind's target dimensions.</summary>
    public static void CropAndResize(Image image, CropRect crop, PhotoKind kind)
    {
        var clipped = ClipToBounds(crop, image.Width, image.Height);
        if (clipped.Width < MinCropSide || clipped.Height < MinCropSide)
        {
            throw new PhotoProcessingException(HubErrors.Format(HubErrors.ImgCropTooSmall, MinCropSide));
        }

        var (targetWidth, targetHeight) = TargetDimensions(kind);
        image.Mutate(ctx =>
        {
            ctx.Crop(new Rectangle(clipped.X, clipped.Y, clipped.Width, clipped.Height));
            ctx.Resize(new ResizeOptions
            {
                Size = new Size(targetWidth, targetHeight),
                Mode = ResizeMode.Stretch,
                Sampler = KnownResamplers.Lanczos3,
            });
        });
    }

    /// <summary>Drops every metadata profile (EXIF can carry GPS); nothing the user embedded survives.</summary>
    public static void StripMetadata(Image image)
    {
        image.Metadata.ExifProfile = null;
        image.Metadata.XmpProfile = null;
        image.Metadata.IptcProfile = null;
        image.Metadata.IccProfile = null;
        image.Metadata.CicpProfile = null;
    }

    private static CropRect ClipToBounds(CropRect crop, int imgWidth, int imgHeight)
    {
        var x = Math.Max(0, Math.Min(crop.X, imgWidth - 1));
        var y = Math.Max(0, Math.Min(crop.Y, imgHeight - 1));
        var w = Math.Max(1, Math.Min(crop.Width, imgWidth - x));
        var h = Math.Max(1, Math.Min(crop.Height, imgHeight - y));
        return new CropRect(x, y, w, h);
    }
}
