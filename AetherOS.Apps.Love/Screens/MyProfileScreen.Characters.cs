using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove.Emoji;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Shared;
using AetherLove.Shared.Profile;
using AetherLove.UI;
using AetherLove.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;

namespace AetherLove.Screens;

public partial class MyProfileScreen
{
    private sealed class RpCharRow
    {
        /// <summary>Stable ImGui id for the row; survives reorders and is independent of the server id.</summary>
        public readonly Guid UiId = Guid.NewGuid();

        public Guid? Id;
        public string Name = "";
        public string Bio = "";

        public ISharedImmediateTexture? ServerImageTex;
        public bool HasServerImage;
        public bool ServerImageIsNsfw;

        public string StagedPath = "";
        public ISharedImmediateTexture? StagedHandle;
        public Vector4 StagedCrop;
        public bool StagedConfirmed;
        public bool StagedNsfw;
        public bool PendingRemoveImage;

        /// <summary>Supporter extra-image slots (index = SortOrder - 1).</summary>
        public readonly RpExtraSlot[] Extras =
            [.. Enumerable.Range(0, SupporterLimits.ExtraCharacterImages).Select(_ => new RpExtraSlot())];
    }

    private sealed class RpExtraSlot
    {
        public ISharedImmediateTexture? ServerTex;
        public bool HasServer;
        public bool ServerIsNsfw;

        public string StagedPath = "";
        public ISharedImmediateTexture? StagedHandle;
        public Vector4 StagedCrop;
        public bool StagedConfirmed;
        public bool StagedNsfw;
        public bool PendingRemove;
    }

    private readonly List<RpCharRow> _rpRows = new();
    /// <summary>Index of the character open in the edit view; -1 shows the overview list.</summary>
    private int _rpEditIdx = -1;
    private int _rpMaxCharacters = 3;
    private bool _rpProfileIsNsfw;
    private volatile bool _rpLoading;
    private volatile bool _rpHydrated;
    private volatile string? _rpLoadError;
    private volatile bool _rpSaving;
    private float _rpSavedTimer;
    private bool _showRpIntro;
    private float _rpIntroHeight;
    private Guid _rpDeleteArmed;
    private float _rpDeleteArmTimer;
    private readonly EmojiPickerPopup _rpEmojiPicker = new();

    private static string RpCharCacheDir =>
        Path.Combine(UiHost.PluginInterface.ConfigDirectory.FullName, "RpCharCache");

    private void OpenRpCharacters()
    {
        _section = Section.RpCharacters;
        _rpEditIdx = -1;
        _rpHydrated = false;
        _rpLoadError = null;
        LoadRpCharacters();
        if (!UiHost.Configuration.SeenRpProfilesIntro)
        {
            UiHost.Configuration.SeenRpProfilesIntro = true;
            UiHost.Configuration.Save();
            _showRpIntro = true;
        }
    }

