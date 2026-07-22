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
                UiHost.Log.Warning(ex, $"[Onboarding] DeletePhotoAsync(order={serverOrder}) failed.");
            }
        });
    }


    private void DrawStepPhotos()
    {
        DrawHero("love_photos", FontAwesomeIcon.Images, Loc.T("onboarding.hero_photos_title"),
            Loc.T("onboarding.hero_photos_sub"), 30f);

        var t = ThemeService.Current;
        var availW = ImGui.GetContentRegionAvail().X;
        var drawList = ImGui.GetWindowDrawList();

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

            drawList.AddRectFilled(sp, sp + new Vector2(SlotW, SlotH), UiColors.PhotoSlotFill, Px(4f));
            var borderColor = i == _activePhotoSlot ? t.AccentU32
                : slot.Confirmed ? UiColors.PhotoSlotStoredBorder
                : isMain ? UiColors.PhotoSlotMainBorder
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
                    isMain ? UiColors.PhotoSlotMainLabel : UiColors.TextMuted, ph);
            }

            // Delete badge submitted first so it wins clicks (ImGui: first-submitted overlapping item wins).
            var deleted = false;
            if (slot.Confirmed)
            {
                var badgeR = Px(11f);
                var badgeC = sp + new Vector2(SlotW - badgeR - Px(3f), badgeR + Px(3f));
                ImGui.SetCursorScreenPos(badgeC - new Vector2(badgeR, badgeR));
                if (ImGui.InvisibleButton($"##del_{i}", new Vector2(badgeR * 2f, badgeR * 2f)))
                {
                    FireRemovePhoto(slot, serverOrder: i + 1);
                    if (_activePhotoSlot == i)
                    {
                        _activePhotoSlot = -1;
                    }
                    deleted = true;
                }
                var delHover = ImGui.IsItemHovered();
                drawList.AddCircleFilled(badgeC, badgeR,
                    delHover ? ImGui.ColorConvertFloat4ToU32(UiColors.Danger)
                             : ImGui.ColorConvertFloat4ToU32(new Vector4(0.10f, 0.10f, 0.12f, 0.85f)));
                IconDraw.AddCentered(drawList, FontAwesomeIcon.TrashAlt, Px(11f), badgeC, 0xFFFFFFFFu);
            }

            ImGui.SetCursorScreenPos(sp);
            ImGui.InvisibleButton($"##slot_{i}", new Vector2(SlotW, SlotH));
            if (!deleted && ImGui.IsItemClicked())
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
            ImGui.TextColored(UiColors.Hint,
                Loc.T("onboarding.photos_tap_slot"));
            return;
        }

        var active   = _photos[_activePhotoSlot];
        var mainSlot = _activePhotoSlot == 0;

        ImGui.TextColored(t.AccentLight, mainSlot ? Loc.T("onboarding.photos_main_photo") : Loc.T("onboarding.photos_extra_photo", _activePhotoSlot));
        ImGui.Spacing();

        if (active.Confirmed)
        {
            if (mainSlot)
            {
                ImGui.PushTextWrapPos(0f);
                ImGui.TextColored(UiColors.Danger,
                    Loc.T("onboarding.photos_main_sfw_warning"));
                ImGui.PopTextWrapPos();
            }
            else
            {
                ImGui.Text(Loc.T("onboarding.photos_sfw_nsfw_question"));
                ImGui.Spacing();
                var gap = Px(8f);
                var choiceW = (availW - Px(8f) - gap) * 0.5f;
                var choiceH = Px(34f);
                if (DrawDeclChoice(Loc.T("onboarding.photos_decl_sfw"), active.Declaration == PhotoNsfwDecl.Sfw,
                        choiceW, choiceH, UiColors.Success, "sfw"))
                {
                    active.Declaration = PhotoNsfwDecl.Sfw;
                }
                ImGui.SameLine(0f, gap);
                if (DrawDeclChoice(Loc.T("onboarding.photos_decl_nsfw"), active.Declaration == PhotoNsfwDecl.Nsfw,
                        choiceW, choiceH, UiColors.Danger, "nsfw"))
                {
                    if (IsLalafellSelected())
                    {
                        active.Declaration = PhotoNsfwDecl.Sfw;
                        _lalafellNsfwModalPending = true;
                    }
                    else
                    {
                        active.Declaration = PhotoNsfwDecl.Nsfw;
                    }
                }
                ImGui.Spacing();
                ImGui.PushTextWrapPos(0f);
                ImGui.TextColored(UiColors.Danger,
                    Loc.T("onboarding.photos_mismatch_warning"));
                ImGui.PopTextWrapPos();
            }
            ImGui.Spacing();

            ImGui.TextColored(UiColors.Success,
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
                TryPickPhoto(OpenFilePicker);
            }
            ImGui.SameLine();
            if (ImGui.Button($"{Loc.T("common.selfie")}##sf{_activePhotoSlot}", Px(90f, 28f)))
            {
                TryPickPhoto(OpenSelfie);
            }
            PopThemeButton();

            if (active.Path.Length > 0)
            {
                ImGui.Spacing();
                ImGui.TextColored(UiColors.SuccessSoft, Path.GetFileName(active.Path));
                ImGui.Spacing();
                ImGui.TextColored(UiColors.Hint,
                    Loc.T("onboarding.photos_crop_hint"));
            }
            else
            {
                ImGui.Spacing();
                ImGui.TextColored(UiColors.Hint,
                    mainSlot ? Loc.T("onboarding.photos_required") : Loc.T("onboarding.photos_optional"));
            }
        }

        PhotoModerationModals.DrawLalafellNsfwModal(ref _lalafellNsfwModalPending);
        PhotoModerationModals.DrawUndeclaredModal(ref _undeclaredModalPending);
    }

    /// <summary>Renders one SFW/NSFW choice segment; returns true when clicked.</summary>
    private static bool DrawDeclChoice(string label, bool selected, float w, float h, Vector4 accent, string id)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, selected ? accent : new Vector4(1f, 1f, 1f, 0.06f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, selected ? accent : new Vector4(1f, 1f, 1f, 0.12f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, selected ? accent : new Vector4(1f, 1f, 1f, 0.16f));
        ImGui.PushStyleColor(ImGuiCol.Text,
            selected ? new Vector4(1f, 1f, 1f, 1f) : new Vector4(0.80f, 0.80f, 0.82f, 1f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
        var clicked = ImGui.Button($"{label}##decl_{id}", new Vector2(w, h));
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(4);
        return clicked;
    }
}
