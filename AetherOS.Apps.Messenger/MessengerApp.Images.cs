using System;
using System.Collections.Concurrent;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Shared;
using AetherLove.Shared.Messenger;
using AetherLove.Shared.Profile;
using AetherLove.UI;
using AetherLove.Widgets;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Messenger;

/// <summary>Image attachments: pick (camera / Photos / disk-crop), the compose+preview with the not-E2E
/// notice, the synchronous send, the in-bubble thumbnail + full-size viewer, the expired placeholder, report,
/// and the "Data limits" storage screen.</summary>
public sealed partial class MessengerApp
{
    private sealed record ImageVisual(ISharedImmediateTexture? Tex, bool Gone);

    private readonly ConcurrentDictionary<Guid, ImageVisual> _imageCache = new();
    private readonly ConcurrentDictionary<Guid, byte> _imageFetches = new();

    private (Guid ChatId, MessengerChatKind Kind, int Epoch)? _pendingPhotoChat;

    private string? _pendingImagePath;
    private Vector4 _pendingImageCrop;
    private Guid _pendingImageChatId;
    private MessengerChatKind _pendingImageKind;
    private int _pendingImageEpoch;
    private bool _pendingImageFromCamera;
    private int _pendingExpiryHours = SupporterLimits.RegularImageTtlHours;
    private volatile bool _imageSending;
    private volatile string? _imageError;

    private Guid _viewerImageId = Guid.Empty;

    private Guid _imageReportId = Guid.Empty;
    private string _imageReportReason = string.Empty;
    private volatile bool _imageReportSubmitting;
    private volatile string? _imageReportError;

    private bool _dataLimitsOpen;
    private volatile bool _storageLoading;
    private MessengerStorageDto? _storage;

    private const float ImageBubbleMaxH = 240f;

    internal bool ImageOverlayActive => _pendingImagePath is not null || _viewerImageId != Guid.Empty
        || _imageReportId != Guid.Empty || _dataLimitsOpen;

    private void DrawAttachPhotoMenu(OpenChatInfo open)
    {
        var chatId = open.ChatId;
        var kind = open.Kind;
        var epoch = open.Kind == MessengerChatKind.Group ? open.Group!.KeyEpoch : 0;
        if (DrawIconMenuItem(FontAwesomeIcon.Camera, Loc.T("os.msgr_attach_camera")))
        {
            ImGui.CloseCurrentPopup();
            _caps.Camera.Capture(new CameraRequest(1f, 128, FreeForm: true),
                shot => _uiActions.Enqueue(() => BeginImageCompose(chatId, kind, epoch, shot.Path, shot.Crop, fromCamera: true)));
        }
        if (DrawIconMenuItem(FontAwesomeIcon.Image, Loc.T("os.msgr_attach_photos")))
        {
            ImGui.CloseCurrentPopup();
            _pendingPhotoChat = (chatId, kind, epoch);
            _shell?.SendIntent("photos", OsIntents.Create(OsIntents.PickPhoto));
        }
        if (DrawIconMenuItem(FontAwesomeIcon.FolderOpen, Loc.T("os.msgr_attach_disk")))
        {
            ImGui.CloseCurrentPopup();
            _caps.Images.PickFile(
                new ImagePickRequest(Loc.T("os.msgr_attach_disk"), "Images{.png,.jpg,.jpeg,.webp}"),
                path => _uiActions.Enqueue(() => BeginImageCompose(chatId, kind, epoch, path, WholeImage)));
        }
    }

    /// <summary>The sentinel crop rect meaning "keep all of it": a picked picture is sent whole, because a
    /// chat attachment is not a portrait and a forced square would cut half of a landscape shot away.</summary>
    private static readonly Vector4 WholeImage = new(0f, 0f, 100000f, 100000f);

    /// <summary>The Photos app returned a picked image path (PhotoPicked intent).</summary>
    private void OnPhotoPicked(string path)
    {
        if (_pendingPhotoChat is not { } ctx)
        {
            return;
        }
        _pendingPhotoChat = null;
        BeginImageCompose(ctx.ChatId, ctx.Kind, ctx.Epoch, path, WholeImage);
    }

