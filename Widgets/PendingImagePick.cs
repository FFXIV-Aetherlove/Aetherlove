using System;
using AetherLove.UI;
using Dalamud.Interface.Textures;

namespace AetherLove.Widgets;

/// <summary>Gates a freshly-picked image behind validation before the user may crop it: it must decode and
/// be at least the server's crop-target size. The texture loads off-thread, so <see cref="Poll"/> is called
/// once per frame until the wrap (or a decode error) is available.</summary>
public sealed class PendingImagePick
{
    private readonly ImageRequirementsModal _modal;
    private ISharedImmediateTexture? _handle;
    private int _minW;
    private int _minH;
    private Action? _onValid;
    private Action? _onReject;

    public PendingImagePick(ImageRequirementsModal modal) => _modal = modal;

    /// <summary>When the picked file is an un-hydrated cloud placeholder (its bytes aren't on disk), shows the
    /// requirements modal explaining it and returns true so the caller aborts before trying to load it.</summary>
    public bool RejectUnavailableCloudFile(string path)
    {
        if (!SharedUiHelpers.IsUnavailableCloudFile(path))
        {
            return false;
        }
        _modal.ShowCloudUnavailable();
        return true;
    }

    /// <param name="onValid">Runs once the image decodes and meets the minimum size.</param>
    /// <param name="onReject">Runs when the image is invalid or too small — unload the pick here.</param>
    public void Begin(ISharedImmediateTexture handle, int minW, int minH, Action onValid, Action onReject)
    {
        _handle = handle;
        _minW = minW;
        _minH = minH;
        _onValid = onValid;
        _onReject = onReject;
    }

    public void Poll()
    {
        if (_handle is null)
        {
            return;
        }

        if (!_handle.TryGetWrap(out var wrap, out var ex))
        {
            if (ex is null)
            {
                return; // still decoding
            }
            var reject = _onReject;
            Clear();
            _modal.ShowInvalid();
            reject?.Invoke();
            return;
        }

        int w = wrap.Width, h = wrap.Height;
        if (w < _minW || h < _minH)
        {
            var reject = _onReject;
            Clear();
            _modal.ShowTooSmall(w, h);
            reject?.Invoke();
            return;
        }

        var valid = _onValid;
        Clear();
        valid?.Invoke();
    }

    private void Clear()
    {
        _handle = null;
        _onValid = null;
        _onReject = null;
    }
}
