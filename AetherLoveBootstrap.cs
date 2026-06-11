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

    private readonly IPluginLog _log;
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly ICommandManager _commandManager;
    private readonly IClientState _clientState;
    private readonly WindowSystem _windowSystem;
    private readonly MainPluginWindow _mainWindow;
    private readonly MiniWindow _miniWindow;
    private readonly ChangelogWindow _changelogWindow;
    private readonly Widgets.ModalHost _modalHost = new();
    private readonly ScreenRouter _router;
    private readonly Configuration _config;
    private readonly SessionBootstrapper _bootstrap;
    private readonly AetherSignalService _signal;
    private readonly PulseService _pulse;

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
        ScreenRouter router,
        Configuration config,
        SessionBootstrapper bootstrap,
        AetherSignalService signal,
        PulseService pulse)
    {
        _log = log;
        _pluginInterface = pluginInterface;
        _commandManager = commandManager;
        _clientState = clientState;
        _windowSystem = windowSystem;
        _mainWindow = mainWindow;
        _miniWindow = miniWindow;
        _changelogWindow = changelogWindow;
        _router = router;
        _config = config;
        _bootstrap = bootstrap;
        _signal = signal;
        _pulse = pulse;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        UiScale.Apply(_config.PhoneSize);
        UiFonts.Rebuild();

        _mainWindow.SetMiniWindow(_miniWindow);

        _windowSystem.AddWindow(_mainWindow);
        _windowSystem.AddWindow(_miniWindow);
        _windowSystem.AddWindow(_changelogWindow);
        _windowSystem.AddWindow(_modalHost);

        _commandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open AetherLove."
        });

        _pluginInterface.UiBuilder.Draw += _windowSystem.Draw;
        _pluginInterface.UiBuilder.OpenMainUi += OpenIfClosed;
        _pluginInterface.UiBuilder.OpenConfigUi += OpenConfig;
        _clientState.Login += OnLogin;
        Plugin.Framework.Update += OnCombatUpdate;
        _pulse.Start();

        var changelogVersion = ChangelogRegistry.CurrentVersion;
        var changelogKey = changelogVersion is null
            ? null
            : $"{changelogVersion.Major}.{changelogVersion.Minor}.{changelogVersion.Build}";

        // First launch: a fresh install has no identity yet and goes to onboarding; an existing user
        // upgrading into this build already carries a device id / refresh token, so don't force it.
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

        // Changelog normally shows on login (so never at character-select); if (re)loaded while
        // already in-game, show it now since the Login event won't fire.
        if (_clientState.IsLoggedIn)
        {
            MaybeShowChangelog();
        }

        _log.Information($"[AetherLove] Loaded. Use {CommandName} to open.");
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _pluginInterface.UiBuilder.Draw -= _windowSystem.Draw;
        _pluginInterface.UiBuilder.OpenMainUi -= OpenIfClosed;
        Plugin.Framework.Update -= OnCombatUpdate;
        _pluginInterface.UiBuilder.OpenConfigUi -= OpenConfig;
        _clientState.Login -= OnLogin;
        Plugin.Framework.Update -= OnFrameworkUpdate;

        _commandManager.RemoveHandler(CommandName);

        _pulse.Stop();

        _windowSystem.RemoveAllWindows();
        _mainWindow.Dispose();
        UiFonts.Dispose();

        // Host.Dispose() doesn't chain to singleton IAsyncDisposable on the sync path; drive it here.
        await _signal.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>Opens the full window only if neither window is already showing, so re-running the
    /// command doesn't reload the current screen.</summary>
    private void OpenIfClosed()
    {
        _pulse.MarkActivity();
        if (_mainWindow.IsOpen || _miniWindow.IsOpen)
        {
            return;
        }
        _mainWindow.IsOpen = true;
        _router.Navigate(Screen.Splash);
    }

    /// <summary>Settings cog in the plugin installer: jumps to Settings for a signed-in user,
    /// otherwise the normal open (splash).</summary>
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

    private void OnLogin()
    {
        // Show "What's New" now that the user is in-game, regardless of the auto-open preference below.
        MaybeShowChangelog();

        if (!_config.AutoOpenMinimizedOnLogin)
        {
            return;
        }
        // Nothing to show without an account; the install flow already handles new users.
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

    /// <summary>On entering combat, applies the configured combat behaviour.</summary>
    private void OnCombatUpdate(IFramework framework)
    {
        var inCombat = Plugin.Condition[ConditionFlag.InCombat];

        if (inCombat && !_wasInCombat)
        {
            if (_config.CombatBehavior == CombatBehavior.Minimize && _mainWindow.IsOpen)
            {
                _mainWindow.IsOpen = false;
                _miniWindow.IsOpen = true;
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
        OpenIfClosed();
    }
}
