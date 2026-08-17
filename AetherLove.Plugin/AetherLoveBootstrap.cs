using System;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Changelog;
using AetherLove.Config;
using AetherLove.Navigation;
using AetherLove.Services;
using AetherLove.Services.Auth;
using AetherLove.Services.Signal;
using AetherLove.UI;
using AetherLove.Windows;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Hosting;

namespace AetherLove;

/// <summary>Plugin lifecycle host registered as an IHostedService.</summary>
public sealed class AetherLoveBootstrap : IHostedService
{
    public const string CommandName = "/aetheros";

    private static readonly string[] AliasCommandNames = { "/aetherlove", "/love", "/aos" };

    private readonly IPluginLog _log;
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly ICommandManager _commandManager;
    private readonly IClientState _clientState;
    private readonly WindowSystem _windowSystem;
    private readonly MainPluginWindow _mainWindow;
    private readonly MiniWindow _miniWindow;
    private readonly ChangelogWindow _changelogWindow;
    private readonly DebugWindow _debugWindow;
    private readonly AetherlingDebugWindow _aetherlingDebugWindow;
    private readonly EchoWindow _echoWindow;
    private readonly SkinPreviewWindow _skinPreviewWindow;
    private readonly Os.AetherlingHostService _aetherlingHost;
    private readonly Widgets.ModalHost _modalHost = new();
    private readonly Widgets.SelfieCaptureOverlay _selfieOverlay;
    private readonly ScreenRouter _router;
    private readonly Configuration _config;
    private readonly SessionBootstrapper _bootstrap;
    private readonly AetherSignalService _signal;
    private readonly PulseService _pulse;
    private readonly MarketAlertService _marketAlerts;
    private readonly Os.MarketContextMenuService _marketContextMenu;
    private readonly Os.ClockAlarmService _clockAlarms;
    private readonly Os.TimerScheduleService _timerSchedule;
    private readonly Os.RetainerFleetService _retainerFleet;
    private readonly Services.TimersDtrService _timersDtr;
    private readonly TomestoneEmoteService _tomestoneEmote;
    private readonly ScreenCaptureService _capture;
    private readonly Services.Hangouts.HangoutStateService _hangoutState;
    private readonly Services.Hub.AetherHubContext _hubClient;
    private readonly Services.Sparks.SparkActivityReporter _sparkActivity;

    /// <summary>When the character logged out to the title screen while owning a live hangout. The hub stays
    /// connected there, so the server-side presence grace never fires; this client-side twin ends the hangout
    /// after the same window unless they log back in first.</summary>
    private DateTimeOffset? _hangoutLogoutAtUtc;

    private static readonly TimeSpan HangoutLogoutGrace = TimeSpan.FromMinutes(15);

    /// <summary>Guards the once-per-session changelog check so character switches don't re-run it.</summary>
    private bool _changelogShown;

    /// <summary>Set on login; a Framework tick waits for the character to finish zoning in before
    /// showing the minimized bubble, so it doesn't pop over the loading screen.</summary>
    private bool _autoOpenPending;

    /// <summary>Previous-frame combat state used to detect the entering-combat edge.</summary>
    private bool _wasInCombat;

