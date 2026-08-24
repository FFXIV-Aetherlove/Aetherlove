using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Shared;
using AetherLove.Shared.Messaging;
using AetherLove.Shared.Messenger;
using AetherLove.Shared.Profile;
using AetherLove.UI;
using AetherLove.Widgets;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;

namespace AetherLove.Screens;

/// <summary>Selfies in a match chat: the attach menu, the camera round trip, the compose panel, the bubble,
/// the breakout viewer and the report flow. Deliberately the messenger's image UI, beat for beat, down to
/// the expiry pills and the breakout window: it is the same job on a forked chat, and a second look for it
/// would be a defect. The picture is the one thing here the server can read, which is what lets it be
/// screened and reported; the compose panel says so before anything is sent.</summary>
public partial class ChatScreen
{
    private const float ImageBubbleMaxH = 240f;

    private sealed record ImageVisual(ISharedImmediateTexture? Tex, bool Gone);

    private readonly Dictionary<Guid, ImageVisual> _imageCache = new();
    private readonly ConcurrentDictionary<Guid, byte> _imageFetches = new();

    private string? _pendingImagePath;
    private Vector4 _pendingImageCrop;
    private int _pendingExpiryHours = SupporterLimits.RegularImageTtlHours;
    private float _composePanelH;
    private volatile bool _imageSending;
    private volatile string? _imageError;

    private MessengerStorageDto? _storage;
    private volatile bool _storageLoading;

    private Guid _viewerImageId = Guid.Empty;

    private Guid _imageReportId = Guid.Empty;
    private string _imageReportReason = string.Empty;
    private float _imageReportPanelH;
    private volatile bool _imageReportSubmitting;
    private volatile string? _imageReportError;

    private void OnChatImageRemoved(ChatImageRemovedPushDto p) =>
        _uiActions.Enqueue(() => PurgeImage(p.ImageId));

    private void DrawAttachMenu()
    {
        if (!ImGui.BeginPopup("##chatAttach"))
        {
            return;
        }
        if (DrawIconMenuItem(FontAwesomeIcon.Camera, Loc.T("chat.attach_selfie")))
        {
            ImGui.CloseCurrentPopup();
            BeginSelfie();
        }
        if (DrawIconMenuItem(FontAwesomeIcon.CommentDots, Loc.T("chat.menu_invite_messenger"),
                enabled: _messengerStore.Sync?.MyCode is { Length: > 0 }))
        {
            ImGui.CloseCurrentPopup();
            if (_messengerStore.Sync?.MyCode is { Length: > 0 } myCode)
            {
                _pendingShareSend = MessengerShare.Compose(myCode);
            }
        }
        ImGui.EndPopup();
    }

    /// <summary>The capture is the game's own screenshot path, so what is sent is the character, never the
    /// person. The callback lands off the draw thread, hence the queue.</summary>
    private void BeginSelfie() =>
        _caps.Camera.Capture(new CameraRequest(1f, 128, FreeForm: true),
            shot => _uiActions.Enqueue(() => BeginImageCompose(shot.Path, shot.Crop)));

