using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove.Config;
using AetherLove.Services;
using AetherLove.Services.Echo;
using AetherLove.Services.Hub;
using AetherLove.Services.Localization;
using AetherLove.Shared;
using AetherLove.Shared.EchoVidya;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Windowing;

namespace AetherLove.Windows;

/// <summary>Echo's popout: the surface video actually plays on, and the room it is watched in. Solo mode is
/// the stage and its transport; room mode adds the playlist, members and chat column beside it.
///
/// Frames arrive from the playback host through the shared-memory buffer it names on launch: a sequence
/// counter is written either side of the pixels, so a copy that raced a paint is skipped rather than torn.</summary>
public sealed partial class EchoWindow : Window, IDisposable
{
    /// <summary>Server-hosted page the playback host drives; it exposes the window.echo bridge.</summary>
    private const string WatchPagePath = "echo/watch";

    private const int FrameHeaderBytes = 16;
    private const int MaxFrameWidth = 1920;
    private const int MaxFrameHeight = 1080;
    private const int Hd720Width = 1280;
    private const int Hd720Height = 720;
    private const int FrameCapacity = FrameHeaderBytes + (MaxFrameWidth * MaxFrameHeight * 4);
    private const int DefaultFrameWidth = 854;
    private const int DefaultFrameHeight = 480;

    private const float MinWindowW = 640f;
    private const float MinWindowH = 420f;
    private const float DefaultWindowW = 1100f;
    private const float DefaultWindowH = 680f;
    private const float MaxWindowSide = 4000f;

    private const float HeaderH = 34f;
    private const float SidebarW = 320f;
    private const float MinStageW = 220f;
    private const float PanelGap = 8f;

    /// <summary>Below this the sidebar takes the window to itself, because the stage no longer fits beside it.
    /// Derived rather than picked: a threshold above what the two panels actually need hides the video on a
    /// window the user is still allowed to have, and the video is the reason the window exists.</summary>
    private const float SidebarCollapseW = MinStageW + PanelGap + SidebarW;
    private const float PanelRounding = 10f;
    private const float StageRounding = 8f;
    private const float BorderThickness = 2f;
    private const float WindowRounding = 6f;
    private const float CardW = 420f;
    private const float CardPad = 26f;
    private const float ButtonH = 30f;

    private const string EndRoomPopupId = "##echoEndRoom";

    private const uint StageFill = 0xFF0B0B0Bu;
    private const uint PanelFill = 0xFF141414u;
    private const uint CardFill = 0xF01A1A1Au;
    private const uint RowHoverFill = 0x14FFFFFFu;

    /// <summary>Seconds a resized stage must hold still before the browser is told to repaint at the new size.
    /// CEF re-lays the page out on every change, so following a drag frame by frame stutters the video.</summary>
    private const float ResizeSettleSeconds = 0.35f;

    private const float ErrorVisibleSeconds = 6f;
    private const float RowEnterSeconds = 0.22f;

    private const int PlayerErrorBadId = 2;
    private const int PlayerErrorRemoved = 100;
    private const int PlayerErrorEmbedBlocked = 101;
    private const int PlayerErrorEmbedDisallowed = 150;

    /// <summary>Stable per-account tints for the initial circles; the plugin has no avatar-URL loader.</summary>
    private static readonly Vector4[] IdentityPalette =
    [
        new(0.36f, 0.55f, 0.85f, 1f),
        new(0.78f, 0.44f, 0.66f, 1f),
        new(0.42f, 0.72f, 0.55f, 1f),
        new(0.85f, 0.60f, 0.34f, 1f),
        new(0.55f, 0.50f, 0.82f, 1f),
        new(0.80f, 0.45f, 0.42f, 1f),
        new(0.38f, 0.70f, 0.75f, 1f),
        new(0.70f, 0.68f, 0.38f, 1f),
    ];

    private enum SidebarPane
    {
        Playlist,
        Members,
        Chat,
    }

    /// <summary>A full-stage explanation: what went wrong in plain language, and the way out of it.</summary>
    private readonly record struct StageNotice(
        FontAwesomeIcon Icon,
        Vector4 Tint,
        string Title,
        string Body,
        string? PrimaryLabel = null,
        Action? Primary = null,
        string? SecondaryLabel = null,
        Action? Secondary = null,
        float? Progress = null);

    private readonly EchoHostClient _host;
    private readonly EchoStateService _state;
    private readonly EchoSyncEngine _sync;
    private readonly EchoHostLocator _locator;
    private readonly AetherHubContext _hub;
    private readonly Configuration _config;

    /// <summary>Hub continuations marshalled onto the draw thread; drained at the top of Draw.</summary>
    private readonly ConcurrentQueue<Action> _uiActions = new();

    private MemoryMappedFile? _map;
    private MemoryMappedViewAccessor? _view;
    private IDalamudTextureWrap? _texture;
    private byte[]? _buffer;
    private string? _mappedName;
    private int _lastSequence;
    private int _frameWidth;
    private int _frameHeight;

    private Guid _myAccountId;
    private readonly AetherOS.Sdk.IAppCapabilities _caps;
    private readonly TranslateUi _translate;
    private string? _menuLine;
    private volatile bool _accountLookupBusy;

    private Guid _knownRoomId;
    private EchoPlaybackDto? _knownPlayback;
    private string? _kickedRoomName;
    private string? _soloVideoId;

    /// <summary>Which service the solo video plays from.</summary>
    private EchoMediaSource _soloSource;

    /// <summary>Whether the solo video is a broadcast. A room reads this off the queued entry, which every
    /// member shares; solo has no entry, so the window asks YouTube itself once per open.</summary>
    private volatile bool _soloIsLive;

    private bool _sidebarOpen = true;
    private bool _confirmEndRoom;
    private SidebarPane _pane = SidebarPane.Playlist;
    private float _paneMarker;

