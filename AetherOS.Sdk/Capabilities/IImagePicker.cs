using System;
using System.Numerics;
using Dalamud.Interface.Textures;

namespace AetherOS.Sdk;

/// <summary>A plain disk-image pick.</summary>
/// <param name="Title">File-dialog title.</param>
/// <param name="Filters">File-dialog filter string, e.g. "Images{.png,.jpg,.jpeg}".</param>
public readonly record struct ImagePickRequest(string Title, string Filters);

/// <summary>A disk-image pick that must clear a minimum size and then be cropped.</summary>
/// <param name="Title">File-dialog title.</param>
/// <param name="Filters">File-dialog filter string.</param>
/// <param name="CropTitle">Title of the crop popup.</param>
/// <param name="Aspect">cropHeight / cropWidth for the crop (1.0 square, 1.6 for 10:16 portrait).</param>
/// <param name="MinWidth">Smallest acceptable source width in pixels.</param>
/// <param name="MinHeight">Smallest acceptable source height in pixels.</param>
public readonly record struct ImageCropRequest(
    string Title, string Filters, string CropTitle, float Aspect, int MinWidth, int MinHeight);

/// <summary>A cropped image: the source path, its preview texture, and the image-space crop rect (x, y, w, h).</summary>
public readonly record struct CroppedImage(string Path, ISharedImmediateTexture Preview, Vector4 Crop);

/// <summary>Disk image picking. The shell owns the shared file dialog + crop popup and draws them each frame,
/// so apps do not host their own.</summary>
public interface IImagePicker
{
    /// <summary>Opens a file dialog; <paramref name="onPicked"/> fires with the chosen path.</summary>
    void PickFile(ImagePickRequest request, Action<string> onPicked);

    /// <summary>Opens a file dialog, validates the minimum size, then a crop popup;
    /// <paramref name="onPicked"/> fires once the user confirms the crop.</summary>
    void PickAndCrop(ImageCropRequest request, Action<CroppedImage> onPicked);
}
