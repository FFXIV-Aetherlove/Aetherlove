using System;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove.Services;
using AetherLove.Services.Localization;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;

namespace AetherLove.Screens;

public partial class OnboardingScreen
{

    private sealed class PhotoSlot
    {
        public string Path = "";
        public ISharedImmediateTexture? Handle;
        public Vector4 CropRect; // image-space (x, y, w, h)
        public bool Confirmed;
        public PhotoNsfwDecl Declaration;
        public bool FromServer;

        public void Clear()
        {
            Path = "";
            Handle = null;
            CropRect = default;
            Confirmed = false;
            Declaration = PhotoNsfwDecl.Unselected;
            FromServer = false;
        }
    }

    private static readonly string[] NsfwDeclLabels = PhotoModerationLabels.NsfwDeclOptions;

    private readonly PhotoSlot[] _photos =
        [new PhotoSlot(), new PhotoSlot(), new PhotoSlot(), new PhotoSlot()];
    private int _activePhotoSlot = -1;

    private bool _lalafellNsfwModalPending;
    private bool _undeclaredModalPending;

    private bool AnyConfirmedExtraIsUndeclared()
    {
        for (int i = 1; i < _photos.Length; i++)
        {
            if (_photos[i].Confirmed && _photos[i].Declaration == PhotoNsfwDecl.Unselected)
            {
                return true;
            }
        }
        return false;
    }

    private bool AllConfirmedExtrasDeclared()
    {
        for (int i = 1; i < _photos.Length; i++)
        {
            if (_photos[i].Confirmed && _photos[i].Declaration == PhotoNsfwDecl.Unselected)
            {
                return false;
            }
        }
        return true;
    }

    private int FirstUndeclaredExtra()
    {
        for (int i = 1; i < _photos.Length; i++)
        {
            if (_photos[i].Confirmed && _photos[i].Declaration == PhotoNsfwDecl.Unselected)
            {
                return i;
            }
        }
        return _activePhotoSlot;
    }

