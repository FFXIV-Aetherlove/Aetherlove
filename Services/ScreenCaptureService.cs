using System;
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
        if (_hiddenGroups != UiFlags.None)
        {
            _hideFramesLeft = HideDelayFrames;
        }
    }

    private void RestoreUi()
    {
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

    private static bool IsSupportedFormat(DXGI_FORMAT f)
        => f is DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM or DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM_SRGB
             or DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM or DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM_SRGB;

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

    public static byte[] EncodePng(byte[] pixels, int w, int h, DXGI_FORMAT fmt, Rectangle? crop = null)
    {
        var copy = (byte[])pixels.Clone();
        for (var i = 3; i < copy.Length; i += 4)
        {
            copy[i] = 0xFF;
        }
        using Image img = fmt is DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM or DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM_SRGB
            ? Image.LoadPixelData<Rgba32>(copy, w, h)
            : Image.LoadPixelData<Bgra32>(copy, w, h);
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