    private void LoadRpCharacters()
    {
        if (_rpLoading)
        {
            return;
        }
        _rpLoading = true;
        _rpLoadError = null;
        var ct = _cts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                var dto = await _hubClient.GetMyCharactersAsync(ct).ConfigureAwait(false);
                if (ct.IsCancellationRequested)
                {
                    return;
                }
                HydrateRpRows(dto);
                _rpHydrated = true;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (ct.IsCancellationRequested)
                {
                    return;
                }
                _rpLoadError = HubErrorText.Localize(ex);
                UiHost.Log.Warning(ex, "[MyProfileScreen] GetMyCharactersAsync failed.");
            }
            finally
            {
                _rpLoading = false;
            }
        }, ct);
    }

    private void HydrateRpRows(MyCharactersDto dto)
    {
        _rpMaxCharacters = dto.MaxCharacters;
        _rpProfileIsNsfw = dto.ProfileIsNsfw;
        _rpRows.Clear();
        foreach (var c in dto.Characters)
        {
            var row = new RpCharRow
            {
                Id = c.Id,
                Name = c.Name,
                Bio = c.Bio,
                HasServerImage = c.ImageBytes is { Length: > 0 },
                ServerImageIsNsfw = c.ImageIsNsfw,
            };
            if (c.ImageBytes is { Length: > 0 })
            {
                row.ServerImageTex = AvatarDiskCache.Store(RpCharCacheDir, $"rpchar_{c.Id:N}", c.ImageBytes);
            }
            foreach (var extra in c.ExtraImages ?? [])
            {
                var idx = extra.SortOrder - 1;
                if (idx < 0 || idx >= row.Extras.Length || extra.Webp is not { Length: > 0 })
                {
                    continue;
                }
                var slot = row.Extras[idx];
                slot.HasServer = true;
                slot.ServerIsNsfw = extra.IsNsfw;
                slot.ServerTex = AvatarDiskCache.Store(RpCharCacheDir, $"rpchar_{c.Id:N}_x{extra.SortOrder}", extra.Webp);
            }
            _rpRows.Add(row);
        }
        if (_rpEditIdx >= _rpRows.Count)
        {
            _rpEditIdx = -1;
        }
    }

    private void DrawRpCharacters()
    {
        var editing = _rpEditIdx >= 0 && _rpEditIdx < _rpRows.Count;
        if (editing)
        {
            if (DrawFloatingBackPill(ImGui.GetCursorScreenPos(), Loc.T("profile.rp_back"), FontAwesomeIcon.List))
            {
                _rpEditIdx = -1;
                editing = false;
            }
            ImGui.Spacing();
        }
        else
        {
            DrawHubBackButton();
        }
        var headingRow = editing ? _rpRows[_rpEditIdx] : null;
        DrawSubpageHeading(
            headingRow is not null && headingRow.Name.Trim().Length > 0 ? headingRow.Name : Loc.T("profile.menu_rp"),
            HubPadX);

        var t = ThemeService.Current;
        var w = ImGui.GetContentRegionAvail().X;
        var availH = ImGui.GetContentRegionAvail().Y;
        var SaveBarH = Px(48f);

        if (_rpSavedTimer > 0f)
        {
            _rpSavedTimer -= ImGui.GetIO().DeltaTime;
        }
        if (_rpDeleteArmTimer > 0f)
        {
            _rpDeleteArmTimer -= ImGui.GetIO().DeltaTime;
            if (_rpDeleteArmTimer <= 0f)
            {
                _rpDeleteArmed = Guid.Empty;
            }
        }

        if (_rpLoading && !_rpHydrated)
        {
            Widgets.LoadingIndicator.Draw();
            DrawRpIntroOverlay(ImGui.GetWindowPos(), ImGui.GetWindowSize());
            return;
        }
        if (_rpLoadError is not null && !_rpHydrated)
        {
            ImGui.Spacing();
            ImGui.SetCursorPosX(Px(HubPadX));
            ImGui.PushTextWrapPos(w - Px(HubPadX));
            ImGui.TextColored(UiColors.Danger, Loc.T("profile.load_profile_failed", _rpLoadError));
            ImGui.PopTextWrapPos();
            ImGui.Spacing();
            ImGui.SetCursorPosX(Px(HubPadX));
            if (ImGui.Button($"{Loc.T("profile.retry")}##rpRetry", Px(120f, 28f)))
            {
                LoadRpCharacters();
            }
            DrawRpIntroOverlay(ImGui.GetWindowPos(), ImGui.GetWindowSize());
            return;
        }

        using (var scroll = ImRaii.Child("##rpCharsScroll", new Vector2(0f, availH - SaveBarH), false))
        {
            if (scroll.Success)
            {
                _rpEmojiPicker.Draw();
                var innerW = ImGui.GetContentRegionAvail().X;
                ImGui.Spacing();

                if (editing)
                {
                    DrawRpEditView(_rpEditIdx, innerW, t);
                }
                else
                {
                    DrawRpListView(innerW, t);
                }
            }
        }

        ImGui.Separator();

        var savingNow = _rpSaving;
        var btnLabel = savingNow ? Loc.T("profile.saving")
                     : _rpSavedTimer > 0f ? Loc.T("profile.saved")
                                          : Loc.T("profile.save_changes");
        var btnColor = _rpSavedTimer > 0f ? new Vector4(0.22f, 0.60f, 0.28f, 1f) : t.ButtonNormal;
        var btnHover = _rpSavedTimer > 0f ? new Vector4(0.22f, 0.60f, 0.28f, 1f) : t.ButtonHovered;

        ImGui.PushStyleColor(ImGuiCol.Button, btnColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, btnHover);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, t.ButtonActive);
        var saveDisabled = savingNow
            || _rpRows.Any(r => r.Name.Trim().Length < ProfileLimits.DisplayNameMinLength);
        if (saveDisabled)
        {
            ImGui.BeginDisabled();
        }
        if (ImGui.Button(btnLabel, new Vector2(w - Px(6f), Px(32f))))
        {
            SaveRpCharacters();
        }
        if (saveDisabled)
        {
            ImGui.EndDisabled();
        }
        ImGui.PopStyleColor(3);

        DrawRpIntroOverlay(ImGui.GetWindowPos(), ImGui.GetWindowSize());
    }

    private void DrawRpListView(float winW, ThemeDefinition t)
    {
        var pad = Px(HubPadX);

        if (_rpRows.Count == 0)
        {
            ImGui.SetCursorPosX(pad);
            ImGui.PushTextWrapPos(winW - pad);
            ImGui.TextColored(UiColors.Muted, Loc.T("profile.rp_empty"));
            ImGui.PopTextWrapPos();
            ImGui.Spacing();
        }

        for (var i = 0; i < _rpRows.Count; i++)
        {
            DrawRpListRow(i, winW, t);
        }

        ImGui.Spacing();
        ImGui.SetCursorPosX(pad);
        var atCap = _rpRows.Count >= _rpMaxCharacters;
        if (atCap)
        {
            ImGui.BeginDisabled();
        }
        PushThemeButton(t);
        if (ImGui.Button($"{Loc.T("profile.rp_add")}##rpAdd", new Vector2(winW - pad * 2f, Px(30f))))
        {
            _rpRows.Add(new RpCharRow());
            _rpEditIdx = _rpRows.Count - 1;
        }
        PopThemeButton();
        if (atCap)
        {
            ImGui.EndDisabled();
            ImGui.SetCursorPosX(pad);
            ImGui.TextColored(UiColors.Hint, Loc.T("profile.rp_limit_reached", _rpMaxCharacters));
        }
        ImGui.Spacing();
    }

    /// <summary>One overview line: reorder arrows plus a clickable card that opens the edit view.</summary>
    private void DrawRpListRow(int index, float winW, ThemeDefinition t)
    {
        var row = _rpRows[index];
        var id = row.UiId.ToString("N");
        var pad = Px(HubPadX);
        var dl = ImGui.GetWindowDrawList();

        ImGui.SetCursorPosX(pad);
        ImGui.BeginDisabled(index == 0);
        if (ImGui.ArrowButton($"##rpUp{id}", ImGuiDir.Up))
        {
            (_rpRows[index - 1], _rpRows[index]) = (_rpRows[index], _rpRows[index - 1]);
        }
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("profile.rp_move_up"));
        }
        ImGui.SameLine(0f, Px(4f));
        ImGui.BeginDisabled(index == _rpRows.Count - 1);
        if (ImGui.ArrowButton($"##rpDown{id}", ImGuiDir.Down))
        {
            (_rpRows[index + 1], _rpRows[index]) = (_rpRows[index], _rpRows[index + 1]);
        }
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("profile.rp_move_down"));
        }

        ImGui.SameLine(0f, Px(8f));
        var rowH = ImGui.GetFrameHeight();
        var cardTL = ImGui.GetCursorScreenPos();
        var cardW = winW - (cardTL.X - ImGui.GetWindowPos().X) - pad;
        ImGui.InvisibleButton($"##rpRowOpen{id}", new Vector2(cardW, rowH));
        var hovered = ImGui.IsItemHovered();
        if (ImGui.IsItemClicked())
        {
            _rpEditIdx = index;
            _rpDeleteArmed = Guid.Empty;
        }

        dl.AddRectFilled(cardTL, cardTL + new Vector2(cardW, rowH),
            hovered ? 0xFF201F2Au : 0xFF1B1A23u, Px(8f));
        var name = row.Name.Trim().Length > 0 ? row.Name : Loc.T("profile.rp_name");
        var nameSz = ImGui.CalcTextSize(name);
        dl.AddText(cardTL + new Vector2(Px(10f), (rowH - nameSz.Y) * 0.5f), 0xFFF2F2F5u, name);

        ImGui.PushFont(UiHost.PluginInterface.UiBuilder.FontIcon);
        var iconX = cardTL.X + cardW - Px(10f);
        var chevron = FontAwesomeIcon.ChevronRight.ToIconString();
        var chevSz = ImGui.CalcTextSize(chevron);
        iconX -= chevSz.X;
        dl.AddText(new Vector2(iconX, cardTL.Y + (rowH - chevSz.Y) * 0.5f), 0xFF8A8A96u, chevron);
        if (row.HasServerImage || row.StagedConfirmed)
        {
            var camera = FontAwesomeIcon.Image.ToIconString();
            var camSz = ImGui.CalcTextSize(camera);
            iconX -= camSz.X + Px(8f);
            dl.AddText(new Vector2(iconX, cardTL.Y + (rowH - camSz.Y) * 0.5f), 0xFF8A8A96u, camera);
        }
        ImGui.PopFont();

        ImGui.Dummy(new Vector2(1f, Px(4f)));
    }

    private void DrawRpEditView(int index, float winW, ThemeDefinition t)
    {
        var row = _rpRows[index];
        var pad = Px(HubPadX);
        var innerW = winW - pad * 2f;
        var id = row.UiId.ToString("N");

        ImGui.Indent(pad);

        DrawFieldLabel(Loc.T("profile.rp_name"), t);
        ImGui.SetNextItemWidth(Px(260f));
        ImGui.InputText($"##rpName{id}", ref row.Name, ProfileLimits.CharacterNameMaxLength);

        DrawFieldLabel(Loc.T("profile.rp_bio"), t);
        ImGui.SameLine();
        {
            var iconH = ImGui.GetTextLineHeight();
            var grinTex = UiHost.EmojiService.GetEmoji("grinning")?.GetWrapOrDefault();
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Px(2f, 2f));
            ImGui.PushID($"rpEmoji{id}");
            var clicked = grinTex != null
                ? ImGui.ImageButton(grinTex.Handle, new Vector2(iconH - Px(4f)))
                : ImGui.SmallButton(":)");
            ImGui.PopID();
            ImGui.PopStyleVar();
            if (clicked)
            {
                var target = row;
                _rpEmojiPicker.Open(name =>
                {
                    var add = $":{name}: ";
                    if (EmojiText.EffectiveLength(target.Bio + add) <= EmojiText.MaxBioLength)
                    {
                        target.Bio += add;
                    }
                });
            }
        }
        var bioBefore = row.Bio;
        InputTextMultilineWithPaste($"##rpBio{id}", ref row.Bio, EmojiText.MaxBioRawLength,
            new Vector2(innerW, Px(68f)));
        // Lock the field at the user-visible limit: undo an edit that pushed it over.
        if (EmojiText.EffectiveLength(row.Bio) > EmojiText.MaxBioLength)
        {
            row.Bio = bioBefore;
        }
        var effectiveLen = EmojiText.EffectiveLength(row.Bio);
        var muted = UiColors.Muted with { W = 0.75f };
        ImGui.TextColored(effectiveLen > EmojiText.MaxBioLength ? UiColors.BioOverLimit : muted,
            Loc.T("profile.char_count", effectiveLen));

        if (row.Bio.Length > 0)
        {
            ImGui.TextColored(muted, Loc.T("profile.preview"));
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.BioText);
            ParsedMessage.Parse(row.Bio).DrawWrapped($"##rpBioPrev{id}", innerW);
            ImGui.PopStyleColor();
        }

        ImGui.Spacing();
        DrawRpRowImage(row, id, innerW, t);
        DrawRpRowExtraImages(row, id, t);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        var deleteLabel = _rpDeleteArmed == row.UiId
            ? Loc.T("profile.rp_delete_confirm")
            : Loc.T("profile.rp_delete");
        PushDangerButton();
        if (ImGui.Button($"{deleteLabel}##rpDel{id}", new Vector2(Px(160f), Px(26f))))
        {
            if (_rpDeleteArmed == row.UiId)
            {
                _rpRows.RemoveAt(index);
                _rpDeleteArmed = Guid.Empty;
                _rpEditIdx = -1;
            }
            else
            {
                _rpDeleteArmed = row.UiId;
                _rpDeleteArmTimer = 3f;
            }
        }
        ImGui.PopStyleColor(3);
        ImGui.Spacing();
        ImGui.Unindent(pad);
    }

    private void DrawRpRowImage(RpCharRow row, string id, float innerW, ThemeDefinition t)
    {
        DrawFieldLabel(Loc.T("profile.rp_image"), t);

        if (row.StagedConfirmed)
        {
            var pv = row.StagedHandle?.GetWrapOrDefault();
            if (pv != null)
            {
                var cr = row.StagedCrop;
                var uv0 = new Vector2(cr.X / pv.Width, cr.Y / pv.Height);
                var uv1 = new Vector2((cr.X + cr.Z) / pv.Width, (cr.Y + cr.W) / pv.Height);
                ImGui.Image(pv.Handle, Px(74f, 118f), uv0, uv1);
                ImGui.SameLine(0f, Px(10f));
            }
            ImGui.BeginGroup();
            ImGui.TextColored(new Vector4(0.90f, 0.75f, 0.25f, 1f), Loc.T("profile.rp_image_ready"));
            if (_rpProfileIsNsfw)
            {
                ImGui.Checkbox($"{Loc.T("profile.rp_image_nsfw")}##rpNsfw{id}", ref row.StagedNsfw);
            }
            PushDangerButton();
            if (ImGui.Button($"{Loc.T("profile.remove")}##rpRmStaged{id}", Px(90f, 24f)))
            {
                row.StagedPath = "";
                row.StagedHandle = null;
                row.StagedConfirmed = false;
                row.StagedNsfw = false;
            }
            ImGui.PopStyleColor(3);
            ImGui.EndGroup();
            return;
        }

        if (row.PendingRemoveImage)
        {
            ImGui.TextColored(new Vector4(0.90f, 0.60f, 0.20f, 1f), Loc.T("profile.photo_will_be_removed"));
            ImGui.SameLine(0f, Px(12f));
            if (ImGui.Button($"{Loc.T("profile.undo")}##rpUndoRm{id}", Px(60f, 24f)))
            {
                row.PendingRemoveImage = false;
            }
            return;
        }

        if (row.HasServerImage)
        {
            var pv = row.ServerImageTex?.GetWrapOrDefault();
            if (pv != null)
            {
                ImGui.Image(pv.Handle, Px(74f, 118f));
                ImGui.SameLine(0f, Px(10f));
            }
            ImGui.BeginGroup();
            ImGui.TextColored(UiColors.Success, Loc.T("profile.photo_set"));
            ImGui.SameLine(0f, Px(10f));
            ImGui.TextColored(row.ServerImageIsNsfw ? UiColors.ReviewOrange : UiColors.SuccessSoft,
                row.ServerImageIsNsfw ? Loc.T("profile.currently_nsfw") : Loc.T("profile.currently_sfw"));
            PushThemeButton(t);
            if (ImGui.Button($"{Loc.T("profile.rp_replace_image")}##rpRepl{id}", Px(120f, 24f)))
            {
                OpenRpImagePicker(row);
            }
            PopThemeButton();
            ImGui.SameLine(0f, Px(8f));
            PushDangerButton();
            if (ImGui.Button($"{Loc.T("profile.rp_remove_image")}##rpRmImg{id}", Px(120f, 24f)))
            {
                row.PendingRemoveImage = true;
            }
            ImGui.PopStyleColor(3);
            DrawRpSelfieButton(row, id, t);
            ImGui.EndGroup();
            return;
        }

        PushThemeButton(t);
        if (ImGui.Button($"{Loc.T("profile.rp_upload_image")}##rpUp{id}", Px(140f, 26f)))
        {
            OpenRpImagePicker(row);
        }
        PopThemeButton();
        DrawRpSelfieButton(row, id, t);
    }

    /// <summary>Supporter extra-image slots under the primary image. Non-supporters only ever see slots that
    /// still hold a server image after a lapse, and those are remove-only.</summary>
    private void DrawRpRowExtraImages(RpCharRow row, string id, ThemeDefinition t)
    {
        var isSupporter = _bootstrap.LastConnection is { IsSupporter: true };
        for (var s = 0; s < row.Extras.Length; s++)
        {
            var slot = row.Extras[s];
            if (!isSupporter && !slot.HasServer && !slot.StagedConfirmed)
            {
                continue;
            }

            ImGui.Spacing();
            var slotId = $"{id}_x{s}";
            DrawFieldLabel(Loc.T("profile.rp_extra_image", s + 1), t);

            if (slot.StagedConfirmed)
            {
                var pv = slot.StagedHandle?.GetWrapOrDefault();
                if (pv != null)
                {
                    var cr = slot.StagedCrop;
                    var uv0 = new Vector2(cr.X / pv.Width, cr.Y / pv.Height);
                    var uv1 = new Vector2((cr.X + cr.Z) / pv.Width, (cr.Y + cr.W) / pv.Height);
                    ImGui.Image(pv.Handle, Px(74f, 118f), uv0, uv1);
                    ImGui.SameLine(0f, Px(10f));
                }
                ImGui.BeginGroup();
                ImGui.TextColored(new Vector4(0.90f, 0.75f, 0.25f, 1f), Loc.T("profile.rp_image_ready"));
                if (_rpProfileIsNsfw)
                {
                    ImGui.Checkbox($"{Loc.T("profile.rp_image_nsfw")}##rpxNsfw{slotId}", ref slot.StagedNsfw);
                }
                PushDangerButton();
                if (ImGui.Button($"{Loc.T("profile.remove")}##rpxRmStaged{slotId}", Px(90f, 24f)))
                {
                    slot.StagedPath = "";
                    slot.StagedHandle = null;
                    slot.StagedConfirmed = false;
                    slot.StagedNsfw = false;
                }
                ImGui.PopStyleColor(3);
                ImGui.EndGroup();
                continue;
            }

            if (slot.PendingRemove)
            {
                ImGui.TextColored(new Vector4(0.90f, 0.60f, 0.20f, 1f), Loc.T("profile.photo_will_be_removed"));
                ImGui.SameLine(0f, Px(12f));
                if (ImGui.Button($"{Loc.T("profile.undo")}##rpxUndo{slotId}", Px(60f, 24f)))
                {
                    slot.PendingRemove = false;
                }
                continue;
            }

            if (slot.HasServer)
            {
                var pv = slot.ServerTex?.GetWrapOrDefault();
                if (pv != null)
                {
                    ImGui.Image(pv.Handle, Px(74f, 118f));
                    ImGui.SameLine(0f, Px(10f));
                }
                ImGui.BeginGroup();
                ImGui.TextColored(UiColors.Success, Loc.T("profile.photo_set"));
                ImGui.SameLine(0f, Px(10f));
                ImGui.TextColored(slot.ServerIsNsfw ? UiColors.ReviewOrange : UiColors.SuccessSoft,
                    slot.ServerIsNsfw ? Loc.T("profile.currently_nsfw") : Loc.T("profile.currently_sfw"));
                if (isSupporter)
                {
                    PushThemeButton(t);
                    if (ImGui.Button($"{Loc.T("profile.rp_replace_image")}##rpxRepl{slotId}", Px(120f, 24f)))
                    {
                        OpenRpExtraPicker(slot);
                    }
                    PopThemeButton();
                    ImGui.SameLine(0f, Px(8f));
                }
                PushDangerButton();
                if (ImGui.Button($"{Loc.T("profile.rp_remove_image")}##rpxRm{slotId}", Px(120f, 24f)))
                {
                    slot.PendingRemove = true;
                }
                ImGui.PopStyleColor(3);
                if (!isSupporter)
                {
                    ImGui.PushTextWrapPos(0f);
                    ImGui.TextColored(UiColors.Hint, Loc.T("profile.slot_locked"));
                    ImGui.PopTextWrapPos();
                }
                ImGui.EndGroup();
                continue;
            }

            PushThemeButton(t);
            if (ImGui.Button($"{Loc.T("profile.rp_upload_image")}##rpxUp{slotId}", Px(140f, 26f)))
            {
                OpenRpExtraPicker(slot);
            }
            PopThemeButton();
        }
    }

    private void OpenRpExtraPicker(RpExtraSlot slot)
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
                HandleRpExtraPicked(slot, path);
            });
    }

    private void HandleRpExtraPicked(RpExtraSlot slot, string path)
    {
        var handle = LoadPickedPreview(path);
        if (handle is null)
        {
            return;
        }
        slot.StagedPath = path;
        slot.StagedHandle = handle;
        slot.StagedConfirmed = false;

        void Unload()
        {
            slot.StagedPath = "";
            slot.StagedHandle = null;
            slot.StagedConfirmed = false;
        }

        _imgPendingPick.Begin(handle, PhotoSpec.PortraitWidth, PhotoSpec.PortraitHeight,
            onValid: () => _imgCropPopup.Open(
                Loc.T("profile.rp_crop_image"),
                handle,
                1.6f,
                cropRect =>
                {
                    slot.StagedCrop = cropRect;
                    slot.StagedConfirmed = true;
                    slot.PendingRemove = false;
                },
                onCancel: Unload),
            onReject: Unload);
    }

    private void OpenRpImagePicker(RpCharRow row)
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
                HandleRpImagePicked(row, path);
            });
    }

    private void StartRpImageSelfie(RpCharRow row)
    {
        _shell.RequestCamera(1.6f, PhotoSpec.PortraitWidth, (path, crop) => HandleRpImagePicked(row, path, crop));
    }

    /// <summary>Loads a picked image into the RP character row and opens the crop popup, or, when
    /// <paramref name="presetCrop"/> is supplied (a selfie already framed its crop), confirms it directly.</summary>
    private void HandleRpImagePicked(RpCharRow row, string path, Vector4? presetCrop = null)
    {
        var handle = LoadPickedPreview(path);
        if (handle is null)
        {
            return;
        }

        row.StagedPath = path;
        row.StagedHandle = handle;
        row.StagedConfirmed = false;

        if (presetCrop is { } crop)
        {
            row.StagedCrop = crop;
            row.StagedConfirmed = true;
            row.PendingRemoveImage = false;
            return;
        }

        void Unload()
        {
            row.StagedPath = "";
            row.StagedHandle = null;
            row.StagedConfirmed = false;
        }

        _imgPendingPick.Begin(handle, PhotoSpec.PortraitWidth, PhotoSpec.PortraitHeight,
            onValid: () => _imgCropPopup.Open(
                Loc.T("profile.rp_crop_image"),
                handle,
                1.6f,
                cropRect =>
                {
                    row.StagedCrop = cropRect;
                    row.StagedConfirmed = true;
                    row.PendingRemoveImage = false;
                },
                onCancel: Unload),
            onReject: Unload);
    }

    private void DrawRpSelfieButton(RpCharRow row, string id, ThemeDefinition t)
    {
        PushThemeButton(t);
        if (ImGui.Button($"{Loc.T("common.selfie")}##rpSelfie{id}", Px(140f, 24f)))
        {
            StartRpImageSelfie(row);
        }
        PopThemeButton();
    }

    private void SaveRpCharacters()
    {
        if (_rpSaving)
        {
            return;
        }
        _rpSaving = true;
        var ct = _cts.Token;

        var request = new SaveCharactersRequest(
            _rpRows.Select(r => new CharacterSaveDto(r.Id, r.Name.Trim(), r.Bio.Trim())).ToArray());
        var stagedRows = _rpRows.ToArray();

        _ = Task.Run(async () =>
        {
            try
            {
                var saved = await _hubClient.SaveCharactersAsync(request, ct).ConfigureAwait(false);

                // Adopt server ids for subsequent image uploads.
                for (var i = 0; i < stagedRows.Length && i < saved.Characters.Length; i++)
                {
                    stagedRows[i].Id = saved.Characters[i].Id;
                }

                foreach (var row in stagedRows)
                {
                    if (row.Id is not { } charId)
                    {
                        continue;
                    }
                    if (row.StagedConfirmed && row.StagedPath.Length > 0)
                    {
                        var dto = ReadPhotoUpload(row.StagedPath, row.StagedCrop, row.StagedNsfw, PhotoKind.Portrait);
                        await _hubClient.SetCharacterImageAsync(charId, dto, ct).ConfigureAwait(false);
                    }
                    else if (row.PendingRemoveImage)
                    {
                        await _hubClient.RemoveCharacterImageAsync(charId, ct).ConfigureAwait(false);
                    }

                    for (short s = 1; s <= row.Extras.Length; s++)
                    {
                        var slot = row.Extras[s - 1];
                        if (slot.StagedConfirmed && slot.StagedPath.Length > 0)
                        {
                            var dto = ReadPhotoUpload(slot.StagedPath, slot.StagedCrop, slot.StagedNsfw, PhotoKind.Portrait);
                            await _hubClient.SetCharacterExtraImageAsync(charId, s, dto, ct).ConfigureAwait(false);
                        }
                        else if (slot.PendingRemove)
                        {
                            await _hubClient.RemoveCharacterExtraImageAsync(charId, s, ct).ConfigureAwait(false);
                        }
                    }
                }

                if (ct.IsCancellationRequested)
                {
                    return;
                }

                var fresh = await _hubClient.GetMyCharactersAsync(ct).ConfigureAwait(false);
                if (ct.IsCancellationRequested)
                {
                    return;
                }
                HydrateRpRows(fresh);
                _rpSavedTimer = 2.5f;
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
                UiHost.Log.Warning(ex, "[MyProfileScreen] SaveRpCharacters failed.");
            }
            finally
            {
                _rpSaving = false;
            }
        }, ct);
    }

    private void DrawRpIntroOverlay(Vector2 windowPos, Vector2 windowSize)
    {
        if (!_showRpIntro)
        {
            return;
        }

        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(windowPos, windowPos + windowSize,
            ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.55f)));

        ImGui.SetCursorScreenPos(windowPos);
        if (ImGui.InvisibleButton("##rpIntroScrim", windowSize))
        {
            _showRpIntro = false;
        }

        var w = Px(272f);
        var pad = Px(16f, 16f);
        var h = _rpIntroHeight > 0f ? _rpIntroHeight : Px(190f);
        var panelPos = windowPos + (windowSize - new Vector2(w, h)) * 0.5f;

        ImGui.SetCursorScreenPos(panelPos);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.11f, 0.10f, 0.13f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.32f, 0.30f, 0.38f, 0.65f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, Px(12f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, pad);
        using (var child = ImRaii.Child("##rpIntroPanel", new Vector2(w, h), true,
                   ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.AlwaysUseWindowPadding))
        {
            if (child.Success)
            {
                var innerW = ImGui.GetContentRegionAvail().X;
                ImGui.PushTextWrapPos(innerW);
                ModalUi.Header(innerW, FontAwesomeIcon.TheaterMasks, Loc.T("profile.rp_intro_title"), ThemeService.Current.AccentLight);
                ImGui.TextColored(UiColors.Body, Loc.T("profile.rp_intro"));
                ImGui.Spacing();
                ImGui.Spacing();
                if (ModalUi.Button($"{Loc.T("common.ok")}##rpIntroOk", innerW))
                {
                    _showRpIntro = false;
                }
                ImGui.PopTextWrapPos();
                _rpIntroHeight = ImGui.GetCursorPosY() + pad.Y;
            }
        }
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(2);
    }
}
