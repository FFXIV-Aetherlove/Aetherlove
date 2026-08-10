using System;
using System.Numerics;
using AetherLove;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using ManagedDoom;
using ManagedDoom.Video;

namespace AetherOS.Apps.Doom.Backend;

/// <summary>Doom's video output, drawn as an ImGui image instead of owning a window. The engine's own
/// software renderer is reused untouched; only the presentation is ours.
///
/// The renderer fills its buffer COLUMN-major, so the pixels come out transposed: the image in memory is
/// <c>Height</c> wide and <c>Width</c> tall. Rather than pay for a CPU transpose every frame, the quad is
/// drawn with rotated corner UVs, which costs nothing. This mirrors what the upstream desktop frontend does
/// with its texture coordinates.</summary>
internal sealed class ImGuiDoomVideo : IVideo, IDisposable
{
    private readonly Renderer renderer;
    private readonly byte[] pixels;

    private IDalamudTextureWrap? texture;
    private bool pending;

    public ImGuiDoomVideo(Config config, GameContent content)
    {
        this.renderer = new Renderer(config, content);
        this.pixels = new byte[4 * this.renderer.Width * this.renderer.Height];
    }

    /// <summary>Width over height of the frame AS DISPLAYED, which is the un-transposed orientation: the
    /// buffer is stored rotated, so this is deliberately not the ratio of the buffer's own dimensions.</summary>
    public float AspectRatio => (float)this.renderer.Width / this.renderer.Height;

    public void Render(global::ManagedDoom.Doom doom, Fixed frameFrac)
    {
        this.renderer.Render(doom, this.pixels, frameFrac);
        this.pending = true;
    }

    /// <summary>Pushes the most recent frame to the GPU and draws it into <paramref name="rect"/>. Kept apart
    /// from <see cref="Render"/> because the engine renders on its own 35 Hz tic while ImGui draws every frame:
    /// re-uploading an unchanged frame would just burn texture allocations.</summary>
    public void Present(ImDrawListPtr dl, Vector2 topLeft, Vector2 size)
    {
        if (this.pending)
        {
            this.pending = false;
            var fresh = UiHost.TextureProvider.CreateFromRaw(
                RawImageSpecification.Rgba32(this.renderer.Height, this.renderer.Width), this.pixels, "doom-frame");
            this.texture?.Dispose();
            this.texture = fresh;
        }

        if (this.texture is not { } wrap)
        {
            return;
        }

        var tl = topLeft;
        var tr = topLeft + new Vector2(size.X, 0f);
        var br = topLeft + size;
        var bl = topLeft + new Vector2(0f, size.Y);
        // Screen X reads down the texture, screen Y reads across it: the transpose, absorbed for free.
        dl.AddImageQuad(wrap.Handle, tl, tr, br, bl,
            new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f),
            0xFFFFFFFF);
    }

    public void InitializeWipe() => this.renderer.InitializeWipe();

    public bool HasFocus() => true;

    public void Dispose()
    {
        this.texture?.Dispose();
        this.texture = null;
    }

    public int WipeBandCount => this.renderer.WipeBandCount;

    public int WipeHeight => this.renderer.WipeHeight;

    public int MaxWindowSize => this.renderer.MaxWindowSize;

    public int WindowSize
    {
        get => this.renderer.WindowSize;
        set => this.renderer.WindowSize = value;
    }

    public bool DisplayMessage
    {
        get => this.renderer.DisplayMessage;
        set => this.renderer.DisplayMessage = value;
    }

    public int MaxGammaCorrectionLevel => this.renderer.MaxGammaCorrectionLevel;

    public int GammaCorrectionLevel
    {
        get => this.renderer.GammaCorrectionLevel;
        set => this.renderer.GammaCorrectionLevel = value;
    }
}
