using System.IO.MemoryMappedFiles;

namespace AetherOS.WatchHost;

/// <summary>The shared-memory contract between this host and the plugin: a single buffer with a sequence
/// counter rather than a swap chain.
///
/// The counter is written twice, once before the pixels and once after. A reader that sees the same value on
/// both sides of its copy knows it read a whole frame; anything else it skips and retries next frame. That is
/// enough to avoid tearing without either side blocking the other, which matters because one side is a
/// browser and the other is the game's render thread.
///
/// The map name is chosen by the plugin per launch rather than fixed here, so a host orphaned by a game crash
/// can never paint into the map a fresh host has just opened.</summary>
public static class FrameBridge
{
    /// <summary>Fixed ceiling so the map can be allocated once. 1920x1080 BGRA is 7.9 MB, which is the last
    /// step that still buys visible detail: the stage is a window inside the game, so a 4K frame would cost
    /// four times the per-frame copy for pixels the downscale throws away.</summary>
    public const int MaxWidth = 1920;
    public const int MaxHeight = 1080;

    /// <summary>seq, width, height, seq2 as four little-endian int32.</summary>
    public const int HeaderBytes = 16;

    public const int Capacity = HeaderBytes + (MaxWidth * MaxHeight * 4);

    public static MemoryMappedFile Create(string name) =>
        MemoryMappedFile.CreateOrOpen(name, Capacity, MemoryMappedFileAccess.ReadWrite);
}
