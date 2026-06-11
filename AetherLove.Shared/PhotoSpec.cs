namespace AetherLove.Shared;

/// <summary>Output dimensions photos are cropped and resized to server-side. The plugin validates that a
/// freshly-picked source image is at least this large before letting the user crop it, so the server never
/// has to stretch a tiny image (or reject a degenerate one like 1×1080).</summary>
public static class PhotoSpec
{
    public const int AvatarSize = 100;
    public const int PortraitWidth = 350;
    public const int PortraitHeight = 560;
}
