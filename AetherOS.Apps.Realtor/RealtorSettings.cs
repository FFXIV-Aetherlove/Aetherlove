using AetherOS.Sdk;

namespace AetherOS.Apps.Realtor;

/// <summary>The Realtor app's own preferences, read once and written straight through on change. They
/// default ON, since a new user should see everything the app knows and opt out of the noisier parts. Free
/// Company tracking is the exception and defaults OFF: its count is only an upper bound, so the player
/// opts in knowing that.</summary>
public sealed class RealtorSettings
{
    public const string ShowStaleKey = "showStale";
    public const string NotifyPhaseKey = "notifyPhase";
    public const string NotifyEstateKey = "notifyEstate";
    public const string TrackFcEstateKey = "trackFcEstate";

    private readonly IAppStorage _storage;
    private bool _loaded;
    private bool _showStale = true;
    private bool _notifyPhase = true;
    private bool _notifyEstate = true;
    private bool _trackFcEstate;

    public RealtorSettings(IAppStorage storage) => _storage = storage;

    /// <summary>Whether plots whose data predates the current cycle are listed and counted at all.</summary>
    public bool ShowStale
    {
        get
        {
            Load();
            return _showStale;
        }
        set
        {
            Load();
            _showStale = value;
            _storage.Set(ShowStaleKey, value);
        }
    }

    /// <summary>Whether the phone announces the lottery flipping between entries and results.</summary>
    public bool NotifyPhase
    {
        get
        {
            Load();
            return _notifyPhase;
        }
        set
        {
            Load();
            _notifyPhase = value;
            _storage.Set(NotifyPhaseKey, value);
        }
    }

    /// <summary>Whether the phone warns that a character has not been home to its private estate in a
    /// long time.</summary>
    public bool NotifyEstate
    {
        get
        {
            Load();
            return _notifyEstate;
        }
        set
        {
            Load();
            _notifyEstate = value;
            _storage.Set(NotifyEstateKey, value);
        }
    }

    /// <summary>Whether Free Company houses are listed, counted and warned about. Independent of
    /// <see cref="NotifyEstate"/>, which covers private estates only.</summary>
    public bool TrackFcEstate
    {
        get
        {
            Load();
            return _trackFcEstate;
        }
        set
        {
            Load();
            _trackFcEstate = value;
            _storage.Set(TrackFcEstateKey, value);
        }
    }

    private void Load()
    {
        if (_loaded)
        {
            return;
        }
        _loaded = true;
        _showStale = _storage.Get<bool?>(ShowStaleKey) ?? true;
        _notifyPhase = _storage.Get<bool?>(NotifyPhaseKey) ?? true;
        _notifyEstate = _storage.Get<bool?>(NotifyEstateKey) ?? true;
        _trackFcEstate = _storage.Get<bool?>(TrackFcEstateKey) ?? false;
    }
}
