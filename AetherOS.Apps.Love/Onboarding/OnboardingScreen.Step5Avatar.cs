using System;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using static AetherLove.UI.OnboardingUi;

namespace AetherLove.Screens;

public partial class OnboardingScreen
{
    private string _avatarPath = "";
    private ISharedImmediateTexture? _avatarHandle;
    private Vector4 _avatarCropRect; // image-space (x, y, w, h)
    private bool _avatarConfirmed;
    private bool _avatarFromServer;

    private void FireRemoveAvatar()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await _hubClient.DeletePhotoAsync(0).ConfigureAwait(false);
            }
            catch (RateLimitException rl)
            {
                _rateLimitModal.Show(rl);
            }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, "[Onboarding] DeletePhotoAsync(order=0) failed for avatar.");
            }
        });
    }


    /// <summary>Reuses the account's OS avatar as the dating-profile avatar by writing the WebP to a temp file and confirming it.</summary>
    private void UseOsAvatar()
    {
        var bytes = _bootstrap.LastAccount?.OsAvatarWebp;
        if (bytes is not { Length: > 0 })
        {
            return;
        }
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "aetherlove_osavatar");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "os_avatar.webp");
            File.WriteAllBytes(path, bytes);

            _avatarPath = path;
            _avatarHandle = LoadPickedPreview(path);
            _avatarFromServer = false;
            _avatarCropRect = new Vector4(0f, 0f, AetherLove.Shared.PhotoSpec.AvatarSize, AetherLove.Shared.PhotoSpec.AvatarSize);
            _avatarConfirmed = true;
        }
        catch (Exception ex)
        {
            UiHost.Log.Warning(ex, "[Onboarding] Reusing the OS avatar as the dating avatar failed.");
        }
    }

    private void DrawStepAvatar()
    {
        DrawHero("love_avatar", FontAwesomeIcon.Camera, Loc.T("onboarding.hero_avatar_title"),
            Loc.T("onboarding.hero_avatar_sub"), 30f);

        var t = ThemeService.Current;
        var winW = ImGui.GetWindowSize().X;
        var dl = ImGui.GetWindowDrawList();

        DrawInfoCallout(Loc.T("onboarding.avatar_sfw_warning"), UiColors.Danger, FontAwesomeIcon.ExclamationTriangle);
        ImGui.Dummy(new Vector2(0f, Px(14f)));

        var r = Px(60f);
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
        }
        else
        {
            dl.AddCircleFilled(new Vector2(cx, cy), r,
                ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.06f)), 64);
            dl.AddCircle(new Vector2(cx, cy), r,
                ImGui.ColorConvertFloat4ToU32(t.Accent with { W = 0.55f }), 64, Px(2f));
            IconDraw.AddCentered(dl, FontAwesomeIcon.Camera, Px(30f), new Vector2(cx, cy),
                ImGui.ColorConvertFloat4ToU32(t.AccentLight));
        }
        ImGui.SetCursorScreenPos(new Vector2(ImGui.GetWindowPos().X, cy + r + Px(14f)));

        if (!_avatarConfirmed)
        {
            var hasOsAvatar = _bootstrap.LastAccount?.OsAvatarWebp is { Length: > 0 };
            PushThemeButton(t);
            if (hasOsAvatar)
            {
                var osW = Px(200f);
                ImGui.SetCursorPosX((winW - osW) * 0.5f);
                if (ImGui.Button(Loc.T("onboarding.avatar_use_os"), new Vector2(osW, Px(32f))))
                {
                    UseOsAvatar();
                }
                ImGui.Dummy(new Vector2(0f, Px(8f)));
            }

            var browseW = Px(120f);
            var selfieW = Px(96f);
            var gap = Px(8f);
            ImGui.SetCursorPosX((winW - browseW - selfieW - gap) * 0.5f);
            if (ImGui.Button(Loc.T("onboarding.avatar_browse"), new Vector2(browseW, Px(32f))))
            {
                _pickerTarget = -1;
                OpenFilePicker();
            }
            ImGui.SameLine(0f, gap);
            if (ImGui.Button(Loc.T("common.selfie"), new Vector2(selfieW, Px(32f))))
            {
                _pickerTarget = -1;
                OpenSelfie();
            }
            PopThemeButton();

            if (_avatarPath.Length > 0)
            {
                ImGui.Dummy(new Vector2(0f, Px(8f)));
                DrawCenteredParagraph(Loc.T("onboarding.avatar_crop_hint"), winW - Px(48f),
                    UiColors.Muted with { W = 0.80f });
            }
        }
        else
        {
            var changeW = Px(140f);
            ImGui.SetCursorPosX((winW - changeW) * 0.5f);
            PushThemeButton(t);
            if (ImGui.Button(Loc.T("onboarding.avatar_change_photo"), new Vector2(changeW, Px(30f))))
            {
                if (_avatarFromServer)
                {
                    FireRemoveAvatar();
                }
                _avatarConfirmed = false;
                _avatarPath = "";
                _avatarHandle = null;
                _avatarFromServer = false;
            }
            PopThemeButton();
        }
    }
}