    private void BeginImageCompose(string path, Vector4 crop)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return;
        }
        _pendingImagePath = path;
        _pendingImageCrop = crop;
        _pendingExpiryHours = SupporterLimits.RegularImageTtlHours;
        _composePanelH = 0f;
        _imageSending = false;
        _imageError = null;
        FetchStorage();
    }

    /// <summary>Usage against the account's image budget, which the messenger's attachments share. Pulled
    /// fresh on every compose so the panel's line and the supporter-only expiry options are current.</summary>
    private void FetchStorage()
    {
        if (_storageLoading)
        {
            return;
        }
        _storageLoading = true;
        _ = Task.Run(async () =>
        {
            try
            {
                var storage = await _hub.GetChatImageStorageAsync().ConfigureAwait(false);
                _uiActions.Enqueue(() => _storage = storage);
            }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, "[ChatScreen] image storage fetch failed.");
            }
            finally
            {
                _storageLoading = false;
            }
        });
    }

    private void DrawImageComposeOverlay()
    {
        if (_pendingImagePath is not { } path)
        {
            return;
        }
        var supporter = _storage?.IsSupporter ?? false;
        var dismissed = DrawPageOverlayPanel("chatImageCompose", ImGui.GetWindowPos(), ImGui.GetWindowSize(),
            ref _composePanelH, Px(340f), innerW =>
        {
            ModalUi.Header(innerW, Loc.T("chat.image_send_title"), ThemeService.Current.Accent);
            var dl = ImGui.GetWindowDrawList();

            var origin = ImGui.GetCursorScreenPos();
            var tex = _caps.Textures.Get(path);
            var texSize = _caps.Textures.GetSize(path);
            if (tex is { } handle && texSize is { } sz && sz.X > 0f && sz.Y > 0f)
            {
                var (uv0, uv1) = CropUv(_pendingImageCrop, sz);
                var aspect = AspectFromUv(uv0, uv1, sz);
                var boxW = innerW;
                var boxH = boxW / MathF.Max(0.05f, aspect);
                var maxH = Px(170f);
                if (boxH > maxH)
                {
                    boxH = maxH;
                    boxW = boxH * aspect;
                }
                var tl = origin + new Vector2(MathF.Max(0f, (innerW - boxW) * 0.5f), 0f);
                dl.AddImageRounded(handle, tl, tl + new Vector2(boxW, boxH), uv0, uv1, 0xFFFFFFFFu, Px(10f));
                ImGui.Dummy(new Vector2(innerW, boxH + Px(10f)));
            }
            else
            {
                var box = new Vector2(innerW, Px(140f));
                dl.AddRectFilled(origin, origin + box, White(0.06f), Px(10f));
                ImGui.Dummy(box + new Vector2(0f, Px(10f)));
            }

            if (_storage is { } s)
            {
                var usedMb = s.UsedBytes / 1024f / 1024f;
                var capMb = s.CapBytes / 1024f / 1024f;
                ImGui.TextColored(UiColors.Muted,
                    Loc.T("chat.image_storage", usedMb.ToString("0.#"), capMb.ToString("0")));
            }
            else
            {
                ImGui.TextColored(UiColors.Muted, Loc.T("chat.image_loading"));
            }
            ImGui.Dummy(new Vector2(0f, Px(6f)));

            ImGui.TextColored(UiColors.Body, Loc.T("chat.image_expiry_label"));
            ImGui.Dummy(new Vector2(0f, Px(4f)));
            DrawExpiryPicker(innerW, supporter);
            ImGui.Dummy(new Vector2(0f, Px(8f)));

            ImGui.PushTextWrapPos(0f);
            ImGui.TextColored(UiColors.Amber, Loc.T("chat.image_moderation_note"));
            if (_imageError is { } err)
            {
                ImGui.TextColored(UiColors.Danger, err);
            }
            ImGui.PopTextWrapPos();
            ImGui.Dummy(new Vector2(0f, Px(8f)));

            var btnW = (innerW - Px(8f)) * 0.5f;
            if (NeutralButton(Loc.T("chat.image_cancel"), btnW) && !_imageSending)
            {
                _pendingImagePath = null;
            }
            ImGui.SameLine(0, Px(8f));
            ImGui.BeginDisabled(_imageSending);
            if (ModalUi.Button(_imageSending ? Loc.T("chat.image_sending") : Loc.T("chat.image_send"), btnW))
            {
                SendPendingImage();
            }
            ImGui.EndDisabled();
        });
        if (dismissed && !_imageSending)
        {
            _pendingImagePath = null;
        }
    }

    /// <summary>The 24h / 48h / 72h expiry pills on one row, and the supporter-only 4-7 day options (starred,
    /// selectable only for supporters) together on the row below.</summary>
    private void DrawExpiryPicker(float innerW, bool supporter)
    {
        var opts = SupporterLimits.ImageTtlHourOptions;
        var split = 0;
        while (split < opts.Length && !SupporterLimits.ImageTtlRequiresSupporter(opts[split]))
        {
            split++;
        }
        var gap = Px(6f);
        var pillH = Px(30f);
        var start = ImGui.GetCursorScreenPos();
        DrawExpiryRow(opts, 0, split, start, innerW, gap, pillH, supporter);
        DrawExpiryRow(opts, split, opts.Length, start + new Vector2(0f, pillH + gap), innerW, gap, pillH, supporter);
        ImGui.SetCursorScreenPos(new Vector2(start.X, start.Y + (2f * pillH) + gap));
        ImGui.Dummy(new Vector2(innerW, 0f));
    }

    private void DrawExpiryRow(int[] opts, int from, int to, Vector2 rowTL, float innerW, float gap, float pillH,
        bool supporter)
    {
        var n = to - from;
        if (n <= 0)
        {
            return;
        }
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var pillW = (innerW - (gap * (n - 1))) / n;
        for (var i = 0; i < n; i++)
        {
            var hours = opts[from + i];
            var tl = rowTL + new Vector2(i * (pillW + gap), 0f);
            var br = tl + new Vector2(pillW, pillH);
            var perk = SupporterLimits.ImageTtlRequiresSupporter(hours);
            var locked = perk && !supporter;
            var selected = _pendingExpiryHours == hours;

            ImGui.SetCursorScreenPos(tl);
            if (ImGui.InvisibleButton($"##chatExp{hours}", new Vector2(pillW, pillH)) && !locked)
            {
                _pendingExpiryHours = hours;
            }
            var hovered = ImGui.IsItemHovered();
            if (hovered && !locked)
            {
                HandOnHover();
            }
            if (locked && hovered)
            {
                ImGui.SetTooltip(Loc.T("chat.image_expiry_supporter"));
            }

            var fill = selected
                ? ImGui.GetColorU32(t.Accent with { W = 0.9f })
                : hovered && !locked ? 0x26FFFFFFu : 0x14FFFFFFu;
            dl.AddRectFilled(tl, br, fill, Px(8f));
            if (selected)
            {
                dl.AddRect(tl, br, ImGui.GetColorU32(t.AccentLight), Px(8f), ImDrawFlags.None, Px(1.2f));
            }

            var label = hours >= 96 ? $"{hours / 24}d" : $"{hours}h";
            var lsz = ImGui.CalcTextSize(label);
            var textCol = locked ? 0x66FFFFFFu : selected ? 0xFFFFFFFFu : 0xCCFFFFFFu;
            dl.AddText(new Vector2(tl.X + ((pillW - lsz.X) * 0.5f), tl.Y + ((pillH - lsz.Y) * 0.5f)), textCol, label);

            if (perk)
            {
                IconDraw.AddCentered(dl, FontAwesomeIcon.Star, Px(9f),
                    new Vector2(br.X - Px(7f), tl.Y + Px(7f)), locked ? 0x66FFD700u : UiColors.FavoriteStar);
            }
        }
    }

    private void SendPendingImage()
    {
        if (_pendingImagePath is not { } path || _imageSending || _messageKey is null)
        {
            return;
        }
        _imageSending = true;
        _imageError = null;
        var crop = _pendingImageCrop;
        var hours = _pendingExpiryHours;
        var peer = _peerId;
        _ = Task.Run(async () =>
        {
            try
            {
                var bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
                var upload = new PhotoUploadDto(Convert.ToBase64String(bytes),
                    (int)crop.X, (int)crop.Y, (int)crop.Z, (int)crop.W, false);
                var dto = await _hub.SendChatImageAsync(
                        new SendChatImageRequest(peer, upload, FromCamera: true, ExpiryHours: hours))
                    .ConfigureAwait(false);
                _uiActions.Enqueue(() =>
                {
                    if (_peerId != peer)
                    {
                        return;
                    }
                    lock (_messagesLock)
                    {
                        _messages.Add(new DisplayedMessage(dto.Id, string.Empty, true, dto.CreatedAtUtc, null,
                            Image: dto.Image));
                        _entryAnim[dto.Id] = 0f;
                    }
                    _msgRowH.Clear();
                    _pendingImagePath = null;
                    _scrollToBottom = 1f;
                });
            }
            catch (Exception ex)
            {
                _imageError = ImageErrorText(ex);
                UiHost.Log.Warning(ex, "[ChatScreen] image send failed.");
            }
            finally
            {
                _imageSending = false;
            }
        });
    }

    private static string ImageErrorText(Exception ex)
    {
        var raw = ex.Message ?? string.Empty;
        if (raw.Contains(HubErrors.MsgrImageRejected, StringComparison.Ordinal))
        {
            return Loc.T("chat.image_err_rejected");
        }
        if (raw.Contains(HubErrors.MsgrStorageFull, StringComparison.Ordinal))
        {
            return Loc.T("chat.image_err_full");
        }
        if (raw.Contains(HubErrors.MsgrImageTooLarge, StringComparison.Ordinal))
        {
            return Loc.T("chat.image_err_large");
        }
        return Loc.T("chat.image_err_generic");
    }

    private Vector2 ImageThumbSize(ChatImageDto img, float windowWidth)
    {
        var maxW = windowWidth * 0.58f;
        var maxH = Px(ImageBubbleMaxH);
        float w = img.Width > 0 ? img.Width : 4;
        float h = img.Height > 0 ? img.Height : 3;
        var scale = MathF.Min(maxW / w, maxH / h);
        return new Vector2(MathF.Max(Px(80f), w * scale), MathF.Max(Px(60f), h * scale));
    }

    private void DrawImageMessage(DisplayedMessage msg, float windowWidth, bool isGroupEnd)
    {
        var img = msg.Image!;
        var dl = ImGui.GetWindowDrawList();
        var cursorPos = ImGui.GetCursorScreenPos();
        var size = ImageThumbSize(img, windowWidth);

        var (entryDy, entryAlpha) = MessageEntrance(msg.Id);
        var fading = entryAlpha < 0.999f;
        if (fading)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, entryAlpha * ImGui.GetStyle().Alpha);
        }

        var left = msg.IsOwn ? cursorPos.X + windowWidth - size.X - Px(10f) : cursorPos.X + Px(10f);
        var tl = new Vector2(left, cursorPos.Y + entryDy);
        var br = tl + size;

        ImGui.SetCursorScreenPos(tl);
        var clicked = ImGui.InvisibleButton($"##chatImg{msg.Id:N}", size);
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            HandOnHover();
        }

        var expired = img.ExpiresAtUtc <= DateTimeOffset.UtcNow
            || (_imageCache.TryGetValue(img.ImageId, out var gone) && gone.Gone);

        if (expired)
        {
            dl.AddRectFilled(tl, br, White(0.06f), Px(12f));
            dl.AddRect(tl, br, White(0.14f), Px(12f), ImDrawFlags.None, Px(1f));
            IconDraw.AddCentered(dl, FontAwesomeIcon.BellSlash, Px(24f),
                tl + (size * 0.5f) - new Vector2(0f, Px(10f)), White(0.4f));
            CenteredText(dl, Loc.T("chat.image_expired"), tl.X + (size.X * 0.5f),
                tl.Y + (size.Y * 0.5f) + Px(12f), White(0.5f));
        }
        else
        {
            StartImageFetch(img.ImageId);
            if (_imageCache.TryGetValue(img.ImageId, out var vis) && vis.Tex?.GetWrapOrDefault() is { } wrap)
            {
                dl.AddImageRounded(wrap.Handle, tl, br, Vector2.Zero, Vector2.One, 0xFFFFFFFFu, Px(12f));
                if (hovered)
                {
                    dl.AddRect(tl, br, White(0.35f), Px(12f), ImDrawFlags.None, Px(1.5f));
                }
                if (clicked)
                {
                    _viewerImageId = img.ImageId;
                }
            }
            else
            {
                dl.AddRectFilled(tl, br, White(0.05f), Px(12f));
                CenteredText(dl, Loc.T("chat.image_loading"), tl.X + (size.X * 0.5f), tl.Y + (size.Y * 0.5f),
                    White(0.4f));
            }

            var days = Math.Max(1, (int)MathF.Ceiling((float)(img.ExpiresAtUtc - DateTimeOffset.UtcNow).TotalDays));
            var label = Loc.T("chat.image_expires", days);
            var lsz = ImGui.CalcTextSize(label);
            var pillBr = new Vector2(br.X - Px(6f), br.Y - Px(6f));
            var pillTl = pillBr - lsz - Px(10f, 4f);
            dl.AddRectFilled(pillTl, pillBr, 0xB0000000u, Px(6f));
            dl.AddText(pillTl + Px(5f, 2f), White(0.85f), label);
        }

        // The context menu belongs to the item, not to some other window that happens to draw later, and a
        // picture that is already gone has nothing left to report or delete.
        if (!expired && ImGui.BeginPopupContextItem($"##chatImgCtx{msg.Id:N}", ImGuiPopupFlags.MouseButtonRight))
        {
            if (ImGui.MenuItem(Loc.T("chat.image_report")))
            {
                _imageReportId = img.ImageId;
                _imageReportReason = string.Empty;
                _imageReportError = null;
                _imageReportSubmitting = false;
                _imageReportPanelH = 0f;
            }
            if (msg.IsOwn && ImGui.MenuItem(Loc.T("chat.image_delete")))
            {
                FireImageDelete(img.ImageId);
                _imageCache[img.ImageId] = new ImageVisual(null, true);
            }
            ImGui.EndPopup();
        }

        if (isGroupEnd)
        {
            var local = msg.SentAt.LocalDateTime;
            var seenSuffix = msg.IsOwn && msg.ReadByOtherAtUtc is not null ? Loc.T("chat.seen_suffix") : string.Empty;
            var timeStr = local.ToString("HH:mm") + seenSuffix;
            var timeSize = ImGui.CalcTextSize(timeStr);
            var timeX = msg.IsOwn ? br.X - timeSize.X : tl.X;
            dl.AddText(new Vector2(timeX, br.Y + Px(3f)),
                ImGui.GetColorU32(new Vector4(0.75f, 0.75f, 0.75f, 0.4f)), timeStr);
            ImGui.SetCursorScreenPos(cursorPos + new Vector2(0f, size.Y + timeSize.Y + Px(8f)));
        }
        else
        {
            ImGui.SetCursorScreenPos(cursorPos + new Vector2(0f, size.Y + Px(2f)));
        }

        if (fading)
        {
            ImGui.PopStyleVar();
        }
    }

    /// <summary>The full-size image opens in its own free-floating, resizable ImGui window outside the phone
    /// bezel (a nested top-level Begin), so it can be dragged onto a second monitor and sized freely. The
    /// messenger and Yapper both break out this way; an in-phone lightbox would be a third look.</summary>
    private void DrawImageViewer()
    {
        if (_viewerImageId == Guid.Empty)
        {
            return;
        }
        if (!_imageCache.TryGetValue(_viewerImageId, out var vis) || vis.Tex?.GetWrapOrDefault() is not { } wrap)
        {
            _viewerImageId = Guid.Empty;
            return;
        }
        var imgW = MathF.Max(1f, wrap.Width);
        var imgH = MathF.Max(1f, wrap.Height);

        var firstW = MathF.Min(imgW, Px(560f));
        ImGui.SetNextWindowSize(
            new Vector2(firstW + Px(12f), (firstW * (imgH / imgW)) + ImGui.GetFrameHeightWithSpacing() + Px(12f)),
            ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(Px(180f), Px(160f)),
            new Vector2(float.MaxValue, float.MaxValue));

        var open = true;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(Px(6f), Px(6f)));
        if (ImGui.Begin($"{Loc.T("chat.image_viewer")}##chatImageViewer", ref open,
                ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            var avail = ImGui.GetContentRegionAvail();
            var scale = MathF.Min(avail.X / imgW, avail.Y / imgH);
            if (scale <= 0f || float.IsInfinity(scale))
            {
                scale = 1f;
            }
            var size = new Vector2(imgW * scale, imgH * scale);
            ImGui.SetCursorPos(ImGui.GetCursorPos()
                + new Vector2(MathF.Max(0f, (avail.X - size.X) * 0.5f), MathF.Max(0f, (avail.Y - size.Y) * 0.5f)));
            ImGui.Image(wrap.Handle, size);
            if (ImGui.IsWindowFocused() && ImGui.IsKeyPressed(ImGuiKey.Escape))
            {
                open = false;
            }
        }
        ImGui.End();
        ImGui.PopStyleVar();

        if (!open)
        {
            _viewerImageId = Guid.Empty;
        }
    }

    private void DrawImageReportOverlay()
    {
        if (_imageReportId == Guid.Empty)
        {
            return;
        }
        var dismissed = DrawPageOverlayPanel("chatImageReport", ImGui.GetWindowPos(), ImGui.GetWindowSize(),
            ref _imageReportPanelH, Px(320f), innerW =>
        {
            ModalUi.Header(innerW, Loc.T("chat.image_report_title"), ThemeService.Current.Accent);
            ImGui.PushTextWrapPos(0f);
            ImGui.TextColored(UiColors.Hint, Loc.T("chat.image_report_hint"));
            ImGui.PopTextWrapPos();
            ImGui.Dummy(new Vector2(0f, Px(6f)));
            ImGui.SetNextItemWidth(innerW);
            InputTextMultilineWithPaste("##chatImgReport", ref _imageReportReason, 500, new Vector2(innerW, Px(80f)));
            if (_imageReportError is { } err)
            {
                ImGui.TextColored(UiColors.Danger, err);
            }
            ImGui.Dummy(new Vector2(0f, Px(8f)));

            var btnW = (innerW - Px(8f)) * 0.5f;
            if (NeutralButton(Loc.T("chat.image_cancel"), btnW) && !_imageReportSubmitting)
            {
                _imageReportId = Guid.Empty;
            }
            ImGui.SameLine(0, Px(8f));
            var canSubmit = !string.IsNullOrWhiteSpace(_imageReportReason) && !_imageReportSubmitting;
            ImGui.BeginDisabled(!canSubmit);
            if (ModalUi.Button(Loc.T("chat.image_report_send"), btnW))
            {
                FireImageReport();
            }
            ImGui.EndDisabled();
        });
        if (dismissed && !_imageReportSubmitting)
        {
            _imageReportId = Guid.Empty;
        }
    }

    private void FireImageReport()
    {
        if (_imageReportSubmitting || _imageReportId == Guid.Empty)
        {
            return;
        }
        _imageReportSubmitting = true;
        _imageReportError = null;
        var imageId = _imageReportId;
        var reason = _imageReportReason.Trim();
        _ = Task.Run(async () =>
        {
            try
            {
                await _hub.ReportChatImageAsync(imageId, reason).ConfigureAwait(false);
                _uiActions.Enqueue(() =>
                {
                    _imageReportId = Guid.Empty;
                    _imageReportReason = string.Empty;
                    _reportSubmittedTimer = 4f;
                });
            }
            catch (Exception ex)
            {
                _imageReportError = Loc.T("chat.image_err_generic");
                UiHost.Log.Warning(ex, "[ChatScreen] image report failed.");
            }
            finally
            {
                _imageReportSubmitting = false;
            }
        });
    }

    private void FireImageDelete(Guid imageId) =>
        _ = Task.Run(async () =>
        {
            try
            {
                await _hub.DeleteChatImageAsync(imageId).ConfigureAwait(false);
                _uiActions.Enqueue(() => PurgeImage(imageId));
            }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, "[ChatScreen] image delete failed.");
            }
        });

    private void StartImageFetch(Guid imageId)
    {
        if (_imageCache.ContainsKey(imageId) || !_imageFetches.TryAdd(imageId, 0))
        {
            return;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                var bytes = await _hub.GetChatImageAsync(imageId).ConfigureAwait(false);
                var visual = bytes is { Length: > 0 }
                    ? new ImageVisual(AvatarDiskCache.Store(ImageCacheDir, imageId.ToString("N"), bytes), false)
                    : new ImageVisual(null, true);
                _uiActions.Enqueue(() => _imageCache[imageId] = visual);
            }
            catch (Exception ex)
            {
                _uiActions.Enqueue(() => _imageCache[imageId] = new ImageVisual(null, true));
                UiHost.Log.Warning(ex, "[ChatScreen] image fetch failed.");
            }
            finally
            {
                _imageFetches.TryRemove(imageId, out _);
            }
        });
    }

    private string ImageCacheDir => Path.Combine(_caps.Storage("aetherlove").Directory, "ChatImages");

    /// <summary>The sender took it back, or a moderator did: flip the bubble to the placeholder and drop the
    /// bytes off this disk too, so nothing outlives the removal.</summary>
    private void PurgeImage(Guid imageId)
    {
        _imageCache[imageId] = new ImageVisual(null, true);
        if (_viewerImageId == imageId)
        {
            _viewerImageId = Guid.Empty;
        }
        try
        {
            if (Directory.Exists(ImageCacheDir))
            {
                foreach (var file in Directory.GetFiles(ImageCacheDir, $"{imageId:N}_*"))
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception ex)
        {
            UiHost.Log.Warning(ex, "[ChatScreen] purging a removed image's cache failed.");
        }
    }

    private static bool NeutralButton(string label, float width)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.22f, 0.22f, 0.22f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.38f, 0.38f, 0.38f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.14f, 0.14f, 0.14f, 1f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
        var clicked = ImGui.Button(label, new Vector2(width, Px(32f)));
        HandOnHover();
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);
        return clicked;
    }

    private static uint White(float alpha) => ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, alpha));

    private static void CenteredText(ImDrawListPtr dl, string text, float cx, float cy, uint color)
    {
        var sz = ImGui.CalcTextSize(text);
        dl.AddText(new Vector2(cx - (sz.X * 0.5f), cy - (sz.Y * 0.5f)), color, text);
    }

    private static (Vector2 Uv0, Vector2 Uv1) CropUv(Vector4 crop, Vector2 imgSize)
    {
        if (imgSize.X <= 0f || imgSize.Y <= 0f)
        {
            return (Vector2.Zero, Vector2.One);
        }
        var uv0 = Vector2.Clamp(new Vector2(crop.X / imgSize.X, crop.Y / imgSize.Y), Vector2.Zero, Vector2.One);
        var uv1 = Vector2.Clamp(new Vector2((crop.X + crop.Z) / imgSize.X, (crop.Y + crop.W) / imgSize.Y),
            Vector2.Zero, Vector2.One);
        return (uv0, uv1);
    }

    private static float AspectFromUv(Vector2 uv0, Vector2 uv1, Vector2 imgSize)
    {
        var w = (uv1.X - uv0.X) * imgSize.X;
        var h = (uv1.Y - uv0.Y) * imgSize.Y;
        return h > 0f ? w / h : 1f;
    }
}
