using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Widgets;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Interface.Textures;

namespace AetherLove.Os;

/// <summary>The plugin's implementation of the shared app-capability surface. One instance is registered for
/// DI and handed to every app, so features that used to be copied into each app's host bridge (the selfie
/// camera, disk image picking, texture caching, host side effects) live here once.</summary>
public sealed class AppCapabilities : IAppCapabilities
{
    private readonly ImagePickerService _images;
    private readonly AppStorageService _storage;
    private readonly ServerBarService _serverBar;

    public AppCapabilities(SelfieCaptureOverlay selfie, ImageRequirementsModal imageReqModal, ShareService share,
        AppStorageService storage, AudioService audio, Services.Together.TogetherStateService togetherState,
        Services.Translation.TranslationService translation, Config.Configuration config,
        ServerBarService serverBar)
    {
        _serverBar = serverBar;
        Audio = audio;
        Camera = new CameraService(selfie);
        _images = new ImagePickerService(imageReqModal);
        Textures = new TextureCacheService();
        Effects = new ImageEffectsService();
        System = new SystemBridgeService();
        Keyboard = new KeyboardInputService();
        Share = share;
        Travel = new LifestreamBridge();
        Party = new PartyStateService(togetherState);
        Translation = new TranslationBridgeService(translation, config);
        _storage = storage;
    }

    public ICameraService Camera { get; }
    public IImagePicker Images => _images;
    public ITextureCache Textures { get; }
    public IImageEffects Effects { get; }
    public ISystemBridge System { get; }
    public IKeyboardInput Keyboard { get; }
    public IAudioPlayer Audio { get; }
    public IShareService Share { get; }
    public ITravelBridge Travel { get; }
    public IPartyState Party { get; }
    public ITranslationBridge Translation { get; }

    public IAppStorage Storage(string appId) => _storage.For(appId);

    public AetherOS.Sdk.IServerBar ServerBar(string appId) => _serverBar.For(appId);

    /// <summary>Whether an app is holding exclusive keyboard capture this frame. Shell controls that fire on
    /// release must adapt while this is live: the hidden capture field takes the active id back on the frame
    /// after any press, so a release can never land on them, and they fire on the press instead.</summary>
    public static bool ExclusiveInputActive => _lastExclusiveFrame >= ImGui.GetFrameCount() - 1;

    private static int _lastExclusiveFrame = -1;

    /// <summary>Draws the shared file dialog and crop popup. The shell calls this once per frame so apps never
    /// host their own picking overlays.</summary>
    public void DrawFrame() => _images.DrawFrame();

    /// <summary>The translation capability over the client-direct Google service (ADR 9); the opt-in and
    /// the target language live in the OS settings config so the settings app and the consent popup write
    /// the same switch.</summary>
    private sealed class TranslationBridgeService(
        Services.Translation.TranslationService service, Config.Configuration config) : ITranslationBridge
    {
        public bool Enabled => config.OsSettings.TranslationsEnabled;

        public string TargetLanguage => config.OsSettings.TranslationLanguage;

        public void Enable()
        {
            config.OsSettings.TranslationsEnabled = true;
            config.Save();
        }

        public async Task<TranslationResult?> TranslateAsync(string text, CancellationToken ct = default)
        {
            if (!Enabled)
            {
                return null;
            }
            var result = await service.TranslateAsync(text, TargetLanguage, ct).ConfigureAwait(false);
            return result is null ? null : new TranslationResult(result.Text, result.SourceLanguage);
        }
    }

    /// <summary>Read-only party view for apps over the client party state. The state service raises
    /// PartyChanged from its per-frame drain, so the forwarded event is already on the draw thread.</summary>
    private sealed class PartyStateService : IPartyState
    {
        private readonly Services.Together.TogetherStateService _state;

        public PartyStateService(Services.Together.TogetherStateService state)
        {
            _state = state;
            _state.PartyChanged += () => Changed?.Invoke();
        }

        public bool InParty => _state.EndReason is null && _state.Party is not null;

        public Guid? PartyId => _state.EndReason is null ? _state.Party?.Id : null;

        public string? Code => _state.EndReason is null ? _state.Party?.Code : null;

        public bool AmHost => _state.AmHost;

        public Guid? OwnAccountId => _state.OwnAccountId;

        public PartyActivityInfo? Activity =>
            _state.Activity is { } activity
                ? new PartyActivityInfo(activity.AppId, activity.RefId, activity.Code)
                : null;