    public AetherLoveBootstrap(
        IPluginLog log,
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IClientState clientState,
        WindowSystem windowSystem,
        MainPluginWindow mainWindow,
        MiniWindow miniWindow,
        ChangelogWindow changelogWindow,
        DebugWindow debugWindow,
        AetherlingDebugWindow aetherlingDebugWindow,
        EchoWindow echoWindow,
        SkinPreviewWindow skinPreviewWindow,
        ScreenRouter router,
        Configuration config,
        SessionBootstrapper bootstrap,
        AetherSignalService signal,
        PulseService pulse,
        MarketAlertService marketAlerts,
        Os.MarketContextMenuService marketContextMenu,
        Os.ClockAlarmService clockAlarms,
        Os.TimerScheduleService timerSchedule,
        Os.RetainerFleetService retainerFleet,
        Services.TimersDtrService timersDtr,
        TomestoneEmoteService tomestoneEmote,
        ScreenCaptureService capture,
        Widgets.SelfieCaptureOverlay selfieOverlay,
        Services.Hangouts.HangoutStateService hangoutState,
        Services.Hub.AetherHubContext hubClient,
        Services.Sparks.SparkActivityReporter sparkActivity,
        Os.OsShell osShell,
        Services.DtrBarService dtrBar,
        Services.GrooveDtrService grooveDtr,
        Services.GrooveAutoMuteService grooveAutoMute,
        Os.ScreenshotImportService screenshotImport,
        Services.Chat.ChatCacheStore chatCache,
        Services.Messenger.MessengerStore messenger,
        Os.RealtorPhaseWatchService realtorPhase,
        Services.AvatarRingService avatarRings,
        Services.Store.PremiumThemeService premiumThemes,
        Os.AetherlingHostService aetherlingHost)
    {
        AetherLove.UI.AvatarRings.Install(avatarRings.Texture);
        // ThemeService.Initialise runs in the plugin ctor, before any of this exists, so a purchased theme
        // can only be restored here; until then the phone draws the built-in fallback.
        premiumThemes.TryRestoreOnBoot();
        _log = log;
        _pluginInterface = pluginInterface;
        _commandManager = commandManager;
        _clientState = clientState;
        _windowSystem = windowSystem;
        _mainWindow = mainWindow;
        _miniWindow = miniWindow;
        _changelogWindow = changelogWindow;
        _debugWindow = debugWindow;
        _aetherlingDebugWindow = aetherlingDebugWindow;
        _echoWindow = echoWindow;
        _skinPreviewWindow = skinPreviewWindow;
        _aetherlingHost = aetherlingHost;
        _router = router;
        _config = config;
        _bootstrap = bootstrap;
        _signal = signal;
        _pulse = pulse;
        _marketAlerts = marketAlerts;
        _marketContextMenu = marketContextMenu;
        _clockAlarms = clockAlarms;
        _tomestoneEmote = tomestoneEmote;
        _capture = capture;
        _selfieOverlay = selfieOverlay;
        _hangoutState = hangoutState;
        _hubClient = hubClient;
        _sparkActivity = sparkActivity;
        _osShell = osShell;
        _dtrBar = dtrBar;
        _grooveDtr = grooveDtr;
        _grooveAutoMute = grooveAutoMute;
        _screenshotImport = screenshotImport;
        _chatCache = chatCache;
        _messenger = messenger;
        _realtorPhase = realtorPhase;
        _timerSchedule = timerSchedule;
        _retainerFleet = retainerFleet;
        _timersDtr = timersDtr;
    }

    private readonly Os.OsShell _osShell;
    private readonly Services.DtrBarService _dtrBar;
    private readonly Services.GrooveDtrService _grooveDtr;
    private readonly Services.GrooveAutoMuteService _grooveAutoMute;
    private readonly Os.ScreenshotImportService _screenshotImport;
    private readonly Services.Chat.ChatCacheStore _chatCache;
    private readonly Services.Messenger.MessengerStore _messenger;
    private readonly Os.RealtorPhaseWatchService _realtorPhase;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        UiScale.Apply(_config.PhoneSize);
        MiniScale.Apply(_config.MiniPhoneSize);
        UiFonts.Rebuild();

        _mainWindow.SetMiniWindow(_miniWindow);
        _aetherlingHost.PhoneOpener = _mainWindow.OpenToAetherlingStatus;

        _windowSystem.AddWindow(_mainWindow);
        _windowSystem.AddWindow(_miniWindow);
        _windowSystem.AddWindow(_changelogWindow);
        _windowSystem.AddWindow(_debugWindow);
        _windowSystem.AddWindow(_aetherlingDebugWindow);
        _windowSystem.AddWindow(_echoWindow);
        _windowSystem.AddWindow(_skinPreviewWindow);
        _windowSystem.AddWindow(_modalHost);
        _windowSystem.AddWindow(_selfieOverlay);

        // Escape closes the focused Dalamud window by default; block it for every AetherLove window.
        foreach (var window in _windowSystem.Windows)
        {
            window.RespectCloseHotkey = false;
        }

