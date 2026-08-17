using AetherOS.Sdk;

namespace AetherOS.Apps.Groove;

/// <summary>The Groove app's own preferences, read once and written straight through on change. All three
/// surface toggles default ON so the feature is discoverable; each is opt-out.</summary>
public sealed class GrooveSettings
{
    public const string ShowWidgetKey = "showWidget";
    public const string ShowShadeTileKey = "showShadeTile";
    public const string ShowDtrKey = "showDtr";
    public const string ShowMiniControlsKey = "showMiniControls";
    public const string PinnedSessionKey = "pinnedSession";
    public const string AutoMuteBgmKey = "autoMuteBgm";
    public const string OnboardingSeenKey = "onboardingSeen";

    private readonly IAppStorage _storage;
    private bool _loaded;
    private bool _showWidget = true;
    private bool _showShadeTile = true;
    private bool _showDtr = true;
    private bool _showMiniControls = true;
    private bool _autoMuteBgm;
    private bool _onboardingSeen;
    private string? _pinnedSession;

    public GrooveSettings(IAppStorage storage) => _storage = storage;

    /// <summary>The now-playing row on the home widgets page.</summary>
    public bool ShowWidget
    {
        get
        {
            Load();
            return _showWidget;
        }
        set
        {
            Load();
            _showWidget = value;
            _storage.Set(ShowWidgetKey, value);
        }
    }

    /// <summary>The media tile in the pull-down notification shade.</summary>
    public bool ShowShadeTile
    {
        get
        {
            Load();
            return _showShadeTile;
        }
        set
        {
            Load();
            _showShadeTile = value;
            _storage.Set(ShowShadeTileKey, value);
        }
    }

    /// <summary>The now-playing entry in the game's server info bar.</summary>
    public bool ShowDtr
    {
        get
        {
            Load();
            return _showDtr;
        }
        set
        {
            Load();
            _showDtr = value;
            _storage.Set(ShowDtrKey, value);
        }
    }

    /// <summary>The transport that replaces the logo on the minimised phone.</summary>
    public bool ShowMiniControls
    {
        get
        {
            Load();
            return _showMiniControls;
        }
        set
        {
            Load();
            _showMiniControls = value;
            _storage.Set(ShowMiniControlsKey, value);
        }
    }

    /// <summary>Silence the game's own BGM while something is playing. Off by default: it reaches outside the
    /// phone and changes the player's game audio, so it is strictly opt-in.</summary>
    public bool AutoMuteBgm
    {
        get
        {
            Load();
            return _autoMuteBgm;
        }
        set
        {
            Load();
            _autoMuteBgm = value;
            _storage.Set(AutoMuteBgmKey, value);
        }
    }

    /// <summary>The what-this-reads explainer has been through once. Defaults false for EXISTING installs
    /// as well as new ones: somebody already using the app has never been told what it reads off their
    /// machine, which is precisely who the explanation is for.</summary>
    public bool OnboardingSeen
    {
        get
        {
            Load();
            return _onboardingSeen;
        }
        set
        {
            Load();
            _onboardingSeen = value;
            _storage.Set(OnboardingSeenKey, value);
        }
    }

    /// <summary>The pinned media session id; null follows the system's current session.</summary>
    public string? PinnedSession
    {
        get
        {
            Load();
            return _pinnedSession;
        }
        set
        {
            Load();
            _pinnedSession = value;
            _storage.Set(PinnedSessionKey, value);
        }
    }

    private void Load()
    {
        if (_loaded)
        {
            return;
        }
        _loaded = true;
        _showWidget = _storage.Get<bool?>(ShowWidgetKey) ?? true;
        _showShadeTile = _storage.Get<bool?>(ShowShadeTileKey) ?? true;
        _showDtr = _storage.Get<bool?>(ShowDtrKey) ?? true;
        _showMiniControls = _storage.Get<bool?>(ShowMiniControlsKey) ?? true;
        _autoMuteBgm = _storage.Get<bool?>(AutoMuteBgmKey) ?? false;
        _onboardingSeen = _storage.Get<bool?>(OnboardingSeenKey) ?? false;
        _pinnedSession = _storage.Get<string?>(PinnedSessionKey);
    }
}
