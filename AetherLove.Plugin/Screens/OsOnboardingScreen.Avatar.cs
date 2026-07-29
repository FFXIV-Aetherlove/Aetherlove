using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Shared;
using AetherLove.Shared.Profile;
using AetherLove.UI;
using AetherLove.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Textures;

namespace AetherLove.Screens;

public sealed partial class OsOnboardingScreen
{
    private readonly FileDialogManager _fileDialog = new();
    private readonly ImageCropPopup _cropPopup = new();

    private string _avatarPath = string.Empty;
    private ISharedImmediateTexture? _avatarHandle;
    private Vector4 _avatarCropRect; // image-space (x, y, w, h)
    private bool _avatarConfirmed;

    /// <summary>The photo picker/preview portion of the combined profile step: a centred avatar circle (a camera
    /// placeholder until set), with pick-photo / selfie actions below. Mandatory to finish. Self-only, so no SFW gate
    /// is needed. The name field and the save error are drawn by the caller (DrawProfile).</summary>
    private void DrawAvatarSection()
    {
        var t = ThemeService.Current;
        var winW = ImGui.GetWindowSize().X;
        var dl = ImGui.GetWindowDrawList();
        var r = Px(52f);

        ImGui.Dummy(new Vector2(0f, Px(2f)));
        var cx = ImGui.GetWindowPos().X + winW * 0.5f;
        var cy = ImGui.GetCursorScreenPos().Y + r;

        var tex = _avatarConfirmed ? _avatarHandle?.GetWrapOrDefault() : null;
        if (tex != null)
        {
            var rc = _avatarCropRect;
            var uv0 = new Vector2(rc.X / tex.Width, rc.Y / tex.Height);
            var uv1 = new Vector2((rc.X + rc.Z) / tex.Width, (rc.Y + rc.W) / tex.Height);
            dl.AddImageRounded(tex.Handle, new Vector2(cx - r, cy - r), new Vector2(cx + r, cy + r),
                uv0, uv1, 0xFFFFFFFFu, r, ImDrawFlags.RoundCornersAll);
            dl.AddCircle(new Vector2(cx, cy), r, t.AccentU32, 64, Px(2.5f));
            var badgeC = new Vector2(cx + r * 0.72f, cy + r * 0.72f);
            dl.AddCircleFilled(badgeC, Px(13f), t.AccentU32, 24);
            dl.AddCircle(badgeC, Px(13f), 0xFF1E1E24u, 24, Px(2f));
            IconDraw.AddCentered(dl, FontAwesomeIcon.Camera, Px(12f), badgeC, 0xFFFFFFFFu);
        }
        else
        {
            dl.AddCircleFilled(new Vector2(cx, cy), r,
                ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.06f)), 64);
            dl.AddCircle(new Vector2(cx, cy), r,
                ImGui.ColorConvertFloat4ToU32(t.Accent with { W = 0.55f }), 64, Px(2f));
            IconDraw.AddCentered(dl, FontAwesomeIcon.Camera, Px(26f), new Vector2(cx, cy),
                ImGui.ColorConvertFloat4ToU32(t.AccentLight));
        }
        ImGui.SetCursorScreenPos(new Vector2(ImGui.GetWindowPos().X, cy + r + Px(10f)));

        if (!_avatarConfirmed)
        {
            var chooseW = Px(120f);
            var selfieW = Px(96f);
            var gap = Px(8f);
            ImGui.SetCursorPosX((winW - chooseW - selfieW - gap) * 0.5f);
            PushThemeButton(t);
            if (ImGui.Button(Loc.T("os_onboarding.avatar_choose"), new Vector2(chooseW, Px(32f))))
            {
                OpenAvatarFilePicker();
            }
            ImGui.SameLine(0f, gap);
            if (ImGui.Button(Loc.T("common.selfie"), new Vector2(selfieW, Px(32f))))
            {
                OpenAvatarSelfie();
            }
            PopThemeButton();
        }
        else
        {
            var changeW = Px(140f);
            ImGui.SetCursorPosX((winW - changeW) * 0.5f);
            PushThemeButton(t);
            if (ImGui.Button(Loc.T("os_onboarding.avatar_change"), new Vector2(changeW, Px(30f))))
            {
                _avatarConfirmed = false;
                _avatarPath = string.Empty;
                _avatarHandle = null;
            }
            PopThemeButton();
        }
    }

    private void OpenAvatarFilePicker()
    {
        _fileDialog.OpenFileDialog(
            title: Loc.T("onboarding.select_photo"),
            filters: $"{Loc.T("onboarding.image_files")}{{.png,.jpg,.jpeg,.bmp,.webp}}",
            callback: (ok, path) =>
            {
                if (!ok || _pendingPick.RejectUnavailableCloudFile(path))
                {
                    return;
                }
                HandleAvatarPicked(path);
            });
    }

    private void OpenAvatarSelfie() =>
        _selfieOverlay.Start(1.0f, PhotoSpec.AvatarSize, (path, crop) =>
        {
            // Pre-boot: the camera app cannot run yet, so the host stores the shot in the photo library directly.
            if (_cameraRoll.AutoImportAppCaptures)
            {
                _cameraRoll.AddCapture(path, crop);
            }
            HandleAvatarPicked(path, crop);
        });

    private void HandleAvatarPicked(string path, Vector4? presetCrop = null)
    {
        var handle = LoadPickedPreview(path);
        if (handle is null)
        {
            return;
        }
        _avatarPath = path;
        _avatarConfirmed = false;
        _avatarHandle = handle;

        if (presetCrop is { } preset)
        {
            _avatarCropRect = preset;
            _avatarConfirmed = true;
            return;
        }

        void Unload()
        {
            _avatarPath = string.Empty;
            _avatarHandle = null;
        }

        _pendingPick.Begin(handle, PhotoSpec.AvatarSize, PhotoSpec.AvatarSize,
            onValid: () => _cropPopup.Open(
                Loc.T("onboarding.crop_avatar"),
                handle,
                1.0f,
                cropRect => { _avatarCropRect = cropRect; _avatarConfirmed = true; },
                onCancel: Unload),
            onReject: Unload);
    }

    /// <summary>Reads the picked file + crop into an upload DTO (IO/CPU-bound; call off the UI thread), or null
    /// when the user skipped the avatar.</summary>
    private PhotoUploadDto? BuildAvatarUpload(string? path, Vector4 crop) =>
        path is { Length: > 0 }
            ? ReadPhotoUpload(path, crop, isNsfw: false, PhotoKind.Avatar)
            : null;

    private void ResetAvatarState()
    {
        _avatarPath = string.Empty;
        _avatarHandle = null;
        _avatarConfirmed = false;
    }
}
