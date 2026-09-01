using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove;
using AetherLove.Services;
using AetherLove.Services.Echo;
using AetherLove.Services.Hub;
using AetherLove.Services.Localization;
using AetherLove.Shared.Profile;
using AetherLove.UI;
using AetherOS.Apps.EchoVidya.Screens;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using static AetherLove.UI.SharedUiHelpers;
using static AetherLove.UI.UiScale;

namespace AetherOS.Apps.EchoVidya;

/// <summary>Echo: watch a video on your own, or together in a room with a shared playlist and chat. The phone
/// surface owns setup, the room and its people; the video itself plays in the plugin's popout window.</summary>
public sealed class EchoVidyaApp : IAetherApp
{
    private static readonly Vector4 TileTopColor = new(0.78f, 0.16f, 0.28f, 1f);
    private static readonly Vector4 TileBottomColor = new(0.24f, 0.03f, 0.12f, 1f);

    private enum View
    {
        Setup,
        Home,
        Room,
    }

    internal const string AppId = "echo";

    private const float PadX = 16f;

    private readonly Func<string> _name;
    private readonly Func<bool>? _available;
    private readonly AetherHubContext _hub;
    private readonly EchoStateService _state;
    private readonly IEchoHost _host;
    private readonly SetupScreen _setup;
    private readonly HomeScreen _home;
    private readonly RoomScreen _room;

    private volatile AetherAccountInfoDto? _account;
    private volatile bool _resolvingAccount;
    private volatile bool _recoveringRoom;
    private View _view = View.Home;
    private string? _pendingJoinCode;

    /// <summary><paramref name="available"/> is the server kill switch, read from the connection info the same
    /// way every other gated app reads its flag; absent, the app is always available.</summary>
    public EchoVidyaApp(
        Func<string> name,
        IAppCapabilities capabilities,
        AetherHubContext hub,
        EchoStateService state,
        EchoHostInstaller installer,
        EchoHostLocator locator,
        IEchoHost host,
        Func<bool>? available = null)
    {
        _name = name;
        _available = available;
        _hub = hub;
        _state = state;
        _host = host;
        _setup = new SetupScreen(capabilities.Storage(AppId), hub, installer, locator, host, FinishSetup);
        _home = new HomeScreen(hub, state, host, OpenRoomScreen, OpenDownloadStep);
        _room = new RoomScreen(capabilities, hub, state, host, () => _account?.AccountId, BackToHome);
    }

    public string Id => AppId;
    public string Name => _name();
    public FontAwesomeIcon Icon => FontAwesomeIcon.Film;
    public Vector4 TileTop => TileTopColor;
    public Vector4 TileBottom => TileBottomColor;
    public int Badge => 0;
    public bool HasSurface => true;
    public bool Available => _available?.Invoke() ?? true;

    /// <summary>False because solo watching is a local runtime plus a browser, so an outage must not blank the
    /// app; the room surfaces disable themselves and say why.</summary>
    public bool RequiresConnection => false;

    /// <summary>Rooms are account-backed, so a disabled account gets the ban card rather than the surface.</summary>
    public bool UsesAccount => true;

    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Strings => Localization.AppStrings.Packs;

    public void Open()
    {
    }

    public void OnForeground()
    {
        EnsureAccount();
        ResyncRoom();
        _host.CheckForUpdate();
        _home.OnShow();
        _room.OnShow();
    }

    public void OnIntent(OsIntent intent)
    {
        if (intent.Type == OsIntents.EchoJoin && OsIntents.TryGetRoomJoin(intent, out _, out var code))
        {
            _pendingJoinCode = code;
        }
    }