        public IReadOnlyList<PartyMemberInfo> Members
        {
            get
            {
                var members = _state.Members;
                var result = new PartyMemberInfo[members.Count];
                for (var i = 0; i < members.Count; i++)
                {
                    var m = members[i];
                    result[i] = new PartyMemberInfo(m.AccountId, m.DisplayName, m.IsHost, m.Connected);
                }
                return result;
            }
        }

        public event Action? Changed;
    }

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

    /// <summary>Keyboard for apps, built on the one lever that actually stops the game from also acting on the
    /// keystroke: Dalamud withholds keyboard input from the game whenever ImGui wants text, which is why typing
    /// in any plugin's text box does not walk your character. So a polling app parks an invisible, permanently
    /// focused input field in its window; the game then never sees WASD at all and ImGui does.
    ///
    /// Clearing Dalamud's key array cannot achieve this on its own (the game reads movement before a plugin gets
    /// to blank it), so it is kept only to cover the single frame before focus lands. Polling stops entirely
    /// while a GAME text field is open, or the phone would eat the keys meant for the chat box.</summary>
    private sealed class KeyboardInputService : IKeyboardInput
    {
        private static readonly ImGuiKey[] Map =
        [
            ImGuiKey.LeftArrow, ImGuiKey.RightArrow, ImGuiKey.UpArrow, ImGuiKey.DownArrow,
            ImGuiKey.W, ImGuiKey.A, ImGuiKey.S, ImGuiKey.D,
            ImGuiKey.Space,
            ImGuiKey.E, ImGuiKey.LeftCtrl, ImGuiKey.LeftShift, ImGuiKey.Enter, ImGuiKey.Escape, ImGuiKey.Tab,
            ImGuiKey.Key1, ImGuiKey.Key2, ImGuiKey.Key3, ImGuiKey.Key4, ImGuiKey.Key5, ImGuiKey.Key6,
            ImGuiKey.Key7,
            ImGuiKey.Y, ImGuiKey.N,
            ImGuiKey.Q, ImGuiKey.R, ImGuiKey.T, ImGuiKey.U, ImGuiKey.I, ImGuiKey.O, ImGuiKey.P,
            ImGuiKey.F, ImGuiKey.G, ImGuiKey.H, ImGuiKey.J, ImGuiKey.K, ImGuiKey.L,
            ImGuiKey.Z, ImGuiKey.X, ImGuiKey.C, ImGuiKey.V, ImGuiKey.B,
            ImGuiKey.Key8, ImGuiKey.Key9, ImGuiKey.Key0, ImGuiKey.Backspace,
            ImGuiKey.Keypad0, ImGuiKey.Keypad1, ImGuiKey.Keypad2, ImGuiKey.Keypad3, ImGuiKey.Keypad4,
            ImGuiKey.Keypad5, ImGuiKey.Keypad6, ImGuiKey.Keypad7, ImGuiKey.Keypad8, ImGuiKey.Keypad9,
            ImGuiKey.KeypadAdd, ImGuiKey.KeypadSubtract, ImGuiKey.KeypadMultiply, ImGuiKey.KeypadDivide,
            ImGuiKey.KeypadDecimal, ImGuiKey.KeypadEnter,
        ];

        private static readonly VirtualKey[] GameKeys =
        [
            VirtualKey.LEFT, VirtualKey.RIGHT, VirtualKey.UP, VirtualKey.DOWN,
            VirtualKey.W, VirtualKey.A, VirtualKey.S, VirtualKey.D,
            VirtualKey.SPACE,
            VirtualKey.E, VirtualKey.CONTROL, VirtualKey.SHIFT, VirtualKey.RETURN, VirtualKey.ESCAPE, VirtualKey.TAB,
            VirtualKey.KEY_1, VirtualKey.KEY_2, VirtualKey.KEY_3, VirtualKey.KEY_4, VirtualKey.KEY_5,
            VirtualKey.KEY_6, VirtualKey.KEY_7,
            VirtualKey.Y, VirtualKey.N,
            VirtualKey.Q, VirtualKey.R, VirtualKey.T, VirtualKey.U, VirtualKey.I, VirtualKey.O, VirtualKey.P,
            VirtualKey.F, VirtualKey.G, VirtualKey.H, VirtualKey.J, VirtualKey.K, VirtualKey.L,
            VirtualKey.Z, VirtualKey.X, VirtualKey.C, VirtualKey.V, VirtualKey.B,
            VirtualKey.KEY_8, VirtualKey.KEY_9, VirtualKey.KEY_0, VirtualKey.BACK,
            VirtualKey.NUMPAD0, VirtualKey.NUMPAD1, VirtualKey.NUMPAD2, VirtualKey.NUMPAD3, VirtualKey.NUMPAD4,
            VirtualKey.NUMPAD5, VirtualKey.NUMPAD6, VirtualKey.NUMPAD7, VirtualKey.NUMPAD8, VirtualKey.NUMPAD9,
            VirtualKey.ADD, VirtualKey.SUBTRACT, VirtualKey.MULTIPLY, VirtualKey.DIVIDE,
            VirtualKey.DECIMAL, VirtualKey.RETURN,
        ];

