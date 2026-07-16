using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Shared;
using AetherLove.Shared.Profile;
using AetherLove.UI;
using AetherLove.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;

namespace AetherLove.Screens;

public partial class MyProfileScreen
{

    private sealed class ImgPhotoSlot
    {
        public string Path = "";
        public ISharedImmediateTexture? Handle;
        public Vector4 CropRect;
        public bool Confirmed;
        public bool PendingRemove;
        /// <summary>Per-photo SFW/NSFW declaration for newly-uploaded extras. Unused for main / avatar.</summary>
        public PhotoNsfwDecl Declaration;

        public void Clear()
        {
            Path = ""; Handle = null; CropRect = default;
            Confirmed = false; PendingRemove = false;
            Declaration = PhotoNsfwDecl.Unselected;
        }
    }


    private readonly FileDialogManager _imgFileDialog = new();
    private readonly ImageCropPopup _imgCropPopup = new();
    private readonly SfwImageGateModal _imgSfwGate = new();
    // -1 = avatar, 0..N = photo slot (0 = main portrait, 1..N = extras; N = supporter max)
    private int _imgPickerTarget;

    private string _imgAvatarPath = "";
    private ISharedImmediateTexture? _imgAvatarHandle;
    private Vector4 _imgAvatarCropRect;
    private bool _imgAvatarConfirmed;

    private readonly ImgPhotoSlot[] _imgPhotoSlots =
        [.. Enumerable.Range(0, 1 + SupporterLimits.SupporterExtraPhotos).Select(_ => new ImgPhotoSlot())];
    private int _imgActiveSlot = -1;

    /// <summary>Extra-photo allowance from the connection snapshot (the server re-validates regardless).</summary>
    private int MaxExtraSlots => SupporterLimits.MaxExtraPhotos(_bootstrap.LastConnection?.IsSupporter == true);

    /// <summary>Slots to draw: main + allowed extras, plus any over-allowance slot still holding a server
    /// photo, so a lapsed supporter can remove (but not refill) those.</summary>
    private int VisibleSlotCount
    {
        get
        {
            var count = 1 + MaxExtraSlots;
            for (int i = count; i < _imgPhotoSlots.Length; i++)
            {
                if (SlotHasServerPhoto(i))
                {
                    count = i + 1;
                }
            }
            return count;
        }
    }

    /// <summary>Set when a Lalafell user attempts to declare an extra NSFW. Drives the policy modal.</summary>
    private bool _imgLalafellNsfwModalPending;
    /// <summary>Set when the user tries to upload a new photo while another confirmed extra remains undeclared.</summary>
    private bool _imgUndeclaredModalPending;

    private bool AnyConfirmedExtraIsUndeclared()
    {
        // Slot 0 is the main portrait and is implicitly SFW; only extras (1..3) need a declaration.
        for (int i = 1; i < _imgPhotoSlots.Length; i++)
        {
            if (_imgPhotoSlots[i].Confirmed && _imgPhotoSlots[i].Declaration == PhotoNsfwDecl.Unselected)
            {
                return true;
            }
        }
        return false;
    }

    private bool AllConfirmedExtrasDeclared()
    {
        for (int i = 1; i < _imgPhotoSlots.Length; i++)
        {
            if (_imgPhotoSlots[i].Confirmed && _imgPhotoSlots[i].Declaration == PhotoNsfwDecl.Unselected)
            {
                return false;
            }
        }
        return true;
    }

    private int FirstUndeclaredImgExtra()
    {
        for (int i = 1; i < _imgPhotoSlots.Length; i++)
        {
            if (_imgPhotoSlots[i].Confirmed && _imgPhotoSlots[i].Declaration == PhotoNsfwDecl.Unselected)
            {
                return i;
            }
        }
        return _imgActiveSlot;
    }


    private bool HasUnsavedChanges
    {
        get
        {
            if (_imgAvatarConfirmed)
            {
                return true;
            }
            foreach (var s in _imgPhotoSlots)
            {
                if (s.Confirmed || s.PendingRemove)
                {
                    return true;
                }
            }
            return false;
        }
    }


