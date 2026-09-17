using System;
using System.Numerics;
using AetherLove.Services;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures.TextureWraps;

namespace AetherLove.Widgets;

/// <summary>Displays an image with a draggable, resizable crop rectangle, either locked to a configured aspect
/// ratio or free when the ratio is zero or less, in which case the box starts as the whole image.</summary>
public class ImageCropWidget
{
    private readonly float _aspectRatio;
    private readonly bool _freeForm;

    private Vector2 _cropTopLeft = new(0, 0);
    private float _cropWidth = 0f;
    private float _cropHeight = 0f;

    private const float MinCropSide = 60f;
    private static float HandleSize => Px(20f);

    private bool _isDraggingMove;
    private Vector2 _moveDragOffset;
    private bool _isDraggingResize;
    private Vector2 _resizeDragStartMouse;
    private Vector2 _resizeDragStartSize;

    private IDalamudTextureWrap? _lastTexture;

    /// <param name="aspectRatio">cropHeight / cropWidth (1.6 for 10:16 portrait, 1.0 for square); zero or less
    /// for a free crop.</param>
    public ImageCropWidget(float aspectRatio = 1.6f)
    {
        _aspectRatio = aspectRatio;
        _freeForm = aspectRatio <= 0f;
    }

    private float CropHeight => _freeForm ? _cropHeight : _cropWidth * _aspectRatio;

    /// <summary>The crop rectangle in image-space: (x, y, width, height).</summary>
    public Vector4 CropRect => new(_cropTopLeft.X, _cropTopLeft.Y, _cropWidth, CropHeight);