    private string? _actionError;
    private float _errorRemaining;

    private Vector2 _stageSize = new(DefaultFrameWidth, DefaultFrameHeight);
    private Vector2 _pendingStageSize;
    private float _resizeSettle;

    private float _savedFontGlobalScale = 1f;

    /// <summary>Dalamud's collapse is off: a collapsed window skips Draw, and Draw is where the room's
    /// events are drained and the sync ticks, so a collapsed Echo silently stopped following its room.
    /// Minimising is the window's own (<see cref="Minimize"/>), and it keeps ticking.</summary>
    private const ImGuiWindowFlags FullFlags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse
        | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoCollapse;

    private const ImGuiWindowFlags BubbleFlags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize
        | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoDocking
        | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoFocusOnAppearing;

    private const float BubbleSide = 64f;
    private const float BubbleDragThreshold = 5f;
    private const string BubbleLogoFile = "echo.png";

    private bool _minimized;
    private bool _bubbleEntering;
    private bool _restorePending;
    private Vector2 _restoreSize;
    private Vector2 _restorePos;
    private Vector2? _bubblePos;
    private bool _bubbleMouseDown;
    private Vector2 _bubbleMouseDownPos;
    private Vector2 _bubbleWindowPosAtDown;
    private bool _bubbleDragged;
    private Dalamud.Interface.Textures.ISharedImmediateTexture? _bubbleLogo;
    private bool _bubbleLogoLoaded;

    /// <summary>True while the window is drawn as the logo bubble. The room, the host and the sync all
    /// carry on underneath; only the picture is put away.</summary>
    public bool Minimized => _minimized;

    public EchoWindow(
        EchoHostClient host,
        EchoStateService state,
        EchoSyncEngine sync,
        EchoHostLocator locator,
        AetherHubContext hub,
        Configuration config,
        AetherOS.Sdk.IAppCapabilities caps,
        Action openTranslationSettings) : base("Echo###AetherEcho", FullFlags)
    {
        _caps = caps;
        _translate = new TranslateUi("echochat", caps.Translation, openTranslationSettings);
        _host = host;
        _state = state;
        _sync = sync;
        _locator = locator;
        _hub = hub;
        _config = config;

        Size = Px(DefaultWindowW, DefaultWindowH);
        SizeCondition = ImGuiCond.FirstUseEver;
        IsOpen = false;

        _sync.EntryFinished += OnEntryFinished;
        _state.RoomChanged += OnRoomChanged;
        _state.ChatReceived += OnChatReceived;
        _state.Kicked += OnKicked;
    }

    /// <summary>Reads the runtime install the plugin owns; the window only renders it.</summary>
    public Func<EchoInstallState>? InstallStateProvider { get; set; }

    /// <summary>Raised when the user asks for the runtime to be downloaded.</summary>
    public Action? InstallRequested { get; set; }

    /// <summary>Raised when the user cancels a running runtime download.</summary>
    public Action? InstallCancelRequested { get; set; }

    /// <summary>Opens the window on a single video, outside any room.</summary>
    public void OpenSolo(string videoRef)
    {
        IsOpen = true;
        Restore();
        _kickedRoomName = null;
        _sync.Enabled = false;
        _sync.Reset();
        if (!EchoMediaRefs.TryParse(videoRef, out var soloSource, out var videoId))
        {
            _soloVideoId = null;
            RaiseError(Loc.T("echo.bad_link"));
            return;
        }
        _soloVideoId = videoId;
        _soloSource = soloSource;
        // A Twitch channel is a broadcast by definition, so it needs no lookup; a YouTube id does.
        _soloIsLive = EchoMediaRefs.IsAlwaysLive(soloSource, videoId);
        if (soloSource == EchoMediaSource.YouTube)
        {
            ResolveSoloLive(videoId);
        }
        if (!EnsureHostStarted())
        {
            return;
        }
        _host.Load(videoId, 0d, EchoMediaRefs.WireName(soloSource));
    }

    /// <summary>Asks whether the solo video is broadcasting, off the draw thread. The answer only decides a
    /// badge and whether resuming jumps to the front, so an unreadable page leaves it playing as an ordinary
    /// video rather than failing anything.</summary>
    private void ResolveSoloLive(string videoId)
    {
        _ = Task.Run(async () =>
        {
            var live = await EchoYouTube.IsLiveAsync(videoId).ConfigureAwait(false);
            if (live is true && string.Equals(_soloVideoId, videoId, StringComparison.Ordinal))
            {
                _soloIsLive = true;
            }
        });
    }

    /// <summary>Opens the window onto the room the state service is already in.</summary>
    public void OpenRoom()
    {
        IsOpen = true;
        Restore();
        _kickedRoomName = null;
        _soloVideoId = null;
        _soloIsLive = false;
        _sidebarOpen = true;
        _sync.Enabled = true;
        _sync.Reset();
        EnsureHostStarted();
    }

