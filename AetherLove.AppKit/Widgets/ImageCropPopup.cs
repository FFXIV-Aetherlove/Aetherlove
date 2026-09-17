using System;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace AetherLove.Widgets;

/// <summary>Resizable popup window that wraps an <see cref="ImageCropWidget"/> and adds quarter-turn rotation.
/// A rotation writes an upright PNG copy under the temp folder and swaps the preview to it, so the crop always
/// works on an upright image and the confirmed <see cref="CroppedImage"/> points at the rotated file.</summary>
public class ImageCropPopup
{
    private static readonly string RotatedDir = Path.Combine(Path.GetTempPath(), "AetherLoveRotated");

    private bool _open;
    private string _title = "";
    private string _path = "";
    private string _originalPath = "";
    private ISharedImmediateTexture? _handle;
    private float _aspectRatio = 1f;
    private ImageCropWidget _crop = new(1.0f);
    private Action<CroppedImage>? _onConfirm;
    private Action? _onCancel;
    private volatile bool _rotating;
    private volatile string? _rotated;

    static ImageCropPopup() => PurgeRotated();

    /// <param name="aspectRatio">cropHeight / cropWidth (1.0 square, 1.6 for 10:16 portrait); ignored when
    /// <paramref name="freeForm"/> is set, where the box starts as the whole image and takes any shape.</param>
    /// <param name="onConfirm">Receives the file to use (the rotated copy after a rotation), its preview and
    /// the image-space crop rect (x, y, w, h) on confirm.</param>
    public void Open(
        string title,
        string path,
        ISharedImmediateTexture handle,
        float aspectRatio,
        Action<CroppedImage> onConfirm,
        Action? onCancel = null,
        bool freeForm = false)
    {
        _title = title;
        _path = path;
        _originalPath = path;
        _handle = handle;
        _aspectRatio = freeForm ? 0f : aspectRatio;
        _crop = new ImageCropWidget(_aspectRatio);
        _onConfirm = onConfirm;
        _onCancel = onCancel;
        _rotating = false;
        _rotated = null;
        _open = true;
    }

    public void Draw(Vector2 parentPos, Vector2 parentSize)
    {
        if (!_open)
        {
            return;
        }
        if (_rotated is { } swapped)
        {
            _rotated = null;
            _path = swapped;
            _handle = LoadPickedPreview(swapped);
            _crop = new ImageCropWidget(_aspectRatio);
            _rotating = false;
        }

        var display = ImGui.GetIO().DisplaySize;

        var mainCenterX = parentPos.X + parentSize.X * 0.5f;
        var placeLeft = mainCenterX > display.X * 0.55f;

        var PopupW = Px(700f);
        var PopupH = Px(560f);
        var Gap = Px(8f);
        float popupX, popupY;

        if (placeLeft)
        {
            popupX = parentPos.X - PopupW - Gap;
            popupX = MathF.Max(popupX, Px(4f));
        }
        else
        {
            popupX = parentPos.X + parentSize.X + Gap;
            popupX = MathF.Min(popupX, display.X - PopupW - Px(4f));
        }

        popupY = Math.Clamp(parentPos.Y, Px(4f), Math.Max(Px(4f), display.Y - PopupH - Px(4f)));

        var initW = MathF.Min(PopupW, display.X - Px(50f));
        var initH = MathF.Min(PopupH, display.Y - Px(50f));

        ImGui.SetNextWindowSize(new Vector2(initW, initH), ImGuiCond.Appearing);
        ImGui.SetNextWindowSizeConstraints(
            Px(440f, 340f),
            new Vector2(float.MaxValue, float.MaxValue));
        ImGui.SetNextWindowPos(new Vector2(popupX, popupY), ImGuiCond.Appearing);

        var open = true;
        var expanded = ImGui.Begin(_title, ref open, ImGuiWindowFlags.NoCollapse);

        if (!open)
        {
            _onCancel?.Invoke();
            _open = false;
            ImGui.End();
            return;
        }

        if (expanded)
        {
            var tex = _rotating ? null : _handle?.GetWrapOrDefault();

            if (tex == null)
            {
                ImGui.Spacing();
                ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.5f, 1f), Loc.T("common.loading_image"));
            }
            else
            {
                var freeForm = _aspectRatio <= 0f;
                var hintH = freeForm ? ImGui.GetTextLineHeightWithSpacing() : 0f;
                var ButtonRowH = Px(56f) + hintH;
                var avail = ImGui.GetContentRegionAvail();
                var cropW = avail.X - Px(8f);
                var imgAspect = (float)tex.Width / tex.Height;
                var maxH = avail.Y - ButtonRowH;
                if (maxH > 0f)
                {
                    cropW = System.Math.Min(cropW, maxH * imgAspect);
                }

                _crop.Draw(tex, cropW);

                if (freeForm)
                {
                    ImGui.TextDisabled(Loc.T("common.crop_free_hint"));
                }
                var t = ThemeService.Current;
                ImGui.Spacing();
                ImGui.PushStyleColor(ImGuiCol.Button, t.ButtonNormal);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, t.ButtonHovered);
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, t.ButtonActive);
                if (ImGui.Button(Loc.T(freeForm ? "common.use_picture" : "common.use_this_crop"), Px(150f, 32f)))
                {
                    _onConfirm?.Invoke(new CroppedImage(_path, _handle!, _crop.CropRect, _originalPath));
                    _open = false;
                }
                ImGui.PopStyleColor(3);

                ImGui.SameLine(0f, Px(12f));
                if (ImGui.Button(Loc.T("common.cancel"), Px(90f, 32f)))
                {
                    _onCancel?.Invoke();
                    _open = false;
                }

                ImGui.SameLine(0f, Px(24f));
                if (RotateButton(FontAwesomeIcon.UndoAlt, "##rotateLeft", Loc.T("common.rotate_left")))
                {
                    Rotate(RotateMode.Rotate270);
                }
                ImGui.SameLine(0f, Px(6f));
                if (RotateButton(FontAwesomeIcon.RedoAlt, "##rotateRight", Loc.T("common.rotate_right")))
                {
                    Rotate(RotateMode.Rotate90);
                }
            }
        }

        ImGui.End();
    }

    private static bool RotateButton(FontAwesomeIcon icon, string id, string tooltip)
    {
        bool clicked;
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            clicked = ImGui.Button($"{icon.ToIconString()}{id}", Px(32f, 32f));
        }
        HandOnHover();
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(tooltip);
        }
        return clicked;
    }

    private void Rotate(RotateMode mode)
    {
        if (_rotating)
        {
            return;
        }
        _rotating = true;
        var source = _path;
        _ = Task.Run(() =>
        {
            try
            {
                Directory.CreateDirectory(RotatedDir);
                var target = Path.Combine(RotatedDir, $"{Guid.NewGuid():N}.png");
                using var image = Image.Load(source);
                image.Mutate(x => x.Rotate(mode));
                image.SaveAsPng(target);
                _rotated = target;
            }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, "[ImageCropPopup] Rotate failed. Path={Path}", source);
                _rotating = false;
            }
        });
    }

    private static void PurgeRotated()
    {
        try
        {
            if (Directory.Exists(RotatedDir))
            {
                Directory.Delete(RotatedDir, true);
            }
        }
        catch (Exception)
        {
        }
    }
}