    public void Draw(IDalamudTextureWrap? texture, float availableWidth)
    {
        if (texture == null)
        {
            ImGui.TextColored(new Vector4(1, 0.5f, 0.5f, 1), "No image loaded");
            return;
        }

        var imgAspect = (float)texture.Width / texture.Height;
        var displayWidth = availableWidth;
        var displayHeight = displayWidth / imgAspect;
        var scale = displayWidth / texture.Width;

        if (_cropWidth < 1f || !ReferenceEquals(_lastTexture, texture))
        {
            _lastTexture = texture;
            if (_freeForm)
            {
                _cropWidth = texture.Width;
                _cropHeight = texture.Height;
                _cropTopLeft = Vector2.Zero;
            }
            else
            {
                var maxByWidth = (float)texture.Width * 0.75f;
                var maxByHeight = texture.Height / _aspectRatio * 0.75f;
                _cropWidth = Math.Min(maxByWidth, maxByHeight);
                _cropTopLeft = new Vector2(
                    (texture.Width - _cropWidth) * 0.5f,
                    (texture.Height - _cropWidth * _aspectRatio) * 0.5f);
            }
        }

        var origin = ImGui.GetCursorScreenPos();
        var imageEnd = origin + new Vector2(displayWidth, displayHeight);

        ImGui.Image(texture.Handle, new Vector2(displayWidth, displayHeight));

        var cropTL = origin + _cropTopLeft * scale;
        var cropSz = new Vector2(_cropWidth, CropHeight) * scale;
        var cropBR = cropTL + cropSz;

        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(origin, new Vector2(imageEnd.X, cropTL.Y), 0x88000000);
        dl.AddRectFilled(new Vector2(origin.X, cropTL.Y), new Vector2(cropTL.X, cropBR.Y), 0x88000000);
        dl.AddRectFilled(new Vector2(cropBR.X, cropTL.Y), new Vector2(imageEnd.X, cropBR.Y), 0x88000000);
        dl.AddRectFilled(new Vector2(origin.X, cropBR.Y), imageEnd, 0x88000000);

        dl.AddRect(cropTL, cropBR, 0xFFFFFFFF, 0f, ImDrawFlags.None, 2f);
        float t = Px(12f);
        dl.AddLine(cropTL, cropTL + new Vector2(t, 0), 0xFFFFFFFF, 3f);
        dl.AddLine(cropTL, cropTL + new Vector2(0, t), 0xFFFFFFFF, 3f);
        dl.AddLine(new Vector2(cropBR.X, cropTL.Y), new Vector2(cropBR.X - t, cropTL.Y), 0xFFFFFFFF, 3f);
        dl.AddLine(new Vector2(cropBR.X, cropTL.Y), new Vector2(cropBR.X, cropTL.Y + t), 0xFFFFFFFF, 3f);
        dl.AddLine(new Vector2(cropTL.X, cropBR.Y), new Vector2(cropTL.X + t, cropBR.Y), 0xFFFFFFFF, 3f);
        dl.AddLine(new Vector2(cropTL.X, cropBR.Y), new Vector2(cropTL.X, cropBR.Y - t), 0xFFFFFFFF, 3f);
        var handleTL = cropBR - new Vector2(HandleSize, HandleSize);
        dl.AddRectFilled(handleTL, cropBR, ThemeService.Current.AccentU32);
        dl.AddRect(handleTL, cropBR, 0xFFFFFFFF, 0f, ImDrawFlags.None, 2f);
        var hc = (handleTL + cropBR) * 0.5f;
        var S = Px(4.5f);
        dl.AddLine(hc - new Vector2(S, S), hc + new Vector2(S, S), 0xFFFFFFFF, 1.5f);
        dl.AddLine(hc + new Vector2(S, S), hc + new Vector2(Px(1f), S), 0xFFFFFFFF, 1.5f);
        dl.AddLine(hc + new Vector2(S, S), hc + new Vector2(S, Px(1f)), 0xFFFFFFFF, 1.5f);

        ImGui.SetCursorScreenPos(handleTL);
        ImGui.InvisibleButton("##cropResize", new Vector2(HandleSize, HandleSize));
        if (ImGui.IsItemActive())
        {
            if (!_isDraggingResize)
            {
                _isDraggingResize = true;
                _resizeDragStartMouse = ImGui.GetMousePos();
                _resizeDragStartSize = new Vector2(_cropWidth, CropHeight);
            }
            var delta = (ImGui.GetMousePos() - _resizeDragStartMouse) / scale;
            if (_freeForm)
            {
                var maxW = texture.Width - _cropTopLeft.X;
                var maxH = texture.Height - _cropTopLeft.Y;
                _cropWidth = MathF.Min(MathF.Max(_resizeDragStartSize.X + delta.X, MinCropSide), maxW);
                _cropHeight = MathF.Min(MathF.Max(_resizeDragStartSize.Y + delta.Y, MinCropSide), maxH);
            }
            else
            {
                var newWidth = Math.Max(MinCropSide, _resizeDragStartSize.X + delta.X);
                var newHeight = newWidth * _aspectRatio;
                newWidth = Math.Min(newWidth, texture.Width - _cropTopLeft.X);
                newHeight = Math.Min(newHeight, texture.Height - _cropTopLeft.Y);
                newWidth = Math.Min(newWidth, newHeight / _aspectRatio);
                if (Math.Abs(newWidth - _cropWidth) > 0.1f)
                {
                    _cropWidth = newWidth;
                }
            }
        }
        else
        {
            _isDraggingResize = false;
        }

        ImGui.SetCursorScreenPos(cropTL);
        ImGui.InvisibleButton("##cropMove", cropSz - new Vector2(HandleSize, HandleSize));
        if (ImGui.IsItemActive())
        {
            if (!_isDraggingMove)
            {
                _isDraggingMove = true;
                _moveDragOffset = ImGui.GetMousePos() - cropTL;
            }
            var newTL = (ImGui.GetMousePos() - origin - _moveDragOffset) / scale;
            newTL.X = Math.Clamp(newTL.X, 0, Math.Max(0f, texture.Width - _cropWidth));
            newTL.Y = Math.Clamp(newTL.Y, 0, Math.Max(0f, texture.Height - CropHeight));
            if (newTL != _cropTopLeft)
            {
                _cropTopLeft = newTL;
            }
        }
        else
        {
            _isDraggingMove = false;
        }

        ImGui.SetCursorScreenPos(new Vector2(origin.X, imageEnd.Y + Px(6)));
    }
}