        private readonly bool[] _down = new bool[Map.Length];
        private readonly bool[] _edge = new bool[Map.Length];
        private string _sink = string.Empty;
        private int _frame = -1;
        private int _exclusiveFrame = -1;

        public void RequestExclusive()
        {
            _exclusiveFrame = ImGui.GetFrameCount();
            _lastExclusiveFrame = _exclusiveFrame;
        }

        public bool IsDown(AppKey key)
        {
            Refresh();
            return _down[(int)key];
        }

        public bool WasPressed(AppKey key)
        {
            Refresh();
            return _edge[(int)key];
        }

        public bool TryGetPressedKey(out AppKey key)
        {
            Refresh();
            for (var i = 0; i < _edge.Length; i++)
            {
                if (_edge[i])
                {
                    key = (AppKey)i;
                    return true;
                }
            }
            key = default;
            return false;
        }

        public bool GameTextFocused => TextInputActive();

        /// <summary>Samples every mapped key once per frame, so an app may mix held and pressed reads on the
        /// same key without one consuming the other's edge.</summary>
        private void Refresh()
        {
            var frame = ImGui.GetFrameCount();
            if (frame == _frame)
            {
                return;
            }
            _frame = frame;

            var blocked = TextInputActive();
            if (!blocked)
            {
                HoldCapture();
            }
            for (var i = 0; i < Map.Length; i++)
            {
                var down = !blocked && ImGui.IsKeyDown(Map[i]);
                _edge[i] = down && !_down[i];
                _down[i] = down;
                if (down && Plugin.KeyState.IsVirtualKeyValid(GameKeys[i]))
                {
                    Plugin.KeyState[GameKeys[i]] = false;
                }
            }
        }

        /// <summary>A zero-alpha one-pixel input kept focused in the calling app's window. Its only job is to
        /// raise ImGui's want-text flag, which is what makes Dalamud withhold the keys from the game.</summary>
        private void HoldCapture()
        {
            var cursor = ImGui.GetCursorScreenPos();
            ImGui.SetCursorScreenPos(ImGui.GetWindowPos());
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0f);
            ImGui.SetNextItemWidth(1f);
            ImGui.InputText("##osKeyCapture", ref _sink, 8);
            ImGui.PopStyleVar();
            // Never steal focus from an item the user is holding: re-focusing every frame would take the active
            // id back mid-click, and anything that fires on RELEASE (the home pill) would never fire at all.
            //
            // An app that asked for exclusive keys is the exception, and it has to be: pressing the mouse on
            // bare window space makes ImGui's own window-move handle the active item, which deactivates this
            // field, and the game sees the keystrokes again for as long as the button is held. Such an app
            // hit-tests its own controls, so there is no held widget here to protect.
            var exclusive = _exclusiveFrame == ImGui.GetFrameCount();
            var reclaim = exclusive
                ? !ImGui.GetIO().WantTextInput
                : !ImGui.IsItemActive() && !ImGui.IsAnyItemActive();
            if (reclaim)
            {
                ImGui.SetKeyboardFocusHere(-1);
            }
            _sink = string.Empty;
            ImGui.SetCursorScreenPos(cursor);
        }

        private static unsafe bool TextInputActive()
        {
            var ui = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework.Instance()->GetUIModule();
            return ui != null && ui->GetRaptureAtkModule()->AtkModule.IsTextInputActive();
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
        private readonly AetherLove.Widgets.AetherFileDialogManager _fileDialog = new();
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
                if (ok)
                {
                    CropFile(path, request, onPicked);
                }
            });
        }

        public void CropFile(string path, ImageCropRequest request, Action<CroppedImage> onPicked)
        {
            if (_pendingPick.RejectUnavailableCloudFile(path))
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
        }

        public void DrawFrame()
        {
            _fileDialog.Draw();
            _pendingPick.Poll();
            _cropPopup.Draw(ImGui.GetWindowPos(), ImGui.GetWindowSize());
        }
    }
}
