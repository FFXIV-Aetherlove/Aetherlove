using System;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Changelog;
using AetherLove.Config;
using AetherLove.Navigation;
using AetherLove.Screens;
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
    public const string CommandName = "/aetherlove";

    public const string AliasCommandName = "/love";

    private readonly IPluginLog _log;
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly ICommandManager _commandManager;
    private readonly IClientState _clientState;
    private readonly WindowSystem _windowSystem;
    private readonly MainPluginWindow _mainWindow;
    private readonly MiniWindow _miniWindow;
    private readonly ChangelogWindow _changelogWindow;
    private readonly DebugWindow _debugWindow;
    private readonly Widgets.ModalHost _modalHost = new();
    private readonly Widgets.SelfieCaptureOverlay _selfieOverlay;
    private readonly ScreenRouter _router;
    private readonly Configuration _config;
    private readonly SessionBootstrapper _bootstrap;
    private readonly AetherSignalService _signal;
    private readonly PulseService _pulse;
    private readonly ScreenCaptureService _capture;
    private readonly Services.Hangouts.HangoutStateService _hangoutState;
    private readonly Services.Hub.AetherLoveHubClient _hubClient;

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
        ScreenRouter router,
        Configuration config,
        SessionBootstrapper bootstrap,
        AetherSignalService signal,
        PulseService pulse,
        ScreenCaptureService capture,
        Widgets.SelfieCaptureOverlay selfieOverlay,
        Services.Hangouts.HangoutStateService hangoutState,
        Services.Hub.AetherLoveHubClient hubClient)
    {
        _log = log;
        _pluginInterface = pluginInterface;
        _commandManager = commandManager;
        _clientState = clientState;
        _windowSystem = windowSystem;
        _mainWindow = mainWindow;
        _miniWindow = miniWindow;
        _changelogWindow = changelogWindow;
        _debugWindow = debugWindow;
        _router = router;
        _config = config;
        _bootstrap = bootstrap;
        _signal = signal;
        _pulse = pulse;
        _capture = capture;
        _selfieOverlay = selfieOverlay;
        _hangoutState = hangoutState;
        _hubClient = hubClient;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        UiScale.Apply(_config.PhoneSize);
        MiniScale.Apply(_config.MiniPhoneSize);
        UiFonts.Rebuild();

        _mainWindow.SetMiniWindow(_miniWindow);

        _windowSystem.AddWindow(_mainWindow);
        _windowSystem.AddWindow(_miniWindow);
        _windowSystem.AddWindow(_changelogWindow);
        _windowSystem.AddWindow(_debugWindow);
        _windowSystem.AddWindow(_modalHost);
        _windowSystem.AddWindow(_selfieOverlay);

        // Escape closes the focused Dalamud window by default; block it for every AetherLove window.
        foreach (var window in _windowSystem.Windows)
        {
            window.RespectCloseHotkey = false;
        }

        _commandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open AetherLove. Subcommands: \"resetscreen\" (recenter the window), \"debug\" (diagnostics)."
        });
        _commandManager.AddHandler(AliasCommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Alias for /aetherlove."
        });

        _pluginInterface.UiBuilder.Draw += DrawWindowSystemGuarded;
        _pluginInterface.UiBuilder.OpenMainUi += OpenIfClosed;
        _pluginInterface.UiBuilder.OpenConfigUi += OpenConfig;
        _pluginInterface.UiBuilder.DisableGposeUiHide = _config.ShowDuringGpose;
        _pluginInterface.UiBuilder.DisableCutsceneUiHide = !_config.HideDuringCutscenes;
        _clientState.Login += OnLogin;
        _clientState.Logout += OnLogout;
        Plugin.Framework.Update += OnCombatUpdate;
        _pulse.Start();
        _capture.Initialize();
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
        _commandManager.RemoveHandler(AliasCommandName);

        _pulse.Stop();
        _capture.Dispose();
        Widgets.SelfieCaptureOverlay.PurgeTempFiles();

        _windowSystem.RemoveAllWindows();
        _mainWindow.Dispose();
        UiFonts.Dispose();

        // Host.Dispose() doesn't chain to singleton IAsyncDisposable on the sync path; drive it here.
        await _signal.DisposeAsync().ConfigureAwait(false);
    }

    private bool _fontScaleLeakLogged;

    /// <summary>Restores ImGui's global font scale even if a window callback throws, so the phone's pin
    /// can never bleed into other plugins' rendering.</summary>
    private void DrawWindowSystemGuarded()
    {
        var io = ImGui.GetIO();
        var savedScale = io.FontGlobalScale;
        try
        {
            _windowSystem.Draw();
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
            io.FontGlobalScale = savedScale;
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
            _router.Navigate(Screen.Settings);
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
        // Login fires while the loading screen is still up, so defer the bubble to a Framework tick
        // that waits until the character exists and we're no longer zoning.
        _autoOpenPending = true;
        Plugin.Framework.Update -= OnFrameworkUpdate; // avoid a double subscription on re-login
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
        OpenIfClosed();
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
