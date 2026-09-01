using System;
using System.Numerics;
using AetherOS.Sdk;
using Dalamud.Interface;

namespace AetherOS.Apps.Racer;

/// <summary>Lumi Racer: the pet's racing career as its own app. The server resolves every race; this
/// app plays them back, keeps the crystal card, and hosts the party flow.</summary>
public sealed class RacerApp : IAetherApp
{
    private enum View
    {
        Home,
        Race,
        Stats,
        Stamps,
        DifficultyHelp,
        Waiting,
        Intro,
        Selection,
    }

    private const string IntroKey = "racer.introSeen";
    private const string MutedKey = "muted";
    private const string VolumeKey = "volume";

    private readonly Func<string> _name;
    private readonly IRacerHost _host;
    private readonly IAppCapabilities _caps;
    private readonly AetherOS.Sdk.IAppStorage _storage;
    private readonly Screens.HomeScreen _home;
    private readonly Screens.RaceScreen _race;
    private readonly Screens.StatsScreen _stats;
    private readonly Screens.StampsScreen _stamps;
    private readonly Screens.DifficultyHelpScreen _difficultyHelp;
    private readonly Screens.WaitingRoomScreen _waiting;
    private readonly Screens.RaceOnboardingScreen _intro;

    private View _view = View.Home;

    private readonly Screens.RaceSelectionScreen _selection;
    private bool _muted;
    private float _volume = 1f;
    private Guid? _openedPartyRace;
    private AetherLove.Shared.Racing.LumiRaceDto? _pendingParty;
    private AetherLove.Shared.Racing.LumiRaceRewardDto? _pendingPartyReward;

    public RacerApp(Func<string> name, IRacerHost host, IAppCapabilities caps)
    {
        _name = name;
        _host = host;
        _caps = caps;
        _storage = caps.Storage("racer");
        _muted = _storage.Get<bool?>(MutedKey) == true;
        _volume = _storage.Get<float?>(VolumeKey) ?? 1f;
        _host.SetBgmVolume(_volume);
        _race = new Screens.RaceScreen(host, BackToHome, () => _muted, ToggleMute, () => _volume, SetVolume);
        _home = new Screens.HomeScreen(host, caps, OpenRace, OpenSelection, OpenStats, OpenStamps,
            OpenWaiting, ReplayIntro, () => _muted, ToggleMute, () => _volume, SetVolume);
        _selection = new Screens.RaceSelectionScreen(host, OpenRace, BackToHome, OpenDifficultyHelp,
            () => _muted, ToggleMute, () => _volume, SetVolume);
        _stats = new Screens.StatsScreen(host, BackToHome, () => _muted, ToggleMute, () => _volume, SetVolume);
        _stamps = new Screens.StampsScreen(host, BackToHome, () => _muted, ToggleMute, () => _volume, SetVolume);
        _difficultyHelp = new Screens.DifficultyHelpScreen(host, OpenSelection, () => _muted, ToggleMute, () => _volume, SetVolume);
        _waiting = new Screens.WaitingRoomScreen(host, caps, BackToHome, () => _muted, ToggleMute, () => _volume, SetVolume);
        _intro = new Screens.RaceOnboardingScreen(host, FinishIntro);
    }

    public string Id => "racer";

    public string Name => _name();

    public FontAwesomeIcon Icon => FontAwesomeIcon.FlagCheckered;

    public Vector4 TileTop => new(0.31f, 0.22f, 0.56f, 1f);

    public Vector4 TileBottom => new(0.55f, 0.42f, 0.90f, 1f);

    public int Badge => _home.PendingPackCount;

    public bool HasSurface => true;

    public bool RequiresConnection => true;

    public System.Collections.Generic.IReadOnlyDictionary<string, System.Collections.Generic.IReadOnlyDictionary<string, string>>? Strings =>
        Localization.AppStrings.Packs;

    public void Open()
    {
    }

    public void OnForeground()
    {
        if (_storage.Get<bool?>(IntroKey) != true)
        {
            _intro.Show();
            _view = View.Intro;
        }
        _home.OnShow();
        if (!_muted && _view != View.Race)
        {
            _host.StartMenuBgm();
        }
    }

