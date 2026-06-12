using System;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;

namespace AetherLove.Screens;

public partial class OnboardingScreen
{

    private string _avatarPath = "";
    private ISharedImmediateTexture? _avatarHandle;
    private Vector4 _avatarCropRect; // image-space (x, y, w, h)
    private bool _avatarConfirmed;
    private bool _avatarFromServer;

    private float _matchAnimElapsed;

    /// <summary>Heartbeat-pulse easing, peaking at dt = 0: a sharp Gaussian rise before the beat (dt &lt; 0)
    /// and a slower exponential fall after it (dt &gt; 0). Two offset calls make the "lub-dub" double thump.
    /// Same curve the splash screen uses.</summary>
    private static float AsymPeakAvatar(float dt)
        => dt < 0f ? MathF.Exp(-80f * dt * dt) : MathF.Exp(-6f * dt);

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
                Plugin.Log.Warning(ex, "[Onboarding] DeletePhotoAsync(order=0) failed for avatar.");
            }
        });
    }


    private void DrawStepAvatar()
    {
        if (!_imageDisclaimerAcknowledged)
        {
            DrawImageDisclaimer();
            return;
        }

        var t = ThemeService.Current;
        var muted = UiColors.Muted with { W = 0.80f };

        DrawSectionHeading(Loc.T("onboarding.avatar_heading"), t);
        ImGui.TextWrapped(Loc.T("onboarding.avatar_intro"));
        ImGui.Spacing();
        ImGui.PushTextWrapPos(0f);
        ImGui.TextColored(muted, Loc.T("onboarding.avatar_tip"));
        ImGui.PopTextWrapPos();
        ImGui.Spacing();
        ImGui.PushTextWrapPos(0f);
        ImGui.TextColored(UiColors.Danger,
            Loc.T("onboarding.avatar_sfw_warning"));
        ImGui.PopTextWrapPos();
        ImGui.Spacing();
        ImGui.Spacing();

        if (!_avatarConfirmed)
        {
            _matchAnimElapsed = 0f;

            PushThemeButton(t);
            if (ImGui.Button(Loc.T("onboarding.avatar_browse"), Px(100f, 28f)))
            { _pickerTarget = -1; OpenFilePicker(); }
            PopThemeButton();

            if (_avatarPath.Length > 0)
            {
                ImGui.SameLine();
                ImGui.TextColored(UiColors.SuccessSoft, Path.GetFileName(_avatarPath));
                ImGui.Spacing();
                ImGui.TextColored(muted, Loc.T("onboarding.avatar_crop_hint"));
            }
            else
            {
                ImGui.Spacing();
                ImGui.TextColored(muted, Loc.T("onboarding.avatar_no_image"));
            }
        }
        else
        {
            _matchAnimElapsed += ImGui.GetIO().DeltaTime;

            ImGui.TextColored(UiColors.Success, Loc.T("onboarding.avatar_set"));
            ImGui.Spacing();

            var tex = _avatarHandle?.GetWrapOrDefault();
            if (tex != null)
            {
                var r = _avatarCropRect;
                var uv0 = new Vector2(r.X / tex.Width, r.Y / tex.Height);
                var uv1 = new Vector2((r.X + r.Z) / tex.Width, (r.Y + r.W) / tex.Height);
                ImGui.Image(tex.Handle, Px(120f, 120f), uv0, uv1);
            }

            ImGui.Spacing();
            if (ImGui.Button(Loc.T("onboarding.avatar_change_photo"), Px(120f, 28f)))
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

            if (tex != null && _matchAnimElapsed >= 2f)
            {
                DrawMatchPreviewCard(tex, t);
            }
        }
    }


    /// <summary>Teaser shown a couple of seconds after the user confirms their avatar: a faux "It's a Match!"
    /// card that fades in, sets their avatar beside a mystery "?" partner, and gives the avatar circle a
    /// heartbeat pulse — a preview of what a real match looks like. Purely cosmetic; nothing is saved.</summary>
    private void DrawMatchPreviewCard(Dalamud.Interface.Textures.TextureWraps.IDalamudTextureWrap tex,
                                      ThemeDefinition t)
    {
        ImGui.Spacing();
        ImGui.Spacing();

        // Card colours, RGB only; the alpha byte is filled in per-element as the card fades in.
        const uint cardBgRgb = 0x000010;      // near-black
        const uint mysteryDiscRgb = 0x1A1A2E; // dark navy disc behind the "?"
        const uint mysteryTextRgb = 0xAAAAAA; // grey "?"
        const uint captionRgb = 0x888888;     // dim caption

        // Card appears at confirm + 2s; the heartbeat fires ~1s after that.
        var cardElapsed = _matchAnimElapsed - 2f;
        var animElapsed = cardElapsed - 1f;

        var lub = AsymPeakAvatar(animElapsed - 0.30f);
        var dub = AsymPeakAvatar(animElapsed - 0.43f) * 0.65f;
        var pulse = AccessibilityService.ReduceMotion ? 0f : MathF.Max(lub, dub);

        var fadeIn = (float)System.Math.Clamp(cardElapsed / 0.5f, 0.0, 1.0);

        var baseCircleR = Px(38f);
        var pulseSwellR = Px(6f);
        var circleR = baseCircleR + pulse * pulseSwellR;

        var centerGap = Px(108f);
        var padH = Px(14f);
        var titleH = Px(18f);
        var gapAfterTitle = Px(10f);
        var circleAreaH = baseCircleR * 2f + pulseSwellR * 2f;
        var gapAfterCirc = Px(8f);
        var captionH = Px(14f);
        var cardH = padH + titleH + gapAfterTitle + circleAreaH + gapAfterCirc + captionH + padH;

        var availW = ImGui.GetContentRegionAvail().X;
        var cardMin = ImGui.GetCursorScreenPos();
        var cardMax = cardMin + new Vector2(availW, cardH);
        var dl = ImGui.GetWindowDrawList();
        var cx = cardMin.X + availW * 0.5f;
        var circleY = cardMin.Y + padH + titleH + gapAfterTitle + circleR + pulseSwellR;

        var leftCenter = new Vector2(cx - centerGap * 0.5f, circleY);
        var rightCenter = new Vector2(cx + centerGap * 0.5f, circleY);

        var borderAlpha = (uint)((0x88 + (int)(pulse * 0x77)) & 0xFF);
        var borderCol = (borderAlpha << 24) | (t.AccentLightU32 & 0x00FFFFFF);
        var bgAlpha = (uint)(fadeIn * 0xCC);
        dl.AddRectFilled(cardMin, cardMax, (bgAlpha << 24) | cardBgRgb, Px(10f));
        dl.AddRect(cardMin, cardMax, borderCol, Px(10f), ImDrawFlags.None, 1.5f);

        var title = Loc.T("onboarding.avatar_its_a_match");
        var titleSz = ImGui.CalcTextSize(title);
        var titlePos = new Vector2(cx - titleSz.X * 0.5f, cardMin.Y + padH);
        var titleAlpha = (uint)(fadeIn * 0xFF);
        var accentRgb = t.AccentLightU32 & 0x00FFFFFF;
        dl.AddText(titlePos, (titleAlpha << 24) | accentRgb, title);

        var r = _avatarCropRect;
        var uv0 = new Vector2(r.X / tex.Width, r.Y / tex.Height);
        var uv1 = new Vector2((r.X + r.Z) / tex.Width, (r.Y + r.W) / tex.Height);
        var imgA = (uint)(fadeIn * 0xFF);
        dl.AddImageRounded(tex.Handle,
            leftCenter - new Vector2(circleR, circleR),
            leftCenter + new Vector2(circleR, circleR),
            uv0, uv1,
            (imgA << 24) | 0xFFFFFF,
            circleR);
        dl.AddCircle(leftCenter, circleR, (borderAlpha << 24) | 0xFFFFFF, 0, 2f);

        var qBgAlpha = (uint)(fadeIn * 0xCC);
        dl.AddCircleFilled(rightCenter, circleR, (qBgAlpha << 24) | mysteryDiscRgb);
        dl.AddCircle(rightCenter, circleR, (borderAlpha << 24) | 0xFFFFFF, 0, 2f);

        var qText = "?";
        var qSz = ImGui.CalcTextSize(qText);
        var qAlpha = (uint)(fadeIn * 0xCC);
        dl.AddText(rightCenter - qSz * 0.5f, (qAlpha << 24) | mysteryTextRgb, qText);

        var caption = Loc.T("onboarding.avatar_match_preview_caption");
        var captionSz = ImGui.CalcTextSize(caption);
        var capAlpha = (uint)(fadeIn * 0x99);
        if (captionSz.X < availW - Px(8f))
        {
            var captionPos = new Vector2(cx - captionSz.X * 0.5f, cardMax.Y - padH - captionH + Px(2f));
            dl.AddText(captionPos, (capAlpha << 24) | captionRgb, caption);
        }

        ImGui.SetCursorScreenPos(new Vector2(cardMin.X, cardMax.Y + Px(8f)));
    }
}
