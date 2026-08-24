using System;
using System.Collections.Generic;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using TerraFX.Interop.DirectX;
using FfxivDevice = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Device;
using FfxivUiModule = FFXIVClientStructs.FFXIV.Client.UI.UIModule;
using FfxivAtkUnitManager = FFXIVClientStructs.FFXIV.Client.UI.RaptureAtkUnitManager;
using UiFlags = FFXIVClientStructs.FFXIV.Client.UI.UiFlags;

namespace AetherLove.Services;

/// <summary>Reads the game backbuffer on demand through a D3D11 staging texture on the render thread.</summary>
public sealed unsafe class ScreenCaptureService : IDisposable
{
    private enum CaptureState
    {
        Idle,
        Requested,
        InProgress,
        Done,
    }

    private const int HideDelayFrames = 2;

    // HUD groups toggled off for a selfie; each restored afterwards only if it was on.
    private static readonly UiFlags[] HideGroups =
    {
        UiFlags.Shortcuts,
        UiFlags.Hud,
        UiFlags.Nameplates,
        UiFlags.Chat,
        UiFlags.ActionBars,
        UiFlags.TargetInfo,
    };

    /// <summary>The group-pose windows, which are ordinary addons rather than a HUD group, so the UiFlags
    /// above never touch them: a gpose selfie photographed its own camera panel. Hidden by node flag for the
    /// capture and put back exactly as found.</summary>
    private static readonly string[] GposeAddons =
    {
        "CameraSetting",
        "GroupPoseStickyGuide",
        "GroupPoseGuide",
        "GposeTargetLoc",
        "GposeGuide",
    };

    private readonly List<string> _hiddenAddons = [];
    private readonly object _lock = new();
    private CaptureState _state = CaptureState.Idle;
    private byte[]? _pixels;
    private int _w;
    private int _h;
    private DXGI_FORMAT _fmt;
    private long _requestedAtMs;
    private bool _disposed;
    private bool _subscribed;

    private bool _hideHud;
    private UiFlags _hiddenGroups;
    private int _hideFramesLeft;

    public bool CaptureInProgress
    {
        get
        {
            lock (_lock)
            {
                return _state is CaptureState.Requested or CaptureState.InProgress;
            }
        }
    }

    public void Initialize()
    {
        if (_subscribed)
        {
            return;
        }
        Plugin.PluginInterface.UiBuilder.Draw += OnDraw;
        _subscribed = true;
    }

    /// <summary>Requests one backbuffer capture, fulfilled over the next render frames.</summary>
    public void RequestCapture(bool hideHud = true)
    {
        lock (_lock)
        {
            if (_state is CaptureState.Requested or CaptureState.InProgress)
            {
                return;
            }
            _pixels = null;
            _hideHud = hideHud;
            _requestedAtMs = Environment.TickCount64;
            _state = CaptureState.Requested;
        }
    }

    /// <summary>Returns true exactly once per attempt; a null <paramref name="pixels"/> then means the capture failed.</summary>
    public bool TryGetResult(out byte[]? pixels, out int width, out int height, out DXGI_FORMAT format)
    {
        lock (_lock)
        {
            if (_state is CaptureState.Requested or CaptureState.InProgress
                && Environment.TickCount64 - _requestedAtMs > 5000)
            {
                _pixels = null;
                _state = CaptureState.Done;
            }
            if (_state != CaptureState.Done)
            {
                pixels = null;
                width = 0;
                height = 0;
                format = default;
                return false;
            }
            pixels = _pixels;
            width = _w;
            height = _h;
            format = _fmt;
            _pixels = null;
            _state = CaptureState.Idle;
            return true;
        }
    }