    private void BeginImageCompose(Guid chatId, MessengerChatKind kind, int epoch, string path, Vector4 crop,
        bool fromCamera = false)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return;
        }
        _pendingImagePath = path;
        _pendingImageCrop = crop;
        _pendingImageChatId = chatId;
        _pendingImageKind = kind;
        _pendingImageEpoch = epoch;
        _pendingImageFromCamera = fromCamera;
        _pendingExpiryHours = SupporterLimits.RegularImageTtlHours;
        _imageSending = false;
        _imageError = null;
        // Pull a fresh storage snapshot so the compose popup shows current usage and knows the supporter tier.
        FetchStorage();
    }

    private void SendPendingImage()
    {
        if (_pendingImagePath is not { } path || _imageSending)
        {
            return;
        }
        _imageSending = true;
        _imageError = null;
        var chatId = _pendingImageChatId;
        var kind = _pendingImageKind;
        var epoch = _pendingImageEpoch;
        var crop = _pendingImageCrop;
        var fromCamera = _pendingImageFromCamera;
        var expiryHours = _pendingExpiryHours;
        _ = Task.Run(async () =>
        {
            try
            {
                var bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
                var upload = new PhotoUploadDto(Convert.ToBase64String(bytes),
                    (int)crop.X, (int)crop.Y, (int)crop.Z, (int)crop.W, false);
                var dto = await _hub.SendMessengerImageAsync(
                        new SendMessengerImageRequest(chatId, kind, epoch, upload,
                            FromCamera: fromCamera, ExpiryHours: expiryHours))
                    .ConfigureAwait(false);
                _store.ApplyMessage(dto);
                _uiActions.Enqueue(() =>
                {
                    _pendingImagePath = null;
                    _scrollToBottom = 1f;
                });
            }
            catch (Exception ex)
            {
                _imageError = FriendlyHubError(ex);
                AetherLove.UiHost.Log.Warning(ex, "[MessengerApp] image send failed.");
            }
            finally
            {
                _imageSending = false;
            }
        });
    }

    /// <summary>Turns a pixel crop rectangle (x, y, width, height) into clamped UV coordinates for the source
    /// image, so a preview shows only the region the server will keep.</summary>
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

    private void DrawImageComposeOverlay(Vector2 contentTL, Vector2 contentSize)
    {
        if (_pendingImagePath is not { } path)
        {
            return;
        }
        var tex = _caps.Textures.Get(path);
        var texSize = _caps.Textures.GetSize(path);
        var supporter = _storage?.IsSupporter ?? false;
        DrawCenteredPanel(contentTL, contentSize, Loc.T("os.msgr_image_send_title"), Px(380f), innerW =>
        {
            var dl = ImGui.GetWindowDrawList();

            // Cropped preview (only the region the server will keep, not the whole source image).
            var origin = ImGui.GetCursorScreenPos();
            if (tex is { } handle && texSize is { } sz)
            {
                var (uv0, uv1) = CropUv(_pendingImageCrop, sz);
                var aspect = AspectFromUv(uv0, uv1, sz);
                var boxW = innerW;
                var boxH = boxW / aspect;
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
                ImGui.Dummy(box + new Vector2(0, Px(10f)));
            }

            // Current storage usage.
            if (_storage is { } s)
            {
                var usedMb = s.UsedBytes / 1024f / 1024f;
                var capMb = s.CapBytes / 1024f / 1024f;
                ImGui.TextColored(MutedText,
                    Loc.T("os.msgr_image_storage", usedMb.ToString("0.#"), capMb.ToString("0")));
            }
            else
            {
                ImGui.TextColored(MutedText, Loc.T("os.msgr_image_loading"));
            }
            ImGui.Dummy(new Vector2(0f, Px(6f)));

            // Expiry picker.
            ImGui.TextColored(BodyText, Loc.T("os.msgr_image_expiry_label"));
            ImGui.Dummy(new Vector2(0f, Px(4f)));
            DrawExpiryPicker(innerW, supporter);
            ImGui.Dummy(new Vector2(0f, Px(8f)));

            // Wrap at the content region's right edge (0f), so the notice never runs under the scrollbar.
            ImGui.PushTextWrapPos(0f);
            ImGui.TextColored(WarnAmber, Loc.T("os.msgr_image_notice"));
            if (_imageError is { } err)
            {
                ImGui.TextColored(DangerRed, err);
            }
            ImGui.PopTextWrapPos();
            ImGui.Dummy(new Vector2(0f, Px(8f)));

            var btnW = (innerW - Px(8f)) * 0.5f;
            if (NeutralButton(Loc.T("os.msgr_cancel"), btnW) && !_imageSending)
            {
                _pendingImagePath = null;
            }
            ImGui.SameLine(0, Px(8f));
            ImGui.BeginDisabled(_imageSending);
            if (ModalUi.Button(_imageSending ? Loc.T("os.msgr_image_sending") : Loc.T("os.msgr_image_send"), btnW))
            {
                SendPendingImage();
            }
            ImGui.EndDisabled();
        });
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
        ImGui.SetCursorScreenPos(new Vector2(start.X, start.Y + 2f * pillH + gap));
        ImGui.Dummy(new Vector2(innerW, 0f));
    }

    private void DrawExpiryRow(int[] opts, int from, int to, Vector2 rowTL, float innerW, float gap, float pillH, bool supporter)
    {
        var n = to - from;
        if (n <= 0)
        {
            return;
        }
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var pillW = (innerW - gap * (n - 1)) / n;
        for (var i = 0; i < n; i++)
        {
            var hours = opts[from + i];
            var tl = rowTL + new Vector2(i * (pillW + gap), 0f);
            var br = tl + new Vector2(pillW, pillH);
            var perk = SupporterLimits.ImageTtlRequiresSupporter(hours);
            var locked = perk && !supporter;
            var selected = _pendingExpiryHours == hours;

            ImGui.SetCursorScreenPos(tl);
            if (ImGui.InvisibleButton($"##exp{hours}", new Vector2(pillW, pillH)) && !locked)
            {
                _pendingExpiryHours = hours;
            }
            var hovered = ImGui.IsItemHovered();
            if (locked && hovered)
            {
                ImGui.SetTooltip(Loc.T("os.msgr_image_expiry_supporter"));
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
            dl.AddText(new Vector2(tl.X + (pillW - lsz.X) * 0.5f, tl.Y + (pillH - lsz.Y) * 0.5f), textCol, label);

            if (perk)
            {
                IconDraw.AddCentered(dl, FontAwesomeIcon.Star, Px(9f),
                    new Vector2(br.X - Px(7f), tl.Y + Px(7f)), locked ? 0x66FFD700u : UiColors.FavoriteStar);
            }
        }
    }

    private void DrawImageReportOverlay(Vector2 contentTL, Vector2 contentSize)
    {
        if (_imageReportId == Guid.Empty)
        {
            return;
        }
        DrawCenteredPanel(contentTL, contentSize, Loc.T("os.msgr_image_report_title"), Px(320f), innerW =>
        {
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + innerW);
            ImGui.TextColored(BodyText, Loc.T("os.msgr_image_report_prompt"));
            ImGui.PopTextWrapPos();
            ImGui.Dummy(new Vector2(0f, Px(6f)));

            InputTextMultilineWithPaste("##msgrImgReport", ref _imageReportReason, 500, new Vector2(innerW, Px(80f)));
            if (_imageReportError is { } err)
            {
                ImGui.TextColored(DangerRed, err);
            }
            if (_imageReportSubmitting)
            {
                ImGui.TextColored(MutedText, Loc.T("os.msgr_image_sending"));
            }
            ImGui.Dummy(new Vector2(0f, Px(8f)));

            var btnW = (innerW - Px(8f)) * 0.5f;
            if (NeutralButton(Loc.T("os.msgr_cancel"), btnW) && !_imageReportSubmitting)
            {
                _imageReportId = Guid.Empty;
            }
            ImGui.SameLine(0, Px(8f));
            var canSubmit = !string.IsNullOrWhiteSpace(_imageReportReason) && !_imageReportSubmitting;
            ImGui.BeginDisabled(!canSubmit);
            if (ModalUi.Button(Loc.T("os.msgr_image_report_submit"), btnW))
            {
                FireImageReport();
            }
            ImGui.EndDisabled();
        });
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
        var reason = _imageReportReason;
        _ = Task.Run(async () =>
        {
            try
            {
                await _hub.ReportMessengerImageAsync(imageId, reason).ConfigureAwait(false);
                _uiActions.Enqueue(() => _imageReportId = Guid.Empty);
            }
            catch (Exception ex)
            {
                _imageReportError = FriendlyHubError(ex);
                AetherLove.UiHost.Log.Warning(ex, "[MessengerApp] image report failed.");
            }
            finally
            {
                _imageReportSubmitting = false;
            }
        });
    }

    private static bool NeutralButton(string label, float width)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.22f, 0.22f, 0.22f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.38f, 0.38f, 0.38f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.14f, 0.14f, 0.14f, 1f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
        var clicked = ImGui.Button(label, new Vector2(width, Px(32f)));
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);
        return clicked;
    }

    /// <summary>A live push removed this image (owner delete or moderation): flip any cached visual to gone
    /// and delete the disk-cached bytes so nothing survives a restart.</summary>
    private void PurgeRemovedImage(Guid imageId)
    {
        _imageCache[imageId] = new ImageVisual(null, true);
        try
        {
            var dir = Path.Combine(_caps.Storage(Id).Directory, "ImageCache");
            if (Directory.Exists(dir))
            {
                foreach (var file in Directory.GetFiles(dir, $"{imageId:N}_*"))
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception ex)
        {
            AetherLove.UiHost.Log.Warning(ex, "[MessengerApp] purging a removed image's cache failed.");
        }
    }

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
                var bytes = await _hub.GetMessengerImageAsync(imageId).ConfigureAwait(false);
                if (bytes is { Length: > 0 })
                {
                    var dir = Path.Combine(_caps.Storage(Id).Directory, "ImageCache");
                    _imageCache[imageId] = new ImageVisual(AvatarDiskCache.Store(dir, imageId.ToString("N"), bytes), false);
                }
                else
                {
                    _imageCache[imageId] = new ImageVisual(null, true);
                }
            }
            catch (Exception ex)
            {
                _imageCache[imageId] = new ImageVisual(null, true);
                AetherLove.UiHost.Log.Warning(ex, "[MessengerApp] image fetch failed.");
            }
            finally
            {
                _imageFetches.TryRemove(imageId, out _);
            }
        });
    }

    private Vector2 ImageThumbSize(MessengerImageDto img, float windowWidth)
    {
        var maxW = windowWidth * 0.58f;
        var maxH = Px(ImageBubbleMaxH);
        float w = img.Width > 0 ? img.Width : 4;
        float h = img.Height > 0 ? img.Height : 3;
        var scale = MathF.Min(maxW / w, maxH / h);
        return new Vector2(MathF.Max(Px(80f), w * scale), MathF.Max(Px(60f), h * scale));
    }

    private float ImageRowHeight(OpenChatInfo open, MessengerMessageDto msg, bool isGroupStart, bool isGroupEnd, float windowWidth)
    {
        var size = ImageThumbSize(msg.Image!, windowWidth);
        var authorH = AuthorNameHeight(open, msg, isGroupStart);
        var footerH = isGroupEnd ? ImGui.GetTextLineHeight() + Px(8f) : Px(2f);
        return authorH + size.Y + Px(14f) + footerH;
    }

    private void DrawImageMessage(OpenChatInfo open, MessengerMessageDto msg, float windowWidth, bool isGroupStart, bool isGroupEnd)
    {
        var img = msg.Image!;
        var mine = msg.SenderAccountId == _store.MyAccountId;
        var dl = ImGui.GetWindowDrawList();
        var cursorPos = ImGui.GetCursorScreenPos();
        var groupPeer = open.Kind == MessengerChatKind.Group && !mine;
        var avatarGutter = groupPeer ? Px(34f) : 0f;

        var (entryDy, entryAlpha) = MessageEntrance(msg.Id);
        var fading = entryAlpha < 0.999f;
        if (fading)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, entryAlpha * ImGui.GetStyle().Alpha);
        }

        var size = ImageThumbSize(img, windowWidth);
        var left = mine ? cursorPos.X + windowWidth - size.X - Px(10f) : cursorPos.X + Px(10f) + avatarGutter;

        var authorH = AuthorNameHeight(open, msg, isGroupStart);
        if (authorH > 0f)
        {
            dl.AddText(new Vector2(left + Px(2f), cursorPos.Y + entryDy),
                AuthorColor(msg.SenderAccountId), SenderName(open, msg.SenderAccountId));
        }

        var tl = new Vector2(left, cursorPos.Y + entryDy + authorH);
        var br = tl + size;

        ImGui.SetCursorScreenPos(tl);
        var clicked = ImGui.InvisibleButton($"##msgrImg{msg.Id:N}", size);
        var hovered = ImGui.IsItemHovered();

        var expired = img.ExpiresAtUtc <= DateTimeOffset.UtcNow
            || _store.IsImageRemoved(img.ImageId)
            || (_imageCache.TryGetValue(img.ImageId, out var gone) && gone.Gone);

        if (expired)
        {
            dl.AddRectFilled(tl, br, White(0.06f), Px(12f));
            dl.AddRect(tl, br, White(0.14f), Px(12f), ImDrawFlags.None, Px(1f));
            IconCentered(dl, FontAwesomeIcon.BellSlash, Px(24f), tl + size * 0.5f - new Vector2(0, Px(10f)), White(0.4f));
            CenteredText(dl, Loc.T("os.msgr_image_expired"), tl.X + size.X * 0.5f, tl.Y + size.Y * 0.5f + Px(12f), White(0.5f));
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
                CenteredText(dl, Loc.T("os.msgr_image_loading"), tl.X + size.X * 0.5f, tl.Y + size.Y * 0.5f, White(0.4f));
            }

            var days = Math.Max(1, (int)MathF.Ceiling((float)(img.ExpiresAtUtc - DateTimeOffset.UtcNow).TotalDays));
            var label = Loc.T("os.msgr_image_expires", days);
            var lsz = ImGui.CalcTextSize(label);
            var pillBr = new Vector2(br.X - Px(6f), br.Y - Px(6f));
            var pillTl = pillBr - lsz - Px(10f, 4f);
            dl.AddRectFilled(pillTl, pillBr, 0xB0000000u, Px(6f));
            dl.AddText(pillTl + Px(5f, 2f), White(0.85f), label);
        }

        // A picture that is already gone has nothing left to report or delete.
        if (!expired && ImGui.BeginPopupContextItem($"##msgrImgCtx{msg.Id:N}", ImGuiPopupFlags.MouseButtonRight))
        {
            if (ImGui.MenuItem(Loc.T("os.msgr_image_report")))
            {
                _imageReportId = img.ImageId;
                _imageReportReason = string.Empty;
                _imageReportError = null;
                _imageReportSubmitting = false;
            }
            if (mine && ImGui.MenuItem(Loc.T("os.msgr_image_delete")))
            {
                RunHub(() => _hub.DeleteMessengerImageAsync(img.ImageId));
                _imageCache[img.ImageId] = new ImageVisual(null, true);
            }
            ImGui.EndPopup();
        }

        if (groupPeer && isGroupEnd)
        {
            DrawSenderGutterAvatar(open, msg, dl, cursorPos.X, br.Y);
        }

        if (isGroupEnd)
        {
            var timeStr = msg.CreatedAtUtc.LocalDateTime.ToString("HH:mm");
            var tsz = ImGui.CalcTextSize(timeStr);
            var tx = mine ? br.X - tsz.X : tl.X;
            dl.AddText(new Vector2(tx, br.Y + Px(3f)), Col(new Vector4(0.75f, 0.75f, 0.75f, 0.4f)), timeStr);
            ImGui.SetCursorScreenPos(cursorPos + new Vector2(0, authorH + size.Y + Px(14f) + tsz.Y));
        }
        else
        {
            ImGui.SetCursorScreenPos(cursorPos + new Vector2(0, authorH + size.Y + Px(14f)));
        }
        if (fading)
        {
            ImGui.PopStyleVar();
        }
    }

    /// <summary>The full-size image opens in its own free-floating, resizable ImGui window that lives outside the
    /// phone bezel (a nested top-level Begin), so it can be dragged onto a second monitor and sized freely.</summary>
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
            new Vector2(firstW + Px(12f), firstW * (imgH / imgW) + ImGui.GetFrameHeightWithSpacing() + Px(12f)),
            ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(Px(180f), Px(160f)),
            new Vector2(float.MaxValue, float.MaxValue));

        var open = true;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(Px(6f), Px(6f)));
        if (ImGui.Begin($"{Loc.T("os.msgr_image_viewer")}##msgrImageViewer", ref open,
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

    private void OpenDataLimits()
    {
        _dataLimitsOpen = true;
        _storage = null;
        FetchStorage();
    }

    /// <summary>Pulls a fresh storage snapshot (usage, cap, supporter tier, stored images) shared by the
    /// Data-limits screen and the compose popup's usage line.</summary>
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
                _storage = await _hub.GetMessengerStorageAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AetherLove.UiHost.Log.Warning(ex, "[MessengerApp] storage fetch failed.");
            }
            finally
            {
                _storageLoading = false;
            }
        });
    }

    private void DrawDataLimitsOverlay(Vector2 contentTL, Vector2 contentSize)
    {
        if (!_dataLimitsOpen)
        {
            return;
        }
        DrawCenteredPanel(contentTL, contentSize, Loc.T("os.msgr_datalimits_title"), Px(360f), innerW =>
        {
            if (_storageLoading || _storage is null)
            {
                ImGui.TextColored(MutedText, Loc.T("os.msgr_image_loading"));
            }
            else
            {
                var s = _storage;
                var usedMb = s.UsedBytes / 1024f / 1024f;
                var capMb = s.CapBytes / 1024f / 1024f;
                var frac = s.CapBytes > 0 ? Math.Clamp((float)s.UsedBytes / s.CapBytes, 0f, 1f) : 0f;
                ImGui.TextColored(BodyText, Loc.T("os.msgr_datalimits_usage", usedMb.ToString("0.#"), capMb.ToString("0")));
                var dl = ImGui.GetWindowDrawList();
                var barTl = ImGui.GetCursorScreenPos();
                dl.AddRectFilled(barTl, barTl + new Vector2(innerW, Px(10f)), White(0.10f), Px(5f));
                dl.AddRectFilled(barTl, barTl + new Vector2(innerW * frac, Px(10f)), ThemeService.Current.AccentU32, Px(5f));
                ImGui.Dummy(new Vector2(innerW, Px(16f)));

                ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + innerW);
                ImGui.TextColored(MutedText, Loc.T("os.msgr_image_notice"));
                ImGui.PopTextWrapPos();
                ImGui.Spacing();

                foreach (var item in s.Items)
                {
                    var mb = item.ByteSize / 1024f / 1024f;
                    var days = Math.Max(1, (int)Math.Ceiling((item.ExpiresAtUtc - DateTimeOffset.UtcNow).TotalDays));
                    var where = item.IsLoveChat
                        ? Loc.T("os.msgr_datalimits_lovechat", item.ChatName)
                        : item.ChatName;
                    ImGui.TextColored(BodyText, Loc.T("os.msgr_datalimits_row", mb.ToString("0.#"), where, days));
                    ImGui.SameLine();
                    if (ImGui.SmallButton($"{Loc.T("os.msgr_image_delete")}##del{item.ImageId:N}"))
                    {
                        var id = item.ImageId;
                        var loveChat = item.IsLoveChat;
                        // Drop the cached texture so the chat bubble flips to the removed placeholder instead
                        // of rendering the now-deleted image from memory.
                        _imageCache[id] = new ImageVisual(null, true);
                        RunHub(async () =>
                        {
                            if (loveChat)
                            {
                                await _hub.DeleteChatImageAsync(id).ConfigureAwait(false);
                            }
                            else
                            {
                                await _hub.DeleteMessengerImageAsync(id).ConfigureAwait(false);
                            }
                            _uiActions.Enqueue(OpenDataLimits);
                        });
                    }
                }
            }

            ImGui.Spacing();
            if (ModalUi.Button(Loc.T("os.msgr_close"), innerW))
            {
                _dataLimitsOpen = false;
            }
        });
    }

    /// <summary>A centred, scrollable in-page popup drawn in its own full-content overlay child so the scrim and
    /// panel layer above the chat list (drawing on the parent window's list renders beneath the list's scroll
    /// child). The panel body is a child window, so its controls still win hover over the scrim catcher.</summary>
    private void DrawCenteredPanel(Vector2 contentTL, Vector2 contentSize, string title, float width, Action<float> body)
    {
        var t = ThemeService.Current;
        ImGui.SetCursorScreenPos(contentTL);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        using var overlay = ImRaii.Child("##msgrPanelOverlay", contentSize, false,
            ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        ImGui.PopStyleVar();
        if (!overlay.Success)
        {
            return;
        }

        var ease = PanelFade(title);
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(contentTL, contentTL + contentSize, Col(ScrimColor with { W = ScrimColor.W * ease }));

        var w = MathF.Min(width, contentSize.X - Px(24f));
        var panelH = MathF.Min(contentSize.Y * 0.74f, Px(460f));
        var panelTL = contentTL + new Vector2((contentSize.X - w) * 0.5f, (contentSize.Y - panelH) * 0.5f);
        panelTL.Y += (1f - ease) * Px(10f);
        dl.AddRectFilled(panelTL, panelTL + new Vector2(w, panelH),
            ImGui.ColorConvertFloat4ToU32(new Vector4(0.09f, 0.09f, 0.12f, 0.99f * ease)), Px(16f));
        dl.AddRect(panelTL, panelTL + new Vector2(w, panelH),
            ImGui.GetColorU32(t.Accent with { W = 0.45f * ease }), Px(16f), ImDrawFlags.RoundCornersAll, Px(1.5f));

        // The header is drawn inside the body with the standard ModalUi centred title + separator, matching the
        // AetherLove / Places / Hangouts in-phone modals.
        ImGui.SetCursorScreenPos(panelTL + new Vector2(Px(16f), Px(14f)));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, ease * ImGui.GetStyle().Alpha);
        using (var child = ImRaii.Child("##msgrPanelBody", new Vector2(w - Px(32f), panelH - Px(28f)), false,
                   ImGuiWindowFlags.NoBackground))
        {
            if (child)
            {
                ModalUi.Header(w - Px(32f), title, t.Accent);
                body(w - Px(32f));
            }
        }
        ImGui.PopStyleVar(2);

        ImGui.SetCursorScreenPos(contentTL);
        ImGui.InvisibleButton("##msgrPanelScrim", contentSize);
    }

    private static uint White(float alpha) => ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, alpha));

    private static void CenteredText(ImDrawListPtr dl, string text, float cx, float cy, uint color)
    {
        var sz = ImGui.CalcTextSize(text);
        dl.AddText(new Vector2(cx - sz.X * 0.5f, cy - sz.Y * 0.5f), color, text);
    }
}
