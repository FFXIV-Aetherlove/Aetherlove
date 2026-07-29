using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using AetherLove.Widgets;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Textures;

namespace AetherLove.Os;

/// <summary>The plugin's implementation of the shared app-capability surface. One instance is registered for
/// DI and handed to every app, so features that used to be copied into each app's host bridge (the selfie
/// camera, disk image picking, texture caching, host side effects) live here once.</summary>
public sealed class AppCapabilities : IAppCapabilities
{
    private readonly ImagePickerService _images;
    private readonly AppStorageService _storage;

    public AppCapabilities(SelfieCaptureOverlay selfie, ImageRequirementsModal imageReqModal, ShareService share,
        AppStorageService storage)
    {
        Camera = new CameraService(selfie);
        _images = new ImagePickerService(imageReqModal);
        Textures = new TextureCacheService();
        Effects = new ImageEffectsService();
        System = new SystemBridgeService();
        Share = share;
        _storage = storage;
    }

    public ICameraService Camera { get; }
    public IImagePicker Images => _images;
    public ITextureCache Textures { get; }
    public IImageEffects Effects { get; }
    public ISystemBridge System { get; }
    public IShareService Share { get; }

    public IAppStorage Storage(string appId) => _storage.For(appId);

    /// <summary>Draws the shared file dialog and crop popup. The shell calls this once per frame so apps never
    /// host their own picking overlays.</summary>
    public void DrawFrame() => _images.DrawFrame();

    private sealed class CameraService : ICameraService
    {
        private readonly SelfieCaptureOverlay _selfie;

        public CameraService(SelfieCaptureOverlay selfie) => _selfie = selfie;

        public void Capture(CameraRequest request, Action<CameraShot> onCaptured)
        {
            _selfie.Start(request.Aspect, request.MinCropWidth,
                (path, crop) => onCaptured(new CameraShot(path, crop)), request.FreeForm);
        }
    }

    private sealed class TextureCacheService : ITextureCache
    {
        private readonly Dictionary<string, ISharedImmediateTexture> _cache = new();

        public ImTextureID? Get(string path)
        {
            if (path.Length == 0 || !File.Exists(path))
            {
                return null;
            }
            if (!_cache.TryGetValue(path, out var tex))
            {
                tex = Plugin.TextureProvider.GetFromFile(path);
                _cache[path] = tex;
            }
            return tex.GetWrapOrDefault()?.Handle;
        }

        public System.Numerics.Vector2? GetSize(string path)
        {
            if (path.Length == 0 || !File.Exists(path))
            {
                return null;
            }
            if (!_cache.TryGetValue(path, out var tex))
            {
                tex = Plugin.TextureProvider.GetFromFile(path);
                _cache[path] = tex;
            }
            return tex.GetWrapOrDefault() is { } wrap
                ? new System.Numerics.Vector2(wrap.Width, wrap.Height)
                : null;
        }
    }

    private sealed class SystemBridgeService : ISystemBridge
    {
        public void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "[AppCapabilities] Failed to open URL. Url={Url}", url);
            }
        }

        public void CopyToClipboard(string text) => ImGui.SetClipboardText(text);

        public void OpenMapMarker(uint territoryId, uint mapId, float mapX, float mapY, string? label = null)
        {
            try
            {
                var payload = new Dalamud.Game.Text.SeStringHandling.Payloads.MapLinkPayload(
                    territoryId, mapId, mapX, mapY);
                Plugin.GameGui.OpenMapWithMapLink(payload);
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "[AppCapabilities] Failed to open map marker.");
            }
        }

        private Dictionary<string, ushort>? _emoteCommands;

        public bool TryExecuteEmote(string chatCommand)
        {
            var token = chatCommand.Trim();
            var space = token.IndexOf(' ');
            if (space > 0)
            {
                token = token[..space];
            }
            if (token.Length < 2 || token[0] != '/' || !EmoteCommands().TryGetValue(token, out var emoteId))
            {
                return false;
            }
            _ = Plugin.Framework.RunOnFrameworkThread(() => ExecuteEmoteCore(emoteId));
            return true;
        }

        private static unsafe void ExecuteEmoteCore(ushort emoteId)
        {
            try
            {
                var agent = FFXIVClientStructs.FFXIV.Client.UI.Agent.AgentEmote.Instance();
                if (agent != null && agent->CanUseEmote(emoteId))
                {
                    agent->ExecuteEmote(emoteId, null, true, true);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "[AppCapabilities] Emote execution failed.");
            }
        }

        private Dictionary<string, ushort> EmoteCommands()
        {
            if (_emoteCommands is not null)
            {
                return _emoteCommands;
            }
            var map = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var emote in Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Emote>())
                {
                    if (emote.TextCommand.ValueNullable is not { } tc)
                    {
                        continue;
                    }
                    Add(map, tc.Command, emote.RowId);
                    Add(map, tc.ShortCommand, emote.RowId);
                    Add(map, tc.Alias, emote.RowId);
                    Add(map, tc.ShortAlias, emote.RowId);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "[AppCapabilities] Failed to build the emote command table.");
            }
            _emoteCommands = map;
            return map;

            static void Add(Dictionary<string, ushort> map, Lumina.Text.ReadOnly.ReadOnlySeString s, uint emoteId)
            {
                var text = s.ExtractText();
                if (text.Length > 1 && text[0] == '/')
                {
                    map.TryAdd(text, (ushort)emoteId);
                }
            }
        }

        public void OpenFolder(string path)
        {
            try
            {
                if (global::System.IO.Directory.Exists(path))
                {
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "[AppCapabilities] Failed to open folder. Path={Path}", path);
            }
        }
    }

    private sealed class ImagePickerService : IImagePicker
    {
        private readonly FileDialogManager _fileDialog = new();
        private readonly ImageCropPopup _cropPopup = new();
        private readonly PendingImagePick _pendingPick;

        public ImagePickerService(ImageRequirementsModal imageReqModal) => _pendingPick = new PendingImagePick(imageReqModal);

        public void PickFile(ImagePickRequest request, Action<string> onPicked)
        {
            _fileDialog.OpenFileDialog(request.Title, request.Filters, (ok, path) =>
            {
                if (ok)
                {
                    onPicked(path);
                }
            });
        }

        public void PickAndCrop(ImageCropRequest request, Action<CroppedImage> onPicked)
        {
            _fileDialog.OpenFileDialog(request.Title, request.Filters, (ok, path) =>
            {
                if (!ok || _pendingPick.RejectUnavailableCloudFile(path))
                {
                    return;
                }
                var handle = LoadPickedPreview(path);
                if (handle is null)
                {
                    return;
                }
                _pendingPick.Begin(handle, request.MinWidth, request.MinHeight,
                    onValid: () => _cropPopup.Open(
                        request.CropTitle,
                        handle,
                        request.Aspect,
                        cropRect => onPicked(new CroppedImage(path, handle, cropRect))),
                    onReject: () => { });
            });
        }

        public void DrawFrame()
        {
            _fileDialog.Draw();
            _pendingPick.Poll();
            _cropPopup.Draw(ImGui.GetWindowPos(), ImGui.GetWindowSize());
        }
    }
}