    private void OnDraw()
    {
        if (_disposed)
        {
            return;
        }

        CaptureState state;
        lock (_lock)
        {
            state = _state;
        }

        if (state == CaptureState.Requested)
        {
            lock (_lock)
            {
                _state = CaptureState.InProgress;
            }
            BeginHide();
            return;
        }

        if (state != CaptureState.InProgress)
        {
            return;
        }

        // The hidden HUD needs a couple of frames to leave the backbuffer before the readback.
        if (_hideFramesLeft > 0)
        {
            _hideFramesLeft--;
            return;
        }

        try
        {
            DoReadback();
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "[ScreenCapture] readback threw.");
            StoreFailure();
        }
        finally
        {
            RestoreUi();
        }
    }

    private void BeginHide()
    {
        _hiddenGroups = UiFlags.None;
        _hideFramesLeft = 0;
        if (!_hideHud)
        {
            return;
        }
        var uim = FfxivUiModule.Instance();
        var ram = FfxivAtkUnitManager.Instance();
        if (uim is null || ram is null)
        {
            return;
        }
        // A set UiFlag means the group is shown; only those are toggled, so a player's own hidden groups stay hidden.
        foreach (var group in HideGroups)
        {
            if (ram->IsUiFlagsSet(group))
            {
                uim->ToggleUi(group, false, true);
                _hiddenGroups |= group;
            }
        }
        if (HideGposeAddons() || _hiddenGroups != UiFlags.None)
        {
            _hideFramesLeft = HideDelayFrames;
        }
    }

    /// <summary>Hides whichever group-pose windows are up right now. Returns whether anything was hidden,
    /// so the readback still waits its couple of frames for them to leave the backbuffer.</summary>
    private bool HideGposeAddons()
    {
        _hiddenAddons.Clear();
        foreach (var name in GposeAddons)
        {
            try
            {
                var addon = (FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase*)
                    Plugin.GameGui.GetAddonByName(name).Address;
                if (addon is null || !addon->IsVisible)
                {
                    continue;
                }
                addon->IsVisible = false;
                _hiddenAddons.Add(name);
            }
            catch (Exception ex)
            {
                Plugin.Log.Debug(ex, "[ScreenCapture] could not hide {Addon}.", name);
            }
        }
        return _hiddenAddons.Count > 0;
    }

    private void RestoreGposeAddons()
    {
        foreach (var name in _hiddenAddons)
        {
            try
            {
                var addon = (FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase*)
                    Plugin.GameGui.GetAddonByName(name).Address;
                if (addon is not null)
                {
                    addon->IsVisible = true;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Debug(ex, "[ScreenCapture] could not restore {Addon}.", name);
            }
        }
        _hiddenAddons.Clear();
    }

    private void RestoreUi()
    {
        RestoreGposeAddons();
        if (_hiddenGroups == UiFlags.None)
        {
            return;
        }
        var uim = FfxivUiModule.Instance();
        if (uim is not null)
        {
            foreach (var group in HideGroups)
            {
                if (_hiddenGroups.HasFlag(group))
                {
                    uim->ToggleUi(group, true, true);
                }
            }
        }
        _hiddenGroups = UiFlags.None;
    }

    private void DoReadback()
    {
        var dev = FfxivDevice.Instance();
        if (dev is null || dev->SwapChain is null || dev->SwapChain->BackBuffer is null)
        {
            StoreFailure();
            return;
        }
        var backBuffer = (ID3D11Texture2D*)dev->SwapChain->BackBuffer->D3D11Texture2D;
        if (backBuffer is null)
        {
            StoreFailure();
            return;
        }

        ID3D11Device* device = null;
        ID3D11DeviceContext* ctx = null;
        ID3D11Texture2D* resolveTex = null;
        ID3D11Texture2D* staging = null;
        try
        {
            D3D11_TEXTURE2D_DESC desc;
            backBuffer->GetDesc(&desc);
            if (!IsSupportedFormat(desc.Format))
            {
                Plugin.Log.Warning($"[ScreenCapture] unsupported backbuffer format {desc.Format}.");
                StoreFailure();
                return;
            }

            backBuffer->GetDevice(&device);
            device->GetImmediateContext(&ctx);

            // Staging can't be multisampled: resolve an MSAA backbuffer to a single-sample copy first.
            var source = backBuffer;
            if (desc.SampleDesc.Count > 1)
            {
                var rd = desc;
                rd.SampleDesc.Count = 1;
                rd.SampleDesc.Quality = 0;
                rd.Usage = D3D11_USAGE.D3D11_USAGE_DEFAULT;
                rd.BindFlags = 0;
                rd.CPUAccessFlags = 0;
                rd.MiscFlags = 0;
                if (device->CreateTexture2D(&rd, null, &resolveTex).FAILED)
                {
                    StoreFailure();
                    return;
                }
                ctx->ResolveSubresource((ID3D11Resource*)resolveTex, 0, (ID3D11Resource*)backBuffer, 0, desc.Format);
                source = resolveTex;
            }

            var sd = desc;
            sd.SampleDesc.Count = 1;
            sd.SampleDesc.Quality = 0;
            sd.Usage = D3D11_USAGE.D3D11_USAGE_STAGING;
            sd.CPUAccessFlags = (uint)D3D11_CPU_ACCESS_FLAG.D3D11_CPU_ACCESS_READ;
            sd.BindFlags = 0;
            sd.MiscFlags = 0;
            if (device->CreateTexture2D(&sd, null, &staging).FAILED)
            {
                StoreFailure();
                return;
            }

            ctx->CopyResource((ID3D11Resource*)staging, (ID3D11Resource*)source);

            D3D11_MAPPED_SUBRESOURCE map;
            if (ctx->Map((ID3D11Resource*)staging, 0, D3D11_MAP.D3D11_MAP_READ, 0, &map).FAILED)
            {
                StoreFailure();
                return;
            }
            try
            {
                var w = (int)desc.Width;
                var h = (int)desc.Height;
                var rowBytes = w * 4;
                var buf = new byte[(long)h * rowBytes];
                var src = (byte*)map.pData;
                fixed (byte* dst = buf)
                {
                    for (var y = 0; y < h; y++)
                    {
                        Buffer.MemoryCopy(src + (long)y * map.RowPitch, dst + (long)y * rowBytes, rowBytes, rowBytes);
                    }
                }
                StoreResult(buf, w, h, desc.Format);
            }
            finally
            {
                ctx->Unmap((ID3D11Resource*)staging, 0);
            }
        }
        finally
        {
            if (staging is not null)
            {
                staging->Release();
            }
            if (resolveTex is not null)
            {
                resolveTex->Release();
            }
            if (ctx is not null)
            {
                ctx->Release();
            }
            if (device is not null)
            {
                device->Release();
            }
            // backBuffer is a borrowed pointer owned by the game; never Release it.
        }
    }

    /// <summary>Every format here is 4 bytes per pixel, which the row copy in <see cref="DoReadback"/> assumes.
    /// A wider one (the 16-bit-float backbuffer a real HDR path would use) needs that stride made per-format
    /// first.</summary>
    private static bool IsSupportedFormat(DXGI_FORMAT f)
        => f is DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM or DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM_SRGB
             or DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM or DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM_SRGB
             or DXGI_FORMAT.DXGI_FORMAT_R10G10B10A2_UNORM;

    private void StoreResult(byte[] pixels, int w, int h, DXGI_FORMAT fmt)
    {
        lock (_lock)
        {
            _pixels = pixels;
            _w = w;
            _h = h;
            _fmt = fmt;
            _state = CaptureState.Done;
        }
    }

    private void StoreFailure()
    {
        lock (_lock)
        {
            _pixels = null;
            _state = CaptureState.Done;
        }
    }

    /// <summary>The captured frame as an image, for a caller that wants to paint on it before saving.</summary>
    public static Image<Rgba32> Decode(byte[] pixels, int w, int h, DXGI_FORMAT fmt)
    {
        using var img = fmt switch
        {
            DXGI_FORMAT.DXGI_FORMAT_R10G10B10A2_UNORM => LoadRgb10A2(pixels, w, h),
            DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM or DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM_SRGB
                => Image.LoadPixelData<Rgba32>(Opaque(pixels), w, h),
            _ => (Image)Image.LoadPixelData<Bgra32>(Opaque(pixels), w, h),
        };
        return img.CloneAs<Rgba32>();
    }

    public static byte[] EncodePng(Image image, Rectangle? crop = null)
    {
        using var copy = image.Clone(_ => { });
        if (crop is { } rect)
        {
            rect = Rectangle.Intersect(rect, new Rectangle(0, 0, copy.Width, copy.Height));
            if (rect.Width > 0 && rect.Height > 0)
            {
                copy.Mutate(x => x.Crop(rect));
            }
        }
        using var stream = new MemoryStream();
        copy.Save(stream, new PngEncoder());
        return stream.ToArray();
    }

    public static byte[] EncodePng(byte[] pixels, int w, int h, DXGI_FORMAT fmt, Rectangle? crop = null)
    {
        using Image img = fmt switch
        {
            DXGI_FORMAT.DXGI_FORMAT_R10G10B10A2_UNORM => LoadRgb10A2(pixels, w, h),
            DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM or DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM_SRGB
                => Image.LoadPixelData<Rgba32>(Opaque(pixels), w, h),
            _ => Image.LoadPixelData<Bgra32>(Opaque(pixels), w, h),
        };
        if (crop is { } r)
        {
            r = Rectangle.Intersect(r, new Rectangle(0, 0, w, h));
            if (r.Width > 0 && r.Height > 0)
            {
                img.Mutate(x => x.Crop(r));
            }
        }
        using var ms = new MemoryStream();
        img.Save(ms, new PngEncoder());
        return ms.ToArray();
    }

    /// <summary>A screenshot is opaque whatever the backbuffer left in its alpha channel.</summary>
    private static byte[] Opaque(byte[] pixels)
    {
        var copy = (byte[])pixels.Clone();
        for (var i = 3; i < copy.Length; i += 4)
        {
            copy[i] = 0xFF;
        }
        return copy;
    }

    /// <summary>Unpacks a 10-bit-per-channel backbuffer, which is what a player running the desktop or the game
    /// at 10 bits gets. Still 4 bytes per pixel, but one 32-bit word rather than four bytes, so the alpha byte
    /// carries the top of blue and must never be overwritten. Shifting by two is exact at both ends (1023 maps
    /// to 255), and the colours are ordinary SDR: the game has no HDR output of its own.</summary>
    private static Image LoadRgb10A2(byte[] pixels, int w, int h)
    {
        var rgba = new byte[pixels.Length];
        for (var i = 0; i + 3 < pixels.Length; i += 4)
        {
            var v = (uint)(pixels[i] | (pixels[i + 1] << 8) | (pixels[i + 2] << 16) | (pixels[i + 3] << 24));
            rgba[i] = (byte)((v & 0x3FF) >> 2);
            rgba[i + 1] = (byte)(((v >> 10) & 0x3FF) >> 2);
            rgba[i + 2] = (byte)(((v >> 20) & 0x3FF) >> 2);
            rgba[i + 3] = 0xFF;
        }
        return Image.LoadPixelData<Rgba32>(rgba, w, h);
    }

    public void Dispose()
    {
        _disposed = true;
        if (_subscribed)
        {
            Plugin.PluginInterface.UiBuilder.Draw -= OnDraw;
            _subscribed = false;
        }
    }
}