        _commandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            // The staff subcommands are deliberately absent: this string is public, in the installer and in
            // /xlhelp, and the server refuses them anyway.
            HelpMessage = "Open AetherOS. Subcommands: \"resetscreen\" (recenter the window), \"debug\" (diagnostics), \"clearcache\" (wipe local caches and restart the phone)."
        });
        foreach (var alias in AliasCommandNames)
        {
            _commandManager.AddHandler(alias, new CommandInfo(OnCommand)
            {
                HelpMessage = $"Alias for {CommandName}."
            });
        }

        _pluginInterface.UiBuilder.Draw += DrawWindowSystemGuarded;
        _pluginInterface.UiBuilder.OpenMainUi += OpenIfClosed;
        _pluginInterface.UiBuilder.OpenConfigUi += OpenConfig;
        _pluginInterface.UiBuilder.DisableGposeUiHide = _config.ShowDuringGpose;
        _pluginInterface.UiBuilder.DisableCutsceneUiHide = !_config.HideDuringCutscenes;
        _clientState.Login += OnLogin;
        _clientState.Logout += OnLogout;
        Plugin.Framework.Update += OnCombatUpdate;
        _pulse.Start();
        _marketAlerts.Start();
        _clockAlarms.Start();
        _timerSchedule.Start();
        _retainerFleet.Start();
        _tomestoneEmote.Start(() => _mainWindow.IsOpen && _mainWindow.IsPhoneFocused);
        _capture.Initialize();
        _dtrBar.Initialize();
        _grooveDtr.Initialize();
        _timersDtr.Initialize();
        _grooveAutoMute.Initialize();
        Widgets.SelfieCaptureOverlay.PurgeTempFiles();

        var changelogVersion = ChangelogRegistry.CurrentVersion;
        var changelogKey = changelogVersion is null
            ? null
            : $"{changelogVersion.Major}.{changelogVersion.Minor}.{changelogVersion.Build}";

        // Only a truly fresh install (no device id / refresh token) is forced into onboarding.
        if (!_config.HasCompletedFirstLaunch)
        {
            var freshInstall = string.IsNullOrEmpty(_config.DeviceId)
                            && string.IsNullOrEmpty(_config.Auth.RefreshToken);

            _config.HasCompletedFirstLaunch = true;
            // Mark the current changelog seen so new users don't get a "What's New" popup mid-onboarding.
            if (freshInstall && changelogKey is not null)
            {
                _config.ShownChangelogVersions.Add(changelogKey);
            }
            _config.Save();

            if (freshInstall)
            {
                _mainWindow.IsOpen = true;
                _router.Navigate(Screen.Splash);
            }
        }

        // If (re)loaded while already in-game, the Login event won't fire; show the changelog now.
        if (_clientState.IsLoggedIn)
        {
            MaybeShowChangelog();
            // A mid-session (re)load never fires Login, so restore the last window state.
            if (!_mainWindow.IsOpen && !_miniWindow.IsOpen)
            {
                RestoreLastWindowState();
            }
        }

        _log.Information($"[AetherLove] Loaded. Use {CommandName} to open.");
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _config.LastWindowState = _mainWindow.IsOpen ? WindowOpenState.Full
            : _miniWindow.IsOpen ? WindowOpenState.Minimized
            : WindowOpenState.Closed;
        _config.Save();

        _pluginInterface.UiBuilder.Draw -= DrawWindowSystemGuarded;
        _pluginInterface.UiBuilder.OpenMainUi -= OpenIfClosed;
        Plugin.Framework.Update -= OnCombatUpdate;
        _pluginInterface.UiBuilder.OpenConfigUi -= OpenConfig;
        _clientState.Login -= OnLogin;
        _clientState.Logout -= OnLogout;
        Plugin.Framework.Update -= OnFrameworkUpdate;

        _commandManager.RemoveHandler(CommandName);
        foreach (var alias in AliasCommandNames)
        {
            _commandManager.RemoveHandler(alias);
        }

        _pulse.Stop();
        _clockAlarms.Stop();
        _timerSchedule.Stop();
        _retainerFleet.Stop();
        _tomestoneEmote.Stop();
        _capture.Dispose();
        _realtorPhase.Dispose();
        _dtrBar.Shutdown();
        _grooveDtr.Shutdown();
        _timersDtr.Shutdown();
        _grooveAutoMute.Shutdown();
        Widgets.SelfieCaptureOverlay.PurgeTempFiles();

        _windowSystem.RemoveAllWindows();
        _echoWindow.Dispose();
        _skinPreviewWindow.Dispose();
        _mainWindow.Dispose();
        UiFonts.Dispose();

        // Host.Dispose() doesn't chain to singleton IAsyncDisposable on the sync path; drive it here.
        await _signal.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>Frames a single /aetherlove fontdiag captures. Enough to see the phone settle rather than
    /// one atypical frame, few enough to paste into a report.</summary>
    private const int FontDiagCaptureFrames = 30;

    private bool _fontScaleLeakLogged;
    private bool _appsResolved;

    /// <summary>Restores ImGui's global font scale even if a window callback throws, so the phone's pin
    /// can never bleed into other plugins' rendering.</summary>
    private void DrawWindowSystemGuarded()
    {
        var io = ImGui.GetIO();
        var savedScale = io.FontGlobalScale;
        try
        {
            UI.FontDiagnostics.Sample("Handler/entry");
            _windowSystem.Draw();
            UI.FontDiagnostics.Sample("Handler/after-window-system");

            // Apps are resolved lazily, and the Aetherling app hands over its floating creature from its
            // constructor, so without this it would only appear once the phone had been opened at least once.
            if (!_appsResolved)
            {
                _appsResolved = true;
                _ = _osShell.Apps;
            }

            // Outside the window system on purpose: the floating creature opens its own window, sized and
            // placed by the app, and a wrapper window would only be an invisible rectangle around it. It
            // goes away with the phone: a switched-off phone leaves nothing of AetherOS on screen, and it
            // follows the phone's own visibility rule of never being on screen while logged out, which
            // covers the title screen and the gap in the middle of a character switch.
            var phoneOn = (_mainWindow.IsOpen || _miniWindow.IsOpen) && _clientState.IsLoggedIn;
            if (phoneOn && _aetherlingHost.Overlay is { Visible: true } overlay)
            {
                overlay.Draw();
            }
            if (io.FontGlobalScale != savedScale && !_fontScaleLeakLogged)
            {
                _fontScaleLeakLogged = true;
                _log.Warning($"[AetherLove] FontGlobalScale left at {io.FontGlobalScale} after draw " +
                             $"(expected {savedScale}) on screen {_router.Current}; restored.");
            }
        }
        catch (Exception ex)
        {
            if (!_fontScaleLeakLogged)
            {
                _fontScaleLeakLogged = true;
                _log.Warning(ex, $"[AetherLove] Draw threw on screen {_router.Current}; font scale restored.");
            }
            throw;
        }
        finally
        {
            UI.FontScalePin.Restore(savedScale);
            UI.FontDiagnostics.Sample("Handler/after-guard-restore");
            // Last thing in the frame that is ours, so this measures precisely what the next plugin gets.
            UI.FontDiagnostics.ProbeDownstream();
            UI.FontDiagnostics.EndFrame();
        }
    }

    /// <summary>Opens the full window only if neither window is already showing.</summary>
    private void OpenIfClosed()
    {
        if (_mainWindow.IsOpen || _miniWindow.IsOpen)
        {
            return;
        }
        _mainWindow.IsOpen = true;
        _router.Navigate(Screen.Splash);
    }

    /// <summary>Reopens the plugin as it was at the last unload; a deliberately-closed plugin stays closed.</summary>
    private void RestoreLastWindowState()
    {
        switch (_config.LastWindowState)
        {
            case WindowOpenState.Full:
                _mainWindow.IsOpen = true;
                _router.Navigate(Screen.Splash);
                break;
            case WindowOpenState.Minimized:
                _miniWindow.IsOpen = true;
                _ = _signal.EnsureConnectedAsync();
                break;
        }
    }

    private void OpenConfig()
    {
        _miniWindow.IsOpen = false;
        _mainWindow.IsOpen = true;
        if (_bootstrap.LastResult == SessionBootstrapResult.SignedInActive)
        {
            _mainWindow.OpenToSettings();
        }
        else
        {
            _router.Navigate(Screen.Splash);
        }
    }

    private void OnLogout(int type, int code)
    {
        if (_hangoutState.MyHangout is not null)
        {
            _hangoutLogoutAtUtc = DateTimeOffset.UtcNow;
        }
    }

    private void OnLogin()
    {
        // Logging back in within the grace keeps the hangout alive.
        _hangoutLogoutAtUtc = null;

        // A fresh play session starts on a full charge.
        Os.BatteryService.Reset();

        // Before the auto-open early-outs: the changelog shows regardless of that preference.
        MaybeShowChangelog();

        if (!_config.AutoOpenMinimizedOnLogin)
        {
            return;
        }
        if (string.IsNullOrEmpty(_config.Auth.RefreshToken))
        {
            return;
        }
        if (_mainWindow.IsOpen || _miniWindow.IsOpen)
        {
            return;
        }
        // Defer until character loads and zoning finishes.
        _autoOpenPending = true;
        Plugin.Framework.Update -= OnFrameworkUpdate;
        Plugin.Framework.Update += OnFrameworkUpdate;
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (!_autoOpenPending)
        {
            Plugin.Framework.Update -= OnFrameworkUpdate;
            return;
        }

        // Wait until the local player exists and we're not mid-zone (BetweenAreas == loading screen).
        if (Plugin.ObjectTable.LocalPlayer is null
            || Plugin.Condition[ConditionFlag.BetweenAreas]
            || Plugin.Condition[ConditionFlag.BetweenAreas51])
        {
            return;
        }

        _autoOpenPending = false;
        Plugin.Framework.Update -= OnFrameworkUpdate;

        // The user may have opened a window manually while we were waiting.
        if (_mainWindow.IsOpen || _miniWindow.IsOpen)
        {
            return;
        }
        _miniWindow.IsOpen = true;
        _ = _signal.EnsureConnectedAsync();
    }

    private void OnCombatUpdate(IFramework framework)
    {
        // Ticked unconditionally so a recharge still applies while logged out; only the drain itself is gated on
        // being in game, which is the whole point of the joke.
        Os.BatteryService.Tick(framework.UpdateDelta.Ticks / (float)TimeSpan.TicksPerSecond, _clientState.IsLoggedIn);

        if (_hangoutLogoutAtUtc is { } loggedOutAt && !_clientState.IsLoggedIn
            && DateTimeOffset.UtcNow - loggedOutAt > HangoutLogoutGrace)
        {
            _hangoutLogoutAtUtc = null;
            if (_hangoutState.MyHangout is not null)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _hubClient.EndMyHangoutAsync().ConfigureAwait(false);
                        _hangoutState.SetMyHangout(null);
                        _log.Information("[AetherLove] Hangout ended: character logged out past the grace window.");
                    }
                    catch (Exception ex)
                    {
                        _log.Warning(ex, "[AetherLove] EndMyHangoutAsync after logout grace failed.");
                    }
                });
            }
        }

        var inCombat = Plugin.Condition[ConditionFlag.InCombat];

        if (inCombat && !_wasInCombat)
        {
            if (_config.CombatBehavior == CombatBehavior.Minimize && _mainWindow.IsOpen)
            {
                _mainWindow.Minimize();
            }
            // Hide is handled by DrawConditions; LeaveOpen does nothing.
        }

        _wasInCombat = inCombat;
    }

    /// <summary>Opens the changelog window once per session if the running version has an unseen entry.</summary>
    private void MaybeShowChangelog()
    {
        if (_changelogShown)
        {
            return;
        }
        _changelogShown = true;

        var version = ChangelogRegistry.CurrentVersion;
        if (version is null)
        {
            return;
        }
        var key = $"{version.Major}.{version.Minor}.{version.Build}";
        if (_config.ShownChangelogVersions.Contains(key))
        {
            return;
        }
        if (ChangelogRegistry.GetEntry(version) is null)
        {
            return;
        }

        _config.ShownChangelogVersions.Add(key);
        _config.Save();
        _changelogWindow.IsOpen = true;
    }

    private void OnCommand(string command, string args)
    {
        var sub = args.Trim();
        if (sub.Equals("resetscreen", StringComparison.OrdinalIgnoreCase))
        {
            ResetScreen();
            return;
        }
        if (sub.Equals("debug", StringComparison.OrdinalIgnoreCase))
        {
            _debugWindow.IsOpen = true;
            return;
        }
        if (sub.Equals("fontdiag", StringComparison.OrdinalIgnoreCase))
        {
            UI.FontDiagnostics.CaptureFrames(FontDiagCaptureFrames);
            return;
        }
        if (sub.Equals("fontdiag verbose", StringComparison.OrdinalIgnoreCase))
        {
            UI.FontDiagnostics.Toggle();
            return;
        }
        if (sub.Equals("clearcache", StringComparison.OrdinalIgnoreCase))
        {
            StartClearCache();
            return;
        }
        if (sub.Equals("debuglumi", StringComparison.OrdinalIgnoreCase))
        {
            _aetherlingDebugWindow.IsOpen = true;
            return;
        }
        if (sub.Equals("hatchling reset", StringComparison.OrdinalIgnoreCase))
        {
            StartAetherlingReset();
            return;
        }
        if (sub.Equals("hatchling replay", StringComparison.OrdinalIgnoreCase))
        {
            ReplayAetherlingBirth();
            return;
        }
        OpenIfClosed();
    }

    /// <summary>Staff-only: puts the account back to before it ever bought one and refunds what it spent, so
    /// the whole ceremony can be walked again. The server does the checking; a non-staff caller is refused
    /// there, not here.</summary>
    private void StartAetherlingReset()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await _hubClient.ResetAetherlingAsync().ConfigureAwait(false);

                // The snapshot is what every surface reads, so clearing it is what actually puts the app back
                // to the beginning; without it the phone keeps drawing the creature the server just deleted.
                _aetherlingHost.ClearSnapshot();
                _osShell.SendIntent("aetherling", AetherOS.Sdk.OsIntents.Create(AetherOS.Sdk.OsIntents.AetherlingReset));
                _log.Information("[AetherLove] Aetherling reset; sparks refunded.");
                Plugin.ChatGui.Print("[AetherOS] Aetherling reset. Sparks refunded.");
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "[AetherLove] Aetherling reset failed.");
                Plugin.ChatGui.PrintError($"[AetherOS] Aetherling reset failed: {ex.Message}");
            }
        });
    }

    /// <summary>Replays the birth animation without touching the server, for tuning its timing.</summary>
    private void ReplayAetherlingBirth()
    {
        OpenIfClosed();
        _osShell.SendIntent("aetherling", AetherOS.Sdk.OsIntents.Create(AetherOS.Sdk.OsIntents.AetherlingReplayBirth));
    }

    /// <summary>Wipes local caches (chats, messenger, cached photos/avatars/icons) then restarts the phone:
    /// close both windows, disconnect the hub, clear caches, reset the bootstrapper, and reopen to Splash which
    /// reconnects and re-syncs from the server. The user's saved Photos/Camera library and wallpaper are left
    /// alone. Runs off the game command thread; window flags are set directly (as the delete-profile flow does).</summary>
    private void StartClearCache()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                _mainWindow.IsOpen = false;
                _miniWindow.IsOpen = false;
                await _signal.DisconnectAsync().ConfigureAwait(false);
                _chatCache.Clear();
                _messenger.Clear();
                Services.ImageCacheCleaner.PurgeAllPhotoCaches();
                _bootstrap.Reset();
                _mainWindow.IsOpen = true;
                _router.Navigate(Screen.Splash);
                _log.Information("[AetherLove] Local caches cleared; phone restarted.");
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "[AetherLove] clearcache failed.");
            }
        });
    }

    /// <summary>Recenters the shown window, recovering one dragged off-screen.</summary>
    private void ResetScreen()
    {
        if (_miniWindow.IsOpen)
        {
            _miniWindow.RequestRecenter();
            return;
        }
        if (!_mainWindow.IsOpen)
        {
            OpenIfClosed();
        }
        _mainWindow.RequestRecenter();
    }
}