    public void Draw(OsAppContext ctx)
    {
        // The only place the state service raises its events, and those events are what refresh this UI.
        _state.DrainEvents();

        if (_view != View.Setup && !UiHost.Configuration.Echo.SetupDone)
        {
            _view = View.Setup;
            _setup.OnShow();
        }
        if (_view == View.Setup)
        {
            _setup.Draw(ctx);
            return;
        }

        // A published playback host that is not the installed one blocks the whole app, the way the
        // onboarding download does: the install has already started itself, this only shows it. Nobody
        // reaches a video and discovers mid-queue that their player is outdated.
        if (_host.UpdatePending)
        {
            _setup.DrawUpdateGate(ctx);
            return;
        }

        if (_pendingJoinCode is { Length: > 0 } code)
        {
            _pendingJoinCode = null;
            _view = View.Home;
            _home.BeginJoin(code);
        }

        DrawHeader(ctx);

        PushScrollbarStyle();
        using (var body = ImRaii.Child("##echoBody", Vector2.Zero, false))
        {
            if (body)
            {
                if (_view == View.Room)
                {
                    _room.Draw(ctx);
                }
                else
                {
                    _home.Draw(ctx);
                }
            }
        }
        PopScrollbarStyle();

        if (_view == View.Room)
        {
            _room.DrawOverlays();
        }
    }

    private void DrawHeader(OsAppContext ctx)
    {
        var t = ThemeService.Current;
        ImGui.Dummy(new Vector2(0f, Px(10f)));
        ImGui.SetCursorPosX(Px(PadX));
        using (ctx.TitleFont?.Push())
        {
            ImGui.TextColored(t.AccentLight, _view == View.Room ? Loc.T("os.echo_room_title") : _name());
        }
        DrawMenu();
        ImGui.Dummy(new Vector2(0f, Px(6f)));
    }

    private void DrawMenu()
    {
        const string popupId = "##echoMenu";
        var menuTL = AppHeader.DrawMenuButton(ImGui.GetWindowSize().X, PadX, popupId);
        var open = AppHeader.BeginMenuPopup(menuTL, popupId);
        if (open)
        {
            var tour = Loc.T("os.echo_menu_tour");
            var close = Loc.T("os.echo_menu_close_window");
            var w = AppHeader.MenuWidth(tour, close);
            var rowH = AppHeader.MenuRowHeight();

            if (AppHeader.MenuRow(FontAwesomeIcon.Film, tour, w, rowH))
            {
                _view = View.Setup;
                _setup.OnShow(0);
                ImGui.CloseCurrentPopup();
            }
            if (_host.WindowOpen && AppHeader.MenuRow(FontAwesomeIcon.Times, close, w, rowH))
            {
                _host.CloseWindow();
                ImGui.CloseCurrentPopup();
            }
        }
        AppHeader.EndMenuPopup(open);
    }

    private void FinishSetup()
    {
        UiHost.Configuration.Echo.SetupDone = true;
        UiHost.Configuration.Save();
        _view = View.Home;
        _home.OnShow();
    }

    private void OpenRoomScreen()
    {
        _view = View.Room;
        _room.OnShow();
    }

    private void BackToHome()
    {
        _view = View.Home;
        _home.OnShow();
    }

    private void OpenDownloadStep()
    {
        _view = View.Setup;
        _setup.OnShow(SetupScreen.DownloadStep);
    }

    /// <summary>The room snapshot is only pushed while the phone is live, so a surface that was in the
    /// background refetches instead of drawing a stale member list. With no room in memory it instead asks
    /// the server whether one exists: a plugin reload or update drops the in-memory state while the room
    /// stays live server-side, and without this the owner could neither see nor end their own session.</summary>
    private void ResyncRoom()
    {
        if (!_hub.IsConnected || _recoveringRoom)
        {
            return;
        }
        var roomId = _state.CurrentRoomId;
        _recoveringRoom = true;
        _ = Task.Run(async () =>
        {
            try
            {
                var snapshot = roomId is { } id
                    ? await _hub.GetEchoRoomSyncAsync(id).ConfigureAwait(false)
                    : await _hub.GetMyEchoRoomAsync().ConfigureAwait(false);
                if (snapshot is not null)
                {
                    _state.ApplySnapshot(snapshot);
                }
            }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, "[EchoVidyaApp] Could not re-sync the room.");
            }
            finally
            {
                _recoveringRoom = false;
            }
        });
    }

    private void EnsureAccount()
    {
        if (_account is not null || _resolvingAccount || !_hub.IsConnected)
        {
            return;
        }
        _resolvingAccount = true;
        _ = Task.Run(async () =>
        {
            try
            {
                _account = await _hub.GetAccountInfoAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, "[EchoVidyaApp] Could not resolve the account.");
            }
            finally
            {
                _resolvingAccount = false;
            }
        });
    }
}