    private void FireRemovePhoto(PhotoSlot slot, int serverOrder)
    {
        var wasFromServer = slot.FromServer;
        slot.Clear();

        if (!wasFromServer)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await _hubClient.DeletePhotoAsync(serverOrder).ConfigureAwait(false);
            }
            catch (RateLimitException rl)
            {
                _rateLimitModal.Show(rl);
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, $"[Onboarding] DeletePhotoAsync(order={serverOrder}) failed.");
            }
        });
    }


    private void DrawStepPhotos()
    {
        var t = ThemeService.Current;
        var availW = ImGui.GetContentRegionAvail().X;
        var drawList = ImGui.GetWindowDrawList();

        ImGui.Spacing();
        ImGui.TextColored(t.AccentLight, Loc.T("onboarding.photos_heading"));
        ImGui.Spacing();
        ImGui.TextWrapped(Loc.T("onboarding.photos_intro"));
        ImGui.Spacing();

        var SlotW = Px(74f);
        var SlotH = SlotW * 1.6f; // 10:16 portrait
        var SlotGap = Px(6f);
        var totalW = 4 * SlotW + 3 * SlotGap;
        var slotsX = (availW - totalW) * 0.5f;
        var slotsY = ImGui.GetCursorPosY();

        for (int i = 0; i < 4; i++)
        {
            var slot   = _photos[i];
            var isMain = i == 0;
            ImGui.SetCursorPos(new Vector2(slotsX + i * (SlotW + SlotGap), slotsY));
            var sp = ImGui.GetCursorScreenPos();

            drawList.AddRectFilled(sp, sp + new Vector2(SlotW, SlotH), 0x33000000, Px(4f));
            var borderColor = i == _activePhotoSlot ? t.AccentU32
                : slot.Confirmed ? 0xFF44AA44
                : isMain         ? 0xFF997733
                : 0xFF555555;
            drawList.AddRect(sp, sp + new Vector2(SlotW, SlotH), borderColor, Px(4f), ImDrawFlags.None, Px(2f));

            var thumbTex = slot.Handle?.GetWrapOrDefault();
            if (thumbTex != null && slot.Confirmed)
            {
                var cr = slot.CropRect;
                var uv0 = new Vector2(cr.X / thumbTex.Width, cr.Y / thumbTex.Height);
                var uv1 = new Vector2((cr.X + cr.Z) / thumbTex.Width, (cr.Y + cr.W) / thumbTex.Height);
                drawList.AddImage(thumbTex.Handle, sp, sp + new Vector2(SlotW, SlotH), uv0, uv1);
            }
            else if (thumbTex != null)
            {
                drawList.AddImage(thumbTex.Handle, sp, sp + new Vector2(SlotW, SlotH), Vector2.Zero, Vector2.One, 0xAAFFFFFF);
            }
            else
            {
                var ph   = isMain ? Loc.T("onboarding.photos_main_thumb") : $"+{i}";
                var phSz = ImGui.CalcTextSize(ph);
                drawList.AddText(
                    sp + new Vector2((SlotW - phSz.X) * 0.5f, (SlotH - phSz.Y) * 0.5f),
                    isMain ? 0xFFBBAA44 : 0xFF888888, ph);
            }

            if (slot.Confirmed)
            {
                drawList.AddCircleFilled(sp + new Vector2(SlotW - Px(11f), Px(11f)), Px(9f), 0xFF33AA33);
                drawList.AddText(sp + new Vector2(SlotW - Px(15f), Px(3.5f)), 0xFFFFFFFF, "v");
            }

            ImGui.InvisibleButton($"##slot_{i}", new Vector2(SlotW, SlotH));
            if (ImGui.IsItemClicked())
            {
                // Block leaving while a confirmed extra is still undeclared; snap to the slot that needs it.
                if (i != _activePhotoSlot && AnyConfirmedExtraIsUndeclared())
                {
                    _undeclaredModalPending = true;
                    _activePhotoSlot = FirstUndeclaredExtra();
                }
                else
                {
                    _activePhotoSlot = _activePhotoSlot == i ? -1 : i;
                }
            }
        }

        ImGui.SetCursorPosY(slotsY + SlotH + Px(8f));
        ImGui.Separator(); ImGui.Spacing();

        if (_activePhotoSlot < 0)
        {
            ImGui.TextColored(new Vector4(0.52f, 0.52f, 0.52f, 0.85f),
                Loc.T("onboarding.photos_tap_slot"));
            return;
        }

        var active   = _photos[_activePhotoSlot];
        var mainSlot = _activePhotoSlot == 0;

        ImGui.TextColored(t.AccentLight, mainSlot ? Loc.T("onboarding.photos_main_photo") : Loc.T("onboarding.photos_extra_photo", _activePhotoSlot));
        ImGui.Spacing();

        if (active.Confirmed)
        {
            // SFW/NSFW selector goes above the preview.
            if (mainSlot)
            {
                ImGui.PushTextWrapPos(0f);
                ImGui.TextColored(new Vector4(0.95f, 0.45f, 0.45f, 1f),
                    Loc.T("onboarding.photos_main_sfw_warning"));
                ImGui.PopTextWrapPos();
            }
            else
            {
                ImGui.Text(Loc.T("onboarding.photos_sfw_nsfw_question"));
                var declIdx = (int)active.Declaration;
                ImGui.SetNextItemWidth(availW - Px(8f));
                if (ImGui.Combo($"##nsfwDecl{_activePhotoSlot}", ref declIdx, NsfwDeclLabels, NsfwDeclLabels.Length))
                {
                    var requested = (PhotoNsfwDecl)declIdx;
                    if (requested == PhotoNsfwDecl.Nsfw && IsLalafellSelected())
                    {
                        // Lalafell: NSFW forbidden. Reset to SFW + modal.
                        active.Declaration = PhotoNsfwDecl.Sfw;
                        _lalafellNsfwModalPending = true;
                    }
                    else
                    {
                        active.Declaration = requested;
                    }
                }
                ImGui.Spacing();
                ImGui.PushTextWrapPos(0f);
                ImGui.TextColored(new Vector4(0.95f, 0.45f, 0.45f, 1f),
                    Loc.T("onboarding.photos_mismatch_warning"));
                ImGui.PopTextWrapPos();
            }
            ImGui.Spacing();

            ImGui.TextColored(new Vector4(0.35f, 0.85f, 0.45f, 1f),
                active.FromServer ? Loc.T("onboarding.photos_previously_uploaded") : Loc.T("onboarding.photos_confirmed"));
            ImGui.SameLine(0f, Px(20f));
            if (ImGui.Button($"{Loc.T("onboarding.photos_remove")}##rm{_activePhotoSlot}", Px(80f, 24f)))
            {
                // Wizard index N → server Order N+1 (slot 0 is avatar).
                FireRemovePhoto(active, serverOrder: _activePhotoSlot + 1);
            }
            ImGui.Spacing();

            var pv = active.Handle?.GetWrapOrDefault();
            if (pv != null)
            {
                var cr = active.CropRect;
                var uv0 = new Vector2(cr.X / pv.Width, cr.Y / pv.Height);
                var uv1 = new Vector2((cr.X + cr.Z) / pv.Width, (cr.Y + cr.W) / pv.Height);
                var prevW = availW - Px(8f);
                var prevH = prevW * 1.6f;
                ImGui.SetCursorPosX(Px(4f));
                ImGui.Image(pv.Handle, new Vector2(prevW, prevH), uv0, uv1);
            }
        }
        else
        {
            PushThemeButton(t);
            if (ImGui.Button($"{Loc.T("onboarding.photos_browse")}##br{_activePhotoSlot}", Px(100f, 28f)))
            {
                if (AnyConfirmedExtraIsUndeclared())
                {
                    _undeclaredModalPending = true;
                }
                else
                {
                    _pickerTarget = _activePhotoSlot;
                    OpenFilePicker();
                }
            }
            PopThemeButton();

            if (active.Path.Length > 0)
            {
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.55f, 0.85f, 0.55f, 1f), Path.GetFileName(active.Path));
                ImGui.Spacing();
                ImGui.TextColored(new Vector4(0.52f, 0.52f, 0.52f, 0.85f),
                    Loc.T("onboarding.photos_crop_hint"));
            }
            else
            {
                ImGui.Spacing();
                ImGui.TextColored(new Vector4(0.52f, 0.52f, 0.52f, 0.85f),
                    mainSlot ? Loc.T("onboarding.photos_required") : Loc.T("onboarding.photos_optional"));
            }
        }

        PhotoModerationModals.DrawLalafellNsfwModal(ref _lalafellNsfwModalPending);
        PhotoModerationModals.DrawUndeclaredModal(ref _undeclaredModalPending);
    }
}