    /// <summary>Pushes pending image changes to the server, then re-hydrates from server truth.</summary>
    private void CommitImages()
    {
        if (_committingImages)
        {
            return;
        }
        _committingImages = true;
        var ct = _cts.Token;

        // Snapshot the upload inputs on the UI thread; the heavy decode/resize/encode runs off-thread below.
        var avatarConfirmed = _imgAvatarConfirmed;
        var avatarPath = _imgAvatarPath;
        var avatarCrop = _imgAvatarCropRect;
        var slotInputs = new (bool Confirmed, string Path, Vector4 Crop, bool IsNsfw)[_imgPhotoSlots.Length];
        for (int i = 0; i < _imgPhotoSlots.Length; i++)
        {
            var slot = _imgPhotoSlots[i];
            // Main portrait (i == 0) is always SFW by policy. Extras carry their per-photo declaration.
            var isNsfw = i != 0 && slot.Declaration == PhotoNsfwDecl.Nsfw;
            slotInputs[i] = (slot.Confirmed, slot.Path, slot.CropRect, isNsfw);
        }

        // Server orders: avatar=0, main=1, extras=2..6.
        var deleteOrders = new System.Collections.Generic.List<int>(_imgPhotoSlots.Length);
        for (int i = 0; i < _imgPhotoSlots.Length; i++)
        {
            if (_imgPhotoSlots[i].PendingRemove)
            {
                deleteOrders.Add(i + 1);
            }
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var avatarUpload = avatarConfirmed
                    ? ReadPhotoUpload(avatarPath, avatarCrop, isNsfw: false, PhotoKind.Avatar)
                    : (PhotoUploadDto?)null;
                PhotoUploadDto? mainUpload = null;
                var extraUploads = new PhotoUploadDto?[SupporterLimits.SupporterExtraPhotos];
                for (int i = 0; i < slotInputs.Length; i++)
                {
                    if (!slotInputs[i].Confirmed)
                    {
                        continue;
                    }
                    var dto = ReadPhotoUpload(slotInputs[i].Path, slotInputs[i].Crop, slotInputs[i].IsNsfw, PhotoKind.Portrait);
                    if (i == 0)
                    {
                        mainUpload = dto;
                    }
                    else
                    {
                        extraUploads[i - 1] = dto;
                    }
                }

                if (avatarUpload is not null
                    || mainUpload is not null
                    || extraUploads.Any(u => u is not null))
                {
                    var batch = new PhotoBatchDto(
                        Avatar: avatarUpload,
                        Main: mainUpload,
                        Extra1: extraUploads[0],
                        Extra2: extraUploads[1],
                        Extra3: extraUploads[2],
                        Extra4: extraUploads[3],
                        Extra5: extraUploads[4]);
                    await _hubClient.SavePhotosAsync(batch, ct).ConfigureAwait(false);
                }

                foreach (var order in deleteOrders)
                {
                    await _hubClient.DeletePhotoAsync(order, ct).ConfigureAwait(false);
                }

                if (ct.IsCancellationRequested)
                {
                    return;
                }

                var state = await _hubClient.GetOnboardingStateAsync(ct).ConfigureAwait(false);
                if (ct.IsCancellationRequested)
                {
                    return;
                }
                HydrateServerPhotos(state.Photos);

                if (avatarUpload is not null)
                {
                    _ownAvatar.Refresh();
                }

                _imgAvatarConfirmed = false;
                _imgAvatarPath = "";
                _imgAvatarHandle = null;
                foreach (var s in _imgPhotoSlots)
                {
                    s.Clear();
                }
                _imgActiveSlot = -1;
                _imagesSavedTimer = 2.5f;

                // Photos changed server-side - force the view/edit caches to re-fetch.
                _cachedState = null;
                _profileScreen.InvalidateMyProfileCache();
            }
            catch (OperationCanceledException) { }
            catch (RateLimitException rl)
            {
                _rateLimitModal.Show(rl);
            }
            catch (Exception ex)
            {
                if (ct.IsCancellationRequested)
                {
                    return;
                }
                _saveErrorModal.Show(HubErrorText.Localize(ex));
                Plugin.Log.Warning(ex, "[MyProfileScreen] CommitImages failed.");
            }
            finally
            {
                _committingImages = false;
            }
        }, ct);
    }


    private void DrawImagesTab()
    {
        if (_prevTab != Tab.Images && !_editFormHydrated && !_editFormLoading)
        {
            // Images and Edit share the same server fetch; load if Edit hasn't already.
            LoadFromServer();
        }
        _prevTab = Tab.Images;

        var t = ThemeService.Current;

        if (_imagesSavedTimer > 0f)
        {
            _imagesSavedTimer -= ImGui.GetIO().DeltaTime;
        }

        var SaveBarH = Px(54f);
        var scrollH = ImGui.GetContentRegionAvail().Y - SaveBarH;

        if (_editFormLoading && !_editFormHydrated)
        {
            Widgets.LoadingIndicator.Draw();
            return;
        }
        if (_editFormLoadError is not null && !_editFormHydrated)
        {
            ImGui.Spacing();
            ImGui.PushTextWrapPos(0f);
            ImGui.TextColored(UiColors.Danger,
                Loc.T("profile.load_photos_failed", _editFormLoadError));
            ImGui.PopTextWrapPos();
            ImGui.Spacing();
            if (ImGui.Button($"{Loc.T("profile.retry")}##imgRetry", Px(120f, 28f)))
            {
                _editFormLoadError = null;
                LoadFromServer();
            }
            return;
        }

        {
            using var scroll = ImRaii.Child("##myProfImgScroll", new Vector2(0f, scrollH), false);
            if (scroll.Success)
            {
                var availW = ImGui.GetContentRegionAvail().X;
                var muted = UiColors.Muted with { W = 0.75f };

                DrawSectionHeading(Loc.T("profile.profile_picture"), t);
                ImGui.PushTextWrapPos(availW);
                ImGui.TextColored(new Vector4(0.82f, 0.82f, 0.82f, 1f),
                    Loc.T("profile.profile_picture_desc"));
                ImGui.PopTextWrapPos();
                ImGui.Spacing();

                DrawAvatarSection(t, muted);

                ImGui.Spacing();
                ImGui.Spacing();

                DrawSectionHeading(Loc.T("profile.profile_photos"), t);
                ImGui.PushTextWrapPos(availW);
                ImGui.TextColored(new Vector4(0.82f, 0.82f, 0.82f, 1f),
                    Loc.T("profile.profile_photos_desc"));
                ImGui.PopTextWrapPos();
                ImGui.Spacing();

                DrawImgPhotoSlots(availW, t);
                ImGui.Spacing();
            }
        }

        ImGui.Separator();
        ImGui.Spacing();

        var declared = AllConfirmedExtrasDeclared();
        var canSave = HasUnsavedChanges && declared && !_committingImages;
        var savingNow = _committingImages;

        if (HasUnsavedChanges && !declared && !savingNow)
        {
            ImGui.PushTextWrapPos(0f);
            ImGui.TextColored(UiColors.ReviewOrange,
                Loc.T("profile.declare_before_save"));
            ImGui.PopTextWrapPos();
        }
        var btnLabel = savingNow ? Loc.T("profile.saving")
                     : _imagesSavedTimer > 0f ? Loc.T("profile.saved")
                                                : Loc.T("profile.save_changes");

        if (!canSave && !savingNow)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.35f);
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.25f, 0.25f, 0.25f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.25f, 0.25f, 0.25f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.25f, 0.25f, 0.25f, 1f));
        }
        else
        {
            PushThemeButton(t);
        }

        if (savingNow)
        {
            ImGui.BeginDisabled();
        }
        var saveClicked = ImGui.Button($"{btnLabel}##imgSave", new Vector2(-1f, Px(30f)));
        var saveBtnMin = ImGui.GetItemRectMin();
        var saveBtnMax = ImGui.GetItemRectMax();
        if (savingNow)
        {
            ImGui.EndDisabled();
        }
        if (saveClicked && canSave)
        {
            CommitImages();
        }

        if (!canSave && !savingNow)
        {
            ImGui.PopStyleColor(3);
            ImGui.PopStyleVar();
        }
        else
        {
            PopThemeButton();
        }

        if (savingNow)
        {
            var cy = (saveBtnMin.Y + saveBtnMax.Y) * 0.5f;
            LoadingSpinner.Draw(new Vector2(saveBtnMin.X + Px(20f), cy), Px(8f), Px(2.5f), 0xFFFFFFFF);
        }

        PhotoModerationModals.DrawLalafellNsfwModal(ref _imgLalafellNsfwModalPending);
        PhotoModerationModals.DrawUndeclaredModal(ref _imgUndeclaredModalPending);
    }


    private void DrawAvatarSection(ThemeDefinition t, Vector4 muted)
    {
        if (_imgAvatarConfirmed)
        {
            var tex = _imgAvatarHandle?.GetWrapOrDefault();
            if (tex != null)
            {
                var r = _imgAvatarCropRect;
                var uv0 = new Vector2(r.X / tex.Width, r.Y / tex.Height);
                var uv1 = new Vector2((r.X + r.Z) / tex.Width, (r.Y + r.W) / tex.Height);
                ImGui.Image(tex.Handle, Px(88f, 88f), uv0, uv1);
                ImGui.SameLine(0f, Px(12f));
                ImGui.BeginGroup();
            }
            ImGui.TextColored(new Vector4(0.90f, 0.75f, 0.25f, 1f), Loc.T("profile.new_photo_ready"));
            ImGui.Spacing();
            PushDangerButton();
            if (ImGui.Button($"{Loc.T("profile.remove")}##rmStagedAv", Px(110f, 26f)))
            {
                _imgAvatarConfirmed = false;
                _imgAvatarPath = "";
                _imgAvatarHandle = null;
            }
            ImGui.PopStyleColor(3);
            if (_imgAvatarHandle?.GetWrapOrDefault() != null)
            {
                ImGui.EndGroup();
            }
        }
        else if (_serverAvatar is not null)
        {
            var tex = _serverAvatarTex?.GetWrapOrDefault();
            if (tex != null)
            {
                ImGui.Image(tex.Handle, Px(88f, 88f));
                ImGui.SameLine(0f, Px(12f));
                ImGui.BeginGroup();
            }
            ImGui.TextColored(UiColors.Success, Loc.T("profile.profile_picture_set"));
            ImGui.Spacing();
            PushThemeButton(t);
            if (ImGui.Button($"{Loc.T("profile.change_photo")}##chgAv2", Px(110f, 26f)))
            {
                TryOpenImgFilePicker(-1);
            }
            PopThemeButton();
            DrawSelfieButton(-1, t);
            if (tex != null)
            {
                ImGui.EndGroup();
            }
        }
        else
        {
            ImGui.TextColored(muted, Loc.T("profile.no_profile_picture"));
            ImGui.Spacing();
            PushThemeButton(t);
            if (ImGui.Button($"{Loc.T("profile.upload_avatar")}##upAv", Px(120f, 28f)))
            {
                TryOpenImgFilePicker(-1);
            }
            PopThemeButton();
            DrawSelfieButton(-1, t);
        }
    }

    private void TryOpenImgFilePicker(int target) => TryPickImage(target, OpenImgFilePicker);

    private void TryStartImgSelfie(int target) => TryPickImage(target, OpenImgSelfie);

    /// <summary>Sets the target slot then runs <paramref name="open"/> (file picker or selfie capture), gated on
    /// declaring any other confirmed extra first.</summary>
    private void TryPickImage(int target, Action open)
    {
        // Gate only applies to a NEW upload - replacing the active slot's own confirmed photo is fine.
        var startingNewSlot = target != _imgActiveSlot
                              || !(target >= 0 && _imgPhotoSlots[target].Confirmed);
        if (startingNewSlot && AnyConfirmedExtraIsUndeclared())
        {
            _imgUndeclaredModalPending = true;
            return;
        }
        _imgPickerTarget = target;
        open();
    }

    private void DrawSelfieButton(int target, ThemeDefinition t)
    {
        PushThemeButton(t);
        if (ImGui.Button($"{Loc.T("common.selfie")}##selfie{target}", Px(120f, 26f)))
        {
            TryStartImgSelfie(target);
        }
        PopThemeButton();
    }


    private void DrawImgPhotoSlots(float availW, ThemeDefinition t)
    {
        var dl = ImGui.GetWindowDrawList();

        var slotCount = VisibleSlotCount;
        var SlotGap = Px(6f);
        // Shrink slots as the count grows so a supporter's 6 always fit the row.
        var SlotW = MathF.Min(Px(74f), (availW - (slotCount - 1) * SlotGap - Px(8f)) / slotCount);
        // 10:16 portrait
        var SlotH = SlotW * 1.6f;
        var totalW = slotCount * SlotW + (slotCount - 1) * SlotGap;
        var slotsX = (availW - totalW) * 0.5f;
        var slotsY = ImGui.GetCursorPosY();

        for (int i = 0; i < slotCount; i++)
        {
            var slot = _imgPhotoSlots[i];
            var isMain = i == 0;
            var serverTex = SlotServerTexture(i);
            var storedSet = SlotHasServerPhoto(i);

            ImGui.SetCursorPos(new Vector2(slotsX + i * (SlotW + SlotGap), slotsY));
            var sp = ImGui.GetCursorScreenPos();

            dl.AddRectFilled(sp, sp + new Vector2(SlotW, SlotH), UiColors.PhotoSlotFill, Px(4f));

            // Background = stored thumbnail (so user can see what they have); foreground UI overlays.
            if (storedSet && serverTex != null && !slot.Confirmed && !slot.PendingRemove)
            {
                var wrap = serverTex.GetWrapOrDefault();
                if (wrap != null)
                {
                    dl.AddImage(wrap.Handle, sp, sp + new Vector2(SlotW, SlotH));
                }
            }

            uint borderCol;
            if (i == _imgActiveSlot)
            {
                borderCol = t.AccentU32;
            }
            else if (slot.PendingRemove)
            {
                borderCol = 0xFF3333CC;
            }
            else if (slot.Confirmed)
            {
                borderCol = 0xFFBB8833;
            }
            else if (storedSet)
            {
                borderCol = UiColors.PhotoSlotStoredBorder;
            }
            else if (isMain)
            {
                borderCol = UiColors.PhotoSlotMainBorder;
            }
            else
            {
                borderCol = UiColors.AvatarFallback;
            }

            dl.AddRect(sp, sp + new Vector2(SlotW, SlotH), borderCol, Px(4f), ImDrawFlags.None, Px(2f));

            if (slot.PendingRemove)
            {
                var sz = ImGui.CalcTextSize("X");
                dl.AddRectFilled(sp, sp + new Vector2(SlotW, SlotH), 0x88000000, Px(4f));
                dl.AddText(sp + new Vector2((SlotW - sz.X) * 0.5f, (SlotH - sz.Y) * 0.5f),
                    0xFF4444EE, "X");
            }
            else if (slot.Confirmed)
            {
                var thumbTex = slot.Handle?.GetWrapOrDefault();
                if (thumbTex != null)
                {
                    var cr = slot.CropRect;
                    var uv0 = new Vector2(cr.X / thumbTex.Width, cr.Y / thumbTex.Height);
                    var uv1 = new Vector2((cr.X + cr.Z) / thumbTex.Width, (cr.Y + cr.W) / thumbTex.Height);
                    dl.AddImage(thumbTex.Handle, sp, sp + new Vector2(SlotW, SlotH), uv0, uv1);
                }
                // Pending-save marker.
                dl.AddCircleFilled(sp + new Vector2(SlotW - Px(11f), Px(11f)), Px(9f), 0xFFAA8822);
                dl.AddText(sp + new Vector2(SlotW - Px(14.5f), Px(3.5f)), 0xFFFFFFFF, "!");
            }
            else if (!storedSet)
            {
                var ph = isMain ? Loc.T("profile.slot_main") : $"+{i}";
                var col = isMain ? UiColors.PhotoSlotMainLabel : UiColors.TextMuted;
                var sz = ImGui.CalcTextSize(ph);
                dl.AddText(sp + new Vector2((SlotW - sz.X) * 0.5f, (SlotH - sz.Y) * 0.5f), col, ph);
            }

            ImGui.InvisibleButton($"##imgSlot_{i}", new Vector2(SlotW, SlotH));
            if (ImGui.IsItemClicked())
            {
                // Block switching slots while a confirmed extra is undeclared; snap to that slot.
                if (i != _imgActiveSlot && AnyConfirmedExtraIsUndeclared())
                {
                    _imgUndeclaredModalPending = true;
                    _imgActiveSlot = FirstUndeclaredImgExtra();
                }
                else
                {
                    _imgActiveSlot = _imgActiveSlot == i ? -1 : i;
                }
            }
        }

        ImGui.SetCursorPosY(slotsY + SlotH + Px(8f));
        ImGui.Separator();
        ImGui.Spacing();

        if (_imgActiveSlot < 0)
        {
            ImGui.TextColored(UiColors.Hint,
                Loc.T("profile.tap_slot"));
            return;
        }

        DrawActiveSlotControls(availW, t);
    }

    private void DrawActiveSlotControls(float availW, ThemeDefinition t)
    {
        var slot = _imgPhotoSlots[_imgActiveSlot];
        var isMain = _imgActiveSlot == 0;
        var hasStored = SlotHasServerPhoto(_imgActiveSlot);
        var storedTex = SlotServerTexture(_imgActiveSlot);

        ImGui.TextColored(t.AccentLight, isMain ? Loc.T("profile.main_photo") : Loc.T("profile.extra_photo", _imgActiveSlot));
        ImGui.Spacing();

        if (slot.PendingRemove)
        {
            ImGui.TextColored(new Vector4(0.90f, 0.60f, 0.20f, 1f), Loc.T("profile.photo_will_be_removed"));
            ImGui.SameLine(0f, Px(20f));
            if (ImGui.Button($"{Loc.T("profile.undo")}##undoPhoto{_imgActiveSlot}", Px(60f, 24f)))
            {
                slot.PendingRemove = false;
            }
        }
        else if (slot.Confirmed)
        {
            // SFW/NSFW selector + warning go above the preview so they're seen first.
            if (isMain)
            {
                ImGui.PushTextWrapPos(0f);
                ImGui.TextColored(UiColors.Danger,
                    Loc.T("profile.main_must_be_sfw"));
                ImGui.PopTextWrapPos();
            }
            else
            {
                ImGui.Text(Loc.T("profile.sfw_or_nsfw"));
                var declIdx = (int)slot.Declaration;
                ImGui.SetNextItemWidth(availW - Px(8f));
                if (ImGui.Combo($"##imgNsfwDecl{_imgActiveSlot}", ref declIdx,
                        PhotoModerationLabels.NsfwDeclOptions, PhotoModerationLabels.NsfwDeclOptions.Length))
                {
                    var requested = (PhotoNsfwDecl)declIdx;
                    if (requested == PhotoNsfwDecl.Nsfw && IsLalafell())
                    {
                        // Lalafell policy: NSFW declaration rejected. Force back to SFW and surface the modal.
                        slot.Declaration = PhotoNsfwDecl.Sfw;
                        _imgLalafellNsfwModalPending = true;
                    }
                    else
                    {
                        slot.Declaration = requested;
                    }
                }
                ImGui.Spacing();
                ImGui.PushTextWrapPos(0f);
                ImGui.TextColored(UiColors.Danger,
                    Loc.T("profile.sfw_mismatch_warning"));
                ImGui.PopTextWrapPos();
            }
            ImGui.Spacing();

            ImGui.TextColored(new Vector4(0.90f, 0.75f, 0.25f, 1f), Loc.T("profile.photo_ready"));
            ImGui.SameLine(0f, Px(20f));
            PushDangerButton();
            if (ImGui.Button($"{Loc.T("profile.remove")}##rmStaged{_imgActiveSlot}", Px(80f, 24f)))
            {
                slot.Clear();
            }
            ImGui.PopStyleColor(3);

            var pv = slot.Handle?.GetWrapOrDefault();
            if (pv != null)
            {
                var cr = slot.CropRect;
                var uv0 = new Vector2(cr.X / pv.Width, cr.Y / pv.Height);
                var uv1 = new Vector2((cr.X + cr.Z) / pv.Width, (cr.Y + cr.W) / pv.Height);
                var prevW = availW - Px(8f);
                ImGui.SetCursorPosX(Px(4f));
                ImGui.Image(pv.Handle, new Vector2(prevW, prevW * 1.6f), uv0, uv1);
            }
        }
        else if (hasStored)
        {
            ImGui.TextColored(UiColors.Success, Loc.T("profile.photo_set"));

            // Surface the server's current SFW/NSFW declaration for extras so the user can see it.
            if (!isMain)
            {
                var serverPhoto = _serverExtras[_imgActiveSlot - 1];
                if (serverPhoto is not null)
                {
                    var declColor = serverPhoto.IsNsfw
                        ? UiColors.ReviewOrange
                        : UiColors.SuccessSoft;
                    ImGui.SameLine(0f, Px(20f));
                    ImGui.TextColored(declColor,
                        serverPhoto.IsNsfw ? Loc.T("profile.currently_nsfw") : Loc.T("profile.currently_sfw"));
                }
            }
            ImGui.Spacing();

            // A slot beyond the current allowance (lapsed supporter) can be emptied but not refilled.
            var lockedSlot = _imgActiveSlot > MaxExtraSlots;

            if (!isMain)
            {
                PushDangerButton();
                if (ImGui.Button($"{Loc.T("profile.remove")}##rmPhoto{_imgActiveSlot}", Px(80f, 26f)))
                {
                    slot.PendingRemove = true;
                }
                ImGui.PopStyleColor(3);
                if (!lockedSlot)
                {
                    ImGui.SameLine(0f, Px(8f));
                }
            }

            if (lockedSlot)
            {
                ImGui.Spacing();
                ImGui.PushTextWrapPos(0f);
                ImGui.TextColored(UiColors.Hint, Loc.T("profile.slot_locked"));
                ImGui.PopTextWrapPos();
            }
            else
            {
                PushThemeButton(t);
                if (ImGui.Button($"{Loc.T("profile.replace")}##replPhoto{_imgActiveSlot}", Px(80f, 26f)))
                {
                    TryOpenImgFilePicker(_imgActiveSlot);
                }
                PopThemeButton();
                DrawSelfieButton(_imgActiveSlot, t);
            }

            // Show the server's current copy below the buttons.
            var pv = storedTex?.GetWrapOrDefault();
            if (pv != null)
            {
                ImGui.Spacing();
                var prevW = availW - Px(8f);
                ImGui.SetCursorPosX(Px(4f));
                ImGui.Image(pv.Handle, new Vector2(prevW, prevW * 1.6f));
            }
        }
        else
        {
            ImGui.TextColored(UiColors.Hint,
                isMain ? Loc.T("profile.photo_required") : Loc.T("profile.photo_optional"));
            ImGui.Spacing();

            PushThemeButton(t);
            if (ImGui.Button($"{Loc.T("profile.upload_photo")}##upPhoto{_imgActiveSlot}", Px(120f, 28f)))
            {
                TryOpenImgFilePicker(_imgActiveSlot);
            }
            PopThemeButton();
            DrawSelfieButton(_imgActiveSlot, t);
        }
    }


    private bool SlotHasServerPhoto(int slotIdx) => slotIdx switch
    {
        0 => _serverMain is not null,
        > 0 when slotIdx - 1 < _serverExtras.Length => _serverExtras[slotIdx - 1] is not null,
        _ => false,
    };

    private ISharedImmediateTexture? SlotServerTexture(int slotIdx) => slotIdx switch
    {
        0 => _serverMainTex,
        > 0 when slotIdx - 1 < _serverExtraTex.Length => _serverExtraTex[slotIdx - 1],
        _ => null,
    };


    private void OpenImgFilePicker()
    {
        // Avatar (-1) and the first profile photo (0) are SFW-mandatory; gate them behind the rules modal.
        if (_imgPickerTarget is -1 or 0)
        {
            _imgSfwGate.Open(OpenImgFilePickerCore);
            return;
        }
        OpenImgFilePickerCore();
    }

    private void OpenImgSelfie()
    {
        if (_imgPickerTarget is -1 or 0)
        {
            _imgSfwGate.Open(OpenImgSelfieCore);
            return;
        }
        OpenImgSelfieCore();
    }

    private void OpenImgSelfieCore()
    {
        var aspect = _imgPickerTarget == -1 ? 1.0f : 1.6f;
        var minW = _imgPickerTarget == -1 ? PhotoSpec.AvatarSize : PhotoSpec.PortraitWidth;
        _selfieOverlay.Start(aspect, minW, (path, crop) => HandleImgPicked(path, crop));
    }

    private void OpenImgFilePickerCore()
    {
        _imgFileDialog.OpenFileDialog(
            title: Loc.T("profile.select_image"),
            filters: Loc.T("profile.image_files_filter") + "{.png,.jpg,.jpeg,.bmp,.webp}",
            callback: (ok, path) =>
            {
                if (!ok)
                {
                    return;
                }
                if (_imgPendingPick.RejectUnavailableCloudFile(path))
                {
                    return;
                }
                HandleImgPicked(path);
            });
    }

    /// <summary>Loads a picked image into the current target slot and opens the crop popup, or, when
    /// <paramref name="presetCrop"/> is supplied (a selfie already framed its crop), confirms the slot directly.</summary>
    private void HandleImgPicked(string path, Vector4? presetCrop = null)
    {
        if (_imgPickerTarget == -1)
        {
            var handle = LoadPickedPreview(path);
            _imgAvatarPath = path;
            _imgAvatarConfirmed = false;
            _imgAvatarHandle = handle;

            if (presetCrop is { } avatarCrop)
            {
                _imgAvatarCropRect = avatarCrop;
                _imgAvatarConfirmed = true;
                return;
            }

            void Unload()
            {
                _imgAvatarPath = "";
                _imgAvatarHandle = null;
            }

            _imgPendingPick.Begin(handle, PhotoSpec.AvatarSize, PhotoSpec.AvatarSize,
                onValid: () => _imgCropPopup.Open(
                    Loc.T("profile.crop_avatar"),
                    handle,
                    1.0f,
                    cropRect =>
                    {
                        _imgAvatarCropRect = cropRect;
                        _imgAvatarConfirmed = true;
                    },
                    onCancel: Unload),
                onReject: Unload);
        }
        else
        {
            var target = _imgPickerTarget;
            var slot = _imgPhotoSlots[target];
            var prevPath = slot.Path;
            var prevHandle = slot.Handle;
            var prevCropRect = slot.CropRect;
            var prevConf = slot.Confirmed;
            var handle = LoadPickedPreview(path);

            slot.Path = path;
            slot.Confirmed = false;
            slot.Handle = handle;

            if (presetCrop is { } photoCrop)
            {
                slot.CropRect = photoCrop;
                slot.Confirmed = true;
                slot.PendingRemove = false;
                return;
            }

            void Restore()
            {
                _imgPhotoSlots[target].Path = prevPath;
                _imgPhotoSlots[target].Handle = prevHandle;
                _imgPhotoSlots[target].CropRect = prevCropRect;
                _imgPhotoSlots[target].Confirmed = prevConf;
            }

            var label = target == 0 ? Loc.T("profile.crop_main_photo") : Loc.T("profile.crop_extra_photo", target);
            _imgPendingPick.Begin(handle, PhotoSpec.PortraitWidth, PhotoSpec.PortraitHeight,
                onValid: () => _imgCropPopup.Open(
                    label,
                    handle,
                    1.6f,
                    cropRect =>
                    {
                        _imgPhotoSlots[target].CropRect = cropRect;
                        _imgPhotoSlots[target].Confirmed = true;
                        _imgPhotoSlots[target].PendingRemove = false;
                    },
                    onCancel: Restore),
                onReject: Restore);
        }
    }


}