    public override void PreDraw()
    {
        if (_minimized)
        {
            var side = Px(BubbleSide);
            Flags = BubbleFlags;
            SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(side), MaximumSize = new Vector2(side) };
            Size = new Vector2(side);
            SizeCondition = ImGuiCond.Always;
            if (_bubbleEntering)
            {
                // Placed once, where the window's corner was (or where the bubble last sat); after that
                // the bubble is dragged by hand, so the position is left to ImGui.
                Position = _bubblePos;
                PositionCondition = ImGuiCond.Always;
                _bubbleEntering = false;
            }
            else
            {
                Position = null;
            }
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, side * 0.5f);
        }
        else
        {
            Flags = FullFlags;
            SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = Px(MinWindowW, MinWindowH),
                MaximumSize = new Vector2(MaxWindowSide, MaxWindowSide),
            };
            if (_restorePending)
            {
                // Back to exactly where it was, for one frame; then the window is the user's again.
                Size = _restoreSize;
                SizeCondition = ImGuiCond.Always;
                Position = _restorePos;
                PositionCondition = ImGuiCond.Always;
                _restorePending = false;
            }
            else
            {
                SizeCondition = ImGuiCond.FirstUseEver;
                Position = null;
            }
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Px(10f, 10f));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, Px(WindowRounding));
        }
        // Dalamud's global font scale would double-scale a UI already sized in Px; pinned last so no fallible
        // PreDraw code runs between the pin and PostDraw's restore.
        FontDiagnostics.Sample("EchoWindow.PreDraw/before-pin");
        _savedFontGlobalScale = FontScalePin.Pin();
        FontDiagnostics.Sample("EchoWindow.PreDraw/after-pin");
    }

    public override void PostDraw()
    {
        FontDiagnostics.Sample("EchoWindow.PostDraw/before-restore");
        FontScalePin.Restore(_savedFontGlobalScale);
        FontDiagnostics.Sample("EchoWindow.PostDraw/after-restore");
        ImGui.PopStyleVar(2);
    }

    public override void OnClose()
    {
        _host.Stop();
        _sync.Reset();
        ReleaseFrame();
        _transportAlpha = 0f;
        _transportIdle = 0f;
        _minimized = false;
        _bubbleEntering = false;
        _restorePending = false;
        _bubbleMouseDown = false;
    }

    /// <summary>Puts the picture away and leaves the room running: the window becomes a logo bubble that
    /// keeps draining events and ticking the sync. Called from inside Draw, where the window rect is live.</summary>
    private void Minimize()
    {
        _restorePos = ImGui.GetWindowPos();
        _restoreSize = ImGui.GetWindowSize();
        _bubblePos ??= _restorePos;
        _minimized = true;
        _bubbleEntering = true;
        _bubbleMouseDown = false;
    }

    /// <summary>Brings the full window back at the size and place it was minimised from.</summary>
    public void Restore()
    {
        if (!_minimized)
        {
            return;
        }
        _minimized = false;
        _restorePending = true;
        _bubbleMouseDown = false;
    }

    /// <summary>Everything that keeps the window in step with its room, split from the drawing so the
    /// bubble runs it too: the hub's callbacks, the room's events, the sync's clock, and the error timer.</summary>
    private void TickEngine()
    {
        while (_uiActions.TryDequeue(out var action))
        {
            action();
        }
        _state.DrainEvents();
        _sync.Tick(DateTimeOffset.UtcNow);
        EnsureAccountId();
        TickError();
    }

    public override void Draw()
    {
        using var bodyFont = UiFonts.Body?.Push();
        var t = ThemeService.Current;

        TickEngine();
        if (_minimized)
        {
            DrawBubble(t);
            return;
        }
        PullFrame();

        DrawWindowBorder(t);
        PushWindowStyle(t);

        var room = _state.Room;
        if (room is not null)
        {
            DrawRoomHeader(t, room);
        }

        var origin = ImGui.GetCursorScreenPos();
        var avail = ImGui.GetContentRegionAvail();
        var showSidebar = room is not null && _sidebarOpen;
        var narrow = avail.X < Px(SidebarCollapseW);
        var sidebarW = showSidebar ? MathF.Min(Px(SidebarW), avail.X) : 0f;
        var stageW = showSidebar
            ? (narrow ? 0f : avail.X - sidebarW - Px(PanelGap))
            : avail.X;

        if (room is null)
        {
            var btn = Px(26f);
            if (DrawIconButton("##echoMinimizeSolo", origin + new Vector2(stageW - btn - Px(8f), Px(8f)), btn,
                    FontAwesomeIcon.WindowMinimize, 0xFFAAAAAAu, Loc.T("echo.minimize")))
            {
                Minimize();
                PopWindowStyle();
                return;
            }
        }
        if (stageW >= Px(MinStageW))
        {
            DrawStage(t, origin, new Vector2(stageW, avail.Y));
        }
        if (showSidebar)
        {
            var sidebarX = narrow ? origin.X : origin.X + stageW + Px(PanelGap);
            DrawSidebar(t, new Vector2(sidebarX, origin.Y), new Vector2(narrow ? avail.X : sidebarW, avail.Y));
        }

        _translate.DrawConsentOverlay(ImGui.GetWindowPos(), ImGui.GetWindowSize());
        PopWindowStyle();
    }

    public void Dispose()
    {
        _sync.EntryFinished -= OnEntryFinished;
        _state.RoomChanged -= OnRoomChanged;
        _state.ChatReceived -= OnChatReceived;
        _state.Kicked -= OnKicked;
        _host.Stop();
        ReleaseFrame();
    }

    /// <summary>The minimised window: a disc in the theme's accent with the Echo logo inside, the party
    /// chat chip's shape. Tap to restore, drag to move; a live indicator when something is playing.</summary>
    private void DrawBubble(ThemeDefinition t)
    {
        var dl = ImGui.GetWindowDrawList();
        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var c = pos + (size * 0.5f);
        var r = size.X * 0.5f;
        var mouse = ImGui.GetMousePos();
        var hovered = ImGui.IsWindowHovered() && Vector2.Distance(mouse, c) <= r;

        dl.AddCircleFilled(c, r, ImGui.ColorConvertFloat4ToU32(new Vector4(0.06f, 0.06f, 0.1f, hovered ? 0.96f : 0.88f)), 40);
        EnsureBubbleLogo();
        if (_bubbleLogo?.GetWrapOrDefault() is { } logo)
        {
            var inset = r * 0.16f;
            dl.AddImageRounded(logo.Handle, c - new Vector2(r - inset), c + new Vector2(r - inset),
                Vector2.Zero, Vector2.One, 0xFFFFFFFFu, r - inset, ImDrawFlags.RoundCornersAll);
        }
        else
        {
            IconDraw.AddCentered(dl, FontAwesomeIcon.Play, r * 0.8f, c, t.AccentU32);
        }
        dl.AddCircle(c, r, t.AccentWithAlpha(hovered ? 0.95f : 0.6f), 40, Px(2f));
        if (_host.Alive && _state.Room?.Playback is { IsPlaying: true })
        {
            var dot = c + new Vector2(r * 0.68f, -r * 0.68f);
            dl.AddCircleFilled(dot, Px(5f), ImGui.ColorConvertFloat4ToU32(UiColors.Party), 12);
        }

        if (hovered)
        {
            HandOnHover();
            ImGui.SetTooltip(Loc.T("echo.restore"));
        }

        // A tap restores; anything past the threshold is a drag, the mini phone's rule.
        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            _bubbleMouseDown = true;
            _bubbleMouseDownPos = mouse;
            _bubbleWindowPosAtDown = pos;
            _bubbleDragged = false;
        }
        if (_bubbleMouseDown && ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            var delta = mouse - _bubbleMouseDownPos;
            if (!_bubbleDragged && (MathF.Abs(delta.X) > BubbleDragThreshold || MathF.Abs(delta.Y) > BubbleDragThreshold))
            {
                _bubbleDragged = true;
            }
            if (_bubbleDragged)
            {
                ImGui.SetWindowPos(_bubbleWindowPosAtDown + delta);
            }
        }
        if (_bubbleMouseDown && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            _bubbleMouseDown = false;
            _bubblePos = ImGui.GetWindowPos();
            if (!_bubbleDragged)
            {
                Restore();
            }
        }
    }

    private void EnsureBubbleLogo()
    {
        if (_bubbleLogoLoaded)
        {
            return;
        }
        _bubbleLogoLoaded = true;
        try
        {
            var dir = Path.GetDirectoryName(Plugin.PluginInterface.AssemblyLocation.FullName) ?? "";
            var path = Path.Combine(dir, "Media", "appicons", BubbleLogoFile);
            if (File.Exists(path))
            {
                _bubbleLogo = Plugin.TextureProvider.GetFromFile(path);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[Echo] Could not load the bubble logo.");
        }
    }

    private void DrawWindowBorder(ThemeDefinition t)
    {
        var dl = ImGui.GetWindowDrawList();
        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        dl.AddRect(pos, pos + size, t.AccentU32, Px(WindowRounding), ImDrawFlags.None, Px(BorderThickness));
    }

    private static void PushWindowStyle(ThemeDefinition t)
    {
        PushThemeButton(t);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(1f, 1f, 1f, 0.07f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, t.Accent with { W = 0.26f });
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, t.Accent with { W = 0.38f });
        PushScrollbarStyle();
    }

    private static void PopWindowStyle()
    {
        PopScrollbarStyle();
        ImGui.PopStyleColor(3);
        PopThemeButton();
    }

    private void DrawRoomHeader(ThemeDefinition t, EchoRoomSnapshotDto room)
    {
        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var h = Px(HeaderH);
        var centerY = origin.Y + h * 0.5f;
        var btn = Px(26f);

        var x = origin.X + width - btn;
        if (DrawIconButton("##echoPanelToggle", new Vector2(x, centerY - btn * 0.5f), btn,
                FontAwesomeIcon.Columns, _sidebarOpen ? t.AccentU32 : 0xFFAAAAAAu, Loc.T("echo.toggle_panel")))
        {
            _sidebarOpen = !_sidebarOpen;
        }

        x -= btn + Px(6f);
        if (DrawIconButton("##echoMinimize", new Vector2(x, centerY - btn * 0.5f), btn,
                FontAwesomeIcon.WindowMinimize, 0xFFAAAAAAu, Loc.T("echo.minimize")))
        {
            Minimize();
        }

        var owner = room.OwnerAccountId == _myAccountId;
        x -= btn + Px(6f);
        if (DrawIconButton("##echoLeave", new Vector2(x, centerY - btn * 0.5f), btn,
                owner ? FontAwesomeIcon.PowerOff : FontAwesomeIcon.SignOutAlt,
                owner ? 0xFF5050E0u : 0xFFAAAAAAu,
                Loc.T(owner ? "echo.end_room" : "echo.leave_room")))
        {
            if (owner)
            {
                _confirmEndRoom = true;
            }
            else
            {
                LeaveRoom();
            }
        }

        var right = x - Px(10f);
        var membersLabel = string.Format(CultureInfo.CurrentCulture, "{0}/{1}", room.Members.Length, EchoLimits.MaxMembers);
        var membersSz = ImGui.CalcTextSize(membersLabel);
        var peopleIcon = Px(11f);
        right -= membersSz.X;
        dl.AddText(new Vector2(right, centerY - membersSz.Y * 0.5f), ImGui.GetColorU32(UiColors.Subtle), membersLabel);
        right -= Px(6f) + peopleIcon;
        IconDraw.AddCentered(dl, FontAwesomeIcon.Users, peopleIcon,
            new Vector2(right + peopleIcon * 0.5f, centerY), ImGui.GetColorU32(UiColors.Muted));

        using (UiFonts.H3?.Push())
        {
            var name = TruncateToWidth(room.Name, MathF.Max(Px(40f), right - origin.X - Px(12f)));
            var nameSz = ImGui.CalcTextSize(name);
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
                new Vector2(origin.X, centerY - nameSz.Y * 0.5f), t.AccentLightU32, name);
        }

        dl.AddLine(new Vector2(origin.X, origin.Y + h), new Vector2(origin.X + width, origin.Y + h),
            t.AccentWithAlpha(0.22f), 1f);

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + h + Px(6f)));
        DrawEndRoomConfirm();
        if (_actionError is { } err)
        {
            ImGui.TextColored(UiColors.Danger, TruncateToWidth(err, width));
        }
    }

    private void DrawEndRoomConfirm()
    {
        if (_confirmEndRoom)
        {
            _confirmEndRoom = false;
            ImGui.OpenPopup(EndRoomPopupId);
        }
        if (!ImGui.BeginPopup(EndRoomPopupId))
        {
            return;
        }
        ImGui.TextColored(UiColors.Body, Loc.T("echo.end_room_confirm"));
        ImGui.Spacing();
        if (Button($"{Loc.T("echo.end_room")}##echoEndYes", new Vector2(Px(140f), Px(ButtonH))))
        {
            ImGui.CloseCurrentPopup();
            EndRoom();
        }
        ImGui.SameLine();
        if (Button($"{Loc.T("common.cancel")}##echoEndNo", new Vector2(Px(100f), Px(ButtonH))))
        {
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndPopup();
    }

    /// <summary>Resolves what the stage should say instead of a picture, in priority order: the room is gone,
    /// the runtime is missing or arriving, the host died, or the player refused this video.</summary>
    private StageNotice? ResolveStageNotice()
    {
        if (_kickedRoomName is { } kickedFrom)
        {
            return new StageNotice(FontAwesomeIcon.UserSlash, UiColors.Amber,
                Loc.T("echo.kicked_title"),
                string.Format(CultureInfo.CurrentCulture, Loc.T("echo.kicked_body"), kickedFrom),
                Loc.T("echo.close"), CloseAfterRoom);
        }
        if (_state.EndReason is { } reason)
        {
            return new StageNotice(FontAwesomeIcon.DoorClosed, UiColors.Subtle,
                Loc.T("echo.room_ended_title"), EndReasonText(reason),
                Loc.T("echo.close"), CloseAfterRoom);
        }

        var install = InstallStateProvider?.Invoke() ?? EchoInstallState.NotInstalled;
        if (install.Busy)
        {
            return new StageNotice(FontAwesomeIcon.CloudDownloadAlt, ThemeService.Current.AccentLight,
                Loc.T("echo.installing_title"), InstallPhaseText(install),
                Loc.T("common.cancel"), () => InstallCancelRequested?.Invoke(),
                Progress: install.Progress);
        }
        if (install.Phase == EchoInstallPhase.Failed && _locator.HostExePath is null)
        {
            return new StageNotice(FontAwesomeIcon.ExclamationTriangle, UiColors.Danger,
                Loc.T("echo.install_failed_title"),
                install.FailureReason ?? Loc.T("echo.runtime_body"),
                Loc.T("echo.retry"), () => InstallRequested?.Invoke());
        }
        if (_locator.HostExePath is null)
        {
            return new StageNotice(FontAwesomeIcon.Download, ThemeService.Current.AccentLight,
                Loc.T("echo.runtime_title"), Loc.T("echo.runtime_body"),
                Loc.T("echo.runtime_install"), () => InstallRequested?.Invoke());
        }

        if (_host.FailureReason is not null)
        {
            return new StageNotice(FontAwesomeIcon.PlugCircleXmark, UiColors.Danger,
                Loc.T("echo.host_failed_title"), Loc.T("echo.host_failed_body"),
                Loc.T("echo.retry"), RestartHost);
        }

        // A live stream is H.264 with AAC audio, which a playback host built without proprietary codecs
        // cannot decode. Say that plainly rather than letting it surface as a nameless player error, and
        // offer the update when one is waiting.
        if (LiveNeedsANewerPlayer())
        {
            return UpdateAvailable?.Invoke() == true
                ? new StageNotice(FontAwesomeIcon.CloudDownloadAlt, ThemeService.Current.AccentLight,
                    Loc.T("echo.live_needs_player_title"), Loc.T("echo.live_needs_player_update"),
                    Loc.T("echo.runtime_install"), () => InstallRequested?.Invoke())
                : new StageNotice(FontAwesomeIcon.Tv, UiColors.Amber,
                    Loc.T("echo.live_needs_player_title"), Loc.T("echo.live_needs_player_body"),
                    SkipLabel(), SkipCurrent);
        }

        if (_host.LastState?.Error is not { } code)
        {
            return null;
        }
        return code switch
        {
            EchoHostErrors.Crashed => new StageNotice(FontAwesomeIcon.PlugCircleXmark, UiColors.Danger,
                Loc.T("echo.host_failed_title"), Loc.T("echo.host_failed_body"),
                Loc.T("echo.retry"), RestartHost),
            EchoHostErrors.Protocol => new StageNotice(FontAwesomeIcon.CodeBranch, UiColors.Amber,
                Loc.T("echo.protocol_title"), Loc.T("echo.protocol_body"),
                Loc.T("echo.reinstall"), () => InstallRequested?.Invoke()),
            PlayerErrorEmbedBlocked or PlayerErrorEmbedDisallowed => new StageNotice(
                FontAwesomeIcon.Ban, UiColors.Amber,
                Loc.T("echo.embed_blocked_title"), Loc.T("echo.embed_blocked_body"),
                SkipLabel(), SkipCurrent),
            PlayerErrorRemoved => new StageNotice(FontAwesomeIcon.EyeSlash, UiColors.Amber,
                Loc.T("echo.video_gone_title"), Loc.T("echo.video_gone_body"),
                SkipLabel(), SkipCurrent),
            PlayerErrorBadId => new StageNotice(FontAwesomeIcon.Unlink, UiColors.Amber,
                Loc.T("echo.bad_id_title"), Loc.T("echo.bad_id_body"),
                SkipLabel(), SkipCurrent),
            // An unnamed error is this machine's problem, not the video's: the three cases above are
            // properties of the video and fail the same way for everyone, but a transient decode or network
            // fault is local. Retry our own player instead of yanking the room off a video it is watching.
            _ => new StageNotice(FontAwesomeIcon.ExclamationTriangle, UiColors.Amber,
                Loc.T("echo.player_error_title"), Loc.T("echo.player_error_body"),
                Loc.T("echo.retry"), RestartHost),
        };
    }

    /// <summary>True when what is on the stage is a broadcast and this player has no decoder for it. In a
    /// room the sync engine has already refused to fail the entry on everyone's behalf; solo has no entry,
    /// so the same question is asked of the video the window was opened on.</summary>
    /// <summary>True when a broadcast on the stage actually FAILED in this player; the sync engine has
    /// already refused to fail the entry for the whole room. Never decided from capability probing:
    /// MediaSource.isTypeSupported answered false inside the game-launched host while that same host was
    /// visibly decoding the stream, so the only trustworthy signal is a real playback error.</summary>
    private bool LiveNeedsANewerPlayer()
    {
        if (_state.CurrentRoomId is null)
        {
            return _soloIsLive && _host.LastState is { Ready: true, Error: not null };
        }
        return _sync.UndecodableEntryId != Guid.Empty;
    }

    /// <summary>Whether a newer playback host is published and waiting to be installed.</summary>
    public Func<bool>? UpdateAvailable { get; set; }

    private string SkipLabel() => Loc.T(_state.CurrentRoomId is null ? "echo.close" : "echo.skip");

    private static string EndReasonText(EchoEndReason reason) => reason switch
    {
        EchoEndReason.OwnerEnded => Loc.T("echo.room_ended_owner"),
        EchoEndReason.OwnerLeft => Loc.T("echo.room_ended_owner_left"),
        EchoEndReason.Empty => Loc.T("echo.room_ended_empty"),
        _ => Loc.T("echo.room_ended_moderation"),
    };

    private static string InstallPhaseText(EchoInstallState install) => install.Phase switch
    {
        EchoInstallPhase.Verifying => Loc.T("echo.install_verifying"),
        EchoInstallPhase.Extracting => Loc.T("echo.install_extracting"),
        _ => Loc.T("echo.install_downloading"),
    };

    private void DrawStageNotice(ThemeDefinition t, Vector2 stageTL, Vector2 stageSize, in StageNotice notice)
    {
        var dl = ImGui.GetWindowDrawList();
        var cardW = MathF.Min(Px(CardW), stageSize.X - Px(32f));
        var innerW = cardW - Px(CardPad) * 2f;
        var iconR = Px(24f);
        var bodyH = ImGui.CalcTextSize(notice.Body, wrapWidth: MathF.Max(1f, innerW)).Y;

        float titleH;
        using (UiFonts.H3?.Push())
        {
            titleH = ImGui.CalcTextSize(notice.Title).Y;
        }

        var barH = notice.Progress is null ? 0f : Px(8f) + Px(14f);
        var hasButtons = notice.PrimaryLabel is not null || notice.SecondaryLabel is not null;
        var buttonsH = hasButtons ? Px(ButtonH) + Px(16f) : 0f;
        var cardH = Px(CardPad) + iconR * 2f + Px(14f) + titleH + Px(8f) + bodyH + barH + buttonsH + Px(CardPad);

        var cardTL = stageTL + (stageSize - new Vector2(cardW, cardH)) * 0.5f;
        var cardBR = cardTL + new Vector2(cardW, cardH);
        dl.AddRectFilled(cardTL, cardBR, CardFill, Px(12f));
        dl.AddRect(cardTL, cardBR, t.AccentWithAlpha(0.28f), Px(12f), ImDrawFlags.None, 1f);

        var tint = ImGui.GetColorU32(notice.Tint);
        var iconCenter = new Vector2(cardTL.X + cardW * 0.5f, cardTL.Y + Px(CardPad) + iconR);
        dl.AddCircleFilled(iconCenter, iconR, (tint & 0x00FFFFFFu) | 0x22000000u, 32);
        IconDraw.AddCentered(dl, notice.Icon, iconR, iconCenter, tint);

        var y = iconCenter.Y + iconR + Px(14f);
        using (UiFonts.H3?.Push())
        {
            var titleSz = ImGui.CalcTextSize(notice.Title);
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
                new Vector2(cardTL.X + (cardW - titleSz.X) * 0.5f, y), 0xFFFFFFFFu, notice.Title);
        }
        y += titleH + Px(8f);

        ImGui.SetCursorScreenPos(new Vector2(cardTL.X + Px(CardPad), y));
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + innerW);
        ImGui.TextColored(UiColors.Body, notice.Body);
        ImGui.PopTextWrapPos();
        y += bodyH;

        if (notice.Progress is { } progress)
        {
            var barTL = new Vector2(cardTL.X + Px(CardPad), y + Px(10f));
            var barBR = new Vector2(barTL.X + innerW, barTL.Y + Px(6f));
            dl.AddRectFilled(barTL, barBR, 0x33FFFFFFu, Px(3f));
            dl.AddRectFilled(barTL, new Vector2(barTL.X + innerW * progress, barBR.Y), t.AccentU32, Px(3f));
            y += barH;
        }

        if (!hasButtons)
        {
            return;
        }

        var buttonW = Px(130f);
        var gap = Px(10f);
        var totalW = notice.SecondaryLabel is null ? buttonW : buttonW * 2f + gap;
        var buttonY = cardBR.Y - Px(CardPad) - Px(ButtonH);
        var buttonX = cardTL.X + (cardW - totalW) * 0.5f;

        if (notice.SecondaryLabel is { } secondary)
        {
            ImGui.SetCursorScreenPos(new Vector2(buttonX, buttonY));
            if (Button($"{secondary}##echoNoticeSecondary", new Vector2(buttonW, Px(ButtonH))))
            {
                notice.Secondary?.Invoke();
            }
            buttonX += buttonW + gap;
        }
        if (notice.PrimaryLabel is { } primary)
        {
            ImGui.SetCursorScreenPos(new Vector2(buttonX, buttonY));
            ImGui.PushStyleColor(ImGuiCol.Button, t.Accent with { W = 0.85f });
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, t.AccentLight);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, t.AccentDark);
            if (Button($"{primary}##echoNoticePrimary", new Vector2(buttonW, Px(ButtonH))))
            {
                notice.Primary?.Invoke();
            }
            ImGui.PopStyleColor(3);
        }
    }

    private bool EnsureHostStarted()
    {
        if (_host.Alive || _host.MapName is not null)
        {
            return true;
        }
        if (_locator.HostExePath is not { } exe)
        {
            return false;
        }
        var render = RenderSize();
        _host.Start(exe, WatchPageUrl(), (int)MathF.Round(render.X), (int)MathF.Round(render.Y),
            _config.Echo.DisableHardwareAcceleration);
        _host.SetVolume(_config.Echo.Volume);
        return true;
    }

    private static string WatchPageUrl() =>
        new Uri(new Uri(AetherConstants.ServerBaseUrl), WatchPagePath).ToString();

    /// <summary>Moves a running player onto a freshly installed build; a closed window needs nothing, the
    /// next open resolves the new exe by itself. The installer completes on a worker thread, so the
    /// restart is queued onto the draw thread.</summary>
    public void RestartHostAfterUpdate()
    {
        if (!IsOpen && !_host.Alive)
        {
            return;
        }
        _uiActions.Enqueue(RestartHost);
    }

    private void RestartHost()
    {
        _host.Stop();
        _sync.Reset();
        ReleaseFrame();
        EnsureHostStarted();
        if (_state.CurrentRoomId is null && _soloVideoId is { } videoId)
        {
            _host.Load(videoId, 0d, EchoMediaRefs.WireName(_soloSource));
        }
    }

    private void SkipCurrent()
    {
        if (_state.CurrentRoomId is not { } roomId)
        {
            IsOpen = false;
            return;
        }
        if (_state.CurrentEntry is { } entry)
        {
            RunHub(() => _hub.AdvanceEchoPlaylistAsync(roomId, entry.Id, true));
        }
    }

    private void LeaveRoom()
    {
        if (_state.CurrentRoomId is not { } roomId)
        {
            return;
        }
        RunHub(() => _hub.LeaveEchoRoomAsync(roomId));
        _state.Clear();
        IsOpen = false;
    }

    /// <summary>The phone was powered off with a room live: leave it and put the window away, the way the
    /// party dock goes with the phone. Quiet when there is no room.</summary>
    public void LeaveWithPhone()
    {
        if (_state.CurrentRoomId is null)
        {
            IsOpen = false;
            return;
        }
        LeaveRoom();
    }

    private void EndRoom()
    {
        if (_state.CurrentRoomId is not { } roomId)
        {
            return;
        }
        RunHub(() => _hub.EndEchoRoomAsync(roomId));
    }

    private void CloseAfterRoom()
    {
        _state.Clear();
        _kickedRoomName = null;
        IsOpen = false;
    }

    private void OnRoomChanged()
    {
        var room = _state.Room;
        if (room is null)
        {
            _knownRoomId = Guid.Empty;
            _knownPlayback = null;
            _sync.Reset();
            return;
        }
        if (room.Id != _knownRoomId)
        {
            _knownRoomId = room.Id;
            _knownPlayback = null;
            _sync.Reset();
            ResetChat();
            ResetPlaylistAnimation();
        }
        if (_knownPlayback != room.Playback)
        {
            _knownPlayback = room.Playback;
            _sync.OnPlaybackChanged(room.Playback);
        }
    }

    private void OnEntryFinished(Guid entryId, bool failed)
    {
        if (_state.CurrentRoomId is not { } roomId || !ShouldAutoAdvance())
        {
            return;
        }
        RunHub(() => _hub.AdvanceEchoPlaylistAsync(roomId, entryId, failed));
    }

    private void OnKicked(EchoKickedDto push)
    {
        _kickedRoomName = push.RoomName;
        _sync.Reset();
        _host.Stop();
    }

    private void EnsureAccountId()
    {
        if (_myAccountId != Guid.Empty || _accountLookupBusy || _state.Room is null)
        {
            return;
        }
        _accountLookupBusy = true;
        _ = Task.Run(async () =>
        {
            try
            {
                var info = await _hub.GetAccountInfoAsync().ConfigureAwait(false);
                _uiActions.Enqueue(() => _myAccountId = info.AccountId);
            }
            catch (Exception ex)
            {
                Plugin.Log.Debug($"[Echo] Could not resolve the account id: {ex.Message}");
            }
            finally
            {
                _accountLookupBusy = false;
            }
        });
    }

    private void RunHub(Func<Task> call)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await call().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var text = FriendlyHubError(ex);
                _uiActions.Enqueue(() => RaiseError(text));
                Plugin.Log.Warning(ex, "[Echo] A hub call failed.");
            }
        });
    }

    private string FriendlyHubError(Exception ex)
    {
        if (!_hub.IsConnected || ex is InvalidOperationException)
        {
            return Loc.T("chat.connectivity_error");
        }
        return HubErrorText.Localize(ex);
    }

    private void RaiseError(string text)
    {
        _actionError = text;
        _errorRemaining = ErrorVisibleSeconds;
    }

    private void TickError()
    {
        if (_actionError is null)
        {
            return;
        }
        _errorRemaining -= ImGui.GetIO().DeltaTime;
        if (_errorRemaining <= 0f)
        {
            _actionError = null;
        }
    }

    /// <summary>True when the local user may drive playback and the queue; always true outside a room.</summary>
    private bool CanControl() => _state.CurrentRoomId is null || _state.CanControl(_myAccountId);

    private bool IsRoomOwner() => _state.Room is { } room && room.OwnerAccountId == _myAccountId;

    private void PullFrame()
    {
        var name = _host.MapName;
        if (!string.Equals(name, _mappedName, StringComparison.Ordinal))
        {
            ReleaseFrame();
            _mappedName = name;
        }
        if (name is null || !EnsureMapped(name) || _view is not { } source || _buffer is not { } buffer)
        {
            return;
        }

        var seq = source.ReadInt32(0);
        if (seq == _lastSequence || seq == 0)
        {
            return;
        }
        var width = source.ReadInt32(4);
        var height = source.ReadInt32(8);
        if (width is <= 0 or > MaxFrameWidth || height is <= 0 or > MaxFrameHeight)
        {
            return;
        }

        var bytes = width * height * 4;
        if (FrameHeaderBytes + bytes > source.Capacity)
        {
            return;
        }
        source.ReadArray(FrameHeaderBytes, buffer, 0, bytes);
        // The counter is written either side of the pixels: a mismatch means the copy raced a paint.
        if (source.ReadInt32(12) != seq)
        {
            return;
        }

        _lastSequence = seq;
        _frameWidth = width;
        _frameHeight = height;
        Upload(width, height, bytes);
    }

    /// <summary>Attaches to the host's buffer once it exists; the host creates it, so this keeps failing
    /// harmlessly until the browser has booted.</summary>
    private bool EnsureMapped(string name)
    {
        if (_view != null)
        {
            return true;
        }
        try
        {
            _map = MemoryMappedFile.OpenExisting(name, MemoryMappedFileRights.Read);
            // Size 0 maps whatever the host actually allocated. Asking for this build's capacity instead
            // throws against an older installed host, whose map is a quarter the size, and Echo would then
            // show nothing at all until the runtime happened to update.
            _view = _map.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            _buffer ??= new byte[FrameCapacity - FrameHeaderBytes];
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[Echo] Could not open the frame buffer.");
            return false;
        }
    }

    /// <summary>The host paints BGRA, so Dalamud is asked for the matching format and no channel swizzle is
    /// needed on the CPU.</summary>
    private void Upload(int width, int height, int bytes)
    {
        if (_buffer is not { } buffer)
        {
            return;
        }
        try
        {
            var fresh = Plugin.TextureProvider.CreateFromRaw(
                RawImageSpecification.Bgra32(width, height), buffer.AsSpan(0, bytes), "echo-frame");
            _texture?.Dispose();
            _texture = fresh;
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[Echo] Frame upload failed.");
        }
    }

    private void ReleaseFrame()
    {
        _view?.Dispose();
        _view = null;
        _map?.Dispose();
        _map = null;
        _texture?.Dispose();
        _texture = null;
        _mappedName = null;
        _lastSequence = 0;
        _frameWidth = 0;
        _frameHeight = 0;
    }


    private static string FormatTime(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0d)
        {
            seconds = 0d;
        }
        var span = TimeSpan.FromSeconds(seconds);
        return span.TotalHours >= 1d
            ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}",
                (int)span.TotalHours, span.Minutes, span.Seconds)
            : string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", (int)span.TotalMinutes, span.Seconds);
    }

    /// <summary>Scales a packed colour's alpha, so a whole hand-drawn control can fade as one.</summary>
    private static uint Fade(uint color, float alpha)
    {
        var scaled = ((color >> 24) & 0xFFu) * Math.Clamp(alpha, 0f, 1f);
        return (color & 0x00FFFFFFu) | ((uint)MathF.Round(scaled) << 24);
    }

    private static Vector4 IdentityColor(Guid accountId)
    {
        var hash = accountId.GetHashCode();
        var index = ((hash % IdentityPalette.Length) + IdentityPalette.Length) % IdentityPalette.Length;
        return IdentityPalette[index];
    }

    /// <summary>The OS avatar when the seat carries one, an initial on a colour disc when it does not, and
    /// the ring over either.</summary>
    private static void DrawIdentityCircle(ImDrawListPtr dl, Guid accountId, string displayName,
        Vector2 center, float radius, string? frameRef = null, byte[]? avatar = null)
    {
        var tex = AetherLove.Services.InlineAvatarCache.Resolve("EchoAvatarCache", accountId, avatar)?.GetWrapOrDefault();
        if (tex is not null)
        {
            dl.AddImageRounded(tex.Handle, center - new Vector2(radius), center + new Vector2(radius),
                Vector2.Zero, Vector2.One, 0xFFFFFFFFu, radius, ImDrawFlags.RoundCornersAll);
        }
        else
        {
            var color = IdentityColor(accountId);
            dl.AddCircleFilled(center, radius, ImGui.GetColorU32(color), 32);
            var initial = string.IsNullOrWhiteSpace(displayName) ? "?" : displayName.Trim()[..1].ToUpperInvariant();
            var glyphPx = radius * 1.1f;
            var size = ImGui.CalcTextSize(initial) * (glyphPx / ImGui.GetFontSize());
            dl.AddText(ImGui.GetFont(), glyphPx, center - size * 0.5f, 0xFF141414u, initial);
        }
        AetherLove.UI.AvatarRings.Draw(dl, center, radius, frameRef);
    }

    private static bool DrawIconButton(string id, Vector2 tl, float size, FontAwesomeIcon icon, uint color,
        string? tooltip = null, bool enabled = true, float alpha = 1f)
    {
        var dl = ImGui.GetWindowDrawList();
        ImGui.SetCursorScreenPos(tl);
        var clicked = ImGui.InvisibleButton(id, new Vector2(size));
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            if (enabled)
            {
                HandOnHover();
                dl.AddCircleFilled(tl + new Vector2(size * 0.5f), size * 0.5f, Fade(0x22FFFFFFu, alpha), 24);
            }
            if (tooltip is { Length: > 0 })
            {
                ImGui.SetTooltip(tooltip);
            }
        }
        IconDraw.AddCentered(dl, icon, size * 0.52f, tl + new Vector2(size * 0.5f),
            Fade(enabled ? color : UiColors.TextMuted, alpha));
        return enabled && clicked;
    }
}
