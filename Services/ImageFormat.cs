using System;

namespace AetherLove.Services;

/// <summary>Maps image bytes to a file extension by their container magic. Cache files are named by
/// actual content so <c>GetFromFile</c> decodes them whether the server sent WebP or JPEG (Wine has
/// no WebP WIC codec and gets JPEG); the on-disk name then never disagrees with the bytes.</summary>
public static class ImageFormat
{
    public static string ExtensionFor(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return ".jpg";
        }
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
        {
            return ".png";
        }
        return ".webp";
    }
}