    public void OnBackground()
    {
        _race.OnHidden();
        _host.StopBgm();
    }

    public void Draw(OsAppContext ctx)
    {
        // A party race the host began arrives by push, and the pushed run carries no viewer slot, so
        // the stage opens from a per-viewer fetch. Watched here rather than on a screen: it must fire
        // whichever one is up.
        if (_host.PartyRun is { Status: (short)AetherLove.Shared.Racing.LumiRacePartyRunStatus.Active, Race: { } run }
            && _openedPartyRace != run.RaceId)
        {
            _openedPartyRace = run.RaceId;
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    var mine = await _host.RefreshPartyRunAsync().ConfigureAwait(false);
                    if (mine is { Race: { } race })
                    {
                        _pendingParty = race;
                        _pendingPartyReward = mine.Reward;
                    }
                }
                catch (Exception)
                {
                }
            });
        }
        if (_pendingParty is { } pending)
        {
            _pendingParty = null;
            // Never restart a race that is already on the stage: a late duplicate would rewind the
            // presentation to the parade and fire the start bang over whatever the player is watching.
            if (_view != View.Race)
            {
                OpenRace(new AetherLove.Shared.Racing.LumiRaceStartResultDto(
                    pending,
                    _pendingPartyReward
                        ?? new AetherLove.Shared.Racing.LumiRaceRewardDto(0, 0, false, 0, false)));
            }
            _pendingPartyReward = null;
        }

        switch (_view)
        {
            case View.Selection:
                _selection.Draw(ctx);
                break;
            case View.Race:
                _race.Draw(ctx);
                break;
            case View.Stats:
                _stats.Draw(ctx);
                break;
            case View.Stamps:
                _stamps.Draw(ctx);
                break;
            case View.DifficultyHelp:
                _difficultyHelp.Draw(ctx);
                break;
            case View.Waiting:
                _waiting.Draw(ctx);
                break;
            case View.Intro:
                _intro.Draw(ctx);
                break;
            default:
                _home.Draw(ctx);
                break;
        }
    }

    public void OnIntent(OsIntent intent)
    {
    }

    private void OpenRace(AetherLove.Shared.Racing.LumiRaceStartResultDto result)
    {
        _race.Begin(result);
        _view = View.Race;
    }

    private void OpenStats()
    {
        _view = View.Stats;
    }

    private void OpenWaiting()
    {
        _waiting.OnShow();
        _view = View.Waiting;
    }

    private void FinishIntro()
    {
        _storage.Set(IntroKey, true);
        BackToHome();
    }

    /// <summary>The home page's way back into the onboarding, for a player who wants the tour again.</summary>
    private void ReplayIntro()
    {
        _intro.Show();
        _view = View.Intro;
    }

    private void OpenStamps()
    {
        _stamps.OnShow();
        _view = View.Stamps;
    }

    /// <summary>Opens the page that explains what the three grades change.</summary>
    private void OpenSelection()
    {
        _selection.OnShow();
        _view = View.Selection;
    }

    private void OpenDifficultyHelp()
    {
        _difficultyHelp.OnShow();
        _view = View.DifficultyHelp;
    }

    private void BackToHome()
    {
        _view = View.Home;
        _home.OnShow();
        if (!_muted)
        {
            _host.StartMenuBgm();
        }
        else
        {
            _host.StopBgm();
        }
    }

    /// <summary>The level under the same chip, kept apart from the mute so silencing the race and
    /// then bringing it back returns to the volume the player chose.</summary>
    private void SetVolume(float volume)
    {
        _volume = Math.Clamp(volume, 0f, 1f);
        _storage.Set(VolumeKey, (float?)_volume);
        _host.SetBgmVolume(_volume);
    }

    /// <summary>One switch for every screen. Muting stops whatever plays; unmuting restarts the track
    /// the current screen owns.</summary>
    private void ToggleMute()
    {
        _muted = !_muted;
        _storage.Set(MutedKey, (bool?)_muted);
        if (_muted)
        {
            _host.StopBgm();
        }
        else if (_view == View.Race)
        {
            _race.ResumeBgm();
        }
        else
        {
            _host.StartMenuBgm();
        }
    }
}
