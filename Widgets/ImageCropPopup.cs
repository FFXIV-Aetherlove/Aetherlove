using System;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;

namespace AetherLove.Widgets;

/// <summary>Resizable popup window that wraps an <see cref="ImageCropWidget"/>.</summary>
public class ImageCropPopup
{
    private bool _open;
    private string _title = "";
    private ISharedImmediateTexture? _handle;
    private ImageCropWidget _crop = new(1.0f);
    private Action<Vector4>? _onConfirm;
    private Action? _onCancel;

    /// <param name="aspectRatio">cropHeight / cropWidth (1.0 square, 1.6 for 10:16 portrait).</param>
    /// <param name="onConfirm">Receives image-space crop rect (x, y, w, h) on confirm.</param>
    public void Open(
        string title,
        ISharedImmediateTexture handle,
        float aspectRatio,
        Action<Vector4> onConfirm,
        Action? onCancel = null)
    {
        _title = title;
        _handle = handle;
        _crop = new ImageCropWidget(aspectRatio);
        _onConfirm = onConfirm;
        _onCancel = onCancel;
        _open = true;
    }

    public void Draw(Vector2 parentPos, Vector2 parentSize)
    {
        if (!_open)
        {
            return;
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
            var tex = _handle?.GetWrapOrDefault();

            if (tex == null)
            {
                ImGui.Spacing();
                ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.5f, 1f), Loc.T("common.loading_image"));
            }
            else
            {
                var ButtonRowH = Px(56f);
                var avail = ImGui.GetContentRegionAvail();
                var cropW = avail.X - Px(8f);
                var imgAspect = (float)tex.Width / tex.Height;
                var maxH = avail.Y - ButtonRowH;
                if (maxH > 0f)
                {
                    cropW = System.Math.Min(cropW, maxH * imgAspect);
                }

                _crop.Draw(tex, cropW);

                var t = ThemeService.Current;
                ImGui.Spacing();
                ImGui.PushStyleColor(ImGuiCol.Button, t.ButtonNormal);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, t.ButtonHovered);
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, t.ButtonActive);
                if (ImGui.Button(Loc.T("common.use_this_crop"), Px(150f, 32f)))
                {
                    _onConfirm?.Invoke(_crop.CropRect);
                    _open = false;
                }
                ImGui.PopStyleColor(3);

                ImGui.SameLine(0f, Px(12f));
                if (ImGui.Button(Loc.T("common.cancel"), Px(90f, 32f)))
                {
                    _onCancel?.Invoke();
                    _open = false;
                }
            }
        }

        ImGui.End();
    }
}
